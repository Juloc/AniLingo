using System.Text;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Vocabulary;
using AniLingo.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Playback;

public enum PlaybackAvailability
{
    Direct,
    Prepared,
    CanPrepare,
    Preparing,
    Failed,
    Unsupported
}

public sealed record PlaybackMedia(
    Guid EpisodeId,
    Guid MediaFileId,
    string SourcePath,
    string? StreamPath,
    string FileName,
    string ContentType,
    PlaybackAvailability Availability,
    string StatusMessage)
{
    public bool IsPlayable =>
        Availability is PlaybackAvailability.Direct or PlaybackAvailability.Prepared;

    public bool CanQueuePreparation =>
        Availability is PlaybackAvailability.CanPrepare or PlaybackAvailability.Failed;

    public bool IsPreparing => Availability == PlaybackAvailability.Preparing;
}

public sealed record PlaybackTermInfo(
    Guid TermId,
    string Canonical,
    string? Reading,
    string? Meaning,
    UserTermState? State);

public sealed record PlaybackToken(
    string Surface,
    Guid? TermId,
    string? Canonical,
    string? Reading,
    string? Meaning,
    string? State)
{
    public bool IsVocabulary => TermId.HasValue;
}

public sealed record PlaybackCue(
    int StartMs,
    int EndMs,
    IReadOnlyList<PlaybackToken> Tokens);

public sealed record EpisodePlaybackSnapshot(
    PlaybackMedia? Media,
    IReadOnlyList<PlaybackCue> Cues)
{
    public static EpisodePlaybackSnapshot Empty { get; } = new(null, []);
}

public static class PlaybackMediaTypes
{
    public static string GetContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" => "video/mp4",
            ".webm" => "video/webm",
            ".ogg" or ".ogv" => "video/ogg",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            ".ts" or ".m2ts" => "video/mp2t",
            _ => "application/octet-stream"
        };

    public static bool IsLikelyBrowserSupported(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is
            ".mp4" or ".m4v" or ".webm" or ".ogg" or ".ogv";
}

public sealed class PlaybackCueProjector(IJapaneseMorphology morphology)
{
    public PlaybackCue Project(
        int startMs,
        int endMs,
        string text,
        IReadOnlyDictionary<string, PlaybackTermInfo> terms)
    {
        var normalized = text.Normalize(NormalizationForm.FormKC);
        var analyzed = morphology.Analyze(normalized);

        if (analyzed.Count == 0)
        {
            return Plain(startMs, endMs, normalized);
        }

        var tokens = new List<PlaybackToken>(analyzed.Count + 2);
        var cursor = 0;

        foreach (var token in analyzed)
        {
            if (string.IsNullOrEmpty(token.Surface))
            {
                continue;
            }

            var index = normalized.IndexOf(token.Surface, cursor, StringComparison.Ordinal);
            if (index < 0)
            {
                return Plain(startMs, endMs, normalized);
            }

            if (index > cursor)
            {
                tokens.Add(PlainToken(normalized[cursor..index]));
            }

            var canonical = token.Canonical.Normalize(NormalizationForm.FormKC).Trim();
            if (terms.TryGetValue(canonical, out var term))
            {
                tokens.Add(new PlaybackToken(
                    token.Surface,
                    term.TermId,
                    term.Canonical,
                    term.Reading,
                    term.Meaning,
                    term.State?.ToString()));
            }
            else
            {
                tokens.Add(PlainToken(token.Surface));
            }

            cursor = index + token.Surface.Length;
        }

        if (cursor < normalized.Length)
        {
            tokens.Add(PlainToken(normalized[cursor..]));
        }

        return new PlaybackCue(startMs, endMs, tokens);
    }

    private static PlaybackCue Plain(int startMs, int endMs, string text) =>
        new(startMs, endMs, string.IsNullOrEmpty(text) ? [] : [PlainToken(text)]);

    private static PlaybackToken PlainToken(string surface) =>
        new(surface, null, null, null, null, null);
}

public sealed record PlaybackStream(
    string Path,
    string ContentType,
    DateTimeOffset LastModified);

public sealed class PlaybackService(
    AppDbContext db,
    PlaybackCueProjector projector,
    PlaybackMediaProbe mediaProbe,
    PlaybackPreparationTracker preparationTracker,
    BackgroundJobQueue backgroundJobs)
{
    public async Task<PlaybackMedia?> GetMediaAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var row = await db.MediaFiles
            .AsNoTracking()
            .Where(x => x.EpisodeId == episodeId)
            .OrderBy(x => x.Path)
            .Select(x => new
            {
                x.Id,
                x.Path,
                x.SizeBytes,
                x.LastWriteTimeUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        if (PlaybackMediaTypes.IsLikelyBrowserSupported(row.Path))
        {
            return new PlaybackMedia(
                episodeId,
                row.Id,
                row.Path,
                row.Path,
                Path.GetFileName(row.Path),
                PlaybackMediaTypes.GetContentType(row.Path),
                PlaybackAvailability.Direct,
                "Direct play");
        }

        var preparedPath = PlaybackCache.BuildPath(
            row.Id,
            row.SizeBytes,
            row.LastWriteTimeUtc);

        if (File.Exists(preparedPath))
        {
            preparationTracker.MarkReady(row.Id);
            return new PlaybackMedia(
                episodeId,
                row.Id,
                row.Path,
                preparedPath,
                Path.GetFileName(row.Path),
                "video/mp4",
                PlaybackAvailability.Prepared,
                "Browser MP4 prepared");
        }

        var state = preparationTracker.Get(row.Id);
        if (state.Status is PlaybackPreparationStatus.Queued or PlaybackPreparationStatus.Processing)
        {
            return new PlaybackMedia(
                episodeId,
                row.Id,
                row.Path,
                null,
                Path.GetFileName(row.Path),
                PlaybackMediaTypes.GetContentType(row.Path),
                PlaybackAvailability.Preparing,
                state.Status == PlaybackPreparationStatus.Queued
                    ? "Queued for browser playback preparation"
                    : "Preparing browser playback");
        }

        var probe = await mediaProbe.ProbeAsync(row.Path, cancellationToken);
        if (probe is null)
        {
            return new PlaybackMedia(
                episodeId,
                row.Id,
                row.Path,
                null,
                Path.GetFileName(row.Path),
                PlaybackMediaTypes.GetContentType(row.Path),
                PlaybackAvailability.Unsupported,
                "Could not inspect this media file.");
        }

        var plan = PlaybackRemuxPlan.Build(probe);
        if (!plan.CanPrepare)
        {
            return new PlaybackMedia(
                episodeId,
                row.Id,
                row.Path,
                null,
                Path.GetFileName(row.Path),
                PlaybackMediaTypes.GetContentType(row.Path),
                PlaybackAvailability.Unsupported,
                plan.Message);
        }

        if (state.Status == PlaybackPreparationStatus.Failed)
        {
            return new PlaybackMedia(
                episodeId,
                row.Id,
                row.Path,
                null,
                Path.GetFileName(row.Path),
                PlaybackMediaTypes.GetContentType(row.Path),
                PlaybackAvailability.Failed,
                state.Message ?? "Browser playback preparation failed.");
        }

        return new PlaybackMedia(
            episodeId,
            row.Id,
            row.Path,
            null,
            Path.GetFileName(row.Path),
            PlaybackMediaTypes.GetContentType(row.Path),
            PlaybackAvailability.CanPrepare,
            plan.Message);
    }

    public async Task<PlaybackStream?> GetStreamAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var row = await db.MediaFiles
            .AsNoTracking()
            .Where(x => x.EpisodeId == episodeId)
            .OrderBy(x => x.Path)
            .Select(x => new
            {
                x.Id,
                x.Path,
                x.SizeBytes,
                x.LastWriteTimeUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        if (PlaybackMediaTypes.IsLikelyBrowserSupported(row.Path) && File.Exists(row.Path))
        {
            return new PlaybackStream(
                row.Path,
                PlaybackMediaTypes.GetContentType(row.Path),
                new DateTimeOffset(File.GetLastWriteTimeUtc(row.Path)));
        }

        var preparedPath = PlaybackCache.BuildPath(
            row.Id,
            row.SizeBytes,
            row.LastWriteTimeUtc);

        if (!File.Exists(preparedPath))
        {
            return null;
        }

        return new PlaybackStream(
            preparedPath,
            "video/mp4",
            new DateTimeOffset(File.GetLastWriteTimeUtc(preparedPath)));
    }

    public async Task<bool> QueuePreparationAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var media = await GetMediaAsync(episodeId, cancellationToken);
        if (media is null || !media.CanQueuePreparation)
        {
            return false;
        }

        if (!preparationTracker.TryQueue(media.MediaFileId))
        {
            return false;
        }

        try
        {
            await backgroundJobs.QueueAsync(
                async (services, jobCancellationToken) =>
                {
                    var remux = services.GetRequiredService<PlaybackRemuxService>();
                    await remux.PrepareAsync(episodeId, jobCancellationToken);
                },
                cancellationToken);

            return true;
        }
        catch
        {
            preparationTracker.MarkFailed(
                media.MediaFileId,
                "Could not queue browser playback preparation.");
            throw;
        }
    }

    public async Task<EpisodePlaybackSnapshot> GetSnapshotAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var media = await GetMediaAsync(episodeId, cancellationToken);

        var trackId = await db.SubtitleTracks
            .AsNoTracking()
            .Where(x => x.EpisodeId == episodeId && x.Language == "ja")
            .OrderByDescending(x => x.ImportedAt)
            .ThenBy(x => x.Id)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (trackId is null)
        {
            return new EpisodePlaybackSnapshot(media, []);
        }

        var cues = await db.SubtitleCues
            .AsNoTracking()
            .Where(x => x.SubtitleTrackId == trackId.Value)
            .OrderBy(x => x.StartMs)
            .Select(x => new { x.StartMs, x.EndMs, x.Text })
            .ToListAsync(cancellationToken);

        var termRows = await (
            from episodeTerm in db.EpisodeTerms.AsNoTracking()
            join term in db.Terms.AsNoTracking() on episodeTerm.TermId equals term.Id
            join userTermValue in db.UserTerms.AsNoTracking()
                    .Where(x => x.ProfileId == LearningProfile.DefaultId)
                on term.Id equals userTermValue.TermId into userTerms
            from userTerm in userTerms.DefaultIfEmpty()
            where episodeTerm.EpisodeId == episodeId
            select new PlaybackTermInfo(
                term.Id,
                term.Canonical,
                term.Reading,
                term.Meaning,
                userTerm == null ? null : userTerm.State))
            .ToListAsync(cancellationToken);

        var terms = termRows.ToDictionary(x => x.Canonical, StringComparer.Ordinal);
        var projected = cues
            .Select(cue => projector.Project(cue.StartMs, cue.EndMs, cue.Text, terms))
            .ToArray();

        return new EpisodePlaybackSnapshot(media, projected);
    }
}
