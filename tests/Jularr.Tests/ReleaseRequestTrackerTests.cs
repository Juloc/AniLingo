using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Wanted;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.ReadingAcquisition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Jularr.Tests;

/// <summary>
/// The one release-request lifecycle shared by Books, Manga and Light Novels (#485 item 6):
/// tried releases, backoff, submit failures and the last problem.
/// </summary>
[TestClass]
public sealed class ReleaseRequestTrackerTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public async Task NextUntriedReleaseIsSubmittedAndRemembered()
    {
        await using var host = await Host.CreateAsync();
        var request = await host.CreateBookAsync(new BookRequestPayload("ol:1", "Dune", "Frank Herbert") { TriedReleases = ["Dune EPUB"] });
        var submitted = new List<string>();

        var execution = await host.Tracker.ContinueAsync(
            request,
            BookAcquisitionExecutor.ReadPayload(request),
            [Candidate("Dune EPUB"), Candidate("Dune PDF")],
            "No release found on the indexers.",
            release =>
            {
                submitted.Add(release.Identity);
                return Task.FromResult(new ReleaseRequestSubmission(true, Guid.NewGuid(), "Sent."));
            },
            CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "Dune PDF" }, submitted, "A tried release is never sent twice.");
        Assert.AreEqual(AcquisitionRequestStatus.Downloading, execution.Status);
        Assert.AreEqual("Dune PDF", execution.Message);
        var payload = BookAcquisitionExecutor.ReadPayload(await host.GetAsync(request.Id));
        CollectionAssert.AreEquivalent(new[] { "Dune EPUB", "Dune PDF" }, payload.TriedReleases!.ToArray());
        Assert.AreEqual(1, payload.Searches);
        Assert.IsNull(payload.NextSearchUtc);
        Assert.AreEqual("ol:1", payload.CatalogId, "The media fields survive every save.");
    }

    [TestMethod]
    public async Task NoReleaseBacksOffAndGivesUpAfterTheLastSearch()
    {
        await using var host = await Host.CreateAsync();
        var request = await host.CreateBookAsync(new BookRequestPayload("ol:1", "Dune", null));

        var waiting = await host.ContinueWithoutReleaseAsync(request);
        Assert.AreEqual(AcquisitionRequestStatus.Approved, waiting.Status);
        var payload = BookAcquisitionExecutor.ReadPayload(await host.GetAsync(request.Id));
        Assert.AreEqual(Now + ReleaseRequestTracker.SearchBackoff(1), payload.NextSearchUtc);
        Assert.IsFalse(ReleaseRequestTracker.IsSearchDue(payload, Now));
        Assert.IsTrue(ReleaseRequestTracker.IsSearchDue(payload, Now + TimeSpan.FromHours(6)));

        await host.SavePayloadAsync(request, payload with { Searches = ReleaseRequestTracker.MaxSearches - 1 });
        var failed = await host.ContinueWithoutReleaseAsync(await host.GetAsync(request.Id));
        Assert.AreEqual(AcquisitionRequestStatus.Failed, failed.Status);
        StringAssert.Contains(failed.Message, $"Gave up after {ReleaseRequestTracker.MaxSearches} searches.");
        Assert.IsNull(BookAcquisitionExecutor.ReadPayload(await host.GetAsync(request.Id)).NextSearchUtc);
    }

    [TestMethod]
    public async Task EveryReleaseTriedIsNamedInsteadOfNoRelease()
    {
        await using var host = await Host.CreateAsync();
        var request = await host.CreateReadingAsync(new ReadingRequestPayload("Frieren", [], null) { TriedReleases = ["frieren-1"] });

        var execution = await host.Tracker.ContinueAsync(
            request,
            ReadingAcquisitionEngine.ReadPayload(request, new ReadingAcquisitionTarget(MediaAcquisitionKind.Manga, "Frieren", [])),
            [Candidate("frieren-1")],
            "No release found on the indexers.",
            _ => throw new AssertFailedException("Nothing may be submitted."),
            CancellationToken.None);

        Assert.AreEqual(AcquisitionRequestStatus.Approved, execution.Status);
        StringAssert.StartsWith(execution.Message, ReleaseRequestTracker.EveryReleaseTried);
    }

    [TestMethod]
    public async Task RefusedSubmissionKeepsTheReleaseTriedAndSearchesAgainLater()
    {
        await using var host = await Host.CreateAsync();
        var request = await host.CreateReadingAsync(new ReadingRequestPayload("Frieren", [], null));
        var operationId = Guid.NewGuid();

        var execution = await host.Tracker.ContinueAsync(
            request,
            ReadingAcquisitionEngine.ReadPayload(request, new ReadingAcquisitionTarget(MediaAcquisitionKind.Manga, "Frieren", [])),
            [Candidate("frieren-1")],
            "No release found on the indexers.",
            _ => Task.FromResult(new ReleaseRequestSubmission(false, operationId, "SABnzbd rejected the request.")),
            CancellationToken.None);

        Assert.AreEqual(AcquisitionRequestStatus.Approved, execution.Status, "A client problem is retried, not a final failure.");
        Assert.AreEqual(operationId, execution.OperationId);
        StringAssert.StartsWith(execution.Message, "SABnzbd rejected the request. Searching again ");
        var payload = ReadingAcquisitionEngine.ReadPayload(await host.GetAsync(request.Id), new ReadingAcquisitionTarget(MediaAcquisitionKind.Manga, "Frieren", []));
        CollectionAssert.AreEqual(new[] { "frieren-1" }, payload.TriedReleases!.ToArray());
        Assert.AreEqual("SABnzbd rejected the request.", payload.LastProblem);
        Assert.AreEqual(Now + ReleaseRequestTracker.SearchBackoff(1), payload.NextSearchUtc);
    }

    [TestMethod]
    public async Task PreviousProblemIsShownOnceThenConsumed()
    {
        await using var host = await Host.CreateAsync();
        var request = await host.CreateBookAsync(
            ReleaseRequestTracker.AfterProblem(new BookRequestPayload("ol:1", "Dune", null), "The download failed: Repair failed."));

        var first = await host.ContinueWithoutReleaseAsync(request);
        var second = await host.ContinueWithoutReleaseAsync(await host.GetAsync(request.Id));

        StringAssert.StartsWith(first.Message, "The download failed: Repair failed. No release found");
        StringAssert.StartsWith(second.Message, "No release found", "The problem is not stacked on every later search.");
    }

    [TestMethod]
    public async Task MigrationRenamesTheReadingTriedReleasesKeyOnce()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jularr-release-state-{Guid.NewGuid():N}.db");
        try
        {
            Guid manga;
            Guid book;
            await using (var oldDb = CreateDb(path))
            {
                await oldDb.GetService<IMigrator>().MigrateAsync("20260928173000_AddReaderLayoutPreferences");
                var store = new AcquisitionAccessStore(oldDb);
                manga = (await store.CreateAsync(
                    new AcquisitionRequestDraft(MediaAcquisitionKind.Manga, "anilist", "1", "Frieren", null, null,
                        """{"title":"Frieren","aliases":[],"triedReleaseIds":["a","b"],"searches":2,"lastProblem":"Bad."}"""),
                    "owner", AcquisitionRequestStatus.Approved, "owner", CancellationToken.None)).Id;
                book = (await store.CreateAsync(
                    new AcquisitionRequestDraft(MediaAcquisitionKind.Book, "books-catalog", "ol:1", "Dune", null, null,
                        """{"catalogId":"ol:1","title":"Dune","triedReleases":["Dune EPUB"],"searches":1}"""),
                    "owner", AcquisitionRequestStatus.Approved, "owner", CancellationToken.None)).Id;
            }

            await using (var upgraded = CreateDb(path))
            {
                await upgraded.GetService<IMigrator>().MigrateAsync();
                var store = new AcquisitionAccessStore(upgraded);

                var reading = ReadingAcquisitionEngine.ReadPayload(
                    (await store.GetAsync(manga, CancellationToken.None))!,
                    new ReadingAcquisitionTarget(MediaAcquisitionKind.Manga, "Frieren", []));
                CollectionAssert.AreEqual(new[] { "a", "b" }, reading.TriedReleases!.ToArray());
                Assert.AreEqual(2, reading.Searches);
                Assert.AreEqual("Bad.", reading.LastProblem);
                Assert.IsFalse((await store.GetAsync(manga, CancellationToken.None))!.PayloadJson!.Contains("triedReleaseIds", StringComparison.Ordinal));

                var books = BookAcquisitionExecutor.ReadPayload((await store.GetAsync(book, CancellationToken.None))!);
                CollectionAssert.AreEqual(new[] { "Dune EPUB" }, books.TriedReleases!.ToArray(), "Books payloads already had the shared shape.");
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static Jularr.Web.Data.AppDbContext CreateDb(string path) =>
        new(new DbContextOptionsBuilder<Jularr.Web.Data.AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options);

    private static ReleaseRequestCandidate Candidate(string identity) =>
        new(identity, identity, new Uri($"https://indexer.invalid/{Uri.EscapeDataString(identity)}.nzb"));

    private sealed class Host : IAsyncDisposable
    {
        private readonly SabnzbdTestEnvironment environment;

        private Host(SabnzbdTestEnvironment environment)
        {
            this.environment = environment;
            Store = new AcquisitionAccessStore(environment.Db);
            Tracker = new ReleaseRequestTracker(Store, new FixedClock(Now));
        }

        public AcquisitionAccessStore Store { get; }
        public ReleaseRequestTracker Tracker { get; }

        public static async Task<Host> CreateAsync() =>
            new(await SabnzbdTestSupport.CreateEnvironmentAsync());

        public Task<AcquisitionRequest> CreateBookAsync(BookRequestPayload payload) =>
            Store.CreateAsync(
                new AcquisitionRequestDraft(MediaAcquisitionKind.Book, "books-catalog", payload.CatalogId, payload.Title, payload.Author, null, payload.Serialize()),
                "owner",
                AcquisitionRequestStatus.Approved,
                "owner",
                CancellationToken.None);

        public Task<AcquisitionRequest> CreateReadingAsync(ReadingRequestPayload payload) =>
            Store.CreateAsync(
                new AcquisitionRequestDraft(MediaAcquisitionKind.Manga, "anilist", "1", payload.Title, null, null, payload.Serialize()),
                "owner",
                AcquisitionRequestStatus.Approved,
                "owner",
                CancellationToken.None);

        public async Task<AcquisitionRequest> GetAsync(Guid id) =>
            (await Store.GetAsync(id, CancellationToken.None))!;

        public Task SavePayloadAsync(AcquisitionRequest request, ReleaseRequestPayload payload) =>
            Store.UpdatePayloadAsync(request.Id, payload.Serialize(), CancellationToken.None);

        public Task<AcquisitionExecution> ContinueWithoutReleaseAsync(AcquisitionRequest request) =>
            Tracker.ContinueAsync(
                request,
                BookAcquisitionExecutor.ReadPayload(request),
                [],
                "No release found on the indexers.",
                _ => throw new AssertFailedException("Nothing may be submitted."),
                CancellationToken.None);

        public ValueTask DisposeAsync() => environment.DisposeAsync();
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
