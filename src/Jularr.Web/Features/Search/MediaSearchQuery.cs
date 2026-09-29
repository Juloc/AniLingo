using System.Data;
using System.Data.Common;
using System.Globalization;
using Jularr.Web.Data;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.MediaCore;
using Jularr.Web.Features.Watchlist;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Search;

/// <summary>A franchise that matched, before it is turned into a result row.</summary>
internal sealed record MediaSearchFranchiseHit(Guid Id, string Title, double Score, int MemberCount);

/// <summary>
/// The PostgreSQL side of the search (#570, #434): ranked title matching over every searchable source
/// table and over franchises. It only decides <i>which titles match and how well</i>; grouping them by
/// canonical work, the Media Facts filters and paging happen on the bounded candidate list it returns.
/// <para>
/// Ranking is the same for every source: an exact title (1000), a title prefix (500), a full-text match
/// (100 + <c>ts_rank</c>) and a trigram-similar title (up to ~90), so strong matches always beat weak
/// fuzzy ones. Anime metadata, novels/books and manga are served by their GIN indexes
/// (<c>SearchVector</c>/<c>SearchText</c>); movies, series, audiobooks, anime without metadata and
/// franchises are small tables that are matched on the fly with the same expressions until they get
/// their own generated columns.
/// </para>
/// </summary>
internal sealed class MediaSearchQuery(AppDbContext db)
{
    // Below this trigram similarity a fuzzy-only candidate is dropped as noise.
    private const double FuzzyThreshold = 0.2;

    // A franchise reached only through the title of one of its works ranks just under a work of
    // that title, so the work itself is listed first.
    private const double MemberTitleWeight = 0.95;

    private static readonly string BookProvider = BookCatalogService.ImportedBookProvider;

    /// <summary>The ranked variants (best first) whose title matches <paramref name="query"/>, at most <paramref name="cap"/>.</summary>
    public async Task<IReadOnlyList<MediaSearchVariant>> FindWorksAsync(
        string query,
        IReadOnlyCollection<MediaSearchType> types,
        int cap,
        CancellationToken cancellationToken)
    {
        var branches = Sources(types).Select(Branch).ToArray();
        if (branches.Length == 0)
        {
            return [];
        }

        var sql =
            $"""
            WITH q AS ({QueryCte})
            SELECT "type", "id", "title", "score"
            FROM (
                {string.Join("\n            UNION ALL\n", branches)}
            ) hits
            WHERE "score" > 0
            ORDER BY "score" DESC, lower("title"), "id"
            LIMIT @cap;
            """;

        return await ExecuteAsync(
            sql,
            command =>
            {
                Add(command, "@raw", query);
                Add(command, "@threshold", FuzzyThreshold);
                Add(command, "@cap", cap);
            },
            reader => new MediaSearchVariant(
                MediaSearchTypes.Parse(reader.GetString(0)) ?? MediaSearchType.Novel,
                Guid.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Convert.ToDouble(reader.GetValue(3), CultureInfo.InvariantCulture)),
            cancellationToken);
    }

    /// <summary>
    /// Franchises that match by their own title or by the title of a member work, best first. Only
    /// members of <paramref name="memberTypes"/> count, so a franchise made of hidden media types
    /// does not exist for the profile.
    /// </summary>
    public async Task<IReadOnlyList<MediaSearchFranchiseHit>> FindFranchisesAsync(
        string query,
        IReadOnlyCollection<WorkMediaType> memberTypes,
        int cap,
        CancellationToken cancellationToken)
    {
        var storage = memberTypes
            .Select(type => WatchlistMediaTypeNames.ToStorage(WorkMediaTypes.ToWatchlist(type)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (storage.Length == 0)
        {
            return [];
        }

        const string franchiseText = "lower(coalesce(t.title, ''))";
        const string franchiseVector = "to_tsvector('simple', coalesce(t.title, ''))";
        var sql =
            $"""
            WITH q AS ({QueryCte}),
            titles AS (
                SELECT f."Id" AS fid, f."Title" AS title, 1.0 AS weight
                FROM "Franchises" f
                WHERE f."Title" <> ''
                UNION ALL
                SELECT m."FranchiseId", m."Title", {MemberTitleWeight.ToString(CultureInfo.InvariantCulture)}
                FROM "FranchiseMembers" m
                WHERE m."MediaType" = ANY(@memberTypes)
                UNION ALL
                SELECT m."FranchiseId", m."NativeTitle", {MemberTitleWeight.ToString(CultureInfo.InvariantCulture)}
                FROM "FranchiseMembers" m
                WHERE m."NativeTitle" IS NOT NULL AND m."MediaType" = ANY(@memberTypes)
            ),
            scored AS (
                SELECT t.fid, t.weight * {Score("t.title", franchiseText, franchiseVector)} AS score
                FROM titles t, q
                WHERE {Match(franchiseText, franchiseVector)}
            )
            SELECT f."Id", f."Title", s.score, c.members
            FROM (SELECT fid, MAX(score) AS score FROM scored WHERE score > 0 GROUP BY fid) s
            JOIN "Franchises" f ON f."Id" = s.fid AND f."Title" <> ''
            JOIN LATERAL (
                SELECT COUNT(*) AS members
                FROM "FranchiseMembers" m
                WHERE m."FranchiseId" = f."Id" AND m."MediaType" = ANY(@memberTypes)
            ) c ON c.members > 0
            ORDER BY s.score DESC, lower(f."Title"), f."Id"
            LIMIT @cap;
            """;

        return await ExecuteAsync(
            sql,
            command =>
            {
                Add(command, "@raw", query);
                Add(command, "@threshold", FuzzyThreshold);
                Add(command, "@cap", cap);
                Add(command, "@memberTypes", storage);
            },
            reader => new MediaSearchFranchiseHit(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture),
                Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture)),
            cancellationToken);
    }

    // The normalised query: lower-cased text, LIKE-escaped prefix/contains patterns (a typed % or _
    // is a character, not a wildcard) and the full-text query.
    private const string QueryCte =
        """
        SELECT lower(@raw) AS norm,
               replace(replace(replace(lower(@raw), '\', '\\'), '%', '\%'), '_', '\_') || '%' AS prefix,
               '%' || replace(replace(replace(lower(@raw), '\', '\\'), '%', '\%'), '_', '\_') || '%' AS contains,
               websearch_to_tsquery('simple', @raw) AS tsq
        """;

    /// <summary>One searchable table. <c>SearchText</c>/<c>SearchVector</c> are SQL expressions over alias <c>t</c>.</summary>
    private sealed record Source(
        string Table,
        string IdColumn,
        string TitleColumn,
        string TypeExpression,
        string SearchText,
        string SearchVector,
        string? Filter = null);

    private static IEnumerable<Source> Sources(IReadOnlyCollection<MediaSearchType> types)
    {
        static string OnTheFlyText(string columns) => $"lower({columns})";
        static string OnTheFlyVector(string columns) => $"to_tsvector('simple', {columns})";

        if (types.Contains(MediaSearchType.Anime))
        {
            yield return new Source(
                "AnimeMetadata", "AnimeId", "PreferredTitle", "'anime'",
                "t.\"SearchText\"", "t.\"SearchVector\"");

            // An anime the metadata match has not reached yet is still a title in the library.
            const string title = "coalesce(t.\"Title\", '')";
            yield return new Source(
                "Anime", "Id", "Title", "'anime'",
                OnTheFlyText(title), OnTheFlyVector(title),
                "NOT EXISTS (SELECT 1 FROM \"AnimeMetadata\" m WHERE m.\"AnimeId\" = t.\"Id\")");
        }

        var novels = types.Contains(MediaSearchType.Novel);
        var books = types.Contains(MediaSearchType.Book);
        if (novels || books)
        {
            yield return new Source(
                "NovelWorks", "Id", "Title",
                $"CASE WHEN t.\"SourceProvider\" = '{BookProvider}' THEN 'book' ELSE 'novel' END",
                "t.\"SearchText\"", "t.\"SearchVector\"",
                novels && books ? null : $"(t.\"SourceProvider\" = '{BookProvider}') = {(books ? "true" : "false")}");
        }

        if (types.Contains(MediaSearchType.Manga))
        {
            yield return new Source(
                "MangaSeries", "Id", "Title", "'manga'",
                "t.\"SearchText\"", "t.\"SearchVector\"");
        }

        if (types.Contains(MediaSearchType.Movie))
        {
            const string title = "coalesce(t.\"Title\", '')";
            yield return new Source(
                "Movies", "Id", "Title", "'movie'", OnTheFlyText(title), OnTheFlyVector(title));
        }

        if (types.Contains(MediaSearchType.Series))
        {
            const string title = "coalesce(t.\"Title\", '')";
            yield return new Source(
                "TvSeries", "Id", "Title", "'series'", OnTheFlyText(title), OnTheFlyVector(title));
        }

        if (types.Contains(MediaSearchType.Audiobook))
        {
            const string text = "coalesce(t.\"Title\", '') || ' ' || coalesce(t.\"Author\", '') || ' ' || coalesce(t.\"Narrator\", '')";
            yield return new Source(
                "Audiobooks", "Id", "Title", "'audiobook'", OnTheFlyText(text), OnTheFlyVector(text));
        }
    }

    // One SELECT over a searchable table; see the class summary for the score.
    private static string Branch(Source source)
    {
        var title = $"t.\"{source.TitleColumn}\"";
        return
            $"""
            SELECT {source.TypeExpression} AS "type",
                       t."{source.IdColumn}"::text AS "id",
                       {title} AS "title",
                       {Score(title, source.SearchText, source.SearchVector)} AS "score"
                FROM "{source.Table}" t, q
                WHERE ({Match(source.SearchText, source.SearchVector)})
                  {(source.Filter is null ? string.Empty : "AND " + source.Filter)}
            """;
    }

    private static string Score(string title, string searchText, string searchVector) =>
        $"""
        GREATEST(
                           CASE WHEN lower(coalesce({title}, '')) = q.norm THEN 1000 ELSE 0 END,
                           CASE WHEN {searchText} LIKE q.prefix THEN 500 ELSE 0 END,
                           CASE WHEN {searchVector} @@ q.tsq THEN 100 + ts_rank({searchVector}, q.tsq) * 10 ELSE 0 END,
                           CASE WHEN similarity({searchText}, q.norm) >= @threshold THEN similarity({searchText}, q.norm) * 90 ELSE 0 END)
        """;

    private static string Match(string searchText, string searchVector) =>
        $"""
        {searchVector} @@ q.tsq
                   OR {searchText} LIKE q.prefix
                   OR {searchText} % q.norm
                   OR {searchText} LIKE q.contains
        """;

    private async Task<IReadOnlyList<T>> ExecuteAsync<T>(
        string sql,
        Action<DbCommand> bind,
        Func<DbDataReader, T> read,
        CancellationToken cancellationToken)
    {
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
            bind(command);

            var rows = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(read(reader));
            }

            return rows;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
