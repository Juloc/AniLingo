namespace AniLingo.Web.Features.Acquisition.Health;

public enum AcquisitionHealthKind
{
    Indexer,
    DownloadClient
}

/// <summary>
/// Health of one indexer or download client entry, keyed by
/// <see cref="Kind"/> + <see cref="EntryId"/>.
/// </summary>
public sealed record AcquisitionHealthStatus(
    AcquisitionHealthKind Kind,
    Guid EntryId,
    string Name,
    bool Reachable,
    bool AuthOk,
    string? LastError,
    DateTimeOffset LastCheckedUtc)
{
    public bool IsHealthy => Reachable && AuthOk;
}

public sealed record AcquisitionHealthState(
    IReadOnlyList<AcquisitionHealthStatus> Statuses);
