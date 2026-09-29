using System.Security.Claims;
using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.AniListAutoMonitor;
using Jularr.Web.Features.Acquisition.Backup;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Acquisition.Indexers;
using Jularr.Web.Features.Acquisition.Policy;
using Jularr.Web.Features.Auth;
using Jularr.Web.Pages.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

/// <summary>
/// The Settings → Acquisition page edits remote path mappings per media type (#389) and its media
/// folders form must not drop the mappings of the media type it saves.
/// </summary>
[TestClass]
public sealed class SettingsAcquisitionPathMappingPageTests
{
    [TestMethod]
    public async Task AddingAMappingStoresItForTheChosenMediaTypeOnly()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Page().OnPostAddPathMappingAsync(MediaAcquisitionKind.Manga, " /downloads/complete ", " /data/downloads/complete ", CancellationToken.None);

        Assert.IsInstanceOfType<RedirectToPageResult>(result);
        var state = await fixture.Store.LoadAsync();
        Assert.AreEqual("/data/downloads/complete/x", state.TranslatePath(MediaAcquisitionKind.Manga, "/downloads/complete/x"));
        Assert.AreEqual("/downloads/complete/x", state.TranslatePath(MediaAcquisitionKind.Book, "/downloads/complete/x"));
        Assert.AreEqual("/downloads/complete/x", state.TranslatePath(MediaAcquisitionKind.Anime, "/downloads/complete/x"));
        Assert.AreEqual(1, state.RemotePathMappingCount);
    }

    [TestMethod]
    public async Task AddingTheSameRemotePrefixAgainReplacesItOnlyWithinThatMediaType()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.UpdateAsync(state => state
            .WithRemotePathMappings(MediaAcquisitionKind.Book, [new RemotePathMapping("/downloads", "/books-old")])
            .WithRemotePathMappings(MediaAcquisitionKind.Manga, [new RemotePathMapping("/downloads", "/manga")]));

        await fixture.Page().OnPostAddPathMappingAsync(MediaAcquisitionKind.Book, "/DOWNLOADS", "/books-new", CancellationToken.None);

        var state = await fixture.Store.LoadAsync();
        Assert.AreEqual("/books-new/x", state.TranslatePath(MediaAcquisitionKind.Book, "/downloads/x"));
        Assert.AreEqual(1, state.RemotePathMappingsFor(MediaAcquisitionKind.Book).Count);
        Assert.AreEqual("/manga/x", state.TranslatePath(MediaAcquisitionKind.Manga, "/downloads/x"));
    }

    [TestMethod]
    public async Task RemovingAMappingLeavesTheOtherMediaTypesAlone()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.UpdateAsync(state => state
            .WithRemotePathMappings(MediaAcquisitionKind.Anime, [new RemotePathMapping("/tv", "/data/anime")])
            .WithRemotePathMappings(MediaAcquisitionKind.Manga, [new RemotePathMapping("/tv", "/data/manga")]));

        await fixture.Page().OnPostRemovePathMappingAsync(MediaAcquisitionKind.Anime, "/TV", CancellationToken.None);

        var state = await fixture.Store.LoadAsync();
        Assert.AreEqual("/tv/x", state.TranslatePath(MediaAcquisitionKind.Anime, "/tv/x"));
        Assert.AreEqual("/data/manga/x", state.TranslatePath(MediaAcquisitionKind.Manga, "/tv/x"));
        Assert.AreEqual(0, state.RemotePathMappingsFor(MediaAcquisitionKind.Anime).Count);
    }

    [TestMethod]
    public async Task AnUnknownMediaTypeOrAnEmptyPrefixChangesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();

        var unknown = await fixture.Page().OnPostAddPathMappingAsync((MediaAcquisitionKind)99, "/a", "/b", CancellationToken.None);
        var empty = await fixture.Page().OnPostAddPathMappingAsync(MediaAcquisitionKind.Book, " ", "/b", CancellationToken.None);

        Assert.IsInstanceOfType<BadRequestResult>(unknown);
        Assert.IsInstanceOfType<RedirectToPageResult>(empty);
        Assert.AreEqual(0, (await fixture.Store.LoadAsync()).RemotePathMappingCount);
    }

    [TestMethod]
    public async Task SavingMediaFoldersKeepsTheMediaTypesMappings()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.UpdateAsync(state => state.WithRemotePathMappings(
            MediaAcquisitionKind.Manga,
            [new RemotePathMapping("/downloads", "/data/downloads")]));

        await fixture.Page().OnPostMediaFoldersAsync(
            MediaAcquisitionKind.Manga,
            "/data/media/manga",
            ImportMode.Hardlink,
            "/data/downloads/complete/manga",
            CancellationToken.None);

        var state = await fixture.Store.LoadAsync();
        var manga = state.FoldersFor(MediaAcquisitionKind.Manga);
        Assert.AreEqual("/data/media/manga", manga.LibraryRoot);
        Assert.AreEqual(ImportMode.Hardlink, manga.ImportMode);
        Assert.AreEqual("/data/downloads/complete/manga", manga.InboxRoot);
        Assert.AreEqual("/data/downloads/x", state.TranslatePath(MediaAcquisitionKind.Manga, "/downloads/x"));
    }

    [TestMethod]
    public async Task ClearingTheFoldersKeepsAMediaTypeThatStillHasMappingsAndDropsOneThatHasNone()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.UpdateAsync(state => (state with
            {
                MediaLibraries = new Dictionary<MediaAcquisitionKind, MediaLibraryTarget>
                {
                    [MediaAcquisitionKind.Manga] = new("/data/media/manga", null, "/inbox/manga"),
                    [MediaAcquisitionKind.Book] = new(null, null, "/inbox/books")
                }
            })
            .WithRemotePathMappings(MediaAcquisitionKind.Manga, [new RemotePathMapping("/downloads", "/data/downloads")]));

        await fixture.Page().OnPostMediaFoldersAsync(MediaAcquisitionKind.Manga, null, null, null, CancellationToken.None);
        await fixture.Page().OnPostMediaFoldersAsync(MediaAcquisitionKind.Book, null, null, null, CancellationToken.None);

        var state = await fixture.Store.LoadAsync();
        Assert.IsTrue(state.MediaLibraries.ContainsKey(MediaAcquisitionKind.Manga));
        Assert.IsNull(state.FoldersFor(MediaAcquisitionKind.Manga).LibraryRoot);
        Assert.AreEqual(1, state.RemotePathMappingsFor(MediaAcquisitionKind.Manga).Count);
        Assert.IsFalse(state.MediaLibraries.ContainsKey(MediaAcquisitionKind.Book));
    }

    [TestMethod]
    public async Task ThePageListsMappingsPerMediaTypeAndOffersEveryMediaTypeForANewOne()
    {
        await using var host = await ManageSheetPageTestHost.CreateAsync();
        await host.Services.GetRequiredService<AnimeImportSettingsStore>().UpdateAsync(state => state
            .WithRemotePathMappings(MediaAcquisitionKind.Anime, [new RemotePathMapping("/sonarr-tv", "/data/anime")])
            .WithRemotePathMappings(MediaAcquisitionKind.Manga, [new RemotePathMapping("/dl/manga", "/data/manga-in")]));

        var html = await host.GetHtmlAsync("/Settings/Acquisition", asOwner: true);

        StringAssert.Contains(html, "/sonarr-tv");
        StringAssert.Contains(html, "/data/anime");
        StringAssert.Contains(html, "/dl/manga");
        StringAssert.Contains(html, "/data/manga-in");
        foreach (var kind in Enum.GetValues<MediaAcquisitionKind>())
        {
            StringAssert.Contains(html, $"<option value=\"{kind}\">", $"{kind} can be chosen for a new mapping.");
        }

        Assert.AreEqual(
            1,
            System.Text.RegularExpressions.Regex.Matches(html, "name=\"kind\" value=\"Manga\"[^>]*/>\\s*<input type=\"hidden\" name=\"remotePrefix\" value=\"/dl/manga\"").Count,
            "Removing a mapping names its media type.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string root;
        private readonly AppDbContext db;

        private Fixture(string root, AppDbContext db, AnimeImportSettingsStore store)
        {
            this.root = root;
            this.db = db;
            Store = store;
        }

        public AnimeImportSettingsStore Store { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"jularr-settings-acquisition-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "app.db")};Foreign Keys=True")
                .Options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new Fixture(root, db, new AnimeImportSettingsStore(root));
        }

        public AcquisitionModel Page()
        {
            var httpContext = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection()
                    .AddSingleton<IModelMetadataProvider, EmptyModelMetadataProvider>()
                    .BuildServiceProvider()
            };
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "owner"), new Claim(ClaimTypes.Role, AccountRoles.Owner)],
                "test"));
            var page = new AcquisitionModel(
                Store,
                new AcquisitionPolicyStore(root),
                new AniListAutoMonitorSettingsStore(root),
                new AcquisitionBackupService(root),
                new IndexerStore(new EphemeralDataProtectionProvider(), new DirectoryInfo(Path.Combine(root, "acquisition"))),
                new CurrentAccountContext(new HttpContextAccessor { HttpContext = httpContext }),
                new MediaInboxImportService(Store, [], null!),
                db,
                NullLogger<AcquisitionModel>.Instance)
            {
                PageContext = new PageContext
                {
                    HttpContext = httpContext,
                    ViewData = new ViewDataDictionary<AcquisitionModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                },
                TempData = new TempDataDictionary(httpContext, new NoTempData())
            };
            return page;
        }

        public async ValueTask DisposeAsync()
        {
            await db.DisposeAsync();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class NoTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
