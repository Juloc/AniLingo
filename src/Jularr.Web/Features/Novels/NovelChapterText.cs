using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Novels;

public static class NovelReadingLanguage
{
    public const string Japanese = "ja";
    public const string German = "de";
    public const string GermanTranslateGemma = "de-gemma";

    public static string Normalize(string? language)
    {
        var normalized = language?.Trim();
        if (string.Equals(
                normalized,
                GermanTranslateGemma,
                StringComparison.OrdinalIgnoreCase))
        {
            return GermanTranslateGemma;
        }

        return string.Equals(normalized, German, StringComparison.OrdinalIgnoreCase)
            ? German
            : Japanese;
    }
}

/// <summary>
/// Resolves the paragraph layout that progress anchors and annotations refer to.
/// Japanese anchors use the cached source text; German anchors use the current
/// German translation (same prompt version and source hash the reader renders).
/// </summary>
internal static class NovelChapterText
{
    public sealed record ChapterParagraphs(
        Guid ChapterId,
        Guid WorkId,
        IReadOnlyList<string> Paragraphs);

    public static async Task<ChapterParagraphs?> LoadAsync(
        AppDbContext db,
        Guid chapterId,
        string language,
        CancellationToken cancellationToken)
    {
        var normalizedLanguage = NovelReadingLanguage.Normalize(language);
        var aiGerman = normalizedLanguage == NovelReadingLanguage.German;
        var translateGemmaGerman =
            normalizedLanguage == NovelReadingLanguage.GermanTranslateGemma;

        var row = await db.NovelChapters
            .AsNoTracking()
            .Where(chapter => chapter.Id == chapterId)
            .Select(chapter => new
            {
                chapter.Id,
                chapter.WorkId,
                Text = aiGerman
                    ? db.NovelTranslations
                        .Where(translation =>
                            translation.ChapterId == chapter.Id &&
                            translation.TargetLanguage == NovelReadingLanguage.German &&
                            translation.PromptVersion == NovelTranslationService.PromptVersion &&
                            translation.SourceHash == chapter.SourceHash &&
                            !translation.ProviderId.StartsWith(
                                NovelTranslationProviders.TranslateGemmaPrefix))
                        .OrderByDescending(translation => translation.CreatedAt)
                        .Select(translation => translation.Text)
                        .FirstOrDefault()
                    : translateGemmaGerman
                        ? db.NovelTranslations
                            .Where(translation =>
                                translation.ChapterId == chapter.Id &&
                                translation.TargetLanguage == NovelReadingLanguage.German &&
                                translation.PromptVersion ==
                                    NovelTranslationService.TranslateGemmaPromptVersion &&
                                translation.SourceHash == chapter.SourceHash &&
                                translation.ProviderId.StartsWith(
                                    NovelTranslationProviders.TranslateGemmaPrefix))
                            .OrderByDescending(translation => translation.CreatedAt)
                            .Select(translation => translation.Text)
                            .FirstOrDefault()
                        : chapter.OriginalText
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new ChapterParagraphs(
                row.Id,
                row.WorkId,
                NovelTextLayout.SplitParagraphs(row.Text));
    }

    public static (int? ParagraphIndex, int Offset, string? AnchorText) ResolveAnchor(
        IReadOnlyList<string> paragraphs,
        int? paragraphIndex,
        int characterOffset)
    {
        if (paragraphIndex is not int index ||
            index < 0 ||
            index >= paragraphs.Count)
        {
            return (null, 0, null);
        }

        var paragraph = paragraphs[index];
        return (
            index,
            Math.Clamp(characterOffset, 0, paragraph.Length),
            NovelTextLayout.CreateAnchorText(paragraph));
    }

    public static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }
}
