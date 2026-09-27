using System.Net;
using System.Text;
using System.Text.Json;
using Jularr.Web.Features.Ai;
using Jularr.Web.Infrastructure.Ai;

namespace Jularr.Tests;

[TestClass]
public sealed class OpenAiCompatibleImageProviderTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    [TestMethod]
    public async Task GeneratesThroughImagesEndpointWithModelSpecificParameters()
    {
        var handler = new Handler(
            $$"""{"data":[{"b64_json":"{{Convert.ToBase64String(Png)}}"}]}""");
        using var client = new HttpClient(handler);
        var usage = new List<AiUsageMeasurement>();
        var provider = new OpenAiCompatibleImageProvider(client, Settings("gpt-image-1"), usage.Add);

        var result = await provider.GenerateAsync(
            new AiImageRequest("chapter-artwork", "A misty harbor.", AiImageLayout.Landscape, AiImageQuality.High, 1),
            CancellationToken.None);

        Assert.AreEqual("https://example.invalid/v1/images/generations", handler.RequestUri?.ToString());
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.AreEqual("gpt-image-1", body.RootElement.GetProperty("model").GetString());
        Assert.AreEqual("1536x1024", body.RootElement.GetProperty("size").GetString());
        Assert.AreEqual("high", body.RootElement.GetProperty("quality").GetString());
        Assert.AreEqual("image/png", result.Images.Single().MediaType);
        Assert.AreEqual("chapter-artwork", usage.Single().Operation);
    }

    [TestMethod]
    public void UnknownModelsGetNoQualityParameterAndMissingModelIsUnavailable()
    {
        Assert.IsNull(OpenAiCompatibleImageProvider.QualityFor("flux-schnell", AiImageQuality.High));
        Assert.AreEqual("1792x1024", OpenAiCompatibleImageProvider.SizeFor("dall-e-3", AiImageLayout.Landscape));

        using var client = new HttpClient(new Handler("{}"));
        var provider = new OpenAiCompatibleImageProvider(client, Settings(null));
        var availability = provider.GetAvailability();

        Assert.IsFalse(availability.IsAvailable);
        Assert.AreEqual(AiImageUnavailableReasons.NoImageModel, availability.Reason);
    }

    private static AiProfileSettings Settings(string? imageModel) =>
        new AiProfileSettings(
            AiProviderIds.OpenAiCompatible,
            "https://example.invalid",
            "text-model",
            "test-secret",
            AiTranslationMode.Efficient)
        {
            ImageModel = imageModel
        };

    private sealed class Handler(string response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
