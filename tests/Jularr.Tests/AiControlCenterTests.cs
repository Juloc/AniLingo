using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using Jularr.Web.Data;
using Jularr.Web.Features.Ai;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.Localization;
using Jularr.Web.Infrastructure.Ai;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

/// <summary>Model catalogs, capability-driven options, usage accounting and live activity (#422).</summary>
[TestClass]
public sealed class AiControlCenterTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task CatalogIsDiscoveredOnceAndServedFromCache()
    {
        var time = new ManualTime(Start);
        var service = new AiModelCatalogService(new MemoryCatalogStore(), time);
        var calls = 0;

        var first = await service.GetOrDiscoverAsync("key", _ =>
        {
            calls++;
            return Task.FromResult<IReadOnlyList<AiModelDescriptor>>([AiModelDescriptor.Basic("m1"), AiModelDescriptor.Basic("m1"), AiModelDescriptor.Basic("m2")]);
        }, CancellationToken.None);
        var second = await service.GetOrDiscoverAsync("key", _ => throw new InvalidOperationException("must not be called"), CancellationToken.None);

        Assert.AreEqual(1, calls);
        CollectionAssert.AreEqual(new[] { "m1", "m2" }, second.Models.Select(x => x.Id).ToArray(), "Duplicates are dropped.");
        Assert.AreEqual(AiModelDiscovery.Supported, first.Discovery);
        Assert.IsFalse(second.IsStale(time.GetUtcNow()));

        time.Advance(TimeSpan.FromHours(25));
        Assert.IsTrue(second.IsStale(time.GetUtcNow()), "A catalog older than a day is stale.");
    }

    [TestMethod]
    public async Task FailedRefreshKeepsTheLastKnownCatalogAndMarksItStale()
    {
        var time = new ManualTime(Start);
        var service = new AiModelCatalogService(new MemoryCatalogStore(), time);
        await service.RefreshAsync("key", _ => Task.FromResult<IReadOnlyList<AiModelDescriptor>>([AiModelDescriptor.Basic("m1")]), CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(5));
        var failed = await service.RefreshAsync(
            "key",
            _ => throw new HttpRequestException("503 from https://api.example.invalid/models?key=sk-abcdefghijkl"),
            CancellationToken.None);

        Assert.AreEqual("m1", failed.Models.Single().Id);
        Assert.AreEqual(Start, failed.FetchedAt);
        Assert.IsTrue(failed.IsStale(time.GetUtcNow()));
        Assert.IsNotNull(failed.LastError);
        Assert.IsFalse(failed.LastError.Contains("sk-abcdefghijkl", StringComparison.Ordinal), "Errors are sanitized.");

        var recovered = await service.RefreshAsync("key", _ => Task.FromResult<IReadOnlyList<AiModelDescriptor>>([AiModelDescriptor.Basic("m2")]), CancellationToken.None);
        Assert.IsFalse(recovered.IsStale(time.GetUtcNow()));
        Assert.IsNull(recovered.LastError);
    }

    [TestMethod]
    public async Task EmptyCatalogsAreRetriedAutomaticallyOnlyAfterTheRetryInterval()
    {
        var time = new ManualTime(Start);
        var service = new AiModelCatalogService(new MemoryCatalogStore(), time);
        var calls = 0;
        Task<IReadOnlyList<AiModelDescriptor>> Failing(CancellationToken _)
        {
            calls++;
            throw new HttpRequestException("offline");
        }

        Assert.IsTrue(AiModelCatalogService.NeedsDiscovery(AiModelCatalog.Empty("key"), Start), "Never asked: discover.");
        await service.GetOrDiscoverAsync("key", Failing, CancellationToken.None);
        await service.GetOrDiscoverAsync("key", Failing, CancellationToken.None);
        Assert.AreEqual(1, calls, "A failed first attempt is not repeated on every page load.");

        time.Advance(AiModelCatalogService.EmptyCatalogRetryInterval);
        var recovered = await service.GetOrDiscoverAsync(
            "key",
            _ => Task.FromResult<IReadOnlyList<AiModelDescriptor>>([AiModelDescriptor.Basic("m1")]),
            CancellationToken.None);
        Assert.AreEqual("m1", recovered.Models.Single().Id);

        time.Advance(TimeSpan.FromDays(3));
        Assert.IsFalse(AiModelCatalogService.NeedsDiscovery(recovered, time.GetUtcNow()), "Catalogs with models refresh explicitly only.");
    }

    [TestMethod]
    public async Task SlowDiscoveryTimesOutAndKeepsTheLastKnownModels()
    {
        var service = new AiModelCatalogService(new MemoryCatalogStore(), new ManualTime(Start));
        await service.RefreshAsync("key", _ => Task.FromResult<IReadOnlyList<AiModelDescriptor>>([AiModelDescriptor.Basic("m1")]), CancellationToken.None);

        var slow = await service.RefreshAsync(
            "key",
            async token =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return [];
            },
            CancellationToken.None,
            TimeSpan.FromMilliseconds(50));

        Assert.AreEqual("m1", slow.Models.Single().Id);
        Assert.IsNotNull(slow.LastError);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => service.RefreshAsync(
            "key",
            token => Task.FromCanceled<IReadOnlyList<AiModelDescriptor>>(token),
            cancelled.Token));
    }

    [TestMethod]
    public void EffectiveServerModelIsAlwaysConcreteOrNull()
    {
        var catalog = new AiModelCatalog(
            AiModelCatalogKeys.CodexServer,
            [AiModelDescriptor.Basic("first"), AiModelDescriptor.Basic("marked") with { IsDefault = true }],
            AiModelDiscovery.Supported,
            Start,
            Start,
            null);

        Assert.AreEqual("marked", AiOptionResolver.EffectiveServerModel(catalog, null), "The provider's marked default, by id.");
        Assert.AreEqual("marked", AiOptionResolver.EffectiveServerModel(catalog, "retired"));
        Assert.AreEqual("first", AiOptionResolver.EffectiveServerModel(catalog, " first "));
        Assert.AreEqual("custom", AiOptionResolver.EffectiveServerModel(AiModelCatalog.Empty("x"), "custom"));
        Assert.IsNull(AiOptionResolver.EffectiveServerModel(AiModelCatalog.Empty("x"), "  "), "No catalog and no model: nothing runs.");
    }

    [TestMethod]
    public async Task UnsupportedDiscoveryFallsBackToManualEntry()
    {
        var service = new AiModelCatalogService(new MemoryCatalogStore(), new ManualTime(Start));

        var catalog = await service.RefreshAsync(
            "key",
            _ => throw new AiModelDiscoveryUnsupportedException("no models endpoint"),
            CancellationToken.None);

        Assert.AreEqual(AiModelDiscovery.Unsupported, catalog.Discovery);
        Assert.IsFalse(catalog.HasModels);
        var options = AiOptionResolver.ResolvePersonal(new AiInvocationOptions("my-custom-model", "high", null, 512));
        Assert.AreEqual("my-custom-model", options.Model, "A manually entered model is never rejected.");
        Assert.IsNull(options.ReasoningEffort, "Unknown provider options are not sent.");
        Assert.AreEqual(512, options.MaxOutputTokens);
    }

    [TestMethod]
    public async Task CatalogStorePersistsModelsAndStaleState()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new AiModelCatalogStore(fixture.Db);
        var catalog = new AiModelCatalog(
            AiModelCatalogKeys.CodexServer,
            [new AiModelDescriptor("gpt-a", "GPT A", "d", [new AiReasoningOption("low", "fast")], "low", [new AiServiceTierOption("fast", "Fast", null)], "fast", true, null)],
            AiModelDiscovery.Supported,
            Start,
            Start.AddMinutes(1),
            "timeout");

        await store.SaveAsync(catalog, CancellationToken.None);
        await using var reopened = fixture.Reopen();
        var loaded = await new AiModelCatalogStore(reopened).GetAsync(AiModelCatalogKeys.CodexServer, CancellationToken.None);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("gpt-a", loaded.Models.Single().Id);
        Assert.AreEqual("low", loaded.Models.Single().ReasoningEfforts.Single().Effort);
        Assert.AreEqual(Start, loaded.FetchedAt);
        Assert.AreEqual("timeout", loaded.LastError);
        Assert.IsTrue(loaded.IsStale(Start.AddMinutes(2)));
    }

    [TestMethod]
    public async Task OpenAiCompatibleModelsAreDiscoveredFromTheModelsEndpoint()
    {
        var handler = new RouteHandler(request => request.RequestUri!.AbsolutePath == "/v1/models"
            ? Json("""{ "object": "list", "data": [ { "id": "zeta" }, { "id": "alpha", "context_length": 128000 }, { "id": "" } ] }""")
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var provider = Personal(handler);

        var models = await provider.ListModelsAsync(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "alpha", "zeta" }, models.Select(x => x.Id).ToArray());
        Assert.AreEqual(128000, models[0].ContextWindow);
        Assert.AreEqual("Bearer", handler.Last!.Headers.Authorization!.Scheme);
    }

    [TestMethod]
    public async Task ProvidersWithoutAModelsEndpointAreUnsupportedNotBroken()
    {
        var provider = Personal(new RouteHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        await Assert.ThrowsExactlyAsync<AiModelDiscoveryUnsupportedException>(() => provider.ListModelsAsync(CancellationToken.None));

        var html = Personal(new RouteHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html></html>") }));
        await Assert.ThrowsExactlyAsync<AiModelDiscoveryUnsupportedException>(() => html.ListModelsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task OpenAiCompatibleUsageReportsCachedAndReasoningTokensAndHonorsOutputCap()
    {
        var handler = new RouteHandler(_ => Json("""
            { "choices": [ { "message": { "content": "OK" } } ],
              "usage": { "prompt_tokens": 100, "completion_tokens": 20,
                         "prompt_tokens_details": { "cached_tokens": 64 },
                         "completion_tokens_details": { "reasoning_tokens": 8 } } }
            """));
        var provider = Personal(handler, maxOutputTokens: 300);
        var usage = new AiUsageTracker();
        var runner = new AiActivityRunner(new AiActivityTracker(TimeProvider.System), usage, TimeProvider.System);

        await runner.RunAsync(
            new AiActivityStart("alice", AiOperations.NovelTranslation, AiProviderIds.OpenAiCompatible, new AiInvocationOptions("override-model", null, null, 300)),
            50,
            token => provider.TranslateAsync("テキスト", "de", token),
            x => x.Length,
            CancellationToken.None);

        var body = JsonNode.Parse(handler.LastBody!)!;
        Assert.AreEqual("override-model", body["model"]!.GetValue<string>(), "Per-operation model overrides reach the request.");
        Assert.AreEqual(300, body["max_tokens"]!.GetValue<int>());

        var measurement = usage.GetSnapshot("alice").Recent.Single();
        Assert.IsFalse(measurement.Estimated);
        Assert.AreEqual(100, measurement.InputTokens);
        Assert.AreEqual(64, measurement.CachedInputTokens);
        Assert.AreEqual(8, measurement.ReasoningOutputTokens);
        Assert.AreEqual("override-model", measurement.Model);

        var withoutCap = new RouteHandler(_ => Json("""{ "choices": [ { "message": { "content": "OK" } } ] }"""));
        await Personal(withoutCap).TranslateAsync("x", "de", CancellationToken.None);
        Assert.IsNull(JsonNode.Parse(withoutCap.LastBody!)!["max_tokens"], "No cap is sent unless configured.");
    }

    [TestMethod]
    public void ServerOptionsOnlyKeepWhatTheSelectedModelSupports()
    {
        var catalog = new AiModelCatalog(
            AiModelCatalogKeys.CodexServer,
            [
                new AiModelDescriptor("gpt-a", "A", null, [new("low", null), new("medium", null), new("high", null)], "medium", [new("fast", "Fast", null)], null, true, null),
                new AiModelDescriptor("gpt-b", "B", null, [], null, [], null, false, null)
            ],
            AiModelDiscovery.Supported,
            Start,
            Start,
            null);

        var high = AiOptionResolver.ResolveServer(new AiInvocationOptions("gpt-a", "high", "fast", 999), catalog, AiOperations.BookTranslation);
        Assert.AreEqual(new AiInvocationOptions("gpt-a", "high", "fast", null), high);

        var noEfforts = AiOptionResolver.ResolveServer(new AiInvocationOptions("gpt-b", "high", "fast", null), catalog, AiOperations.BookTranslation);
        Assert.AreEqual(new AiInvocationOptions("gpt-b", null, null, null), noEfforts, "Unsupported options are dropped, not guessed.");

        var providerDefault = AiOptionResolver.ResolveServer(AiInvocationOptions.Default, catalog, AiOperations.BookQa);
        Assert.AreEqual("gpt-a", providerDefault.Model, "The catalog default is passed explicitly instead of delegating to an unknown CLI default.");
        Assert.AreEqual("medium", providerDefault.ReasoningEffort);

        var unknownModel = AiOptionResolver.ResolveServer(new AiInvocationOptions("retired", "xhigh", null, null), catalog, AiOperations.SentenceExplanation);
        Assert.AreEqual("gpt-a", unknownModel.Model);
        Assert.AreEqual("low", unknownModel.ReasoningEffort, "A retired model falls back to the known catalog default.");

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            AiOptionResolver.ResolveServer(AiInvocationOptions.Default, AiModelCatalog.Empty("x"), AiOperations.BookQa));

        var manualWithoutCatalog = AiOptionResolver.ResolveServer(
            new AiInvocationOptions("gpt-manual", null, null, null),
            AiModelCatalog.Empty("x"),
            AiOperations.BookQa);
        Assert.AreEqual("gpt-manual", manualWithoutCatalog.Model, "Manual selection still works when discovery is unavailable.");
        Assert.AreEqual("medium", manualWithoutCatalog.ReasoningEffort);

        CollectionAssert.AreEqual(new[] { "low", "medium", "high" }, AiOptionResolver.ReasoningOptions(catalog, null).Select(x => x.Effort).ToArray());
        Assert.AreEqual(0, AiOptionResolver.ReasoningOptions(catalog, "gpt-b").Count);
    }

    [TestMethod]
    public void CapabilitiesForModelOptionsComeFromTheCatalog()
    {
        var catalog = new AiModelCatalog("k", [new AiModelDescriptor("m", "M", null, [new("low", null)], null, [], null, true, null)], AiModelDiscovery.Supported, Start, Start, null);

        var capabilities = AiProviderCapabilities.None(AiTransports.CodexAppServer).WithCatalog(catalog);

        Assert.AreEqual(AiCapabilityState.Supported, capabilities[AiCapability.ReasoningEffort]);
        Assert.AreEqual(AiCapabilityState.Unsupported, capabilities[AiCapability.ServiceTier]);
        Assert.AreEqual(AiCapabilityState.Unknown, AiProviderCapabilities.None("x").WithCatalog(AiModelCatalog.Empty("k"))[AiCapability.ReasoningEffort]);
    }

    [TestMethod]
    public async Task OverridesInheritDefaultsAndPersistOnlyDifferences()
    {
        var root = Path.Combine(Path.GetTempPath(), "jularr-ai-overrides", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "keys"));
        try
        {
            var store = new AiProfileSettingsStore(
                DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "keys"))),
                NullLogger<AiProfileSettingsStore>.Instance,
                new DirectoryInfo(Path.Combine(root, "integrations")));

            await store.SaveAsync("alice", AiProfileSettings.Default with
            {
                Model = "gpt-a",
                ReasoningEffort = "Medium",
                Overrides = AiOperationOverrides.From(
                [
                    KeyValuePair.Create(AiOperations.BookTranslation, new AiOperationOverride("gpt-a", "high")),
                    KeyValuePair.Create(AiOperations.SentenceExplanation, new AiOperationOverride(" ", "medium")),
                    KeyValuePair.Create(AiOperations.NovelMapping, new AiOperationOverride("gpt-mini", null)),
                    KeyValuePair.Create("not-an-operation", new AiOperationOverride("x", "low"))
                ])
            }, CancellationToken.None);

            var loaded = await store.LoadAsync("alice", CancellationToken.None);

            Assert.AreEqual("medium", loaded.ReasoningEffort);
            Assert.AreEqual(2, loaded.Overrides.Count, "Values equal to the defaults and unknown operations are not stored.");
            Assert.AreEqual(new AiOperationOverride(null, "high"), loaded.Overrides.Get(AiOperations.BookTranslation));
            Assert.AreEqual(new AiInvocationOptions("gpt-a", "high", null, null), loaded.Resolve(AiOperations.BookTranslation));
            Assert.AreEqual(new AiInvocationOptions("gpt-mini", "medium", null, null), loaded.Resolve(AiOperations.NovelMapping));
            Assert.AreEqual(new AiInvocationOptions("gpt-a", "medium", null, null), loaded.Resolve(AiOperations.BookQa));

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.SaveAsync(
                "alice",
                AiProfileSettings.Default with { ReasoningEffort = "rm -rf" },
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void PerOperationTuningOverridesInheritSharedDefaults()
    {
        var settings = AiProfileSettings.Default with
        {
            Model = "gpt-a",
            ContextBudgetTokens = 3000,
            MaxRetries = 1,
            TimeoutSeconds = 30,
            Verbosity = "low",
            Overrides = AiOperationOverrides.From(
                [
                    KeyValuePair.Create(
                        AiOperations.BookTranslation,
                        new AiOperationOverride(null, null) { ContextBudgetTokens = 8000, MaxRetries = 3, Verbosity = "high" })
                ],
                defaultContextBudgetTokens: 3000,
                defaultMaxRetries: 1,
                defaultTimeoutSeconds: 30,
                defaultVerbosity: "low")
        };

        var overridden = settings.Resolve(AiOperations.BookTranslation);
        Assert.AreEqual(8000, overridden.ContextBudgetTokens, "The task override replaces the shared default.");
        Assert.AreEqual(3, overridden.MaxRetries);
        Assert.AreEqual(30, overridden.TimeoutSeconds, "A field the task did not override still inherits the shared default.");
        Assert.AreEqual("high", overridden.Verbosity);

        var inherited = settings.Resolve(AiOperations.BookQa);
        Assert.AreEqual(3000, inherited.ContextBudgetTokens);
        Assert.AreEqual(1, inherited.MaxRetries);
        Assert.AreEqual(30, inherited.TimeoutSeconds);
        Assert.AreEqual("low", inherited.Verbosity);

        // A value equal to the shared default is not stored as a difference.
        Assert.AreEqual(1, settings.Overrides.Count);
    }

    [TestMethod]
    public async Task LegacySettingsFilesWithoutNewFieldsStillLoad()
    {
        var root = Path.Combine(Path.GetTempPath(), "jularr-ai-legacy", Guid.NewGuid().ToString("N"));
        var accounts = Path.Combine(root, "integrations", "ai", "accounts");
        Directory.CreateDirectory(accounts);
        Directory.CreateDirectory(Path.Combine(root, "keys"));
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(accounts, "bob.json"),
                """{ "providerId": "server", "baseUrl": null, "model": null, "protectedApiKey": null, "translationMode": 1 }""");
            var store = new AiProfileSettingsStore(
                DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "keys"))),
                NullLogger<AiProfileSettingsStore>.Instance,
                new DirectoryInfo(Path.Combine(root, "integrations")));

            var loaded = await store.LoadAsync("bob", CancellationToken.None);

            Assert.AreEqual(AiTranslationMode.Quality, loaded.TranslationMode);
            Assert.AreEqual(AiOperationOverrides.Empty, loaded.Overrides);
            Assert.IsNull(loaded.ReasoningEffort);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task UsageAggregatesSurviveRestartAndSeparateExactFromEstimated()
    {
        await using var fixture = await Fixture.CreateAsync();
        var store = new AiUsageStore(fixture.Db);
        var today = DateOnly.FromDateTime(Start.UtcDateTime);

        await store.AddAsync(
        [
            ("alice", Measurement(Start, AiOperations.BookTranslation, "gpt-a", 1000, 200, estimated: false) with { CachedInputTokens = 600, ReasoningOutputTokens = 30, ContextTokens = 250, FullContextTokens = 900, DurationMs = 1500 }),
            ("alice", Measurement(Start.AddMinutes(1), AiOperations.BookTranslation, "gpt-a", 500, 100, estimated: false) with { Retries = 1 }),
            ("alice", Measurement(Start, AiOperations.SentenceExplanation, null, 40, 10, estimated: true)),
            ("alice", Measurement(Start.AddDays(-3), AiOperations.BookQa, "gpt-a", 10, 1, estimated: false) with { Outcome = AiUsageOutcome.Failed }),
            ("alice", Measurement(Start, AiOperations.BookTranslation, null, 0, 0, estimated: false) with { }),
            ("bob", Measurement(Start, AiOperations.BookTranslation, "gpt-a", 7, 7, estimated: false))
        ], CancellationToken.None);
        await store.AddAsync([("alice", new AiUsageMeasurement(Start, AiOperations.BookTranslation, "cache", null, 0, 0, 0, 0, false, CacheHit: true, ResumedChunk: false))], CancellationToken.None);

        await using var reopened = fixture.Reopen();
        var report = await new AiUsageStore(reopened).GetReportAsync("alice", today, AiUsagePeriod.Today, CancellationToken.None);

        Assert.AreEqual(4, report.Today.Requests);
        Assert.AreEqual(1500, report.Today.InputTokens, "Exact tokens only.");
        Assert.AreEqual(40, report.Today.EstimatedInputTokens);
        Assert.AreEqual(1, report.Today.EstimatedRequests);
        Assert.AreEqual(600, report.Today.CachedInputTokens);
        Assert.AreEqual(30, report.Today.ReasoningOutputTokens);
        Assert.AreEqual(250, report.Today.ContextTokens);
        Assert.AreEqual(900, report.Today.FullContextTokens, "Context before compaction survives restarts too.");
        Assert.AreEqual(1, report.Today.Retries);
        Assert.AreEqual(1, report.Today.CacheHits);
        Assert.IsTrue(report.Today.HasEstimates);
        Assert.AreEqual(5, report.Last7Days.Requests);
        Assert.AreEqual(1, report.Last7Days.Failures);

        var byOperation = report.ByOperation.ToDictionary(x => x.Key);
        Assert.AreEqual(3, byOperation[AiOperations.BookTranslation].Totals.Requests);
        Assert.IsTrue(report.ByModel.Any(x => x.Key.EndsWith("gpt-a", StringComparison.Ordinal)));
        Assert.IsFalse(report.ByProfile.Any(x => x.Key == "bob"), "A profile only sees its own usage.");

        var all = await new AiUsageStore(reopened).GetReportAsync(null, today, AiUsagePeriod.Last30Days, CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "alice", "bob" }, all.ByProfile.Select(x => x.Key).ToArray());
    }

    [TestMethod]
    public async Task RunnerRecordsExactUsageWhenReportedAndEstimatesOtherwise()
    {
        var usage = new AiUsageTracker();
        var tracker = new AiActivityTracker(TimeProvider.System);
        var runner = new AiActivityRunner(tracker, usage, TimeProvider.System);
        var start = new AiActivityStart("alice", AiOperations.BookEdit, "codex-cli", AiInvocationOptions.Default, 100, 40);

        await runner.RunAsync(start, 800, _ =>
        {
            AiActivityScope.Current!.ReportUsage(new AiTokenUsage(900, 300, 50, 5, false), 10_000);
            return Task.FromResult("done");
        }, x => x.Length, CancellationToken.None);
        await runner.RunAsync(start, 800, _ => Task.FromResult("done!"), x => x.Length, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            runner.RunAsync<string>(start, 800, _ => throw new InvalidOperationException("boom"), x => x.Length, CancellationToken.None));

        var recent = usage.GetSnapshot("alice").Recent.Reverse().ToArray();
        Assert.IsFalse(recent[0].Estimated);
        Assert.AreEqual(900, recent[0].InputTokens);
        Assert.AreEqual(300, recent[0].CachedInputTokens);
        Assert.AreEqual(40, recent[0].ContextTokens);
        Assert.IsTrue(recent[1].Estimated);
        Assert.AreEqual(AiUsageTracker.EstimateTokens(800), recent[1].InputTokens);
        Assert.AreEqual(AiUsageOutcome.Failed, recent[2].Outcome);
        Assert.AreEqual(0, recent[2].InputTokens, "Failed requests are never given guessed token counts.");

        var activities = tracker.List("alice");
        Assert.AreEqual(3, activities.Count);
        Assert.AreEqual(AiActivityState.Failed, activities[0].State);
        Assert.AreEqual("boom", activities[0].Error);
        Assert.AreEqual(AiActivityState.Completed, activities[2].State);
        Assert.AreEqual(9d, activities[2].ContextWindowPercent);
    }

    [TestMethod]
    public async Task WorkScopeAddsPartProgressAndContextBeforeCompaction()
    {
        var usage = new AiUsageTracker();
        var tracker = new AiActivityTracker(TimeProvider.System);
        var runner = new AiActivityRunner(tracker, usage, TimeProvider.System);
        var start = new AiActivityStart("alice", AiOperations.BookTranslation, "codex-cli", AiInvocationOptions.Default, 100, 40);

        AiActivitySnapshot? during = null;
        using (AiWorkScope.Enter(3, 7, 4000))
        {
            await runner.RunAsync(start, 800, _ =>
            {
                during = AiActivityScope.Current!.Snapshot;
                return Task.FromResult("done");
            }, x => x.Length, CancellationToken.None);
        }

        await runner.RunAsync(start, 800, _ => Task.FromResult("done"), x => x.Length, CancellationToken.None);

        Assert.AreEqual(3, during!.ProgressCurrent);
        Assert.AreEqual(7, during.ProgressTotal);
        Assert.AreEqual(1000, during.FullContextTokens);
        Assert.IsNull(AiWorkScope.Current, "The scope ends with its block.");

        var recent = usage.GetSnapshot("alice").Recent.Reverse().ToArray();
        Assert.AreEqual(40, recent[0].ContextTokens);
        Assert.AreEqual(1000, recent[0].FullContextTokens);
        Assert.AreEqual(40, recent[1].FullContextTokens, "Without a scope the sent context is all that is known.");
        Assert.IsNull(tracker.List("alice")[0].ProgressTotal);
    }

    [TestMethod]
    public async Task ChapterArtworkImagesRunAsTrackedActivitiesWithOneUsageRecord()
    {
        await using var fixture = await Fixture.CreateAsync();
        var root = Path.Combine(Path.GetTempPath(), "jularr-ai-images", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new AiProfileSettingsStore(
                new EphemeralDataProtectionProvider(),
                NullLogger<AiProfileSettingsStore>.Instance,
                new DirectoryInfo(root));
            await settings.SaveAsync(
                "alice",
                new AiProfileSettings(AiProviderIds.OpenAiCompatible, "https://api.example.invalid/v1", "text-model", "test-key", AiTranslationMode.Efficient)
                {
                    ImageModel = "image-model"
                },
                CancellationToken.None);
            byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
            var handler = new RouteHandler(_ => Json($$"""{"data":[{"b64_json":"{{Convert.ToBase64String(png)}}"}]}"""));
            var usage = new AiUsageTracker();
            var tracker = new AiActivityTracker(TimeProvider.System);
            var router = new ProfileAiImageRouter(
                settings,
                new SingleClientFactory(handler),
                new AiActivityRunner(tracker, usage, TimeProvider.System),
                new AiUsageStore(fixture.Db),
                TimeProvider.System);

            await router.GenerateAsync(
                "alice",
                new AiImageRequest(AiOperations.ChapterArtwork, "A misty harbor.", AiImageLayout.Landscape, AiImageQuality.High, 1),
                CancellationToken.None);

            var activity = tracker.List("alice").Single();
            Assert.AreEqual(AiOperations.ChapterArtwork, activity.Operation);
            Assert.AreEqual(AiActivityState.Completed, activity.State);
            Assert.AreEqual("image-model", activity.Model);
            var measurement = usage.GetSnapshot("alice").Recent.Single();
            Assert.AreEqual(AiOperations.ChapterArtwork, measurement.Operation);
            Assert.IsTrue(measurement.Estimated);
            Assert.IsTrue(AiOperations.IsKnown(AiOperations.ChapterArtwork));
            Assert.IsFalse(AiOperations.AcceptsOverride(AiOperations.ChapterArtwork), "Images use the separate image model.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ParallelLimitKeepsFurtherRequestsQueuedAndCancellable()
    {
        var usage = new AiUsageTracker();
        var tracker = new AiActivityTracker(TimeProvider.System);
        var runner = new AiActivityRunner(tracker, usage, TimeProvider.System);
        var start = new AiActivityStart("alice", AiOperations.BookTranslation, "codex-cli", AiInvocationOptions.Default) { ConcurrencyLimit = 1 };
        var release = new TaskCompletionSource();

        var first = runner.RunAsync(start, 10, async _ => { await release.Task; return "one"; }, x => x.Length, CancellationToken.None);
        var second = runner.RunAsync(start, 10, _ => Task.FromResult("two"), x => x.Length, CancellationToken.None);
        var third = runner.RunAsync(start, 10, _ => Task.FromResult("three"), x => x.Length, CancellationToken.None);
        var other = await runner.RunAsync(start with { ProfileId = "bob" }, 10, _ => Task.FromResult("bob"), x => x.Length, CancellationToken.None);

        Assert.AreEqual("bob", other, "Limits are per profile.");
        var waiting = tracker.List("alice").Where(x => x.IsActive).ToArray();
        Assert.AreEqual(1, waiting.Count(x => x.State == AiActivityState.Running));
        Assert.AreEqual(2, waiting.Count(x => x.State == AiActivityState.Queued));

        var queuedThird = waiting.Last(x => x.State == AiActivityState.Queued);
        Assert.AreEqual(AiActivityCancelResult.Cancelled, tracker.Cancel(queuedThird.Id, "alice", isOwner: false));
        await Assert.ThrowsAsync<OperationCanceledException>(() => third);

        release.SetResult();
        Assert.AreEqual("one", await first);
        Assert.AreEqual("two", await second);
        Assert.AreEqual(AiActivityState.Cancelled, tracker.List("alice").Single(x => x.Id == queuedThird.Id).State);
    }

    [TestMethod]
    public void BudgetStatusSeparatesOkWarningAndReached()
    {
        var settings = AiProfileSettings.Default with { DailyTokenBudget = 1000 };
        AiUsageTotals Used(long input, long estimatedOutput) =>
            AiUsageTotals.Zero with { InputTokens = input, EstimatedOutputTokens = estimatedOutput };

        Assert.AreEqual(AiBudgetState.Unlimited, AiBudgetStatus.For(AiProfileSettings.Default, Used(5000, 0)).State);
        Assert.AreEqual(AiBudgetState.Ok, AiBudgetStatus.For(settings, Used(700, 99)).State);
        Assert.AreEqual(AiBudgetState.Warning, AiBudgetStatus.For(settings, Used(700, 100)).State, "Estimates count against the limit.");
        Assert.AreEqual(AiBudgetState.Reached, AiBudgetStatus.For(settings, Used(1000, 0)).State);
        Assert.AreEqual(AiBudgetState.Warning, AiBudgetStatus.For(settings with { BudgetWarningPercent = 50 }, Used(500, 0)).State);
        Assert.AreEqual(100, AiBudgetStatus.For(settings, Used(4000, 0)).Percent);
    }

    [TestMethod]
    public void SessionBudgetStatusSeparatesOkAndReached()
    {
        var settings = AiProfileSettings.Default with { SessionTokenBudget = 1000 };
        AiUsageSnapshot Snapshot(long input, long output) => new(0, input, output, 0, 0, []);

        Assert.IsFalse(AiSessionBudgetStatus.For(AiProfileSettings.Default, Snapshot(5000, 0)).IsReached, "No session limit means no reached state.");
        Assert.IsFalse(AiSessionBudgetStatus.For(settings, Snapshot(600, 399)).IsReached);
        Assert.IsTrue(AiSessionBudgetStatus.For(settings, Snapshot(600, 400)).IsReached);
        Assert.AreEqual(100, AiSessionBudgetStatus.For(settings, Snapshot(4000, 0)).Percent);
    }

    [TestMethod]
    public void FindLighterModelUsesOnlyKnownContextWindowsAndNeverGuesses()
    {
        var withWindows = new AiModelCatalog(
            "key",
            [
                new AiModelDescriptor("heavy", "Heavy", null, [], null, [], null, false, 200_000),
                new AiModelDescriptor("medium", "Medium", null, [], null, [], null, false, 50_000),
                new AiModelDescriptor("light", "Light", null, [], null, [], null, false, 8_000),
                new AiModelDescriptor("unknown-size", "Unknown", null, [], null, [], null, false, null)
            ],
            AiModelDiscovery.Supported,
            null,
            null,
            null);

        Assert.IsTrue(AiOptionResolver.CanSuggestLighterModel(withWindows));
        Assert.AreEqual("light", AiOptionResolver.FindLighterModel(withWindows, "heavy")!.Id, "The smallest known window lighter than the current model is chosen.");
        Assert.AreEqual("light", AiOptionResolver.FindLighterModel(withWindows, "medium")!.Id);
        Assert.IsNull(AiOptionResolver.FindLighterModel(withWindows, "light"), "The lightest known model has nothing lighter to fall back to.");
        Assert.IsNull(AiOptionResolver.FindLighterModel(withWindows, "unknown-size"), "A model without a known window is never used to judge lighter alternatives.");
        Assert.IsNull(AiOptionResolver.FindLighterModel(withWindows, null));

        var noWindowData = new AiModelCatalog(
            "key",
            [AiModelDescriptor.Basic("a"), AiModelDescriptor.Basic("b")],
            AiModelDiscovery.Supported,
            null,
            null,
            null);
        Assert.IsFalse(AiOptionResolver.CanSuggestLighterModel(noWindowData), "Catalogs with no context-window metadata can never suggest a lighter model.");
        Assert.IsNull(AiOptionResolver.FindLighterModel(noWindowData, "a"));
    }

    [TestMethod]
    public async Task LimitsPersistAndOutOfRangeValuesAreRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "jularr-ai-limits", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new AiProfileSettingsStore(
                new EphemeralDataProtectionProvider(),
                NullLogger<AiProfileSettingsStore>.Instance,
                new DirectoryInfo(root));
            await store.SaveAsync(
                "alice",
                AiProfileSettings.Default with
                {
                    DailyTokenBudget = 50_000,
                    BudgetWarningPercent = 90,
                    MaxConcurrentJobs = 2,
                    SessionTokenBudget = 20_000,
                    ContextBudgetTokens = 4_000,
                    MaxRetries = 2,
                    TimeoutSeconds = 60,
                    Verbosity = "high",
                    FallbackModelEnabled = true
                },
                CancellationToken.None);

            var loaded = await store.LoadAsync("alice", CancellationToken.None);
            Assert.AreEqual(50_000, loaded.DailyTokenBudget);
            Assert.AreEqual(90, loaded.EffectiveWarningPercent);
            Assert.AreEqual(2, loaded.MaxConcurrentJobs);
            Assert.AreEqual(20_000, loaded.SessionTokenBudget);
            Assert.AreEqual(4_000, loaded.ContextBudgetTokens);
            Assert.AreEqual(2, loaded.MaxRetries);
            Assert.AreEqual(60, loaded.TimeoutSeconds);
            Assert.AreEqual("high", loaded.Verbosity);
            Assert.IsTrue(loaded.FallbackModelEnabled);

            foreach (var invalid in new[]
            {
                AiProfileSettings.Default with { DailyTokenBudget = 0 },
                AiProfileSettings.Default with { BudgetWarningPercent = 100 },
                AiProfileSettings.Default with { MaxConcurrentJobs = AiProfileSettings.MaxConcurrentJobsLimit + 1 },
                AiProfileSettings.Default with { SessionTokenBudget = 0 },
                AiProfileSettings.Default with { ContextBudgetTokens = AiProfileSettings.MaxContextBudgetTokens + 1 },
                AiProfileSettings.Default with { MaxRetries = AiProfileSettings.MaxRetriesLimit + 1 },
                AiProfileSettings.Default with { MaxRetries = -1 },
                AiProfileSettings.Default with { TimeoutSeconds = AiProfileSettings.MinTimeoutSeconds - 1 },
                AiProfileSettings.Default with { TimeoutSeconds = AiProfileSettings.MaxTimeoutSeconds + 1 }
            })
            {
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.SaveAsync("alice", invalid, CancellationToken.None));
            }

            // Per-task output caps are OpenAI-compatible only; the server provider strips them even
            // when a client sends one, and verbosity is Codex-only so a personal provider never keeps it.
            var serverWithBadOverride = AiProfileSettings.Default with
            {
                Overrides = AiOperationOverrides.From(
                [
                    KeyValuePair.Create(AiOperations.BookTranslation, new AiOperationOverride(null, null) { MaxOutputTokens = 500 })
                ])
            };
            await store.SaveAsync("alice", serverWithBadOverride, CancellationToken.None);
            var reloadedServer = await store.LoadAsync("alice", CancellationToken.None);
            Assert.IsNull(reloadedServer.Overrides.Get(AiOperations.BookTranslation)?.MaxOutputTokens, "Server profiles never keep a per-task output cap.");

            var personalWithBadOverride = new AiProfileSettings(AiProviderIds.OpenAiCompatible, "https://api.example.invalid/v1", "model", "secret", AiTranslationMode.Efficient)
            {
                Overrides = AiOperationOverrides.From(
                [
                    KeyValuePair.Create(AiOperations.BookTranslation, new AiOperationOverride(null, null) { Verbosity = "high" })
                ])
            };
            await store.SaveAsync("alice", personalWithBadOverride, CancellationToken.None);
            var reloadedPersonal = await store.LoadAsync("alice", CancellationToken.None);
            Assert.IsNull(reloadedPersonal.Overrides.Get(AiOperations.BookTranslation)?.Verbosity, "Personal providers never keep a per-task verbosity override.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ActivityStateTransitionsArePublished()
    {
        var tracker = new AiActivityTracker(TimeProvider.System);
        var version = tracker.Version;
        var changed = tracker.WaitForChangeAsync(version, CancellationToken.None);

        using var handle = tracker.Start(new AiActivityStart("alice", AiOperations.BookQa, "codex-cli", AiInvocationOptions.Default), CancellationToken.None);
        await changed.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(AiActivityState.Queued, tracker.List("alice").Single().State);

        handle.SetState(AiActivityState.Running);
        handle.ReportRetry();
        Assert.AreEqual(AiActivityState.Retrying, handle.Snapshot.State);
        handle.SetState(AiActivityState.Running);
        handle.ReportProgress(2, 5);
        handle.Complete();
        handle.SetState(AiActivityState.Running);

        var finished = tracker.List("alice").Single();
        Assert.AreEqual(AiActivityState.Completed, finished.State, "Finished requests do not change state again.");
        Assert.AreEqual(1, finished.Retries);
        Assert.AreEqual(5, finished.ProgressTotal);
        Assert.IsNotNull(finished.CompletedAt);
        Assert.IsTrue(tracker.Version > version + 3);
        Assert.AreEqual(0, tracker.List("bob").Count);
    }

    [TestMethod]
    public async Task CancellationRespectsProfileBoundaries()
    {
        var tracker = new AiActivityTracker(TimeProvider.System);
        var runner = new AiActivityRunner(tracker, new AiUsageTracker(), TimeProvider.System);
        var started = new TaskCompletionSource();

        var run = runner.RunAsync(
            new AiActivityStart("alice", AiOperations.BookTranslation, "codex-cli", AiInvocationOptions.Default),
            10,
            async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return "never";
            },
            x => x.Length,
            CancellationToken.None);
        await started.Task;
        var id = tracker.List("alice").Single().Id;

        Assert.AreEqual(AiActivityCancelResult.NotFound, tracker.Cancel(id, "bob", isOwner: false), "Other profiles cannot see or cancel it.");
        Assert.AreEqual(AiActivityCancelResult.NotFound, tracker.Cancel(Guid.NewGuid(), "alice", isOwner: false));
        Assert.AreEqual(AiActivityCancelResult.Cancelled, tracker.Cancel(id, "owner", isOwner: true));

        await Assert.ThrowsAsync<OperationCanceledException>(() => run);
        Assert.AreEqual(AiActivityState.Cancelled, tracker.List("alice").Single().State);
        Assert.AreEqual(AiActivityCancelResult.NotActive, tracker.Cancel(id, "alice", isOwner: false));
    }

    [TestMethod]
    public void OnlyTheOwnerMayWatchAllProfiles()
    {
        Assert.IsFalse(AiActivityEndpoints.TryResolveScope("all", Account("alice", owner: false), out _));
        Assert.IsTrue(AiActivityEndpoints.TryResolveScope(null, Account("alice", owner: false), out var own));
        Assert.AreEqual("alice", own);
        Assert.IsTrue(AiActivityEndpoints.TryResolveScope("all", Account("owner", owner: true), out var all));
        Assert.IsNull(all);

        var snapshot = new AiActivityTracker(TimeProvider.System)
            .Start(new AiActivityStart("alice", AiOperations.BookQa, "codex-cli", AiInvocationOptions.Default), CancellationToken.None)
            .Snapshot;
        Assert.IsNull(AiActivityDto.From(snapshot, includeProfile: false, Start).ProfileId);
        Assert.AreEqual("alice", AiActivityDto.From(snapshot, includeProfile: true, Start).ProfileId);
    }

    [TestMethod]
    public void QuotaMergeAddsUnknownBucketsWithoutGuessingModels()
    {
        var snapshot = new AiQuotaSnapshot(
            true,
            [new AiQuotaBucket("codex", "Codex", null, new AiQuotaWindow(10, 300, null), null, null, null, null, "plus", null)],
            Start,
            null);

        var merged = snapshot
            .Merge(new AiQuotaBucket(null, null, null, new AiQuotaWindow(20, 300, null), null, null, null, null, null, null), Start.AddMinutes(1))
            .Merge(new AiQuotaBucket("other", null, null, null, new AiQuotaWindow(3, 10080, null), null, null, null, null, null), Start.AddMinutes(2));

        Assert.AreEqual(2, merged.Buckets.Count);
        Assert.AreEqual(20, merged.Buckets[0].Primary!.UsedPercent);
        Assert.AreEqual("plus", merged.Buckets[0].PlanType);
        Assert.IsNull(merged.Buckets[1].ModelSlug);
        Assert.AreEqual(Start.AddMinutes(2), merged.UpdatedAt);
        Assert.AreEqual(80, merged.Buckets[0].Primary!.RemainingPercent);
    }

    [TestMethod]
    public void ErrorsAreSanitizedBeforeTheyAreShownOrStored()
    {
        var clean = AiErrorSanitizer.Sanitize("HTTP 401 Bearer abc.def-ghi api_key=sk-1234567890abcdef https://x.invalid/v1?token=secret\n trailing");

        Assert.IsNotNull(clean);
        Assert.IsFalse(clean.Contains("abc.def-ghi", StringComparison.Ordinal));
        Assert.IsFalse(clean.Contains("sk-1234567890abcdef", StringComparison.Ordinal));
        Assert.IsFalse(clean.Contains("token=secret", StringComparison.Ordinal));
        Assert.IsFalse(clean.Contains('\n'));
        Assert.IsTrue(AiErrorSanitizer.Sanitize(new string('x', 1000))!.Length <= 301);
    }

    [TestMethod]
    public void EveryDynamicAiUiKeyExists()
    {
        var keys = AiOperations.All.Select(x => "ai.operation." + x)
            .Concat(AiActivityStates.All.Select(x => "ai.state." + AiActivityStates.Name(x)))
            .Concat(Enum.GetValues<AiCapability>().Select(x => "ai.capability." + char.ToLowerInvariant(x.ToString()[0]) + x.ToString()[1..]))
            .Concat(Enum.GetValues<AiCapabilityState>().Select(x => "ai.capabilityState." + x.ToString().ToLowerInvariant()));

        var missing = keys.Where(key => !UiTranslationResources.TryGet(key, out _)).ToArray();

        Assert.AreEqual(0, missing.Length, string.Join(", ", missing));
    }

    private static AiUsageMeasurement Measurement(
        DateTimeOffset timestamp,
        string operation,
        string? model,
        int input,
        int output,
        bool estimated) =>
        new(timestamp, operation, "codex-cli", model, input * 4, output * 4, input, output, estimated, CacheHit: false, ResumedChunk: false);

    private static OpenAiCompatibleProvider Personal(RouteHandler handler, int? maxOutputTokens = null) =>
        new(
            new HttpClient(handler),
            new AiProfileSettings(AiProviderIds.OpenAiCompatible, "https://api.example.invalid", "test-model", "test-secret", AiTranslationMode.Efficient)
            {
                MaxOutputTokens = maxOutputTokens
            });

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static CurrentAccountContext Account(string profileId, bool owner)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, profileId) };
        if (owner)
        {
            claims.Add(new Claim(ClaimTypes.Role, AccountRoles.Owner));
        }

        return new CurrentAccountContext(new FixedAccessor(
            new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) }));
    }

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta) => now += delta;
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FixedAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class RouteHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private sealed class MemoryCatalogStore : IAiModelCatalogStore
    {
        private readonly Dictionary<string, AiModelCatalog> catalogs = new(StringComparer.Ordinal);

        public Task<AiModelCatalog?> GetAsync(string providerKey, CancellationToken cancellationToken) =>
            Task.FromResult(catalogs.GetValueOrDefault(providerKey));

        public Task SaveAsync(AiModelCatalog catalog, CancellationToken cancellationToken)
        {
            catalogs[catalog.ProviderKey] = catalog;
            return Task.CompletedTask;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory;

        private Fixture(string directory, AppDbContext db)
        {
            this.directory = directory;
            Db = db;
        }

        public AppDbContext Db { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"jularr-ai-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var fixture = new Fixture(directory, Open(directory));
            await DatabaseMigrationBridge.UpgradeAsync(fixture.Db);
            return fixture;
        }

        /// <summary>A second context on the same file, as after a restart.</summary>
        public AppDbContext Reopen() => Open(directory);

        private static AppDbContext Open(string directory) =>
            new(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "app.db")};Pooling=False")
                .Options);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
