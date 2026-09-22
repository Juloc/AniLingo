using AniLingo.Web.Data;
using AniLingo.Web.Features.Ai;
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
    public void PreprocessorFindsHighConfidenceGrammarWithoutAi()
    {
        var prepared = JapaneseSentencePreprocessor.Prepare(
            "行かなければならないけど、やってみることにする。");

        CollectionAssert.Contains(
            prepared.LocalHints.ToArray(),
            "なければならない = müssen");
        CollectionAssert.Contains(
            prepared.LocalHints.ToArray(),
            "けど = aber / obwohl");
        CollectionAssert.Contains(
            prepared.LocalHints.ToArray(),
            "てみる = versuchsweise etwas tun");
        CollectionAssert.Contains(
            prepared.LocalHints.ToArray(),
            "ことにする = sich entscheiden, etwas zu tun");
    }

    [TestMethod]
    public void PreprocessorFindsBoundedMultiPartPatterns()
    {
        var prepared = JapaneseSentencePreprocessor.Prepare(
            "水しか飲まない。休んだり勉強したりする。");

        CollectionAssert.Contains(
            prepared.LocalHints.ToArray(),
            "しか…ない = nur / nichts außer");
        CollectionAssert.Contains(
            prepared.LocalHints.ToArray(),
            "たり…たりする = Beispiele von Handlungen aufzählen");
    }

    [TestMethod]
    public void PreprocessorAvoidsAmbiguousBroadRules()
    {
        var prepared = JapaneseSentencePreprocessor.Prepare(
            "とてもいい。そうだね。ここから行こう。見たい。");

        Assert.IsFalse(
            prepared.LocalHints.Any(hint =>
                hint.Contains("たい", StringComparison.Ordinal)
                || hint.Contains("そう", StringComparison.Ordinal)
                || hint.Contains("から", StringComparison.Ordinal)
                || hint.Contains("てもいい", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task CacheReadNeverCallsProvider()
    {
        var databasePath = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(databasePath);
            var fake = new FakeExplainer();
            var service = new AiSentenceExplanationService(db, fake);

            var cached = await service.GetCachedAsync(
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
            var fake = new FakeExplainer
            {
                Result = new AiSentenceExplanation(
                    "Ich habe noch nicht gegessen.",
                    ["Kurze Grammatik.", "Extra.", "Dritter Punkt.", "Ignoriert."],
                    ["Umgangssprachlich.", "Zweiter.", "Ignoriert."],
                    FromCache: false)
            };
            var service = new AiSentenceExplanationService(db, fake);

            var first = await service.ExplainAsync(
                "まだ食べてないんだ。",
                CancellationToken.None);
            var second = await service.ExplainAsync(
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
    public async Task EquivalentWhitespaceSharesOneSentenceCacheEntry()
    {
        var databasePath = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(databasePath);
            var fake = new FakeExplainer();
            var service = new AiSentenceExplanationService(db, fake);

            await service.ExplainAsync(
                "まだ\n食べてない。",
                CancellationToken.None);
            await service.ExplainAsync(
                "  まだ 食べてない。  ",
                CancellationToken.None);

            Assert.AreEqual(1, fake.Calls);
            Assert.AreEqual(1, await db.AiSentenceExplanationCache.CountAsync());
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
