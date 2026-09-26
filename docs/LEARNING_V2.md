# Learning v2 architecture

GitHub issue #226 is the umbrella backlog. This file describes durable architecture only; it is not a second backlog or AGENDA.

## Product rule

AniLingo is a Watch / Read / Discover application first. Learning is optional and must not leak into normal media UX when it is disabled.

Learning assistance and active study are separate:

- Off: no learning surfaces unless a lower scope explicitly opts in.
- Language Tools: lookup, readings, translation and optional explanations without review obligations.
- Study: vocabulary, reviews, sentence practice and progress in addition to language tools.
- Custom: no capabilities enabled by default; the user explicitly selects them.

Even Study keeps Home learning widgets, content learning metrics and pre-study suggestions off by default so the media experience is not dominated by learning.

## Scope inheritance

There is one canonical hierarchy:

1. profile
2. media type: Anime / Novel / Book / Manga
3. work or series
4. individual content such as episode/chapter/item

Each lower scope can override the Learning mode. No mode row means Inherit.

Capabilities are independently tri-state in the UI: Inherit, On or Off. In persistence, Inherit is represented by the absence of an override row.

Resolution works in two phases:

1. The nearest mode override in the hierarchy chooses the base capability set.
2. Explicit capability overrides are applied broad-to-specific.

This permits examples such as global Off with Study enabled for one Japanese book, or Study globally with sentence practice disabled only for Anime.

## Capabilities

The canonical capability set is:

- LanguageLookup
- ReadingAids
- Translation
- AiExplanations
- Vocabulary
- Reviews
- SentencePractice
- ScriptTrainer
- Progress
- HomeWidget
- ContentMetrics
- PreparationSuggestions
- PlayerTools
- ReaderTools

UI modules and content integrations must resolve these capabilities rather than inventing separate booleans.

## Learning Hub

`/Learn` is a dashboard, not a review screen. `LearningModuleResolver` (Features/Learning) decides which modules exist for a profile; the hub, the module pages and their POST handlers all consume it. Each module shows only when the profile-scope resolver enables the matching capability:

| Module | Capability |
| --- | --- |
| Reviews (`/Learn/Review`) | Reviews |
| Vocabulary (`/Learn/Vocabulary`) | Vocabulary |
| Sentences (`/Learn/Sentences`) | SentencePractice |
| Kana (`/Learn/Kana`) | ScriptTrainer **and** an enabled Learning course whose source language toolkit provides a script trainer (Japanese). Without one the hub shows a hint linking to Learning courses. |
| Progress (`/Learn/Progress`) | Progress |

Historic Learning cards never re-surface a module the profile switched off. Each module page re-resolves its capability on GET and on every POST handler: a disabled module redirects to the hub, a disabled POST is refused.

The hub distinguishes three quiet states: Learning fully off, Language Tools only (lookup/readings/translation without study modules), and Learning enabled only at lower scopes.

## Vocabulary lifecycle

States are Untracked → Saved → Learning → Known, plus Ignored and Suspended. They live on the directional `LearningCard`s of a unit (the Recognition card is the unit anchor shown on the Vocabulary page). `LearningService` (`SetStateAsync` for catalog words, `SetUnitStateAsync` for course units) applies them through `LearningCardTransitions`, the single transition path:

- Saved never schedules a review; it clears `NextReviewAt`, `LearningStartedAt` and the queue position.
- Learning enqueues the word; it only becomes due when `GetDueAsync` activates it within the "new words per day" and batch limits.
- Known, Ignored and Suspended clear the next review. Ignored also leaves the queue; Suspended keeps its history so Resume continues where it stopped.

Untracked words become Saved from the player, readers or episode pages; the Vocabulary page lists tracked word cards per course. It offers Start learning, Suspend and Resume only when the Reviews capability resolves on, and refuses those transitions server-side otherwise. FSRS retention, batch size and the daily new-word limit stay under the Advanced section of `/Settings/Learning`.

## Home

Home leads with Continue Watching. The due-review shortcut and the due metric render only when both HomeWidget and Reviews resolve on at profile scope (HomeWidget is off by default in every mode, including Study). Episode coverage captions render, and their vocabulary joins run, only when ContentMetrics resolves on for the Anime media type.

## Migration behavior

Profiles that already contained legacy UserTerms, Reviews or LearningPreferences were seeded to Study during the Learning v2 settings migration so existing learning progress and behavior are preserved.

Profiles without historic learning state have no profile mode row and resolve to Off. This makes Learning opt-in for new users.

LearningPreferences (FSRS retention, batch size, new cards per day) and the per-card review history in LearningCardReviews are canonical for scheduling. Learning v2 scope settings decide visibility/availability; they do not discard scheduling data.

## Learning state

Per-profile learning state lives only in directional Learning cards inside Learning courses. See [LEARNING_LANGUAGES.md](LEARNING_LANGUAGES.md) for the course/unit/card model and the one-time conversion of the former Term/UserTerm/Review state.