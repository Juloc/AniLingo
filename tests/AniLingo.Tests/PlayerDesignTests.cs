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
        StringAssert.Contains(page, "data-player-action=\"repeatCurrentCue\"");

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

    [TestMethod]
    public void EpisodePlayerWiresTheSharedLanguageInspector()
    {
        // #233: the player opens the shared inspector for subtitle taps when
        // it is available, and only falls back to its own learning sheet
        // otherwise; it also mirrors state changes back into the cached cues.
        var root = FindRepositoryRoot();
        var episodeScript = File.ReadAllText(
            Path.Combine(root, "src", "AniLingo.Web", "wwwroot", "js", "episode-player.js"));

        StringAssert.Contains(episodeScript, "window.AniLingoLanguageInspector");
        StringAssert.Contains(episodeScript, "sharedInspector.open(");
        StringAssert.Contains(episodeScript, "sharedInspector.addEventListener(\"open\"");
        StringAssert.Contains(episodeScript, "sharedInspector.addEventListener(\"close\"");
        StringAssert.Contains(episodeScript, "sharedInspector.addEventListener(\"statechange\"");
    }

    [TestMethod]
    public void PlaybackSpeedsAndSeekStepComeFromTheCanonicalTokens()
    {
        var root = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "design", "player", "player-tokens.json")));
        var playback = document.RootElement.GetProperty("playback");
        var speeds = playback.GetProperty("speeds").EnumerateArray().Select(x => x.GetDouble()).ToArray();

        CollectionAssert.AreEqual(speeds, AniLingo.Web.Features.Playback.PlayerDesign.PlaybackSpeeds.ToArray(),
            "The server must read speeds from the embedded design tokens, not a copy.");
        CollectionAssert.AreEqual(speeds, AniLingo.Web.Features.Progress.PlaybackPreferenceRules.Speeds.ToArray());
        Assert.AreEqual(0.5, speeds.Min());
        Assert.AreEqual(2.0, speeds.Max());
        CollectionAssert.Contains(speeds, 1.0);
        Assert.AreEqual(
            playback.GetProperty("seekStepSeconds").GetInt32(),
            AniLingo.Web.Features.Playback.PlayerDesign.SeekStepSeconds);
    }

    [TestMethod]
    public void WebPlayerControlsUseCanonicalIconsTokensAndActions()
    {
        var root = FindRepositoryRoot();
        using var icons = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "design", "player", "player-icons.json")));
        using var tokens = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "design", "player", "player-tokens.json")));
        using var actions = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "design", "player", "player-actions.json")));

        var iconIds = icons.RootElement.GetProperty("icons").EnumerateObject().Select(x => x.Name).ToArray();
        var page = File.ReadAllText(
            Path.Combine(root, "src", "AniLingo.Web", "Pages", "Library", "Episode.cshtml"));
        var usedIcons = System.Text.RegularExpressions.Regex
            .Matches(page, "_PlayerIcon\" model=\"@\\(\"(?<id>[A-Za-z0-9]+)\"\\)")
            .Select(x => x.Groups["id"].Value)
            .ToArray();

        Assert.IsTrue(usedIcons.Length >= 7, "Player controls render their icons from player-icons.json.");
        foreach (var id in usedIcons)
        {
            CollectionAssert.Contains(iconIds, id);
            Assert.AreEqual(
                icons.RootElement.GetProperty("icons").GetProperty(id).GetProperty("path").GetString(),
                AniLingo.Web.Features.Playback.PlayerDesign.Icon(id).Path);
        }

        var partial = File.ReadAllText(
            Path.Combine(root, "src", "AniLingo.Web", "Pages", "Shared", "_PlayerIcon.cshtml"));
        StringAssert.Contains(partial, "PlayerDesign.Icon(Model)");

        foreach (var action in new[] { "seekBack10", "seekForward10", "repeatCurrentCue" })
        {
            StringAssert.Contains(page, $"data-player-action=\"{action}\"");
        }

        StringAssert.Contains(page, "data-playback-speed");
        StringAssert.Contains(page, "data-subtitle-track");
        StringAssert.Contains(page, "data-quality-cap");
        StringAssert.Contains(page, "data-audio-track");

        var designScript = File.ReadAllText(
            Path.Combine(root, "src", "AniLingo.Web", "wwwroot", "js", "player-design.js"));
        foreach (var id in actions.RootElement.GetProperty("actions").EnumerateArray()
                     .Select(x => x.GetProperty("id").GetString()!))
        {
            StringAssert.Contains(designScript, $"{id}: \"{id}\"");
        }

        var css = File.ReadAllText(
            Path.Combine(root, "src", "AniLingo.Web", "wwwroot", "css", "player.css"));
        var radius = tokens.RootElement.GetProperty("radiusDp").GetProperty("control").GetInt32();
        var playbackSubtitle = tokens.RootElement.GetProperty("typographySp").GetProperty("playbackSubtitle").GetInt32();
        StringAssert.Contains(css, $"--player-control-radius: {radius}px;");
        StringAssert.Contains(css, $"--player-playback-subtitle-size: {playbackSubtitle}px;");
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
