using Jularr.Web.Data;
using Jularr.Web.Features.Learning.Courses;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Learning.Curriculum;

/// <summary>
/// Owns shared (deduped) course instances and each learner's personal variant on top
/// of them. Enrolling a learner in a blueprint for a language pair resolves the one
/// <see cref="SharedCourseInstance"/> for that specialized content (creating it only
/// the first time) and gives the learner their own <see cref="LearnerCourse"/>. The
/// learner's differences are stored sparsely as deltas, and their advancement as
/// <see cref="LearnerCourseProgress"/> — the v3 course-progress store, which is
/// deliberately independent of the FSRS review state carried by
/// <see cref="LearningCard"/>.
/// </summary>
public sealed class SharedCourseService(AppDbContext db)
{
    /// <summary>
    /// Enrolls a learner in a blueprint specialized to a language pair. The shared
    /// instance is deduplicated on its content fingerprint, so concurrent learners of
    /// the same specialization share one instance while each getting their own variant.
    /// </summary>
    public async Task<SharedCourseEnrollment> EnrollAsync(
        string profileId,
        Guid blueprintId,
        string sourceLanguage,
        string targetLanguage,
        Guid? learningCourseId,
        CancellationToken cancellationToken)
    {
        var profile = RequireProfile(profileId);
        var source = LearningLanguageTag.Normalize(sourceLanguage);
        var target = LearningLanguageTag.Normalize(targetLanguage);
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Source and target languages must be different.");
        }

        var blueprint = await db.CurriculumBlueprints
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == blueprintId, cancellationToken)
            ?? throw new KeyNotFoundException("Curriculum blueprint was not found.");

        var fingerprint = CurriculumFingerprint.ForSharedInstance(
            blueprint.ContentFingerprint,
            blueprint.Version,
            source,
            target);

        var (instance, instanceCreated) = await ResolveSharedInstanceAsync(
            blueprint,
            source,
            target,
            fingerprint,
            cancellationToken);

        var (learnerCourse, enrollmentCreated) = await ResolveLearnerCourseAsync(
            profile,
            instance.Id,
            learningCourseId,
            cancellationToken);

        return new SharedCourseEnrollment(
            LearnerCourseSnapshot.From(learnerCourse),
            SharedCourseInstanceSnapshot.From(instance),
            instanceCreated,
            enrollmentCreated);
    }

    /// <summary>Links (or clears) the v2 FSRS card container for a learner course.</summary>
    public async Task<LearnerCourseSnapshot> LinkLearningCourseAsync(
        Guid learnerCourseId,
        Guid? learningCourseId,
        CancellationToken cancellationToken)
    {
        var learnerCourse = await db.LearnerCourses
            .SingleOrDefaultAsync(x => x.Id == learnerCourseId, cancellationToken)
            ?? throw new KeyNotFoundException("Learner course was not found.");

        learnerCourse.LearningCourseId = learningCourseId;
        learnerCourse.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return LearnerCourseSnapshot.From(learnerCourse);
    }

    public async Task<IReadOnlyList<LearnerCourseSnapshot>> ListLearnerCoursesAsync(
        string profileId,
        CancellationToken cancellationToken)
    {
        var profile = RequireProfile(profileId);
        var courses = await db.LearnerCourses
            .AsNoTracking()
            .Where(x => x.ProfileId == profile)
            .OrderBy(x => x.EnrolledAt)
            .ToListAsync(cancellationToken);
        return courses.Select(LearnerCourseSnapshot.From).ToArray();
    }

    /// <summary>
    /// Sets a learner's personal delta for one curriculum item. Clearing both flags
    /// (not hidden, no custom ordinal) removes the delta so the item inherits the
    /// shared blueprint again.
    /// </summary>
    public async Task<LearnerCourseItemDeltaSnapshot?> SetItemDeltaAsync(
        Guid learnerCourseId,
        CurriculumItemType itemType,
        Guid itemId,
        bool isHidden,
        int? customOrdinal,
        CancellationToken cancellationToken)
    {
        await RequireLearnerCourseAsync(learnerCourseId, cancellationToken);

        var delta = await db.LearnerCourseItemDeltas
            .SingleOrDefaultAsync(
                x => x.LearnerCourseId == learnerCourseId
                    && x.ItemType == itemType
                    && x.ItemId == itemId,
                cancellationToken);

        var now = DateTime.UtcNow;
        if (!isHidden && customOrdinal is null)
        {
            if (delta is not null)
            {
                db.LearnerCourseItemDeltas.Remove(delta);
                await db.SaveChangesAsync(cancellationToken);
            }

            return null;
        }

        if (delta is null)
        {
            delta = new LearnerCourseItemDelta
            {
                LearnerCourseId = learnerCourseId,
                ItemType = itemType,
                ItemId = itemId,
                CreatedAt = now
            };
            db.LearnerCourseItemDeltas.Add(delta);
        }

        delta.IsHidden = isHidden;
        delta.CustomOrdinal = customOrdinal;
        delta.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return LearnerCourseItemDeltaSnapshot.From(delta);
    }

    public async Task<IReadOnlyList<LearnerCourseItemDeltaSnapshot>> ListDeltasAsync(
        Guid learnerCourseId,
        CancellationToken cancellationToken)
    {
        var deltas = await db.LearnerCourseItemDeltas
            .AsNoTracking()
            .Where(x => x.LearnerCourseId == learnerCourseId)
            .ToListAsync(cancellationToken);
        return deltas.Select(LearnerCourseItemDeltaSnapshot.From).ToArray();
    }

    /// <summary>
    /// Records a learner's course progress for one curriculum item. This is pure course
    /// progress: it never reads or writes FSRS card state.
    /// </summary>
    public async Task<LearnerCourseProgressSnapshot> RecordProgressAsync(
        Guid learnerCourseId,
        CurriculumItemType itemType,
        Guid itemId,
        CurriculumProgressStatus status,
        double? score,
        CancellationToken cancellationToken)
    {
        await RequireLearnerCourseAsync(learnerCourseId, cancellationToken);

        var progress = await db.LearnerCourseProgress
            .SingleOrDefaultAsync(
                x => x.LearnerCourseId == learnerCourseId
                    && x.ItemType == itemType
                    && x.ItemId == itemId,
                cancellationToken);

        var now = DateTime.UtcNow;
        if (progress is null)
        {
            progress = new LearnerCourseProgress
            {
                LearnerCourseId = learnerCourseId,
                ItemType = itemType,
                ItemId = itemId
            };
            db.LearnerCourseProgress.Add(progress);
        }

        if (status != CurriculumProgressStatus.NotStarted && progress.StartedAt is null)
        {
            progress.StartedAt = now;
        }

        if (status == CurriculumProgressStatus.Completed)
        {
            progress.CompletedAt = now;
            progress.CompletedCount++;
        }

        progress.Status = status;
        if (score is not null)
        {
            progress.Score = score;
        }

        progress.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return LearnerCourseProgressSnapshot.From(progress);
    }

    public async Task<IReadOnlyList<LearnerCourseProgressSnapshot>> ListProgressAsync(
        Guid learnerCourseId,
        CancellationToken cancellationToken)
    {
        var progress = await db.LearnerCourseProgress
            .AsNoTracking()
            .Where(x => x.LearnerCourseId == learnerCourseId)
            .ToListAsync(cancellationToken);
        return progress.Select(LearnerCourseProgressSnapshot.From).ToArray();
    }

    private async Task<(SharedCourseInstance Instance, bool Created)> ResolveSharedInstanceAsync(
        CurriculumBlueprint blueprint,
        string source,
        string target,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var existing = await db.SharedCourseInstances
            .SingleOrDefaultAsync(x => x.ContentFingerprint == fingerprint, cancellationToken);
        if (existing is not null)
        {
            return (existing, false);
        }

        var now = DateTime.UtcNow;
        var instance = new SharedCourseInstance
        {
            BlueprintId = blueprint.Id,
            SourceLanguage = source,
            TargetLanguage = target,
            ContentFingerprint = fingerprint,
            Title = $"{blueprint.Title} ({source} → {target})",
            CreatedAt = now,
            UpdatedAt = now
        };
        db.SharedCourseInstances.Add(instance);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent enrollment created the same shared instance first.
            db.Entry(instance).State = EntityState.Detached;
            var raced = await db.SharedCourseInstances
                .SingleOrDefaultAsync(x => x.ContentFingerprint == fingerprint, cancellationToken);
            if (raced is null)
            {
                throw;
            }

            return (raced, false);
        }

        return (instance, true);
    }

    private async Task<(LearnerCourse Course, bool Created)> ResolveLearnerCourseAsync(
        string profileId,
        Guid instanceId,
        Guid? learningCourseId,
        CancellationToken cancellationToken)
    {
        var existing = await db.LearnerCourses
            .SingleOrDefaultAsync(
                x => x.ProfileId == profileId && x.SharedInstanceId == instanceId,
                cancellationToken);
        if (existing is not null)
        {
            return (existing, false);
        }

        var now = DateTime.UtcNow;
        var learnerCourse = new LearnerCourse
        {
            ProfileId = profileId,
            SharedInstanceId = instanceId,
            LearningCourseId = learningCourseId,
            EnrolledAt = now,
            UpdatedAt = now
        };
        db.LearnerCourses.Add(learnerCourse);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            db.Entry(learnerCourse).State = EntityState.Detached;
            var raced = await db.LearnerCourses
                .SingleOrDefaultAsync(
                    x => x.ProfileId == profileId && x.SharedInstanceId == instanceId,
                    cancellationToken);
            if (raced is null)
            {
                throw;
            }

            return (raced, false);
        }

        return (learnerCourse, true);
    }

    private async Task RequireLearnerCourseAsync(
        Guid learnerCourseId,
        CancellationToken cancellationToken)
    {
        if (!await db.LearnerCourses.AnyAsync(x => x.Id == learnerCourseId, cancellationToken))
        {
            throw new KeyNotFoundException("Learner course was not found.");
        }
    }

    private static string RequireProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("A profile ID is required.", nameof(profileId));
        }

        return profileId.Trim();
    }
}
