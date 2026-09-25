using System.Net;
using System.Text;
using AniLingo.Web.Features.Acquisition.Sabnzbd;
using Microsoft.AspNetCore.DataProtection;

namespace AniLingo.Tests;

[TestClass]
public sealed class SabnzbdClientTests
{
    [TestMethod]
    public async Task SettingsStoreProtectsApiKeyAtRest()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var provider = new EphemeralDataProtectionProvider();
            var store = new SabnzbdSettingsStore(provider, directory);
            await store.SaveAsync(Connection());

            var raw = await File.ReadAllTextAsync(
                Path.Combine(directory.FullName, "sabnzbd.json"));

            Assert.IsFalse(raw.Contains("secret-key", StringComparison.Ordinal));

            var loaded = await store.LoadAsync();
            Assert.IsNotNull(loaded);
            Assert.AreEqual("secret-key", loaded.ApiKey);
            Assert.AreEqual("anilingo", loaded.Settings.Category);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task TestUsesVersionModeAndApiKey()
    {
        HttpRequestMessage? captured = null;
        var client = new SabnzbdClient(
            new HttpClient(
                new StubHandler(request =>
                {
                    captured = CloneRequest(request);
                    return JsonResponse("""{"version":"4.5.3"}""");
                })));

        var result = await client.TestAsync(
            Connection(),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("4.5.3", result.Version);
        Assert.IsNotNull(captured);
        StringAssert.Contains(captured.RequestUri!.Query, "mode=version");
        StringAssert.Contains(captured.RequestUri.Query, "output=json");
        StringAssert.Contains(captured.RequestUri.Query, "apikey=secret-key");
    }

    [TestMethod]
    public async Task GrabAddsUrlWithConfiguredCategory()
    {
        HttpRequestMessage? captured = null;
        var client = new SabnzbdClient(
            new HttpClient(
                new StubHandler(request =>
                {
                    captured = CloneRequest(request);
                    return JsonResponse(
                        """{"status":true,"nzo_ids":["SABnzbd_nzo_123"]}""");
                })));

        var result = await client.GrabAsync(
            Connection(),
            new SabnzbdGrabRequest(
                new Uri("https://prowlarr.example/download?id=123"),
                "Anime - 01"),
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        CollectionAssert.AreEqual(
            new[] { "SABnzbd_nzo_123" },
            result.NzoIds.ToArray());
        StringAssert.Contains(captured!.RequestUri!.Query, "mode=addurl");
        StringAssert.Contains(captured.RequestUri.Query, "cat=anilingo");
        StringAssert.Contains(
            Uri.UnescapeDataString(captured.RequestUri.Query),
            "name=https://prowlarr.example/download?id=123");
    }

    [TestMethod]
    public void QueueParserHandlesStringMetrics()
    {
        const string json = """
        {
          "queue": {
            "paused": false,
            "speed": "42.1 M",
            "timeleft": "0:01:20",
            "slots": [
              {
                "nzo_id": "SABnzbd_nzo_1",
                "filename": "Anime - 01",
                "status": "Downloading",
                "cat": "anilingo",
                "percentage": "52.4",
                "mb": "1000",
                "mbleft": "476"
              }
            ]
          }
        }
        """;

        var queue = SabnzbdClient.ParseQueueResponse(json);

        Assert.IsFalse(queue.Paused);
        Assert.AreEqual(1, queue.Jobs.Count);
        Assert.AreEqual(52.4, queue.Jobs[0].Percentage);
        Assert.AreEqual(1000L * 1024 * 1024, queue.Jobs[0].SizeBytes);
        Assert.AreEqual(476L * 1024 * 1024, queue.Jobs[0].SizeLeftBytes);
    }

    [TestMethod]
    public void HistoryParserClassifiesPasswordAndUnpackFailures()
    {
        const string json = """
        {
          "history": {
            "slots": [
              {
                "nzo_id": "a",
                "name": "Passworded",
                "status": "Failed",
                "fail_message": "Encrypted archive requires a password"
              },
              {
                "nzo_id": "b",
                "name": "Broken",
                "status": "Failed",
                "fail_message": "Unpack failed"
              }
            ]
          }
        }
        """;

        var history = SabnzbdClient.ParseHistoryResponse(json);

        Assert.AreEqual(
            SabnzbdFailureKind.Password,
            history.Jobs[0].FailureKind);
        Assert.AreEqual(
            SabnzbdFailureKind.Unpack,
            history.Jobs[1].FailureKind);
    }

    [TestMethod]
    public async Task CancelAndRetryUseSABJobId()
    {
        var captured = new List<HttpRequestMessage>();
        var responses = new Queue<string>(
        [
            """{"status":true,"nzo_ids":["job-1"]}""",
            """{"status":true,"nzo_id":"job-2"}"""
        ]);

        var client = new SabnzbdClient(
            new HttpClient(
                new StubHandler(request =>
                {
                    captured.Add(CloneRequest(request));
                    return JsonResponse(responses.Dequeue());
                })));

        var cancelled = await client.CancelAsync(
            Connection(),
            "job-1",
            deleteFiles: true,
            CancellationToken.None);
        var retried = await client.RetryAsync(
            Connection(),
            "job-1",
            CancellationToken.None);

        Assert.IsTrue(cancelled.Success);
        Assert.IsTrue(retried.Success);
        Assert.AreEqual("job-2", retried.NewNzoId);
        StringAssert.Contains(captured[0].RequestUri!.Query, "name=delete");
        StringAssert.Contains(captured[0].RequestUri.Query, "del_files=1");
        StringAssert.Contains(captured[1].RequestUri!.Query, "mode=retry");
    }

    [TestMethod]
    public async Task AcquisitionStorePersistsRelationshipWithoutDownloadUrl()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new SabnzbdAcquisitionStore(directory);
            var animeId = Guid.NewGuid();
            var episodeId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            await store.AddAsync(
                new SabnzbdAcquisitionJob(
                    Guid.NewGuid(),
                    "job-1",
                    animeId,
                    [episodeId],
                    "release:abc",
                    "Anime - 01",
                    SabnzbdAcquisitionState.Queued,
                    1,
                    SabnzbdFailureKind.None,
                    null,
                    now,
                    now));

            var reloaded = new SabnzbdAcquisitionStore(directory);
            var jobs = await reloaded.LoadAsync();

            Assert.AreEqual(1, jobs.Count);
            Assert.AreEqual(animeId, jobs[0].AnimeId);
            Assert.AreEqual(episodeId, jobs[0].EpisodeIds.Single());

            var raw = await File.ReadAllTextAsync(
                Path.Combine(directory.FullName, "sabnzbd-jobs.json"));
            Assert.IsFalse(
                raw.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("https://", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [TestMethod]
    public async Task AcquisitionServiceReconcilesFailedHistoryJob()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new SabnzbdAcquisitionStore(directory);
            var fake = new FakeSabnzbdClient
            {
                Queue = new SabnzbdQueueSnapshot(false, null, null, []),
                History = new SabnzbdHistorySnapshot(
                [
                    new SabnzbdHistoryJob(
                        "job-1",
                        "Anime - 01",
                        "Failed",
                        "anilingo",
                        null,
                        "Unpack failed",
                        SabnzbdFailureKind.Unpack,
                        DateTimeOffset.UtcNow)
                ])
            };
            var service = new SabnzbdAcquisitionService(fake, store);
            var now = DateTimeOffset.UtcNow;

            await store.AddAsync(
                new SabnzbdAcquisitionJob(
                    Guid.NewGuid(),
                    "job-1",
                    Guid.NewGuid(),
                    [Guid.NewGuid()],
                    "release:abc",
                    "Anime - 01",
                    SabnzbdAcquisitionState.Downloading,
                    1,
                    SabnzbdFailureKind.None,
                    null,
                    now,
                    now));

            var jobs = await service.ReconcileAsync(
                Connection(),
                CancellationToken.None);

            Assert.AreEqual(SabnzbdAcquisitionState.Failed, jobs.Single().State);
            Assert.AreEqual(SabnzbdFailureKind.Unpack, jobs.Single().FailureKind);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static SabnzbdConnection Connection() =>
        new(
            SabnzbdSettings.CreateDefault("http://sabnzbd:8080"),
            "secret-key");

    private static DirectoryInfo CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"anilingo-sab-{Guid.NewGuid():N}");
        return Directory.CreateDirectory(path);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request) =>
        new(
            request.Method,
            request.RequestUri);

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class FakeSabnzbdClient : ISabnzbdClient
    {
        public SabnzbdQueueSnapshot Queue { get; set; } =
            new(false, null, null, []);

        public SabnzbdHistorySnapshot History { get; set; } =
            new([]);

        public Task<SabnzbdConnectionTestResult> TestAsync(
            SabnzbdConnection connection,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SabnzbdConnectionTestResult(true, "test"));

        public Task<SabnzbdGrabResult> GrabAsync(
            SabnzbdConnection connection,
            SabnzbdGrabRequest grab,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new SabnzbdGrabResult(true, ["job-new"]));

        public Task<SabnzbdQueueSnapshot> GetQueueAsync(
            SabnzbdConnection connection,
            CancellationToken cancellationToken) =>
            Task.FromResult(Queue);

        public Task<SabnzbdHistorySnapshot> GetHistoryAsync(
            SabnzbdConnection connection,
            IReadOnlyCollection<string>? nzoIds,
            CancellationToken cancellationToken) =>
            Task.FromResult(History);

        public Task<SabnzbdActionResult> CancelAsync(
            SabnzbdConnection connection,
            string nzoId,
            bool deleteFiles,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SabnzbdActionResult(true));

        public Task<SabnzbdActionResult> RetryAsync(
            SabnzbdConnection connection,
            string nzoId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SabnzbdActionResult(true, "job-retry"));

        public Task<SabnzbdActionResult> PauseAsync(
            SabnzbdConnection connection,
            string nzoId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SabnzbdActionResult(true));

        public Task<SabnzbdActionResult> ResumeAsync(
            SabnzbdConnection connection,
            string nzoId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SabnzbdActionResult(true));
    }
}
