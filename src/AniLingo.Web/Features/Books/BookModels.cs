using System.Globalization;
using System.Text.RegularExpressions;
using AniLingo.Web.Features.Novels;

namespace AniLingo.Web.Features.Books;

public static class BookLanguageCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "Indonesian",
            ["id-modern"] = "Modern Indonesian",
            ["de"] = "German",
            ["en"] = "English",
            ["fr"] = "French",
            ["es"] = "Spanish",
            ["it"] = "Italian",
            ["nl"] = "Dutch",
            ["pl"] = "Polish",
            ["pt"] = "Portuguese",
            ["tr"] = "Turkish",
            ["ja"] = "Japanese",
            ["ko"] = "Korean",
            ["zh"] = "Chinese",
            ["ru"] = "Russian"
        };

    public static IReadOnlyList<KeyValuePair<string, string>> Supported =>
        Names.ToArray();

    public static string Normalize(string? language, string fallback = "id")
    {
        var value = language?
            .Trim()
            .Replace('_', '-')
            .ToLowerInvariant();

        if (IsSupportedTag(value))
        {
            return value!;
        }

        var normalizedFallback = fallback
            .Trim()
            .Replace('_', '-')
            .ToLowerInvariant();

        return IsSupportedTag(normalizedFallback)
            ? normalizedFallback
            : "id";
    }

    public static string GetName(string? language)
    {
        var normalized = Normalize(language);

        if (Names.TryGetValue(normalized, out var name))
        {
            return name;
        }

        try
        {
            return CultureInfo.GetCultureInfo(normalized).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return normalized;
        }
    }

    private static bool IsSupportedTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 35)
        {
            return false;
        }

        if (Names.ContainsKey(value))
        {
            return true;
        }

        return Regex.IsMatch(
            value,
            @"^[a-z]{2,3}(?:-[a-z0-9]{2,8}){0,3}$",
            RegexOptions.CultureInvariant);
    }
}

public sealed record ImportedBookChapter(
    int Number,
    string Title,
    string Text);

public sealed record ParsedEpubBook(
    string Title,
    string? Author,
    string? Description,
    string? Language,
    string? Isbn10,
    string? Isbn13,
    string? Publisher,
    string? PublishedDate,
    IReadOnlyList<string> Subjects,
    IReadOnlyList<ImportedBookChapter> Chapters,
    byte[]? CoverBytes,
    string? CoverMediaType);

public sealed record BookLibraryItem(
    Guid WorkId,
    string Title,
    string? Author,
    string? Description,
    string? CoverImageUrl,
    IReadOnlyList<string> Subjects,
    int ChapterCount,
    int TranslatedChapterCount,
    Guid? CurrentChapterId,
    int ProgressPermille,
    DateTime? LastReadAt);

public sealed record BookChapterItem(
    Guid Id,
    int Number,
    string Title,
    bool HasTranslation);

public sealed record BookLibraryDetail(
    NovelWork Work,
    IReadOnlyList<string> Subjects,
    IReadOnlyList<BookChapterItem> Chapters,
    NovelProgress? Progress);

public sealed record BookReaderChapter(
    NovelWork Work,
    NovelChapter Chapter,
    NovelTranslation? Translation,
    IReadOnlyList<string> OriginalParagraphs,
    IReadOnlyList<string> TranslatedParagraphs,
    Guid? PreviousChapterId,
    Guid? NextChapterId,
    NovelProgress? Progress,
    IReadOnlyList<NovelBookmark> Bookmarks,
    string SourceLanguage,
    string TargetLanguage);

public sealed record SabnzbdSubmissionResult(
    bool Accepted,
    string Message);
