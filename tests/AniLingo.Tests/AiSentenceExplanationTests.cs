using AniLingo.Web.Data;
using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

[TestClass]
public sealed class AiSentenceExplanationTests
{
    [TestMethod]
    public void PreprocessorNormalizesAndFindsCommonAnimeContractions()
    {
        var prepared = JapaneseSentencePreprocessor.Prepare(
            "  まだ\n食べてないんだ。  ");

        Assert.AreEqual("まだ 食べてないんだ。", prepared.Sentence);
        CollectionAssert.Contains(prepared.LocalHints.ToArray(), "てない→ていない");
        CollectionAssert.Contains(prepared.LocalHints.ToArray(), "んだ→のだ");
    }

    [TestMethod]
    public async Task CacheReadNeverCallsProvider()
    {
        var databasePath = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(databasePath);
            var term = new Term
            {
                Language = "ja",
                Canonical = "食べる",
                Reading = "たべる",
                Meaning = "essen"
            };
            db.Terms.Add(term);
            await db.SaveChangesAsync();

            var fake = new FakeExplainer();
            var service = new AiSentenceExplanationService(db, fake);

            var cached = await service.GetCachedAsync(
                term.Id,
                "まだ食べてない。",
                CancellationToken.None);

            Assert.IsNull(cached);
            Assert.AreEqual(0, fake.Calls);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task RepeatedExplanationCallsProviderOnceAndUsesCache()
    {
        var databasePath = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(databasePath);
            var term = new Term
            {
                Language = "ja",
                Canonical = "食べる",
                Reading = "たべる",
                Meaning = "essen"
            };
            db.Terms.Add(term);
            await db.SaveChangesAsync();

            var fake = new FakeExplainer
            {
                Result = new AiSentenceExplanation(
                    "Ich habe noch nicht gegessen.",
                    ["てない is the casual negative progressive form.", "extra", "third", "ignored"],
                    ["んだ adds explanatory nuance.", "second", "ignored"],
                    FromCache: false)
            };
            var service = new AiSentenceExplanationService(db, fake);

            var first = await service.ExplainAsync(
                term.Id,
                "まだ食べてないんだ。",
                CancellationToken.None);
            var second = await service.ExplainAsync(
                term.Id,
                "まだ食べてないんだ。",
                CancellationToken.None);

            Assert.AreEqual(1, fake.Calls);
            Assert.IsFalse(first.FromCache);
            Assert.IsTrue(second.FromCache);
            Assert.AreEqual(3, first.Grammar.Count);
            Assert.AreEqual(2, first.Colloquial.Count);
            Assert.AreEqual(1, await db.AiSentenceExplanationCache.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    [TestMethod]
    public async Task LocalMeaningChangesInvalidateTheCacheKey()
    {
        var databasePath = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(databasePath);
            var term = new Term
            {
                Language = "ja",
                Canonical = "掛ける",
                Meaning = "hängen"
            };
            db.Terms.Add(term);
            await db.SaveChangesAsync();

            var fake = new FakeExplainer();
            var service = new AiSentenceExplanationService(db, fake);

            await service.ExplainAsync(term.Id, "電話を掛ける。", CancellationToken.None);

            term.Meaning = "anrufen";
            await db.SaveChangesAsync();

            await service.ExplainAsync(term.Id, "電話を掛ける。", CancellationToken.None);

            Assert.AreEqual(2, fake.Calls);
            Assert.AreEqual(2, await db.AiSentenceExplanationCache.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    private static string TempDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"anilingo-ai-{Guid.NewGuid():N}.db");

    private static async Task<AppDbContext> CreateDatabaseAsync(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options;

        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }

    private sealed class FakeExplainer : IAiSentenceExplainer
    {
        public string Id => "fake";
        public int Calls { get; private set; }

        public AiSentenceExplanation Result { get; set; } =
            new(
                "Testübersetzung",
                ["Kurze Grammatik."],
                [],
                FromCache: false);

        public Task<AiSentenceExplanation> ExplainSentenceAsync(
            AiSentenceExplainRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }
}
