using System.Security.Claims;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Books;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Manga;
using AniLingo.Web.Features.MediaMapping;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Novels;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.Tracking;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using DiscoverIndexModel = AniLingo.Web.Pages.Discover.IndexModel;
using DiscoverMangaImportModel = AniLingo.Web.Pages.Discover.MangaImportModel;
using LibraryIndexModel = AniLingo.Web.Pages.Library.IndexModel;
using MangaIndexModel = AniLingo.Web.Pages.Manga.IndexModel;
using MangaReadModel = AniLingo.Web.Pages.Manga.ReadModel;
using MangaSeriesModel = AniLingo.Web.Pages.Manga.SeriesModel;

namespace AniLingo.Tests;

/// <summary>
/// Guards the #186 rule that ordinary page GETs render from local state.
/// Every external client handed to a page throws if it is used, so a page GET
/// that starts contacting AniList (or any other remote service) fails here.
/// </summary>
[TestClass]
public sealed class LocalFirstPageGetTests
{
    [TestMethod]
    public async Task MangaReaderGetRendersWithoutExternalCallsAsync()
    {
        await using var fixture = await LocalFirstFixture.CreateAsync();
        await fixture.ConnectAniListAsync();
        var (seriesId, chapterIds) = await fixture.AddMangaAsync(
            "Local Manga",
            aniListId: "321",
            chapters: 3);
        await fixture.SaveMangaProgressAsync(seriesId, chapterIds[1], pageIndex: 4);
        var page = fixture.Attach(new MangaReadModel(fixture.Db, fixture.OwnerAccount));

        var result = await page.OnGetAsync(chapterIds[1], null, CancellationToken.None);

        Assert.IsInstanceOfType<PageResult>(result);
        Assert.AreEqual(4, page.InitialPage);
        Assert.AreEqual(chapterIds[0], page.PreviousChapterId);
        Assert.AreEqual(chapterIds[2], page.NextChapterId);
        fixture.Guard.AssertNotCalled();
    }

    [TestMethod]
    public void MangaReaderHasNoRemoteProgressDependency()
    {
        // The reader used to preview AniList progress on every GET. Remote
        // state now lives only on the series page's lazy card.
        var dependencies = typeof(MangaReadModel)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        CollectionAssert.DoesNotContain(dependencies, typeof(AniListAccountService));
        CollectionAssert.DoesNotContain(dependencies, typeof(IHttpClientFactory));
        CollectionAssert.DoesNotContain(dependencies, typeof(HttpClient));
    }

    [TestMethod]
    public async Task MangaSeriesGetWithoutQueryRendersWithoutExternalCallsAsync()
    {
        await using var fixture = await LocalFirstFixture.CreateAsync();
        await fixture.ConnectAniListAsync();
        var (seriesId, chapterIds) = await fixture.AddMangaAsync(
            "Local Manga",
            aniListId: "321",
            chapters: 2);
        await fixture.SaveMangaProgressAsync(seriesId, chapterIds[1], pageIndex: 9);
        var page = fixture.Attach(fixture.MangaSeriesPage());

        var result = await page.OnGetAsync(seriesId, null, CancellationToken.None);

        Assert.IsInstanceOfType<PageResult>(result);
        Assert.IsNotNull(page.ExternalProgress);
        Assert.AreEqual(0, page.SearchResults.Count);
        fixture.Guard.AssertNotCalled();
    }

    [TestMethod]
    public async Task MangaSeriesSearchFailureStillRendersLocalPageAsync()
    {
        await using var fixture = await LocalFirstFixture.CreateAsync();
        var (seriesId, _) = await fixture.AddMangaAsync(
            "Local Manga",
            aniListId: null,
            chapters: 1);
        var page = fixture.Attach(fixture.MangaSeriesPage());

        // An explicit search submit may contact AniList, but its failure must
        // not turn the local series page into an HTTP 500.
        var result = await page.OnGetAsync(seriesId, "Local Manga", CancellationToken.None);

        Assert.IsInstanceOfType<PageResult>(result);
        Assert.AreEqual(1, fixture.Guard.Requests.Count);
        Assert.AreEqual(0, page.SearchResults.Count);
        Assert.IsNotNull(page.SearchError);
        Assert.IsNotNull(page.ExternalProgress);
    }

    [TestMethod]
    public async Task MangaLibraryGetRendersWithoutExternalCallsAsync()
    {
        await using var fixture = await LocalFirstFixture.CreateAsync();
        await fixture.ConnectAniListAsync();
        var (seriesId, chapterIds) = await fixture.AddMangaAsync(
            "Local Manga",
            aniListId: "321",
            chapters: 2);
        await fixture.SaveMangaProgressAsync(seriesId, chapterIds[0], pageIndex: 1);
        var page = fixture.Attach(new MangaIndexModel(
            fixture.Db,
            fixture.OwnerAccount,
            fixture.Operations,
            fixture.HttpClientFactory,
            fixture.ReviewStore));

        await page.OnGetAsync(CancellationToken.None);

        Assert.AreEqual(1, page.Series.Count);
        Assert.AreEqual(1, page.ContinueReading.Count);
        fixture.Guard.AssertNotCalled();
    }

    [TestMethod]
    public async Task DiscoverGetWithoutQueryRendersWithoutExternalCallsAsync()
    {
        await using var fixture = await LocalFirstFixture.CreateAsync();
        await fixture.ConnectAniListAsync();
        var page = fixture.Attach(fixture.DiscoverPage());

        page.OnGet();

        // Browse/search results come from the explicit, no-store Results
        // handler after first paint; the page GET itself stays local.
        fixture.Guard.AssertNotCalled();
    }

    [TestMethod]
    public async Task DiscoverMangaImportGetRendersWithoutExternalCallsAsync()
    {
        await using var fixture = await LocalFirstFixture.CreateAsync();
        var page = fixture.Attach(new DiscoverMangaImportModel(
            fixture.Db,
            fixture.OwnerAccount,
            fixture.HttpClientFactory,
            fixture.Operations));

        var result = page.OnGet("321", "Remote Manga");

        Assert.IsInstanceOfType<PageResult>(result);
        Assert.AreEqual("321", page.AniListId);
        fixture.Guard.AssertNotCalled();
    }

    [TestMethod]
    public async Task AnimeLibraryGetRendersWithoutExternalCallsAsync()
    {
        await using var fixture = await LocalFirstFixture.CreateAsync();
        await fixture.AddAnimeAsync("Local Anime", episodes: 3);
        var page = fixture.Attach(new LibraryIndexModel(fixture.Db));

        await page.OnGetAsync(CancellationToken.None);

        Assert.AreEqual(1, page.Anime.Count);
        Assert.AreEqual(3, page.Anime[0].EpisodeCount);
        fixture.Guard.AssertNotCalled();
    }

    /// <summary>
    /// Stands in for every outbound HTTP dependency. Any request fails the
    /// calling code immediately and is recorded for the assertion.
    /// </summary>
    internal sealed class ExternalCallGuard : HttpMessageHandler
    {
        private readonly List<string> requests = [];

        public IReadOnlyList<string> Requests => requests;

        public HttpClient CreateClient() =>
            new(this, disposeHandler: false)
            {
                BaseAddress = new Uri("https://external.invalid/")
            };

        public void AssertNotCalled() =>
            Assert.AreEqual(
                0,
                requests.Count,
                "An ordinary page GET contacted an external service: " +
                string.Join(", ", requests));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            requests.Add($"{request.Method} {request.RequestUri}");
            throw new InvalidOperationException(
                $"External call during a local-first page GET: {request.RequestUri}");
        }
    }

    private sealed class GuardedHttpClientFactory(ExternalCallGuard guard) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => guard.CreateClient();
    }

    private sealed class LocalFirstFixture : IAsyncDisposable
    {
        private const string OwnerProfileId = "owner";

        private readonly string root;
        private readonly AniListAccountStore accountStore;
        private readonly ReadingSegmentMappingStore segmentStore;

        private LocalFirstFixture(string root, AppDbContext db)
        {
            this.root = root;
            Db = db;
            Guard = new ExternalCallGuard();
            HttpClientFactory = new GuardedHttpClientFactory(Guard);
            OwnerAccount = CreateOwnerAccount();
            Operations = new OperationRunner(db, new ServiceCollection().BuildServiceProvider());
            accountStore = new AniListAccountStore(
                new EphemeralDataProtectionProvider(),
                NullLogger<AniListAccountStore>.Instance,
                new DirectoryInfo(root));
            var mappingDirectory = new DirectoryInfo(Path.Combine(root, "anilist"));
            ReviewStore = new MediaMappingReviewStore(
                NullLogger<MediaMappingReviewStore>.Instance,
                mappingDirectory);
            segmentStore = new ReadingSegmentMappingStore(
                NullLogger<ReadingSegmentMappingStore>.Instance,
                mappingDirectory);
        }

        public AppDbContext Db { get; }
        public ExternalCallGuard Guard { get; }
        public IHttpClientFactory HttpClientFactory { get; }
        public CurrentAccountContext OwnerAccount { get; }
        public OperationRunner Operations { get; }
        public MediaMappingReviewStore ReviewStore { get; }

        public static async Task<LocalFirstFixture> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "anilingo-tests",
                $"local-first-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(root, "anilist"));
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "anilingo.db")};Foreign Keys=True")
                .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new LocalFirstFixture(root, db);
        }

        /// <summary>
        /// A connected account means every AniList-aware code path would try
        /// to reach AniList if it were asked for remote state.
        /// </summary>
        public Task ConnectAniListAsync() =>
            accountStore.SaveAsync(
                OwnerProfileId,
                new StoredAniListAccount(
                    12345,
                    42,
                    "viewer-42",
                    null,
                    "owner-token",
                    DateTimeOffset.UtcNow,
                    null),
                CancellationToken.None);

        public AniListAccountService AniListAccount() =>
            new(
                Guard.CreateClient(),
                accountStore,
                Db,
                new AnimeMetadataService(Db, [], accountStore, ReviewStore),
                segmentStore,
                ReviewStore,
                OwnerAccount,
                NullLogger<AniListAccountService>.Instance);

        public MangaSeriesModel MangaSeriesPage() =>
            new(
                Db,
                OwnerAccount,
                HttpClientFactory,
                Operations,
                ReviewStore,
                AniListAccount());

        public DiscoverIndexModel DiscoverPage()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Books:Translation:MemoryPath"] = Path.Combine(root, "translation-memory")
                })
                .Build();
            var readingProvider = new NovelAniListProvider(
                Guard.CreateClient(),
                NullLogger<NovelAniListProvider>.Instance);
            var novelMetadata = new NovelMetadataService(
                Db,
                [readingProvider],
                ReviewStore,
                segmentStore);

            return new DiscoverIndexModel(
                new AniListMetadataProvider(
                    Guard.CreateClient(),
                    NullLogger<AniListMetadataProvider>.Instance),
                readingProvider,
                new BookCatalogService(
                    Guard.CreateClient(),
                    Db,
                    new ThrowingBookTranslator(),
                    configuration),
                AniListAccount(),
                Db,
                new NovelImportService(Db, [], novelMetadata),
                novelMetadata,
                OwnerAccount,
                Operations);
        }

        public async Task<(Guid SeriesId, IReadOnlyList<Guid> ChapterIds)> AddMangaAsync(
            string title,
            string? aniListId,
            int chapters)
        {
            var repository = new MangaRepository(Db);
            var seriesId = Guid.NewGuid();
            var sourcePath = Path.Combine(root, "manga", seriesId.ToString("N"));
            await repository.UpsertSeriesAsync(
                seriesId,
                title,
                sourcePath,
                CancellationToken.None);

            var chapterIds = new List<Guid>();
            for (var number = 1; number <= chapters; number++)
            {
                var chapterId = Guid.NewGuid();
                chapterIds.Add(chapterId);
                await repository.UpsertChapterAsync(
                    new MangaChapterItem(
                        chapterId,
                        seriesId,
                        number,
                        null,
                        $"Chapter {number}",
                        10,
                        "folder",
                        DateTime.UtcNow),
                    Path.Combine(sourcePath, number.ToString()),
                    CancellationToken.None);
            }

            if (aniListId is not null)
            {
                await repository.UpdateMetadataAsync(
                    seriesId,
                    new MangaAniListCandidate(aniListId, title, null, null, null, null, null),
                    CancellationToken.None);
            }

            return (seriesId, chapterIds);
        }

        public async Task SaveMangaProgressAsync(Guid seriesId, Guid chapterId, int pageIndex)
        {
            var repository = new MangaRepository(Db);
            var chapter = await repository.GetChapterAsync(chapterId, CancellationToken.None);
            Assert.IsNotNull(chapter);
            Assert.AreEqual(seriesId, chapter.SeriesId);
            await repository.SaveProgressAsync(
                OwnerProfileId,
                chapter,
                pageIndex,
                CancellationToken.None);
        }

        public async Task AddAnimeAsync(string title, int episodes)
        {
            var anime = new Anime { Key = Guid.NewGuid().ToString("N"), Title = title };
            Db.Add(anime);
            for (var number = 1; number <= episodes; number++)
            {
                Db.Add(new Episode
                {
                    AnimeId = anime.Id,
                    SeasonNumber = 1,
                    Number = number,
                    Title = $"Episode {number}"
                });
            }

            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public TPage Attach<TPage>(TPage page)
            where TPage : PageModel
        {
            var services = new ServiceCollection()
                .AddSingleton<IModelMetadataProvider, EmptyModelMetadataProvider>()
                .BuildServiceProvider();
            var httpContext = new DefaultHttpContext { RequestServices = services };
            page.PageContext = new PageContext
            {
                HttpContext = httpContext,
                ViewData = new ViewDataDictionary<TPage>(
                    new EmptyModelMetadataProvider(),
                    new ModelStateDictionary())
            };
            page.TempData = new TempDataDictionary(httpContext, new NoTempDataProvider());
            return page;
        }

        private static CurrentAccountContext CreateOwnerAccount()
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [
                            new Claim(ClaimTypes.NameIdentifier, OwnerProfileId),
                            new Claim(ClaimTypes.Role, AccountRoles.Owner)
                        ],
                        "test"))
            };

            return new CurrentAccountContext(
                new FixedHttpContextAccessor { HttpContext = httpContext });
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class FixedHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }

    private sealed class ThrowingBookTranslator : IBookTranslator
    {
        public string Id => "local-first-guard";

        public Task<string> TranslateLiteraryAsync(
            string sourceText,
            string sourceLanguage,
            string targetLanguage,
            string context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Translation must not run during a local-first page GET.");
    }

    private sealed class NoTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
