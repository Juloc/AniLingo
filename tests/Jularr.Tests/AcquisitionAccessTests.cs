using System.Security.Claims;
using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Wanted;
using Jularr.Web.Features.Acquisition.Prowlarr;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Books;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

[TestClass]
public sealed class AcquisitionAccessTests
{
    [TestMethod]
    public void OwnerAlwaysAddsAutomaticallyAndManually()
    {
        var capabilities = AcquisitionCapabilities.Resolve(
            new AcquisitionAccessPolicy(MediaAcquisitionKind.Book, UserAddMode.Disabled, ManualAddMode.OwnerOnly),
            isOwner: true);

        Assert.IsTrue(capabilities.CanAdd);
        Assert.IsFalse(capabilities.AddCreatesRequest);
        Assert.IsTrue(capabilities.CanAddManually);
    }

    [TestMethod]
    [DataRow(UserAddMode.Disabled, false, false)]
    [DataRow(UserAddMode.Request, true, true)]
    [DataRow(UserAddMode.Automatic, true, false)]
    public void UserCapabilitiesFollowThePolicy(UserAddMode mode, bool canAdd, bool createsRequest)
    {
        var capabilities = AcquisitionCapabilities.Resolve(
            new AcquisitionAccessPolicy(MediaAcquisitionKind.Anime, mode, ManualAddMode.Users),
            isOwner: false);

        Assert.AreEqual(canAdd, capabilities.CanAdd);
        Assert.AreEqual(createsRequest, capabilities.AddCreatesRequest);
        Assert.IsTrue(capabilities.CanAddManually);
    }

    [TestMethod]
    public async Task DefaultsAreRequestAndOwnerOnlyManualAndPoliciesPersist()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new AcquisitionAccessStore(fixture.Db);

        var defaults = await store.GetPoliciesAsync(CancellationToken.None);
        Assert.AreEqual(4, defaults.Count);
        Assert.IsTrue(defaults.All(policy => policy.UserAdd == UserAddMode.Request && policy.Manual == ManualAddMode.OwnerOnly));

        await store.SavePolicyAsync(
            new AcquisitionAccessPolicy(MediaAcquisitionKind.Manga, UserAddMode.Automatic, ManualAddMode.Users),
            CancellationToken.None);

        var manga = await store.GetPolicyAsync(MediaAcquisitionKind.Manga, CancellationToken.None);
        Assert.AreEqual(UserAddMode.Automatic, manga.UserAdd);
        Assert.AreEqual(ManualAddMode.Users, manga.Manual);
        Assert.AreEqual(UserAddMode.Request, (await store.GetPolicyAsync(MediaAcquisitionKind.Book, CancellationToken.None)).UserAdd);
    }

    [TestMethod]
    public async Task UserRequestWaitsForOwnerApprovalThenRunsTheExecutor()
    {
        await using var fixture = await Fixture.CreateAsync();
        var executor = new RecordingExecutor(MediaAcquisitionKind.Book);
        var user = fixture.Service("alice", isOwner: false, executor);
        var owner = fixture.Service("owner", isOwner: true, executor);

        var request = await user.SubmitAsync(Draft("dune"), CancellationToken.None);
        Assert.AreEqual(AcquisitionRequestStatus.Pending, request.Status);
        Assert.AreEqual(0, executor.Runs);

        var again = await user.SubmitAsync(Draft("dune"), CancellationToken.None);
        Assert.AreEqual(request.Id, again.Id, "An open request is reused, not duplicated.");

        var approved = await owner.ApproveAsync(request.Id, CancellationToken.None);
        Assert.AreEqual(1, executor.Runs);
        Assert.AreEqual(AcquisitionRequestStatus.Downloading, approved.Status);
        Assert.AreEqual("owner", approved.DecidedByProfileId);
        Assert.IsNotNull(approved.OperationId);
    }

    [TestMethod]
    public async Task AutomaticUsersAndTheOwnerStartAcquisitionImmediately()
    {
        await using var fixture = await Fixture.CreateAsync();
        await new AcquisitionAccessStore(fixture.Db).SavePolicyAsync(
            new AcquisitionAccessPolicy(MediaAcquisitionKind.Book, UserAddMode.Automatic, ManualAddMode.OwnerOnly),
            CancellationToken.None);
        var executor = new RecordingExecutor(MediaAcquisitionKind.Book);

        var request = await fixture.Service("alice", isOwner: false, executor).SubmitAsync(Draft("dune"), CancellationToken.None);

        Assert.AreEqual(1, executor.Runs);
        Assert.AreEqual(AcquisitionRequestStatus.Downloading, request.Status);
    }

    [TestMethod]
    public async Task DisabledUsersAreRefusedAndCannotDecide()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new AcquisitionAccessStore(fixture.Db);
        await store.SavePolicyAsync(
            new AcquisitionAccessPolicy(MediaAcquisitionKind.Anime, UserAddMode.Disabled, ManualAddMode.OwnerOnly),
            CancellationToken.None);
        var user = fixture.Service("alice", isOwner: false, new RecordingExecutor(MediaAcquisitionKind.Anime));

        await Assert.ThrowsExactlyAsync<AcquisitionAccessDeniedException>(
            () => user.SubmitAsync(Draft("frieren", MediaAcquisitionKind.Anime), CancellationToken.None));

        var bookRequest = await user.SubmitAsync(Draft("dune"), CancellationToken.None);
        await Assert.ThrowsExactlyAsync<AcquisitionAccessDeniedException>(
            () => user.ApproveAsync(bookRequest.Id, CancellationToken.None));
    }

    [TestMethod]
    public async Task MediaWithoutExecutorStaysApprovedForTheOwnerAndFailuresAreRecorded()
    {
        await using var fixture = await Fixture.CreateAsync();
        var owner = fixture.Service("owner", isOwner: true, new RecordingExecutor(MediaAcquisitionKind.Book, fail: true));

        var manga = await owner.SubmitAsync(Draft("berserk", MediaAcquisitionKind.Manga), CancellationToken.None);
        Assert.AreEqual(AcquisitionRequestStatus.Approved, manga.Status);

        var book = await owner.SubmitAsync(Draft("dune"), CancellationToken.None);
        Assert.AreEqual(AcquisitionRequestStatus.Failed, book.Status);
        Assert.AreEqual("indexer down", book.StatusMessage);

        await owner.MarkCompletedAsync(manga.Id, CancellationToken.None);
        Assert.AreEqual(
            AcquisitionRequestStatus.Completed,
            (await new AcquisitionAccessStore(fixture.Db).GetAsync(manga.Id, CancellationToken.None))!.Status);
    }

    [TestMethod]
    public async Task RequesterCanWithdrawOwnPendingRequestOnly()
    {
        await using var fixture = await Fixture.CreateAsync();
        var executor = new RecordingExecutor(MediaAcquisitionKind.Book);
        var alice = fixture.Service("alice", isOwner: false, executor);
        var bob = fixture.Service("bob", isOwner: false, executor);

        var request = await alice.SubmitAsync(Draft("dune"), CancellationToken.None);
        await Assert.ThrowsExactlyAsync<AcquisitionAccessDeniedException>(() => bob.CancelAsync(request.Id, CancellationToken.None));

        await alice.CancelAsync(request.Id, CancellationToken.None);
        var stored = await new AcquisitionAccessStore(fixture.Db).GetAsync(request.Id, CancellationToken.None);
        Assert.AreEqual(AcquisitionRequestStatus.Rejected, stored!.Status);
    }

    [TestMethod]
    public void BookReleaseSelectorPrefersMatchingEpubThenAcceptsPdf()
    {
        ProwlarrReleaseCandidate Release(string title, string protocol = "usenet", long size = 5_000_000) =>
            new(title, "idx", 1, protocol, size, null, null, DateTimeOffset.UtcNow, 1, 1, Guid.NewGuid().ToString(), null,
                Jularr.Web.Features.Acquisition.AnimeReleaseParser.Parse(title), [], new Uri("https://indexer.example/get/" + Guid.NewGuid()), null);

        var releases = new[]
        {
            Release("Frank Herbert - Dune (1965) PDF"),
            Release("Frank Herbert - Dune Messiah EPUB"),
            Release("Frank Herbert - Dune (1965) retail EPUB"),
            Release("Frank Herbert - Dune EPUB", protocol: "torrent"),
            Release("Some Other Book EPUB"),
            Release("Frank Herbert - Dune MOBI")
        };

        var best = BookReleaseSelector.Pick(releases, "Dune", "Frank Herbert");

        Assert.IsNotNull(best);
        Assert.AreEqual("Frank Herbert - Dune (1965) retail EPUB", best.Title);
        Assert.AreEqual("Frank Herbert - Dune (1965) PDF", BookReleaseSelector.Pick([releases[0], releases[3], releases[5]], "Dune", "Frank Herbert")?.Title,
            "Without an EPUB the PDF is taken; torrent and MOBI results never are.");
        Assert.IsNull(BookReleaseSelector.Pick([releases[3], releases[5]], "Dune", "Frank Herbert"));
    }

    [TestMethod]
    public void BookReleaseSelectorRanksEpubOverPdfOverUnknownFormat()
    {
        ProwlarrReleaseCandidate Release(string title) =>
            new(title, "idx", 1, "usenet", 3_000_000, null, null, DateTimeOffset.UtcNow, 1, 1, Guid.NewGuid().ToString(), null,
                Jularr.Web.Features.Acquisition.AnimeReleaseParser.Parse(title), [], new Uri("https://indexer.example/get/" + Guid.NewGuid()), null);

        var ranked = BookReleaseSelector.Rank(
            [Release("James Clear - Atomic Habits"), Release("James Clear - Atomic Habits PDF"), Release("James Clear - Atomic Habits EPUB")],
            "Atomic Habits",
            "James Clear");

        CollectionAssert.AreEqual(
            new[] { "James Clear - Atomic Habits EPUB", "James Clear - Atomic Habits PDF", "James Clear - Atomic Habits" },
            ranked.Select(release => release.Release.Title).ToArray());
        Assert.IsTrue(ranked.All(release => release.Score > 0), "A name without a format is allowed; the download import checks it.");
    }

    [TestMethod]
    public void BookReleaseSelectorIgnoresSubtitlesAndExplainsRejections()
    {
        ProwlarrReleaseCandidate Release(string title, string protocol = "usenet") =>
            new(title, "idx", 1, protocol, 2_000_000, null, null, DateTimeOffset.UtcNow, 1, 1, Guid.NewGuid().ToString(), null,
                Jularr.Web.Features.Acquisition.AnimeReleaseParser.Parse(title), [], new Uri("https://indexer.example/get/" + Guid.NewGuid()), null);

        var ranked = BookReleaseSelector.Rank(
            [
                Release("Mary Shelley - Frankenstein AZW3"),
                Release("Mary Shelley - Frankenstein EPUB"),
                Release("Frankenstein EPUB", protocol: "torrent"),
                Release("Dracula EPUB")
            ],
            "Frankenstein; or, The Modern Prometheus",
            "Mary Shelley");

        Assert.AreEqual("Mary Shelley - Frankenstein EPUB", ranked[0].Release.Title, "The subtitle is not required in the release name.");
        Assert.IsNull(ranked[0].RejectedBecause);
        Assert.AreEqual(1, ranked.Count(release => release.Score > 0));
        CollectionAssert.AreEquivalent(
            new[] { "AZW3, not EPUB or PDF", "not a Usenet release", "title does not match" },
            ranked.Where(release => release.Score == 0).Select(release => release.RejectedBecause).ToArray());
    }

    [TestMethod]
    public void BookUsenetQueriesUseAuthorAndMainTitleFirst()
    {
        CollectionAssert.AreEqual(
            new[] { "Mary Shelley Frankenstein", "Frankenstein", "Frankenstein: The 1818 Text" },
            BookUsenetSearch.Queries("Frankenstein: The 1818 Text", "Mary Shelley").ToArray());
        CollectionAssert.AreEqual(new[] { "Dune" }, BookUsenetSearch.Queries(" Dune ", null).ToArray());
        Assert.AreEqual("Dune", BookReleaseSelector.MainTitle("Dune - Deluxe Edition"));
    }

    [TestMethod]
    public async Task WaitingBookRequestsAreSearchedAgainOnlyWhenDue()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new AcquisitionAccessStore(fixture.Db);
        var executor = new RecordingExecutor(MediaAcquisitionKind.Book);
        var owner = fixture.Service("owner", isOwner: true, executor);
        var now = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

        var due = await store.CreateAsync(BookDraft("dune", now.AddMinutes(-1)), "owner", AcquisitionRequestStatus.Approved, "owner", CancellationToken.None);
        var later = await store.CreateAsync(BookDraft("emma", now.AddHours(3)), "owner", AcquisitionRequestStatus.Approved, "owner", CancellationToken.None);
        var handler = new BookWantedRequestHandler(store, owner);

        Assert.IsTrue(handler.IsSearchDue(due, now));
        Assert.IsFalse(handler.IsSearchDue(later, now));
        Assert.IsTrue(handler.IsSearchDue(later, now.AddHours(3)));
        Assert.AreEqual(0, executor.Runs, "Wanted decides when to search; the handler only answers whether it is due.");
    }

    [TestMethod]
    public async Task FailedBookDownloadContinuesTheRequestWithTheNextRelease()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new AcquisitionAccessStore(fixture.Db);
        var executor = new RecordingExecutor(MediaAcquisitionKind.Book);
        var owner = fixture.Service("owner", isOwner: true, executor);

        var request = await owner.SubmitAsync(Draft("dune"), CancellationToken.None);
        var failedOperation = request.OperationId!.Value;
        Assert.AreEqual(1, executor.Runs);

        await new BookWantedRequestHandler(store, owner).ContinueAfterProblemAsync(
            request,
            "The download failed: Repair failed.",
            CancellationToken.None);
        Assert.AreEqual(2, executor.Runs);
        var continued = (await store.GetAsync(request.Id, CancellationToken.None))!;
        Assert.AreNotEqual(failedOperation, continued.OperationId);
        Assert.AreEqual("The download failed: Repair failed.", BookAcquisitionExecutor.ReadPayload(continued).LastProblem);
    }

    [TestMethod]
    public void BookSearchBackoffGrowsToDaily()
    {
        Assert.AreEqual(TimeSpan.FromHours(6), ReleaseRequestTracker.SearchBackoff(1));
        Assert.AreEqual(TimeSpan.FromHours(12), ReleaseRequestTracker.SearchBackoff(2));
        Assert.AreEqual(TimeSpan.FromHours(24), ReleaseRequestTracker.SearchBackoff(7));
    }

    private static AcquisitionRequestDraft BookDraft(string id, DateTime nextSearchUtc) =>
        new(MediaAcquisitionKind.Book, "test", id, id.ToUpperInvariant(), "Author", null,
            System.Text.Json.JsonSerializer.Serialize(
                new BookRequestPayload(id, id, "Author") { NextSearchUtc = nextSearchUtc },
                System.Text.Json.JsonSerializerOptions.Web));

    private static AcquisitionRequestDraft Draft(string id, MediaAcquisitionKind kind = MediaAcquisitionKind.Book) =>
        new(kind, "test", id, id.ToUpperInvariant(), "Author", null);

    private sealed class RecordingExecutor(MediaAcquisitionKind kind, bool fail = false) : IAcquisitionRequestExecutor
    {
        public int Runs { get; private set; }
        public MediaAcquisitionKind Kind => kind;

        public Task<AcquisitionExecution> ExecuteAsync(AcquisitionRequest request, CancellationToken cancellationToken)
        {
            Runs++;
            return fail
                ? throw new InvalidOperationException("indexer down")
                : Task.FromResult(new AcquisitionExecution(AcquisitionRequestStatus.Downloading, "release", Guid.NewGuid()));
        }
    }

    private sealed class FixedHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory;

        private Fixture(string directory, AppDbContext db)
        {
            this.directory = directory;
            Db = db;
        }

        public AppDbContext Db { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"jularr-access-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "app.db")};Foreign Keys=True")
                .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new Fixture(directory, db);
        }

        public AcquisitionRequestService Service(string profileId, bool isOwner, params IAcquisitionRequestExecutor[] executors)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, profileId) };
            if (isOwner)
            {
                claims.Add(new Claim(ClaimTypes.Role, AccountRoles.Owner));
            }

            // HttpContextAccessor keeps its context in a static AsyncLocal, so two accounts in one
            // test need their own accessor instances.
            var account = new CurrentAccountContext(new FixedHttpContextAccessor(
                new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }));
            return new AcquisitionRequestService(
                new AcquisitionAccessStore(Db),
                executors,
                account,
                new RecordingEventPublisher(),
                NullLogger<AcquisitionRequestService>.Instance);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}
