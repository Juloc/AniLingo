using AniLingo.Web.Features.Artwork;

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
