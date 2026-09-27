using System.Net;
using System.Net.Http.Headers;
using Jularr.Web.Data;
using Jularr.Web.Features.Artwork;
using Jularr.Web.Features.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Jularr.Tests;

// Issue #406: durable anime artwork lives beside the media on the library storage; /data only
// keeps rebuildable derivatives.
[TestClass]
public sealed class AnimeArtworkLibraryTests
{
    [TestMethod]
    public async Task ProviderArtworkIsPersistedBesideTheSeriesAndRecorded()
    {
        await using var fixture = await ArtworkFixture.CreateAsync();
        var poster = Image(600, 900, SKEncodedImageFormat.Jpeg);

        var outcome = await fixture.Library.PersistProviderAsync(
            fixture.AnimeId, fixture.SeriesDirectory, AnimeArtworkKind.Poster, poster, MediaArtworkSources.Sonarr, "sonarr:1", CancellationToken.None);

        Assert.AreEqual(AnimeArtworkPersistOutcome.Saved, outcome);
        var canonical = Path.Combine(fixture.SeriesDirectory, "poster.jpg");
        CollectionAssert.AreEqual(poster, await File.ReadAllBytesAsync(canonical));
        var record = (await fixture.Assets.ListAsync(MediaArtworkScopes.AnimeSeries, fixture.AnimeId, CancellationToken.None)).Single();
        Assert.AreEqual("poster.jpg", record.FileName);
        Assert.AreEqual(MediaArtworkSources.Sonarr, record.Source);
        Assert.IsNotNull(fixture.Cache.FindPath(fixture.AnimeId, AnimeArtworkSlot.Poster), "The derivative is rebuilt after the canonical file changed.");
        Assert.IsFalse(Directory.EnumerateFiles(fixture.SeriesDirectory, "*.tmp").Any(), "No temporary files remain beside the media.");
    }

    [TestMethod]
    [DataRow("poster.jpg")]
    [DataRow("folder.png")]
    public async Task UserArtworkBesideTheMediaIsNeverReplaced(string customName)
    {
        await using var fixture = await ArtworkFixture.CreateAsync();
        var custom = Image(300, 450, SKEncodedImageFormat.Png);
        var customPath = Path.Combine(fixture.SeriesDirectory, customName);
        await File.WriteAllBytesAsync(customPath, custom);

        Assert.IsFalse(await fixture.Library.ShouldPersistAsync(
            fixture.AnimeId, fixture.SeriesDirectory, AnimeArtworkKind.Poster, MediaArtworkSources.Sonarr, "sonarr:1", CancellationToken.None));
        var outcome = await fixture.Library.PersistProviderAsync(
            fixture.AnimeId, fixture.SeriesDirectory, AnimeArtworkKind.Poster, Image(600, 900, SKEncodedImageFormat.Jpeg), MediaArtworkSources.Sonarr, "sonarr:1", CancellationToken.None);

        Assert.AreEqual(AnimeArtworkPersistOutcome.KeptCustom, outcome);
        CollectionAssert.AreEqual(custom, await File.ReadAllBytesAsync(customPath));
        Assert.AreEqual(1, Directory.EnumerateFiles(fixture.SeriesDirectory).Count(), "No provider copy is added next to the user's poster.");
    }

    [TestMethod]
    public async Task ReplacingAJularrFileMakesItTheUsersOwn()
    {
        await using var fixture = await ArtworkFixture.CreateAsync();
        await fixture.Library.PersistProviderAsync(
            fixture.AnimeId, fixture.SeriesDirectory, AnimeArtworkKind.Poster, Image(600, 900, SKEncodedImageFormat.Jpeg), MediaArtworkSources.AniList, "https://a/1.jpg", CancellationToken.None);
        var canonical = Path.Combine(fixture.SeriesDirectory, "poster.jpg");
        var custom = Image(320, 480, SKEncodedImageFormat.Jpeg);
        await File.WriteAllBytesAsync(canonical, custom);

        var outcome = await fixture.Library.PersistProviderAsync(
            fixture.AnimeId, fixture.SeriesDirectory, AnimeArtworkKind.Poster, Image(610, 900, SKEncodedImageFormat.Jpeg), MediaArtworkSources.AniList, "https://a/2.jpg", CancellationToken.None);

        Assert.AreEqual(AnimeArtworkPersistOutcome.KeptCustom, outcome);
        CollectionAssert.AreEqual(custom, await File.ReadAllBytesAsync(canonical));
    }

    [TestMethod]
    public async Task RefreshReplacesJularrArtworkAtomicallyWithoutStaleExtensions()
    {
        await using var fixture = await ArtworkFixture.CreateAsync();
        await fixture.Library.PersistProviderAsync(
            fixture.AnimeId, fixture.SeriesDirectory, AnimeArtworkKind.Poster, Image(600, 900, SKEncodedImageFormat.Jpeg), MediaArtworkSources.AniList, "https://a/1.jpg", CancellationToken.None);

        var unchanged = await fixture.Library.PersistProviderAsync(
            fixture.AnimeId, fixture.SeriesDirectory, AnimeArtworkKind.Poster, Image(600, 900, SKEncodedImageFormat.Jpeg), MediaArtworkSources.AniList, "https://a/1.jpg", CancellationToken.None);
        var replacement = Image(500, 750, SKEncodedImageFormat.Png);
        var changed = await fixture.Library.PersistProviderAsync(
            fixture.AnimeId, fixture.SeriesDirectory, AnimeArtworkKind.Poster, replacement, MediaArtworkSources.AniList, "https://a/2.png", CancellationToken.None);

        Assert.AreEqual(AnimeArtworkPersistOutcome.Current, unchanged);
        Assert.AreEqual(AnimeArtworkPersistOutcome.Saved, changed);
        CollectionAssert.AreEqual(new[] { "poster.png" }, Directory.EnumerateFiles(fixture.SeriesDirectory).Select(Path.GetFileName).ToArray());
        CollectionAssert.AreEqual(replacement, await File.ReadAllBytesAsync(Path.Combine(fixture.SeriesDirectory, "poster.png")));
    }

    [TestMethod]
    public async Task LowerRankedProviderDoesNotReplaceBetterArtwork()
    {
        await using var fixture = await ArtworkFixture.CreateAsync();
        var sonarr = Image(600, 900, SKEncodedImageFormat.Jpeg);
        await fixture.Library.PersistProviderAsync(
            fixture.AnimeId, fixture.SeriesDirectory, AnimeArtworkKind.Poster, sonarr, MediaArtworkSources.Sonarr, "sonarr:1", CancellationToken.None);

        var anilist = await fixture.Library.PersistProviderAsync(
            fixture.AnimeId, fixture.SeriesDirectory, AnimeArtworkKind.Poster, Image(400, 600, SKEncodedImageFormat.Jpeg), MediaArtworkSources.AniList, "https://a/1.jpg", CancellationToken.None);

        Assert.AreEqual(AnimeArtworkPersistOutcome.Current, anilist);
        CollectionAssert.AreEqual(sonarr, await File.ReadAllBytesAsync(Path.Combine(fixture.SeriesDirectory, "poster.jpg")));
    }

    [TestMethod]
    public async Task UnavailableStorageChangesNothing()
    {
        await using var fixture = await ArtworkFixture.CreateAsync();
        var legacy = await fixture.WriteLegacyAsync(AnimeArtworkKind.Poster, Image(512, 768, SKEncodedImageFormat.Webp));
        Directory.Delete(fixture.SeriesDirectory, recursive: true);

        var persisted = await fixture.Library.PersistProviderAsync(
            fixture.AnimeId, fixture.SeriesDirectory, AnimeArtworkKind.Poster, Image(600, 900, SKEncodedImageFormat.Jpeg), MediaArtworkSources.Sonarr, "sonarr:1", CancellationToken.None);
        var reconciled = await fixture.Library.ReconcileAsync(
            fixture.AnimeId, fixture.SeriesDirectory, new Dictionary<int, string?>(), null, CancellationToken.None);

        Assert.AreEqual(AnimeArtworkPersistOutcome.Unavailable, persisted);
        Assert.IsTrue(reconciled.Deferred);
        Assert.IsFalse(Directory.Exists(fixture.SeriesDirectory), "An unavailable media folder is never created.");
        Assert.IsTrue(File.Exists(legacy), "The local copy stays until it can be moved beside the media.");
        Assert.AreEqual(legacy, fixture.Cache.FindPath(fixture.AnimeId, AnimeArtworkSlot.Poster), "Existing artwork keeps being served meanwhile.");
    }

    [TestMethod]
    public async Task LegacyArtworkIsMigratedBesideTheMediaVerifiedAndRemovedIdempotently()
    {
        await using var fixture = await ArtworkFixture.CreateAsync();
        var poster = Image(512, 768, SKEncodedImageFormat.Webp);
        var fanart = Image(1600, 900, SKEncodedImageFormat.Webp);
        await fixture.WriteLegacyAsync(AnimeArtworkKind.Poster, poster);
        await fixture.WriteLegacyAsync(AnimeArtworkKind.Fanart, fanart);
        await File.WriteAllTextAsync(Path.Combine(fixture.Cache.LegacyDirectory(fixture.AnimeId), "poster.derivative"), "v1");

        var first = await fixture.ReconcileAsync();
        var second = await fixture.ReconcileAsync();

        Assert.AreEqual(2, first.Migrated);
        Assert.AreEqual(0, second.Migrated);
        CollectionAssert.AreEqual(poster, await File.ReadAllBytesAsync(Path.Combine(fixture.SeriesDirectory, "poster.webp")));
        CollectionAssert.AreEqual(fanart, await File.ReadAllBytesAsync(Path.Combine(fixture.SeriesDirectory, "fanart.webp")));
        Assert.IsFalse(Directory.Exists(fixture.Cache.LegacyDirectory(fixture.AnimeId)), "The obsolete local copy is removed after the verified move.");
        var records = await fixture.Assets.ListAsync(MediaArtworkScopes.AnimeSeries, fixture.AnimeId, CancellationToken.None);
        Assert.IsTrue(records.All(x => x.Source == MediaArtworkSources.Migrated));
        Assert.IsTrue(fixture.Cache.FindPath(fixture.AnimeId, AnimeArtworkSlot.Poster)!.StartsWith(fixture.Cache.RootPath, StringComparison.Ordinal));

        // Sonarr's full-resolution artwork may replace the migrated derivative copy later.
        Assert.IsTrue(await fixture.Library.ShouldPersistAsync(
            fixture.AnimeId, fixture.SeriesDirectory, AnimeArtworkKind.Poster, MediaArtworkSources.Sonarr, "sonarr:1", CancellationToken.None));
    }

    [TestMethod]
    public async Task MigrationPrefersExistingArtworkBesideTheMedia()
    {
        await using var fixture = await ArtworkFixture.CreateAsync();
        var custom = Image(300, 450, SKEncodedImageFormat.Jpeg);
        await File.WriteAllBytesAsync(Path.Combine(fixture.SeriesDirectory, "cover.jpg"), custom);
        await fixture.WriteLegacyAsync(AnimeArtworkKind.Poster, Image(512, 768, SKEncodedImageFormat.Webp));

        var result = await fixture.ReconcileAsync();

        Assert.AreEqual(0, result.Migrated);
        CollectionAssert.AreEqual(new[] { "cover.jpg" }, Directory.EnumerateFiles(fixture.SeriesDirectory).Select(Path.GetFileName).ToArray());
        CollectionAssert.AreEqual(custom, await File.ReadAllBytesAsync(Path.Combine(fixture.SeriesDirectory, "cover.jpg")));
        Assert.IsFalse(Directory.Exists(fixture.Cache.LegacyDirectory(fixture.AnimeId)));
        Assert.AreEqual(300, DerivativeWidth(fixture, AnimeArtworkSlot.Poster));
    }

    [TestMethod]
    public async Task DeletedCacheIsRebuiltFromTheMediaFolder()
    {
        await using var fixture = await ArtworkFixture.CreateAsync();
        await File.WriteAllBytesAsync(Path.Combine(fixture.SeriesDirectory, "poster.jpg"), Image(1000, 1500, SKEncodedImageFormat.Jpeg));
        await File.WriteAllBytesAsync(Path.Combine(fixture.SeriesDirectory, "fanart.png"), Image(2000, 1000, SKEncodedImageFormat.Png));

        var built = await fixture.ReconcileAsync();
        Directory.Delete(fixture.Cache.RootPath, recursive: true);
        Assert.IsNull(fixture.Cache.FindPath(fixture.AnimeId, AnimeArtworkSlot.Poster));
        var rebuilt = await fixture.ReconcileAsync();
        var unchanged = await fixture.ReconcileAsync();

        Assert.AreEqual(2, built.Refreshed);
        Assert.AreEqual(2, rebuilt.Refreshed);
        Assert.AreEqual(2, unchanged.Unchanged);
        Assert.AreEqual(AnimeArtworkStore.PosterMaxWidth, DerivativeWidth(fixture, AnimeArtworkSlot.Poster));
        Assert.AreEqual(AnimeArtworkStore.FanartMaxWidth, DerivativeWidth(fixture, AnimeArtworkSlot.Fanart));
    }

    [TestMethod]
    public async Task UserArtworkTakesPrecedenceOverPersistedProviderArtwork()
    {
        await using var fixture = await ArtworkFixture.CreateAsync();
        await fixture.Library.PersistProviderAsync(
            fixture.AnimeId, fixture.SeriesDirectory, AnimeArtworkKind.Poster, Image(400, 600, SKEncodedImageFormat.Jpeg), MediaArtworkSources.Sonarr, "sonarr:1", CancellationToken.None);
        await File.WriteAllBytesAsync(Path.Combine(fixture.SeriesDirectory, "folder.jpg"), Image(200, 300, SKEncodedImageFormat.Jpeg));

        await fixture.ReconcileAsync();

        Assert.AreEqual(200, DerivativeWidth(fixture, AnimeArtworkSlot.Poster));
    }

    [TestMethod]
    public async Task SeasonArtworkComesFromTheSeasonFolderAndFallsBackToTheSeries()
    {
        await using var fixture = await ArtworkFixture.CreateAsync();
        var season1 = Directory.CreateDirectory(Path.Combine(fixture.SeriesDirectory, "Season 01")).FullName;
        var season2 = Directory.CreateDirectory(Path.Combine(fixture.SeriesDirectory, "Season 02")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(fixture.SeriesDirectory, "poster.jpg"), Image(400, 600, SKEncodedImageFormat.Jpeg));
        await File.WriteAllBytesAsync(Path.Combine(season1, "poster.jpg"), Image(300, 450, SKEncodedImageFormat.Jpeg));
        await File.WriteAllBytesAsync(Path.Combine(fixture.SeriesDirectory, "season-specials-poster.png"), Image(250, 375, SKEncodedImageFormat.Png));

        await fixture.ReconcileAsync(new Dictionary<int, string?> { [0] = null, [1] = season1, [2] = season2 });

        Assert.AreEqual(300, DerivativeWidth(fixture, AnimeArtworkSlot.SeasonPoster(1)));
        Assert.AreEqual(250, DerivativeWidth(fixture, AnimeArtworkSlot.SeasonPoster(0)));
        Assert.IsNull(fixture.Cache.FindPath(fixture.AnimeId, AnimeArtworkSlot.SeasonPoster(2)), "Season 2 has no own poster; pages fall back to the series poster.");
        Assert.AreEqual(400, DerivativeWidth(fixture, AnimeArtworkSlot.Poster));
    }

    [TestMethod]
    public async Task RemoteProviderArtworkIsPersistedOnceTheAnimeIsInTheLibrary()
    {
        var handler = new ImageHandler(Image(600, 900, SKEncodedImageFormat.Jpeg));
        await using var fixture = await ArtworkFixture.CreateAsync(handler);
        var provider = new AnimeProviderArtwork("https://s4.anilist.co/cover.jpg", null);

        await fixture.ReconcileAsync(provider: provider);
        await fixture.ReconcileAsync(provider: provider);

        Assert.AreEqual(1, handler.Requests, "Once persisted beside the media the remote URL is not fetched again.");
        Assert.IsTrue(File.Exists(Path.Combine(fixture.SeriesDirectory, "poster.jpg")));
        Assert.IsNotNull(fixture.Cache.FindPath(fixture.AnimeId, AnimeArtworkSlot.Poster));
        var record = (await fixture.Assets.ListAsync(MediaArtworkScopes.AnimeSeries, fixture.AnimeId, CancellationToken.None)).Single();
        Assert.AreEqual(MediaArtworkSources.AniList, record.Source);
        Assert.AreEqual(provider.PosterUrl, record.SourceIdentity);
    }

    [TestMethod]
    public void PagesFallBackToTheRemoteUrlWhenNoArtworkIsKnown()
    {
        var animeId = Guid.NewGuid();

        Assert.AreEqual("https://remote/cover.jpg", AnimeArtworkStore.ResolvePosterUrl(animeId, "https://remote/cover.jpg"));
        Assert.AreEqual("https://remote/cover.jpg", AnimeArtworkStore.ResolveSeasonPosterUrl(animeId, 2, "https://remote/cover.jpg"));
    }

    [TestMethod]
    [DataRow("poster", AnimeArtworkKind.Poster, null)]
    [DataRow("fanart", AnimeArtworkKind.Fanart, null)]
    [DataRow("season-01-poster", AnimeArtworkKind.Poster, 1)]
    [DataRow("season-00-poster", AnimeArtworkKind.Poster, 0)]
    public void ArtworkSlotsRoundTripThroughTheirUrlSlug(string slug, AnimeArtworkKind kind, int? season)
    {
        Assert.IsTrue(AnimeArtworkSlot.TryParse(slug, out var slot));
        Assert.AreEqual(new AnimeArtworkSlot(kind, season), slot);
        Assert.AreEqual(slug, slot.Slug);
    }

    [TestMethod]
    [DataRow("banner")]
    [DataRow("season--poster")]
    [DataRow("season-01-fanart")]
    [DataRow("../poster")]
    public void UnknownArtworkSlugsAreRejected(string slug) =>
        Assert.IsFalse(AnimeArtworkSlot.TryParse(slug, out _));

    [TestMethod]
    public async Task MediaFoldersResolveSeriesAndSeasonFoldersFromKnownMedia()
    {
        await using var fixture = await ArtworkFixture.CreateAsync();
        var root = new LibraryRoot { Name = "Anime", Path = fixture.LibraryPath };
        var anime = new Anime { Key = "frieren", Title = "Frieren" };
        fixture.Db.LibraryRoots.Add(root);
        fixture.Db.Anime.Add(anime);
        AddMedia(fixture.Db, root, anime, 1, Path.Combine(fixture.SeriesDirectory, "Season 01", "Frieren - S01E01.mkv"));
        AddMedia(fixture.Db, root, anime, 0, Path.Combine(fixture.SeriesDirectory, "Frieren - S00E01.mkv"));
        await fixture.Db.SaveChangesAsync();

        var folder = (await AnimeMediaFolders.ResolveAsync(fixture.Db, [anime.Id], CancellationToken.None))[anime.Id];

        Assert.AreEqual(fixture.SeriesDirectory, folder.SeriesDirectory);
        Assert.AreEqual("Frieren", folder.FolderName);
        Assert.AreEqual(Path.Combine(fixture.SeriesDirectory, "Season 01"), folder.SeasonDirectories[1]);
        Assert.IsNull(folder.SeasonDirectories[0]);
    }

    private static void AddMedia(AppDbContext db, LibraryRoot root, Anime anime, int season, string path)
    {
        var episode = new Episode { AnimeId = anime.Id, SeasonNumber = season, Number = 1, Title = "Episode" };
        db.Episodes.Add(episode);
        db.MediaFiles.Add(new MediaFile { LibraryRootId = root.Id, EpisodeId = episode.Id, Path = path });
    }

    private static int DerivativeWidth(ArtworkFixture fixture, AnimeArtworkSlot slot)
    {
        var path = fixture.Cache.FindPath(fixture.AnimeId, slot);
        Assert.IsNotNull(path, $"No derivative for {slot.Slug}.");
        using var bitmap = SKBitmap.Decode(path);
        return bitmap.Width;
    }

    private static byte[] Image(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private sealed class ArtworkFixture : IAsyncDisposable
    {
        private ArtworkFixture(string tempRoot, AppDbContext db, IHttpClientFactory? http)
        {
            TempRoot = tempRoot;
            Db = db;
            Cache = new AnimeArtworkCache(Path.Combine(tempRoot, "cache"), Path.Combine(tempRoot, "legacy"));
            Library = new AnimeArtworkLibrary(db, NullLogger<AnimeArtworkLibrary>.Instance, http, Cache);
            Assets = new MediaArtworkAssetStore(db);
        }

        public string TempRoot { get; }
        public AppDbContext Db { get; }
        public AnimeArtworkCache Cache { get; }
        public AnimeArtworkLibrary Library { get; }
        public MediaArtworkAssetStore Assets { get; }
        public Guid AnimeId { get; } = Guid.NewGuid();
        public string LibraryPath => Path.Combine(TempRoot, "anime");
        public string SeriesDirectory => Path.Combine(LibraryPath, "Frieren");

        public static async Task<ArtworkFixture> CreateAsync(HttpMessageHandler? handler = null)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), $"jularr-artwork-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(tempRoot, "anime", "Frieren"));
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(tempRoot, "jularr.db")};Pooling=False")
                .Options;
            var db = new AppDbContext(options);
            await DatabaseMigrationBridge.UpgradeAsync(db);
            return new ArtworkFixture(tempRoot, db, handler is null ? null : new HandlerFactory(handler));
        }

        public async Task<string> WriteLegacyAsync(AnimeArtworkKind kind, byte[] bytes)
        {
            var directory = Cache.LegacyDirectory(AnimeId);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, AnimeArtworkFiles.BaseName(kind) + ".webp");
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }

        public Task<AnimeArtworkReconcileResult> ReconcileAsync(
            IReadOnlyDictionary<int, string?>? seasons = null,
            AnimeProviderArtwork? provider = null) =>
            Library.ReconcileAsync(AnimeId, SeriesDirectory, seasons ?? new Dictionary<int, string?>(), provider, CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            try
            {
                Directory.Delete(TempRoot, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class HandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ImageHandler(byte[] image) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var content = new ByteArrayContent(image);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
