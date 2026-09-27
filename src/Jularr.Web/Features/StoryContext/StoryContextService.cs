using System.Collections.Concurrent;
using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.StoryContext;

public sealed record StoryContextExtractionResult(
    int Extracted,
    int Remaining);

/// <summary>
/// Process-wide cache of derived snapshots. Keys contain the document
/// revision, so any persisted change invalidates deterministically; the
/// bounded size keeps memory flat.
/// </summary>
public sealed class StoryContextSnapshotCache
{
    private const int MaxEntries = 512;
    private readonly ConcurrentDictionary<string, StoryContextSnapshot> entries =
        new(StringComparer.Ordinal);

    public static string Key(Guid workId, long revision, StoryContextQuery query) =>
        $"{workId:N}|{revision}|{query.CacheKey}";

    public bool TryGet(string key, out StoryContextSnapshot snapshot) =>
        entries.TryGetValue(key, out snapshot!);

    public void Set(string key, StoryContextSnapshot snapshot)
    {
        if (entries.Count >= MaxEntries)
        {
            entries.Clear();
        }

        entries[key] = snapshot;
    }
}

/// <summary>
/// Entry point for AI features that read the shared story memory: bounded
/// snapshots (with first-seen resolution), caching and incremental
/// extraction of chapters that have not been analyzed yet.
/// </summary>
public sealed class StoryContextService(
    AppDbContext db,
    StoryContextStore store,
    StoryContextSnapshotCache cache,
    IStoryContextExtractor? extractor = null)
{
    private const int ExtractionSampleCharacters = 6000;

    public int SnapshotBuilds { get; private set; }

    public async Task<StoryContextSnapshot> GetSnapshotAsync(
        Guid workId,
        StoryContextQuery query,
        CancellationToken cancellationToken)
    {
        var document = await store.LoadAsync(workId, cancellationToken);
        if (document is null)
        {
            return StoryContextSnapshot.Empty(workId, query);
        }

        if (query.Scope != StoryContextScope.Full && query.Chapter is int chapter)
        {
            var knownThrough = query.Scope == StoryContextScope.BeforeChapter
                ? chapter - 1
                : chapter;
            document = await ResolveFirstSeenAsync(document, knownThrough, cancellationToken);
        }

        var key = query.IsCacheable
            ? StoryContextSnapshotCache.Key(workId, document.Revision, query)
            : null;
        if (key is not null && cache.TryGet(key, out var cached))
        {
            return cached;
        }

        SnapshotBuilds++;
        var snapshot = StoryContextBuilder.Build(document, query);
        if (key is not null)
        {
            cache.Set(key, snapshot);
        }

        return snapshot;
    }

    /// <summary>
    /// Extracts story memory for chapters up to <paramref name="throughChapter"/>
    /// that have none yet (or whose text changed), oldest first, at most
    /// <paramref name="maxChapters"/> per call. Each extraction only receives
    /// the chapter text and the compact context known before that chapter.
    /// </summary>
    public Task<StoryContextExtractionResult> ExtractThroughAsync(
        Guid workId,
        int throughChapter,
        int maxChapters,
        CancellationToken cancellationToken) =>
        ExtractThroughAsync(workId, fromChapter: 1, throughChapter, maxChapters, cancellationToken);

    /// <summary>Like <see cref="ExtractThroughAsync(Guid, int, int, CancellationToken)"/> for chapters from <paramref name="fromChapter"/> on.</summary>
    public async Task<StoryContextExtractionResult> ExtractThroughAsync(
        Guid workId,
        int fromChapter,
        int throughChapter,
        int maxChapters,
        CancellationToken cancellationToken)
    {
        if (throughChapter < 1)
        {
            return new StoryContextExtractionResult(0, 0);
        }

        var document = await store.LoadAsync(workId, cancellationToken);
        var known = (document?.Chapters ?? [])
            .GroupBy(x => x.Number)
            .ToDictionary(x => x.Key, x => x.Last().SourceHash);

        var candidates = await db.NovelChapters
            .AsNoTracking()
            .Where(x =>
                x.WorkId == workId
                && x.Number >= fromChapter
                && x.Number <= throughChapter
                && x.OriginalText != "")
            .OrderBy(x => x.Number)
            .Select(x => new { x.Id, x.Number, x.Title, x.SourceHash })
            .ToListAsync(cancellationToken);

        var pending = candidates
            .Where(x =>
                !known.TryGetValue(x.Number, out var hash)
                || hash is not null && hash != x.SourceHash)
            .ToArray();

        if (pending.Length == 0 || extractor is null || maxChapters < 1)
        {
            return new StoryContextExtractionResult(0, pending.Length);
        }

        var sourceLanguage = document?.SourceLanguage ?? "und";
        var extracted = 0;

        foreach (var chapter in pending.Take(maxChapters))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = await db.NovelChapters
                .AsNoTracking()
                .Where(x => x.Id == chapter.Id)
                .Select(x => x.OriginalText)
                .SingleAsync(cancellationToken);

            var before = await GetSnapshotAsync(
                workId,
                StoryContextQuery.Before(chapter.Number) with
                {
                    Fields = StoryContextFields.Entities
                        | StoryContextFields.EntityDetails
                        | StoryContextFields.Terms
                        | StoryContextFields.RecentChapters,
                    RecentChapterCount = 2,
                    MaxEntities = 40,
                    MaxTerms = 40
                },
                cancellationToken);

            var result = await extractor.ExtractChapterAsync(
                new StoryChapterExtractionRequest(
                    chapter.Number,
                    chapter.Title,
                    sourceLanguage,
                    CompactSample(text, ExtractionSampleCharacters),
                    StoryContextRenderer.Render(before, StoryContextBudgets.Extraction)),
                cancellationToken);

            await ApplyExtractionAsync(
                workId,
                chapter.Id,
                chapter.Number,
                chapter.Title,
                chapter.SourceHash,
                result,
                cancellationToken);
            extracted++;
        }

        return new StoryContextExtractionResult(extracted, pending.Length - extracted);
    }

    public Task ApplyExtractionAsync(
        Guid workId,
        Guid chapterId,
        int chapterNumber,
        string chapterTitle,
        string? sourceHash,
        StoryChapterExtraction result,
        CancellationToken cancellationToken) =>
        store.UpdateAsync(
            workId,
            sourceLanguage: null,
            document => StoryContextMerge.ApplyChapter(
                document,
                new StoryChapterInput(
                    chapterId,
                    chapterNumber,
                    chapterTitle,
                    result.ChapterSummary,
                    result.ContinuityNotes,
                    sourceHash,
                    "story",
                    result.Entities
                        .Select(x => new StoryEntityInput(
                            x.Name,
                            x.Type,
                            x.Description,
                            x.Pronouns,
                            x.Relationships,
                            VoiceNotes: null,
                            x.Appearance,
                            x.Aliases))
                        .ToArray(),
                    result.Terms
                        .Select(x => new StoryTermInput(x.Source, x.Category))
                        .ToArray())),
            cancellationToken);

    public Task DeleteAsync(Guid workId, CancellationToken cancellationToken) =>
        store.DeleteAsync(workId, cancellationToken);

    /// <summary>
    /// Places entities and terms whose first chapter is unknown (analysis
    /// seed, owner edits, migrated data) by searching chapter text for their
    /// name. Deterministic and token-free; results are persisted so each
    /// chapter is scanned once.
    /// </summary>
    private async Task<StoryContextDocument> ResolveFirstSeenAsync(
        StoryContextDocument document,
        int knownThrough,
        CancellationToken cancellationToken)
    {
        if (knownThrough < 1)
        {
            return document;
        }

        var pendingEntities = document.Entities
            .Where(x => x.FirstSeenChapter is null && x.TextScanThrough < knownThrough)
            .ToArray();
        var pendingTerms = document.Terms
            .Where(x => x.FirstSeenChapter is null && x.TextScanThrough < knownThrough)
            .ToArray();

        if (pendingEntities.Length == 0 && pendingTerms.Length == 0)
        {
            return document;
        }

        var scanFrom = pendingEntities.Select(x => x.TextScanThrough)
            .Concat(pendingTerms.Select(x => x.TextScanThrough))
            .Min();

        var foundEntities = new Dictionary<StoryEntity, int>();
        var foundTerms = new Dictionary<StoryTerm, int>();

        var chapters = db.NovelChapters
            .AsNoTracking()
            .Where(x =>
                x.WorkId == document.WorkId
                && x.Number > scanFrom
                && x.Number <= knownThrough)
            .OrderBy(x => x.Number)
            .Select(x => new { x.Number, x.OriginalText })
            .AsAsyncEnumerable();

        await foreach (var chapter in chapters.WithCancellation(cancellationToken))
        {
            foreach (var entity in pendingEntities)
            {
                if (!foundEntities.ContainsKey(entity)
                    && chapter.Number > entity.TextScanThrough
                    && entity.Aliases.Prepend(entity.Name).Any(name => StoryTextScan.ContainsName(chapter.OriginalText, name)))
                {
                    foundEntities[entity] = chapter.Number;
                }
            }

            foreach (var term in pendingTerms)
            {
                if (!foundTerms.ContainsKey(term)
                    && chapter.Number > term.TextScanThrough
                    && StoryTextScan.ContainsName(chapter.OriginalText, term.Source))
                {
                    foundTerms[term] = chapter.Number;
                }
            }
        }

        var updated = await store.UpdateExistingAsync(
            document.WorkId,
            current =>
            {
                foreach (var entity in pendingEntities)
                {
                    var target = current.Entities.FirstOrDefault(x =>
                        x.Name == entity.Name
                        && x.Type == entity.Type
                        && x.FirstSeenChapter is null);
                    if (target is null)
                    {
                        continue;
                    }

                    if (foundEntities.TryGetValue(entity, out var number))
                    {
                        target.FirstSeenChapter = number;
                    }

                    target.TextScanThrough = Math.Max(target.TextScanThrough, knownThrough);
                }

                foreach (var term in pendingTerms)
                {
                    var target = current.Terms.FirstOrDefault(x =>
                        x.Source == term.Source
                        && x.FirstSeenChapter is null);
                    if (target is null)
                    {
                        continue;
                    }

                    if (foundTerms.TryGetValue(term, out var number))
                    {
                        target.FirstSeenChapter = number;
                    }

                    target.TextScanThrough = Math.Max(target.TextScanThrough, knownThrough);
                }

                return true;
            },
            cancellationToken);

        return updated ?? document;
    }

    private static string CompactSample(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var half = Math.Max(1, (maxLength - 40) / 2);
        return value[..half]
            + "\n\n[… middle omitted …]\n\n"
            + value[^half..];
    }
}

public static class StoryTextScan
{
    /// <summary>
    /// Case-sensitive whole-word search. Scripts without spaces (CJK) match
    /// anywhere; very short names never match to avoid false positives.
    /// </summary>
    public static bool ContainsName(string text, string name)
    {
        var needle = name.Trim();
        if (needle.Length < 2 || string.IsNullOrEmpty(text))
        {
            return false;
        }

        var spaceless = needle.Any(IsSpacelessScript);
        if (!spaceless && needle.Length < 3)
        {
            return false;
        }

        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            if (spaceless)
            {
                return true;
            }

            var end = index + needle.Length;
            var startsWord = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var endsWord = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (startsWord && endsWord)
            {
                return true;
            }

            index = end;
        }

        return false;
    }

    private static bool IsSpacelessScript(char value) =>
        value is >= '⺀' and <= '鿿'
            or >= '가' and <= '힯'
            or >= '豈' and <= '﫿'
            or >= '぀' and <= 'ヿ';
}
