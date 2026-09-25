using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Novels;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Books;

public sealed record BookCatalogItem(
    string Id,
    string Title,
    string? Author,
    string? Summary,
    string? CoverImageUrl,
    IReadOnlyList<string> Subjects,
    int? FirstPublishYear,
    string? TextUrl,
    string? EpubUrl,
    string SourceUrl,
    string SourceName,
    string? TextSourceName)
{
    public bool CanPreview => !string.IsNullOrWhiteSpace(TextUrl);
    public bool CanAcquire =>
        !string.IsNullOrWhiteSpace(EpubUrl)
        || Id.StartsWith(
            "wsid-",
            StringComparison.OrdinalIgnoreCase);
}

public sealed partial class BookCatalogService(
    HttpClient httpClient,
    AppDbContext db,
    IBookTranslator translator,
    IConfiguration configuration)
{
    public const string ImportedBookProvider = "book-epub";
    public const int TranslationPromptVersion = 3;

    private const int SearchLimit = 24;
    private const int DefaultSampleCharacters = 5500;
    private const int MaxRedirects = 5;
    private const int MaxEpubBytes = 100 * 1024 * 1024;
    private static readonly TimeSpan CatalogRequestTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan EpubDownloadTimeout = TimeSpan.FromSeconds(45);
    private static readonly SemaphoreSlim TranslationGate = new(1, 1);

    public async Task<IReadOnlyList<BookCatalogItem>> SearchAsync(
        string? query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await BrowsePopularBooksAsync(cancellationToken);
        }

        var normalizedQuery = query.Trim();

        var openLibraryTask = CaptureCatalogAsync(
            token => SearchOpenLibraryAsync(normalizedQuery, token),
            cancellationToken);
        var googleTask = CaptureCatalogAsync(
            token => SearchGoogleBooksAsync(normalizedQuery, token),
            cancellationToken);
        var wikisourceTask = CaptureCatalogAsync(
            token => SearchIndonesianWikisourceAsync(
                normalizedQuery,
                token),
            cancellationToken);

        await Task.WhenAll(
            openLibraryTask,
            googleTask,
            wikisourceTask);

        var merged = MergeCatalogResults(
            wikisourceTask.Result,
            openLibraryTask.Result,
            googleTask.Result);

        if (merged.Count > 0)
        {
            return merged
                .Take(SearchLimit)
                .ToArray();
        }

        return await CaptureCatalogAsync(
            token => SearchGutenbergAsync(normalizedQuery, token),
            cancellationToken,
            fallbackToEmpty: true);
    }

    public async Task<BookCatalogItem?> GetAsync(
        string id,
        CancellationToken cancellationToken)
    {
        if (TryParseGutenbergId(id, out var gutenbergId))
        {
            return await GetGutenbergAsync(
                gutenbergId,
                cancellationToken);
        }

        if (TryParseOpenLibraryId(id, out var workKey))
        {
            return await GetOpenLibraryAsync(
                workKey,
                cancellationToken);
        }

        if (TryParseGoogleBooksId(id, out var googleId))
        {
            return await GetGoogleBooksAsync(
                googleId,
                cancellationToken);
        }

        if (TryParseWikisourceId(id, out var wikisourcePageId))
        {
            return await GetIndonesianWikisourceAsync(
                wikisourcePageId,
                cancellationToken);
        }

        return null;
    }

    public async Task<IReadOnlyList<BookLibraryItem>> GetLibraryAsync(
        string profileId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        targetLanguage = BookLanguageCatalog.Normalize(targetLanguage);

        var works = await db.NovelWorks
            .AsNoTracking()
            .Where(x => x.SourceProvider == ImportedBookProvider)
            .OrderBy(x => x.MetadataTitle ?? x.Title)
            .ToListAsync(cancellationToken);

        if (works.Count == 0)
        {
            return [];
        }

        var workIds = works.Select(x => x.Id).ToArray();

        var chapters = await db.NovelChapters
            .AsNoTracking()
            .Where(x => workIds.Contains(x.WorkId))
            .Select(x => new
            {
                x.Id,
                x.WorkId,
                x.SourceHash
            })
            .ToListAsync(cancellationToken);

        var chapterIds = chapters.Select(x => x.Id).ToArray();

        var translations = await db.NovelTranslations
            .AsNoTracking()
            .Where(x =>
                chapterIds.Contains(x.ChapterId)
                && x.TargetLanguage == targetLanguage
                && x.ProviderId == translator.Id
                && x.PromptVersion == TranslationPromptVersion)
            .Select(x => new
            {
                x.ChapterId,
                x.SourceHash
            })
            .ToListAsync(cancellationToken);

        var translated = translations
            .Select(x => (x.ChapterId, x.SourceHash))
            .ToHashSet();

        var progress = await db.NovelProgress
            .AsNoTracking()
            .Where(x =>
                x.ProfileId == profileId
                && workIds.Contains(x.WorkId))
            .ToDictionaryAsync(
                x => x.WorkId,
                cancellationToken);

        var chaptersByWork = chapters
            .GroupBy(x => x.WorkId)
            .ToDictionary(x => x.Key, x => x.ToArray());

        return works.Select(work =>
        {
            var workChapters = chaptersByWork.GetValueOrDefault(work.Id) ?? [];
            progress.TryGetValue(work.Id, out var current);

            return new BookLibraryItem(
                work.Id,
                work.MetadataTitle ?? work.Title,
                work.Author,
                work.MetadataDescription ?? work.Description,
                work.CoverImageUrl,
                ParseGenres(work.MetadataGenresJson),
                workChapters.Length,
                workChapters.Count(x =>
                    translated.Contains((x.Id, x.SourceHash))),
                current?.ChapterId,
                current?.PositionPermille ?? 0,
                current?.UpdatedAt);
        }).ToArray();
    }

    public async Task<BookLibraryDetail?> GetLibraryBookAsync(
        Guid workId,
        string profileId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        targetLanguage = BookLanguageCatalog.Normalize(targetLanguage);

        var work = await db.NovelWorks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == workId
                    && x.SourceProvider == ImportedBookProvider,
                cancellationToken);

        if (work is null)
        {
            return null;
        }

        var chapters = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderBy(x => x.Number)
            .Select(chapter => new BookChapterItem(
                chapter.Id,
                chapter.Number,
                chapter.Title,
                db.NovelTranslations.Any(translation =>
                    translation.ChapterId == chapter.Id
                    && translation.TargetLanguage == targetLanguage
                    && translation.ProviderId == translator.Id
                    && translation.PromptVersion == TranslationPromptVersion
                    && translation.SourceHash == chapter.SourceHash)))
            .ToListAsync(cancellationToken);

        var progress = await db.NovelProgress
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ProfileId == profileId
                    && x.WorkId == workId,
                cancellationToken);

        return new BookLibraryDetail(
            work,
            ParseGenres(work.MetadataGenresJson),
            chapters,
            progress);
    }

    public async Task<BookReaderChapter?> GetReaderChapterAsync(
        Guid chapterId,
        string profileId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        targetLanguage = BookLanguageCatalog.Normalize(targetLanguage);

        var chapter = await db.NovelChapters
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == chapterId,
                cancellationToken);

        if (chapter is null)
        {
            return null;
        }

        var work = await db.NovelWorks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == chapter.WorkId
                    && x.SourceProvider == ImportedBookProvider,
                cancellationToken);

        if (work is null)
        {
            return null;
        }

        var sourceLanguage = GetSourceLanguage(work);
        var translation = sourceLanguage.Equals(
                targetLanguage,
                StringComparison.OrdinalIgnoreCase)
            ? null
            : await GetCachedTranslationAsync(
                chapter.Id,
                targetLanguage,
                cancellationToken);

        var previous = await db.NovelChapters
            .AsNoTracking()
            .Where(x =>
                x.WorkId == work.Id
                && x.Number < chapter.Number)
            .OrderByDescending(x => x.Number)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var next = await db.NovelChapters
            .AsNoTracking()
            .Where(x =>
                x.WorkId == work.Id
                && x.Number > chapter.Number)
            .OrderBy(x => x.Number)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var progress = await db.NovelProgress
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ProfileId == profileId
                    && x.WorkId == work.Id,
                cancellationToken);

        var bookmarks = await db.NovelBookmarks
            .AsNoTracking()
            .Where(x =>
                x.ProfileId == profileId
                && x.ChapterId == chapter.Id)
            .OrderBy(x => x.PositionPermille)
            .ToListAsync(cancellationToken);

        return new BookReaderChapter(
            work,
            chapter,
            translation,
            NovelTextLayout.SplitParagraphs(chapter.OriginalText),
            NovelTextLayout.SplitParagraphs(translation?.Text),
            previous,
            next,
            progress?.ChapterId == chapter.Id ? progress : null,
            bookmarks,
            sourceLanguage,
            targetLanguage);
    }

    public async Task<Guid> AcquireCatalogBookAsync(
        string catalogId,
        CancellationToken cancellationToken)
    {
        if (TryParseWikisourceId(
                catalogId,
                out var wikisourcePageId))
        {
            return await ImportIndonesianWikisourceAsync(
                wikisourcePageId,
                cancellationToken);
        }

        var catalog = await GetAsync(
            catalogId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Book could not be found in the catalog.");

        if (!catalog.CanAcquire)
        {
            var match = await FindGutenbergMatchAsync(
                catalog.Title,
                catalog.Author,
                cancellationToken);

            if (match is not null)
            {
                catalog = catalog with
                {
                    EpubUrl = match.EpubUrl,
                    TextUrl = match.TextUrl,
                    TextSourceName = "Project Gutenberg"
                };
            }
        }

        if (string.IsNullOrWhiteSpace(catalog.EpubUrl))
        {
            throw new InvalidOperationException(
                "No authorized downloadable EPUB source was found for this title. Upload your EPUB instead.");
        }

        var bytes = await DownloadGutenbergFileAsync(
            new Uri(catalog.EpubUrl, UriKind.Absolute),
            cancellationToken);

        using var stream = new MemoryStream(bytes, writable: false);
        var parsed = EpubBookParser.Parse(
            stream,
            catalog.Title + ".epub");

        return await ImportParsedBookAsync(
            parsed,
            sourceKey: CleanSourceKey(catalog.Id),
            sourceUrl: catalog.SourceUrl,
            metadataProvider: NormalizeProvider(catalog.SourceName),
            metadataExternalId: catalog.Id,
            coverImageUrl: catalog.CoverImageUrl,
            fallbackAuthor: catalog.Author,
            fallbackDescription: catalog.Summary,
            fallbackSubjects: catalog.Subjects,
            cancellationToken);
    }

    public async Task<Guid> ImportUploadedEpubAsync(
        Stream stream,
        string fileName,
        CancellationToken cancellationToken)
    {
        using var copy = await CopyToMemoryBoundedAsync(
            stream,
            MaxEpubBytes,
            cancellationToken);

        var hash = Convert.ToHexString(
            SHA256.HashData(copy.ToArray()));
        var sourceKey =
            "upload-" + hash[..48].ToLowerInvariant();

        var existingId = await db.NovelWorks
            .AsNoTracking()
            .Where(x =>
                x.SourceProvider == ImportedBookProvider
                && x.SourceKey == sourceKey)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (existingId is Guid existing)
        {
            return existing;
        }

        copy.Position = 0;

        var parsed = EpubBookParser.Parse(
            copy,
            fileName);

        return await ImportParsedBookAsync(
            parsed,
            sourceKey: sourceKey,
            sourceUrl: "upload://" + Uri.EscapeDataString(fileName),
            metadataProvider: null,
            metadataExternalId: null,
            coverImageUrl: null,
            fallbackAuthor: null,
            fallbackDescription: null,
            fallbackSubjects: [],
            cancellationToken);
    }

    public async Task<Guid> ImportRemoteEpubAsync(
        string sourceUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(
                sourceUrl?.Trim(),
                UriKind.Absolute,
                out var initialUri))
        {
            throw new InvalidOperationException(
                "Enter a valid absolute EPUB URL.");
        }

        await ValidateExternalEpubUriAsync(
            initialUri,
            cancellationToken);

        var (bytes, finalUri) = await DownloadExternalEpubAsync(
            initialUri,
            cancellationToken);

        var hash = Convert.ToHexString(
            SHA256.HashData(bytes));
        var sourceKey =
            "remote-" + hash[..48].ToLowerInvariant();

        var existingId = await db.NovelWorks
            .AsNoTracking()
            .Where(x =>
                x.SourceProvider == ImportedBookProvider
                && x.SourceKey == sourceKey)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (existingId is Guid existing)
        {
            return existing;
        }

        using var stream = new MemoryStream(
            bytes,
            writable: false);
        var fileName = Path.GetFileName(
            finalUri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName)
            || !fileName.EndsWith(
                ".epub",
                StringComparison.OrdinalIgnoreCase))
        {
            fileName = "remote-book.epub";
        }

        var parsed = EpubBookParser.Parse(
            stream,
            fileName);

        return await ImportParsedBookAsync(
            parsed,
            sourceKey,
            finalUri.ToString(),
            metadataProvider: "direct-epub",
            metadataExternalId: null,
            coverImageUrl: null,
            fallbackAuthor: null,
            fallbackDescription: null,
            fallbackSubjects: [],
            cancellationToken);
    }

    public async Task<NovelTranslation?> GetCachedTranslationAsync(
        Guid chapterId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        targetLanguage = BookLanguageCatalog.Normalize(targetLanguage);

        return await (
            from translation in db.NovelTranslations.AsNoTracking()
            join chapter in db.NovelChapters.AsNoTracking()
                on translation.ChapterId equals chapter.Id
            where translation.ChapterId == chapterId
                && translation.TargetLanguage == targetLanguage
                && translation.ProviderId == translator.Id
                && translation.PromptVersion == TranslationPromptVersion
                && translation.SourceHash == chapter.SourceHash
            orderby translation.CreatedAt descending
            select translation
        ).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<NovelTranslation> TranslateChapterAsync(
        Guid chapterId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        targetLanguage = BookLanguageCatalog.Normalize(targetLanguage);

        var chapter = await db.NovelChapters
            .SingleOrDefaultAsync(
                x => x.Id == chapterId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Book chapter was not found.");

        var work = await db.NovelWorks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == chapter.WorkId
                    && x.SourceProvider == ImportedBookProvider,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Imported book was not found.");

        var sourceLanguage = GetSourceLanguage(work);
        if (sourceLanguage.Equals(
            targetLanguage,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "This chapter is already in the selected language.");
        }

        var cached = await GetCachedTranslationAsync(
            chapterId,
            targetLanguage,
            cancellationToken);

        if (cached is not null)
        {
            return cached;
        }

        await TranslationGate.WaitAsync(cancellationToken);
        try
        {
            cached = await GetCachedTranslationAsync(
                chapterId,
                targetLanguage,
                cancellationToken);
            if (cached is not null)
            {
                return cached;
            }

            var previousChapter = await db.NovelChapters
                .AsNoTracking()
                .Where(x =>
                    x.WorkId == work.Id
                    && x.Number < chapter.Number)
                .OrderByDescending(x => x.Number)
                .FirstOrDefaultAsync(cancellationToken);

            string? previousTargetContext = null;
            if (previousChapter is not null)
            {
                previousTargetContext = await db.NovelTranslations
                    .AsNoTracking()
                    .Where(x =>
                        x.ChapterId == previousChapter.Id
                        && x.TargetLanguage == targetLanguage
                        && x.ProviderId == translator.Id
                        && x.PromptVersion == TranslationPromptVersion
                        && x.SourceHash == previousChapter.SourceHash)
                    .OrderByDescending(x => x.CreatedAt)
                    .Select(x => x.Text)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var nextContext = await db.NovelChapters
                .AsNoTracking()
                .Where(x =>
                    x.WorkId == work.Id
                    && x.Number > chapter.Number)
                .OrderBy(x => x.Number)
                .Select(x => x.OriginalText)
                .FirstOrDefaultAsync(cancellationToken);

            var bookContext = BuildTranslationContext(
                work,
                chapter,
                previousChapter?.OriginalText,
                previousTargetContext,
                nextContext,
                targetLanguage);

            var translatedChunks = new List<string>();
            var chunks = NovelTranslationService.ChunkText(
                chapter.OriginalText,
                5200);

            for (var index = 0; index < chunks.Count; index++)
            {
                var localContext = bookContext
                    + "\nCurrent segment: "
                    + (index + 1).ToString(CultureInfo.InvariantCulture)
                    + "/"
                    + chunks.Count.ToString(CultureInfo.InvariantCulture);

                if (translatedChunks.Count > 0)
                {
                    localContext +=
                        "\nPrevious translated segment ending:\n"
                        + Tail(translatedChunks[^1], 1000);
                }

                var translated = await translator.TranslateLiteraryAsync(
                    chunks[index],
                    sourceLanguage,
                    targetLanguage,
                    localContext,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(translated))
                {
                    throw new InvalidOperationException(
                        "AI translation returned an empty book segment.");
                }

                translatedChunks.Add(translated.Trim());
            }

            var completed = new NovelTranslation
            {
                ChapterId = chapter.Id,
                TargetLanguage = targetLanguage,
                ProviderId = translator.Id,
                PromptVersion = TranslationPromptVersion,
                SourceHash = chapter.SourceHash,
                Text = string.Join("\n\n", translatedChunks),
                CreatedAt = DateTime.UtcNow
            };

            db.NovelTranslations.Add(completed);
            await db.SaveChangesAsync(cancellationToken);
            return completed;
        }
        finally
        {
            TranslationGate.Release();
        }
    }

    public async Task TranslateBookAsync(
        Guid workId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        targetLanguage = BookLanguageCatalog.Normalize(targetLanguage);

        var chapterIds = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId)
            .OrderBy(x => x.Number)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (chapterIds.Count == 0)
        {
            throw new InvalidOperationException(
                "Book has no chapters to translate.");
        }

        foreach (var chapterId in chapterIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cached = await GetCachedTranslationAsync(
                chapterId,
                targetLanguage,
                cancellationToken);

            if (cached is null)
            {
                await TranslateChapterAsync(
                    chapterId,
                    targetLanguage,
                    cancellationToken);
            }
        }
    }

    public async Task<int> ClearBookTranslationsAsync(
        Guid workId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        targetLanguage = BookLanguageCatalog.Normalize(targetLanguage);

        var isBook = await db.NovelWorks
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == workId
                    && x.SourceProvider == ImportedBookProvider,
                cancellationToken);

        if (!isBook)
        {
            throw new InvalidOperationException(
                "Imported book was not found.");
        }

        var chapterIds = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var deleted = await db.NovelTranslations
            .Where(x =>
                chapterIds.Contains(x.ChapterId)
                && x.TargetLanguage == targetLanguage
                && x.ProviderId == translator.Id
                && x.PromptVersion == TranslationPromptVersion)
            .ExecuteDeleteAsync(cancellationToken);

        // ExecuteDelete bypasses the EF change tracker. Detach matching
        // cached translations so a later parent delete in this request
        // cannot try to delete an already-removed row.
        var chapterSet = chapterIds.ToHashSet();
        foreach (var entry in db.ChangeTracker
                     .Entries<NovelTranslation>()
                     .Where(x =>
                         chapterSet.Contains(x.Entity.ChapterId)
                         && x.Entity.TargetLanguage == targetLanguage
                         && x.Entity.ProviderId == translator.Id
                         && x.Entity.PromptVersion == TranslationPromptVersion)
                     .ToArray())
        {
            entry.State = EntityState.Detached;
        }

        return deleted;
    }

    public async Task DeleteImportedBookAsync(
        Guid workId,
        CancellationToken cancellationToken)
    {
        var work = await db.NovelWorks
            .SingleOrDefaultAsync(
                x => x.Id == workId
                    && x.SourceProvider == ImportedBookProvider,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Imported book was not found.");

        db.NovelWorks.Remove(work);
        await db.SaveChangesAsync(cancellationToken);

        DeleteLocalCoverFiles(workId);
    }

    private static void DeleteLocalCoverFiles(Guid workId)
    {
        var directory = Path.Combine(
            "/data",
            "books",
            "covers");

        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(
                     directory,
                     workId.ToString("N") + ".*",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public async Task SaveProgressAsync(
        string profileId,
        Guid workId,
        Guid chapterId,
        int positionPermille,
        string language,
        CancellationToken cancellationToken)
    {
        var exists = await db.NovelChapters
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == chapterId
                    && x.WorkId == workId,
                cancellationToken);

        if (!exists)
        {
            throw new InvalidOperationException(
                "Book chapter was not found.");
        }

        var progress = await db.NovelProgress
            .SingleOrDefaultAsync(
                x => x.ProfileId == profileId
                    && x.WorkId == workId,
                cancellationToken);

        if (progress is null)
        {
            progress = new NovelProgress
            {
                ProfileId = profileId,
                WorkId = workId
            };
            db.NovelProgress.Add(progress);
        }

        progress.ChapterId = chapterId;
        progress.PositionPermille = Math.Clamp(
            positionPermille,
            0,
            1000);
        progress.AnchorLanguage = NormalizeAnchorLanguage(language);
        progress.AnchorParagraphIndex = null;
        progress.AnchorOffset = 0;
        progress.AnchorText = null;
        progress.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<NovelBookmark> AddBookmarkAsync(
        string profileId,
        Guid workId,
        Guid chapterId,
        int positionPermille,
        string language,
        CancellationToken cancellationToken)
    {
        var exists = await db.NovelChapters
            .AsNoTracking()
            .AnyAsync(
                x => x.Id == chapterId
                    && x.WorkId == workId,
                cancellationToken);

        if (!exists)
        {
            throw new InvalidOperationException(
                "Book chapter was not found.");
        }

        var bookmark = new NovelBookmark
        {
            ProfileId = profileId,
            WorkId = workId,
            ChapterId = chapterId,
            PositionPermille = Math.Clamp(
                positionPermille,
                0,
                1000),
            Language = NormalizeAnchorLanguage(language),
            ParagraphIndex = null,
            CharacterOffset = 0,
            AnchorText = null,
            Label = null,
            CreatedAt = DateTime.UtcNow
        };

        db.NovelBookmarks.Add(bookmark);
        await db.SaveChangesAsync(cancellationToken);
        return bookmark;
    }

    public async Task RemoveBookmarkAsync(
        string profileId,
        Guid bookmarkId,
        CancellationToken cancellationToken)
    {
        await db.NovelBookmarks
            .Where(x =>
                x.Id == bookmarkId
                && x.ProfileId == profileId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public BookIntegrationSettings StoredIntegrationSettings =>
        BookIntegrationSettingsStore.Load();

    public bool IsInboxConfigured =>
        TryGetInboxPath(out _);

    public async Task<IReadOnlyList<Guid>> ImportInboxAsync(
        CancellationToken cancellationToken)
    {
        if (!TryGetInboxPath(out var inboxPath))
        {
            throw new InvalidOperationException(
                "Books inbox is not configured.");
        }

        if (!Directory.Exists(inboxPath))
        {
            throw new InvalidOperationException(
                $"Books inbox '{inboxPath}' is not available.");
        }

        var files = Directory
            .EnumerateFiles(
                inboxPath,
                "*.epub",
                SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Take(200)
            .ToArray();

        var imported = new List<Guid>(files.Length);
        foreach (var path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 81920,
                    useAsync: true);

                imported.Add(await ImportUploadedEpubAsync(
                    stream,
                    Path.GetFileName(path),
                    cancellationToken));
            }
            catch (IOException)
            {
                // A downloader may still be moving/writing this file.
                // The next inbox scan can retry it safely.
            }
        }

        return imported
            .Distinct()
            .ToArray();
    }

    public bool IsSabnzbdConfigured =>
        TryGetSabnzbdConfiguration(out _, out _, out _);

    public async Task<SabnzbdSubmissionResult> QueueSabnzbdUrlAsync(
        string nzbUrl,
        string? displayName,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(
                nzbUrl?.Trim(),
                UriKind.Absolute,
                out var source)
            || source.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "Enter a valid HTTP or HTTPS NZB URL.");
        }

        if (!TryGetSabnzbdConfiguration(
                out var baseUri,
                out var apiKey,
                out var category))
        {
            throw new InvalidOperationException(
                "SABnzbd is not configured for AniLingo.");
        }

        var fields = new Dictionary<string, string>
        {
            ["mode"] = "addurl",
            ["name"] = source.ToString(),
            ["nzbname"] = displayName ?? "AniLingo book",
            ["apikey"] = apiKey,
            ["output"] = "json"
        };
        if (!string.IsNullOrWhiteSpace(category))
        {
            fields["cat"] = category;
        }

        using var content = new FormUrlEncodedContent(fields);
        using var response = await httpClient.PostAsync(
            new Uri(baseUri, "api"),
            content,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return ParseSabResponse(body);
    }

    public async Task<string> TestSabnzbdAsync(
        CancellationToken cancellationToken)
    {
        if (!TryGetSabnzbdConfiguration(
                out var baseUri,
                out var apiKey,
                out _))
        {
            throw new InvalidOperationException(
                "SABnzbd is not configured for AniLingo.");
        }

        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["mode"] = "version",
                ["apikey"] = apiKey,
                ["output"] = "json"
            });

        using var response = await httpClient.PostAsync(
            new Uri(baseUri, "api"),
            content,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);
        response.EnsureSuccessStatusCode();

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty(
                    "version",
                    out var version)
                && version.ValueKind == JsonValueKind.String)
            {
                return "Connected to SABnzbd "
                    + version.GetString()
                    + ".";
            }
        }
        catch (JsonException)
        {
        }

        return "Connected to SABnzbd.";
    }

    public async Task<SabnzbdSubmissionResult> QueueSabnzbdFileAsync(
        Stream nzb,
        string fileName,
        CancellationToken cancellationToken)
    {
        if (!fileName.EndsWith(
                ".nzb",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only .nzb files can be sent to SABnzbd.");
        }

        if (!TryGetSabnzbdConfiguration(
                out var baseUri,
                out var apiKey,
                out var category))
        {
            throw new InvalidOperationException(
                "SABnzbd is not configured for AniLingo.");
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("addfile"), "mode");
        content.Add(new StringContent(apiKey), "apikey");
        content.Add(new StringContent("json"), "output");
        if (!string.IsNullOrWhiteSpace(category))
        {
            content.Add(new StringContent(category), "cat");
        }

        using var file = new StreamContent(nzb);
        content.Add(file, "name", fileName);

        using var response = await httpClient.PostAsync(
            new Uri(baseUri, "api"),
            content,
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return ParseSabResponse(body);
    }

    public async Task<string> GetReadableSampleAsync(
        BookCatalogItem book,
        CancellationToken cancellationToken,
        int maxCharacters = DefaultSampleCharacters)
    {
        if (string.IsNullOrWhiteSpace(book.TextUrl))
        {
            throw new InvalidOperationException(
                "No readable text source is available for this catalog item.");
        }

        var raw = await GetTextFollowingRedirectsAsync(
            new Uri(book.TextUrl, UriKind.Absolute),
            cancellationToken);

        return ExtractReadableSample(
            raw,
            maxCharacters);
    }

    public static string ExtractReadableSample(
        string rawText,
        int maxCharacters = DefaultSampleCharacters)
    {
        if (maxCharacters < 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCharacters));
        }

        var text = rawText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var startMarker = text.IndexOf(
            "*** START OF",
            StringComparison.OrdinalIgnoreCase);
        if (startMarker >= 0)
        {
            var contentStart = text.IndexOf(
                '\n',
                startMarker);
            if (contentStart >= 0)
            {
                text = text[(contentStart + 1)..];
            }
        }

        var endMarker = text.IndexOf(
            "*** END OF",
            StringComparison.OrdinalIgnoreCase);
        if (endMarker >= 0)
        {
            text = text[..endMarker];
        }

        text = text.Trim();

        var chapterMatches = Regex.Matches(
            text,
            @"(?im)^[ \t]*chapter\s+(?:[ivxlcdm]+|\d+|one|two|three|four|five|six|seven|eight|nine|ten)\b[^\n]*");

        for (var index = 0;
             index < chapterMatches.Count;
             index++)
        {
            var current = chapterMatches[index];
            var nextIndex =
                index + 1 < chapterMatches.Count
                    ? chapterMatches[index + 1].Index
                    : text.Length;

            if (nextIndex - current.Index >= 900)
            {
                text = text[current.Index..]
                    .TrimStart();
                break;
            }
        }

        if (text.Length <= maxCharacters)
        {
            return text;
        }

        var candidate = text[..maxCharacters];
        var paragraphBreak = candidate.LastIndexOf(
            "\n\n",
            StringComparison.Ordinal);

        if (paragraphBreak >= maxCharacters / 2)
        {
            candidate = candidate[..paragraphBreak];
        }
        else
        {
            var sentenceBreak = candidate.LastIndexOfAny(
                ['.', '!', '?']);
            if (sentenceBreak >= maxCharacters / 2)
            {
                candidate = candidate[..(sentenceBreak + 1)];
            }
        }

        return candidate.Trim();
    }

    private async Task<Guid> ImportParsedBookAsync(
        ParsedEpubBook parsed,
        string sourceKey,
        string sourceUrl,
        string? metadataProvider,
        string? metadataExternalId,
        string? coverImageUrl,
        string? fallbackAuthor,
        string? fallbackDescription,
        IReadOnlyList<string> fallbackSubjects,
        CancellationToken cancellationToken)
    {
        sourceKey = CleanSourceKey(sourceKey);

        var work = await db.NovelWorks
            .SingleOrDefaultAsync(
                x => x.SourceProvider == ImportedBookProvider
                    && x.SourceKey == sourceKey,
                cancellationToken);

        if (work is null)
        {
            work = new NovelWork
            {
                SourceProvider = ImportedBookProvider,
                SourceKey = sourceKey,
                ImportedAt = DateTime.UtcNow
            };
            db.NovelWorks.Add(work);
        }

        var subjects = parsed.Subjects.Count > 0
            ? parsed.Subjects
            : fallbackSubjects;

        work.SourceUrl = Truncate(
            sourceUrl,
            2048);
        work.Title = Truncate(
            parsed.Title,
            500);
        work.Author = TruncateNullable(
            parsed.Author ?? fallbackAuthor,
            300);
        work.Description = TruncateNullable(
            parsed.Description ?? fallbackDescription,
            4000);
        work.MetadataProvider = TruncateNullable(
            metadataProvider,
            80);
        work.MetadataExternalId = TruncateNullable(
            metadataExternalId,
            200);
        work.MetadataTitle = work.Title;
        work.MetadataDescription = work.Description;
        work.CoverImageUrl = TruncateNullable(
            coverImageUrl,
            2048);

        if (parsed.CoverBytes is { Length: > 0 }
            && !string.IsNullOrWhiteSpace(parsed.CoverMediaType))
        {
            var localCover = await SaveLocalCoverAsync(
                work.Id,
                parsed.CoverBytes,
                parsed.CoverMediaType,
                cancellationToken);
            if (localCover is not null)
            {
                work.CoverImageUrl = $"/Books/Cover/{work.Id}";
            }
        }

        work.Format = "EPUB:" + NormalizeSourceLanguage(
            parsed.Language);
        work.MetadataStatus = "IMPORTED";
        work.MetadataGenresJson = subjects.Count > 0
            ? JsonSerializer.Serialize(
                subjects.Take(32).ToArray())
            : null;
        work.UpdatedAt = DateTime.UtcNow;

        var existing = await db.NovelChapters
            .Where(x => x.WorkId == work.Id)
            .ToDictionaryAsync(
                x => x.Number,
                cancellationToken);

        var importedNumbers = new HashSet<int>();

        foreach (var imported in parsed.Chapters)
        {
            importedNumbers.Add(imported.Number);

            if (!existing.TryGetValue(
                    imported.Number,
                    out var chapter))
            {
                chapter = new NovelChapter
                {
                    WorkId = work.Id,
                    Number = imported.Number,
                    ImportedAt = DateTime.UtcNow
                };
                db.NovelChapters.Add(chapter);
            }

            chapter.Title = Truncate(
                imported.Title,
                500);
            chapter.SourceUrl =
                $"book://{work.Id:N}/{imported.Number.ToString(CultureInfo.InvariantCulture)}";
            chapter.OriginalText = imported.Text;
            chapter.SourceHash = Hash(imported.Text);
            chapter.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var stale in existing.Values.Where(
                     x => !importedNumbers.Contains(x.Number)))
        {
            db.NovelChapters.Remove(stale);
        }

        await db.SaveChangesAsync(cancellationToken);
        return work.Id;
    }

    private async Task<IReadOnlyList<BookCatalogItem>> SearchOpenLibraryAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            "https://openlibrary.org/search.json"
            + "?q=" + Uri.EscapeDataString(query)
            + "&fields=key,title,author_name,cover_i,first_publish_year,subject"
            + $"&limit={SearchLimit}");

        var response = await GetJsonAsync<OpenLibrarySearchResponse>(
            uri,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Open Library returned no data.");

        return response.Docs
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.Key)
                && !string.IsNullOrWhiteSpace(x.Title)
                && x.Key.StartsWith(
                    "/works/",
                    StringComparison.Ordinal))
            .Take(SearchLimit)
            .Select(MapOpenLibrarySearch)
            .ToArray();
    }

    private async Task<IReadOnlyList<BookCatalogItem>> SearchGutenbergAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var response = await GetJsonAsync<GutendexListResponse>(
            new Uri(
                httpClient.BaseAddress!,
                "books?languages=en&search="
                    + Uri.EscapeDataString(query)),
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Project Gutenberg catalog returned no data.");

        return response.Results
            .Select(MapGutenberg)
            .Take(SearchLimit)
            .ToArray();
    }

    private async Task<BookCatalogItem?> GetGutenbergAsync(
        int id,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsyncWithTimeout(
            new HttpRequestMessage(
                HttpMethod.Get,
                $"books/{id}"),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var book = await response.Content
            .ReadFromJsonAsync<GutendexBook>(
                cancellationToken: cancellationToken);

        return book is null
            ? null
            : MapGutenberg(book);
    }

    private async Task<BookCatalogItem?> GetOpenLibraryAsync(
        string workKey,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsyncWithTimeout(
            new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(
                    $"https://openlibrary.org/works/{Uri.EscapeDataString(workKey)}.json")),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var work = await response.Content
            .ReadFromJsonAsync<OpenLibraryWork>(
                cancellationToken: cancellationToken);

        if (work is null
            || string.IsNullOrWhiteSpace(work.Title))
        {
            return null;
        }

        var author = await ResolveOpenLibraryAuthorsAsync(
            work.Authors,
            cancellationToken);

        var coverId = work.Covers?
            .FirstOrDefault(x => x > 0);

        return new BookCatalogItem(
            "ol-" + workKey,
            work.Title.Trim(),
            string.IsNullOrWhiteSpace(author)
                ? null
                : author,
            ExtractDescription(work.Description),
            coverId is > 0
                ? $"https://covers.openlibrary.org/b/id/{coverId}-L.jpg"
                : null,
            (work.Subjects ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Take(16)
                .ToArray(),
            ParseYear(work.FirstPublishDate),
            null,
            null,
            $"https://openlibrary.org/works/{workKey}",
            "Open Library",
            null);
    }

    private async Task<string?> ResolveOpenLibraryAuthorsAsync(
        OpenLibraryAuthorReference[]? authorReferences,
        CancellationToken cancellationToken)
    {
        var keys = (authorReferences ?? [])
            .Select(x => x.Author?.Key)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();

        if (keys.Length == 0)
        {
            return null;
        }

        var names = new List<string>(keys.Length);
        foreach (var key in keys)
        {
            try
            {
                var author = await GetJsonAsync<OpenLibraryAuthor>(
                    new Uri(
                        $"https://openlibrary.org{key}.json"),
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(author?.Name))
                {
                    names.Add(author.Name.Trim());
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException
                    or TaskCanceledException)
            {
                // Optional metadata lookup.
            }
        }

        return names.Count == 0
            ? null
            : string.Join(", ", names);
    }

    private async Task<BookCatalogItem?> FindGutenbergMatchAsync(
        string title,
        string? author,
        CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(author)
            ? title
            : $"{title} {author}";

        var response = await GetJsonAsync<GutendexListResponse>(
            new Uri(
                httpClient.BaseAddress!,
                "books?languages=en&search="
                    + Uri.EscapeDataString(query)),
            cancellationToken);

        return response?.Results
            .Where(x => IsLikelyMatch(
                x,
                title,
                author))
            .Select(MapGutenberg)
            .FirstOrDefault(x => x.CanAcquire);
    }

    private async Task<T?> GetJsonAsync<T>(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsyncWithTimeout(
            new HttpRequestMessage(
                HttpMethod.Get,
                uri),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<T>(
                cancellationToken: cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsyncWithTimeout(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(CatalogRequestTimeout);

        return await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
    }

    private async Task<byte[]> DownloadGutenbergFileAsync(
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        if (!IsAllowedGutenbergUri(initialUri))
        {
            throw new InvalidOperationException(
                "The ebook download source is not trusted.");
        }

        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(EpubDownloadTimeout);

        var currentUri = initialUri;
        for (var redirect = 0;
             redirect <= MaxRedirects;
             redirect++)
        {
            using var response = await httpClient.SendAsync(
                new HttpRequestMessage(
                    HttpMethod.Get,
                    currentUri),
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            if (IsRedirect(response.StatusCode))
            {
                if (redirect == MaxRedirects
                    || response.Headers.Location is null)
                {
                    throw new InvalidOperationException(
                        "The ebook download redirected too many times.");
                }

                currentUri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(
                        currentUri,
                        response.Headers.Location);

                if (!IsAllowedGutenbergUri(currentUri))
                {
                    throw new InvalidOperationException(
                        "The ebook download redirected to an untrusted host.");
                }

                continue;
            }

            response.EnsureSuccessStatusCode();

            var length = response.Content.Headers.ContentLength;
            if (length is > MaxEpubBytes)
            {
                throw new InvalidOperationException(
                    "EPUB exceeds the 100 MB import limit.");
            }

            await using var stream =
                await response.Content.ReadAsStreamAsync(
                    timeout.Token);
            using var memory = await CopyToMemoryBoundedAsync(
                stream,
                MaxEpubBytes,
                timeout.Token);
            return memory.ToArray();
        }

        throw new InvalidOperationException(
            "The ebook download could not be completed.");
    }

    private async Task<(byte[] Bytes, Uri FinalUri)> DownloadExternalEpubAsync(
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeout.CancelAfter(EpubDownloadTimeout);

        var currentUri = initialUri;
        for (var redirect = 0;
             redirect <= MaxRedirects;
             redirect++)
        {
            await ValidateExternalEpubUriAsync(
                currentUri,
                timeout.Token);

            using var response = await httpClient.SendAsync(
                new HttpRequestMessage(
                    HttpMethod.Get,
                    currentUri),
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            if (IsRedirect(response.StatusCode))
            {
                if (redirect == MaxRedirects
                    || response.Headers.Location is null)
                {
                    throw new InvalidOperationException(
                        "The EPUB download redirected too many times.");
                }

                currentUri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(
                        currentUri,
                        response.Headers.Location);
                continue;
            }

            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > MaxEpubBytes)
            {
                throw new InvalidOperationException(
                    "EPUB exceeds the 100 MB import limit.");
            }

            await using var stream =
                await response.Content.ReadAsStreamAsync(
                    timeout.Token);
            using var memory = await CopyToMemoryBoundedAsync(
                stream,
                MaxEpubBytes,
                timeout.Token);

            var bytes = memory.ToArray();
            if (bytes.Length < 4
                || bytes[0] != (byte)'P'
                || bytes[1] != (byte)'K')
            {
                throw new InvalidOperationException(
                    "The remote URL did not return an EPUB/ZIP file.");
            }

            return (bytes, currentUri);
        }

        throw new InvalidOperationException(
            "The EPUB download could not be completed.");
    }

    public static void ValidateExternalEpubUriSyntax(Uri uri)
    {
        if (!uri.Scheme.Equals(
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Remote EPUB imports require HTTPS.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host)
            || uri.UserInfo.Length > 0)
        {
            throw new InvalidOperationException(
                "Remote EPUB URL is not allowed.");
        }

        if (IPAddress.TryParse(
                uri.Host,
                out var literal)
            && IsPrivateOrSpecialAddress(literal))
        {
            throw new InvalidOperationException(
                "Remote EPUB URL must not target a private or local address.");
        }
    }

    private static async Task ValidateExternalEpubUriAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        ValidateExternalEpubUriSyntax(uri);

        if (IPAddress.TryParse(
                uri.Host,
                out _))
        {
            return;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(
                uri.DnsSafeHost,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is System.Net.Sockets.SocketException
                or ArgumentException)
        {
            throw new InvalidOperationException(
                "Remote EPUB host could not be resolved.",
                exception);
        }

        if (addresses.Length == 0
            || addresses.Any(IsPrivateOrSpecialAddress))
        {
            throw new InvalidOperationException(
                "Remote EPUB host resolves to a private or local address.");
        }
    }

    private static bool IsPrivateOrSpecialAddress(
        IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return IsPrivateOrSpecialAddress(
                address.MapToIPv4());
        }

        if (address.AddressFamily
            == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast
                || (bytes[0] & 0xfe) == 0xfc;
        }

        var octets = address.GetAddressBytes();
        if (octets.Length != 4)
        {
            return true;
        }

        return octets[0] == 0
            || octets[0] == 10
            || octets[0] == 127
            || (octets[0] == 100
                && octets[1] is >= 64 and <= 127)
            || (octets[0] == 169
                && octets[1] == 254)
            || (octets[0] == 172
                && octets[1] is >= 16 and <= 31)
            || (octets[0] == 192
                && octets[1] == 168)
            || (octets[0] == 198
                && octets[1] is 18 or 19)
            || octets[0] >= 224;
    }

    private async Task<string> GetTextFollowingRedirectsAsync(
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        if (!IsAllowedGutenbergUri(initialUri))
        {
            throw new InvalidOperationException(
                "The readable text source is not trusted.");
        }

        var currentUri = initialUri;

        for (var redirect = 0;
             redirect <= MaxRedirects;
             redirect++)
        {
            using var response = await SendAsyncWithTimeout(
                new HttpRequestMessage(
                    HttpMethod.Get,
                    currentUri),
                cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                if (redirect == MaxRedirects
                    || response.Headers.Location is null)
                {
                    throw new InvalidOperationException(
                        "The book text source redirected too many times.");
                }

                currentUri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(
                        currentUri,
                        response.Headers.Location);

                if (!IsAllowedGutenbergUri(currentUri))
                {
                    throw new InvalidOperationException(
                        "The book text source redirected to an untrusted host.");
                }

                continue;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content
                .ReadAsStringAsync(cancellationToken);
        }

        throw new InvalidOperationException(
            "The book text source could not be loaded.");
    }

    private static BookCatalogItem MapOpenLibrarySearch(
        OpenLibrarySearchDoc book)
    {
        var workKey = book.Key!["/works/".Length..];
        var author = book.AuthorName is { Length: > 0 }
            ? string.Join(
                ", ",
                book.AuthorName.Where(x =>
                    !string.IsNullOrWhiteSpace(x)))
            : null;

        return new BookCatalogItem(
            "ol-" + workKey,
            book.Title!.Trim(),
            string.IsNullOrWhiteSpace(author)
                ? null
                : author,
            null,
            book.CoverId is > 0
                ? $"https://covers.openlibrary.org/b/id/{book.CoverId}-M.jpg"
                : null,
            (book.Subjects ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Take(16)
                .ToArray(),
            book.FirstPublishYear,
            null,
            null,
            $"https://openlibrary.org/works/{workKey}",
            "Open Library",
            null);
    }

    private static BookCatalogItem MapGutenberg(
        GutendexBook book)
    {
        var author = string.Join(
            ", ",
            book.Authors
                .Select(x => x.Name?.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        var cover = book.Formats
            .Where(x => x.Key.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Key.Equals(
                "image/jpeg",
                StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x));

        var textUrl = book.Formats
            .Where(x => x.Key.StartsWith(
                "text/plain",
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Key.Contains(
                "utf-8",
                StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x));

        var epubUrl = book.Formats
            .Where(x => x.Key.Equals(
                "application/epub+zip",
                StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x));

        var summary = book.Summaries
            .FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x))
            ?.Trim();

        return new BookCatalogItem(
            book.Id.ToString(
                CultureInfo.InvariantCulture),
            book.Title.Trim(),
            string.IsNullOrWhiteSpace(author)
                ? null
                : author,
            summary,
            cover,
            book.Subjects
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Take(16)
                .ToArray(),
            null,
            textUrl,
            epubUrl,
            $"https://www.gutenberg.org/ebooks/{book.Id}",
            "Project Gutenberg",
            textUrl is null
                ? null
                : "Project Gutenberg");
    }

    private static bool IsLikelyMatch(
        GutendexBook candidate,
        string title,
        string? author)
    {
        var expectedTitle = NormalizeForMatch(title);
        var candidateTitle = NormalizeForMatch(
            candidate.Title);

        if (expectedTitle.Length == 0
            || (!candidateTitle.Contains(
                    expectedTitle,
                    StringComparison.Ordinal)
                && !expectedTitle.Contains(
                    candidateTitle,
                    StringComparison.Ordinal)))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(author))
        {
            return true;
        }

        var expectedAuthorParts =
            NormalizeForMatch(author)
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);
        var surname = expectedAuthorParts.LastOrDefault();

        return string.IsNullOrWhiteSpace(surname)
            || candidate.Authors.Any(x =>
                NormalizeForMatch(x.Name ?? "")
                    .Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Contains(
                        surname,
                        StringComparer.Ordinal));
    }

    private static string BuildTranslationContext(
        NovelWork work,
        NovelChapter chapter,
        string? previousSource,
        string? previousTarget,
        string? nextSource,
        string targetLanguage)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            $"Book: {work.MetadataTitle ?? work.Title}");
        if (!string.IsNullOrWhiteSpace(work.Author))
        {
            builder.AppendLine(
                $"Author: {work.Author}");
        }

        if (!string.IsNullOrWhiteSpace(work.Description))
        {
            builder.AppendLine(
                $"Book description: {Truncate(work.Description, 1800)}");
        }

        var genres = ParseGenres(
            work.MetadataGenresJson);
        if (genres.Count > 0)
        {
            builder.AppendLine(
                "Genres/themes: "
                + string.Join(", ", genres.Take(10)));
        }

        builder.AppendLine(
            $"Chapter {chapter.Number}: {chapter.Title}");

        if (targetLanguage.Equals(
                "id-modern",
                StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine(
                "Target variant: Modern Indonesian. Use current Indonesian spelling and natural contemporary wording where the source is archaic, while preserving meaning, historical setting, names, relationships, tone, dialogue voice and literary atmosphere.");
        }

        if (!string.IsNullOrWhiteSpace(previousSource))
        {
            builder.AppendLine(
                "Previous source chapter ending (semantic context only; do not translate it):");
            builder.AppendLine(
                Tail(previousSource, 1600));
        }

        if (!string.IsNullOrWhiteSpace(previousTarget))
        {
            builder.AppendLine(
                $"Previously established {BookLanguageCatalog.GetName(targetLanguage)} translation ending "
                + "(translation-memory context only; do not repeat it):");
            builder.AppendLine(
                Tail(previousTarget, 1800));
        }

        if (!string.IsNullOrWhiteSpace(nextSource))
        {
            builder.AppendLine(
                "Next source chapter opening (disambiguation context only; do not translate it):");
            builder.AppendLine(
                Head(nextSource, 900));
        }

        return builder.ToString();
    }

    public string? GetLocalCoverPath(Guid workId)
    {
        var directory = Path.Combine(
            "/data",
            "books",
            "covers");

        if (!Directory.Exists(directory))
        {
            return null;
        }

        var prefix = workId.ToString("N") + ".";
        return Directory
            .EnumerateFiles(
                directory,
                prefix + "*",
                SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
    }

    public static string GetCoverContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };

    private static async Task<string?> SaveLocalCoverAsync(
        Guid workId,
        byte[] bytes,
        string mediaType,
        CancellationToken cancellationToken)
    {
        if (bytes.Length == 0 || bytes.Length > 10 * 1024 * 1024)
        {
            return null;
        }

        var extension = mediaType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/jpeg" or "image/jpg" => ".jpg",
            _ => null
        };

        if (extension is null)
        {
            return null;
        }

        var directory = Path.Combine(
            "/data",
            "books",
            "covers");
        Directory.CreateDirectory(directory);

        foreach (var stale in Directory.EnumerateFiles(
                     directory,
                     workId.ToString("N") + ".*",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                File.Delete(stale);
            }
            catch (IOException)
            {
            }
        }

        var path = Path.Combine(
            directory,
            workId.ToString("N") + extension);

        await File.WriteAllBytesAsync(
            path,
            bytes,
            cancellationToken);
        return path;
    }

    private bool TryGetInboxPath(
        out string inboxPath)
    {
        var stored = BookIntegrationSettingsStore.Load();
        var configured = FirstNonEmpty(
            configuration["Books:InboxPath"],
            stored.InboxPath);

        if (string.IsNullOrWhiteSpace(configured))
        {
            inboxPath = "";
            return false;
        }

        inboxPath = Path.GetFullPath(configured);
        return true;
    }

    private bool TryGetSabnzbdConfiguration(
        out Uri baseUri,
        out string apiKey,
        out string category)
    {
        var stored = BookIntegrationSettingsStore.Load();
        var rawBase = FirstNonEmpty(
            configuration["Books:SABnzbd:BaseUrl"],
            stored.SabnzbdBaseUrl);
        apiKey = FirstNonEmpty(
            configuration["Books:SABnzbd:ApiKey"],
            stored.SabnzbdApiKey)
            ?? "";
        category = FirstNonEmpty(
            configuration["Books:SABnzbd:Category"],
            stored.SabnzbdCategory)
            ?? "";

        if (!Uri.TryCreate(
                rawBase?.TrimEnd('/') + "/",
                UriKind.Absolute,
                out baseUri!)
            || baseUri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(apiKey))
        {
            baseUri = new Uri(
                "http://localhost/",
                UriKind.Absolute);
            return false;
        }

        return true;
    }

    private static string? FirstNonEmpty(
        string? primary,
        string? fallback)
    {
        var cleanPrimary = primary?.Trim();
        return string.IsNullOrWhiteSpace(cleanPrimary)
            ? fallback?.Trim()
            : cleanPrimary;
    }

    private static SabnzbdSubmissionResult ParseSabResponse(
        string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.TryGetProperty(
                    "status",
                    out var status)
                && status.ValueKind is
                    JsonValueKind.True or JsonValueKind.False)
            {
                var accepted = status.GetBoolean();
                var message =
                    root.TryGetProperty(
                        "error",
                        out var error)
                    && error.ValueKind == JsonValueKind.String
                        ? error.GetString()
                        : null;

                return new SabnzbdSubmissionResult(
                    accepted,
                    accepted
                        ? "Sent to SABnzbd."
                        : message
                            ?? "SABnzbd rejected the request.");
            }
        }
        catch (JsonException)
        {
            // Fall back to the textual response below.
        }

        var ok = body.Contains(
            "ok",
            StringComparison.OrdinalIgnoreCase);
        return new SabnzbdSubmissionResult(
            ok,
            ok
                ? "Sent to SABnzbd."
                : "SABnzbd returned an unexpected response.");
    }

    private static string BuildQuery(
        params (string Key, string Value)[] values) =>
        string.Join(
            "&",
            values
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .Select(x =>
                    Uri.EscapeDataString(x.Key)
                    + "="
                    + Uri.EscapeDataString(x.Value)));

    private static string GetSourceLanguage(
        NovelWork work)
    {
        if (!string.IsNullOrWhiteSpace(work.Format)
            && work.Format.StartsWith(
                "EPUB:",
                StringComparison.OrdinalIgnoreCase))
        {
            var language = work.Format[5..]
                .Trim()
                .ToLowerInvariant();
            if (language.Length > 0)
            {
                return language;
            }
        }

        return "en";
    }

    private static string NormalizeSourceLanguage(
        string? language)
    {
        var normalized = language?
            .Trim()
            .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "en";
        }

        var separator = normalized.IndexOfAny(
            ['-', '_']);
        if (separator > 0)
        {
            normalized = normalized[..separator];
        }

        return normalized.Length <= 8
            ? normalized
            : "und";
    }

    private static string NormalizeAnchorLanguage(
        string? language)
    {
        var normalized = language?
            .Trim()
            .ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "original";
        }

        return normalized.Length <= 16
            ? normalized
            : normalized[..16];
    }

    private static IReadOnlyList<string> ParseGenres(
        string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(json)
                ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string CleanSourceKey(string value)
    {
        var clean = Regex.Replace(
                value.Trim().ToLowerInvariant(),
                @"[^a-z0-9._-]+",
                "-")
            .Trim('-');

        if (clean.Length == 0)
        {
            clean = "book";
        }

        return clean.Length <= 80
            ? clean
            : clean[..80];
    }

    private static string NormalizeProvider(string value)
    {
        var normalized = Regex.Replace(
                value.Trim().ToLowerInvariant(),
                @"[^a-z0-9]+",
                "-")
            .Trim('-');

        return Truncate(
            "books-" + normalized,
            80);
    }

    private static string NormalizeForMatch(string value) =>
        Regex.Replace(
                value.ToLowerInvariant(),
                @"[^\p{L}\p{N}]+",
                " ")
            .Trim();

    private static bool TryParseGutenbergId(
        string id,
        out int gutenbergId)
    {
        if (id.StartsWith(
                "pg-",
                StringComparison.OrdinalIgnoreCase))
        {
            id = id[3..];
        }

        return int.TryParse(
            id,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out gutenbergId)
            && gutenbergId > 0;
    }

    private static bool TryParseOpenLibraryId(
        string id,
        out string workKey)
    {
        workKey = "";

        if (!id.StartsWith(
                "ol-",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = id[3..].Trim();
        if (!Regex.IsMatch(
                candidate,
                @"^OL\d+W$",
                RegexOptions.IgnoreCase))
        {
            return false;
        }

        workKey = candidate;
        return true;
    }

    private static string? ExtractDescription(
        JsonElement description)
    {
        if (description.ValueKind == JsonValueKind.String)
        {
            return description.GetString()?.Trim();
        }

        if (description.ValueKind == JsonValueKind.Object
            && description.TryGetProperty(
                "value",
                out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()?.Trim();
        }

        return null;
    }

    private static int? ParseYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = Regex.Match(
            value,
            @"\b(1[0-9]{3}|20[0-9]{2})\b");

        return match.Success
            && int.TryParse(
                match.Value,
                out var year)
            ? year
            : null;
    }

    private static bool IsRedirect(
        HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static bool IsAllowedGutenbergUri(Uri uri) =>
        uri.Scheme.Equals(
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase)
        && (uri.Host.Equals(
                "gutenberg.org",
                StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(
                ".gutenberg.org",
                StringComparison.OrdinalIgnoreCase));

    private static string Hash(string value) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(value)));

    private static string Head(
        string value,
        int maxLength) =>
        value.Length <= maxLength
            ? value
            : value[..maxLength];

    private static string Tail(
        string value,
        int maxLength) =>
        value.Length <= maxLength
            ? value
            : value[^maxLength..];

    private static string Truncate(
        string value,
        int maxLength) =>
        value.Length <= maxLength
            ? value
            : value[..maxLength];

    private static string? TruncateNullable(
        string? value,
        int maxLength)
    {
        var clean = value?.Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return null;
        }

        return clean.Length <= maxLength
            ? clean
            : clean[..maxLength];
    }

    private static async Task<MemoryStream> CopyToMemoryBoundedAsync(
        Stream input,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var result = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;

        while (true)
        {
            var read = await input.ReadAsync(
                buffer,
                cancellationToken);
            if (read <= 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                result.Dispose();
                throw new InvalidOperationException(
                    "File exceeds the 100 MB import limit.");
            }

            await result.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken);
        }

        result.Position = 0;
        return result;
    }

    private sealed record GutendexListResponse(
        int Count,
        GutendexBook[] Results);

    private sealed record GutendexBook(
        int Id,
        string Title,
        string[] Subjects,
        GutendexPerson[] Authors,
        string[] Summaries,
        Dictionary<string, string> Formats,
        [property: JsonPropertyName("download_count")]
        int DownloadCount);

    private sealed record GutendexPerson(
        string? Name);

    private sealed record OpenLibrarySearchResponse(
        [property: JsonPropertyName("docs")]
        OpenLibrarySearchDoc[] Docs);

    private sealed record OpenLibrarySearchDoc(
        [property: JsonPropertyName("key")]
        string? Key,
        [property: JsonPropertyName("title")]
        string? Title,
        [property: JsonPropertyName("author_name")]
        string[]? AuthorName,
        [property: JsonPropertyName("cover_i")]
        int? CoverId,
        [property: JsonPropertyName("first_publish_year")]
        int? FirstPublishYear,
        [property: JsonPropertyName("subject")]
        string[]? Subjects);

    private sealed record OpenLibraryWork(
        [property: JsonPropertyName("title")]
        string Title,
        [property: JsonPropertyName("description")]
        JsonElement Description,
        [property: JsonPropertyName("subjects")]
        string[]? Subjects,
        [property: JsonPropertyName("covers")]
        int[]? Covers,
        [property: JsonPropertyName("first_publish_date")]
        string? FirstPublishDate,
        [property: JsonPropertyName("authors")]
        OpenLibraryAuthorReference[]? Authors);

    private sealed record OpenLibraryAuthorReference(
        [property: JsonPropertyName("author")]
        OpenLibraryKeyReference? Author);

    private sealed record OpenLibraryKeyReference(
        [property: JsonPropertyName("key")]
        string? Key);

    private sealed record OpenLibraryAuthor(
        [property: JsonPropertyName("name")]
        string? Name);
}
