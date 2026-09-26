using System.Security.Cryptography;
using System.Text;
using AniLingo.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Acquisition.Api;

/// <summary>
/// The one canonical automation API key created/listed/revoked. Only a SHA-256 hash of the raw
/// key is ever persisted, and the raw value is returned exactly once, from <see cref="CreateAsync"/>.
/// Keys carry enough entropy (32 random bytes, base64url-encoded) that a fast hash is appropriate —
/// unlike an owner password, there is no realistic dictionary/brute-force surface to slow down.
/// </summary>
public sealed class AcquisitionApiKeyService(AppDbContext db)
{
    public const string KeyPrefixTag = "alk_";
    private const int RawKeyBytes = 32;
    private const int DisplayPrefixLength = 12;

    public async Task<IReadOnlyList<AcquisitionApiKey>> ListAsync(CancellationToken cancellationToken) =>
        await db.AcquisitionApiKeys
            .AsNoTracking()
            .OrderByDescending(key => key.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    /// <summary>Creates a new key and returns it together with the raw value, shown only once.</summary>
    public async Task<(AcquisitionApiKey Key, string RawKey)> CreateAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var rawKey = GenerateRawKey();
        var entity = new AcquisitionApiKey
        {
            Name = name.Trim(),
            KeyPrefix = rawKey[..Math.Min(DisplayPrefixLength, rawKey.Length)],
            KeyHash = Hash(rawKey),
            CreatedAtUtc = DateTime.UtcNow
        };

        db.AcquisitionApiKeys.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return (entity, rawKey);
    }

    public async Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await db.AcquisitionApiKeys.SingleOrDefaultAsync(key => key.Id == id, cancellationToken);
        if (entity is null || !entity.IsActive)
        {
            return false;
        }

        entity.RevokedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Validates a raw key presented via <c>X-Api-Key</c>. Returns null for an unknown, malformed
    /// or revoked key. Updates <see cref="AcquisitionApiKey.LastUsedAtUtc"/> on success.
    /// </summary>
    public async Task<AcquisitionApiKey?> ValidateAsync(
        string? rawKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawKey) || !rawKey.StartsWith(KeyPrefixTag, StringComparison.Ordinal))
        {
            return null;
        }

        var hash = Hash(rawKey);
        var entity = await db.AcquisitionApiKeys
            .SingleOrDefaultAsync(key => key.KeyHash == hash, cancellationToken);
        if (entity is null || !entity.IsActive)
        {
            return null;
        }

        entity.LastUsedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    private static string GenerateRawKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(RawKeyBytes);
        return $"{KeyPrefixTag}{Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
    }

    private static string Hash(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes);
    }
}
