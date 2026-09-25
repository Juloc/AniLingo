using System.Text;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Auth;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Storage;
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
    string StatusMessage,
    bool UsesLiveStream = false)
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
    PlaybackOption Server,
    double? DurationSeconds = null,
    string? PixelFormat = null,
    string? AudioCodec = null,
    long? SizeBytes = null,
    IReadOnlyList<PlaybackMediaTrack>? Tracks = null,
    MediaAvailabilitySnapshot? Storage = null)
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
    IReadOnlyList<PlaybackToken> Tokens,
    long CueId = 0);

public sealed record PlaybackCueSet(
    Guid? TrackId,
    IReadOnlyList<PlaybackCue> Cues)
{
    public static PlaybackCueSet Empty { get; } = new(null, []);
}

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
        IReadOnlyDictionary<string, PlaybackTermInfo> terms,
        long cueId = 0)
    {
        var normalized = text.Normalize(NormalizationForm.FormKC);
        var analyzed = morphology.Analyze(normalized);

        if (analyzed.Count == 0)
        {
            return Plain(startMs, endMs, normalized, cueId);
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
                return Plain(startMs, endMs, normalized, cueId);
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

        return new PlaybackCue(startMs, endMs, tokens, cueId);
    }

    private static PlaybackCue Plain(int startMs, int endMs, string text, long cueId) =>
        new(startMs, endMs, string.IsNullOrEmpty(text) ? [] : [PlainToken(text)], cueId);

    private static PlaybackToken PlainToken(string surface) =>
        new(surface, null, null, null, null, null);
}

public sealed record PlaybackStream(
    string SourcePath,
    string ContentType,
    DateTimeOffset LastModified,
    PlaybackPreparationPlan? LivePlan,
    double? DurationSeconds = null)
{
    public bool IsLive => LivePlan is not null;
}

public sealed class PlaybackService
{
    private readonly AppDbContext db;
    private readonly PlaybackCueProjector projector;
    private readonly PlaybackMediaProbe mediaProbe;
    private readonly MediaAvailabilityService? mediaAvailability;
    private readonly string profileId;

    public PlaybackService(
        AppDbContext db,
        PlaybackCueProjector projector,
        PlaybackMediaProbe mediaProbe,
        MediaAvailabilityService mediaAvailability,
        CurrentAccountContext currentAccount)
        : this(db, projector, mediaProbe, mediaAvailability, currentAccount.ProfileId)
    {
    }

    public PlaybackService(
        AppDbContext db,
        PlaybackCueProjector projector,
        PlaybackMediaProbe mediaProbe)
        : this(db, projector, mediaProbe, null, LearningProfile.DefaultId)
    {
    }

    private PlaybackService(
        AppDbContext db,
        PlaybackCueProjector projector,
        PlaybackMediaProbe mediaProbe,
        MediaAvailabilityService? mediaAvailability,
        string profileId)
    {
        this.db = db;
        this.projector = projector;
        this.mediaProbe = mediaProbe;
        this.mediaAvailability = mediaAvailability;
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

        var availability = await CheckAvailabilityAsync(
            row.Id,
            cancellationToken);

        if (availability is { IsAvailable: false })
        {
            var unavailable = new PlaybackOption(
                PlaybackOptionAvailability.Unsupported,
                availability.State == StorageAvailabilityState.FileMissing
                    ? "The media file is missing from otherwise available storage."
                    : "Media storage is currently unavailable.");

            return new PlaybackMedia(
                episodeId,
                row.Id,
                row.Path,
                Path.GetFileName(row.Path),
                PlaybackMediaTypes.GetContentType(row.Path),
                null,
                unavailable,
                unavailable,
                SizeBytes: row.SizeBytes,
                Storage: availability);
        }

        var probe = await mediaProbe.ProbeAsync(row.Path, cancellationToken);
        if (probe is null)
        {
            availability = await CheckAvailabilityAsync(
                row.Id,
                cancellationToken,
                force: true);

            if (availability is { IsAvailable: false })
            {
                var unavailable = new PlaybackOption(
                    PlaybackOptionAvailability.Unsupported,
                    availability.State == StorageAvailabilityState.FileMissing
                        ? "The media file is missing from otherwise available storage."
                        : "Media storage is currently unavailable.");

                return new PlaybackMedia(
                    episodeId,
                    row.Id,
                    row.Path,
                    Path.GetFileName(row.Path),
                    PlaybackMediaTypes.GetContentType(row.Path),
                    null,
                    unavailable,
                    unavailable,
                    SizeBytes: row.SizeBytes,
                    Storage: availability);
            }

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
                failed,
                SizeBytes: row.SizeBytes,
                Storage: availability);
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
                direct,
                probe.DurationSeconds,
                probe.PixelFormat,
                probe.AudioCodec,
                row.SizeBytes,
                probe.Tracks,
                availability);
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
            server,
            probe.DurationSeconds,
            probe.PixelFormat,
            probe.AudioCodec,
            row.SizeBytes,
            probe.Tracks,
            availability);
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

        var availability = await CheckAvailabilityAsync(
            row.Id,
            cancellationToken);
        if (availability is { IsAvailable: false })
        {
            return null;
        }

        var probe = await mediaProbe.ProbeAsync(row.Path, cancellationToken);
        if (probe is null || !File.Exists(row.Path))
        {
            _ = await CheckAvailabilityAsync(
                row.Id,
                cancellationToken,
                force: true);
            return null;
        }

        if (IsUniversalDirect(row.Path, probe) ||
            (mode == PlaybackRequestedMode.Device &&
             IsHevc(probe.VideoCodec) &&
             PlaybackMediaTypes.IsLikelyBrowserSupportedContainer(row.Path)))
        {
            return SourceStream(row.Path, probe.DurationSeconds);
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
            plan,
            probe.DurationSeconds);
    }

    public async Task<EpisodePlaybackSnapshot> GetSnapshotAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var media = await GetMediaAsync(episodeId, cancellationToken);
        var cueSet = await GetCueSetAsync(
            episodeId,
            trackId: null,
            fromMs: null,
            toMs: null,
            cancellationToken);

        return new EpisodePlaybackSnapshot(media, cueSet.Cues);
    }

    public async Task<PlaybackCueSet> GetCueSetAsync(
        Guid episodeId,
        Guid? trackId,
        int? fromMs,
        int? toMs,
        CancellationToken cancellationToken)
    {
        if (fromMs is < 0 || toMs is < 0)
        {
            throw new ArgumentOutOfRangeException(
                fromMs is < 0 ? nameof(fromMs) : nameof(toMs));
        }

        if (fromMs.HasValue && toMs.HasValue && fromMs.Value > toMs.Value)
        {
            throw new ArgumentException("fromMs must be less than or equal to toMs.");
        }

        var trackQuery = db.SubtitleTracks
            .AsNoTracking()
            .Where(x => x.EpisodeId == episodeId && x.Language == "ja");

        if (trackId.HasValue)
        {
            trackQuery = trackQuery.Where(x => x.Id == trackId.Value);
        }

        var resolvedTrackId = await trackQuery
            .OrderByDescending(x => x.ImportedAt)
            .ThenBy(x => x.Id)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (resolvedTrackId is null)
        {
            return PlaybackCueSet.Empty;
        }

        var cueQuery = db.SubtitleCues
            .AsNoTracking()
            .Where(x => x.SubtitleTrackId == resolvedTrackId.Value);

        if (fromMs.HasValue)
        {
            cueQuery = cueQuery.Where(x => x.EndMs >= fromMs.Value);
        }

        if (toMs.HasValue)
        {
            cueQuery = cueQuery.Where(x => x.StartMs <= toMs.Value);
        }

        var cues = await cueQuery
            .OrderBy(x => x.StartMs)
            .ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.StartMs, x.EndMs, x.Text })
            .ToListAsync(cancellationToken);

        var termRows = await (
            from episodeTerm in db.EpisodeTerms.AsNoTracking()
            join term in db.Terms.AsNoTracking() on episodeTerm.TermId equals term.Id
            join stateValue in LearningQueries.TermStates(db, profileId)
                on term.Id equals stateValue.TermId into states
            from state in states.DefaultIfEmpty()
            where episodeTerm.EpisodeId == episodeId
            select new PlaybackTermInfo(
                term.Id,
                term.Canonical,
                term.Reading,
                term.Meaning,
                state == null ? null : state.State))
            .ToListAsync(cancellationToken);

        var terms = termRows.ToDictionary(x => x.Canonical, StringComparer.Ordinal);
        var projected = cues
            .Select(cue => projector.Project(
                cue.StartMs,
                cue.EndMs,
                cue.Text,
                terms,
                cue.Id))
            .ToArray();

        return new PlaybackCueSet(resolvedTrackId, projected);
    }

    public async Task<PlaybackStream?> GetOriginalContentAsync(
        Guid mediaFileId,
        CancellationToken cancellationToken)
    {
        var row = await db.MediaFiles
            .AsNoTracking()
            .Where(x => x.Id == mediaFileId)
            .Select(x => new MediaRow(
                x.Id,
                x.Path,
                x.SizeBytes,
                x.LastWriteTimeUtc))
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var availability = await CheckAvailabilityAsync(
            row.Id,
            cancellationToken);
        if (availability is { IsAvailable: false })
        {
            return null;
        }

        if (!File.Exists(row.Path))
        {
            _ = await CheckAvailabilityAsync(
                row.Id,
                cancellationToken,
                force: true);
            return null;
        }

        return new PlaybackStream(
            row.Path,
            PlaybackMediaTypes.GetContentType(row.Path),
            new DateTimeOffset(File.GetLastWriteTimeUtc(row.Path)),
            null);
    }

    private async Task<MediaAvailabilitySnapshot?> CheckAvailabilityAsync(
        Guid mediaFileId,
        CancellationToken cancellationToken,
        bool force = false) =>
        mediaAvailability is null
            ? null
            : await mediaAvailability.CheckMediaAsync(
                mediaFileId,
                force,
                cancellationToken);

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
            message,
            UsesLiveStream: true);
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

    private static PlaybackStream SourceStream(
        string path,
        double? durationSeconds) =>
        new(
            path,
            PlaybackMediaTypes.GetContentType(path),
            new DateTimeOffset(File.GetLastWriteTimeUtc(path)),
            null,
            durationSeconds);

    private sealed record MediaRow(
        Guid Id,
        string Path,
        long SizeBytes,
        DateTime LastWriteTimeUtc);
}
