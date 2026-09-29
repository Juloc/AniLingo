using Jularr.Web.Features.Books;
using Jularr.Web.Features.Franchises;
using Jularr.Web.Features.MediaCore;
using Jularr.Web.Features.Recommendations;
using Jularr.Web.Features.Watchlist;

namespace Jularr.Tests;

/// <summary>
/// Unit tests for the media-neutral recommendation engine (#428): cross-media content matching,
/// relation-based continuation, explainability, capability filtering, and Books-parity of the shared
/// scoring framework the Books engine now delegates to.
/// </summary>
[TestClass]
public sealed class MediaRecommendationTests
{
    private static readonly DateTime BaseTime = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string Adaptation = FranchiseLabels.RelationGroupOrder[0];
    private static readonly string SequelPrequel = FranchiseLabels.RelationGroupOrder[1];

    [TestMethod]
    public void ABookSeedRecommendsALightNovelSharingSubjects()
    {
        var seeds = new[]
        {
            Seed(WorkMediaType.Book, "Space Saga", "Ann Author", ["Space Opera", "Adventure"], finished: true)
        };
        var candidates = new[]
        {
            Candidate("ln1", WorkMediaType.LightNovel, "Star Voyage", "Other Writer", ["Space Opera", "Politics"]),
            Candidate("bk2", WorkMediaType.Book, "Cooking 101", null, ["Cooking"])
        };

        var result = MediaRecommendationEngine.Build(
            seeds,
            candidates,
            [],
            Visible(WorkMediaType.Book, WorkMediaType.LightNovel),
            [],
            MediaRecommendationOptions.Default);

        var shelf = result.Shelves.Single(x => x.Kind == MediaRecommendationShelfKind.BecauseYou);
        Assert.AreEqual("Space Saga", shelf.SeedTitle);
        CollectionAssert.AreEqual(
            new[] { "Star Voyage" },
            shelf.Items.Select(x => x.Candidate.Title).ToArray());
        Assert.AreEqual(WorkMediaType.LightNovel, shelf.Items[0].Candidate.MediaType);
    }

    [TestMethod]
    public void ContinuationSurfacesTheCrossMediaSourceOrderedByRelationStrength()
    {
        var animeIdentity = new WatchlistIdentity(WatchlistMediaType.Anime, "anilist", "1");
        var seed = Seed(WorkMediaType.Anime, "Titan", null, [], finished: true, identity: animeIdentity);

        var mangaTarget = Candidate(
            "manga:anilist:2",
            WorkMediaType.Manga,
            "Titan Manga",
            null,
            [],
            new WatchlistIdentity(WatchlistMediaType.Manga, "anilist", "2"));
        var sequelTarget = Candidate(
            "anime:anilist:3",
            WorkMediaType.Anime,
            "Titan Season 2",
            null,
            [],
            new WatchlistIdentity(WatchlistMediaType.Anime, "anilist", "3"));

        var continuations = new[]
        {
            new MediaContinuationLink(seed, sequelTarget, SequelPrequel),
            new MediaContinuationLink(seed, mangaTarget, Adaptation)
        };

        var result = MediaRecommendationEngine.Build(
            [],
            [],
            continuations,
            Visible(WorkMediaType.Anime, WorkMediaType.Manga),
            [],
            MediaRecommendationOptions.Default);

        var shelf = result.Shelves.Single(x => x.Kind == MediaRecommendationShelfKind.Continuation);
        Assert.AreEqual("Titan", shelf.SeedTitle);
        CollectionAssert.AreEqual(
            new[] { "Titan Manga", "Titan Season 2" },
            shelf.Items.Select(x => x.Candidate.Title).ToArray());
        Assert.AreEqual(WorkMediaType.Manga, shelf.Items[0].Candidate.MediaType);
        Assert.AreEqual(MediaRecommendationReasonKind.Continuation, shelf.Items[0].Reason.Kind);
        Assert.AreEqual(Adaptation, shelf.Items[0].Reason.RelationGroupKey);
        Assert.AreEqual("Titan", shelf.Items[0].Reason.SeedTitle);
    }

    [TestMethod]
    public void ContentReasonNamesTheSharedCreatorAndSubjects()
    {
        var seeds = new[]
        {
            Seed(WorkMediaType.Book, "First", "Same Writer", ["Mystery", "Crime"], finished: true)
        };
        var candidates = new[]
        {
            Candidate("c1", WorkMediaType.Book, "Second", "Same Writer", ["Mystery"])
        };

        var result = MediaRecommendationEngine.Build(
            seeds,
            candidates,
            [],
            Visible(WorkMediaType.Book),
            [],
            MediaRecommendationOptions.Default);

        var reason = result.Shelves.Single().Items.Single().Reason;
        Assert.AreEqual(MediaRecommendationReasonKind.SameCreatorAndSubjects, reason.Kind);
        Assert.AreEqual("Same Writer", reason.Creator);
        Assert.AreEqual("First", reason.SeedTitle);
        CollectionAssert.Contains(reason.SharedSubjects.ToArray(), "Mystery");
    }

    [TestMethod]
    public void HiddenMediaTypesAreFilteredFromEverySurface()
    {
        var seeds = new[]
        {
            Seed(WorkMediaType.Book, "Space Saga", "Ann Author", ["Space Opera"], finished: true)
        };
        var candidates = new[]
        {
            Candidate("ln1", WorkMediaType.LightNovel, "Star Voyage", null, ["Space Opera"])
        };
        var animeIdentity = new WatchlistIdentity(WatchlistMediaType.Anime, "anilist", "1");
        var continuations = new[]
        {
            new MediaContinuationLink(
                Seed(WorkMediaType.Anime, "Titan", null, [], identity: animeIdentity),
                Candidate("manga:anilist:2", WorkMediaType.Manga, "Titan Manga", null, [],
                    new WatchlistIdentity(WatchlistMediaType.Manga, "anilist", "2")),
                Adaptation)
        };

        // Only Books are visible: the light-novel content match and the manga continuation must vanish.
        var result = MediaRecommendationEngine.Build(
            seeds,
            candidates,
            continuations,
            Visible(WorkMediaType.Book),
            [],
            MediaRecommendationOptions.Default);

        var surfaced = result.Shelves.SelectMany(x => x.Items).Select(x => x.Candidate.MediaType).ToArray();
        CollectionAssert.DoesNotContain(surfaced, WorkMediaType.LightNovel);
        CollectionAssert.DoesNotContain(surfaced, WorkMediaType.Manga);
        Assert.IsFalse(result.Shelves.Any(x => x.Kind == MediaRecommendationShelfKind.Continuation));
    }

    [TestMethod]
    public void OwnedAndAlreadyShownCandidatesAreSuppressed()
    {
        var seeds = new[]
        {
            Seed(WorkMediaType.Book, "Space Saga", "Ann Author", ["Space Opera"], finished: true)
        };
        var candidates = new[]
        {
            Candidate("ln1", WorkMediaType.LightNovel, "Star Voyage", null, ["Space Opera"])
        };
        var owned = new[]
        {
            new RecommendationOwnedEntity(RecommendationScoring.Identity("Star Voyage", null), null)
        };

        var result = MediaRecommendationEngine.Build(
            seeds,
            candidates,
            [],
            Visible(WorkMediaType.Book, WorkMediaType.LightNovel),
            owned,
            MediaRecommendationOptions.Default);

        Assert.IsTrue(result.IsEmpty, "An owned candidate must not be recommended.");
    }

    [TestMethod]
    public async Task BooksEngineKeepsItsExplainableReasonCopyAfterTheMove()
    {
        var library = new[]
        {
            new BookRecommendationLibraryBook(
                Guid.NewGuid(),
                "Dune",
                "Frank Herbert",
                null,
                ["Science Fiction", "Desert"],
                null,
                BaseTime,
                new BookRecommendationProgress(1, 10, 300, BaseTime))
        };

        var catalog = new BookCatalogItem[]
        {
            new(
                "c1",
                "Dune Messiah",
                "Frank Herbert",
                null,
                null,
                ["Science Fiction"],
                null,
                null,
                null,
                "https://example.test/c1",
                "Test Catalog",
                null)
        };

        var result = await BookRecommendationEngine.BuildAsync(
            library,
            (_, _) => Task.FromResult<IReadOnlyList<BookCatalogItem>>(catalog),
            new BookRecommendationOptions { MinimumRecommendationsBeforeFallback = 0 },
            CancellationToken.None);

        var item = result.Shelves
            .SelectMany(shelf => shelf.Items)
            .First(x => x.Book.Id == "c1");
        StringAssert.Contains(item.Reason, "Same author");
        StringAssert.Contains(item.Reason, "Science Fiction");
    }

    private static MediaRecommendationSeed Seed(
        WorkMediaType type,
        string title,
        string? creator,
        string[] subjects,
        bool finished = false,
        DateTime? when = null,
        WatchlistIdentity? identity = null) =>
        new(type, identity, title, creator, subjects, finished, when ?? BaseTime);

    private static MediaRecommendationCandidate Candidate(
        string id,
        WorkMediaType type,
        string title,
        string? creator,
        string[] subjects,
        WatchlistIdentity? identity = null) =>
        new(id, type, identity, title, creator, subjects, null, "/details/" + id, null);

    private static IReadOnlySet<WorkMediaType> Visible(params WorkMediaType[] types) =>
        types.ToHashSet();
}
