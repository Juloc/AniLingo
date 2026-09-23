using AniLingo.Web.Data;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Novels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace AniLingo.Tests;

[TestClass]
public sealed class NovelTests
{
    [TestMethod]
    public void NcodeParserExtractsJapaneseBodyWithoutRubyAnnotations()
    {
        const string html = """
            <html>
              <head><title>Fallback title - 無職転生</title></head>
              <body>
                <h1 class="p-novel__title">第百五十七話「覚悟」</h1>
                <div class="p-novel__body">
                  <p>最近、<ruby>ルディ<rt>Rudy</rt></ruby>の様子がおかしい。</p>
                  <p>でも、大丈夫だ。</p>
                </div>
              </body>
            </html>
            """;

        var chapter = NcodeNovelSourceProvider.ParseChapterHtml(
            html,
            "n9669bk",
            171,
            "https://ncode.syosetu.com/n9669bk/171/");

        Assert.AreEqual("第百五十七話「覚悟」", chapter.Title);
        Assert.AreEqual(
            "最近、ルディの様子がおかしい。\n\nでも、大丈夫だ。",
            chapter.OriginalText);
        Assert.IsFalse(chapter.OriginalText.Contains("Rudy", StringComparison.Ordinal));
    }

    [TestMethod]
    public void NcodeParserExtractsOrderedChapterLinks()
    {
        const string html = """
            <a href="/n9669bk/172/">第百五十八話「次」</a>
            <a href="/n9669bk/171/">第百五十七話「覚悟」</a>
            <a href="/other/1/">Ignore</a>
            """;

        var chapters = NcodeNovelSourceProvider.ParseChapterLinks(html, "n9669bk");

        Assert.AreEqual(2, chapters.Count);
        Assert.AreEqual(171, chapters[0].Number);
        Assert.AreEqual(172, chapters[1].Number);
    }

    [TestMethod]
    public async Task NcodeWorkImportStopsAtLastLinkedTocPage()
    {
        var requested = new List<string>();

        using var httpClient = new HttpClient(
            new DelegateHttpMessageHandler(request =>
            {
                var uri = request.RequestUri
                    ?? throw new AssertFailedException("Narou request URI was missing.");
                requested.Add(uri.ToString());

                var html = uri.PathAndQuery switch
                {
                    "/n9669bk/" => """
                        <html>
                          <head><title>無職転生 - 小説家になろう</title></head>
                          <body>
                            <h1 class="p-novel__title">無職転生</h1>
                            <a href="/n9669bk/1/">One</a>
                            <a href="?p=2">Next</a>
                            <a href="?p=3">Last</a>
                          </body>
                        </html>
                        """,
                    "/n9669bk/?p=2" => """
                        <html>
                          <body>
                            <a href="/n9669bk/101/">One hundred one</a>
                            <a href="?p=1">Previous</a>
                            <a href="?p=3">Next</a>
                          </body>
                        </html>
                        """,
                    "/n9669bk/?p=3" => """
                        <html>
                          <body>
                            <a href="/n9669bk/201/">Two hundred one</a>
                            <a href="?p=1">First</a>
                            <a href="?p=2">Previous</a>
                          </body>
                        </html>
                        """,
                    _ => throw new AssertFailedException(
                        $"Unexpected Narou TOC request: {uri}")
                };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(html)
                };
            }));

        var provider = new NcodeNovelSourceProvider(
            httpClient,
            NullLogger<NcodeNovelSourceProvider>.Instance);

        var work = await provider.GetWorkAsync(
            new Uri("https://ncode.syosetu.com/n9669bk"),
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                "https://ncode.syosetu.com/n9669bk/",
                "https://ncode.syosetu.com/n9669bk/?p=2",
                "https://ncode.syosetu.com/n9669bk/?p=3"
            },
            requested);
        Assert.AreEqual(3, work.Chapters.Count);
    }

    [TestMethod]
    public async Task ReimportingWorkIsIdempotent()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var provider = new FakeNovelSourceProvider();
            var service = new NovelService(db, [provider]);

            var first = await service.ImportWorkAsync(
                "https://example.invalid/work",
                CancellationToken.None);
            var second = await service.ImportWorkAsync(
                "https://example.invalid/work",
                CancellationToken.None);

            Assert.AreEqual(first, second);
            Assert.AreEqual(1, await db.NovelWorks.CountAsync());
            Assert.AreEqual(2, await db.NovelChapters.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task CompletedTranslationIsCachedBySourceHashAndProvider()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);
            var work = new NovelWork
            {
                SourceProvider = "fake",
                SourceKey = "work",
                SourceUrl = "https://example.invalid/work",
                Title = "Test"
            };
            var chapter = new NovelChapter
            {
                WorkId = work.Id,
                Number = 1,
                SourceUrl = "https://example.invalid/work/1",
                Title = "One",
                OriginalText = "これはテストです。",
                SourceHash = "SOURCE-A"
            };
            db.Add(work);
            db.Add(chapter);
            await db.SaveChangesAsync();

            var source = new FakeNovelSourceProvider();
            var translator = new FakeNovelTranslator();
            var novelService = new NovelService(db, [source]);
            var service = new NovelTranslationService(db, novelService, translator);

            var first = await service.TranslateChapterAsync(
                chapter.Id,
                "de",
                CancellationToken.None);
            var second = await service.TranslateChapterAsync(
                chapter.Id,
                "de",
                CancellationToken.None);

            Assert.AreEqual("Das ist ein Test.", first.Text);
            Assert.AreEqual(first.Id, second.Id);
            Assert.AreEqual(1, translator.Calls);
            Assert.AreEqual(1, await db.NovelTranslations.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task ManualEpisodeMappingRejectsMissingEpisodeRange()
    {
        var path = TempDatabasePath();

        try
        {
            await using var db = await CreateDatabaseAsync(path);

            var work = new NovelWork
            {
                SourceProvider = "fake",
                SourceKey = "work",
                SourceUrl = "https://example.invalid/work",
                Title = "Novel"
            };
            db.Add(work);
            db.AddRange(
                new NovelChapter
                {
                    WorkId = work.Id,
                    Number = 1,
                    SourceUrl = "https://example.invalid/work/1",
                    Title = "One"
                },
                new NovelChapter
                {
                    WorkId = work.Id,
                    Number = 2,
                    SourceUrl = "https://example.invalid/work/2",
                    Title = "Two"
                });

            var anime = new Anime { Key = "anime", Title = "Anime" };
            db.Add(anime);
            db.Add(new AnimeMetadata
            {
                AnimeId = anime.Id,
                Provider = "anilist",
                ExternalId = "123",
                PreferredTitle = "Anime"
            });
            db.Add(new Episode
            {
                AnimeId = anime.Id,
                SeasonNumber = 1,
                Number = 1,
                Title = "Episode 1"
            });
            await db.SaveChangesAsync();

            var service = new NovelMappingService(db, new FakeMappingSuggester());

            await service.AddManualAsync(
                work.Id,
                anime.Id,
                1,
                2,
                1,
                1,
                1,
                null,
                CancellationToken.None);

            Assert.AreEqual(1, await db.NovelAnimeMappings.CountAsync());

            var invalidRangeRejected = false;
            try
            {
                await service.AddManualAsync(
                    work.Id,
                    anime.Id,
                    1,
                    2,
                    1,
                    1,
                    99,
                    null,
                    CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                invalidRangeRejected = true;
            }

            Assert.IsTrue(invalidRangeRejected);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [TestMethod]
    public void TranslationChunkingPreservesAllTextWithinBounds()
    {
        var text = string.Join(
            "\n\n",
            Enumerable.Range(1, 30).Select(i => $"段落{i}。" + new string('あ', 80)));

        var chunks = NovelTranslationService.ChunkText(text, 500);

        Assert.IsTrue(chunks.Count > 1);
        Assert.IsTrue(chunks.All(x => x.Length <= 500));
        Assert.AreEqual(
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim(),
            string.Join("\n\n", chunks));
    }

    private static string TempDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"anilingo-novels-{Guid.NewGuid():N}.db");

    private static async Task<AppDbContext> CreateDatabaseAsync(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Foreign Keys=True")
            .Options;

        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);
        return db;
    }

    private sealed class DelegateHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class FakeNovelSourceProvider : INovelSourceProvider
    {
        public string Key => "fake";

        public bool CanHandle(Uri sourceUri) =>
            sourceUri.Host.Equals("example.invalid", StringComparison.OrdinalIgnoreCase);

        public Task<NovelSourceWorkSnapshot> GetWorkAsync(
            Uri sourceUri,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new NovelSourceWorkSnapshot(
                    Key,
                    "work",
                    "https://example.invalid/work",
                    "Test Novel",
                    "Author",
                    null,
                    [
                        new NovelSourceChapterReference(
                            1,
                            "One",
                            "https://example.invalid/work/1"),
                        new NovelSourceChapterReference(
                            2,
                            "Two",
                            "https://example.invalid/work/2")
                    ]));

        public Task<NovelSourceChapterSnapshot> GetChapterAsync(
            Uri sourceUri,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new NovelSourceChapterSnapshot(
                    1,
                    "One",
                    sourceUri.ToString(),
                    "これはテストです。"));
    }

    private sealed class FakeNovelTranslator : INovelTranslator
    {
        public string Id => "fake-translator";
        public int Calls { get; private set; }

        public Task<string> TranslateAsync(
            string japaneseText,
            string targetLanguage,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult("Das ist ein Test.");
        }
    }

    private sealed class FakeMappingSuggester : INovelMappingSuggester
    {
        public string Id => "fake-mapper";

        public Task<IReadOnlyList<NovelMappingSuggestion>> SuggestMappingsAsync(
            NovelMappingSuggestionRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NovelMappingSuggestion>>([]);
    }
}
