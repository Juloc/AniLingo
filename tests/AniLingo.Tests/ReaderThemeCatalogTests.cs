using Xunit;
using AniLingo.Web.Features.ReaderThemes;

namespace AniLingo.Tests;

public sealed class ReaderThemeCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "anilingo-reader-themes-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Discovers_legacy_flat_theme_without_a_manifest()
    {
        Write("horror/fog-forest.webp");

        var item = Assert.Single(new ReaderThemeCatalog(_root).GetAll());

        Assert.Equal("horror/fog-forest", item.Id);
        Assert.Equal("Horror", item.GenreLabel);
        Assert.Equal("Fog Forest", item.Name);
        Assert.Equal(
            "/reader-backgrounds/horror/fog-forest.webp",
            item.Assets.Page?.Default);
        Assert.Equal(item.Assets.Page, item.Assets.ScrollStatic);
    }

    [Fact]
    public void Directory_theme_keeps_same_id_and_discovers_modes_and_breakpoints()
    {
        Write("sci-fi/starchart/page.webp");
        Write("sci-fi/starchart/page-mobile.webp");
        Write("sci-fi/starchart/scroll.webp");
        Write("sci-fi/starchart/parallax-back.webp");
        Write("sci-fi/starchart/parallax-mid.webp");

        var item = Assert.Single(new ReaderThemeCatalog(_root).GetAll());

        Assert.Equal("sci-fi/starchart", item.Id);
        Assert.Equal(
            "/reader-backgrounds/sci-fi/starchart/page-mobile.webp",
            item.Assets.Page?.Mobile);
        Assert.Equal(
            "/reader-backgrounds/sci-fi/starchart/scroll.webp",
            item.Assets.ScrollStatic?.Default);
        Assert.True(item.HasParallax);
    }

    [Fact]
    public void Manifest_aliases_drive_genre_matching_and_values_are_bounded()
    {
        Write("sci-fi/starchart/page.webp");
        WriteText(
            "sci-fi/starchart/theme.json",
            """
            {
              "label": "Star Chart",
              "aliases": ["Science Fiction", "Space"],
              "appearance": {
                "brightness": 99,
                "grain": -2,
                "tint": "#112233"
              },
              "motion": {
                "effect": "stars",
                "effectStrength": 9,
                "frontSpeed": 9
              }
            }
            """);

        var catalog = new ReaderThemeCatalog(_root);
        var item = catalog.FindBestMatch(["Science Fiction"]);

        Assert.NotNull(item);
        Assert.Equal("sci-fi/starchart", item.Id);
        Assert.Equal(1.5, item.Appearance.Brightness);
        Assert.Equal(0, item.Appearance.Grain);
        Assert.Equal("#112233", item.Appearance.Tint);
        Assert.Equal("stars", item.Motion.Effect);
        Assert.Equal(1, item.Motion.EffectStrength);
        Assert.Equal(.6, item.Motion.FrontSpeed);
    }

    [Fact]
    public void Unsupported_effect_falls_back_to_none()
    {
        Write("mystery/manor/page.webp");
        WriteText(
            "mystery/manor/theme.json",
            """{ "motion": { "effect": "javascript" } }""");

        var item = Assert.Single(new ReaderThemeCatalog(_root).GetAll());

        Assert.Equal("none", item.Motion.Effect);
    }

    [Theory]
    [InlineData("Sci-Fi", "sci-fi")]
    [InlineData("Crime / Detective", "crime-detective")]
    [InlineData("Slice of Life", "slice-of-life")]
    [InlineData("DARK FANTASY", "dark-fantasy")]
    public void Normalizes_genre_keys(string value, string expected)
    {
        Assert.Equal(expected, ReaderThemeCatalog.NormalizeKey(value));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void Write(string relativePath) =>
        WriteText(relativePath, "");

    private void WriteText(string relativePath, string content)
    {
        var path = Path.Combine(
            _root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
