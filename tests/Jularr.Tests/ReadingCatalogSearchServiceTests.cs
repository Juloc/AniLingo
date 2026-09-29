using Jularr.Web.Features.ReadingDiscovery;
using Jularr.Web.Features.ReadingSources;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

/// <summary>
/// The combined manual Light Novel search (#477): enable/disable, ranking by relevance and
/// priority, and independent degradation of every source.
/// </summary>
[TestClass]
public sealed class ReadingCatalogSearchServiceTests
{
    private const string AniList = "anilist";
    private const string Syosetu = "syosetu";
    private const string BookWalker = "bookwalker";
    private const string WebNovel = "webnovel";
    private const string Archive = "internetarchive";

    [TestMethod]
    public async Task OnlyEnabledSourcesAreAsked()
    {
        var aniList = new FakeProvider(AniList, Candidate(AniList, "1", "Overlord"));
        var syosetu = new FakeProvider(Syosetu, Candidate(Syosetu, "n1234ab", "Overlord"));
        var service = Service(aniList, syosetu);

        var outcome = await service.SearchLightNovelsAsync(
            OnlyEnabled(AniList),
            "Overlord",
            24,
            CancellationToken.None);

        Assert.AreEqual(1, aniList.Calls);
        Assert.AreEqual(0, syosetu.Calls);
        CollectionAssert.AreEqual(
            new[] { "anilist:1" },
            outcome.Candidates.Select(candidate => candidate.Identity).ToArray());
        Assert.AreEqual(0, outcome.UnavailableProviders.Count);
    }

    [TestMethod]
    public async Task ADisabledSourceIsNeverAskedEvenWhenItWouldFail()
    {
        var blocked = new FakeProvider(WebNovel, throws: new InvalidOperationException("boom"));
        var service = Service(blocked);

        var outcome = await service.SearchLightNovelsAsync(
            OnlyEnabled(),
            "Overlord",
            24,
            CancellationToken.None);

        Assert.AreEqual(0, blocked.Calls);
        Assert.AreEqual(0, outcome.Candidates.Count);
        Assert.AreEqual(0, outcome.UnavailableProviders.Count);
    }

    [TestMethod]
    public async Task ProvidersOutsideTheCatalogAreIgnored()
    {
        var stray = new FakeProvider("not-a-source", Candidate("not-a-source", "1", "Overlord"));
        var service = Service(stray);

        var outcome = await service.SearchLightNovelsAsync(
            ReadingSourceSettingsState.Default,
            "Overlord",
            24,
            CancellationToken.None);

        Assert.AreEqual(0, stray.Calls);
        Assert.AreEqual(0, outcome.Candidates.Count);
    }

    [TestMethod]
    public async Task ConfiguredPriorityOrdersEquallyRelevantResults()
    {
        var aniList = new FakeProvider(AniList, Candidate(AniList, "1", "Overlord"));
        var syosetu = new FakeProvider(Syosetu, Candidate(Syosetu, "n1234ab", "Overlord"));
        var bookWalker = new FakeProvider(BookWalker, Candidate(BookWalker, "series-1", "Overlord"));
        var service = Service(aniList, syosetu, bookWalker);

        var preferSyosetu = await service.SearchLightNovelsAsync(
            Preferences(new()
            {
                [Syosetu] = new(true, 5),
                [AniList] = new(true, 20),
                [BookWalker] = new(true, 30)
            }),
            "Overlord",
            24,
            CancellationToken.None);
        var preferBookWalker = await service.SearchLightNovelsAsync(
            Preferences(new()
            {
                [Syosetu] = new(true, 50),
                [AniList] = new(true, 20),
                [BookWalker] = new(true, 1)
            }),
            "Overlord",
            24,
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { Syosetu, AniList, BookWalker },
            preferSyosetu.Candidates.Select(candidate => candidate.Provider).ToArray());
        CollectionAssert.AreEqual(
            new[] { BookWalker, AniList, Syosetu },
            preferBookWalker.Candidates.Select(candidate => candidate.Provider).ToArray());
    }

    [TestMethod]
    public async Task RelevanceOutranksPriorityAndNoSourceGetsAHiddenBonus()
    {
        // The best-priority source only has a weak match; a lower-priority source has the
        // exact title. The exact match must come first, and a published source gets no
        // built-in head start over a web source with the same relevance.
        var syosetu = new FakeProvider(
            Syosetu,
            Candidate(Syosetu, "n1111aa", "Overlord Side Story Collection"),
            Candidate(Syosetu, "n2222bb", "Overlord"));
        var aniList = new FakeProvider(AniList, Candidate(AniList, "9", "Overlord"));
        var service = Service(aniList, syosetu);

        var outcome = await service.SearchLightNovelsAsync(
            Preferences(new()
            {
                [Syosetu] = new(true, 1),
                [AniList] = new(true, 999)
            }),
            "Overlord",
            24,
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "syosetu:n2222bb", "anilist:9", "syosetu:n1111aa" },
            outcome.Candidates.Select(candidate => candidate.Identity).ToArray());
    }

    [TestMethod]
    public async Task SameTitleFromDifferentSourcesStaysSeparate()
    {
        var aniList = new FakeProvider(AniList, Candidate(AniList, "1", "Mushoku Tensei"));
        var syosetu = new FakeProvider(Syosetu, Candidate(Syosetu, "n9669bk", "Mushoku Tensei"));
        var service = Service(aniList, syosetu);

        var outcome = await service.SearchLightNovelsAsync(
            ReadingSourceSettingsState.Default,
            "Mushoku Tensei",
            24,
            CancellationToken.None);

        Assert.AreEqual(2, outcome.Candidates.Count);
    }

    [TestMethod]
    public async Task OneFailingSourceDoesNotFailTheSearch()
    {
        var aniList = new FakeProvider(AniList, Candidate(AniList, "1", "Overlord"));
        var broken = new FakeProvider(
            BookWalker,
            throws: new ReadingSourceUnavailableException(
                "BOOK☆WALKER returned HTTP 500.",
                ReadingSourceFailureKind.Unavailable));
        var crashing = new FakeProvider(Archive, throws: new FormatException("layout changed"));
        var health = new ReadingSourceHealthTracker(TimeProvider.System);
        var service = Service(health, aniList, broken, crashing);

        var outcome = await service.SearchLightNovelsAsync(
            ReadingSourceSettingsState.Default,
            "Overlord",
            24,
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "anilist:1" },
            outcome.Candidates.Select(candidate => candidate.Identity).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { BookWalker, Archive },
            outcome.UnavailableProviders.ToArray());
        Assert.AreEqual(ReadingSourceHealthStatus.Healthy, health.Get(AniList).Status);
        Assert.AreEqual(ReadingSourceHealthStatus.Unavailable, health.Get(BookWalker).Status);
        Assert.AreEqual(ReadingSourceHealthStatus.Unavailable, health.Get(Archive).Status);
    }

    [TestMethod]
    public async Task ASlowSourceIsCutOffWithoutStallingTheOthers()
    {
        var aniList = new FakeProvider(AniList, Candidate(AniList, "1", "Overlord"));
        var slow = new FakeProvider(
            Archive,
            respond: async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return [];
            });
        var service = new ReadingCatalogSearchService(
            [aniList, slow],
            new ReadingSourceHealthTracker(TimeProvider.System),
            NullLogger<ReadingCatalogSearchService>.Instance)
        {
            ProviderTimeout = TimeSpan.FromMilliseconds(100)
        };

        var outcome = await service.SearchLightNovelsAsync(
            ReadingSourceSettingsState.Default,
            "Overlord",
            24,
            CancellationToken.None);

        Assert.AreEqual(1, outcome.Candidates.Count);
        CollectionAssert.AreEqual(new[] { Archive }, outcome.UnavailableProviders.ToArray());
    }

    [TestMethod]
    public async Task CancellingTheSearchStillCancelsIt()
    {
        var slow = new FakeProvider(
            AniList,
            respond: async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return [];
            });
        var service = Service(slow);
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.SearchLightNovelsAsync(
                ReadingSourceSettingsState.Default,
                "Overlord",
                24,
                cancellation.Token));
    }

    [TestMethod]
    public async Task AFailedSourceIsLeftAloneUntilItsCooldownEnds()
    {
        var clock = new ManualTime(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));
        var flaky = new FakeProvider(
            BookWalker,
            throws: new ReadingSourceUnavailableException(
                "down",
                ReadingSourceFailureKind.Unavailable));
        var service = Service(new ReadingSourceHealthTracker(clock), flaky);

        await service.SearchLightNovelsAsync(
            ReadingSourceSettingsState.Default, "x", 24, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(10));
        var skipped = await service.SearchLightNovelsAsync(
            ReadingSourceSettingsState.Default, "x", 24, CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(30));
        await service.SearchLightNovelsAsync(
            ReadingSourceSettingsState.Default, "x", 24, CancellationToken.None);

        // Asked once, skipped while cooling down (still reported unavailable), asked again
        // after the 30 second base backoff.
        Assert.AreEqual(2, flaky.Calls);
        CollectionAssert.AreEqual(new[] { BookWalker }, skipped.UnavailableProviders.ToArray());
    }

    [TestMethod]
    public async Task ABlockingSourceIsNotRetriedForAnHour()
    {
        var clock = new ManualTime(new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero));
        var blocking = new FakeProvider(
            WebNovel,
            throws: new ReadingSourceUnavailableException(
                "challenge",
                ReadingSourceFailureKind.Blocked));
        var health = new ReadingSourceHealthTracker(clock);
        var service = Service(health, blocking);
        var settings = Preferences(new() { [WebNovel] = new(true, 40) });

        await service.SearchLightNovelsAsync(settings, "x", 24, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(59));
        await service.SearchLightNovelsAsync(settings, "x", 24, CancellationToken.None);
        Assert.AreEqual(1, blocking.Calls);
        Assert.AreEqual(ReadingSourceHealthStatus.Blocked, health.Get(WebNovel).Status);

        clock.Advance(TimeSpan.FromMinutes(2));
        await service.SearchLightNovelsAsync(settings, "x", 24, CancellationToken.None);
        Assert.AreEqual(2, blocking.Calls);
    }

    [TestMethod]
    public async Task ResultsAreBoundedToTwiceThePerSourceLimit()
    {
        var many = Enumerable.Range(1, 30)
            .Select(index => Candidate(AniList, index.ToString(), "Overlord " + index))
            .ToArray();
        var syosetu = Enumerable.Range(1, 30)
            .Select(index => Candidate(Syosetu, $"n{1000 + index}aa", "Overlord " + index))
            .ToArray();
        var service = Service(new FakeProvider(AniList, many), new FakeProvider(Syosetu, syosetu));

        var outcome = await service.SearchLightNovelsAsync(
            ReadingSourceSettingsState.Default,
            "Overlord",
            10,
            CancellationToken.None);

        Assert.AreEqual(20, outcome.Candidates.Count);
    }

    [TestMethod]
    public async Task EmptyQueryAsksNoSource()
    {
        var aniList = new FakeProvider(AniList, Candidate(AniList, "1", "Overlord"));
        var service = Service(aniList);

        var outcome = await service.SearchLightNovelsAsync(
            ReadingSourceSettingsState.Default,
            "  \t ",
            24,
            CancellationToken.None);

        Assert.AreEqual(0, aniList.Calls);
        Assert.AreEqual(0, outcome.Candidates.Count);
    }

    // ---- helpers -----------------------------------------------------------------------

    private static ReadingCatalogSearchService Service(params IReadingCatalogProvider[] providers) =>
        Service(new ReadingSourceHealthTracker(TimeProvider.System), providers);

    private static ReadingCatalogSearchService Service(
        ReadingSourceHealthTracker health,
        params IReadingCatalogProvider[] providers) =>
        new(providers, health, NullLogger<ReadingCatalogSearchService>.Instance);

    /// <summary>Applies the catalog's own normalization, as the settings store does.</summary>
    private static ReadingSourceSettingsState Preferences(
        Dictionary<string, ReadingSourcePreference> providers) =>
        ReadingSourceCatalog.Normalize(providers, rejectInvalidPriority: true);

    private static ReadingSourceSettingsState OnlyEnabled(params string[] enabled) =>
        Preferences(
            ReadingSourceCatalog.Definitions.ToDictionary(
                source => source.Key,
                source => new ReadingSourcePreference(
                    enabled.Contains(source.Key),
                    source.DefaultPriority)));

    private static ReadingCatalogCandidate Candidate(
        string provider,
        string id,
        string title) =>
        new(
            provider,
            id,
            title,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            IsPublicWebSource: provider == Syosetu);

    private sealed class FakeProvider : IReadingCatalogProvider
    {
        private readonly Func<string, CancellationToken, Task<IReadOnlyList<ReadingCatalogCandidate>>> respond;

        public FakeProvider(string key, params ReadingCatalogCandidate[] results)
            : this(key, respond: (_, _) => Task.FromResult<IReadOnlyList<ReadingCatalogCandidate>>(results))
        {
        }

        public FakeProvider(string key, Exception throws)
            : this(key, respond: (_, _) => Task.FromException<IReadOnlyList<ReadingCatalogCandidate>>(throws))
        {
        }

        public FakeProvider(
            string key,
            Func<string, CancellationToken, Task<IReadOnlyList<ReadingCatalogCandidate>>> respond)
        {
            Key = key;
            this.respond = respond;
        }

        public string Key { get; }

        public int Calls { get; private set; }

        public Task<IReadOnlyList<ReadingCatalogCandidate>> SearchAsync(
            string query,
            int limit,
            CancellationToken cancellationToken)
        {
            Calls++;
            return respond(query, cancellationToken);
        }
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public void Advance(TimeSpan by) => now += by;

        public override DateTimeOffset GetUtcNow() => now;
    }
}
