using System.Text;
using AniLingo.Web.Data;
using AniLingo.Web.Features.Learning;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Playback;

public sealed record PlaybackMedia(
    Guid EpisodeId,
    string Path,
    string FileName,
    string ContentType,
    bool IsLikelyBrowserSupported);

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

public sealed class PlaybackService(
    AppDbContext db,
    PlaybackCueProjector projector)
{
    public async Task<PlaybackMedia?> GetMediaAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var row = await db.MediaFiles
            .AsNoTracking()
            .Where(x => x.EpisodeId == episodeId)
            .OrderBy(x => x.Path)
            .Select(x => new { x.Path })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new PlaybackMedia(
            episodeId,
            row.Path,
            Path.GetFileName(row.Path),
            PlaybackMediaTypes.GetContentType(row.Path),
            PlaybackMediaTypes.IsLikelyBrowserSupported(row.Path));
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
