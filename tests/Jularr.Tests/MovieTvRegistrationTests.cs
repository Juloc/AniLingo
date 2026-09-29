using Jularr.Web.Features.Acquisition.Access;
using Jularr.Web.Features.Acquisition.Release;
using Jularr.Web.Features.MediaCore;

namespace Jularr.Tests;

/// <summary>
/// Movie (#593) and TV/Series (#594) as first-class acquisition kinds: their name mappings resolve, and
/// each registers a parser + default quality profile on the shared engine so the registry never throws.
/// </summary>
[TestClass]
public sealed class MovieTvRegistrationTests
{
    private static MediaAcquisitionRegistry Registry() =>
        new([
            new AnimeAcquisitionRegistration(),
            new MovieAcquisitionRegistration(),
            new TvAcquisitionRegistration()
        ]);

    [TestMethod]
    public void KindNamesRoundTripForMovieAndTv()
    {
        Assert.AreEqual("movie", AcquisitionAccessNames.Kind(MediaAcquisitionKind.Movie));
        Assert.AreEqual("tv", AcquisitionAccessNames.Kind(MediaAcquisitionKind.Tv));
        Assert.AreEqual(MediaAcquisitionKind.Movie, AcquisitionAccessNames.ParseKind("movie"));
        Assert.AreEqual(MediaAcquisitionKind.Tv, AcquisitionAccessNames.ParseKind("tv"));
    }

    [TestMethod]
    public void WorkTypeMapsMovieToMovieAndTvToSeries()
    {
        Assert.AreEqual(WorkMediaType.Movie, AcquisitionAccessNames.WorkType(MediaAcquisitionKind.Movie));
        Assert.AreEqual(WorkMediaType.Series, AcquisitionAccessNames.WorkType(MediaAcquisitionKind.Tv));
    }

    [TestMethod]
    public void EveryKindHasAWorkTypeAndAName()
    {
        // Guards against a new MediaAcquisitionKind slipping in without the central mappings that
        // GetCapabilitiesAsync and the acquisition stores rely on.
        foreach (var kind in Enum.GetValues<MediaAcquisitionKind>())
        {
            var name = AcquisitionAccessNames.Kind(kind);
            Assert.AreEqual(kind, AcquisitionAccessNames.ParseKind(name));
            _ = AcquisitionAccessNames.WorkType(kind);
        }
    }

    [TestMethod]
    public void RegistryResolvesParserAndDefaultProfileForMovieAndTv()
    {
        var registry = Registry();

        Assert.IsTrue(registry.Supports(MediaAcquisitionKind.Movie));
        Assert.IsTrue(registry.Supports(MediaAcquisitionKind.Tv));
        Assert.IsNotNull(registry.ParserFor(MediaAcquisitionKind.Movie));
        Assert.IsNotNull(registry.ParserFor(MediaAcquisitionKind.Tv));

        Assert.AreEqual(VideoQualityProfiles.DefaultMovie1080pId, registry.DefaultProfileFor(MediaAcquisitionKind.Movie).Id);
        Assert.AreEqual(VideoQualityProfiles.DefaultTv1080pId, registry.DefaultProfileFor(MediaAcquisitionKind.Tv).Id);
    }

    [TestMethod]
    public void MovieAndTvParsersReadSeasonEpisodeAndTitle()
    {
        var registry = Registry();

        Assert.IsTrue(registry.ParserFor(MediaAcquisitionKind.Tv)
            .TryParse("Breaking.Bad.S01E02.1080p.BluRay.x264-GROUP", out var episode));
        Assert.AreEqual(1, episode.SeasonNumber);
        Assert.AreEqual(2, episode.EpisodeStart);

        Assert.IsTrue(registry.ParserFor(MediaAcquisitionKind.Movie)
            .TryParse("Inception.2010.1080p.BluRay.x264-GROUP", out var movie));
        Assert.IsFalse(string.IsNullOrWhiteSpace(movie.SeriesTitle));
    }
}
