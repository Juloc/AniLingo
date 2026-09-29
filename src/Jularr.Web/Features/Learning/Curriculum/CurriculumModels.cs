using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jularr.Web.Features.Learning.Curriculum;

// ---------------------------------------------------------------------------
// Blueprint authoring specs (input to CurriculumBlueprintService.BuildAsync).
// Ordinals are implied by list order, so authors describe structure, not indexes.
// ---------------------------------------------------------------------------

public sealed record CurriculumBlueprintSpec(
    string Key,
    string Title,
    string? Description,
    int Version,
    IReadOnlyList<CurriculumLevelSpec> Levels);

public sealed record CurriculumLevelSpec(
    string Key,
    string Title,
    IReadOnlyList<CurriculumChapterSpec> Chapters);

public sealed record CurriculumChapterSpec(
    string Key,
    string Title,
    IReadOnlyList<CurriculumLessonSpec> Lessons);

public sealed record CurriculumLessonSpec(
    string Key,
    string Title,
    IReadOnlyList<CurriculumExerciseSpec> Exercises);

public sealed record CurriculumExerciseSpec(
    string Key,
    CurriculumExerciseKind Kind,
    string? Prompt = null);

// ---------------------------------------------------------------------------
// Read snapshots (output; never leak tracked entities to callers).
// ---------------------------------------------------------------------------

public sealed record CurriculumBlueprintSnapshot(
    Guid Id,
    string Key,
    string Title,
    string? Description,
    int Version,
    string ContentFingerprint,
    bool IsPublished,
    IReadOnlyList<CurriculumLevelSnapshot> Levels);

public sealed record CurriculumLevelSnapshot(
    Guid Id,
    string Key,
    string Title,
    int Ordinal,
    IReadOnlyList<CurriculumChapterSnapshot> Chapters);

public sealed record CurriculumChapterSnapshot(
    Guid Id,
    string Key,
    string Title,
    int Ordinal,
    IReadOnlyList<CurriculumLessonSnapshot> Lessons);

public sealed record CurriculumLessonSnapshot(
    Guid Id,
    string Key,
    string Title,
    int Ordinal,
    IReadOnlyList<CurriculumExerciseSnapshot> Exercises);

public sealed record CurriculumExerciseSnapshot(
    Guid Id,
    string Key,
    CurriculumExerciseKind Kind,
    string? Prompt,
    int Ordinal);

public sealed record SharedCourseInstanceSnapshot(
    Guid Id,
    Guid BlueprintId,
    string SourceLanguage,
    string TargetLanguage,
    string ContentFingerprint,
    string Title)
{
    public static SharedCourseInstanceSnapshot From(SharedCourseInstance instance) =>
        new(
            instance.Id,
            instance.BlueprintId,
            instance.SourceLanguage,
            instance.TargetLanguage,
            instance.ContentFingerprint,
            instance.Title);
}

public sealed record LearnerCourseSnapshot(
    Guid Id,
    string ProfileId,
    Guid SharedInstanceId,
    Guid? LearningCourseId,
    string? DisplayNameOverride,
    bool IsArchived,
    DateTime EnrolledAt)
{
    public static LearnerCourseSnapshot From(LearnerCourse course) =>
        new(
            course.Id,
            course.ProfileId,
            course.SharedInstanceId,
            course.LearningCourseId,
            course.DisplayNameOverride,
            course.IsArchived,
            course.EnrolledAt);
}

public sealed record LearnerCourseItemDeltaSnapshot(
    Guid Id,
    Guid LearnerCourseId,
    CurriculumItemType ItemType,
    Guid ItemId,
    bool IsHidden,
    int? CustomOrdinal)
{
    public static LearnerCourseItemDeltaSnapshot From(LearnerCourseItemDelta delta) =>
        new(
            delta.Id,
            delta.LearnerCourseId,
            delta.ItemType,
            delta.ItemId,
            delta.IsHidden,
            delta.CustomOrdinal);
}

public sealed record LearnerCourseProgressSnapshot(
    Guid Id,
    Guid LearnerCourseId,
    CurriculumItemType ItemType,
    Guid ItemId,
    CurriculumProgressStatus Status,
    int CompletedCount,
    double? Score,
    DateTime? StartedAt,
    DateTime? CompletedAt)
{
    public static LearnerCourseProgressSnapshot From(LearnerCourseProgress progress) =>
        new(
            progress.Id,
            progress.LearnerCourseId,
            progress.ItemType,
            progress.ItemId,
            progress.Status,
            progress.CompletedCount,
            progress.Score,
            progress.StartedAt,
            progress.CompletedAt);
}

/// <summary>Result of enrolling a learner: the shared instance (deduped) and their personal variant.</summary>
public sealed record SharedCourseEnrollment(
    LearnerCourseSnapshot LearnerCourse,
    SharedCourseInstanceSnapshot SharedInstance,
    bool SharedInstanceCreated,
    bool EnrollmentCreated);

/// <summary>
/// Deterministic content fingerprints for the curriculum. A fingerprint is a
/// lowercase hex SHA-256 over a canonical, order-sensitive description of the
/// content, so identical structure always yields the same key regardless of ids,
/// timestamps or in-memory ordering differences.
/// </summary>
public static class CurriculumFingerprint
{
    private const char FieldSeparator = '\u001f';
    private const char RecordSeparator = '\u001e';

    /// <summary>Fingerprint of a whole blueprint's structure (keys, titles, order, kinds, prompts).</summary>
    public static string ForBlueprint(CurriculumBlueprintSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var builder = new StringBuilder();
        builder.Append("blueprint")
            .Append(FieldSeparator).Append(spec.Version.ToString(CultureInfo.InvariantCulture))
            .Append(FieldSeparator).Append(spec.Key.Trim());

        var levelOrdinal = 0;
        foreach (var level in spec.Levels)
        {
            builder.Append(RecordSeparator).Append("L")
                .Append(FieldSeparator).Append((++levelOrdinal).ToString(CultureInfo.InvariantCulture))
                .Append(FieldSeparator).Append(level.Key.Trim())
                .Append(FieldSeparator).Append(level.Title.Trim());

            var chapterOrdinal = 0;
            foreach (var chapter in level.Chapters)
            {
                builder.Append(RecordSeparator).Append("C")
                    .Append(FieldSeparator).Append((++chapterOrdinal).ToString(CultureInfo.InvariantCulture))
                    .Append(FieldSeparator).Append(chapter.Key.Trim())
                    .Append(FieldSeparator).Append(chapter.Title.Trim());

                var lessonOrdinal = 0;
                foreach (var lesson in chapter.Lessons)
                {
                    builder.Append(RecordSeparator).Append("N")
                        .Append(FieldSeparator).Append((++lessonOrdinal).ToString(CultureInfo.InvariantCulture))
                        .Append(FieldSeparator).Append(lesson.Key.Trim())
                        .Append(FieldSeparator).Append(lesson.Title.Trim());

                    var exerciseOrdinal = 0;
                    foreach (var exercise in lesson.Exercises)
                    {
                        builder.Append(RecordSeparator).Append("X")
                            .Append(FieldSeparator).Append((++exerciseOrdinal).ToString(CultureInfo.InvariantCulture))
                            .Append(FieldSeparator).Append(exercise.Key.Trim())
                            .Append(FieldSeparator).Append(exercise.Kind.ToString())
                            .Append(FieldSeparator).Append(exercise.Prompt?.Trim() ?? "");
                    }
                }
            }
        }

        return Hash(builder.ToString());
    }

    /// <summary>
    /// Fingerprint of a specialized shared instance: the blueprint fingerprint and
    /// version combined with the normalized language pair. This is the dedup key for
    /// <see cref="SharedCourseInstance"/>.
    /// </summary>
    public static string ForSharedInstance(
        string blueprintFingerprint,
        int blueprintVersion,
        string sourceLanguage,
        string targetLanguage)
    {
        var canonical = string.Join(
            FieldSeparator,
            "shared",
            blueprintFingerprint,
            blueprintVersion.ToString(CultureInfo.InvariantCulture),
            sourceLanguage,
            targetLanguage);
        return Hash(canonical);
    }

    private static string Hash(string canonical)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(bytes);
    }
}
