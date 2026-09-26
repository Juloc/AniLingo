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

`/Learn` is a dashboard, not a review screen. It lists modules and shows each one only when the profile-scope resolver enables the matching capability:

| Module | Capability |
| --- | --- |
| Reviews (`/Learn/Review`) | Reviews |
| Vocabulary (`/Learn/Vocabulary`) | Vocabulary |
| Sentences (`/Learn/Sentences`) | SentencePractice |
| Kana (`/Learn/Kana`) | ScriptTrainer (currently assumes a Japanese course; the universal course toolkit will make this per language) |
| Progress (`/Learn/Progress`) | Progress |

Historic vocabulary rows never re-surface a module the profile switched off. Each module page re-resolves its capability on GET and on every POST handler: a disabled module redirects to the hub, a disabled POST is refused.

The hub distinguishes three quiet states: Learning fully off, Language Tools only (lookup/readings/translation without study modules), and Learning enabled only at lower scopes.

## Vocabulary lifecycle

States are Untracked → Saved → Learning → Known, plus Ignored and Suspended. `LearningService.SetStateAsync` is the single transition path:

- Saved never schedules a review; it clears `NextReviewAt`, `LearningStartedAt` and the queue position.
- Learning enqueues the word; it only becomes due when `GetDueAsync` activates it within the "new words per day" and batch limits.
- Known, Ignored and Suspended clear the next review. Ignored also leaves the queue; Suspended keeps its history so Resume continues where it stopped.

The Vocabulary page offers Start learning, Suspend and Resume only when the Reviews capability resolves on, and refuses those transitions server-side otherwise. FSRS retention, batch size and the daily new-word limit stay under the Advanced section of `/Settings/Learning`.

## Home

Home leads with Continue Watching. The due-review shortcut and the due metric render only when HomeWidget resolves on at profile scope (off by default in every mode, including Study). Episode coverage captions render, and their vocabulary joins run, only when ContentMetrics resolves on for the Anime media type.

## Migration behavior

Profiles that already contain UserTerms, Reviews or LearningPreferences are seeded to Study during the migration so existing learning progress and behavior are preserved.

Profiles without historic learning state have no profile mode row and resolve to Off. This makes Learning opt-in for new users.

Existing FSRS LearningPreferences and Review history remain canonical for scheduling. Learning v2 scope settings decide visibility/availability; they do not discard scheduling data.

## Next slices

Issue #251 moves Learn, Reviews, Vocabulary, Sentences, Kana and Progress behind this resolver.

Issue #252 replaces Japanese-only Term/Meaning assumptions with universal language courses, variants and directional cards while preserving existing progress.