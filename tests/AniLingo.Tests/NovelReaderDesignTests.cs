namespace AniLingo.Tests;

[TestClass]
public sealed class NovelReaderDesignTests
{
    [TestMethod]
    public void ReaderUsesChapterDrawerAndStableSvgChrome()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "Pages",
            "Novels",
            "Read.cshtml"));

        StringAssert.Contains(page, "data-reader-chapters-toggle");
        StringAssert.Contains(page, "data-chapter-drawer");
        StringAssert.Contains(page, "data-chapter-data");
        StringAssert.Contains(page, "<svg");
        Assert.IsFalse(page.Contains("🔖", StringComparison.Ordinal));
        Assert.IsFalse(page.Contains("✎", StringComparison.Ordinal));
    }

    [TestMethod]
    public void BilingualReaderPairsLanguagesBySegment()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "Pages",
            "Novels",
            "Read.cshtml"));
        var css = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "wwwroot",
            "css",
            "novels.css"));

        StringAssert.Contains(page, "data-reader-segment");
        StringAssert.Contains(page, "data-language="ja"");
        StringAssert.Contains(page, "data-language="de"");
        StringAssert.Contains(
            css,
            ".novel-reader-shell[data-view="both"] .novel-reader-segment");
        StringAssert.Contains(
            css,
            ".novel-reader-shell[data-view="both"] .novel-reader-paragraph.de");
    }

    [TestMethod]
    public void ReaderKeepsProgressOutsideReadingMeasureAndHidesAppNavigation()
    {
        var root = FindRepositoryRoot();
        var css = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "wwwroot",
            "css",
            "novels.css"));

        StringAssert.Contains(css, ".novel-reader-progress-rail");
        StringAssert.Contains(css, "right: 12px;");
        StringAssert.Contains(css, ".novel-mobile-bookmark-track");
        StringAssert.Contains(css, "body:has(.novel-reader-shell) .mobile-nav");
        StringAssert.Contains(css, "body:has(.novel-reader-shell) .sidebar");
    }

    [TestMethod]
    public void ReaderDefersChapterDomAndActivatesTranslationWithoutReload()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "wwwroot",
            "js",
            "novel-reader.js"));

        StringAssert.Contains(script, "ensureChapterRows");
        StringAssert.Contains(script, "installGermanParagraphs");
        StringAssert.Contains(script, "waitForTranslation");
        StringAssert.Contains(script, "TranslationStatus");
        Assert.IsFalse(
            script.Contains("window.location.reload", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void NovelPagesAvoidDecorativeAiStyleSubheadings()
    {
        var root = FindRepositoryRoot();
        var index = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "Pages",
            "Novels",
            "Index.cshtml"));
        var work = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "Pages",
            "Novels",
            "Work.cshtml"));

        Assert.IsFalse(index.Contains("eyebrow", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(index.Contains(
            "reading progress is saved automatically",
            StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(work.Contains(
            "Source cache, AniList metadata and anime matching",
            StringComparison.Ordinal));
        Assert.IsFalse(work.Contains(
            "Manual mappings are authoritative",
            StringComparison.Ordinal));
        StringAssert.Contains(work, "isEarlier");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AniLingo.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AniLingo repository root.");
    }
}
