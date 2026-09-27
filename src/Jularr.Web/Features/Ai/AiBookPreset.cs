namespace Jularr.Web.Features.Ai;

/// <summary>What the "Recommended for books" preset would store for the current provider and catalog.</summary>
public sealed record AiBookPresetPlan(
    AiProfileSettings Settings,
    string Model,
    string? TranslationEffort,
    string? LightEffort)
{
    /// <summary>True when the profile already uses exactly these book settings.</summary>
    public bool IsActiveFor(AiProfileSettings current) =>
        current.TranslationMode == Settings.TranslationMode
        && string.Equals(current.Model, Settings.Model, StringComparison.Ordinal)
        && current.Overrides.Equals(Settings.Overrides);
}

/// <summary>
/// "Recommended for books": Quality mode, the selected model for every book step, and the model's own
/// reasoning levels — the standard <c>medium</c> level for translating, editing and reviewing and the
/// lighter <c>low</c> (or <c>minimal</c>) level for analysis, translation memory and story context.
/// Everything is resolved from the discovered catalog: a level is only used when the model lists it,
/// no model name is assumed, and no cheaper model is guessed because catalogs carry no cost data.
/// Other tasks' overrides stay as they are; the stored overrides still hold only differences.
/// </summary>
public static class AiBookPreset
{
    public static IReadOnlyList<string> TranslationOperations { get; } =
        [AiOperations.BookTranslation, AiOperations.BookEdit, AiOperations.BookQa];

    public static IReadOnlyList<string> LightOperations { get; } =
        [AiOperations.BookAnalysis, AiOperations.BookMemory, AiOperations.StoryContext];

    /// <summary>The preset for <paramref name="current"/>, or null while no concrete model is known.</summary>
    public static AiBookPresetPlan? Build(AiProfileSettings current, AiModelCatalog serverCatalog)
    {
        string? model;
        IReadOnlyList<string> listed = [];
        if (current.ProviderId == AiProviderIds.Server)
        {
            model = AiOptionResolver.EffectiveServerModel(serverCatalog, current.Model);
            listed = serverCatalog.Find(model)?.ReasoningEfforts.Select(x => x.Effort).ToArray() ?? [];
        }
        else
        {
            // Personal providers get no reasoning options (see AiOptionResolver.ResolvePersonal).
            model = string.IsNullOrWhiteSpace(current.Model) ? null : current.Model.Trim();
        }

        if (model is null)
        {
            return null;
        }

        var translationEffort = FirstListed(listed, "medium");
        var lightEffort = FirstListed(listed, "low", "minimal");

        // A level is only stored where the task would not reach it anyway through the profile's
        // default level or Jularr's automatic per-task level.
        KeyValuePair<string, AiOperationOverride> Entry(string operation, string? effort) =>
            KeyValuePair.Create(
                operation,
                new AiOperationOverride(
                    null,
                    effort is not null && effort != (current.ReasoningEffort ?? AiOperationDefaults.ReasoningEffort(operation))
                        ? effort
                        : null));

        var bookOperations = TranslationOperations.Concat(LightOperations).ToHashSet(StringComparer.Ordinal);
        var entries = current.Overrides.Items
            .Where(x => !bookOperations.Contains(x.Key))
            .Concat(TranslationOperations.Select(x => Entry(x, translationEffort)))
            .Concat(LightOperations.Select(x => Entry(x, lightEffort)));

        var settings = current with
        {
            Model = model,
            TranslationMode = AiTranslationMode.Quality,
            Overrides = AiOperationOverrides.From(entries, model, current.ReasoningEffort)
        };

        return new AiBookPresetPlan(settings, model, translationEffort, lightEffort);
    }

    private static string? FirstListed(IReadOnlyList<string> listed, params string[] candidates) =>
        candidates.FirstOrDefault(x => listed.Contains(x, StringComparer.Ordinal));
}
