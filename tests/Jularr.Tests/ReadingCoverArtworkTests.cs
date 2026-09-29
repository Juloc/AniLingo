using System.Net;
using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Artwork;
using Jularr.Web.Features.Manga;
using Jularr.Web.Features.MediaMapping;
using Jularr.Web.Features.Novels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Jularr.Tests;

// Issue #581: Light Novel and Manga covers are stored beside their media on the media type's NAS
// library root through the one BesideMediaArtworkStore path (as Books and anime do), and are served
// from there through the local thumbnail cache.
[TestClass]
public sealed class ReadingCoverArtworkTests
{
    private const string CoverUrl = "https://cdn.example/covers/frieren.jpg";

    [TestMethod]
    public async Task LightNovelCoverIsStoredBesideTheSeriesAndServedFromThere()
    {
        await using var host = await ReadingArtworkTestHost.CreateAsync();
        var workId = await host.AddLightNovelAsync((1, Path.Combine(host.NovelRoot, "Frieren", "Vol 01", "Frieren v01.epub")));
        var cover = Jpeg();
        host.Http.ServeImage(CoverUrl, cover);

        var recorded = await host.Covers.PersistProviderCoverAsync(MediaAcquisitionKind.LightNovel, workId, CoverUrl, CancellationToken.None);

        Assert.AreEqual($"/Novels/Cover/{workId}", recorded);
        var stored = Path.Combine(host.NovelRoot, "Frieren", "cover.jpg");
        CollectionAssert.AreEqual(cover, await File.ReadAllBytesAsync(stored));
        var row = (await new MediaArtworkAssetStore(host.Db).ListAsync(MediaArtworkScopes.LightNovel, workId, CancellationToken.None)).Single();
        Assert.AreEqual("cover.jpg", row.FileName);
        Assert.AreEqual(CoverUrl, row.SourceIdentity);
        Assert.AreEqual(stored, await host.Covers.ResolveAsync(MediaAcquisitionKind.LightNovel, workId, CancellationToken.None));

        var thumbnail = await host.Covers.GetThumbnailAsync(MediaAcquisitionKind.LightNovel, workId, width: null, CancellationToken.None);
        Assert.IsNotNull(thumbnail);
        Assert.AreEqual("image/webp", thumbnail.MediaType);
        Assert.IsTrue(thumbnail.Path.StartsWith(Path.Combine(host.Root, "cache"), StringComparison.Ordinal), "Served from the local /data cache.");
    }

    [TestMethod]
    public async Task MangaCoverIsStoredBesideTheSeriesAndServedFromThere()
    {
        await using var host = await ReadingArtworkTestHost.CreateAsync();
        var seriesId = await host.AddMangaAsync(Path.Combine(host.MangaRoot, "Frieren"));
        var cover = Jpeg();
        host.Http.ServeImage(CoverUrl, cover);

        var recorded = await host.Covers.PersistProviderCoverAsync(MediaAcquisitionKind.Manga, seriesId, CoverUrl, CancellationToken.None);

        Assert.AreEqual($"/Manga/Cover/{seriesId}", recorded);
        var stored = Path.Combine(host.MangaRoot, "Frieren", "cover.jpg");
        CollectionAssert.AreEqual(cover, await File.ReadAllBytesAsync(stored));
        Assert.AreEqual(1, (await new MediaArtworkAssetStore(host.Db).ListAsync(MediaArtworkScopes.Manga, seriesId, CancellationToken.None)).Count);
        Assert.AreEqual(stored, await host.Covers.ResolveAsync(MediaAcquisitionKind.Manga, seriesId, CancellationToken.None));
    }

    [TestMethod]
    public async Task WithoutAConfiguredLibraryRootTheProviderCoverStaysAndNothingIsDownloaded()
    {
        await using var host = await ReadingArtworkTestHost.CreateAsync(withLibraryRoots: false);
        var workId = await host.AddLightNovelAsync((1, Path.Combine(host.NovelRoot, "Frieren", "Frieren v01.epub")));
        var seriesId = await host.AddMangaAsync(Path.Combine(host.MangaRoot, "Frieren"));
        host.Http.ServeImage(CoverUrl, Jpeg());

        var novel = await host.Covers.PersistProviderCoverAsync(MediaAcquisitionKind.LightNovel, workId, CoverUrl, CancellationToken.None);
        var manga = await host.Covers.PersistProviderCoverAsync(MediaAcquisitionKind.Manga, seriesId, CoverUrl, CancellationToken.None);

        Assert.AreEqual(CoverUrl, novel);
        Assert.AreEqual(CoverUrl, manga);
        Assert.AreEqual(0, host.Http.Requests, "Nothing to store it beside, so nothing is fetched.");
        Assert.IsFalse(Directory.EnumerateFiles(host.Root, "cover.*", SearchOption.AllDirectories).Any());
        Assert.IsNull(await host.Covers.ResolveAsync(MediaAcquisitionKind.LightNovel, workId, CancellationToken.None));
        Assert.IsNull(await host.Covers.GetThumbnailAsync(MediaAcquisitionKind.Manga, seriesId, width: null, CancellationToken.None));
    }

    [TestMethod]
    public async Task SeriesNotYetOnTheLibraryRootKeepsTheProviderCover()
    {
        await using var host = await ReadingArtworkTestHost.CreateAsync();
        var webNovel = await host.AddLightNovelAsync((1, null));
        var inPlace = await host.AddMangaAsync(Path.Combine(host.Root, "downloads", "Frieren"));
        host.Http.ServeImage(CoverUrl, Jpeg());

        Assert.AreEqual(CoverUrl, await host.Covers.PersistProviderCoverAsync(MediaAcquisitionKind.LightNovel, webNovel, CoverUrl, CancellationToken.None));
        Assert.AreEqual(CoverUrl, await host.Covers.PersistProviderCoverAsync(MediaAcquisitionKind.Manga, inPlace, CoverUrl, CancellationToken.None));
        Assert.AreEqual(0, host.Http.Requests);
    }

    [TestMethod]
    public async Task UnavailableSeriesFolderKeepsTheProviderCoverWithoutFailing()
    {
        await using var host = await ReadingArtworkTestHost.CreateAsync();
        var seriesId = await host.AddMangaAsync(Path.Combine(host.MangaRoot, "Frieren"));
        host.Http.ServeImage(CoverUrl, Jpeg());
        Directory.Delete(Path.Combine(host.MangaRoot, "Frieren"));

        var recorded = await host.Covers.PersistProviderCoverAsync(MediaAcquisitionKind.Manga, seriesId, CoverUrl, CancellationToken.None);

        Assert.AreEqual(CoverUrl, recorded);
        Assert.IsNull(await host.Covers.ResolveAsync(MediaAcquisitionKind.Manga, seriesId, CancellationToken.None));
    }

    [TestMethod]
    public async Task UserCoverBesideTheMediaIsNeverReplacedAndIsWhatIsServed()
    {
        await using var host = await ReadingArtworkTestHost.CreateAsync();
        var workId = await host.AddLightNovelAsync((1, Path.Combine(host.NovelRoot, "Frieren", "Frieren v01.epub")));
        var custom = Image(300, 450, SKEncodedImageFormat.Png);
        var customPath = Path.Combine(host.NovelRoot, "Frieren", "cover.png");
        await File.WriteAllBytesAsync(customPath, custom);
        host.Http.ServeImage(CoverUrl, Jpeg());

        var recorded = await host.Covers.PersistProviderCoverAsync(MediaAcquisitionKind.LightNovel, workId, CoverUrl, CancellationToken.None);

        Assert.AreEqual($"/Novels/Cover/{workId}", recorded);
        CollectionAssert.AreEqual(custom, await File.ReadAllBytesAsync(customPath));
        Assert.IsFalse(File.Exists(Path.Combine(host.NovelRoot, "Frieren", "cover.jpg")), "No provider copy is added next to the user's cover.");
        Assert.AreEqual(customPath, await host.Covers.ResolveAsync(MediaAcquisitionKind.LightNovel, workId, CancellationToken.None));
    }

    [TestMethod]
    public async Task NewProviderCoverReplacesTheCoverJularrWroteWithoutLeavingAStaleFile()
    {
        await using var host = await ReadingArtworkTestHost.CreateAsync();
        var seriesId = await host.AddMangaAsync(Path.Combine(host.MangaRoot, "Frieren"));
        var second = Image(400, 600, SKEncodedImageFormat.Png);
        host.Http.ServeImage(CoverUrl, Jpeg());
        host.Http.ServeImage("https://cdn.example/covers/frieren-2.png", second, "image/png");
        await host.Covers.PersistProviderCoverAsync(MediaAcquisitionKind.Manga, seriesId, CoverUrl, CancellationToken.None);

        var recorded = await host.Covers.PersistProviderCoverAsync(
            MediaAcquisitionKind.Manga, seriesId, "https://cdn.example/covers/frieren-2.png", CancellationToken.None);

        Assert.AreEqual($"/Manga/Cover/{seriesId}", recorded);
        CollectionAssert.AreEqual(new[] { "cover.png" }, Directory.EnumerateFiles(Path.Combine(host.MangaRoot, "Frieren")).Select(Path.GetFileName).ToArray());
        CollectionAssert.AreEqual(second, await File.ReadAllBytesAsync(Path.Combine(host.MangaRoot, "Frieren", "cover.png")));
    }

    [TestMethod]
    public async Task UnusableProviderResponsesKeepTheProviderCover()
    {
        await using var host = await ReadingArtworkTestHost.CreateAsync();
        var seriesId = await host.AddMangaAsync(Path.Combine(host.MangaRoot, "Frieren"));
        host.Http.Serve("https://cdn.example/missing.jpg", () => new HttpResponseMessage(HttpStatusCode.NotFound));
        host.Http.Serve("https://cdn.example/page.html", () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html></html>", System.Text.Encoding.UTF8, "text/html")
        });
        host.Http.Serve("https://cdn.example/fake.jpg", () =>
        {
            var content = new ByteArrayContent("not an image"u8.ToArray());
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        host.Http.Serve("https://cdn.example/offline.jpg", () => throw new HttpRequestException("provider offline"));

        foreach (var url in new[]
                 {
                     "https://cdn.example/missing.jpg", "https://cdn.example/page.html",
                     "https://cdn.example/fake.jpg", "https://cdn.example/offline.jpg", "not a url", null
                 })
        {
            Assert.AreEqual(url, await host.Covers.PersistProviderCoverAsync(MediaAcquisitionKind.Manga, seriesId, url, CancellationToken.None));
        }

        Assert.IsFalse(Directory.EnumerateFiles(host.MangaRoot, "*", SearchOption.AllDirectories).Any());
    }

    [TestMethod]
    public async Task CachedThumbnailKeepsServingWhenTheNasIsOfflineOrTheRootIsRemoved()
    {
        await using var host = await ReadingArtworkTestHost.CreateAsync();
        var workId = await host.AddLightNovelAsync((1, Path.Combine(host.NovelRoot, "Frieren", "Frieren v01.epub")));
        host.Http.ServeImage(CoverUrl, Jpeg());
        await host.Covers.PersistProviderCoverAsync(MediaAcquisitionKind.LightNovel, workId, CoverUrl, CancellationToken.None);
        var warm = await host.Covers.GetThumbnailAsync(MediaAcquisitionKind.LightNovel, workId, width: 240, CancellationToken.None);
        Assert.IsNotNull(warm);

        Directory.Delete(Path.Combine(host.NovelRoot, "Frieren"), recursive: true);
        var offline = await host.Covers.GetThumbnailAsync(MediaAcquisitionKind.LightNovel, workId, width: 240, CancellationToken.None);
        await host.ConfigureLibraryRootsAsync(novelRoot: null, mangaRoot: host.MangaRoot);
        var rootRemoved = await host.Covers.GetThumbnailAsync(MediaAcquisitionKind.LightNovel, workId, width: 240, CancellationToken.None);

        Assert.AreEqual(warm.Path, offline?.Path);
        Assert.AreEqual(warm.Path, rootRemoved?.Path);
        Assert.IsNull(await host.Covers.ResolveAsync(MediaAcquisitionKind.LightNovel, workId, CancellationToken.None));
    }

    [TestMethod]
    public async Task CoverRoutesServeTheCachedThumbnailAndNotFoundWhenThereIsNoCover()
    {
        await using var host = await ReadingArtworkTestHost.CreateAsync();
        var workId = await host.AddLightNovelAsync((1, Path.Combine(host.NovelRoot, "Frieren", "Frieren v01.epub")));
        var seriesId = await host.AddMangaAsync(Path.Combine(host.MangaRoot, "Frieren"));
        var withoutCover = await host.AddMangaAsync(Path.Combine(host.MangaRoot, "Dungeon Meshi"));
        host.Http.ServeImage(CoverUrl, Jpeg());
        await host.Covers.PersistProviderCoverAsync(MediaAcquisitionKind.LightNovel, workId, CoverUrl, CancellationToken.None);
        await host.Covers.PersistProviderCoverAsync(MediaAcquisitionKind.Manga, seriesId, CoverUrl, CancellationToken.None);

        var novelPage = new Jularr.Web.Pages.Novels.CoverModel(host.Covers) { PageContext = NewPageContext() };
        var mangaPage = new Jularr.Web.Pages.Manga.CoverModel(host.Covers) { PageContext = NewPageContext() };
        var novel = await novelPage.OnGetAsync(workId, 320, CancellationToken.None);
        var manga = await mangaPage.OnGetAsync(seriesId, null, CancellationToken.None);
        var none = await mangaPage.OnGetAsync(withoutCover, null, CancellationToken.None);

        Assert.AreEqual("image/webp", ((PhysicalFileResult)novel).ContentType);
        Assert.AreEqual("private,max-age=604800", novelPage.Response.Headers.CacheControl.ToString());
        Assert.AreEqual("image/webp", ((PhysicalFileResult)manga).ContentType);
        Assert.IsInstanceOfType<NotFoundResult>(none);
    }

    [TestMethod]
    public async Task MatchingALightNovelStoresItsAniListCoverBesideTheEpubsAndRecordsTheRoute()
    {
        await using var host = await ReadingArtworkTestHost.CreateAsync();
        var workId = await host.AddLightNovelAsync((1, Path.Combine(host.NovelRoot, "Frieren", "Frieren v01.epub")));
        var cover = Jpeg();
        host.Http.ServeImage(CoverUrl, cover);
        var service = NovelMetadata(host, CoverUrl);

        await service.MatchAsync(workId, "fake", "1", CancellationToken.None);

        var work = await host.Db.NovelWorks.FindAsync(workId);
        Assert.AreEqual($"/Novels/Cover/{workId}", work!.CoverImageUrl);
        CollectionAssert.AreEqual(cover, await File.ReadAllBytesAsync(Path.Combine(host.NovelRoot, "Frieren", "cover.jpg")));
    }

    [TestMethod]
    public async Task MatchingALightNovelNotOnTheLibraryRootKeepsTheProviderCoverUrl()
    {
        await using var host = await ReadingArtworkTestHost.CreateAsync(withLibraryRoots: false);
        var workId = await host.AddLightNovelAsync((1, Path.Combine(host.NovelRoot, "Frieren", "Frieren v01.epub")));
        var service = NovelMetadata(host, CoverUrl);

        await service.MatchAsync(workId, "fake", "1", CancellationToken.None);

        Assert.AreEqual(CoverUrl, (await host.Db.NovelWorks.FindAsync(workId))!.CoverImageUrl);
        Assert.AreEqual(0, host.Http.Requests);
    }

    [TestMethod]
    public async Task MatchingMangaStoresItsAniListCoverBesideTheSeriesAndRecordsTheRoute()
    {
        await using var host = await ReadingArtworkTestHost.CreateAsync();
        var seriesId = await host.AddMangaAsync(Path.Combine(host.MangaRoot, "Frieren"));
        var cover = Jpeg();
        host.Http.ServeImage(CoverUrl, cover);
        host.Http.Serve("https://graphql.anilist.co/", () => AniListMedia(CoverUrl));
        var service = MangaMetadata(host);

        await service.MatchAsync(seriesId, "42", CancellationToken.None);

        var series = (await new MangaRepository(host.Db).GetLibraryAsync("profile", CancellationToken.None)).Single();
        Assert.AreEqual($"/Manga/Cover/{seriesId}", series.CoverImageUrl);
        CollectionAssert.AreEqual(cover, await File.ReadAllBytesAsync(Path.Combine(host.MangaRoot, "Frieren", "cover.jpg")));
    }

    [TestMethod]
    public async Task MatchingMangaReadInPlaceKeepsTheProviderCoverUrl()
    {
        await using var host = await ReadingArtworkTestHost.CreateAsync();
        var seriesId = await host.AddMangaAsync(Path.Combine(host.Root, "downloads", "Frieren"));
        host.Http.ServeImage(CoverUrl, Jpeg());
        host.Http.Serve("https://graphql.anilist.co/", () => AniListMedia(CoverUrl));
        var service = MangaMetadata(host);

        await service.MatchAsync(seriesId, "42", CancellationToken.None);

        var series = (await new MangaRepository(host.Db).GetLibraryAsync("profile", CancellationToken.None)).Single();
        Assert.AreEqual(CoverUrl, series.CoverImageUrl);
        Assert.IsFalse(Directory.EnumerateFiles(host.Root, "cover.*", SearchOption.AllDirectories).Any());
    }

    private static NovelMetadataService NovelMetadata(ReadingArtworkTestHost host, string coverUrl)
    {
        var directory = new DirectoryInfo(Path.Combine(host.Root, "mapping"));
        return new NovelMetadataService(
            host.Db,
            [new FakeNovelProvider(coverUrl)],
            new MediaMappingReviewStore(NullLogger<MediaMappingReviewStore>.Instance, directory),
            new ReadingSegmentMappingStore(NullLogger<ReadingSegmentMappingStore>.Instance, directory),
            host.Covers);
    }

    private static MangaAniListService MangaMetadata(ReadingArtworkTestHost host) =>
        new(
            new MangaRepository(host.Db),
            host.HttpClientFactory,
            reviewStore: null,
            segmentMappings: new ReadingSegmentMappingStore(
                NullLogger<ReadingSegmentMappingStore>.Instance,
                new DirectoryInfo(Path.Combine(host.Root, "mapping"))),
            coverArtwork: host.Covers);

    private static HttpResponseMessage AniListMedia(string coverUrl) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    data = new
                    {
                        Media = new
                        {
                            id = 42,
                            format = "MANGA",
                            status = "FINISHED",
                            isAdult = false,
                            title = new { romaji = "Sousou no Frieren", english = "Frieren" },
                            coverImage = new { extraLarge = coverUrl }
                        }
                    }
                }),
                System.Text.Encoding.UTF8,
                "application/json")
        };

    private static PageContext NewPageContext() => new() { HttpContext = new DefaultHttpContext() };

    private static byte[] Jpeg() => Image(600, 900, SKEncodedImageFormat.Jpeg);

    private static byte[] Image(int width, int height, SKEncodedImageFormat format) =>
        ReadingArtworkTestHost.Image(width, height, format);

    private sealed class FakeNovelProvider(string coverUrl) : INovelMetadataProvider
    {
        public string Key => "fake";

        public Task<IReadOnlyList<NovelMetadataCandidate>> SearchAsync(string query, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NovelMetadataCandidate>>([]);

        public Task<NovelMetadataCandidate?> GetAsync(string externalId, CancellationToken cancellationToken) =>
            Task.FromResult<NovelMetadataCandidate?>(new NovelMetadataCandidate(
                "fake", externalId, "Frieren", null, null, coverUrl, null, "NOVEL", "FINISHED", null, null));
    }
}
