using System.Net;
using System.Text;
using AniLingo.Web.Features.Ai;
using AniLingo.Web.Infrastructure.Ai;

namespace AniLingo.Tests;

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

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        responseJson,
                        Encoding.UTF8,
                        "application/json")
                });
        }
    }
}
