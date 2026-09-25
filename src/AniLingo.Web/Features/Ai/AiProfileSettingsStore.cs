using System.Security.Cryptography;
using System.Text.Json;
using AniLingo.Web.Features.Auth;
using Microsoft.AspNetCore.DataProtection;

namespace AniLingo.Web.Features.Ai;

public sealed class AiProfileSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IDataProtector protector;
    private readonly ILogger<AiProfileSettingsStore> logger;
    private readonly string accountDirectory;
    private readonly SemaphoreSlim gate = new(1, 1);

    public AiProfileSettingsStore(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<AiProfileSettingsStore> logger)
        : this(
            dataProtectionProvider,
            logger,
            new DirectoryInfo("/data/integrations"))
    {
    }

    public AiProfileSettingsStore(
        IDataProtectionProvider dataProtectionProvider,
        ILogger<AiProfileSettingsStore> logger,
        DirectoryInfo integrationDirectory)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(integrationDirectory);

        protector = dataProtectionProvider.CreateProtector(
            "AniLingo.Ai.ProfileApiKey.v1");
        this.logger = logger;
        accountDirectory = Path.Combine(
            integrationDirectory.FullName,
            "ai",
            "accounts");
    }

    public async Task<AiProfileSettings> LoadAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var safeProfileId = ValidateProfileId(profileId);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var path = GetPath(safeProfileId);
            if (!File.Exists(path))
            {
                return AiProfileSettings.Default;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken);
                var persisted = JsonSerializer.Deserialize<PersistedAiProfileSettings>(
                    json,
                    JsonOptions);

                if (persisted is null || !AiProviderIds.IsSupported(persisted.ProviderId))
                {
                    return AiProfileSettings.Default;
                }

                var apiKey = string.IsNullOrWhiteSpace(persisted.ProtectedApiKey)
                    ? null
                    : protector.Unprotect(persisted.ProtectedApiKey);

                return Validate(
                    new AiProfileSettings(
                        persisted.ProviderId,
                        persisted.BaseUrl,
                        persisted.Model,
                        apiKey,
                        persisted.TranslationMode),
                    requireSecret: false);
            }
            catch (Exception exception) when (
                exception is JsonException
                    or CryptographicException
                    or IOException
                    or UnauthorizedAccessException)
            {
                logger.LogWarning(
                    exception,
                    "Could not load AI settings for profile {ProfileId}.",
                    safeProfileId);
                return AiProfileSettings.Default;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveAsync(
        string profileId,
        AiProfileSettings settings,
        CancellationToken cancellationToken = default)
    {
        var safeProfileId = ValidateProfileId(profileId);
        var validated = Validate(settings, requireSecret: true);

        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(accountDirectory);
            var path = GetPath(safeProfileId);
            var temporaryPath = $"{path}.tmp-{Guid.NewGuid():N}";

            var persisted = new PersistedAiProfileSettings(
                validated.ProviderId,
                validated.BaseUrl,
                validated.Model,
                string.IsNullOrWhiteSpace(validated.ApiKey)
                    ? null
                    : protector.Protect(validated.ApiKey),
                validated.TranslationMode);

            try
            {
                await File.WriteAllTextAsync(
                    temporaryPath,
                    JsonSerializer.Serialize(persisted, JsonOptions),
                    cancellationToken);
                SetPrivateFileMode(temporaryPath);
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                exception,
                "Could not save AI settings for profile {ProfileId}.",
                safeProfileId);
            throw new InvalidOperationException(
                "AI settings could not be saved to persistent storage.",
                exception);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ResetAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        var safeProfileId = ValidateProfileId(profileId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            TryDelete(GetPath(safeProfileId));
        }
        finally
        {
            gate.Release();
        }
    }

    private string GetPath(string profileId) =>
        Path.Combine(accountDirectory, $"{profileId}.json");

    private static AiProfileSettings Validate(
        AiProfileSettings settings,
        bool requireSecret)
    {
        if (!AiProviderIds.IsSupported(settings.ProviderId))
        {
            throw new InvalidOperationException("Unsupported AI provider.");
        }

        if (!Enum.IsDefined(settings.TranslationMode))
        {
            throw new InvalidOperationException("Unsupported AI translation mode.");
        }

        if (settings.ProviderId == AiProviderIds.Server)
        {
            return settings with
            {
                BaseUrl = null,
                Model = null,
                ApiKey = null
            };
        }

        var baseUrl = settings.BaseUrl?.Trim();
        var model = settings.Model?.Trim();
        var apiKey = settings.ApiKey?.Trim();

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "Enter a valid HTTP(S) base URL for the OpenAI-compatible provider.");
        }

        if (string.IsNullOrWhiteSpace(model) || model.Length > 120)
        {
            throw new InvalidOperationException(
                "Enter a valid model name.");
        }

        if (requireSecret && string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "An API key is required for the OpenAI-compatible provider.");
        }

        return settings with
        {
            BaseUrl = baseUrl.TrimEnd('/'),
            Model = model,
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey
        };
    }

    private static string ValidateProfileId(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        if (profileId.Length > 80 ||
            profileId.Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_'))
        {
            throw new ArgumentException(
                "Profile ID contains unsupported characters.",
                nameof(profileId));
        }

        return profileId;
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

    private sealed record PersistedAiProfileSettings(
        string ProviderId,
        string? BaseUrl,
        string? Model,
        string? ProtectedApiKey,
        AiTranslationMode TranslationMode);
}
