using System.Text.Json;
using Jularr.Web.Data;
using Jularr.Web.Features.Ai;
using Jularr.Web.Features.Auth;
using Jularr.Web.Features.StoryContext;
using Jularr.Web.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.ChapterArtwork;

public static class ChapterArtworkUnavailableReasons
{
    public const string DisabledGlobally = "disabled-globally";
    public const string DisabledForProfile = "disabled-for-profile";
    public const string DisabledForBook = "disabled-for-book";
    public const string StorageNotConfigured = "storage-not-configured";
    public const string StorageUnavailable = "storage-unavailable";
}

public sealed record ChapterArtworkAvailability(
    bool CanView,
    bool CanGenerate,
    string? Reason);

public enum ChapterArtworkRequestOutcome
{
    Queued,
    Unavailable,
    ChapterNotFound,
    AlreadyRunning,
    AlreadyExists
}

public sealed record ChapterArtworkRequestResult(
    ChapterArtworkRequestOutcome Outcome,
    IReadOnlyList<Guid> ArtworkIds,
    string? Reason = null);

public sealed record ChapterArtworkImageFile(
    string Path,
    string MediaType);

/// <summary>
/// Spoiler-safe chapter artwork: builds the image prompt for chapter N only
/// from the shared story context known before chapter N (#412), checks the
/// finished prompt a second time, stores images on media storage with their
/// generation metadata, and never replaces an image unless asked to.
/// </summary>
public sealed class ChapterArtworkService(
    AppDbContext db,
    ChapterArtworkStore store,
    StoryContextService storyContext,
    StoryContextStore storyStore,
    IAiImageGenerator images,
    ChapterArtworkGlobalSettingsStore globalSettings,
    IConfiguration configuration,
    ILogger<ChapterArtworkService>? logger = null)
{
    public const string Operation = "chapter-artwork";
    private const int ContextExtractionChapters = 3;

    /// <summary>
    /// A service whose text and image AI calls run on behalf of
    /// <paramref name="profileId"/> (background jobs have no request).
    /// </summary>
    public static ChapterArtworkService ForProfile(IServiceProvider services, string profileId)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var router = new ProfileAiProviderRouter(
            CurrentAccountContext.ForProfile(profileId),
            services.GetRequiredService<AiProfileSettingsStore>(),
            services.GetRequiredService<AiUsageTracker>(),
            services.GetRequiredService<CodexCliProvider>(),
            services.GetRequiredService<IHttpClientFactory>());

        return new ChapterArtworkService(
            db,
            new ChapterArtworkStore(db),
            new StoryContextService(
                db,
                services.GetRequiredService<StoryContextStore>(),
                services.GetRequiredService<StoryContextSnapshotCache>(),
                router),
            services.GetRequiredService<StoryContextStore>(),
            services.GetRequiredService<IAiImageGenerator>(),
            services.GetRequiredService<ChapterArtworkGlobalSettingsStore>(),
            services.GetRequiredService<IConfiguration>(),
            services.GetService<ILogger<ChapterArtworkService>>());
    }

    public ChapterArtworkStorage Storage =>
        ChapterArtworkStorage.Create(globalSettings.Load(), configuration);

    public async Task<ChapterArtworkAvailability> GetAvailabilityAsync(
        string profileId,
        Guid? workId,
        CancellationToken cancellationToken)
    {
        var global = globalSettings.Load();
        if (!global.Enabled)
        {
            return new(false, false, ChapterArtworkUnavailableReasons.DisabledGlobally);
        }

        var preferences = await store.GetPreferencesAsync(profileId, cancellationToken);
        if (!preferences.Enabled)
        {
            return new(false, false, ChapterArtworkUnavailableReasons.DisabledForProfile);
        }

        if (workId is Guid work
            && (await store.GetWorkSettingsAsync(work, cancellationToken)).Enabled == false)
        {
            return new(false, false, ChapterArtworkUnavailableReasons.DisabledForBook);
        }

        var storage = ChapterArtworkStorage.Create(global, configuration);
        if (!storage.IsConfigured)
        {
            return new(true, false, ChapterArtworkUnavailableReasons.StorageNotConfigured);
        }

        var image = await images.GetAvailabilityAsync(profileId, cancellationToken);
        if (!image.IsAvailable)
        {
            return new(true, false, image.Reason);
        }

        return storage.IsAvailable
            ? new(true, true, null)
            : new(true, false, ChapterArtworkUnavailableReasons.StorageUnavailable);
    }

    /// <summary>
    /// Reserves queued rows for one generation. An existing image (accepted or
    /// preview) is only replaced when <paramref name="regenerate"/> is set.
    /// </summary>
    public async Task<ChapterArtworkRequestResult> RequestAsync(
        Guid workId,
        int chapterNumber,
        string profileId,
        bool regenerate,
        CancellationToken cancellationToken)
    {
        var availability = await GetAvailabilityAsync(profileId, workId, cancellationToken);
        if (!availability.CanGenerate)
        {
            return new(ChapterArtworkRequestOutcome.Unavailable, [], availability.Reason);
        }

        var chapter = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId && x.Number == chapterNumber)
            .Select(x => new { x.Id, x.Number })
            .SingleOrDefaultAsync(cancellationToken);
        if (chapter is null)
        {
            return new(ChapterArtworkRequestOutcome.ChapterNotFound, []);
        }

        var existing = await store.ListChapterAsync(workId, chapterNumber, cancellationToken);
        if (existing.Any(x => x.IsActive))
        {
            return new(ChapterArtworkRequestOutcome.AlreadyRunning, []);
        }

        if (!regenerate && existing.Any(x => x.HasImage))
        {
            return new(ChapterArtworkRequestOutcome.AlreadyExists, []);
        }

        var preferences = await store.GetPreferencesAsync(profileId, cancellationToken);
        var workSettings = await store.GetWorkSettingsAsync(workId, cancellationToken);
        var now = DateTime.UtcNow;
        var ids = new List<Guid>();

        for (var variant = 0; variant < Math.Clamp(preferences.Variations, 1, ChapterArtworkPreferences.MaxVariations); variant++)
        {
            var item = new ChapterArtworkItem(
                Guid.NewGuid(),
                workId,
                chapter.Id,
                chapter.Number,
                ChapterArtworkStatus.Queued,
                AssetPath: null,
                MediaType: null,
                ContentHash: null,
                ByteSize: null,
                ProviderId: null,
                Model: null,
                ChapterArtworkPromptComposer.PromptVersion,
                ContextHash: null,
                ChapterArtworkPromptComposer.ContextScope,
                Prompt: null,
                NeutralPrompt: false,
                workSettings.Style ?? preferences.Style,
                preferences.Quality,
                Error: null,
                OperationId: null,
                profileId,
                now,
                now,
                AcceptedAt: null);

            await store.InsertAsync(item, cancellationToken);
            ids.Add(item.Id);
        }

        // Failed or cancelled attempts are superseded by the new request.
        foreach (var stale in existing.Where(x => x.Status is ChapterArtworkStatus.Failed or ChapterArtworkStatus.Cancelled))
        {
            await store.DeleteAsync(stale.Id, cancellationToken);
        }

        return new(ChapterArtworkRequestOutcome.Queued, ids);
    }

    public async Task AttachOperationAsync(
        IReadOnlyList<Guid> artworkIds,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        foreach (var id in artworkIds)
        {
            if (await store.GetAsync(id, cancellationToken) is { IsActive: true } item)
            {
                await store.UpdateAsync(item with { OperationId = operationId, UpdatedAt = DateTime.UtcNow }, cancellationToken);
            }
        }
    }

    /// <summary>Runs one reserved generation. Cancellation leaves no half-written file behind.</summary>
    public async Task GenerateAsync(
        IReadOnlyList<Guid> artworkIds,
        CancellationToken cancellationToken)
    {
        var rows = new List<ChapterArtworkItem>();
        foreach (var id in artworkIds)
        {
            if (await store.GetAsync(id, cancellationToken) is { Status: ChapterArtworkStatus.Queued } row)
            {
                rows.Add(row with { Status = ChapterArtworkStatus.Generating, UpdatedAt = DateTime.UtcNow });
                await store.UpdateAsync(rows[^1], cancellationToken);
            }
        }

        if (rows.Count == 0)
        {
            return;
        }

        var first = rows[0];
        var completed = new HashSet<Guid>();
        try
        {
            var prepared = await PreparePromptAsync(first.WorkId, first.ChapterNumber, first.Style, cancellationToken);
            var storage = Storage;
            if (!storage.IsAvailable)
            {
                throw new InvalidOperationException("The chapter artwork folder is not reachable.");
            }

            var result = await images.GenerateAsync(
                first.RequestedByProfileId,
                new AiImageRequest(Operation, prepared.Prompt.Text, AiImageLayout.Landscape, first.Quality, rows.Count),
                cancellationToken);

            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (index >= result.Images.Count)
                {
                    await store.UpdateAsync(
                        row with
                        {
                            Status = ChapterArtworkStatus.Failed,
                            Error = "The image provider returned fewer images than requested.",
                            UpdatedAt = DateTime.UtcNow
                        },
                        CancellationToken.None);
                    completed.Add(row.Id);
                    continue;
                }

                var saved = await storage.SaveCandidateAsync(
                    row.WorkId,
                    prepared.WorkTitle,
                    row.ChapterNumber,
                    row.Id,
                    result.Images[index],
                    cancellationToken);

                await store.UpdateAsync(
                    row with
                    {
                        Status = ChapterArtworkStatus.Preview,
                        AssetPath = saved.AssetPath,
                        MediaType = saved.MediaType,
                        ContentHash = saved.ContentHash,
                        ByteSize = saved.ByteSize,
                        ProviderId = result.ProviderId,
                        Model = result.Model,
                        ContextHash = prepared.Prompt.ContextHash,
                        Prompt = prepared.Prompt.Text,
                        NeutralPrompt = prepared.Prompt.Neutral,
                        Error = null,
                        UpdatedAt = DateTime.UtcNow
                    },
                    CancellationToken.None);
                completed.Add(row.Id);
            }
        }
        catch (OperationCanceledException)
        {
            await FinishAsync(rows, completed, ChapterArtworkStatus.Cancelled, null);
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Chapter artwork generation failed for work {WorkId} chapter {Chapter}.", first.WorkId, first.ChapterNumber);
            await FinishAsync(rows, completed, ChapterArtworkStatus.Failed, exception.Message);
            throw;
        }
    }

    public sealed record PreparedPrompt(
        ChapterArtworkPrompt Prompt,
        string WorkTitle);

    /// <summary>
    /// Builds and checks the prompt for one chapter. Uses (and incrementally
    /// extends) the shared story context known before the chapter; falls back
    /// to a neutral prompt when the story-based one fails the spoiler check.
    /// </summary>
    public async Task<PreparedPrompt> PreparePromptAsync(
        Guid workId,
        int chapterNumber,
        ChapterArtworkStyle style,
        CancellationToken cancellationToken)
    {
        var work = await db.NovelWorks
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == workId, cancellationToken)
            ?? throw new InvalidOperationException("The book was not found.");
        var chapter = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId && x.Number == chapterNumber)
            .Select(x => new { x.Number, x.Title, x.OriginalText })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The chapter was not found.");
        var workSettings = await store.GetWorkSettingsAsync(workId, cancellationToken);

        if (chapterNumber > 1)
        {
            try
            {
                await storyContext.ExtractThroughAsync(
                    workId,
                    fromChapter: Math.Max(1, chapterNumber - ContextExtractionChapters),
                    throughChapter: chapterNumber - 1,
                    maxChapters: ContextExtractionChapters,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Missing story memory only makes the image more neutral.
                logger?.LogInformation(exception, "Story context extraction skipped for work {WorkId}.", workId);
            }
        }

        var snapshot = await storyContext.GetSnapshotAsync(
            workId,
            ChapterArtworkPromptComposer.ContextQuery(chapterNumber),
            cancellationToken);
        var full = await storyStore.LoadAsync(workId, cancellationToken);

        var title = work.MetadataTitle ?? work.Title;
        var genres = ParseGenres(work.MetadataGenresJson);
        var chapterTitle = workSettings.UseChapterTitles ? chapter.Title : null;

        var input = new ChapterArtworkPromptInput(
            title,
            genres,
            chapterNumber,
            chapterTitle,
            style,
            workSettings.SeriesStyle,
            snapshot);

        var laterChapters = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId && x.Number >= chapterNumber)
            .OrderBy(x => x.Number)
            .Select(x => new { x.Number, x.Title })
            .ToListAsync(cancellationToken);
        var hiddenTitles = laterChapters
            .Where(x => !(workSettings.UseChapterTitles && x.Number == chapterNumber))
            .Select(x => x.Title.Trim())
            .Where(x => x.Length >= 4)
            .ToArray();

        var nextText = await db.NovelChapters
            .AsNoTracking()
            .Where(x => x.WorkId == workId && x.Number > chapterNumber)
            .OrderBy(x => x.Number)
            .Select(x => x.OriginalText)
            .FirstOrDefaultAsync(cancellationToken);

        var protectedTexts = new List<string> { chapter.OriginalText, nextText ?? "" };
        protectedTexts.AddRange(
            (full?.Chapters ?? [])
                .Where(x => x.Number >= chapterNumber)
                .SelectMany(x => new[] { x.Summary ?? "", x.ContinuityNotes ?? "" }));

        var forbidden = ChapterArtworkSpoilerGuard.ForbiddenNames(
            full,
            snapshot,
            chapterNumber,
            hiddenTitles,
            [title, string.Join(", ", genres), workSettings.SeriesStyle ?? "", chapterTitle ?? ""]);

        var prompt = ChapterArtworkPromptComposer.Compose(input);
        var check = ChapterArtworkSpoilerGuard.Check(prompt.Text, forbidden, protectedTexts);
        if (!check.Passed && !prompt.Neutral)
        {
            prompt = ChapterArtworkPromptComposer.Compose(input, forceNeutral: true);
            check = ChapterArtworkSpoilerGuard.Check(prompt.Text, forbidden, protectedTexts);
        }

        if (!check.Passed)
        {
            throw new InvalidOperationException(
                "The spoiler check blocked the image prompt. Check the series art direction and chapter title settings.");
        }

        return new PreparedPrompt(prompt, title);
    }

    public async Task<bool> AcceptAsync(Guid artworkId, CancellationToken cancellationToken)
    {
        var item = await store.GetAsync(artworkId, cancellationToken);
        var storage = Storage;
        if (item is not { Status: ChapterArtworkStatus.Preview, AssetPath: { } candidate }
            || !storage.Exists(candidate))
        {
            return false;
        }

        var chapterItems = await store.ListChapterAsync(item.WorkId, item.ChapterNumber, cancellationToken);
        foreach (var other in chapterItems.Where(x => x.Id != item.Id && !x.IsActive))
        {
            if (other.AssetPath != candidate)
            {
                storage.Delete(other.Status == ChapterArtworkStatus.Accepted ? null : other.AssetPath);
            }

            storage.DeleteDerivatives(other.ContentHash);
            await store.DeleteAsync(other.Id, cancellationToken);
        }

        var accepted = storage.Promote(candidate, item.ChapterNumber);
        var now = DateTime.UtcNow;
        await store.UpdateAsync(
            item with
            {
                Status = ChapterArtworkStatus.Accepted,
                AssetPath = accepted,
                AcceptedAt = now,
                UpdatedAt = now
            },
            cancellationToken);
        return true;
    }

    /// <summary>Removes an image (accepted or preview) and its derivatives.</summary>
    public async Task<bool> RemoveAsync(Guid artworkId, CancellationToken cancellationToken)
    {
        var item = await store.GetAsync(artworkId, cancellationToken);
        if (item is null || item.IsActive)
        {
            return false;
        }

        var storage = Storage;
        if (item.AssetPath is not null && storage.IsConfigured)
        {
            if (!storage.IsAvailable)
            {
                throw new InvalidOperationException("The chapter artwork folder is not reachable.");
            }

            storage.Delete(item.AssetPath);
        }

        storage.DeleteDerivatives(item.ContentHash);
        await store.DeleteAsync(item.Id, cancellationToken);
        return true;
    }

    public async Task CancelAsync(Guid artworkId, CancellationToken cancellationToken)
    {
        var item = await store.GetAsync(artworkId, cancellationToken);
        if (item is not { IsActive: true })
        {
            return;
        }

        foreach (var row in (await store.ListChapterAsync(item.WorkId, item.ChapterNumber, cancellationToken)).Where(x => x.IsActive))
        {
            await store.UpdateAsync(
                row with { Status = ChapterArtworkStatus.Cancelled, UpdatedAt = DateTime.UtcNow },
                cancellationToken);
        }
    }

    /// <summary>The accepted image a reader should show, or null.</summary>
    public async Task<ChapterArtworkItem?> GetReaderArtworkAsync(
        Guid workId,
        int chapterNumber,
        string profileId,
        CancellationToken cancellationToken)
    {
        // Viewing needs no AI provider or reachable storage (derivatives are cached locally).
        if (!globalSettings.Load().Enabled
            || !(await store.GetPreferencesAsync(profileId, cancellationToken)).Enabled
            || (await store.GetWorkSettingsAsync(workId, cancellationToken)).Enabled == false)
        {
            return null;
        }

        return await store.GetAcceptedAsync(workId, chapterNumber, cancellationToken);
    }

    public async Task<ChapterArtworkImageFile?> OpenImageAsync(
        ChapterArtworkItem item,
        int width,
        CancellationToken cancellationToken)
    {
        if (item.AssetPath is null || item.ContentHash is null)
        {
            return null;
        }

        var storage = Storage;
        var derivative = await storage.GetDerivativeAsync(item.AssetPath, item.ContentHash, width, cancellationToken);
        if (derivative is not null)
        {
            return new(derivative, "image/webp");
        }

        return storage.Exists(item.AssetPath)
            ? new(storage.Resolve(item.AssetPath), item.MediaType ?? "image/webp")
            : null;
    }

    public Task<ChapterArtworkItem?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        store.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<ChapterArtworkItem>> ListWorkAsync(Guid workId, CancellationToken cancellationToken) =>
        store.ListWorkAsync(workId, cancellationToken);

    /// <summary>Deletes all artwork files, derivatives and metadata of a work (book deletion).</summary>
    public static async Task DeleteWorkAsync(
        AppDbContext db,
        IConfiguration configuration,
        Guid workId,
        CancellationToken cancellationToken)
    {
        var store = new ChapterArtworkStore(db);
        var storage = ChapterArtworkStorage.Create(
            ChapterArtworkGlobalSettingsStore.FromConfiguration(configuration).Load(),
            configuration);

        foreach (var item in await store.ListWorkAsync(workId, cancellationToken))
        {
            storage.Delete(item.AssetPath);
            storage.DeleteDerivatives(item.ContentHash);
        }

        await store.DeleteWorkAsync(workId, cancellationToken);
    }

    private async Task FinishAsync(
        IEnumerable<ChapterArtworkItem> rows,
        HashSet<Guid> completed,
        ChapterArtworkStatus status,
        string? error)
    {
        foreach (var row in rows.Where(x => !completed.Contains(x.Id)))
        {
            await store.UpdateAsync(
                row with { Status = status, Error = error, UpdatedAt = DateTime.UtcNow },
                CancellationToken.None);
        }
    }

    private static IReadOnlyList<string> ParseGenres(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
