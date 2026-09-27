namespace Jularr.Web.Features.Ai;

/// <summary>
/// Protocol surfaces a provider may expose. Capabilities are detected at runtime and never assumed:
/// the UI only offers a setting or view when the matching capability is <see cref="AiCapabilityState.Supported"/>.
/// </summary>
public enum AiCapability
{
    ModelCatalog,
    AccountStatus,
    RateLimits,
    RateLimitUpdates,
    TurnLifecycle,
    TokenUsage,
    Interrupt,
    ReasoningEffort,
    ServiceTier,
    MaxOutputTokens
}

public enum AiCapabilityState
{
    Unknown,
    Supported,
    Unsupported
}

public sealed record AiProviderCapabilities(
    string Transport,
    IReadOnlyDictionary<AiCapability, AiCapabilityState> States,
    DateTimeOffset? DetectedAt)
{
    public static AiProviderCapabilities None(string transport) =>
        new(transport, new Dictionary<AiCapability, AiCapabilityState>(), null);

    public AiCapabilityState this[AiCapability capability] =>
        States.TryGetValue(capability, out var state)
            ? state
            : AiCapabilityState.Unknown;

    public bool Supports(AiCapability capability) =>
        this[capability] == AiCapabilityState.Supported;

    public AiProviderCapabilities With(AiCapability capability, AiCapabilityState state) =>
        this with
        {
            States = new Dictionary<AiCapability, AiCapabilityState>(States) { [capability] = state }
        };

    /// <summary>Model-level options are only known once a catalog lists them.</summary>
    public AiProviderCapabilities WithCatalog(AiModelCatalog catalog) =>
        !catalog.HasModels
            ? this
            : With(
                    AiCapability.ReasoningEffort,
                    catalog.Models.Any(x => x.ReasoningEfforts.Count > 0) ? AiCapabilityState.Supported : AiCapabilityState.Unsupported)
                .With(
                    AiCapability.ServiceTier,
                    catalog.Models.Any(x => x.ServiceTiers.Count > 0) ? AiCapabilityState.Supported : AiCapabilityState.Unsupported);
}

public static class AiTransports
{
    public const string CodexAppServer = "codex-app-server";
    public const string CodexExec = "codex-exec";
    public const string OpenAiChatCompletions = "openai-chat-completions";
}

public sealed record AiReasoningOption(string Effort, string? Description);

public sealed record AiServiceTierOption(string Id, string Name, string? Description);

/// <summary>A model exactly as the provider described it; nothing here is hardcoded by Jularr.</summary>
public sealed record AiModelDescriptor(
    string Id,
    string DisplayName,
    string? Description,
    IReadOnlyList<AiReasoningOption> ReasoningEfforts,
    string? DefaultReasoningEffort,
    IReadOnlyList<AiServiceTierOption> ServiceTiers,
    string? DefaultServiceTier,
    bool IsDefault,
    long? ContextWindow)
{
    public static AiModelDescriptor Basic(string id, long? contextWindow = null) =>
        new(id, id, null, [], null, [], null, false, contextWindow);
}
