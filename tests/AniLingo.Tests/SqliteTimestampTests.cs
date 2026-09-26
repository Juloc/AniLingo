using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class SqliteTimestampTests
{
    [TestMethod]
    public async Task SQLiteCanCompareAndOrderPersistedUtcTimestamps()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-time-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var now = DateTime.UtcNow;
            var anime = new Anime { Key = "test", Title = "Test" };
            var olderEpisode = new Episode
            {
                AnimeId = anime.Id,
                Number = 1,
                Title = "Older",
                DiscoveredAt = now.AddMinutes(-10)
            };
            var newerEpisode = new Episode
            {
                AnimeId = anime.Id,
                Number = 2,
                Title = "Newer",
                DiscoveredAt = now.AddMinutes(-1)
            };
            var dueTerm = new Term { Language = "ja", Canonical = "猫" };
            var futureTerm = new Term { Language = "ja", Canonical = "犬" };

            db.AddRange(anime, olderEpisode, newerEpisode, dueTerm, futureTerm);
            await LearningTestData.SeedTermCardAsync(
                db,
                LearningProfile.DefaultId,
                dueTerm,
                UserTermState.Learning,
                nextReviewAt: now.AddMinutes(-1),
                learningStartedAt: now.AddDays(-1),
                updatedAt: now.AddMinutes(-2));
            await LearningTestData.SeedTermCardAsync(
                db,
                LearningProfile.DefaultId,
                futureTerm,
                UserTermState.Learning,
                nextReviewAt: now.AddMinutes(10),
                learningStartedAt: now.AddDays(-1),
                updatedAt: now.AddMinutes(-2));
            db.SubtitleTracks.AddRange(
                new SubtitleTrack
                {
                    EpisodeId = newerEpisode.Id,
                    Path = "/tmp/older.srt",
                    Format = "srt",
                    SourceUpdatedAt = now.AddMinutes(-5),
                    ImportedAt = now.AddMinutes(-5)
                },
                new SubtitleTrack
                {
                    EpisodeId = newerEpisode.Id,
                    Path = "/tmp/newer.srt",
                    Format = "srt",
                    SourceUpdatedAt = now.AddMinutes(-2),
                    ImportedAt = now.AddMinutes(-1)
                });

            await db.SaveChangesAsync();

            var dueCount = await db.LearningCards
                .AsNoTracking()
                .CountAsync(x =>
                    x.ProfileId == LearningProfile.DefaultId &&
                    x.State == UserTermState.Learning &&
                    x.NextReviewAt != null &&
                    x.NextReviewAt <= now);

            Assert.AreEqual(1, dueCount);

            var due = await new LearningService(db, new FsrsReviewScheduler())
                .GetDueAsync(CancellationToken.None);

            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(dueTerm.Id, due[0].TermId);

            var episodeOrder = await db.Episodes
                .AsNoTracking()
                .OrderByDescending(x => x.DiscoveredAt)
                .Select(x => x.Id)
                .ToListAsync();

            Assert.AreEqual(newerEpisode.Id, episodeOrder[0]);

            var newestTrack = await db.SubtitleTracks
                .AsNoTracking()
                .OrderByDescending(x => x.ImportedAt)
                .Select(x => x.Path)
                .FirstAsync();

            Assert.AreEqual("/tmp/newer.srt", newestTrack);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task ExistingAlphaUtcOffsetTextRemainsReadableAndComparable()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-old-time-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var term = new Term { Language = "ja", Canonical = "旧" };
            db.Terms.Add(term);
            var card = await LearningTestData.SeedTermCardAsync(
                db,
                LearningProfile.DefaultId,
                term,
                UserTermState.Learning);

            // Converted alpha rows keep their original offset text verbatim.
            await using (var command = db.Database.GetDbConnection().CreateCommand())
            {
                await db.Database.OpenConnectionAsync();
                command.CommandText = """
                    UPDATE LearningCards
                    SET NextReviewAt = '2026-09-21 12:00:00+00:00',
                        UpdatedAt = '2026-09-21 12:00:00+00:00'
                    WHERE Id = $id;
                    """;
                command.Parameters.Add(new SqliteParameter("$id", card.Id));
                await command.ExecuteNonQueryAsync();
                await db.Database.CloseConnectionAsync();
            }

            db.ChangeTracker.Clear();
            var cutoff = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
            var row = await db.LearningCards
                .AsNoTracking()
                .SingleAsync(x => x.Id == card.Id);

            Assert.AreEqual(DateTimeKind.Utc, row.NextReviewAt!.Value.Kind);

            var due = await db.LearningCards
                .AsNoTracking()
                .CountAsync(x => x.NextReviewAt != null && x.NextReviewAt <= cutoff);

            Assert.AreEqual(1, due);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
}
