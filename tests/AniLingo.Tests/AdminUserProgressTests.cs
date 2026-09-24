using AniLingo.Web.Data;
using AniLingo.Web.Features.Admin;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Progress;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class AdminUserProgressTests
{
    [TestMethod]
    public async Task OverviewCombinesAnimeNovelAndLearningProgressPerAccount()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var now = DateTime.UtcNow;

            db.OwnerAccounts.AddRange(
                new OwnerAccount
                {
                    Id = OwnerAccount.SingletonId,
                    UserName = "owner",
                    NormalizedUserName = "OWNER",
                    PasswordHash = "hash",
                    Role = AccountRole.Owner,
                    IsEnabled = true,
                    CreatedAt = now.AddDays(-10)
                },
                new OwnerAccount
                {
                    Id = "reader",
                    UserName = "reader",
                    NormalizedUserName = "READER",
                    PasswordHash = "hash",
                    Role = AccountRole.User,
                    IsEnabled = true,
                    CreatedAt = now.AddDays(-5)
                });

            var anime = new Anime { Key = "admin-anime", Title = "Anime title" };
            var episode = new Episode
            {
                AnimeId = anime.Id,
                SeasonNumber = 2,
                Number = 4,
                Title = "Episode title"
            };
            db.Add(anime);
            db.Add(episode);
            db.EpisodeProgress.Add(new EpisodeProgress
            {
                ProfileId = "reader",
                EpisodeId = episode.Id,
                PositionMs = 45_000,
                DurationMs = 90_000,
                UpdatedAt = now.AddMinutes(-2)
            });

            var work = new NovelWork
            {
                SourceProvider = "fake",
                SourceKey = "admin-novel",
                SourceUrl = "https://example.invalid/admin-novel",
                Title = "Novel title"
            };
            var chapter = new NovelChapter
            {
                WorkId = work.Id,
                Number = 12,
                SourceUrl = "https://example.invalid/admin-novel/12",
                Title = "Chapter title"
            };
            db.Add(work);
            db.Add(chapter);
            db.NovelProgress.Add(new NovelProgress
            {
                ProfileId = "reader",
                WorkId = work.Id,
                ChapterId = chapter.Id,
                PositionPermille = 730,
                UpdatedAt = now.AddMinutes(-4)
            });

            var knownTerm = new Term { Canonical = "猫" };
            var learningTerm = new Term { Canonical = "犬" };
            db.AddRange(knownTerm, learningTerm);
            db.UserTerms.AddRange(
                new UserTerm
                {
                    ProfileId = "reader",
                    TermId = knownTerm.Id,
                    State = UserTermState.Known,
                    UpdatedAt = now.AddMinutes(-3)
                },
                new UserTerm
                {
                    ProfileId = "reader",
                    TermId = learningTerm.Id,
                    State = UserTermState.Learning,
                    UpdatedAt = now.AddMinutes(-3)
                });
            db.Reviews.Add(new Review
            {
                ProfileId = "reader",
                TermId = learningTerm.Id,
                Rating = ReviewRating.Good,
                ReviewedAt = now.AddMinutes(-1),
                NextReviewAt = now.AddDays(1)
            });

            await db.SaveChangesAsync();

            var service = new AdminUserProgressService(db);
            var users = await service.GetAsync();

            Assert.AreEqual(2, users.Count);

            var reader = users.Single(x => x.Account.Id == "reader");
            Assert.IsNotNull(reader.CurrentAnime);
            Assert.AreEqual("Anime title", reader.CurrentAnime.AnimeTitle);
            Assert.AreEqual(50, reader.CurrentAnime.Percent);
            Assert.AreEqual(1, reader.EpisodesStarted);
            Assert.AreEqual(0, reader.EpisodesCompleted);

            Assert.IsNotNull(reader.CurrentNovel);
            Assert.AreEqual("Novel title", reader.CurrentNovel.WorkTitle);
            Assert.AreEqual(12, reader.CurrentNovel.ChapterNumber);
            Assert.AreEqual(73, reader.CurrentNovel.Percent);
            Assert.AreEqual(1, reader.NovelsStarted);

            Assert.AreEqual(1, reader.Learning.KnownTerms);
            Assert.AreEqual(1, reader.Learning.LearningTerms);
            Assert.AreEqual(1, reader.Learning.Reviews);
            Assert.IsNotNull(reader.LastActivityAt);

            var owner = users.Single(x => x.Account.Role == AccountRole.Owner);
            Assert.IsNull(owner.CurrentAnime);
            Assert.IsNull(owner.CurrentNovel);
            Assert.AreEqual(0, owner.Learning.Reviews);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static string TempDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"anilingo-admin-progress-{Guid.NewGuid():N}.db");

    private static async Task<AppDbContext> CreateDatabaseAsync(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options;

        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }
}
