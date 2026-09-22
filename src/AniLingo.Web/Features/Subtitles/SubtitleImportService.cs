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

        var updatedAt = new DateTimeOffset(info.LastWriteTimeUtc);
        var track = await db.SubtitleTracks.SingleOrDefaultAsync(x => x.Path == fullPath, cancellationToken);

        if (track is not null && track.SourceUpdatedAt == updatedAt)
        {
            return;
        }

        var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var cues = SubtitleParser.Parse(fullPath, content);

        if (track is null)
        {
            track = new SubtitleTrack
            {
                EpisodeId = episodeId,
                Path = fullPath,
                Language = "ja",
                Format = Path.GetExtension(fullPath).TrimStart('.').ToLowerInvariant(),
                SourceUpdatedAt = updatedAt
            };
            db.SubtitleTracks.Add(track);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            track.SourceUpdatedAt = updatedAt;
            track.ImportedAt = DateTimeOffset.UtcNow;
            await db.SubtitleCues
                .Where(x => x.SubtitleTrackId == track.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }

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
}
