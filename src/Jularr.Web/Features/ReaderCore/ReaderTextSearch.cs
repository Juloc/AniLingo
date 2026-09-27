using Jularr.Web.Data;
using Jularr.Web.Features.Novels;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.ReaderCore;

/// <summary>
/// One match of an in-book search. <see cref="ParagraphIndex"/> uses the same
/// paragraph layout the reader renders (<see cref="NovelTextLayout.SplitParagraphs"/>),
/// so the reader can jump straight to the paragraph. <see cref="Language"/> is
/// <c>original</c> or the translation language tag.
/// </summary>
public sealed record ReaderTextSearchHit(
    Guid ChapterId,
    int ChapterNumber,
    string ChapterTitle,
    string Language,
    int ParagraphIndex,
    string Snippet,
    int MatchStart,
    int MatchLength);

/// <summary>
/// Bounded full-text search across the chapters of one work (Books and Novels
/// share the NovelChapters/NovelTranslations tables). The database narrows the
/// candidate chapters with a case-insensitive LIKE; paragraphs and snippets are
/// computed in memory for at most <see cref="MaxChaptersScanned"/> chapters per
/// source, so a query never loads a whole large book.
/// </summary>
public static class ReaderTextSearch
{
    public const int MinQueryLength = 2;
    public const int MaxQueryLength = 120;
    public const int MaxHits = 60;
    public const int MaxChaptersScanned = 40;
    private const int SnippetBefore = 50;
    private const int SnippetAfter = 90;

    public static async Task<IReadOnlyList<ReaderTextSearchHit>> SearchWorkAsync(
        AppDbContext db,
        Guid workId,
        string? query,
        string? translationLanguage,
        CancellationToken cancellationToken)
    {
        var needle = query?.Trim() ?? "";
        if (needle.Length < MinQueryLength)
        {
            return [];
        }

        if (needle.Length > MaxQueryLength)
        {
            needle = needle[..MaxQueryLength];
        }

        var pattern = "%" + EscapeLike(needle) + "%";
        var hits = new List<ReaderTextSearchHit>();

        var originals = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId
                && EF.Functions.Like(x.OriginalText, pattern, "\\"))
            .OrderBy(x => x.Number)
            .Select(x => new { x.Id, x.Number, x.Title, Text = x.OriginalText })
            .Take(MaxChaptersScanned)
            .ToListAsync(cancellationToken);

        foreach (var chapter in originals)
        {
            Collect(hits, chapter.Id, chapter.Number, chapter.Title, "original", chapter.Text, needle);
        }

        if (!string.IsNullOrWhiteSpace(translationLanguage))
        {
            // Only translations that still match the chapter source are shown by
            // the reader, so only those are searched.
            var translated = await (
                from translation in db.NovelTranslations.AsNoTracking()
                join chapter in db.NovelChapters.AsNoTracking()
                    on translation.ChapterId equals chapter.Id
                where chapter.WorkId == workId
                    && translation.TargetLanguage == translationLanguage
                    && translation.SourceHash == chapter.SourceHash
                    && EF.Functions.Like(translation.Text, pattern, "\\")
                orderby chapter.Number, translation.CreatedAt descending
                select new { chapter.Id, chapter.Number, chapter.Title, translation.Text })
                .Take(MaxChaptersScanned * 2)
                .ToListAsync(cancellationToken);

            foreach (var chapter in translated.DistinctBy(x => x.Id))
            {
                Collect(hits, chapter.Id, chapter.Number, chapter.Title, translationLanguage, chapter.Text, needle);
            }
        }

        return hits
            .OrderBy(x => x.ChapterNumber)
            .ThenBy(x => x.Language == "original" ? 0 : 1)
            .ThenBy(x => x.ParagraphIndex)
            .Take(MaxHits)
            .ToArray();
    }

    private static void Collect(
        List<ReaderTextSearchHit> hits,
        Guid chapterId,
        int number,
        string title,
        string language,
        string text,
        string needle)
    {
        var paragraphs = NovelTextLayout.SplitParagraphs(text);
        var perChapter = 0;
        for (var index = 0; index < paragraphs.Count && perChapter < MaxHits; index++)
        {
            var paragraph = paragraphs[index];
            var position = paragraph.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (position < 0)
            {
                continue;
            }

            var start = Math.Max(0, position - SnippetBefore);
            var end = Math.Min(paragraph.Length, position + needle.Length + SnippetAfter);
            var prefix = start > 0 ? "…" : "";
            var suffix = end < paragraph.Length ? "…" : "";
            hits.Add(new ReaderTextSearchHit(
                chapterId,
                number,
                title,
                language,
                index,
                prefix + paragraph[start..end] + suffix,
                prefix.Length + position - start,
                needle.Length));
            perChapter++;
        }
    }

    private static string EscapeLike(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
