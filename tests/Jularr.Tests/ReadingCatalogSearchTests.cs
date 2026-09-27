using System.Net;
using System.Text;
using Jularr.Web.Features.ReadingDiscovery;

namespace Jularr.Tests;

[TestClass]
public sealed class ReadingCatalogSearchTests
{
    [TestMethod]
    public void SyosetuResponseMapsPublicWebNovelIdentity()
    {
        const string json = """
        [
          { "allcount": 1 },
          {
            "title": "無職転生",
            "ncode": "N9669BK",
            "writer": "理不尽な孫の手",
            "general_firstup": "2012-11-22 17:00:00",
            "end": 0,
            "general_all_no": 286
          }
        ]
        """;

        var items = SyosetuCatalogClient.ParseResponse(json);

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("syosetu", items[0].Provider);
        Assert.AreEqual("n9669bk", items[0].ExternalId);
        Assert.AreEqual("無職転生", items[0].Title);
        Assert.AreEqual("理不尽な孫の手", items[0].Author);
        Assert.AreEqual(2012, items[0].FirstPublishYear);
        Assert.AreEqual(286, items[0].ChapterCount);
        Assert.AreEqual("FINISHED", items[0].Status);
        Assert.AreEqual(
            "https://ncode.syosetu.com/n9669bk/",
            items[0].SourceUrl);
        Assert.IsTrue(items[0].IsPublicWebSource);
    }

    [TestMethod]
    public void SyosetuSearchUsesOfficialJsonApiAndTitleAuthorScope()
    {
        var uri = SyosetuCatalogClient.BuildSearchUri(
            "Mushoku Tensei",
            18);

        Assert.AreEqual(
            "api.syosetu.com",
            uri.Host);
        Assert.AreEqual(
            "/novelapi/api/",
            uri.AbsolutePath);
        Assert.IsTrue(
            uri.Query.Contains("out=json", StringComparison.Ordinal));
        Assert.IsTrue(
            uri.Query.Contains("title=1", StringComparison.Ordinal));
        Assert.IsTrue(
            uri.Query.Contains("wname=1", StringComparison.Ordinal));
        Assert.IsTrue(
            uri.Query.Contains("word=Mushoku%20Tensei", StringComparison.Ordinal));
    }

    [TestMethod]
    public void NcodeValidationRejectsArbitraryExternalUrls()
    {
        Assert.IsTrue(
            SyosetuCatalogClient.IsValidNcode("N9669BK"));
        Assert.IsTrue(
            SyosetuCatalogClient.IsValidNcode("n1234a"));

        Assert.IsFalse(
            SyosetuCatalogClient.IsValidNcode("https://example.com"));
        Assert.IsFalse(
            SyosetuCatalogClient.IsValidNcode("../n9669bk"));
        Assert.IsFalse(
            SyosetuCatalogClient.IsValidNcode("N96BK"));
    }

    [TestMethod]
    public void ExactCanonicalTitleRanksAboveLooseAuthorMatch()
    {
        var exact = new ReadingCatalogCandidate(
            "anilist",
            "1",
            "Mushoku Tensei",
            null,
            null,
            null,
            2014,
            "FINISHED",
            26,
            null,
            null,
            false);
        var authorOnly = new ReadingCatalogCandidate(
            "syosetu",
            "n1234ab",
            "Another Story",
            null,
            "Mushoku Tensei",
            null,
            2013,
            "FINISHED",
            null,
            10,
            "https://ncode.syosetu.com/n1234ab/",
            true);

        Assert.IsGreaterThan(
            ReadingCatalogSearch.MatchScore(
                "Mushoku Tensei",
                exact),
            ReadingCatalogSearch.MatchScore(
                "Mushoku Tensei",
                authorOnly));
    }

    [TestMethod]
    public void PublishedAndWebNovelRemainDistinctSources()
    {
        var published = new ReadingCatalogCandidate(
            "anilist",
            "123",
            "Example",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false);
        var web = new ReadingCatalogCandidate(
            "syosetu",
            "n1234ab",
            "Example",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "https://ncode.syosetu.com/n1234ab/",
            true);

        Assert.AreNotEqual(
            published.Identity,
            web.Identity);
    }

    [TestMethod]
    public async Task SyosetuClientSendsRequiredUserAgent()
    {
        var handler = new RecordingHandler();
        using var http = new HttpClient(handler);
        var client = new SyosetuCatalogClient(http);

        var results = await client.SearchAsync(
            "test",
            3,
            CancellationToken.None);

        Assert.AreEqual(0, results.Count);
        Assert.IsTrue(
            handler.UserAgent?.Contains(
                "Jularr",
                StringComparison.Ordinal) == true);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? UserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """[{"allcount":0}]""",
                        Encoding.UTF8,
                        "application/json")
                });
        }
    }
}
