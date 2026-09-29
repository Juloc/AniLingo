using Jularr.Web.Features.Providers;
using Jularr.Web.Features.Subtitles.OpenSubtitles;
using Microsoft.AspNetCore.DataProtection;

namespace Jularr.Web.Features.Subtitles;

/// <summary>
/// Registers the manual subtitle search (#526) and its concrete provider integrations (#560). Each
/// integration contributes an <see cref="ISubtitleProviderSource"/>, so a provider only appears once
/// the owner has configured it. Requires <see cref="ProviderFrameworkServiceCollectionExtensions.AddProviderFramework"/>.
/// </summary>
public static class SubtitleProviderServiceCollectionExtensions
{
    public static IServiceCollection AddSubtitleProviders(this IServiceCollection services)
    {
        services.AddScoped<SubtitleManualSearchService>();

        // OpenSubtitles (#560): credentials in /data/integrations/opensubtitles.json (secrets protected),
        // one shared login, and a typed client whose calls all run through ProviderExecutor.
        services.AddSingleton(provider => new OpenSubtitlesCredentialStore(
            provider.GetRequiredService<IDataProtectionProvider>(),
            OpenSubtitlesCredentialStore.DefaultDirectory,
            provider.GetService<ILogger<OpenSubtitlesCredentialStore>>()));
        services.AddSingleton<IProviderCredentialStore<OpenSubtitlesCredential>>(
            provider => provider.GetRequiredService<OpenSubtitlesCredentialStore>());
        services.AddSingleton<OpenSubtitlesSessionCache>();
        services.AddHttpClient<OpenSubtitlesClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddScoped<ISubtitleProviderSource, OpenSubtitlesProviderSource>();
        services.AddScoped<OpenSubtitlesSettingsService>();
        return services;
    }
}
