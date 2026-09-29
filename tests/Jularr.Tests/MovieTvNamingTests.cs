using Jularr.Web.Features.Movies;
using Jularr.Web.Features.Tv;

namespace Jularr.Tests;

/// <summary>Library naming for the video media types: movie single-unit folders (#593) and TV season/episode layout (#594).</summary>
[TestClass]
public sealed class MovieTvNamingTests
{
    [TestMethod]
    public void MovieFolderAndFileCarryTitleAndYear()
    {
        Assert.AreEqual("Inception (2010)", MovieNaming.FolderName("Inception", 2010));
        Assert.AreEqual("Inception (2010).mkv", MovieNaming.FileName("Inception", 2010, ".mkv"));
        Assert.AreEqual("Inception (2010).mkv", MovieNaming.FileName("Inception", 2010, "mkv"));
    }

    [TestMethod]
    public void MovieNameFallsBackWhenYearOrTitleMissing()
    {
        Assert.AreEqual("Inception", MovieNaming.FolderName("Inception", null));
        Assert.AreEqual("Untitled", MovieNaming.FolderName("", 0));
    }

    [TestMethod]
    public void MovieNameSanitisesIllegalCharacters()
    {
        // Colons and slashes are illegal on Windows/SMB shares; they are folded to "_".
        Assert.AreEqual("Mission_ Impossible (1996)", MovieNaming.FolderName("Mission: Impossible", 1996));
    }

    [TestMethod]
    public void TvLayoutIsSeriesSeasonEpisode()
    {
        Assert.AreEqual("Breaking Bad (2008)", TvNaming.SeriesFolderName("Breaking Bad", 2008));
        Assert.AreEqual("Season 01", TvNaming.SeasonFolderName(1));
        Assert.AreEqual("Specials", TvNaming.SeasonFolderName(0));
        Assert.AreEqual(
            "Breaking Bad - S01E02.mkv",
            TvNaming.EpisodeFileName("Breaking Bad", 1, 2, null, ".mkv"));
        Assert.AreEqual(
            "Breaking Bad - S01E02 - Cat's in the Bag.mkv",
            TvNaming.EpisodeFileName("Breaking Bad", 1, 2, "Cat's in the Bag", ".mkv"));
    }
}
