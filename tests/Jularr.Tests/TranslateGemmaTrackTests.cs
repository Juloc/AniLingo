using System.Net;
using System.Text.Json;
using Jularr.Web.Data;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.OfflineLibrary;
using Jularr.Web.Features.ReaderCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jularr.Tests;

/// <summary>
/// #487: the local TranslateGemma track stays separate from the AI "de" track, honours its
/// configured timeout, treats a malformed endpoint as "not configured" and keeps the paragraphs
/// it already translated when a later one fails.
/// </summary>
[TestClass]
public sealed class TranslateGemmaTrackTests
{
    private const string Endpoint = "http://localhost:11434/v1/chat/completions";

    [TestMethod]
    public async Task LocalTextNeverReachesTheAiTrackInOfflineExportSearchOrCounters()
    {
        await using var fixture = await Fixture.CreateAsync("これは猫です。");
        var service = fixture.CreateService(
            new DelegateHandler(_ => Completion("Lokale Katze.")));

        await service.TranslateChapterAsync(
            fixture.ChapterId,
            NovelReadingLanguage.German,
            NovelTranslationEngine.TranslateGemma,
            CancellationToken.None);

        var stored = await fixture.Db.NovelTranslations.AsNoTracking().SingleAsync();
        Assert.AreEqual(NovelReadingLanguage.GermanTranslateGemma, stored.TargetLanguage);

        var catalog = new NovelCatalogQueries(fixture.Db);
        var library = await catalog.GetLibraryAsync("reader", CancellationToken.None);
        Assert.AreEqual(0, library.Single().TranslatedChapterCount);
        var detail = await catalog.GetWorkDetailAsync(fixture.WorkId, CancellationToken.None);
        Assert.IsFalse(detail!.Chapters.Single().HasTranslation);
        var window = await catalog.GetChapterWindowAsync(
            fixture.WorkId, 1, null, null, null, 10, CancellationToken.None);
        Assert.IsFalse(window.Items.Single().HasTranslation);

        var germanHits = await ReaderTextSearch.SearchWorkAsync(
            fixture.Db, fixture.WorkId, "Katze", NovelReadingLanguage.German, CancellationToken.None);
        Assert.AreEqual(0, germanHits.Count, "German search must not jump to local-track text.");

        await fixture.AddAiTranslationAsync("KI Katze.");

        var offline = new OfflineLibraryQueries(
            fixture.Db,
            new NovelVolumeAssetStore(new DirectoryInfo(fixture.Directory)));
        var payload = await offline.GetChapterPayloadAsync(fixture.ChapterId, CancellationToken.None);
        Assert.AreEqual(
            "KI Katze.",
            payload!.Translations.Single(x => x.TargetLanguage == NovelReadingLanguage.German).Text);
        Assert.AreEqual(
            "Lokale Katze.",
            payload.Translations.Single(x => x.TargetLanguage == NovelReadingLanguage.GermanTranslateGemma).Text);

        germanHits = await ReaderTextSearch.SearchWorkAsync(
            fixture.Db, fixture.WorkId, "Katze", NovelReadingLanguage.German, CancellationToken.None);
        Assert.AreEqual("KI Katze.", germanHits.Single().Snippet);

        library = await catalog.GetLibraryAsync("reader", CancellationToken.None);
        Assert.AreEqual(1, library.Single().TranslatedChapterCount);
    }

    [TestMethod]
    public async Task MigrationMovesExistingLocalRowsToTheirOwnTrack()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jularr-gemma-migration-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "jularr.db")};Foreign Keys=True")
                .Options);

            var migrations = db.Database.GetMigrations().ToList();
            var index = migrations.IndexOf("20260928140000_SeparateTranslateGemmaTrack");
            Assert.IsTrue(index > 0, "The track migration must be registered.");
            await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);

            var chapterId = await SeedLegacyChapterAsync(db, "本。");
            db.NovelTranslations.AddRange(
                LegacyRow(chapterId, "fake-ai", "KI."),
                LegacyRow(chapterId, NovelTranslationProviders.TranslateGemmaPrefix + "model:abc", "Lokal."));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await DatabaseMigrationBridge.UpgradeAsync(db);

            var rows = await db.NovelTranslations.AsNoTracking().ToDictionaryAsync(x => x.Text, x => x.TargetLanguage);
            Assert.AreEqual(NovelReadingLanguage.German, rows["KI."]);
            Assert.AreEqual(NovelReadingLanguage.GermanTranslateGemma, rows["Lokal."]);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            System.IO.Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ConfiguredTimeoutIsReadAndMalformedEndpointMeansNotConfigured()
    {
        var options = TranslateGemmaOptions.FromConfiguration(Configuration(new()
        {
            ["TranslateGemma:Endpoint"] = Endpoint,
            ["TranslateGemma:TimeoutMinutes"] = "45"
        }));
        Assert.AreEqual(TimeSpan.FromMinutes(45), options!.RequestTimeout);

        var logger = new RecordingLogger();
        foreach (var malformed in new[] { "localhost:11434/v1/chat/completions", "not a url", "ftp://host/v1" })
        {
            Assert.IsNull(TranslateGemmaOptions.FromConfiguration(
                Configuration(new() { ["TranslateGemma:Endpoint"] = malformed }),
                logger));
        }

        Assert.IsTrue(
            logger.Warnings.Any(x => x.Contains("TranslateGemma:Endpoint", StringComparison.Ordinal)),
            "An invalid endpoint must be reported as a warning.");
    }

    [TestMethod]
    public async Task MalformedEndpointDoesNotBreakTheService()
    {
        await using var fixture = await Fixture.CreateAsync("本。");
        var service = fixture.CreateService(
            new DelegateHandler(_ => throw new AssertFailedException("No request expected.")),
            "localhost:11434/v1/chat/completions");

        Assert.IsFalse(service.TranslateGemmaConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.TranslateChapterAsync(
                fixture.ChapterId,
                NovelReadingLanguage.German,
                NovelTranslationEngine.TranslateGemma,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task RequestsUseTheConfiguredTimeoutInsteadOfTheHttpClientDefault()
    {
        var factory = new RecordingFactory(new DelegateHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Completion("never");
        }));
        var translator = new TranslateGemmaNovelTranslator(
            factory,
            new TranslateGemmaOptions(new Uri(Endpoint), "model", 1200, TimeSpan.FromMilliseconds(200))
            {
                RetryDelay = TimeSpan.Zero
            });

        var error = await Assert.ThrowsExactlyAsync<TranslateGemmaTransientException>(() =>
            translator.TranslateParagraphsAsync(["本。"], "de", new string?[1], CancellationToken.None));

        StringAssert.Contains(error.Message, "did not answer within");
        Assert.AreEqual(TranslateGemmaNovelTranslator.HttpClientName, factory.Names.First());
        Assert.IsTrue(
            factory.Clients.All(x => x.Timeout == Timeout.InfiniteTimeSpan),
            "HttpClient's 100 s default must not cut off a slow local model before TimeoutMinutes.");
        Assert.AreEqual(3, factory.Names.Count, "A timed-out paragraph is retried.");
    }

    [TestMethod]
    public async Task TransientParagraphFailureIsRetried()
    {
        var calls = 0;
        var translator = new TranslateGemmaNovelTranslator(
            new RecordingFactory(new DelegateHandler(_ => ++calls == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Completion("Eins."))),
            new TranslateGemmaOptions(new Uri(Endpoint), "model", 1200, TimeSpan.FromMinutes(1))
            {
                RetryDelay = TimeSpan.Zero
            });

        var translated = new string?[1];
        await translator.TranslateParagraphsAsync(["一。"], "de", translated, CancellationToken.None);

        Assert.AreEqual("Eins.", translated[0]);
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task FailedParagraphKeepsTheFinishedOnesForTheNextRun()
    {
        await using var fixture = await Fixture.CreateAsync("一。\n\n二。\n\n三。");
        var firstRunRequests = new List<string>();
        var failing = fixture.CreateService(new DelegateHandler(request =>
        {
            var source = SourceText(request);
            firstRunRequests.Add(source);
            // 二 is rejected (not transient), so the chapter fails after paragraph one.
            return source.Contains('二')
                ? new HttpResponseMessage(HttpStatusCode.BadRequest)
                : Completion(source.Contains('一') ? "Eins." : "Drei.");
        }));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failing.TranslateChapterAsync(
                fixture.ChapterId,
                NovelReadingLanguage.German,
                NovelTranslationEngine.TranslateGemma,
                CancellationToken.None));
        Assert.AreEqual(0, await fixture.Db.NovelTranslations.CountAsync());

        var secondRunRequests = new List<string>();
        var working = fixture.CreateService(new DelegateHandler(request =>
        {
            var source = SourceText(request);
            secondRunRequests.Add(source);
            return Completion(source.Contains('二') ? "Zwei." : "Drei.");
        }));

        var completed = await working.TranslateChapterAsync(
            fixture.ChapterId,
            NovelReadingLanguage.German,
            NovelTranslationEngine.TranslateGemma,
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "Eins.", "Zwei.", "Drei." },
            NovelTextLayout.SplitParagraphs(completed.Text).ToArray());
        Assert.IsFalse(
            secondRunRequests.Any(x => x.Contains('一')),
            "The paragraph finished before the failure must not be requested again.");
    }

    [TestMethod]
    public async Task GroupedParagraphsAreHalvedUntilTheAnswerKeepsTheParagraphCount()
    {
        var requests = new List<string>();
        var translator = new TranslateGemmaNovelTranslator(
            new RecordingFactory(new DelegateHandler(request =>
            {
                var source = SourceText(request);
                requests.Add(source);
                var parts = source[(source.LastIndexOf(":\n\n\n", StringComparison.Ordinal) + 4)..]
                    .Split("\n\n");
                // This model merges any group of more than two paragraphs into one.
                return Completion(parts.Length > 2
                    ? string.Join(" ", parts.Select(Translate))
                    : string.Join("\n\n", parts.Select(Translate)));
            })),
            new TranslateGemmaOptions(new Uri(Endpoint), "model", 1200, TimeSpan.FromMinutes(1)));

        string[] source = ["「一」", "「二」", "「三」", "「四」"];
        var translated = new string?[source.Length];
        await translator.TranslateParagraphsAsync(source, "de", translated, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "Eins", "Zwei", "Drei", "Vier" }, translated);
        Assert.AreEqual(3, requests.Count, "One group of four, then two accepted halves.");
        StringAssert.StartsWith(requests[0], "You are a professional Japanese (ja) to German (de-DE) translator.");

        static string Translate(string paragraph) => paragraph switch
        {
            "「一」" => "Eins",
            "「二」" => "Zwei",
            "「三」" => "Drei",
            _ => "Vier"
        };
    }

    [TestMethod]
    public async Task SceneBreaksAreCopiedAndAnswersNeverAddParagraphs()
    {
        var requests = new List<string>();
        var translator = new TranslateGemmaNovelTranslator(
            new RecordingFactory(new DelegateHandler(request =>
            {
                requests.Add(SourceText(request));
                return Completion("Erster Satz.\n\nZweiter Satz.");
            })),
            new TranslateGemmaOptions(new Uri(Endpoint), "model", 1200, TimeSpan.FromMinutes(1)));

        string[] source = ["◇◇◇", "長い段落です。"];
        var translated = new string?[source.Length];
        await translator.TranslateParagraphsAsync(source, "de", translated, CancellationToken.None);

        Assert.AreEqual("◇◇◇", translated[0], "A scene break has nothing to translate.");
        Assert.AreEqual("Erster Satz.\nZweiter Satz.", translated[1]);
        Assert.AreEqual(1, requests.Count);
        Assert.AreEqual("en", TranslateGemmaNovelTranslator.DetectSourceLanguage("Hello there."));
        Assert.IsNull(TranslateGemmaNovelTranslator.DetectSourceLanguage("……！？"));
    }

    [TestMethod]
    public void ProviderIdChangesWithModelAndEndpoint()
    {
        var baseline = TranslateGemmaNovelTranslator.BuildProviderId(
            new TranslateGemmaOptions(new Uri(Endpoint), "translategemma-12b-it", 1200, TimeSpan.FromMinutes(1)));

        StringAssert.StartsWith(baseline, NovelTranslationProviders.TranslateGemmaPrefix + "translategemma-12b-it:");
        Assert.AreNotEqual(baseline, TranslateGemmaNovelTranslator.BuildProviderId(
            new TranslateGemmaOptions(new Uri(Endpoint), "translategemma-4b-it", 1200, TimeSpan.FromMinutes(1))));
        Assert.AreNotEqual(baseline, TranslateGemmaNovelTranslator.BuildProviderId(
            new TranslateGemmaOptions(new Uri("http://translategemma:11434/v1/chat/completions"), "translategemma-12b-it", 1200, TimeSpan.FromMinutes(1))));
    }

    private static string SourceText(HttpRequestMessage request)
    {
        using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        var content = body.RootElement.GetProperty("messages")[0].GetProperty("content");
        return content.ValueKind == JsonValueKind.String
            ? content.GetString()!
            : content[0].GetProperty("text").GetString()!;
    }

    private static HttpResponseMessage Completion(string text) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = text } } } }),
                System.Text.Encoding.UTF8,
                "application/json")
        };

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static NovelTranslation LegacyRow(Guid chapterId, string providerId, string text) => new()
    {
        ChapterId = chapterId,
        TargetLanguage = NovelReadingLanguage.German,
        ProviderId = providerId,
        PromptVersion = 1,
        SourceHash = "hash",
        Text = text
    };

    private static async Task<Guid> SeedChapterAsync(AppDbContext db, string text)
    {
        var work = new NovelWork
        {
            SourceProvider = "test",
            SourceKey = Guid.NewGuid().ToString("N"),
            SourceUrl = "https://example.invalid/gemma",
            Title = "Gemma Track Novel"
        };
        var volume = new NovelVolume { WorkId = work.Id, Number = 1, SourceKey = "web" };
        var chapter = new NovelChapter
        {
            WorkId = work.Id,
            VolumeId = volume.Id,
            Number = 1,
            SourceUrl = "https://example.invalid/gemma/1",
            Title = "Chapter One",
            OriginalText = text,
            SourceHash = "hash"
        };
        db.AddRange(work, volume, chapter);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return chapter.Id;
    }

    private static async Task<Guid> SeedLegacyChapterAsync(AppDbContext db, string text)
    {
        var workId = Guid.NewGuid();
        var volumeId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // This test intentionally migrates to a historic schema. Seed it with
        // its own columns so newer model properties cannot leak into the test.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "NovelWorks" (
                "Id", "SourceProvider", "SourceKey", "SourceUrl", "Title", "ImportedAt", "UpdatedAt")
            VALUES ({workId}, {"test"}, {workId.ToString("N")}, {"https://example.invalid/gemma"},
                {"Gemma Track Novel"}, {now}, {now});
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "NovelVolumes" (
                "Id", "WorkId", "Number", "Kind", "SourceKey", "ImportedAt", "UpdatedAt")
            VALUES ({volumeId}, {workId}, {1}, {NovelVolumeKinds.Web}, {"web"}, {now}, {now});
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "NovelChapters" (
                "Id", "WorkId", "VolumeId", "Number", "SourceUrl", "Title", "OriginalText", "SourceHash",
                "ImportedAt", "UpdatedAt")
            VALUES ({chapterId}, {workId}, {volumeId}, {1}, {"https://example.invalid/gemma/1"},
                {"Chapter One"}, {text}, {"hash"}, {now}, {now});
            """);

        return chapterId;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string directory, AppDbContext db, Guid workId, Guid chapterId)
        {
            Directory = directory;
            Db = db;
            WorkId = workId;
            ChapterId = chapterId;
        }

        public string Directory { get; }
        public AppDbContext Db { get; }
        public Guid WorkId { get; }
        public Guid ChapterId { get; }

        public static async Task<Fixture> CreateAsync(string text)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"jularr-gemma-track-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "jularr.db")};Foreign Keys=True")
                .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var chapterId = await SeedChapterAsync(db, text);
            var workId = await db.NovelChapters.Where(x => x.Id == chapterId).Select(x => x.WorkId).SingleAsync();
            return new Fixture(directory, db, workId, chapterId);
        }

        public NovelTranslationService CreateService(HttpMessageHandler handler, string endpoint = Endpoint) =>
            new(
                Db,
                new NovelImportService(Db, []),
                new UnusedTranslator(),
                new RecordingFactory(handler),
                Configuration(new()
                {
                    ["TranslateGemma:Endpoint"] = endpoint,
                    ["TranslateGemma:Model"] = "translategemma-12b-it"
                }));

        public async Task AddAiTranslationAsync(string text)
        {
            Db.NovelTranslations.Add(new NovelTranslation
            {
                ChapterId = ChapterId,
                TargetLanguage = NovelReadingLanguage.German,
                ProviderId = "fake-ai",
                PromptVersion = NovelTranslationService.PromptVersion,
                SourceHash = "hash",
                Text = text
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler;

        public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
            this.handler = (request, _) => Task.FromResult(handler(request));

        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
            this.handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }

    private sealed class RecordingFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public List<string> Names { get; } = [];
        public List<HttpClient> Clients { get; } = [];

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(handler, disposeHandler: false);
            Names.Add(name);
            Clients.Add(client);
            return client;
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }

    private sealed class UnusedTranslator : INovelTranslator
    {
        public string Id => "unused";

        public Task<string> TranslateAsync(string japaneseText, string targetLanguage, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The AI translator is not used in these tests.");
    }
}
