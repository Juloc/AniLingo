using Jularr.Web.Features.Subtitles;
using Jularr.Web.Features.Vocabulary;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

// Owner-only manual search scaffold (#526): with zero registered ISubtitleProvider (today's real
// state - no credential-free provider ships yet) search returns no results without erroring; with
// a fake in-test provider, results aggregate and a chosen result imports through the existing
// subtitle-import path.
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

        Assert.IsFalse(service.HasProviders);

        var outcomes = await service.SearchAsync(
            new SubtitleSearchRequest(Guid.NewGuid(), "Frieren", 1, 1, "en", false, false),
            CancellationToken.None);

        Assert.AreEqual(0, outcomes.Count);
    }

    [TestMethod]
    public async Task AFailingProviderDoesNotHideResultsFromWorkingOnes()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var goodProvider = new FakeSubtitleProvider("good", "Good Provider");
        goodProvider.QueueResult(new SubtitleSearchResult(
            "good", "token-1", "en", false, false, "Release.Name", "uploader", 0.9, null));
        var badProvider = new FakeSubtitleProvider("bad", "Bad Provider") { ThrowOnSearch = true };

        var service = new SubtitleManualSearchService([goodProvider, badProvider], CreateImportService(fixture));

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

        var service = new SubtitleManualSearchService([provider], CreateImportService(fixture));

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

    private sealed class FakeSubtitleProvider(string id, string displayName) : ISubtitleProvider
    {
        private readonly List<SubtitleSearchResult> queuedResults = [];
        private readonly Dictionary<string, SubtitleDownloadResult> queuedDownloads = new(StringComparer.Ordinal);

        public string Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public bool ThrowOnSearch { get; init; }

        public void QueueResult(SubtitleSearchResult result) => queuedResults.Add(result);

        public void QueueDownload(SubtitleSearchResult result, SubtitleDownloadResult download) =>
            queuedDownloads[result.ResultToken] = download;

        public Task<IReadOnlyList<SubtitleSearchResult>> SearchAsync(
            SubtitleSearchRequest request, CancellationToken cancellationToken) =>
            ThrowOnSearch
                ? throw new InvalidOperationException("Provider unavailable.")
                : Task.FromResult<IReadOnlyList<SubtitleSearchResult>>(queuedResults);

        public Task<SubtitleDownloadResult> DownloadAsync(
            SubtitleSearchResult result, CancellationToken cancellationToken) =>
            Task.FromResult(queuedDownloads.TryGetValue(result.ResultToken, out var download)
                ? download
                : new SubtitleDownloadResult(false, null, null, "No download queued."));
    }

    private sealed class NoMorphology : IJapaneseMorphology
    {
        public IReadOnlyList<JapaneseMorphToken> Analyze(string text) => [];
    }
}
