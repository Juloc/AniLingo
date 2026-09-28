using Jularr.Web.Features.Localization;
using Jularr.Web.Features.MediaFacts;
using Jularr.Web.Ui;

namespace Jularr.Tests;

[TestClass]
public sealed class MediaFactsStripModelTests
{
    private static readonly UiTextBundle Ui = UiTextBundle.English;

    [TestMethod]
    public void FactsRenderStatusAndCountsForAnime()
    {
        var facts = new MediaFacts(
            MediaBannerKind.Anime,
            MediaReleaseStatus.Ongoing,
            PrimaryUnitCount: 24,
            SecondaryUnitCount: 2,
            RuntimeMinutes: 24,
            ReleaseYear: 2024,
            Languages: []);

        var model = MediaFactsStripModel.Create(facts, Ui);

        Assert.IsTrue(model.HasFacts);
        Assert.IsNotNull(model.Status);
        Assert.AreEqual(MediaReleaseStatus.Ongoing, model.Status!.Status);
        Assert.AreEqual("Ongoing", model.Status.Label);
        CollectionAssert.AreEqual(
            new[] { "2024", "24 episodes", "2 seasons", "24 min" },
            model.Chips.ToArray());
    }

    [TestMethod]
    public void ChapterAndVolumeCountsAreUsedForReadingKinds()
    {
        var manga = MediaFactsStripModel.Create(
            new MediaFacts(MediaBannerKind.Manga, null, 40, 4, null, null, []),
            Ui);
        CollectionAssert.AreEqual(new[] { "40 chapters", "4 volumes" }, manga.Chips.ToArray());

        var novel = MediaFactsStripModel.Create(
            new MediaFacts(MediaBannerKind.LightNovel, null, 12, 1, null, null, []),
            Ui);
        CollectionAssert.AreEqual(new[] { "12 chapters", "1 volumes" }, novel.Chips.ToArray());
    }

    [TestMethod]
    public void ShowFactsFalseSuppressesStatusAndChipsButKeepsLanguages()
    {
        var facts = new MediaFacts(
            MediaBannerKind.Anime,
            MediaReleaseStatus.Finished,
            24,
            1,
            24,
            2019,
            [new MediaFactsLanguageRow("ja", MediaFactsLanguageUsage.Audio, 24, 24)]);

        var model = MediaFactsStripModel.Create(facts, Ui, showFacts: false);

        Assert.IsFalse(model.HasFacts);
        Assert.IsNull(model.Status);
        Assert.AreEqual(0, model.Chips.Count);
        Assert.IsTrue(model.HasLanguages);
    }

    [TestMethod]
    public void ShowLanguagesFalseSuppressesLanguageGroups()
    {
        var facts = new MediaFacts(
            MediaBannerKind.Anime,
            null,
            null,
            null,
            null,
            null,
            [new MediaFactsLanguageRow("ja", MediaFactsLanguageUsage.Audio, 1, 1)]);

        var model = MediaFactsStripModel.Create(facts, Ui, showLanguages: false);

        Assert.IsFalse(model.HasLanguages);
        Assert.AreEqual(0, model.LanguageGroups.Count);
    }

    [TestMethod]
    public void LanguageGroupsAreSplitByUsageAndOrderedByAvailability()
    {
        var facts = new MediaFacts(
            MediaBannerKind.Anime,
            null,
            null,
            null,
            null,
            null,
            [
                new MediaFactsLanguageRow("de", MediaFactsLanguageUsage.Audio, 1, 3),
                new MediaFactsLanguageRow("ja", MediaFactsLanguageUsage.Audio, 3, 3),
                new MediaFactsLanguageRow("en", MediaFactsLanguageUsage.Subtitle, 2, 3)
            ]);

        var model = MediaFactsStripModel.Create(facts, Ui);

        Assert.AreEqual(2, model.LanguageGroups.Count);
        var audio = model.LanguageGroups[0];
        Assert.AreEqual(MediaFactsLanguageUsage.Audio, audio.Usage);
        Assert.AreEqual("Audio", audio.Label);
        CollectionAssert.AreEqual(new[] { "JA", "DE" }, audio.Chips.Select(x => x.Code).ToArray());

        var subtitles = model.LanguageGroups[1];
        Assert.AreEqual(MediaFactsLanguageUsage.Subtitle, subtitles.Usage);
        Assert.AreEqual("EN", subtitles.Chips[0].Code);
    }

    [TestMethod]
    public void PartialCoverageChipsCarryAFractionAndQualifierCompleteOnesDoNot()
    {
        var facts = new MediaFacts(
            MediaBannerKind.Anime,
            null,
            null,
            null,
            null,
            null,
            [
                new MediaFactsLanguageRow("ja", MediaFactsLanguageUsage.Audio, 24, 24),
                new MediaFactsLanguageRow("de", MediaFactsLanguageUsage.Audio, 6, 24)
            ]);

        var model = MediaFactsStripModel.Create(facts, Ui);
        var chips = model.LanguageGroups.Single().Chips;

        var complete = chips.Single(x => x.Code == "JA");
        Assert.AreEqual("24/24", complete.Fraction);
        Assert.IsFalse(complete.IsPartial);
        Assert.IsNull(complete.PartialLabel);

        var partial = chips.Single(x => x.Code == "DE");
        Assert.AreEqual("6/24", partial.Fraction);
        Assert.IsTrue(partial.IsPartial);
        Assert.AreEqual("Partial", partial.PartialLabel);
    }

    [TestMethod]
    public void ASingleKnownUnitShowsNoFraction()
    {
        var facts = new MediaFacts(
            MediaBannerKind.Book,
            null,
            null,
            null,
            null,
            2001,
            [new MediaFactsLanguageRow("en", MediaFactsLanguageUsage.Text, 1, 1)]);

        var model = MediaFactsStripModel.Create(facts, Ui);
        var chip = model.LanguageGroups.Single().Chips.Single();

        Assert.IsNull(chip.Fraction);
        Assert.IsFalse(chip.IsPartial);
    }

    [TestMethod]
    public void EmptyFactsProduceAnEmptyStrip()
    {
        var model = MediaFactsStripModel.Create(MediaFacts.Empty(MediaBannerKind.Book), Ui);

        Assert.IsFalse(model.HasFacts);
        Assert.IsFalse(model.HasLanguages);
    }
}
