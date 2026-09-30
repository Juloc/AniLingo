using Jularr.Web.Features.Providers;
using Jularr.Web.Features.Subtitles;
using Jularr.Web.Features.Vocabulary;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

// Owner-only manual search scaffold (#526): with zero configured providers (an unconfigured
// server) search returns no results without erroring; with a fake in-test provider, results
// aggregate and a chosen result imports through the existing subtitle-import path.
[TestClass]
public sealed class SubtitleManualSearchServiceTests
{
    private static SubtitleImportService CreateImportService(SubtitleProfileFixture fixture) =>
        new(
            fixture.Db,
            new VocabularyService(fixture.Db, new JapaneseTermExtractor(new NoMorphology()), new JapaneseDictionary()));

    [TestMethod]
    public async Task WithNoProvidersSearchReturnsNoOutcomesAndReportsNoneConfigured()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var service = new SubtitleManualSearchService([], CreateImportService(fixture));

        Assert.AreEqual(0, (await service.GetProvidersAsync(CancellationToken.None)).Count);

        var outcomes = await service.SearchAsync(
            new SubtitleSearchRequest(Guid.NewGuid(), "Frieren", 1, 1, "en", false, false),
            CancellationToken.None);

        Assert.AreEqual(0, outcomes.Count);
    }

    [TestMethod]
    public async Task ASourceWithoutAConfiguredProviderContributesNothing()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var service = new SubtitleManualSearchService(
            [new FakeSource(null), new FakeSource(new FakeSubtitleProvider("good", "Good Provider"))],
            CreateImportService(fixture));

        var providers = await service.GetProvidersAsync(CancellationToken.None);

        Assert.AreEqual(1, providers.Count);
        Assert.AreEqual("good", providers[0].Id);
    }

    [TestMethod]
    public async Task AFailingProviderDoesNotHideResultsFromWorkingOnes()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var goodProvider = new FakeSubtitleProvider("good", "Good Provider");
        goodProvider.QueueResult(new SubtitleSearchResult(
            "good", "token-1", "en", false, false, "Release.Name", "uploader", 0.9, null));
        var badProvider = new FakeSubtitleProvider("bad", "Bad Provider") { SearchException = new InvalidOperationException("Provider unavailable.") };

        var service = new SubtitleManualSearchService(
            [new FakeSource(goodProvider), new FakeSource(badProvider)],
            CreateImportService(fixture));

        var outcomes = await service.SearchAsync(
            new SubtitleSearchRequest(Guid.NewGuid(), "Frieren", 1, 1, "en", false, false),
            CancellationToken.None);

        Assert.AreEqual(2, outcomes.Count);
        var good = outcomes.Single(o => o.ProviderId == "good");
        var bad = outcomes.Single(o => o.ProviderId == "bad");
        Assert.AreEqual(1, good.Results.Count);
        Assert.IsNull(good.Error);
        Assert.AreEqual(0, bad.Results.Count);
        Assert.IsNotNull(bad.Error);
    }

    [TestMethod]
    public async Task RateLimitedAndUnavailableProvidersAreReportedAsOutcomesNotThrown()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var limited = new FakeSubtitleProvider("limited", "Limited")
        {
            SearchException = new ProviderRateLimitedException("limited", TimeSpan.FromSeconds(30))
        };
        var paused = new FakeSubtitleProvider("paused", "Paused")
        {
            SearchException = new ProviderUnavailableException("paused")
        };
        var service = new SubtitleManualSearchService(
            [new FakeSource(limited), new FakeSource(paused)],
            CreateImportService(fixture));

        var outcomes = await service.SearchAsync(
            new SubtitleSearchRequest(Guid.NewGuid(), "Frieren", 1, 1, "en", false, false),
            CancellationToken.None);

        StringAssert.Contains(outcomes.Single(o => o.ProviderId == "limited").Error, "rate limited");
        StringAssert.Contains(outcomes.Single(o => o.ProviderId == "paused").Error, "unavailable");
    }

    [TestMethod]
    public async Task ACancelledSearchStillPropagatesInsteadOfBecomingAnOutcome()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var provider = new FakeSubtitleProvider("slow", "Slow") { SearchException = new OperationCanceledException() };
        var service = new SubtitleManualSearchService([new FakeSource(provider)], CreateImportService(fixture));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.SearchAsync(
            new SubtitleSearchRequest(Guid.NewGuid(), "Frieren", 1, 1, "en", false, false),
            cancellation.Token));
    }

    [TestMethod]
    public async Task ImportingAChosenResultWritesATrackThroughTheExistingImportPath()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var (_, episode, _, _) = await fixture.AddEpisodeAsync();

        const string srt = """
            1
            00:00:01,000 --> 00:00:02,000
            Hello world
            """;
        var provider = new FakeSubtitleProvider("good", "Good Provider");
        var result = new SubtitleSearchResult(
            "good", "token-1", "en", false, false, "Release.Name", "uploader", 0.9, null);
        provider.QueueDownload(result, new SubtitleDownloadResult(true, "srt", srt, null));

        var service = new SubtitleManualSearchService([new FakeSource(provider)], CreateImportService(fixture));

        var downloadResult = await service.ImportAsync(episode.Id, result, CancellationToken.None);

        Assert.IsTrue(downloadResult.Success);
        var track = await fixture.Db.SubtitleTracks.SingleAsync(x => x.EpisodeId == episode.Id);
        Assert.AreEqual("en", track.Language);
        Assert.IsFalse(track.Forced);
        Assert.IsFalse(track.Sdh);
        Assert.AreEqual(1, await fixture.Db.SubtitleCues.CountAsync(x => x.SubtitleTrackId == track.Id));
    }

    [TestMethod]
    public async Task ImportingFromAProviderThatIsNoLongerRegisteredFailsCleanly()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var (_, episode, _, _) = await fixture.AddEpisodeAsync();
        var service = new SubtitleManualSearchService([], CreateImportService(fixture));
        var result = new SubtitleSearchResult(
            "missing", "token", "en", false, false, "Release.Name", null, null, null);

        var downloadResult = await service.ImportAsync(episode.Id, result, CancellationToken.None);

        Assert.IsFalse(downloadResult.Success);
    }

    [TestMethod]
    public async Task AProviderDownloadFailureBecomesAFailedResultInsteadOfAnException()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var (_, episode, _, _) = await fixture.AddEpisodeAsync();
        var provider = new FakeSubtitleProvider("good", "Good Provider")
        {
            DownloadException = new SubtitleProviderException("OpenSubtitles download quota reached.")
        };
        var service = new SubtitleManualSearchService([new FakeSource(provider)], CreateImportService(fixture));
        var result = new SubtitleSearchResult("good", "token-1", "en", false, false, "Release.Name", null, null, null);

        var downloadResult = await service.ImportAsync(episode.Id, result, CancellationToken.None);

        Assert.IsFalse(downloadResult.Success);
        StringAssert.Contains(downloadResult.Message, "quota");
        Assert.AreEqual(0, await fixture.Db.SubtitleTracks.CountAsync());
    }

    [TestMethod]
    public async Task AnUnparsableSubtitleFailsTheImportWithoutWritingATrack()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var (_, episode, _, _) = await fixture.AddEpisodeAsync();
        var provider = new FakeSubtitleProvider("good", "Good Provider");
        var result = new SubtitleSearchResult("good", "token-1", "en", false, false, "Release.Name", null, null, null);
        provider.QueueDownload(result, new SubtitleDownloadResult(true, "srt", "this is not a subtitle file", null));
        var service = new SubtitleManualSearchService([new FakeSource(provider)], CreateImportService(fixture));

        var downloadResult = await service.ImportAsync(episode.Id, result, CancellationToken.None);

        Assert.IsFalse(downloadResult.Success);
        Assert.AreEqual(0, await fixture.Db.SubtitleTracks.CountAsync());
    }

    // #612: the manual import upserts by Path like every other source, so re-importing a provider
    // result (or importing it for another episode) reuses the single row that owns the Path instead
    // of inserting a second one (IX_SubtitleTracks_Path, 23505).
    [TestMethod]
    public async Task ImportingTheSameProviderResultTwiceOrForAnotherEpisodeNeverDuplicatesThePath()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var (_, first, _, _) = await fixture.AddEpisodeAsync(episodeNumber: 1);
        var (_, second, _, _) = await fixture.AddEpisodeAsync(episodeNumber: 2);
        var service = CreateImportService(fixture);
        var stamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        const string srt = """
            1
            00:00:01,000 --> 00:00:02,000
            Hello world
            """;

        var firstId = await service.ImportManualSearchResultAsync(
            first.Id, "good", "token-1", "en", false, false, "srt", stamp, srt, CancellationToken.None);
        var againId = await service.ImportManualSearchResultAsync(
            first.Id, "good", "token-1", "en", false, false, "srt", stamp.AddMinutes(1), srt, CancellationToken.None);
        var movedId = await service.ImportManualSearchResultAsync(
            second.Id, "good", "token-1", "en", false, false, "srt", stamp.AddMinutes(2), srt, CancellationToken.None);

        Assert.AreEqual(firstId, againId);
        Assert.AreEqual(firstId, movedId);
        var track = await fixture.Db.SubtitleTracks.AsNoTracking().SingleAsync();
        Assert.AreEqual(second.Id, track.EpisodeId);
        Assert.AreEqual(1, await fixture.Db.SubtitleCues.CountAsync(x => x.SubtitleTrackId == track.Id));
    }

    private sealed class FakeSource(ISubtitleProvider? provider) : ISubtitleProviderSource
    {
        public Task<ISubtitleProvider?> GetProviderAsync(CancellationToken cancellationToken) =>
            Task.FromResult(provider);
    }

    private sealed class FakeSubtitleProvider(string id, string displayName) : ISubtitleProvider
    {
        private readonly List<SubtitleSearchResult> queuedResults = [];
        private readonly Dictionary<string, SubtitleDownloadResult> queuedDownloads = new(StringComparer.Ordinal);

        public string Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public Exception? SearchException { get; init; }
        public Exception? DownloadException { get; init; }

        public void QueueResult(SubtitleSearchResult result) => queuedResults.Add(result);

        public void QueueDownload(SubtitleSearchResult result, SubtitleDownloadResult download) =>
            queuedDownloads[result.ResultToken] = download;

        public Task<IReadOnlyList<SubtitleSearchResult>> SearchAsync(
            SubtitleSearchRequest request, CancellationToken cancellationToken) =>
            SearchException is not null
                ? throw SearchException
                : Task.FromResult<IReadOnlyList<SubtitleSearchResult>>(queuedResults);

        public Task<SubtitleDownloadResult> DownloadAsync(
            SubtitleSearchResult result, CancellationToken cancellationToken) =>
            DownloadException is not null
                ? throw DownloadException
                : Task.FromResult(queuedDownloads.TryGetValue(result.ResultToken, out var download)
                    ? download
                    : new SubtitleDownloadResult(false, null, null, "No download queued."));
    }

    private sealed class NoMorphology : IJapaneseMorphology
    {
        public IReadOnlyList<JapaneseMorphToken> Analyze(string text) => [];
    }
}
