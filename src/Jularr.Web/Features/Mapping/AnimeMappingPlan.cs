namespace Jularr.Web.Features.Mapping;

/// <summary>Coverage state of a local episode (or a proposed range) against the provider mapping set.</summary>
public enum AnimeMappingState
{
    /// <summary>Local episode maps 1:1 to a provider episode within a fully-covered, in-bounds range.</summary>
    Exact,

    /// <summary>Mapped, but the range extends past the known local episodes or the provider episode count.</summary>
    Partial,

    /// <summary>Local episode has no mapping and is not explicitly unmapped.</summary>
    Missing,

    /// <summary>Local episode is covered by more than one range, or a range overlaps another.</summary>
    Conflict,

    /// <summary>Local episode is explicitly declared unmapped (e.g. specials with no provider entry).</summary>
    Unmapped
}

/// <summary>
/// One proposed range: a local episode range (season + start..end) mapped to a provider's episode
/// numbering starting at <see cref="RemoteStart"/>, or explicitly <see cref="Unmapped"/>. A range
/// where <see cref="LocalStart"/> equals <see cref="LocalEnd"/> is a single-episode override.
/// </summary>
public sealed record AnimeMappingRange(
    int SeasonNumber,
    int LocalStart,
    int LocalEnd,
    string Provider,
    string ExternalId,
    int RemoteStart,
    string? PreferredTitle = null,
    int? RemoteEpisodeCount = null,
    bool Unmapped = false)
{
    public bool Covers(int seasonNumber, int episodeNumber) =>
        SeasonNumber == seasonNumber &&
        episodeNumber >= LocalStart &&
        episodeNumber <= LocalEnd;

    public bool OverlapsLocal(AnimeMappingRange other) =>
        SeasonNumber == other.SeasonNumber &&
        LocalStart <= other.LocalEnd &&
        LocalEnd >= other.LocalStart;

    public int ResolveRemoteEpisode(int localEpisodeNumber) =>
        RemoteStart + (localEpisodeNumber - LocalStart);
}

/// <summary>A local episode as it exists in Jularr's internal structure (season 0 = specials).</summary>
public sealed record LocalEpisodeRef(int SeasonNumber, int Number)
{
    public bool IsSpecial => SeasonNumber == 0;
}

/// <summary>A proposed range with its computed state and a short human note.</summary>
public sealed record AnimeMappingRangePreview(
    AnimeMappingRange Range,
    AnimeMappingState State,
    string Note);

/// <summary>One local episode row for the local-vs-provider side-by-side view.</summary>
public sealed record AnimeEpisodeMappingRow(
    int SeasonNumber,
    int Number,
    bool IsSpecial,
    AnimeMappingState State,
    string? Provider,
    string? ExternalId,
    int? RemoteEpisode);

/// <summary>
/// The full preview of applying a proposed range set to a work's local episodes: every range with
/// its state, every local episode with its resolved provider coordinate/state, and any hard problems
/// that block apply. Building this never mutates anything — it is the preview-before-apply step.
/// </summary>
public sealed record AnimeMappingPreview(
    bool CanApply,
    IReadOnlyList<AnimeMappingRangePreview> Ranges,
    IReadOnlyList<AnimeEpisodeMappingRow> Episodes,
    IReadOnlyList<string> Problems)
{
    public int ExactCount => Episodes.Count(x => x.State == AnimeMappingState.Exact);
    public int PartialCount => Episodes.Count(x => x.State == AnimeMappingState.Partial);
    public int MissingCount => Episodes.Count(x => x.State == AnimeMappingState.Missing);
    public int ConflictCount => Episodes.Count(x => x.State == AnimeMappingState.Conflict);
    public int UnmappedCount => Episodes.Count(x => x.State == AnimeMappingState.Unmapped);
}

/// <summary>
/// Pure classifier for anime range mappings. Given the local episodes and a proposed range set it
/// derives per-range and per-episode states (exact / partial / missing / conflict / unmapped) with
/// no I/O, so both the preview UI and the tests share one source of truth for what a mapping means.
/// </summary>
public static class AnimeMappingPlanner
{
    public static AnimeMappingPreview BuildPreview(
        IReadOnlyList<LocalEpisodeRef> localEpisodes,
        IReadOnlyList<AnimeMappingRange> ranges)
    {
        var problems = new List<string>();
        var episodesBySeason = localEpisodes
            .GroupBy(x => x.SeasonNumber)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Number).ToHashSet());

        // Per-range validity and overlap detection.
        var conflicting = new bool[ranges.Count];
        for (var i = 0; i < ranges.Count; i++)
        {
            for (var j = i + 1; j < ranges.Count; j++)
            {
                if (ranges[i].OverlapsLocal(ranges[j]))
                {
                    conflicting[i] = true;
                    conflicting[j] = true;
                    problems.Add(
                        $"Ranges S{ranges[i].SeasonNumber:00}E{ranges[i].LocalStart:00}-{ranges[i].LocalEnd:00} " +
                        $"and S{ranges[j].SeasonNumber:00}E{ranges[j].LocalStart:00}-{ranges[j].LocalEnd:00} overlap.");
                }
            }
        }

        var rangePreviews = new List<AnimeMappingRangePreview>(ranges.Count);
        for (var i = 0; i < ranges.Count; i++)
        {
            rangePreviews.Add(ClassifyRange(ranges[i], conflicting[i], episodesBySeason, problems));
        }

        var episodeRows = localEpisodes
            .OrderBy(x => x.SeasonNumber)
            .ThenBy(x => x.Number)
            .Select(episode => ClassifyEpisode(episode, ranges))
            .ToArray();

        var canApply = problems.Count == 0 &&
            rangePreviews.All(x => x.State != AnimeMappingState.Conflict) &&
            episodeRows.All(x => x.State != AnimeMappingState.Conflict);

        return new AnimeMappingPreview(canApply, rangePreviews, episodeRows, problems);
    }

    private static AnimeMappingRangePreview ClassifyRange(
        AnimeMappingRange range,
        bool overlaps,
        IReadOnlyDictionary<int, HashSet<int>> episodesBySeason,
        List<string> problems)
    {
        if (range.LocalStart <= 0 || range.LocalEnd < range.LocalStart)
        {
            problems.Add($"Range S{range.SeasonNumber:00}E{range.LocalStart:00}-{range.LocalEnd:00} has an invalid local span.");
            return new AnimeMappingRangePreview(range, AnimeMappingState.Conflict, "Invalid local episode span.");
        }

        if (range.Unmapped)
        {
            return new AnimeMappingRangePreview(range, AnimeMappingState.Unmapped, "Explicitly left unmapped.");
        }

        if (range.RemoteStart <= 0 || !MappingProviders.IsKnown(range.Provider) || string.IsNullOrWhiteSpace(range.ExternalId))
        {
            problems.Add($"Range S{range.SeasonNumber:00}E{range.LocalStart:00}-{range.LocalEnd:00} has an invalid provider target.");
            return new AnimeMappingRangePreview(range, AnimeMappingState.Conflict, "Invalid provider target.");
        }

        if (overlaps)
        {
            return new AnimeMappingRangePreview(range, AnimeMappingState.Conflict, "Overlaps another range.");
        }

        var span = range.LocalEnd - range.LocalStart + 1;
        var localForSeason = episodesBySeason.TryGetValue(range.SeasonNumber, out var set) ? set : [];
        var missingLocal = Enumerable.Range(range.LocalStart, span).Count(n => !localForSeason.Contains(n));
        var remoteEnd = range.RemoteStart + span - 1;
        var remoteOverflow = range.RemoteEpisodeCount is > 0 && remoteEnd > range.RemoteEpisodeCount.Value;

        if (missingLocal > 0)
        {
            return new AnimeMappingRangePreview(
                range,
                AnimeMappingState.Partial,
                $"{missingLocal} local episode(s) in this span do not exist yet.");
        }

        if (remoteOverflow)
        {
            return new AnimeMappingRangePreview(
                range,
                AnimeMappingState.Partial,
                $"Reaches provider episode {remoteEnd}, above the known count ({range.RemoteEpisodeCount}).");
        }

        return new AnimeMappingRangePreview(range, AnimeMappingState.Exact, "Maps 1:1.");
    }

    private static AnimeEpisodeMappingRow ClassifyEpisode(
        LocalEpisodeRef episode,
        IReadOnlyList<AnimeMappingRange> ranges)
    {
        var covering = ranges.Where(r => r.Covers(episode.SeasonNumber, episode.Number)).ToArray();

        if (covering.Length == 0)
        {
            return new AnimeEpisodeMappingRow(
                episode.SeasonNumber, episode.Number, episode.IsSpecial,
                AnimeMappingState.Missing, null, null, null);
        }

        if (covering.Length > 1)
        {
            return new AnimeEpisodeMappingRow(
                episode.SeasonNumber, episode.Number, episode.IsSpecial,
                AnimeMappingState.Conflict, null, null, null);
        }

        var range = covering[0];
        if (range.Unmapped)
        {
            return new AnimeEpisodeMappingRow(
                episode.SeasonNumber, episode.Number, episode.IsSpecial,
                AnimeMappingState.Unmapped, null, null, null);
        }

        var remoteEpisode = range.ResolveRemoteEpisode(episode.Number);
        var overflow = range.RemoteEpisodeCount is > 0 && remoteEpisode > range.RemoteEpisodeCount.Value;
        var state = overflow ? AnimeMappingState.Partial : AnimeMappingState.Exact;

        return new AnimeEpisodeMappingRow(
            episode.SeasonNumber, episode.Number, episode.IsSpecial,
            state, range.Provider, range.ExternalId, remoteEpisode);
    }
}
