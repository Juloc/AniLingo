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

public interface ILearningLanguageToolkit
{
    string LanguageTag { get; }

    bool Supports(LearningLanguageCapability capability);

    string NormalizeText(string text);
}

public sealed class GenericLearningLanguageToolkit(
    string languageTag) : ILearningLanguageToolkit
{
    public string LanguageTag { get; } =
        LearningLanguageTag.Normalize(languageTag);

    public bool Supports(LearningLanguageCapability capability) =>
        capability == LearningLanguageCapability.TextToSpeech;

    public string NormalizeText(string text) =>
        text?.Trim()
        ?? throw new ArgumentNullException(nameof(text));
}

public sealed class JapaneseLearningLanguageToolkit : ILearningLanguageToolkit
{
    public string LanguageTag => "ja";

    public bool Supports(LearningLanguageCapability capability) =>
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
}

public sealed class LearningLanguageToolkitRegistry
{
    private static readonly ILearningLanguageToolkit Japanese =
        new JapaneseLearningLanguageToolkit();

    public ILearningLanguageToolkit Get(string languageTag)
    {
        var normalized = LearningLanguageTag.Normalize(languageTag);

        return normalized.Equals(
            Japanese.LanguageTag,
            StringComparison.OrdinalIgnoreCase)
            ? Japanese
            : new GenericLearningLanguageToolkit(normalized);
    }
}
