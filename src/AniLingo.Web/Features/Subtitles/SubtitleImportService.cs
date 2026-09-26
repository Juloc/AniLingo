using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning.Courses;
using AniLingo.Web.Features.Library;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.Vocabulary;
using AniLingo.Web.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Subtitles;

public sealed record JimakuConnectionStatus(bool Configured, DateTimeOffset? UpdatedAt = null);

public sealed record JimakuKeyTestResult(bool Success, string Message);

public enum SubtitleSidecarImportStatus
{
    Imported,
    NotFound,
    Unavailable
}

public sealed record SubtitleSidecarImportResult(
    SubtitleSidecarImportStatus Status,
    string? Path = null);

public sealed class SubtitleImportService
{
    public const string JimakuSourcePrefix = "jimaku:";

    // The canonical default when nothing else configures a content language, and the
    // only language Jimaku (a Japanese fansub site) or the local Whisper fallback
    // (a Japanese-only model invocation) ever serve.
    private const string JapaneseLanguageTag = "ja";

    private const string JimakuStorePath = "/data/integrations/jimaku.json";
    private const string JimakuApiBase = "https://jimaku.cc/api/";
    private const int MaxJimakuDownloadBytes = 64 * 1024 * 1024;
    private const int MaxSubtitleBytes = 8 * 1024 * 1024;

    private static readonly ConcurrentDictionary<Guid, LearningTextPreparationState>
        PreparationStates = new();

    private static readonly object PreparationStateLock = new();
    private static readonly SemaphoreSlim JimakuStoreGate = new(1, 1);
    private static int batchQueued;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly AppDbContext db;
    private readonly VocabularyService vocabularyService;
    private readonly EmbeddedSubtitleExtractor? embeddedSubtitleExtractor;
    private readonly AnimeMetadataService? animeMetadataService;
    private readonly BackgroundJobQueue? jobs;
    private readonly IHttpClientFactory? httpClientFactory;
    private readonly IDataProtector? jimakuProtector;
    private readonly LearningContentLanguageResolver contentLanguageResolver;
    private readonly ILogger<SubtitleImportService>? logger;

    public SubtitleImportService(
        AppDbContext db,
        VocabularyService vocabularyService,
        EmbeddedSubtitleExtractor? embeddedSubtitleExtractor = null,
        AnimeMetadataService? animeMetadataService = null,
        BackgroundJobQueue? jobs = null,
        IHttpClientFactory? httpClientFactory = null,
        IDataProtectionProvider? dataProtectionProvider = null,
        ILogger<SubtitleImportService>? logger = null)
    {
        this.db = db;
        this.vocabularyService = vocabularyService;
        this.embeddedSubtitleExtractor = embeddedSubtitleExtractor;
        this.animeMetadataService = animeMetadataService;
        this.jobs = jobs;
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;
        contentLanguageResolver = new LearningContentLanguageResolver(db);
        jimakuProtector = dataProtectionProvider?.CreateProtector(
            "AniLingo.Subtitles.Jimaku.ApiKey.v1");
    }

    // The first usable sidecar in preference order becomes the learning track. Sidecar tracks are
    // removed when no usable sidecar remains; unreadable locations leave the state untouched.
    public async Task<SubtitleSidecarImportResult> ImportPreferredSidecarAsync(
        Guid episodeId,
        IReadOnlyList<string> mediaPaths,
        SubtitleSidecarDirectoryCache listings,
        CancellationToken cancellationToken)
    {
        var targetLanguage = await contentLanguageResolver.ResolveTargetLanguageAsync(cancellationToken);

        try
        {
            foreach (var candidate in SubtitleSidecarLocator.FindCandidates(mediaPaths, listings, targetLanguage))
            {
                if (await TryImportSidecarAsync(episodeId, candidate, targetLanguage, cancellationToken))
                {
                    return new SubtitleSidecarImportResult(
                        SubtitleSidecarImportStatus.Imported,
                        candidate.Path);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning(
                exception,
                "Subtitle sidecars for episode {EpisodeId} could not be read; keeping the current learning text.",
                episodeId);
            return new SubtitleSidecarImportResult(SubtitleSidecarImportStatus.Unavailable);
        }

        await RemoveSidecarTracksAsync(episodeId, targetLanguage, cancellationToken);
        return new SubtitleSidecarImportResult(SubtitleSidecarImportStatus.NotFound);
    }

    public async Task ImportPreferredContentAsync(
        Guid episodeId,
        string sourceKey,
        string format,
        DateTime sourceUpdatedAt,
        string content,
        CancellationToken cancellationToken)
    {
        var normalizedFormat = format.Trim().TrimStart('.').ToLowerInvariant();
        var cues = SubtitleParser.ParseFormat(normalizedFormat, content);
        if (cues.Count == 0)
        {
            return;
        }

        var targetLanguage = await contentLanguageResolver.ResolveTargetLanguageAsync(cancellationToken);
        await ImportCuesAsync(
            episodeId,
            sourceKey,
            normalizedFormat,
            sourceUpdatedAt,
            cues,
            targetLanguage,
            cancellationToken);
    }

    private async Task ImportCuesAsync(
        Guid episodeId,
        string sourceKey,
        string normalizedFormat,
        DateTime sourceUpdatedAt,
        IReadOnlyList<SubtitleCueData> cues,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var track = await db.SubtitleTracks
            .SingleOrDefaultAsync(x => x.Path == sourceKey, cancellationToken);

        if (track is not null && track.SourceUpdatedAt == sourceUpdatedAt)
        {
            await KeepCurrentTrackAsync(episodeId, track.Id, sourceKey, targetLanguage, cancellationToken);
            return;
        }

        if (track is null)
        {
            track = new SubtitleTrack
            {
                EpisodeId = episodeId,
                Path = sourceKey,
                Language = targetLanguage,
                Format = normalizedFormat,
                SourceUpdatedAt = sourceUpdatedAt
            };
            db.SubtitleTracks.Add(track);
        }
        else
        {
            track.EpisodeId = episodeId;
            track.Language = targetLanguage;
            track.Format = normalizedFormat;
            track.SourceUpdatedAt = sourceUpdatedAt;
            track.ImportedAt = DateTime.UtcNow;

            await db.SubtitleCues
                .Where(x => x.SubtitleTrackId == track.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await RemoveOtherTracksAsync(episodeId, track.Id, targetLanguage, cancellationToken);

        db.SubtitleCues.AddRange(cues.Select(cue => new SubtitleCue
        {
            SubtitleTrackId = track.Id,
            StartMs = cue.StartMs,
            EndMs = cue.EndMs,
            Text = cue.Text
        }));

        await db.SaveChangesAsync(cancellationToken);
        await vocabularyService.RebuildEpisodeAsync(episodeId, cancellationToken);

        MarkPreparationReady(
            episodeId,
            DetectSourceKind(sourceKey),
            $"Learning text is ready ({cues.Count} cues).");
    }

    private async Task KeepCurrentTrackAsync(
        Guid episodeId,
        Guid trackId,
        string sourceKey,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var removed = await RemoveOtherTracksAsync(
            episodeId,
            trackId,
            targetLanguage,
            cancellationToken);

        if (removed > 0)
        {
            await vocabularyService.RebuildEpisodeAsync(episodeId, cancellationToken);
        }

        MarkPreparationReady(
            episodeId,
            DetectSourceKind(sourceKey),
            "Learning text is ready.");
    }

    private async Task<bool> TryImportSidecarAsync(
        Guid episodeId,
        SubtitleSidecarCandidate candidate,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(candidate.Path);
        if (!info.Exists || info.Length > MaxSubtitleBytes)
        {
            return false;
        }

        var sourceUpdatedAt = info.LastWriteTimeUtc;
        var current = await db.SubtitleTracks
            .AsNoTracking()
            .Where(x => x.Path == candidate.Path)
            .Select(x => new { x.Id, x.EpisodeId, x.SourceUpdatedAt })
            .SingleOrDefaultAsync(cancellationToken);

        // An unchanged sidecar was already validated when it was imported.
        if (current is not null &&
            current.EpisodeId == episodeId &&
            current.SourceUpdatedAt == sourceUpdatedAt)
        {
            await KeepCurrentTrackAsync(episodeId, current.Id, candidate.Path, targetLanguage, cancellationToken);
            return true;
        }

        var content = await File.ReadAllTextAsync(candidate.Path, cancellationToken);
        var cues = SubtitleParser.ParseFormat(candidate.Format, content);
        if (cues.Count == 0)
        {
            return false;
        }

        // Japanese keeps its untagged-content kana heuristic; FindCandidates already
        // restricts every other language's untagged candidates to "no tagged file
        // exists", so an untagged candidate reaching here needs no further content gate.
        var isJapaneseTarget = targetLanguage.Equals(JapaneseLanguageTag, StringComparison.OrdinalIgnoreCase);
        if (!candidate.IsTargetLanguageTagged && isJapaneseTarget && !ContainsJapaneseDialogue(cues))
        {
            return false;
        }

        await ImportCuesAsync(
            episodeId,
            candidate.Path,
            candidate.Format,
            sourceUpdatedAt,
            cues,
            targetLanguage,
            cancellationToken);
        return true;
    }

    private async Task RemoveSidecarTracksAsync(
        Guid episodeId,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var removed = await db.SubtitleTracks
            .Where(x =>
                x.EpisodeId == episodeId &&
                x.Language == targetLanguage &&
                !x.Path.StartsWith(EmbeddedSubtitleExtractor.SourcePrefix) &&
                !x.Path.StartsWith(EmbeddedSubtitleExtractor.TranscriptionSourcePrefix) &&
                !x.Path.StartsWith(JimakuSourcePrefix))
            .ExecuteDeleteAsync(cancellationToken);

        if (removed == 0)
        {
            return;
        }

        await vocabularyService.RebuildEpisodeAsync(episodeId, cancellationToken);
        if (PreparationStates.TryGetValue(episodeId, out var state) &&
            state.Status == LearningTextPreparationStatus.Ready)
        {
            PreparationStates.TryRemove(KeyValuePair.Create(episodeId, state));
        }
    }

    // Untagged sidecars are only trusted when at least a third of their cues contain kana,
    // which rejects English or Chinese files that happen to share the episode base name.
    private static bool ContainsJapaneseDialogue(IReadOnlyList<SubtitleCueData> cues)
    {
        var kanaCues = cues.Count(cue => cue.Text.Any(ch => ch is >= '぀' and <= 'ヿ'));
        return kanaCues * 3 >= cues.Count;
    }

    public LearningTextPreparationState GetPreparationState(Guid episodeId) =>
        PreparationStates.TryGetValue(episodeId, out var state)
            ? state
            : LearningTextPreparationState.Empty;

    public async Task<LearningTextCoverageSnapshot> GetCoverageAsync(
        CancellationToken cancellationToken)
    {
        var episodeIds = await db.MediaFiles
            .AsNoTracking()
            .Select(x => x.EpisodeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (episodeIds.Count == 0)
        {
            return new LearningTextCoverageSnapshot(0, 0, 0, 0, 0, 0);
        }

        var targetLanguage = await contentLanguageResolver.ResolveTargetLanguageAsync(cancellationToken);
        var readyIds = await db.SubtitleTracks
            .AsNoTracking()
            .Where(x =>
                episodeIds.Contains(x.EpisodeId) &&
                x.Language == targetLanguage &&
                db.SubtitleCues.Any(cue => cue.SubtitleTrackId == x.Id))
            .Select(x => x.EpisodeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var readySet = readyIds.ToHashSet();
        var queued = 0;
        var processing = 0;
        var failed = 0;

        foreach (var episodeId in episodeIds)
        {
            if (readySet.Contains(episodeId))
            {
                continue;
            }

            switch (GetPreparationState(episodeId).Status)
            {
                case LearningTextPreparationStatus.Queued:
                    queued++;
                    break;
                case LearningTextPreparationStatus.Processing:
                    processing++;
                    break;
                case LearningTextPreparationStatus.Failed:
                    failed++;
                    break;
            }
        }

        var missing = Math.Max(
            0,
            episodeIds.Count - readySet.Count - queued - processing - failed);

        return new LearningTextCoverageSnapshot(
            episodeIds.Count,
            readySet.Count,
            queued,
            processing,
            failed,
            missing);
    }

    public async Task<IReadOnlyList<LearningTextEpisodeStatus>> GetMissingEpisodesAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var boundedLimit = Math.Clamp(limit, 1, 500);
        var targetLanguage = await contentLanguageResolver.ResolveTargetLanguageAsync(cancellationToken);

        var rows = await (
            from episode in db.Episodes.AsNoTracking()
            join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
            where db.MediaFiles.Any(media => media.EpisodeId == episode.Id)
                && !db.SubtitleTracks.Any(track =>
                    track.EpisodeId == episode.Id &&
                    track.Language == targetLanguage &&
                    db.SubtitleCues.Any(cue => cue.SubtitleTrackId == track.Id))
            orderby anime.Title, episode.SeasonNumber, episode.Number
            select new
            {
                episode.Id,
                AnimeTitle = anime.Title,
                episode.SeasonNumber,
                episode.Number,
                episode.Title
            })
            .Take(boundedLimit)
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new LearningTextEpisodeStatus(
                row.Id,
                row.AnimeTitle,
                row.SeasonNumber,
                row.Number,
                row.Title,
                GetPreparationState(row.Id)))
            .ToArray();
    }

    public async Task<bool> QueueLearningTextAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var targetLanguage = await contentLanguageResolver.ResolveTargetLanguageAsync(cancellationToken);
        if (await HasUsableTextAsync(episodeId, targetLanguage, cancellationToken))
        {
            MarkPreparationReady(
                episodeId,
                LearningTextSourceKind.Existing,
                "Learning text is already available.");
            return false;
        }

        lock (PreparationStateLock)
        {
            var state = GetPreparationState(episodeId);
            if (state.Status is LearningTextPreparationStatus.Queued
                or LearningTextPreparationStatus.Processing)
            {
                return false;
            }

            PreparationStates[episodeId] = new LearningTextPreparationState(
                LearningTextPreparationStatus.Queued,
                UpdatedAt: DateTimeOffset.UtcNow);
        }

        try
        {
            await RequireJobs().QueueAsync(
                new OperationDescriptor(
                    "learning-text-preparation",
                    "Subtitles",
                    "Prepare episode learning text",
                    $"Episode {episodeId:N}",
                    Lane: OperationLane.Normal,
                    IsDownload: true,
                    Retryable: true),
                async (operation, services, jobCancellationToken) =>
                {
                    await operation.ReportAsync(
                        5,
                        "Checking subtitle and transcript sources.",
                        cancellationToken: jobCancellationToken);

                    var importer = services.GetRequiredService<SubtitleImportService>();
                    await importer.PrepareLearningTextAsync(
                        episodeId,
                        jobCancellationToken);

                    await operation.ReportAsync(
                        100,
                        "Learning text is ready.",
                        cancellationToken: jobCancellationToken);
                },
                cancellationToken);
            return true;
        }
        catch
        {
            MarkPreparationFailed(
                episodeId,
                "Could not queue learning-text preparation.");
            throw;
        }
    }

    public async Task<int> QueueAllMissingAsync(CancellationToken cancellationToken)
    {
        lock (PreparationStateLock)
        {
            if (batchQueued != 0)
            {
                return 0;
            }

            batchQueued = 1;
        }

        List<Guid> episodeIds;
        try
        {
            episodeIds = await GetMissingEpisodeIdsAsync(cancellationToken);
        }
        catch
        {
            Interlocked.Exchange(ref batchQueued, 0);
            throw;
        }

        if (episodeIds.Count == 0)
        {
            Interlocked.Exchange(ref batchQueued, 0);
            return 0;
        }

        foreach (var episodeId in episodeIds)
        {
            lock (PreparationStateLock)
            {
                var state = GetPreparationState(episodeId);
                if (state.Status is not LearningTextPreparationStatus.Processing)
                {
                    PreparationStates[episodeId] = new LearningTextPreparationState(
                        LearningTextPreparationStatus.Queued,
                        UpdatedAt: DateTimeOffset.UtcNow);
                }
            }
        }

        try
        {
            await RequireJobs().QueueAsync(
                new OperationDescriptor(
                    "learning-text-batch",
                    "Subtitles",
                    "Prepare missing learning text",
                    $"{episodeIds.Count} episode(s)",
                    Lane: OperationLane.Maintenance,
                    IsDownload: true,
                    Retryable: true),
                async (operation, services, jobCancellationToken) =>
                {
                    try
                    {
                        var importer =
                            services.GetRequiredService<SubtitleImportService>();
                        var pendingIds =
                            await importer.GetMissingEpisodeIdsAsync(jobCancellationToken);

                        if (pendingIds.Count == 0)
                        {
                            await operation.ReportAsync(
                                100,
                                "No missing learning text remains.",
                                cancellationToken: jobCancellationToken);
                            return;
                        }

                        for (var index = 0; index < pendingIds.Count; index++)
                        {
                            jobCancellationToken.ThrowIfCancellationRequested();

                            var episodeId = pendingIds[index];
                            var percent = Math.Clamp(
                                (int)Math.Round(index * 100d / pendingIds.Count),
                                0,
                                99);

                            await operation.ReportAsync(
                                percent,
                                $"Preparing episode {index + 1} of {pendingIds.Count}.",
                                cancellationToken: jobCancellationToken);

                            await importer.PrepareLearningTextAsync(
                                episodeId,
                                jobCancellationToken);
                        }

                        await operation.ReportAsync(
                            100,
                            $"Prepared {pendingIds.Count} episode(s).",
                            cancellationToken: jobCancellationToken);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref batchQueued, 0);
                    }
                },
                cancellationToken);
        }
        catch
        {
            Interlocked.Exchange(ref batchQueued, 0);
            throw;
        }

        return episodeIds.Count;
    }

    public async Task PrepareLearningTextAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        PreparationStates[episodeId] = new LearningTextPreparationState(
            LearningTextPreparationStatus.Processing,
            Message: "Checking learning-text sources.",
            UpdatedAt: DateTimeOffset.UtcNow);

        try
        {
            var targetLanguage = await contentLanguageResolver.ResolveTargetLanguageAsync(cancellationToken);
            if (await HasUsableTextAsync(episodeId, targetLanguage, cancellationToken))
            {
                MarkPreparationReady(
                    episodeId,
                    LearningTextSourceKind.Existing,
                    "Learning text is already available.");
                return;
            }

            var media = await GetEpisodeMediaAsync(episodeId, cancellationToken);
            if (media is null || !File.Exists(media.MediaPath))
            {
                MarkPreparationFailed(episodeId, "Episode media file was not found.");
                return;
            }

            // Jimaku only indexes Japanese fansub releases and the local Whisper fallback
            // only ever decodes as Japanese, so both stay Japanese-only: neither is a
            // usable source once the resolved content language is something else.
            var isJapaneseTarget = targetLanguage.Equals(
                JapaneseLanguageTag,
                StringComparison.OrdinalIgnoreCase);
            var jimakuApiKey = await LoadJimakuApiKeyAsync(cancellationToken);
            if (!isJapaneseTarget && jimakuApiKey is not null)
            {
                logger?.LogInformation(
                    "Episode {EpisodeId} content language is {Language}; Jimaku (Japanese-only) and local Whisper transcription (Japanese-only model) are skipped for this stage.",
                    episodeId,
                    targetLanguage);
            }

            var stages = LearningTextFallbackPolicy.Build(
                !string.IsNullOrWhiteSpace(jimakuApiKey),
                isJapaneseTarget);

            foreach (var stage in stages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var imported = stage switch
                {
                    LearningTextFallbackStage.LocalSubtitle =>
                        await TryImportLocalSubtitleAsync(media, targetLanguage, cancellationToken),
                    LearningTextFallbackStage.EmbeddedSubtitle =>
                        await TryImportEmbeddedSubtitleAsync(media, targetLanguage, cancellationToken),
                    LearningTextFallbackStage.Jimaku when jimakuApiKey is not null && isJapaneseTarget =>
                        await TryImportJimakuAsync(
                            media,
                            jimakuApiKey,
                            cancellationToken),
                    LearningTextFallbackStage.Whisper when isJapaneseTarget =>
                        await TryImportWhisperAsync(media, cancellationToken),
                    _ => false
                };

                if (imported &&
                    await HasUsableTextAsync(episodeId, targetLanguage, cancellationToken))
                {
                    return;
                }
            }

            var audioState =
                RequireEmbeddedSubtitleExtractor().GetAudioTranscriptionState(media.MediaPath);
            MarkPreparationFailed(
                episodeId,
                audioState.Message ??
                "No usable subtitle or audio transcript could be produced.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogError(
                exception,
                "Learning-text preparation failed for episode {EpisodeId}.",
                episodeId);
            MarkPreparationFailed(
                episodeId,
                "Learning-text preparation failed unexpectedly.");
        }
    }

    public async Task<JimakuConnectionStatus> GetJimakuConnectionStatusAsync(
        CancellationToken cancellationToken)
    {
        var persisted = await LoadPersistedJimakuSettingsAsync(cancellationToken);
        if (persisted is null)
        {
            return new JimakuConnectionStatus(false);
        }

        try
        {
            var key = RequireJimakuProtector().Unprotect(persisted.ProtectedApiKey);
            return new JimakuConnectionStatus(
                !string.IsNullOrWhiteSpace(key),
                persisted.UpdatedAt);
        }
        catch (CryptographicException exception)
        {
            logger?.LogWarning(exception, "Could not decrypt the Jimaku API key.");
            return new JimakuConnectionStatus(false);
        }
    }

    public async Task<JimakuKeyTestResult> SaveJimakuApiKeyAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        var normalized = apiKey.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new JimakuKeyTestResult(false, "Jimaku API key is required.");
        }

        var test = await TestJimakuApiKeyAsync(normalized, cancellationToken);
        if (!test.Success)
        {
            return test;
        }

        var directory = Path.GetDirectoryName(JimakuStorePath)
            ?? throw new InvalidOperationException("Jimaku settings path has no directory.");
        Directory.CreateDirectory(directory);

        var persisted = new PersistedJimakuSettings(
            RequireJimakuProtector().Protect(normalized),
            DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(persisted, JsonOptions);
        var temporaryPath = $"{JimakuStorePath}.tmp-{Guid.NewGuid():N}";

        await JimakuStoreGate.WaitAsync(cancellationToken);
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            SetPrivateFileMode(temporaryPath);
            File.Move(temporaryPath, JimakuStorePath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
            JimakuStoreGate.Release();
        }

        return new JimakuKeyTestResult(true, "Jimaku connection saved.");
    }

    public async Task DisconnectJimakuAsync(CancellationToken cancellationToken)
    {
        await JimakuStoreGate.WaitAsync(cancellationToken);
        try
        {
            TryDelete(JimakuStorePath);
        }
        finally
        {
            JimakuStoreGate.Release();
        }
    }

    public async Task<JimakuKeyTestResult> TestJimakuApiKeyAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendJimakuAsync(
                apiKey,
                "entries/search?query=Sousou%20no%20Frieren",
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new JimakuKeyTestResult(true, "Jimaku API key is valid.");
            }

            if ((int)response.StatusCode is 401 or 403)
            {
                return new JimakuKeyTestResult(false, "Jimaku rejected the API key.");
            }

            return new JimakuKeyTestResult(
                false,
                $"Jimaku returned HTTP {(int)response.StatusCode}.");
        }
        catch (HttpRequestException)
        {
            return new JimakuKeyTestResult(
                false,
                "Jimaku could not be reached.");
        }
    }

    private async Task<bool> TryImportLocalSubtitleAsync(
        EpisodeMediaSnapshot media,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var sidecar = await ImportPreferredSidecarAsync(
            media.EpisodeId,
            [media.MediaPath],
            new SubtitleSidecarDirectoryCache(),
            cancellationToken);

        if (sidecar.Status != SubtitleSidecarImportStatus.Imported ||
            !await HasUsableTextAsync(media.EpisodeId, targetLanguage, cancellationToken))
        {
            return false;
        }

        MarkPreparationReady(
            media.EpisodeId,
            LearningTextSourceKind.LocalSubtitle,
            $"Using local subtitle {Path.GetFileName(sidecar.Path)}.");
        return true;
    }

    private async Task<bool> TryImportEmbeddedSubtitleAsync(
        EpisodeMediaSnapshot media,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var embedded = await RequireEmbeddedSubtitleExtractor().ExtractPreferredTextAsync(
            media.MediaPath,
            targetLanguage,
            cancellationToken);

        if (embedded is null)
        {
            return false;
        }

        await ImportPreferredContentAsync(
            media.EpisodeId,
            embedded.SourceKey,
            embedded.Format,
            media.SourceUpdatedAt,
            embedded.Content,
            cancellationToken);

        if (!await HasUsableTextAsync(media.EpisodeId, targetLanguage, cancellationToken))
        {
            return false;
        }

        MarkPreparationReady(
            media.EpisodeId,
            LearningTextSourceKind.EmbeddedSubtitle,
            "Using embedded text subtitles.");
        return true;
    }

    private async Task<bool> TryImportJimakuAsync(
        EpisodeMediaSnapshot media,
        string apiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await RequireAnimeMetadataService().ResolveEpisodeAsync(
                media.EpisodeId,
                cancellationToken);

            var remoteEpisode = resolved?.RemoteEpisodeNumber ?? media.EpisodeNumber;
            var entries = await SearchJimakuEntriesAsync(
                apiKey,
                resolved,
                media.AnimeTitle,
                cancellationToken);

            foreach (var entry in entries.Take(5))
            {
                var filteredFiles = await GetJimakuFilesAsync(
                    apiKey,
                    entry.Id,
                    remoteEpisode,
                    cancellationToken);

                var candidate = JimakuSubtitleMatcher.SelectBest(
                    filteredFiles,
                    remoteEpisode,
                    episodeFiltered: true);

                if (candidate is null)
                {
                    var allFiles = await GetJimakuFilesAsync(
                        apiKey,
                        entry.Id,
                        episodeNumber: null,
                        cancellationToken);

                    candidate = JimakuSubtitleMatcher.SelectBest(
                        allFiles,
                        remoteEpisode,
                        episodeFiltered: false);
                }

                if (candidate is null)
                {
                    continue;
                }

                var downloaded = await DownloadJimakuSubtitleAsync(
                    candidate,
                    remoteEpisode,
                    cancellationToken);

                if (downloaded is null)
                {
                    continue;
                }

                var sourceKey = BuildJimakuSourceKey(
                    entry.Id,
                    candidate.Url,
                    downloaded.Value.FileName);

                await ImportPreferredContentAsync(
                    media.EpisodeId,
                    sourceKey,
                    downloaded.Value.Format,
                    candidate.LastModified?.UtcDateTime ?? DateTime.UnixEpoch,
                    downloaded.Value.Content,
                    cancellationToken);

                if (!await HasUsableTextAsync(
                        media.EpisodeId,
                        JapaneseLanguageTag,
                        cancellationToken))
                {
                    continue;
                }

                MarkPreparationReady(
                    media.EpisodeId,
                    LearningTextSourceKind.Jimaku,
                    $"Using Jimaku subtitle {downloaded.Value.FileName}.");
                return true;
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
            JsonException or
            InvalidDataException or
            IOException)
        {
            logger?.LogWarning(
                exception,
                "Jimaku lookup failed for episode {EpisodeId}; falling back to Whisper.",
                media.EpisodeId);
        }

        return false;
    }

    private async Task<bool> TryImportWhisperAsync(
        EpisodeMediaSnapshot media,
        CancellationToken cancellationToken)
    {
        var transcript = await RequireEmbeddedSubtitleExtractor().TranscribeJapaneseAudioAsync(
            media.MediaPath,
            cancellationToken);

        if (transcript is null)
        {
            return false;
        }

        await ImportPreferredContentAsync(
            media.EpisodeId,
            transcript.SourceKey,
            transcript.Format,
            media.SourceUpdatedAt,
            transcript.Content,
            cancellationToken);

        if (!await HasUsableTextAsync(media.EpisodeId, JapaneseLanguageTag, cancellationToken))
        {
            return false;
        }

        RequireEmbeddedSubtitleExtractor().MarkAudioTranscriptionReady(media.MediaPath);
        MarkPreparationReady(
            media.EpisodeId,
            LearningTextSourceKind.Whisper,
            "Using local Whisper Japanese audio transcription.");
        return true;
    }

    private async Task<IReadOnlyList<JimakuEntry>> SearchJimakuEntriesAsync(
        string apiKey,
        ResolvedAnimeEpisodeMetadata? resolved,
        string animeTitle,
        CancellationToken cancellationToken)
    {
        string relativeUrl;

        if (resolved is not null &&
            resolved.Provider.Equals("anilist", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(resolved.ExternalId, out var anilistId))
        {
            relativeUrl = $"entries/search?anilist_id={anilistId}";
        }
        else
        {
            var query = Uri.EscapeDataString(
                resolved?.PreferredTitle ?? animeTitle);
            relativeUrl = $"entries/search?query={query}";
        }

        using var response = await SendJimakuAsync(
            apiKey,
            relativeUrl,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger?.LogWarning(
                "Jimaku entry search returned HTTP {StatusCode}.",
                (int)response.StatusCode);
            return [];
        }

        return await response.Content.ReadFromJsonAsync<List<JimakuEntry>>(
                   JsonOptions,
                   cancellationToken)
               ?? [];
    }

    private async Task<IReadOnlyList<JimakuSubtitleFileCandidate>> GetJimakuFilesAsync(
        string apiKey,
        long entryId,
        int? episodeNumber,
        CancellationToken cancellationToken)
    {
        var relativeUrl = episodeNumber is > 0
            ? $"entries/{entryId}/files?episode={episodeNumber.Value}"
            : $"entries/{entryId}/files";

        using var response = await SendJimakuAsync(
            apiKey,
            relativeUrl,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var files = await response.Content.ReadFromJsonAsync<List<JimakuFile>>(
                        JsonOptions,
                        cancellationToken)
                    ?? [];

        return files
            .Where(file =>
                !string.IsNullOrWhiteSpace(file.Name) &&
                !string.IsNullOrWhiteSpace(file.Url))
            .Select(file => new JimakuSubtitleFileCandidate(
                file.Name,
                file.Url,
                file.LastModified))
            .ToArray();
    }

    private async Task<(string FileName, string Format, string Content)?>
        DownloadJimakuSubtitleAsync(
            JimakuSubtitleFileCandidate candidate,
            int episodeNumber,
            CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(candidate.Url, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !(uri.Host.Equals("jimaku.cc", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".jimaku.cc", StringComparison.OrdinalIgnoreCase)))
        {
            logger?.LogWarning(
                "Rejected unexpected Jimaku download host for {FileName}.",
                candidate.Name);
            return null;
        }

        using var client = RequireHttpClientFactory().CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd("application/octet-stream");

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        if (response.Content.Headers.ContentLength is > MaxJimakuDownloadBytes)
        {
            return null;
        }

        var bytes = await ReadLimitedBytesAsync(
            response.Content,
            MaxJimakuDownloadBytes,
            cancellationToken);

        if (bytes is null)
        {
            return null;
        }

        var extension = Path.GetExtension(candidate.Name).ToLowerInvariant();
        if (extension == ".zip")
        {
            return ExtractSubtitleFromZip(bytes, episodeNumber);
        }

        var format = NormalizeDownloadedFormat(extension);
        if (format is null || bytes.Length > MaxSubtitleBytes)
        {
            return null;
        }

        return (
            candidate.Name,
            format,
            DecodeSubtitle(bytes));
    }

    private static (string FileName, string Format, string Content)?
        ExtractSubtitleFromZip(byte[] bytes, int episodeNumber)
    {
        using var memory = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);

        var candidates = archive.Entries
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.Name) &&
                NormalizeDownloadedFormat(Path.GetExtension(entry.Name)) is not null &&
                entry.Length is > 0 and <= MaxSubtitleBytes)
            .Select(entry => new
            {
                Entry = entry,
                Candidate = new JimakuSubtitleFileCandidate(entry.FullName, "")
            })
            .Select(x => new
            {
                x.Entry,
                Score = JimakuSubtitleMatcher.Score(
                    x.Candidate.Name,
                    episodeNumber,
                    episodeFiltered: false)
            })
            .Where(x => x.Score > int.MinValue)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (candidates is null)
        {
            return null;
        }

        using var stream = candidates.Entry.Open();
        using var output = new MemoryStream((int)candidates.Entry.Length);
        stream.CopyTo(output);

        var format = NormalizeDownloadedFormat(
            Path.GetExtension(candidates.Entry.Name));

        return format is null
            ? null
            : (
                candidates.Entry.FullName,
                format,
                DecodeSubtitle(output.ToArray()));
    }

    private async Task<HttpResponseMessage> SendJimakuAsync(
        string apiKey,
        string relativeUrl,
        CancellationToken cancellationToken)
    {
        var client = RequireHttpClientFactory().CreateClient();
        client.BaseAddress = new Uri(JimakuApiBase);
        client.Timeout = TimeSpan.FromSeconds(20);

        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        request.Headers.TryAddWithoutValidation("Authorization", apiKey);
        request.Headers.UserAgent.ParseAdd(
            "AniLingo/0.1 (+https://github.com/Juloc/AniLingo)");
        request.Headers.Accept.ParseAdd("application/json");

        return await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private async Task<string?> LoadJimakuApiKeyAsync(
        CancellationToken cancellationToken)
    {
        var persisted = await LoadPersistedJimakuSettingsAsync(cancellationToken);
        if (persisted is null)
        {
            return null;
        }

        try
        {
            var key = RequireJimakuProtector().Unprotect(persisted.ProtectedApiKey);
            return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        }
        catch (CryptographicException exception)
        {
            logger?.LogWarning(exception, "Could not decrypt the Jimaku API key.");
            return null;
        }
    }

    private async Task<PersistedJimakuSettings?> LoadPersistedJimakuSettingsAsync(
        CancellationToken cancellationToken)
    {
        await JimakuStoreGate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(JimakuStorePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(
                JimakuStorePath,
                cancellationToken);
            return JsonSerializer.Deserialize<PersistedJimakuSettings>(
                json,
                JsonOptions);
        }
        catch (Exception exception) when (
            exception is JsonException or
            IOException or
            UnauthorizedAccessException)
        {
            logger?.LogWarning(exception, "Could not load Jimaku settings.");
            return null;
        }
        finally
        {
            JimakuStoreGate.Release();
        }
    }

    private async Task<EpisodeMediaSnapshot?> GetEpisodeMediaAsync(
        Guid episodeId,
        CancellationToken cancellationToken) =>
        await (
            from media in db.MediaFiles.AsNoTracking()
            join episode in db.Episodes.AsNoTracking()
                on media.EpisodeId equals episode.Id
            join anime in db.Anime.AsNoTracking()
                on episode.AnimeId equals anime.Id
            where episode.Id == episodeId
            orderby media.Path
            select new EpisodeMediaSnapshot(
                episode.Id,
                anime.Title,
                episode.Number,
                media.Path,
                media.LastWriteTimeUtc))
            .FirstOrDefaultAsync(cancellationToken);

    private Task<bool> HasUsableTextAsync(
        Guid episodeId,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        db.SubtitleTracks
            .AsNoTracking()
            .AnyAsync(
                track =>
                    track.EpisodeId == episodeId &&
                    track.Language == targetLanguage &&
                    db.SubtitleCues.Any(cue => cue.SubtitleTrackId == track.Id),
                cancellationToken);

    private async Task<List<Guid>> GetMissingEpisodeIdsAsync(
        CancellationToken cancellationToken)
    {
        var targetLanguage = await contentLanguageResolver.ResolveTargetLanguageAsync(cancellationToken);
        return await db.MediaFiles
            .AsNoTracking()
            .Select(x => x.EpisodeId)
            .Distinct()
            .Where(episodeId =>
                !db.SubtitleTracks.Any(track =>
                    track.EpisodeId == episodeId &&
                    track.Language == targetLanguage &&
                    db.SubtitleCues.Any(cue => cue.SubtitleTrackId == track.Id)))
            .ToListAsync(cancellationToken);
    }

    private Task<int> RemoveOtherTracksAsync(
        Guid episodeId,
        Guid preferredTrackId,
        string targetLanguage,
        CancellationToken cancellationToken) =>
        db.SubtitleTracks
            .Where(x =>
                x.EpisodeId == episodeId &&
                x.Language == targetLanguage &&
                x.Id != preferredTrackId)
            .ExecuteDeleteAsync(cancellationToken);

    private static LearningTextSourceKind DetectSourceKind(string sourceKey)
    {
        if (sourceKey.StartsWith(
                EmbeddedSubtitleExtractor.TranscriptionSourcePrefix,
                StringComparison.Ordinal))
        {
            return LearningTextSourceKind.Whisper;
        }

        if (sourceKey.StartsWith(
                EmbeddedSubtitleExtractor.SourcePrefix,
                StringComparison.Ordinal))
        {
            return LearningTextSourceKind.EmbeddedSubtitle;
        }

        if (sourceKey.StartsWith(JimakuSourcePrefix, StringComparison.Ordinal))
        {
            return LearningTextSourceKind.Jimaku;
        }

        return LearningTextSourceKind.LocalSubtitle;
    }

    private static void MarkPreparationReady(
        Guid episodeId,
        LearningTextSourceKind source,
        string message) =>
        PreparationStates[episodeId] = new LearningTextPreparationState(
            LearningTextPreparationStatus.Ready,
            source,
            message,
            DateTimeOffset.UtcNow);

    private static void MarkPreparationFailed(Guid episodeId, string message) =>
        PreparationStates[episodeId] = new LearningTextPreparationState(
            LearningTextPreparationStatus.Failed,
            Message: message,
            UpdatedAt: DateTimeOffset.UtcNow);

    private static string BuildJimakuSourceKey(
        long entryId,
        string url,
        string fileName)
    {
        var fingerprint = $"{url}\n{fileName}";
        var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)))
            .ToLowerInvariant();
        return $"{JimakuSourcePrefix}{entryId}:{hash}";
    }

    private static string? NormalizeDownloadedFormat(string extension) =>
        extension.Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "srt" => "srt",
            "ass" => "ass",
            "ssa" => "ssa",
            _ => null
        };

    private static string DecodeSubtitle(byte[] bytes)
    {
        var content = Encoding.UTF8.GetString(bytes);
        return content.Length > 0 && content[0] == '\uFEFF'
            ? content[1..]
            : content;
    }

    private static async Task<byte[]?> ReadLimitedBytesAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81920];

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > maxBytes)
            {
                return null;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private EmbeddedSubtitleExtractor RequireEmbeddedSubtitleExtractor() =>
        embeddedSubtitleExtractor ?? throw new InvalidOperationException(
            "Learning-text preparation requires EmbeddedSubtitleExtractor.");

    private AnimeMetadataService RequireAnimeMetadataService() =>
        animeMetadataService ?? throw new InvalidOperationException(
            "Jimaku lookup requires AnimeMetadataService.");

    private BackgroundJobQueue RequireJobs() =>
        jobs ?? throw new InvalidOperationException(
            "Learning-text preparation requires BackgroundJobQueue.");

    private IHttpClientFactory RequireHttpClientFactory() =>
        httpClientFactory ?? throw new InvalidOperationException(
            "Jimaku lookup requires IHttpClientFactory.");

    private IDataProtector RequireJimakuProtector() =>
        jimakuProtector ?? throw new InvalidOperationException(
            "Jimaku settings require ASP.NET Core Data Protection.");

    private static void SetPrivateFileMode(string path)
    {
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed record EpisodeMediaSnapshot(
        Guid EpisodeId,
        string AnimeTitle,
        int EpisodeNumber,
        string MediaPath,
        DateTime SourceUpdatedAt);

    private sealed record PersistedJimakuSettings(
        string ProtectedApiKey,
        DateTimeOffset UpdatedAt);

    private sealed record JimakuEntry(
        long Id,
        string Name,
        [property: JsonPropertyName("anilist_id")] long? AniListId);

    private sealed record JimakuFile(
        string Name,
        string Url,
        [property: JsonPropertyName("last_modified")] DateTimeOffset? LastModified);
}
