using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

/// <summary>
/// Every learning surface reads the canonical Learning cards.
/// </summary>
[TestClass]
public sealed class LearningSurfaceTests
{
    private const string Profile = "reader";

    [TestMethod]
    public async Task SurfacesReadStateFromLearningCards()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"anilingo-learning-surfaces-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "jmdict-ger.tsv"), "");
        File.WriteAllText(Path.Combine(directory, "jmdict-eng-common.tsv"), "");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "anilingo.db")};Foreign Keys=True")
                .Options;
            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var anime = new Anime { Key = "surface", Title = "Surface" };
            var episode = new Episode
            {
                AnimeId = anime.Id,
                SeasonNumber = 1,
                Number = 1,
                Title = "Episode 1",
                DiscoveredAt = DateTime.UtcNow
            };
            var cat = new Term { Language = "ja", Canonical = "猫", Reading = "ねこ", Meaning = "Katze" };
            var dog = new Term { Language = "ja", Canonical = "犬", Reading = "いぬ", Meaning = "Hund" };
            var sky = new Term { Language = "ja", Canonical = "空", Reading = "そら", Meaning = "Himmel" };
            var track = new SubtitleTrack
            {
                EpisodeId = episode.Id,
                Path = "/tmp/surface.ja.srt",
                Language = "ja",
                Format = "srt",
                SourceUpdatedAt = DateTime.UtcNow
            };

            db.AddRange(anime, episode, cat, dog, sky, track);
            db.EpisodeTerms.AddRange(
                new EpisodeTerm { EpisodeId = episode.Id, TermId = cat.Id, Occurrences = 6, FirstCueStartMs = 1_000 },
                new EpisodeTerm { EpisodeId = episode.Id, TermId = dog.Id, Occurrences = 3, FirstCueStartMs = 2_000 },
                new EpisodeTerm { EpisodeId = episode.Id, TermId = sky.Id, Occurrences = 1, FirstCueStartMs = 3_000 });
            db.SubtitleCues.Add(new SubtitleCue
            {
                SubtitleTrackId = track.Id,
                StartMs = 1_000,
                EndMs = 2_000,
                Text = "これは猫です。"
            });

            var now = DateTime.UtcNow;
            await LearningTestData.SeedTermCardAsync(db, Profile, cat, UserTermState.Known);
            await LearningTestData.SeedTermCardAsync(
                db,
                Profile,
                dog,
                UserTermState.Learning,
                nextReviewAt: now.AddMinutes(-5),
                learningStartedAt: now.AddDays(-1));
            await LearningTestData.SeedTermCardAsync(db, "someone-else", sky, UserTermState.Known);

            var account = TestAccounts.Context(Profile);
            var learning = LearningTestData.Service(db, Profile);

            // Episode preparation / coverage.
            var preparation = await new EpisodePreparationService(db, learning, account)
                .GetAsync(episode.Id, anime.Id, CancellationToken.None);
            Assert.AreEqual(UserTermState.Known, preparation.Terms.Single(x => x.TermId == cat.Id).State);
            Assert.AreEqual(UserTermState.Learning, preparation.Terms.Single(x => x.TermId == dog.Id).State);
            Assert.IsNull(preparation.Terms.Single(x => x.TermId == sky.Id).State);
            Assert.AreEqual(90, preparation.PreparedPercent);

            // Home learning widget and coverage.
            var configuration = new LearningConfigurationStore(db);
            await configuration.SetModeAsync(Profile, LearningScopeRef.Profile, LearningMode.Study, CancellationToken.None);
            await configuration.SetCapabilityOverrideAsync(
                Profile,
                LearningScopeRef.Profile,
                LearningCapability.HomeWidget,
                true,
                CancellationToken.None);
            var home = new AniLingo.Web.Pages.IndexModel(db, account);
            await home.OnGetAsync(CancellationToken.None);
            Assert.AreEqual(1, home.DueReviews);
            Assert.AreEqual(9, home.RecentEpisodes.Single().PreparedOccurrences);

            // Learning hub counters.
            var hub = new AniLingo.Web.Pages.Learn.IndexModel(db, account);
            await hub.OnGetAsync(CancellationToken.None);
            Assert.AreEqual(1, hub.DueReviews);
            Assert.AreEqual(1, hub.KnownTerms);
            Assert.AreEqual(1, hub.LearningTerms);

            // Vocabulary list, search and state filter.
            var vocabulary = new AniLingo.Web.Pages.Learn.VocabularyModel(db, learning, account);
            await vocabulary.OnGetAsync("Hund", null, null, CancellationToken.None);
            var row = vocabulary.Items.Single();
            Assert.AreEqual("犬", row.Text);
            Assert.AreEqual("いぬ", row.Reading);
            Assert.AreEqual("Hund", row.Meaning);
            Assert.IsTrue(row.Due);

            await vocabulary.OnGetAsync(null, "known", null, CancellationToken.None);
            Assert.AreEqual("猫", vocabulary.Items.Single().Text);

            // Sentence practice prefers studied words from the primary course.
            var sentences = await new SentencePracticeService(
                    db,
                    Profile,
                    new SurfaceMorphology(),
                    new JapaneseDictionary(directory))
                .LoadAsync(5, CancellationToken.None);
            Assert.AreEqual("猫", sentences.Single().TargetCanonical);

            // Course settings list the course with its word count.
            var courses = new AniLingo.Web.Pages.Settings.LearningCoursesModel(db, account);
            await courses.OnGetAsync(CancellationToken.None);
            var course = courses.Courses.Single();
            Assert.AreEqual(2, courses.WordCounts[course.Id]);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class SurfaceMorphology : IJapaneseMorphology
    {
        public IReadOnlyList<JapaneseMorphToken> Analyze(string text) =>
            text == "これは猫です。"
                ? [
                    new("これ", "これ", "コレ", "名詞"),
                    new("は", "は", "ハ", "助詞"),
                    new("猫", "猫", "ネコ", "名詞"),
                    new("です", "です", "デス", "助動詞"),
                    new("。", "。", "。", "記号")
                ]
                : [];
    }
}
