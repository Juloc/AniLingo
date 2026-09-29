using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jularr.Web.Features.ReadingDiscovery;
using Jularr.Web.Features.ReadingSources;

namespace Jularr.Tests;

/// <summary>
/// Parsing and normalization of the BOOK☆WALKER, WebNovel and Internet Archive catalog
/// providers (#477), driven through their public search entry point with canned responses.
/// </summary>
[TestClass]
public sealed class ReadingSourceProviderTests
{
    private const string SeriesUuid = "9f9e37f1-573b-4ee2-bd24-23b9610e3d5e";

    // ---- BOOK☆WALKER -------------------------------------------------------------------

    [TestMethod]
    public async Task BookWalkerListsSeriesAndSingleBooksFromBothNovelCategories()
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.Query.Contains("qcat=3", StringComparison.Ordinal)
                ? Html(
                    BookWalkerTile(
                        "https://bookwalker.jp/series/22669/list/",
                        "災厄戦線のオーバーロード（富士見ファンタジア文庫）",
                        "https://rimg.bookwalker.jp/8644421/cover.jpg",
                        "<span class=\"a-tag-LN\">ラノベ</span><span class=\"a-tag-comp\">完結</span>",
                        "シリーズ3冊"),
                    BookWalkerTile(
                        "https://bookwalker.jp/de" + SeriesUuid + "/",
                        "ヴァンキッシュ・オーバーロード",
                        "https://rimg.bookwalker.jp/817448/cover.jpg",
                        "<span class=\"a-tag-LN\">ラノベ</span>",
                        seriesLabel: null))
                : Html(
                    BookWalkerTile(
                        "https://bookwalker.jp/series/138219/list/",
                        "オーバーロード",
                        "https://rimg.bookwalker.jp/2d4e3f/cover.jpg",
                        "<span class=\"a-tag-other\">新文芸</span>",
                        "シリーズ17冊")));
        using var http = new HttpClient(handler);

        var results = await new BookWalkerCatalogProvider(http)
            .SearchAsync("オーバーロード", 24, CancellationToken.None);

        // Both categories are asked, at the paths robots.txt allows, once each.
        Assert.AreEqual(2, handler.Requests.Count);
        CollectionAssert.AreEquivalent(
            new[] { "3", "9" },
            handler.Requests
                .Select(request => System.Web.HttpUtility.ParseQueryString(request.Query)["qcat"])
                .ToArray());
        Assert.IsTrue(handler.Requests.All(request =>
            request.Host == "bookwalker.jp" && request.AbsolutePath == "/search/"));
        Assert.IsTrue(handler.Requests.All(request =>
            System.Web.HttpUtility.ParseQueryString(request.Query)["word"] == "オーバーロード"));

        // Categories are interleaved in the store's relevance order.
        CollectionAssert.AreEqual(
            new[] { "series-22669", "series-138219", "book-" + SeriesUuid },
            results.Select(item => item.ExternalId).ToArray());

        var series = results[0];
        Assert.AreEqual("bookwalker", series.Provider);
        Assert.AreEqual("災厄戦線のオーバーロード（富士見ファンタジア文庫）", series.Title);
        Assert.AreEqual(3, series.VolumeCount);
        Assert.AreEqual("FINISHED", series.Status);
        Assert.AreEqual("https://rimg.bookwalker.jp/8644421/cover.jpg", series.CoverImageUrl);
        Assert.AreEqual("https://bookwalker.jp/series/22669/list/", series.SourceUrl);
        Assert.IsFalse(series.IsPublicWebSource);

        Assert.AreEqual(17, results[1].VolumeCount);
        Assert.IsNull(results[1].Status);

        var book = results[2];
        Assert.IsNull(book.VolumeCount);
        Assert.AreEqual("https://bookwalker.jp/de" + SeriesUuid + "/", book.SourceUrl);
    }

    [TestMethod]
    public async Task BookWalkerDropsResultsThatLeaveTheStore()
    {
        var handler = new StubHandler(_ => Html(
            BookWalkerTile(
                "https://evil.example/series/1/list/",
                "Off-site link",
                "https://rimg.bookwalker.jp/x.jpg",
                "",
                null),
            BookWalkerTile(
                "https://bookwalker.jp/search/?sample=1",
                "Not a series or book page",
                "https://rimg.bookwalker.jp/x.jpg",
                "",
                null),
            BookWalkerTile(
                "https://bookwalker.jp/series/5/list/",
                "Foreign cover",
                "https://evil.example/cover.jpg",
                "",
                null)));
        using var http = new HttpClient(handler);

        var results = await new BookWalkerCatalogProvider(http)
            .SearchAsync("test", 24, CancellationToken.None);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("series-5", results[0].ExternalId);
        Assert.IsNull(results[0].CoverImageUrl);
    }

    [TestMethod]
    public async Task BookWalkerNeverRequestsTrialOrSampleUrls()
    {
        var handler = new StubHandler(_ => Html(
            BookWalkerTile(
                "https://bookwalker.jp/series/1/list/",
                "Title",
                "https://rimg.bookwalker.jp/x.jpg",
                "",
                null)));
        using var http = new HttpClient(handler);

        await new BookWalkerCatalogProvider(http)
            .SearchAsync("title", 24, CancellationToken.None);

        Assert.IsTrue(handler.Requests.All(request =>
            !request.ToString().Contains("sample", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task BookWalkerAnswersAnEmptySearchWithNotFound()
    {
        using var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("<html>0 results</html>")
            }));

        var results = await new BookWalkerCatalogProvider(http)
            .SearchAsync("zzzz", 24, CancellationToken.None);

        Assert.AreEqual(0, results.Count);
    }

    // ---- WebNovel ----------------------------------------------------------------------

    [TestMethod]
    public async Task WebNovelListsBooksByNumericIdWithTitleAndCover()
    {
        const string html = """
        <ul class="search-results">
          <li>
            <a href="/book/lord-of-the-mysteries_11022733006234905" title="Lord of the Mysteries">
              <img src="data:image/gif;base64,R0lGOD" data-src="https://book-pic.webnovel.com/bookcover/11022733006234905?imageId=1" alt="Lord of the Mysteries">
            </a>
            <a href="/book/lord-of-the-mysteries_11022733006234905">Read now</a>
          </li>
          <li>
            <a href="https://www.webnovel.com/book/12345678901">
              <img src="https://book-pic.webnovel.com/bookcover/12345678901" alt="Overlord &amp; Friends">
            </a>
          </li>
          <li><a href="/book/only-a-button_98765432109">Read now</a></li>
          <li><a href="/book/too-short_123" title="Short id">x</a></li>
          <li><a href="https://evil.example/book/x_11022733006234999" title="Off-site">x</a></li>
        </ul>
        """;
        var handler = new StubHandler(_ => Html(html));
        using var http = new HttpClient(handler);

        var results = await new WebNovelCatalogProvider(http)
            .SearchAsync("lord of the mysteries", 24, CancellationToken.None);

        Assert.AreEqual(1, handler.Requests.Count);
        Assert.AreEqual("www.webnovel.com", handler.Requests[0].Host);
        Assert.AreEqual("/search", handler.Requests[0].AbsolutePath);
        Assert.AreEqual(
            "lord of the mysteries",
            System.Web.HttpUtility.ParseQueryString(handler.Requests[0].Query)["keywords"]);

        CollectionAssert.AreEqual(
            new[] { "11022733006234905", "12345678901" },
            results.Select(item => item.ExternalId).ToArray());

        var first = results[0];
        Assert.AreEqual("webnovel", first.Provider);
        Assert.AreEqual("Lord of the Mysteries", first.Title);
        Assert.AreEqual(
            "https://book-pic.webnovel.com/bookcover/11022733006234905?imageId=1",
            first.CoverImageUrl);
        Assert.AreEqual(
            "https://www.webnovel.com/book/11022733006234905",
            first.SourceUrl);
        Assert.IsFalse(first.IsPublicWebSource);

        Assert.AreEqual("Overlord & Friends", results[1].Title);
    }

    [TestMethod]
    public async Task WebNovelBotChallengeIsReportedAsBlockedNotBypassed()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("<title>Just a moment...</title>")
            });
        using var http = new HttpClient(handler);

        var exception = await Assert.ThrowsExactlyAsync<ReadingSourceUnavailableException>(() =>
            new WebNovelCatalogProvider(http)
                .SearchAsync("overlord", 24, CancellationToken.None));

        Assert.AreEqual(ReadingSourceFailureKind.Blocked, exception.Kind);
        // One request, no retry with other headers.
        Assert.AreEqual(1, handler.Requests.Count);
        Assert.IsTrue(handler.UserAgents.All(agent =>
            agent.Contains("Jularr", StringComparison.Ordinal)));
    }

    // ---- Internet Archive --------------------------------------------------------------

    [TestMethod]
    public async Task InternetArchiveOnlyListsOpenOrLendableItemsAndLabelsTheirRights()
    {
        var json = ArchiveResponse(
            """
            {
              "identifier": "cc-licensed-novel",
              "title": "A Creative Commons Novel",
              "creator": ["Some Author", "Another"],
              "year": 2015,
              "licenseurl": "https://creativecommons.org/licenses/by/4.0/",
              "collection": ["opensource", "community"]
            }
            """,
            """
            {
              "identifier": "public-domain-classic",
              "title": "A Public Domain Classic",
              "date": "1911-01-01T00:00:00Z",
              "rights": "This work is in the Public Domain.",
              "collection": ["americana"]
            }
            """,
            """
            {
              "identifier": "community-upload",
              "title": "Community Upload Without Rights",
              "collection": ["opensource", "community"]
            }
            """,
            """
            {
              "identifier": "lendable-book_x1y2",
              "title": ["Lendable Book"],
              "creator": "Kawahara, Reki, author",
              "year": "2014",
              "access-restricted-item": "true",
              "collection": ["internetarchivebooks", "inlibrary", "printdisabled"]
            }
            """,
            """
            {
              "identifier": "restricted-not-lendable",
              "title": "Restricted Without Lending",
              "access-restricted-item": "true",
              "collection": ["internetarchivebooks", "printdisabled"]
            }
            """,
            """
            {
              "identifier": "hidden-preview",
              "title": "No Preview Item",
              "collection": ["no-preview", "opensource"]
            }
            """,
            """
            {
              "identifier": "../escape",
              "title": "Unsafe Identifier",
              "collection": ["opensource"]
            }
            """);
        var handler = new StubHandler(_ => Json(json));
        using var http = new HttpClient(handler);

        var results = await new InternetArchiveCatalogProvider(http)
            .SearchAsync("novel", 24, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                "cc-licensed-novel",
                "public-domain-classic",
                "community-upload",
                "lendable-book_x1y2"
            },
            results.Select(item => item.ExternalId).ToArray());

        Assert.AreEqual(ReadingAccess.OpenLicense, results[0].Access);
        Assert.AreEqual(ReadingAccess.OpenLicense, results[1].Access);
        Assert.AreEqual(ReadingAccess.OpenUnverified, results[2].Access);
        Assert.AreEqual(ReadingAccess.Lendable, results[3].Access);

        var first = results[0];
        Assert.AreEqual("internetarchive", first.Provider);
        Assert.AreEqual("Some Author", first.Author);
        Assert.AreEqual(2015, first.FirstPublishYear);
        Assert.AreEqual("https://archive.org/details/cc-licensed-novel", first.SourceUrl);
        Assert.AreEqual(
            "https://archive.org/services/img/cc-licensed-novel",
            first.CoverImageUrl);
        Assert.AreEqual(1911, results[1].FirstPublishYear);
        Assert.AreEqual("Lendable Book", results[3].Title);
        Assert.AreEqual(2014, results[3].FirstPublishYear);
        Assert.IsTrue(results.All(item => !item.IsPublicWebSource));
        Assert.IsTrue(results.All(item => item.AccessKey is not null));
    }

    [TestMethod]
    public async Task InternetArchiveQuotesEveryTermSoInputCannotInjectSearchSyntax()
    {
        var handler = new StubHandler(_ => Json(ArchiveResponse()));
        using var http = new HttpClient(handler);

        await new InternetArchiveCatalogProvider(http).SearchAsync(
            "foo\" OR (collection:secret) -mediatype:movies",
            24,
            CancellationToken.None);

        var uri = handler.Requests.Single();
        Assert.AreEqual("archive.org", uri.Host);
        Assert.AreEqual("/advancedsearch.php", uri.AbsolutePath);

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        Assert.AreEqual(
            "title:(\"foo\" AND \"OR\" AND \"collection\" AND \"secret\" AND \"mediatype\" AND \"movies\") AND mediatype:texts",
            query["q"]);
        Assert.AreEqual("json", query["output"]);
        CollectionAssert.Contains(query.GetValues("fl[]")!, "access-restricted-item");
        CollectionAssert.Contains(query.GetValues("fl[]")!, "identifier");
    }

    [TestMethod]
    public async Task InternetArchiveIgnoresQueriesWithoutSearchableWords()
    {
        var handler = new StubHandler(_ => Json(ArchiveResponse()));
        using var http = new HttpClient(handler);

        var results = await new InternetArchiveCatalogProvider(http)
            .SearchAsync("\"()\" : ", 24, CancellationToken.None);

        Assert.AreEqual(0, results.Count);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task InternetArchiveRateLimitCarriesRetryAfter()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(
                TimeSpan.FromMinutes(7));
            return response;
        });
        using var http = new HttpClient(handler);

        var exception = await Assert.ThrowsExactlyAsync<ReadingSourceUnavailableException>(() =>
            new InternetArchiveCatalogProvider(http)
                .SearchAsync("novel", 24, CancellationToken.None));

        Assert.AreEqual(ReadingSourceFailureKind.RateLimited, exception.Kind);
        Assert.AreEqual(TimeSpan.FromMinutes(7), exception.RetryAfter);
    }

    [TestMethod]
    public async Task InternetArchiveMalformedResponseIsAFailureNotEmptyResults()
    {
        using var http = new HttpClient(new StubHandler(_ => Json("{\"unexpected\":true}")));

        await Assert.ThrowsExactlyAsync<JsonException>(() =>
            new InternetArchiveCatalogProvider(http)
                .SearchAsync("novel", 24, CancellationToken.None));
    }

    [TestMethod]
    public async Task ServerErrorsAreUnavailableNotBlocked()
    {
        using var http = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var exception = await Assert.ThrowsExactlyAsync<ReadingSourceUnavailableException>(() =>
            new InternetArchiveCatalogProvider(http)
                .SearchAsync("novel", 24, CancellationToken.None));

        Assert.AreEqual(ReadingSourceFailureKind.Unavailable, exception.Kind);
    }

    // ---- fixtures ----------------------------------------------------------------------

    private static HttpResponseMessage Html(params string[] tiles) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "<html><body><ul class=\"m-tile-list\">" + string.Concat(tiles) + "</ul></body></html>",
                Encoding.UTF8,
                "text/html")
        };

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static string ArchiveResponse(params string[] docs) =>
        "{\"responseHeader\":{\"status\":0},\"response\":{\"numFound\":" + docs.Length +
        ",\"start\":0,\"docs\":[" + string.Join(",", docs) + "]}}";

    /// <summary>One search result tile, in the markup bookwalker.jp serves.</summary>
    private static string BookWalkerTile(
        string href,
        string title,
        string cover,
        string tags,
        string? seriesLabel) =>
        $$"""
        <li class="m-tile">
        <div class="m-book-item ">
        <div class="m-book-item__thumb-block"><div class="m-thumb">
          <a href="{{href}}" class="m-thumb__image" data-series-id="1">
            <img class="lazy" src="https://c.bookwalker.jp/louis/pc/img/common/bg-now-loading-pc.png"
                 data-original="{{cover}}" alt="{{title}}" title="{{title}}" />
          </a>
        </div></div>
        <div class="m-book-item__info-block">
          <div class="m-book-item__secondary">
            <div class="m-book-item__tag-box">{{tags}}</div>
            <p class="m-book-item__title">
              <a href="{{href}}"
                 class="m-book-item__title"
                 data-ga-category="list"
                 data-action-label="タイトル"
                 title="{{title}}"
              >
                {{title}}
              </a>
            </p>
          </div>
          <div class="m-book-item__primary">
            <div class="m-book-item__series">{{(seriesLabel is null ? "" : "<span class=\"ico-txt\">" + seriesLabel + "</span>")}}</div>
          </div>
        </div>
        </div>
        </li>
        """;

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public List<string> UserAgents { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            UserAgents.Add(request.Headers.UserAgent.ToString());
            return Task.FromResult(respond(request));
        }
    }
}
