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

        var drawer = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "Pages",
            "Novels",
            "_NovelChapterDrawer.cshtml"));

        StringAssert.Contains(page, "data-reader-chapters-toggle");
        StringAssert.Contains(page, "_NovelChapterDrawer");
        StringAssert.Contains(page, "data-chapters-url");
        StringAssert.Contains(drawer, "data-chapter-drawer");
        StringAssert.Contains(drawer, "data-chapter-list");
        StringAssert.Contains(page, "<svg");
        Assert.IsFalse(page.Contains("🔖", StringComparison.Ordinal));
        Assert.IsFalse(page.Contains("✎", StringComparison.Ordinal));
    }

    [TestMethod]
    public void WorkPageUsesSharedAniListProgressCardLoadedAfterFirstPaint()
    {
        var root = FindRepositoryRoot();
        var novels = Path.Combine(root, "src", "AniLingo.Web", "Pages", "Novels");
        var page = File.ReadAllText(Path.Combine(novels, "Work.cshtml"));
        var model = File.ReadAllText(Path.Combine(novels, "Work.cshtml.cs"));

        StringAssert.Contains(page, "<partial name=\"_ExternalProgress\"");
        StringAssert.Contains(model, "GetNovelProgressSummaryAsync");
        StringAssert.Contains(model, "OnGetExternalProgressAsync");
        StringAssert.Contains(model, "ExternalProgressMediaKind.Novel");
        Assert.IsFalse(model.Contains("GetNovelProgressPreviewAsync", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReaderDoesNotEmbedTheFullChapterIndex()
    {
        var root = FindRepositoryRoot();
        var novels = Path.Combine(root, "src", "AniLingo.Web", "Pages", "Novels");
        var page = File.ReadAllText(Path.Combine(novels, "Read.cshtml"));
        var drawer = File.ReadAllText(Path.Combine(novels, "_NovelChapterDrawer.cshtml"));

        Assert.IsFalse(page.Contains("data-chapter-data", StringComparison.Ordinal));
        Assert.IsFalse(drawer.Contains("data-chapter-data", StringComparison.Ordinal));
        Assert.IsFalse(page.Contains("Model.Chapters", StringComparison.Ordinal));
        Assert.IsFalse(drawer.Contains("Model.Chapters", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReaderScriptsAreFocusedModulesUnderOneBootstrap()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "Pages", "Novels", "Read.cshtml"));
        var js = Path.Combine(root, "src", "AniLingo.Web", "wwwroot", "js");
        var bootstrap = File.ReadAllText(Path.Combine(js, "novel-reader.js"));
        var modules = new[]
        {
            "novel-position.js",
            "novel-annotations.js",
            "novel-chapter-drawer.js",
            "novel-translation.js"
        };

        var bootstrapIndex = page.IndexOf("~/js/novel-reader.js", StringComparison.Ordinal);
        foreach (var module in modules)
        {
            var source = File.ReadAllText(Path.Combine(js, module));
            StringAssert.Contains(source, "window.AniLingoNovelReader");
            Assert.IsFalse(
                source.Contains("document.querySelector(\"[data-novel-reader]\")", StringComparison.Ordinal),
                $"{module} must not bootstrap itself.");
            var moduleIndex = page.IndexOf("~/js/" + module, StringComparison.Ordinal);
            Assert.IsTrue(moduleIndex >= 0 && moduleIndex < bootstrapIndex, $"{module} loads before the bootstrap.");
        }

        StringAssert.Contains(bootstrap, "document.querySelector(\"[data-novel-reader]\")");
        StringAssert.Contains(bootstrap, "modules.position(reader)");
        StringAssert.Contains(bootstrap, "modules.annotations(reader)");
        StringAssert.Contains(bootstrap, "modules.chapterDrawer(reader)");
        StringAssert.Contains(bootstrap, "modules.translation(reader)");
    }

    [TestMethod]
    public void SavedHighlightsRenderAsFlatSegmentsSoOverlapsSurvive()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "wwwroot", "js", "novel-annotations.js"));
        var css = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "wwwroot", "css", "novel-reader-panels.css"));

        StringAssert.Contains(script, "buildHighlightSegments");
        StringAssert.Contains(script, "highlightIds");
        Assert.IsFalse(script.Contains("surroundContents", StringComparison.Ordinal));
        StringAssert.Contains(css, "data-highlight-depth=\"2\"");
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
        StringAssert.Contains(page, "data-language=\"ja\"");
        StringAssert.Contains(page, "data-language=\"de\"");
        StringAssert.Contains(
            css,
            ".novel-reader-shell[data-view=\"both\"] .novel-reader-segment");
        StringAssert.Contains(
            css,
            ".novel-reader-shell[data-view=\"both\"] .novel-reader-paragraph.de");
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
        var js = Path.Combine(root, "src", "AniLingo.Web", "wwwroot", "js");
        var drawer = File.ReadAllText(Path.Combine(js, "novel-chapter-drawer.js"));
        var translation = File.ReadAllText(Path.Combine(js, "novel-translation.js"));
        var page = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "Pages", "Novels", "Read.cshtml"));

        StringAssert.Contains(drawer, "ensureChapterRows");
        StringAssert.Contains(translation, "installGermanParagraphs");
        StringAssert.Contains(translation, "waitForTranslation");
        StringAssert.Contains(page, "\"TranslationStatus\"");
        foreach (var file in Directory.GetFiles(js, "novel-*.js"))
        {
            Assert.IsFalse(
                File.ReadAllText(file).Contains("window.location.reload", StringComparison.OrdinalIgnoreCase),
                Path.GetFileName(file));
        }
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
