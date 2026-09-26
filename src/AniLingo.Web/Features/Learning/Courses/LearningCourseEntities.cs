namespace AniLingo.Web.Features.Learning.Courses;

/// <summary>
/// Language-neutral concept or sense. Units created from the subtitle/dictionary
/// catalog keep a link to their catalog <see cref="Vocabulary.Term"/> through
/// <see cref="TermId"/>; the Term itself is lexical data and never learning state.
/// </summary>
public sealed class LearningUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public LearningUnitKind Kind { get; set; } = LearningUnitKind.Word;
    public Guid? TermId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One language-specific representation of a unit.</summary>
public sealed class LearningVariant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UnitId { get; set; }
    public string LanguageTag { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Reading { get; set; }
    public string Role { get; set; } = LearningVariantRole.Primary;
    public string SourceKind { get; set; } = LearningVariantSource.Manual;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Profile-scoped language pair. At most one course per profile and source
/// language is primary; catalog words from content in that language are saved
/// into the primary course.
/// </summary>
public sealed class LearningCourse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProfileId { get; set; } = LearningProfile.DefaultId;
    public string Name { get; set; } = "";
    public string SourceLanguage { get; set; } = "";
    public string TargetLanguage { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public bool IsPrimary { get; set; }
    public bool RecognitionEnabled { get; set; } = true;
    public bool ProductionEnabled { get; set; }
    public bool ListeningEnabled { get; set; }
    public bool WritingEnabled { get; set; }
    public bool SentencePracticeEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Canonical per-profile learning state: one directional card per course, unit
/// and practice mode, each with its own FSRS history.
/// </summary>
public sealed class LearningCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProfileId { get; set; } = LearningProfile.DefaultId;
    public Guid CourseId { get; set; }
    public Guid UnitId { get; set; }
    public string PromptLanguage { get; set; } = "";
    public string AnswerLanguage { get; set; } = "";
    public LearningCardMode Mode { get; set; } = LearningCardMode.Recognition;
    public UserTermState State { get; set; } = UserTermState.Saved;
    public int IntervalDays { get; set; }
    public DateTime? NextReviewAt { get; set; }
    public DateTime? LearningStartedAt { get; set; }
    public long? QueuePosition { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Durable FSRS input for one directional card.</summary>
public sealed class LearningCardReview
{
    public long Id { get; set; }
    public string ProfileId { get; set; } = LearningProfile.DefaultId;
    public Guid CardId { get; set; }
    public ReviewRating Rating { get; set; }
    public Guid? ClientEventId { get; set; }
    public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;
    public DateTime NextReviewAt { get; set; }
}

/// <summary>Source-agnostic anchor of a unit in watched or read content.</summary>
public sealed class LearningContext
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UnitId { get; set; }
    public string SourceType { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string? PositionKey { get; set; }
    public string LanguageTag { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class LearningVariantRole
{
    public const string Primary = "Primary";
    public const string Meaning = "Meaning";
}

public static class LearningVariantSource
{
    public const string Manual = "Manual";
    public const string Term = "Term";
    public const string Dictionary = "Dictionary";
    public const string ScriptCatalog = "ScriptCatalog";
}
