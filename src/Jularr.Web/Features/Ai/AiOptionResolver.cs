namespace Jularr.Web.Features.Ai;

/// <summary>
/// Turns requested options into options the selected provider/model actually supports. Values the
/// model does not list are dropped so the provider's own default applies; nothing is guessed.
/// </summary>
public static class AiOptionResolver
{
    public const string MissingServerModelMessage =
        "Select a server AI model or refresh the model list before running AI tasks.";

    /// <summary>
    /// The concrete server model a request runs with: the requested model when the catalog lists it,
    /// otherwise the catalog's default. Without a catalog only an explicitly entered model is used;
    /// null means no model is known and AI work must not start (the provider's implicit default is
    /// never delegated to).
    /// </summary>
    public static string? EffectiveServerModel(AiModelCatalog catalog, string? requested)
    {
        var model = string.IsNullOrWhiteSpace(requested) ? null : requested.Trim();
        if (!catalog.HasModels)
        {
            return model;
        }

        return model is not null && catalog.Find(model) is not null
            ? model
            : catalog.DefaultModel?.Id;
    }

    public static AiInvocationOptions ResolveServer(
        AiInvocationOptions requested,
        AiModelCatalog catalog,
        string operation)
    {
        var model = EffectiveServerModel(catalog, requested.Model)
            ?? throw new InvalidOperationException(MissingServerModelMessage);

        var descriptor = catalog.Find(model);
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

        return new AiInvocationOptions(model, effort, tier, null)
        {
            ContextBudgetTokens = requested.ContextBudgetTokens,
            MaxRetries = requested.MaxRetries,
            TimeoutSeconds = requested.TimeoutSeconds,
            Verbosity = ValidVerbosity(requested.Verbosity)
        };
    }

    public const string MissingPersonalModelMessage =
        "Choose a model for your AI provider in Settings → AI before running AI tasks.";

    /// <summary>Response verbosity levels Codex accepts; nothing else is ever sent.</summary>
    public static IReadOnlyList<string> VerbosityLevels { get; } = ["low", "medium", "high"];

    public static string? ValidVerbosity(string? value) =>
        value is not null && VerbosityLevels.Contains(value, StringComparer.Ordinal) ? value : null;

    /// <summary>Personal providers get no reasoning options; a request without a model never starts.</summary>
    public static AiInvocationOptions ResolvePersonal(AiInvocationOptions requested) =>
        string.IsNullOrWhiteSpace(requested.Model)
            ? throw new InvalidOperationException(MissingPersonalModelMessage)
            : new AiInvocationOptions(requested.Model, null, null, requested.MaxOutputTokens)
            {
                ContextBudgetTokens = requested.ContextBudgetTokens,
                MaxRetries = requested.MaxRetries,
                TimeoutSeconds = requested.TimeoutSeconds
            };

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

    /// <summary>
    /// True when the catalog reports a context window for at least two models, so a lighter model can
    /// ever be identified. Catalogs carry no cost data, so context window — the one numeric, provider-
    /// reported size signal Jularr has — is the only safe basis for "lighter"; nothing is guessed.
    /// </summary>
    public static bool CanSuggestLighterModel(AiModelCatalog catalog) =>
        catalog.Models.Count(x => x.ContextWindow is > 0) >= 2;

    /// <summary>
    /// A model with a strictly smaller known context window than <paramref name="currentModelId"/>, or
    /// null when the current model or the catalog does not report enough context-window metadata to
    /// tell safely. Never a guess: a model without a reported context window is never suggested and
    /// never used to judge another model.
    /// </summary>
    public static AiModelDescriptor? FindLighterModel(AiModelCatalog catalog, string? currentModelId)
    {
        var currentWindow = catalog.Find(currentModelId)?.ContextWindow;
        if (currentWindow is not > 0)
        {
            return null;
        }

        return catalog.Models
            .Where(x =>
                x.ContextWindow is > 0
                && x.ContextWindow < currentWindow
                && !string.Equals(x.Id, currentModelId, StringComparison.Ordinal))
            .OrderBy(x => x.ContextWindow)
            .FirstOrDefault();
    }
}
