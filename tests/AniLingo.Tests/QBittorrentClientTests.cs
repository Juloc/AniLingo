using System.Net;
using System.Text;
using AniLingo.Web.Features.Acquisition.DownloadClients;

namespace AniLingo.Tests;

[TestClass]
public sealed class QBittorrentClientTests
{
    private const string Magnet =
        "magnet:?xt=urn:btih:ABCDEF0123ABCDEF0123ABCDEF0123ABCDEF0123&dn=test";

    [TestMethod]
    public async Task TestAsyncLogsInAndReadsVersion()
    {
        var handler = new FakeHandler();
        handler.OnLogin = _ => TextResponse("Ok.", withSetCookie: true);
        handler.OnVersion = _ => TextResponse("v4.6.0");
        var client = new QBittorrentClient(new HttpClient(handler));

        var result = await client.TestAsync(Entry(), CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("v4.6.0", result.Version);
        var versionRequest = handler.Requests.Single(request => request.RequestUri!.AbsolutePath.Contains("app/version"));
        Assert.AreEqual("SID=abc123", versionRequest.Headers.GetValues("Cookie").Single());
    }

    [TestMethod]
    public async Task TestAsyncFailsWithoutCrashingWhenLoginIsRejected()
    {
        var handler = new FakeHandler { OnLogin = _ => TextResponse("Fails.") };
        var client = new QBittorrentClient(new HttpClient(handler));

        var result = await client.TestAsync(Entry(), CancellationToken.None);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "login");
    }

    [TestMethod]
    public async Task SubmitAsyncAddsMagnetWithCategoryAndReturnsComputedHash()
    {
        var handler = new FakeHandler();
        handler.OnLogin = _ => TextResponse("Ok.", withSetCookie: true);
        string? addBody = null;
        handler.OnAdd = async request =>
        {
            addBody = await request.Content!.ReadAsStringAsync();
            return TextResponse("Ok.");
        };
        var client = new QBittorrentClient(new HttpClient(handler));

        var result = await client.SubmitAsync(
            Entry(),
            new DownloadClientSubmitRequest(DownloadProtocol.Torrent, Url: null, Magnet, Name: "Show - 01"),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("abcdef0123abcdef0123abcdef0123abcdef0123", result.ExternalId);
        StringAssert.Contains(addBody, Magnet);
        StringAssert.Contains(addBody, "anime");
        var addRequest = handler.Requests.Single(request => request.RequestUri!.AbsolutePath.Contains("torrents/add"));
        Assert.AreEqual("SID=abc123", addRequest.Headers.GetValues("Cookie").Single());
    }

    [TestMethod]
    public async Task SubmitAsyncReportsRejectionWithoutThrowing()
    {
        var handler = new FakeHandler();
        handler.OnLogin = _ => TextResponse("Ok.", withSetCookie: true);
        handler.OnAdd = _ => Task.FromResult(TextResponse("Fails."));
        var client = new QBittorrentClient(new HttpClient(handler));

        var result = await client.SubmitAsync(
            Entry(),
            new DownloadClientSubmitRequest(DownloadProtocol.Torrent, Url: null, Magnet, Name: "Show - 01"),
            CancellationToken.None);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "rejected");
    }

    [TestMethod]
    public async Task GetStatusAsyncMapsQBittorrentStateAndProgress()
    {
        var handler = new FakeHandler();
        handler.OnLogin = _ => TextResponse("Ok.", withSetCookie: true);
        handler.OnInfo = _ => JsonResponse(
            """
            [
              {
                "hash": "abcdef0123abcdef0123abcdef0123abcdef0123",
                "name": "Show - 01",
                "state": "downloading",
                "progress": 0.5,
                "size": 1000,
                "completed": 500,
                "dlspeed": 100,
                "eta": 50,
                "save_path": "/downloads",
                "content_path": "/downloads/show-01"
              }
            ]
            """);
        var client = new QBittorrentClient(new HttpClient(handler));

        var statuses = await client.GetStatusAsync(
            Entry(),
            ["abcdef0123abcdef0123abcdef0123abcdef0123"],
            CancellationToken.None);

        var status = statuses.Single();
        Assert.AreEqual(DownloadClientJobState.Downloading, status.State);
        Assert.AreEqual(50d, status.Percentage);
        Assert.AreEqual(TimeSpan.FromSeconds(50), status.TimeLeft);
        Assert.AreEqual(1000L, status.SizeBytes);
        Assert.AreEqual(500L, status.SizeLeftBytes);
        Assert.AreEqual(100d, status.BytesPerSecond);
        Assert.AreEqual("/downloads/show-01", status.StoragePath);
    }

    [TestMethod]
    public async Task GetStatusAsyncMapsCompletedAndErrorStates()
    {
        var handler = new FakeHandler();
        handler.OnLogin = _ => TextResponse("Ok.", withSetCookie: true);
        handler.OnInfo = _ => JsonResponse(
            """
            [
              { "hash": "a", "name": "Done", "state": "uploading", "progress": 1, "size": 100, "completed": 100 },
              { "hash": "b", "name": "Broken", "state": "error", "progress": 0, "size": 100, "completed": 0 }
            ]
            """);
        var client = new QBittorrentClient(new HttpClient(handler));

        var statuses = await client.GetStatusAsync(Entry(), ["a", "b"], CancellationToken.None);

        Assert.AreEqual(DownloadClientJobState.Completed, statuses.Single(item => item.ExternalId == "a").State);
        var broken = statuses.Single(item => item.ExternalId == "b");
        Assert.AreEqual(DownloadClientJobState.Failed, broken.State);
        Assert.IsNotNull(broken.FailureMessage);
    }

    [TestMethod]
    public async Task DeleteAsyncSendsHashesAndDeleteFilesFlag()
    {
        var handler = new FakeHandler();
        handler.OnLogin = _ => TextResponse("Ok.", withSetCookie: true);
        string? deleteBody = null;
        handler.OnDelete = async request =>
        {
            deleteBody = await request.Content!.ReadAsStringAsync();
            return TextResponse("");
        };
        var client = new QBittorrentClient(new HttpClient(handler));

        var deleted = await client.DeleteAsync(Entry(), "abcdef0123abcdef0123abcdef0123abcdef0123", deleteFiles: true, CancellationToken.None);

        Assert.IsTrue(deleted);
        StringAssert.Contains(deleteBody, "abcdef0123abcdef0123abcdef0123abcdef0123");
        StringAssert.Contains(deleteBody, "true");
    }

    private static DownloadClientEntry Entry() =>
        new(
            Guid.NewGuid(),
            "qBittorrent",
            DownloadClientType.QBittorrent,
            Enabled: true,
            Priority: 1,
            new DownloadClientSettings("http://qbittorrent.example:8080", "admin", null, "anime", null),
            "secret-password");

    private static HttpResponseMessage TextResponse(string body, bool withSetCookie = false)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain")
        };
        if (withSetCookie)
        {
            response.Headers.Add("Set-Cookie", "SID=abc123; path=/; HttpOnly");
        }

        return response;
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage>? OnLogin { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnVersion { get; set; }
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? OnAdd { get; set; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnInfo { get; set; }
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? OnDelete { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var path = request.RequestUri!.AbsolutePath;

            if (path.Contains("auth/login"))
            {
                return OnLogin!(request);
            }

            if (path.Contains("app/version"))
            {
                return OnVersion!(request);
            }

            if (path.Contains("torrents/add"))
            {
                return await OnAdd!(request);
            }

            if (path.Contains("torrents/info"))
            {
                return OnInfo!(request);
            }

            if (path.Contains("torrents/delete"))
            {
                return await OnDelete!(request);
            }

            throw new InvalidOperationException($"Unexpected request: {path}");
        }
    }
}
