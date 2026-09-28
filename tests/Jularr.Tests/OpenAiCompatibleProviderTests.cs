using System.Net;
using System.Text;
using Jularr.Web.Features.Ai;
using Jularr.Web.Infrastructure.Ai;

namespace Jularr.Tests;

[TestClass]
public sealed class OpenAiCompatibleProviderTests
{
    [TestMethod]
    public async Task TestRequestUsesChatCompletionsAndRecordsExactUsage()
    {
        var handler = new RecordingHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "content": "OK"
                  }
                }
              ],
              "usage": {
                "prompt_tokens": 12,
                "completion_tokens": 3
              }
            }
            """);
        using var client = new HttpClient(handler);
        var usage = new List<AiUsageMeasurement>();
        var provider = new OpenAiCompatibleProvider(
            client,
            new AiProfileSettings(
                AiProviderIds.OpenAiCompatible,
                "https://example.invalid",
                "test-model",
                "test-secret",
                AiTranslationMode.Efficient),
            usage.Add);

        var status = await provider.TestAsync(CancellationToken.None);

        Assert.IsTrue(status.IsAuthenticated);
        Assert.IsNotNull(handler.RequestUri);
        Assert.AreEqual(
            "https://example.invalid/v1/chat/completions",
            handler.RequestUri.ToString());
        Assert.AreEqual("Bearer", handler.AuthorizationScheme);
        Assert.AreEqual("test-secret", handler.AuthorizationParameter);

        Assert.AreEqual(1, usage.Count);
        Assert.AreEqual("provider-test", usage[0].Operation);
        Assert.AreEqual(12, usage[0].InputTokens);
        Assert.AreEqual(3, usage[0].OutputTokens);
        Assert.IsFalse(usage[0].Estimated);
    }

    [TestMethod]
    public async Task MissingUsageFallsBackToEstimatedTokens()
    {
        var handler = new RecordingHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "content": "OK"
                  }
                }
              ]
            }
            """);
        using var client = new HttpClient(handler);
        var usage = new List<AiUsageMeasurement>();
        var provider = new OpenAiCompatibleProvider(
            client,
            new AiProfileSettings(
                AiProviderIds.OpenAiCompatible,
                "https://example.invalid/v1",
                "test-model",
                "test-secret",
                AiTranslationMode.Efficient),
            usage.Add);

        var status = await provider.TestAsync(CancellationToken.None);

        Assert.IsTrue(status.IsAuthenticated);
        Assert.AreEqual(1, usage.Count);
        Assert.IsTrue(usage[0].Estimated);
        Assert.IsTrue(usage[0].InputTokens > 0);
        Assert.IsTrue(usage[0].OutputTokens > 0);
    }

    [TestMethod]
    public async Task RetriesTransientFailuresUntilSuccess()
    {
        var attempts = 0;
        var handler = new SequenceHandler(_ =>
        {
            attempts++;
            return attempts < 3
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("busy") }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"choices":[{"message":{"content":"OK"}}]}""", Encoding.UTF8, "application/json")
                };
        });
        using var client = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            client,
            new AiProfileSettings(AiProviderIds.OpenAiCompatible, "https://example.invalid", "test-model", "test-secret", AiTranslationMode.Efficient));
        var tracker = new AiActivityTracker(TimeProvider.System);
        using var activity = tracker.Start(
            new AiActivityStart("alice", "provider-test", AiProviderIds.OpenAiCompatible, new AiInvocationOptions("test-model", null, null, null) { MaxRetries = 2 }),
            CancellationToken.None);

        AiProviderStatus status;
        using (AiActivityScope.Enter(activity))
        {
            status = await provider.TestAsync(CancellationToken.None);
        }

        Assert.IsTrue(status.IsAuthenticated);
        Assert.AreEqual(3, attempts, "Two retries after the first two failures reach the third, successful attempt.");
        Assert.AreEqual(2, activity.Snapshot.Retries);
    }

    [TestMethod]
    public async Task GivesUpAfterExhaustingTheResolvedRetryLimit()
    {
        var attempts = 0;
        var handler = new SequenceHandler(_ =>
        {
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("busy") };
        });
        using var client = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            client,
            new AiProfileSettings(AiProviderIds.OpenAiCompatible, "https://example.invalid", "test-model", "test-secret", AiTranslationMode.Efficient));
        var tracker = new AiActivityTracker(TimeProvider.System);
        using var activity = tracker.Start(
            new AiActivityStart("alice", "provider-test", AiProviderIds.OpenAiCompatible, new AiInvocationOptions("test-model", null, null, null) { MaxRetries = 1 }),
            CancellationToken.None);

        AiProviderStatus status;
        using (AiActivityScope.Enter(activity))
        {
            status = await provider.TestAsync(CancellationToken.None);
        }

        Assert.IsFalse(status.IsAuthenticated);
        Assert.AreEqual(2, attempts, "A retry limit of 1 allows exactly one retry: two attempts total.");
    }

    [TestMethod]
    public async Task TrimsSharedContextToTheResolvedContextBudget()
    {
        var handler = new RecordingHandler("""{"choices":[{"message":{"content":"OK"}}]}""");
        using var client = new HttpClient(handler);
        var provider = new OpenAiCompatibleProvider(
            client,
            new AiProfileSettings(AiProviderIds.OpenAiCompatible, "https://example.invalid", "test-model", "test-secret", AiTranslationMode.Efficient));
        var longContext = new string('c', 5000);
        var tracker = new AiActivityTracker(TimeProvider.System);
        using var activity = tracker.Start(
            new AiActivityStart("alice", "book-translation", AiProviderIds.OpenAiCompatible, new AiInvocationOptions("test-model", null, null, null) { ContextBudgetTokens = 10 }),
            CancellationToken.None);

        using (AiActivityScope.Enter(activity))
        {
            await provider.TranslateLiteraryAsync("source text", "ja", "de", longContext, CancellationToken.None);
        }

        Assert.IsNotNull(handler.LastRequestBody);
        Assert.IsFalse(
            handler.LastRequestBody!.Contains(new string('c', 100), StringComparison.Ordinal),
            "The context was trimmed to approximately 40 characters (10 tokens) before it was sent.");
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseJson,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class SequenceHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
