using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class LearningHubVocabularyTests
{
    [TestMethod]
    public async Task SavingWordDoesNotScheduleReview()
    {
        await using var fixture = await Fixture.CreateAsync();
        var term = await fixture.AddTermAsync("保存", "ほぞん", "save");

        await fixture.Service.SaveForLaterAsync(
            term.Id,
            CancellationToken.None);

        var userTerm = await fixture.WordCardAsync(term.Id);

        Assert.AreEqual(UserTermState.Saved, userTerm.State);
        Assert.IsNull(userTerm.NextReviewAt);
        Assert.IsNull(userTerm.LearningStartedAt);
        Assert.IsNull(userTerm.QueuePosition);

        var due = await fixture.Service.GetDueAsync(CancellationToken.None);
        Assert.IsFalse(due.Any(x => x.TermId == term.Id));
    }

    [TestMethod]
    public async Task SavedWordCanExplicitlyEnterLearningQueue()
    {
        await using var fixture = await Fixture.CreateAsync();
        var term = await fixture.AddTermAsync("学ぶ", "まなぶ", "learn");

        await fixture.Service.SaveForLaterAsync(
            term.Id,
            CancellationToken.None);
        await fixture.Service.SetStateAsync(
            term.Id,
            UserTermState.Learning,
            CancellationToken.None);

        var queued = await fixture.WordCardAsync(term.Id);

        Assert.AreEqual(UserTermState.Learning, queued.State);
        Assert.IsNull(queued.LearningStartedAt);
        Assert.IsNotNull(queued.QueuePosition);

        var due = await fixture.Service.GetDueAsync(CancellationToken.None);

        Assert.IsTrue(due.Any(x => x.TermId == term.Id));

        var activated = await fixture.WordCardAsync(term.Id);

        Assert.IsNotNull(activated.LearningStartedAt);
        Assert.IsNotNull(activated.NextReviewAt);
    }

    [TestMethod]
    public async Task IgnoredAndSuspendedWordsAreNotDue()
    {
        await using var fixture = await Fixture.CreateAsync();
        var ignored = await fixture.AddTermAsync("無視", "むし", "ignore");
        var suspended = await fixture.AddTermAsync("止める", "とめる", "stop");

        await fixture.Service.SetStateAsync(
            ignored.Id,
            UserTermState.Learning,
            CancellationToken.None);
        await fixture.Service.SetStateAsync(
            suspended.Id,
            UserTermState.Learning,
            CancellationToken.None);

        // Activate them once, then remove them from active scheduling.
        await fixture.Service.GetDueAsync(CancellationToken.None);

        await fixture.Service.IgnoreAsync(
            ignored.Id,
            CancellationToken.None);
        await fixture.Service.SuspendAsync(
            suspended.Id,
            CancellationToken.None);

        var due = await fixture.Service.GetDueAsync(CancellationToken.None);

        Assert.IsFalse(due.Any(x => x.TermId == ignored.Id));
        Assert.IsFalse(due.Any(x => x.TermId == suspended.Id));

        var states = new Dictionary<Guid, LearningCard>
        {
            [ignored.Id] = await fixture.WordCardAsync(ignored.Id),
            [suspended.Id] = await fixture.WordCardAsync(suspended.Id)
        };

        Assert.AreEqual(UserTermState.Ignored, states[ignored.Id].State);
        Assert.IsNull(states[ignored.Id].NextReviewAt);
        Assert.AreEqual(UserTermState.Suspended, states[suspended.Id].State);
        Assert.IsNull(states[suspended.Id].NextReviewAt);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            string directory,
            AppDbContext db,
            LearningService service)
        {
            Directory = directory;
            Db = db;
            Service = service;
        }

        public string Directory { get; }
        public AppDbContext Db { get; }
        public LearningService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"anilingo-learning-hub-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(
                    $"Data Source={Path.Combine(directory, "anilingo.db")};Foreign Keys=True")
                .Options;

            var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            return new Fixture(
                directory,
                db,
                new LearningService(db, new FsrsReviewScheduler()));
        }

        public async Task<Term> AddTermAsync(
            string canonical,
            string reading,
            string meaning)
        {
            var term = new Term
            {
                Id = Guid.NewGuid(),
                Language = "ja",
                Canonical = canonical,
                Reading = reading,
                Meaning = meaning
            };
            Db.Terms.Add(term);
            await Db.SaveChangesAsync();
            return term;
        }

        public Task<LearningCard> WordCardAsync(Guid termId) =>
            (from card in Db.LearningCards.AsNoTracking()
             join unit in Db.LearningUnits.AsNoTracking() on card.UnitId equals unit.Id
             where card.ProfileId == LearningProfile.DefaultId
                 && card.Mode == LearningCardMode.Recognition
                 && unit.TermId == termId
             select card)
            .SingleAsync();

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();

            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
