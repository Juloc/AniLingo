using AniLingo.Web.Features.ReaderThemes;

namespace AniLingo.Tests;

[TestClass]
public sealed class ReaderThemeCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "anilingo-reader-themes-" + Guid.NewGuid().ToString("N"));

    [TestMethod]
    public void Discovers_legacy_flat_theme_without_a_manifest()
    {
        Write("horror/fog-forest.webp");

        var items = new ReaderThemeCatalog(_root).GetAll();
        Assert.AreEqual(1, items.Count);
        var item = items[0];

        Assert.AreEqual("horror/fog-forest", item.Id);
        Assert.AreEqual("Horror", item.GenreLabel);
        Assert.AreEqual("Fog Forest", item.Name);
        Assert.AreEqual(
            "/reader-backgrounds/horror/fog-forest.webp",
            item.Assets.Page?.Default);
        Assert.AreEqual(item.Assets.Page, item.Assets.ScrollStatic);
    }

    [TestMethod]
    public void Directory_theme_keeps_same_id_and_discovers_modes_and_breakpoints()
    {
        Write("sci-fi/starchart/page.webp");
        Write("sci-fi/starchart/page-mobile.webp");
        Write("sci-fi/starchart/scroll.webp");
        Write("sci-fi/starchart/parallax-back.webp");
        Write("sci-fi/starchart/parallax-mid.webp");

        var items = new ReaderThemeCatalog(_root).GetAll();
        Assert.AreEqual(1, items.Count);
        var item = items[0];

        Assert.AreEqual("sci-fi/starchart", item.Id);
        Assert.AreEqual(
            "/reader-backgrounds/sci-fi/starchart/page-mobile.webp",
            item.Assets.Page?.Mobile);
        Assert.AreEqual(
            "/reader-backgrounds/sci-fi/starchart/scroll.webp",
            item.Assets.ScrollStatic?.Default);
        Assert.IsTrue(item.HasParallax);
    }

    [TestMethod]
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
                "blurPx": 99,
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

        Assert.IsNotNull(item);
        Assert.AreEqual("sci-fi/starchart", item.Id);
        Assert.AreEqual(1.5, item.Appearance.Brightness);
        Assert.AreEqual(12, item.Appearance.BlurPx);
        Assert.AreEqual(0, item.Appearance.Grain);
        Assert.AreEqual("#112233", item.Appearance.Tint);
        Assert.AreEqual("stars", item.Motion.Effect);
        Assert.AreEqual(1, item.Motion.EffectStrength);
        Assert.AreEqual(.6, item.Motion.FrontSpeed);
    }

    [TestMethod]
    public void Book_specific_theme_can_inherit_assets_and_override_only_selected_values()
    {
        Write("fantasy/enchanted/page.webp");
        Write("books/sample-light-novel/page-mobile.webp");
        WriteText(
            "fantasy/enchanted/theme.json",
            """
            {
              "appearance": {
                "brightness": 0.9,
                "contrast": 1.1
              },
              "motion": {
                "effect": "shimmer",
                "effectStrength": 0.2
              }
            }
            """);
        WriteText(
            "books/sample-light-novel/theme.json",
            """
            {
              "label": "Sample Light Novel",
              "extends": "fantasy/enchanted",
              "appearance": {
                "brightness": 0.75
              }
            }
            """);

        var catalog = new ReaderThemeCatalog(_root);
        var item = catalog.FindById("books/sample-light-novel");

        Assert.IsNotNull(item);
        Assert.AreEqual("fantasy/enchanted", item.Extends);
        Assert.AreEqual(
            "/reader-backgrounds/fantasy/enchanted/page.webp",
            item.Assets.Page?.Default);
        Assert.AreEqual(
            "/reader-backgrounds/books/sample-light-novel/page-mobile.webp",
            item.Assets.Page?.Mobile);
        Assert.AreEqual(0.75, item.Appearance.Brightness);
        Assert.AreEqual(1.1, item.Appearance.Contrast);
        Assert.AreEqual("shimmer", item.Motion.Effect);
        Assert.AreEqual(0.2, item.Motion.EffectStrength);
    }

    [TestMethod]
    public void Circular_theme_inheritance_is_ignored_safely()
    {
        WriteText(
            "books/first/theme.json",
            """{ "extends": "books/second" }""");
        WriteText(
            "books/second/theme.json",
            """{ "extends": "books/first" }""");

        var items = new ReaderThemeCatalog(_root).GetAll();

        Assert.AreEqual(0, items.Count);
    }

    [TestMethod]
    public void Unsupported_effect_falls_back_to_none()
    {
        Write("mystery/manor/page.webp");
        WriteText(
            "mystery/manor/theme.json",
            """{ "motion": { "effect": "javascript" } }""");

        var items = new ReaderThemeCatalog(_root).GetAll();
        Assert.AreEqual(1, items.Count);
        var item = items[0];

        Assert.AreEqual("none", item.Motion.Effect);
    }

    [DataTestMethod]
    [DataRow("Sci-Fi", "sci-fi")]
    [DataRow("Crime / Detective", "crime-detective")]
    [DataRow("Slice of Life", "slice-of-life")]
    [DataRow("DARK FANTASY", "dark-fantasy")]
    public void Normalizes_genre_keys(string value, string expected)
    {
        Assert.AreEqual(expected, ReaderThemeCatalog.NormalizeKey(value));
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
