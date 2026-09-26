using System.Text.RegularExpressions;
using AniLingo.Web.Features.Acquisition.Ownership;
using AniLingo.Web.Features.Acquisition.Quality;

namespace AniLingo.Web.Features.Acquisition.Import;

public static class CompletedDownloadImportPlanner
{
    private static readonly HashSet<string> VideoExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mkv", ".mp4", ".m4v", ".avi", ".ts", ".m2ts", ".webm"
        };

    private static readonly HashSet<string> SidecarExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".srt", ".ass", ".ssa", ".vtt", ".nfo"
        };

    public static CompletedDownloadImportPlan Plan(
        CompletedDownloadImportContext context,
        IEnumerable<CompletedDownloadFile> completedFiles,
        IEnumerable<ExistingAnimeFile>? existingFiles = null,
        AcquisitionOwnershipSnapshot? ownership = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(completedFiles);
        ValidateContext(context);

        var allFiles = completedFiles
            .Where(file => !string.IsNullOrWhiteSpace(file.Path))
            .ToArray();

        if (ownership is not null)
        {
            var download = SonarrParallelSafety.CanImport(
                ownership,
                new AcquisitionImportRequest(context.AnimeKey, context.AcquisitionId, context.DownloadId, ""));
            if (!download.Allowed)
            {
                var reason = $"Ownership: {download.Reason}";
                return new CompletedDownloadImportPlan(
                    context.AcquisitionId,
                    allFiles
                        .Select(file => new PlannedAnimeImport(
                            file,
                            AnimeImportDisposition.Ignore,
                            context.PreferredAction,
                            [],
                            [],
                            [],
                            0,
                            [reason]))
                        .ToArray(),
                    reason);
            }
        }

        var videos = allFiles
            .Where(file => VideoExtensions.Contains(Path.GetExtension(file.Path)))
            .ToArray();

        var sidecars = allFiles
            .Where(file => SidecarExtensions.Contains(Path.GetExtension(file.Path)))
            .ToArray();

        var existing = existingFiles?.ToArray() ?? [];
        var plans = new List<PlannedAnimeImport>();

        foreach (var video in videos)
        {
            var planned = PlanVideo(context, video, videos.Length, sidecars, existing);
            plans.Add(ownership is null ? planned : ApplyOwnership(context, planned, ownership));
        }

        foreach (var nonVideo in allFiles.Except(videos).Except(sidecars))
        {
            plans.Add(new PlannedAnimeImport(
                nonVideo,
                AnimeImportDisposition.Ignore,
                context.PreferredAction,
                [],
                [],
                [],
                1,
                ["Unsupported import file type."]));
        }

        return new CompletedDownloadImportPlan(context.AcquisitionId, plans);
    }

    // Sonarr-owned sources are left to Sonarr; replacing a file AniLingo may not mutate needs
    // an owner decision instead of an automatic import.
    private static PlannedAnimeImport ApplyOwnership(
        CompletedDownloadImportContext context,
        PlannedAnimeImport planned,
        AcquisitionOwnershipSnapshot ownership)
    {
        var source = SonarrParallelSafety.CanImport(
            ownership,
            new AcquisitionImportRequest(
                context.AnimeKey,
                context.AcquisitionId,
                context.DownloadId,
                planned.Source.Path));
        if (!source.Allowed)
        {
            return planned with
            {
                Disposition = AnimeImportDisposition.Ignore,
                ExistingPathsToReplaceAfterCommit = [],
                Reasons = [.. planned.Reasons, $"Ownership: {source.Reason}"]
            };
        }

        if (planned.Disposition != AnimeImportDisposition.AutoImport)
        {
            return planned;
        }

        foreach (var replacement in planned.ExistingPathsToReplaceAfterCommit)
        {
            var decision = SonarrParallelSafety.CanMutateLibraryPath(ownership, context.AnimeKey, replacement);
            if (!decision.Allowed)
            {
                return planned with
                {
                    Disposition = AnimeImportDisposition.ManualReview,
                    ExistingPathsToReplaceAfterCommit = [],
                    Reasons = [.. planned.Reasons, $"Ownership: existing file cannot be replaced. {decision.Reason}"]
                };
            }
        }

        return planned;
    }

    private static PlannedAnimeImport PlanVideo(
        CompletedDownloadImportContext context,
        CompletedDownloadFile video,
        int videoCount,
        IReadOnlyList<CompletedDownloadFile> sidecars,
        IReadOnlyList<ExistingAnimeFile> existing)
    {
        var reasons = new List<string>();
        var fileName = Path.GetFileName(video.Path);
        var release = AnimeReleaseParser.Parse(fileName);
        var mapping = ResolveTargets(context, release, videoCount);

        reasons.AddRange(mapping.Reasons);

        if (!SeriesMatches(context.SeriesAliases, release.SeriesTitle))
        {
            reasons.Add($"Parsed series title '{release.SeriesTitle}' does not match a configured alias.");
            return Manual(video, context, mapping.Targets, MatchSidecars(video, sidecars), mapping.Confidence, reasons);
        }

        if (mapping.Targets.Count == 0)
        {
            return Manual(video, context, [], MatchSidecars(video, sidecars), mapping.Confidence, reasons);
        }

        var candidateScore = AnimeReleaseScorer.Score(
            context.QualityProfile,
            new AnimeReleaseCandidate(release, video.SizeBytes));

        if (!candidateScore.Accepted)
        {
            reasons.AddRange(candidateScore.RejectionReasons.Select(reason => $"Downloaded release rejected by current profile: {reason}"));
            return Manual(video, context, mapping.Targets, MatchSidecars(video, sidecars), mapping.Confidence, reasons);
        }

        var replacementPaths = new List<string>();
        foreach (var target in mapping.Targets)
        {
            var current = existing.FirstOrDefault(file =>
                file.SeasonNumber == target.SeasonNumber &&
                file.EpisodeNumber == target.EpisodeNumber);

            if (current is null)
            {
                continue;
            }

            var currentRelease = AnimeReleaseParser.Parse(Path.GetFileName(current.Path));
            var currentScore = AnimeReleaseScorer.Score(
                context.QualityProfile,
                new AnimeReleaseCandidate(currentRelease, current.SizeBytes));

            if (!currentScore.Accepted)
            {
                reasons.Add($"Existing S{target.SeasonNumber:00}E{target.EpisodeNumber:00} quality cannot be compared safely.");
                return Manual(video, context, mapping.Targets, MatchSidecars(video, sidecars), mapping.Confidence, reasons);
            }

            if (!AnimeReleaseScorer.IsUpgrade(context.QualityProfile, currentScore, candidateScore))
            {
                reasons.Add($"Existing S{target.SeasonNumber:00}E{target.EpisodeNumber:00} is equal or preferred by the assigned quality profile.");
                return new PlannedAnimeImport(
                    video,
                    AnimeImportDisposition.Ignore,
                    context.PreferredAction,
                    mapping.Targets,
                    MatchSidecars(video, sidecars),
                    [],
                    mapping.Confidence,
                    reasons);
            }

            replacementPaths.Add(current.Path);
        }

        if (mapping.Confidence < context.AutoImportConfidenceThreshold)
        {
            reasons.Add(
                $"Mapping confidence {mapping.Confidence:0.00} is below auto-import threshold {context.AutoImportConfidenceThreshold:0.00}.");
            return Manual(video, context, mapping.Targets, MatchSidecars(video, sidecars), mapping.Confidence, reasons);
        }

        if (replacementPaths.Count > 0)
        {
            reasons.Add("Existing files are preserved until the replacement import commits successfully.");
        }

        return new PlannedAnimeImport(
            video,
            AnimeImportDisposition.AutoImport,
            context.PreferredAction,
            mapping.Targets,
            MatchSidecars(video, sidecars),
            replacementPaths,
            mapping.Confidence,
            reasons);
    }

    private static MappingResult ResolveTargets(
        CompletedDownloadImportContext context,
        AnimeReleaseInfo release,
        int videoCount)
    {
        if (release.SeasonNumber is int season &&
            release.EpisodeStart is int episodeStart &&
            release.EpisodeEnd is int episodeEnd)
        {
            var expectedCount = episodeEnd - episodeStart + 1;
            if (expectedCount <= 0 || expectedCount > 100)
            {
                return new([], 0, ["Invalid parsed season/episode range."]);
            }

            var targets = context.RequestedEpisodes
                .Where(item =>
                    item.SeasonNumber == season &&
                    item.EpisodeNumber >= episodeStart &&
                    item.EpisodeNumber <= episodeEnd)
                .OrderBy(item => item.EpisodeNumber)
                .ToArray();

            if (targets.Length == expectedCount)
            {
                return new(
                    targets,
                    1.0,
                    [$"Exact S{season:00}E{episodeStart:00}" +
                     (episodeEnd == episodeStart ? "" : $"-E{episodeEnd:00}") +
                     " mapping from filename."]);
            }

            return new(
                targets,
                0.35,
                ["Parsed season/episode range does not exactly match the episodes requested by this acquisition."]);
        }

        if (release.AbsoluteEpisodeStart is int absoluteStart &&
            release.AbsoluteEpisodeEnd is int absoluteEnd)
        {
            var expectedCount = absoluteEnd - absoluteStart + 1;
            if (expectedCount <= 0 || expectedCount > 100)
            {
                return new([], 0, ["Invalid parsed absolute episode range."]);
            }

            var targets = context.RequestedEpisodes
                .Where(item =>
                    item.AbsoluteEpisodeNumber is int absolute &&
                    absolute >= absoluteStart &&
                    absolute <= absoluteEnd)
                .OrderBy(item => item.AbsoluteEpisodeNumber)
                .ToArray();

            if (targets.Length == expectedCount)
            {
                return new(
                    targets,
                    0.98,
                    [$"Exact absolute episode {absoluteStart}" +
                     (absoluteEnd == absoluteStart ? "" : $"-{absoluteEnd}") +
                     " mapping from filename."]);
            }

            return new(
                targets,
                0.35,
                ["Parsed absolute episode range does not exactly match the episodes requested by this acquisition."]);
        }

        if (context.RequestedEpisodes.Count == 1 && videoCount == 1)
        {
            return new(
                [context.RequestedEpisodes[0]],
                0.70,
                ["Single-file/single-request fallback has no episode number evidence in the filename."]);
        }

        return new(
            [],
            0,
            ["Episode identity could not be resolved safely from the completed file."]);
    }

    // Shared by the grab path so a search result is matched to the anime by the same rule as an
    // imported file. Without aliases every title matches.
    public static bool SeriesMatches(
        IReadOnlyList<string> aliases,
        string parsedSeriesTitle)
    {
        if (aliases.Count == 0 || string.IsNullOrWhiteSpace(parsedSeriesTitle))
        {
            return true;
        }

        var parsed = NormalizeTitle(parsedSeriesTitle);
        return aliases.Any(alias => NormalizeTitle(alias) == parsed);
    }

    private static string NormalizeTitle(string value) =>
        Regex.Replace(
                value.Normalize().ToLowerInvariant(),
                @"[^\p{L}\p{N}]+",
                " ",
                RegexOptions.CultureInvariant)
            .Trim();

    private static string[] MatchSidecars(
        CompletedDownloadFile video,
        IReadOnlyList<CompletedDownloadFile> sidecars)
    {
        var directory = Path.GetDirectoryName(video.Path) ?? "";
        var stem = Path.GetFileNameWithoutExtension(video.Path);

        return sidecars
            .Where(sidecar =>
                string.Equals(Path.GetDirectoryName(sidecar.Path) ?? "", directory, StringComparison.Ordinal) &&
                (Path.GetFileNameWithoutExtension(sidecar.Path).Equals(stem, StringComparison.OrdinalIgnoreCase) ||
                 Path.GetFileName(sidecar.Path).StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)))
            .Select(sidecar => sidecar.Path)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static PlannedAnimeImport Manual(
        CompletedDownloadFile video,
        CompletedDownloadImportContext context,
        IReadOnlyList<RequestedAnimeEpisode> targets,
        IReadOnlyList<string> sidecars,
        double confidence,
        IReadOnlyList<string> reasons) =>
        new(
            video,
            AnimeImportDisposition.ManualReview,
            context.PreferredAction,
            targets,
            sidecars,
            [],
            confidence,
            reasons);

    private static void ValidateContext(CompletedDownloadImportContext context)
    {
        if (string.IsNullOrWhiteSpace(context.AcquisitionId))
        {
            throw new ArgumentException("Acquisition ID is required.", nameof(context));
        }

        if (string.IsNullOrWhiteSpace(context.AnimeKey))
        {
            throw new ArgumentException("Anime key is required.", nameof(context));
        }

        if (context.RequestedEpisodes.Count == 0)
        {
            throw new ArgumentException("At least one requested episode is required.", nameof(context));
        }

        if (context.AutoImportConfidenceThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(context),
                "Auto-import confidence threshold must be between 0 and 1.");
        }
    }

    private sealed record MappingResult(
        IReadOnlyList<RequestedAnimeEpisode> Targets,
        double Confidence,
        IReadOnlyList<string> Reasons);
}
