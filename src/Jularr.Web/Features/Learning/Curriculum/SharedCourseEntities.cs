namespace Jularr.Web.Features.Learning.Curriculum;

/// <summary>
/// A <see cref="CurriculumBlueprint"/> specialized to one source → target language
/// pair, shared across every learner that studies it. Deduplicated on
/// <see cref="ContentFingerprint"/> (blueprint fingerprint + version + language
/// pair), so two learners enrolling in the same specialization reference one row
/// rather than each forking the content. It holds no per-profile state — a learner's
/// personal copy lives in <see cref="LearnerCourse"/> and its delta/progress rows.
/// </summary>
public sealed class SharedCourseInstance
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BlueprintId { get; set; }

    /// <summary>BCP-47 tag of the language being learned.</summary>
    public string SourceLanguage { get; set; } = "";

    /// <summary>BCP-47 tag the learner already knows / answers in.</summary>
    public string TargetLanguage { get; set; } = "";

    /// <summary>Deterministic dedup key; unique across shared instances.</summary>
    public string ContentFingerprint { get; set; } = "";

    public string Title { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A learner's personal enrollment in a <see cref="SharedCourseInstance"/>: the root
/// of their personal variant. It never copies the shared content; the learner's
/// differences are stored sparsely as <see cref="LearnerCourseItemDelta"/> rows and
/// their advancement as <see cref="LearnerCourseProgress"/> rows. An optional link to
/// the v2 per-profile <see cref="Courses.LearningCourse"/> is the bridge to reuse the
/// existing FSRS card/scheduling machinery without forking it.
/// </summary>
public sealed class LearnerCourse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProfileId { get; set; } = LearningProfile.DefaultId;
    public Guid SharedInstanceId { get; set; }

    /// <summary>
    /// Optional v2 <see cref="Courses.LearningCourse"/> that holds this learner's FSRS
    /// cards for the same language pair. Course progress is tracked here; card review
    /// state stays in the linked course. Null until the learner has a card container.
    /// </summary>
    public Guid? LearningCourseId { get; set; }

    /// <summary>Part of the personal delta: a learner-chosen display name for the course.</summary>
    public string? DisplayNameOverride { get; set; }

    /// <summary>Part of the personal delta: the learner archived the course from their hub.</summary>
    public bool IsArchived { get; set; }

    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One learner's personal modification of a single shared curriculum item — the
/// sparse delta that makes the learner's copy a variant rather than a fork. Absence
/// of a row means "inherit the shared blueprint exactly". Deltas describe structure
/// (hidden, re-ordered), never progress.
/// </summary>
public sealed class LearnerCourseItemDelta
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LearnerCourseId { get; set; }

    public CurriculumItemType ItemType { get; set; }

    /// <summary>Id of the addressed blueprint node (level/chapter/lesson/exercise).</summary>
    public Guid ItemId { get; set; }

    /// <summary>Learner hid this item from their personal view of the shared course.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Learner-chosen override of the item's position; null keeps the shared order.</summary>
    public int? CustomOrdinal { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A learner's course progress for a single curriculum item. This is the canonical,
/// standalone course-progress store of Learning v3 and is intentionally decoupled
/// from FSRS: completing an item here never touches <see cref="Courses.LearningCard"/>
/// / <see cref="Courses.LearningCardReview"/>, and reviewing a card never advances
/// this. One row per (learner course, item).
/// </summary>
public sealed class LearnerCourseProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LearnerCourseId { get; set; }

    public CurriculumItemType ItemType { get; set; }

    /// <summary>Id of the addressed blueprint node (level/chapter/lesson/exercise).</summary>
    public Guid ItemId { get; set; }

    public CurriculumProgressStatus Status { get; set; } = CurriculumProgressStatus.NotStarted;

    /// <summary>How many times the item was completed (repeated practice keeps counting).</summary>
    public int CompletedCount { get; set; }

    /// <summary>Optional last score in [0,1] for scored exercises.</summary>
    public double? Score { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
