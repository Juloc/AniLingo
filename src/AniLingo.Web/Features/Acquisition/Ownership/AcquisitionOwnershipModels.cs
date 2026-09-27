namespace AniLingo.Web.Features.Acquisition.Ownership;

public enum AnimeManagementMode
{
    ReadOnlyCoexistence,
    ParallelAcquisition,
    AniLingoManaged
}

public enum AcquisitionOwner
{
    AniLingo,
    Sonarr
}

public enum AcquisitionOwnershipStatus
{
    Pending,
    Importing,
    Completed,
    Failed,
    Cancelled
}

public enum AnimeMigrationAction
{
    KeepSonarr,
    StartParallel,
    HandOverToAniLingo,
    Revert
}

// SonarrSeriesId links the Jularr anime to the Sonarr series that manages it.
// SonarrUnmonitoredByAniLingo records that a documented migration action unmonitored the
// series in Sonarr, so a revert can restore exactly that change and nothing else.
public sealed record AnimeManagementAssignment(
    string AnimeKey,
    AnimeManagementMode Mode,
    DateTimeOffset ChangedAtUtc,
    int? SonarrSeriesId = null,
    string? SonarrSeriesTitle = null,
    bool SonarrUnmonitoredByAniLingo = false);

public sealed record AcquisitionOwnership(
    string JobId,
    string AnimeKey,
    AcquisitionOwner Owner,
    string? ReleaseKey,
    AcquisitionOwnershipStatus Status,
    DateTimeOffset UpdatedAtUtc,
    string? DownloadId = null);

public sealed record ManagedMediaPath(
    string Path,
    string AnimeKey,
    AcquisitionOwner Owner,
    string? JobId,
    DateTimeOffset UpdatedAtUtc);

public sealed record AnimeMigrationEvent(
    DateTimeOffset AtUtc,
    string AnimeKey,
    AnimeMigrationAction Action,
    AnimeManagementMode FromMode,
    AnimeManagementMode ToMode,
    string Detail);

public sealed record OwnershipDecision(bool Allowed, string Reason);

public sealed record OwnershipConflict(
    string Kind,
    string AnimeKey,
    string Value,
    string Reason);

public sealed record AcquisitionOwnershipState(
    int Version,
    Dictionary<string, AnimeManagementAssignment> Anime,
    Dictionary<string, AcquisitionOwnership> Jobs,
    Dictionary<string, ManagedMediaPath> Paths)
{
    public const int MaxMigrationEvents = 500;

    public List<AnimeMigrationEvent> MigrationLog { get; init; } = [];

    public static AcquisitionOwnershipState Empty() =>
        new(
            1,
            new Dictionary<string, AnimeManagementAssignment>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, AcquisitionOwnership>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, ManagedMediaPath>(StringComparer.OrdinalIgnoreCase));
}

// Everything an acquisition decision seam needs: persisted Jularr ownership plus the
// latest read-only Sonarr observation.
public sealed record AcquisitionOwnershipSnapshot(
    AcquisitionOwnershipState State,
    SonarrObservedState Sonarr);
