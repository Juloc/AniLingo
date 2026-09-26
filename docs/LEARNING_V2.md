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

## Migration behavior

Profiles that already contained legacy UserTerms, Reviews or LearningPreferences were seeded to Study during the Learning v2 settings migration so existing learning progress and behavior are preserved.

Profiles without historic learning state have no profile mode row and resolve to Off. This makes Learning opt-in for new users.

LearningPreferences (FSRS retention, batch size, new cards per day) and the per-card review history in LearningCardReviews are canonical for scheduling. Learning v2 scope settings decide visibility/availability; they do not discard scheduling data.

## Learning state

Per-profile learning state lives only in directional Learning cards inside Learning courses. See [LEARNING_LANGUAGES.md](LEARNING_LANGUAGES.md) for the course/unit/card model and the one-time conversion of the former Term/UserTerm/Review state.