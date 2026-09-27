namespace Jularr.Web.Features.Calendar;

public static class ReleaseCalendarRegistration
{
    /// <summary>
    /// The release calendar: the provider cache, its bounded background refresh, the event
    /// sources and the read model. A new media type only needs another <see cref="IReleaseEventSource"/>.
    /// </summary>
    public static IServiceCollection AddReleaseCalendar(this IServiceCollection services)
    {
        services.AddHttpClient<IAniListReleaseScheduleClient, AniListReleaseScheduleClient>(client =>
        {
            client.BaseAddress = new Uri("https://graphql.anilist.co/");
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });
        services.AddScoped<ReleaseCalendarCacheStore>();
        services.AddScoped<ReleaseCalendarRefresher>();
        services.AddHostedService<ReleaseCalendarRefreshService>();

        services.AddScoped<IReleaseEventSource, AniListReleaseEventSource>();
        services.AddScoped<IReleaseEventSource, NovelChapterReleaseEventSource>();
        services.AddScoped<IReleaseEventSource, BookReleaseEventSource>();
        services.AddScoped<ReleaseCalendarService>();
        return services;
    }
}
