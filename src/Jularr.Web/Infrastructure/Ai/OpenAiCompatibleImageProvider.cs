using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Jularr.Web.Features.Ai;

namespace Jularr.Web.Infrastructure.Ai;

/// <summary>
/// Image generation through an OpenAI-compatible <c>/images/generations</c>
/// endpoint. Only parameters the configured model family is known to accept
/// are sent, so other compatible servers keep working.
/// </summary>
public sealed class OpenAiCompatibleImageProvider(
    HttpClient httpClient,
    AiProfileSettings settings,
    Action<AiUsageMeasurement>? usageSink = null)
{
    private const int MaxImageBytes = 25 * 1024 * 1024;

    public string Id => AiProviderIds.OpenAiCompatible;

    public AiImageAvailability GetAvailability()
    {
        if (settings.ProviderId != AiProviderIds.OpenAiCompatible
            || string.IsNullOrWhiteSpace(settings.BaseUrl)
            || string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return AiImageAvailability.Unavailable(AiImageUnavailableReasons.NoImageProvider);
        }

        return string.IsNullOrWhiteSpace(settings.ImageModel)
            ? AiImageAvailability.Unavailable(AiImageUnavailableReasons.NoImageModel)
            : new AiImageAvailability(true, Id, settings.ImageModel, null);
    }

    public async Task<AiImageGenerationResult> GenerateAsync(
        AiImageRequest request,
        CancellationToken cancellationToken)
    {
        var availability = GetAvailability();
        if (!availability.IsAvailable)
        {
            throw new InvalidOperationException(
                "No image model is configured for the OpenAI-compatible provider.");
        }

        var model = settings.ImageModel!.Trim();
        var count = Math.Clamp(request.Count, 1, 4);
        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["prompt"] = request.Prompt,
            ["n"] = count,
            ["size"] = SizeFor(model, request.Layout)
        };

        if (QualityFor(model, request.Quality) is string quality)
        {
            body["quality"] = quality;
        }

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(settings.BaseUrl!, "images/generations"));
        message.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        message.Content = JsonContent.Create(body);

        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            if (error.Length > 800)
            {
                error = error[..800];
            }

            throw new InvalidOperationException(
                $"Image provider returned HTTP {(int)response.StatusCode}: {error}");
        }

        var payload = await response.Content.ReadFromJsonAsync<ImageResponse>(cancellationToken)
            ?? throw new InvalidOperationException(
                "The image provider returned an empty response.");

        var images = new List<AiGeneratedImage>();
        foreach (var item in payload.Data ?? [])
        {
            var bytes = item.Base64 is { Length: > 0 } base64
                ? Convert.FromBase64String(base64)
                : item.Url is { Length: > 0 } url
                    ? await DownloadAsync(url, cancellationToken)
                    : null;

            if (bytes is null || bytes.Length == 0 || bytes.Length > MaxImageBytes)
            {
                continue;
            }

            var mediaType = AiImageMediaTypes.Detect(bytes);
            if (mediaType is not null)
            {
                images.Add(new AiGeneratedImage(bytes, mediaType));
            }
        }

        if (images.Count == 0)
        {
            throw new InvalidOperationException(
                "The image provider returned no usable image.");
        }

        usageSink?.Invoke(
            new AiUsageMeasurement(
                DateTimeOffset.UtcNow,
                request.Operation,
                Id,
                model,
                request.Prompt.Length,
                0,
                AiUsageTracker.EstimateTokens(request.Prompt.Length),
                0,
                Estimated: true,
                CacheHit: false,
                ResumedChunk: false));

        return new AiImageGenerationResult(Id, model, images);
    }

    public static string SizeFor(string model, AiImageLayout layout)
    {
        var value = model.ToLowerInvariant();
        if (layout == AiImageLayout.Square || value.Contains("dall-e-2", StringComparison.Ordinal))
        {
            return "1024x1024";
        }

        return value.Contains("dall-e-3", StringComparison.Ordinal)
            ? "1792x1024"
            : "1536x1024";
    }

    public static string? QualityFor(string model, AiImageQuality quality)
    {
        var value = model.ToLowerInvariant();
        if (value.StartsWith("gpt-image", StringComparison.Ordinal))
        {
            return quality == AiImageQuality.High ? "high" : "medium";
        }

        if (value.Contains("dall-e-3", StringComparison.Ordinal))
        {
            return quality == AiImageQuality.High ? "hd" : "standard";
        }

        return null;
    }

    private async Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        using var response = await httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode
            || response.Content.Headers.ContentLength > MaxImageBytes)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return bytes.Length > MaxImageBytes ? null : bytes;
    }

    private static Uri BuildUri(string baseUrl, string path)
    {
        var normalized = baseUrl.Trim().TrimEnd('/');
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var parsed)
            && (string.IsNullOrWhiteSpace(parsed.AbsolutePath)
                || parsed.AbsolutePath == "/"))
        {
            normalized += "/v1";
        }

        return new Uri(normalized + "/" + path, UriKind.Absolute);
    }

    private sealed record ImageResponse(
        [property: JsonPropertyName("data")]
        IReadOnlyList<ImageData>? Data);

    private sealed record ImageData(
        [property: JsonPropertyName("b64_json")]
        string? Base64,
        [property: JsonPropertyName("url")]
        string? Url);
}

/// <summary>
/// Resolves the image provider of a profile; the server Codex provider cannot generate images.
/// Each generation runs as a tracked, cancellable activity that records exactly one usage
/// measurement (the prompt size as an estimate, since image APIs report no text tokens).
/// </summary>
public sealed class ProfileAiImageRouter(
    AiProfileSettingsStore settingsStore,
    IHttpClientFactory httpClientFactory,
    AiActivityRunner activityRunner) : IAiImageGenerator
{
    public async Task<AiImageAvailability> GetAvailabilityAsync(
        string profileId,
        CancellationToken cancellationToken) =>
        (await CreateAsync(profileId, cancellationToken)).Provider.GetAvailability();

    public async Task<AiImageGenerationResult> GenerateAsync(
        string profileId,
        AiImageRequest request,
        CancellationToken cancellationToken)
    {
        var (provider, settings) = await CreateAsync(profileId, cancellationToken);
        return await activityRunner.RunAsync(
            new AiActivityStart(
                profileId,
                request.Operation,
                provider.Id,
                new AiInvocationOptions(settings.ImageModel?.Trim(), null, null, null),
                AiUsageTracker.EstimateTokens(request.Prompt.Length)),
            request.Prompt.Length,
            token => provider.GenerateAsync(request, token),
            _ => 0,
            cancellationToken);
    }

    private async Task<(OpenAiCompatibleImageProvider Provider, AiProfileSettings Settings)> CreateAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(profileId, cancellationToken);
        return (new OpenAiCompatibleImageProvider(
            httpClientFactory.CreateClient("ai-openai-compatible"),
            settings), settings);
    }
}
