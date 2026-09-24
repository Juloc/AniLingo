using System.Text.Json;

namespace AniLingo.Tests;

[TestClass]
public sealed class PlayerDesignTests
{
    [TestMethod]
    public void SharedPlayerContractContainsRequiredLearningActions()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "design", "player", "player-actions.json")));

        var ids = document.RootElement
            .GetProperty("actions")
            .EnumerateArray()
            .Select(x => x.GetProperty("id").GetString()!)
            .ToArray();

        CollectionAssert.Contains(ids, "learnCurrentCue");
        CollectionAssert.Contains(ids, "openWord");
        CollectionAssert.Contains(ids, "repeatCurrentCue");
        CollectionAssert.Contains(ids, "closeOverlay");
        Assert.AreEqual(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [TestMethod]
    public void WebPlayerUsesCanonicalTokenValuesAndLoadsSharedLayerFirst()
    {
        var root = FindRepositoryRoot();
        var tokenPath = Path.Combine(root, "design", "player", "player-tokens.json");
        using var document = JsonDocument.Parse(File.ReadAllText(tokenPath));

        var touchSize = document.RootElement
            .GetProperty("controlSizeDp")
            .GetProperty("touch")
            .GetInt32();
        var accent = document.RootElement
            .GetProperty("colors")
            .GetProperty("accent")
            .GetString()!;

        var css = File.ReadAllText(
            Path.Combine(root, "src", "AniLingo.Web", "wwwroot", "css", "player.css"));
        StringAssert.Contains(css, $"--player-control-size: {touchSize}px;");
        StringAssert.Contains(css.ToLowerInvariant(), $"--player-accent: {accent.ToLowerInvariant()};");

        var page = File.ReadAllText(
            Path.Combine(root, "src", "AniLingo.Web", "Pages", "Library", "Episode.cshtml"));
        StringAssert.Contains(page, "~/css/player.css");
        StringAssert.Contains(page, "~/js/player-design.js");
        StringAssert.Contains(page, "data-word-inspector");
        StringAssert.Contains(page, "data-player-action="repeatCurrentCue"");

        var designScriptIndex = page.IndexOf("~/js/player-design.js", StringComparison.Ordinal);
        var episodeScriptIndex = page.IndexOf("~/js/episode-player.js", StringComparison.Ordinal);
        Assert.IsTrue(designScriptIndex >= 0 && designScriptIndex < episodeScriptIndex);
    }

    [TestMethod]
    public void SharedWebLayerRendersInteractiveSubtitleWords()
    {
        var root = FindRepositoryRoot();
        var designScript = File.ReadAllText(
            Path.Combine(root, "src", "AniLingo.Web", "wwwroot", "js", "player-design.js"));
        var episodeScript = File.ReadAllText(
            Path.Combine(root, "src", "AniLingo.Web", "wwwroot", "js", "episode-player.js"));

        StringAssert.Contains(designScript, "token.isInteractive");
        StringAssert.Contains(designScript, "actions.openWord");
        StringAssert.Contains(designScript, "actions.learnCurrentCue");
        StringAssert.Contains(episodeScript, "design.actions.repeatCurrentCue");
        StringAssert.Contains(episodeScript, "learningResumeOnClose");
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
