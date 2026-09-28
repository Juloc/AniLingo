using Jularr.Web.Data;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Subtitles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

/// <summary>
/// Shared sqlite-backed fixture for subtitle-language-profile / missing-detection tests (#526):
/// a migrated database plus small helpers to seed an anime/episode, an embedded subtitle stream,
/// and an imported external subtitle track without going through ffprobe or the full import
/// pipeline.
/// </summary>
internal sealed class SubtitleProfileFixture : IAsyncDisposable
{
    private SubtitleProfileFixture(string directory, AppDbContext db)
    {
        Directory = directory;
        Db = db;
    }

    public string Directory { get; }
    public AppDbContext Db { get; }

    public static async Task<SubtitleProfileFixture> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"jularr-subtitle-profiles-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(directory);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "jularr.db")};Foreign Keys=True")
            .Options;

        var fixture = new SubtitleProfileFixture(directory, new AppDbContext(options));
        await DatabaseMigrationBridge.UpgradeAsync(fixture.Db);
        return fixture;
    }

    public async Task<(Anime Anime, Episode Episode, MediaFile Media, LibraryRoot Root)> AddEpisodeAsync(
        string animeTitle = "Frieren",
        int seasonNumber = 1,
        int episodeNumber = 1)
    {
        var root = new LibraryRoot { Name = "Anime", Path = Path.Combine(Directory, $"root-{Guid.NewGuid():N}") };
        var anime = new Anime { Key = $"{animeTitle}-{Guid.NewGuid():N}", Title = animeTitle };
        var episode = new Episode
        {
            AnimeId = anime.Id,
            SeasonNumber = seasonNumber,
            Number = episodeNumber,
            Title = $"Episode {episodeNumber}"
        };
        var media = new MediaFile
        {
            LibraryRootId = root.Id,
            EpisodeId = episode.Id,
            Path = Path.Combine(Directory, $"{Guid.NewGuid():N}.mkv"),
            SizeBytes = 1024,
            LastWriteTimeUtc = DateTime.UtcNow
        };

        Db.AddRange(root, anime, episode, media);
        await Db.SaveChangesAsync();
        return (anime, episode, media, root);
    }

    /// <summary>Adds an embedded text-subtitle stream to the media inventory (as if ffprobe had run).</summary>
    public async Task AddEmbeddedSubtitleAsync(
        MediaFile media,
        string? language,
        bool isForced = false,
        string? title = null,
        string codec = "ass")
    {
        if (!await Db.MediaAnalyses.AnyAsync(x => x.MediaFileId == media.Id))
        {
            Db.MediaAnalyses.Add(new MediaAnalysis
            {
                MediaFileId = media.Id,
                Status = MediaAnalysisStatus.Succeeded,
                ProbeVersion = 1,
                SourceSizeBytes = media.SizeBytes,
                SourceLastWriteTimeUtc = media.LastWriteTimeUtc
            });
        }

        var nextIndex = await Db.MediaAnalysisStreams
            .Where(x => x.MediaFileId == media.Id)
            .Select(x => (int?)x.StreamIndex)
            .MaxAsync() ?? -1;

        Db.MediaAnalysisStreams.Add(new MediaAnalysisStream
        {
            MediaFileId = media.Id,
            StreamIndex = nextIndex + 1,
            Kind = MediaStreamKind.Subtitle,
            Codec = codec,
            Language = language,
            Title = title,
            IsForced = isForced
        });

        await Db.SaveChangesAsync();
    }

    /// <summary>Adds an already-imported external subtitle track with one cue, as the import pipeline would.</summary>
    public async Task<SubtitleTrack> AddExternalTrackAsync(
        Episode episode,
        string language,
        bool forced = false,
        bool sdh = false,
        string? path = null)
    {
        var track = new SubtitleTrack
        {
            EpisodeId = episode.Id,
            Path = path ?? $"manual:{Guid.NewGuid():N}",
            Language = language,
            Forced = forced,
            Sdh = sdh,
            Format = "srt",
            SourceUpdatedAt = DateTime.UtcNow
        };
        Db.SubtitleTracks.Add(track);
        await Db.SaveChangesAsync();

        Db.SubtitleCues.Add(new SubtitleCue { SubtitleTrackId = track.Id, StartMs = 0, EndMs = 1000, Text = "line" });
        await Db.SaveChangesAsync();

        return track;
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        SqliteConnection.ClearAllPools();
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
