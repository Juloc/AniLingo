namespace AniLingo.Web.Features.Acquisition.Ownership;

public enum SonarrPathOwnershipKind
{
    None,
    QueueOutput,
    EpisodeFile,
    SeriesFolder
}

public sealed record SonarrPathOwnership(
    SonarrPathOwnershipKind Kind,
    SonarrObservedSeries? Series,
    string Detail)
{
    public static SonarrPathOwnership None { get; } =
        new(SonarrPathOwnershipKind.None, null, "Path is not known to Sonarr.");

    public bool IsSonarrOwned => Kind != SonarrPathOwnershipKind.None;
}

public sealed record AcquisitionGrabRequest(
    string AnimeKey,
    string ReleaseKey,
    int? SeasonNumber,
    int? EpisodeStart,
    int? EpisodeEnd,
    int? AbsoluteEpisodeStart = null,
    int? AbsoluteEpisodeEnd = null)
{
    public bool Covers(SonarrObservedEpisode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);

        if (SeasonNumber is int season &&
            EpisodeStart is int start &&
            EpisodeEnd is int end &&
            episode.SeasonNumber == season &&
            episode.EpisodeNumber >= start &&
            episode.EpisodeNumber <= end)
        {
            return true;
        }

        return AbsoluteEpisodeStart is int absoluteStart &&
               AbsoluteEpisodeEnd is int absoluteEnd &&
               episode.AbsoluteEpisodeNumber is int absolute &&
               absolute >= absoluteStart &&
               absolute <= absoluteEnd;
    }
}

public sealed record AcquisitionImportRequest(
    string AnimeKey,
    string JobId,
    string? DownloadId,
    string SourcePath);

// Recognizes Sonarr-owned series, paths and downloads from a read-only observation.
public static class SonarrOwnershipRecognizer
{
    public static int? GetLinkedSeriesId(AcquisitionOwnershipState state, string animeKey) =>
        state.Anime.TryGetValue(animeKey, out var assignment)
            ? assignment.SonarrSeriesId
            : null;

    public static SonarrObservedSeries? FindSeries(SonarrObservedState sonarr, int? seriesId) =>
        seriesId is int id
            ? sonarr.Series.FirstOrDefault(series => series.Id == id)
            : null;

    public static SonarrPathOwnership RecognizePath(SonarrObservedState sonarr, string path)
    {
        ArgumentNullException.ThrowIfNull(sonarr);
        var normalized = NormalizeOrNull(path);
        if (normalized is null)
        {
            return SonarrPathOwnership.None;
        }

        foreach (var output in sonarr.Queue)
        {
            var outputPath = NormalizeOrNull(output.OutputPath);
            if (outputPath is not null && IsSameOrUnder(normalized, outputPath))
            {
                return new(
                    SonarrPathOwnershipKind.QueueOutput,
                    FindSeries(sonarr, output.SeriesId),
                    $"Path belongs to the active Sonarr download '{output.Title}'.");
            }
        }

        foreach (var file in sonarr.EpisodeFiles)
        {
            var filePath = NormalizeOrNull(file.Path);
            if (filePath is not null &&
                filePath.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                var series = FindSeries(sonarr, file.SeriesId);
                return new(
                    SonarrPathOwnershipKind.EpisodeFile,
                    series,
                    $"Path is a Sonarr episode file of '{series?.Title ?? $"series {file.SeriesId}"}'.");
            }
        }

        SonarrObservedSeries? owner = null;
        var ownerRootLength = -1;
        foreach (var series in sonarr.Series)
        {
            var root = NormalizeOrNull(series.Path);
            if (root is not null &&
                root.Length > ownerRootLength &&
                IsSameOrUnder(normalized, root))
            {
                owner = series;
                ownerRootLength = root.Length;
            }
        }

        return owner is null
            ? SonarrPathOwnership.None
            : new(
                SonarrPathOwnershipKind.SeriesFolder,
                owner,
                $"Path is inside the Sonarr series folder of '{owner.Title}'.");
    }

    public static bool IsSonarrDownload(SonarrObservedState sonarr, string? downloadId)
    {
        ArgumentNullException.ThrowIfNull(sonarr);
        if (string.IsNullOrWhiteSpace(downloadId))
        {
            return false;
        }

        return sonarr.ActiveDownloadIds.Contains(downloadId) ||
               sonarr.Queue.Any(item =>
                   string.Equals(item.DownloadId, downloadId, StringComparison.OrdinalIgnoreCase)) ||
               sonarr.History.Any(item =>
                   string.Equals(item.DownloadId, downloadId, StringComparison.OrdinalIgnoreCase));
    }

    public static bool PathEquals(string? left, string? right)
    {
        var a = NormalizeOrNull(left);
        var b = NormalizeOrNull(right);
        return a is not null && b is not null && a.Equals(b, StringComparison.OrdinalIgnoreCase);
    }

    public static string? NormalizeOrNull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var normalized = SonarrParallelSafety.NormalizePath(path.Trim());
            return normalized.Length == 0 ? null : normalized;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsSameOrUnder(string path, string root)
    {
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.Length > root.Length &&
               path.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
               (path[root.Length] == Path.DirectorySeparatorChar ||
                path[root.Length] == Path.AltDirectorySeparatorChar);
    }
}
