using AniLingo.Web.Data;
using AniLingo.Web.Features.Ai;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Features.Learning.LanguageAssistance;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Tests;

/// <summary>
/// #233: the shared language inspector resolves capabilities per source scope,
/// returns token/reading/meaning/state, changes card state only with Study
/// capabilities, records source contexts once and reuses cached explanations.
/// </summary>
[TestClass]
public sealed class LanguageInspectorTests
{
    [TestMethod]
    public async Task OffRefusesEveryInspectorCall()
    {
        await using var fixture = await LanguageInspectorFixture.CreateAsync();

        await Assert.ThrowsExactlyAsync<LanguageAssistanceDeniedException>(() =>
            fixture.Inspector().InspectAsync(
                new LanguageInspectRequest("猫がいる", fixture.HubContext()),
                CancellationToken.None));
        await Assert.ThrowsExactlyAsync<LanguageAssistanceDeniedException>(() =>
            fixture.Inspector().SetStateAsync(
                new LanguageWordStateRequest("猫", "saved", fixture.HubContext()),
                CancellationToken.None));
        await Assert.ThrowsExactlyAsync<LanguageAssistanceDeniedException>(() =>
            fixture.Inspector().ExplainAsync(
                new LanguageExplainRequest("猫がいる。", fixture.HubContext()),
                CancellationToken.None));

        Assert.AreEqual(0, fixture.Explainer.Calls);
    }

    [TestMethod]
    public async Task LanguageToolsReturnsTokensReadingsAndMeaningsWithoutStudyActions()
    {
        await using var fixture = await LanguageInspectorFixture.CreateAsync();
        await fixture.SetProfileModeAsync(LearningMode.LanguageTools);
        await fixture.SeedTermStateAsync("猫", UserTermState.Learning);

        var inspection = await fixture.Inspector().InspectAsync(
            new LanguageInspectRequest("猫がいる", fixture.AnimeContext(cueStartMs: 1_000)),
            CancellationToken.None);

        Assert.AreEqual("ja", inspection.Language);
        Assert.AreEqual("猫がいる", string.Concat(inspection.Tokens.Select(x => x.Surface)));
        var cat = inspection.Tokens.Single(x => x.Surface == "猫");
        Assert.IsTrue(cat.Interactive);
        Assert.AreEqual("猫", cat.Canonical);
        Assert.AreEqual("ねこ", cat.Reading);
        Assert.AreEqual("Katze", cat.Meaning);
        Assert.AreEqual("de", cat.MeaningLanguage);
        Assert.IsNull(cat.State, "Language Tools never expose card state.");
        Assert.IsTrue(inspection.Features.Lookup);
        Assert.IsTrue(inspection.Features.Readings);
        Assert.IsTrue(inspection.Features.Explanation);
        Assert.IsFalse(inspection.Features.Save);
        Assert.IsFalse(inspection.Features.Learn);

        var cardsBefore = await fixture.Db.LearningCards.CountAsync();
        await Assert.ThrowsExactlyAsync<LanguageAssistanceDeniedException>(() =>
            fixture.Inspector().SetStateAsync(
                new LanguageWordStateRequest("犬", "saved", fixture.AnimeContext(1_000)),
                CancellationToken.None));
        Assert.AreEqual(cardsBefore, await fixture.Db.LearningCards.CountAsync());
        Assert.IsFalse(await fixture.Db.Terms.AnyAsync(x => x.Canonical == "犬"));
    }

    [TestMethod]
    public async Task ReadingsAndMeaningsFollowTheirOwnCapabilities()
    {
        await using var fixture = await LanguageInspectorFixture.CreateAsync();
        await fixture.SetProfileModeAsync(LearningMode.Custom);
        await fixture.SetProfileCapabilityAsync(LearningCapability.ReadingAids, true);

        var inspection = await fixture.Inspector().InspectAsync(
            new LanguageInspectRequest("猫がいる", fixture.HubContext()),
            CancellationToken.None);

        var cat = inspection.Tokens.Single(x => x.Surface == "猫");
        Assert.AreEqual("ねこ", cat.Reading);
        Assert.IsNull(cat.Meaning, "Lookup is off.");
        Assert.IsFalse(inspection.Features.Explanation);
        Assert.IsNull(inspection.Explanation);
    }

    [TestMethod]
    public async Task StudySavesKnowsAndIgnoresThroughCanonicalTransitions()
    {
        await using var fixture = await LanguageInspectorFixture.CreateAsync();
        await fixture.SetProfileModeAsync(LearningMode.Study);
        var inspector = fixture.Inspector();

        var saved = await inspector.SetStateAsync(
            new LanguageWordStateRequest("猫", "saved", fixture.AnimeContext(1_000, "猫がいる")),
            CancellationToken.None);
        Assert.AreEqual("saved", saved.State);
        Assert.IsTrue(saved.ContextRecorded);

        var card = await fixture.WordCardAsync("猫");
        Assert.AreEqual(UserTermState.Saved, card.State);
        Assert.IsNull(card.NextReviewAt);
        Assert.IsNull(card.QueuePosition);
        var term = await fixture.Db.Terms.SingleAsync(x => x.Canonical == "猫");
        Assert.AreEqual("ねこ", term.Reading);
        Assert.AreEqual("Katze", term.Meaning);

        var inspection = await inspector.InspectAsync(
            new LanguageInspectRequest("猫がいる", fixture.AnimeContext(1_000)),
            CancellationToken.None);
        Assert.AreEqual("saved", inspection.Tokens.Single(x => x.Surface == "猫").State);
        Assert.IsTrue(inspection.Features.Save);
        Assert.IsTrue(inspection.Features.Learn);

        await inspector.SetStateAsync(
            new LanguageWordStateRequest("猫", "learning", fixture.AnimeContext(1_000)),
            CancellationToken.None);
        card = await fixture.WordCardAsync("猫");
        Assert.AreEqual(UserTermState.Learning, card.State);
        Assert.IsNotNull(card.QueuePosition, "Learning queues the new card.");

        var known = await inspector.SetStateAsync(
            new LanguageWordStateRequest("猫", "known", fixture.AnimeContext(1_000)),
            CancellationToken.None);
        Assert.IsFalse(known.ContextRecorded);
        Assert.AreEqual(UserTermState.Known, (await fixture.WordCardAsync("猫")).State);

        await inspector.SetStateAsync(
            new LanguageWordStateRequest("猫", "ignored", fixture.HubContext()),
            CancellationToken.None);
        card = await fixture.WordCardAsync("猫");
        Assert.AreEqual(UserTermState.Ignored, card.State);
        Assert.IsNull(card.QueuePosition);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            inspector.SetStateAsync(
                new LanguageWordStateRequest("猫", "suspended", fixture.HubContext()),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task VocabularyWithoutReviewsSavesButNeverQueuesLearning()
    {
        await using var fixture = await LanguageInspectorFixture.CreateAsync();
        await fixture.SetProfileModeAsync(LearningMode.Custom);
        await fixture.SetProfileCapabilityAsync(LearningCapability.LanguageLookup, true);
        await fixture.SetProfileCapabilityAsync(LearningCapability.Vocabulary, true);

        var inspection = await fixture.Inspector().InspectAsync(
            new LanguageInspectRequest("猫", fixture.HubContext()),
            CancellationToken.None);
        Assert.IsTrue(inspection.Features.Save);
        Assert.IsFalse(inspection.Features.Learn);

        await Assert.ThrowsExactlyAsync<LanguageAssistanceDeniedException>(() =>
            fixture.Inspector().SetStateAsync(
                new LanguageWordStateRequest("猫", "learning", fixture.HubContext()),
                CancellationToken.None));
        Assert.AreEqual(0, await fixture.Db.LearningCards.CountAsync());

        await fixture.Inspector().SetStateAsync(
            new LanguageWordStateRequest("猫", "saved", fixture.HubContext()),
            CancellationToken.None);
        var card = await fixture.WordCardAsync("猫");
        Assert.AreEqual(UserTermState.Saved, card.State);
        Assert.IsNull(card.NextReviewAt);
        Assert.IsNull(card.QueuePosition);
    }

    [TestMethod]
    public async Task CapabilitiesResolveForTheInspectedSourceScope()
    {
        await using var fixture = await LanguageInspectorFixture.CreateAsync();
        await new LearningConfigurationStore(fixture.Db).SetModeAsync(
            LanguageInspectorFixture.Profile,
            LearningScopeRef.ForWork(LearningMediaType.Anime, fixture.AnimeId.ToString()),
            LearningMode.Study,
            CancellationToken.None);

        await Assert.ThrowsExactlyAsync<LanguageAssistanceDeniedException>(() =>
            fixture.Inspector().InspectAsync(
                new LanguageInspectRequest("猫", fixture.HubContext()),
                CancellationToken.None));
        await Assert.ThrowsExactlyAsync<LanguageAssistanceDeniedException>(() =>
            fixture.Inspector().InspectAsync(
                new LanguageInspectRequest("猫", fixture.BookContext(0)),
                CancellationToken.None));

        var inspection = await fixture.Inspector().InspectAsync(
            new LanguageInspectRequest("猫", fixture.AnimeContext(1_000)),
            CancellationToken.None);
        Assert.IsTrue(inspection.Features.Save, "The anime work scope enables Study.");

        await Assert.ThrowsExactlyAsync<KeyNotFoundException>(() =>
            fixture.Inspector().InspectAsync(
                new LanguageInspectRequest(
                    "猫",
                    new LanguageInspectContext("ja", "anime", Guid.NewGuid().ToString())),
                CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            fixture.Inspector().InspectAsync(
                new LanguageInspectRequest("猫", new LanguageInspectContext("ja", "podcast", fixture.EpisodeId.ToString())),
                CancellationToken.None));
    }

    [TestMethod]
    public async Task SavingRecordsEachSourceContextOncePerProfile()
    {
        await using var fixture = await LanguageInspectorFixture.CreateAsync();
        await fixture.SetProfileModeAsync(LearningMode.Study);
        await fixture.SetProfileModeAsync(LearningMode.Study, LanguageInspectorFixture.OtherProfile);
        await fixture.AddMangaChapterAsync();

        async Task SaveAsync(LanguageInspectContext context, string profile = LanguageInspectorFixture.Profile) =>
            await fixture.Inspector(profile).SetStateAsync(
                new LanguageWordStateRequest("猫", "saved", context),
                CancellationToken.None);

        await SaveAsync(fixture.AnimeContext(1_000, "猫がいる"));
        await SaveAsync(fixture.AnimeContext(1_000, "猫がいる"));
        await fixture.Inspector().SetStateAsync(
            new LanguageWordStateRequest("猫", "learning", fixture.AnimeContext(1_000, "猫がいる")),
            CancellationToken.None);
        await SaveAsync(fixture.AnimeContext(2_000, "猫だ"));
        await SaveAsync(fixture.BookContext(3, "本の猫。"));
        await SaveAsync(fixture.BookContext(3, "本の猫。"));
        await SaveAsync(fixture.MangaContext(4, 2, "猫！"));
        await SaveAsync(fixture.AnimeContext(1_000, "猫がいる"), LanguageInspectorFixture.OtherProfile);

        var contexts = await fixture.Db.LearningContexts
            .AsNoTracking()
            .Where(x => x.ProfileId == LanguageInspectorFixture.Profile)
            .OrderBy(x => x.SourceType)
            .ThenBy(x => x.PositionKey)
            .ToListAsync();

        CollectionAssert.AreEqual(
            new[]
            {
                $"anime|episode:{fixture.EpisodeId}|cue:1000|猫がいる",
                $"anime|episode:{fixture.EpisodeId}|cue:2000|猫だ",
                $"book|chapter:{fixture.ChapterId}|paragraph:3|本の猫。",
                $"manga|chapter:{fixture.MangaChapterId}|page:4#region:2|猫！"
            },
            contexts.Select(x => $"{x.SourceType}|{x.SourceKey}|{x.PositionKey}|{x.Text}").ToArray());
        Assert.AreEqual(
            1,
            await fixture.Db.LearningContexts.CountAsync(x => x.ProfileId == LanguageInspectorFixture.OtherProfile));

        var unitId = contexts[0].UnitId;
        var store = new LearningCourseStore(fixture.Db);
        Assert.AreEqual(4, (await store.ListContextsAsync(LanguageInspectorFixture.Profile, unitId, CancellationToken.None)).Count);
        Assert.AreEqual(1, (await store.ListContextsAsync(LanguageInspectorFixture.OtherProfile, unitId, CancellationToken.None)).Count);
    }

    [TestMethod]
    public void AnchorsMapEverySourceOntoLearningContextsAndBack()
    {
        var content = Guid.NewGuid();
        var cases = new[]
        {
            (Anchor: new LanguageSourceAnchor(LanguageSourceType.Anime, "work", content, new LanguageSourcePosition(CueStartMs: 754_000)),
                Media: LearningMediaType.Anime, Key: $"episode:{content}", Position: "cue:754000"),
            (Anchor: new LanguageSourceAnchor(LanguageSourceType.Novel, "work", content, new LanguageSourcePosition(Paragraph: 8)),
                Media: LearningMediaType.Novel, Key: $"chapter:{content}", Position: "paragraph:8"),
            (Anchor: new LanguageSourceAnchor(LanguageSourceType.Book, "work", content, new LanguageSourcePosition(Paragraph: 0)),
                Media: LearningMediaType.Book, Key: $"chapter:{content}", Position: "paragraph:0"),
            (Anchor: new LanguageSourceAnchor(LanguageSourceType.Manga, "work", content, new LanguageSourcePosition(Page: 17, Region: 2)),
                Media: LearningMediaType.Manga, Key: $"chapter:{content}", Position: "page:17#region:2"),
            (Anchor: new LanguageSourceAnchor(LanguageSourceType.Manga, "work", content, new LanguageSourcePosition(Page: 3)),
                Media: LearningMediaType.Manga, Key: $"chapter:{content}", Position: "page:3")
        };

        foreach (var item in cases)
        {
            Assert.AreEqual(item.Key, item.Anchor.SourceKey);
            Assert.AreEqual(item.Position, item.Anchor.PositionKey);
            Assert.AreEqual(item.Media, item.Anchor.Scope.MediaType);
            Assert.AreEqual("work", item.Anchor.Scope.WorkKey);
            Assert.AreEqual(content.ToString(), item.Anchor.Scope.ContentKey);

            Assert.IsTrue(LanguageSourceAnchor.TryParseStored(
                item.Anchor.SourceType,
                item.Anchor.SourceKey,
                item.Anchor.PositionKey,
                out var type,
                out var contentId,
                out var position));
            Assert.AreEqual(item.Anchor.Type, type);
            Assert.AreEqual(content, contentId);
            Assert.AreEqual(item.Anchor.Position, position);
        }

        Assert.IsNull(new LanguageSourceAnchor(
            LanguageSourceType.Anime,
            "work",
            content,
            new LanguageSourcePosition(Paragraph: 2)).PositionKey);
    }

    [TestMethod]
    public async Task ExplanationsAreGeneratedOnceAndServedFromTheCache()
    {
        await using var fixture = await LanguageInspectorFixture.CreateAsync();
        await fixture.SetProfileModeAsync(LearningMode.LanguageTools);
        var context = fixture.AnimeContext(1_000, "猫がいる。");

        var before = await fixture.Inspector().InspectAsync(
            new LanguageInspectRequest("猫", context),
            CancellationToken.None);
        Assert.IsNull(before.Explanation, "Inspecting never calls the AI provider.");
        Assert.AreEqual(0, fixture.Explainer.Calls);

        var first = await fixture.Inspector().ExplainAsync(
            new LanguageExplainRequest("猫がいる。", context),
            CancellationToken.None);
        var second = await fixture.Inspector().ExplainAsync(
            new LanguageExplainRequest("猫がいる。", context),
            CancellationToken.None);
        Assert.IsFalse(first.FromCache);
        Assert.IsTrue(second.FromCache);
        Assert.AreEqual(1, fixture.Explainer.Calls);

        var after = await fixture.Inspector().InspectAsync(
            new LanguageInspectRequest("猫", context),
            CancellationToken.None);
        Assert.AreEqual("There is a cat.", after.Explanation?.Translation);
        Assert.AreEqual(1, fixture.Explainer.Calls);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Inspector().ExplainAsync(
                new LanguageExplainRequest("There is a cat.", new LanguageInspectContext("en")),
                CancellationToken.None));

        await fixture.SetProfileCapabilityAsync(LearningCapability.AiExplanations, false);
        await Assert.ThrowsExactlyAsync<LanguageAssistanceDeniedException>(() =>
            fixture.Inspector().ExplainAsync(
                new LanguageExplainRequest("犬がいる。", context),
                CancellationToken.None));
        Assert.AreEqual(1, fixture.Explainer.Calls);
    }

    [TestMethod]
    public async Task GenericLanguagesUseWordBoundariesAndTheSameCardPath()
    {
        await using var fixture = await LanguageInspectorFixture.CreateAsync();
        await fixture.SetProfileModeAsync(LearningMode.Study);

        var inspection = await fixture.Inspector().InspectAsync(
            new LanguageInspectRequest("Hello, brave world!", fixture.BookContext(1, language: "en")),
            CancellationToken.None);
        CollectionAssert.AreEqual(
            new[] { "Hello", "brave", "world" },
            inspection.Tokens.Where(x => x.Interactive).Select(x => x.Surface).ToArray());
        Assert.IsFalse(inspection.Features.Explanation, "The AI explainer is Japanese-only.");
        Assert.IsTrue(inspection.Tokens.All(x => x.Reading is null && x.Meaning is null));

        await fixture.Inspector().SetStateAsync(
            new LanguageWordStateRequest("brave", "saved", fixture.BookContext(1, "Hello, brave world!", "en")),
            CancellationToken.None);
        var course = await fixture.Db.LearningCourses.SingleAsync(x => x.ProfileId == LanguageInspectorFixture.Profile);
        Assert.AreEqual("en", course.SourceLanguage);
        Assert.AreEqual(UserTermState.Saved, (await fixture.WordCardAsync("brave", "en")).State);

        inspection = await fixture.Inspector().InspectAsync(
            new LanguageInspectRequest("brave", fixture.BookContext(1, language: "en")),
            CancellationToken.None);
        Assert.AreEqual("saved", inspection.Tokens.Single().State);
    }
}

/// <summary>Shared database, dictionary and content for language inspector tests.</summary>
internal sealed class LanguageInspectorFixture : IAsyncDisposable
{
    public const string Profile = "inspector-user";
    public const string OtherProfile = "inspector-other";

    private readonly string directory;

    private LanguageInspectorFixture(string directory, AppDbContext db)
    {
        this.directory = directory;
        Db = db;
        Analyzer = new LanguageTextAnalyzer(new FakeMorphology(), new JapaneseDictionary(directory));
    }

    public AppDbContext Db { get; }
    public LanguageTextAnalyzer Analyzer { get; }
    public CountingExplainer Explainer { get; } = new();
    public Guid AnimeId { get; private set; }
    public Guid EpisodeId { get; private set; }
    public Guid WorkId { get; private set; }
    public Guid ChapterId { get; private set; }
    public Guid MangaChapterId { get; private set; }

    public static async Task<LanguageInspectorFixture> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"anilingo-inspector-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "jmdict-ger.tsv"),
            "猫\tネコ\t1\tKatze\n犬\tイヌ\t1\tHund\n");
        await File.WriteAllTextAsync(Path.Combine(directory, "jmdict-eng-common.tsv"), "");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "anilingo.db")};Foreign Keys=True")
            .Options;
        var db = new AppDbContext(options);
        await DatabaseMigrationBridge.UpgradeAsync(db);

        var fixture = new LanguageInspectorFixture(directory, db);
        await fixture.SeedContentAsync();
        return fixture;
    }

    public LanguageInspectorService Inspector(string profile = Profile) =>
        new(
            Db,
            LearningTestData.Service(Db, profile),
            Analyzer,
            new AiSentenceExplanationService(Db, Explainer));

    public LanguageInspectContext HubContext(string language = "ja") => new(language);

    public LanguageInspectContext AnimeContext(int cueStartMs, string? sentence = null) =>
        new("ja", "anime", EpisodeId.ToString(), sentence, CueStartMs: cueStartMs);

    public LanguageInspectContext BookContext(int paragraph, string? sentence = null, string language = "ja") =>
        new(language, "book", ChapterId.ToString(), sentence, Paragraph: paragraph);

    public LanguageInspectContext MangaContext(int page, int region, string? sentence = null) =>
        new("ja", "manga", MangaChapterId.ToString(), sentence, Page: page, Region: region);

    public Task SetProfileModeAsync(LearningMode mode, string profile = Profile) =>
        new LearningConfigurationStore(Db).SetModeAsync(
            profile,
            LearningScopeRef.Profile,
            mode,
            CancellationToken.None);

    public Task SetProfileCapabilityAsync(LearningCapability capability, bool? enabled) =>
        new LearningConfigurationStore(Db).SetCapabilityOverrideAsync(
            Profile,
            LearningScopeRef.Profile,
            capability,
            enabled,
            CancellationToken.None);

    public async Task<Term> SeedTermStateAsync(string canonical, UserTermState state)
    {
        var term = await Db.Terms.SingleOrDefaultAsync(x => x.Language == "ja" && x.Canonical == canonical)
            ?? new Term { Language = "ja", Canonical = canonical };
        if (Db.Entry(term).State == EntityState.Detached)
        {
            Db.Terms.Add(term);
        }

        await LearningTestData.SeedTermCardAsync(Db, Profile, term, state);
        return term;
    }

    public Task<LearningCard> WordCardAsync(string canonical, string language = "ja") =>
        (from card in Db.LearningCards.AsNoTracking()
         join unit in Db.LearningUnits.AsNoTracking() on card.UnitId equals unit.Id
         join term in Db.Terms.AsNoTracking() on unit.TermId equals term.Id
         where card.ProfileId == Profile
             && card.Mode == LearningCardMode.Recognition
             && term.Canonical == canonical
             && term.Language == language
         select card)
        .SingleAsync();

    public async Task AddMangaChapterAsync()
    {
        var seriesId = Guid.NewGuid().ToString();
        MangaChapterId = Guid.NewGuid();
        var chapterId = MangaChapterId.ToString();
        var now = DateTime.UtcNow.ToString("O");
        await Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "MangaSeries" ("Id", "Title", "SourcePath", "CreatedAt", "UpdatedAt")
            VALUES ({seriesId}, 'Manga', {"/manga/" + seriesId}, {now}, {now});
            """);
        await Db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "MangaChapters" ("Id", "SeriesId", "Number", "Title", "SourcePath", "SourceKind", "PageCount", "SourceUpdatedAt", "CreatedAt", "UpdatedAt")
            VALUES ({chapterId}, {seriesId}, 1, 'Chapter 1', {"/manga/" + chapterId}, 'folder', 10, {now}, {now}, {now});
            """);
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

    private async Task SeedContentAsync()
    {
        var anime = new Anime { Key = "inspector", Title = "Inspector Anime" };
        var episode = new Episode
        {
            AnimeId = anime.Id,
            SeasonNumber = 1,
            Number = 1,
            Title = "Episode 1",
            DiscoveredAt = DateTime.UtcNow
        };
        var work = new NovelWork
        {
            SourceProvider = "test",
            SourceKey = Guid.NewGuid().ToString("N"),
            SourceUrl = "https://example.invalid/book",
            Title = "Inspector Book"
        };
        var volume = new NovelVolume
        {
            WorkId = work.Id,
            Number = 1,
            SourceKey = "volume-1"
        };
        var chapter = new NovelChapter
        {
            WorkId = work.Id,
            VolumeId = volume.Id,
            Number = 1,
            SourceUrl = "https://example.invalid/book/1",
            Title = "Chapter One",
            OriginalText = "本の猫。",
            SourceHash = "hash"
        };

        Db.AddRange(anime, episode, work, volume, chapter);
        await Db.SaveChangesAsync();

        AnimeId = anime.Id;
        EpisodeId = episode.Id;
        WorkId = work.Id;
        ChapterId = chapter.Id;
    }

    /// <summary>Deterministic morphology for the handful of sentences the tests use.</summary>
    internal sealed class FakeMorphology : IJapaneseMorphology
    {
        public IReadOnlyList<JapaneseMorphToken> Analyze(string text) =>
            text switch
            {
                "猫" => [new("猫", "猫", "ネコ", "名詞")],
                "猫がいる" or "猫がいる。" =>
                [
                    new("猫", "猫", "ネコ", "名詞"),
                    new("が", "が", "ガ", "助詞"),
                    new("いる", "いる", "イル", "動詞"),
                    .. text.EndsWith('。') ? new JapaneseMorphToken[] { new("。", "。", "。", "記号") } : []
                ],
                "犬がいる。" =>
                [
                    new("犬", "犬", "イヌ", "名詞"),
                    new("が", "が", "ガ", "助詞"),
                    new("いる", "いる", "イル", "動詞"),
                    new("。", "。", "。", "記号")
                ],
                "本の猫。" =>
                [
                    new("本", "本", "ホン", "名詞"),
                    new("の", "の", "ノ", "助詞"),
                    new("猫", "猫", "ネコ", "名詞"),
                    new("。", "。", "。", "記号")
                ],
                _ => []
            };
    }

    internal sealed class CountingExplainer : IAiSentenceExplainer
    {
        public string Id => "counting";
        public int Calls { get; private set; }

        public Task<AiSentenceExplanation> ExplainSentenceAsync(
            AiSentenceExplainRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AiSentenceExplanation(
                "There is a cat.",
                ["いる = to exist (animate)"],
                [],
                FromCache: false));
        }
    }
}
