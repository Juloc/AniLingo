using Jularr.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Web.Features.Learning.Curriculum;

/// <summary>
/// Authors and reads the universal, media-independent curriculum blueprint hierarchy
/// (Curriculum → Level → Chapter → Lesson → Exercise). It validates a
/// <see cref="CurriculumBlueprintSpec"/>, assigns deterministic 1-based ordinals from
/// authoring order, computes the content fingerprint and persists the graph. Building
/// the same content under the same key is idempotent; changing the content requires a
/// version bump so the fingerprint (and any shared instances derived from it) stay
/// consistent.
/// </summary>
public sealed class CurriculumBlueprintService(AppDbContext db)
{
    public async Task<CurriculumBlueprintSnapshot> BuildAsync(
        CurriculumBlueprintSpec spec,
        bool publish,
        CancellationToken cancellationToken)
    {
        Validate(spec);
        var fingerprint = CurriculumFingerprint.ForBlueprint(spec);
        var key = spec.Key.Trim();

        var existing = await db.CurriculumBlueprints
            .SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (existing is not null)
        {
            if (existing.ContentFingerprint != fingerprint)
            {
                throw new InvalidOperationException(
                    $"Curriculum blueprint '{key}' already exists with different content. " +
                    "Bump the blueprint version to publish changed content.");
            }

            if (publish && !existing.IsPublished)
            {
                existing.IsPublished = true;
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            return await LoadAsync(existing.Id, cancellationToken)
                ?? throw new InvalidOperationException("Blueprint disappeared during build.");
        }

        var now = DateTime.UtcNow;
        var blueprint = new CurriculumBlueprint
        {
            Key = key,
            Title = spec.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(spec.Description) ? null : spec.Description.Trim(),
            Version = spec.Version,
            ContentFingerprint = fingerprint,
            IsPublished = publish,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.CurriculumBlueprints.Add(blueprint);

        var levelOrdinal = 0;
        foreach (var levelSpec in spec.Levels)
        {
            var level = new CurriculumLevel
            {
                BlueprintId = blueprint.Id,
                Key = levelSpec.Key.Trim(),
                Title = levelSpec.Title.Trim(),
                Ordinal = ++levelOrdinal,
                CreatedAt = now
            };
            db.CurriculumLevels.Add(level);

            var chapterOrdinal = 0;
            foreach (var chapterSpec in levelSpec.Chapters)
            {
                var chapter = new CurriculumChapter
                {
                    LevelId = level.Id,
                    Key = chapterSpec.Key.Trim(),
                    Title = chapterSpec.Title.Trim(),
                    Ordinal = ++chapterOrdinal,
                    CreatedAt = now
                };
                db.CurriculumChapters.Add(chapter);

                var lessonOrdinal = 0;
                foreach (var lessonSpec in chapterSpec.Lessons)
                {
                    var lesson = new CurriculumLesson
                    {
                        ChapterId = chapter.Id,
                        Key = lessonSpec.Key.Trim(),
                        Title = lessonSpec.Title.Trim(),
                        Ordinal = ++lessonOrdinal,
                        CreatedAt = now
                    };
                    db.CurriculumLessons.Add(lesson);

                    var exerciseOrdinal = 0;
                    foreach (var exerciseSpec in lessonSpec.Exercises)
                    {
                        db.CurriculumExercises.Add(new CurriculumExercise
                        {
                            LessonId = lesson.Id,
                            Key = exerciseSpec.Key.Trim(),
                            Kind = exerciseSpec.Kind,
                            Prompt = string.IsNullOrWhiteSpace(exerciseSpec.Prompt)
                                ? null
                                : exerciseSpec.Prompt.Trim(),
                            Ordinal = ++exerciseOrdinal,
                            CreatedAt = now
                        });
                    }
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return await LoadAsync(blueprint.Id, cancellationToken)
            ?? throw new InvalidOperationException("Blueprint disappeared during build.");
    }

    public Task<CurriculumBlueprintSnapshot?> GetByKeyAsync(
        string key,
        CancellationToken cancellationToken) =>
        LoadInternalAsync(x => x.Key == key.Trim(), cancellationToken);

    public Task<CurriculumBlueprintSnapshot?> LoadAsync(
        Guid blueprintId,
        CancellationToken cancellationToken) =>
        LoadInternalAsync(x => x.Id == blueprintId, cancellationToken);

    private async Task<CurriculumBlueprintSnapshot?> LoadInternalAsync(
        System.Linq.Expressions.Expression<Func<CurriculumBlueprint, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var blueprint = await db.CurriculumBlueprints
            .AsNoTracking()
            .SingleOrDefaultAsync(predicate, cancellationToken);
        if (blueprint is null)
        {
            return null;
        }

        var levels = await db.CurriculumLevels
            .AsNoTracking()
            .Where(x => x.BlueprintId == blueprint.Id)
            .OrderBy(x => x.Ordinal)
            .ToListAsync(cancellationToken);
        var levelIds = levels.Select(x => x.Id).ToList();

        var chapters = await db.CurriculumChapters
            .AsNoTracking()
            .Where(x => levelIds.Contains(x.LevelId))
            .OrderBy(x => x.Ordinal)
            .ToListAsync(cancellationToken);
        var chapterIds = chapters.Select(x => x.Id).ToList();

        var lessons = await db.CurriculumLessons
            .AsNoTracking()
            .Where(x => chapterIds.Contains(x.ChapterId))
            .OrderBy(x => x.Ordinal)
            .ToListAsync(cancellationToken);
        var lessonIds = lessons.Select(x => x.Id).ToList();

        var exercises = await db.CurriculumExercises
            .AsNoTracking()
            .Where(x => lessonIds.Contains(x.LessonId))
            .OrderBy(x => x.Ordinal)
            .ToListAsync(cancellationToken);

        var exercisesByLesson = exercises.ToLookup(x => x.LessonId);
        var lessonsByChapter = lessons.ToLookup(x => x.ChapterId);
        var chaptersByLevel = chapters.ToLookup(x => x.LevelId);

        var levelSnapshots = levels
            .Select(level => new CurriculumLevelSnapshot(
                level.Id,
                level.Key,
                level.Title,
                level.Ordinal,
                chaptersByLevel[level.Id]
                    .OrderBy(x => x.Ordinal)
                    .Select(chapter => new CurriculumChapterSnapshot(
                        chapter.Id,
                        chapter.Key,
                        chapter.Title,
                        chapter.Ordinal,
                        lessonsByChapter[chapter.Id]
                            .OrderBy(x => x.Ordinal)
                            .Select(lesson => new CurriculumLessonSnapshot(
                                lesson.Id,
                                lesson.Key,
                                lesson.Title,
                                lesson.Ordinal,
                                exercisesByLesson[lesson.Id]
                                    .OrderBy(x => x.Ordinal)
                                    .Select(exercise => new CurriculumExerciseSnapshot(
                                        exercise.Id,
                                        exercise.Key,
                                        exercise.Kind,
                                        exercise.Prompt,
                                        exercise.Ordinal))
                                    .ToArray()))
                            .ToArray()))
                    .ToArray()))
            .ToArray();

        return new CurriculumBlueprintSnapshot(
            blueprint.Id,
            blueprint.Key,
            blueprint.Title,
            blueprint.Description,
            blueprint.Version,
            blueprint.ContentFingerprint,
            blueprint.IsPublished,
            levelSnapshots);
    }

    private static void Validate(CurriculumBlueprintSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        RequireKey(spec.Key, "blueprint");
        RequireTitle(spec.Title, "blueprint");
        if (spec.Version < 1)
        {
            throw new ArgumentException("Blueprint version must be at least 1.", nameof(spec));
        }

        if (spec.Levels.Count == 0)
        {
            throw new ArgumentException("A blueprint needs at least one level.", nameof(spec));
        }

        RequireUniqueKeys(spec.Levels.Select(x => x.Key), "level");
        foreach (var level in spec.Levels)
        {
            RequireKey(level.Key, "level");
            RequireTitle(level.Title, "level");
            if (level.Chapters.Count == 0)
            {
                throw new ArgumentException(
                    $"Level '{level.Key}' needs at least one chapter.", nameof(spec));
            }

            RequireUniqueKeys(level.Chapters.Select(x => x.Key), $"chapter in level '{level.Key}'");
            foreach (var chapter in level.Chapters)
            {
                RequireKey(chapter.Key, "chapter");
                RequireTitle(chapter.Title, "chapter");
                if (chapter.Lessons.Count == 0)
                {
                    throw new ArgumentException(
                        $"Chapter '{chapter.Key}' needs at least one lesson.", nameof(spec));
                }

                RequireUniqueKeys(chapter.Lessons.Select(x => x.Key), $"lesson in chapter '{chapter.Key}'");
                foreach (var lesson in chapter.Lessons)
                {
                    RequireKey(lesson.Key, "lesson");
                    RequireTitle(lesson.Title, "lesson");
                    if (lesson.Exercises.Count == 0)
                    {
                        throw new ArgumentException(
                            $"Lesson '{lesson.Key}' needs at least one exercise.", nameof(spec));
                    }

                    RequireUniqueKeys(lesson.Exercises.Select(x => x.Key), $"exercise in lesson '{lesson.Key}'");
                    foreach (var exercise in lesson.Exercises)
                    {
                        RequireKey(exercise.Key, "exercise");
                    }
                }
            }
        }
    }

    private static void RequireKey(string? key, string node)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException($"A {node} key is required.", nameof(key));
        }

        if (key.Trim().Length > CurriculumModelConfiguration.KeyMaxLength)
        {
            throw new ArgumentException(
                $"A {node} key can have at most {CurriculumModelConfiguration.KeyMaxLength} characters.",
                nameof(key));
        }
    }

    private static void RequireTitle(string? title, string node)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException($"A {node} title is required.", nameof(title));
        }
    }

    private static void RequireUniqueKeys(IEnumerable<string> keys, string scope)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (!seen.Add((key ?? "").Trim()))
            {
                throw new ArgumentException($"Duplicate {scope} key '{key}'.", nameof(keys));
            }
        }
    }
}
