using System.Globalization;
using Jularr.Web.Features.Localization;

namespace Jularr.Web.Features.Ai;

public sealed record AiActivityPanel(
    UiTextBundle Ui,
    IReadOnlyList<AiActivityDto> Items,
    bool AllProfiles,
    IReadOnlyDictionary<string, string>? ProfileNames = null);

public enum AiUsageSections
{
    /// <summary>Period tabs, totals and every breakdown table.</summary>
    All,

    /// <summary>Period tabs and totals only.</summary>
    Totals,

    /// <summary>Breakdown tables by task, model (and profile) only.</summary>
    Breakdown
}

public sealed record AiUsagePanel(
    UiTextBundle Ui,
    AiUsageReport Report,
    bool ShowProfiles,
    string PagePath,
    IReadOnlyDictionary<string, string>? ProfileNames = null,
    AiUsageSections Sections = AiUsageSections.All);

public sealed record AiQuotaPanel(
    UiTextBundle Ui,
    AiQuotaSnapshot? Quota,
    bool Detailed);

/// <summary>Formatting shared by the Settings → AI and Admin → AI views.</summary>
public static class AiViewFormat
{
    public static string Tokens(long value) =>
        value.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Estimated values carry a leading "~" so they are never mistaken for exact counts.</summary>
    public static string Tokens(long exact, long estimated) =>
        estimated > 0
            ? "~" + Tokens(exact + estimated)
            : Tokens(exact);

    public static string Percent(double value) =>
        value.ToString("0.#", CultureInfo.CurrentCulture) + " %";

    public static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public static string Duration(TimeSpan value) =>
        value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes}:{value.Seconds:00}";

    public static string Window(UiTextBundle ui, int? minutes) =>
        minutes switch
        {
            null => ui["ai.quota.windowUnknown"],
            >= 1440 when minutes % 1440 == 0 => ui.Format("ai.quota.windowDays", ("days", minutes / 1440)),
            >= 60 when minutes % 60 == 0 => ui.Format("ai.quota.windowHours", ("hours", minutes / 60)),
            _ => ui.Format("ai.quota.windowMinutes", ("minutes", minutes))
        };

    /// <summary>Catalog freshness: updated, stale (with the last good time), unsupported or failed.</summary>
    public static string CatalogStatus(UiTextBundle ui, AiModelCatalog catalog, DateTimeOffset now) =>
        catalog.Discovery == AiModelDiscovery.Unsupported
            ? ui["ai.models.unsupported"]
            : catalog.FetchedAt is { } fetched
                ? catalog.IsStale(now)
                    ? ui.Format("ai.models.stale", ("time", Timestamp(fetched)))
                    : ui.Format("ai.models.updated", ("time", Timestamp(fetched)))
                : catalog.LastError is not null
                    ? ui["ai.models.refreshFailed"]
                    : ui["ai.models.notLoaded"];

    /// <summary>Localized name of a standard reasoning level; levels Jularr does not know show as reported.</summary>
    public static string Effort(UiTextBundle ui, string effort) =>
        UiTranslationResources.TryGet("ai.effort." + effort, out _)
            ? ui["ai.effort." + effort]
            : effort;

    /// <summary>Model picker label; the provider's own default carries a marker.</summary>
    public static string ModelOption(UiTextBundle ui, AiModelDescriptor model) =>
        model.IsDefault
            ? ui.Format("ai.models.defaultMarker", ("model", model.DisplayName))
            : model.DisplayName;

    public static string Operation(UiTextBundle ui, string operation) =>
        AiOperations.IsKnown(operation)
            ? ui["ai.operation." + operation]
            : operation;

    public static string State(UiTextBundle ui, string state) =>
        ui["ai.state." + state];

    public static string Capability(UiTextBundle ui, AiCapability capability) =>
        ui["ai.capability." + char.ToLowerInvariant(capability.ToString()[0]) + capability.ToString()[1..]];

    public static string CapabilityState(UiTextBundle ui, AiCapabilityState state) =>
        ui["ai.capabilityState." + state.ToString().ToLowerInvariant()];

    public static string Transport(UiTextBundle ui, string? transport) =>
        transport switch
        {
            AiTransports.CodexAppServer => ui["ai.transport.appServer"],
            AiTransports.CodexExec => ui["ai.transport.exec"],
            AiTransports.OpenAiChatCompletions => ui["ai.transport.chatCompletions"],
            _ => "—"
        };

    public static string Provider(UiTextBundle ui, string providerId) =>
        providerId switch
        {
            AiProviderIds.OpenAiCompatible => ui["settings.ai.openaiCompatible"],
            "cache" => ui["settings.ai.cacheHits"],
            _ => ui["settings.ai.server"]
        };
}
