using Jularr.Web.Features.Instance;

namespace Jularr.Web.Features.Calendar;

public static class ReleaseInstanceModules
{
    public static InstanceModule For(ReleaseMediaType mediaType) =>
        mediaType switch
        {
            ReleaseMediaType.Anime => InstanceModule.Anime,
            ReleaseMediaType.Tv => InstanceModule.Tv,
            ReleaseMediaType.Movie => InstanceModule.Movie,
            ReleaseMediaType.Manga => InstanceModule.Manga,
            ReleaseMediaType.LightNovel => InstanceModule.Novel,
            ReleaseMediaType.Book => InstanceModule.Book,
            _ => throw new ArgumentOutOfRangeException(nameof(mediaType))
        };
}

public sealed record ReleaseCalendarDay(DateOnly Date, IReadOnlyList<ReleaseEvent> Events);

/// <summary>
/// The calendar read model for a day range: every day with its exactly dated releases, and the
/// releases whose date is only a month, quarter, year (overlapping the range) or unknown.
/// </summary>
public sealed record ReleaseCalendarResult(
    DateOnly Start,
    DateOnly End,
    IReadOnlyList<ReleaseCalendarDay> Days,
    IReadOnlyList<ReleaseEvent> Imprecise,
    IReadOnlyList<string> FailedSources)
{
    public bool IsEmpty => Imprecise.Count == 0 && Days.All(day => day.Events.Count == 0);
}

/// <summary>Groups normalized events into calendar days in the viewer's time zone. Pure; no I/O.</summary>
public static class ReleaseCalendarAssembler
{
    public static (IReadOnlyList<ReleaseCalendarDay> Days, IReadOnlyList<ReleaseEvent> Imprecise) Assemble(
        IEnumerable<ReleaseEvent> events,
        DateOnly start,
        DateOnly end,
        TimeZoneInfo zone,
        ReleaseCalendarFilter filter,
        DateTimeOffset now)
    {
        var byDay = new Dictionary<DateOnly, List<ReleaseEvent>>();
        var imprecise = new List<ReleaseEvent>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var release in WithoutFollowedDuplicates(events))
        {
            if (!seen.Add(release.Id) || !filter.Matches(release, now, zone))
            {
                continue;
            }

            if (release.Date.ExactDay(zone) is { } day)
            {
                if (day >= start && day <= end)
                {
                    (byDay.TryGetValue(day, out var list) ? list : byDay[day] = []).Add(release);
                }

                continue;
            }

            if (release.Date.Precision == ReleaseDatePrecision.Unknown || release.Date.Overlaps(start, end, zone))
            {
                imprecise.Add(release);
            }
        }

        var days = new List<ReleaseCalendarDay>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            days.Add(new ReleaseCalendarDay(
                day,
                byDay.TryGetValue(day, out var list) ? Order(list) : []));
        }

        return (days, Order(imprecise));
    }

    /// <summary>
    /// Drops watchlist events for a provider release the library already shows: the same provider
    /// entry, kind and date. The library event wins because it carries the local state.
    /// </summary>
    public static IReadOnlyList<ReleaseEvent> WithoutFollowedDuplicates(IEnumerable<ReleaseEvent> events)
    {
        var all = events as IReadOnlyList<ReleaseEvent> ?? events.ToArray();
        var library = all
            .Where(release => release.Local.State != ReleaseLocalState.Following)
            .Select(ProviderKey)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        return all
            .Where(release => release.Local.State != ReleaseLocalState.Following ||
                              ProviderKey(release) is not { } key ||
                              !library.Contains(key))
            .ToArray();
    }

    private static string? ProviderKey(ReleaseEvent release) =>
        string.IsNullOrWhiteSpace(release.ProviderExternalId)
            ? null
            : string.Join(
                '|',
                release.Provider.ToLowerInvariant(),
                release.ProviderExternalId,
                release.Kind,
                release.Date.ToStorage());

    public static IReadOnlyList<ReleaseEvent> Order(IEnumerable<ReleaseEvent> events) =>
        events
            .OrderBy(release => release.Date.SortKey)
            .ThenBy(release => release.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(release => release.Unit?.Season ?? 0)
            .ThenBy(release => release.Unit?.Number ?? 0)
            .ToArray();
}

/// <summary>
/// Reads the calendar from every registered <see cref="IReleaseEventSource"/>. Each source is
/// isolated: one that fails is reported and the others still render.
/// </summary>
public sealed class ReleaseCalendarService(
    IEnumerable<IReleaseEventSource> sources,
    ILogger<ReleaseCalendarService> logger,
    IInstanceModuleService? instanceModules = null)
{
    public const int MaxRangeDays = 62;

    private readonly IReadOnlyList<IReleaseEventSource> registered = sources.ToArray();

    /// <summary>Media types some source can produce, before instance-level filtering.</summary>
    public IReadOnlyList<ReleaseMediaType> SupportedMediaTypes =>
        registered.SelectMany(source => source.MediaTypes).Distinct().Order().ToArray();

    public async Task<IReadOnlyList<ReleaseMediaType>> GetSupportedMediaTypesAsync(
        CancellationToken cancellationToken)
    {
        if (instanceModules is null)
        {
            return SupportedMediaTypes;
        }

        var settings = await instanceModules.GetAsync(cancellationToken);
        return SupportedMediaTypes
            .Where(type => settings.IsEnabled(ReleaseInstanceModules.For(type)))
            .ToArray();
    }

    public async Task<ReleaseCalendarResult> GetAsync(
        DateOnly start,
        DateOnly end,
        TimeZoneInfo zone,
        ReleaseCalendarFilter filter,
        DateTimeOffset now,
        bool includeUndated,
        CancellationToken cancellationToken)
    {
        if (end < start || end.DayNumber - start.DayNumber >= MaxRangeDays)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "The calendar range is limited to two months.");
        }

        var enabledMediaTypes = (await GetSupportedMediaTypesAsync(cancellationToken)).ToHashSet();
        var requestedMediaTypes = filter.MediaTypes.Count == 0
            ? enabledMediaTypes
            : filter.MediaTypes.Where(enabledMediaTypes.Contains).ToHashSet();
        if (filter.MediaTypes.Count > 0 && requestedMediaTypes.Count == 0)
        {
            var empty = ReleaseCalendarAssembler.Assemble([], start, end, zone, filter, now);
            return new ReleaseCalendarResult(start, end, empty.Days, empty.Imprecise, []);
        }

        var mediaType = requestedMediaTypes.Count == 1 ? requestedMediaTypes.Single() : (ReleaseMediaType?)null;
        var (events, failed) = await CollectAsync(
            new ReleaseEventQuery(start, end, zone, now, includeUndated, mediaType, ProfileId: filter.ProfileId),
            requestedMediaTypes,
            cancellationToken);
        var (days, imprecise) = ReleaseCalendarAssembler.Assemble(events, start, end, zone, filter, now);
        return new ReleaseCalendarResult(start, end, days, imprecise, failed);
    }

    /// <summary>
    /// The next known releases of one media entry for its detail page: exact dates from today on
    /// that have not passed, then future periods and unknown dates.
    /// </summary>
    public async Task<IReadOnlyList<ReleaseEvent>> GetUpcomingAsync(
        ReleaseMediaType mediaType,
        Guid mediaId,
        TimeZoneInfo zone,
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken)
    {
        if (instanceModules is not null)
        {
            var settings = await instanceModules.GetAsync(cancellationToken);
            if (!settings.IsEnabled(ReleaseInstanceModules.For(mediaType)))
            {
                return [];
            }
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var (events, _) = await CollectAsync(
            new ReleaseEventQuery(today, today.AddDays(MaxRangeDays * 2), zone, now, IncludeUndated: true, mediaType, mediaId),
            new HashSet<ReleaseMediaType> { mediaType },
            cancellationToken);

        return ReleaseCalendarAssembler.Order(events
                .Where(release => release.MediaType == mediaType && release.MediaId == mediaId)
                .Where(release => !release.Date.IsReleased(now, zone))
                .DistinctBy(release => release.Id))
            .Take(limit)
            .ToArray();
    }

    private async Task<(IReadOnlyList<ReleaseEvent> Events, IReadOnlyList<string> Failed)> CollectAsync(
        ReleaseEventQuery query,
        IReadOnlySet<ReleaseMediaType> mediaTypes,
        CancellationToken cancellationToken)
    {
        var events = new List<ReleaseEvent>();
        var failed = new List<string>();
        foreach (var source in registered)
        {
            if (mediaTypes.Count > 0 && !source.MediaTypes.Any(mediaTypes.Contains))
            {
                continue;
            }

            try
            {
                events.AddRange(await source.GetEventsAsync(query, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Release source {Source} could not be read.", source.Name);
                failed.Add(source.Name);
            }
        }

        return (events, failed);
    }
}
