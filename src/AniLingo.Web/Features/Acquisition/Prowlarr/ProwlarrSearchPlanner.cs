namespace AniLingo.Web.Features.Acquisition.Prowlarr;

public static class ProwlarrSearchPlanner
{
    private const int MaximumQueries = 16;

    public static IReadOnlyList<ProwlarrSearchQuery> Build(
        ProwlarrAnimeSearchTarget target)
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
                case ProwlarrAnimeSearchMode.Episode:
                    AddEpisodeQueries(queries, title, target);
                    break;

                case ProwlarrAnimeSearchMode.Season:
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
            .Select(query => new ProwlarrSearchQuery(query))
            .ToArray();
    }

    private static void AddEpisodeQueries(
        ICollection<string> queries,
        string title,
        ProwlarrAnimeSearchTarget target)
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
