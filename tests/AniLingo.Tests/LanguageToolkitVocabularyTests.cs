using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

/// <summary>
/// Covers the remaining #231 work: subtitle term extraction and review context
/// following the actual content language of a subtitle track instead of an
/// assumed Japanese one, while Japanese tracks keep working exactly as before.
/// </summary>
[TestClass]
public sealed class LanguageToolkitVocabularyTests
{
    [TestMethod]
    public async Task RebuildEpisode_ExtractsGenericLanguageTrackWithoutReadingsOrDictionary()
    {
        await using var fixture = await Fixture.CreateAsync();

        var anime = new Anime { Key = "de-show", Title = "German Show" };
        var episode = new Episode { AnimeId = anime.Id, SeasonNumber = 1, Number = 1, Title = "Episode 1" };
        var track = new SubtitleTrack
        {
            EpisodeId = episode.Id,
            Path = "/tmp/test.de.srt",
            Language = "de",
            Format = "srt",
            SourceUpdatedAt = DateTime.UtcNow
        };

        fixture.Db.AddRange(anime, episode, track);
        await fixture.Db.SaveChangesAsync();

        fixture.Db.SubtitleCues.Add(new SubtitleCue
        {
            SubtitleTrackId = track.Id,
            StartMs = 1_000,
            EndMs = 2_000,
            Text = "Der Hund läuft schnell."
        });
        await fixture.Db.SaveChangesAsync();

        await fixture.Vocabulary.RebuildEpisodeAsync(episode.Id, CancellationToken.None);

        var terms = await fixture.Db.EpisodeTerms
            .Join(fixture.Db.Terms, et => et.TermId, t => t.Id, (et, t) => t)
            .Where(t => t.Language == "de")
            .ToListAsync();

        // "der" is a curated German stopword and is filtered out.
        CollectionAssert.AreEquivalent(
            new[] { "hund", "läuft", "schnell" },
            terms.Select(x => x.Canonical).ToArray());
        Assert.IsTrue(terms.All(x => x.Reading == null));
        Assert.IsTrue(terms.All(x => x.Meaning == null));
    }

    [TestMethod]
    public async Task RebuildEpisode_ProcessesEachTrackInItsOwnLanguageIndependently()
    {
        await using var fixture = await Fixture.CreateAsync();

        var anime = new Anime { Key = "mixed-show", Title = "Mixed Show" };
        var episode = new Episode { AnimeId = anime.Id, SeasonNumber = 1, Number = 1, Title = "Episode 1" };
        var japaneseTrack = new SubtitleTrack
        {
            EpisodeId = episode.Id,
            Path = "/tmp/test.ja.srt",
            Language = "ja",
            Format = "srt",
            SourceUpdatedAt = DateTime.UtcNow
        };
        var romanianTrack = new SubtitleTrack
        {
            EpisodeId = episode.Id,
            Path = "/tmp/test.ro.srt",
            Language = "ro",
            Format = "srt",
            SourceUpdatedAt = DateTime.UtcNow
        };

        fixture.Db.AddRange(anime, episode, japaneseTrack, romanianTrack);
        await fixture.Db.SaveChangesAsync();

        fixture.Db.SubtitleCues.AddRange(
            new SubtitleCue
            {
                SubtitleTrackId = japaneseTrack.Id,
                StartMs = 1_000,
                EndMs = 2_000,
                Text = "猫が走る"
            },
            new SubtitleCue
            {
                SubtitleTrackId = romanianTrack.Id,
                StartMs = 1_000,
                EndMs = 2_000,
                Text = "Pisica aleargă repede."
            });
        await fixture.Db.SaveChangesAsync();

        await fixture.Vocabulary.RebuildEpisodeAsync(episode.Id, CancellationToken.None);

        var japaneseTerms = await fixture.Db.Terms.Where(x => x.Language == "ja").ToListAsync();
        var romanianTerms = await fixture.Db.Terms.Where(x => x.Language == "ro").ToListAsync();

        Assert.AreEqual(2, japaneseTerms.Count);
        Assert.AreEqual("ねこ", japaneseTerms.Single(x => x.Canonical == "猫").Reading);

        CollectionAssert.AreEquivalent(
            new[] { "pisica", "aleargă", "repede" },
            romanianTerms.Select(x => x.Canonical).ToArray());
        Assert.IsTrue(romanianTerms.All(x => x.Reading == null));
    }

    [TestMethod]
    public async Task ReviewContext_CarriesTheTermsOwnSourceLanguageNotJapanese()
    {
        await using var fixture = await Fixture.CreateAsync();

        var anime = new Anime { Key = "de-context-show", Title = "German Context Show" };
        var episode = new Episode { AnimeId = anime.Id, SeasonNumber = 1, Number = 1, Title = "Episode 1" };
        var track = new SubtitleTrack
        {
            EpisodeId = episode.Id,
            Path = "/tmp/context.de.srt",
            Language = "de",
            Format = "srt",
            SourceUpdatedAt = DateTime.UtcNow
        };

        fixture.Db.AddRange(anime, episode, track);
        await fixture.Db.SaveChangesAsync();

        fixture.Db.SubtitleCues.Add(new SubtitleCue
        {
            SubtitleTrackId = track.Id,
            StartMs = 3_000,
            EndMs = 4_000,
            Text = "Der Hund läuft schnell."
        });

        var term = new Term { Language = "de", Canonical = "hund", Reading = null, Meaning = "dog" };
        fixture.Db.Terms.Add(term);
        await fixture.Db.SaveChangesAsync();

        fixture.Db.EpisodeTerms.Add(new EpisodeTerm
        {
            EpisodeId = episode.Id,
            TermId = term.Id,
            Occurrences = 1,
            FirstCueStartMs = 3_000
        });
        await fixture.Db.SaveChangesAsync();

        var context = await fixture.Learning.GetReviewContextAsync(term.Id, CancellationToken.None);

        Assert.IsNotNull(context);
        Assert.AreEqual("de", context.Language);
        Assert.AreEqual("Der Hund läuft schnell.", context.Sentence);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory;

        private Fixture(string directory, AppDbContext db)
        {
            this.directory = directory;
            Db = db;
            Vocabulary = new VocabularyService(
                db,
                new JapaneseTermExtractor(new StubMorphology()),
                new JapaneseDictionary(Path.Combine(directory, "dictionary")));
            Learning = new LearningService(db, new FsrsReviewScheduler());
        }

        public AppDbContext Db { get; }
        public VocabularyService Vocabulary { get; }
        public LearningService Learning { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"anilingo-toolkit-vocab-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);

            var dictionaryDirectory = Path.Combine(directory, "dictionary");
            Directory.CreateDirectory(dictionaryDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(dictionaryDirectory, "jmdict-ger.tsv"),
                "猫\tねこ\t1\tKatze\n");
            await File.WriteAllTextAsync(
                Path.Combine(dictionaryDirectory, "jmdict-eng-common.tsv"),
                "");

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "anilingo.db")};Foreign Keys=True")
                .Options;

            var fixture = new Fixture(directory, new AppDbContext(options));
            await DatabaseMigrationBridge.UpgradeAsync(fixture.Db);
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class StubMorphology : IJapaneseMorphology
    {
        public IReadOnlyList<JapaneseMorphToken> Analyze(string text) =>
            text == "猫が走る"
                ?
                [
                    new JapaneseMorphToken("猫", "猫", "ネコ", "名詞"),
                    new JapaneseMorphToken("が", "が", "ガ", "助詞"),
                    new JapaneseMorphToken("走る", "走る", "ハシル", "動詞")
                ]
                : [];
    }
}
