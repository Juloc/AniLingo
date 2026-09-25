namespace AniLingo.Web.Features.Progress;

public sealed record EpisodeOrderKey(
    Guid Id,
    int SeasonNumber,
    int Number);

public sealed record EpisodeNeighborIds(
    Guid? PreviousEpisodeId,
    Guid? NextEpisodeId)
{
    public static EpisodeNeighborIds None { get; } = new(null, null);
}

/// <summary>
/// Canonical previous/next resolution over the local episodes of one anime.
/// The resolver never guesses: it only moves to the directly adjacent local
/// episode number, crosses a season boundary only from the last local episode
/// of season N to episode 1 of season N + 1 (and back), never crosses into or
/// out of specials (season 0), and returns no neighbor when the current or the
/// target position is ambiguous (duplicate season/episode numbers) or when the
/// local numbering has a gap.
/// </summary>
public static class EpisodeSequence
{
    public static EpisodeNeighborIds Resolve(
        IReadOnlyCollection<EpisodeOrderKey> localEpisodes,
        Guid currentEpisodeId)
    {
        var current = localEpisodes.FirstOrDefault(x => x.Id == currentEpisodeId);
        if (current is null ||
            Count(localEpisodes, current.SeasonNumber, current.Number) != 1)
        {
            return EpisodeNeighborIds.None;
        }

        return new EpisodeNeighborIds(
            ResolvePrevious(localEpisodes, current),
            ResolveNext(localEpisodes, current));
    }

    private static Guid? ResolveNext(
        IReadOnlyCollection<EpisodeOrderKey> episodes,
        EpisodeOrderKey current)
    {
        if (Count(episodes, current.SeasonNumber, current.Number + 1) > 0)
        {
            return Unique(episodes, current.SeasonNumber, current.Number + 1);
        }

        if (current.SeasonNumber <= 0 ||
            episodes.Any(x =>
                x.SeasonNumber == current.SeasonNumber &&
                x.Number > current.Number))
        {
            return null;
        }

        return Unique(episodes, current.SeasonNumber + 1, 1);
    }

    private static Guid? ResolvePrevious(
        IReadOnlyCollection<EpisodeOrderKey> episodes,
        EpisodeOrderKey current)
    {
        if (Count(episodes, current.SeasonNumber, current.Number - 1) > 0)
        {
            return Unique(episodes, current.SeasonNumber, current.Number - 1);
        }

        if (current.SeasonNumber <= 1 ||
            current.Number != 1 ||
            episodes.Any(x =>
                x.SeasonNumber == current.SeasonNumber &&
                x.Number < current.Number))
        {
            return null;
        }

        var previousSeason = episodes
            .Where(x => x.SeasonNumber == current.SeasonNumber - 1)
            .ToArray();

        return previousSeason.Length == 0
            ? null
            : Unique(
                previousSeason,
                current.SeasonNumber - 1,
                previousSeason.Max(x => x.Number));
    }

    private static int Count(
        IEnumerable<EpisodeOrderKey> episodes,
        int seasonNumber,
        int number) =>
        episodes.Count(x => x.SeasonNumber == seasonNumber && x.Number == number);

    private static Guid? Unique(
        IEnumerable<EpisodeOrderKey> episodes,
        int seasonNumber,
        int number)
    {
        var matches = episodes
            .Where(x => x.SeasonNumber == seasonNumber && x.Number == number)
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0].Id : null;
    }
}
