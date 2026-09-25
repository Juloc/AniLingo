using System.Globalization;

namespace AniLingo.Web.Features.Learning.Courses;

public enum LearningUnitKind
{
    Word = 1,
    Sentence = 2,
    Script = 3
}

public enum LearningCardMode
{
    Recognition = 1,
    Production = 2,
    Listening = 3,
    Writing = 4
}

public enum LearningCardState
{
    Saved = 1,
    Learning = 2,
    Known = 3,
    Ignored = 4,
    Suspended = 5
}

public sealed record LearningCourseOptions(
    bool RecognitionEnabled = true,
    bool ProductionEnabled = false,
    bool ListeningEnabled = false,
    bool WritingEnabled = false,
    bool SentencePracticeEnabled = true);

public sealed record LearningCourseSnapshot(
    string Id,
    string ProfileId,
    string Name,
    string SourceLanguage,
    string TargetLanguage,
    bool IsEnabled,
    LearningCourseOptions Options,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record LearningUnitSnapshot(
    string Id,
    LearningUnitKind Kind,
    string? LegacyTermId,
    DateTime CreatedAt);

public sealed record LearningVariantSnapshot(
    string Id,
    string UnitId,
    string LanguageTag,
    string Text,
    string? Reading,
    string Role,
    string SourceKind);

public sealed record LearningVariantInput(
    string LanguageTag,
    string Text,
    string? Reading = null,
    string Role = "Primary",
    string SourceKind = "Manual");

public sealed record LearningUnitCreateResult(
    LearningUnitSnapshot Unit,
    IReadOnlyList<LearningVariantSnapshot> Variants);

public sealed record LearningCardSnapshot(
    string Id,
    string ProfileId,
    string CourseId,
    string UnitId,
    string PromptLanguage,
    string AnswerLanguage,
    LearningCardMode Mode,
    LearningCardState State,
    int IntervalDays,
    DateTime? NextReviewAt,
    string? LegacyUserTermId);

public sealed record LearningContextAnchor(
    string Id,
    string UnitId,
    string SourceType,
    string SourceKey,
    string? PositionKey,
    string LanguageTag,
    string Text,
    DateTime CreatedAt);

public sealed record LearningContextInput(
    string SourceType,
    string SourceKey,
    string? PositionKey,
    string LanguageTag,
    string Text);

public static class LearningLanguageTag
{
    public static string Normalize(string languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            throw new ArgumentException(
                "A BCP-47 language tag is required.",
                nameof(languageTag));
        }

        var candidate = languageTag.Trim().Replace('_', '-');

        try
        {
            var culture = CultureInfo.GetCultureInfo(candidate);
            if (string.IsNullOrWhiteSpace(culture.Name))
            {
                throw new CultureNotFoundException();
            }

            return culture.Name;
        }
        catch (CultureNotFoundException exception)
        {
            throw new ArgumentException(
                $"'{languageTag}' is not a supported BCP-47 language tag on this server.",
                nameof(languageTag),
                exception);
        }
    }
}
