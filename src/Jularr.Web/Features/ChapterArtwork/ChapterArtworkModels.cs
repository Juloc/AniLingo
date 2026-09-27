using Jularr.Web.Features.Ai;

namespace Jularr.Web.Features.ChapterArtwork;

public enum ChapterArtworkStatus
{
    Queued,
    Generating,
    Preview,
    Accepted,
    Failed,
    Cancelled
}

public enum ChapterArtworkStyle
{
    Painterly,
    Watercolor,
    Ink,
    Anime,
    Minimal
}

/// <summary>
/// Metadata of one generated chapter image. The image itself lives on media
/// storage (<see cref="AssetPath"/> is relative to the artwork storage root);
/// no chapter text is stored here.
/// </summary>
public sealed record ChapterArtworkItem(
    Guid Id,
    Guid WorkId,
    Guid ChapterId,
    int ChapterNumber,
    ChapterArtworkStatus Status,
    string? AssetPath,
    string? MediaType,
    string? ContentHash,
    long? ByteSize,
    string? ProviderId,
    string? Model,
    int PromptVersion,
    string? ContextHash,
    string ContextScope,
    string? Prompt,
    bool NeutralPrompt,
    ChapterArtworkStyle Style,
    AiImageQuality Quality,
    string? Error,
    Guid? OperationId,
    string RequestedByProfileId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? AcceptedAt)
{
    public bool IsActive => Status is ChapterArtworkStatus.Queued or ChapterArtworkStatus.Generating;
    public bool HasImage => AssetPath is not null && Status is ChapterArtworkStatus.Preview or ChapterArtworkStatus.Accepted;
}

/// <summary>Per-profile preferences. Everything is off by default.</summary>
public sealed record ChapterArtworkPreferences(
    bool Enabled,
    bool AutoGenerate,
    ChapterArtworkStyle Style,
    AiImageQuality Quality,
    int Variations)
{
    public const int MaxVariations = 3;

    public static ChapterArtworkPreferences Default { get; } =
        new(false, false, ChapterArtworkStyle.Painterly, AiImageQuality.Standard, 1);
}

/// <summary>Per-book overrides; null values inherit the profile preference.</summary>
public sealed record ChapterArtworkWorkSettings(
    Guid WorkId,
    bool? Enabled,
    ChapterArtworkStyle? Style,
    string? SeriesStyle,
    bool UseChapterTitles)
{
    public const int SeriesStyleLength = 400;

    public static ChapterArtworkWorkSettings Default(Guid workId) =>
        new(workId, null, null, null, false);
}

/// <summary>Model of the reader header partial <c>_ChapterArtwork</c>.</summary>
public sealed record ChapterArtworkSlot(
    Guid WorkId,
    int ChapterNumber,
    string Label);

public static class ChapterArtworkNames
{
    public static string Status(ChapterArtworkStatus value) => value switch
    {
        ChapterArtworkStatus.Queued => "queued",
        ChapterArtworkStatus.Generating => "generating",
        ChapterArtworkStatus.Preview => "preview",
        ChapterArtworkStatus.Accepted => "accepted",
        ChapterArtworkStatus.Failed => "failed",
        _ => "cancelled"
    };

    public static ChapterArtworkStatus ParseStatus(string value) => value switch
    {
        "queued" => ChapterArtworkStatus.Queued,
        "generating" => ChapterArtworkStatus.Generating,
        "preview" => ChapterArtworkStatus.Preview,
        "accepted" => ChapterArtworkStatus.Accepted,
        "failed" => ChapterArtworkStatus.Failed,
        _ => ChapterArtworkStatus.Cancelled
    };

    public static string Style(ChapterArtworkStyle value) =>
        value.ToString().ToLowerInvariant();

    public static ChapterArtworkStyle ParseStyle(string? value) =>
        Enum.TryParse<ChapterArtworkStyle>(value, ignoreCase: true, out var parsed)
            ? parsed
            : ChapterArtworkStyle.Painterly;

    public static string Quality(AiImageQuality value) =>
        value == AiImageQuality.High ? "high" : "standard";

    public static AiImageQuality ParseQuality(string? value) =>
        value == "high" ? AiImageQuality.High : AiImageQuality.Standard;
}
