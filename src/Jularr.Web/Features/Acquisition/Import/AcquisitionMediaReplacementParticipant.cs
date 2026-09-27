using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Naming;
using Jularr.Web.Features.Acquisition.Ownership;
using Jularr.Web.Features.Media.Optimization;
using Jularr.Web.Features.Operations;
using Jularr.Web.Features.Sonarr;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Acquisition.Import;

// Keeps acquisition's path-keyed state consistent when a library file is replaced by its
// optimized copy: the replacement waits for running imports/renames, obeys Sonarr ownership like a
// rename (the path changes), and the owned-path registry and import records follow the new path.
public sealed class AcquisitionMediaReplacementParticipant(
    AppDbContext db,
    AcquisitionOwnershipStore ownershipStore,
    SonarrObservationService observation,
    AnimeImportStore imports) : IMediaFileReplacementParticipant
{
    public async Task<MediaReplacementCheck> CheckAsync(
        MediaFileReplacement replacement,
        CancellationToken cancellationToken)
    {
        // Renames run to completion (they refuse to start next to an import or scan), so waiting is
        // safe. A deferred import keeps its Operation running while it waits for a queued scan, so
        // imports are excluded through the executor's gate instead, which only a moving import holds.
        var rename = (await new OperationStore(db).ListAsync(
                new OperationListFilter(View: "active", Kind: AnimeRenameService.OperationKind),
                cancellationToken))
            .FirstOrDefault(operation => operation.Status == OperationStatus.Running);
        if (rename is not null)
        {
            return new(MediaReplacementVerdict.Wait, $"Waiting for '{rename.Title}' to finish.");
        }

        if (await FindAnimeKeyAsync(replacement.MediaFileId, cancellationToken) is { } animeKey)
        {
            var snapshot = await observation.GetSnapshotAsync(forceRefresh: true, cancellationToken);
            var decision = SonarrParallelSafety.CanRename(
                snapshot,
                animeKey,
                replacement.SourcePath,
                replacement.TargetPath,
                DateTimeOffset.UtcNow);
            if (!decision.Allowed)
            {
                return new(MediaReplacementVerdict.Blocked, $"Ownership: {decision.Reason}");
            }
        }

        return AnimeImportExecutor.TryEnterExecution() is { } lease
            ? new(MediaReplacementVerdict.Allowed, Lease: lease)
            : new(MediaReplacementVerdict.Wait, "Waiting for an anime import to finish.");
    }

    public async Task OnReplacedAsync(
        MediaFileReplacement replacement,
        CancellationToken cancellationToken)
    {
        string MapPath(string path) =>
            SonarrOwnershipRecognizer.PathEquals(path, replacement.SourcePath) ? replacement.TargetPath : path;

        var animeKey = await FindAnimeKeyAsync(replacement.MediaFileId, cancellationToken);
        if (animeKey is null)
        {
            return;
        }

        await ownershipStore.UpdateAsync(
            state => AnimeRenameService.RekeyOwnership(state, animeKey, animeKey, MapPath),
            cancellationToken);
        await imports.RekeyAnimeAsync(animeKey, animeKey, MapPath, cancellationToken);
    }

    private Task<string?> FindAnimeKeyAsync(Guid mediaFileId, CancellationToken cancellationToken) =>
        (from media in db.MediaFiles.AsNoTracking()
         join episode in db.Episodes.AsNoTracking() on media.EpisodeId equals episode.Id
         join anime in db.Anime.AsNoTracking() on episode.AnimeId equals anime.Id
         where media.Id == mediaFileId
         select anime.Key)
        .FirstOrDefaultAsync(cancellationToken);
}
