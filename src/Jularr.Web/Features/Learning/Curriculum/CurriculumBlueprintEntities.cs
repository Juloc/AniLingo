namespace Jularr.Web.Features.Learning.Curriculum;

/// <summary>
/// The universal, media-independent and language-neutral curriculum template. A
/// blueprint owns the whole hierarchy (levels → chapters → lessons → exercises)
/// and is the thing language pairs specialize into a shared course instance. It is
/// intentionally provider-independent: no <see cref="LearningMediaType"/>, no
/// lexicon, no per-language content — those bind later through the deferred lexeme
/// and provider pipeline.
/// </summary>
public sealed class CurriculumBlueprint
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Stable human-readable slug, unique across blueprints (e.g. <c>foundations</c>).</summary>
    public string Key { get; set; } = "";

    public string Title { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>Content revision of the blueprint; a structural change bumps it.</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Deterministic hash of the full hierarchy content. Two blueprints that describe
    /// the same structure share a fingerprint, which is what shared-instance dedup
    /// keys off once a language pair is applied.
    /// </summary>
    public string ContentFingerprint { get; set; } = "";

    public bool IsPublished { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A top-level band of a <see cref="CurriculumBlueprint"/> (e.g. a CEFR level).</summary>
public sealed class CurriculumLevel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BlueprintId { get; set; }

    /// <summary>Stable slug, unique within its blueprint.</summary>
    public string Key { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>1-based position within the blueprint.</summary>
    public int Ordinal { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A chapter of a <see cref="CurriculumLevel"/>.</summary>
public sealed class CurriculumChapter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LevelId { get; set; }

    /// <summary>Stable slug, unique within its level.</summary>
    public string Key { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>1-based position within the level.</summary>
    public int Ordinal { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A lesson of a <see cref="CurriculumChapter"/>.</summary>
public sealed class CurriculumLesson
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChapterId { get; set; }

    /// <summary>Stable slug, unique within its chapter.</summary>
    public string Key { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>1-based position within the chapter.</summary>
    public int Ordinal { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// An exercise of a <see cref="CurriculumLesson"/>. The foundation stores its kind,
/// order and an optional language-neutral prompt template only; the interactive
/// execution of an exercise is the deferred exercise-execution engine.
/// </summary>
public sealed class CurriculumExercise
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LessonId { get; set; }

    /// <summary>Stable slug, unique within its lesson.</summary>
    public string Key { get; set; } = "";

    public CurriculumExerciseKind Kind { get; set; } = CurriculumExerciseKind.Flashcard;

    /// <summary>Optional language-neutral prompt/instruction template.</summary>
    public string? Prompt { get; set; }

    /// <summary>1-based position within the lesson.</summary>
    public int Ordinal { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
