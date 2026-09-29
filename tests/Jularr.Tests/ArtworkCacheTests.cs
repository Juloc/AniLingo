using Jularr.Web.Data;
using Jularr.Web.Features.Artwork;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Jularr.Tests;

[TestClass]
public sealed class ArtworkCacheTests
{
    [TestMethod]
    public async Task GeneratesWebpThumbnailAndKeepsServingItWhenSourceIsOffline()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), $"jularr-artcache-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(Path.GetTempPath(), $"jularr-cover-{Guid.NewGuid():N}.png");
        try
        {
            WriteImage(sourcePath, 600, 900);

            var cache = new BesideMediaArtworkCache(NoDbStore(), cacheRoot);

            var first = await cache.GetThumbnailForSourceAsync(
                "book-abc", () => Task.FromResult<string?>(sourcePath), 240, CancellationToken.None);

            Assert.IsNotNull(first);
            Assert.IsTrue(first!.Path.StartsWith(cacheRoot, StringComparison.Ordinal), "Derivative must live under the cache root.");
            Assert.AreEqual("image/webp", first.MediaType);
            Assert.IsTrue(File.Exists(first.Path));

            using (var thumb = SKBitmap.Decode(first.Path))
            {
                Assert.IsNotNull(thumb);
                Assert.IsTrue(thumb!.Width <= 240, "Thumbnail is resized to the requested width.");
            }

            // NAS goes offline: the source is gone, but the cached derivative is still served.
            File.Delete(sourcePath);
            var offline = await cache.GetThumbnailForSourceAsync(
                "book-abc", () => Task.FromResult<string?>(null), 240, CancellationToken.None);

            Assert.IsNotNull(offline);
            Assert.AreEqual(first.Path, offline!.Path);
            Assert.IsTrue(File.Exists(offline.Path));

            // With neither a derivative nor a source, nothing is returned (caller shows a placeholder).
            var missing = await cache.GetThumbnailForSourceAsync(
                "book-missing", () => Task.FromResult<string?>(null), 240, CancellationToken.None);
            Assert.IsNull(missing);
        }
        finally
        {
            if (File.Exists(sourcePath))
            {
                File.Delete(sourcePath);
            }

            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    private static void WriteImage(string path, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(200, 80, 80));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }

    // GetThumbnailForSourceAsync never touches the store; a non-connecting context is enough.
    private static BesideMediaArtworkStore NoDbStore() =>
        new(new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=unused")
            .Options));
}
