using AniLingo.Web.Features.Sonarr;

namespace AniLingo.Tests;

[TestClass]
public sealed class SonarrImportTests
{
    [TestMethod]
    public void FolderNameMatchesExistingLocalAnimeBeforeTitle()
    {
        var anime = new[]
        {
            new SonarrLocalAnime(
                Guid.NewGuid(),
                "Mushoku Tensei - Jobless Reincarnation",
                "mushoku tensei - jobless reincarnation")
        };
        var series = new[]
        {
            new SonarrSeriesItem(
                7,
                "Mushoku Tensei",
                "/data/anime/Mushoku Tensei - Jobless Reincarnation",
                [])
        };

        var matches = SonarrSeriesMatcher.Match(anime, series, []);

        Assert.AreEqual(7, matches[anime[0].Id].Id);
    }

    [TestMethod]
    public void PreviousMappingSurvivesLaterTitleChanges()
    {
        var animeId = Guid.NewGuid();
        var anime = new[]
        {
            new SonarrLocalAnime(animeId, "New local title", "new local title")
        };
        var series = new[]
        {
            new SonarrSeriesItem(11, "Completely different Sonarr title", "/data/anime/Other", [])
        };
        var previous = new[]
        {
            new SonarrArtworkMapping(
                animeId,
                11,
                "Old title",
                "/data/anime/Old",
                DateTime.UtcNow.AddDays(-1))
        };

        var matches = SonarrSeriesMatcher.Match(anime, series, previous);

        Assert.AreEqual(11, matches[animeId].Id);
    }


    [TestMethod]
    public void ExistingArtworkWithoutLegacyIdentityIsAdoptedWithoutDownload()
    {
        Assert.IsFalse(SonarrArtworkCache.ShouldDownload(
            previousIdentity: null,
            currentIdentity: "/MediaCover/7/poster-250.jpg?lastWrite=123",
            cacheExists: true));
    }

    [TestMethod]
    public void UnchangedTrackedArtworkDoesNotDownload()
    {
        const string identity = "/MediaCover/7/poster-250.jpg?lastWrite=123";

        Assert.IsFalse(SonarrArtworkCache.ShouldDownload(
            identity,
            identity,
            cacheExists: true));
    }

    [TestMethod]
    public void ChangedArtworkIdentityDownloadsAgain()
    {
        Assert.IsTrue(SonarrArtworkCache.ShouldDownload(
            "/MediaCover/7/poster-250.jpg?lastWrite=123",
            "/MediaCover/7/poster-250.jpg?lastWrite=456",
            cacheExists: true));
    }

    [TestMethod]
    public void MissingArtworkCacheDownloadsAgain()
    {
        Assert.IsTrue(SonarrArtworkCache.ShouldDownload(
            "/MediaCover/7/poster-250.jpg?lastWrite=123",
            "/MediaCover/7/poster-250.jpg?lastWrite=123",
            cacheExists: false));
    }

    [TestMethod]
    public void SonarrImageIdentityPrefersLocalCachedImageUrl()
    {
        var image = new SonarrImage(
            "poster",
            "/MediaCover/7/poster-250.jpg?lastWrite=123",
            "https://remote.example/full-resolution.jpg");

        Assert.AreEqual(
            "/MediaCover/7/poster-250.jpg?lastWrite=123",
            SonarrArtworkCache.GetImageIdentity(image));
    }

    [TestMethod]
    public void AmbiguousTitleDoesNotGuess()
    {
        var anime = new[]
        {
            new SonarrLocalAnime(Guid.NewGuid(), "Example", "example")
        };
        var series = new[]
        {
            new SonarrSeriesItem(1, "Example", "/data/anime/A", []),
            new SonarrSeriesItem(2, "Example", "/data/anime/B", [])
        };

        var matches = SonarrSeriesMatcher.Match(anime, series, []);

        Assert.AreEqual(0, matches.Count);
    }

    [TestMethod]
    public void MatchingNormalizesPunctuationAndUnicodeWidth()
    {
        var anime = new[]
        {
            new SonarrLocalAnime(Guid.NewGuid(), "SPY×FAMILY", "spy×family")
        };
        var series = new[]
        {
            new SonarrSeriesItem(3, "SPY x FAMILY", "/data/anime/Unrelated", [])
        };

        var matches = SonarrSeriesMatcher.Match(anime, series, []);

        Assert.AreEqual(3, matches[anime[0].Id].Id);
    }
}
