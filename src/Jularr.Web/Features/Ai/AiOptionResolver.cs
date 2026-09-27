namespace Jularr.Web.Features.Ai;

/// <summary>
/// Turns requested options into options the selected provider/model actually supports. Values the
/// model does not list are dropped so the provider's own default applies; nothing is guessed.
/// </summary>
public static class AiOptionResolver
{
    public static AiInvocationOptions ResolveServer(
        AiInvocationOptions requested,
        AiModelCatalog catalog,
        string operation)
    {
        var model = requested.Model;
        if (model is not null && catalog.HasModels && catalog.Find(model) is null)
        {
            model = null;
        }

        var descriptor = catalog.Find(model) ?? (model is null ? catalog.DefaultModel : null);
        var fallbackEffort = AiOperationDefaults.ReasoningEffort(operation);
        string? effort;

        if (descriptor is null)
        {
            // No catalog yet: keep the historical per-operation defaults.
            effort = requested.ReasoningEffort ?? fallbackEffort;
        }
        else
        {
            var listed = descriptor.ReasoningEfforts.Select(x => x.Effort).ToArray();
            effort = new[] { requested.ReasoningEffort, fallbackEffort }
                .FirstOrDefault(x => x is not null && listed.Contains(x, StringComparer.Ordinal));
        }

        var tier = descriptor is not null
            && requested.ServiceTier is not null
            && descriptor.ServiceTiers.Any(x => string.Equals(x.Id, requested.ServiceTier, StringComparison.Ordinal))
                ? requested.ServiceTier
                : null;

        return new AiInvocationOptions(model, effort, tier, null);
    }

    public static AiInvocationOptions ResolvePersonal(AiInvocationOptions requested) =>
        new(requested.Model, null, null, requested.MaxOutputTokens);

    /// <summary>Reasoning options to offer for a model, or none when the model lists none.</summary>
    public static IReadOnlyList<AiReasoningOption> ReasoningOptions(AiModelCatalog catalog, string? modelId) =>
        (catalog.Find(modelId) ?? (modelId is null ? catalog.DefaultModel : null))?.ReasoningEfforts ?? [];

    /// <summary>Union of reasoning options over the catalog, used by per-operation overrides.</summary>
    public static IReadOnlyList<AiReasoningOption> AllReasoningOptions(AiModelCatalog catalog) =>
        catalog.Models
            .SelectMany(x => x.ReasoningEfforts)
            .GroupBy(x => x.Effort, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToArray();

    public static IReadOnlyList<AiServiceTierOption> ServiceTierOptions(AiModelCatalog catalog, string? modelId) =>
        (catalog.Find(modelId) ?? (modelId is null ? catalog.DefaultModel : null))?.ServiceTiers ?? [];
}
