namespace AniLingo.Web.Features.Acquisition.Indexers;

/// <summary>
/// Builds the search query strings for an anime/episode/season target. The
/// one canonical implementation; <see cref="Prowlarr.ProwlarrSearchPlanner"/>
/// delegates here.
/// </summary>
public static class IndexerSearchPlanner
{
    private const int MaximumQueries = 16;

    public static IReadOnlyList<IndexerSearchQuery> Build(
        IndexerAnimeSearchTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var titles = new[] { target.CanonicalTitle }
            .Concat(target.Aliases ?? [])
            .Select(Normalize)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        if (titles.Length == 0)
        {
            throw new ArgumentException(
                "At least one anime title is required.",
                nameof(target));
        }

        var queries = new List<string>();

        foreach (var title in titles)
        {
            switch (target.Mode)
            {
                case IndexerAnimeSearchMode.Episode:
                    AddEpisodeQueries(queries, title, target);
                    break;

                case IndexerAnimeSearchMode.Season:
                    AddSeasonQueries(queries, title, target.SeasonNumber);
                    break;

                default:
                    Add(queries, title);
                    break;
            }

            if (queries.Count >= MaximumQueries)
            {
                break;
            }
        }

        return queries
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumQueries)
            .Select(query => new IndexerSearchQuery(query))
            .ToArray();
    }

    private static void AddEpisodeQueries(
        ICollection<string> queries,
        string title,
        IndexerAnimeSearchTarget target)
    {
        if (target.SeasonNumber is >= 0 &&
            target.EpisodeNumber is > 0)
        {
            Add(
                queries,
                $"{title} S{target.SeasonNumber.Value:00}E{target.EpisodeNumber.Value:00}");
        }

        if (target.AbsoluteEpisodeNumber is > 0)
        {
            Add(queries, $"{title} - {target.AbsoluteEpisodeNumber.Value:00}");
            Add(queries, $"{title} {target.AbsoluteEpisodeNumber.Value:00}");
        }

        if (target.EpisodeNumber is > 0 &&
            target.AbsoluteEpisodeNumber is null)
        {
            Add(queries, $"{title} {target.EpisodeNumber.Value:00}");
        }

        if (target.SeasonNumber is null &&
            target.EpisodeNumber is null &&
            target.AbsoluteEpisodeNumber is null)
        {
            Add(queries, title);
        }
    }

    private static void AddSeasonQueries(
        ICollection<string> queries,
        string title,
        int? seasonNumber)
    {
        if (seasonNumber is >= 0)
        {
            Add(queries, $"{title} S{seasonNumber.Value:00}");
            Add(queries, $"{title} Season {seasonNumber.Value}");
        }
        else
        {
            Add(queries, title);
        }
    }

    private static void Add(ICollection<string> queries, string query)
    {
        var normalized = Normalize(query);
        if (normalized.Length > 0)
        {
            queries.Add(normalized);
        }
    }

    private static string Normalize(string value) =>
        string.Join(
            ' ',
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
