using System.Text.Json;
using Jularr.Web.Data;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.StoryContext;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

[TestClass]
public sealed class StoryContextTests
{
    [TestMethod]
    public void FirstSeenIsTheEarliestExtractedChapterEvenWhenChaptersArriveOutOfOrder()
    {
        var document = NewDocument();

        Apply(document, 5, Entity("Mira", "character", "A courier."));
        Apply(document, 2, Entity("Mira", "character", null));
        Apply(document, 7, Entity("Mira", "character", "Now a captain."));

        var mira = document.Entities.Single();
        Assert.AreEqual(2, mira.FirstSeenChapter);
        Assert.AreEqual(7, mira.LastChangedChapter);
        Assert.AreEqual("Now a captain.", mira.Description);
    }

    [TestMethod]
    public void BeforeChapterExcludesEverythingIntroducedInOrAfterTheTargetChapter()
    {
        var document = NewDocument();
        Apply(document, 1, Entity("Alice", "character", "Apprentice at the tower."), summary: "Alice arrives at the tower.");
        Apply(document, 2, Entity("Tower", "place", "A tall black tower."), summary: "Alice explores the tower.");
        Apply(document, 3, Entity("The Masked King", "character", "Rules the city."), summary: "The Masked King reveals himself.");
        Apply(document, 4, Entity("Dragon", "creature", "Wakes beneath the city."), summary: "A dragon wakes.");

        var snapshot = StoryContextBuilder.Build(document, StoryContextQuery.Before(3));
        var rendered = StoryContextRenderer.Render(snapshot, 4000);

        CollectionAssert.AreEquivalent(
            new[] { "Alice", "Tower" },
            snapshot.Entities.Select(x => x.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            snapshot.RecentChapters.Select(x => x.Number).ToArray());
        Assert.IsFalse(rendered.Contains("Masked King", StringComparison.Ordinal));
        Assert.IsFalse(rendered.Contains("reveals himself", StringComparison.Ordinal));
        Assert.IsFalse(rendered.Contains("Dragon", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void AfterChapterIncludesTheFinishedChapterButNothingLater()
    {
        var document = NewDocument();
        Apply(document, 1, Entity("Alice", "character", null), summary: "Opening.");
        Apply(document, 2, Entity("Bob", "character", null), summary: "Bob appears.");
        Apply(document, 3, Entity("Carol", "character", null), summary: "Carol appears.");

        var snapshot = StoryContextBuilder.Build(document, StoryContextQuery.After(2));

        CollectionAssert.AreEquivalent(
            new[] { "Alice", "Bob" },
            snapshot.Entities.Select(x => x.Name).ToArray());
        Assert.AreEqual(2, snapshot.RecentChapters.Max(x => x.Number));
    }

    [TestMethod]
    public void ChapterOneHasNoStoryKnowledgeAtAll()
    {
        var document = NewDocument();
        document.Style.OverallStyle = "Gothic";
        document.Themes.Add("Grief");
        document.AnalysisThroughChapter = 3;
        Apply(document, 1, Entity("Alice", "character", "Heroine."), summary: "Alice arrives.");

        var snapshot = StoryContextBuilder.Build(document, StoryContextQuery.Before(1));

        Assert.IsFalse(snapshot.HasStoryKnowledge);
        Assert.AreEqual(0, snapshot.Entities.Count);
        Assert.AreEqual(0, snapshot.RecentChapters.Count);
        Assert.IsNull(snapshot.Style);
        Assert.AreEqual(0, snapshot.Themes.Count);
    }

    [TestMethod]
    public void LaterFactsAboutAKnownEntityDoNotLeakIntoEarlierBoundaries()
    {
        var document = NewDocument();
        Apply(document, 1, Entity("Alice", "character", "A loyal knight.", appearance: "Short red hair."));
        Apply(document, 6, Entity("Alice", "character", "Betrays the queen.", relationships: "Enemy of the queen"));

        var before4 = StoryContextBuilder.Build(document, StoryContextQuery.Before(4)).Entities.Single();
        var before7 = StoryContextBuilder.Build(document, StoryContextQuery.Before(7)).Entities.Single();
        var full = StoryContextBuilder.Build(document, new StoryContextQuery()).Entities.Single();

        Assert.AreEqual("A loyal knight.", before4.Description);
        Assert.AreEqual("Short red hair.", before4.Appearance);
        Assert.IsNull(before4.Relationships);
        Assert.AreEqual("Betrays the queen.", before7.Description);
        Assert.AreEqual("Short red hair.", before7.Appearance);
        Assert.AreEqual("Enemy of the queen", full.Relationships);
    }

    [TestMethod]
    public void AnalysisAndOwnerEntitiesWithUnknownFirstChapterStayOutOfBoundedContext()
    {
        var document = NewDocument();
        StoryContextMerge.ApplySeed(
            document,
            new StorySeedInput(
                "Third person",
                "Dry fantasy",
                null,
                null,
                ["Loss"],
                [Entity("Hidden Heir", "character", "Secretly royal.")],
                [new StoryTermInput("Mana Core", "magic")],
                AnalysisThroughChapter: 3));
        StoryContextMerge.UpsertEntity(
            document,
            Entity("Owner Pick", "character", "Added in the bible editor."));

        var before3 = StoryContextBuilder.Build(document, StoryContextQuery.Before(3));
        var before4 = StoryContextBuilder.Build(document, StoryContextQuery.Before(4));

        Assert.AreEqual(0, before4.Entities.Count);
        Assert.AreEqual(0, before4.Terms.Count);
        Assert.IsNull(before3.Style, "The analysis sample reaches chapter 3, so it is unknown before chapter 3.");
        Assert.AreEqual(0, before3.Themes.Count);
        Assert.AreEqual("Dry fantasy", before4.Style?.OverallStyle);
        CollectionAssert.AreEqual(new[] { "Loss" }, before4.Themes.ToArray());
    }

    [TestMethod]
    public void BoundedSnapshotsNeverExposeAliases()
    {
        var document = NewDocument();
        Apply(document, 1, Entity("The Stranger", "character", null));
        Apply(document, 9, Entity("Prince Aren", "character", "Revealed identity.", aliases: ["The Stranger"]));

        var entity = StoryContextBuilder.Build(document, StoryContextQuery.Before(5)).Entities.Single();

        Assert.AreEqual("The Stranger", entity.Name);
        Assert.AreEqual(0, entity.Aliases.Count);
        Assert.IsNull(entity.Description);
    }

    [TestMethod]
    public void ReapplyingAChapterDeduplicatesChaptersEntitiesAndHistory()
    {
        var document = NewDocument();
        Apply(document, 1, Entity("Alice", "character", "Heroine."), summary: "First pass.");
        Apply(document, 1, Entity("alice", "character", "Heroine."), summary: "Second pass.");
        Apply(document, 2, Entity("Alice", "character", "Heroine."));

        var alice = document.Entities.Single();
        Assert.AreEqual(1, document.Chapters.Count(x => x.Number == 1));
        Assert.AreEqual("Second pass.", document.Chapters.Single(x => x.Number == 1).Summary);
        Assert.AreEqual(1, alice.History.Count, "Unchanged facts must not create another state.");
        Assert.AreEqual(1, alice.LastChangedChapter);
    }

    [TestMethod]
    public void HistoryIsCappedButKeepsTheIntroduction()
    {
        var document = NewDocument();
        for (var chapter = 1; chapter <= 20; chapter++)
        {
            Apply(document, chapter, Entity("Alice", "character", $"State {chapter}."));
        }

        StoryContextMerge.Normalize(document);
        var alice = document.Entities.Single();

        Assert.AreEqual(StoryContextMerge.MaxHistoryPerEntity, alice.History.Count);
        Assert.AreEqual(1, alice.History[0].Chapter);
        Assert.AreEqual(20, alice.History[^1].Chapter);
        Assert.AreEqual(
            "State 1.",
            StoryContextBuilder.Build(document, StoryContextQuery.Before(3)).Entities.Single().Description);
    }

    [TestMethod]
    public void SnapshotHashOnlyChangesWhenKnowledgeInsideTheBoundaryChanges()
    {
        var document = NewDocument();
        Apply(document, 1, Entity("Alice", "character", "Heroine."), summary: "Opening.");
        Apply(document, 2, Entity("Bob", "character", "Rival."), summary: "Rivalry.");

        var first = StoryContextBuilder.Build(document, StoryContextQuery.Before(2)).Hash;

        Apply(document, 5, Entity("Carol", "character", "Late arrival."), summary: "Much later.");
        var afterLaterChange = StoryContextBuilder.Build(document, StoryContextQuery.Before(2)).Hash;

        Apply(document, 1, Entity("Alice", "character", "Heroine with a secret."), summary: "Opening revised.");
        var afterEarlierChange = StoryContextBuilder.Build(document, StoryContextQuery.Before(2)).Hash;

        Assert.AreEqual(first, afterLaterChange);
        Assert.AreNotEqual(first, afterEarlierChange);
    }

    [TestMethod]
    public void RenderedContextRespectsTheCharacterBudgetAndCompactsOlderChapters()
    {
        var document = NewDocument();
        for (var chapter = 1; chapter <= 40; chapter++)
        {
            Apply(
                document,
                chapter,
                Entity($"Person {chapter}", "character", new string('x', 400)),
                summary: $"Chapter {chapter} begins. " + new string('y', 1500));
        }

        var snapshot = StoryContextBuilder.Build(document, StoryContextQuery.Before(41));
        var rendered = StoryContextRenderer.Render(snapshot, StoryContextBudgets.ImagePrompt);

        Assert.IsTrue(rendered.Length <= StoryContextBudgets.ImagePrompt);
        Assert.AreEqual(3, snapshot.RecentChapters.Count);
        Assert.IsTrue(snapshot.EarlierChapters.All(x => x.Summary!.Length <= StoryContextBudgets.EarlierChapterLine));
        Assert.IsTrue(snapshot.Entities.Count <= 24);
    }

    [TestMethod]
    public void TranslationContextOnlyUsesChaptersLeadingIntoTheTranslatedChapter()
    {
        var bible = new BookTranslationBible
        {
            WorkId = Guid.NewGuid(),
            Chapters =
            [
                new(Guid.NewGuid(), 1, "One", "Past one.", null, DateTime.UtcNow),
                new(Guid.NewGuid(), 2, "Two", "Past two.", null, DateTime.UtcNow),
                new(Guid.NewGuid(), 5, "Five", "Future five.", null, DateTime.UtcNow)
            ]
        };

        var context = BookTranslationMemoryStore.RenderRelevantContext(bible, "text", 5000, chapterNumber: 3);

        StringAssert.Contains(context, "Past two.");
        Assert.IsFalse(context.Contains("Future five.", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task LegacyBibleIsMigratedIntoSharedContextWithBackupAndStaysReadable()
    {
        var root = TempDirectory();
        try
        {
            var workId = Guid.NewGuid();
            var legacyPath = Path.Combine(root, workId.ToString("N"), "id.json");
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            var chapterId = Guid.NewGuid();
            await File.WriteAllTextAsync(
                legacyPath,
                JsonSerializer.Serialize(
                    new BookTranslationBible
                    {
                        WorkId = workId,
                        SourceLanguage = "en",
                        TargetLanguage = "id",
                        OverallStyle = "Dry fantasy",
                        Themes = ["Fantasy"],
                        Entities = [new("Alice", "Alicia", "character", "Heroine", "she/her", null, "Dry")],
                        Terms = [new("Mana Core", "Inti Mana", "magic", "Keep title case", Locked: true)],
                        Chapters = [new(chapterId, 1, "Arrival", "Alice arrives.", "Distrusts the headmaster.", DateTime.UtcNow)]
                    },
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

            var store = new BookTranslationMemoryStore(root);
            var bible = await store.LoadAsync(workId, "id", CancellationToken.None);

            Assert.IsNotNull(bible);
            Assert.AreEqual("Dry fantasy", bible.OverallStyle);
            Assert.AreEqual("Alicia", bible.Entities.Single().TargetName);
            Assert.AreEqual("she/her", bible.Entities.Single().Pronouns);
            Assert.IsTrue(bible.Terms.Single().Locked);
            Assert.AreEqual("magic", bible.Terms.Single().Category);
            Assert.AreEqual("Alice arrives.", bible.Chapters.Single().Summary);
            Assert.IsTrue(File.Exists(legacyPath + ".v1.bak"));

            using (var stored = JsonDocument.Parse(await File.ReadAllTextAsync(legacyPath)))
            {
                Assert.AreEqual(2, stored.RootElement.GetProperty("version").GetInt32());
                Assert.IsFalse(stored.RootElement.TryGetProperty("chapters", out _));
            }

            var shared = await store.StoryContext.LoadAsync(workId, CancellationToken.None);
            Assert.IsNotNull(shared);
            Assert.AreEqual(StoryFactOrigins.Legacy, shared.Entities.Single().Origin);
            Assert.IsNull(shared.Entities.Single().FirstSeenChapter);
            Assert.IsNull(shared.AnalysisThroughChapter);

            Assert.AreEqual(0, await store.MigrateLegacyAsync(CancellationToken.None));
            var again = await store.LoadAsync(workId, "id", CancellationToken.None);
            Assert.AreEqual(1, again!.Entities.Count);
            Assert.AreEqual(1, again.Chapters.Count);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task StartupMigrationMovesEveryLegacyBible()
    {
        var root = TempDirectory();
        try
        {
            foreach (var language in new[] { "de", "id" })
            {
                var workId = Guid.NewGuid();
                var path = Path.Combine(root, workId.ToString("N"), language + ".json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(
                    path,
                    $$"""{"version":1,"workId":"{{workId}}","sourceLanguage":"en","targetLanguage":"{{language}}","themes":[],"entities":[],"terms":[],"chapters":[]}""");
            }

            var store = new BookTranslationMemoryStore(root);
            Assert.AreEqual(2, await store.MigrateLegacyAsync(CancellationToken.None));
            Assert.AreEqual(0, await store.MigrateLegacyAsync(CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task LanguagesShareStoryFactsButKeepTheirOwnTargetNames()
    {
        var root = TempDirectory();
        try
        {
            var store = new BookTranslationMemoryStore(root);
            var workId = Guid.NewGuid();

            var german = await store.GetOrCreateAsync(workId, "en", "de", _ => Task.FromResult(BookTranslationBibleSeed.Empty), CancellationToken.None);
            await store.ApplyChapterDeltaAsync(
                german,
                Guid.NewGuid(),
                1,
                "Opening",
                new BookTranslationMemoryDelta(
                    "The gate opens.",
                    null,
                    [new BookTranslationEntity("Silver Gate", "Silbertor", "place", "An ancient gate.", null, null, null)],
                    []),
                CancellationToken.None);

            var indonesian = await store.GetOrCreateAsync(workId, "en", "id", _ => Task.FromResult(BookTranslationBibleSeed.Empty), CancellationToken.None);
            Assert.AreEqual(0, indonesian.Entities.Count, "No Indonesian name exists yet.");
            Assert.AreEqual("The gate opens.", indonesian.Chapters.Single().Summary);

            await store.UpsertEntityAsync(workId, "id", "Silver Gate", "Gerbang Perak", "place", "An ancient gate of silver.", null, null, null, CancellationToken.None);

            var reloadedGerman = await store.LoadAsync(workId, "de", CancellationToken.None);
            var reloadedIndonesian = await store.LoadAsync(workId, "id", CancellationToken.None);
            Assert.AreEqual("Silbertor", reloadedGerman!.Entities.Single().TargetName);
            Assert.AreEqual("Gerbang Perak", reloadedIndonesian!.Entities.Single().TargetName);
            Assert.AreEqual("An ancient gate of silver.", reloadedGerman.Entities.Single().Description);

            await store.ResetAsync(workId, "de", CancellationToken.None);
            Assert.IsNotNull(await store.StoryContext.LoadAsync(workId, CancellationToken.None));
            await store.ResetAsync(workId, "id", CancellationToken.None);
            Assert.IsNull(await store.StoryContext.LoadAsync(workId, CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task TranslationExtractionIsReusedByChapterBoundedConsumers()
    {
        var fixture = await Fixture.CreateAsync(chapterCount: 3);
        try
        {
            var bible = await fixture.Memory.GetOrCreateAsync(fixture.WorkId, "en", "de", _ => Task.FromResult(BookTranslationBibleSeed.Empty), CancellationToken.None);
            await fixture.Memory.ApplyChapterDeltaAsync(
                bible,
                fixture.Chapters[0].Id,
                1,
                "One",
                new BookTranslationMemoryDelta(
                    "Alice finds a map.",
                    null,
                    [new BookTranslationEntity("Alice", "Alice", "character", "A cartographer.", "she/her", null, null)],
                    []),
                CancellationToken.None,
                fixture.Chapters[0].SourceHash);

            var extractor = new RecordingExtractor();
            var service = fixture.Service(extractor);

            var result = await service.ExtractThroughAsync(fixture.WorkId, 2, 10, CancellationToken.None);
            var snapshot = await service.GetSnapshotAsync(fixture.WorkId, StoryContextQuery.Before(3), CancellationToken.None);

            Assert.AreEqual(1, result.Extracted);
            CollectionAssert.AreEqual(new[] { 2 }, extractor.Chapters.ToArray(), "Chapter 1 was already extracted by translation.");
            CollectionAssert.AreEquivalent(new[] { "Alice", "Bram" }, snapshot.Entities.Select(x => x.Name).ToArray());
            StringAssert.Contains(extractor.Contexts[0], "Alice");
            Assert.IsFalse(extractor.Contexts[0].Contains("Bram", StringComparison.Ordinal));

            var second = await service.ExtractThroughAsync(fixture.WorkId, 2, 10, CancellationToken.None);
            Assert.AreEqual(0, second.Extracted);
            Assert.AreEqual(1, extractor.Chapters.Count);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [TestMethod]
    public async Task ExtractionIsIncrementalAndBoundedPerCall()
    {
        var fixture = await Fixture.CreateAsync(chapterCount: 5);
        try
        {
            var extractor = new RecordingExtractor();
            var service = fixture.Service(extractor);

            var first = await service.ExtractThroughAsync(fixture.WorkId, 4, 2, CancellationToken.None);
            var second = await service.ExtractThroughAsync(fixture.WorkId, 4, 2, CancellationToken.None);

            Assert.AreEqual(new StoryContextExtractionResult(2, 2), first);
            Assert.AreEqual(new StoryContextExtractionResult(2, 0), second);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, extractor.Chapters.ToArray());
            Assert.IsTrue(extractor.Texts.All(text => !text.Contains("chapter five", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [TestMethod]
    public async Task UnknownFirstSeenIsResolvedFromChapterTextOnceAndCached()
    {
        var fixture = await Fixture.CreateAsync(chapterCount: 4);
        try
        {
            await fixture.Story.UpdateAsync(
                fixture.WorkId,
                "en",
                document => StoryContextMerge.ApplySeed(
                    document,
                    new StorySeedInput(
                        null,
                        null,
                        null,
                        null,
                        [],
                        [Entity("Bram", "character", "A smith."), Entity("Dara", "character", "A thief.")],
                        [],
                        AnalysisThroughChapter: 3)),
                CancellationToken.None);

            var service = fixture.Service(extractor: null);
            var before3 = await service.GetSnapshotAsync(fixture.WorkId, StoryContextQuery.Before(3), CancellationToken.None);
            var builds = service.SnapshotBuilds;
            var cached = await service.GetSnapshotAsync(fixture.WorkId, StoryContextQuery.Before(3), CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "Bram" }, before3.Entities.Select(x => x.Name).ToArray());
            Assert.IsNull(before3.Entities.Single().Description, "Seed facts have no chapter and stay out.");
            Assert.AreEqual(builds, service.SnapshotBuilds);
            Assert.AreEqual(before3.Hash, cached.Hash);

            var stored = await fixture.Story.LoadAsync(fixture.WorkId, CancellationToken.None);
            Assert.AreEqual(2, stored!.Entities.Single(x => x.Name == "Bram").FirstSeenChapter);
            Assert.IsNull(stored.Entities.Single(x => x.Name == "Dara").FirstSeenChapter);
            Assert.AreEqual(2, stored.Entities.Single(x => x.Name == "Dara").TextScanThrough);

            var before5 = await service.GetSnapshotAsync(fixture.WorkId, StoryContextQuery.Before(5), CancellationToken.None);
            CollectionAssert.AreEquivalent(new[] { "Bram", "Dara" }, before5.Entities.Select(x => x.Name).ToArray());
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [TestMethod]
    public void TextScanMatchesWholeNamesOnly()
    {
        Assert.IsTrue(StoryTextScan.ContainsName("Then Will spoke.", "Will"));
        Assert.IsFalse(StoryTextScan.ContainsName("I will go.", "Will"));
        Assert.IsFalse(StoryTextScan.ContainsName("Willow trees.", "Will"));
        Assert.IsTrue(StoryTextScan.ContainsName("彼は勇者だ。", "勇者"));
        Assert.IsFalse(StoryTextScan.ContainsName("Al went home.", "Al"));
    }

    private static StoryContextDocument NewDocument() =>
        new() { WorkId = Guid.NewGuid(), SourceLanguage = "en" };

    private static StoryEntityInput Entity(
        string name,
        string type,
        string? description,
        string? relationships = null,
        string? appearance = null,
        IReadOnlyList<string>? aliases = null) =>
        new(name, type, description, null, relationships, null, appearance, aliases);

    private static void Apply(
        StoryContextDocument document,
        int chapter,
        StoryEntityInput entity,
        string? summary = null) =>
        StoryContextMerge.ApplyChapter(
            document,
            new StoryChapterInput(
                Guid.NewGuid(),
                chapter,
                $"Chapter {chapter}",
                summary,
                null,
                null,
                "test",
                [entity],
                []));

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "jularr-story-context", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class RecordingExtractor : IStoryContextExtractor
    {
        private static readonly string[] Names = ["Alice", "Bram", "Cyra", "Dara", "Ezra"];

        public string Id => "recording-story";
        public List<int> Chapters { get; } = [];
        public List<string> Contexts { get; } = [];
        public List<string> Texts { get; } = [];

        public Task<StoryChapterExtraction> ExtractChapterAsync(
            StoryChapterExtractionRequest request,
            CancellationToken cancellationToken)
        {
            Chapters.Add(request.ChapterNumber);
            Contexts.Add(request.ExistingContext);
            Texts.Add(request.SourceText);

            return Task.FromResult(
                new StoryChapterExtraction(
                    $"Summary {request.ChapterNumber}.",
                    null,
                    [new StoryExtractedEntity(Names[request.ChapterNumber - 1], "character", null, "Someone.", null, null, "Tall.")],
                    []));
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string databasePath;
        private readonly string root;

        private Fixture(string databasePath, string root, AppDbContext db, Guid workId, IReadOnlyList<NovelChapter> chapters)
        {
            this.databasePath = databasePath;
            this.root = root;
            Db = db;
            WorkId = workId;
            Chapters = chapters;
            Memory = new BookTranslationMemoryStore(root);
            Story = Memory.StoryContext;
        }

        public AppDbContext Db { get; }
        public Guid WorkId { get; }
        public IReadOnlyList<NovelChapter> Chapters { get; }
        public BookTranslationMemoryStore Memory { get; }
        public StoryContextStore Story { get; }

        public StoryContextService Service(IStoryContextExtractor? extractor) =>
            new(Db, Story, new StoryContextSnapshotCache(), extractor);

        public static async Task<Fixture> CreateAsync(int chapterCount)
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"jularr-story-context-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                .Options;
            var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var work = new NovelWork
            {
                SourceProvider = BookCatalogService.ImportedBookProvider,
                SourceKey = "story-context",
                SourceUrl = "upload://story-context.epub",
                Title = "Story Context"
            };
            var volume = new NovelVolume { WorkId = work.Id, Number = 1, Kind = NovelVolumeKinds.Book, SourceKey = "book" };
            string[] texts =
            [
                "Alice unrolled the map in chapter one.",
                "Bram hammered at the forge while Alice watched.",
                "Cyra arrived by ship.",
                "Dara stole the map.",
                "In chapter five everything burned."
            ];

            var chapters = Enumerable.Range(1, chapterCount)
                .Select(number => new NovelChapter
                {
                    WorkId = work.Id,
                    VolumeId = volume.Id,
                    Number = number,
                    Title = $"Chapter {number}",
                    SourceUrl = $"book://story-context/{number}",
                    OriginalText = texts[number - 1],
                    SourceHash = $"hash-{number}"
                })
                .ToArray();

            db.NovelWorks.Add(work);
            db.NovelVolumes.Add(volume);
            db.NovelChapters.AddRange(chapters);
            await db.SaveChangesAsync();

            return new Fixture(databasePath, TempDirectory(), db, work.Id, chapters);
        }

        public void Dispose()
        {
            Db.Dispose();
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
            DeleteDirectory(root);
        }
    }
}
