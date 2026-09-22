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
    public void ExtractsEpisodeTitleBeforeReleaseMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "anime");
        var path = Path.Combine(
            root,
            "Mushoku Tensei - Jobless Reincarnation",
            "Season 03",
            "Mushoku Tensei - Jobless Reincarnation - S03E13 - The Diary WEBRip-1080p h265 AAC[JA] [ZH+EN+AR+FR+DE+ID+IT+MS+PL] Feibanyama tt13293588.mkv");

        var parsed = MediaPathParser.TryParse(root, path, out var descriptor);

        Assert.IsTrue(parsed);
        Assert.AreEqual("Mushoku Tensei - Jobless Reincarnation", descriptor.AnimeTitle);
        Assert.AreEqual(3, descriptor.SeasonNumber);
        Assert.AreEqual(13, descriptor.EpisodeNumber);
        Assert.AreEqual("The Diary", descriptor.EpisodeTitle);
    }

    [TestMethod]
    public void FallsBackToEpisodeNumberWhenFilenameHasNoEpisodeTitle()
    {
        var root = Path.Combine(Path.GetTempPath(), "anime");
        var path = Path.Combine(
            root,
            "Sousou no Frieren",
            "Season 01",
            "Sousou no Frieren - S01E03 [1080p].mkv");

        var parsed = MediaPathParser.TryParse(root, path, out var descriptor);

        Assert.IsTrue(parsed);
        Assert.AreEqual("Episode 3", descriptor.EpisodeTitle);
    }

    [TestMethod]
    public void RejectsFilesWithoutAnEpisodeNumber()
    {
        var root = Path.Combine(Path.GetTempPath(), "anime");
        var path = Path.Combine(root, "Sousou no Frieren", "special.mkv");

        Assert.IsFalse(MediaPathParser.TryParse(root, path, out _));
    }
}
