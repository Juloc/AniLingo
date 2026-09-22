using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Playback;
using AniLingo.Web.Features.Subtitles;
using AniLingo.Web.Features.Vocabulary;
using AniLingo.Web.Infrastructure;
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
    EmbeddedSubtitleExtractor embeddedSubtitleExtractor,
    SubtitleImportService subtitleImportService,
    BackgroundJobQueue transcriptionJobs) : PageModel
{
    public Guid EpisodeId { get; private set; }
    public Guid AnimeId { get; private set; }
    public string AnimeTitle { get; private set; } = "";
    public string EpisodeTitle { get; private set; } = "";
    public int SeasonNumber { get; private set; }
    public int EpisodeNumber { get; private set; }
    public EpisodePreparationSnapshot Preparation { get; private set; } = EpisodePreparationSnapshot.Empty;
    public EpisodePlaybackSnapshot Playback { get; private set; } = EpisodePlaybackSnapshot.Empty;
    public IReadOnlyList<EpisodePreparationTerm> Terms => Preparation.Terms;
    public IReadOnlyList<EpisodeSubtitleSource> SubtitleSources { get; private set; } = [];
    public ActiveEpisodeSubtitle? ActiveSubtitle { get; private set; }
    public AudioTranscriptionState Transcription { get; private set; } =
        new(AudioTranscriptionStatus.None);
    public string? SubtitleNotice => TempData["SubtitleNotice"] as string;
    public string? SubtitleError => TempData["SubtitleError"] as string;

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
        await LoadSubtitleSourcesAsync(id, Playback.Media?.SourcePath, cancellationToken);

        if (ActiveSubtitle is null && Playback.Media is { } media)
        {
            var hasJapaneseTextSource = SubtitleSources.Any(source =>
                source.IsText &&
                EmbeddedSubtitleExtractor.IsJapanese(source.Language, source.Title));

            if (!hasJapaneseTextSource)
            {
                await QueueAudioTranscriptionAsync(
                    id,
                    media.SourcePath,
                    cancellationToken);
            }

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
            var liveStream = LivePlaybackStream.Start(
                stream.SourcePath,
                stream.LivePlan!);

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

    private async Task QueueAudioTranscriptionAsync(
        Guid episodeId,
        string mediaPath,
        CancellationToken cancellationToken)
    {
        if (!embeddedSubtitleExtractor.TryQueueAudioTranscription(mediaPath))
        {
            return;
        }

        try
        {
            await transcriptionJobs.QueueAsync(
                async (services, jobCancellationToken) =>
                {
                    var extractor =
                        services.GetRequiredService<EmbeddedSubtitleExtractor>();
                    var importer =
                        services.GetRequiredService<SubtitleImportService>();
                    var jobDb =
                        services.GetRequiredService<AppDbContext>();

                    try
                    {
                        var media = await jobDb.MediaFiles
                            .AsNoTracking()
                            .Where(x => x.EpisodeId == episodeId)
                            .OrderBy(x => x.Path)
                            .Select(x => new
                            {
                                x.Path,
                                x.LastWriteTimeUtc
                            })
                            .FirstOrDefaultAsync(jobCancellationToken);

                        if (media is null)
                        {
                            extractor.MarkAudioTranscriptionFailed(
                                mediaPath,
                                "Media file no longer exists.");
                            return;
                        }

                        var alreadyHasJapaneseText = await jobDb.SubtitleTracks
                            .AsNoTracking()
                            .AnyAsync(
                                x => x.EpisodeId == episodeId &&
                                     x.Language == "ja",
                                jobCancellationToken);

                        if (alreadyHasJapaneseText)
                        {
                            extractor.MarkAudioTranscriptionReady(media.Path);
                            return;
                        }

                        var transcript =
                            await extractor.TranscribeJapaneseAudioAsync(
                                media.Path,
                                jobCancellationToken);

                        if (transcript is null)
                        {
                            return;
                        }

                        alreadyHasJapaneseText = await jobDb.SubtitleTracks
                            .AsNoTracking()
                            .AnyAsync(
                                x => x.EpisodeId == episodeId &&
                                     x.Language == "ja",
                                jobCancellationToken);

                        if (alreadyHasJapaneseText)
                        {
                            extractor.MarkAudioTranscriptionReady(media.Path);
                            return;
                        }

                        await importer.ImportPreferredContentAsync(
                            episodeId,
                            transcript.SourceKey,
                            transcript.Format,
                            media.LastWriteTimeUtc,
                            transcript.Content,
                            jobCancellationToken);

                        extractor.MarkAudioTranscriptionReady(media.Path);
                    }
                    catch (Exception)
                    {
                        extractor.MarkAudioTranscriptionFailed(
                            mediaPath,
                            "Japanese audio transcription could not be imported.");
                        throw;
                    }
                },
                cancellationToken);
        }
        catch
        {
            embeddedSubtitleExtractor.MarkAudioTranscriptionFailed(
                mediaPath,
                "Could not queue Japanese audio transcription.");
            throw;
        }
    }

    public async Task<IActionResult> OnPostPrepareAsync(Guid id, CancellationToken cancellationToken)
    {
        var preparedCount = await preparationService.PrepareToTargetAsync(id, cancellationToken);
        if (preparedCount is null)
        {
            return NotFound();
        }

        return RedirectToPage("/Learn/Index");
    }
}
