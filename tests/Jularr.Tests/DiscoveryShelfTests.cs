using System.Security.Claims;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Discovery;
using Jularr.Web.Features.MediaCore;
using Jularr.Web.Features.Shell;

namespace Jularr.Tests;

/// <summary>
/// Covers the provider-driven discovery shelf surface (#595): the row taxonomy, media-core identity
/// de-duplication across sources, capability-filtered rows and the board cache. All pure/faked, so no
/// network is touched.
/// </summary>
[TestClass]
public sealed class DiscoveryShelfTests
{
    [TestMethod]
    public void PlanBuildsTrendingThenTopRowsPerTypeThenBooksOnlyNew()
    {
        var plans = DiscoveryShelfComposer.Plan(
            [WorkMediaType.Anime, WorkMediaType.Manga, WorkMediaType.LightNovel, WorkMediaType.Book],
            includeAniList: true,
            includeBooks: true);

        CollectionAssert.AreEqual(
            new[]
            {
                "trending-anime", "trending-manga", "trending-lightnovel", "trending-book",
                "top-anime", "top-manga", "top-lightnovel", "top-book",
                "new-book"
            },
            plans.Select(plan => plan.Id).ToArray());

        // "New" (recently published) is a Books-only signal (#371); it never appears for other types.
        Assert.AreEqual(1, plans.Count(plan => plan.Kind == DiscoveryShelfKind.NewlyPublished));
        Assert.IsTrue(plans.Where(plan => plan.Mode == DiscoveryMode.New).All(plan => plan.Category == DiscoveryCategory.Book));
    }

    [TestMethod]
    public void PlanOmitsTypesWithoutAProviderFeed()
    {
        // Movie/Series exist as media types (#593/#594) but have no provider feed adapter yet: a clean
        // seam, not an empty or fabricated row.
        var plans = DiscoveryShelfComposer.Plan(
            [WorkMediaType.Movie, WorkMediaType.Series, WorkMediaType.Anime],
            includeAniList: true,
            includeBooks: true);

        CollectionAssert.AreEqual(
            new[] { "trending-anime", "top-anime" },
            plans.Select(plan => plan.Id).ToArray());
    }

    [TestMethod]
    public void PlanRespectsDisabledSources()
    {
        var booksOnly = DiscoveryShelfComposer.Plan(
            [WorkMediaType.Anime, WorkMediaType.Book],
            includeAniList: false,
            includeBooks: true);
        Assert.IsTrue(booksOnly.All(plan => plan.Category == DiscoveryCategory.Book));

        var aniListOnly = DiscoveryShelfComposer.Plan(
            [WorkMediaType.Anime, WorkMediaType.Book],
            includeAniList: true,
            includeBooks: false);
        Assert.IsFalse(aniListOnly.Any(plan => plan.Category == DiscoveryCategory.Book));
    }

    [TestMethod]
    public void DeduplicateMergesTheSameWorkAcrossSourcesByIdentity()
    {
        // Two sources return the same AniList anime with different provider casing/ids; the media-core
        // identity normalises them, so the row keeps one (first occurrence wins).
        var sourceA = new[] { Item("anime", "anilist", "111", "Frieren"), Item("anime", "anilist", "222", "Dandadan") };
        var sourceB = new[] { Item("anime", "AniList", "111", "Frieren (dup)"), Item("book", "openlibrary", "111", "A Book") };

        var deduped = DiscoveryShelfComposer.Deduplicate(sourceA.Concat(sourceB));

        Assert.AreEqual(3, deduped.Count);
        Assert.AreEqual("Frieren", deduped[0].Title, "First occurrence of the shared identity must win.");
        // Same external id but a different media type is a different work and is kept.
        Assert.IsTrue(deduped.Any(item => item.Category == "book" && item.ExternalId == "111"));
    }

    [TestMethod]
    public async Task BoardOnlyContainsRowsForVisibleMediaTypes()
    {
        DiscoveryShelfService.InvalidateCache();
        var feed = new FakeFeed();
        var service = new DiscoveryShelfService(feed, Shell(WorkMediaType.Book));

        var board = await service.GetBoardAsync(
            null, "cap-filter", isOwner: false, includeAniList: true, includeBooks: true, CancellationToken.None);

        Assert.IsTrue(board.Rows.Count > 0);
        Assert.IsTrue(
            board.Rows.All(row => row.MediaType == WorkMediaType.Book),
            "A Books-only profile must see book rows only.");
        Assert.IsTrue(board.Rows.All(row => row.Items.Count > 0), "Empty rows must be dropped.");
    }

    [TestMethod]
    public async Task BoardIsEmptyWhenNoVisibleTypeHasAFeed()
    {
        DiscoveryShelfService.InvalidateCache();
        var feed = new FakeFeed();
        var service = new DiscoveryShelfService(feed, Shell(WorkMediaType.Movie, WorkMediaType.Series));

        var board = await service.GetBoardAsync(
            null, "no-feed", isOwner: false, includeAniList: true, includeBooks: true, CancellationToken.None);

        Assert.IsTrue(board.IsEmpty);
        Assert.AreEqual(0, feed.Calls, "No feed call should be made when there is nothing to show.");
    }

    [TestMethod]
    public async Task BoardIsCachedPerProfileWithinTtl()
    {
        DiscoveryShelfService.InvalidateCache();
        var feed = new FakeFeed();
        var service = new DiscoveryShelfService(feed, Shell(WorkMediaType.Book));

        await service.GetBoardAsync(null, "cache-a", false, true, true, CancellationToken.None);
        var afterFirst = feed.Calls;
        Assert.IsTrue(afterFirst > 0);

        await service.GetBoardAsync(null, "cache-a", false, true, true, CancellationToken.None);
        Assert.AreEqual(afterFirst, feed.Calls, "A repeat board request within TTL must be served from cache.");

        await service.GetBoardAsync(null, "cache-b", false, true, true, CancellationToken.None);
        Assert.AreEqual(afterFirst * 2, feed.Calls, "A different profile must not hit another profile's cached board.");
    }

    [TestMethod]
    public void DeepLinkMatchesTheClientDiscoverUrlScheme()
    {
        Assert.AreEqual("/Discover", DiscoveryShelfLinks.ToDiscoverUrl(DiscoveryCategory.All, DiscoveryMode.Trending, ""));
        Assert.AreEqual("/Discover?category=anime", DiscoveryShelfLinks.ToDiscoverUrl(DiscoveryCategory.Anime, DiscoveryMode.Trending, ""));
        Assert.AreEqual("/Discover?category=book&mode=new", DiscoveryShelfLinks.ToDiscoverUrl(DiscoveryCategory.Book, DiscoveryMode.New, ""));
        Assert.AreEqual("/Discover?category=light-novel&mode=top", DiscoveryShelfLinks.ToDiscoverUrl(DiscoveryCategory.LightNovel, DiscoveryMode.Top, ""));
    }

    [TestMethod]
    public void DiscoverClientRoutesBetweenShelfLandingAndGrid()
    {
        var script = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Jularr.Web", "wwwroot", "js", "discover.js"));

        // Rows or the result grid are one server-rendered body fetched from the Body handler after
        // first paint; the address decides which of the two the server renders.
        StringAssert.Contains(script, "data-dc-body");
        StringAssert.Contains(script, "params.set(\"handler\", \"Body\")");
        Assert.IsFalse(script.Contains("handler=Results", StringComparison.Ordinal), "The JSON Results handler is gone.");
        // The debounce and stale-request guards must survive.
        StringAssert.Contains(script, "const SEARCH_DELAY = 250");
        StringAssert.Contains(script, "abortController?.abort()");
        StringAssert.Contains(script, "requestVersion");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Jularr.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Jularr repository root.");
    }

    private static IAppShellService Shell(params WorkMediaType[] visible)
    {
        var capabilities = WorkMediaTypes.All.ToDictionary(
            type => type,
            type => visible.Contains(type) ? MediaCapability.Browse : MediaCapability.Hidden);
        return new FakeShell(new ShellMediaAccess(new MediaCapabilityView(false, capabilities)));
    }

    private static DiscoveryItem Item(string category, string provider, string externalId, string title) =>
        new(
            $"{provider}:{category}:{externalId}",
            category,
            provider,
            externalId,
            title,
            null, null, null, null, null, null, null, null, null, null,
            [],
            false,
            null,
            "/details",
            false);

    private sealed class FakeShell(ShellMediaAccess access) : IAppShellService
    {
        public Task<ShellMediaAccess> GetMediaAccessAsync(ClaimsPrincipal? user, CancellationToken cancellationToken = default) =>
            Task.FromResult(access);
    }

    private sealed class FakeFeed : IDiscoveryFeed
    {
        public int Calls { get; private set; }

        public Task<DiscoveryResponse> GetAsync(
            DiscoveryRequest request,
            string profileId,
            bool isOwner,
            bool includeAniList,
            bool includeBooks,
            CancellationToken cancellationToken)
        {
            Calls++;
            var category = request.Category switch
            {
                DiscoveryCategory.Anime => "anime",
                DiscoveryCategory.Manga => "manga",
                DiscoveryCategory.LightNovel => "light-novel",
                DiscoveryCategory.Book => "book",
                _ => "all"
            };
            var provider = category == "book" ? "openlibrary" : "anilist";
            IReadOnlyList<DiscoveryItem> items =
            [
                Item(category, provider, $"{category}-1", $"{category} one"),
                Item(category, provider, $"{category}-2", $"{category} two")
            ];

            return Task.FromResult(new DiscoveryResponse(
                request.Query,
                category,
                request.Mode.ToString().ToLowerInvariant(),
                request.Genre,
                AniListConnected: false,
                items,
                []));
        }
    }
}
