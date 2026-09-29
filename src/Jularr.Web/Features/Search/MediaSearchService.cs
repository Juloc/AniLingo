using System.Data;
using System.Data.Common;
using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Search;

/// <summary>Which library a <see cref="MediaSearchHit"/> came from.</summary>
public enum MediaSearchType
{
    Anime,
    Novel,
    Manga
}

/// <summary>One ranked search result. Books are stored as Novel-table rows and surface as Novel.</summary>
public sealed record MediaSearchHit(MediaSearchType Type, Guid Id, string Title, double Score);

/// <summary>
/// The one shared PostgreSQL search backend for every media type (issue #570). It runs a single
/// indexed query across the title metadata of Anime, Novels/Books and Manga using PostgreSQL
/// full-text search (<c>tsvector</c>/<c>websearch_to_tsquery</c>) together with <c>pg_trgm</c>
/// similarity, so results tolerate typos, casing, punctuation and prefixes and match across
/// localized/native/romaji/English titles. Ranking puts exact and prefix title matches ahead of
/// full-text matches, and full-text ahead of weak fuzzy matches. Every branch is served by a GIN
/// index (see the SearchVector/SearchText generated columns), and results are always bounded.
/// </summary>
public sealed class MediaSearchService(AppDbContext db)
{
    public const int MaxLimit = 100;

    // Below this trigram similarity a fuzzy-only candidate is dropped as noise.
    private const double FuzzyThreshold = 0.2;

    public async Task<IReadOnlyList<MediaSearchHit>> SearchAsync(
        string? query,
        IReadOnlyCollection<MediaSearchType>? types = null,
        int limit = 40,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var normalized = (query ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return [];
        }

        var wanted = types is { Count: > 0 }
            ? new HashSet<MediaSearchType>(types)
            : [MediaSearchType.Anime, MediaSearchType.Novel, MediaSearchType.Manga];

        var boundedLimit = Math.Clamp(limit, 1, MaxLimit);
        var boundedOffset = Math.Max(0, offset);

        var branches = new List<string>();
        if (wanted.Contains(MediaSearchType.Anime))
        {
            branches.Add(Branch("anime", "AnimeMetadata", "AnimeId", "PreferredTitle"));
        }

        if (wanted.Contains(MediaSearchType.Novel))
        {
            branches.Add(Branch("novel", "NovelWorks", "Id", "Title"));
        }

        if (wanted.Contains(MediaSearchType.Manga))
        {
            branches.Add(Branch("manga", "MangaSeries", "Id", "Title"));
        }

        var union = string.Join("\n            UNION ALL\n", branches);
        var sql =
            $"""
            WITH q AS (
                SELECT lower(@raw) AS norm,
                       lower(@raw) || '%' AS prefix,
                       websearch_to_tsquery('simple', @raw) AS tsq
            )
            SELECT "type", "id", "title", "score"
            FROM (
                {union}
            ) hits
            WHERE "score" > 0
            ORDER BY "score" DESC, lower("title"), "id"
            LIMIT @limit OFFSET @offset;
            """;

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            Add(command, "@raw", normalized);
            Add(command, "@threshold", FuzzyThreshold);
            Add(command, "@limit", boundedLimit);
            Add(command, "@offset", boundedOffset);

            var hits = new List<MediaSearchHit>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                hits.Add(new MediaSearchHit(
                    ParseType(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Convert.ToDouble(reader.GetValue(3), System.Globalization.CultureInfo.InvariantCulture)));
            }

            return hits;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    // One SELECT over a searchable table. The score ranks exact-title (1000), prefix (500),
    // full-text (100 + ts_rank) and trigram similarity (up to ~90) so strong matches always win.
    private static string Branch(string type, string table, string idColumn, string titleColumn) =>
        $"""
        SELECT '{type}' AS "type",
                   t."{idColumn}"::text AS "id",
                   t."{titleColumn}" AS "title",
                   GREATEST(
                       CASE WHEN lower(coalesce(t."{titleColumn}", '')) = q.norm THEN 1000 ELSE 0 END,
                       CASE WHEN t."SearchText" LIKE q.prefix THEN 500 ELSE 0 END,
                       CASE WHEN t."SearchVector" @@ q.tsq THEN 100 + ts_rank(t."SearchVector", q.tsq) * 10 ELSE 0 END,
                       CASE WHEN similarity(t."SearchText", q.norm) >= @threshold THEN similarity(t."SearchText", q.norm) * 90 ELSE 0 END
                   ) AS "score"
            FROM "{table}" t, q
            WHERE t."SearchVector" @@ q.tsq
               OR t."SearchText" LIKE q.prefix
               OR t."SearchText" % q.norm
               OR t."SearchText" LIKE '%' || q.norm || '%'
        """;

    private static MediaSearchType ParseType(string value) =>
        value switch
        {
            "anime" => MediaSearchType.Anime,
            "manga" => MediaSearchType.Manga,
            _ => MediaSearchType.Novel
        };

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
