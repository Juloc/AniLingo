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

public sealed record LearningCourseOptions(
    bool RecognitionEnabled = true,
    bool ProductionEnabled = false,
    bool ListeningEnabled = false,
    bool WritingEnabled = false,
    bool SentencePracticeEnabled = true)
{
    public bool IsModeEnabled(LearningCardMode mode) =>
        mode switch
        {
            LearningCardMode.Recognition => RecognitionEnabled,
            LearningCardMode.Production => ProductionEnabled,
            LearningCardMode.Listening => ListeningEnabled,
            LearningCardMode.Writing => WritingEnabled,
            _ => false
        };
}

public sealed record LearningCourseSnapshot(
    Guid Id,
    string ProfileId,
    string Name,
    string SourceLanguage,
    string TargetLanguage,
    bool IsEnabled,
    bool IsPrimary,
    LearningCourseOptions Options,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static LearningCourseSnapshot From(LearningCourse course) =>
        new(
            course.Id,
            course.ProfileId,
            course.Name,
            course.SourceLanguage,
            course.TargetLanguage,
            course.IsEnabled,
            course.IsPrimary,
            new LearningCourseOptions(
                course.RecognitionEnabled,
                course.ProductionEnabled,
                course.ListeningEnabled,
                course.WritingEnabled,
                course.SentencePracticeEnabled),
            course.CreatedAt,
            course.UpdatedAt);
}

public sealed record LearningUnitSnapshot(
    Guid Id,
    LearningUnitKind Kind,
    Guid? TermId,
    DateTime CreatedAt);

public sealed record LearningVariantSnapshot(
    Guid Id,
    Guid UnitId,
    string LanguageTag,
    string Text,
    string? Reading,
    string Role,
    string SourceKind)
{
    public static LearningVariantSnapshot From(LearningVariant variant) =>
        new(
            variant.Id,
            variant.UnitId,
            variant.LanguageTag,
            variant.Text,
            variant.Reading,
            variant.Role,
            variant.SourceKind);
}

public sealed record LearningVariantInput(
    string LanguageTag,
    string Text,
    string? Reading = null,
    string Role = LearningVariantRole.Primary,
    string SourceKind = LearningVariantSource.Manual);

public sealed record LearningUnitCreateResult(
    LearningUnitSnapshot Unit,
    IReadOnlyList<LearningVariantSnapshot> Variants);

public sealed record LearningCardSnapshot(
    Guid Id,
    string ProfileId,
    Guid CourseId,
    Guid UnitId,
    string PromptLanguage,
    string AnswerLanguage,
    LearningCardMode Mode,
    UserTermState State,
    int IntervalDays,
    DateTime? NextReviewAt,
    DateTime? LearningStartedAt,
    long? QueuePosition)
{
    public static LearningCardSnapshot From(LearningCard card) =>
        new(
            card.Id,
            card.ProfileId,
            card.CourseId,
            card.UnitId,
            card.PromptLanguage,
            card.AnswerLanguage,
            card.Mode,
            card.State,
            card.IntervalDays,
            card.NextReviewAt,
            card.LearningStartedAt,
            card.QueuePosition);
}

public sealed record LearningContextAnchor(
    Guid Id,
    Guid UnitId,
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

    public static bool TryNormalize(string? languageTag, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(languageTag))
        {
            return false;
        }

        try
        {
            normalized = Normalize(languageTag);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
