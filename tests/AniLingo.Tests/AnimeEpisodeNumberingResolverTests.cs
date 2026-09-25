using AniLingo.Web.Features.MediaMapping;
using AniLingo.Web.Features.Metadata;

namespace AniLingo.Tests;

[TestClass]
public sealed class AnimeEpisodeNumberingResolverTests
{
    [TestMethod]
    public void ResolvesSeasonEpisodeToAbsoluteAndAniListPartEpisode()
    {
        var animeId = Guid.NewGuid();
        var episodes = Enumerable.Range(1, 24)
            .Select(number => new LocalEpisodeCoordinate(1, number))
            .Concat(Enumerable.Range(1, 12).Select(number => new LocalEpisodeCoordinate(2, number)))
            .ToArray();

        var mappings = new[]
        {
            Mapping(animeId, season: 1, localStart: 1, localEnd: 12, remoteStart: 1, externalId: "100"),
            Mapping(animeId, season: 1, localStart: 13, localEnd: 24, remoteStart: 1, externalId: "101"),
            Mapping(animeId, season: 2, localStart: 1, localEnd: 12, remoteStart: 1, externalId: "102")
        };

        var resolved = AnimeEpisodeNumberingResolver.Resolve(
            1,
            13,
            episodes,
            mappings);

        Assert.AreEqual(13, resolved.AbsoluteEpisodeNumber);
        Assert.IsNotNull(resolved.Remote);
        Assert.AreEqual("101", resolved.Remote.ExternalId);
        Assert.AreEqual(1, resolved.Remote.RemoteEpisodeNumber);
    }

    [TestMethod]
    public void AbsoluteNumberMapsBackAcrossLocalSeasonBoundary()
    {
        var episodes = Enumerable.Range(1, 12)
            .Select(number => new LocalEpisodeCoordinate(1, number))
            .Concat(Enumerable.Range(1, 12).Select(number => new LocalEpisodeCoordinate(2, number)))
            .ToArray();

        var local = AnimeEpisodeNumberingResolver.ResolveLocalEpisode(13, episodes);

        Assert.IsNotNull(local);
        Assert.AreEqual(2, local.SeasonNumber);
        Assert.AreEqual(1, local.EpisodeNumber);
    }

    [TestMethod]
    public void SpecialsDoNotConsumeMainSeriesAbsoluteNumbering()
    {
        var episodes = new[]
        {
            new LocalEpisodeCoordinate(0, 1),
            new LocalEpisodeCoordinate(1, 1),
            new LocalEpisodeCoordinate(1, 2)
        };

        Assert.IsNull(
            AnimeEpisodeNumberingResolver.ResolveAbsoluteEpisode(0, 1, episodes));
        Assert.AreEqual(
            1,
            AnimeEpisodeNumberingResolver.ResolveAbsoluteEpisode(1, 1, episodes));
        Assert.AreEqual(
            2,
            AnimeEpisodeNumberingResolver.ResolveAbsoluteEpisode(1, 2, episodes));
    }

    [TestMethod]
    public void ResolvesSeasonZeroThroughExplicitSpecialMapping()
    {
        var animeId = Guid.NewGuid();
        var resolved = AnimeEpisodeNumberingResolver.Resolve(
            0,
            2,
            [
                new LocalEpisodeCoordinate(0, 1),
                new LocalEpisodeCoordinate(0, 2)
            ],
            [
                Mapping(animeId, 0, 1, 2, 1, "special")
            ]);

        Assert.IsNull(resolved.AbsoluteEpisodeNumber);
        Assert.IsNotNull(resolved.Remote);
        Assert.AreEqual("special", resolved.Remote.ExternalId);
        Assert.AreEqual(2, resolved.Remote.RemoteEpisodeNumber);
    }

    [TestMethod]
    public void OverlappingMappingsFailClosed()
    {
        var animeId = Guid.NewGuid();

        Assert.ThrowsException<InvalidOperationException>(() =>
            AnimeEpisodeNumberingResolver.Resolve(
                1,
                3,
                [new LocalEpisodeCoordinate(1, 3)],
                [
                    Mapping(animeId, 1, 1, 6, 1, "100"),
                    Mapping(animeId, 1, 3, 8, 1, "101")
                ]));
    }

    [TestMethod]
    public void UnknownAbsoluteEpisodeDoesNotGuess()
    {
        var local = AnimeEpisodeNumberingResolver.ResolveLocalEpisode(
            50,
            [new LocalEpisodeCoordinate(1, 1)]);

        Assert.IsNull(local);
    }

    private static AnimeEpisodeMetadataMapping Mapping(
        Guid animeId,
        int season,
        int localStart,
        int localEnd,
        int remoteStart,
        string externalId) =>
        new(
            Guid.NewGuid(),
            animeId,
            season,
            localStart,
            localEnd,
            remoteStart,
            "AniList",
            externalId,
            $"Part {externalId}",
            localEnd - localStart + 1,
            DateTimeOffset.UtcNow);
}
