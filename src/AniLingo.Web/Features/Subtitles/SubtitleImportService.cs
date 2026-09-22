using AniLingo.Web.Data;
using AniLingo.Web.Features.Vocabulary;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Subtitles;

public sealed class SubtitleImportService(
    AppDbContext db,
    VocabularyService vocabularyService)
{
    public async Task ImportAsync(Guid episodeId, string path, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            return;
        }

        var format = Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant();
        var content = await File.ReadAllTextAsync(fullPath, cancellationToken);

        await ImportPreferredContentAsync(
            episodeId,
            fullPath,
            format,
            info.LastWriteTimeUtc,
            content,
            cancellationToken);
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
        var track = await db.SubtitleTracks
            .SingleOrDefaultAsync(x => x.Path == sourceKey, cancellationToken);

        if (track is not null && track.SourceUpdatedAt == sourceUpdatedAt)
        {
            var removed = await RemoveOtherJapaneseTracksAsync(episodeId, track.Id, cancellationToken);
            if (removed > 0)
            {
                await vocabularyService.RebuildEpisodeAsync(episodeId, cancellationToken);
            }

            return;
        }

        if (track is null)
        {
            track = new SubtitleTrack
            {
                EpisodeId = episodeId,
                Path = sourceKey,
                Language = "ja",
                Format = normalizedFormat,
                SourceUpdatedAt = sourceUpdatedAt
            };
            db.SubtitleTracks.Add(track);
        }
        else
        {
            track.EpisodeId = episodeId;
            track.Language = "ja";
            track.Format = normalizedFormat;
            track.SourceUpdatedAt = sourceUpdatedAt;
            track.ImportedAt = DateTime.UtcNow;

            await db.SubtitleCues
                .Where(x => x.SubtitleTrackId == track.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await RemoveOtherJapaneseTracksAsync(episodeId, track.Id, cancellationToken);

        db.SubtitleCues.AddRange(cues.Select(cue => new SubtitleCue
        {
            SubtitleTrackId = track.Id,
            StartMs = cue.StartMs,
            EndMs = cue.EndMs,
            Text = cue.Text
        }));

        await db.SaveChangesAsync(cancellationToken);
        await vocabularyService.RebuildEpisodeAsync(episodeId, cancellationToken);
    }

    private Task<int> RemoveOtherJapaneseTracksAsync(
        Guid episodeId,
        Guid preferredTrackId,
        CancellationToken cancellationToken) =>
        db.SubtitleTracks
            .Where(x =>
                x.EpisodeId == episodeId &&
                x.Language == "ja" &&
                x.Id != preferredTrackId)
            .ExecuteDeleteAsync(cancellationToken);
}
