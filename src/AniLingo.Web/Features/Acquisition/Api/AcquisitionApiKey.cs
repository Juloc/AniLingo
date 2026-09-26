namespace AniLingo.Web.Features.Acquisition.Api;

/// <summary>
/// One canonical automation API key, scoped to the acquisition automation API
/// (<c>/api/acquisition/v1/**</c>) only. Only a SHA-256 hash of the raw key is ever stored; the
/// raw value is shown to the owner exactly once, at creation time, and cannot be recovered
/// afterwards — only revoked and replaced with a new key.
/// </summary>
public sealed class AcquisitionApiKey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owner-chosen label (for example "Sonarr-style automation script").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>First few characters of the raw key, kept only so the list page can tell keys apart.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the raw key, hex-encoded. Never the raw key itself.</summary>
    public string KeyHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? LastUsedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public bool IsActive => RevokedAtUtc is null;
}
