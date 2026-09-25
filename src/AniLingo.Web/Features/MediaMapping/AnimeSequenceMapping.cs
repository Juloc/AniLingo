namespace AniLingo.Web.Features.MediaMapping;

public sealed record LocalEpisodeCoordinate(
    int SeasonNumber,
    int EpisodeNumber);

public sealed record RemoteAnimePart(
    string Provider,
    string ExternalId,
    string Title,
    int EpisodeCount);

public sealed record PlannedAnimeEpisodeRange(
    int SeasonNumber,
    int LocalEpisodeStart,
    int LocalEpisodeEnd,
    int RemoteEpisodeStart,
    RemoteAnimePart RemotePart);

public sealed record AnimeSequenceMappingPlan(
    bool CanApply,
    string Reason,
    IReadOnlyList<PlannedAnimeEpisodeRange> Ranges)
{
    public static AnimeSequenceMappingPlan Blocked(string reason) =>
        new(false, reason, []);
}

public static class AnimeSequenceMappingPlanner
{
    public static AnimeSequenceMappingPlan Plan(
        IReadOnlyList<LocalEpisodeCoordinate> localEpisodes,
        IReadOnlyList<RemoteAnimePart> remoteSequence,
        string anchorExternalId)
    {
        if (localEpisodes.Count == 0)
        {
            return AnimeSequenceMappingPlan.Blocked(
                "No local episodes are available for automatic mapping.");
        }

        if (remoteSequence.Count == 0 ||
            string.IsNullOrWhiteSpace(anchorExternalId))
        {
            return AnimeSequenceMappingPlan.Blocked(
                "No AniList sequel/part sequence is available.");
        }

        if (localEpisodes.Any(x => x.EpisodeNumber <= 0) ||
            !LocalOrderingIsContiguous(localEpisodes))
        {
            return AnimeSequenceMappingPlan.Blocked(
                "Local episode numbering contains gaps or unsupported episode numbers.");
        }

        if (remoteSequence.Any(x => x.EpisodeCount <= 0))
        {
            return AnimeSequenceMappingPlan.Blocked(
                "AniList does not expose reliable episode counts for the full sequence.");
        }

        var matchingWindows = new List<(int Start, int End)>();
        for (var start = 0; start < remoteSequence.Count; start++)
        {
            var sum = 0;
            var containsAnchor = false;
            for (var end = start; end < remoteSequence.Count; end++)
            {
                checked
                {
                    sum += remoteSequence[end].EpisodeCount;
                }

                containsAnchor |= string.Equals(
                    remoteSequence[end].ExternalId,
                    anchorExternalId,
                    StringComparison.Ordinal);

                if (sum == localEpisodes.Count && containsAnchor)
                {
                    matchingWindows.Add((start, end));
                }

                if (sum >= localEpisodes.Count)
                {
                    break;
                }
            }
        }

        if (matchingWindows.Count != 1)
        {
            return AnimeSequenceMappingPlan.Blocked(
                matchingWindows.Count == 0
                    ? "Local episode count does not match one unambiguous AniList part/sequel range."
                    : "More than one AniList part/sequel range fits the local episode count.");
        }

        var window = matchingWindows[0];
        var ranges = new List<PlannedAnimeEpisodeRange>();
        var localIndex = 0;

        for (var remoteIndex = window.Start; remoteIndex <= window.End; remoteIndex++)
        {
            var remote = remoteSequence[remoteIndex];
            var remaining = remote.EpisodeCount;
            var remoteEpisode = 1;

            while (remaining > 0)
            {
                if (localIndex >= localEpisodes.Count)
                {
                    return AnimeSequenceMappingPlan.Blocked(
                        "The local episode list ended before the AniList range.");
                }

                var season = localEpisodes[localIndex].SeasonNumber;
                var localStart = localEpisodes[localIndex].EpisodeNumber;
                var localEnd = localStart;
                var segmentLength = 1;

                while (segmentLength < remaining &&
                       localIndex + segmentLength < localEpisodes.Count)
                {
                    var previous = localEpisodes[localIndex + segmentLength - 1];
                    var next = localEpisodes[localIndex + segmentLength];

                    if (next.SeasonNumber != season ||
                        next.EpisodeNumber != previous.EpisodeNumber + 1)
                    {
                        break;
                    }

                    localEnd = next.EpisodeNumber;
                    segmentLength++;
                }

                ranges.Add(new PlannedAnimeEpisodeRange(
                    season,
                    localStart,
                    localEnd,
                    remoteEpisode,
                    remote));

                localIndex += segmentLength;
                remoteEpisode += segmentLength;
                remaining -= segmentLength;
            }
        }

        return localIndex == localEpisodes.Count
            ? new AnimeSequenceMappingPlan(
                true,
                "Local episodes map uniquely to the AniList sequel/part sequence.",
                ranges)
            : AnimeSequenceMappingPlan.Blocked(
                "Not every local episode was consumed by the AniList sequence.");
    }

    private static bool LocalOrderingIsContiguous(
        IReadOnlyList<LocalEpisodeCoordinate> episodes)
    {
        for (var index = 1; index < episodes.Count; index++)
        {
            var previous = episodes[index - 1];
            var current = episodes[index];

            if (current.SeasonNumber < previous.SeasonNumber)
            {
                return false;
            }

            if (current.SeasonNumber == previous.SeasonNumber &&
                current.EpisodeNumber != previous.EpisodeNumber + 1)
            {
                return false;
            }

            if (current.SeasonNumber > previous.SeasonNumber &&
                current.EpisodeNumber <= 0)
            {
                return false;
            }
        }

        return true;
    }
}
