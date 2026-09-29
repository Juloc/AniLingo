using Jularr.Web.Features.Acquisition.Quality;

namespace Jularr.Web.Features.Acquisition.Import;

// The Anime-specific import plan: which completed file is which requested episode, and whether it
// replaces an existing one. Everything that is not episode mapping (finding the files, placing
// them, replacing files after the commit) is shared: CompletedDownloadFiles, LibraryFilePlacer.

public enum AnimeImportDisposition
{
    AutoImport,
    ManualReview,
    Ignore
}

public sealed record RequestedAnimeEpisode(
    int SeasonNumber,
    int EpisodeNumber,
    int? AbsoluteEpisodeNumber = null);

public sealed record ExistingAnimeFile(
    string Path,
    int SeasonNumber,
    int EpisodeNumber,
    long SizeBytes);

public sealed record AnimeImportPlanContext(
    string AcquisitionId,
    string AnimeKey,
    IReadOnlyList<string> SeriesAliases,
    IReadOnlyList<RequestedAnimeEpisode> RequestedEpisodes,
    AnimeQualityProfile QualityProfile,
    ImportFileAction PreferredAction = ImportFileAction.Move,
    // Only meaningful when PreferredAction is Hardlink: the owner explicitly chose "Hardlink or
    // copy", so a cross-filesystem hardlink falls back to a copy instead of failing the import.
    bool AllowHardlinkFallbackToCopy = false,
    double AutoImportConfidenceThreshold = 0.90,
    string? DownloadId = null);

public sealed record PlannedAnimeImport(
    CompletedDownloadFile Source,
    AnimeImportDisposition Disposition,
    ImportFileAction FileAction,
    IReadOnlyList<RequestedAnimeEpisode> Targets,
    IReadOnlyList<string> SidecarPaths,
    IReadOnlyList<string> ExistingPathsToReplaceAfterCommit,
    double Confidence,
    IReadOnlyList<string> Reasons,
    bool AllowHardlinkFallbackToCopy = false);

public sealed record AnimeImportPlan(
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
