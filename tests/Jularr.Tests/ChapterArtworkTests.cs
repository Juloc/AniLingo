using Jularr.Web.Data;
using Jularr.Web.Features.Ai;
using Jularr.Web.Features.Books;
using Jularr.Web.Features.ChapterArtwork;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.StoryContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SkiaSharp;

namespace Jularr.Tests;

[TestClass]
public sealed class ChapterArtworkTests
{
    // ---- Spoiler boundary: prompt composition -------------------------------------------

    [TestMethod]
    public void ChapterOneIsNeutralEvenWhenTheBookIsFullyAnalyzed()
    {
        var document = StoryDocument();
        var prompt = Compose(document, 1);

        Assert.IsTrue(prompt.Neutral);
        foreach (var secret in new[] { "Mira", "silver braid", "fishing boats", "Ashen King", "burning crown", "Varn" })
        {
            Assert.IsFalse(prompt.Text.Contains(secret, StringComparison.OrdinalIgnoreCase), secret);
        }

        StringAssert.Contains(prompt.Text, "without identifiable characters");
    }

    [TestMethod]
    public void OnlyEntitiesFirstSeenBeforeTheTargetChapterReachThePrompt()
    {
        var document = StoryDocument();

        var chapter3 = Compose(document, 3).Text;
        StringAssert.Contains(chapter3, "misty harbor");
        StringAssert.Contains(chapter3, "silver braid");
        Assert.IsFalse(chapter3.Contains("burning crown", StringComparison.OrdinalIgnoreCase));

        var chapter5 = Compose(document, 5).Text;
        Assert.IsFalse(chapter5.Contains("burning crown", StringComparison.OrdinalIgnoreCase), "First seen in chapter 5 itself.");

        var chapter6 = Compose(document, 6).Text;
        StringAssert.Contains(chapter6, "burning crown");
    }

    [TestMethod]
    public void AppearanceLearnedInOrAfterTheTargetChapterIsNotUsed()
    {
        var document = new StoryContextDocument { WorkId = Guid.NewGuid() };
        Apply(document, 1, new StoryEntityInput("Mira", "character", null, null, null, null, "Plain grey cloak"));
        Apply(document, 4, new StoryEntityInput("Mira", "character", null, null, null, null, "Scarred face and a royal sash"));

        var chapter4 = Compose(document, 4).Text;
        var chapter5 = Compose(document, 5).Text;

        StringAssert.Contains(chapter4, "Plain grey cloak");
        Assert.IsFalse(chapter4.Contains("royal sash", StringComparison.Ordinal));
        StringAssert.Contains(chapter5, "royal sash");
    }

    [TestMethod]
    public void EntitiesWithUnknownFirstChapterNeverReachThePrompt()
    {
        var document = new StoryContextDocument { WorkId = Guid.NewGuid() };
        StoryContextMerge.ApplySeed(
            document,
            new StorySeedInput(null, null, null, null, [], [new StoryEntityInput("Hidden Heir", "character", null, null, null, null, "Golden mask")], [], 3));

        var prompt = Compose(document, 9);

        Assert.IsTrue(prompt.Neutral);
        Assert.IsFalse(prompt.Text.Contains("Golden mask", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ComposerRejectsASnapshotThatIsNotBoundedBeforeTheChapter()
    {
        var document = StoryDocument();
        var after = StoryContextBuilder.Build(document, StoryContextQuery.After(3));

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            ChapterArtworkPromptComposer.Compose(Input(3, after)));
    }

    [TestMethod]
    public void PromptStaysWithinTheImageBudgetAndKeepsTitleSpace()
    {
        var prompt = Compose(StoryDocument(), 3);

        Assert.IsTrue(prompt.Text.Length <= StoryContextBudgets.ImagePrompt);
        StringAssert.Contains(prompt.Text, "No text, letters");
        Assert.AreEqual(32, prompt.ContextHash.Length);
    }

    // ---- Spoiler boundary: second check on the prompt -----------------------------------

    [TestMethod]
    public void GuardForbidsLaterUnknownAndAliasNamesButAllowsKnownOnes()
    {
        var document = StoryDocument();
        var safe = StoryContextBuilder.Build(document, ChapterArtworkPromptComposer.ContextQuery(3));

        var forbidden = ChapterArtworkSpoilerGuard.ForbiddenNames(document, safe, 3, ["The Coronation"], ["The Harbor Book"]);

        CollectionAssert.Contains(forbidden.ToArray(), "Ashen King");
        CollectionAssert.Contains(forbidden.ToArray(), "House Varn");
        CollectionAssert.Contains(forbidden.ToArray(), "The Stranger", "Aliases can reveal identities.");
        CollectionAssert.Contains(forbidden.ToArray(), "The Coronation");
        CollectionAssert.DoesNotContain(forbidden.ToArray(), "Mira");
        CollectionAssert.DoesNotContain(forbidden.ToArray(), "Harbor", "Words from safe metadata are allowed.");
    }

    [TestMethod]
    public void GuardDetectsForbiddenNamesAndPassagesFromTheTargetChapter()
    {
        const string chapterText = "At dawn the Ashen King rode through the northern gate with his banners burning.";

        var named = ChapterArtworkSpoilerGuard.Check("A figure resembling the ashen king at dusk.", ["Ashen King"], []);
        var quoted = ChapterArtworkSpoilerGuard.Check("Scene: the northern gate with his banners burning.", [], [chapterText]);
        var clean = ChapterArtworkSpoilerGuard.Check("A misty harbor at dawn, painterly.", ["Ashen King"], [chapterText]);

        Assert.IsFalse(named.Passed);
        Assert.AreEqual(1, named.NameHits);
        Assert.IsFalse(quoted.Passed);
        Assert.IsTrue(quoted.TextOverlaps > 0);
        Assert.IsTrue(clean.Passed);
    }

    // ---- Service: pipeline, storage and no silent regeneration --------------------------

    [TestMethod]
    public async Task GenerationUsesOnlyEarlierChaptersAndStoresImageWithMetadataOnMediaStorage()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.EnableAsync();

        var request = await fixture.Service.RequestAsync(fixture.WorkId, 3, Fixture.Profile, regenerate: false, CancellationToken.None);
        Assert.AreEqual(ChapterArtworkRequestOutcome.Queued, request.Outcome);

        await fixture.Service.GenerateAsync(request.ArtworkIds, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { 1, 2 }, fixture.Extractor.Chapters.ToArray(), "Only chapters before 3 are analyzed.");
        Assert.IsTrue(fixture.Extractor.Texts.All(text => !text.Contains("coronation", StringComparison.OrdinalIgnoreCase)));

        var prompt = fixture.Images.Prompts.Single();
        Assert.IsFalse(prompt.Contains("Ashen", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(prompt.Contains("coronation", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(prompt, "silver braid");

        var item = (await fixture.Service.GetAsync(request.ArtworkIds[0], CancellationToken.None))!;
        Assert.AreEqual(ChapterArtworkStatus.Preview, item.Status);
        Assert.AreEqual("fake-image-model", item.Model);
        Assert.AreEqual(ChapterArtworkPromptComposer.PromptVersion, item.PromptVersion);
        Assert.AreEqual("before-chapter", item.ContextScope);
        Assert.IsNotNull(item.ContextHash);
        Assert.IsFalse(item.Prompt!.Contains("northern gate", StringComparison.Ordinal), "No chapter text in metadata.");
        StringAssert.StartsWith(item.AssetPath!.Replace('\\', '/'), $"The Harbor Book [{fixture.WorkId.ToString("N")[..8]}]/chapter-art/candidates/");
        Assert.IsTrue(File.Exists(Path.Combine(fixture.MediaRoot, item.AssetPath)));
    }

    [TestMethod]
    public async Task AcceptedImageGetsADeterministicNameAndIsNeverReplacedSilently()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.EnableAsync(variations: 2);

        var first = await fixture.Service.RequestAsync(fixture.WorkId, 2, Fixture.Profile, false, CancellationToken.None);
        Assert.AreEqual(2, first.ArtworkIds.Count);
        await fixture.Service.GenerateAsync(first.ArtworkIds, CancellationToken.None);
        Assert.IsTrue(await fixture.Service.AcceptAsync(first.ArtworkIds[1], CancellationToken.None));

        var accepted = (await fixture.Service.GetAsync(first.ArtworkIds[1], CancellationToken.None))!;
        Assert.AreEqual(ChapterArtworkStatus.Accepted, accepted.Status);
        StringAssert.EndsWith(accepted.AssetPath!.Replace('\\', '/'), "/chapter-art/chapter-0002.webp");
        Assert.IsNull(await fixture.Service.GetAsync(first.ArtworkIds[0], CancellationToken.None), "The other candidate is discarded.");
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.MediaRoot, Path.GetDirectoryName(accepted.AssetPath)!, "candidates")));

        var again = await fixture.Service.RequestAsync(fixture.WorkId, 2, Fixture.Profile, regenerate: false, CancellationToken.None);
        Assert.AreEqual(ChapterArtworkRequestOutcome.AlreadyExists, again.Outcome);
        Assert.AreEqual(1, fixture.Images.Prompts.Count);

        var regenerate = await fixture.Service.RequestAsync(fixture.WorkId, 2, Fixture.Profile, regenerate: true, CancellationToken.None);
        Assert.AreEqual(ChapterArtworkRequestOutcome.Queued, regenerate.Outcome);
        Assert.IsNotNull(await fixture.Service.GetAsync(accepted.Id, CancellationToken.None), "Regenerating keeps the accepted image until a new one is accepted.");

        var reader = await fixture.Service.GetReaderArtworkAsync(fixture.WorkId, 2, Fixture.Profile, CancellationToken.None);
        Assert.AreEqual(accepted.Id, reader?.Id);
        var file = await fixture.Service.OpenImageAsync(accepted, 640, CancellationToken.None);
        Assert.IsNotNull(file);
        StringAssert.StartsWith(Path.GetFullPath(file.Path), Path.GetFullPath(fixture.CacheRoot), "Readers get a cached derivative.");

        Assert.IsTrue(await fixture.Service.RemoveAsync(accepted.Id, CancellationToken.None));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.MediaRoot, accepted.AssetPath)));
        Assert.IsFalse(File.Exists(file.Path));
    }

    [TestMethod]
    public async Task ChapterOneGenerationUsesANeutralPromptWithoutAnyExtraction()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.EnableAsync();

        var request = await fixture.Service.RequestAsync(fixture.WorkId, 1, Fixture.Profile, false, CancellationToken.None);
        await fixture.Service.GenerateAsync(request.ArtworkIds, CancellationToken.None);

        var item = (await fixture.Service.GetAsync(request.ArtworkIds[0], CancellationToken.None))!;
        Assert.IsTrue(item.NeutralPrompt);
        Assert.AreEqual(0, fixture.Extractor.Chapters.Count);
        Assert.IsFalse(fixture.Images.Prompts.Single().Contains("Mira", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task StoryPromptThatFailsTheSpoilerCheckFallsBackToNeutral()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.EnableAsync();

        // Mira is known since chapter 1, but her appearance mentions House Varn, first met in chapter 7.
        await fixture.Story.UpdateAsync(fixture.WorkId, "en", document =>
        {
            Apply(document, 1, new StoryEntityInput("Mira", "character", null, null, null, null, "Wears the crest of House Varn"));
            Apply(document, 7, new StoryEntityInput("House Varn", "faction", "Rules the south.", null, null, null));
            return true;
        }, CancellationToken.None);
        fixture.Extractor.Disabled = true;

        var prepared = await fixture.Service.PreparePromptAsync(fixture.WorkId, 3, ChapterArtworkStyle.Ink, CancellationToken.None);

        Assert.IsTrue(prepared.Prompt.Neutral);
        Assert.IsFalse(prepared.Prompt.Text.Contains("Varn", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FeatureIsOffUntilEnabledAndConfigured()
    {
        using var fixture = await Fixture.CreateAsync();

        var off = await fixture.Service.GetAvailabilityAsync(Fixture.Profile, fixture.WorkId, CancellationToken.None);
        Assert.AreEqual(ChapterArtworkUnavailableReasons.DisabledForProfile, off.Reason);
        Assert.IsFalse(off.CanView);
        Assert.IsNull(await fixture.Service.GetReaderArtworkAsync(fixture.WorkId, 1, Fixture.Profile, CancellationToken.None));

        await fixture.EnableAsync();
        fixture.Images.Available = false;
        var noModel = await fixture.Service.GetAvailabilityAsync(Fixture.Profile, fixture.WorkId, CancellationToken.None);
        Assert.IsTrue(noModel.CanView);
        Assert.IsFalse(noModel.CanGenerate);
        Assert.AreEqual(AiImageUnavailableReasons.NoImageModel, noModel.Reason);

        fixture.Images.Available = true;
        await fixture.ArtworkStore.SaveWorkSettingsAsync(new ChapterArtworkWorkSettings(fixture.WorkId, false, null, null, false), CancellationToken.None);
        var bookOff = await fixture.Service.RequestAsync(fixture.WorkId, 2, Fixture.Profile, false, CancellationToken.None);
        Assert.AreEqual(ChapterArtworkRequestOutcome.Unavailable, bookOff.Outcome);
        Assert.AreEqual(ChapterArtworkUnavailableReasons.DisabledForBook, bookOff.Reason);
    }

    [TestMethod]
    public async Task DeletingTheWorkRemovesArtworkFilesAndMetadata()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.EnableAsync();
        var request = await fixture.Service.RequestAsync(fixture.WorkId, 2, Fixture.Profile, false, CancellationToken.None);
        await fixture.Service.GenerateAsync(request.ArtworkIds, CancellationToken.None);
        var item = (await fixture.Service.GetAsync(request.ArtworkIds[0], CancellationToken.None))!;

        await ChapterArtworkService.DeleteWorkAsync(fixture.Db, fixture.Configuration, fixture.WorkId, CancellationToken.None);

        Assert.IsFalse(File.Exists(Path.Combine(fixture.MediaRoot, item.AssetPath!)));
        Assert.AreEqual(0, (await fixture.Service.ListWorkAsync(fixture.WorkId, CancellationToken.None)).Count);
    }

    // ---- Helpers ------------------------------------------------------------------------

    private static StoryContextDocument StoryDocument()
    {
        var document = new StoryContextDocument { WorkId = Guid.NewGuid() };
        Apply(document, 1, new StoryEntityInput("Harbor", "place", "A misty harbor under grey cliffs.", null, null, null, "misty harbor with fishing boats"));
        Apply(document, 2, new StoryEntityInput("Mira", "character", "A courier.", null, null, null, "Young woman with a silver braid", ["The Stranger"]));
        Apply(document, 5, new StoryEntityInput("Ashen King", "character", "Returns to claim the throne.", null, null, null, "Tall man with a burning crown"));
        Apply(document, 7, new StoryEntityInput("House Varn", "faction", "Rules the south.", null, null, null));
        return document;
    }

    private static void Apply(StoryContextDocument document, int chapter, StoryEntityInput entity) =>
        StoryContextMerge.ApplyChapter(
            document,
            new StoryChapterInput(Guid.NewGuid(), chapter, $"Chapter {chapter}", $"Summary {chapter}.", null, null, "test", [entity], []));

    private static ChapterArtworkPrompt Compose(StoryContextDocument document, int chapter) =>
        ChapterArtworkPromptComposer.Compose(
            Input(chapter, StoryContextBuilder.Build(document, ChapterArtworkPromptComposer.ContextQuery(chapter))));

    private static ChapterArtworkPromptInput Input(int chapter, StoryContextSnapshot snapshot) =>
        new("The Harbor Book", ["Fantasy"], chapter, null, ChapterArtworkStyle.Painterly, null, snapshot);

    private sealed class FakeImages : IAiImageGenerator
    {
        public bool Available { get; set; } = true;
        public List<string> Prompts { get; } = [];

        public Task<AiImageAvailability> GetAvailabilityAsync(string profileId, CancellationToken cancellationToken) =>
            Task.FromResult(
                Available
                    ? new AiImageAvailability(true, "fake", "fake-image-model", null)
                    : AiImageAvailability.Unavailable(AiImageUnavailableReasons.NoImageModel));

        public Task<AiImageGenerationResult> GenerateAsync(string profileId, AiImageRequest request, CancellationToken cancellationToken)
        {
            Prompts.Add(request.Prompt);
            var images = Enumerable.Range(0, request.Count)
                .Select(index => new AiGeneratedImage(Png(index), "image/png"))
                .ToArray();
            return Task.FromResult(new AiImageGenerationResult("fake", "fake-image-model", images));
        }

        private static byte[] Png(int seed)
        {
            using var bitmap = new SKBitmap(96, 64);
            bitmap.Erase(new SKColor((byte)(40 + seed * 60), 90, 140));
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }

    private sealed class FakeExtractor : IStoryContextExtractor
    {
        public string Id => "fake-story";
        public bool Disabled { get; set; }
        public List<int> Chapters { get; } = [];
        public List<string> Texts { get; } = [];

        public Task<StoryChapterExtraction> ExtractChapterAsync(StoryChapterExtractionRequest request, CancellationToken cancellationToken)
        {
            if (Disabled)
            {
                return Task.FromResult(StoryChapterExtraction.Empty);
            }

            Chapters.Add(request.ChapterNumber);
            Texts.Add(request.SourceText);
            StoryExtractedEntity[] entities = request.ChapterNumber switch
            {
                1 => [new("Harbor", "place", null, "A harbor.", null, null, "misty harbor with fishing boats")],
                2 => [new("Mira", "character", null, "A courier.", null, null, "Young woman with a silver braid")],
                _ => []
            };
            return Task.FromResult(new StoryChapterExtraction($"Summary {request.ChapterNumber}.", null, entities, []));
        }
    }

    private sealed class Fixture : IDisposable
    {
        public const string Profile = "owner-profile";
        private readonly string databasePath;
        private readonly string root;

        private Fixture(string databasePath, string root, AppDbContext db, Guid workId)
        {
            this.databasePath = databasePath;
            this.root = root;
            Db = db;
            WorkId = workId;
            MediaRoot = Path.Combine(root, "media");
            CacheRoot = Path.Combine(root, "cache");
            Directory.CreateDirectory(MediaRoot);
            Configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ChapterArtwork:CachePath"] = CacheRoot,
                    ["ChapterArtwork:SettingsPath"] = Path.Combine(root, "settings.json"),
                    ["ChapterArtwork:StorageRoot"] = MediaRoot
                })
                .Build();
            Story = new StoryContextStore(Path.Combine(root, "story"));
            ArtworkStore = new ChapterArtworkStore(db);
            Service = new ChapterArtworkService(
                db,
                ArtworkStore,
                new StoryContextService(db, Story, new StoryContextSnapshotCache(), Extractor),
                Story,
                Images,
                ChapterArtworkGlobalSettingsStore.FromConfiguration(Configuration),
                Configuration);
        }

        public AppDbContext Db { get; }
        public Guid WorkId { get; }
        public string MediaRoot { get; }
        public string CacheRoot { get; }
        public IConfiguration Configuration { get; }
        public StoryContextStore Story { get; }
        public ChapterArtworkStore ArtworkStore { get; }
        public FakeImages Images { get; } = new();
        public FakeExtractor Extractor { get; } = new();
        public ChapterArtworkService Service { get; }

        public Task EnableAsync(int variations = 1) =>
            ArtworkStore.SavePreferencesAsync(
                Profile,
                ChapterArtworkPreferences.Default with { Enabled = true, Variations = variations },
                CancellationToken.None);

        public static async Task<Fixture> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"jularr-chapter-art-{Guid.NewGuid():N}.db");
            var root = Path.Combine(Path.GetTempPath(), "jularr-chapter-art", Guid.NewGuid().ToString("N"));
            var db = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
                    .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);

            var work = new NovelWork
            {
                SourceProvider = BookCatalogService.ImportedBookProvider,
                SourceKey = "chapter-art",
                SourceUrl = "upload://chapter-art.epub",
                Title = "The Harbor Book",
                MetadataGenresJson = "[\"Fantasy\"]"
            };
            var volume = new NovelVolume { WorkId = work.Id, Number = 1, Kind = NovelVolumeKinds.Book, SourceKey = "book" };
            string[] texts =
            [
                "The misty harbor woke slowly.",
                "Mira carried letters through the rain.",
                "At the coronation the Ashen King rode through the northern gate with his banners burning.",
                "Nothing more."
            ];
            db.NovelWorks.Add(work);
            db.NovelVolumes.Add(volume);
            db.NovelChapters.AddRange(texts.Select((text, index) => new NovelChapter
            {
                WorkId = work.Id,
                VolumeId = volume.Id,
                Number = index + 1,
                Title = index == 2 ? "The Coronation" : $"Part {index + 1}",
                SourceUrl = $"book://chapter-art/{index + 1}",
                OriginalText = text,
                SourceHash = $"hash-{index + 1}"
            }));
            await db.SaveChangesAsync();

            return new Fixture(databasePath, root, db, work.Id);
        }

        public void Dispose()
        {
            Db.Dispose();
            File.Delete(databasePath);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
