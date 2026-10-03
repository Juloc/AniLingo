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


## 8. Daily Goal snapshot

Snapshot the active targets into one row per profile/local date:

```text
LearningDailyGoal
- Id                    BIGINT identity
- ProfileId             VARCHAR(80)
- LocalDate             DATE
- XpTarget              INTEGER, nullable
- ActiveSecondsTarget   INTEGER, nullable
- SessionsTarget        INTEGER, nullable
- CompletedAt           TIMESTAMPTZ, nullable
UNIQUE (ProfileId, LocalDate)
```

The row is created from current preferences when that gamified day first becomes active. Later preference changes do not rewrite today's existing goal; they apply to future days.

For every enabled component:

`component progress = min(actual / target, 1)`.

Combined Daily Goal progress is the **minimum** enabled component ratio. Therefore 100% means every configured target is satisfied.

A session counts toward `SessionsTarget` only after meaningful work. Opening/closing a screen is not enough.

## 9. Streak

Streak is derived from completed Daily Goal rows.

Rules:
- if today is complete, count the consecutive chain ending today;
- while today is incomplete, keep the chain ending yesterday until the local day ends;
- a missed prior enabled day breaks the chain;
- no midnight decrement job is needed.

Disabling gamification ends the active streak. Re-enabling starts a new active streak while historical completed days remain historical data.

No Streak Freeze/currency in V1.

## 10. XP levels

Global and optional per-language XP levels are deterministic projections from awarded XP.

Per-language projection uses the session LanguageTag.

Levels are motivational only:
- never CEFR/JLPT;
- never mastery truth;
- never curriculum prerequisites.

No separate level persistence is required in V1.

## 11. Achievements

V1 achievement definitions live in deterministic code/domain policy rather than an editable database catalog.

Each definition has:
- stable Key;
- Version;
- category;
- deterministic criterion/threshold;
- localization/icon references.

Keep the launch set small and meaningful.

Persist unlocks only:

```text
LearningAchievementUnlock
- Id                BIGINT identity
- ProfileId         VARCHAR(80)
- DefinitionKey     VARCHAR(120)
- DefinitionVersion INTEGER
- UnlockedAt        TIMESTAMPTZ
UNIQUE (ProfileId, DefinitionKey, DefinitionVersion)
```

Achievement progress is derived from canonical Learning state/activity. Do not add a mutable badge-progress table.

Achievements never unlock curriculum.

## 12. Failure isolation

Course progress and Review state remain valid if derived Achievement evaluation fails.

Activity/XP writes belonging to the base learner action should be transactionally consistent with that action. Derived Achievement evaluation may retry later, but failures must be logged/observable rather than silently swallowed.

## 13. Canonical statistic sources

Use:
- XP -> `LearningActivityEvent`;
- learning time -> activity sessions/time slices;
- Lessons -> `LearnerCourseProgress` / completion activity;
- Reviews -> `LearningCardReview`;
- Vocabulary -> Recognition/card lifecycle semantics;
- Achievements -> `LearningAchievementUnlock`.

Do not add copied dashboard counters as competing truth.

## 14. Persistence/performance

New internal IDs use BIGINT identity.

Use PostgreSQL DATE, TIMESTAMPTZ and BOOLEAN plus FK/UNIQUE/CHECK constraints. New lifecycle relationships use NO ACTION/RESTRICT; backend operations own business mutation.

Useful access paths:
- activity by `ProfileId, OccurredAt`;
- sessions by `ProfileId, StartedAt`;
- unique session/day slice;
- unique profile/day goal;
- unique achievement unlock.

Do not add a daily aggregate/cache table until measurement proves it necessary.

## 15. Required tests

Active time:
- duplicate activity report adds zero;
- idle/background adds no time;
- impossible time inflation is capped;
- profile-local day attribution works.

XP:
- same SourceEventId cannot award twice;
- gamification Off awards zero;
- Extra Practice cap applies;
- policy version is stored.

Daily Goal/Streak:
- today's target snapshot stays stable after preference edit;
- combined progress uses the minimum enabled component;
- completion is idempotent;
- incomplete today does not prematurely break yesterday's streak;
- missed enabled day breaks;
- disable/re-enable starts a new active streak.

Achievements:
- deterministic/idempotent unlock;
- no curriculum effect.

Database-specific invariants use PostgreSQL integration tests.

## 16. Must not implement

- No high-volume Learning actions in the global notification event log.
- No page-open learning time.
- No automatic Player/Reader consumption time.
- No heartbeat-event table.
- No duplicate XP counter/store.
- No XP while gamification is Off.
- No retroactive XP.
- No XP/level curriculum locks.
- No CEFR/JLPT from XP.
- No mutable Achievement progress store.
- No virtual currency/shop/required leaderboard.

## 17. Acceptance

Learning has one bounded session/activity model, truthful active time, idempotent/versioned XP, stable Daily Goal/Streak semantics and deterministic Achievements. Gamification can disappear without changing core Learning behavior or corrupting statistics.
