using Jularr.Web.Features.ReadingDiscovery;

namespace Jularr.Web.Features.ReadingSources;

public static class ReadingSourceRegistration
{
    /// <summary>
    /// Registers the reading source settings, health tracking and every catalog provider.
    /// A provider is added here and in <see cref="ReadingSourceCatalog"/> and nowhere else.
    /// </summary>
    public static IServiceCollection AddReadingSources(
        this IServiceCollection services,
        string dataRoot = "/data")
    {
        services.AddSingleton(new ReadingSourceSettingsStore(dataRoot));
        services.AddSingleton<ReadingSourceHealthTracker>();

        services.AddHttpClient<SyosetuCatalogClient>(ReadingSourceHttp.ConfigureClient);
        services.AddScoped<IReadingCatalogProvider>(
            provider => provider.GetRequiredService<SyosetuCatalogClient>());
        services.AddScoped<IReadingCatalogProvider, AniListCatalogProvider>();

        services.AddHttpClient<BookWalkerCatalogProvider>(ReadingSourceHttp.ConfigureClient);
        services.AddScoped<IReadingCatalogProvider>(
            provider => provider.GetRequiredService<BookWalkerCatalogProvider>());

        services.AddHttpClient<WebNovelCatalogProvider>(ReadingSourceHttp.ConfigureClient);
        services.AddScoped<IReadingCatalogProvider>(
            provider => provider.GetRequiredService<WebNovelCatalogProvider>());

        services.AddHttpClient<InternetArchiveCatalogProvider>(ReadingSourceHttp.ConfigureClient);
        services.AddScoped<IReadingCatalogProvider>(
            provider => provider.GetRequiredService<InternetArchiveCatalogProvider>());

        services.AddScoped<ReadingCatalogSearchService>();
        return services;
    }
}
