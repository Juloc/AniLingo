using System.Security.Cryptography;
using System.Text.Json;
using Jularr.Web.Features.Providers;
using Microsoft.AspNetCore.DataProtection;

namespace Jularr.Web.Features.Subtitles.OpenSubtitles;

/// <summary>
/// Persists the owner's OpenSubtitles access as JSON under <c>/data/integrations</c> (the same home as
/// the Jimaku key), following the provider-framework credential convention (#438): the username
/// and metadata are plain JSON, the API key and password are encrypted with the
/// <see cref="ProviderCredentials.ProtectorFor"/> protector for <see cref="ProviderKeys.OpenSubtitles"/>
/// and never written unprotected. There is at most one OpenSubtitles account, so
/// <see cref="SaveAsync"/> replaces whatever is stored.
/// </summary>
public sealed class OpenSubtitlesCredentialStore : IProviderCredentialStore<OpenSubtitlesCredential>
{
    public const string FileName = "opensubtitles.json";
    public const string DefaultDirectory = "/data/integrations";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IDataProtector protector;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string storePath;
    private readonly ILogger<OpenSubtitlesCredentialStore>? logger;

    public OpenSubtitlesCredentialStore(
        IDataProtectionProvider dataProtectionProvider,
        string directory = DefaultDirectory,
        ILogger<OpenSubtitlesCredentialStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        protector = ProviderCredentials.ProtectorFor(dataProtectionProvider, ProviderKeys.OpenSubtitles);
        storePath = Path.Combine(directory, FileName);
        this.logger = logger;
    }

    /// <summary>The configured credential, or <see langword="null"/> when none is stored (or it can no longer be decrypted).</summary>
    public async Task<OpenSubtitlesCredential?> GetAsync(CancellationToken cancellationToken = default) =>
        (await LoadAllAsync(cancellationToken)).FirstOrDefault();

    public async Task<IReadOnlyList<OpenSubtitlesCredential>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(storePath))
            {
                return [];
            }

            PersistedCredential? persisted;
            try
            {
                var json = await File.ReadAllTextAsync(storePath, cancellationToken);
                persisted = JsonSerializer.Deserialize<PersistedCredential>(json, JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("OpenSubtitles settings contain invalid JSON.", exception);
            }

            if (persisted is null)
            {
                return [];
            }

            try
            {
                return
                [
                    new OpenSubtitlesCredential
                    {
                        Id = persisted.Id,
                        Username = persisted.Username ?? "",
                        UpdatedAtUtc = persisted.UpdatedAtUtc,
                        ApiKey = string.IsNullOrEmpty(persisted.ProtectedApiKey)
                            ? ""
                            : protector.Unprotect(persisted.ProtectedApiKey),
                        Password = string.IsNullOrEmpty(persisted.ProtectedPassword)
                            ? ""
                            : protector.Unprotect(persisted.ProtectedPassword)
                    }
                ];
            }
            catch (CryptographicException exception)
            {
                // The Data Protection key ring changed: the secrets are unreadable, so the integration
                // is treated as not configured and the owner re-enters them.
                logger?.LogWarning(exception, "Could not decrypt the stored OpenSubtitles credentials.");
                return [];
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Stores <paramref name="credential"/>, replacing any previous OpenSubtitles account.</summary>
    public async Task SaveAsync(OpenSubtitlesCredential credential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (string.IsNullOrWhiteSpace(credential.ApiKey))
        {
            throw new ArgumentException("An OpenSubtitles API key is required.", nameof(credential));
        }

        var persisted = new PersistedCredential(
            credential.Id,
            credential.Username.Trim(),
            DateTimeOffset.UtcNow,
            protector.Protect(credential.ApiKey.Trim()),
            string.IsNullOrEmpty(credential.Password) ? null : protector.Protect(credential.Password));

        await gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(storePath)
                ?? throw new InvalidOperationException("OpenSubtitles settings path has no directory.");
            Directory.CreateDirectory(directory);

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

    /// <summary>Removes the stored account whatever it contains, including secrets that can no longer be decrypted.</summary>
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            File.Delete(storePath);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(storePath))
            {
                return false;
            }

            PersistedCredential? persisted;
            try
            {
                persisted = JsonSerializer.Deserialize<PersistedCredential>(
                    await File.ReadAllTextAsync(storePath, cancellationToken), JsonOptions);
            }
            catch (JsonException)
            {
                // An unreadable file has no identity to match; removing it is what the owner asked for.
                persisted = null;
            }

            if (persisted is not null && persisted.Id != id)
            {
                return false;
            }

            File.Delete(storePath);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    private static void SetPrivateFileMode(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
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

    // What is on disk: the non-secret fields as plain JSON, each secret as a Data Protection blob.
    private sealed record PersistedCredential(
        Guid Id,
        string? Username,
        DateTimeOffset UpdatedAtUtc,
        string? ProtectedApiKey,
        string? ProtectedPassword);
}
