using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class ReviewContextTests
{
    [TestMethod]
    public async Task ReviewContextUsesMostUsefulEpisodeAndExactCue()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-review-context-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var anime = new Anime { Key = "frieren", Title = "Frieren" };
            var lowerEpisode = new Episode
            {
                AnimeId = anime.Id,
                SeasonNumber = 1,
                Number = 1,
                Title = "Low frequency"
            };
            var chosenEpisode = new Episode
            {
                AnimeId = anime.Id,
                SeasonNumber = 1,
                Number = 2,
                Title = "Chosen context"
            };
            var term = new Term
            {
                Language = "ja",
                Canonical = "魔法",
                Reading = "まほう",
                Meaning = "Magie"
            };

            db.AddRange(anime, lowerEpisode, chosenEpisode, term);
            db.EpisodeTerms.AddRange(
                new EpisodeTerm
                {
                    EpisodeId = lowerEpisode.Id,
                    TermId = term.Id,
                    Occurrences = 2,
                    FirstCueStartMs = 10_000
                },
                new EpisodeTerm
                {
                    EpisodeId = chosenEpisode.Id,
                    TermId = term.Id,
                    Occurrences = 7,
                    FirstCueStartMs = 83_000
                });

            var lowerTrack = new SubtitleTrack
            {
                EpisodeId = lowerEpisode.Id,
                Path = "/tmp/frieren-01.ja.srt",
                Language = "ja",
                Format = "srt",
                ImportedAt = DateTime.UtcNow.AddMinutes(-2),
                SourceUpdatedAt = DateTime.UtcNow.AddMinutes(-3)
            };
            var chosenTrack = new SubtitleTrack
            {
                EpisodeId = chosenEpisode.Id,
                Path = "/tmp/frieren-02.ja.srt",
                Language = "ja",
                Format = "srt",
                ImportedAt = DateTime.UtcNow.AddMinutes(-1),
                SourceUpdatedAt = DateTime.UtcNow.AddMinutes(-2)
            };

            db.SubtitleTracks.AddRange(lowerTrack, chosenTrack);
            await db.SaveChangesAsync();

            db.SubtitleCues.AddRange(
                new SubtitleCue
                {
                    SubtitleTrackId = lowerTrack.Id,
                    StartMs = 10_000,
                    EndMs = 12_000,
                    Text = "魔法だ。"
                },
                new SubtitleCue
                {
                    SubtitleTrackId = chosenTrack.Id,
                    StartMs = 83_000,
                    EndMs = 85_000,
                    Text = "これは魔法だよ。"
                });
            await db.SaveChangesAsync();

            var context = await new LearningService(db, new FsrsReviewScheduler())
                .GetReviewContextAsync(term.Id, CancellationToken.None);

            Assert.IsNotNull(context);
            Assert.AreEqual(chosenEpisode.Id, context.EpisodeId);
            Assert.AreEqual("これは魔法だよ。", context.Sentence);
            Assert.AreEqual("Frieren", context.AnimeTitle);
            Assert.AreEqual("1:23", context.TimestampLabel);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task ReviewContextReturnsNullWithoutEpisodeOccurrence()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-review-context-empty-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var term = new Term { Language = "ja", Canonical = "猫" };
            db.Terms.Add(term);
            await db.SaveChangesAsync();

            var context = await new LearningService(db, new FsrsReviewScheduler())
                .GetReviewContextAsync(term.Id, CancellationToken.None);

            Assert.IsNull(context);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }
}
