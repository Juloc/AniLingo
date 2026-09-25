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
    public double ResumePositionSeconds =>
        LocalProgress is { IsCompleted: false, PositionMs: >= 5000 } progress
            ? progress.PositionMs / 1000d
            : 0;
    public IReadOnlyList<EpisodePreparationTerm> Terms => Preparation.Terms;
    public IReadOnlyList<EpisodeSubtitleSource> SubtitleSources { get; private set; } = [];
    public ActiveEpisodeSubtitle? ActiveSubtitle { get; private set; }
    public AudioTranscriptionState Transcription { get; private set; } =
        new(AudioTranscriptionStatus.None);
    public AniListProgressPreview? AniListProgress { get; private set; }
    public string? SubtitleNotice => TempData["SubtitleNotice"] as string;
    public string? SubtitleError => TempData["SubtitleError"] as string;
    public bool IsOwner => currentAccount.IsOwner;

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
        Preparation = await preparationService.GetAsync(id, header.AnimeId, cancellationToken);
        Playback = await playbackService.GetSnapshotAsync(id, cancellationToken);
        LocalProgress = await episodeProgressService.GetAsync(id, cancellationToken);
        AniListProgress = await aniListAccountService.GetEpisodeProgressPreviewAsync(
            id,
            cancellationToken);

        if (IsOwner)
        {
            await LoadSubtitleSourcesAsync(
                id,
                Playback.Media is { Storage.IsAvailable: true } availableMedia
                    ? availableMedia.SourcePath
                    : null,
                cancellationToken);
        }

        if (IsOwner &&
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

        var extracted = await embeddedSubtitleExtractor.ExtractTextStreamAsync(
            media.Path,
            streamIndex,
            cancellationToken);

        if (extracted is null)
        {
            TempData["SubtitleError"] =
                "This subtitle stream could not be imported as text.";
            return RedirectToPage(new { id });
        }

        await subtitleImportService.ImportPreferredContentAsync(
            id,
            extracted.SourceKey,
            extracted.Format,
            media.LastWriteTimeUtc,
            extracted.Content,
            cancellationToken);

        TempData["SubtitleNotice"] =
            $"Subtitle stream #{streamIndex} is now the Japanese learning source.";
        return RedirectToPage(new { id });
    }

    private async Task LoadSubtitleSourcesAsync(
        Guid episodeId,
        string? mediaPath,
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

        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            return;
        }

        var streams = await embeddedSubtitleExtractor.ProbeStreamsAsync(
            mediaPath,
            cancellationToken);

        SubtitleSources = streams
            .Select(stream => new EpisodeSubtitleSource(
                stream.Index,
                stream.Codec,
                stream.Language,
                stream.Title,
                stream.IsDefault,
                stream.IsForced,
                stream.IsText,
                active is not null &&
                string.Equals(
                    active.Path,
                    EmbeddedSubtitleExtractor.BuildSourceKey(mediaPath, stream.Index),
                    StringComparison.Ordinal)))
            .ToArray();
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
        CancellationToken cancellationToken)
    {
        var stream = await playbackService.GetStreamAsync(
            id,
            ParsePlaybackMode(mode),
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
                startSeconds);

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

    public async Task<IActionResult> OnPostKnownAsync(Guid id, Guid termId, CancellationToken cancellationToken)
    {
        await learningService.SetStateAsync(termId, UserTermState.Known, cancellationToken);
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostLearningAsync(Guid id, Guid termId, CancellationToken cancellationToken)
    {
        await learningService.SetStateAsync(termId, UserTermState.Learning, cancellationToken);
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

    public async Task<IActionResult> OnPostPrepareAsync(Guid id, CancellationToken cancellationToken)
    {
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
}
