namespace Jularr.Web.Features.Ai;

public enum AiImageQuality
{
    Standard = 0,
    High = 1
}

/// <summary>Landscape is the only layout chapter headers use today.</summary>
public enum AiImageLayout
{
    Landscape = 0,
    Square = 1
}

public sealed record AiImageRequest(
    string Operation,
    string Prompt,
    AiImageLayout Layout,
    AiImageQuality Quality,
    int Count);

public sealed record AiGeneratedImage(
    byte[] Bytes,
    string MediaType);

public sealed record AiImageGenerationResult(
    string ProviderId,
    string Model,
    IReadOnlyList<AiGeneratedImage> Images);

public static class AiImageUnavailableReasons
{
    public const string NoImageProvider = "no-image-provider";
    public const string NoImageModel = "no-image-model";
}

public sealed record AiImageAvailability(
    bool IsAvailable,
    string? ProviderId,
    string? Model,
    string? Reason)
{
    public static AiImageAvailability Unavailable(string reason) =>
        new(false, null, null, reason);
}

/// <summary>
/// Image-capable AI provider, next to the text providers. Feature code asks
/// for images through this interface only, so reader and domain logic never
/// depend on one backend. The profile is explicit because generation runs in
/// background jobs outside a request.
/// </summary>
public interface IAiImageGenerator
{
    Task<AiImageAvailability> GetAvailabilityAsync(
        string profileId,
        CancellationToken cancellationToken);

    Task<AiImageGenerationResult> GenerateAsync(
        string profileId,
        AiImageRequest request,
        CancellationToken cancellationToken);
}

public static class AiImageMediaTypes
{
    public static string? Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return "image/png";
        }

        if (bytes.Length >= 3
            && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 12
            && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return "image/webp";
        }

        return null;
    }
}
