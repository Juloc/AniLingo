using Jularr.Web.Features.Franchises;
using Jularr.Web.Features.Metadata;
using Jularr.Web.Features.Novels;
using Jularr.Web.Features.Tracking;
using Jularr.Web.Features.Watchlist;

namespace Jularr.Web.Features.Calendar;

public static class ReleaseCalendarRegistration
{
    /// <summary>
    /// The release calendar: the provider cache, its bounded background refresh, local follow
    /// state, franchise discovery and event sources.
    /// </summary>
    public static IServiceCollection AddReleaseCalendar(this IServiceCollection services)
    {
        services.AddHttpClient<IAniListReleaseScheduleClient, AniListReleaseScheduleClient>(client =>
        {
            client.BaseAddress = new Uri("https://graphql.anilist.co/");
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }).AddHttpMessageHandler<AniListRateLimitHandler>();

        // Franchise relations use the metadata clients; their 429s pause every background job.
        services.AddHttpClient(nameof(AniListMetadataProvider)).AddHttpMessageHandler<AniListRateLimitHandler>();
        services.AddHttpClient(nameof(NovelAniListProvider)).AddHttpMessageHandler<AniListRateLimitHandler>();
        services.AddSingleton<AniListRequestLimiter>();

        services.AddScoped<WatchlistStore>();
        services.AddScoped<WatchlistLibraryResolver>();
        services.AddScoped<FranchiseStore>();
        services.AddScoped<MediaRelationStore>();
        services.AddScoped<IFranchiseRelationSource, AniListFranchiseRelationSource>();
        services.AddScoped<FranchiseService>();
        services.AddSingleton<FranchiseRefreshSignal>();
        services.AddHostedService<FranchiseRefreshService>();

        services.AddScoped<ReleaseCalendarCacheStore>();
        services.AddScoped<ReleaseCalendarRefresher>();
        services.AddHostedService<ReleaseCalendarRefreshService>();

        services.AddScoped<IReleaseEventSource, AniListReleaseEventSource>();
        services.AddScoped<IReleaseEventSource, NovelChapterReleaseEventSource>();
        services.AddScoped<IReleaseEventSource, BookReleaseEventSource>();
        services.AddScoped<IReleaseEventSource, WatchlistReleaseEventSource>();
        services.AddScoped<ReleaseCalendarService>();
        return services;
    }
}
