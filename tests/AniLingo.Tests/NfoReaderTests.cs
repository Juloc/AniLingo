using System.Text;
using AniLingo.Web.Features.Library;

namespace AniLingo.Tests;

[TestClass]
public sealed class NfoReaderTests
{
    [TestMethod]
    public void KodiSonarrShowNfoReadsSafeSubsetAndIgnoresHybridUrlLine()
    {
        var result = ParseShow(
            """
            <?xml version="1.0" encoding="utf-8" standalone="yes"?>
            <tvshow>
              <title>Frieren: Beyond Journey's   End</title>
              <originaltitle>葬送のフリーレン</originaltitle>
              <plot>An elf mage outlives her party.</plot>
              <year>2023</year>
              <premiered>2023-09-29</premiered>
              <uniqueid type="tvdb" default="true">424536</uniqueid>
              <uniqueid type="imdb">tt22248376</uniqueid>
              <uniqueid type="tmdb">209867</uniqueid>
              <genre>Anime</genre>
              <actor><name>Atsumi Tanezaki</name></actor>
            </tvshow>
            https://thetvdb.com/series/frieren-beyond-journeys-end
            """);

        Assert.IsNull(result.Warning);
        var show = result.Value!;
        Assert.AreEqual("Frieren: Beyond Journey's End", show.Title);
        Assert.AreEqual("葬送のフリーレン", show.OriginalTitle);
        Assert.AreEqual("An elf mage outlives her party.", show.Plot);
        Assert.AreEqual(2023, show.Year);
        Assert.AreEqual(new DateOnly(2023, 9, 29), show.Premiered);
        Assert.AreEqual(new NfoProviderIds(null, null, "424536", "209867", "tt22248376"), show.ProviderIds);
    }

    [TestMethod]
    public void JellyfinShowNfoReadsProviderTypesCaseInsensitivelyAndOutlineAsPlot()
    {
        var result = ParseShow(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <tvshow>
              <outline>Short overview.</outline>
              <lockdata>false</lockdata>
              <title>Frieren</title>
              <uniqueid type="AniList">154587</uniqueid>
              <uniqueid type="MyAnimeList">52991</uniqueid>
              <uniqueid type="Tvdb">424536</uniqueid>
              <anilistid>999</anilistid>
              <tvdbid>424536</tvdbid>
            </tvshow>
            """);

        var show = result.Value!;
        Assert.AreEqual("Short overview.", show.Plot);
        Assert.AreEqual("154587", show.ProviderIds.AniList);
        Assert.AreEqual("52991", show.ProviderIds.MyAnimeList);
        Assert.AreEqual("424536", show.ProviderIds.Tvdb);
    }

    [TestMethod]
    public void LegacyProviderTagsAreReadWhenNoUniqueIdExists()
    {
        var result = ParseShow(
            """
            <tvshow>
              <id>12345</id>
              <anilistid>154587</anilistid>
              <malid>52991</malid>
              <tvdbid>424536</tvdbid>
              <tmdbid>209867</tmdbid>
              <imdb_id>tt22248376</imdb_id>
            </tvshow>
            """);

        Assert.AreEqual(
            new NfoProviderIds("154587", "52991", "424536", "209867", "tt22248376"),
            result.Value!.ProviderIds);
    }

    [TestMethod]
    public void DefaultUniqueIdWinsAndMalformedProviderValuesAreIgnored()
    {
        var result = ParseShow(
            """
            <tvshow>
              <uniqueid type="anilist">not-a-number</uniqueid>
              <uniqueid type="anilist">111</uniqueid>
              <uniqueid type="anilist" default="true">222</uniqueid>
              <uniqueid type="tvdb">0</uniqueid>
              <uniqueid type="imdb">22248376</uniqueid>
              <uniqueid type="anidb">17617</uniqueid>
              <uniqueid>333</uniqueid>
              <tmdbid>99999999999</tmdbid>
            </tvshow>
            """);

        Assert.AreEqual(new NfoProviderIds("222", null, null, null, null), result.Value!.ProviderIds);
    }

    [TestMethod]
    public void OutOfRangeAndMalformedScalarsAreTreatedAsAbsent()
    {
        var result = ParseShow(
            $"""
            <tvshow>
              <title>{new string('x', NfoReader.MaxTitleLength + 1)}</title>
              <originaltitle>   </originaltitle>
              <year>23</year>
              <premiered>29.09.2023</premiered>
            </tvshow>
            """);

        var show = result.Value!;
        Assert.IsNull(show.Title);
        Assert.IsNull(show.OriginalTitle);
        Assert.IsNull(show.Year);
        Assert.IsNull(show.Premiered);
    }

    [TestMethod]
    public void SonarrEpisodeNfoReadsNumbersTitleAndIds()
    {
        var result = ParseEpisodes(
            """
            <?xml version="1.0" encoding="utf-8" standalone="yes"?>
            <episodedetails>
              <title>The Journey's End</title>
              <season>1</season>
              <episode>1</episode>
              <aired>2023-09-29</aired>
              <plot>The party returns.</plot>
              <uniqueid type="tvdb" default="true">9958325</uniqueid>
              <watched>false</watched>
            </episodedetails>
            """);

        Assert.IsNull(result.Warning);
        var episode = result.Value!.Single();
        Assert.AreEqual("The Journey's End", episode.Title);
        Assert.AreEqual(1, episode.SeasonNumber);
        Assert.AreEqual(1, episode.EpisodeNumber);
        Assert.AreEqual(new DateOnly(2023, 9, 29), episode.Aired);
        Assert.AreEqual("The party returns.", episode.Plot);
        Assert.AreEqual("9958325", episode.ProviderIds.Tvdb);
        Assert.IsTrue(episode.Describes(1, 1));
        Assert.IsFalse(episode.Describes(1, 2));
    }

    [TestMethod]
    public void KodiMultiEpisodeNfoReturnsEveryEpisodeInDocumentOrder()
    {
        var result = ParseEpisodes(
            """
            <episodedetails><title>Part A</title><season>1</season><episode>1</episode></episodedetails>
            <episodedetails><title>Part B</title><season>1</season><episode>2</episode></episodedetails>
            """);

        var episodes = result.Value!;
        Assert.AreEqual(2, episodes.Count);
        Assert.AreEqual("Part B", episodes.First(x => x.Describes(1, 2)).Title);
    }

    [TestMethod]
    public void EpisodeWithoutNumbersDescribesAnyFileItSitsBeside()
    {
        var episode = ParseEpisodes("<episodedetails><title>Only</title></episodedetails>")
            .Value!
            .Single();

        Assert.IsTrue(episode.Describes(2, 7));
    }

    [TestMethod]
    public void MalformedXmlIsRejectedWithWarning()
    {
        var result = ParseShow("<tvshow><title>Broken</tvshow>");

        Assert.IsNull(result.Value);
        StringAssert.Contains(result.Warning, "not well-formed");
    }

    [TestMethod]
    public void TrailingGarbageAfterValidRootIsRejected()
    {
        var result = ParseEpisodes("<episodedetails><title>A</title></episodedetails><broken");

        Assert.IsNull(result.Value);
        Assert.IsNotNull(result.Warning);
    }

    [TestMethod]
    public void DtdAndExternalEntityAreRejectedWithoutResolvingThem()
    {
        var secretPath = Path.Combine(Path.GetTempPath(), $"anilingo-nfo-secret-{Guid.NewGuid():N}.txt");
        File.WriteAllText(secretPath, "TOP-SECRET");
        try
        {
            var result = ParseShow(
                $"""
                <?xml version="1.0"?>
                <!DOCTYPE tvshow [ <!ENTITY xxe SYSTEM "file:///{secretPath.Replace('\\', '/')}"> ]>
                <tvshow><title>&xxe;</title></tvshow>
                """);

            Assert.IsNull(result.Value);
            Assert.IsNotNull(result.Warning);
            Assert.IsFalse(result.Warning.Contains("TOP-SECRET", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(secretPath);
        }
    }

    [TestMethod]
    public void EntityExpansionBombIsRejected()
    {
        var result = ParseShow(
            """
            <!DOCTYPE lolz [
              <!ENTITY lol "lol">
              <!ENTITY lol2 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
              <!ENTITY lol3 "&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;">
            ]>
            <tvshow><title>&lol3;</title></tvshow>
            """);

        Assert.IsNull(result.Value);
        Assert.IsNotNull(result.Warning);
    }

    [TestMethod]
    public void UnsupportedRootElementIsRejected()
    {
        Assert.IsNull(ParseShow("<movie><title>Film</title></movie>").Value);
        Assert.IsNull(ParseEpisodes("<tvshow><title>Show</title></tvshow>").Value);
        Assert.IsNull(ParseEpisodes("https://thetvdb.com/series/frieren").Value);
    }

    [TestMethod]
    public void SeasonNfoReadsTheAniListUniqueId()
    {
        var result = ParseSeason(
            """
            <season>
              <seasonnumber>2</seasonnumber>
              <uniqueid type="anilist">154595</uniqueid>
              <uniqueid type="tvdb">424537</uniqueid>
            </season>
            """);

        Assert.IsNull(result.Warning);
        Assert.AreEqual("154595", result.Value!.ProviderIds.AniList);
        Assert.AreEqual("424537", result.Value.ProviderIds.Tvdb);
    }

    [TestMethod]
    public void SeasonNfoWithoutASeasonRootIsRejected()
    {
        var result = ParseSeason("<tvshow><title>Show</title></tvshow>");

        Assert.IsNull(result.Value);
        Assert.IsNotNull(result.Warning);
    }

    [TestMethod]
    public void MalformedSeasonNfoIsRejected()
    {
        var result = ParseSeason("<season><uniqueid type=\"anilist\">154595</season>");

        Assert.IsNull(result.Value);
        Assert.IsNotNull(result.Warning);
    }

    [TestMethod]
    public void OversizedFileIsRejectedBeforeParsing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"anilingo-nfo-{Guid.NewGuid():N}.nfo");
        try
        {
            var padding = new string(' ', (int)NfoReader.MaxFileBytes);
            File.WriteAllText(path, $"<tvshow><title>Big</title>{padding}</tvshow>");

            var result = NfoReader.ReadShow(path);

            Assert.IsNull(result.Value);
            StringAssert.Contains(result.Warning, "1024 KiB");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void MissingFileIsRejectedWithWarningInsteadOfThrowing()
    {
        var result = NfoReader.ReadEpisodes(
            Path.Combine(Path.GetTempPath(), $"anilingo-missing-{Guid.NewGuid():N}.nfo"));

        Assert.IsNull(result.Value);
        StringAssert.Contains(result.Warning, "could not be read");
    }

    private static NfoReadResult<NfoShowMetadata> ParseShow(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return NfoReader.ParseShow(stream);
    }

    private static NfoReadResult<IReadOnlyList<NfoEpisodeMetadata>> ParseEpisodes(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return NfoReader.ParseEpisodes(stream);
    }

    private static NfoReadResult<NfoSeasonMetadata> ParseSeason(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return NfoReader.ParseSeason(stream);
    }
}
