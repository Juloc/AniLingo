using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.MediaCore;
using Jularr.Web.Features.MediaFacts;
using Jularr.Web.Features.Search;
using Jularr.Web.Features.Watchlist;

namespace Jularr.Tests;

/// <summary>
/// Unified global search (#434), local half: canonical grouping across media types, franchise results,
/// the Media Facts filters, media-type visibility and ranking, all on a real PostgreSQL database.
/// </summary>
[TestClass]
public sealed class GlobalSearchTests
{
    private static readonly WorkMediaType[] AllTypes = [.. WorkMediaTypes.All];

    // -- canonical grouping ------------------------------------------------------------------------

    [TestMethod]
    public async Task RecordsBridgedToTheSameWorkCollapseIntoOneRowAcrossMediaTypes()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        var book = await fixture.AddNovelAsync("Project Hail Mary", book: true, format: "EPUB:en");
        var audiobook = await fixture.AddAudiobookAsync("Project Hail Mary", 2021, author: "Andy Weir");
        var bookWork = await fixture.Bridge.EnsureWorkForNovelAsync(book, WorkMediaType.Book, CancellationToken.None);
        var audioWork = await fixture.Bridge.EnsureWorkForAudiobookAsync(audiobook, CancellationToken.None);
        Assert.AreNotEqual(bookWork, audioWork);

        var separate = await fixture.SearchAsync("Project Hail Mary");
        Assert.AreEqual(2, separate.Items.Count, "Two distinct works stay two rows until they are merged.");

        await fixture.Works.MergeWorksAsync(bookWork, audioWork, "test", CancellationToken.None);

        var merged = await fixture.SearchAsync("Project Hail Mary");
        var row = merged.Items.Single();
        Assert.AreEqual(bookWork, row.WorkId);
        Assert.AreEqual(2, row.Variants.Count);
        CollectionAssert.AreEqual(new[] { MediaSearchType.Book, MediaSearchType.Audiobook }, row.Types.ToArray());
        CollectionAssert.AreEquivalent(new[] { book.Id, audiobook.Id }, row.Variants.Select(v => v.Id).ToArray());
        Assert.AreEqual(1, merged.Total);
        Assert.AreEqual(2021, row.Facts.Year, "The year any variant knows is kept.");
    }

    [TestMethod]
    public async Task MergedRowReportsTheFactsOfEveryVariant()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        var book = await fixture.AddNovelAsync("Dune", book: true, format: "EPUB:en", withContent: true, genres: ["Science fiction"]);
        var audiobook = await fixture.AddAudiobookAsync("Dune", 1965, withFile: true);
        var bookWork = await fixture.Bridge.EnsureWorkForNovelAsync(book, WorkMediaType.Book, CancellationToken.None);
        var audioWork = await fixture.Bridge.EnsureWorkForAudiobookAsync(audiobook, CancellationToken.None);
        await fixture.Works.MergeWorksAsync(bookWork, audioWork, "test", CancellationToken.None);

        var row = (await fixture.SearchAsync("Dune")).Items.Single();

        Assert.IsTrue(row.Facts.HasLocalContent);
        Assert.AreEqual(1965, row.Facts.Year);
        CollectionAssert.AreEqual(new[] { "en" }, row.Facts.Languages.ToArray());
        CollectionAssert.AreEqual(new[] { "Science fiction" }, row.Facts.Genres.ToArray());
        Assert.AreEqual(1, (await fixture.SearchAsync("Dune", new MediaSearchFilters(Genre: "science fiction", YearFrom: 1960, YearTo: 1970))).Total);
    }

    [TestMethod]
    public async Task UnbridgedRecordsWithTheSameTitleStayApart()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await fixture.AddMovieAsync("Dune", 1984);
        await fixture.AddSeriesAsync("Dune", 2000);
        await fixture.AddAudiobookAsync("Dune", 2007);

        var page = await fixture.SearchAsync("Dune");

        Assert.AreEqual(3, page.Items.Count, "Only a shared media-core work collapses records, never a shared title.");
        Assert.IsTrue(page.Items.All(item => item.WorkId is null));
        CollectionAssert.AreEquivalent(
            new[] { MediaSearchType.Movie, MediaSearchType.Series, MediaSearchType.Audiobook },
            page.Items.Select(item => item.Type!.Value).ToArray());
    }

    [TestMethod]
    public async Task BridgedMovieAndSeriesOfDifferentWorksAreNotCollapsed()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        var movie = await fixture.AddMovieAsync("The Thing", 1982, tmdbId: "1091");
        var series = await fixture.AddSeriesAsync("The Thing", 2011);
        await fixture.Bridge.EnsureWorkForMovieAsync(movie, CancellationToken.None);
        await fixture.Bridge.EnsureWorkForSeriesAsync(series, CancellationToken.None);

        var page = await fixture.SearchAsync("The Thing");

        Assert.AreEqual(2, page.Items.Count);
        Assert.AreNotEqual(page.Items[0].WorkId, page.Items[1].WorkId);
        Assert.IsTrue(page.Items.All(item => item.WorkId is not null));
    }

    [TestMethod]
    public async Task AnimeWithoutAProviderMatchIsStillFoundByItsLibraryTitle()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        var unmatched = await fixture.AddUnmatchedAnimeAsync("Unmatched Folder Show");
        await fixture.AddAnimeAsync("Matched Show");

        var page = await fixture.SearchAsync("Unmatched Folder");

        var row = page.Items.Single();
        Assert.AreEqual(MediaSearchType.Anime, row.Type);
        Assert.AreEqual(unmatched.Id, row.Id);
        Assert.AreEqual(1, (await fixture.SearchAsync("Matched Show")).Items.Count(item => item.Title == "Matched Show"));
    }

    // -- franchises --------------------------------------------------------------------------------

    [TestMethod]
    public async Task FranchiseIsFoundAsItsOwnResultByItsTitleOrByAMembersTitle()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        var franchise = await fixture.AddFranchiseAsync(
            "Sousou no Frieren",
            (WatchlistMediaType.Anime, "154587", "Sousou no Frieren"),
            (WatchlistMediaType.Manga, "118586", "Frieren: Beyond Journey's End"),
            (WatchlistMediaType.LightNovel, "9001", "Frieren Side Story"));
        await fixture.AddAnimeAsync("Sousou no Frieren", 2023);

        var byTitle = await fixture.SearchAsync("Sousou no Frieren");
        var franchiseRow = byTitle.Items.Single(item => item.Kind == MediaSearchResultKind.Franchise);
        Assert.AreEqual(franchise, franchiseRow.Id);
        Assert.AreEqual(3, franchiseRow.MemberCount);
        Assert.IsNull(franchiseRow.Type);
        Assert.IsNull(franchiseRow.WorkId);
        Assert.AreEqual("Franchise:Sousou no Frieren", GlobalSearchFixture.Rows(byTitle)[0], "The franchise is listed before the works it groups.");
        Assert.IsTrue(byTitle.Items.Any(item => item.Kind == MediaSearchResultKind.Work && item.Type == MediaSearchType.Anime));

        var byMember = await fixture.SearchAsync("Beyond Journey's End");
        var memberHit = byMember.Items.Single();
        Assert.AreEqual(MediaSearchResultKind.Franchise, memberHit.Kind);
        Assert.AreEqual(franchise, memberHit.Id, "A franchise is found through the title of one of its works.");
        Assert.AreEqual("Sousou no Frieren", memberHit.Title, "The row is named after the franchise, not the member.");
    }

    [TestMethod]
    public async Task FranchiseMemberCountAndVisibilityFollowTheProfilesMediaTypes()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await fixture.AddFranchiseAsync(
            "Overlord",
            (WatchlistMediaType.Anime, "20", "Overlord"),
            (WatchlistMediaType.LightNovel, "21", "Overlord Novel"));

        var everything = await fixture.SearchAsync("Overlord", visible: AllTypes);
        Assert.AreEqual(2, everything.Items.Single(item => item.Kind == MediaSearchResultKind.Franchise).MemberCount);

        var animeOnly = await fixture.SearchAsync("Overlord", visible: [WorkMediaType.Anime]);
        Assert.AreEqual(1, animeOnly.Items.Single(item => item.Kind == MediaSearchResultKind.Franchise).MemberCount);

        var neither = await fixture.SearchAsync("Overlord", visible: [WorkMediaType.Movie, WorkMediaType.Book]);
        Assert.AreEqual(0, neither.Items.Count, "A franchise whose works are all hidden does not exist for the profile.");
    }

    [TestMethod]
    public async Task FranchiseIsLimitedByTheTypeFilterAndAbsentUnderFactFilters()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await fixture.AddFranchiseAsync(
            "Mushoku Tensei",
            (WatchlistMediaType.Anime, "30", "Mushoku Tensei"),
            (WatchlistMediaType.LightNovel, "31", "Mushoku Tensei Novel"));
        await fixture.AddAnimeAsync("Mushoku Tensei", 2021);

        var manga = await fixture.SearchAsync("Mushoku Tensei", new MediaSearchFilters(Types: [MediaSearchType.Manga]));
        Assert.AreEqual(0, manga.Items.Count, "No manga in the franchise, so neither the franchise nor a work matches.");

        var anime = await fixture.SearchAsync("Mushoku Tensei", new MediaSearchFilters(Types: [MediaSearchType.Anime]));
        Assert.IsTrue(anime.Items.Any(item => item.Kind == MediaSearchResultKind.Franchise));
        Assert.AreEqual(1, anime.Items.Single(item => item.Kind == MediaSearchResultKind.Franchise).MemberCount);

        var byYear = await fixture.SearchAsync("Mushoku Tensei", new MediaSearchFilters(YearFrom: 2021));
        Assert.IsFalse(byYear.Items.Any(item => item.Kind == MediaSearchResultKind.Franchise), "A franchise has no facts, so it cannot pass a fact filter.");
        Assert.AreEqual(1, byYear.Items.Count);
    }

    // -- ranking -----------------------------------------------------------------------------------

    [TestMethod]
    public async Task RankingIsExactThenPrefixThenFullTextThenFuzzyAcrossTypes()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await fixture.AddMovieAsync("Mushuku Tensai", 2001);                    // fuzzy only
        await fixture.AddNovelAsync("Legend of Mushoku Tensei Chronicles");      // full text
        await fixture.AddMangaAsync("Mushoku Tensei Extra");                     // prefix
        await fixture.AddAnimeAsync("Mushoku Tensei", 2021);                     // exact

        var page = await fixture.SearchAsync("Mushoku Tensei");

        CollectionAssert.AreEqual(
            new[]
            {
                "Anime:Mushoku Tensei",
                "Manga:Mushoku Tensei Extra",
                "Novel:Legend of Mushoku Tensei Chronicles",
                "Movie:Mushuku Tensai"
            },
            GlobalSearchFixture.Rows(page));
        Assert.IsTrue(page.Items.Zip(page.Items.Skip(1), (a, b) => a.Score >= b.Score).All(ordered => ordered));
        Assert.IsTrue(page.Items[0].Score >= 1000 && page.Items[1].Score is >= 500 and < 1000);
        Assert.IsTrue(page.Items[2].Score is >= 100 and < 500 && page.Items[3].Score < 100);
    }

    [TestMethod]
    public async Task PagingIsStableBoundedAndCoversEveryRowExactlyOnce()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        for (var index = 0; index < 25; index++)
        {
            await fixture.AddMovieAsync($"Serial Saga {index:D2}", 2000 + index);
        }

        var seen = new List<string>();
        for (var offset = 0; offset < 30; offset += 10)
        {
            var page = await fixture.SearchAsync("Serial Saga", limit: 10, offset: offset);
            Assert.AreEqual(25, page.Total);
            Assert.IsTrue(page.Items.Count <= 10);
            seen.AddRange(page.Items.Select(item => item.Title));
        }

        Assert.AreEqual(25, seen.Count);
        Assert.AreEqual(25, seen.Distinct().Count(), "No row repeats or is skipped across pages.");
        CollectionAssert.AreEqual(seen.OrderBy(title => title, StringComparer.OrdinalIgnoreCase).ToArray(), seen.ToArray());
        var last = await fixture.SearchAsync("Serial Saga", limit: 10, offset: 20);
        Assert.AreEqual(5, last.Items.Count);
        Assert.IsFalse(last.HasMore);
    }

    // -- media-type visibility ---------------------------------------------------------------------

    [TestMethod]
    public async Task HiddenMediaTypesNeverAppear()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await fixture.AddAnimeAsync("Cowboy Bebop", 1998);
        await fixture.AddMovieAsync("Cowboy Bebop: The Movie", 2001);
        await fixture.AddNovelAsync("Cowboy Bebop Novel");
        await fixture.AddMangaAsync("Cowboy Bebop Manga");
        await fixture.AddSeriesAsync("Cowboy Bebop Live Action", 2021);
        await fixture.AddAudiobookAsync("Cowboy Bebop Audio");
        await fixture.AddNovelAsync("Cowboy Bebop Book", book: true);

        var all = await fixture.SearchAsync("Cowboy Bebop", visible: AllTypes);
        Assert.AreEqual(7, all.Items.Count);

        var withoutAnime = await fixture.SearchAsync(
            "Cowboy Bebop",
            visible: [.. AllTypes.Where(type => type != WorkMediaType.Anime)]);
        Assert.AreEqual(6, withoutAnime.Items.Count);
        Assert.IsFalse(withoutAnime.Items.Any(item => item.Type == MediaSearchType.Anime));

        var readingOnly = await fixture.SearchAsync(
            "Cowboy Bebop",
            visible: [WorkMediaType.LightNovel, WorkMediaType.Manga]);
        CollectionAssert.AreEquivalent(
            new[] { MediaSearchType.Novel, MediaSearchType.Manga },
            readingOnly.Items.Select(item => item.Type!.Value).ToArray());

        // Books and audiobooks share the Book media type: hiding it hides both.
        var bookVisible = await fixture.SearchAsync("Cowboy Bebop", visible: [WorkMediaType.Book]);
        CollectionAssert.AreEquivalent(
            new[] { MediaSearchType.Book, MediaSearchType.Audiobook },
            bookVisible.Items.Select(item => item.Type!.Value).ToArray());

        var nothing = await fixture.SearchAsync("Cowboy Bebop", visible: []);
        Assert.AreEqual(0, nothing.Items.Count);
        Assert.AreEqual(0, nothing.Total);
    }

    [TestMethod]
    public async Task HiddenTypesDoNotLeakThroughAFilterEither()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await fixture.AddAnimeAsync("Secret Show", 2020);

        var page = await fixture.SearchAsync(
            "Secret Show",
            new MediaSearchFilters(Types: [MediaSearchType.Anime], YearFrom: 2000),
            visible: [WorkMediaType.Movie]);

        Assert.AreEqual(0, page.Items.Count);
        Assert.AreEqual(MediaSearchFacets.Empty, page.Facets, "Filter values must not reveal titles of a hidden type.");
    }

    [TestMethod]
    public async Task ARowSpanningVisibleAndHiddenTypesOnlyShowsTheVisibleVariant()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        var movie = await fixture.AddMovieAsync("Ghost in the Shell", 1995);
        var anime = await fixture.AddAnimeAsync("Ghost in the Shell", 1995);
        var movieWork = await fixture.Bridge.EnsureWorkForMovieAsync(movie, CancellationToken.None);
        var animeWork = await fixture.Bridge.EnsureWorkForAnimeAsync(anime, CancellationToken.None);
        await fixture.Works.MergeWorksAsync(movieWork, animeWork, "test", CancellationToken.None);

        var both = (await fixture.SearchAsync("Ghost in the Shell", visible: AllTypes)).Items.Single();
        Assert.AreEqual(2, both.Variants.Count);

        var onlyMovies = (await fixture.SearchAsync("Ghost in the Shell", visible: [WorkMediaType.Movie])).Items.Single();
        Assert.AreEqual(1, onlyMovies.Variants.Count);
        Assert.AreEqual(MediaSearchType.Movie, onlyMovies.Variants[0].Type);
        Assert.AreEqual(movieWork, onlyMovies.WorkId);
    }

    // -- Media Facts filters -----------------------------------------------------------------------

    [TestMethod]
    public async Task TypeFilterKeepsOnlyTheChosenSourceTypes()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await SeedStarsAsync(fixture);

        var manga = await fixture.SearchAsync("Star", new MediaSearchFilters(Types: [MediaSearchType.Manga]));
        CollectionAssert.AreEqual(new[] { "Manga:Star Manga" }, GlobalSearchFixture.Rows(manga));

        var two = await fixture.SearchAsync("Star", new MediaSearchFilters(Types: [MediaSearchType.Movie, MediaSearchType.Series]));
        CollectionAssert.AreEquivalent(new[] { "Movie:Star Movie", "Series:Star Series" }, GlobalSearchFixture.Rows(two));

        var books = await fixture.SearchAsync("Star", new MediaSearchFilters(Types: [MediaSearchType.Book]));
        CollectionAssert.AreEqual(new[] { "Book:Star Book" }, GlobalSearchFixture.Rows(books));

        var novels = await fixture.SearchAsync("Star", new MediaSearchFilters(Types: [MediaSearchType.Novel]));
        CollectionAssert.AreEqual(new[] { "Novel:Star Novel" }, GlobalSearchFixture.Rows(novels));
    }

    [TestMethod]
    public async Task LocalFilterSeparatesTitlesWithPlayableOrReadableContentFromTheRest()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await SeedStarsAsync(fixture);

        var local = await fixture.SearchAsync("Star", new MediaSearchFilters(Local: true));
        CollectionAssert.AreEquivalent(
            new[]
            {
                "Anime:Star Anime Local", "Manga:Star Manga", "Novel:Star Novel", "Book:Star Book",
                "Audiobook:Star Audiobook", "Anime:Star Anime Wanted"
            },
            GlobalSearchFixture.Rows(local));

        var notLocal = await fixture.SearchAsync("Star", new MediaSearchFilters(Local: false));
        CollectionAssert.AreEquivalent(
            new[] { "Anime:Star Anime Remote", "Movie:Star Movie", "Series:Star Series" },
            GlobalSearchFixture.Rows(notLocal));
    }

    [TestMethod]
    public async Task MonitoredFilterUsesTheAnimeMonitoringState()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await SeedStarsAsync(fixture);

        var monitored = await fixture.SearchAsync("Star", new MediaSearchFilters(Monitored: true));
        CollectionAssert.AreEquivalent(
            new[] { "Anime:Star Anime Local", "Anime:Star Anime Wanted" },
            GlobalSearchFixture.Rows(monitored));

        var notMonitored = await fixture.SearchAsync("Star", new MediaSearchFilters(Monitored: false));
        Assert.IsFalse(notMonitored.Items.Any(item => item.Title is "Star Anime Local" or "Star Anime Wanted"));
        Assert.AreEqual(7, notMonitored.Items.Count);
    }

    [TestMethod]
    public async Task WantedFilterCoversWantedAnimeEpisodesAndOpenRequestsForAnyType()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await SeedStarsAsync(fixture);

        var wanted = await fixture.SearchAsync("Star", new MediaSearchFilters(Wanted: true));
        CollectionAssert.AreEquivalent(
            new[] { "Anime:Star Anime Wanted", "Anime:Star Anime Remote", "Movie:Star Movie" },
            GlobalSearchFixture.Rows(wanted));

        var notWanted = await fixture.SearchAsync("Star", new MediaSearchFilters(Wanted: false));
        Assert.IsFalse(notWanted.Items.Any(item => item.Title is "Star Anime Wanted" or "Star Anime Remote" or "Star Movie"));
        Assert.AreEqual(6, notWanted.Items.Count);
    }

    [TestMethod]
    public async Task FinishedRequestsDoNotMakeATitleWanted()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        var anime = await fixture.AddAnimeAsync("Finished Request Show", externalId: "4242");
        var request = await fixture.OpenRequestAsync(MediaAcquisitionKind.Anime, "anilist", "4242", anime.Title);
        Assert.AreEqual(1, (await fixture.SearchAsync("Finished Request", new MediaSearchFilters(Wanted: true))).Total);

        await fixture.Requests.UpdateStatusAsync(request.Id, AcquisitionRequestStatus.Completed, null, null, null, null, CancellationToken.None);

        Assert.AreEqual(0, (await fixture.SearchAsync("Finished Request", new MediaSearchFilters(Wanted: true))).Total);
    }

    [TestMethod]
    public async Task LanguageFilterMatchesAudioSubtitleAndTextLanguagesInAnyTagForm()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await SeedStarsAsync(fixture);

        var german = await fixture.SearchAsync("Star", new MediaSearchFilters(Language: "de"));
        CollectionAssert.AreEquivalent(
            new[] { "Novel:Star Novel", "Book:Star Book" },
            GlobalSearchFixture.Rows(german));

        // A container tag (jpn) and a region tag (ja-JP) select the same titles as ja.
        var japanese = GlobalSearchFixture.Rows(await fixture.SearchAsync("Star", new MediaSearchFilters(Language: "ja")));
        CollectionAssert.AreEquivalent(
            new[] { "Anime:Star Anime Local", "Anime:Star Anime Wanted", "Manga:Star Manga", "Novel:Star Novel" },
            japanese);
        CollectionAssert.AreEquivalent(japanese, GlobalSearchFixture.Rows(await fixture.SearchAsync("Star", new MediaSearchFilters(Language: "jpn"))));
        CollectionAssert.AreEquivalent(japanese, GlobalSearchFixture.Rows(await fixture.SearchAsync("Star", new MediaSearchFilters(Language: "ja-JP"))));

        // An embedded English subtitle stream counts as English.
        var english = await fixture.SearchAsync("Star", new MediaSearchFilters(Language: "eng"));
        CollectionAssert.AreEqual(new[] { "Anime:Star Anime Local" }, GlobalSearchFixture.Rows(english));

        Assert.AreEqual(0, (await fixture.SearchAsync("Star", new MediaSearchFilters(Language: "fr"))).Items.Count);
    }

    [TestMethod]
    public async Task GenreFilterIsCaseInsensitiveAndNeverMatchesTitlesWithoutGenres()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await SeedStarsAsync(fixture);

        var fantasy = await fixture.SearchAsync("Star", new MediaSearchFilters(Genre: "fantasy"));
        CollectionAssert.AreEquivalent(new[] { "Novel:Star Novel", "Book:Star Book" }, GlobalSearchFixture.Rows(fantasy));

        var romance = await fixture.SearchAsync("Star", new MediaSearchFilters(Genre: " Romance "));
        CollectionAssert.AreEqual(new[] { "Novel:Star Novel" }, GlobalSearchFixture.Rows(romance));

        Assert.AreEqual(0, (await fixture.SearchAsync("Star", new MediaSearchFilters(Genre: "Horror"))).Items.Count);
    }

    [TestMethod]
    public async Task YearFilterIsInclusiveAndNeverMatchesAnUnknownYear()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await SeedStarsAsync(fixture);

        var range = await fixture.SearchAsync("Star", new MediaSearchFilters(YearFrom: 2019, YearTo: 2021));
        CollectionAssert.AreEquivalent(
            new[] { "Anime:Star Anime Local", "Series:Star Series", "Audiobook:Star Audiobook" },
            GlobalSearchFixture.Rows(range));

        var from = await fixture.SearchAsync("Star", new MediaSearchFilters(YearFrom: 2021));
        CollectionAssert.AreEqual(new[] { "Series:Star Series" }, GlobalSearchFixture.Rows(from));

        var to = await fixture.SearchAsync("Star", new MediaSearchFilters(YearTo: 2010));
        CollectionAssert.AreEqual(new[] { "Movie:Star Movie" }, GlobalSearchFixture.Rows(to));

        var exact = await fixture.SearchAsync("Star", new MediaSearchFilters(YearFrom: 2015, YearTo: 2015));
        CollectionAssert.AreEqual(new[] { "Anime:Star Anime Remote" }, GlobalSearchFixture.Rows(exact));
    }

    [TestMethod]
    public async Task FiltersCombineWithAnd()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await SeedStarsAsync(fixture);

        var page = await fixture.SearchAsync(
            "Star",
            new MediaSearchFilters(Local: true, Language: "ja", Genre: "Fantasy"));

        CollectionAssert.AreEqual(new[] { "Novel:Star Novel" }, GlobalSearchFixture.Rows(page));

        var none = await fixture.SearchAsync(
            "Star",
            new MediaSearchFilters(Types: [MediaSearchType.Movie], Local: true));
        Assert.AreEqual(0, none.Items.Count);
    }

    [TestMethod]
    public async Task FacetsOfferTheValuesTheQueryCanReachBeforeTheFactFiltersNarrowThem()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        await SeedStarsAsync(fixture);

        var unfiltered = await fixture.SearchAsync("Star");
        CollectionAssert.AreEquivalent(new[] { "de", "en", "ja" }, unfiltered.Facets.Languages.ToArray());
        CollectionAssert.AreEquivalent(new[] { "Fantasy", "Romance" }, unfiltered.Facets.Genres.ToArray());
        Assert.AreEqual(2010, unfiltered.Facets.MinYear);
        Assert.AreEqual(2021, unfiltered.Facets.MaxYear);

        var narrowed = await fixture.SearchAsync("Star", new MediaSearchFilters(Language: "de"));
        CollectionAssert.AreEquivalent(unfiltered.Facets.Languages.ToArray(), narrowed.Facets.Languages.ToArray());
    }

    [TestMethod]
    public async Task SearchFactsAgreeWithTheMediaFactsProjectionOfTheSameWork()
    {
        await using var fixture = await GlobalSearchFixture.CreateAsync();
        var data = await SeedStarsAsync(fixture);
        var facts = new MediaFactsService(fixture.Db);

        var anime = await facts.GetAnimeFactsAsync(data.AnimeLocal.Id, CancellationToken.None);
        var novel = await facts.GetNovelFactsAsync(data.Novel.Id, CancellationToken.None);
        var book = await facts.GetNovelFactsAsync(data.Book.Id, CancellationToken.None);
        var manga = await facts.GetMangaFactsAsync(data.Manga, CancellationToken.None);

        var page = await fixture.SearchAsync("Star");
        string[] Languages(string title) => [.. page.Items.Single(item => item.Title == title).Facts.Languages];
        string[] Codes(MediaFacts value) => [.. value.Languages.Select(row => row.Language).Distinct().Order(StringComparer.Ordinal)];

        CollectionAssert.AreEqual(Codes(anime), Languages("Star Anime Local"));
        CollectionAssert.AreEqual(Codes(novel), Languages("Star Novel"));
        CollectionAssert.AreEqual(Codes(book), Languages("Star Book"));
        CollectionAssert.AreEqual(Codes(manga), Languages("Star Manga"));
        Assert.AreEqual(anime.ReleaseYear, page.Items.Single(item => item.Title == "Star Anime Local").Facts.Year);
    }

    [TestMethod]
    public void FilterSemanticsAreExactOnKnownFactsAndNeverGuess()
    {
        var facts = new MediaSearchFacts(2020, ["ja", "en"], ["Fantasy"], true, true, false);

        Assert.IsTrue(MediaSearchGrouping.Matches(facts, MediaSearchFilters.None));
        Assert.IsTrue(MediaSearchGrouping.Matches(facts, new MediaSearchFilters(Local: true, Monitored: true, Wanted: false)));
        Assert.IsFalse(MediaSearchGrouping.Matches(facts, new MediaSearchFilters(Wanted: true)));
        Assert.IsFalse(MediaSearchGrouping.Matches(facts, new MediaSearchFilters(Local: false)));
        Assert.IsTrue(MediaSearchGrouping.Matches(facts, new MediaSearchFilters(Language: "JPN")));
        Assert.IsTrue(MediaSearchGrouping.Matches(facts, new MediaSearchFilters(Genre: "FANTASY")));
        Assert.IsTrue(MediaSearchGrouping.Matches(facts, new MediaSearchFilters(YearFrom: 2020, YearTo: 2020)));
        Assert.IsFalse(MediaSearchGrouping.Matches(facts, new MediaSearchFilters(YearTo: 2019)));

        var unknown = MediaSearchFacts.Unknown;
        Assert.IsFalse(MediaSearchGrouping.Matches(unknown, new MediaSearchFilters(YearFrom: 1900)));
        Assert.IsFalse(MediaSearchGrouping.Matches(unknown, new MediaSearchFilters(YearTo: 2100)));
        Assert.IsFalse(MediaSearchGrouping.Matches(unknown, new MediaSearchFilters(Language: "en")));
        Assert.IsFalse(MediaSearchGrouping.Matches(unknown, new MediaSearchFilters(Genre: "Fantasy")));
        Assert.IsTrue(MediaSearchGrouping.Matches(unknown, new MediaSearchFilters(Local: false, Monitored: false, Wanted: false)));
    }

    // -- data ---------------------------------------------------------------------------------------

    private sealed record StarData(
        Web.Features.Library.Anime AnimeLocal,
        Guid Manga,
        Web.Features.Novels.NovelWork Novel,
        Web.Features.Novels.NovelWork Book);

    /// <summary>
    /// One title of every type named "Star ...": a local monitored anime with Japanese audio and an
    /// English subtitle, a remote anime with an open request, a local wanted anime, a manga with
    /// pages, a novel translated to German, a German book, and a movie (with an open request), a
    /// series and an audiobook that have no local files except the audiobook.
    /// </summary>
    private static async Task<StarData> SeedStarsAsync(GlobalSearchFixture fixture)
    {
        var local = await fixture.AddAnimeAsync(
            "Star Anime Local", 2020, withFile: true,
            streams: [("jpn", MediaStreamKind.Audio), ("eng", MediaStreamKind.Subtitle)]);
        await fixture.MonitorAnimeAsync(local);

        var wantedAnime = await fixture.AddAnimeAsync(
            "Star Anime Wanted", 2018, withFile: true,
            streams: [("jpn", MediaStreamKind.Audio)]);
        await fixture.MonitorAnimeAsync(wantedAnime, wanted: true);

        await fixture.AddAnimeAsync("Star Anime Remote", 2015, externalId: "777");
        await fixture.OpenRequestAsync(MediaAcquisitionKind.Anime, "anilist", "777", "Star Anime Remote");

        var manga = await fixture.AddMangaAsync("Star Manga", pages: 12);
        var novel = await fixture.AddNovelAsync(
            "Star Novel", genres: ["Fantasy", "Romance"], withContent: true, translatedTo: "de");
        var book = await fixture.AddNovelAsync(
            "Star Book", book: true, format: "EPUB:de", genres: ["Fantasy"], withContent: true);

        await fixture.AddMovieAsync("Star Movie", 2010, tmdbId: "5555");
        await fixture.OpenRequestAsync(MediaAcquisitionKind.Movie, "tmdb", "5555", "Star Movie");
        await fixture.AddSeriesAsync("Star Series", 2021);
        await fixture.AddAudiobookAsync("Star Audiobook", 2019, withFile: true);

        return new StarData(local, manga, novel, book);
    }
}
