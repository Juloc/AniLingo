# Lesson / Review — V1 Learning Session Design

Status: **binding V1 planning specification for Lesson and Review mockups**. This defines the focused learning-session surfaces that pair with the V1 Learning Dashboard. It intentionally does not pull future Speaking/Writing/AI-Tutor/exam systems into the first delivery.

Binding pedagogy and Lesson sequencing: `docs/LEARNING_PEDAGOGY.md`. This screen spec defines presentation/interaction; pedagogy defines when content is introduced, guided, retrieved, corrected, transferred and summarized.

## 1. Product role

Lesson and Review are separate session types over the same canonical Learning domain.

**Lesson**
- teaches new structured course material;
- moves through an authored ordered sequence;
- may introduce vocabulary, grammar/concepts, reading/listening and practice;
- records exact lesson/step progress;
- awards XP for meaningful work and completion.

**Review**
- practices already scheduled LearningCards;
- uses the existing SRS/FSRS scheduler;
- may present different exercise forms for the same canonical unit/card;
- records the learner's review outcome;
- never creates a second scheduling system.

Both are distraction-minimized and resumable.

The approved Original Jularr visual direction uses the canonical Jularr mascot reference:
`docs/assets/original-j/jularr-mascot-reference.png`.

The mascot/illustration is a presentation layer only. It must never change Lesson/Review data, scoring, scheduling, exercise semantics or navigation.

## 2. Shared session shell

Shared structure:
- compact Back/Close;
- course/language/session title;
- progress indicator;
- optional Daily Goal/XP mini indicator;
- central exercise/content surface;
- feedback area;
- primary Next/Continue action;
- pause/exit with state preserved.

Do not show:
- full Learning dashboard navigation inside the exercise;
- admin curriculum metadata;
- FSRS internals;
- provider/model health;
- media acquisition details.

## 3. Lesson structure

The canonical pedagogical sequence is defined in `docs/LEARNING_PEDAGOGY.md`:

```text
Orient -> Introduce -> Example -> Guided Practice
-> Independent Retrieval -> Apply/Transfer -> Checkpoint -> Summary
```

The visible Lesson remains a mostly linear flow divided into meaningful sections. Simple content may combine adjacent phases; the UI must not invent a different teaching sequence.

Rules:
- learner normally proceeds forward;
- previous/review-current-section may be allowed;
- Lesson can be exited and resumed exactly;
- sections provide natural checkpoints;
- short section summaries prevent one giant endless card flow;
- no unnecessary route change between every exercise;
- ordinary Lesson completion does not require 100% accuracy or retry-until-green.

## 4. V1 Lesson step types

V1 should support a bounded generic exercise contract rather than many custom page types.

Required types:
- explanation / introduction;
- vocabulary presentation;
- grammar/concept explanation;
- reading passage;
- listening prompt;
- recognition / multiple choice;
- matching;
- cloze;
- sentence ordering;
- sentence building;
- typed short answer where reliable;
- translation where pedagogically appropriate;
- checkpoint / section summary.

Optional within V1 if existing capabilities already support them cleanly:
- dictation;
- kana/script input;
- media-context exercise.

Future only unless separately pulled forward:
- free-form Writing assessment;
- pronunciation/Speaking scoring;
- open-ended AI-evaluated answers.

## 5. Exercise card anatomy

Each exercise has:
- concise instruction;
- prompt/content;
- optional context;
- optional Listen/Replay;
- answer surface;
- Submit/Check;
- feedback;
- Continue.

Instructions must be short and stable in placement.

Avoid repeating long pedagogical explanations above every card.

### Illustration / companion rule

Original Jularr Lessons may use the canonical mascot as a visual companion:
- beside the exercise;
- in the header/background edge;
- presenting a lesson card;
- explaining a rule;
- encouraging after an answer;
- celebrating completion.

Illustration must stay subordinate to the exercise.

**Never use an illustration that reveals the correct answer unless the exercise explicitly tests image recognition.**

Examples:
- a neutral mascot beside `食べる` is allowed;
- a bowl of food beside a meaning-recall question for `食べる` is not allowed because it gives away the answer;
- a contextual scene is allowed only when that context is intentionally part of the exercise.

Review is stricter than Lesson:
- neutral mascot/atmosphere is allowed;
- direct object/action illustrations that provide a semantic clue are forbidden for recall/meaning questions;
- optional media context may appear only when the exercise intentionally includes that context.

Clean uses the same exercise layout/semantics without the Original J mascot by default.

## 6. Answer behavior

### Before submit
- primary action disabled until a valid answer exists where appropriate;
- keyboard/touch focus is obvious;
- no accidental answer from generic swipe/tap.

### Correct
Show:
- clear correct state;
- concise explanation only when useful;
- earned XP feedback;
- Continue.

### Incorrect
Show:
- learner's answer;
- expected/correct answer;
- concise explanation or relevant rule;
- optional Retry if lesson policy allows;
- Continue.

Do not use color alone for correct/incorrect.

Incorrect answers are learning data, not a reason to block the learner indefinitely.

## 7. Hints

V1 Lessons may provide bounded hints.

Examples:
- reveal reading;
- reveal first character/word;
- grammar reminder;
- replay audio;
- show one eliminated option.

Hint use may reduce an exercise XP bonus but does not erase core progress.

Reviews should keep hints more restricted so the FSRS result remains meaningful.

## 8. Lesson progress

Store separately:
- current Lesson;
- current Section;
- current Step;
- completed Step/Section state;
- attempts/results where required by the course;
- completion timestamp/state.

Lesson progress is **not** inferred from SRS mastery.

Exit/background/navigation flushes exact lesson progress.

Returning to the Lesson resumes the exact incomplete Step when safe.

## 9. Lesson section summary

At the end of a meaningful section show a small summary:
- section completed;
- exercises completed;
- accuracy;
- XP earned in section;
- key items introduced;
- Continue.

Do not turn every short section into a giant results ceremony.

## 10. Lesson completion summary

After final Step:

Show:
- **Lesson complete**;
- total XP;
- accuracy;
- time;
- new words/concepts introduced;
- items added to review/scheduled where applicable;
- course progress change;
- Daily Goal contribution;
- Next recommendation.

Primary actions:
- Continue to next Lesson when appropriate;
- Back to Learning Dashboard.

Optional:
- Review mistakes.

Do not auto-start the next Lesson without an explicit policy/user preference.

## 11. Review session purpose

Review is a focused queue of due canonical LearningCards.

V1 supports:
- due reviews;
- bounded session size;
- mixed vocabulary/sentence/script items according to enabled capabilities;
- optional media context where it helps recall.

The queue is scheduler-owned. UI does not reorder cards using its own hidden algorithm.

## 12. Review exercise variety

Review should feel more like Jularr Learning than a plain flashcard clone while preserving FSRS semantics.

Possible presentation types:
- recall from meaning -> target;
- recognition target -> meaning;
- reading/reading selection;
- cloze;
- sentence context;
- listening recognition;
- matching;
- script recognition where relevant.

The scheduler owns **when** the canonical card is reviewed.

The exercise renderer owns **how** the current review is presented, within the card/unit capability.

Do not create separate SRS cards merely to obtain visual variety.

## 13. Review answer flow

Preferred flow:

1. prompt;
2. learner answers or reveals;
3. correctness/result appears;
4. optional concise explanation/context;
5. review rating;
6. next card.

Rating baseline:
- **Again**
- **Hard**
- **Good**
- **Easy**

On exercises with objective correctness, Jularr may visually recommend an appropriate rating based on result/response behavior, but the stored scheduler result follows the explicit supported review contract. Do not silently invent a second difficulty scale.

The four FSRS ratings are shown/enabled **only after the learner answered or explicitly revealed the answer**. A visual mockup may place the rating area in the eventual feedback layout, but the live UI must not let the learner rate an unrevealed card.

Keyboard shortcuts may map to the four ratings on Desktop after answer/reveal.

## 14. Review rating UI

Ratings must be:
- clearly ordered;
- keyboard accessible;
- large enough for touch;
- not encoded only by red/yellow/green;
- unavailable before required answer/reveal state.

Mobile:
- four large compact buttons in one/two rows depending on width.

Desktop:
- one row with optional number shortcuts.

TV:
- only if the review exercise itself is remote-friendly.

Do not expose raw FSRS interval formulas in the primary UI. A subtle next-interval label may be shown later if deliberately specified.

## 15. Review mistakes and reappearance

`Again` / incorrect items may reappear according to the existing scheduler/session policy.

Rules:
- no infinite in-session punishment loop;
- session remains bounded;
- exact scheduler state remains durable;
- closing session preserves completed reviews;
- unfinished due items remain due according to canonical policy.

## 16. No reviews due

Do not enter an empty review shell.

Show a compact completion state:
- `You're caught up`;
- next review time/count if meaningful;
- Continue Learning;
- Back to Dashboard.

No fake practice queue unless the user explicitly selects optional extra practice.

## 17. Media context in Review

If a LearningCard has canonical media context, Review may show:
- Work title;
- episode/chapter;
- sentence/line excerpt;
- thumbnail/cover only when helpful;
- button to open source context after the review/session.

Media context is supportive evidence, not a duplicate learning object.

Do not leak spoilers unnecessarily; use only context the learner already captured/encountered unless explicit settings allow otherwise.

## 18. Audio / TTS

Words/sentences with speech capability expose:
- Listen;
- Replay;
- configured speed/voice through shared speech settings.

Listening exercises may initially hide the written prompt.

TTS failure:
- show local unavailable state;
- never fail the whole Lesson/Review.

No second TTS implementation.

## 19. XP during sessions

XP appears as restrained feedback:
- small +XP on meaningful completed exercise/review;
- section total;
- session total.

Avoid large animations after every tap.

### Anti-farming

Repeated trivial actions must not generate unlimited XP.

XP policy can consider:
- exercise difficulty/type;
- correctness;
- first completion;
- repeated optional practice;
- Hint use;
- session completion.

FSRS rating and XP remain separate values.

## 20. Daily Goal / Streak interaction

Lesson/Review updates the Daily Goal as activity is completed.

The session shell may show a compact Daily Goal progress indicator, but must not distract from the exercise.

When a session completes the Daily Goal:
- show a brief completion moment in Session Summary;
- mark today's Streak day complete.

Do not interrupt an exercise mid-answer with a large Streak animation.

## 21. Session Summary

Both Lesson and Review end with a consistent summary language.

### Lesson
- XP;
- time;
- accuracy;
- exercises;
- new vocabulary/concepts;
- course progress;
- Daily Goal;
- next recommendation.

### Review
- XP;
- time;
- reviews completed;
- accuracy;
- Again/Hard/Good/Easy distribution;
- items needing attention;
- next due summary;
- Daily Goal;
- next recommendation.

Use charts sparingly; the summary should be readable in seconds.

## 22. Review pause / exit

Exit confirmation is needed only if an answer is currently in an ambiguous unsaved state.

Otherwise:
- save completed outcomes immediately;
- return to Dashboard;
- remaining queue stays intact.

Never discard already completed reviews because the learner did not finish the entire batch.

## 23. Lesson pause / exit

Persist exact Step state at safe boundaries.

If the current Step contains unsent free-form input:
- preserve locally/session-wise where feasible;
- otherwise warn before destructive exit.

Do not mark the Lesson complete from mere entry or partial progress.

## 24. Desktop

Desktop is keyboard + pointer optimized.

Original J approved direction:
- warm paper/ink surface;
- red/pink accent;
- restrained sakura/landscape decoration around, not over, the exercise;
- canonical mascot may occupy the side/background edge without shrinking the exercise below a comfortable width;
- the mascot changes pose/expression between Question, Feedback, Help and Summary while remaining the same character.

Requirements:
- centered exercise area;
- controlled maximum width;
- optional keyboard shortcuts;
- visible focus;
- number/letter selection only when it does not conflict with typing;
- Enter can submit/continue only when unambiguous;
- review rating shortcuts after answer.

The normal global Jularr shell may remain on wide Desktop if the product shell requires it, but it must stay visually subordinate. Do not add a second dense Learning navigation tree inside the session merely because a mockup happens to show one.

## 25. Mobile

Touch-first:
- one exercise at a time;
- large answer targets;
- bottom primary action;
- safe keyboard handling;
- no hover;
- avoid swipe for destructive or answer-semantic actions.

The software keyboard must not cover Submit/Continue.

## 26. Tablet

Same touch model with more breathing room.

Landscape may use:
- prompt left / answer right only when pedagogically useful;
- otherwise preserve the centered single-task design.

Do not turn Tablet into a dense desktop dashboard.

## 27. TV

Only a simplified exercise subset is required if TV Learning is enabled.

Suitable:
- multiple choice;
- recognition;
- listening;
- simple matching/select.

Not suitable baseline:
- typed translation;
- free writing;
- complex script input.

Unsupported exercises offer a handoff rather than a broken control.

## 28. Visual skins / Light / Dark

Both first-class.

Original Jularr uses the same canonical mascot reference as other Original J surfaces:
`docs/assets/original-j/jularr-mascot-reference.png`.

For Lesson/Review:
- Question: focused/neutral/explaining pose;
- Hint/help: pointing or tablet/help pose;
- Correct feedback: happy/encouraging pose;
- Incorrect feedback: supportive/focused pose, not punitive;
- Session completion: celebrating-success pose;
- Error/degraded state: apologetic/error pose where a mascot is appropriate.

The character identity, face, hair, eyes, outfit family and ornaments stay canonical. Pose, crop, expression and props may vary.

Future language/course-specific companion sets may be introduced later, but they remain optional visual assets only. They must never fork layout, exercise logic, scoring or scheduler behavior. Until such a set is explicitly specified, Original Jularr uses the canonical Jularr mascot.

Clean keeps the same layout and behavior and does not use the Original J mascot by default.

Use color carefully:
- correct;
- incorrect;
- selected;
- XP/progress;
- review ratings.

Every state also has icon/text/shape differences.

No bright arcade aesthetic unless a future theme explicitly introduces it.

## 29. Loading / offline / degraded

Loading:
- preserve session shell;
- skeleton/spinner only for current content;
- never show fake answer choices.

Offline:
- only sessions whose required content/state is available may start;
- completed local events queue for sync through the canonical offline contract when implemented.

AI unavailable:
- AI explanations disappear/degrade;
- core exercise remains usable.

TTS unavailable:
- Listen disabled/unavailable;
- session continues.

## 30. Error recovery

Recoverable error:
- keep session index/progress;
- Retry current Step/card;
- do not duplicate recorded answer/review on retry.

Permanent invalid content:
- skip only through explicit safe policy;
- log/report internally;
- learner receives concise state.

Never expose raw exception text.

## 31. Accessibility

Required:
- full keyboard use on Desktop;
- focus visible;
- screen-reader labels;
- answer state announced;
- correct/incorrect not color-only;
- reduced-motion mode;
- touch targets >=44px;
- appropriate language tags for source/target text;
- audio controls labeled;
- time pressure is not required in normal V1 Lessons/Reviews.

## 32. Required mockups

Approved Original Jularr reference direction:
1. **Desktop Lesson — Question** — approved visual direction;
2. **Desktop Lesson — Correct/feedback** — approved visual direction;
3. **Desktop Review — Question + post-answer FSRS rating layout** — approved visual direction, with rating disabled/hidden until answer/reveal;
4. **Mobile Lesson — Question** — approved visual direction;
5. **Mobile Lesson — Feedback** — approved visual direction;
6. **Mobile Review — Question/Feedback** — approved visual direction;
7. **Desktop Lesson Session Summary** — approved visual direction.

Still required as validation:
8. **Desktop Lesson — incorrect feedback**;
9. **Desktop Review Session Summary**;
10. **Dark validation — one Lesson + one Review state**;
11. **No reviews due / TTS unavailable reference**;
12. **Clean parity reference** using the same layout/behavior without Original J decoration.

The approved image is a visual aid. This text specification remains authoritative for timing, accessibility, answer visibility and scheduler semantics.

## 33. Future-proofing, not V1 scope

Architecture must leave room for:
- Placement Tests;
- formal checkpoints/exams;
- adaptive difficulty;
- Remedial Lessons;
- mastery decay;
- Speaking/pronunciation scoring;
- free Writing correction;
- AI-generated exercises;
- AI Tutor;
- deeper skill mastery;
- media-specific prepared courses.

Do not implement these simply because the architecture permits them.

## 34. Must not implement

- No second review scheduler.
- No second LearningCard store.
- No XP-derived FSRS rating.
- No curriculum completion inferred from review mastery.
- No Review completion inferred from Lesson completion.
- No forced AI.
- No duplicate TTS.
- No separate media-learning copies.
- No loss of progress on normal exit.
- No giant Dashboard inside a Lesson/Review.
- No hidden keyboard-only actions.
- No raw scheduler internals in learner UI.
- No endless `Again` punishment loop.
- No unsupported TV text-entry exercises.
- No future Speaking/Writing/AI Tutor implementation in the V1 slice.
- No answer-spoiling illustration for recall/meaning exercises.
- No alternate Original J mascot identity when the canonical mascot is used.
- No FSRS rating before answer/reveal.
- No language-specific companion variant that changes lesson/review behavior.

## 35. Acceptance

Lesson/Review V1 is complete only when:
- Lesson is a coherent linear flow with section checkpoints;
- exact Lesson resume works;
- Review uses existing FSRS/SRS state;
- review presentation can vary without duplicating scheduled cards;
- Again/Hard/Good/Easy is clear and accessible;
- correct/incorrect feedback is useful but concise;
- XP and Daily Goal integrate without dominating the session;
- Session Summary covers XP, accuracy, progress and next action;
- exit preserves completed work;
- Desktop/Mobile/Tablet are intentional;
- TV only exposes compatible exercise types;
- Light/Dark and accessibility states are designed;
- optional AI/TTS failure does not break core learning;
- Original J companion artwork uses the canonical mascot and never reveals an answer;
- Clean and Original J remain the same learning product, not separate session implementations.
