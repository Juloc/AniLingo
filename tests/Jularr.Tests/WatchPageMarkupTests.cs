using System.Text.RegularExpressions;

namespace Jularr.Tests;

[TestClass]
public sealed class WatchPageMarkupTests
{
    private static readonly string Page = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(), "src", "Jularr.Web", "Pages", "Library", "Episode.cshtml"));

    [TestMethod]
    public void PlayerHasOwnChromeWithExactlyOneTimelineAndNoNativeControls()
    {
        var video = Regex.Match(Page, "<video[^>]*>").Value;
        Assert.IsFalse(video.Contains("controls", StringComparison.Ordinal), video);

        Assert.AreEqual(1, Regex.Matches(Page, "data-playback-timeline[\\s/>]").Count, "Exactly one timeline.");
        Assert.AreEqual(1, Regex.Matches(Page, "type=\"range\"[^>]*data-playback-timeline").Count
            + Regex.Matches(Page, "data-playback-timeline[^-][^>]*type=\"range\"").Count);
    }

    [TestMethod]
    public void EveryPlayerControlExistsExactlyOnce()
    {
        foreach (var hook in new[]
                 {
                     "data-chrome-play", "data-chrome-settings-toggle", "data-chrome-fullscreen", "data-chrome-subtitles",
                     "data-chrome-pip", "data-chrome-mute", "data-player-next", "data-repeat-line",
                     "data-player-action=\"seekBack10\"", "data-player-action=\"seekForward10\""
                 })
        {
            Assert.AreEqual(1, Regex.Matches(Page, Regex.Escape(hook) + "[\\s>]").Count, $"{hook} must appear once.");
        }

        // The speed shortcut duplicated the speed menu entry; the top bar only carries the title.
        StringAssert.DoesNotMatch(Page, new Regex("data-chrome-speed"));
        StringAssert.DoesNotMatch(Page, new Regex("player-top-actions"));
        StringAssert.DoesNotMatch(Page, new Regex(">\\s*✕\\s*<"), "Close buttons use the shared close icon.");
    }

    [TestMethod]
    public void PictureSubtitlesAreMarkedAsBurnedInInTheMenu()
    {
        StringAssert.Contains(Page, "ui[\"library.episode.subtitleBurnedIn\"]");
        StringAssert.Contains(Page, "ui[\"library.episode.subtitleUnsupported\"]");
        StringAssert.Contains(Page, "data-image=");
        StringAssert.Contains(Page, "data-subtitle-hint");
    }

    [TestMethod]
    public void EverySettingLivesInsideThePlayerStage()
    {
        var stageStart = Page.IndexOf("data-video-stage", StringComparison.Ordinal);
        var stageEnd = Page.IndexOf("data-player-controls-data", StringComparison.Ordinal);
        Assert.IsTrue(stageStart > 0 && stageEnd > stageStart);
        var stage = Page[stageStart..stageEnd];

        foreach (var hook in new[]
                 {
                     "data-playback-speed", "data-subtitle-track", "data-audio-track", "data-quality-cap",
                     "data-playback-mode", "data-autoplay-toggle", "data-restart", "data-save-playback-defaults",
                     "data-chrome-fullscreen", "data-chrome-pip", "data-chrome-volume"
                 })
        {
            StringAssert.Contains(stage, hook, $"{hook} must be part of the player.");
        }
    }

    [TestMethod]
    public void SourceAndMappingManagementIsOwnerOnly()
    {
        var ownerBlock = Page.IndexOf("@if (Model.IsOwner)\r\n{", StringComparison.Ordinal) is var crlf and >= 0
            ? crlf
            : Page.IndexOf("@if (Model.IsOwner)\n{", StringComparison.Ordinal);
        Assert.IsTrue(ownerBlock > 0, "Owner-only dialog block exists.");

        var subtitleSources = Page.IndexOf("_EpisodeSubtitleSources", StringComparison.Ordinal);
        Assert.IsTrue(subtitleSources > ownerBlock, "Subtitle source mapping renders only in the owner dialog.");
        Assert.AreEqual(1, Regex.Matches(Page, "CanManageMapping: true").Count);
        StringAssert.Contains(Page, "Model.ExternalProgress is { IsMatched: true, ReviewReason: null } externalProgress");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Jularr.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
