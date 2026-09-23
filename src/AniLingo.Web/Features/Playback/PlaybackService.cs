using System.Text;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Vocabulary;
using AniLingo.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Playback;

public enum PlaybackOptionAvailability
{
    Ready,
    CanPrepare,
    Preparing,
    Failed,
    Unsupported
}

public sealed record PlaybackOption(
    PlaybackOptionAvailability Availability,
    string StatusMessage)
{
    public bool IsReady => Availability == PlaybackOptionAvailability.Ready;
    public bool CanPrepare =>
        Availability is PlaybackOptionAvailability.CanPrepare or PlaybackOptionAvailability.Failed;
    public bool IsPreparing => Availability == PlaybackOptionAvailability.Preparing;
}

public sealed record PlaybackMedia(
    Guid EpisodeId,
    Guid MediaFileId,
    string SourcePath,
    string FileName,
    string ContentType,
    string? VideoCodec,
    PlaybackOption Device,
    PlaybackOption Server)
{
    public bool HasReadyOption => Device.IsReady || Server.IsReady;
    public bool IsPreparing => Device.IsPreparing || Server.IsPreparing;
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

    public static bool IsLikelyBrowserSupportedContainer(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is
            ".mp4" or ".m4v" or ".webm" or ".ogg" or ".ogv";

    public static bool IsLikelyBrowserSupported(string path) =>
        IsLikelyBrowserSupportedContainer(path);
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
    string SourcePath,
    string ContentType,
    DateTimeOffset LastModified,
    PlaybackPreparationPlan? LivePlan)
{
    public bool IsLive => LivePlan is not null;
}

public sealed class PlaybackService
{
    private readonly AppDbContext db;
    private readonly PlaybackCueProjector projector;
    private readonly PlaybackMediaProbe mediaProbe;
    private readonly string profileId;

    public PlaybackService(
        AppDbContext db,
        PlaybackCueProjector projector,
        PlaybackMediaProbe mediaProbe,
        CurrentAccountContext currentAccount)
        : this(db, projector, mediaProbe, currentAccount.ProfileId)
    {
    }

    public PlaybackService(
        AppDbContext db,
        PlaybackCueProjector projector,
        PlaybackMediaProbe mediaProbe)
        : this(db, projector, mediaProbe, profileId)
    {
    }

    private PlaybackService(
        AppDbContext db,
        PlaybackCueProjector projector,
        PlaybackMediaProbe mediaProbe,
        string profileId)
    {
        this.db = db;
        this.projector = projector;
        this.mediaProbe = mediaProbe;
        this.profileId = profileId;
    }
    public async Task<PlaybackMedia?> GetMediaAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var row = await GetMediaRowAsync(episodeId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var probe = await mediaProbe.ProbeAsync(row.Path, cancellationToken);
        if (probe is null)
        {
            var failed = new PlaybackOption(
                PlaybackOptionAvailability.Unsupported,
                "Could not inspect this media file.");
            return new PlaybackMedia(
                episodeId,
                row.Id,
                row.Path,
                Path.GetFileName(row.Path),
                PlaybackMediaTypes.GetContentType(row.Path),
                null,
                failed,
                failed);
        }

        if (IsUniversalDirect(row.Path, probe))
        {
            var direct = new PlaybackOption(
                PlaybackOptionAvailability.Ready,
                "Direct play");
            return new PlaybackMedia(
                episodeId,
                row.Id,
                row.Path,
                Path.GetFileName(row.Path),
                PlaybackMediaTypes.GetContentType(row.Path),
                probe.VideoCodec,
                direct,
                direct);
        }

        var device = BuildOption(row, probe, PlaybackRequestedMode.Device);
        var server = BuildOption(row, probe, PlaybackRequestedMode.Server);

        if (IsHevc(probe.VideoCodec) &&
            PlaybackMediaTypes.IsLikelyBrowserSupportedContainer(row.Path))
        {
            device = new PlaybackOption(
                PlaybackOptionAvailability.Ready,
                "HEVC direct play on a capable device");
        }

        return new PlaybackMedia(
            episodeId,
            row.Id,
            row.Path,
            Path.GetFileName(row.Path),
            PlaybackMediaTypes.GetContentType(row.Path),
            probe.VideoCodec,
            device,
            server);
    }

    public async Task<PlaybackStream?> GetStreamAsync(
        Guid episodeId,
        PlaybackRequestedMode mode,
        CancellationToken cancellationToken)
    {
        var row = await GetMediaRowAsync(episodeId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var probe = await mediaProbe.ProbeAsync(row.Path, cancellationToken);
        if (probe is null || !File.Exists(row.Path))
        {
            return null;
        }

        if (IsUniversalDirect(row.Path, probe) ||
            (mode == PlaybackRequestedMode.Device &&
             IsHevc(probe.VideoCodec) &&
             PlaybackMediaTypes.IsLikelyBrowserSupportedContainer(row.Path)))
        {
            return SourceStream(row.Path);
        }

        var plan = PlaybackPreparationPlan.Build(probe, mode);
        if (!plan.CanPrepare || plan.Kind is null)
        {
            return null;
        }

        return new PlaybackStream(
            row.Path,
            "video/mp4",
            new DateTimeOffset(File.GetLastWriteTimeUtc(row.Path)),
            plan);
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
                    .Where(x => x.ProfileId == profileId)
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

    private static PlaybackOption BuildOption(
        MediaRow row,
        PlaybackProbeResult probe,
        PlaybackRequestedMode mode)
    {
        var plan = PlaybackPreparationPlan.Build(probe, mode);
        if (!plan.CanPrepare || plan.Kind is null)
        {
            return new PlaybackOption(
                PlaybackOptionAvailability.Unsupported,
                plan.Message);
        }

        var message = plan.Kind.Value switch
        {
            PlaybackPreparationKind.CompatibleRemux =>
                "Instant MP4 remux; video is copied without re-encoding.",
            PlaybackPreparationKind.DeviceHevcRemux =>
                "Instant HEVC MP4 stream for device decoding.",
            PlaybackPreparationKind.ServerH264Transcode =>
                "Instant H.264 server transcode.",
            _ => "Instant playback stream."
        };

        return new PlaybackOption(
            PlaybackOptionAvailability.Ready,
            message);
    }

    private async Task<MediaRow?> GetMediaRowAsync(
        Guid episodeId,
        CancellationToken cancellationToken) =>
        await db.MediaFiles
            .AsNoTracking()
            .Where(x => x.EpisodeId == episodeId)
            .OrderBy(x => x.Path)
            .Select(x => new MediaRow(
                x.Id,
                x.Path,
                x.SizeBytes,
                x.LastWriteTimeUtc))
            .FirstOrDefaultAsync(cancellationToken);

    private static bool IsUniversalDirect(string path, PlaybackProbeResult probe)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        if (extension is ".mp4" or ".m4v" or ".mov")
        {
            return string.Equals(
                       probe.VideoCodec,
                       "h264",
                       StringComparison.OrdinalIgnoreCase) &&
                   (probe.PixelFormat is "yuv420p" or "yuvj420p") &&
                   IsOneOf(probe.AudioCodec, null, "aac", "mp3");
        }

        if (extension == ".webm")
        {
            return IsOneOf(probe.VideoCodec, "vp8", "vp9", "av1") &&
                   IsOneOf(probe.AudioCodec, null, "opus", "vorbis");
        }

        if (extension is ".ogg" or ".ogv")
        {
            return IsOneOf(probe.VideoCodec, "theora", "vp8") &&
                   IsOneOf(probe.AudioCodec, null, "vorbis", "opus");
        }

        return false;
    }

    private static bool IsOneOf(string? value, params string?[] choices) =>
        choices.Any(choice =>
            string.Equals(value, choice, StringComparison.OrdinalIgnoreCase));

    private static bool IsHevc(string? codec) =>
        string.Equals(codec, "hevc", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(codec, "h265", StringComparison.OrdinalIgnoreCase);

    private static PlaybackStream SourceStream(string path) =>
        new(
            path,
            PlaybackMediaTypes.GetContentType(path),
            new DateTimeOffset(File.GetLastWriteTimeUtc(path)),
            null);

    private sealed record MediaRow(
        Guid Id,
        string Path,
        long SizeBytes,
        DateTime LastWriteTimeUtc);
}
