using AniLingo.Web.Features.Artwork;
using SkiaSharp;

namespace AniLingo.Tests;

[TestClass]
public sealed class LocalArtworkImportTests
{
    [TestMethod]
    public void PosterPrefersPosterBeforeFolderAndCover()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "cover.jpg"), "cover");
        File.WriteAllText(Path.Combine(directory.Path, "folder.png"), "folder");
        File.WriteAllText(Path.Combine(directory.Path, "poster.webp"), "poster");

        var result = LocalAnimeArtworkImporter.FindCandidate(
            directory.Path,
            AnimeArtworkKind.Poster);

        Assert.AreEqual("poster.webp", Path.GetFileName(result));
    }

    [TestMethod]
    public void FanartPrefersFanartBeforeBackdropBackgroundAndBanner()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "banner.jpg"), "banner");
        File.WriteAllText(Path.Combine(directory.Path, "background.jpg"), "background");
        File.WriteAllText(Path.Combine(directory.Path, "backdrop.png"), "backdrop");
        File.WriteAllText(Path.Combine(directory.Path, "fanart.webp"), "fanart");

        var result = LocalAnimeArtworkImporter.FindCandidate(
            directory.Path,
            AnimeArtworkKind.Fanart);

        Assert.AreEqual("fanart.webp", Path.GetFileName(result));
    }

    [TestMethod]
    public void ArtworkFileNamesAreMatchedCaseInsensitively()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Poster.JPG"), "poster");
        File.WriteAllText(Path.Combine(directory.Path, "FanArt.PNG"), "fanart");

        var poster = LocalAnimeArtworkImporter.FindCandidate(
            directory.Path,
            AnimeArtworkKind.Poster);
        var fanart = LocalAnimeArtworkImporter.FindCandidate(
            directory.Path,
            AnimeArtworkKind.Fanart);

        Assert.AreEqual("Poster.JPG", Path.GetFileName(poster));
        Assert.AreEqual("FanArt.PNG", Path.GetFileName(fanart));
    }

    [TestMethod]
    public void ArtworkSearchDoesNotUseSeasonSubfolderImages()
    {
        using var directory = new TemporaryDirectory();
        var season = Directory.CreateDirectory(Path.Combine(directory.Path, "Season 01"));
        File.WriteAllText(Path.Combine(season.FullName, "poster.jpg"), "season-poster");

        var result = LocalAnimeArtworkImporter.FindCandidate(
            directory.Path,
            AnimeArtworkKind.Poster);

        Assert.IsNull(result);
    }


    [TestMethod]
    public async Task PosterDerivativeIsBoundedTo512PixelsWithoutChangingAspectRatio()
    {
        await using var source = await CreatePngAsync(1000, 1500);
        await using var destination = new MemoryStream();

        var saved = await AnimeArtworkStore.CreateOptimizedDerivativeAsync(
            AnimeArtworkKind.Poster,
            source,
            destination,
            CancellationToken.None);

        Assert.IsTrue(saved);
        using var result = SKBitmap.Decode(destination.ToArray());
        Assert.IsNotNull(result);
        Assert.AreEqual(AnimeArtworkStore.PosterMaxWidth, result.Width);
        Assert.AreEqual(768, result.Height);
    }

    [TestMethod]
    public async Task FanartDerivativeIsBoundedTo1600Pixels()
    {
        await using var source = await CreatePngAsync(2000, 1000);
        await using var destination = new MemoryStream();

        var saved = await AnimeArtworkStore.CreateOptimizedDerivativeAsync(
            AnimeArtworkKind.Fanart,
            source,
            destination,
            CancellationToken.None);

        Assert.IsTrue(saved);
        using var result = SKBitmap.Decode(destination.ToArray());
        Assert.IsNotNull(result);
        Assert.AreEqual(AnimeArtworkStore.FanartMaxWidth, result.Width);
        Assert.AreEqual(800, result.Height);
    }

    [TestMethod]
    public async Task ArtworkDerivativeNeverUpscalesSmallImages()
    {
        await using var source = await CreatePngAsync(300, 450);
        await using var destination = new MemoryStream();

        var saved = await AnimeArtworkStore.CreateOptimizedDerivativeAsync(
            AnimeArtworkKind.Poster,
            source,
            destination,
            CancellationToken.None);

        Assert.IsTrue(saved);
        using var result = SKBitmap.Decode(destination.ToArray());
        Assert.IsNotNull(result);
        Assert.AreEqual(300, result.Width);
        Assert.AreEqual(450, result.Height);
    }

    [TestMethod]
    public async Task ArtworkDerivativeRejectsNonImageContent()
    {
        await using var source = new MemoryStream("not-an-image"u8.ToArray());
        await using var destination = new MemoryStream();

        var saved = await AnimeArtworkStore.CreateOptimizedDerivativeAsync(
            AnimeArtworkKind.Poster,
            source,
            destination,
            CancellationToken.None);

        Assert.IsFalse(saved);
        Assert.AreEqual(0, destination.Length);
    }

    private static Task<MemoryStream> CreatePngAsync(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return Task.FromResult(new MemoryStream(data.ToArray(), writable: false));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "anilingo-artwork-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
