using AniLingo.Web.Features.Acquisition.Health;

namespace AniLingo.Web.Features.Acquisition.DownloadClients;

/// <summary>
/// Picks the enabled, healthy download clients, highest priority (lowest
/// number) first, so several SABnzbd connections fail over to each other.
/// </summary>
public sealed class DownloadClientSelector(
    DownloadClientStore store,
    AcquisitionHealthStore health)
{
    public async Task<IReadOnlyList<DownloadClientEntry>> SelectAsync(
        CancellationToken cancellationToken)
    {
        var candidates = (await store.LoadAllAsync(cancellationToken))
            .Where(entry => entry.Enabled)
            .OrderBy(entry => entry.Priority)
            .ToArray();

        var healthy = new List<DownloadClientEntry>();
        foreach (var entry in candidates)
        {
            if (await health.IsHealthyAsync(AcquisitionHealthKind.DownloadClient, entry.Id, cancellationToken))
            {
                healthy.Add(entry);
            }
        }

        return healthy;
    }
}
