using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Features.Vocabulary;
using AniLingo.Web.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniLingo.Tests;

// End-to-end subtitle-acquisition behaviour once the content language is not
// Japanese: local sidecars are tagged/consumed for the resolved target
// language, and Jimaku/Whisper (both Japanese-only by construction) are
// skipped rather than silently mis-tagging or mis-transcribing other-language
// content as Japanese.
[TestClass]
public sealed class SubtitleLanguageAcquisitionTests
{
    [TestMethod]
    public void JimakuStageIsExcludedWhenTheContentLanguageIsNotJapanese()
    {
        var stages = LearningTextFallbackPolicy.Build(
            jimakuConfigured: true,
            jimakuEligibleForLanguage: false);

        CollectionAssert.DoesNotContain(stages.ToArray(), LearningTextFallbackStage.Jimaku);
        CollectionAssert.Contains(stages.ToArray(), LearningTextFallbackStage.Whisper);
    }

    [TestMethod]
    public void JimakuStageStaysIncludedWhenConfiguredAndTheContentLanguageIsJapanese()
    {
        var stages = LearningTextFallbackPolicy.Build(
            jimakuConfigured: true,
            jimakuEligibleForLanguage: true);

        CollectionAssert.Contains(stages.ToArray(), LearningTextFallbackStage.Jimaku);
    }

    [TestMethod]
    public async Task GermanTaggedSidecarBecomesTheGermanLearningTrackWhenGermanIsTheContentLanguage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCourseAsync("reader", "de", "en");
        fixture.WriteSidecar("de", "Guten Morgen");

        await fixture.Importer.PrepareLearningTextAsync(fixture.EpisodeId, CancellationToken.None);

        var track = await fixture.Db.SubtitleTracks.AsNoTracking().SingleAsync();
        Assert.AreEqual("de", track.Language);
        Assert.AreEqual(1, await fixture.Db.SubtitleCues.CountAsync(x => x.SubtitleTrackId == track.Id));

        var state = fixture.Importer.GetPreparationState(fixture.EpisodeId);
        Assert.AreEqual(LearningTextPreparationStatus.Ready, state.Status);
        Assert.AreEqual(LearningTextSourceKind.LocalSubtitle, state.Source);
    }

    [TestMethod]
    public async Task SkipsJimakuAndWhisperForNonJapaneseContentWithNoLocalOrEmbeddedSource()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCourseAsync("reader", "de", "en");

        await fixture.Importer.PrepareLearningTextAsync(fixture.EpisodeId, CancellationToken.None);

        Assert.AreEqual(0, await fixture.Db.SubtitleTracks.CountAsync());
        var state = fixture.Importer.GetPreparationState(fixture.EpisodeId);
        Assert.AreEqual(LearningTextPreparationStatus.Failed, state.Status);

        // Whisper is Japanese-only; it must never have been invoked for German content.
        Assert.AreEqual(
            AudioTranscriptionStatus.None,
            fixture.EmbeddedSubtitleExtractor.GetAudioTranscriptionState(fixture.MediaPath).Status);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string tempRoot;

        private Fixture(
            string tempRoot,
            string mediaPath,
            AppDbContext db,
            Guid episodeId,
            SubtitleImportService importer,
            EmbeddedSubtitleExtractor embeddedSubtitleExtractor)
        {
            this.tempRoot = tempRoot;
            MediaPath = mediaPath;
            Db = db;
            EpisodeId = episodeId;
            Importer = importer;
            EmbeddedSubtitleExtractor = embeddedSubtitleExtractor;
        }

        public string MediaPath { get; }
        public AppDbContext Db { get; }
        public Guid EpisodeId { get; }
        public SubtitleImportService Importer { get; }
        public EmbeddedSubtitleExtractor EmbeddedSubtitleExtractor { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                $"anilingo-lang-acquisition-{Guid.NewGuid():N}");
            var seasonDirectory = Path.Combine(tempRoot, "anime", "Alltag", "Season 01");
            var dictionaryPath = Path.Combine(tempRoot, "dictionary");
            Directory.CreateDirectory(seasonDirectory);
            Directory.CreateDirectory(dictionaryPath);

            var mediaPath = Path.Combine(seasonDirectory, "Alltag - S01E01.mkv");
            await File.WriteAllBytesAsync(mediaPath, [0]);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(tempRoot, "anilingo.db")};Foreign Keys=True")
                .Options;
            var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var root = new LibraryRoot { Name = "Anime", Path = Path.Combine(tempRoot, "anime") };
            var anime = new Anime { Title = "Alltag", Key = "alltag" };
            var episode = new Episode { AnimeId = anime.Id, Number = 1, Title = "Episode 1" };
            var media = new MediaFile
            {
                LibraryRootId = root.Id,
                EpisodeId = episode.Id,
                Path = mediaPath,
                SizeBytes = 1,
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(mediaPath)
            };
            db.LibraryRoots.Add(root);
            db.Anime.Add(anime);
            db.Episodes.Add(episode);
            db.MediaFiles.Add(media);
            await db.SaveChangesAsync();

            var vocabulary = new VocabularyService(
                db,
                new JapaneseTermExtractor(new NoMorphology()),
                new JapaneseDictionary(dictionaryPath));
            var inventory = MediaInventoryTestSupport.Create(options);
            var embedded = new EmbeddedSubtitleExtractor(
                new MediaProcessRunner(NullLogger<MediaProcessRunner>.Instance),
                inventory,
                NullLogger<EmbeddedSubtitleExtractor>.Instance);
            var importer = new SubtitleImportService(db, vocabulary, embedded);

            return new Fixture(tempRoot, mediaPath, db, episode.Id, importer, embedded);
        }

        public async Task AddCourseAsync(string profileId, string sourceLanguage, string targetLanguage)
        {
            var now = DateTime.UtcNow;
            Db.LearningCourses.Add(new LearningCourse
            {
                ProfileId = profileId,
                Name = $"{sourceLanguage} → {targetLanguage}",
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage,
                IsEnabled = true,
                IsPrimary = true,
                CreatedAt = now,
                UpdatedAt = now
            });
            await Db.SaveChangesAsync();
        }

        public string WriteSidecar(string languageTag, string text)
        {
            var path = Path.ChangeExtension(MediaPath, null) + $".{languageTag}.srt";
            File.WriteAllText(path, $"1\n00:00:01,000 --> 00:00:03,000\n{text}\n");
            return path;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private sealed class NoMorphology : IJapaneseMorphology
    {
        public IReadOnlyList<JapaneseMorphToken> Analyze(string text) => [];
    }
}
