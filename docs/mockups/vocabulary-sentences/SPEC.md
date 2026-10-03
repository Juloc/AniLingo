# Vocabulary & Sentences — V1 Learning Library

Status: **binding V1 planning specification**. The approved Original Jularr Desktop + Mobile mockup is the visual reference. Text wins over imagery when data/behavior semantics conflict.

Global UX: `docs/UX.md`  
Learning architecture: `docs/LEARNING_V2.md`  
Universal language model: `docs/LEARNING_LANGUAGES.md`  
Learning Home: `docs/mockups/learning-home/SPEC.md`  
Lesson / Review: `docs/mockups/lesson-review/SPEC.md`  
Original J mascot: `docs/assets/original-j/jularr-mascot-reference.png`

## 1. Purpose

Vocabulary & Sentences is one focused Learning Library surface with two tabs:

- **Vocabulary**
- **Sentences**

Learning Home may still expose separate Vocabulary and Sentences actions. They open the same shared surface with the relevant tab selected.

The shared shell exists because both views need:
- course/language context;
- search/filtering;
- media/source context;
- practice entry points;
- shared language inspection/audio behavior;
- responsive list/detail interaction.

This does **not** merge their domain semantics.

## 2. Canonical data ownership

Vocabulary is a projection over the existing Learning domain:
- `LearningUnit`;
- `LearningVariant`;
- directional `LearningCard` state;
- `LearningCardReview` scheduling/history;
- `LearningContext` source anchors;
- lexical `Term` catalog where applicable.

The Recognition card remains the anchor state shown for a vocabulary unit.

Vocabulary state lifecycle remains:

```text
Untracked -> Saved -> Learning -> Known
                  \-> Ignored
Learning <-> Suspended
```

Rules:
- `Saved` is tracked but not scheduled;
- `Learning` participates in review scheduling;
- `Known`, `Ignored` and `Suspended` retain the canonical meanings defined by Learning architecture;
- this surface never introduces a second word-state store.

### Sentences are not a second persistence model

V1 does **not** introduce a new `SavedSentence` table/entity merely to support this UI.

The Sentences tab is a view/practice surface over:
- canonical `LearningContext` text/context;
- existing Sentence Practice candidate generation;
- canonical media source anchors;
- cached explanation/translation where capability permits;
- LearningUnits/cards referenced by that context.

If Jularr later adds independent sentence bookmarking/favorites, that requires a separately specified canonical contract and migration. Do not silently turn `LearningContext` into a generic notes/favorites database.

## 3. Capability gates

The shared surface itself appears only when Learning is available for the profile.

Tab visibility:
- Vocabulary requires `Vocabulary`;
- Sentences requires `SentencePractice`.

If only one capability resolves:
- show only that tab;
- do not display a dead/disabled second tab merely for symmetry.

State actions:
- Save / Known / Ignore require Vocabulary;
- Learn / Suspend / Resume require Vocabulary + Reviews;
- Listen requires actual speech/TTS capability;
- AI explanation is optional and never required for this surface.

Instance module, authorization, profile module preference and detailed Learning capability resolution all remain authoritative.

## 4. Desktop approved composition

Wide Desktop:

```text
<- Learning

Vocabulary & Sentences
[ Vocabulary ] [ Sentences ]

[ Search ] [ Course ] [ Status ] [ Source ] [ Sort ] [ view ]

VOCABULARY LIST                         DETAIL PANEL
[ row ]                                 selected word
[ row ]                                 reading / meaning
[ row ]                                 Listen
[ row ]                                 state actions
[ row ]                                 Details / Examples /
...                                     Contexts / Review
```

Rules:
- list is primary;
- detail panel is secondary and appears only when an item is selected;
- detail panel can remain open while moving through items;
- selecting another row updates the panel without a full-page navigation;
- closing the panel restores the full list width;
- no dashboard/statistics widgets on this page.

## 5. Vocabulary list

Each row may show:
- selection checkbox only if a real batch action exists;
- optional contextual thumbnail;
- source-language word;
- reading/transliteration where supported;
- concise meaning;
- state;
- due/next-review summary where relevant;
- source type/context summary;
- chevron/open action.

The mockup's thumbnail is contextual media artwork, not a required image for every vocabulary item.

Do not add visual clutter for every directional card. Secondary card modes belong in detail or compact metadata when needed.

## 6. Search and filters

Baseline controls:
- Search;
- Course;
- State;
- Source;
- Sort.

Search can match:
- source text;
- reading/transliteration;
- target/meaning variants where supported.

State filter can expose canonical states that the current capability set permits:
- All;
- Saved;
- Learning;
- Known;
- Ignored;
- Suspended when Reviews is available.

Course filter uses real LearningCourse identity/language direction.

Source filter may include:
- Anime;
- Book;
- Novel/LN;
- Manga;
- Course/manual context;
- other canonical LearningContext sources as implemented.

No provider-specific filters.

## 7. Sorting

Useful V1 sort options:
- Recently added/updated;
- Due first;
- Alphabetical/source-language order where meaningful;
- State;
- Course.

Do not invent a popularity/ranking score.

Large lists require server-side paging/cursoring or virtualization; never load the entire vocabulary history merely to filter in-browser.

## 8. Vocabulary detail panel

The approved detail panel may contain:

### Identity
- primary source-language form;
- reading/transliteration when toolkit supports it;
- target/meaning variant(s);
- part of speech if canonical lexical metadata provides it;
- Listen.

### State
Canonical actions:
- Saved;
- Learn / Resume;
- Known;
- Ignore;
- Suspend when currently Learning and Reviews is enabled.

The UI presents valid transitions, not every state as an always-clickable segmented control.

### Details
- written forms/variants;
- readings;
- meanings/senses;
- language tags where ambiguity exists.

### Examples
Examples may come from:
- recorded LearningContexts;
- course-authored examples;
- other explicitly supported canonical sources.

### Contexts
Show where this profile encountered the unit:
- Work title;
- episode/chapter/page;
- position/time;
- excerpt;
- open-source action.

Do not duplicate Work/Episode/Chapter identity.

### Review
When Reviews is enabled:
- due/next-review summary;
- active modes;
- Practice/Review action.

Do not expose raw FSRS parameters or formulas here.

## 9. Context source behavior

`LearningContext` remains source-agnostic.

A context can resolve to:
- Anime timestamp;
- Novel/Book paragraph;
- Manga page/region;
- other canonical content anchors.

Selecting a context may open the canonical source at its position if:
- the user can access that content;
- the target still resolves.

Missing source media does not delete the LearningUnit/card. Show a compact unavailable context state.

## 10. Audio

Audio/Listen:
- uses the shared speech/TTS contract;
- supports source-language pronunciation;
- may offer normal/slow rate where useful.

If TTS/speech is unavailable:
- hide/disable Listen locally;
- rest of the detail remains functional.

No page-specific TTS implementation.

## 11. Practice action

`Practice` / `Üben` opens the existing Review/Sentence Practice flow with a supported scoped target.

Rules:
- do not create a second practice engine;
- do not directly mutate FSRS merely from opening Practice;
- the scheduler still owns due reviews;
- optional extra practice must be explicit and must not fake a due review.

## 12. Sentences tab

The Sentences tab is a browsing/practice entry over sentence candidates and canonical contexts.

A row/card may show:
- source sentence;
- reading/transliteration where available;
- translation/meaning only when the current mode/view is allowed to reveal it;
- source Work and episode/chapter/location;
- target LearningUnit(s) contained in the sentence;
- Listen;
- Practice;
- open source context.

The tab may provide practice modes:
- Cloze;
- Comprehension;
- Listening where source audio/TTS is available.

Those remain the existing Sentence Practice modes.

### Reveal semantics

A practice/review prompt must not reveal its own answer in the browsing card before the learner chooses to reveal/start practice.

Browsing mode may show translated meaning as reference if it is not currently acting as an assessment prompt.

Do not mix "library browsing" and "active test" semantics ambiguously.

## 13. Shared Language Inspector

Words inside sentence/context text may open the shared Language Inspector when capability permits.

Inspector remains the canonical place for:
- lookup;
- reading;
- meaning;
- optional explanation;
- Save / Learn / Known / Ignore.

Do not rebuild a second word inspector inside the Sentences tab.

## 14. Notes

The exploratory mockup contains a `Notes` tab in the detail panel.

For V1, **Notes are not required** unless a canonical annotation/note contract is separately specified.

Do not persist ad-hoc free-text notes directly onto LearningCard, LearningUnit or LearningContext merely to match the mockup.

If Notes are absent, the detail tabs are:
- Details;
- Examples;
- Contexts;
- Review where available.

## 15. Batch selection

The checkbox column from the approved mockup is optional.

Only show multi-select if real batch operations exist, such as:
- mark selected Known;
- Ignore selected;
- Start Learning selected;
- Suspend selected.

Batch actions must use the same canonical transition service as single-item actions.

If no safe batch action is implemented, remove checkboxes entirely.

## 16. Original J visual direction

Original Jularr uses:
- warm cream/paper surfaces;
- restrained ink/watercolor scenery;
- sakura decoration around edges/header;
- red/pink accent;
- contextual media thumbnails;
- canonical mascot only where a companion is intentionally useful.

Canonical mascot:
`docs/assets/original-j/jularr-mascot-reference.png`

For this dense working surface:
- mascot belongs primarily in the header, empty state or detail hero;
- do not repeat the mascot beside every row;
- do not let decorative artwork reduce list density/readability.

Clean uses the exact same layout/data/actions with neutral surfaces and purple accent.

## 17. Spoiler / answer-clue rule

This screen follows the same Learning illustration rule as Lesson/Review:
- decorative art must not reveal a quiz/review answer;
- semantic object imagery is allowed in browsing/reference context only when it is not functioning as an assessment clue;
- active Review/Practice uses the stricter Lesson/Review rules.

## 18. Mobile approved composition

Mobile:

```text
Vocabulary & Sentences
[ Vocabulary | Sentences ]

[ Search ] [ Filter ]

[ vocabulary row ]
[ vocabulary row ]
[ vocabulary row ]

selected item ->
[ bottom/full-height detail sheet ]
word / reading / meaning
Listen
state actions
Details / Examples / Contexts / Review
[ Practice ]
```

Rules:
- one column;
- no permanent desktop split panel;
- selected item opens a bottom sheet/full-height sheet;
- sheet can expand for contexts/examples;
- filters use a compact filter sheet;
- large tap targets;
- list position is preserved when detail closes;
- no desktop table squeezed onto phone.

## 19. Tablet

Portrait follows Mobile with wider sheets.

Landscape may use:
- list + detail split view;
- same Desktop semantic hierarchy;
- touch-sized actions;
- no hover dependency.

## 20. Empty states

### No vocabulary yet
Show:
- concise explanation;
- ways to add words from Player/Reader/Language Inspector;
- optional open media/continue learning action.

Do not fabricate example words into the user's real list.

### No sentence candidates
Show:
- concise explanation;
- suggest learning/saving words or using media;
- allow another Sentence Practice mode if candidates exist there.

### Filtered empty
Show:
- clear filters;
- no generic onboarding copy.

Original J may use the canonical mascot in an appropriate focused/thinking pose.

## 21. Loading / error / partial

Loading:
- stable header/filter skeleton;
- row/detail skeletons;
- no fake terms/meanings/due dates.

Partial:
- list usable if one context/artwork lookup fails;
- failed thumbnail does not fail the row;
- unavailable TTS affects Listen only;
- unavailable AI affects explanation only.

Error:
- section-level retry where safe;
- no raw exception/provider internals.

## 22. Accessibility

- semantic tabs;
- keyboard navigation between list and detail;
- selected row announced;
- status not color-only;
- source/target text uses language tags;
- audio controls labeled;
- detail sheet traps/restores focus correctly;
- filters accessible without hover;
- >=44px touch targets.

## 23. Data/application contract

Vocabulary list/detail requires at least:
- CourseId;
- UnitId;
- primary Recognition card state;
- supported directional card summaries;
- source/target language tags;
- LearningVariants;
- due/next-review summary;
- LearningContexts;
- resolvable source metadata;
- effective capabilities.

Sentences requires:
- SentencePractice candidate/context identity;
- source sentence/language;
- target LearningUnit(s);
- source anchor;
- optional cached explanation/translation;
- supported practice modes;
- effective capabilities.

UI must not expose EF entities directly.

## 24. Route migration / compatibility

Current V1 implementation already has:
- `/Learn/Vocabulary`;
- `/Learn/Sentences`.

The approved UX may converge them into one shared shell while retaining those routes as:
- tab-specific deep links;
- compatibility redirects;
- route aliases.

Do not duplicate backend stores/services merely because two historic routes exist.

## 25. Required visual references

Approved:
1. **Desktop Original Jularr Light — Vocabulary list + word detail**
2. **Mobile Original Jularr Light — Vocabulary list + detail sheet**

Still useful:
3. Sentences tab;
4. filtered/empty state;
5. Dark validation;
6. Clean parity;
7. Tablet landscape split view.

The approved image is a visual reference; this text specification remains authoritative.

## 26. Must not implement

- No second vocabulary store.
- No second word-state lifecycle.
- No new `SavedSentence` persistence solely for this UI.
- No duplicate Language Inspector.
- No duplicate Review/Practice scheduler.
- No raw FSRS controls.
- No provider/debug metadata.
- No required Notes tab without a canonical note contract.
- No batch checkboxes without real batch actions.
- No Japanese-only core assumptions.
- No theme-specific Learning behavior.
- No alternate Original J mascot identity.
- No answer-spoiling imagery during assessment.

## 27. Acceptance

Vocabulary & Sentences is complete only when:
- Vocabulary and Sentences share one coherent shell without merging their domain semantics;
- Vocabulary reads/writes canonical LearningUnit/Variant/Card state;
- sentence browsing/practice reuses canonical LearningContext/Sentence Practice data;
- current historic routes can deep-link into the appropriate tab;
- filters/search scale beyond small datasets;
- word detail exposes useful variants, contexts and valid state transitions;
- no duplicate SRS/state stores exist;
- Mobile uses a sheet rather than squeezed Desktop split view;
- Original J and Clean remain skins over identical behavior;
- the canonical mascot is used whenever Original J intentionally shows a mascot.
