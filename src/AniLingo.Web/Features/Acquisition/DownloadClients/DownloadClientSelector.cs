using AniLingo.Web.Features.Acquisition.Health;

namespace AniLingo.Web.Features.Acquisition.DownloadClients;

/// <summary>
/// Picks the enabled, healthy download clients that support a release
/// protocol, highest priority (lowest number) first.
/// </summary>
public sealed class DownloadClientSelector(
    DownloadClientStore store,
    AcquisitionHealthStore health)
{
    public async Task<IReadOnlyList<DownloadClientEntry>> SelectAsync(
        DownloadProtocol protocol,
        CancellationToken cancellationToken)
    {
        var candidates = (await store.LoadAllAsync(cancellationToken))
            .Where(entry => entry.Enabled && entry.Protocol == protocol)
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
