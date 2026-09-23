using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace AniLingo.Web.Features.Tracking;

public sealed record StoredAniListAccount(
    int ClientId,
    int ViewerId,
    string ViewerName,
    string? ViewerAvatarUrl,
    string AccessToken,
    DateTimeOffset ConnectedAt,
    DateTimeOffset? TokenExpiresAt);

public sealed class AniListAccountStore(
    IDataProtectionProvider dataProtectionProvider,
    ILogger<AniListAccountStore> logger)
{
    private const string StorePath = "/data/integrations/anilist.json";
    private const string ProgressBackupPath =
        "/data/integrations/anilist-progress-backups.ndjson";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IDataProtector protector =
        dataProtectionProvider.CreateProtector("AniLingo.AniList.AccessToken.v1");

    private readonly SemaphoreSlim backupGate = new(1, 1);

    public async Task<StoredAniListAccount?> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(StorePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(StorePath, cancellationToken);
            var persisted = JsonSerializer.Deserialize<PersistedAniListAccount>(
                json,
                JsonOptions);

            if (persisted is null ||
                persisted.ClientId <= 0 ||
                persisted.ViewerId <= 0 ||
                string.IsNullOrWhiteSpace(persisted.ViewerName) ||
                string.IsNullOrWhiteSpace(persisted.ProtectedAccessToken))
            {
                return null;
            }

            return new StoredAniListAccount(
                persisted.ClientId,
                persisted.ViewerId,
                persisted.ViewerName,
                persisted.ViewerAvatarUrl,
                protector.Unprotect(persisted.ProtectedAccessToken),
                persisted.ConnectedAt,
                persisted.TokenExpiresAt);
        }
        catch (Exception exception) when (
            exception is JsonException or
            CryptographicException or
            IOException or
            UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not load the AniList account connection.");
            return null;
        }
    }

    public async Task SaveAsync(
        StoredAniListAccount account,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(StorePath)
            ?? throw new InvalidOperationException("AniList store path has no directory.");

        Directory.CreateDirectory(directory);

        var persisted = new PersistedAniListAccount(
            account.ClientId,
            account.ViewerId,
            account.ViewerName,
            account.ViewerAvatarUrl,
            protector.Protect(account.AccessToken),
            account.ConnectedAt,
            account.TokenExpiresAt);

        var temporaryPath = $"{StorePath}.tmp-{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(persisted, JsonOptions);

        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);

        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                temporaryPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temporaryPath, StorePath, overwrite: true);
    }

    public async Task AppendProgressBackupAsync(
        AniListProgressBackup backup,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(ProgressBackupPath)
            ?? throw new InvalidOperationException(
                "AniList backup path has no directory.");

        Directory.CreateDirectory(directory);

        await backupGate.WaitAsync(cancellationToken);
        try
        {
            var line = JsonSerializer.Serialize(
                backup,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            await File.AppendAllTextAsync(
                ProgressBackupPath,
                line + Environment.NewLine,
                cancellationToken);

            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(
                    ProgressBackupPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(
                exception,
                "Could not persist AniList pre-write progress backup.");

            // Never write to AniList if the safety backup cannot be persisted.
            throw new AniListAccountException(
                "AniList sync was not attempted because the local pre-write backup could not be saved.",
                exception);
        }
        finally
        {
            backupGate.Release();
        }
    }

    public Task DisconnectAsync()
    {
        if (File.Exists(StorePath))
        {
            File.Delete(StorePath);
        }

        return Task.CompletedTask;
    }

    private sealed record PersistedAniListAccount(
        int ClientId,
        int ViewerId,
        string ViewerName,
        string? ViewerAvatarUrl,
        string ProtectedAccessToken,
        DateTimeOffset ConnectedAt,
        DateTimeOffset? TokenExpiresAt);
}
