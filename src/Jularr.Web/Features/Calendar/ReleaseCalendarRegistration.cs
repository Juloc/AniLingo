using Jularr.Web.Features.Franchises;
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
        });

        services.AddScoped<WatchlistStore>();
        services.AddScoped<FranchiseStore>();
        services.AddScoped<FranchiseService>();
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
