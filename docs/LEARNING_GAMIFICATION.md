# Jularr Learning Activity and Gamification

Status: **binding Learning Phase 1 architecture/product plan**.

Related:
- `docs/LEARNING_PROGRESS.md`
- `docs/LEARNING_PRACTICE_REVIEW.md`
- `docs/mockups/learning-home/SPEC.md`
- `docs/mockups/progress-achievements/SPEC.md`
- `docs/mockups/user-settings/SPEC.md`

## 1. Ownership

Learning activity is truthful product/statistics infrastructure. XP, Daily Goal, Streak and Achievements are optional gamification over that activity.

Do not publish every exercise, review or timer update into the global `JularrEvent` notification/audit pipeline. Learning owns compact profile-private activity persistence because the volume and purpose differ. This is not a second notification bus.

Gamification Off must not disable Lessons, Reviews, Vocabulary, Sentences, Script Trainer, course progress or core activity/time statistics.

## 2. Learning activity session

Use one `LearningActivitySession` for:
- Lesson;
- Review;
- VocabularyPractice;
- SentencePractice;
- ScriptPractice;
- MediaPractice.

Target fields:

```text
LearningActivitySession
- Id                        BIGINT identity
- ClientSessionId           UUID
- ProfileId                 VARCHAR(80)
- Kind                      INTEGER
- LanguageTag               VARCHAR(35), nullable
- LearnerCourseId           UUID, nullable
- LearningCourseId          UUID, nullable
- CurriculumLessonId        UUID, nullable
- StartedAt                 TIMESTAMPTZ
- LastActivityAt            TIMESTAMPTZ
- EndedAt                   TIMESTAMPTZ, nullable
- EndReason                 INTEGER, nullable
- ClientActiveMilliseconds  BIGINT
- ActiveSeconds             BIGINT
```

Unique: `(ProfileId, ClientSessionId)`.

End reasons are Completed, Exited and Abandoned. Session existence never implies Lesson/Review completion.

## 3. Active learning time

Learning time means **active Learning interaction**, never page-open wall time.

Do not count:
- background/idle tabs;
- normal Player playback;
- normal Reader time;
- loading/downloading;
- paused sessions.

Player/Reader time counts only while an actual Learning interaction is active.

The client reports a monotonic `activeMillisecondsSinceSessionStart` counter. The server:
- accepts positive delta only;
- treats duplicate/smaller values as zero;
- caps accepted delta against plausible elapsed wall time plus small transport tolerance.

Do not persist heartbeat rows.

## 4. Local-day time slices

Persist compact attribution:

```text
LearningSessionTimeSlice
- Id              BIGINT identity
- SessionId       BIGINT FK
- LocalDate       DATE
- ActiveSeconds   BIGINT
UNIQUE (SessionId, LocalDate)
```

LocalDate comes from the profile's canonical time-zone/regional setting. Do not add a Learning-specific time zone.

## 5. Meaningful activity ledger

Persist only meaningful actions:

```text
LearningActivityEvent
- Id              BIGINT identity
- SessionId       BIGINT FK
- ProfileId       VARCHAR(80)
- SourceEventId   UUID
- Kind            INTEGER
- OccurredAt      TIMESTAMPTZ
- XpAwarded       INTEGER
- XpPolicyVersion INTEGER
```

Unique: `(ProfileId, SourceEventId, Kind)`.

V1 kinds:
- ExerciseCompleted;
- ReviewCompleted;
- LessonCompleted;
- PracticeItemCompleted;
- SessionCompleted.

Detailed answer/review facts stay in `LearnerExerciseAttempt` and `LearningCardReview`. Do not copy raw answers into this ledger.

## 6. XP

XP is stored on the activity event that earned it, so historical XP does not change when balancing changes.

One versioned `LearningXpPolicy` owns award values.

Rules:
- no XP for navigation, lookup-only actions or idle time;
- mistakes are not punishment;
- correctness/hints may affect a bounded bonus only;
- repeated Extra Practice has reduced/capped XP;
- SourceEventId makes award idempotent;
- no currency/shop/spending;
- XP never changes course progress or FSRS.

## 7. Gamification preferences

Keep scheduler preferences separate from profile-scoped gamification preferences:

```text
LearningGamificationPreferences
- ProfileId             PK
- IsEnabled             BOOLEAN
- DailyXpTarget         INTEGER, nullable
- DailyActiveMinutes    INTEGER, nullable
- DailySessionsTarget   INTEGER, nullable
- UpdatedAt             TIMESTAMPTZ
```

When Off:
- core activity/time still records;
- no new XP;
- no Daily Goal/Streak advancement;
- gamification UI is hidden;
- historical XP/unlocks are preserved.

Re-enabling does not award retroactive XP.
