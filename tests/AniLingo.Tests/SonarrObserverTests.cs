using System.Net;
using System.Text;
using System.Text.Json;
using AniLingo.Web.Features.Acquisition.Ownership;
using AniLingo.Web.Features.Sonarr;

namespace AniLingo.Tests;

[TestClass]
public sealed class SonarrObserverTests
{
    internal const string SeriesJson = """
    [
      { "id": 1, "title": "Frieren", "path": "/tv/anime/Frieren", "monitored": true, "seriesType": "anime", "tvdbId": 424536 },
      { "id": 2, "title": "Dungeon Meshi", "path": "/tv/anime/Dungeon Meshi", "monitored": true, "seriesType": "anime" },
      { "id": 0, "title": "Broken" },
      { "id": 3, "path": "/tv/anime/No Title" }
    ]
    """;

    internal const string EpisodeFilesJson = """
    [
      {
        "id": 11,
        "seriesId": 2,
        "seasonNumber": 1,
        "relativePath": "Season 01/Dungeon Meshi - S01E01.mkv",
        "path": "/tv/anime/Dungeon Meshi/Season 01/Dungeon Meshi - S01E01.mkv",
        "size": 1400000000,
        "quality": { "quality": { "id": 3, "name": "WEBDL-1080p" } }
      },
      { "id": 12, "seriesId": 2, "seasonNumber": 1 }
    ]
    """;

    internal const string QueueJson = """
    {
      "page": 1,
      "pageSize": 200,
      "sortKey": "timeleft",
      "sortDirection": "ascending",
      "totalRecords": 2,
      "records": [
        {
          "id": 101,
          "seriesId": 2,
          "episodeId": 505,
          "episode": { "seasonNumber": 1, "episodeNumber": 5, "absoluteEpisodeNumber": 5 },
          "title": "Dungeon Meshi - S01E05 WEB-DL 1080p AVC AAC[JA]",
          "status": "downloading",
          "trackedDownloadStatus": "ok",
          "trackedDownloadState": "downloading",
          "downloadId": "SABnzbd_nzo_sonarr05",
          "protocol": "usenet",
          "downloadClient": "SABnzbd",
          "outputPath": "/downloads/complete/tv/Dungeon.Meshi.S01E05"
        },
        {
          "id": 102,
          "title": "Unknown.Show.S01E01.1080p.WEB-DL",
          "status": "completed",
          "trackedDownloadState": "importPending",
          "downloadId": "SABnzbd_nzo_unknown"
        },
        { "id": 103, "seriesId": 2 }
      ]
    }
    """;

    internal const string HistoryJson = """
    {
      "page": 1,
      "pageSize": 250,
      "sortKey": "date",
      "sortDirection": "descending",
      "totalRecords": 4,
      "records": [
        {
          "id": 9001,
          "episodeId": 504,
          "seriesId": 2,
          "sourceTitle": "Dungeon Meshi - S01E04 WEB-DL 1080p AVC AAC[JA]",
          "date": "2026-09-26T08:00:00Z",
          "eventType": "grabbed",
          "downloadId": "SABnzbd_nzo_sonarr04",
          "data": { "indexer": "Nyaa", "downloadClient": "SABnzbd" },
          "episode": { "seasonNumber": 1, "episodeNumber": 4, "absoluteEpisodeNumber": 4 }
        },
        {
          "id": 9002,
          "seriesId": 2,
          "sourceTitle": "Dungeon Meshi - S01E03 WEB-DL 1080p AVC AAC[JA]",
          "date": "2026-09-26T07:00:00Z",
          "eventType": "downloadFolderImported",
          "downloadId": "SABnzbd_nzo_sonarr03",
          "data": {
            "droppedPath": "/downloads/complete/tv/Dungeon.Meshi.S01E03/episode.mkv",
            "importedPath": "/tv/anime/Dungeon Meshi/Season 01/Dungeon Meshi - S01E03.mkv"
          }
        },
        {
          "id": 9003,
          "seriesId": 2,
          "date": "2026-09-26T06:00:00Z",
          "eventType": "episodeFileRenamed",
          "data": {
            "sourcePath": "/tv/anime/Dungeon Meshi/Season 01/old-name.mkv",
            "path": "/tv/anime/Dungeon Meshi/Season 01/Dungeon Meshi - S01E02.mkv"
          }
        },
        { "id": 9004, "seriesId": 2, "eventType": "seriesFolderImported", "date": "not-a-date" }
      ]
    }
    """;

    [TestMethod]
    public void SeriesParserKeepsValidSeriesWithPathAndMonitoring()
    {
        var series = SonarrObserverClient.ParseSeries(SeriesJson);

        Assert.AreEqual(2, series.Count);
        Assert.AreEqual(new SonarrObservedSeries(1, "Frieren", "/tv/anime/Frieren", true), series[0]);
        Assert.AreEqual("/tv/anime/Dungeon Meshi", series[1].Path);
    }

    [TestMethod]
    public void EpisodeFileParserSkipsFilesWithoutPath()
    {
        var files = SonarrObserverClient.ParseEpisodeFiles(EpisodeFilesJson);

        Assert.AreEqual(1, files.Count);
        Assert.AreEqual(2, files[0].SeriesId);
        Assert.AreEqual(1, files[0].SeasonNumber);
        Assert.AreEqual("/tv/anime/Dungeon Meshi/Season 01/Dungeon Meshi - S01E01.mkv", files[0].Path);
    }

    [TestMethod]
    public void QueueParserReadsDownloadIdentityPathsAndEpisode()
    {
        var parsed = SonarrObserverClient.ParseQueuePage(QueueJson);

        Assert.AreEqual(3, parsed.RecordCount);
        Assert.AreEqual(2, parsed.TotalRecords);
        Assert.AreEqual(2, parsed.Items.Count);

        var item = parsed.Items[0];
        Assert.AreEqual(2, item.SeriesId);
        Assert.AreEqual("SABnzbd_nzo_sonarr05", item.DownloadId);
        Assert.AreEqual("/downloads/complete/tv/Dungeon.Meshi.S01E05", item.OutputPath);
        Assert.AreEqual("downloading", item.TrackedDownloadState);
        Assert.AreEqual(new SonarrObservedEpisode(1, 5, 5), item.Episode);
        Assert.IsFalse(string.IsNullOrWhiteSpace(item.ReleaseKey));

        Assert.IsNull(parsed.Items[1].SeriesId);
        Assert.IsNull(parsed.Items[1].Episode);
    }

    [TestMethod]
    public void HistoryParserClassifiesEventsAndReadsPaths()
    {
        var history = SonarrObserverClient.ParseHistoryPage(HistoryJson);

        Assert.AreEqual(4, history.Count);
        Assert.AreEqual(SonarrHistoryEventKind.Grabbed, history[0].Kind);
        Assert.AreEqual("SABnzbd_nzo_sonarr04", history[0].DownloadId);
        Assert.AreEqual(new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero), history[0].AtUtc);
        Assert.AreEqual(new SonarrObservedEpisode(1, 4, 4), history[0].Episode);

        Assert.AreEqual(SonarrHistoryEventKind.Imported, history[1].Kind);
        Assert.AreEqual("/downloads/complete/tv/Dungeon.Meshi.S01E03/episode.mkv", history[1].SourcePath);
        Assert.AreEqual("/tv/anime/Dungeon Meshi/Season 01/Dungeon Meshi - S01E03.mkv", history[1].Path);

        Assert.AreEqual(SonarrHistoryEventKind.Renamed, history[2].Kind);
        Assert.AreEqual("/tv/anime/Dungeon Meshi/Season 01/old-name.mkv", history[2].SourcePath);
        Assert.IsNull(history[2].ReleaseKey);

        Assert.AreEqual(SonarrHistoryEventKind.Imported, history[3].Kind);
        Assert.IsNull(history[3].AtUtc);
    }

    [TestMethod]
    public void MalformedResponsesAreRejected()
    {
        Assert.ThrowsExactly<SonarrObserverException>(() => SonarrObserverClient.ParseSeries("{}"));
        Assert.ThrowsExactly<SonarrObserverException>(() => SonarrObserverClient.ParseQueuePage("[]"));
        Assert.ThrowsExactly<SonarrObserverException>(() => SonarrObserverClient.ParseHistoryPage("{\"records\":1}"));
    }

    [TestMethod]
    public void BaseUrlValidationRejectsCredentialsQueryAndOtherSchemes()
    {
        Assert.IsTrue(SonarrObserverClient.TryCreateBaseUri("http://sonarr:8989/", out var uri, out _));
        Assert.AreEqual("http://sonarr:8989/", uri.ToString());
        Assert.IsTrue(SonarrObserverClient.TryCreateBaseUri("https://host/sonarr", out uri, out _));
        Assert.AreEqual("https://host/sonarr/", uri.ToString());

        Assert.IsFalse(SonarrObserverClient.TryCreateBaseUri("http://user:pass@sonarr:8989", out _, out _));
        Assert.IsFalse(SonarrObserverClient.TryCreateBaseUri("http://sonarr:8989?apikey=x", out _, out _));
        Assert.IsFalse(SonarrObserverClient.TryCreateBaseUri("ftp://sonarr", out _, out _));
        Assert.IsFalse(SonarrObserverClient.TryCreateBaseUri("", out _, out _));
    }

    [TestMethod]
    public async Task ObservationIsReadOnlyAndPaginatesQueue()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            var query = request.RequestUri.Query;
            var body = path switch
            {
                "/api/v3/series" => SeriesJson,
                "/api/v3/episodefile" => EpisodeFilesJson,
                "/api/v3/history" => HistoryJson,
                "/api/v3/queue" when query.Contains("page=1&") => QueuePage(1, 201),
                "/api/v3/queue" => QueuePage(2, 201),
                _ => throw new AssertFailedException($"Unexpected Sonarr request {path}.")
            };
            return Json(body);
        });

        var client = new SonarrObserverClient(new StubFactory(handler));
        var settings = new SonarrConnectionSettings("http://sonarr:8989/", " secret ");

        var series = await client.GetSeriesAsync(settings, CancellationToken.None);
        var files = await client.GetEpisodeFilesAsync(settings, 2, CancellationToken.None);
        var queue = await client.GetQueueAsync(settings, CancellationToken.None);
        var history = await client.GetRecentHistoryAsync(settings, CancellationToken.None);

        Assert.AreEqual(2, series.Count);
        Assert.AreEqual(1, files.Count);
        Assert.AreEqual(2, queue.Count);
        Assert.AreEqual(4, history.Count);
        Assert.AreEqual(5, requests.Count);
        Assert.IsTrue(requests.All(request => request.Method == HttpMethod.Get), "Observation must only use GET.");
        Assert.IsTrue(requests.All(request =>
            request.Headers.TryGetValues("X-Api-Key", out var values) && values.Single() == "secret"));
        Assert.IsTrue(requests.Any(request => request.RequestUri!.Query.Contains("seriesId=2")));
        Assert.IsTrue(requests.Any(request => request.RequestUri!.Query.Contains("includeEpisode=true")));
    }

    [TestMethod]
    public async Task ObservationReportsHttpFailuresAsObserverException()
    {
        var client = new SonarrObserverClient(new StubFactory(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized))));

        var exception = await Assert.ThrowsExactlyAsync<SonarrObserverException>(() =>
            client.GetSeriesAsync(new SonarrConnectionSettings("http://sonarr:8989", "bad"), CancellationToken.None));

        StringAssert.Contains(exception.Message, "401");
    }

    [TestMethod]
    public async Task MonitoringClientOnlyUnmonitorsThroughSeriesEditor()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });

        await new SonarrSeriesMonitoringClient(new StubFactory(handler)).SetMonitoredAsync(
            new SonarrConnectionSettings("http://sonarr:8989", "secret"),
            7,
            monitored: false,
            CancellationToken.None);

        Assert.IsNotNull(captured);
        Assert.AreEqual(HttpMethod.Put, captured.Method);
        Assert.AreEqual("/api/v3/series/editor", captured.RequestUri!.AbsolutePath);
        using var document = JsonDocument.Parse(body!);
        Assert.AreEqual(7, document.RootElement.GetProperty("seriesIds")[0].GetInt32());
        Assert.IsFalse(document.RootElement.GetProperty("monitored").GetBoolean());
        Assert.AreEqual(2, document.RootElement.EnumerateObject().Count(), "Only series ids and monitoring may be sent.");
    }

    [TestMethod]
    public void ObservedStateCollectsActiveReleasesDownloadsAndPaths()
    {
        var queue = SonarrObserverClient.ParseQueuePage(QueueJson).Items;
        var state = SonarrObservedState.FromObservation(
            SonarrObserverClient.ParseSeries(SeriesJson),
            [],
            queue,
            [],
            DateTimeOffset.UtcNow);

        Assert.AreEqual(SonarrObservationStatus.Observed, state.Status);
        Assert.IsTrue(state.ActiveDownloadIds.Contains("sabnzbd_nzo_sonarr05"));
        Assert.IsTrue(state.ActivePaths.Contains("/downloads/complete/tv/Dungeon.Meshi.S01E05"));
        Assert.AreEqual(2, state.ActiveReleaseKeys.Count);
    }

    private static string QueuePage(int page, int total) =>
        $$"""
        {
          "page": {{page}},
          "pageSize": 200,
          "totalRecords": {{total}},
          "records": [
            { "id": {{page}}, "seriesId": 2, "title": "Dungeon Meshi - S01E0{{page}} WEB-DL 1080p", "downloadId": "nzo_{{page}}" }
          ]
        }
        """;

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
