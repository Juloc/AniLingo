using Jularr.Web.Features.Providers;

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
/// account/API key and are built on the shared external-provider framework
/// (<see cref="ProviderExecutor"/>: timeouts, retries, Retry-After, health). A provider is only ever
/// offered through an <see cref="ISubtitleProviderSource"/> once the owner has configured it - see
/// the "no subtitle providers configured" state in Admin/Subtitles and Settings/Subtitles. Never a
/// torrent source: this project is usenet-only.
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

/// <summary>
/// Yields a configured <see cref="ISubtitleProvider"/>, or nothing while its integration is not
/// configured. Credentials are owner-editable at runtime, so a provider cannot be a fixed DI
/// registration: each integration contributes a source that inspects its own credential store on
/// demand, which keeps an unconfigured server on the "no subtitle providers configured" state and
/// makes a freshly saved key usable without a restart.
/// </summary>
public interface ISubtitleProviderSource
{
    /// <summary>The usable provider, or <see langword="null"/> when the integration is not configured.</summary>
    Task<ISubtitleProvider?> GetProviderAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A provider call failed in a way the owner can act on (rejected credentials, exhausted download
/// quota, unusable response). The message is safe to show and never contains a secret.
/// Derives from <see cref="InvalidOperationException"/> so it is reported as a per-provider
/// outcome by <see cref="SubtitleManualSearchService"/> rather than hiding other providers' results.
/// </summary>
public sealed class SubtitleProviderException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

public sealed record SubtitleManualSearchOutcome(
    string ProviderId,
    string ProviderDisplayName,
    IReadOnlyList<SubtitleSearchResult> Results,
    string? Error);

/// <summary>
/// Fans a manual search out to every configured <see cref="ISubtitleProvider"/> and imports a
/// chosen result through the existing subtitle-import path
/// (<see cref="SubtitleImportService.ImportManualSearchResultAsync"/>), the owner-only "search
/// subtitles" scaffold #526 asks for. Language, forced and SDH come from the searched language-profile
/// item and are carried unchanged onto the imported track, so an import satisfies exactly the item
/// that was searched.
/// </summary>
public sealed class SubtitleManualSearchService(
    IEnumerable<ISubtitleProviderSource> sources,
    SubtitleImportService importService)
{
    private readonly IReadOnlyList<ISubtitleProviderSource> sources = sources.ToArray();

    /// <summary>The providers that are configured right now, in registration order.</summary>
    public async Task<IReadOnlyList<ISubtitleProvider>> GetProvidersAsync(CancellationToken cancellationToken)
    {
        var providers = new List<ISubtitleProvider>(sources.Count);
        foreach (var source in sources)
        {
            if (await source.GetProviderAsync(cancellationToken) is { } provider)
            {
                providers.Add(provider);
            }
        }

        return providers;
    }

    public async Task<IReadOnlyList<SubtitleManualSearchOutcome>> SearchAsync(
        SubtitleSearchRequest request,
        CancellationToken cancellationToken)
    {
        var providers = await GetProvidersAsync(cancellationToken);
        var outcomes = new List<SubtitleManualSearchOutcome>(providers.Count);

        foreach (var provider in providers)
        {
            try
            {
                var results = await provider.SearchAsync(request, cancellationToken);
                outcomes.Add(new SubtitleManualSearchOutcome(provider.Id, provider.DisplayName, results, null));
            }
            catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
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
        var provider = (await GetProvidersAsync(cancellationToken)).SingleOrDefault(p => p.Id == result.ProviderId);
        if (provider is null)
        {
            return new SubtitleDownloadResult(false, null, null, "Subtitle provider is no longer configured.");
        }

        SubtitleDownloadResult downloaded;
        try
        {
            downloaded = await provider.DownloadAsync(result, cancellationToken);
        }
        catch (Exception exception) when (IsProviderFailure(exception, cancellationToken))
        {
            return new SubtitleDownloadResult(false, null, null, exception.Message);
        }

        if (!downloaded.Success)
        {
            return downloaded;
        }

        if (string.IsNullOrWhiteSpace(downloaded.Content) || string.IsNullOrWhiteSpace(downloaded.Format))
        {
            return new SubtitleDownloadResult(false, null, null, "The provider returned an empty subtitle.");
        }

        try
        {
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
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            return new SubtitleDownloadResult(false, null, null, exception.Message);
        }

        return downloaded;
    }

    // Transport faults, framework short-circuits (rate limit / open circuit) and provider-reported
    // problems become a per-provider outcome; a requested cancellation always propagates.
    private static bool IsProviderFailure(Exception exception, CancellationToken cancellationToken) =>
        exception switch
        {
            OperationCanceledException when cancellationToken.IsCancellationRequested => false,
            HttpRequestException or IOException or InvalidOperationException or TimeoutException => true,
            TaskCanceledException => true,
            ProviderRateLimitedException or ProviderUnavailableException => true,
            _ => false
        };
}
