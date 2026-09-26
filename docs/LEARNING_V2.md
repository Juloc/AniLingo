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

Untracked words become Saved from the player, readers or episode pages (the Episode page offers Learn only when Reviews resolves on for the episode and refuses it server-side otherwise); the Vocabulary page lists tracked word cards per course. It offers Start learning, Suspend and Resume only when the Reviews capability resolves on, and refuses those transitions server-side otherwise. FSRS retention, batch size and the daily new-word limit stay under the Advanced section of `/Settings/Learning`.

## Language inspector

The Anime player, the readers (Novels, Books, later Manga/OCR) and Learning modules share one word/sentence inspector instead of page-specific learning panels. It consists of:

- `LanguageInspectorService` (Features/Learning/LanguageAssistance) behind `POST /api/language-inspector/{inspect|state|explain}` (signed-in profile, antiforgery header `RequestVerificationToken`);
- the partial `Pages/Shared/_LanguageInspector.cshtml`, rendered by a host with one line, e.g. `<partial name="_LanguageInspector" model="LanguageInspectorHost.ForBookChapter(workId, chapterId, language)" />`;
- `wwwroot/js/language-inspector.js`, which exposes `window.AniLingoLanguageInspector`.

Every call resolves the capabilities of the inspected source through `LearningModuleResolver.ResolveAssistanceAsync` (profile → media type → work → content). The server derives the work from the content, so the client cannot choose a scope.

| Inspector feature | Capability |
| --- | --- |
| Inspector at all | LanguageLookup, ReadingAids or AiExplanations; the host surface additionally needs PlayerTools (Anime) or ReaderTools (readers) |
| Dictionary form and meaning | LanguageLookup |
| Reading | ReadingAids |
| Sentence explanation | AiExplanations, Japanese text (the explainer contract in Features/Ai is Japanese-only); generated once, then served from `AiSentenceExplanationCache` |
| Save, Known, Ignore and the word's state | Vocabulary |
| Learn (queue for reviews) | Vocabulary and Reviews |

Language Tools therefore show readings, meanings and explanations without ever creating cards. Save/Learn/Known/Ignore go through `LearningService.SetStateAsync` and `LearningCardTransitions`. A word saved from any source joins the lexical `Terms` catalog (dictionary reading/meaning for Japanese), so subtitles, readers and Vocabulary see the same card state. Japanese uses the morphological analyzer and bundled dictionary; other languages use Unicode word boundaries without readings or dictionary meanings.

### Script contract

```js
const inspector = window.AniLingoLanguageInspector; // undefined when the page did not render the partial
inspector?.available;                               // false when the scope has no language tools
inspector?.open(text, context);                     // Promise<inspection | null>
inspector?.close();
inspector?.isOpen();
inspector?.bindSelection(surfaceElementOrSelector, { paragraphAttribute });
inspector?.speak(text, language, rate);
inspector?.addEventListener("open" | "close" | "statechange", handler);
```

`context` fields: `language`, `sourceType` (`anime`, `novel`, `book`, `manga`), `contentKey` (episode or chapter ID), `sentence`, and the position `cueStartMs` (anime), `paragraph` (novel/book) or `page` + `region` (manga). Missing fields fall back to the page context of the partial. Elements with `data-language-inspect="text"` inside a `data-language-context` element (carrying the same fields as `data-*` attributes) open the inspector declaratively; `data-language-speak` buttons play on-device TTS.

### Source contexts

Saving or learning a word from a source records one personal `LearningContext` per profile, unit, source and position (unique index; recording the same place again is a no-op):

| Source | SourceType | SourceKey | PositionKey |
| --- | --- | --- | --- |
| Anime | `anime` | `episode:{episodeId}` | `cue:{startMs}` |
| Novel | `novel` | `chapter:{chapterId}` | `paragraph:{index}` |
| Book | `book` | `chapter:{chapterId}` | `paragraph:{index}` |
| Manga | `manga` | `chapter:{chapterId}` | `page:{page}` or `page:{page}#region:{region}` |

## Sentence practice

Sentence practice (`/Learn/Sentences`, Features/Learning/Sentences) is its own module, independent of Kana. It prefers, in this order: sentences the profile recorded as contexts for Saved/Learning words (any source), anime subtitle sentences of Saved/Learning words from watched episodes, then unwatched episodes, then Known words; without tracked words it falls back to short subtitle sentences from watched episodes first. Modes: Cloze (the studied word is blanked), Comprehension (full sentence, reveal meaning and explanation) and Listening (anime sentences only: listen, then reveal). Page rendering never calls the AI provider; cached explanations are shown when AiExplanations is on. Words in practice sentences open the shared inspector.

## Home

Home leads with Continue Watching. The due-review shortcut and the due metric render only when both HomeWidget and Reviews resolve on at profile scope (HomeWidget is off by default in every mode, including Study). Episode coverage captions render, and their vocabulary joins run, only when ContentMetrics resolves on for the Anime media type.

## Migration behavior

Profiles that already contained legacy UserTerms, Reviews or LearningPreferences were seeded to Study during the Learning v2 settings migration so existing learning progress and behavior are preserved.

Profiles without historic learning state have no profile mode row and resolve to Off. This makes Learning opt-in for new users.

LearningPreferences (FSRS retention, batch size, new cards per day) and the per-card review history in LearningCardReviews are canonical for scheduling. Learning v2 scope settings decide visibility/availability; they do not discard scheduling data.

## Learning state

Per-profile learning state lives only in directional Learning cards inside Learning courses. See [LEARNING_LANGUAGES.md](LEARNING_LANGUAGES.md) for the course/unit/card model and the one-time conversion of the former Term/UserTerm/Review state.