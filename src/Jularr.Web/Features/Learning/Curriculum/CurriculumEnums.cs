namespace Jularr.Web.Features.Learning.Curriculum;

/// <summary>
/// The node kinds of the universal curriculum hierarchy
/// (Curriculum → Level → Chapter → Lesson → Exercise). Used to address a single
/// hierarchy node from a learner's personal delta or progress row without a
/// separate table per level.
/// </summary>
public enum CurriculumItemType
{
    Level = 1,
    Chapter = 2,
    Lesson = 3,
    Exercise = 4
}

/// <summary>
/// The kind of a curriculum exercise. The foundation only records the kind so the
/// blueprint hierarchy is complete and stable; turning a kind into an interactive
/// task (and, where relevant, into FSRS cards) is the deferred exercise-execution
/// engine, not this PR.
/// </summary>
public enum CurriculumExerciseKind
{
    Flashcard = 1,
    MultipleChoice = 2,
    Cloze = 3,
    Matching = 4,
    Listening = 5,
    Speaking = 6,
    Writing = 7,
    Translation = 8
}

/// <summary>
/// A learner's progress through a single curriculum item. This is the v3 course
/// progress axis and is deliberately independent of the FSRS review state that
/// lives on <see cref="Courses.LearningCard"/>: a lesson can be marked complete
/// while its cards are still due, and a card can be reviewed without advancing the
/// course.
/// </summary>
public enum CurriculumProgressStatus
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2
}
