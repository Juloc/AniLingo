using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Jularr.Web.Features.Ai;

public static class AiProviderIds
{
    public const string Server = "server";
    public const string OpenAiCompatible = "openai-compatible";

    public static bool IsSupported(string? value) =>
        string.Equals(value, Server, StringComparison.Ordinal)
        || string.Equals(value, OpenAiCompatible, StringComparison.Ordinal);
}

public enum AiTranslationMode
{
    Efficient = 0,
    Quality = 1,
    Maximum = 2
}

/// <summary>
/// Operation ids used for routing, activity, usage accounting and per-operation overrides.
/// New features add their id here so they appear in the override table and usage breakdowns.
/// </summary>
public static class AiOperations
{
    public const string SentenceExplanation = "sentence-explanation";
    public const string UiTranslation = "ui-translation";
    public const string NovelTranslation = "novel-translation";
    public const string BookTranslation = "book-translation";
    public const string BookAnalysis = "book-analysis";
    public const string BookEdit = "book-edit";
    public const string BookQa = "book-qa";
    public const string BookMemory = "book-memory";
    public const string NovelMapping = "novel-mapping";
    public const string StoryContext = "story-context";

    /// <summary>Chapter artwork images; the image model is chosen separately from text models.</summary>
    public const string ChapterArtwork = "chapter-artwork";

    /// <summary>Operations a profile can tune; UI translation runs on the owner's server connection only.</summary>
    public static IReadOnlyList<string> ProfileConfigurable { get; } =
    [
        SentenceExplanation,
        NovelTranslation,
        BookTranslation,
        BookAnalysis,
        BookEdit,
        BookQa,
        BookMemory,
        NovelMapping,
        StoryContext
    ];

    /// <summary>Every operation shown in activity and usage views.</summary>
    public static IReadOnlyList<string> All { get; } =
        [.. ProfileConfigurable, UiTranslation, ChapterArtwork];

    public static bool IsKnown(string? operation) =>
        operation is not null && All.Contains(operation, StringComparer.Ordinal);

    /// <summary>Operations that accept a model/reasoning override.</summary>
    public static bool AcceptsOverride(string? operation) =>
        operation is not null
        && (ProfileConfigurable.Contains(operation, StringComparer.Ordinal)
            || string.Equals(operation, UiTranslation, StringComparison.Ordinal));
}

/// <summary>Built-in defaults used when neither the profile nor an override chose a value.</summary>
public static class AiOperationDefaults
{
    /// <summary>Short interactive jobs use low effort; literary and analysis work uses medium.</summary>
    public static string ReasoningEffort(string operation) =>
        operation is AiOperations.SentenceExplanation
            or AiOperations.NovelTranslation
            or AiOperations.NovelMapping
            ? "low"
            : "medium";
}

/// <summary>Only the values that differ from the profile defaults; null inherits.</summary>
public sealed record AiOperationOverride(string? Model, string? ReasoningEffort)
{
    public bool IsEmpty => Model is null && ReasoningEffort is null;
}

/// <summary>Per-operation overrides with value equality, keyed by <see cref="AiOperations"/> ids.</summary>
public sealed class AiOperationOverrides : IEquatable<AiOperationOverrides>
{
    private readonly ImmutableSortedDictionary<string, AiOperationOverride> items;

    private AiOperationOverrides(ImmutableSortedDictionary<string, AiOperationOverride> items) =>
        this.items = items;

    public static AiOperationOverrides Empty { get; } =
        new(ImmutableSortedDictionary<string, AiOperationOverride>.Empty.WithComparers(StringComparer.Ordinal));

    public IReadOnlyDictionary<string, AiOperationOverride> Items => items;

    public int Count => items.Count;

    public AiOperationOverride? Get(string operation) =>
        items.TryGetValue(operation, out var value) ? value : null;

    /// <summary>Normalizes input: trims, drops empty entries and values equal to the inherited default.</summary>
    public static AiOperationOverrides From(
        IEnumerable<KeyValuePair<string, AiOperationOverride>> entries,
        string? defaultModel = null,
        string? defaultReasoningEffort = null)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<string, AiOperationOverride>(StringComparer.Ordinal);
        foreach (var (operation, value) in entries)
        {
            if (!AiOperations.AcceptsOverride(operation))
            {
                continue;
            }

            var model = Clean(value.Model);
            var effort = Clean(value.ReasoningEffort)?.ToLowerInvariant();
            if (string.Equals(model, Clean(defaultModel), StringComparison.Ordinal))
            {
                model = null;
            }

            if (string.Equals(effort, Clean(defaultReasoningEffort)?.ToLowerInvariant(), StringComparison.Ordinal))
            {
                effort = null;
            }

            var normalized = new AiOperationOverride(model, effort);
            if (!normalized.IsEmpty)
            {
                builder[operation] = normalized;
            }
        }

        return builder.Count == 0 ? Empty : new AiOperationOverrides(builder.ToImmutable());
    }

    public bool Equals(AiOperationOverrides? other) =>
        other is not null
        && items.Count == other.items.Count
        && items.All(pair => other.items.TryGetValue(pair.Key, out var value) && value == pair.Value);

    public override bool Equals(object? obj) => Equals(obj as AiOperationOverrides);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var (key, value) in items)
        {
            hash.Add(key);
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Effective per-call options after applying an operation override to the profile defaults.</summary>
public sealed record AiInvocationOptions(
    string? Model,
    string? ReasoningEffort,
    string? ServiceTier,
    int? MaxOutputTokens)
{
    public static AiInvocationOptions Default { get; } = new(null, null, null, null);
}

public sealed partial record AiProfileSettings(
    string ProviderId,
    string? BaseUrl,
    string? Model,
    string? ApiKey,
    AiTranslationMode TranslationMode)
{
    /// <summary>
    /// Image model of the OpenAI-compatible provider (for example
    /// <c>gpt-image-1</c>). Image generation stays unavailable without it.
    /// </summary>
    public string? ImageModel { get; init; }

    public const int MaxOutputTokensLimit = 128_000;

    public static AiProfileSettings Default { get; } =
        new(
            AiProviderIds.Server,
            null,
            null,
            null,
            AiTranslationMode.Efficient);

    /// <summary>Default reasoning effort; only honored when the selected model lists it.</summary>
    public string? ReasoningEffort { get; init; }

    /// <summary>Optional service/speed tier; only honored when the selected model exposes it.</summary>
    public string? ServiceTier { get; init; }

    /// <summary>Output token cap for providers that accept one (OpenAI-compatible APIs).</summary>
    public int? MaxOutputTokens { get; init; }

    public AiOperationOverrides Overrides { get; init; } = AiOperationOverrides.Empty;

    public AiInvocationOptions Resolve(string operation)
    {
        var entry = Overrides.Get(operation);
        return new AiInvocationOptions(
            entry?.Model ?? Model,
            entry?.ReasoningEffort ?? ReasoningEffort,
            ServiceTier,
            MaxOutputTokens);
    }

    public static bool IsValidModelId(string? value) =>
        value is not null && ModelIdRegex().IsMatch(value);

    public static bool IsValidOptionId(string? value) =>
        value is not null && OptionIdRegex().IsMatch(value);

    [GeneratedRegex(@"^[A-Za-z0-9][^\s\x00-\x1F\x7F]{0,119}$")]
    private static partial Regex ModelIdRegex();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9_-]{0,31}$")]
    private static partial Regex OptionIdRegex();
}
