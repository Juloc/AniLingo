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

public sealed record AnimeManagementAssignment(
    string AnimeKey,
    AnimeManagementMode Mode,
    DateTimeOffset ChangedAtUtc);

public sealed record AcquisitionOwnership(
    string JobId,
    string AnimeKey,
    AcquisitionOwner Owner,
    string? ReleaseKey,
    AcquisitionOwnershipStatus Status,
    DateTimeOffset UpdatedAtUtc);

public sealed record ManagedMediaPath(
    string Path,
    string AnimeKey,
    AcquisitionOwner Owner,
    string? JobId,
    DateTimeOffset UpdatedAtUtc);

public sealed record SonarrObservedState(
    IReadOnlySet<string> ActiveReleaseKeys,
    IReadOnlySet<string> ActivePaths)
{
    public static SonarrObservedState Empty { get; } =
        new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}

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
    public static AcquisitionOwnershipState Empty() =>
        new(
            1,
            new Dictionary<string, AnimeManagementAssignment>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, AcquisitionOwnership>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, ManagedMediaPath>(StringComparer.OrdinalIgnoreCase));
}
