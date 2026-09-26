using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Operations;
using AniLingo.Web.Features.Playback;
using AniLingo.Web.Features.Progress;
using AniLingo.Web.Features.Storage;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Features.Tracking;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Pages.Library;

public sealed record EpisodeSubtitleSource(
    int StreamIndex,
    string Codec,
    string? Language,
    string? Title,
    bool IsDefault,
    bool IsForced,
    bool IsText,
    bool IsSelected);

public sealed record ActiveEpisodeSubtitle(
    string Label,
    string Format,
    int CueCount);

public sealed class EpisodeModel(
    AppDbContext db,
    LearningService learningService,
    EpisodePreparationService preparationService,
    PlaybackService playbackService,
    EpisodeProgressService episodeProgressService,
    EmbeddedSubtitleExtractor embeddedSubtitleExtractor,
    SubtitleImportService subtitleImportService,
    AniListAccountService aniListAccountService,
    CurrentAccountContext currentAccount,
    OperationRunner operations) : PageModel
{
    public Guid EpisodeId { get; private set; }
    public Guid AnimeId { get; private set; }
    public string AnimeTitle { get; private set; } = "";
    public string EpisodeTitle { get; private set; } = "";
    public int SeasonNumber { get; private set; }
    public int EpisodeNumber { get; private set; }
    public EpisodePreparationSnapshot Preparation { get; private set; } = EpisodePreparationSnapshot.Empty;
    public EpisodePlaybackSnapshot Playback { get; private set; } = EpisodePlaybackSnapshot.Empty;
    public EpisodeProgressSnapshot? LocalProgress { get; private set; }
    public EpisodeFlowSnapshot? Flow { get; private set; }
    public PlayerControls? Controls { get; private set; }
    public double ResumePositionSeconds =>
        (LocalProgress?.ResumePositionMs ?? 0) / 1000d;
    public string? NextEpisodeUrl =>
        Flow?.Next is { } next ? $"/Library/Episode/{next.Id}" : null;
    public string? PreviousEpisodeUrl =>
        Flow?.Previous is { } previous ? $"/Library/Episode/{previous.Id}" : null;
    public IReadOnlyList<EpisodePreparationTerm> Terms => Preparation.Terms;
    public IReadOnlyList<EpisodeSubtitleSource> SubtitleSources { get; private set; } = [];
    public ActiveEpisodeSubtitle? ActiveSubtitle { get; private set; }
    public AudioTranscriptionState Transcription { get; private set; } =
        new(AudioTranscriptionStatus.None);
    public ExternalProgressSummary? ExternalProgress { get; private set; }
    public string? SubtitleNotice => TempData["SubtitleNotice"] as string;
    public string? SubtitleError => TempData["SubtitleError"] as string;
    public bool IsOwner => currentAccount.IsOwner;
    public LearningResolvedSettings LearningSettings { get; private set; } =
        new(
            LearningMode.Off,
            LearningConfigurationDefaults.For(LearningMode.Off));
    public bool ShowContentMetrics =>
        LearningSettings.IsEnabled(LearningCapability.ContentMetrics);
    public bool ShowPreparationSuggestions =>
        LearningSettings.IsEnabled(LearningCapability.PreparationSuggestions);
    public bool ShowVocabularyTools =>
        LearningSettings.IsEnabled(LearningCapability.Vocabulary);
    /// <summary>Learn queues spaced repetition, so it also needs Reviews for this episode.</summary>
    public bool ShowLearnAction =>
        ShowVocabularyTools && LearningSettings.IsEnabled(LearningCapability.Reviews);
    public bool ShowPlayerTools =>
        LearningSettings.IsEnabled(LearningCapability.PlayerTools);
    public bool NeedsLearningSource =>
        ShowContentMetrics
        || ShowPreparationSuggestions
        || ShowVocabularyTools
        || ShowPlayerTools
        || LearningSettings.IsEnabled(LearningCapability.LanguageLookup)
        || LearningSettings.IsEnabled(LearningCapability.SentencePractice);

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var header = await (
            from episode in db.Episodes.AsNoTracking()
            join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
            where episode.Id == id
            select new
            {
                episode.Id,
                episode.AnimeId,
                AnimeTitle = anime.Title,
                episode.Title,
                episode.SeasonNumber,
                episode.Number
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return NotFound();
        }

        EpisodeId = header.Id;
        AnimeId = header.AnimeId;
        AnimeTitle = header.AnimeTitle;
        EpisodeTitle = header.Title;
        SeasonNumber = header.SeasonNumber;
        EpisodeNumber = header.Number;
        LearningSettings = await new LearningConfigurationStore(db).ResolveAsync(
            currentAccount.ProfileId,
            new LearningScopeContext(
                LearningMediaType.Anime,
                WorkKey: header.AnimeId.ToString(),
                ContentKey: id.ToString()),
            cancellationToken);

        if (NeedsLearningSource)
        {
            Preparation = await preparationService.GetAsync(
                id,
                header.AnimeId,
                cancellationToken);
        }

        Playback = await playbackService.GetSnapshotAsync(id, cancellationToken);
        LocalProgress = await episodeProgressService.GetAsync(id, cancellationToken);
        Flow = await episodeProgressService.GetFlowAsync(id, cancellationToken);
        // Local-only: remote AniList progress is loaded after first paint
        // through OnGetExternalProgressAsync.
        ExternalProgress = await aniListAccountService.GetEpisodeProgressSummaryAsync(
            id,
            cancellationToken);

        if (Playback.Media is { } playbackMedia)
        {
            var preferences = Flow?.Preferences
                ?? await episodeProgressService.GetPreferencesAsync(cancellationToken);
            Controls = PlayerControls.Build(
                playbackMedia,
                Playback.Cues.Count > 0,
                await FindLearningSourceStreamIndexAsync(id, playbackMedia.SourcePath, cancellationToken),
                preferences);
        }

        if (IsOwner && NeedsLearningSource)
        {
            await LoadSubtitleSourcesAsync(
                id,
                Playback.Media is { Storage.IsAvailable: true } availableMedia
                    ? availableMedia
                    : null,
                cancellationToken);
        }

        if (IsOwner &&
            NeedsLearningSource &&
            ActiveSubtitle is null &&
            Playback.Media is { Storage.IsAvailable: true } media)
        {
            await subtitleImportService.QueueLearningTextAsync(
                id,
                cancellationToken);

            Transcription =
                embeddedSubtitleExtractor.GetAudioTranscriptionState(media.SourcePath);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostUseSubtitleAsync(
        Guid id,
        int streamIndex,
        CancellationToken cancellationToken)
    {
        if (!IsOwner)
        {
            return Forbid();
        }

        var media = await db.MediaFiles
            .AsNoTracking()
            .Where(x => x.EpisodeId == id)
            .OrderBy(x => x.Path)
            .Select(x => new
            {
                x.Path,
                x.LastWriteTimeUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (media is null)
        {
            return NotFound();
        }

        try
        {
            await operations.RunAsync(
                new OperationDescriptor(
                    "episode-subtitle-import",
                    "Learning",
                    "Import episode subtitle stream",
                    $"Stream #{streamIndex}",
                    currentAccount.ProfileId,
                    OperationLane.Normal,
                    Retryable: false),
                async (operation, token) =>
                {
                    await operation.ReportAsync(
                        10,
                        "Extracting subtitle stream.",
                        cancellationToken: token);

                    var extracted = await embeddedSubtitleExtractor.ExtractTextStreamAsync(
                        media.Path,
                        streamIndex,
                        token);

                    if (extracted is null)
                    {
                        throw new InvalidOperationException(
                            "This subtitle stream could not be imported as text.");
                    }

                    await operation.ReportAsync(
                        70,
                        "Importing subtitle as the learning source.",
                        cancellationToken: token);

                    await subtitleImportService.ImportPreferredContentAsync(
                        id,
                        extracted.SourceKey,
                        extracted.Format,
                        media.LastWriteTimeUtc,
                        extracted.Content,
                        token);
                },
                "Episode subtitle imported.",
                cancellationToken);

            TempData["SubtitleNotice"] =
                $"Subtitle stream #{streamIndex} is now the Japanese learning source.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["SubtitleError"] = exception.Message;
        }
        return RedirectToPage(new { id });
    }

    private async Task LoadSubtitleSourcesAsync(
        Guid episodeId,
        PlaybackMedia? media,
        CancellationToken cancellationToken)
    {
        var active = await db.SubtitleTracks
            .AsNoTracking()
            .Where(x => x.EpisodeId == episodeId && x.Language == "ja")
            .OrderByDescending(x => x.ImportedAt)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.Path,
                x.Format
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (active is not null)
        {
            var cueCount = await db.SubtitleCues
                .AsNoTracking()
                .CountAsync(x => x.SubtitleTrackId == active.Id, cancellationToken);

            ActiveSubtitle = new ActiveEpisodeSubtitle(
                BuildSubtitleLabel(active.Path),
                active.Format,
                cueCount);
        }

        if (media is null)
        {
            return;
        }

        // Embedded streams come from the canonical media inventory via the playback snapshot.
        SubtitleSources = (media.Tracks ?? [])
            .Where(stream => stream.Kind == PlaybackTrackKind.Subtitle && stream.Codec is not null)
            .Select(stream => new EpisodeSubtitleSource(
                stream.StreamIndex,
                stream.Codec!,
                stream.Language,
                stream.Title,
                stream.IsDefault,
                stream.IsForced,
                stream.IsText,
                active is not null &&
                string.Equals(
                    active.Path,
                    EmbeddedSubtitleExtractor.BuildSourceKey(media.SourcePath, stream.StreamIndex),
                    StringComparison.Ordinal)))
            .ToArray();
    }

    private async Task<int?> FindLearningSourceStreamIndexAsync(
        Guid episodeId,
        string mediaPath,
        CancellationToken cancellationToken)
    {
        var activePath = await db.SubtitleTracks
            .AsNoTracking()
            .Where(x => x.EpisodeId == episodeId && x.Language == "ja")
            .OrderByDescending(x => x.ImportedAt)
            .ThenBy(x => x.Id)
            .Select(x => x.Path)
            .FirstOrDefaultAsync(cancellationToken);

        var prefix = EmbeddedSubtitleExtractor.BuildSourcePrefix(mediaPath);
        return activePath is not null &&
               activePath.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(
                   activePath.AsSpan(prefix.Length),
                   System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var streamIndex)
            ? streamIndex
            : null;
    }

    private static string BuildSubtitleLabel(string sourceKey)
    {
        if (sourceKey.StartsWith(
                EmbeddedSubtitleExtractor.TranscriptionSourcePrefix,
                StringComparison.Ordinal))
        {
            return "Japanese audio transcription";
        }

        if (sourceKey.StartsWith(
                SubtitleImportService.JimakuSourcePrefix,
                StringComparison.Ordinal))
        {
            return "Jimaku online subtitle";
        }

        if (!sourceKey.StartsWith(
                EmbeddedSubtitleExtractor.SourcePrefix,
                StringComparison.Ordinal))
        {
            return Path.GetFileName(sourceKey);
        }

        var marker = sourceKey.LastIndexOf("#stream=", StringComparison.Ordinal);
        return marker >= 0
            ? $"Embedded stream #{sourceKey[(marker + 8)..]}"
            : "Embedded subtitle";
    }

    public async Task<IActionResult> OnGetMediaAsync(
        Guid id,
        string? mode,
        double? start,
        string? audio,
        string? quality,
        CancellationToken cancellationToken)
    {
        if (!PlaybackQuality.TryParse(quality, out var qualityCap))
        {
            return BadRequest();
        }

        var stream = await playbackService.GetStreamAsync(
            id,
            new PlaybackStreamRequest(
                ParsePlaybackMode(mode),
                string.IsNullOrWhiteSpace(audio) ? null : audio.Trim(),
                qualityCap),
            cancellationToken);
        if (stream is null || !System.IO.File.Exists(stream.SourcePath))
        {
            return NotFound();
        }

        if (!stream.IsLive)
        {
            return new PhysicalFileResult(stream.SourcePath, stream.ContentType)
            {
                EnableRangeProcessing = true,
                LastModified = stream.LastModified
            };
        }

        try
        {
            var startSeconds = NormalizePlaybackStart(start, stream.DurationSeconds);
            var liveStream = LivePlaybackStream.Start(
                stream.SourcePath,
                stream.LivePlan!,
                startSeconds,
                stream.AudioStreamIndex,
                stream.QualityCap);

            return new FileStreamResult(liveStream, stream.ContentType)
            {
                EnableRangeProcessing = false,
                LastModified = stream.LastModified
            };
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    public async Task<IActionResult> OnPostWatchedAsync(
        Guid id,
        bool watched,
        CancellationToken cancellationToken)
    {
        var progress = await episodeProgressService.SetWatchedAsync(
            id,
            watched,
            cancellationToken);

        if (progress is null)
        {
            return NotFound();
        }

        TempData["Status"] = watched
            ? "Episode marked as watched."
            : "Episode marked as unwatched.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetExternalProgressAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!await db.Episodes.AsNoTracking().AnyAsync(x => x.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var state = await aniListAccountService.GetEpisodeProgressStateAsync(
            id,
            cancellationToken);

        Response.Headers.CacheControl = "no-store";
        return Partial(
            "_ExternalProgressState",
            new ExternalProgressRemoteView(
                ExternalProgressMediaKind.Episode,
                state,
                "SyncAniList"));
    }

    public async Task<IActionResult> OnPostSyncAniListAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await operations.RunAsync(
            new OperationDescriptor(
                "anilist-episode-progress-sync",
                "AniList",
                "Sync episode progress",
                ProfileId: currentAccount.ProfileId,
                Lane: OperationLane.Normal,
                Retryable: false),
            (_, token) => aniListAccountService.SyncEpisodeProgressAsync(
                id,
                token),
            "Episode progress sync completed.",
            cancellationToken);

        TempData["Status"] = result.Message;
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostKnownAsync(
        Guid id,
        Guid termId,
        CancellationToken cancellationToken)
    {
        if (!await IsCapabilityEnabledAsync(
                id,
                LearningCapability.Vocabulary,
                cancellationToken))
        {
            return Forbid();
        }

        await learningService.SetStateAsync(
            termId,
            UserTermState.Known,
            cancellationToken);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSavedAsync(
        Guid id,
        Guid termId,
        CancellationToken cancellationToken)
    {
        if (!await IsCapabilityEnabledAsync(
                id,
                LearningCapability.Vocabulary,
                cancellationToken))
        {
            return Forbid();
        }

        await learningService.SetStateAsync(
            termId,
            UserTermState.Saved,
            cancellationToken);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostLearningAsync(
        Guid id,
        Guid termId,
        CancellationToken cancellationToken)
    {
        if (!await IsCapabilityEnabledAsync(
                id,
                LearningCapability.Vocabulary,
                cancellationToken)
            || !await IsCapabilityEnabledAsync(
                id,
                LearningCapability.Reviews,
                cancellationToken))
        {
            return Forbid();
        }

        await learningService.SetStateAsync(
            termId,
            UserTermState.Learning,
            cancellationToken);
        return RedirectToPage(new { id });
    }

    private static PlaybackRequestedMode ParsePlaybackMode(string? mode) =>
        string.Equals(mode, "server", StringComparison.OrdinalIgnoreCase)
            ? PlaybackRequestedMode.Server
            : PlaybackRequestedMode.Device;

    private static double NormalizePlaybackStart(
        double? requestedStart,
        double? durationSeconds)
    {
        if (requestedStart is null ||
            !double.IsFinite(requestedStart.Value) ||
            requestedStart.Value <= 0)
        {
            return 0;
        }

        if (durationSeconds is > 0 && double.IsFinite(durationSeconds.Value))
        {
            return Math.Min(
                requestedStart.Value,
                Math.Max(0, durationSeconds.Value - 0.05));
        }

        return requestedStart.Value;
    }

    public async Task<IActionResult> OnPostPrepareAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!await IsCapabilityEnabledAsync(
                id,
                LearningCapability.PreparationSuggestions,
                cancellationToken))
        {
            return Forbid();
        }

        var preparedCount = await operations.RunAsync(
            new OperationDescriptor(
                "episode-learning-preparation",
                "Learning",
                "Prepare episode learning data",
                ProfileId: currentAccount.ProfileId,
                Lane: OperationLane.Normal,
                Retryable: false),
            async (operation, token) =>
            {
                await operation.ReportAsync(
                    10,
                    "Preparing episode vocabulary and learning data.",
                    cancellationToken: token);

                return await preparationService.PrepareToTargetAsync(
                    id,
                    token);
            },
            "Episode learning data prepared.",
            cancellationToken);

        if (preparedCount is null)
        {
            return NotFound();
        }

        return RedirectToPage("/Learn/Index");
    }

    private async Task<bool> IsCapabilityEnabledAsync(
        Guid episodeId,
        LearningCapability capability,
        CancellationToken cancellationToken)
    {
        var animeId = await db.Episodes
            .AsNoTracking()
            .Where(x => x.Id == episodeId)
            .Select(x => (Guid?)x.AnimeId)
            .SingleOrDefaultAsync(cancellationToken);

        if (animeId is null)
        {
            return false;
        }

        var resolved = await new LearningConfigurationStore(db).ResolveAsync(
            currentAccount.ProfileId,
            new LearningScopeContext(
                LearningMediaType.Anime,
                WorkKey: animeId.Value.ToString(),
                ContentKey: episodeId.ToString()),
            cancellationToken);

        return resolved.IsEnabled(capability);
    }
}
