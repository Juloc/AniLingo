# Jularr Learning Pedagogy — V1

Status: **binding product and behavior specification** for structured Learning. This document defines how Jularr teaches and practices material. Screen layout remains owned by the relevant mockup specifications.

Related:
- `docs/LEARNING_V2.md`
- `docs/LEARNING_LANGUAGES.md`
- `docs/LEARNING_EXERCISES.md`
- `docs/LEARNING_PROGRESS.md`
- `docs/LEARNING_PRACTICE_REVIEW.md`
- `docs/LEARNING_GAMIFICATION.md`
- `docs/mockups/learning-home/SPEC.md`
- `docs/mockups/course-detail/SPEC.md`
- `docs/mockups/lesson-review/SPEC.md`
- `docs/mockups/vocabulary-sentences/SPEC.md`
- `docs/mockups/script-trainer/SPEC.md`
- `docs/mockups/progress-achievements/SPEC.md`

## 1. Goal

V1 Learning should produce a coherent cycle:

```text
Understand
  -> Guided Practice
  -> Independent Retrieval
  -> Corrective Feedback
  -> Apply / Transfer
  -> Spaced Review later
```

Jularr is not a flashcard app with lessons wrapped around it, and it is not a passive reading course with occasional quizzes.

Structured courses, media-derived learning and FSRS Reviews are complementary parts of one Learning system.

## 2. Evidence-informed product principles

V1 follows these durable principles:

1. **Teach before demanding recall.** New or difficult material receives a concise explanation/example before independent retrieval.
2. **Retrieval matters.** Learners should actively recall/produce information rather than only reread or recognize it.
3. **Corrective feedback follows meaningful attempts.** Objective exercises normally receive immediate feedback.
4. **Spacing belongs to Reviews.** Long-term retention is handled by canonical LearningCards/FSRS rather than repeated massed drilling inside one Lesson.
5. **Blocked introduction, mixed later practice.** New related items can first be introduced in a small coherent group; later retrieval may mix items, contexts and exercise forms.
6. **Context matters.** Vocabulary and concepts should increasingly reappear in sentences, reading, listening and real media context rather than only isolated cards.
7. **Input and output both matter.** V1 supports reading/listening input and bounded recall/production; later phases may add richer speaking/writing.
8. **No fake difficulty.** Confusing wording, trick distractors and hidden answer rules are not desirable difficulty.
9. **Errors are information.** Incorrect answers drive feedback and later practice; they are not punishment.
10. **The learner stays in control.** No forced time pressure, no endless retry loops and no automatic next Lesson unless explicitly configured.

## 3. Lesson role

A Lesson introduces or develops authored course material.

It is not:
- a due-review queue;
- a random collection of LearningCards;
- an exam;
- a long article followed by one quiz.

A Lesson has one or more explicit learning objectives.

Examples:
- recognize and use the topic particle `は`;
- understand five high-frequency place words;
- distinguish `き` from nearby Kana;
- understand a short everyday exchange.

Every required exercise must map to at least one authored Lesson objective or LearningUnit/concept.

## 4. Canonical Lesson flow

The default V1 Lesson flow is:

```text
1. Orient
2. Introduce
3. Example
4. Guided Practice
5. Independent Retrieval
6. Apply / Transfer
7. Checkpoint
8. Summary
```

Not every Lesson needs a visible screen for every phase. Adjacent phases may be combined when the content is simple.

### 4.1 Orient

Purpose:
- tell the learner what they are about to learn;
- activate relevant prior knowledge when useful.

Keep it short.

Do not front-load long theory.

### 4.2 Introduce

Present genuinely new content.

May contain:
- concise explanation;
- new word/concept/script symbol;
- pronunciation/audio;
- reading/transliteration;
- grammar rule;
- small visual when it does not reveal a later answer.

### 4.3 Example

Show the new item in a meaningful example.

Prefer:
- short natural sentence;
- small dialogue;
- media context already known to the learner;
- worked language example.

The example should demonstrate the target concept rather than introduce several unrelated new difficulties at once.

### 4.4 Guided Practice

The learner performs a low-risk task with support.

Possible support:
- constrained choices;
- visible reference;
- hint;
- partial sentence;
- highlighted target;
- replayable audio.

Guided Practice may allow one immediate correction/retry.

### 4.5 Independent Retrieval

The learner must recall or recognize the target without the teaching answer remaining visible.

Examples:
- meaning -> target word;
- target word -> meaning;
- cloze;
- listening -> choice;
- sentence ordering;
- short typed answer.

Do not present an image/text clue that effectively contains the answer.

### 4.6 Apply / Transfer

Use the target in a changed but related context.

Examples:
- different sentence;
- short reading;
- different speaker/audio;
- combine two concepts already introduced;
- media-derived sentence.

V1 transfer remains bounded and deterministically assessable. Free essays/conversation are later scope.

### 4.7 Checkpoint

A short mixed check across the current section.

Purpose:
- verify usable understanding;
- bring back earlier items after intervening material;
- avoid finishing a section with only immediate repetition.

Checkpoint is not a high-stakes exam.

### 4.8 Summary

Show:
- what was learned;
- important mistakes/weak items;
- what entered Learning/Review where applicable;
- concise next action.

The Summary does not reteach the entire Lesson.

## 5. Lesson sections

Longer Lessons split into small meaningful sections.

A section normally contains one coherent concept cluster rather than unrelated topics.

Example:

```text
Section A — Topic particle
  introduce
  example
  guided practice
  retrieval

Section B — Use in sentences
  example
  cloze
  reading/listening
  transfer

Checkpoint
Summary
```

Do not make every individual exercise its own section.

Do not create a Lesson that feels like an endless card stream.

## 6. New-material budget

V1 uses **small batches**, not a fixed universal number.

Each Lesson/section must declare or derive a new-material budget appropriate to:
- item complexity;
- learner level;
- exercise modes;
- language/toolkit;
- expected session duration.

Product defaults should favor short Lessons that can usually be completed in roughly **5–15 minutes**, but duration is a UX target, not a completion rule.

Rules:
- avoid introducing many unrelated new lexical items and a new grammar concept at the same time;
- split overloaded Lessons into sections/Lessons;
- new material can be revisited inside the same Lesson, but long-term retention is delegated to spaced Review;
- exact daily new-unit pacing remains scheduler/user policy, not Lesson layout policy.

## 7. Scaffolding and fading

Support should reduce as the learner demonstrates understanding.

Typical sequence:

```text
full example
-> guided choice
-> reduced cue
-> independent retrieval
-> changed context
```

Do not keep answer-equivalent cues visible during independent retrieval.

Do not remove all support before the learner has seen a usable example.

## 8. Hint ladder

Hints are allowed mainly in Lessons and optional extra practice.

A bounded hint ladder can progress from weaker to stronger help:

1. restate/rephrase instruction;
2. conceptual reminder;
3. reading/pronunciation replay or grammar cue;
4. eliminate one implausible option / reveal a small part;
5. reveal answer.

Rules:
- hints are explicit user actions unless accessibility requires otherwise;
- the system records that a hint/reveal was used when attempt semantics need it;
- revealing the answer means the attempt is **not independent retrieval**;
- Review keeps hints more restrictive because scheduler feedback must remain meaningful;
- no shame/punishment language for using hints.

## 9. Answer and feedback policy

### Correct first attempt

Show:
- clear correct state;
- concise explanation/example only if useful;
- Continue.

Do not force the learner to reread a long explanation after every easy correct answer.

### Incorrect first attempt

Show:
- what the learner selected/entered;
- the correct answer;
- concise explanation of the relevant distinction;
- optional comparison with a commonly confused item;
- Continue or one guided retry depending on step role.

### Guided Practice retry

Guided Practice may allow one immediate corrected retry because the purpose is supported acquisition.

### Independent Retrieval / Checkpoint

Do **not** require repeated immediate attempts until the answer becomes correct.

Preferred:
- record the first meaningful attempt;
- show corrective feedback;
- optionally re-present the target later after intervening items;
- continue the Lesson.

This avoids an artificial "keep clicking until green" completion rule.

## 10. Feedback content

Feedback should answer only what helps the learner understand the result.

Good feedback may include:
- correct form;
- short reason/rule;
- contrast with learner answer;
- reading/pronunciation;
- one useful example.

Avoid:
- generic `Wrong` with no correction;
- multi-paragraph lectures for simple mistakes;
- unrelated trivia;
- AI-generated speculation presented as authoritative;
- revealing unrelated future Lesson content.

AI explanations may enrich feedback only when enabled and available. Core feedback remains authored/deterministic.

## 11. Exercise difficulty

Difficulty should come from the learning task, not UI friction.

Valid progression:
- more independent recall;
- less scaffolding;
- more similar distractors when pedagogically justified;
- changed context;
- combine already learned concepts;
- listening without text;
- production instead of recognition.

Invalid difficulty:
- ambiguous prompts;
- obscure distractors;
- tiny click targets;
- misleading wording;
- hidden rules;
- time pressure by default.

## 12. Blocking and mixing

### During initial acquisition

Related new material may be blocked/grouped.

Examples:
- K-row Kana together;
- five place words;
- one grammar pattern with several examples.

### During later practice

Use increasing mixture across:
- previously learned units;
- contexts;
- exercise forms;
- Lesson origins.

Do not randomize blindly.

The practice renderer should avoid creating confusion before a distinction has been taught.

## 13. Retrieval form progression

Where capability supports it, a unit can progress from easier recognition toward stronger recall/production.

Example vocabulary path:

```text
source -> meaning recognition
-> source -> meaning recall
-> meaning -> source
-> sentence cloze
-> listening/context use
```

This is presentation/practice policy over canonical LearningUnits/Cards.

Do not create duplicate domain units solely for each visual exercise form.

## 14. Vocabulary pedagogy

Vocabulary should not live only as isolated word/translation pairs.

Initial introduction may show:
- form;
- reading/pronunciation;
- concise meaning;
- one clear example/context.

Later practice should increasingly use:
- sentence context;
- source media context;
- listening where supported;
- directional recall.

Avoid showing all dictionary senses at once to beginners unless the Lesson objective requires them.

A LearningUnit should represent the intended concept/sense rather than silently treating every homograph/polyseme as one thing.

## 15. Grammar / concept pedagogy

Grammar is taught in context first.

Preferred:
- short example;
- highlight relevant structure;
- concise rule;
- guided contrast;
- retrieval/application.

Do not make V1 depend on a separate giant Grammar encyclopedia.

Reference material can be added later without changing Lesson semantics.

## 16. Reading pedagogy

Reading tasks should progress from:
- sentence;
- short connected passage;
- comprehension;
- target-form noticing where useful.

Comprehension questions should test meaning, not merely copy an obvious phrase.

Language Inspector remains available where capability allows, but using it during an assessment may count as help/reveal depending on exercise policy.

## 17. Listening pedagogy

Listening can progress from:
- replayable word/phrase;
- short sentence;
- comprehension/selection;
- listening without visible transcript;
- reveal transcript after attempt.

Rules:
- replay is normally allowed in Lessons;
- number of replays may be recorded for analytics but should not be punitive;
- TTS/source-audio failure degrades only the listening task/mode;
- written answer must not appear before a listening-only attempt unless the exercise explicitly teaches rather than tests.

## 18. Script Trainer pedagogy

Script Trainer follows the same pattern:

```text
see symbol + reading/audio
-> guided recognition
-> independent recognition
-> reverse recognition
-> listening
-> later spaced review
```

Interactive handwriting is not required until real stroke/evaluation capability exists.

See `docs/mockups/script-trainer/SPEC.md`.

## 19. Media-derived learning

Media context is a first-class source of examples and practice.

Use:
- content the profile already encountered/captured;
- canonical LearningContext anchors;
- short excerpts sufficient for the task.

Media-derived practice should:
- reuse LearningUnits/Cards;
- avoid spoilers where possible;
- preserve source context;
- never create a parallel media-learning scheduler.

Structured course learning must still work without media.

## 20. Lesson completion

A normal V1 Lesson is completed when:
- all required authored steps have been meaningfully reached/attempted;
- required terminal checkpoint/summary conditions are satisfied;
- completion is explicitly persisted.

A Lesson does **not** require:
- 100% accuracy;
- every item becoming Known;
- no Again ratings;
- Daily Goal completion.

If a future course needs a mastery gate/exam threshold, that must be explicit course policy and not silently applied to ordinary Lessons.

Opening or partially viewing a Lesson never completes it.

## 21. Repetition inside a Lesson

Use limited within-Lesson reappearance for:
- missed items;
- especially important new units;
- discrimination between commonly confused items.

Rules:
- insert intervening material before reappearance where practical;
- avoid immediate endless repetition;
- do not overwrite the learner's original first-attempt outcome;
- do not treat within-Lesson repetition as a replacement for FSRS spacing.

## 22. Review role

Review answers the question **what is due for long-term retention now?**

FSRS owns:
- due state;
- scheduling;
- persistent review history.

The Review renderer owns:
- how the scheduled card is presented;
- compatible exercise form;
- feedback presentation.

Lesson pedagogy must never invent its own long-term review intervals.

## 23. Extra Practice role

Extra Practice is explicitly voluntary and distinct from Due Review.

It may:
- target Lesson mistakes;
- target difficult Vocabulary;
- practice a selected course/group/context.

It must not:
- label items `Due` when they are not due;
- create a second scheduler;
- silently move the FSRS due date merely because the learner opened extra practice.

The exact relationship between optional practice and scheduling is defined by `docs/LEARNING_PRACTICE_REVIEW.md`; Extra Practice does not change FSRS due dates.

## 24. Error tolerance and accepted answers

Objective exercises must define:
- correct answer(s);
- normalized accepted variants where appropriate;
- language-specific normalization through toolkit/application policy.

Do not mark semantically equivalent supported answers incorrect because of superficial formatting when the exercise is not testing that formatting.

Do not accept overly broad variants that make the exercise meaningless.

Detailed answer schema belongs to `docs/LEARNING_EXERCISES.md`.

## 25. Motivation without distortion

Use:
- clear progress;
- small wins;
- restrained XP when enabled;
- supportive feedback;
- visible continuation.

Do not use:
- lives/hearts that block learning;
- punishment for mistakes;
- XP curriculum locks;
- compulsory streak pressure;
- manipulative scarcity.

Gamification can be disabled without changing pedagogy.

## 26. Session length and stopping

Lessons and Reviews are designed for bounded sessions.

Learner can exit safely.

Rules:
- exact Lesson progress is preserved at safe boundaries;
- completed Reviews stay completed;
- unfinished Due cards remain in canonical scheduler state;
- stopping does not penalize progress or Streak;
- Daily Plan may recommend a next action but does not force it.

## 27. Accessibility and pedagogy

Accessibility support must not be treated as cheating.

Examples:
- screen reader;
- larger text;
- reduced motion;
- keyboard navigation;
- captions when the exercise is not specifically listening-only;
- replay controls.

When an exercise specifically assesses a capability, the UI may constrain aids that would reveal the answer, but should provide an alternative accessible exercise form where practical.

## 28. Authoring validation

A published Lesson should fail validation or emit an explicit authoring error when:
- it has no learning objective;
- a required exercise maps to no objective/content unit;
- an objective is introduced but never practiced;
- an assessed answer is undefined/ambiguous;
- an independent retrieval step still visibly exposes the answer;
- an audio-only exercise has no supported audio capability/fallback policy;
- a required exercise uses a future-only capability;
- the Lesson has no terminal completion point.

Advisory warnings may flag:
- too much new material in one section;
- long explanation blocks;
- excessive consecutive recognition-only exercises;
- no transfer/context exercise;
- too many immediate repetitions.

## 29. Telemetry / learning signals

V1 may record deterministic learning signals needed for product behavior:
- attempt;
- correctness;
- hint/reveal use;
- response duration where meaningful;
- exercise type;
- Lesson/section/step;
- associated LearningUnit/card/context.

These signals are not themselves mastery truth.

Do not create opaque AI mastery scores in V1.

The later Practice Policy may use these signals to select exercise form while FSRS remains the scheduling source of truth.

## 30. Phase boundaries

### Phase 1 — Learn & Retain

Includes:
- structured Lessons;
- bounded input/recognition/recall;
- corrective feedback;
- Vocabulary/Sentences;
- Script Trainer;
- FSRS Review;
- media-derived examples/context.

### Phase 2 — Understand & Transfer

Future:
- richer reading/listening comprehension;
- stronger deterministic production;
- adaptive practice selection;
- placement/knowledge checks;
- real handwriting evaluation.

### Phase 3 — Communicate

Future:
- Speaking/pronunciation assessment;
- free Writing feedback;
- conversation;
- AI Tutor;
- richer exam/proficiency systems.

Do not pull Phase 2/3 requirements into V1 implementation merely because the pedagogy leaves room for them.

## 31. Must not implement

- No Lesson made only of passive explanation.
- No Lesson made only of random flashcards.
- No testing of genuinely new content without teaching/context unless explicitly diagnostic.
- No answer-equivalent clues during independent retrieval.
- No repeated retry-until-green loop as the default.
- No 100%-accuracy requirement for normal Lesson completion.
- No Lesson-owned long-term scheduler.
- No separate media-learning scheduler.
- No punishment/lives system.
- No forced time pressure in normal V1.
- No AI dependency for core teaching/feedback.
- No fake handwriting/speaking assessment.
- No XP/Streak as pedagogy or mastery truth.

## 32. Acceptance

The V1 pedagogy is satisfied only when:
- every Lesson has explicit objectives;
- new material is taught before independent retrieval;
- examples/guidance fade into active retrieval;
- objective mistakes receive corrective feedback;
- later practice includes changed contexts rather than only immediate repetition;
- normal Lesson completion does not demand perfection;
- long-term spacing stays with canonical FSRS Review;
- media context reuses canonical LearningContext;
- gamification can disappear without changing learning behavior;
- Phase 2/3 capabilities are not required for a complete Phase 1.
