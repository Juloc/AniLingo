using AniLingo.Web.Data;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class VocabularyAggregationTests
{
    [TestMethod]
    public async Task RebuildAggregatesOccurrencesAndKeepsFirstCueTimestamp()
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-vocab-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(tempRoot, "anilingo.db");
        var dictionaryPath = Path.Combine(tempRoot, "dictionary");

        Directory.CreateDirectory(dictionaryPath);
        await File.WriteAllTextAsync(
            Path.Combine(dictionaryPath, "jmdict-ger.tsv"),
            """
            猫	ねこ	1	Katze
            走る	はしる	1	laufen
            寝る	ねる	1	schlafen
            """);
        await File.WriteAllTextAsync(
            Path.Combine(dictionaryPath, "jmdict-eng-common.tsv"),
            "");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;

            await using var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var anime = new Anime { Key = "test", Title = "Test" };
            var episode = new Episode
            {
                AnimeId = anime.Id,
                SeasonNumber = 1,
                Number = 1,
                Title = "Episode 1"
            };
            var track = new SubtitleTrack
            {
                EpisodeId = episode.Id,
                Path = "/tmp/test.ja.srt",
                Language = "ja",
                Format = "srt",
                SourceUpdatedAt = DateTime.UtcNow
            };

            db.AddRange(anime, episode, track);
            await db.SaveChangesAsync();

            db.SubtitleCues.AddRange(
                new SubtitleCue
                {
                    SubtitleTrackId = track.Id,
                    StartMs = 1_000,
                    EndMs = 2_000,
                    Text = "猫が走る"
                },
                new SubtitleCue
                {
                    SubtitleTrackId = track.Id,
                    StartMs = 5_000,
                    EndMs = 6_000,
                    Text = "猫は寝る"
                });
            await db.SaveChangesAsync();

            var extractor = new JapaneseTermExtractor(
                new StubMorphology(new Dictionary<string, JapaneseMorphToken[]>
                {
                    ["猫が走る"] =
                    [
                        new("猫", "猫", "ネコ", "名詞"),
                        new("が", "が", "ガ", "助詞"),
                        new("走る", "走る", "ハシル", "動詞")
                    ],
                    ["猫は寝る"] =
                    [
                        new("猫", "猫", "ネコ", "名詞"),
                        new("は", "は", "ハ", "助詞"),
                        new("寝る", "寝る", "ネル", "動詞")
                    ]
                }));

            var service = new VocabularyService(
                db,
                extractor,
                new JapaneseDictionary(dictionaryPath));

            await service.RebuildEpisodeAsync(
                episode.Id,
                CancellationToken.None);

            var rows = await (
                from episodeTerm in db.EpisodeTerms.AsNoTracking()
                join term in db.Terms.AsNoTracking()
                    on episodeTerm.TermId equals term.Id
                where episodeTerm.EpisodeId == episode.Id
                orderby term.Canonical
                select new
                {
                    term.Canonical,
                    term.Reading,
                    term.Meaning,
                    episodeTerm.Occurrences,
                    episodeTerm.FirstCueStartMs
                })
                .ToListAsync();

            Assert.AreEqual(3, rows.Count);

            var cat = rows.Single(x => x.Canonical == "猫");
            Assert.AreEqual(2, cat.Occurrences);
            Assert.AreEqual(1_000, cat.FirstCueStartMs);
            Assert.AreEqual("ねこ", cat.Reading);
            Assert.AreEqual("Katze", cat.Meaning);

            Assert.AreEqual(
                1,
                rows.Single(x => x.Canonical == "走る").Occurrences);
            Assert.AreEqual(
                1,
                rows.Single(x => x.Canonical == "寝る").Occurrences);

            await service.RebuildEpisodeAsync(
                episode.Id,
                CancellationToken.None);

            Assert.AreEqual(
                3,
                await db.EpisodeTerms.CountAsync(
                    x => x.EpisodeId == episode.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private sealed class StubMorphology(
        IReadOnlyDictionary<string, JapaneseMorphToken[]> tokens)
        : IJapaneseMorphology
    {
        public IReadOnlyList<JapaneseMorphToken> Analyze(string text) =>
            tokens.GetValueOrDefault(text) ?? [];
    }
}
