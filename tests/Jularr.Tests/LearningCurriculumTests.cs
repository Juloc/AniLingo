using Jularr.Web.Data;
using Jularr.Web.Features.Learning;
using Jularr.Web.Features.Learning.Courses;
using Jularr.Web.Features.Learning.Curriculum;
using Jularr.Web.Features.Vocabulary;
using Microsoft.EntityFrameworkCore;

namespace Jularr.Tests;

/// <summary>
/// Learning v3 foundation (#441): the media-independent curriculum blueprint
/// hierarchy, shared (deduped) course instances specialized per language pair with a
/// personal variant/delta per learner, and course progress kept separate from the v2
/// FSRS review state.
/// </summary>
[TestClass]
public sealed class LearningCurriculumTests
{
    // -------------------------------------------------------------------------
    // Curriculum hierarchy: build + validate
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task BuildsAndReloadsTheFullHierarchyWithOrdinals()
    {
        await using var fixture = await Fixture.CreateAsync();

        var built = await fixture.Blueprints.BuildAsync(
            SampleSpec(),
            publish: true,
            CancellationToken.None);

        var loaded = await fixture.Blueprints.LoadAsync(built.Id, CancellationToken.None);
        Assert.IsNotNull(loaded);

        foreach (var blueprint in new[] { built, loaded })
        {
            Assert.IsTrue(blueprint.IsPublished);
            Assert.IsFalse(string.IsNullOrEmpty(blueprint.ContentFingerprint));

            // Curriculum -> Level -> Chapter -> Lesson -> Exercise, ordinals 1-based by author order.
            CollectionAssert.AreEqual(
                new[] { 1, 2 },
                blueprint.Levels.Select(x => x.Ordinal).ToArray());
            CollectionAssert.AreEqual(
                new[] { "a1", "a2" },
                blueprint.Levels.Select(x => x.Key).ToArray());

            var a1 = blueprint.Levels[0];
            CollectionAssert.AreEqual(
                new[] { "greetings", "numbers" },
                a1.Chapters.Select(x => x.Key).ToArray());
            CollectionAssert.AreEqual(
                new[] { 1, 2 },
                a1.Chapters.Select(x => x.Ordinal).ToArray());

            var hello = a1.Chapters[0].Lessons[0];
            Assert.AreEqual("hello", hello.Key);
            CollectionAssert.AreEqual(
                new[] { 1, 2 },
                hello.Exercises.Select(x => x.Ordinal).ToArray());
            CollectionAssert.AreEqual(
                new[] { CurriculumExerciseKind.Flashcard, CurriculumExerciseKind.MultipleChoice },
                hello.Exercises.Select(x => x.Kind).ToArray());
        }
    }

    [TestMethod]
    public async Task BuildingIdenticalContentIsIdempotent()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.Blueprints.BuildAsync(SampleSpec(), true, CancellationToken.None);
        var second = await fixture.Blueprints.BuildAsync(SampleSpec(), true, CancellationToken.None);

        Assert.AreEqual(first.Id, second.Id);
        Assert.AreEqual(first.ContentFingerprint, second.ContentFingerprint);
        Assert.AreEqual(1, await fixture.Db.CurriculumBlueprints.CountAsync());
        Assert.AreEqual(2, await fixture.Db.CurriculumLevels.CountAsync());
    }

    [TestMethod]
    public async Task RepublishingTheSameKeyWithChangedContentIsRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Blueprints.BuildAsync(SampleSpec(), true, CancellationToken.None);

        var changed = SampleSpec() with
        {
            Levels =
            [
                new CurriculumLevelSpec(
                    "a1",
                    "A1 Beginner",
                    [
                        new CurriculumChapterSpec(
                            "greetings",
                            "Greetings",
                            [
                                new CurriculumLessonSpec(
                                    "hello",
                                    "Saying hello",
                                    [new CurriculumExerciseSpec("flash-1", CurriculumExerciseKind.Flashcard)])
                            ])
                    ])
            ]
        };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => fixture.Blueprints.BuildAsync(changed, true, CancellationToken.None));
    }

    [TestMethod]
    public async Task ValidationRejectsMalformedBlueprints()
    {
        await using var fixture = await Fixture.CreateAsync();

        // No levels.
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => fixture.Blueprints.BuildAsync(
                SampleSpec() with { Levels = [] },
                true,
                CancellationToken.None));

        // Duplicate level keys.
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => fixture.Blueprints.BuildAsync(
                SampleSpec() with { Levels = [SingleLevel("dup"), SingleLevel("dup")] },
                true,
                CancellationToken.None));

        // Chapter without lessons.
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => fixture.Blueprints.BuildAsync(
                SampleSpec() with
                {
                    Levels = [new CurriculumLevelSpec("a1", "A1", [new CurriculumChapterSpec("c", "Chapter", [])])]
                },
                true,
                CancellationToken.None));

        // Lesson without exercises.
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => fixture.Blueprints.BuildAsync(
                SampleSpec() with
                {
                    Levels =
                    [
                        new CurriculumLevelSpec("a1", "A1",
                        [
                            new CurriculumChapterSpec("c", "Chapter",
                            [
                                new CurriculumLessonSpec("l", "Lesson", [])
                            ])
                        ])
                    ]
                },
                true,
                CancellationToken.None));

        // Blank title.
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => fixture.Blueprints.BuildAsync(
                SampleSpec() with { Title = "  " },
                true,
                CancellationToken.None));

        Assert.AreEqual(0, await fixture.Db.CurriculumBlueprints.CountAsync());
    }

    // -------------------------------------------------------------------------
    // Shared instance dedup + personal variant/delta
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task SharedInstanceIsDedupedAcrossProfilesWithIndependentVariants()
    {
        await using var fixture = await Fixture.CreateAsync();
        var blueprint = await fixture.Blueprints.BuildAsync(SampleSpec(), true, CancellationToken.None);

        var alice = await fixture.Shared.EnrollAsync(
            "alice", blueprint.Id, "ja", "de", null, CancellationToken.None);
        var bob = await fixture.Shared.EnrollAsync(
            "bob", blueprint.Id, "ja", "de", null, CancellationToken.None);

        // One shared instance is created and then reused; each learner gets their own variant.
        Assert.IsTrue(alice.SharedInstanceCreated);
        Assert.IsTrue(alice.EnrollmentCreated);
        Assert.IsFalse(bob.SharedInstanceCreated);
        Assert.IsTrue(bob.EnrollmentCreated);
        Assert.AreEqual(alice.SharedInstance.Id, bob.SharedInstance.Id);
        Assert.AreNotEqual(alice.LearnerCourse.Id, bob.LearnerCourse.Id);

        Assert.AreEqual(1, await fixture.Db.SharedCourseInstances.CountAsync());
        Assert.AreEqual(2, await fixture.Db.LearnerCourses.CountAsync());

        // Re-enrolling the same profile is idempotent.
        var aliceAgain = await fixture.Shared.EnrollAsync(
            "alice", blueprint.Id, "ja", "de", null, CancellationToken.None);
        Assert.AreEqual(alice.LearnerCourse.Id, aliceAgain.LearnerCourse.Id);
        Assert.IsFalse(aliceAgain.EnrollmentCreated);
        Assert.IsFalse(aliceAgain.SharedInstanceCreated);
        Assert.AreEqual(2, await fixture.Db.LearnerCourses.CountAsync());

        // A different language pair specializes into a different shared instance.
        var aliceEnglish = await fixture.Shared.EnrollAsync(
            "alice", blueprint.Id, "ja", "en", null, CancellationToken.None);
        Assert.IsTrue(aliceEnglish.SharedInstanceCreated);
        Assert.AreNotEqual(alice.SharedInstance.Id, aliceEnglish.SharedInstance.Id);
        Assert.AreEqual(2, await fixture.Db.SharedCourseInstances.CountAsync());
    }

    [TestMethod]
    public async Task PersonalDeltaIsIsolatedAndLeavesSharedContentUntouched()
    {
        await using var fixture = await Fixture.CreateAsync();
        var blueprint = await fixture.Blueprints.BuildAsync(SampleSpec(), true, CancellationToken.None);
        var lessonId = blueprint.Levels[0].Chapters[0].Lessons[0].Id;

        var alice = await fixture.Shared.EnrollAsync(
            "alice", blueprint.Id, "ja", "de", null, CancellationToken.None);
        var bob = await fixture.Shared.EnrollAsync(
            "bob", blueprint.Id, "ja", "de", null, CancellationToken.None);

        var delta = await fixture.Shared.SetItemDeltaAsync(
            alice.LearnerCourse.Id,
            CurriculumItemType.Lesson,
            lessonId,
            isHidden: true,
            customOrdinal: null,
            CancellationToken.None);
        Assert.IsNotNull(delta);
        Assert.IsTrue(delta.IsHidden);

        // The delta is Alice's alone; Bob's variant is unaffected.
        Assert.AreEqual(1, (await fixture.Shared.ListDeltasAsync(alice.LearnerCourse.Id, CancellationToken.None)).Count);
        Assert.AreEqual(0, (await fixture.Shared.ListDeltasAsync(bob.LearnerCourse.Id, CancellationToken.None)).Count);

        // The shared blueprint content is never mutated by a personal delta.
        var reloaded = await fixture.Blueprints.LoadAsync(blueprint.Id, CancellationToken.None);
        Assert.IsNotNull(reloaded);
        Assert.IsTrue(reloaded.Levels[0].Chapters[0].Lessons.Any(x => x.Id == lessonId));

        // Clearing the delta removes it so the item inherits the shared blueprint again.
        var cleared = await fixture.Shared.SetItemDeltaAsync(
            alice.LearnerCourse.Id,
            CurriculumItemType.Lesson,
            lessonId,
            isHidden: false,
            customOrdinal: null,
            CancellationToken.None);
        Assert.IsNull(cleared);
        Assert.AreEqual(0, (await fixture.Shared.ListDeltasAsync(alice.LearnerCourse.Id, CancellationToken.None)).Count);
    }

    // -------------------------------------------------------------------------
    // Course progress vs FSRS review state: separate axes
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task CourseProgressAndFsrsReviewStateDoNotLeakIntoEachOther()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.SavePreferencesAsync(0.90, 50, 100, CancellationToken.None);

        var blueprint = await fixture.Blueprints.BuildAsync(SampleSpec(), true, CancellationToken.None);
        var lessonId = blueprint.Levels[0].Chapters[0].Lessons[0].Id;
        var enrollment = await fixture.Shared.EnrollAsync(
            LearningProfile.DefaultId, blueprint.Id, "ja", "de", null, CancellationToken.None);
        var learnerCourseId = enrollment.LearnerCourse.Id;

        // Advance course progress. This must not create any FSRS card/review state.
        await fixture.Shared.RecordProgressAsync(
            learnerCourseId, CurriculumItemType.Lesson, lessonId,
            CurriculumProgressStatus.InProgress, null, CancellationToken.None);
        var progress = await fixture.Shared.RecordProgressAsync(
            learnerCourseId, CurriculumItemType.Lesson, lessonId,
            CurriculumProgressStatus.Completed, 0.8, CancellationToken.None);

        Assert.AreEqual(CurriculumProgressStatus.Completed, progress.Status);
        Assert.AreEqual(1, progress.CompletedCount);
        Assert.IsNotNull(progress.StartedAt);
        Assert.IsNotNull(progress.CompletedAt);
        Assert.AreEqual(0.8, progress.Score);
        Assert.AreEqual(1, await fixture.Db.LearnerCourseProgress.CountAsync());
        Assert.AreEqual(0, await fixture.Db.LearningCards.CountAsync());
        Assert.AreEqual(0, await fixture.Db.LearningCardReviews.CountAsync());

        // Now exercise FSRS through the v2 path. This must not touch course progress.
        var term = await fixture.AddTermAsync("水", "みず", "Wasser");
        await fixture.Service.SetStateAsync(term.Id, UserTermState.Learning, CancellationToken.None);

        var due = await fixture.Service.GetDueAsync(CancellationToken.None);
        Assert.IsTrue(
            due.Any(x => x.TermId == term.Id),
            "The card is due in FSRS even though a lesson is already completed in course progress.");
        await fixture.Service.ReviewAsync(due[0].CardId, ReviewRating.Good, CancellationToken.None);

        Assert.AreEqual(1, await fixture.Db.LearningCardReviews.CountAsync());

        // Course progress is exactly what the curriculum actions set: FSRS did not advance it.
        var progressRows = await fixture.Db.LearnerCourseProgress.AsNoTracking().ToListAsync();
        Assert.AreEqual(1, progressRows.Count);
        Assert.AreEqual(CurriculumProgressStatus.Completed, progressRows[0].Status);
        Assert.AreEqual(1, progressRows[0].CompletedCount);
    }

    private static CurriculumBlueprintSpec SampleSpec(string key = "foundations", int version = 1) =>
        new(
            key,
            "Foundations",
            "A universal, media-independent language curriculum.",
            version,
            [
                new CurriculumLevelSpec(
                    "a1",
                    "A1 Beginner",
                    [
                        new CurriculumChapterSpec(
                            "greetings",
                            "Greetings",
                            [
                                new CurriculumLessonSpec(
                                    "hello",
                                    "Saying hello",
                                    [
                                        new CurriculumExerciseSpec("flash-1", CurriculumExerciseKind.Flashcard),
                                        new CurriculumExerciseSpec("choice-1", CurriculumExerciseKind.MultipleChoice, "Pick the greeting.")
                                    ]),
                                new CurriculumLessonSpec(
                                    "goodbye",
                                    "Saying goodbye",
                                    [new CurriculumExerciseSpec("flash-2", CurriculumExerciseKind.Flashcard)])
                            ]),
                        new CurriculumChapterSpec(
                            "numbers",
                            "Numbers",
                            [
                                new CurriculumLessonSpec(
                                    "count",
                                    "Counting to ten",
                                    [new CurriculumExerciseSpec("match-1", CurriculumExerciseKind.Matching)])
                            ])
                    ]),
                new CurriculumLevelSpec(
                    "a2",
                    "A2 Elementary",
                    [
                        new CurriculumChapterSpec(
                            "food",
                            "Food",
                            [
                                new CurriculumLessonSpec(
                                    "order",
                                    "Ordering food",
                                    [new CurriculumExerciseSpec("cloze-1", CurriculumExerciseKind.Cloze)])
                            ])
                    ])
            ]);

    private static CurriculumLevelSpec SingleLevel(string key) =>
        new(
            key,
            "Level",
            [
                new CurriculumChapterSpec(
                    "c",
                    "Chapter",
                    [
                        new CurriculumLessonSpec(
                            "l",
                            "Lesson",
                            [new CurriculumExerciseSpec("x", CurriculumExerciseKind.Flashcard)])
                    ])
            ]);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string directory, AppDbContext db)
        {
            Directory = directory;
            Db = db;
            Blueprints = new CurriculumBlueprintService(db);
            Shared = new SharedCourseService(db);
            Service = new LearningService(db, new FsrsReviewScheduler());
        }

        public string Directory { get; }
        public AppDbContext Db { get; }
        public CurriculumBlueprintService Blueprints { get; }
        public SharedCourseService Shared { get; }
        public LearningService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"jularr-learning-curriculum-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(
                    $"Data Source={Path.Combine(directory, "jularr.db")};Foreign Keys=True")
                .Options;

            var fixture = new Fixture(directory, new AppDbContext(options));
            await DatabaseMigrationBridge.UpgradeAsync(fixture.Db);
            return fixture;
        }

        public async Task<Term> AddTermAsync(string canonical, string reading, string meaning)
        {
            var term = new Term
            {
                Language = "ja",
                Canonical = canonical,
                Reading = reading,
                Meaning = meaning
            };
            Db.Terms.Add(term);
            await Db.SaveChangesAsync();
            return term;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();

            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
