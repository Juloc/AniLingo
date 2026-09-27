using System.Security.Cryptography;
using System.Text;

namespace Jularr.Web.Features.Ai;

public enum AiModelDiscovery
{
    Unknown,
    Supported,
    Unsupported
}

/// <summary>
/// Last known model catalog of one provider. A failed refresh keeps the previous models and marks
/// the catalog stale instead of dropping them.
/// </summary>
public sealed record AiModelCatalog(
    string ProviderKey,
    IReadOnlyList<AiModelDescriptor> Models,
    AiModelDiscovery Discovery,
    DateTimeOffset? FetchedAt,
    DateTimeOffset? LastAttemptAt,
    string? LastError)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    public static AiModelCatalog Empty(string providerKey) =>
        new(providerKey, [], AiModelDiscovery.Unknown, null, null, null);

    public bool HasModels => Models.Count > 0;

    /// <summary>True when the models shown are not confirmed by the latest attempt or are old.</summary>
    public bool IsStale(DateTimeOffset now) =>
        FetchedAt is null
        || (LastError is not null && LastAttemptAt >= FetchedAt)
        || now - FetchedAt.Value > MaxAge;

    public AiModelDescriptor? Find(string? modelId) =>
        string.IsNullOrWhiteSpace(modelId)
            ? null
            : Models.FirstOrDefault(x => string.Equals(x.Id, modelId, StringComparison.Ordinal));

    public AiModelDescriptor? DefaultModel =>
        Models.FirstOrDefault(x => x.IsDefault) ?? Models.FirstOrDefault();
}

public static class AiModelCatalogKeys
{
    public const string CodexServer = "codex-server";

    /// <summary>Personal catalogs are per profile and per endpoint, so a new base URL starts fresh.</summary>
    public static string OpenAiCompatible(string profileId, string baseUrl)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(baseUrl.Trim().TrimEnd('/').ToLowerInvariant()));
        return $"openai-compatible:{profileId}:{Convert.ToHexString(hash)[..16].ToLowerInvariant()}";
    }
}

/// <summary>Thrown by a provider that cannot list models; the UI then offers manual model entry.</summary>
public sealed class AiModelDiscoveryUnsupportedException(string message) : Exception(message);

public interface IAiModelCatalogStore
{
    Task<AiModelCatalog?> GetAsync(string providerKey, CancellationToken cancellationToken);

    Task SaveAsync(AiModelCatalog catalog, CancellationToken cancellationToken);
}

public sealed class AiModelCatalogService(IAiModelCatalogStore store, TimeProvider time)
{
    /// <summary>How long an empty catalog waits before it is discovered automatically again.</summary>
    public static readonly TimeSpan EmptyCatalogRetryInterval = TimeSpan.FromMinutes(30);

    public async Task<AiModelCatalog> GetCachedAsync(
        string providerKey,
        CancellationToken cancellationToken) =>
        await store.GetAsync(providerKey, cancellationToken)
        ?? AiModelCatalog.Empty(providerKey);

    /// <summary>
    /// True when a catalog should be discovered without an explicit refresh: it was never asked for,
    /// or it has no models and the last attempt is old enough to try again. Catalogs with models are
    /// only refreshed explicitly.
    /// </summary>
    public static bool NeedsDiscovery(AiModelCatalog catalog, DateTimeOffset now) =>
        catalog.LastAttemptAt is not { } attempted
        || (!catalog.HasModels && now - attempted >= EmptyCatalogRetryInterval);

    /// <summary>Returns the cached catalog and only asks the provider when <see cref="NeedsDiscovery"/>.</summary>
    public async Task<AiModelCatalog> GetOrDiscoverAsync(
        string providerKey,
        Func<CancellationToken, Task<IReadOnlyList<AiModelDescriptor>>> fetch,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var cached = await GetCachedAsync(providerKey, cancellationToken);
        return NeedsDiscovery(cached, time.GetUtcNow())
            ? await RefreshAsync(providerKey, fetch, cancellationToken, timeout)
            : cached;
    }

    /// <summary>
    /// Asks the provider for its models. A failure or <paramref name="timeout"/> keeps the last known
    /// models and records a sanitized error; only the caller's own cancellation is rethrown.
    /// </summary>
    public async Task<AiModelCatalog> RefreshAsync(
        string providerKey,
        Func<CancellationToken, Task<IReadOnlyList<AiModelDescriptor>>> fetch,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var cached = await GetCachedAsync(providerKey, cancellationToken);
        var now = time.GetUtcNow();
        AiModelCatalog next;

        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeout is { } bound)
            {
                limit.CancelAfter(bound);
            }

            var models = (await fetch(limit.Token))
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .GroupBy(x => x.Id, StringComparer.Ordinal)
                .Select(x => x.First())
                .ToArray();

            next = new AiModelCatalog(providerKey, models, AiModelDiscovery.Supported, now, now, null);
        }
        catch (AiModelDiscoveryUnsupportedException exception)
        {
            next = cached with
            {
                Discovery = AiModelDiscovery.Unsupported,
                LastAttemptAt = now,
                LastError = AiErrorSanitizer.Sanitize(exception.Message)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            next = cached with
            {
                LastAttemptAt = now,
                LastError = "The provider did not return its models in time."
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            next = cached with
            {
                LastAttemptAt = now,
                LastError = AiErrorSanitizer.Sanitize(exception.Message)
            };
        }

        await store.SaveAsync(next, cancellationToken);
        return next;
    }
}
