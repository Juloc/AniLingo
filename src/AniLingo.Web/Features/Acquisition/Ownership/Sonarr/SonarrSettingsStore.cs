using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace AniLingo.Web.Features.Acquisition.Ownership.Sonarr;

public sealed class SonarrSettingsStore
{
    private const string FileName = "sonarr.json";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IDataProtector protector;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string storePath;

    public SonarrSettingsStore(IDataProtectionProvider dataProtectionProvider)
        : this(dataProtectionProvider, new DirectoryInfo("/data/acquisition"))
    {
    }

    public SonarrSettingsStore(
        IDataProtectionProvider dataProtectionProvider,
        DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentNullException.ThrowIfNull(directory);

        protector = dataProtectionProvider.CreateProtector(
            "AniLingo.Acquisition.Sonarr.ApiKey.v1");
        storePath = Path.Combine(directory.FullName, FileName);
    }

    public async Task<SonarrConnection?> LoadAsync(CancellationToken cancellationToken = default)
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
                var persisted = JsonSerializer.Deserialize<PersistedSonarrSettings>(
                    json,
                    JsonOptions);

                if (persisted is null)
                {
                    return null;
                }

                var settings = NormalizeAndValidate(new SonarrSettings(persisted.BaseUrl));
                var apiKey = protector.Unprotect(persisted.ProtectedApiKey);
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new InvalidDataException("Stored Sonarr API key is empty.");
                }

                return new(settings, apiKey);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("Sonarr settings contain invalid JSON.", exception);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(
        SonarrConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var settings = NormalizeAndValidate(connection.Settings);
        if (string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            throw new ArgumentException("Sonarr API key is required.", nameof(connection));
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(storePath)
                ?? throw new InvalidOperationException("Sonarr settings path has no directory.");
            Directory.CreateDirectory(directory);

            var persisted = new PersistedSonarrSettings(
                settings.BaseUrl,
                protector.Protect(connection.ApiKey.Trim()));

            var temporaryPath = $"{storePath}.tmp-{Guid.NewGuid():N}";
            try
            {
                await File.WriteAllTextAsync(
                    temporaryPath,
                    JsonSerializer.Serialize(persisted, JsonOptions),
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

    public static SonarrSettings NormalizeAndValidate(SonarrSettings settings)
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
                "Sonarr Base URL must be an absolute HTTP(S) URL without credentials, query or fragment.",
                nameof(settings));
        }

        return settings with
        {
            BaseUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/')
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

    private sealed record PersistedSonarrSettings(
        string BaseUrl,
        string ProtectedApiKey);
}
