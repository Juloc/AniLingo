using Jularr.Web.Features.Watchlist;

namespace Jularr.Web.Features.Calendar;

/// <summary>
/// Projects provider release-cache rows for profile-local followed works that are not in the
/// library yet. The follow state is owned by Jularr; provider data only supplies release dates.
/// </summary>
public sealed class WatchlistReleaseEventSource(
    ReleaseCalendarCacheStore cache,
    WatchlistStore watchlist) : IReleaseEventSource
{
    public string Name => "watchlist";

    public IReadOnlyCollection<ReleaseMediaType> MediaTypes { get; } =
        Enum.GetValues<ReleaseMediaType>();

    public async Task<IReadOnlyList<ReleaseEvent>> GetEventsAsync(
        ReleaseEventQuery query,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.ProfileId))
        {
            return [];
        }

        var items = (await watchlist.GetEffectiveAsync(query.ProfileId, cancellationToken))
            .Where(item => item.LocalMediaId is null)
            .Where(item => ToReleaseMediaType(item.Identity.MediaType) is { } type && query.Wants(type))
            .ToArray();
        if (items.Length == 0)
        {
            return [];
        }

        var events = new List<ReleaseEvent>();
        foreach (var providerGroup in items.GroupBy(item => item.Identity.ProviderKey, StringComparer.Ordinal))
        {
            var byExternalId = providerGroup
                .GroupBy(item => item.Identity.ExternalKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var releases = await cache.GetReleasesAsync(
                providerGroup.Key,
                query.Start.AddDays(-1),
                query.End.AddDays(1),
                query.IncludeUndated,
                byExternalId.Keys.ToArray(),
                cancellationToken);

            foreach (var release in releases)
            {
                if (!byExternalId.TryGetValue(release.ExternalId, out var item) ||
                    ToReleaseMediaType(item.Identity.MediaType) is not { } mediaType ||
                    !KindFits(mediaType, release.Kind) ||
                    !query.Includes(release.Date))
                {
                    continue;
                }

                var unit = release.Kind is ReleaseKind.Episode or ReleaseKind.SeasonPremiere or ReleaseKind.Chapter or ReleaseKind.Volume
                    ? new ReleaseUnit(release.UnitNumber)
                    : null;
                var local = new ReleaseLocalStatus(false, true, ReleaseLocalState.Monitored);
                events.Add(new ReleaseEvent(
                    ReleaseEvent.BuildId(mediaType, item.StableId, release.Kind, unit, release.Provider),
                    mediaType,
                    item.StableId,
                    null,
                    release.Kind,
                    item.Title,
                    unit,
                    release.Date,
                    release.Provider,
                    release.ExternalId,
                    local,
                    item.CoverImageUrl,
                    DetailsUrl: item.DetailsUrl ?? "/Watchlist"));
            }
        }

        return events;
    }

    private static ReleaseMediaType? ToReleaseMediaType(WatchlistMediaType type) => type switch
    {
        WatchlistMediaType.Anime => ReleaseMediaType.Anime,
        WatchlistMediaType.Tv => ReleaseMediaType.Tv,
        WatchlistMediaType.Movie => ReleaseMediaType.Movie,
        WatchlistMediaType.Manga => ReleaseMediaType.Manga,
        WatchlistMediaType.LightNovel => ReleaseMediaType.LightNovel,
        WatchlistMediaType.Book => ReleaseMediaType.Book,
        _ => null
    };

    private static bool KindFits(ReleaseMediaType type, ReleaseKind kind) => type switch
    {
        ReleaseMediaType.Anime or ReleaseMediaType.Tv =>
            kind is ReleaseKind.Episode or ReleaseKind.SeasonPremiere,
        ReleaseMediaType.Movie =>
            kind is ReleaseKind.Cinema or ReleaseKind.Digital or ReleaseKind.Streaming or ReleaseKind.Physical,
        ReleaseMediaType.Manga =>
            kind is ReleaseKind.SeriesStart or ReleaseKind.Chapter or ReleaseKind.Volume,
        ReleaseMediaType.LightNovel =>
            kind is ReleaseKind.SeriesStart or ReleaseKind.Volume or ReleaseKind.Chapter,
        ReleaseMediaType.Book => kind == ReleaseKind.Publication,
        _ => false
    };
}
