using System.Collections.Concurrent;

namespace Jularr.Web.Features.Providers;

/// <summary>
/// The registry of every external provider integration known to the process,
/// keyed by <see cref="ExternalProviderDescriptor.Key"/>. It is the read side
/// admin diagnostics use to list providers together with their advertised
/// capabilities and (joined with <see cref="ProviderHealthTracker"/>) their
/// live health. Registered as a singleton and populated at start-up.
/// </summary>
public sealed class ProviderCatalog
{
    private readonly ConcurrentDictionary<string, ExternalProviderDescriptor> descriptors =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds or replaces the descriptor for its key.</summary>
    public void Register(ExternalProviderDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.Key))
        {
            throw new ArgumentException("Provider key is required.", nameof(descriptor));
        }

        descriptors[descriptor.Key] = descriptor;
    }

    public ExternalProviderDescriptor? Get(string key) =>
        descriptors.TryGetValue(key, out var descriptor) ? descriptor : null;

    /// <summary>Every registered provider, ordered by display name for stable admin output.</summary>
    public IReadOnlyList<ExternalProviderDescriptor> All() =>
        descriptors.Values
            .OrderBy(descriptor => descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
