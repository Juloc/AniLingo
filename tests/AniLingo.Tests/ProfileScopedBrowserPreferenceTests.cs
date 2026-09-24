namespace AniLingo.Tests;

[TestClass]
public sealed class ProfileScopedBrowserPreferenceTests
{
    [TestMethod]
    public void LayoutExposesAuthenticatedProfileForBrowserState()
    {
        var root = FindRepositoryRoot();
        var layout = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "Pages",
            "Shared",
            "_Layout.cshtml"));

        StringAssert.Contains(layout, "ClaimTypes.NameIdentifier");
        StringAssert.Contains(layout, "data-profile-id=\"@profileId\"");
    }

    [TestMethod]
    public void PlayerPreferenceIsProfileScoped()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "wwwroot",
            "js",
            "episode-player.js"));

        StringAssert.Contains(
            script,
            "anilingo.profile.${profileId}.playbackMode");
        Assert.IsFalse(
            script.Contains(
                "\"anilingo.playbackMode\"",
                StringComparison.Ordinal));
    }

    [TestMethod]
    public void NovelReaderPreferencesAreProfileScoped()
    {
        var root = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(
            root,
            "src",
            "AniLingo.Web",
            "wwwroot",
            "js",
            "novel-reader.js"));

        StringAssert.Contains(
            script,
            "anilingo.profile.${profileId}.novel");
        Assert.IsFalse(
            script.Contains(
                "\"anilingo.novel.view\"",
                StringComparison.Ordinal));
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

        throw new DirectoryNotFoundException(
            "Could not locate AniLingo repository root.");
    }
}
