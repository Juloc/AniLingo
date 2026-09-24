using AniLingo.Web.Features.Novels;

namespace AniLingo.Web.Features.Books;

public static class BookLanguageCatalog
{
    private static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = "Indonesian",
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
        var value = language?.Trim().ToLowerInvariant();
        return value is not null && Names.ContainsKey(value)
            ? value
            : fallback;
    }

    public static string GetName(string? language)
    {
        var normalized = Normalize(language);
        return Names.TryGetValue(normalized, out var name)
            ? name
            : normalized;
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
    IReadOnlyList<string> Subjects,
    IReadOnlyList<ImportedBookChapter> Chapters);

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
