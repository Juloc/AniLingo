namespace AniLingo.Tests;

[TestClass]
public sealed class UnifiedReaderShellTests
{
    [TestMethod]
    public void BooksAndNovelsUseTheSameSettingsPartialAndShellRuntime()
    {
        var root = FindRepositoryRoot();
        var novel = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "Pages", "Novels", "Read.cshtml"));
        var book = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "Pages", "Books", "Read.cshtml"));
        var shared = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "Pages", "Shared", "_ReaderSettingsPanel.cshtml"));

        StringAssert.Contains(novel, "data-unified-reader");
        StringAssert.Contains(book, "data-unified-reader");
        StringAssert.Contains(novel, "_ReaderSettingsPanel");
        StringAssert.Contains(book, "_ReaderSettingsPanel");
        StringAssert.Contains(novel, "reader-shell.js");
        StringAssert.Contains(book, "reader-shell.js");
        StringAssert.Contains(novel, "reader-shell.css");
        StringAssert.Contains(book, "reader-shell.css");

        StringAssert.Contains(shared, "data-reader-settings-form");
        StringAssert.Contains(shared, "data-book-settings-form");
        StringAssert.Contains(shared, "data-reader-setting");
        StringAssert.Contains(shared, "data-book-setting");
        StringAssert.Contains(shared, "data-reader-background-select");
        StringAssert.Contains(shared, "data-reader-genre-select");

        Assert.IsFalse(novel.Contains(
            "<select name="ReadingMode"",
            StringComparison.Ordinal));
        Assert.IsFalse(book.Contains(
            "Settings.ReadingMode",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void UnifiedChromeStartsVisibleAndRestoreDoesNotHideIt()
    {
        var root = FindRepositoryRoot();
        var shell = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "wwwroot", "js", "reader-shell.js"));
        var novel = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "wwwroot", "js", "novel-reader.js"));
        var book = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "wwwroot", "js", "books-reader.js"));

        StringAssert.Contains(shell, "root.classList.remove("reader-chrome-hidden")");
        StringAssert.Contains(shell, "accumulatedScroll > 64");
        StringAssert.Contains(shell, "event.clientY <= 24");
        StringAssert.Contains(shell, "toggleChrome()");
        StringAssert.Contains(shell, "anilingo:reader-restoring");

        StringAssert.Contains(novel, "anilingo:reader-restoring");
        StringAssert.Contains(book, "anilingo:reader-restoring");
        Assert.IsFalse(novel.Contains(
            "hideReaderChrome",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void DurableReaderAppearanceDoesNotUseLegacyNovelLocalStorage()
    {
        var root = FindRepositoryRoot();
        var novel = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "wwwroot", "js", "novel-reader.js"));

        Assert.IsFalse(novel.Contains("storage.size", StringComparison.Ordinal));
        Assert.IsFalse(novel.Contains("storage.leading", StringComparison.Ordinal));
        Assert.IsFalse(novel.Contains("storage.width", StringComparison.Ordinal));
        Assert.IsFalse(novel.Contains("storage.theme", StringComparison.Ordinal));
        Assert.IsFalse(novel.Contains("data-reader-adjust", StringComparison.Ordinal));
        Assert.IsFalse(novel.Contains("data-reader-theme", StringComparison.Ordinal));
        StringAssert.Contains(novel, "storage.view");
    }

    [TestMethod]
    public void SharedSettingsExposeReaderFirstHierarchy()
    {
        var root = FindRepositoryRoot();
        var shell = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "wwwroot", "js", "reader-shell.js"));
        var css = File.ReadAllText(Path.Combine(
            root, "src", "AniLingo.Web", "wwwroot", "css", "reader-shell.css"));

        StringAssert.Contains(shell, "["reading", "Lesen"]");
        StringAssert.Contains(shell, "["text", "Text"]");
        StringAssert.Contains(shell, "["appearance", "Aussehen"]");
        StringAssert.Contains(shell, "["defaults", "Defaults"]");
        StringAssert.Contains(shell, "[["continuous", "Scrollen"], ["paged", "Seiten"]]");
        StringAssert.Contains(shell, "data-reader-mode-only");
        StringAssert.Contains(shell, "Alle " + humanType");
        StringAssert.Contains(shell, "Genre: " + genre");
        StringAssert.Contains(shell, "Mein globaler Standard");

        StringAssert.Contains(css, "width: min(410px");
        StringAssert.Contains(css, "max-height: min(88dvh");
        StringAssert.Contains(css, "min-height: 48px");
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
