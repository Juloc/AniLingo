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
    Copy
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
    double AutoImportConfidenceThreshold = 0.90);

public sealed record PlannedAnimeImport(
    CompletedDownloadFile Source,
    AnimeImportDisposition Disposition,
    AnimeImportFileAction FileAction,
    IReadOnlyList<RequestedAnimeEpisode> Targets,
    IReadOnlyList<string> SidecarPaths,
    IReadOnlyList<string> ExistingPathsToReplaceAfterCommit,
    double Confidence,
    IReadOnlyList<string> Reasons);

public sealed record CompletedDownloadImportPlan(
    string AcquisitionId,
    IReadOnlyList<PlannedAnimeImport> Files)
{
    public bool RequiresManualIntervention =>
        Files.Any(file => file.Disposition == AnimeImportDisposition.ManualReview);

    public int AutoImportCount =>
        Files.Count(file => file.Disposition == AnimeImportDisposition.AutoImport);
}
