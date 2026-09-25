using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace AniLingo.Web.Features.Acquisition.Sabnzbd;

public sealed class SabnzbdSettingsStore
{
    private const string FileName = "sabnzbd.json";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IDataProtector protector;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string storePath;

    public SabnzbdSettingsStore(IDataProtectionProvider dataProtectionProvider)
        : this(
            dataProtectionProvider,
            new DirectoryInfo("/data/acquisition"))
    {
    }

    public SabnzbdSettingsStore(
        IDataProtectionProvider dataProtectionProvider,
        DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentNullException.ThrowIfNull(directory);

        protector = dataProtectionProvider.CreateProtector(
            "AniLingo.Acquisition.Sabnzbd.ApiKey.v1");
        storePath = Path.Combine(directory.FullName, FileName);
    }

    public async Task<SabnzbdConnection?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(storePath))
            {
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(storePath, cancellationToken);
                var persisted = JsonSerializer.Deserialize<PersistedSabnzbdSettings>(
                    json,
                    JsonOptions);

                if (persisted is null)
                {
                    return null;
                }

                var settings = NormalizeAndValidate(
                    new SabnzbdSettings(
                        persisted.BaseUrl,
                        persisted.Category,
                        persisted.QueuePageSize,
                        persisted.HistoryPageSize));

                var apiKey = protector.Unprotect(persisted.ProtectedApiKey);
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new InvalidDataException("Stored SABnzbd API key is empty.");
                }

                return new SabnzbdConnection(settings, apiKey);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "SABnzbd settings contain invalid JSON.",
                    exception);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(
        SabnzbdConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var settings = NormalizeAndValidate(connection.Settings);

        if (string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            throw new ArgumentException(
                "SABnzbd API key is required.",
                nameof(connection));
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(storePath)
                ?? throw new InvalidOperationException(
                    "SABnzbd settings path has no directory.");
            Directory.CreateDirectory(directory);

            var persisted = new PersistedSabnzbdSettings(
                settings.BaseUrl,
                settings.Category,
                settings.QueuePageSize,
                settings.HistoryPageSize,
                protector.Protect(connection.ApiKey.Trim()));

            var temporaryPath = $"{storePath}.tmp-{Guid.NewGuid():N}";
            try
            {
                var json = JsonSerializer.Serialize(persisted, JsonOptions);
                await File.WriteAllTextAsync(
                    temporaryPath,
                    json,
                    cancellationToken);
                SetPrivateFileMode(temporaryPath);
                File.Move(temporaryPath, storePath, overwrite: true);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            TryDelete(storePath);
        }
        finally
        {
            gate.Release();
        }
    }

    public static SabnzbdSettings NormalizeAndValidate(SabnzbdSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!Uri.TryCreate(settings.BaseUrl?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "SABnzbd Base URL must be an absolute HTTP(S) URL without credentials, query or fragment.",
                nameof(settings));
        }

        var category = settings.Category?.Trim();
        if (string.IsNullOrWhiteSpace(category) || category.Length > 80)
        {
            throw new ArgumentException(
                "SABnzbd category must contain between 1 and 80 characters.",
                nameof(settings));
        }

        if (settings.QueuePageSize is < 1 or > 1000 ||
            settings.HistoryPageSize is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "SABnzbd queue/history page sizes must be between 1 and 1000.");
        }

        return settings with
        {
            BaseUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/'),
            Category = category
        };
    }

    private static void SetPrivateFileMode(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record PersistedSabnzbdSettings(
        string BaseUrl,
        string Category,
        int QueuePageSize,
        int HistoryPageSize,
        string ProtectedApiKey);
}
