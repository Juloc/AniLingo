# Jularr Learning Course Progress and Resume

Status: **binding Learning Phase 1 architecture plan**.

Related:
- `docs/LEARNING_PEDAGOGY.md`
- `docs/LEARNING_EXERCISES.md`
- `docs/LEARNING_V2.md`
- `docs/mockups/course-detail/SPEC.md`
- `docs/mockups/lesson-review/SPEC.md`
- `docs/DATABASE_CONVENTIONS.md`

## 1. Canonical owner

`LearnerCourseProgress` remains the only durable course-progress store.

Course progress is separate from:
- LearningCard/FSRS state;
- XP/Streak/Achievements;
- media watch/read progress.

Completing a Lesson never means its cards are Known. Reviewing a card never advances course progress.

## 2. Progress granularity

Keep:
`Level -> Chapter -> Lesson -> Exercise`.

Do not add a persisted Section node in V1. Pedagogical sections are represented by authored Exercise flow and Checkpoint boundaries. Exact resume targets an Exercise.

## 3. Progress states

Evolve `CurriculumProgressStatus` without renumbering existing values:

```text
NotStarted
InProgress
Completed
Skipped
```

V1 skip reasons:
- Optional
- CapabilityUnavailable

Incorrect is an attempt outcome, not a progress state.

## 4. Progress row target

Keep the existing unique identity:
`UNIQUE (LearnerCourseId, ItemType, ItemId)`.

Target semantics:

```text
LearnerCourseProgress
- existing Id
- LearnerCourseId
- ItemType
- ItemId
- Status
- CompletedCount
- LastScore             NUMERIC(5,4), nullable
- FirstStartedAt        TIMESTAMPTZ, nullable
- FirstCompletedAt      TIMESTAMPTZ, nullable
- LastCompletedAt       TIMESTAMPTZ, nullable
- SkipReason            INTEGER, nullable
- UpdatedAt             TIMESTAMPTZ
```

Database checks:
- score in 0.0000..1.0000;
- CompletedCount >= 0;
- SkipReason only when Status = Skipped.

Migration:
- current StartedAt -> FirstStartedAt;
- current CompletedAt seeds first/last completion;
- current floating Score moves to bounded exact numeric;
- remove obsolete runtime meaning after migration instead of keeping two paths.

## 5. Exercise attempts

Add durable attempt facts; they are history, not a second progress store.

```text
LearnerExerciseAttempt
- Id                    BIGINT identity
- LearnerCourseId       UUID FK
- ExerciseId            UUID FK
- LearningSessionId     BIGINT FK
- ClientEventId         UUID UNIQUE
- Outcome               INTEGER enum
- Score                 NUMERIC(5,4), nullable
- UsedHint              BOOLEAN
- RevealedAnswer        BOOLEAN
- ResponseDurationMs    BIGINT, nullable
- OccurredAt            TIMESTAMPTZ
```

V1 outcomes:
- Completed for non-scored Presentation;
- Correct;
- Incorrect;
- Partial.

Do not store a fake answer attempt for capability failure.

Raw typed answers are not persisted by default. Immediate feedback may use the submitted value transiently.

## 6. One submission transaction

A scored Exercise submission is one application action:

```text
authorize
-> load published Exercise content
-> check ClientEventId
-> evaluate with canonical Exercise/toolkit rules
-> insert attempt
-> update Exercise progress
-> update affected Lesson/Chapter/Level aggregates
-> append Learning activity event
-> commit
-> return feedback
```

UI/page code does not own correctness or progression rules.

Database constraints validate structure; cascades/triggers do not perform the business workflow.

## 7. Idempotency

Submitting the same ClientEventId twice must not:
- create a second attempt;
- increment completion twice;
- award XP twice;
- duplicate review/activity state.

A unique constraint is the final concurrent-race protection.

## 8. Exercise start and completion

Entering a not-started Exercise marks it InProgress and sets FirstStartedAt once.

Opening Course Detail or the Lesson shell alone does not start/complete an Exercise.

A required Exercise completes after a valid terminal interaction:
- Correct completes;
- Incorrect completes after corrective feedback when normal Lesson policy proceeds;
- Partial completes after deterministic submission;
- Presentation completes on explicit Continue.

Normal Lesson completion is attempt-based, not perfection-based.

## 9. Repeating work

Re-practice never erases earlier completion.

On another completion:
- CompletedCount increments;
- LastCompletedAt updates;
- FirstCompletedAt stays unchanged;
- LastScore updates;
- a new attempt is recorded.

Practice Again does not reset the Lesson to NotStarted.

## 10. Exact resume

Do not add another durable resume pointer while Exercise progress can resolve it deterministically.

Resume order:

1. apply the learner's visible personal variant;
2. find an InProgress Exercise;
3. otherwise find the first unsatisfied required Exercise;
4. otherwise the Lesson is complete and opens Summary/completed state.

Satisfied means:
- Completed;
- permitted Optional skip;
- permitted CapabilityUnavailable skip.

Transient unsent form input may be kept locally, but is not canonical progress.

## 11. Continue Learning

For a learner course:

1. resume an InProgress Lesson;
2. otherwise choose the first available incomplete Lesson;
3. if no Lesson remains, the course is complete.

Learning Home and Course Detail must call the same backend/application resolver.

## 12. Lesson completion

A Lesson completes when every visible required Exercise is satisfied.

It does not require:
- 100% accuracy;
- Known cards;
- completed Reviews;
- Daily Goal;
- XP threshold.

Optional Exercises do not block completion.

Temporary TTS/capability loss cannot permanently trap an ordinary V1 Lesson.

## 13. Chapter and Level aggregates

Chapter/Level rows are materialized aggregates only.

They are updated by the course-progress owner from child state. UI code cannot directly mark them complete.

- Chapter completes when all visible required Lessons are complete.
- Level completes when all visible required Chapters/Lessons are complete.
- voluntary re-practice does not make a completed parent incomplete.

## 14. Course percentage

No separate CourseProgress table.

V1 percentage:

```text
satisfied visible required Exercises
/
total visible required Exercises
```

No XP, FSRS or difficulty weighting.

Optional Exercises are excluded. CapabilityUnavailable skips can satisfy course flow but never count as successful learning attempts.

## 15. Accuracy

Accuracy is attempt/session data, not course progress.

Lesson-session accuracy uses scored Exercises only and should use the first independent meaningful attempt for the current run. Presentation is excluded; a later guided retry does not erase the original miss.

## 16. Personal deltas

Existing `LearnerCourseItemDelta` remains the personal structural override.

Hidden items:
- keep stored progress/history;
- are excluded from the current visible denominator;
- do not delete LearningCards/attempts.

Unhiding restores prior progress.

Reordering changes navigation only, not canonical blueprint order.

## 17. V1 curriculum progression

Do not prebuild a generic prerequisite graph.

Use one explicit course/blueprint policy:

```text
Open
Sequential
```

Open: any visible Lesson may start.

Sequential: a Lesson is available when the previous visible required Lesson in course order is complete.

The backend enforces this on direct requests too.

Never lock curriculum from XP, Streak, Achievement, FSRS mastery or currency.

If real cross-branch prerequisites are needed later, add an explicit relational model then.

## 18. Learning sessions

Attempts reference the canonical Learning session defined in `LEARNING_GAMIFICATION.md`.

The session provides the run boundary used for:
- Lesson/Review/Practice grouping;
- summaries;
- active learning time.

Course progress continues to work when gamified UI is disabled.

## 19. Multiple devices

Correctness relies on PostgreSQL transaction/constraint semantics, not process-local locks.

- each attempt has a unique ClientEventId;
- progress completion is monotonic under normal use;
- one device cannot erase another device's completion;
- duplicate retries are idempotent;
- two genuinely different attempts may both be recorded.

## 20. Shared course revisions

A LearnerCourse stays pinned to its SharedCourseInstance.

Publishing a newer content revision:
- does not silently switch the learner;
- does not mutate the old published revision;
- does not reset progress;
- never switches in the middle of an active Lesson.

For content-only revisions over the same blueprint structure, Exercise IDs remain valid and an explicit safe update can repoint the learner course.

## 21. Structural curriculum revisions

Structural blueprint changes are not auto-remapped in V1.

Existing learners stay pinned. New learners may enroll in the newer structure.

Future migration may use stable hierarchy keys, but must preview/validate mapping. Do not match progress by title or ordinal.

## 22. Service ownership

Do not expand current `SharedCourseService` into the Lesson engine.

Target responsibilities:
- shared-course/enrollment owner: enrollment + personal structural deltas;
- Lesson execution/progress owner: start/resume/submit/skip/aggregate progress;
- Exercise evaluator: deterministic kind/toolkit answer evaluation.

The current generic `RecordProgressAsync` must not remain an unrestricted public way for UI callers to mark arbitrary parent items complete once the engine exists.

Do not add forwarding wrappers between these owners.

## 23. Query shape

Course Detail/Learning Home load progress with bounded set-based projections.

No one-query-per-Lesson or one-query-per-Exercise progress access.

The current course percentage and next action come from one canonical query/application owner.

## 24. Persistence rules

Implementation must use:
- FK/UNIQUE/NOT NULL/CHECK constraints;
- NO ACTION/RESTRICT for new lifecycle relations;
- backend-owned dependent mutations;
- PostgreSQL integration tests.

Existing UUID identities remain. New internal row identities default to BIGINT identity unless independently generated identity is required.

## 25. Tests required

Unit/domain:
- resume selection;
- Sequential availability;
- required vs optional completion;
- capability skip;
- hidden-item denominator;
- course percentage;
- aggregate transitions;
- compatible revision behavior.

PostgreSQL:
- duplicate ClientEventId idempotency;
- concurrent attempts do not corrupt completion;
- unique progress row;
- score/count checks;
- transaction rollback across attempt/progress/activity.

End-to-end:
- partial Lesson -> exit -> exact resume;
- wrong answer -> feedback -> continue -> Lesson completes;
- completed Lesson -> Practice Again without reset;
- direct request cannot bypass Sequential progression.

## 26. Must not implement

- No second course-progress store.
- No persisted Section hierarchy in V1.
- No redundant resume pointer while Exercise progress is sufficient.
- No UI-owned completion.
- No 100%-accuracy gate.
- No XP/FSRS-derived completion.
- No generic polymorphic prerequisite graph in V1.
- No reset-on-practice-again.
- No silent revision switch.
- No title/ordinal structural remapping.
- No database cascade/trigger workflow.
- No duplicate activity on retry.

## 27. Acceptance

The plan is complete when:
- exact resume resolves to one Exercise;
- attempts preserve useful history without unnecessary raw input;
- Lesson completion is deterministic and non-punitive;
- Chapter/Level/course state follows child work;
- locks come only from explicit course progression policy;
- practice preserves completion;
- revisions do not reset learners;
- FSRS, XP and course progress remain independent;
- the implementation can be transactional/idempotent with PostgreSQL as the final integrity boundary.
