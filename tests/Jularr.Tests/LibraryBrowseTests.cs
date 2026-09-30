using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Localization;
using Jularr.Web.Ui;

namespace Jularr.Tests;

/// <summary>The pure rules behind the Library page (docs/mockups/library): address, filters, sorts, counts and card text.</summary>
[TestClass]
public sealed class LibraryBrowseTests
{
    private static readonly UiTextBundle Ui = UiTextBundle.English;
    private static readonly DateTime Stamp = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static LibraryBrowseQuery Parse(string query)
    {
        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2))
            .ToLookup(x => x[0], x => Uri.UnescapeDataString(x.Length > 1 ? x[1] : ""));
        return LibraryBrowse.Parse(key => [.. pairs[key]]);
    }

    private static LibraryCardEntry Entry(
        string title,
        int? year = null,
        string? status = null,
        string? format = null,
        int? score = null,
        string[]? audio = null,
        string[]? subtitles = null,
        MediaBannerProgress? progress = null,
        int playable = 12,
        int missing = 0,
        DateTime? added = null,
        DateTime? watched = null,
        AcquisitionRequestStatus? request = null) =>
        new(
            new MediaBannerCardData(
                MediaBannerKind.Anime,
                title,
                "/Library/Anime/" + title,
                ProviderStatus: status,
                Year: year,
                AverageScore: score,
                AudioLanguages: audio ?? [],
                SubtitleLanguages: subtitles ?? [],
                Progress: progress,
                Availability: new MediaAvailabilityFacts(true, playable > 0, request)),
            "/poster/" + title,
            format,
            playable,
            missing,
            added ?? Stamp,
            watched);

    private static MediaBannerProgress Progress(
        MediaBannerProgressState state,
        double next = 1,
        int? season = null,
        int? percent = null) =>
        new(state, MediaBannerUnit.Episode, next, "/Library/Episode/" + next, null, season, percent);

    [TestMethod]
    public void PlainLibraryHasNoQueryAndEveryViewRoundTripsThroughTheAddress()
    {
        Assert.AreEqual("/Library", LibraryBrowse.Href(new LibraryBrowseQuery()));

        var query = new LibraryBrowseQuery
        {
            Sort = LibrarySort.ProgressHighest,
            Layout = LibraryLayout.List,
            Progress = [LibraryProgressState.NotStarted, LibraryProgressState.InProgress],
            Availability = [LibraryAvailabilityState.Complete, LibraryAvailabilityState.Requested],
            PreferredLanguage = true,
            AudioLanguage = "ja",
            SubtitleLanguage = "de",
            Year = 2024,
            Status = MediaReleaseStatus.Ongoing,
            Format = "TV_SHORT"
        };

        var href = LibraryBrowse.Href(query);
        Assert.AreEqual(
            "/Library?sort=progress-desc&view=list&progress=notstarted&progress=inprogress&avail=complete&avail=requested"
            + "&pref=1&audio=ja&sub=de&year=2024&status=ongoing&format=TV_SHORT",
            href);

        var parsed = Parse(href[(href.IndexOf('?') + 1)..]);
        Assert.AreEqual(LibrarySort.ProgressHighest, parsed.Sort);
        Assert.AreEqual(LibraryLayout.List, parsed.Layout);
        CollectionAssert.AreEqual(query.Progress.ToArray(), parsed.Progress.ToArray());
        CollectionAssert.AreEqual(query.Availability.ToArray(), parsed.Availability.ToArray());
        Assert.IsTrue(parsed.PreferredLanguage);
        Assert.AreEqual("ja", parsed.AudioLanguage);
        Assert.AreEqual("de", parsed.SubtitleLanguage);
        Assert.AreEqual(2024, parsed.Year);
        Assert.AreEqual(MediaReleaseStatus.Ongoing, parsed.Status);
        Assert.AreEqual("TV_SHORT", parsed.Format);
        Assert.AreEqual(10, parsed.ActiveFilterCount);
    }

    [TestMethod]
    public void ActiveFilterCountCountsEveryChosenValueAndSortOrLayoutDoNotCount()
    {
        Assert.AreEqual(0, new LibraryBrowseQuery { Sort = LibrarySort.Title, Layout = LibraryLayout.List }.ActiveFilterCount);
        Assert.AreEqual(3, Parse("progress=inprogress&progress=completed&year=2023").ActiveFilterCount);
        Assert.AreEqual(0, Parse("progress=inprogress&progress=completed&year=2023").WithoutFilters().ActiveFilterCount);
    }

    [TestMethod]
    public void UnknownOrMalformedValuesAreIgnoredSoStaleBookmarksStillOpenTheLibrary()
    {
        var query = Parse("sort=sideways&view=cube&progress=nope&avail=&year=abc&status=exploded&format=tv%20show&audio=off&section=other");

        Assert.AreEqual(LibrarySort.Recent, query.Sort);
        Assert.AreEqual(LibraryLayout.Grid, query.Layout);
        Assert.AreEqual(0, query.ActiveFilterCount);
        Assert.IsFalse(query.Collections);
        Assert.AreEqual("/Library", LibraryBrowse.Href(query));
        Assert.IsNull(Parse("year=1500").Year);
    }

    [TestMethod]
    public void CollectionsViewHasItsOwnAddressAndDropsLibraryState()
    {
        var query = Parse("section=collections&sort=title&progress=completed");

        Assert.IsTrue(query.Collections);
        Assert.AreEqual("/Library?section=collections", LibraryBrowse.Href(query));
    }

    [TestMethod]
    public void ProgressFilterIsAnOrWithinTheGroupAndNotStartedIncludesUnplayableTitles()
    {
        var entries = new[]
        {
            Entry("a", progress: Progress(MediaBannerProgressState.InProgress, 3, percent: 25)),
            Entry("b", progress: Progress(MediaBannerProgressState.Completed, 1, percent: 100)),
            Entry("c", progress: Progress(MediaBannerProgressState.NotStarted)),
            Entry("d", progress: null, playable: 0)
        };

        Assert.AreEqual("a,b", Titles(entries, "progress=inprogress&progress=completed"));
        Assert.AreEqual("c,d", Titles(entries, "progress=notstarted&sort=title"));
    }

    [TestMethod]
    public void AvailabilityStatesMatchAsDocumentedAndTheCardShowsOneMarker()
    {
        var complete = Entry("complete");
        var partial = Entry("partial", playable: 8, missing: 4);
        var requested = Entry("requested", playable: 0, request: AcquisitionRequestStatus.Downloading);
        var partialRequested = Entry("partial-requested", playable: 3, missing: 9, request: AcquisitionRequestStatus.Searching);
        var missing = Entry("missing", playable: 0);
        var all = new[] { complete, partial, requested, partialRequested, missing };

        Assert.AreEqual("complete", Titles(all, "avail=complete"));
        Assert.AreEqual("partial,partial-requested", Titles(all, "avail=partial&sort=title"));
        Assert.AreEqual("partial-requested,requested", Titles(all, "avail=requested&sort=title"));
        Assert.AreEqual("missing", Titles(all, "avail=missing"));
        Assert.AreEqual("complete,partial-requested,requested", Titles(all, "avail=complete&avail=requested&sort=title"));

        Assert.IsNull(LibraryBrowse.IndicatorOf(complete));
        Assert.AreEqual(LibraryAvailabilityState.Partial, LibraryBrowse.IndicatorOf(partial));
        Assert.AreEqual(LibraryAvailabilityState.Requested, LibraryBrowse.IndicatorOf(partialRequested), "An open request wins.");
        Assert.AreEqual(LibraryAvailabilityState.Missing, LibraryBrowse.IndicatorOf(missing));
    }

    [TestMethod]
    public void LanguageFiltersUseThePreferredLanguageAndTheChosenAudioOrSubtitle()
    {
        var german = Entry("german", audio: ["ja", "de"], subtitles: ["en"]);
        var subbed = Entry("subbed", audio: ["ja"], subtitles: ["de", "en"]);
        var other = Entry("other", audio: ["ja"], subtitles: ["en"]);
        var entries = new[] { german, subbed, other };
        var preference = LibraryLanguagePreference.From("de", "de");

        Assert.AreEqual("german,subbed", Titles(entries, "pref=1&sort=title", preference));
        Assert.AreEqual("german", Titles(entries, "audio=de&sort=title"));
        Assert.AreEqual("german,other,subbed", Titles(entries, "audio=ja&sub=&sort=title"));
        Assert.AreEqual("subbed", Titles(entries, "sub=de"));
        Assert.AreEqual("german,other,subbed", Titles(entries, "audio=ja&sub=en&sort=title"));
    }

    [TestMethod]
    public void PreferenceIgnoresSubtitlesOffAndUnknownValues()
    {
        Assert.IsFalse(LibraryLanguagePreference.From(null, "off").IsSet);
        Assert.IsFalse(LibraryLanguagePreference.From("", null).IsSet);
        var both = LibraryLanguagePreference.From("ja", "de");
        CollectionAssert.AreEqual(new[] { "JA", "DE" }, both.Codes.ToArray());
        CollectionAssert.AreEqual(new[] { "DE" }, LibraryLanguagePreference.From("de", "de").Codes.ToArray());
    }

    [TestMethod]
    public void YearStatusAndFormatNarrowTheGrid()
    {
        var entries = new[]
        {
            Entry("a", year: 2024, status: "RELEASING", format: "TV"),
            Entry("b", year: 2023, status: "FINISHED", format: "movie"),
            Entry("c", year: 2024, status: "FINISHED", format: "TV")
        };

        Assert.AreEqual("a,c", Titles(entries, "year=2024&sort=title"));
        Assert.AreEqual("b,c", Titles(entries, "status=finished&sort=title"));
        Assert.AreEqual("b", Titles(entries, "format=MOVIE"));
        Assert.AreEqual("c", Titles(entries, "year=2024&status=finished"));
    }

    [TestMethod]
    public void SortsOrderAsNamedAndKeepMissingValuesLastWithTheTitleAsTieBreak()
    {
        var older = Stamp.AddDays(-10);
        var entries = new[]
        {
            Entry("delta", year: 2020, score: 70, added: older, watched: Stamp,
                progress: Progress(MediaBannerProgressState.InProgress, 2, percent: 40)),
            Entry("alpha", year: 2024, score: 90, added: Stamp,
                progress: Progress(MediaBannerProgressState.Completed, 1, percent: 100)),
            Entry("charlie", year: null, score: null, added: older, watched: Stamp.AddDays(-1)),
            Entry("bravo", year: 2020, score: 70, added: older)
        };

        Assert.AreEqual("alpha,bravo,charlie,delta", Titles(entries, "sort=recent"));
        Assert.AreEqual("alpha,bravo,charlie,delta", Titles(entries, "sort=title"));
        Assert.AreEqual("delta,charlie,alpha,bravo", Titles(entries, "sort=watched"));
        Assert.AreEqual("alpha,bravo,delta,charlie", Titles(entries, "sort=year-desc"));
        Assert.AreEqual("bravo,delta,alpha,charlie", Titles(entries, "sort=year-asc"));
        Assert.AreEqual("alpha,delta,bravo,charlie", Titles(entries, "sort=progress-desc"));
        Assert.AreEqual("bravo,charlie,delta,alpha", Titles(entries, "sort=progress-asc"));
        Assert.AreEqual("alpha,bravo,delta,charlie", Titles(entries, "sort=rating"));
    }

    [TestMethod]
    public void FacetsCountWholeLibraryAndOfferOnlyValuesThatExist()
    {
        var entries = new[]
        {
            Entry("a", year: 2024, status: "RELEASING", format: "TV", audio: ["ja", "de"], subtitles: ["en"],
                progress: Progress(MediaBannerProgressState.InProgress, 2, percent: 10)),
            Entry("b", year: 2022, status: "FINISHED", format: "tv", audio: ["ja"], playable: 4, missing: 2),
            Entry("c", format: "SOMETHING_NEW", playable: 0)
        };

        var facets = LibraryBrowse.Facets(entries, LibraryLanguagePreference.From("de", null));

        Assert.AreEqual(1, facets.Progress[LibraryProgressState.InProgress]);
        Assert.AreEqual(2, facets.Progress[LibraryProgressState.NotStarted]);
        Assert.AreEqual(1, facets.Availability[LibraryAvailabilityState.Complete]);
        Assert.AreEqual(1, facets.Availability[LibraryAvailabilityState.Partial]);
        Assert.AreEqual(1, facets.Availability[LibraryAvailabilityState.Missing]);
        Assert.AreEqual(0, facets.Availability[LibraryAvailabilityState.Requested]);
        Assert.AreEqual(1, facets.PreferredLanguage);
        CollectionAssert.AreEqual(new[] { "de", "ja" }, facets.AudioLanguages.ToArray());
        CollectionAssert.AreEqual(new[] { "en" }, facets.SubtitleLanguages.ToArray());
        CollectionAssert.AreEqual(new[] { 2024, 2022 }, facets.Years.ToArray());
        CollectionAssert.AreEqual(
            new[] { MediaReleaseStatus.Ongoing, MediaReleaseStatus.Finished },
            facets.Statuses.ToArray());
        CollectionAssert.AreEqual(new[] { "TV" }, facets.Formats.ToArray(), "Unknown formats have no label, so they are not offered.");
    }

    [TestMethod]
    [DataRow(false, false, 0, 0, LibraryPageState.Empty)]
    [DataRow(false, false, 5, 0, LibraryPageState.NoResults)]
    [DataRow(false, false, 5, 3, LibraryPageState.Ready)]
    [DataRow(false, true, 5, 3, LibraryPageState.Degraded)]
    [DataRow(true, false, 5, 3, LibraryPageState.Error)]
    [DataRow(true, true, 0, 0, LibraryPageState.Error)]
    [DataRow(false, true, 0, 0, LibraryPageState.Empty)]
    public void BodyStateFollowsTheReadAndTheFilters(bool failed, bool degraded, int total, int shown, LibraryPageState expected)
    {
        Assert.AreEqual(expected, LibraryBrowse.ResolveState(failed, degraded, total, shown));
    }

    [TestMethod]
    public void InProgressCardNamesTheNextEpisodeAndOffersToContinue()
    {
        var single = LibraryCardView.Create(
            Entry("Solo", progress: Progress(MediaBannerProgressState.InProgress, 8, percent: 72)),
            LibraryLanguagePreference.None,
            Ui);
        var multi = LibraryCardView.Create(
            Entry("Multi", progress: Progress(MediaBannerProgressState.InProgress, 3, season: 2, percent: 10)),
            LibraryLanguagePreference.None,
            Ui);

        Assert.AreEqual("Episode 8", single.StatusText);
        Assert.AreEqual(72, single.ProgressPercent);
        Assert.AreEqual("72%", single.ProgressText);
        Assert.AreEqual("Continue watching", single.Action?.Label);
        Assert.AreEqual("/Library/Episode/8", single.Action?.Url);
        Assert.AreEqual("S2 E3", multi.StatusText);
        Assert.AreEqual("S", single.Initial);
    }

    [TestMethod]
    public void NotStartedCompletedAndUnplayableCardsSayOnlyWhatIsUseful()
    {
        var fresh = LibraryCardView.Create(
            Entry("Fresh", progress: Progress(MediaBannerProgressState.NotStarted)), LibraryLanguagePreference.None, Ui);
        var done = LibraryCardView.Create(
            Entry("Done", progress: Progress(MediaBannerProgressState.Completed, 1, percent: 100)), LibraryLanguagePreference.None, Ui);
        var empty = LibraryCardView.Create(
            Entry("Empty", year: 2019, playable: 0, progress: null), LibraryLanguagePreference.None, Ui);

        Assert.AreEqual("Not started", fresh.StatusText);
        Assert.IsNull(fresh.ProgressPercent, "A title that was not started has no bar.");
        Assert.AreEqual("Start watching", fresh.Action?.Label);
        Assert.AreEqual("Completed", done.StatusText);
        Assert.AreEqual(100, done.ProgressPercent);
        Assert.AreEqual("Watch again", done.Action?.Label);
        Assert.AreEqual("2019", empty.StatusText);
        Assert.IsNull(empty.Action, "Nothing playable means no menu action.");
        Assert.AreEqual("Not available", empty.Availability?.Label);
    }

    [TestMethod]
    public void LanguageLineShowsThePreferredLanguageFirstAndFallsBackToWhatExists()
    {
        var preference = LibraryLanguagePreference.From("de", "de");
        var hasGerman = LibraryCardView.Create(
            Entry("A", audio: ["ja", "en", "de", "fr", "es"], subtitles: ["en", "de"]), preference, Ui);
        var fallback = LibraryCardView.Create(Entry("B", audio: ["ja"], subtitles: ["en"]), preference, Ui);
        var none = LibraryCardView.Create(Entry("C"), preference, Ui);

        Assert.AreEqual("DE", hasGerman.Audio.Shown[0].Code);
        Assert.IsTrue(hasGerman.Audio.Shown[0].IsPreferred);
        Assert.AreEqual(3, hasGerman.Audio.Shown.Count);
        Assert.AreEqual(2, hasGerman.Audio.More);
        Assert.AreEqual("DE", hasGerman.Subtitles.Shown[0].Code);
        Assert.IsFalse(hasGerman.PreferredMissing);

        Assert.AreEqual("JA", fallback.Audio.Shown[0].Code);
        Assert.IsFalse(fallback.Audio.Shown[0].IsPreferred);
        Assert.IsTrue(fallback.PreferredMissing, "The preferred language is unavailable, so the fallback languages are shown as such.");

        Assert.IsFalse(none.HasLanguages);
        Assert.IsFalse(none.PreferredMissing, "Without any language there is nothing to call a fallback.");
    }

    [TestMethod]
    public void RequestedCardNamesTheStageOfTheRequest()
    {
        var card = LibraryCardView.Create(
            Entry("R", playable: 0, request: AcquisitionRequestStatus.Downloading), LibraryLanguagePreference.None, Ui);

        Assert.AreEqual(LibraryAvailabilityState.Requested, card.Availability?.State);
        Assert.AreEqual("Downloading", card.Availability?.Label);
    }

    [TestMethod]
    public void EverySortFilterAndAvailabilityKeyExistsInTheCatalog()
    {
        foreach (var sort in LibraryBrowse.Sorts)
        {
            Assert.IsTrue(UiTranslationResources.TryGet(LibraryBrowse.SortKey(sort), out _), sort.ToString());
        }

        foreach (var state in LibraryBrowse.ProgressStates)
        {
            Assert.IsTrue(UiTranslationResources.TryGet(LibraryBrowse.ProgressKey(state), out _), state.ToString());
        }

        foreach (var state in LibraryBrowse.AvailabilityStates)
        {
            Assert.IsTrue(UiTranslationResources.TryGet(LibraryBrowse.AvailabilityKey(state), out _), state.ToString());
        }

        foreach (var status in Enum.GetValues<MediaReleaseStatus>())
        {
            Assert.IsTrue(UiTranslationResources.TryGet(LibraryBrowse.StatusKey(status), out _), status.ToString());
        }
    }

    private static string Titles(
        IEnumerable<LibraryCardEntry> entries,
        string query,
        LibraryLanguagePreference? preference = null) =>
        string.Join(
            ',',
            LibraryBrowse.Apply(entries, Parse(query), preference ?? LibraryLanguagePreference.None)
                .Select(x => x.Card.Title));
}
