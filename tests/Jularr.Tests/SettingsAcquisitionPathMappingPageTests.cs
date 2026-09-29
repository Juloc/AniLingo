using System.Security.Claims;
using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.AniListAutoMonitor;
using Jularr.Web.Features.Acquisition.Backup;
using Jularr.Web.Features.Acquisition.Import;
using Jularr.Web.Features.Acquisition.Indexers;
using Jularr.Web.Features.Acquisition.Policy;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Storage.FolderBrowse;
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

    [TestMethod]
    public async Task ThePathTestAppliesTheCanonicalTranslationNotACopyOfIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.UpdateAsync(state => state.WithRemotePathMappings(
            MediaAcquisitionKind.Manga,
            [new RemotePathMapping("/dl", "/data/dl"), new RemotePathMapping("/dl/manga", "/data/manga")]));
        var state = await fixture.Store.LoadAsync();

        // Longest prefix first, ignoring case and slash direction, unmatched paths unchanged: the
        // importer's rules, whatever they are, because the test asks the same method.
        foreach (var sample in new[] { "/dl/manga/ch1.cbz", "/DL/Manga\\ch1.cbz", "/dl/other/x", "/elsewhere/y", "/dl", "/dl/manga" })
        {
            var preview = await PreviewAsync(fixture, MediaAcquisitionKind.Manga, sample);

            Assert.AreEqual(state.TranslatePath(MediaAcquisitionKind.Manga, sample), preview.Mapped, sample);
            Assert.AreEqual(sample, preview.Reported);
            Assert.AreEqual(sample != preview.Mapped, preview.Changed, sample);
        }

        Assert.AreEqual("/data/manga/ch1.cbz", (await PreviewAsync(fixture, MediaAcquisitionKind.Manga, "/dl/manga/ch1.cbz")).Mapped);
        Assert.IsFalse(
            (await PreviewAsync(fixture, MediaAcquisitionKind.Book, "/dl/manga/ch1.cbz")).Changed,
            "Mappings belong to one media type.");
    }

    [TestMethod]
    public async Task ATypedMappingIsPreviewedExactlyAsAddingItWouldStoreIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Store.UpdateAsync(state => state.WithRemotePathMappings(
            MediaAcquisitionKind.Manga,
            [new RemotePathMapping("/downloads", "/old")]));

        var typed = await PreviewAsync(
            fixture,
            MediaAcquisitionKind.Manga,
            "/downloads/complete/x.cbz",
            remotePrefix: " /DOWNLOADS ",
            localPrefix: " /data/new ");

        Assert.AreEqual("/data/new/complete/x.cbz", typed.Mapped);
        Assert.AreEqual(
            "/old/complete/x.cbz",
            (await fixture.Store.LoadAsync()).TranslatePath(MediaAcquisitionKind.Manga, "/downloads/complete/x.cbz"),
            "Previewing stores nothing.");

        await fixture.Page().OnPostAddPathMappingAsync(MediaAcquisitionKind.Manga, " /DOWNLOADS ", " /data/new ", CancellationToken.None);

        Assert.AreEqual(
            typed.Mapped,
            (await fixture.Store.LoadAsync()).TranslatePath(MediaAcquisitionKind.Manga, "/downloads/complete/x.cbz"));
    }

    [TestMethod]
    public async Task ThePathTestLooksTheMappedPathUpInsideTheContainerForTheOwnerOnly()
    {
        await using var fixture = await Fixture.CreateAsync();
        var downloads = Directory.CreateDirectory(Path.Combine(fixture.Root, "downloads")).FullName;
        File.WriteAllText(Path.Combine(downloads, "test.mkv"), "x");
        await fixture.Store.UpdateAsync(state => state.WithRemotePathMappings(
            MediaAcquisitionKind.Anime,
            [new RemotePathMapping("/data/downloads", downloads)]));

        var found = await PreviewAsync(fixture, MediaAcquisitionKind.Anime, "/data/downloads/test.mkv");
        var folder = await PreviewAsync(fixture, MediaAcquisitionKind.Anime, "/data/downloads");
        var missing = await PreviewAsync(fixture, MediaAcquisitionKind.Anime, "/data/downloads/absent.mkv");
        var outside = await PreviewAsync(fixture, MediaAcquisitionKind.Anime, Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
        var manager = await PreviewAsync(fixture, MediaAcquisitionKind.Anime, "/data/downloads/test.mkv", asOwner: false);

        Assert.AreEqual(PathKind.File, found.Check!.Kind);
        Assert.AreEqual(PathProblem.None, found.Check.Problem);
        Assert.AreEqual(PathKind.Directory, folder.Check!.Kind);
        Assert.AreEqual(PathProblem.NotFound, missing.Check!.Problem);
        Assert.AreEqual(PathProblem.OutsideStorage, outside.Check!.Problem, "Paths outside the mounted storage are not looked at.");
        Assert.IsNull(manager.Check, "Whether a path exists is the owner's view of the container.");
        Assert.AreEqual(found.Mapped, manager.Mapped, "The translation itself is not secret.");
    }

    [TestMethod]
    public async Task ThePathTestRefusesAnUnknownMediaTypeOrAnEmptyPath()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.IsInstanceOfType<BadRequestResult>(
            await fixture.Page().OnGetPreviewPathMappingAsync((MediaAcquisitionKind)99, "/a", null, null, CancellationToken.None));
        Assert.IsInstanceOfType<BadRequestResult>(
            await fixture.Page().OnGetPreviewPathMappingAsync(MediaAcquisitionKind.Book, "  ", null, null, CancellationToken.None));
    }

    [TestMethod]
    public async Task LibraryAndInboxOnTheSameFolderAreRefusedWhileNestedFoldersAreSaved()
    {
        await using var fixture = await Fixture.CreateAsync();
        var library = Directory.CreateDirectory(Path.Combine(fixture.Root, "library")).FullName;
        var page = fixture.Page();

        await page.OnPostMediaFoldersAsync(MediaAcquisitionKind.Manga, library, null, library + Path.DirectorySeparatorChar, CancellationToken.None);

        Assert.AreEqual(page.Ui["storage.pair.same"], page.TempData["AcquisitionSettingsError"]);
        Assert.IsFalse((await fixture.Store.LoadAsync()).MediaLibraries.ContainsKey(MediaAcquisitionKind.Manga));

        var nested = Path.Combine(library, "inbox");
        await fixture.Page().OnPostMediaFoldersAsync(MediaAcquisitionKind.Manga, library, null, nested, CancellationToken.None);

        var manga = (await fixture.Store.LoadAsync()).FoldersFor(MediaAcquisitionKind.Manga);
        Assert.AreEqual(library, manga.LibraryRoot);
        Assert.AreEqual(nested, manga.InboxRoot);
    }

    [TestMethod]
    public async Task EveryStoragePathFieldOpensTheOneFolderBrowserForTheOwnerOnly()
    {
        await using var host = await ManageSheetPageTestHost.CreateAsync();

        var owner = await host.GetHtmlAsync("/Settings/Acquisition", asOwner: true);
        var manager = await host.GetHtmlAsync("/Settings/Acquisition", asOwner: false, asMediaManager: true);

        // Library and inbox of Manga, Light Novels and Books, and the local side of a mapping.
        Assert.AreEqual(3, Occurrences(owner, "name=\"libraryRoot\""));
        Assert.AreEqual(3, Occurrences(owner, "name=\"inboxRoot\""));
        Assert.AreEqual(1, Occurrences(owner, "name=\"localPrefix\""));
        Assert.AreEqual(7, Occurrences(owner, "data-path-browse"), "Every one of them has the Browse button.");
        Assert.AreEqual(1, Occurrences(owner, "<dialog class=\"folder-browser\""), "One shared browser, not one per field.");
        StringAssert.Contains(owner, "/js/folder-browser.js");
        StringAssert.Contains(owner, "/css/storage-paths.css");

        // A media manager edits the mapping as before but cannot look into the container.
        Assert.AreEqual(1, Occurrences(manager, "name=\"localPrefix\""));
        Assert.AreEqual(0, Occurrences(manager, "data-path-browse"));
        Assert.AreEqual(0, Occurrences(manager, "<dialog class=\"folder-browser\""));
        Assert.IsFalse(manager.Contains("/js/folder-browser.js", StringComparison.Ordinal));
        StringAssert.Contains(manager, "data-path-test");
    }

    [TestMethod]
    public async Task TheMappingFormNamesTheRemoteAndTheLocalSide()
    {
        await using var host = await ManageSheetPageTestHost.CreateAsync();

        var html = await host.GetHtmlAsync("/Settings/Acquisition", asOwner: true);

        StringAssert.Contains(html, "Remote path reported by Sonarr or SABnzbd");
        StringAssert.Contains(html, "Local path visible to Jularr");
    }

    private static async Task<PathMappingPreview> PreviewAsync(
        Fixture fixture,
        MediaAcquisitionKind kind,
        string sample,
        string? remotePrefix = null,
        string? localPrefix = null,
        bool asOwner = true)
    {
        var result = await fixture.Page(asOwner).OnGetPreviewPathMappingAsync(kind, sample, remotePrefix, localPrefix, CancellationToken.None);

        var json = Assert.IsInstanceOfType<JsonResult>(result);
        return Assert.IsInstanceOfType<PathMappingPreview>(json.Value);
    }

    private static int Occurrences(string text, string needle) =>
        System.Text.RegularExpressions.Regex.Count(text, System.Text.RegularExpressions.Regex.Escape(needle));

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

        public string Root => root;

        public AcquisitionModel Page(bool asOwner = true)
        {
            var httpContext = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection()
                    .AddSingleton<IModelMetadataProvider, EmptyModelMetadataProvider>()
                    .BuildServiceProvider()
            };
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, asOwner ? "owner" : "manager"),
                    new Claim(ClaimTypes.Role, asOwner ? AccountRoles.Owner : AccountRoles.MediaManager)
                ],
                "test"));
            var page = new AcquisitionModel(
                Store,
                new AcquisitionPolicyStore(root),
                new AniListAutoMonitorSettingsStore(root),
                new AcquisitionBackupService(root),
                new IndexerStore(new EphemeralDataProtectionProvider(), new DirectoryInfo(Path.Combine(root, "acquisition"))),
                new CurrentAccountContext(new HttpContextAccessor { HttpContext = httpContext }),
                new MediaInboxImportService(Store, [], null!),
                // The test folder stands in for a volume mounted into the container.
                new FolderBrowseService(
                    new FolderBrowseTests.FakeMountTable([new MountPoint(root, "ext4", null, ReadOnly: false)]),
                    new FolderBrowseTests.FakeAccess(),
                    new FolderBrowseOptions(Path.Combine(root, "data"))),
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
