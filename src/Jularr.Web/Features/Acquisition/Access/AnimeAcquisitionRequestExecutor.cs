using Jularr.Web.Data;
using Jularr.Web.Features.Acquisition.Monitoring;
using Jularr.Web.Features.Acquisition.Naming;
using Jularr.Web.Features.Acquisition.Ownership;
using Jularr.Web.Features.Acquisition.Pipeline;
using Jularr.Web.Features.Discovery;
using Jularr.Web.Features.Library;
using Jularr.Web.Features.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Acquisition.Access;

/// <summary>
/// Automatic anime acquisition for a request identified by its AniList id, like adding a series
/// in Sonarr: a series that is not in the library yet is created (AniList match, Jularr-managed,
/// first library root, default quality profile), then monitoring and search-on-add are switched
/// on through the acquisition pipeline and the Usenet search is queued. The importer later puts
/// the files into the series folder the naming profile builds, and the scan finds this entry by
/// the key of that folder.
/// </summary>
public sealed class AnimeAcquisitionRequestExecutor(
    AppDbContext db,
    AnimeMetadataService metadata,
    AnimeNamingProfileStore namingStore,
    AcquisitionOwnershipStore ownershipStore,
    AnimeMonitoringStore monitoringStore,
    AnimeAcquisitionPipeline pipeline,
    AnimeAcquisitionScheduler scheduler) : IAcquisitionRequestExecutor
{
    public MediaAcquisitionKind Kind => MediaAcquisitionKind.Anime;

    public async Task<AcquisitionExecution> ExecuteAsync(AcquisitionRequest request, CancellationToken cancellationToken)
    {
        var anime = await (
                from match in db.AnimeMetadata.AsNoTracking()
                join item in db.Anime.AsNoTracking() on match.AnimeId equals item.Id
                where match.Provider == AniListMetadataProvider.ProviderKey && match.ExternalId == request.ExternalId
                select new { item.Id, item.Key })
            .FirstOrDefaultAsync(cancellationToken);
        if (anime is not null)
        {
            return await MonitorAsync(anime.Id, anime.Key, targetRootId: null, "Monitoring is on; missing episodes are searched automatically.", cancellationToken);
        }

        var root = await db.LibraryRoots
            .AsNoTracking()
            .Where(item => item.IsEnabled)
            .OrderBy(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (root is null)
        {
            return new AcquisitionExecution(
                AcquisitionRequestStatus.Failed,
                "No library root is set up. Add one under Admin → System, then retry.");
        }

        var candidate = await metadata.GetCandidateAsync(AniListMetadataProvider.ProviderKey, request.ExternalId, cancellationToken);
        if (candidate is null)
        {
            return new AcquisitionExecution(AcquisitionRequestStatus.Failed, "AniList has no entry with this id.");
        }

        var created = new Anime { Title = candidate.PreferredTitle };
        created.Key = SeriesKey(await namingStore.LoadAsync(cancellationToken), created, candidate, root);
        if (created.Key.Length == 0)
        {
            return new AcquisitionExecution(AcquisitionRequestStatus.Failed, "The naming profile builds an empty series folder name for this title.");
        }

        var existing = await db.Anime.AsNoTracking().SingleOrDefaultAsync(item => item.Key == created.Key, cancellationToken);
        if (existing is not null)
        {
            // A local series of the same name without an AniList match is this series.
            if (await db.AnimeMetadata.AnyAsync(item => item.AnimeId == existing.Id, cancellationToken))
            {
                return new AcquisitionExecution(
                    AcquisitionRequestStatus.Failed,
                    $"The library already has another series in the folder of '{existing.Title}'.",
                    ResultUrl: $"/Library/Anime/{existing.Id}");
            }

            created = existing;
        }
        else
        {
            db.Anime.Add(created);
            await db.SaveChangesAsync(cancellationToken);
        }

        var matched = await metadata.MatchAsync(created.Id, candidate, cancellationToken);
        if (!matched.Success)
        {
            return new AcquisitionExecution(AcquisitionRequestStatus.Failed, matched.Error, ResultUrl: $"/Library/Anime/{created.Id}");
        }

        DiscoveryCoordinator.InvalidateCache();
        if (existing is null)
        {
            // Jularr created the series, so Jularr manages it. A series that was already on disk
            // keeps its Sonarr decision.
            await ownershipStore.UpdateAsync(
                state => state.Anime.ContainsKey(created.Key)
                    ? state
                    : SonarrParallelSafety.SetMode(state, created.Key, AnimeManagementMode.JularrManaged, DateTimeOffset.UtcNow),
                cancellationToken);
        }

        return await MonitorAsync(created.Id, created.Key, root.Id, "Added to the library; the Usenet search has started.", cancellationToken);
    }

    // The key the library scan derives from the series folder the importer will create.
    internal static string SeriesKey(AnimeNamingState naming, Anime anime, AnimeMetadataCandidate candidate, LibraryRoot root)
    {
        var rootPath = Path.GetFullPath(root.Path);
        var resolution = AnimeNamingProfileStore.Resolve(naming, anime.Id, root.Id);
        var match = new AnimeMetadata
        {
            AnimeId = anime.Id,
            Provider = candidate.Provider,
            ExternalId = candidate.ExternalId,
            SeasonYear = candidate.SeasonYear
        };
        var folder = AnimeNamingFormatter.BuildSeriesFolderName(
            resolution.Profile,
            AnimeRenameService.BuildSeries(anime, match, rootPath, resolution.SeriesType));
        return MediaPathParser.AnimeKeyForSeriesFolder(folder);
    }

    private async Task<AcquisitionExecution> MonitorAsync(
        Guid animeId,
        string animeKey,
        Guid? targetRootId,
        string message,
        CancellationToken cancellationToken)
    {
        var ownership = await ownershipStore.LoadAsync(cancellationToken);
        if (SonarrParallelSafety.GetMode(ownership, animeKey) == AnimeManagementMode.ReadOnlyCoexistence)
        {
            return new AcquisitionExecution(
                AcquisitionRequestStatus.Approved,
                "Sonarr manages this series — the owner decides in Sonarr migration.",
                ResultUrl: $"/Library/Anime/{animeId}");
        }

        var monitoring = await monitoringStore.LoadAsync(cancellationToken);
        var existing = monitoring.Anime.TryGetValue(animeKey, out var settings) ? settings : null;
        if (existing?.Monitored != true)
        {
            var update = await pipeline.UpdateAnimeSettingsAsync(
                animeId,
                monitored: true,
                searchOnAdd: true,
                profileId: null,
                indexerIds: existing?.IndexerIds ?? [],
                cancellationToken,
                tagIds: existing?.TagIds,
                targetRootId: existing?.TargetRootId ?? targetRootId);
            if (update is { StartedMonitoring: true })
            {
                scheduler.RequestRun(update.AnimeKey, AnimeSearchTrigger.SearchOnAdd);
            }
        }

        return new AcquisitionExecution(
            AcquisitionRequestStatus.Completed,
            message,
            ResultUrl: $"/Library/Anime/{animeId}");
    }
}
