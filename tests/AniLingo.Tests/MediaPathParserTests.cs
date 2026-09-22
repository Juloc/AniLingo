using AniLingo.Web.Features.Library;

namespace AniLingo.Tests;

[TestClass]
public sealed class MediaPathParserTests
{
    [TestMethod]
    public void ParsesSeasonEpisodeFromCommonNasLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "anime");
        var path = Path.Combine(root, "Sousou no Frieren", "Season 01", "Sousou no Frieren - S01E03.mkv");

        var parsed = MediaPathParser.TryParse(root, path, out var descriptor);

        Assert.IsTrue(parsed);
        Assert.AreEqual("Sousou no Frieren", descriptor.AnimeTitle);
        Assert.AreEqual(1, descriptor.SeasonNumber);
        Assert.AreEqual(3, descriptor.EpisodeNumber);
    }

    [TestMethod]
    public void RejectsFilesWithoutAnEpisodeNumber()
    {
        var root = Path.Combine(Path.GetTempPath(), "anime");
        var path = Path.Combine(root, "Sousou no Frieren", "special.mkv");

        Assert.IsFalse(MediaPathParser.TryParse(root, path, out _));
    }
}
