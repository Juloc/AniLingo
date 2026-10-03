# Script Trainer / Kana Trainer — V1

Status: **binding V1 planning specification**. The approved Original Jularr mockups form a three-part storyboard:
1. Overview / Learn;
2. Practice states;
3. Group Summary.

These are **sequential states of one trainer flow**, not three independent products and not one giant page rendered at the same time.

Global UX: `docs/UX.md`  
Learning architecture: `docs/LEARNING_V2.md`  
Language/toolkit model: `docs/LEARNING_LANGUAGES.md`  
Lesson/Review: `docs/mockups/lesson-review/SPEC.md`  
Progress/Achievements: `docs/mockups/progress-achievements/SPEC.md`  
Original J mascot: `docs/assets/original-j/jularr-mascot-reference.png`

## 1. Product model

The product concept is a generic **Script Trainer** capability.

The current concrete implementation is Japanese Kana:
- Hiragana;
- Katakana.

The Japanese UI may therefore call the surface **Kana Trainer**.

Future writing systems may plug into the same ScriptTrainer capability only when a language toolkit supplies an explicit script catalog and trainer behavior. Do not generalize by hardcoding assumptions about another writing system.

Current route:
- `/Learn/Kana`

The route may remain as the Japanese deep link even if the internal/shared component later becomes Script Trainer.

## 2. Canonical data

Kana already uses the canonical Learning model:
- each symbol is a `LearningUnitKind.Script`;
- symbol is a `ja` LearningVariant;
- romaji is a `ja-Latn` LearningVariant;
- stable unit IDs come from `KanaCatalog.IdFor`;
- profile learning state lives in LearningCards;
- Review history lives in LearningCardReviews;
- the profile's Kana course remains non-primary.

Do not create:
- KanaProgress table;
- KanaReview table;
- separate known/learning booleans;
- separate SRS scheduler.

## 3. Availability

Show the trainer only when:
- instance Learning module is enabled;
- profile Learning module preference is On;
- user is permitted to use Learning;
- `ScriptTrainer` capability resolves On;
- an enabled course/language toolkit actually supplies ScriptTrainer support.

Today, the Japanese toolkit is the implemented ScriptTrainer.

If no compatible course exists:
- Learning Home may show a compact setup hint;
- do not show a broken Kana screen.

## 4. One route, sequential UI states

The approved flow is:

```text
Overview / Learn
    ↓ Practice Group
Practice Question
    ↓ Answer
Feedback
    ↓ Next
Practice Question
    ↓ ...
Group Summary
    ├─ Practice Again
    ├─ Next Group
    └─ Back to Overview
```

Rules:
- do not render Overview + Reading + Listening + Writing + Summary simultaneously;
- do not create separate URLs for every question state;
- browser/session state may support safe resume, but all states belong to one trainer flow;
- Back from a practice session asks/behaves predictably if unsaved transient session state would be lost;
- canonical card/review writes happen through Learning services, not by preserving UI-only state.

## 5. Overview / Learn state

The approved Overview mockup contains:

### Header
- title: Kana Trainer;
- short learning purpose;
- Hiragana / Katakana switch;
- compact Original J illustration/mascot.

### Summary
Useful counts:
- Learning;
- Known;
- Due.

A total-symbol count may be shown only when its scope is explicit.

Do not mix:
- new/untracked symbols;
- active Learning;
- Due review cards.

`Due` means scheduled review is due now; it does not mean "new symbol to learn".

### Groups
Display authored/catalog groups from the actual KanaCatalog.

Current catalog has **20 stages**, not a fake fixed 8-group model.

Examples:
- Vokale;
- K-Reihe;
- S-Reihe;
- T-Reihe;
- ...
- combinations.

The UI may horizontally scroll or progressively reveal groups, but the displayed count/order must come from the real catalog.

Group states can include:
- complete/known;
- in progress;
- available;
- future/locked only if progression policy explicitly requires it.

Do not hardcode lock icons merely for visual decoration.

## 6. Group detail

Selecting a group shows its actual symbols.

For K-Reihe:
- か / ka
- き / ki
- く / ku
- け / ke
- こ / ko

Each symbol card can show:
- symbol;
- romanization;
- state;
- Due marker when due;
- Listen.

Selecting a symbol opens/updates the learning detail without navigating away.

## 7. Symbol learning detail

The detail teaches before testing.

Useful fields:
- large symbol;
- reading/romaji;
- audio;
- stroke-order reference when canonical stroke data exists;
- one or a few simple example words;
- optionally a short note for exceptional pronunciation/use.

Example:

```text
き
ki
[Listen]

Stroke order
1 → 2 → 3 → 4

Example
きた · kita · north
```

Rules:
- examples are learning aids, not required state;
- examples must be linguistically correct and sourced from an explicit catalog/content source;
- do not invent random vocabulary purely for decoration;
- detail remains compact.

## 8. Start group learning

Primary Overview action:
- **Practice Group**

Optional secondary action:
- **Learn only new symbols** when a deterministic definition exists.

Starting a group:
- prepares canonical Script units/course if needed;
- moves eligible new symbols into Learning through the canonical Learning service;
- does not automatically mark them Known;
- creates a transient practice session over the selected group.

## 9. Practice modes

Target mode family:
- **Read** — symbol -> reading;
- **Listen** — audio -> symbol;
- **Write** — produce/draw symbol;
- **Mixed** — trainer selects among supported modes.

### Implemented V1 modes today

Current canonical Kana practice supports:
- Kana -> Romaji;
- Romaji -> Kana;
- Audio -> Kana.

These map naturally to Read/Recognition, Reverse/Production-style recognition, and Listen.

### Writing mode

The approved mockup shows a handwriting/stroke-order exercise.

This is a **target capability**, not permission to fake handwriting assessment.

Writing can be enabled only after Jularr has:
- canonical stroke-path/order data for each symbol;
- a touch/pointer drawing surface;
- deterministic evaluation/tolerance rules or a clearly defined self-check mode;
- accessibility fallback;
- persistence semantics that reuse normal Learning cards/reviews.

Until that exists:
- stroke order may be shown as a reference;
- **hide the interactive Writing practice mode**;
- do not pretend a drawing is correct just because the user pressed Check.

Writing is not required to ship the initial Script Trainer V1.

## 10. Practice session layout

When practice starts, Overview chrome is reduced to a compact session header.

Desktop/Mobile both focus on **one question at a time**.

Example:

```text
K-Reihe                         3 / 10
──────── progress ────────

              き
             [Listen]

Which reading is correct?

[ ka ]       [ ki ]
[ ku ]       [ ke ]

            [ Check ]
```

Do not keep:
- full group grid;
- statistics cards;
- other exercise modes;
- summary;
visible beside the active question.

## 11. Read mode

Prompt:
- Kana symbol.

Answer:
- select/enter correct reading according to the exercise contract.

Distractors:
- come from plausible symbols/readings in the active learned set;
- must not contain duplicate equivalent answers;
- must not be intentionally misleading through typography tricks.

Audio is optional support unless the exercise specifically tests silent recognition.

## 12. Listen mode

Prompt:
- source-language audio only;
- replay control.

Answer:
- choose the matching Kana symbol.

Rules:
- do not reveal the symbol before answer;
- fallback when TTS is unavailable must be local and explicit;
- unavailable Listen mode does not break Read mode;
- no server AI dependency.

## 13. Reverse recognition

Current `RomajiToKana` mode may be used as:
- reading -> Kana.

It remains a normal supported Script Trainer mode.

Do not label this handwriting/Writing unless the learner actually produces the symbol.

## 14. Feedback state

After answer, replace/transform the question into concise feedback.

Correct:
- expected answer;
- optional tiny explanation;
- Continue.

Incorrect:
- learner's choice;
- correct answer;
- short reinforcement;
- Continue.

The canonical mascot may appear in a small feedback role:
- encouraging when correct;
- supportive/focused when incorrect.

No punitive language.

Do not show the next question before the user can understand the feedback.

## 15. Review / scheduling semantics

Trainer practice reuses LearningCards and LearningCardReviews.

Current behavior may map objective group-practice answers to scheduler ratings such as:
- correct -> Good;
- incorrect -> Again.

This mapping must be centralized in the Script/Kana application service and remain consistent.

Rules:
- practice does not create a second difficulty scale;
- Due reviews still belong to the canonical Review scheduler;
- group practice may provide extra practice, but it must not fabricate "due" status;
- Known state is not automatically inferred merely from one correct practice answer;
- manually marking Known remains an explicit canonical state transition.

If the general Lesson/Review scheduler semantics change, Script Trainer must follow the shared contract rather than diverge.

## 16. Session composition

A group practice session:
- has a finite question count;
- can repeat difficult symbols within the session;
- does not need to test every mode equally;
- uses only modes currently supported/enabled;
- should bias toward new/weak symbols without hiding due-state truth.

Mixed mode may interleave Read/Listen/reverse recognition.

Writing joins Mixed only after Writing capability is genuinely implemented.

## 17. Group Summary state

After the session show:

- correct / total;
- accuracy;
- new symbols moved into Learning;
- symbols that need more practice;
- active practice time only if canonical Learning-time tracking exists.

Then:
- difficult symbols;
- selected group's state;
- next group preview;
- actions:
  - Practice again;
  - Next group;
  - Back to Overview.

### Terminology

`Newly learned` means joined the Learning state during the session, **not automatically Known/mastered**.

Use `Known` only for canonical Known state.

Avoid ambiguous labels like `Mastered` unless the product defines an explicit mastery contract.

## 18. Difficult symbols

Summary can identify difficult symbols from the current transient session:
- errors;
- repeated misses;
- lower local session success.

This is a **session summary**, not a new durable mastery model.

Long-term scheduling remains FSRS/card history.

A "Practice difficult symbols" action may start a scoped extra-practice session without modifying Due timestamps merely by opening it.

## 19. Progression

V1 does not need XP/level locks for script groups.

Default progression:
- groups can be browsed;
- next group is suggested in catalog order;
- optional curriculum policy may recommend completion before advancing.

Never lock a Kana group based on:
- XP;
- streak;
- achievement;
- virtual currency.

If groups are actually locked, the reason must come from an explicit Script Trainer progression policy and be accessible.

## 20. Original J visual direction

Use:
- warm paper/cream base;
- restrained Japanese ink landscape;
- sakura decoration;
- red/pink accent;
- canonical Jularr mascot.

Canonical reference:
`docs/assets/original-j/jularr-mascot-reference.png`

Mascot usage:
- Overview: teaching/pointing pose;
- Feedback: small encouraging/supportive pose;
- Summary: celebration pose;
- Empty/error: relevant state pose.

Do not:
- place the mascot beside every symbol;
- let decorative art obscure Kana glyphs;
- use third-party anime characters;
- use imagery as an answer clue during Listen/recognition questions.

Clean has identical behavior/layout with neutral Fluent-style surfaces and no Original J mascot by default.

## 21. Mobile

Mobile Overview:
- compact header;
- Hiragana/Katakana;
- small Learning/Known/Due metrics;
- horizontal/compact group selector;
- selected group's symbols;
- symbol detail;
- Practice Group.

Mobile Practice:
- **one question per screen**;
- progress at top;
- large prompt;
- 2x2 or otherwise comfortable answer targets;
- Check/Continue thumb-reachable;
- on-screen keyboard/drawing surface must not cover controls.

Mobile Summary:
- compact headline result;
- difficult symbols;
- next group;
- three actions without dense dashboard content.

No Desktop multi-panel composition squeezed into phone.

## 22. Tablet

Portrait follows Mobile with wider cards.

Landscape may use:
- group list + symbol detail side by side on Overview;
- centered single-question Practice;
- two-column Summary.

Touch remains primary.

## 23. TV

Script Trainer is optional on TV.

Remote-friendly modes:
- Read multiple choice;
- Listen multiple choice.

Do not implement:
- drawing/writing;
- keyboard-heavy input.

TV uses large focus targets and may hand off unsupported modes.

## 24. Accessibility

- Kana symbols use appropriate language metadata;
- audio controls have labels;
- state is not color-only;
- keyboard operation on Desktop;
- visible focus;
- answer feedback announced;
- reduced motion;
- touch targets >=44px;
- stroke-order reference does not rely solely on animation;
- writing mode, if ever enabled, needs a non-drawing alternative where feasible.

## 25. Loading / empty / errors

Loading:
- stable layout skeleton;
- no fake symbol states.

No compatible Script Trainer:
- return to Learning with a concise capability/setup explanation.

TTS unavailable:
- hide/disable Listen mode only;
- Read/reverse modes remain usable.

Missing stroke data:
- hide stroke-order detail for that symbol;
- do not fail group learning.

Practice request failure:
- preserve current question where safe;
- Retry;
- never double-submit/review the same answer accidentally.

## 26. Required visual references

Approved storyboard:

1. **Original Jularr Light — Overview / Learn**
   - Desktop + Mobile.
2. **Original Jularr Light — Practice States**
   - Read;
   - Feedback;
   - Listen;
   - Writing target state;
   - Mobile equivalents.
3. **Original Jularr Light — Group Summary**
   - Desktop + Mobile.

The Practice States image is a **state board**. Those panels are alternatives in time, not simultaneous production layout.

Still useful later:
4. Dark validation;
5. Clean parity;
6. Katakana Overview;
7. TTS unavailable;
8. Writing implementation reference only after its capability contract exists.

## 27. Suggested mockup filenames

Upload the approved images as:

- `docs/mockups/script-trainer/overview-original-j-light.png`
- `docs/mockups/script-trainer/practice-states-original-j-light.png`
- `docs/mockups/script-trainer/summary-original-j-light.png`

## 28. Must not implement

- No giant page showing Overview, all practice modes and Summary together.
- No separate route per question.
- No second Kana progress/review store.
- No fake 8-group model when KanaCatalog has 20 stages.
- No "Due" meaning "new".
- No one-correct-answer -> Known shortcut.
- No fake handwriting recognition.
- No Writing mode without real capability/data.
- No permanent Japanese assumptions in generic ScriptTrainer capability.
- No XP/streak locks.
- No answer-spoiling illustration.
- No alternate Original J mascot identity.
- No theme-specific learning logic.

## 29. Acceptance

Script Trainer is complete when:
- Overview teaches the selected group before testing;
- real KanaCatalog stages/groups drive navigation;
- Practice is one focused question at a time;
- Read/Listen/reverse modes reuse canonical Learning state;
- feedback is clear before the next question;
- Summary distinguishes session performance from durable Known/FSRS state;
- Due reviews remain canonical scheduler truth;
- Writing is hidden until genuinely implemented;
- Mobile is intentionally designed;
- Original J and Clean are skins over identical behavior;
- the flow remains one coherent trainer rather than multiple mini-apps.
