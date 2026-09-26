using AniLingo.Web.Data;
using AniLingo.Web.Features.Artwork;
using AniLingo.Web.Features.MediaMapping;
using AniLingo.Web.Features.Metadata;
using AniLingo.Web.Features.Subtitles;
using Microsoft.EntityFrameworkCore;

namespace AniLingo.Web.Features.Library;

// The root-relative folder that a per-anime repair action scopes its work to. Derived from an
// already-known MediaFile instead of a second folder-naming rule, so it always agrees with the
// folder MediaPathParser used when the anime was discovered.
public sealed record AnimeRepairFolder(Guid RootId, string RootName, string Folder);

public sealed record AnimeRepairLocalRefreshResult(
    int EpisodesConsidered,
    int SubtitlesImported,
    int ArtworkImported,
    int ArtworkUnchanged,
    int NfoWarnings);

public sealed record AnimeRepairReanalysisResult(
    int MediaFilesConsidered,
    int Analyzed,
    int Failed,
    int Deferred);

public sealed record AnimeRepairMatchCandidate(
    AnimeMetadataCandidate Candidate,
    int Score,
    IReadOnlyList<string> Evidence);

// Per-anime repair tools: rescan one anime's folder, refresh its local sidecar subtitles/NFO/
// artwork, force a media re-analysis, and identify/fix its AniList match. Every action reuses the
// canonical scan/probe/match services directly; none of them re-implement scanning, probing or
// matching. Filesystem discovery (rescan) and metadata refresh stay on their own distinct paths,
// as required by the repair tools issue.
public sealed class AnimeRepairService(
    AppDbContext db,
    LibraryScanCoordinator scans,
    MediaInventoryService mediaInventory,
    SubtitleImportService subtitleImport,
    AnimeMetadataService metadataService)
{
    // Locates the anime's folder from its own MediaFiles: the first segment of any known media
    // file's path relative to its library root, which is exactly the folder MediaPathParser used
    // to discover the anime in the first place.
    public async Task<AnimeRepairFolder?> ResolveFolderAsync(
        Guid animeId,
        CancellationToken cancellationToken)
    {
        var mediaPath = await (
                from media in db.MediaFiles.AsNoTracking()
                join episode in db.Episodes.AsNoTracking() on media.EpisodeId equals episode.Id
                where episode.AnimeId == animeId
                orderby media.Path
                select new { media.LibraryRootId, media.Path })
            .FirstOrDefaultAsync(cancellationToken);

        if (mediaPath is null)
        {
            return null;
        }

        var root = await db.LibraryRoots
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == mediaPath.LibraryRootId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        var rootPath = Path.GetFullPath(root.Path);
        var relative = Path.GetRelativePath(rootPath, mediaPath.Path);
        var parts = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        return parts.Length < 2
            ? null
            : new AnimeRepairFolder(root.Id, root.Name, parts[0]);
    }

    // The single entry point for a per-anime rescan: a folder-scoped request through the
    // coordinator, so the per-root coalescing guard and rename/import wait apply exactly as they
    // do for the watcher and the admin "run again" action.
    public async Task<LibraryScanQueueResult> RescanFolderAsync(
        Guid animeId,
        string? profileId,
        CancellationToken cancellationToken)
    {
        var folder = await ResolveFolderAsync(animeId, cancellationToken);
        return folder is null
            ? new LibraryScanQueueResult(
                LibraryScanQueueOutcome.RootNotFound,
                null,
                "This anime has no known media files, so a folder to rescan could not be found.")
            : await scans.QueueAsync(
                new LibraryScanRequest(folder.RootId, LibraryScanTrigger.Manual, folder.Folder, profileId),
                cancellationToken);
    }

    // Refreshes local sidecar subtitles, NFO-derived titles and local artwork for the anime's
    // already-known episodes and media files, reusing the exact services a full folder scan uses
    // for the same steps. Unlike a rescan, it never adds, updates or removes MediaFiles rows, so
    // it stays distinct from the filesystem scan.
    public async Task<AnimeRepairLocalRefreshResult> RefreshLocalAsync(
        Guid animeId,
        CancellationToken cancellationToken)
    {
        var folder = await ResolveFolderAsync(animeId, cancellationToken);
        if (folder is null)
        {
            return new AnimeRepairLocalRefreshResult(0, 0, 0, 0, 0);
        }

        var root = await db.LibraryRoots
            .AsNoTracking()
            .SingleAsync(x => x.Id == folder.RootId, cancellationToken);
        var animeDirectory = Path.GetFullPath(Path.Combine(Path.GetFullPath(root.Path), folder.Folder));

        var anime = await db.Anime.SingleAsync(x => x.Id == animeId, cancellationToken);
        var episodes = await db.Episodes
            .Where(x => x.AnimeId == animeId)
            .ToListAsync(cancellationToken);
        var episodeIds = episodes.Select(x => x.Id).ToArray();
        var mediaFiles = await db.MediaFiles
            .Where(x => episodeIds.Contains(x.EpisodeId))
            .ToListAsync(cancellationToken);

        var nfoWarnings = RereadNfo(anime, episodes, mediaFiles, animeDirectory);
        await db.SaveChangesAsync(cancellationToken);

        var subtitlesImported = 0;
        var sidecarListings = new SubtitleSidecarDirectoryCache();
        foreach (var episode in episodes)
        {
            var mediaPaths = mediaFiles
                .Where(x => x.EpisodeId == episode.Id)
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .Select(x => x.Path)
                .ToArray();
            if (mediaPaths.Length == 0)
            {
                continue;
            }

            var sidecar = await subtitleImport.ImportPreferredSidecarAsync(
                episode.Id,
                mediaPaths,
                sidecarListings,
                cancellationToken);
            if (sidecar.Status == SubtitleSidecarImportStatus.Imported)
            {
                subtitlesImported++;
            }
        }

        var artwork = await LocalAnimeArtworkImporter.ImportAsync(animeId, animeDirectory, cancellationToken);

        return new AnimeRepairLocalRefreshResult(
            episodes.Count,
            subtitlesImported,
            artwork.ImportedCount,
            artwork.UnchangedCount,
            nfoWarnings);
    }

    // Re-reads tvshow.nfo and each episode's sidecar NFO exactly like the scanner does, using the
    // same NfoReader rules; only the show/episode title fields are refreshed here since that is
    // all the scanner itself derives from local NFO.
    private static int RereadNfo(
        Anime anime,
        IReadOnlyList<Episode> episodes,
        IReadOnlyList<MediaFile> mediaFiles,
        string animeDirectory)
    {
        var warnings = 0;

        var showNfoPath = Path.Combine(animeDirectory, NfoFileIndex.ShowFileName);
        if (File.Exists(showNfoPath))
        {
            var show = NfoReader.ReadShow(showNfoPath);
            if (show.Value is null)
            {
                warnings++;
            }
            else if (show.Value.Title is { Length: > 0 } title &&
                     !string.Equals(anime.Title, title, StringComparison.Ordinal))
            {
                anime.Title = title;
            }
        }

        foreach (var episode in episodes)
        {
            var episodeMediaPaths = mediaFiles
                .Where(x => x.EpisodeId == episode.Id)
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .Select(x => x.Path);

            foreach (var mediaPath in episodeMediaPaths)
            {
                var nfoPath = Path.Combine(
                    Path.GetDirectoryName(mediaPath)!,
                    Path.GetFileNameWithoutExtension(mediaPath) + ".nfo");
                if (!File.Exists(nfoPath))
                {
                    continue;
                }

                var nfo = NfoReader.ReadEpisodes(nfoPath);
                if (nfo.Value is null)
                {
                    warnings++;
                    continue;
                }

                var entry = nfo.Value.FirstOrDefault(x => x.Describes(episode.SeasonNumber, episode.Number));
                if (entry?.Title is { Length: > 0 } episodeTitle &&
                    !string.Equals(episode.Title, episodeTitle, StringComparison.Ordinal))
                {
                    episode.Title = episodeTitle;
                }

                break;
            }
        }

        return warnings;
    }

    // Forces MediaInventoryService to re-probe every media file of this anime by invalidating
    // their analyses, then brings each back up to date through its one existing analysis path
    // (EnsureAnalyzedAsync); no second probe path is introduced.
    public async Task<AnimeRepairReanalysisResult> ReanalyzeMediaAsync(
        Guid animeId,
        CancellationToken cancellationToken)
    {
        var mediaFileIds = await (
                from media in db.MediaFiles.AsNoTracking()
                join episode in db.Episodes.AsNoTracking() on media.EpisodeId equals episode.Id
                where episode.AnimeId == animeId
                select media.Id)
            .ToArrayAsync(cancellationToken);

        if (mediaFileIds.Length == 0)
        {
            return new AnimeRepairReanalysisResult(0, 0, 0, 0);
        }

        await mediaInventory.InvalidateAsync(mediaFileIds, cancellationToken);

        var analyzed = 0;
        var failed = 0;
        var deferred = 0;
        foreach (var mediaFileId in mediaFileIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = await mediaInventory.EnsureAnalyzedAsync(mediaFileId, cancellationToken);
            switch (entry?.Status)
            {
                case MediaAnalysisStatus.Succeeded:
                    analyzed++;
                    break;
                case MediaAnalysisStatus.Failed:
                    failed++;
                    break;
                case MediaAnalysisStatus.Pending:
                    deferred++;
                    break;
            }
        }

        return new AnimeRepairReanalysisResult(mediaFileIds.Length, analyzed, failed, deferred);
    }

    // Searches AniList the same way the anime page does, but scores every candidate alone through
    // the exact matcher AutoMatchAsync uses (one candidate in, one decision out) so the evidence
    // shown for each result is specific to it instead of only the automatic winner.
    public async Task<IReadOnlyList<AnimeRepairMatchCandidate>> SearchCandidatesAsync(
        Guid animeId,
        string? query,
        CancellationToken cancellationToken)
    {
        var local = await db.Anime
            .AsNoTracking()
            .Where(x => x.Id == animeId)
            .Select(x => new
            {
                x.Title,
                EpisodeCount = db.Episodes.Count(episode => episode.AnimeId == x.Id),
                SeasonCount = db.Episodes
                    .Where(episode => episode.AnimeId == x.Id)
                    .Select(episode => episode.SeasonNumber)
                    .Distinct()
                    .Count()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (local is null)
        {
            return [];
        }

        var candidates = await metadataService.SearchAsync(
            AniListMetadataProvider.ProviderKey,
            string.IsNullOrWhiteSpace(query) ? local.Title : query.Trim(),
            8,
            cancellationToken);

        var input = new AutomaticMediaMatchInput(
            local.Title,
            UnitCount: local.SeasonCount == 1 && local.EpisodeCount > 0 ? local.EpisodeCount : null,
            Format: "ANIME");

        return candidates
            .Select(candidate =>
            {
                var matchCandidate = new AutomaticMediaMatchCandidate(
                    candidate.Provider,
                    candidate.ExternalId,
                    candidate.PreferredTitle,
                    new[]
                    {
                        candidate.PreferredTitle,
                        candidate.EnglishTitle ?? "",
                        candidate.RomajiTitle ?? "",
                        candidate.NativeTitle ?? ""
                    },
                    candidate.SeasonYear,
                    candidate.EpisodeCount,
                    candidate.Format);

                // Scoring one candidate at a time reuses AutomaticMediaMatcher.Select verbatim;
                // Disposition/RunnerUpScore are meaningless here and intentionally unused.
                var decision = AutomaticMediaMatcher.Select(input, [matchCandidate]);
                return new AnimeRepairMatchCandidate(candidate, decision.Score, decision.Evidence);
            })
            .OrderByDescending(x => x.Score)
            .ToArray();
    }
}
