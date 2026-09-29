using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Jularr.Web.Features.Providers;
using Jularr.Web.Features.Subtitles;
using Jularr.Web.Features.Subtitles.OpenSubtitles;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

/// <summary>
/// The OpenSubtitles integration (#560) against a mock HTTP handler: request building, response
/// mapping (language / forced / SDH stay faithful to the searched profile item), the login + download
/// flow, Retry-After handling through the provider framework, credential protection and the
/// "offered only when configured" seam. No live network calls.
/// </summary>
[TestClass]
public sealed class OpenSubtitlesProviderTests
{
    private const string ApiKey = "test-api-key-not-real";
    private const string Password = "test-password-not-real";
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static readonly ProviderExecutionPolicy FastPolicy = new()
    {
        MaxAttempts = 2,
        BaseBackoff = TimeSpan.Zero,
        MinSpacing = null,
        DefaultRetryAfter = TimeSpan.FromSeconds(5),
        MaxRetryAfter = TimeSpan.FromMinutes(10)
    };

    private static readonly OpenSubtitlesCredential SearchOnly = new() { ApiKey = ApiKey };

    private static readonly OpenSubtitlesCredential FullAccount = new()
    {
        ApiKey = ApiKey,
        Username = "jularr-owner",
        Password = Password
    };

    private static SubtitleSearchRequest Request(
        string language = "en",
        bool forced = false,
        bool sdh = false,
        string title = "Sousou no Frieren",
        int season = 1,
        int episode = 5) =>
        new(Guid.NewGuid(), title, season, episode, language, forced, sdh);

    // --- Search: request building + mapping -------------------------------------------------------

    [TestMethod]
    public async Task SearchAsksForTheProfileItemAndMapsOnlyMatchingCandidates()
    {
        var handler = new StubHandler().On("/subtitles", _ => Json(SearchBody(
            Item(fileId: 101, language: "en", release: "Frieren.S01E05.1080p.WEB", trusted: true, downloads: 9000, uploader: "trusted-uploader", upload: "2026-05-01T10:00:00Z"),
            Item(fileId: 102, language: "en", release: "Frieren.S01E05.MT", machineTranslated: true, downloads: 5),
            Item(fileId: 103, language: "fr", release: "Frieren.S01E05.FR"),
            Item(fileId: 104, language: "en", release: "Frieren.S01E05.SDH", hearingImpaired: true),
            Item(fileId: 105, language: "en", release: "Frieren.S01E05.Forced", foreignPartsOnly: true),
            Item(fileId: 106, language: "en", release: "Frieren.S01E06.Wrong", episode: 6),
            Item(fileId: null, language: "en", release: "Frieren.NoFiles"))));
        var (provider, _) = CreateProvider(handler);

        var results = await provider.SearchAsync(Request(), CancellationToken.None);

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual("101", results[0].ResultToken);
        Assert.AreEqual("102", results[1].ResultToken);
        Assert.IsTrue(results[0].Score > results[1].Score, "A trusted, well-downloaded upload ranks above a machine translation.");
        foreach (var result in results)
        {
            Assert.AreEqual(ProviderKeys.OpenSubtitles, result.ProviderId);
            Assert.AreEqual("en", result.LanguageTag);
            Assert.IsFalse(result.Forced);
            Assert.IsFalse(result.Sdh);
        }

        Assert.AreEqual("Frieren.S01E05.1080p.WEB", results[0].ReleaseName);
        Assert.AreEqual("trusted-uploader", results[0].UploaderOrSource);
        Assert.AreEqual(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero), results[0].PublishedAt);

        var sent = handler.Requests.Single();
        Assert.AreEqual(HttpMethod.Get, sent.Method);
        Assert.AreEqual("api.opensubtitles.com", sent.Uri.Host);
        var query = System.Web.HttpUtility.ParseQueryString(sent.Uri.Query);
        Assert.AreEqual("en", query["languages"]);
        Assert.AreEqual("sousou no frieren", query["query"]);
        Assert.AreEqual("episode", query["type"]);
        Assert.AreEqual("1", query["season_number"]);
        Assert.AreEqual("5", query["episode_number"]);
        Assert.AreEqual("exclude", query["foreign_parts_only"]);
        Assert.AreEqual("exclude", query["hearing_impaired"]);
        CollectionAssert.AreEqual(
            query.AllKeys.OrderBy(key => key, StringComparer.Ordinal).ToArray(),
            query.AllKeys,
            "OpenSubtitles asks for alphabetically ordered query parameters.");
        Assert.AreEqual(ApiKey, sent.Header("Api-Key"));
        StringAssert.StartsWith(sent.Header("User-Agent"), "Jularr v");
        Assert.IsNull(sent.Header("Authorization"), "Searching needs only the API key.");
        StringAssert.DoesNotMatch(sent.Uri.ToString(), new System.Text.RegularExpressions.Regex(ApiKey), "The API key never goes in a URL.");
    }

    [TestMethod]
    public async Task ForcedAndSdhItemsSearchForThoseFlagsAndKeepTheRequestedTag()
    {
        var handler = new StubHandler().On("/subtitles", _ => Json(SearchBody(
            Item(fileId: 201, language: "en", release: "Both", hearingImpaired: true, foreignPartsOnly: true),
            Item(fileId: 202, language: "en", release: "SdhOnly", hearingImpaired: true))));
        var (provider, _) = CreateProvider(handler);

        var results = await provider.SearchAsync(Request(language: "ENG", forced: true, sdh: true), CancellationToken.None);

        var result = results.Single();
        Assert.AreEqual("201", result.ResultToken);
        Assert.IsTrue(result.Forced);
        Assert.IsTrue(result.Sdh);
        Assert.AreEqual("eng", result.LanguageTag, "Results carry the profile item's own tag so they satisfy exactly that item.");

        var query = System.Web.HttpUtility.ParseQueryString(handler.Requests.Single().Uri.Query);
        Assert.AreEqual("en", query["languages"], "The three-letter alias is translated to the code OpenSubtitles expects.");
        Assert.AreEqual("only", query["foreign_parts_only"]);
        Assert.AreEqual("only", query["hearing_impaired"]);
    }

    [TestMethod]
    public async Task RegionalVariantsAreSearchedAndLabelledWithTheRequestedTag()
    {
        var handler = new StubHandler().On("/subtitles", _ => Json(SearchBody(
            Item(fileId: 301, language: "pt-BR", release: "Brazil"),
            Item(fileId: 302, language: "pt-PT", release: "Portugal"),
            Item(fileId: 303, language: "es", release: "Spanish"))));
        var (provider, _) = CreateProvider(handler);

        var results = await provider.SearchAsync(Request(language: "pt"), CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] { "301", "302" }, results.Select(r => r.ResultToken).ToArray());
        Assert.IsTrue(results.All(r => r.LanguageTag == "pt"));
        Assert.AreEqual("pt-pt,pt-br", System.Web.HttpUtility.ParseQueryString(handler.Requests.Single().Uri.Query)["languages"]);
    }

    [TestMethod]
    public async Task SearchWithoutATitleOrLanguageMakesNoRequest()
    {
        var handler = new StubHandler();
        var (provider, _) = CreateProvider(handler);

        Assert.AreEqual(0, (await provider.SearchAsync(Request(title: "  "), CancellationToken.None)).Count);
        Assert.AreEqual(0, (await provider.SearchAsync(Request(language: ""), CancellationToken.None)).Count);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task IdenticalSearchesAreAnsweredFromTheCacheAndServedStaleWhenTheProviderFails()
    {
        var clock = new FakeClock(Now);
        var failing = false;
        var handler = new StubHandler().On("/subtitles", _ => failing
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Json(SearchBody(Item(fileId: 401, language: "en", release: "Cached"))));
        var (provider, _) = CreateProvider(handler, clock: clock);

        var first = await provider.SearchAsync(Request(), CancellationToken.None);
        var second = await provider.SearchAsync(Request(), CancellationToken.None);
        Assert.AreEqual(1, handler.Requests.Count, "A fresh identical search does not hit OpenSubtitles again.");
        Assert.AreEqual("401", second.Single().ResultToken);

        clock.Advance(OpenSubtitlesSubtitleProvider.SearchFreshFor + TimeSpan.FromMinutes(1));
        failing = true;
        var stale = await provider.SearchAsync(Request(), CancellationToken.None);

        Assert.AreEqual("401", stale.Single().ResultToken, "Stale-while-unavailable serves the last good answer.");
        Assert.IsTrue(handler.Requests.Count > 1);
        Assert.AreEqual(first.Single().ResultToken, stale.Single().ResultToken);
    }

    // --- Search: errors, rate limiting, health ---------------------------------------------------

    [TestMethod]
    public async Task ARateLimitedSearchFailsFastWithTheRetryAfterAndPausesFurtherCalls()
    {
        var clock = new FakeClock(Now);
        var handler = new StubHandler().On("/subtitles", _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });
        var health = new ProviderHealthTracker(clock);
        var (provider, _) = CreateProvider(handler, clock: clock, health: health);

        var limited = await Assert.ThrowsExactlyAsync<ProviderRateLimitedException>(
            () => provider.SearchAsync(Request(), CancellationToken.None));
        Assert.AreEqual(TimeSpan.FromSeconds(30), limited.RetryAfter);
        Assert.AreEqual(ProviderKeys.OpenSubtitles, limited.ProviderKey);
        Assert.AreEqual(1, handler.Requests.Count);

        // The framework gate now blocks every OpenSubtitles call until the Retry-After has passed.
        await Assert.ThrowsExactlyAsync<ProviderRateLimitedException>(
            () => provider.SearchAsync(Request(episode: 6), CancellationToken.None));
        Assert.AreEqual(1, handler.Requests.Count, "No request is sent while the provider is rate limited.");

        var snapshot = health.Get(ProviderKeys.OpenSubtitles);
        Assert.AreEqual(ProviderHealthStatus.Degraded, snapshot.Status);
        StringAssert.Contains(snapshot.LastError, "429");

        clock.Advance(TimeSpan.FromSeconds(31));
        handler.On("/subtitles", _ => Json(SearchBody(Item(fileId: 501, language: "en", release: "After", episode: 7))));
        Assert.AreEqual("501", (await provider.SearchAsync(Request(episode: 7), CancellationToken.None)).Single().ResultToken);
        Assert.AreEqual(ProviderHealthStatus.Healthy, health.Get(ProviderKeys.OpenSubtitles).Status);
    }

    [TestMethod]
    public async Task ARejectedApiKeyIsAnActionableProviderErrorWithoutTheKey()
    {
        var handler = new StubHandler().On("/subtitles", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var (provider, _) = CreateProvider(handler);

        var error = await Assert.ThrowsExactlyAsync<SubtitleProviderException>(
            () => provider.SearchAsync(Request(), CancellationToken.None));

        StringAssert.Contains(error.Message, "rejected");
        Assert.IsFalse(error.Message.Contains(ApiKey, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task AServerErrorIsRetriedOnceThenSurfacedAndCountsAgainstHealth()
    {
        var clock = new FakeClock(Now);
        var health = new ProviderHealthTracker(clock);
        var handler = new StubHandler().On("/subtitles", _ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var (provider, _) = CreateProvider(handler, clock: clock, health: health);

        var error = await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => provider.SearchAsync(Request(), CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.AreEqual(2, handler.Requests.Count, "One retry for a 5xx, then the failure is reported.");
        Assert.AreEqual(2, health.Get(ProviderKeys.OpenSubtitles).ConsecutiveFailures);
    }

    [TestMethod]
    public async Task AMalformedResponseIsReportedNotThrownAsAJsonException()
    {
        var handler = new StubHandler().On("/subtitles", _ => Json("{ this is not json"));
        var (provider, _) = CreateProvider(handler);

        await Assert.ThrowsExactlyAsync<SubtitleProviderException>(
            () => provider.SearchAsync(Request(), CancellationToken.None));
    }

    // --- Download ---------------------------------------------------------------------------------

    [TestMethod]
    public async Task DownloadLogsInOnceRequestsTheFileAndDecodesTheSubtitle()
    {
        var handler = HappyDownloadHandler(out var counters);
        var (provider, _) = CreateProvider(handler, FullAccount);
        var result = ResultFor("777");

        var first = await provider.DownloadAsync(result, CancellationToken.None);
        var second = await provider.DownloadAsync(result, CancellationToken.None);

        Assert.IsTrue(first.Success);
        Assert.AreEqual("srt", first.Format);
        Assert.AreEqual("1\n00:00:01,000 --> 00:00:02,000\nHello", first.Content, "The byte-order mark is stripped.");
        Assert.IsTrue(second.Success);
        Assert.AreEqual(1, counters.Logins, "The login is shared between downloads.");
        Assert.AreEqual(2, counters.Downloads);

        var login = handler.Requests.First(r => r.Uri.AbsolutePath.EndsWith("/login", StringComparison.Ordinal));
        using var loginBody = JsonDocument.Parse(login.Body!);
        Assert.AreEqual("jularr-owner", loginBody.RootElement.GetProperty("username").GetString());
        Assert.AreEqual(Password, loginBody.RootElement.GetProperty("password").GetString());

        var download = handler.Requests.First(r => r.Uri.AbsolutePath.EndsWith("/download", StringComparison.Ordinal));
        Assert.AreEqual("Bearer test-token", download.Header("Authorization"));
        Assert.AreEqual(ApiKey, download.Header("Api-Key"));
        using var downloadBody = JsonDocument.Parse(download.Body!);
        Assert.AreEqual(777, downloadBody.RootElement.GetProperty("file_id").GetInt64());
        Assert.AreEqual("srt", downloadBody.RootElement.GetProperty("sub_format").GetString());

        var file = handler.Requests.First(r => r.Uri.Host == "www.opensubtitles.com");
        Assert.IsNull(file.Header("Api-Key"), "The file host never receives the API key.");
        Assert.IsNull(file.Header("Authorization"), "The file host never receives the account token.");
    }

    [TestMethod]
    public async Task ARejectedTokenIsRenewedOnceThenTheDownloadSucceeds()
    {
        var handler = HappyDownloadHandler(out var counters);
        var rejectFirst = true;
        handler.On("/download", request =>
        {
            counters.Downloads++;
            if (rejectFirst)
            {
                rejectFirst = false;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            return Json("""{ "link": "https://www.opensubtitles.com/download/abc/file.srt", "file_name": "file.srt" }""");
        });
        var (provider, _) = CreateProvider(handler, FullAccount);

        var result = await provider.DownloadAsync(ResultFor("778"), CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(2, counters.Logins, "A revoked token triggers exactly one fresh login.");
        Assert.AreEqual(2, counters.Downloads);
    }

    [TestMethod]
    public async Task ADownloadQuotaErrorCarriesTheProvidersMessageAndNothingElse()
    {
        var handler = HappyDownloadHandler(out _);
        handler.On("/download", _ => new HttpResponseMessage(HttpStatusCode.NotAcceptable)
        {
            Content = new StringContent("""{ "message": "You have downloaded your allowed 20 subtitles for 24h. Your quota will be renewed in 3 hours." }""", Encoding.UTF8, "application/json")
        });
        var (provider, _) = CreateProvider(handler, FullAccount);

        var error = await Assert.ThrowsExactlyAsync<SubtitleProviderException>(
            () => provider.DownloadAsync(ResultFor("779"), CancellationToken.None));

        StringAssert.Contains(error.Message, "quota will be renewed");
        Assert.IsFalse(error.Message.Contains(Password, StringComparison.Ordinal));
        Assert.IsFalse(error.Message.Contains("test-token", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task DownloadNeedsTheAccountLoginNotJustTheApiKey()
    {
        var handler = new StubHandler();
        var (provider, _) = CreateProvider(handler, SearchOnly);

        var error = await Assert.ThrowsExactlyAsync<SubtitleProviderException>(
            () => provider.DownloadAsync(ResultFor("780"), CancellationToken.None));

        StringAssert.Contains(error.Message, "username and password");
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ALoginRejectedByOpenSubtitlesIsAProviderError()
    {
        var handler = new StubHandler().On("/login", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var (provider, _) = CreateProvider(handler, FullAccount);

        var error = await Assert.ThrowsExactlyAsync<SubtitleProviderException>(
            () => provider.DownloadAsync(ResultFor("781"), CancellationToken.None));

        StringAssert.Contains(error.Message, "username or password");
    }

    [TestMethod]
    public async Task DownloadLinksAndLoginHostsOutsideOpenSubtitlesAreNeverFollowed()
    {
        var counters = new Counters();
        var handler = new StubHandler()
            .On("/login", _ =>
            {
                counters.Logins++;
                return Json("""{ "token": "test-token", "base_url": "evil.example.net", "user": { "allowed_downloads": 20 } }""");
            })
            .On("/download", _ => Json("""{ "link": "https://evil.example.net/steal.srt", "file_name": "steal.srt" }"""));
        var (provider, _) = CreateProvider(handler, FullAccount);

        var error = await Assert.ThrowsExactlyAsync<SubtitleProviderException>(
            () => provider.DownloadAsync(ResultFor("782"), CancellationToken.None));

        StringAssert.Contains(error.Message, "unexpected download link");
        Assert.IsTrue(
            handler.Requests.All(r => r.Uri.Host == "api.opensubtitles.com"),
            "The bearer token and API key only ever go to opensubtitles.com, even if login names another host.");
        Assert.AreEqual(0, handler.Requests.Count(r => r.Uri.Host == "evil.example.net"));
    }

    [TestMethod]
    public async Task AnOversizedSubtitleFileIsRejected()
    {
        var handler = HappyDownloadHandler(out _);
        handler.On("/download/abc", _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[SubtitleDownloadContent.MaxSubtitleBytes + 1])
        });
        var (provider, _) = CreateProvider(handler, FullAccount);

        await Assert.ThrowsExactlyAsync<SubtitleProviderException>(
            () => provider.DownloadAsync(ResultFor("783"), CancellationToken.None));
    }

    [TestMethod]
    public async Task AnInvalidResultTokenIsAFailedDownloadWithoutARequest()
    {
        var handler = new StubHandler();
        var (provider, _) = CreateProvider(handler, FullAccount);

        var result = await provider.DownloadAsync(ResultFor("not-a-file-id"), CancellationToken.None);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    // --- Credentials ------------------------------------------------------------------------------

    [TestMethod]
    public async Task CredentialStoreEncryptsSecretsAtRestAndRoundTrips()
    {
        using var directory = new TempDirectory();
        var protection = new EphemeralDataProtectionProvider();
        var store = new OpenSubtitlesCredentialStore(protection, directory.Path);

        Assert.IsNull(await store.GetAsync());
        await store.SaveAsync(FullAccount);

        var file = await File.ReadAllTextAsync(Path.Combine(directory.Path, OpenSubtitlesCredentialStore.FileName));
        StringAssert.Contains(file, "jularr-owner");
        Assert.IsFalse(file.Contains(ApiKey, StringComparison.Ordinal), "The API key is never written unprotected.");
        Assert.IsFalse(file.Contains(Password, StringComparison.Ordinal), "The password is never written unprotected.");
        Assert.IsFalse(
            JsonSerializer.Serialize(FullAccount).Contains(ApiKey, StringComparison.Ordinal),
            "The in-memory credential keeps secrets out of JSON via [JsonIgnore].");

        var loaded = await store.GetAsync();
        Assert.IsNotNull(loaded);
        Assert.AreEqual(ApiKey, loaded.ApiKey);
        Assert.AreEqual(Password, loaded.Password);
        Assert.AreEqual("jularr-owner", loaded.Username);
        Assert.IsTrue(loaded.CanDownload);

        // A second save replaces the single account rather than adding another.
        await store.SaveAsync(new OpenSubtitlesCredential { ApiKey = "other-key", Username = "someone", Password = "x" });
        var all = await store.LoadAllAsync();
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual("other-key", all[0].ApiKey);

        Assert.IsTrue(await store.DeleteAsync(all[0].Id));
        Assert.IsNull(await store.GetAsync());
    }

    [TestMethod]
    public async Task SecretsThatCanNoLongerBeDecryptedCountAsNotConfiguredAndCanBeCleared()
    {
        using var directory = new TempDirectory();
        await new OpenSubtitlesCredentialStore(new EphemeralDataProtectionProvider(), directory.Path).SaveAsync(FullAccount);

        // A different key ring (as after losing /data/keys) cannot read the old blobs.
        var otherKeyRing = new OpenSubtitlesCredentialStore(new EphemeralDataProtectionProvider(), directory.Path);
        Assert.IsNull(await otherKeyRing.GetAsync());

        await otherKeyRing.ClearAsync();
        Assert.IsFalse(File.Exists(Path.Combine(directory.Path, OpenSubtitlesCredentialStore.FileName)));
    }

    [TestMethod]
    public void ProtectorsAreScopedPerProvider()
    {
        var protection = new EphemeralDataProtectionProvider();
        var openSubtitles = ProviderCredentials.ProtectorFor(protection, ProviderKeys.OpenSubtitles);
        var other = ProviderCredentials.ProtectorFor(protection, "another-provider");

        var blob = openSubtitles.Protect("secret");

        Assert.AreEqual("secret", openSubtitles.Unprotect(blob));
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => other.Unprotect(blob));
    }

    // --- Settings + "offered only when configured" -------------------------------------------------

    [TestMethod]
    public async Task ProviderIsOfferedOnlyWhileAnApiKeyIsConfigured()
    {
        using var directory = new TempDirectory();
        var handler = new StubHandler();
        using var services = BuildServices(directory.Path, handler);
        using var scope = services.CreateScope();
        var sources = scope.ServiceProvider.GetServices<ISubtitleProviderSource>().ToArray();
        var store = services.GetRequiredService<OpenSubtitlesCredentialStore>();

        Assert.AreEqual(1, sources.Length);
        Assert.IsNull(await sources[0].GetProviderAsync(CancellationToken.None), "Unconfigured: no provider, so the UI keeps saying 'no providers configured'.");

        await store.SaveAsync(SearchOnly);
        var configured = await sources[0].GetProviderAsync(CancellationToken.None);
        Assert.IsNotNull(configured);
        Assert.AreEqual(ProviderKeys.OpenSubtitles, configured.Id);
        Assert.AreEqual("OpenSubtitles", configured.DisplayName);

        await store.ClearAsync();
        Assert.IsNull(await sources[0].GetProviderAsync(CancellationToken.None), "Removing the key takes effect immediately.");
    }

    [TestMethod]
    public void ProviderCatalogListsOpenSubtitlesWithTheSubtitlesCapability()
    {
        using var directory = new TempDirectory();
        using var services = BuildServices(directory.Path, new StubHandler());

        var descriptor = services.GetRequiredService<ProviderCatalog>().Get(ProviderKeys.OpenSubtitles);

        Assert.IsNotNull(descriptor);
        Assert.IsTrue(descriptor.Supports(ProviderCapabilities.Subtitles));
        Assert.AreEqual(descriptor, OpenSubtitlesSubtitleProvider.ProviderDescriptor);
    }

    [TestMethod]
    public async Task SavingTestsTheLoginFirstAndOnlyPersistsAWorkingAccount()
    {
        using var directory = new TempDirectory();
        var accepted = false;
        var handler = new StubHandler().On("/login", _ => accepted
            ? Json("""{ "token": "t", "base_url": "api.opensubtitles.com", "user": { "allowed_downloads": 20 } }""")
            : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var (settings, store) = CreateSettings(directory.Path, handler);

        Assert.AreEqual(OpenSubtitlesSaveOutcome.MissingFields, await settings.SaveAsync(ApiKey, "user", "", CancellationToken.None));
        Assert.AreEqual(OpenSubtitlesSaveOutcome.Rejected, await settings.SaveAsync(ApiKey, "user", Password, CancellationToken.None));
        Assert.IsNull(await store.GetAsync(), "A rejected login is not saved.");
        Assert.IsFalse((await settings.GetStatusAsync(CancellationToken.None)).Configured);

        accepted = true;
        Assert.AreEqual(OpenSubtitlesSaveOutcome.Saved, await settings.SaveAsync($"  {ApiKey}  ", " user ", Password, CancellationToken.None));
        var status = await settings.GetStatusAsync(CancellationToken.None);
        Assert.IsTrue(status.Configured);
        Assert.IsTrue(status.CanDownload);
        Assert.AreEqual("user", status.Username);
        Assert.AreEqual(ApiKey, (await store.GetAsync())!.ApiKey);

        await settings.RemoveAsync(CancellationToken.None);
        Assert.IsFalse((await settings.GetStatusAsync(CancellationToken.None)).Configured);
    }

    [TestMethod]
    public async Task AnUnreachableOpenSubtitlesDoesNotSaveTheAccount()
    {
        using var directory = new TempDirectory();
        var handler = new StubHandler().On("/login", _ => throw new HttpRequestException("connection refused"));
        var (settings, store) = CreateSettings(directory.Path, handler);

        var outcome = await settings.SaveAsync(ApiKey, "user", Password, CancellationToken.None);

        Assert.AreEqual(OpenSubtitlesSaveOutcome.Unreachable, outcome);
        Assert.IsNull(await store.GetAsync());
    }

    // --- Import wiring (#526 language profiles) ---------------------------------------------------

    [TestMethod]
    public async Task ImportedResultsSatisfyExactlyTheSearchedProfileItemsAndReimportIsIdempotent()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var (_, episode, _, _) = await fixture.AddEpisodeAsync("Sousou no Frieren", 1, 5);
        var profiles = new SubtitleLanguageProfileService(fixture.Db);
        await profiles.UpsertAsync(
            null,
            "Learner",
            [new SubtitleLanguageProfileItemInput("en", false, false), new SubtitleLanguageProfileItemInput("de", false, true)],
            null,
            CancellationToken.None);
        var completeness = new SubtitleCompletenessService(fixture.Db, profiles);

        var before = await completeness.GetEpisodeCompletionAsync(episode.Id, CancellationToken.None);
        Assert.AreEqual(SubtitleProfileCompletionStatus.Missing, before!.Status);
        Assert.AreEqual(2, before.Missing.Count);

        const string EnglishSrt = "1\n00:00:01,000 --> 00:00:02,000\nHello\n\n2\n00:00:03,000 --> 00:00:04,000\nWorld\n";
        const string GermanSrt = "1\n00:00:01,000 --> 00:00:02,000\nHallo\n";
        var handler = new StubHandler()
            .On("/subtitles", request =>
            {
                var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
                return query["languages"] == "de"
                    ? Json(SearchBody(Item(fileId: 201, language: "de", release: "Frieren.DE.SDH", hearingImpaired: true)))
                    : Json(SearchBody(Item(fileId: 101, language: "en", release: "Frieren.EN")));
            })
            .On("/login", _ => Json("""{ "token": "test-token", "base_url": "api.opensubtitles.com", "user": { "allowed_downloads": 20 } }"""))
            .On("/download", request => Json(request.Content!.ReadAsStringAsync().Result.Contains("201", StringComparison.Ordinal)
                ? """{ "link": "https://www.opensubtitles.com/download/f201/de.srt", "file_name": "de.srt" }"""
                : """{ "link": "https://www.opensubtitles.com/download/f101/en.srt", "file_name": "en.srt" }"""))
            .On("/download/f", request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("f201", StringComparison.Ordinal) ? GermanSrt : EnglishSrt)
            });

        using var directory = new TempDirectory();
        var protection = new EphemeralDataProtectionProvider();
        var store = new OpenSubtitlesCredentialStore(protection, directory.Path);
        await store.SaveAsync(FullAccount);
        var clock = new FakeClock(Now);
        var source = new OpenSubtitlesProviderSource(store, CreateClient(handler, clock), new ProviderResponseCache(clock));
        var importService = new SubtitleImportService(
            fixture.Db,
            new Jularr.Web.Features.Vocabulary.VocabularyService(
                fixture.Db,
                new Jularr.Web.Features.Vocabulary.JapaneseTermExtractor(new NoMorphology()),
                new Jularr.Web.Features.Vocabulary.JapaneseDictionary()));
        var service = new SubtitleManualSearchService([source], importService);

        // English, full track.
        var english = (await service.SearchAsync(
            new SubtitleSearchRequest(episode.Id, "Sousou no Frieren", 1, 5, "en", false, false), CancellationToken.None))
            .Single().Results.Single();
        Assert.IsTrue((await service.ImportAsync(episode.Id, english, CancellationToken.None)).Success);

        var afterEnglish = await completeness.GetEpisodeCompletionAsync(episode.Id, CancellationToken.None);
        Assert.AreEqual(SubtitleProfileCompletionStatus.Missing, afterEnglish!.Status);
        Assert.AreEqual("de", afterEnglish.Missing.Single().LanguageTag);
        Assert.IsTrue(afterEnglish.Missing.Single().Sdh);

        // German SDH, the remaining profile item.
        var german = (await service.SearchAsync(
            new SubtitleSearchRequest(episode.Id, "Sousou no Frieren", 1, 5, "de", false, true), CancellationToken.None))
            .Single().Results.Single();
        Assert.IsTrue(german.Sdh);
        Assert.IsTrue((await service.ImportAsync(episode.Id, german, CancellationToken.None)).Success);

        var complete = await completeness.GetEpisodeCompletionAsync(episode.Id, CancellationToken.None);
        Assert.AreEqual(SubtitleProfileCompletionStatus.Complete, complete!.Status);

        var tracks = await fixture.Db.SubtitleTracks.Where(t => t.EpisodeId == episode.Id).OrderBy(t => t.Language).ToListAsync();
        Assert.AreEqual(2, tracks.Count);
        Assert.IsTrue(tracks.All(t => t.Path.StartsWith(SubtitleImportService.ProviderSourcePrefix, StringComparison.Ordinal)));
        Assert.AreEqual(("de", false, true), (tracks[0].Language, tracks[0].Forced, tracks[0].Sdh));
        Assert.AreEqual(("en", false, false), (tracks[1].Language, tracks[1].Forced, tracks[1].Sdh));
        Assert.AreEqual(2, await fixture.Db.SubtitleCues.CountAsync(c => c.SubtitleTrackId == tracks[1].Id));

        // Importing the same result again replaces the track's cues instead of duplicating anything.
        Assert.IsTrue((await service.ImportAsync(episode.Id, english, CancellationToken.None)).Success);
        Assert.AreEqual(2, await fixture.Db.SubtitleTracks.CountAsync(t => t.EpisodeId == episode.Id));
        Assert.AreEqual(2, await fixture.Db.SubtitleCues.CountAsync(c => c.SubtitleTrackId == tracks[1].Id));
    }

    [TestMethod]
    public async Task ARateLimitedProviderIsReportedAsAnOutcomeThroughTheManualSearchService()
    {
        await using var fixture = await SubtitleProfileFixture.CreateAsync();
        var clock = new FakeClock(Now);
        var handler = new StubHandler().On("/subtitles", _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
            return response;
        });
        using var directory = new TempDirectory();
        var store = new OpenSubtitlesCredentialStore(new EphemeralDataProtectionProvider(), directory.Path);
        await store.SaveAsync(SearchOnly);
        var source = new OpenSubtitlesProviderSource(store, CreateClient(handler, clock), new ProviderResponseCache(clock));
        var service = new SubtitleManualSearchService(
            [source],
            new SubtitleImportService(
                fixture.Db,
                new Jularr.Web.Features.Vocabulary.VocabularyService(
                    fixture.Db,
                    new Jularr.Web.Features.Vocabulary.JapaneseTermExtractor(new NoMorphology()),
                    new Jularr.Web.Features.Vocabulary.JapaneseDictionary())));

        var outcome = (await service.SearchAsync(
            new SubtitleSearchRequest(Guid.NewGuid(), "Frieren", 1, 1, "en", false, false), CancellationToken.None)).Single();

        Assert.AreEqual(ProviderKeys.OpenSubtitles, outcome.ProviderId);
        Assert.AreEqual(0, outcome.Results.Count);
        StringAssert.Contains(outcome.Error, "rate limited");
        StringAssert.Contains(outcome.Error, "45s");
    }

    // --- Helpers ----------------------------------------------------------------------------------

    private static (OpenSubtitlesSubtitleProvider Provider, OpenSubtitlesClient Client) CreateProvider(
        StubHandler handler,
        OpenSubtitlesCredential? credential = null,
        FakeClock? clock = null,
        ProviderHealthTracker? health = null)
    {
        clock ??= new FakeClock(Now);
        var client = CreateClient(handler, clock, health);
        var provider = new OpenSubtitlesSubtitleProvider(
            credential ?? SearchOnly,
            client,
            new ProviderResponseCache(clock));
        return (provider, client);
    }

    private static OpenSubtitlesClient CreateClient(
        StubHandler handler,
        FakeClock? clock = null,
        ProviderHealthTracker? health = null)
    {
        clock ??= new FakeClock(Now);
        return new OpenSubtitlesClient(
            new HttpClient(handler),
            ProviderTestFactory.NewExecutor(clock, new ProviderRateLimiter(), health ?? new ProviderHealthTracker(clock)),
            new OpenSubtitlesSessionCache(),
            clock,
            FastPolicy);
    }

    private static (OpenSubtitlesSettingsService Settings, OpenSubtitlesCredentialStore Store) CreateSettings(
        string directory,
        StubHandler handler)
    {
        var clock = new FakeClock(Now);
        var health = new ProviderHealthTracker(clock);
        var sessions = new OpenSubtitlesSessionCache();
        var store = new OpenSubtitlesCredentialStore(new EphemeralDataProtectionProvider(), directory);
        var client = new OpenSubtitlesClient(
            new HttpClient(handler),
            ProviderTestFactory.NewExecutor(clock, new ProviderRateLimiter(), health),
            sessions,
            clock,
            FastPolicy);
        return (new OpenSubtitlesSettingsService(store, client, sessions, health), store);
    }

    private static ServiceProvider BuildServices(string directory, StubHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddProviderFramework();
        services.AddSubtitleProviders();

        // The real registration points at /data; tests use a scratch directory and the mock handler.
        services.AddSingleton(provider => new OpenSubtitlesCredentialStore(
            provider.GetRequiredService<IDataProtectionProvider>(), directory));
        services.AddHttpClient<OpenSubtitlesClient>().ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider();
    }

    private static SubtitleSearchResult ResultFor(string token) =>
        new(ProviderKeys.OpenSubtitles, token, "en", false, false, "Release", null, null, null);

    private static StubHandler HappyDownloadHandler(out Counters counters)
    {
        var captured = new Counters();
        counters = captured;
        return new StubHandler()
            .On("/login", _ =>
            {
                captured.Logins++;
                return Json("""{ "token": "test-token", "base_url": "api.opensubtitles.com", "user": { "allowed_downloads": 20 }, "status": 200 }""");
            })
            .On("/download", _ =>
            {
                captured.Downloads++;
                return Json("""{ "link": "https://www.opensubtitles.com/download/abc/file.srt", "file_name": "file.srt", "remaining": 19 }""");
            })
            .On("/download/abc", _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("1\n00:00:01,000 --> 00:00:02,000\nHello")).ToArray())
            });
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string SearchBody(params string[] items) =>
        $$"""{ "total_pages": 1, "total_count": {{items.Length}}, "per_page": 60, "page": 1, "data": [{{string.Join(",", items)}}] }""";

    private static string Item(
        long? fileId,
        string language,
        string release,
        bool trusted = false,
        bool hearingImpaired = false,
        bool foreignPartsOnly = false,
        bool machineTranslated = false,
        int downloads = 10,
        string? uploader = "someone",
        string upload = "2026-01-01T00:00:00Z",
        int season = 1,
        int episode = 5)
    {
        var files = fileId is null
            ? "[]"
            : $$"""[{ "file_id": {{fileId}}, "cd_number": 1, "file_name": "{{release}}.srt" }]""";
        return $$"""
            {
              "id": "{{fileId ?? 0}}",
              "type": "subtitle",
              "attributes": {
                "subtitle_id": "{{fileId ?? 0}}",
                "language": "{{language}}",
                "download_count": {{downloads}},
                "hearing_impaired": {{hearingImpaired.ToString().ToLowerInvariant()}},
                "foreign_parts_only": {{foreignPartsOnly.ToString().ToLowerInvariant()}},
                "from_trusted": {{trusted.ToString().ToLowerInvariant()}},
                "ai_translated": false,
                "machine_translated": {{machineTranslated.ToString().ToLowerInvariant()}},
                "ratings": 8.5,
                "votes": 12,
                "upload_date": "{{upload}}",
                "release": "{{release}}",
                "uploader": { "uploader_id": 1, "name": "{{uploader}}", "rank": "Trusted member" },
                "feature_details": { "title": "Episode", "parent_title": "Sousou no Frieren", "season_number": {{season}}, "episode_number": {{episode}} },
                "files": {{files}}
              }
            }
            """;
    }

    private sealed class NoMorphology : Jularr.Web.Features.Vocabulary.IJapaneseMorphology
    {
        public IReadOnlyList<Jularr.Web.Features.Vocabulary.JapaneseMorphToken> Analyze(string text) => [];
    }

    private sealed class Counters
    {
        public int Logins { get; set; }
        public int Downloads { get; set; }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, string> Headers,
        string? Body)
    {
        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        // Longest matching path fragment wins, so "/download/abc" (the file) beats "/download" (the API).
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> routes = new(StringComparer.Ordinal);

        public List<RecordedRequest> Requests { get; } = [];

        public StubHandler On(string pathFragment, Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            routes[pathFragment] = responder;
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // NonValidated yields each header as it goes on the wire (User-Agent products are space separated).
            var headers = request.Headers.NonValidated.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, headers, body));

            var path = request.RequestUri!.AbsolutePath;
            var route = routes
                .Where(pair => path.Contains(pair.Key, StringComparison.Ordinal))
                .OrderByDescending(pair => pair.Key.Length)
                .FirstOrDefault();
            if (route.Value is null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return route.Value(request);
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan by) => current += by;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"jularr-opensubtitles-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
