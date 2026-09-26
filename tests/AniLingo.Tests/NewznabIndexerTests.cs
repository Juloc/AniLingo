using System.Net;
using System.Text;
using AniLingo.Web.Features.Acquisition.Indexers;

namespace AniLingo.Tests;

[TestClass]
public sealed class NewznabIndexerTests
{
    [TestMethod]
    public async Task TestAsyncParsesCapsAndReportsVersion()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return XmlResponse(
                """
                <?xml version="1.0" encoding="UTF-8"?>
                <caps>
                  <server version="1.1" title="Example Indexer" />
                  <categories />
                </caps>
                """);
        });
        var indexer = new NewznabIndexer(new HttpClient(handler));

        var result = await indexer.TestAsync(Entry(IndexerType.Newznab), CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("1.1", result.Version);
        Assert.IsNotNull(captured);
        StringAssert.Contains(captured!.RequestUri!.Query, "t=caps");
        StringAssert.Contains(captured.RequestUri!.Query, "apikey=indexer-key");
    }

    [TestMethod]
    public async Task TestAsyncRejectsNonCapsResponse()
    {
        var handler = new StubHandler(_ => XmlResponse("<error code=\"100\">Invalid API key</error>"));
        var indexer = new NewznabIndexer(new HttpClient(handler));

        var result = await indexer.TestAsync(Entry(IndexerType.Newznab), CancellationToken.None);

        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public void ParsesNewznabUsenetSearchResultsWithEnclosureDownloadLink()
    {
        const string xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:newznab="http://www.newznab.com/DTD/2010/feeds/attributes/">
          <channel>
            <item>
              <title>[Group] Anime - 01 WEB-DL 1080p AAC</title>
              <guid isPermaLink="false">abc123</guid>
              <link>https://indexer.example/details/abc123</link>
              <pubDate>Fri, 26 Sep 2025 12:00:00 +0000</pubDate>
              <enclosure url="https://indexer.example/getnzb/abc123.nzb" length="1234567890" type="application/x-nzb" />
              <newznab:attr name="size" value="1234567890" />
            </item>
          </channel>
        </rss>
        """;

        var releases = NewznabIndexer.ParseSearchResponse(Entry(IndexerType.Newznab), "Anime 01", xml);

        var release = releases.Single();
        Assert.AreEqual("usenet", release.Protocol);
        Assert.AreEqual(1234567890L, release.SizeBytes);
        Assert.AreEqual(
            "https://indexer.example/getnzb/abc123.nzb",
            release.InternalDownloadUri!.ToString());
        Assert.IsNull(release.InternalMagnetUri);
        Assert.AreEqual("abc123", release.Guid);
    }

    [TestMethod]
    public void ParsesTorznabMagnetResultsWithSeedersAndLeechers()
    {
        const string magnet = "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&dn=test";
        var xml = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:newznab="http://www.newznab.com/DTD/2010/feeds/attributes/">
          <channel>
            <item>
              <title>[Group] Anime - 01 1080p BluRay x264</title>
              <guid>def456</guid>
              <link>{magnet.Replace("&", "&amp;")}</link>
              <newznab:attr name="size" value="900000000" />
              <newznab:attr name="seeders" value="50" />
              <newznab:attr name="peers" value="60" />
            </item>
          </channel>
        </rss>
        """;

        var releases = NewznabIndexer.ParseSearchResponse(Entry(IndexerType.Torznab), "Anime 01", xml);

        var release = releases.Single();
        Assert.AreEqual("torrent", release.Protocol);
        Assert.AreEqual(magnet, release.InternalMagnetUri);
        Assert.IsNull(release.InternalDownloadUri);
        Assert.AreEqual(50, release.Seeders);
        Assert.AreEqual(10, release.Leechers);
    }

    [TestMethod]
    public void MalformedItemsDoNotBreakOtherResults()
    {
        const string xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
          <channel>
            <item><guid>no-title</guid></item>
            <item><title>Anime - 01 WEB-DL 1080p AAC</title><guid>good</guid></item>
          </channel>
        </rss>
        """;

        var releases = NewznabIndexer.ParseSearchResponse(Entry(IndexerType.Newznab), "Anime 01", xml);

        Assert.AreEqual(1, releases.Count);
        Assert.AreEqual("good", releases[0].Guid);
    }

    private static IndexerEntry Entry(IndexerType type) =>
        new(
            Guid.NewGuid(),
            "Example Indexer",
            type,
            Enabled: true,
            Priority: 1,
            new IndexerSettings("https://indexer.example", [5070], [], 100),
            "indexer-key");

    private static HttpResponseMessage XmlResponse(string xml) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml")
        };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
