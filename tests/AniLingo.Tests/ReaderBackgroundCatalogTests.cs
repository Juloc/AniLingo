using AniLingo.Web.Features.ReaderBackgrounds;

namespace AniLingo.Tests;

public sealed class ReaderBackgroundCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "anilingo-reader-backgrounds-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Discovers_new_genres_and_variants_from_the_filesystem()
    {
        Write("action/embers.webp");
        Write("action/sparks.webp");
        Write("brand-new-genre/aurora.webp");

        var catalog = new ReaderBackgroundCatalog(_root);
        var items = catalog.GetAll();

        Assert.Collection(
            items,
            item =>
            {
                Assert.Equal("action/embers", item.Id);
                Assert.Equal("action", item.Genre);
                Assert.Equal("Action", item.GenreLabel);
                Assert.Equal("Embers", item.Name);
                Assert.Equal("/reader-backgrounds/action/embers.webp", item.Url);
            },
            item => Assert.Equal("action/sparks", item.Id),
            item => Assert.Equal("brand-new-genre/aurora", item.Id));
    }

    [Theory]
    [InlineData("Sci-Fi", "sci-fi")]
    [InlineData("Crime / Detective", "crime-detective")]
    [InlineData("Slice of Life", "slice-of-life")]
    [InlineData("DARK FANTASY", "dark-fantasy")]
    public void Genre_matching_uses_normalized_folder_names(string sourceGenre, string expected)
    {
        Write($"{expected}/default.webp");

        var catalog = new ReaderBackgroundCatalog(_root);
        var match = catalog.FindBestMatch([sourceGenre]);

        Assert.NotNull(match);
        Assert.Equal(expected, match.Genre);
    }

    [Fact]
    public void Ignores_non_images_and_files_without_a_genre_directory()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "orphan.webp"), "");
        Write("horror/readme.txt");
        Write("horror/fog.png");

        var items = new ReaderBackgroundCatalog(_root).GetAll();

        var item = Assert.Single(items);
        Assert.Equal("horror/fog", item.Id);
    }

    [Fact]
    public void Returns_all_variants_but_a_stable_first_match_for_automatic_selection()
    {
        Write("romance/blossom.webp");
        Write("romance/soft-hearts.webp");

        var catalog = new ReaderBackgroundCatalog(_root);

        Assert.Equal(2, catalog.GetAll().Count);
        Assert.Equal("romance/blossom", catalog.FindBestMatch(["Romance"])?.Id);
        Assert.Equal(
            "romance/soft-hearts",
            catalog.FindById("romance/soft-hearts")?.Id);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Write(string relativePath)
    {
        var path = Path.Combine(
            _root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }
}
