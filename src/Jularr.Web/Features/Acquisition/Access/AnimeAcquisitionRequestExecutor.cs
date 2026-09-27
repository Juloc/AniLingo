using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Monitoring;
using Jularr.Web.Features.Acquisition.Ownership;
using Jularr.Web.Features.Acquisition.Pipeline;
using Jularr.Web.Features.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Acquisition.Access;

/// <summary>
/// Automatic anime acquisition for a request identified by its AniList id: when the series is in
/// the library, monitoring and search-on-add are switched on through the acquisition pipeline
/// (the same call AniList list auto-monitor uses). A series that is not in the library yet stays
/// approved for the owner to add.
/// </summary>
public sealed class AnimeAcquisitionRequestExecutor(
    AppDbContext db,
    AcquisitionOwnershipStore ownershipStore,
    AnimeMonitoringStore monitoringStore,
    AnimeAcquisitionPipeline pipeline) : IAcquisitionRequestExecutor
{
    public MediaAcquisitionKind Kind => MediaAcquisitionKind.Anime;

    public async Task<AcquisitionExecution> ExecuteAsync(AcquisitionRequest request, CancellationToken cancellationToken)
    {
        var anime = await (
                from metadata in db.AnimeMetadata.AsNoTracking()
                join item in db.Anime.AsNoTracking() on metadata.AnimeId equals item.Id
                where metadata.Provider == AniListMetadataProvider.ProviderKey && metadata.ExternalId == request.ExternalId
                select new { item.Id, item.Key })
            .FirstOrDefaultAsync(cancellationToken);
        if (anime is null)
        {
            return new AcquisitionExecution(
                AcquisitionRequestStatus.Approved,
                "Not in the library yet — the owner adds the series.");
        }

        var ownership = await ownershipStore.LoadAsync(cancellationToken);
        if (SonarrParallelSafety.GetMode(ownership, anime.Key) == AnimeManagementMode.ReadOnlyCoexistence)
        {
            return new AcquisitionExecution(
                AcquisitionRequestStatus.Approved,
                "Sonarr manages this series — the owner decides in Sonarr migration.",
                ResultUrl: $"/Library/Anime/{anime.Id}");
        }

        var monitoring = await monitoringStore.LoadAsync(cancellationToken);
        var existing = monitoring.Anime.TryGetValue(anime.Key, out var settings) ? settings : null;
        if (existing?.Monitored != true)
        {
            await pipeline.UpdateAnimeSettingsAsync(
                anime.Id,
                monitored: true,
                searchOnAdd: true,
                profileId: null,
                indexerIds: existing?.IndexerIds ?? [],
                cancellationToken,
                tagIds: existing?.TagIds,
                targetRootId: existing?.TargetRootId);
        }

        return new AcquisitionExecution(
            AcquisitionRequestStatus.Completed,
            "Monitoring is on; missing episodes are searched automatically.",
            ResultUrl: $"/Library/Anime/{anime.Id}");
    }
}
