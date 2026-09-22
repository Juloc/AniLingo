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
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IDataProtector protector =
        dataProtectionProvider.CreateProtector("AniLingo.AniList.AccessToken.v1");

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
