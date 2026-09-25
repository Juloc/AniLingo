using System.Net;
using System.Text;
using AniLingo.Web.Features.Acquisition.Ownership.Sonarr;

namespace AniLingo.Tests;

[TestClass]
public sealed class SonarrObserverTests
{
    [TestMethod]
    public void QueueParserBuildsReleaseKeysAndPaths()
    {
        const string json = """
        {
          "totalRecords": 2,
          "records": [
            {
              "id": 1,
              "seriesId": 10,
              "episodeId": 20,
              "title": "[Group] Anime Name - 01 WEB-DL 1080p HEVC AAC[JA]",
              "downloadId": "abc",
              "status": "downloading",
              "trackedDownloadStatus": "ok",
              "outputPath": "/downloads/anime-01"
            },
            {
              "id": 2,
              "title": "Anime Name - S01E02 WEB-DL 1080p AVC AAC[JA]",
              "downloadId": "def"
            }
          ]
        }
        """;

        var parsed = SonarrObserverClient.ParseQueuePage(json);

        Assert.AreEqual(2, parsed.Items.Count);
        Assert.AreEqual(2, parsed.TotalRecords);
        Assert.IsFalse(string.IsNullOrWhiteSpace(parsed.Items[0].ReleaseKey));
        Assert.AreEqual("/downloads/anime-01", parsed.Items[0].OutputPath);
        Assert.AreEqual(10, parsed.Items[0].SeriesId);
        Assert.AreEqual(20, parsed.Items[0].EpisodeId);
    }

    [TestMethod]
    public void QueueParserSkipsMalformedEntriesWithoutTitle()
    {
        const string json = """
        {
          "totalRecords": 2,
          "records": [
            { "id": 1 },
            { "id": 2, "title": "Anime - S01E01 WEB-DL 1080p" }
          ]
        }
        """;

        var parsed = SonarrObserverClient.ParseQueuePage(json);

        Assert.AreEqual(1, parsed.Items.Count);
    }

    [TestMethod]
    public async Task QueueObservationUsesApiKeyHeaderAndReturnsConflictSnapshot()
    {
        var handler = new StubHandler(request =>
        {
            Assert.IsTrue(request.Headers.TryGetValues("X-Api-Key", out var values));
            Assert.AreEqual("secret", values.Single());

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "totalRecords": 1,
                      "records": [
                        {
                          "id": 1,
                          "title": "Anime - S01E01 WEB-DL 1080p",
                          "outputPath": "/downloads/anime"
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var client = new SonarrObserverClient(new HttpClient(handler));
        var snapshot = await client.GetQueueAsync(
            new SonarrConnection(new SonarrSettings("http://sonarr:8989"), "secret"),
            CancellationToken.None);

        Assert.AreEqual(1, snapshot.Items.Count);
        Assert.AreEqual(1, snapshot.ObservedState.ActiveReleaseKeys.Count);
        Assert.IsTrue(snapshot.ObservedState.ActivePaths.Contains("/downloads/anime"));
    }

    [TestMethod]
    public void SettingsRejectCredentialsInUrl()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            SonarrSettingsStore.NormalizeAndValidate(
                new SonarrSettings("http://user:pass@sonarr:8989")));
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
