# Universal Learning languages

Issue #252 replaced the original Japanese → German Term/UserTerm/Review learning state with language-neutral Learning courses and directional cards.

## Separate concepts

These values are independent:

- UI locale: which language AniLingo itself uses.
- content language: language of the watched/read source.
- course source language: the language that is read, heard or written in a Learning course.
- course target language: the other side of the course, used for meanings and prompts.
- card mode: what the learner must recognize or produce.

A German UI can therefore host Japanese → Indonesian and German → Indonesian courses at the same time.

## Canonical data model

Per-profile learning state has exactly one store: `LearningCards` plus `LearningCardReviews`. Nothing else holds Saved/Learning/Known state, intervals, due times or review history.

`LearningUnit` represents one concept/sense. It is not tied to one language. Units created from catalog words keep a link to their `Term` through `LearningUnit.TermId` (unique).

`LearningVariant` stores one language-specific representation of a unit: language tag, text, optional reading, role (`Primary` or `Meaning`) and provenance (`Term`, `Dictionary`, `ScriptCatalog`, `Manual`).

`LearningCourse` is profile-scoped and stores a BCP-47 source/target pair plus enabled practice modes. At most one course per profile and source language is *primary*; catalog words from content in that language are saved into it. The first course created for a source language becomes primary; Settings → Learning → Courses can switch it.

`LearningCard` is directional: prompt language, answer language and mode are explicit, and each card has its own state, interval, due time, queue position and FSRS history. The unique key is course + unit + mode.

`LearningContext` is source-agnostic. It stores `SourceType`, `SourceKey`, optional `PositionKey`, language and text, so a unit can point at an Anime timestamp, Novel/Book paragraph or Manga page/region without making the scheduler media-aware.

### Terms are the lexical catalog, not learning state

`Terms` and `EpisodeTerms` remain the profile-independent subtitle/dictionary catalog: canonical form, reading, the bundled dictionary gloss (`Term.Meaning`, language `Term.MeaningLanguage` = `de`) and per-episode occurrences. They carry no per-profile state.

When a profile acts on a catalog word (Episode Known/Learn, native client API, vocabulary preparation), `LearningService` resolves the profile's primary course for the term language, creates or reuses the term's unit (source variant from the term, meaning variant from the dictionary gloss) and applies the state to every card of that unit in the course.

The "state of a term" shown on content surfaces (preparation coverage, subtitle word colouring, Home/Anime coverage, native client coverage) is the Recognition card of the term's unit in the primary course for the term language: `LearningQueries.TermStates`.

## Practice modes and card existence

- Recognition: source prompt → target answer. Always exists for every unit in a course; it is the unit's anchor card and carries its word state.
- Production: target prompt → source answer. Exists while the course enables it and the unit has a target-language variant to prompt with.
- Listening: source-language audio (on-device TTS) → answer. Exists while the course enables it.
- Writing: target prompt → typed source answer. Exists while the course enables it and a target variant exists.

Enabling a mode creates the missing cards for every unit already in the course: they copy the anchor's Saved/Ignored/Suspended state, and anchors that are Learning or Known queue the new card as new learning, so the daily new-card limit applies. Disabling a mode (or the whole course) keeps the cards and their review history but removes them from scheduling, due counts and review sessions (`LearningQueries.ScheduledCards`).

Direction is never inferred from UI locale.

## BCP-47

Course languages are normalized culture/BCP-47 tags such as `ja`, `de`, `id`, `ro`, `de-DE`, `pt-BR`, `zh-Hant` or `ja-Latn`.

Do not add enum-based lists of supported languages to the core model. Specialized language toolkits advertise enhanced capabilities, but a generic course is not blocked merely because no special toolkit exists.

`LearningLanguageToolkitRegistry` returns the specialized Japanese toolkit for `ja` and a generic toolkit for every other valid language tag.

## Kana

Kana practice uses the same model: every Kana symbol is a `Script` unit with a stable ID (`KanaCatalog.IdFor`), a `ja` variant and a `ja-Latn` romaji variant. A profile practices them in its non-primary `ja → ja-Latn` course named "Kana", created on first practice, so Japanese words are never saved into it implicitly. Kana cards are regular cards and appear in Reviews while that course is enabled.

## Reviews and offline sync

FSRS replays each card's own review history (`LearningCardReviews`). The Review page, the offline review queue in the browser and the sync endpoint address cards by `CardId`.

Offline review events are idempotent per profile through the unique `(ProfileId, ClientEventId)` index. An event that carries only a `TermId` (queued by a browser before the upgrade) rates the Recognition card of that term in the profile's primary course.

The native client API `/api/client/v1` keeps its term-based contracts: term detail and `PUT /terms/{termId}/state` resolve through the same catalog-word path as the web UI.

## One-time conversion of legacy learning state

Databases created before this change stored learning state in `UserTerms`/`Reviews` keyed to `Terms`. The upgrade converts it exactly once at a migration boundary:

1. `20260925203500_AddUniversalLearningCourses` creates the Learning tables.
2. While `20260925204500_RetireLegacyLearningState` is still pending, `DatabaseMigrationBridge` migrates up to step 1 and copies the legacy state in one transaction with deterministic IDs (card = UserTerm ID, unit = Term ID, review = Review ID), so an interrupted upgrade can be retried.
3. `RetireLegacyLearningState` drops `UserTerms` and `Reviews`. It refuses to run (and rolls back) if any UserTerm was not converted, so applying migrations without the bridge can never discard progress.

Mapping:

- every learned Term becomes a `Word` unit with a source variant (canonical form + reading) and, when present, a `de` meaning variant;
- every profile gets a primary `<term language> → de` Recognition course for the languages it studied;
- every UserTerm becomes that course's Recognition card with the same state, interval, due time, learning start and queue position;
- every Review becomes a card review with the same ID, rating, times and offline `ClientEventId`; reviews without a UserTerm had no schedulable card and are not carried over;
- legacy Kana Terms (language `ja-kana`) become `Script` units in the profile's Kana course and are removed from the word catalog.

Data of the pre-account `default` profile is moved to the owner once the owner account exists; owner data wins where both profiles hold the same course pair and card.

After the conversion there is no runtime read or write path to the legacy tables.
