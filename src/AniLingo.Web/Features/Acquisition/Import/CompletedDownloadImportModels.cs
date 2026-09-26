using AniLingo.Web.Features.Acquisition.Quality;

namespace AniLingo.Web.Features.Acquisition.Import;

public enum AnimeImportDisposition
{
    AutoImport,
    ManualReview,
    Ignore
}

public enum AnimeImportFileAction
{
    Move,
    Copy,
    Hardlink
}

public sealed record RequestedAnimeEpisode(
    int SeasonNumber,
    int EpisodeNumber,
    int? AbsoluteEpisodeNumber = null);

public sealed record CompletedDownloadFile(
    string Path,
    long SizeBytes);

public sealed record ExistingAnimeFile(
    string Path,
    int SeasonNumber,
    int EpisodeNumber,
    long SizeBytes);

public sealed record CompletedDownloadImportContext(
    string AcquisitionId,
    string AnimeKey,
    IReadOnlyList<string> SeriesAliases,
    IReadOnlyList<RequestedAnimeEpisode> RequestedEpisodes,
    AnimeQualityProfile QualityProfile,
    AnimeImportFileAction PreferredAction = AnimeImportFileAction.Move,
    // Only meaningful when PreferredAction is Hardlink: the owner explicitly chose "Hardlink or
    // copy", so a cross-filesystem hardlink falls back to a copy instead of failing the import.
    bool AllowHardlinkFallbackToCopy = false,
    double AutoImportConfidenceThreshold = 0.90,
    string? DownloadId = null);

public sealed record PlannedAnimeImport(
    CompletedDownloadFile Source,
    AnimeImportDisposition Disposition,
    AnimeImportFileAction FileAction,
    IReadOnlyList<RequestedAnimeEpisode> Targets,
    IReadOnlyList<string> SidecarPaths,
    IReadOnlyList<string> ExistingPathsToReplaceAfterCommit,
    double Confidence,
    IReadOnlyList<string> Reasons,
    bool AllowHardlinkFallbackToCopy = false);

public sealed record CompletedDownloadImportPlan(
    string AcquisitionId,
    IReadOnlyList<PlannedAnimeImport> Files,
    string? OwnershipBlockReason = null)
{
    public bool BlockedByOwnership => OwnershipBlockReason is not null;

    public bool RequiresManualIntervention =>
        Files.Any(file => file.Disposition == AnimeImportDisposition.ManualReview);

    public int AutoImportCount =>
        Files.Count(file => file.Disposition == AnimeImportDisposition.AutoImport);
}
