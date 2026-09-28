using Jularr.Web.Features.Subtitles;

namespace Jularr.Tests;

// Missing-subtitle detection (#526): combines a resolved language profile with the embedded
// (media-inventory) and external (imported SubtitleTrack) tracks already known for an episode.
[TestClass]
public sealed class SubtitleCompletenessServiceTests
{
    [TestMethod]
    public async Task EpisodeWithNoTracksAtAllIsMissingEveryWantedLanguage()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var (_, episode, _, _) = await fixture.AddEpisodeAsync();
        var profiles = new SubtitleLanguageProfileService(fixture.Db);
        var completeness = new SubtitleCompletenessService(fixture.Db, profiles);

        var result = await completeness.GetEpisodeCompletionAsync(episode.Id, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(SubtitleProfileCompletionStatus.Missing, result!.Status);
        Assert.AreEqual(1, result.Missing.Count);
        Assert.AreEqual("ja", result.Missing[0].LanguageTag);
    }

    [TestMethod]
    public async Task EmbeddedTextSubtitleInTheWantedLanguageSatisfiesIt()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var (_, episode, media, _) = await fixture.AddEpisodeAsync();
        await fixture.AddEmbeddedSubtitleAsync(media, "eng", isForced: false);

        var profiles = new SubtitleLanguageProfileService(fixture.Db);
        await profiles.UpsertAsync(
            null, "English", [new SubtitleLanguageProfileItemInput("en", false, false)], null, CancellationToken.None);
        var completeness = new SubtitleCompletenessService(fixture.Db, profiles);

        var result = await completeness.GetEpisodeCompletionAsync(episode.Id, CancellationToken.None);

        Assert.AreEqual(SubtitleProfileCompletionStatus.Complete, result!.Status);
        Assert.AreEqual(0, result.Missing.Count);
        Assert.IsTrue(result.Items.Single().Satisfied);
    }

    [TestMethod]
    public async Task ImageOnlySubtitleStreamsDoNotSatisfyAWantedLanguage()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var (_, episode, media, _) = await fixture.AddEpisodeAsync();
        await fixture.AddEmbeddedSubtitleAsync(media, "eng", isForced: false, codec: "hdmv_pgs_subtitle");

        var profiles = new SubtitleLanguageProfileService(fixture.Db);
        await profiles.UpsertAsync(
            null, "English", [new SubtitleLanguageProfileItemInput("en", false, false)], null, CancellationToken.None);
        var completeness = new SubtitleCompletenessService(fixture.Db, profiles);

        var result = await completeness.GetEpisodeCompletionAsync(episode.Id, CancellationToken.None);

        Assert.AreEqual(SubtitleProfileCompletionStatus.Missing, result!.Status);
    }

    [TestMethod]
    public async Task ExternallyImportedTrackSatisfiesTheMatchingWantedLanguage()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var (_, episode, _, _) = await fixture.AddEpisodeAsync();
        await fixture.AddExternalTrackAsync(episode, "de");

        var profiles = new SubtitleLanguageProfileService(fixture.Db);
        await profiles.UpsertAsync(
            null, "German", [new SubtitleLanguageProfileItemInput("de", false, false)], null, CancellationToken.None);
        var completeness = new SubtitleCompletenessService(fixture.Db, profiles);

        var result = await completeness.GetEpisodeCompletionAsync(episode.Id, CancellationToken.None);

        Assert.AreEqual(SubtitleProfileCompletionStatus.Complete, result!.Status);
    }

    [TestMethod]
    public async Task ForcedWantedItemIsNotSatisfiedByANonForcedTrackInTheSameLanguage()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var (_, episode, media, _) = await fixture.AddEpisodeAsync();
        await fixture.AddEmbeddedSubtitleAsync(media, "eng", isForced: false);

        var profiles = new SubtitleLanguageProfileService(fixture.Db);
        await profiles.UpsertAsync(
            null, "EnglishForced", [new SubtitleLanguageProfileItemInput("en", true, false)], null, CancellationToken.None);
        var completeness = new SubtitleCompletenessService(fixture.Db, profiles);

        var result = await completeness.GetEpisodeCompletionAsync(episode.Id, CancellationToken.None);

        Assert.AreEqual(SubtitleProfileCompletionStatus.Missing, result!.Status);
    }

    [TestMethod]
    public async Task SdhWantedItemIsSatisfiedByAnEmbeddedStreamTitledSdh()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var (_, episode, media, _) = await fixture.AddEpisodeAsync();
        await fixture.AddEmbeddedSubtitleAsync(media, "eng", isForced: false, title: "English SDH");

        var profiles = new SubtitleLanguageProfileService(fixture.Db);
        await profiles.UpsertAsync(
            null, "EnglishSdh", [new SubtitleLanguageProfileItemInput("en", false, true)], null, CancellationToken.None);
        var completeness = new SubtitleCompletenessService(fixture.Db, profiles);

        var result = await completeness.GetEpisodeCompletionAsync(episode.Id, CancellationToken.None);

        Assert.AreEqual(SubtitleProfileCompletionStatus.Complete, result!.Status);
    }

    [TestMethod]
    public async Task CutoffMetOnTheTopPriorityLanguageReportsCutoffMetNotMissing()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var (_, episode, media, _) = await fixture.AddEpisodeAsync();
        await fixture.AddEmbeddedSubtitleAsync(media, "jpn", isForced: false);

        var profiles = new SubtitleLanguageProfileService(fixture.Db);
        await profiles.UpsertAsync(
            null,
            "JapaneseCutoff",
            [
                new SubtitleLanguageProfileItemInput("ja", false, false),
                new SubtitleLanguageProfileItemInput("en", false, false)
            ],
            cutoffPosition: 0,
            CancellationToken.None);
        var completeness = new SubtitleCompletenessService(fixture.Db, profiles);

        var result = await completeness.GetEpisodeCompletionAsync(episode.Id, CancellationToken.None);

        Assert.AreEqual(SubtitleProfileCompletionStatus.CutoffMet, result!.Status);
        Assert.AreEqual(0, result.Missing.Count);
    }

    [TestMethod]
    public async Task GetCompletionsListsEpisodesInLibraryOrder()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var (animeA, episodeA1, _, _) = await fixture.AddEpisodeAsync("Alpha", episodeNumber: 1);
        var (_, episodeA2, _, _) = await fixture.AddEpisodeAsync("Alpha", episodeNumber: 2);
        episodeA2.AnimeId = animeA.Id;
        await fixture.Db.SaveChangesAsync();

        var profiles = new SubtitleLanguageProfileService(fixture.Db);
        var completeness = new SubtitleCompletenessService(fixture.Db, profiles);

        var results = await completeness.GetCompletionsAsync(10, CancellationToken.None);

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual(episodeA1.Id, results[0].EpisodeId);
        Assert.AreEqual(episodeA2.Id, results[1].EpisodeId);
    }
}
