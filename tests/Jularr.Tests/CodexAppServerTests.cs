using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Jularr.Web.Features.Ai;
using Jularr.Web.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jularr.Tests;

/// <summary>Codex app-server v2 integration against a scripted in-memory server (no codex binary).</summary>
[TestClass]
public sealed class CodexAppServerTests
{
    [TestMethod]
    public async Task InitializesAndDetectsCapabilitiesFromResponses()
    {
        var server = new FakeCodexServer();
        server.Handle("model/list", request =>
            request["params"]?["cursor"]?.GetValue<string>() is null
                ? Result(new JsonObject
                {
                    ["data"] = new JsonArray(
                        Model("gpt-a", "GPT A", isDefault: true, efforts: ["low", "medium", "high"], tiers: ["fast"]),
                        Model("hidden-model", "Hidden", hidden: true)),
                    ["nextCursor"] = "page-2"
                })
                : Result(new JsonObject
                {
                    ["data"] = new JsonArray(Model("gpt-b", "GPT B", efforts: [])),
                    ["nextCursor"] = null
                }));
        server.Handle("account/rateLimits/read", _ => MethodNotFound());
        await using var fixture = new Fixture(server);

        var models = await fixture.Gateway.ListModelsAsync(CancellationToken.None);
        await Assert.ThrowsExactlyAsync<CodexAppServerException>(
            () => fixture.Gateway.ReadRateLimitsAsync(CancellationToken.None));

        CollectionAssert.AreEqual(new[] { "gpt-a", "gpt-b" }, models.Select(x => x.Id).ToArray());
        Assert.IsTrue(models[0].IsDefault);
        CollectionAssert.AreEqual(new[] { "low", "medium", "high" }, models[0].ReasoningEfforts.Select(x => x.Effort).ToArray());
        Assert.AreEqual("fast", models[0].ServiceTiers.Single().Id);
        Assert.AreEqual(0, models[1].ReasoningEfforts.Count);

        Assert.AreEqual("initialize", server.Received.First()["method"]!.GetValue<string>());
        Assert.IsTrue(server.Received.Any(x => x["method"]?.GetValue<string>() == "initialized" && x["id"] is null));

        var capabilities = fixture.Gateway.GetCapabilities(execAvailable: true);
        Assert.AreEqual(AiTransports.CodexAppServer, capabilities.Transport);
        Assert.AreEqual(AiCapabilityState.Supported, capabilities[AiCapability.ModelCatalog]);
        Assert.AreEqual(AiCapabilityState.Unsupported, capabilities[AiCapability.RateLimits]);
        Assert.AreEqual(AiCapabilityState.Unknown, capabilities[AiCapability.AccountStatus]);
        Assert.AreEqual(AiCapabilityState.Unsupported, capabilities[AiCapability.MaxOutputTokens]);
    }

    [TestMethod]
    public async Task UnavailableAppServerReportsModelDiscoveryAsUnsupported()
    {
        await using var fixture = new Fixture(launcher: new FailingLauncher());
        var provider = new CodexCliProvider(fixture.Gateway);

        Assert.IsFalse(await fixture.Gateway.IsAvailableAsync(CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AiModelDiscoveryUnsupportedException>(
            () => provider.ListModelsAsync(CancellationToken.None));

        var capabilities = fixture.Gateway.GetCapabilities(execAvailable: true);
        Assert.AreEqual(AiTransports.CodexExec, capabilities.Transport);
        Assert.AreEqual(AiCapabilityState.Unsupported, capabilities[AiCapability.ModelCatalog]);
        Assert.AreEqual(AiCapabilityState.Supported, capabilities[AiCapability.TokenUsage], "codex exec --json reports usage.");
    }

    [TestMethod]
    public async Task JobsWithoutAConcreteModelNeverReachCodex()
    {
        var server = new FakeCodexServer();
        await using var fixture = new Fixture(server);
        var provider = new CodexCliProvider(fixture.Gateway);

        // No activity (a direct caller) and an activity without a model: both are refused before
        // anything starts, so neither thread/start nor codex exec can pick an implicit default.
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => provider.TranslateAsync("こんにちは", "de", CancellationToken.None));

        var tracker = new AiActivityTracker(TimeProvider.System);
        using var activity = tracker.Start(
            new AiActivityStart("alice", AiOperations.NovelTranslation, "codex-cli", new AiInvocationOptions(null, "low", null, null)),
            CancellationToken.None);
        using (AiActivityScope.Enter(activity))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                () => provider.TranslateAsync("こんにちは", "de", CancellationToken.None));
        }

        Assert.IsFalse(server.Received.Any(x => x["method"]?.GetValue<string>() == "thread/start"));
    }

    [TestMethod]
    public async Task TurnRunsLeastPrivilegedAndReportsExactTokenUsage()
    {
        var server = new FakeCodexServer();
        server.Handle("thread/start", _ => Result(new JsonObject
        {
            ["thread"] = new JsonObject { ["id"] = "thread-1" },
            ["model"] = "gpt-a",
            ["reasoningEffort"] = "low"
        }));
        server.Handle("turn/start", _ =>
        {
            server.Push("turn/started", new JsonObject { ["threadId"] = "thread-1", ["turn"] = new JsonObject { ["id"] = "turn-1" } });
            server.Push("item/completed", new JsonObject
            {
                ["threadId"] = "thread-1",
                ["turnId"] = "turn-1",
                ["item"] = new JsonObject { ["type"] = "agentMessage", ["id"] = "i1", ["text"] = "{\"translation\":\"Hallo\"}" }
            });
            server.Push("thread/tokenUsage/updated", new JsonObject
            {
                ["threadId"] = "thread-1",
                ["turnId"] = "turn-1",
                ["tokenUsage"] = new JsonObject
                {
                    ["total"] = Usage(1200, 800, 40, 12),
                    ["last"] = Usage(1200, 800, 40, 12),
                    ["modelContextWindow"] = 200000
                }
            });
            server.Push("turn/completed", new JsonObject
            {
                ["threadId"] = "thread-1",
                ["turn"] = new JsonObject { ["id"] = "turn-1", ["status"] = "completed", ["items"] = new JsonArray() }
            });
            return Result(new JsonObject { ["turn"] = new JsonObject { ["id"] = "turn-1", ["status"] = "inProgress" } });
        });
        await using var fixture = new Fixture(server);
        var tracker = new AiActivityTracker(TimeProvider.System);
        using var activity = tracker.Start(
            new AiActivityStart("alice", AiOperations.BookTranslation, "codex-cli", new AiInvocationOptions("gpt-a", "low", null, null)),
            CancellationToken.None);

        var result = await fixture.Gateway.RunTurnAsync(
            new CodexTurnRequest("prompt", """{"type":"object"}""", "/tmp", "gpt-a", "low", null, TimeSpan.FromSeconds(10)),
            activity,
            activity.Token);

        Assert.AreEqual("{\"translation\":\"Hallo\"}", result.Text);
        Assert.AreEqual(new AiTokenUsage(1200, 800, 40, 12, Estimated: false), result.Usage);
        Assert.AreEqual(200000, result.ContextWindow);

        var snapshot = activity.Snapshot;
        Assert.AreEqual(AiTransports.CodexAppServer, snapshot.Transport);
        Assert.AreEqual(AiActivityState.Running, snapshot.State);
        Assert.AreEqual(0.6, snapshot.ContextWindowPercent!.Value, 0.001);

        var threadStart = server.Received.Single(x => x["method"]?.GetValue<string>() == "thread/start")["params"]!;
        Assert.AreEqual("gpt-a", threadStart["model"]!.GetValue<string>(), "The selected model is sent explicitly.");
        Assert.AreEqual("read-only", threadStart["sandbox"]!.GetValue<string>());
        Assert.AreEqual("never", threadStart["approvalPolicy"]!.GetValue<string>());
        Assert.IsTrue(threadStart["ephemeral"]!.GetValue<bool>());
        Assert.IsFalse(threadStart["config"]!["features.shell_tool"]!.GetValue<bool>());
        Assert.IsFalse(threadStart["config"]!["features.standalone_web_search"]!.GetValue<bool>());
        Assert.IsFalse(threadStart["config"]!["features.plugins"]!.GetValue<bool>());

        var turnStart = server.Received.Single(x => x["method"]?.GetValue<string>() == "turn/start")["params"]!;
        Assert.AreEqual("low", turnStart["effort"]!.GetValue<string>());
        Assert.AreEqual("object", turnStart["outputSchema"]!["type"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task CancellingARunningTurnInterruptsIt()
    {
        var server = new FakeCodexServer();
        server.Handle("thread/start", _ => Result(new JsonObject { ["thread"] = new JsonObject { ["id"] = "thread-9" } }));
        server.Handle("turn/start", _ => Result(new JsonObject { ["turn"] = new JsonObject { ["id"] = "turn-9" } }));
        server.Handle("turn/interrupt", _ =>
        {
            server.Push("turn/completed", new JsonObject
            {
                ["threadId"] = "thread-9",
                ["turn"] = new JsonObject { ["id"] = "turn-9", ["status"] = "interrupted" }
            });
            return Result(new JsonObject());
        });
        await using var fixture = new Fixture(server);
        using var cancellation = new CancellationTokenSource();

        var run = fixture.Gateway.RunTurnAsync(
            new CodexTurnRequest("prompt", "{}", "/tmp", null, null, null, TimeSpan.FromMinutes(1)),
            null,
            cancellation.Token);
        await server.WaitForAsync("turn/start");
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => run);
        var interrupt = server.Received.Single(x => x["method"]?.GetValue<string>() == "turn/interrupt")["params"]!;
        Assert.AreEqual("thread-9", interrupt["threadId"]!.GetValue<string>());
        Assert.AreEqual("turn-9", interrupt["turnId"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task FailedTurnSurfacesASanitizedError()
    {
        var server = new FakeCodexServer();
        server.Handle("thread/start", _ => Result(new JsonObject { ["thread"] = new JsonObject { ["id"] = "t" } }));
        server.Handle("turn/start", _ =>
        {
            server.Push("error", new JsonObject { ["threadId"] = "t", ["turnId"] = "u", ["willRetry"] = true, ["error"] = new JsonObject { ["message"] = "overloaded" } });
            server.Push("turn/completed", new JsonObject
            {
                ["threadId"] = "t",
                ["turn"] = new JsonObject
                {
                    ["id"] = "u",
                    ["status"] = "failed",
                    ["error"] = new JsonObject { ["message"] = "Unauthorized: Bearer sk-secretsecretsecret" }
                }
            });
            return Result(new JsonObject { ["turn"] = new JsonObject { ["id"] = "u" } });
        });
        await using var fixture = new Fixture(server);
        var tracker = new AiActivityTracker(TimeProvider.System);
        using var activity = tracker.Start(
            new AiActivityStart("alice", AiOperations.BookQa, "codex-cli", AiInvocationOptions.Default),
            CancellationToken.None);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Gateway.RunTurnAsync(
            new CodexTurnRequest("p", "{}", "/tmp", null, null, null, TimeSpan.FromSeconds(10)),
            activity,
            CancellationToken.None));

        Assert.IsFalse(error.Message.Contains("sk-secret", StringComparison.Ordinal));
        Assert.AreEqual(1, activity.Snapshot.Retries);
    }

    [TestMethod]
    public async Task ServerInitiatedRequestsAreDeclined()
    {
        var server = new FakeCodexServer();
        server.Handle("model/list", _ =>
        {
            server.PushRequest(77, "item/commandExecution/requestApproval", new JsonObject());
            return Result(new JsonObject { ["data"] = new JsonArray(), ["nextCursor"] = null });
        });
        await using var fixture = new Fixture(server);

        await fixture.Gateway.ListModelsAsync(CancellationToken.None);
        var reply = await server.WaitForReplyAsync(77);

        Assert.AreEqual(CodexAppServerException.MethodNotFound, reply["error"]!["code"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task RollingRateLimitUpdatesMergeIntoTheLastSnapshot()
    {
        var server = new FakeCodexServer();
        server.Handle("account/rateLimits/read", _ => Result(JsonNode.Parse("""
            {
              "ordinaryUsageAllowed": true,
              "rateLimits": { "limitId": "codex", "primary": { "usedPercent": 10, "windowDurationMins": 300, "resetsAt": 1900000000 } },
              "rateLimitsByLimitId": {
                "codex": { "limitId": "codex", "limitName": "Codex", "planType": "pro",
                           "primary": { "usedPercent": 10, "windowDurationMins": 300, "resetsAt": 1900000000 },
                           "secondary": { "usedPercent": 40, "windowDurationMins": 10080, "resetsAt": 1900500000 } },
                "gpt-special": { "limitId": "gpt-special", "normalModelSlug": "gpt-special",
                           "primary": { "usedPercent": 5, "windowDurationMins": null, "resetsAt": null } }
              }
            }
            """)!.AsObject()));
        await using var fixture = new Fixture(server);

        var snapshot = await fixture.Gateway.ReadRateLimitsAsync(CancellationToken.None);
        Assert.AreEqual(2, snapshot.Buckets.Count);
        Assert.IsNull(snapshot.Buckets.Single(x => x.LimitId == "codex").ModelSlug, "No model mapping is inferred.");
        Assert.AreEqual("gpt-special", snapshot.Buckets.Single(x => x.LimitId == "gpt-special").ModelSlug);

        server.Push("account/rateLimits/updated", JsonNode.Parse("""
            { "rateLimits": { "limitId": "codex", "planType": null, "primary": { "usedPercent": 55, "windowDurationMins": 300, "resetsAt": 1900000000 } } }
            """)!.AsObject());
        await WaitUntilAsync(() => fixture.Gateway.LatestQuota?.Buckets.Single(x => x.LimitId == "codex").Primary?.UsedPercent == 55);

        var merged = fixture.Gateway.LatestQuota!.Buckets.Single(x => x.LimitId == "codex");
        Assert.AreEqual("pro", merged.PlanType, "A sparse update never clears known metadata.");
        Assert.AreEqual(40, merged.Secondary!.UsedPercent);
        Assert.AreEqual(AiCapabilityState.Supported, fixture.Gateway.GetCapabilities(true)[AiCapability.RateLimitUpdates]);
    }

    [TestMethod]
    public void QuotaWithoutStructuredDataStaysUnavailable()
    {
        using var document = JsonDocument.Parse("""
            { "ordinaryUsageAllowed": null, "rateLimits": { "limitId": null, "primary": null, "secondary": null, "credits": null } , "rateLimitsByLimitId": null }
            """);

        var snapshot = CodexAppServerGateway.ParseRateLimits(document.RootElement, DateTimeOffset.UnixEpoch);

        Assert.IsNull(snapshot.OrdinaryUsageAllowed);
        var bucket = snapshot.Buckets.Single();
        Assert.IsNull(bucket.Primary);
        Assert.IsNull(bucket.Secondary);
        Assert.IsNull(bucket.Credits);
        Assert.IsNull(bucket.ModelSlug);
    }

    [TestMethod]
    public void ExecJsonEventsProvideExactUsageAndFailureReason()
    {
        var events = CodexCliProvider.ParseExecEvents("""
            {"type":"thread.started","thread_id":"x"}
            not json
            {"type":"turn.completed","usage":{"input_tokens":24763,"cached_input_tokens":24448,"output_tokens":122,"reasoning_output_tokens":7}}
            {"type":"error","message":"stream disconnected"}
            """);

        Assert.AreEqual(new AiTokenUsage(24763, 24448, 122, 7, Estimated: false), events.Usage);
        Assert.AreEqual("stream disconnected", events.Error);
    }

    [TestMethod]
    public void ExecArgumentsStayReadOnlyAndSeparateThePrompt()
    {
        var arguments = CodexCliProvider.BuildExecArguments("/s.json", "/o.json", "gpt-a", "high", "medium", "--version");

        CollectionAssert.IsSubsetOf(new[] { "--sandbox", "read-only", "--json", "--ephemeral" }, arguments.ToArray());
        CollectionAssert.Contains(arguments.ToArray(), "model_reasoning_effort=high");
        CollectionAssert.Contains(arguments.ToArray(), "features.shell_tool=false");
        CollectionAssert.Contains(arguments.ToArray(), "model_verbosity=medium");
        Assert.AreEqual("gpt-a", arguments[arguments.ToList().IndexOf("--model") + 1]);
        Assert.AreEqual("--", arguments[^2], "A prompt starting with a dash must not be parsed as a flag.");
        Assert.AreEqual("--version", arguments[^1]);

        var defaults = CodexCliProvider.BuildExecArguments("/s.json", "/o.json", null, null, null, "p");
        Assert.IsFalse(defaults.Contains("--model"));
        Assert.IsFalse(defaults.Any(x => x.StartsWith("model_reasoning_effort", StringComparison.Ordinal)));
        CollectionAssert.Contains(defaults.ToArray(), "model_verbosity=low", "An invalid or missing verbosity falls back to low.");
    }

    [TestMethod]
    public void TrimContextCapsToApproximatelyFourCharactersPerToken()
    {
        var context = new string('x', 100);

        Assert.AreEqual(context, CodexCliProvider.TrimContext(context, null), "No budget keeps the context unchanged.");
        Assert.AreEqual(40, CodexCliProvider.TrimContext(context, 10).Length, "10 tokens is approximately 40 characters.");
        Assert.AreEqual(context, CodexCliProvider.TrimContext(context, 1000), "A generous budget never grows the context.");
        Assert.AreEqual(string.Empty, CodexCliProvider.TrimContext(string.Empty, 10));
    }

    [TestMethod]
    public async Task TurnConfigCarriesTheResolvedVerbosityAndFallsBackToLow()
    {
        async Task<string> VerbosityForAsync(string? requested)
        {
            var server = new FakeCodexServer();
            server.Handle("thread/start", _ => Result(new JsonObject { ["thread"] = new JsonObject { ["id"] = "t1" } }));
            server.Handle("turn/start", _ =>
            {
                server.Push("turn/completed", new JsonObject
                {
                    ["threadId"] = "t1",
                    ["turn"] = new JsonObject
                    {
                        ["id"] = "u1",
                        ["status"] = "completed",
                        ["items"] = new JsonArray(new JsonObject { ["type"] = "agentMessage", ["text"] = "{}" })
                    }
                });
                return Result(new JsonObject { ["turn"] = new JsonObject { ["id"] = "u1" } });
            });
            await using var fixture = new Fixture(server);

            await fixture.Gateway.RunTurnAsync(
                new CodexTurnRequest("p", "{}", "/tmp", "gpt-a", null, null, TimeSpan.FromSeconds(5)) { Verbosity = requested },
                null,
                CancellationToken.None);

            return server.Received.Single(x => x["method"]?.GetValue<string>() == "thread/start")["params"]!["config"]!["model_verbosity"]!.GetValue<string>();
        }

        Assert.AreEqual("high", await VerbosityForAsync("high"));
        Assert.AreEqual("low", await VerbosityForAsync(null), "Missing verbosity defaults to low.");
        Assert.AreEqual("low", await VerbosityForAsync("invalid-level"), "An unrecognized verbosity falls back to low rather than being sent as-is.");
    }

    [TestMethod]
    public async Task StructuredJobsRetryTransientTurnFailuresUpToTheResolvedLimit()
    {
        var server = new FakeCodexServer();
        var turnAttempts = 0;
        server.Handle("thread/start", _ => Result(new JsonObject { ["thread"] = new JsonObject { ["id"] = $"thread-{Guid.NewGuid():N}" } }));
        server.Handle("turn/start", request =>
        {
            turnAttempts++;
            var threadId = request["params"]!["threadId"]!.GetValue<string>();
            var turnId = $"turn-{turnAttempts}";
            server.Push(
                "turn/completed",
                turnAttempts < 3
                    ? new JsonObject
                    {
                        ["threadId"] = threadId,
                        ["turn"] = new JsonObject { ["id"] = turnId, ["status"] = "failed", ["error"] = new JsonObject { ["message"] = "temporary overload" } }
                    }
                    : new JsonObject
                    {
                        ["threadId"] = threadId,
                        ["turn"] = new JsonObject
                        {
                            ["id"] = turnId,
                            ["status"] = "completed",
                            ["items"] = new JsonArray(new JsonObject { ["type"] = "agentMessage", ["text"] = "{\"translation\":\"Hallo\"}" })
                        }
                    });
            return Result(new JsonObject { ["turn"] = new JsonObject { ["id"] = turnId } });
        });
        await using var fixture = new Fixture(server);
        var provider = new CodexCliProvider(fixture.Gateway);
        var tracker = new AiActivityTracker(TimeProvider.System);
        using var activity = tracker.Start(
            new AiActivityStart("alice", AiOperations.NovelTranslation, "codex-cli", new AiInvocationOptions("gpt-a", "low", null, null) { MaxRetries = 2 }),
            CancellationToken.None);

        string result;
        using (AiActivityScope.Enter(activity))
        {
            result = await provider.TranslateAsync("こんにちは", "de", CancellationToken.None);
        }

        Assert.AreEqual("Hallo", result);
        Assert.AreEqual(3, turnAttempts, "Two retries after the first failed attempt reach the third, successful attempt.");
        Assert.AreEqual(2, activity.Snapshot.Retries);
    }

    [TestMethod]
    public async Task StructuredJobsGiveUpAfterExhaustingTheResolvedRetryLimit()
    {
        var server = new FakeCodexServer();
        var turnAttempts = 0;
        server.Handle("thread/start", _ => Result(new JsonObject { ["thread"] = new JsonObject { ["id"] = $"thread-{Guid.NewGuid():N}" } }));
        server.Handle("turn/start", request =>
        {
            turnAttempts++;
            var threadId = request["params"]!["threadId"]!.GetValue<string>();
            var turnId = $"turn-{turnAttempts}";
            server.Push("turn/completed", new JsonObject
            {
                ["threadId"] = threadId,
                ["turn"] = new JsonObject { ["id"] = turnId, ["status"] = "failed", ["error"] = new JsonObject { ["message"] = "still overloaded" } }
            });
            return Result(new JsonObject { ["turn"] = new JsonObject { ["id"] = turnId } });
        });
        await using var fixture = new Fixture(server);
        var provider = new CodexCliProvider(fixture.Gateway);
        var tracker = new AiActivityTracker(TimeProvider.System);
        using var activity = tracker.Start(
            new AiActivityStart("alice", AiOperations.NovelTranslation, "codex-cli", new AiInvocationOptions("gpt-a", "low", null, null) { MaxRetries = 1 }),
            CancellationToken.None);

        using (AiActivityScope.Enter(activity))
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => provider.TranslateAsync("こんにちは", "de", CancellationToken.None));
        }

        Assert.AreEqual(2, turnAttempts, "A retry limit of 1 allows exactly one retry: two attempts total.");
    }

    [TestMethod]
    public async Task AppServerLoginStartsAndCompletesWhenTheAccountChanges()
    {
        var server = new FakeCodexServer();
        server.Handle("account/login/start", _ => Result(new JsonObject
        {
            ["type"] = "chatgptDeviceCode",
            ["loginId"] = "login-1",
            ["verificationUrl"] = "https://auth.openai.com/codex/device",
            ["userCode"] = "ABCD-1234"
        }));
        await using var fixture = new Fixture(server);
        var provider = new CodexCliProvider(fixture.Gateway);
        Assert.IsFalse(provider.AppServerLoginSupported, "Not proven supported before the first attempt.");

        var snapshot = await provider.StartAppServerLoginAsync(CancellationToken.None);
        Assert.AreEqual(DeviceLoginState.WaitingForUser, snapshot.State);
        Assert.AreEqual("https://auth.openai.com/codex/device", snapshot.VerificationUrl);
        Assert.AreEqual("ABCD-1234", snapshot.UserCode);
        Assert.IsTrue(provider.AppServerLoginSupported);

        server.Push("account/updated", new JsonObject { ["authMode"] = "chatgpt" });
        await WaitUntilAsync(() => provider.GetAppServerLoginSnapshot().State == DeviceLoginState.Succeeded);

        var cancelled = server.Received.Any(x => x["method"]?.GetValue<string>() == "account/login/cancel");
        Assert.IsFalse(cancelled, "A login that succeeded is never also cancelled.");
    }

    [TestMethod]
    public async Task AppServerLoginCancelSendsTheLoginId()
    {
        var server = new FakeCodexServer();
        server.Handle("account/login/start", _ => Result(new JsonObject
        {
            ["type"] = "chatgptDeviceCode",
            ["loginId"] = "login-9",
            ["verificationUrl"] = "https://auth.openai.com/codex/device",
            ["userCode"] = "WXYZ-9999"
        }));
        server.Handle("account/login/cancel", _ => Result(new JsonObject()));
        await using var fixture = new Fixture(server);
        var provider = new CodexCliProvider(fixture.Gateway);

        await provider.StartAppServerLoginAsync(CancellationToken.None);
        await provider.CancelAppServerLoginAsync(CancellationToken.None);

        Assert.AreEqual(DeviceLoginState.Cancelled, provider.GetAppServerLoginSnapshot().State);
        var cancel = server.Received.Single(x => x["method"]?.GetValue<string>() == "account/login/cancel")["params"]!;
        Assert.AreEqual("login-9", cancel["loginId"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task AppServerLoginMarksUnsupportedOnMethodNotFoundAndLeavesTheCliFlowUnaffected()
    {
        var server = new FakeCodexServer();
        server.Handle("account/login/start", _ => MethodNotFound());
        await using var fixture = new Fixture(server);
        var provider = new CodexCliProvider(fixture.Gateway);

        var snapshot = await provider.StartAppServerLoginAsync(CancellationToken.None);
        Assert.AreEqual(DeviceLoginState.Failed, snapshot.State);
        Assert.IsFalse(provider.AppServerLoginSupported);
        Assert.AreEqual(AiCapabilityState.Unsupported, fixture.Gateway.GetCapabilities(true)[AiCapability.AppServerLogin]);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(condition());
    }

    private static JsonObject Usage(long input, long cached, long output, long reasoning) => new()
    {
        ["totalTokens"] = input + output,
        ["inputTokens"] = input,
        ["cachedInputTokens"] = cached,
        ["cacheWriteInputTokens"] = 0,
        ["outputTokens"] = output,
        ["reasoningOutputTokens"] = reasoning
    };

    private static JsonObject Model(
        string slug,
        string name,
        bool isDefault = false,
        bool hidden = false,
        string[]? efforts = null,
        string[]? tiers = null) => new()
    {
        ["id"] = slug + "-preset",
        ["model"] = slug,
        ["displayName"] = name,
        ["description"] = name + " description",
        ["hidden"] = hidden,
        ["isDefault"] = isDefault,
        ["defaultReasoningEffort"] = efforts?.FirstOrDefault(),
        ["supportedReasoningEfforts"] = new JsonArray((efforts ?? []).Select(x => (JsonNode)new JsonObject { ["reasoningEffort"] = x, ["description"] = x }).ToArray()),
        ["serviceTiers"] = new JsonArray((tiers ?? []).Select(x => (JsonNode)new JsonObject { ["id"] = x, ["name"] = x.ToUpperInvariant(), ["description"] = "" }).ToArray())
    };

    private static JsonObject Result(JsonObject result) => new() { ["result"] = result };

    private static JsonObject MethodNotFound() => new()
    {
        ["error"] = new JsonObject { ["code"] = CodexAppServerException.MethodNotFound, ["message"] = "method not found" }
    };

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(FakeCodexServer? server = null, ICodexAppServerLauncher? launcher = null)
        {
            Client = new CodexAppServerClient(
                launcher ?? new FakeLauncher(server ?? new FakeCodexServer()),
                TimeProvider.System,
                NullLogger<CodexAppServerClient>.Instance);
            Gateway = new CodexAppServerGateway(Client, TimeProvider.System);
        }

        public CodexAppServerClient Client { get; }

        public CodexAppServerGateway Gateway { get; }

        public ValueTask DisposeAsync() => Client.DisposeAsync();
    }

    private sealed class FailingLauncher : ICodexAppServerLauncher
    {
        public Task<ICodexAppServerTransport> StartAsync(CancellationToken cancellationToken) =>
            throw new System.ComponentModel.Win32Exception("codex: not found");
    }

    private sealed class FakeLauncher(FakeCodexServer server) : ICodexAppServerLauncher
    {
        public Task<ICodexAppServerTransport> StartAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ICodexAppServerTransport>(server);
    }

    /// <summary>Scripted app-server: answers requests by method and can push notifications.</summary>
    private sealed class FakeCodexServer : ICodexAppServerTransport
    {
        private readonly Channel<string> outgoing = Channel.CreateUnbounded<string>();
        private readonly Dictionary<string, Func<JsonObject, JsonObject>> handlers = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<JsonObject> received = new();

        public FakeCodexServer()
        {
            Handle("initialize", _ => Result(new JsonObject { ["userAgent"] = "codex-test/1.0" }));
        }

        public IReadOnlyList<JsonObject> Received => received.ToArray();

        public void Handle(string method, Func<JsonObject, JsonObject> handler) => handlers[method] = handler;

        public void Push(string method, JsonObject parameters) =>
            outgoing.Writer.TryWrite(new JsonObject { ["method"] = method, ["params"] = parameters }.ToJsonString());

        public void PushRequest(long id, string method, JsonObject parameters) =>
            outgoing.Writer.TryWrite(new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString());

        public async Task WaitForAsync(string method)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                if (received.Any(x => x["method"]?.GetValue<string>() == method))
                {
                    return;
                }

                await Task.Delay(10);
            }

            Assert.Fail($"{method} was never sent.");
        }

        public async Task<JsonObject> WaitForReplyAsync(long id)
        {
            for (var attempt = 0; attempt < 200; attempt++)
            {
                var reply = received.FirstOrDefault(x => x["method"] is null && x["id"]?.GetValue<long>() == id);
                if (reply is not null)
                {
                    return reply;
                }

                await Task.Delay(10);
            }

            Assert.Fail($"No reply to request {id}.");
            return null!;
        }

        public Task WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            var message = JsonNode.Parse(line)!.AsObject();
            received.Enqueue(message);

            if (message["method"]?.GetValue<string>() is { } method && message["id"] is { } id)
            {
                var response = handlers.TryGetValue(method, out var handler)
                    ? handler(message)
                    : MethodNotFound();
                response["id"] = id.DeepClone();
                outgoing.Writer.TryWrite(response.ToJsonString());
            }

            return Task.CompletedTask;
        }

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await outgoing.Reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        public ValueTask DisposeAsync()
        {
            outgoing.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
