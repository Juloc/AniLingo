using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Library;

namespace Jularr.Tests;

/// <summary>The pure decisions behind the Anime/Series detail page (docs/mockups/anime-series-detail/SPEC.md).</summary>
[TestClass]
public sealed class AnimeDetailViewTests
{
    private sealed record Ep(int Season, int Number, bool Watched = false, int? Resume = null);

    private static IReadOnlySet<string> Set(params string[] values) => new HashSet<string>(values);

    private static AnimeEpisodeMediaFacts File(params string[] audioThenSubs) =>
        new(true, 24, Set(audioThenSubs.Where(x => !x.StartsWith("sub:", StringComparison.Ordinal)).ToArray()),
            Set(audioThenSubs.Where(x => x.StartsWith("sub:", StringComparison.Ordinal)).Select(x => x[4..]).ToArray()));

    [TestMethod]
    public void SortAndLayoutParseOnlyKnownValuesAndKeepTheDefaultOutOfTheAddress()
    {
        Assert.AreEqual(AnimeEpisodeSort.EpisodeDescending, AnimeDetailView.ParseSort("DESC"));
        Assert.AreEqual(AnimeEpisodeSort.UnwatchedFirst, AnimeDetailView.ParseSort("unwatched"));
        Assert.AreEqual(AnimeEpisodeSort.EpisodeAscending, AnimeDetailView.ParseSort("nonsense"));
        Assert.AreEqual(AnimeEpisodeSort.EpisodeAscending, AnimeDetailView.ParseSort(null));
        Assert.IsNull(AnimeDetailView.SortName(AnimeEpisodeSort.EpisodeAscending));
        Assert.AreEqual("desc", AnimeDetailView.SortName(AnimeEpisodeSort.EpisodeDescending));

        Assert.AreEqual(AnimeEpisodeLayout.List, AnimeDetailView.ParseLayout("list"));
        Assert.AreEqual(AnimeEpisodeLayout.Grid, AnimeDetailView.ParseLayout("anything"));
        Assert.IsNull(AnimeDetailView.LayoutName(AnimeEpisodeLayout.Grid));
        Assert.AreEqual("list", AnimeDetailView.LayoutName(AnimeEpisodeLayout.List));
    }

    [TestMethod]
    public void EpisodesSortByNumberDescendingOrUnwatchedFirstKeepingNumberOrderInATie()
    {
        Ep[] episodes = [new(1, 1, Watched: true), new(1, 2), new(1, 3, Watched: true), new(1, 4)];

        CollectionAssert.AreEqual(
            new[] { 4, 3, 2, 1 },
            AnimeDetailView.Sort(episodes, AnimeEpisodeSort.EpisodeDescending, x => x.Number, x => x.Watched).Select(x => x.Number).ToArray());
        CollectionAssert.AreEqual(
            new[] { 2, 4, 1, 3 },
            AnimeDetailView.Sort(episodes, AnimeEpisodeSort.UnwatchedFirst, x => x.Number, x => x.Watched).Select(x => x.Number).ToArray());
        CollectionAssert.AreEqual(
            new[] { 1, 2, 3, 4 },
            AnimeDetailView.Sort(episodes.Reverse(), AnimeEpisodeSort.EpisodeAscending, x => x.Number, x => x.Watched).Select(x => x.Number).ToArray());
    }

    [TestMethod]
    public void SeasonRailListsRegularSeasonsFirstAndTheSpecialsLastWithTheirEpisodeCounts()
    {
        var entries = AnimeDetailView.SeasonEntries([0, 2, 1, 1, 2, 2, 1]);

        CollectionAssert.AreEqual(new[] { "s1", "s2", "s0" }, entries.Select(x => x.Key).ToArray());
        CollectionAssert.AreEqual(new[] { 3, 3, 1 }, entries.Select(x => x.EpisodeCount).ToArray());
        Assert.IsTrue(entries[2].IsSpecials);
        Assert.IsFalse(entries[0].IsSpecials);
        Assert.AreEqual(0, AnimeDetailView.SeasonEntries([]).Count);
    }

    [TestMethod]
    public void GroupEntriesCarryTheOwnersGroupNamesInOrder()
    {
        var entries = AnimeDetailView.GroupEntries([("Cour 1", 12), ("Cour 2", 13)]);

        CollectionAssert.AreEqual(new[] { "g0", "g1" }, entries.Select(x => x.Key).ToArray());
        CollectionAssert.AreEqual(new[] { "Cour 1", "Cour 2" }, entries.Select(x => x.GroupName).ToArray());
        Assert.IsNull(entries[0].SeasonNumber);
    }

    [TestMethod]
    public void TheSelectedEntryIsTheRequestedOneThenTheOneWithTheNextEpisodeThenTheFirst()
    {
        var entries = AnimeDetailView.SeasonEntries([1, 2, 0]);

        Assert.AreEqual("s2", AnimeDetailView.SelectEntry(entries, "s2", "s1")!.Key);
        Assert.AreEqual("s1", AnimeDetailView.SelectEntry(entries, "s9", "s1")!.Key);
        Assert.AreEqual("s2", AnimeDetailView.SelectEntry(entries, null, "s2")!.Key);
        Assert.AreEqual("s1", AnimeDetailView.SelectEntry(entries, "bad", null)!.Key);
        Assert.IsNull(AnimeDetailView.SelectEntry([], "s1", "s1"));
    }

    [TestMethod]
    public void PrimaryActionResumesAnEpisodeInProgressBeforeAnythingElse()
    {
        Ep[] episodes = [new(1, 1, Watched: true), new(1, 2, Resume: 40), new(1, 3)];

        var (episode, action) = AnimeDetailView.ChoosePrimary(episodes, x => x.Season, x => x.Watched, x => x.Resume);

        Assert.AreEqual(2, episode!.Number);
        Assert.AreEqual(AnimePrimaryAction.Continue, action);
    }

    [TestMethod]
    public void PrimaryActionStartsAtTheFirstRegularEpisodeBeforeTheSpecials()
    {
        Ep[] episodes = [new(0, 1), new(1, 1), new(1, 2)];

        var (episode, action) = AnimeDetailView.ChoosePrimary(episodes, x => x.Season, x => x.Watched, x => x.Resume);

        Assert.AreEqual((1, 1), (episode!.Season, episode.Number));
        Assert.AreEqual(AnimePrimaryAction.Start, action);
    }

    [TestMethod]
    public void PrimaryActionContinuesAfterWatchedEpisodesAndWatchesAgainWhenAllAreWatched()
    {
        Ep[] partly = [new(1, 1, Watched: true), new(1, 2)];
        var (next, action) = AnimeDetailView.ChoosePrimary(partly, x => x.Season, x => x.Watched, x => x.Resume);
        Assert.AreEqual(2, next!.Number);
        Assert.AreEqual(AnimePrimaryAction.Continue, action);

        Ep[] all = [new(1, 1, Watched: true), new(1, 2, Watched: true)];
        var (again, againAction) = AnimeDetailView.ChoosePrimary(all, x => x.Season, x => x.Watched, x => x.Resume);
        Assert.AreEqual(1, again!.Number);
        Assert.AreEqual(AnimePrimaryAction.WatchAgain, againAction);

        var (none, _) = AnimeDetailView.ChoosePrimary(Array.Empty<Ep>(), x => x.Season, x => x.Watched, x => x.Resume);
        Assert.IsNull(none);
    }

    [TestMethod]
    public void AnEpisodeWithAFileIsAvailableWithoutAPreferenceOrWhenThePreferredLanguageIsThere()
    {
        Assert.AreEqual(
            AnimeEpisodeAvailability.Available,
            AnimeDetailView.Availability(File("ja", "sub:en"), null, null, null));
        Assert.AreEqual(
            AnimeEpisodeAvailability.Available,
            AnimeDetailView.Availability(File("ja", "sub:en"), null, "ja", "de"));
        Assert.AreEqual(
            AnimeEpisodeAvailability.Available,
            AnimeDetailView.Availability(File("en", "sub:de"), null, "ja", "de"));
        // A file whose languages are unknown cannot be judged against a preference.
        Assert.AreEqual(
            AnimeEpisodeAvailability.Available,
            AnimeDetailView.Availability(File(), null, "ja", "en"));
    }

    [TestMethod]
    public void AnEpisodeInOtherLanguagesOnlyIsFlaggedButTurningSubtitlesOffIsNotAPreference()
    {
        Assert.AreEqual(
            AnimeEpisodeAvailability.OtherLanguageOnly,
            AnimeDetailView.Availability(File("de", "sub:fr"), null, "ja", "en"));
        Assert.AreEqual(
            AnimeEpisodeAvailability.OtherLanguageOnly,
            AnimeDetailView.Availability(File("de"), null, "ja", null));
        Assert.AreEqual(
            AnimeEpisodeAvailability.Available,
            AnimeDetailView.Availability(File("de"), null, null, "off"));
    }

    [TestMethod]
    public void AnEpisodeWithoutAFileShowsWhatTheOpenRequestIsDoing()
    {
        var none = AnimeEpisodeMediaFacts.None;

        Assert.AreEqual(AnimeEpisodeAvailability.Unavailable, AnimeDetailView.Availability(none, null, null, null));
        Assert.AreEqual(AnimeEpisodeAvailability.Requested, AnimeDetailView.Availability(none, AcquisitionRequestStatus.Pending, null, null));
        Assert.AreEqual(AnimeEpisodeAvailability.Requested, AnimeDetailView.Availability(none, AcquisitionRequestStatus.Approved, null, null));
        Assert.AreEqual(AnimeEpisodeAvailability.Requested, AnimeDetailView.Availability(none, AcquisitionRequestStatus.Searching, null, null));
        Assert.AreEqual(AnimeEpisodeAvailability.Downloading, AnimeDetailView.Availability(none, AcquisitionRequestStatus.Downloading, null, null));
        Assert.AreEqual(AnimeEpisodeAvailability.Importing, AnimeDetailView.Availability(none, AcquisitionRequestStatus.Importing, null, null));
        Assert.AreEqual(AnimeEpisodeAvailability.Unavailable, AnimeDetailView.Availability(none, AcquisitionRequestStatus.Failed, null, null));
    }

    [TestMethod]
    public void LanguageChipsPutThePreferredFirstUpperCaseThemAndCapTheList()
    {
        var chips = AnimeDetailView.Chips(Set("de", "en", "ja", "fr", "es"), "ja");

        Assert.AreEqual(AnimeDetailView.MaxLanguageChips, chips.Shown.Count);
        Assert.AreEqual("JA", chips.Shown[0].Code);
        Assert.IsTrue(chips.Shown[0].IsPreferred);
        Assert.IsFalse(chips.Shown[1].IsPreferred);
        Assert.AreEqual(2, chips.More);
        Assert.IsFalse(AnimeDetailView.Chips(Set(), "ja").Any);
    }

    [TestMethod]
    public void RuntimeRoundsTheFileDurationToWholeMinutesAndIsUnknownWithoutOne()
    {
        Assert.AreEqual(24, AnimeDetailView.RuntimeMinutes(1440));
        Assert.AreEqual(24, AnimeDetailView.RuntimeMinutes(1449));
        Assert.AreEqual(1, AnimeDetailView.RuntimeMinutes(5));
        Assert.IsNull(AnimeDetailView.RuntimeMinutes(0));
        Assert.IsNull(AnimeDetailView.RuntimeMinutes(null));
        Assert.AreEqual("S01 E03", AnimeDetailView.EpisodeCode(1, 3));
    }
}
