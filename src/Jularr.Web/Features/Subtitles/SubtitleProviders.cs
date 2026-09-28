namespace Jularr.Web.Features.Subtitles;

/// <summary>What a manual (or, eventually, automatic) subtitle search is looking for.</summary>
public sealed record SubtitleSearchRequest(
    Guid EpisodeId,
    string AnimeTitle,
    int SeasonNumber,
    int EpisodeNumber,
    string LanguageTag,
    bool Forced,
    bool Sdh);

/// <summary>
/// One candidate returned by an <see cref="ISubtitleProvider"/>. <see cref="ResultToken"/> is an
/// opaque, provider-defined value round-tripped back into <see cref="ISubtitleProvider.DownloadAsync"/>
/// - it never needs to mean anything outside that provider's own implementation.
/// </summary>
public sealed record SubtitleSearchResult(
    string ProviderId,
    string ResultToken,
    string LanguageTag,
    bool Forced,
    bool Sdh,
    string ReleaseName,
    string? UploaderOrSource,
    double? Score,
    DateTimeOffset? PublishedAt);

public sealed record SubtitleDownloadResult(
    bool Success,
    string? Format,
    string? Content,
    string? Message);

/// <summary>
/// Contract for a general subtitle-search/download source, distinct from Jimaku
/// (<see cref="SubtitleImportService"/>'s Japanese-only automatic import) which predates this
/// abstraction and is not migrated onto it. Concrete providers (OpenSubtitles, etc.) need an
/// account/API key and are intentionally not implemented here - see the "no subtitle providers
/// configured" state in Admin/Subtitles and Settings/Subtitles, and the follow-up issue linked
/// from #526. Never a torrent source: this project is usenet-only.
/// </summary>
public interface ISubtitleProvider
{
    /// <summary>Stable, unique identifier persisted on imported tracks and in settings.</summary>
    string Id { get; }

    string DisplayName { get; }

    Task<IReadOnlyList<SubtitleSearchResult>> SearchAsync(
        SubtitleSearchRequest request,
        CancellationToken cancellationToken);

    Task<SubtitleDownloadResult> DownloadAsync(
        SubtitleSearchResult result,
        CancellationToken cancellationToken);
}

public sealed record SubtitleManualSearchOutcome(
    string ProviderId,
    string ProviderDisplayName,
    IReadOnlyList<SubtitleSearchResult> Results,
    string? Error);

/// <summary>
/// Fans a manual search out to every registered <see cref="ISubtitleProvider"/> (today: none - see
/// <see cref="ISubtitleProvider"/>'s remarks) and imports a chosen result through the existing
/// subtitle-import path (<see cref="SubtitleImportService.ImportManualSearchResultAsync"/>), the
/// owner-only "search subtitles" scaffold #526 asks for.
/// </summary>
public sealed class SubtitleManualSearchService(
    IEnumerable<ISubtitleProvider> providers,
    SubtitleImportService importService)
{
    private readonly IReadOnlyList<ISubtitleProvider> providers = providers.ToArray();

    public bool HasProviders => providers.Count > 0;

    public IReadOnlyList<string> ProviderNames => providers.Select(p => p.DisplayName).ToArray();

    public async Task<IReadOnlyList<SubtitleManualSearchOutcome>> SearchAsync(
        SubtitleSearchRequest request,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<SubtitleManualSearchOutcome>(providers.Count);

        foreach (var provider in providers)
        {
            try
            {
                var results = await provider.SearchAsync(request, cancellationToken);
                outcomes.Add(new SubtitleManualSearchOutcome(provider.Id, provider.DisplayName, results, null));
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or InvalidOperationException)
            {
                // One misbehaving provider must not hide results from the others.
                outcomes.Add(new SubtitleManualSearchOutcome(provider.Id, provider.DisplayName, [], exception.Message));
            }
        }

        return outcomes;
    }

    public async Task<SubtitleDownloadResult> ImportAsync(
        Guid episodeId,
        SubtitleSearchResult result,
        CancellationToken cancellationToken)
    {
        var provider = providers.SingleOrDefault(p => p.Id == result.ProviderId);
        if (provider is null)
        {
            return new SubtitleDownloadResult(false, null, null, "Subtitle provider is no longer configured.");
        }

        var downloaded = await provider.DownloadAsync(result, cancellationToken);
        if (!downloaded.Success || string.IsNullOrWhiteSpace(downloaded.Content) || string.IsNullOrWhiteSpace(downloaded.Format))
        {
            return downloaded;
        }

        await importService.ImportManualSearchResultAsync(
            episodeId,
            provider.Id,
            result.ResultToken,
            result.LanguageTag,
            result.Forced,
            result.Sdh,
            downloaded.Format,
            DateTime.UtcNow,
            downloaded.Content,
            cancellationToken);

        return downloaded;
    }
}
