using System.Net.Http.Json;

namespace AniLingo.Web.Features.Sonarr;

public interface ISonarrSeriesMonitoringClient
{
    Task SetMonitoredAsync(
        SonarrConnectionSettings settings,
        int seriesId,
        bool monitored,
        CancellationToken cancellationToken);
}

// The single Sonarr mutation Jularr performs: toggling series monitoring through the series
// editor. It is only called by SonarrMigrationService for an explicit owner migration action
// (hand over / revert with the Sonarr monitoring option). It never deletes series or files.
public sealed class SonarrSeriesMonitoringClient(IHttpClientFactory httpClientFactory)
    : ISonarrSeriesMonitoringClient
{
    public async Task SetMonitoredAsync(
        SonarrConnectionSettings settings,
        int seriesId,
        bool monitored,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seriesId);
        if (!SonarrObserverClient.TryCreateBaseUri(settings.BaseUrl, out var baseUri, out var error))
        {
            throw new SonarrObserverException(error!);
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new SonarrObserverException("Sonarr API key is required.");
        }

        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri(baseUri, "api/v3/series/editor"))
        {
            Content = JsonContent.Create(new SeriesEditorRequest([seriesId], monitored))
        };
        request.Headers.TryAddWithoutValidation("X-Api-Key", settings.ApiKey.Trim());
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SonarrObserverException(
                $"Sonarr series editor returned HTTP {(int)response.StatusCode}; monitoring was not changed.");
        }
    }

    private sealed record SeriesEditorRequest(int[] SeriesIds, bool Monitored);
}
