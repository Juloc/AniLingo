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
    public async Task<AiModelCatalog> GetCachedAsync(
        string providerKey,
        CancellationToken cancellationToken) =>
        await store.GetAsync(providerKey, cancellationToken)
        ?? AiModelCatalog.Empty(providerKey);

    /// <summary>Returns the cached catalog and only asks the provider when it was never asked.</summary>
    public async Task<AiModelCatalog> GetOrDiscoverAsync(
        string providerKey,
        Func<CancellationToken, Task<IReadOnlyList<AiModelDescriptor>>> fetch,
        CancellationToken cancellationToken)
    {
        var cached = await GetCachedAsync(providerKey, cancellationToken);
        return cached.LastAttemptAt is null
            ? await RefreshAsync(providerKey, fetch, cancellationToken)
            : cached;
    }

    public async Task<AiModelCatalog> RefreshAsync(
        string providerKey,
        Func<CancellationToken, Task<IReadOnlyList<AiModelDescriptor>>> fetch,
        CancellationToken cancellationToken)
    {
        var cached = await GetCachedAsync(providerKey, cancellationToken);
        var now = time.GetUtcNow();
        AiModelCatalog next;

        try
        {
            var models = (await fetch(cancellationToken))
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
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
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
