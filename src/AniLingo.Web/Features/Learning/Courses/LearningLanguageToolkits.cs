using AniLingo.Web.Features.Learning.Toolkits;

namespace AniLingo.Web.Features.Learning.Courses;

public enum LearningLanguageCapability
{
    Tokenization = 1,
    Dictionary = 2,
    Readings = 3,
    Transliteration = 4,
    ScriptTrainer = 5,
    TextToSpeech = 6
}

/// <summary>
/// Language-specific behaviour for a BCP-47 language tag. This is the single
/// place that decides which implementation backs a language: no other feature
/// re-implements "is this Japanese" or similar checks. <see cref="TermExtractor"/>
/// and <see cref="Dictionary"/> are null when the toolkit has none; callers
/// must treat that as "capability not available" rather than fall back to a
/// language check of their own.
/// </summary>
public interface ILearningLanguageToolkit
{
    string LanguageTag { get; }

    bool Supports(LearningLanguageCapability capability);

    string NormalizeText(string text);

    /// <summary>Tokenization/term extraction, when <see cref="LearningLanguageCapability.Tokenization"/> is supported.</summary>
    ITermExtractor? TermExtractor { get; }

    /// <summary>Bundled dictionary lookup, when <see cref="LearningLanguageCapability.Dictionary"/> is supported.</summary>
    IDictionaryLookup? Dictionary { get; }
}

/// <summary>
/// Every language without a specialized toolkit: generic Unicode tokenization,
/// on-device TTS, no bundled dictionary, no readings/transliteration/script
/// trainer. A generic course is never blocked merely because no specialized
/// toolkit exists for its language.
/// </summary>
public sealed class GenericLearningLanguageToolkit : ILearningLanguageToolkit
{
    public GenericLearningLanguageToolkit(string languageTag)
    {
        LanguageTag = LearningLanguageTag.Normalize(languageTag);
        TermExtractor = new GenericTermExtractor(LanguageTag);
    }

    public string LanguageTag { get; }

    public bool Supports(LearningLanguageCapability capability) => SupportsCapability(capability);

    public static bool SupportsCapability(LearningLanguageCapability capability) =>
        capability is LearningLanguageCapability.Tokenization or LearningLanguageCapability.TextToSpeech;

    public string NormalizeText(string text) =>
        text?.Trim()
        ?? throw new ArgumentNullException(nameof(text));

    public ITermExtractor TermExtractor { get; }

    public IDictionaryLookup? Dictionary => null;
}

/// <summary>
/// Japanese: MeCab-based tokenization/term extraction, the bundled JMdict
/// lookup, kana readings, romaji transliteration, the Kana script trainer and
/// on-device TTS. <paramref name="termExtractor"/>/<paramref name="dictionary"/>
/// are optional so capability-only queries (<see cref="Supports"/>) work
/// without wiring up the real MeCab/JMdict instances; production and the
/// content pipeline always provide both through <see cref="LearningLanguageToolkitRegistry"/>.
/// </summary>
public sealed class JapaneseLearningLanguageToolkit(
    ITermExtractor? termExtractor = null,
    IDictionaryLookup? dictionary = null) : ILearningLanguageToolkit
{
    public const string Tag = "ja";

    public string LanguageTag => Tag;

    public bool Supports(LearningLanguageCapability capability) => SupportsCapability(capability);

    public static bool SupportsCapability(LearningLanguageCapability capability) =>
        capability is
            LearningLanguageCapability.Tokenization
            or LearningLanguageCapability.Dictionary
            or LearningLanguageCapability.Readings
            or LearningLanguageCapability.Transliteration
            or LearningLanguageCapability.ScriptTrainer
            or LearningLanguageCapability.TextToSpeech;

    public string NormalizeText(string text) =>
        text?.Trim()
        ?? throw new ArgumentNullException(nameof(text));

    public ITermExtractor? TermExtractor => termExtractor;

    public IDictionaryLookup? Dictionary => dictionary;
}

/// <summary>
/// Resolves the toolkit for a BCP-47 language tag: the specialized Japanese
/// toolkit for <c>ja</c>, a generic toolkit for every other valid tag. This is
/// the only registry for language-specific behaviour; nothing else keeps a
/// second list of supported/specialized languages. <see cref="Supports"/> is a
/// cheap, dependency-free capability query for call sites that only need to
/// know what a language can do (no MeCab/JMdict instances required); <see cref="Get"/>
/// returns the toolkit consumers use to actually tokenize or look words up.
/// </summary>
public sealed class LearningLanguageToolkitRegistry(
    ITermExtractor? japaneseTermExtractor = null,
    IDictionaryLookup? japaneseDictionary = null)
{
    private readonly ILearningLanguageToolkit japanese =
        new JapaneseLearningLanguageToolkit(japaneseTermExtractor, japaneseDictionary);

    public ILearningLanguageToolkit Get(string languageTag)
    {
        var normalized = LearningLanguageTag.Normalize(languageTag);

        return normalized.Equals(JapaneseLearningLanguageToolkit.Tag, StringComparison.OrdinalIgnoreCase)
            ? japanese
            : new GenericLearningLanguageToolkit(normalized);
    }

    public static bool Supports(string languageTag, LearningLanguageCapability capability)
    {
        var normalized = LearningLanguageTag.Normalize(languageTag);

        return normalized.Equals(JapaneseLearningLanguageToolkit.Tag, StringComparison.OrdinalIgnoreCase)
            ? JapaneseLearningLanguageToolkit.SupportsCapability(capability)
            : GenericLearningLanguageToolkit.SupportsCapability(capability);
    }
}
