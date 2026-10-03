# Jularr Learning Practice and Review Policy

Status: **binding Learning Phase 1 architecture/product plan**.

Related:
- `docs/LEARNING_PEDAGOGY.md`
- `docs/LEARNING_EXERCISES.md`
- `docs/LEARNING_PROGRESS.md`
- `docs/LEARNING_LANGUAGES.md`
- `docs/mockups/lesson-review/SPEC.md`
- `docs/mockups/script-trainer/SPEC.md`

## 1. One scheduler

`LearningCard`, `LearningCardReview` and the existing FSRS scheduler remain the only long-term scheduling system.

Lesson, Kana, Sentences, Vocabulary and Media Learning must not create their own intervals or due queues.

## 2. Three activity types

### Learning Practice
Initial acquisition in Lessons, Kana groups and guided practice.

It can move a unit into Learning/queue state, but does not itself write a scheduled FSRS review.

### Scheduled Review
A due `LearningCard` selected by the canonical scheduler.

It writes one `LearningCardReview` and updates FSRS.

### Extra Practice
Voluntary practice of selected mistakes/units/groups/context.

It does not change FSRS due dates by default.

## 3. Unit lifecycle

Keep:
`Untracked -> Saved -> Learning -> Known`, plus Ignored/Suspended.

Rules:
- Saved has no review pressure;
- Learning participates in scheduling;
- Known is explicit, never inferred from one correct answer or course completion;
- Lesson/Practice does not override Known/Ignored/Suspended.

## 4. When Lesson material enters Learning

A Target LearningUnit is queued when the learner reaches its first independent retrieval/application/checkpoint attempt.

Presentation or Guided Practice alone does not create review pressure.

If the unit is already tracked, preserve its current canonical state.

## 5. Daily new limit

Migrate `NewWordsPerDay` to `NewUnitsPerDay`.

Meaning:
- count newly activated Recognition anchor **units**, not directional cards;
- one vocabulary/script concept counts once even when Production/Listening are enabled;
- due existing Reviews always take priority.

The current stored value migrates 1:1. Do not keep both semantics at runtime.

Daily counting uses the profile's existing regional/time-zone setting, not UTC calendar day and not a Learning-specific time-zone setting.

## 6. Directional mode staging

Recognition is the anchor.

When a unit enters Learning:
- Recognition enters the new-unit queue;
- enabled secondary cards may exist but are not all treated as new units.

Production/Listening/Writing become eligible after the Recognition card records its first successful scheduled recall:
- Hard;
- Good;
- Easy.

Again does not unlock harder secondary modes.

Secondary modes keep independent FSRS history once activated and do not consume another `NewUnitsPerDay` slot.

If a mode is enabled later:
- Known unit stays Known;
- Learning unit with successful Recognition history can queue the new mode;
- otherwise it waits for successful Recognition.

## 7. Queue priority

Review start selects, in order:

1. already-started due cards, oldest due first;
2. due unlocked secondary-mode cards;
3. new Recognition units only when the Review batch and daily new-unit budget have space.

Stable tie-breakers keep ordering deterministic.

`ReviewBatchSize` remains the maximum cards in one Review session, not a daily limit.

## 8. Bounded session snapshot

A Review session takes a snapshot of due cards at start.

Ratings during the session may:
- schedule an Again card minutes later;
- unlock a secondary mode.

Those cards are not inserted into the already-running snapshot.

They can appear in the next Review session. This prevents endless same-session loops and keeps session progress stable.

## 9. Review presentation

The existing `LearningCardMode` remains authoritative:
- Recognition;
- Production;
- Listening;
- Writing.

The renderer may vary compatible presentation/context but must not create extra scheduled cards merely for visual variety.

Handwriting is not implied by Writing.

## 10. Explicit FSRS rating

Scheduled Review uses:
- Again;
- Hard;
- Good;
- Easy.

The rating UI appears only after answer/reveal.

Objective correctness can provide feedback and may visually suggest a rating, but V1 stores the explicit supported Review rating rather than a hidden second difficulty scale.

## 11. Review persistence

Keep `LearningCardReview` as the only durable scheduled-review history.

Evolve it with optional execution metadata instead of creating a parallel ReviewAttempt table:

```text
LearningCardReview
- existing Id/ProfileId/CardId/Rating/ClientEventId/ReviewedAt/NextReviewAt
- LearningSessionId     BIGINT FK
- WasCorrect            BOOLEAN nullable
- UsedHint              BOOLEAN
- ResponseDurationMs    BIGINT nullable
```

All new Review submissions use `ClientEventId` and the existing unique idempotency boundary.

`WasCorrect = null` is valid for reveal/self-rated Reviews.

## 12. Review metrics

A universal Review-success metric is:

```text
(Hard + Good + Easy) / all rated Reviews
```

Again is failed recall for that metric.

Objective accuracy is separate and uses only Review rows where `WasCorrect` is known.

Do not silently mix these two definitions.

## 13. Again

Again follows FSRS.

V1 does not force immediate repetition inside the current session snapshot. The card remains/returns due according to FSRS and can appear later.

## 14. Learning Practice does not fake Reviews

Lesson/Kana acquisition can:
- record practice/activity outcome;
- queue a unit into Learning;
- identify weak items.

It does not create a `LearningCardReview` merely because an acquisition-practice question was answered.

This keeps:
- Lesson/Kana = acquisition/practice;
- Review = scheduled spacing.

## 15. Script Trainer alignment

Target Kana behavior:
- group practice records practice outcome;
- first independent retrieval queues the Script unit;
- ordinary group practice does not call FSRS Review;
- due Kana cards appear in normal Review;
- Practice Difficult Symbols is Extra Practice and leaves due dates unchanged.

The current direct Kana-practice-to-`ReviewAsync` coupling must be replaced when this policy is implemented; do not keep both behaviors.

## 16. Vocabulary and media alignment

Vocabulary:
- Save -> Saved;
- Start Learning -> queue unit;
- Known/Ignore/Suspend -> explicit existing transitions.

Player/Reader:
- lookup only -> no state change;
- Save -> Saved;
- Start Learning -> queue;
- encountering a word while consuming media is not itself an FSRS Review.

A due card may use media context, but the card remains the reviewed identity.

## 17. Sentence Practice

Sentence Practice uses `LearningContext` and canonical units.

Normal Sentence Practice is Learning/Extra Practice and does not create another sentence scheduler.

When a due card is intentionally rendered with sentence context inside Review, exactly one Review event is written for that due card.

## 18. Extra Practice

Extra Practice may target:
- Lesson mistakes;
- Kana;
- Vocabulary;
- media contexts.

Rules:
- clearly distinct from Due Review;
- no due-date mutation;
- no clearing the due queue;
- activity can still contribute to non-FSRS statistics under the gamification/activity policy.

If an item is due, normal Reviews remain the preferred path.

## 19. Desired retention and Known

Existing desired-retention preference remains an Advanced FSRS setting only.

It does not affect:
- Lesson completion;
- course percentage;
- XP;
- Known state.

FSRS interval length never automatically changes a unit to Known.

## 20. Mode toggles

Turning a mode off:
- preserves cards/history;
- removes it from scheduling.

Turning it on:
- preserves old history;
- applies the Recognition staging rule if that mode has never started.

Do not delete history on toggle.

## 21. Exit/failure/idempotency

Rated Reviews commit individually.

Exiting a session:
- keeps completed Reviews;
- leaves unrated cards due.

Network retry with the same ClientEventId must not schedule twice.

A failed persistence operation must not advance the UI as if the Review succeeded.

## 22. Service ownership

Preserve clear owners:
- `IReviewScheduler/FsrsReviewScheduler`: FSRS math;
- Learning application owner: queue/lifecycle/review transaction;
- Learning course persistence owner: cards/units/history access.

Do not put scheduler rules into Razor/JavaScript and do not introduce another scheduler abstraction for Lessons/Kana.

## 23. Query shape

Review start should batch due cards, variants, histories and relevant context.

No query-per-card N+1 path.

New-unit activation should be set-based and deterministic.

## 24. Migration

Implementation must deliberately migrate:
- `NewWordsPerDay -> NewUnitsPerDay`;
- card activation to unit-based daily pacing;
- secondary mode staging;
- optional Review execution metadata.

Existing card/review history and current FSRS schedules must survive.

No legacy/new scheduler semantics side by side after migration.

## 25. Tests required

- new-unit limit counts units, not modes;
- secondary modes unlock after Hard/Good/Easy, not Again;
- due Reviews outrank new units;
- profile-local day boundary;
- Lesson/Kana practice creates no Review row;
- Extra Practice leaves NextReviewAt unchanged;
- ClientEventId prevents duplicate Review;
- mode toggle preserves history;
- migration preserves current history/preferences.

## 26. Must not implement

- No second scheduler.
- No Lesson/Kana fake Review rows.
- No automatic Save -> Learning from lookup.
- No mode multiplication of the daily new-unit limit.
- No new content ahead of a full overdue batch.
- No same-session endless Again loop.
- No Extra Practice due-date change.
- No automatic Known from FSRS.
- No Learning-only time zone.
- No history deletion on mode toggle.
- No parallel old/new queue semantics.

## 27. Acceptance

The policy is complete when one FSRS history remains authoritative, acquisition is distinct from due Review, daily pacing is unit-based, secondary modes enter progressively, sessions remain bounded/idempotent, Extra Practice cannot distort spacing, and every Learning surface reuses the same units/cards instead of creating another scheduler.
