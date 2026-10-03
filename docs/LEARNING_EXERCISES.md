# Jularr Learning Exercise Content Model

Status: **binding architecture/product plan for Learning Phase 1**. This document defines the durable content model and execution contract for structured Lesson exercises. It does not implement the engine.

Related:
- `docs/LEARNING_PEDAGOGY.md`
- `docs/LEARNING_V2.md`
- `docs/LEARNING_LANGUAGES.md`
- `docs/ARCHITECTURE.md`
- `docs/DATABASE_CONVENTIONS.md`
- `docs/mockups/lesson-review/SPEC.md`

## 1. Existing foundation to preserve

The existing curriculum hierarchy remains canonical:

```text
CurriculumBlueprint
  -> CurriculumLevel
    -> CurriculumChapter
      -> CurriculumLesson
        -> CurriculumExercise
```

The current `CurriculumExercise` foundation intentionally stores only structural information. That separation remains correct.

The exercise engine must **evolve this model**, not create a parallel Lesson/Course hierarchy.

The existing Learning state remains canonical:
- `LearningUnit`
- `LearningVariant`
- `LearningCourse`
- `LearningCard`
- `LearningCardReview`
- `LearningContext`

Exercise content must reuse these identities where relevant. It must not introduce another vocabulary/card/review store.

## 2. Core separation

There are three different concerns and they must remain separate.

### 2.1 Blueprint = language-neutral teaching structure

The blueprint owns:
- Lesson/Exercise identity;
- ordering;
- pedagogical phase;
- exercise interaction kind;
- required/optional course-flow status;
- Lesson objectives and exercise-to-objective mapping.

It does **not** own learner-facing Japanese/German/etc. answer text.

### 2.2 Shared course instance = language-pair content

A `SharedCourseInstance` owns the concrete authored specialization for one source -> target language pair.

It owns:
- concrete prompts/examples;
- choices/accepted answers;
- hints;
- feedback/explanations;
- source/target text;
- audio stimulus text;
- links to concrete `LearningUnit` targets.

This content is shared by every learner enrolled in the same published course revision.

### 2.3 Learner state = personal execution result

Personal state does not live in shared exercise content.

Attempts/progress belong to the learner/course execution layer defined in `LEARNING_PROGRESS.md`.

FSRS state remains in `LearningCard` / `LearningCardReview`.

## 3. Blueprint objectives

Add one explicit structural objective concept under a Lesson.

Target entity:

```text
CurriculumLessonObjective
- Id                  BIGINT identity
- LessonId            UUID FK
- Key                 VARCHAR(120)
- Skill               INTEGER enum
- Ordinal             INTEGER
```

Constraints:
- FK to `CurriculumLesson`;
- UNIQUE (`LessonId`, `Key`);
- UNIQUE (`LessonId`, `Ordinal`);
- `Ordinal > 0`;
- no database cascade used as a business workflow.

Target generic skill enum:

```text
Vocabulary
Grammar
Reading
Listening
Script
Production
Writing       // future-capable, not required for V1
Speaking      // future-capable, not required for V1
```

The objective is structural/internal. It does not need learner-facing prose in the blueprint.

## 4. Exercise-to-objective mapping

Use an explicit join table:

```text
CurriculumExerciseObjective
- ExerciseId           UUID FK
- ObjectiveId          BIGINT FK
PRIMARY KEY (ExerciseId, ObjectiveId)
```

This provides real referential integrity instead of storing objective keys in an arbitrary JSON array.

Rules:
- normal teaching/practice exercises map to at least one Lesson objective;
- orientation and pure Summary content may be exempt;
- an Exercise cannot reference an objective belonging to another Lesson;
- the application validates same-Lesson ownership before insertion; database FKs remain the final referential boundary.

## 5. Exercise pedagogical phase

Add a typed phase to the structural Exercise:

```text
Orient
Introduce
Example
GuidedPractice
IndependentRetrieval
Apply
Checkpoint
Summary
```

This is the durable implementation of the flow from `LEARNING_PEDAGOGY.md`.

Do not infer phase from ordinal or UI component.

## 6. Exercise interaction kind

The current `CurriculumExerciseKind` is a foundation enum and should evolve without renumbering existing values during migration.

The target V1 renderer set should be small:

- **Presentation** — explanation/example/vocabulary/concept/passages with no scored answer;
- **MultipleChoice**;
- **Matching**;
- **Cloze**;
- **Ordering** — sentence/token ordering;
- **ShortAnswer**.

Important modeling rule:

**Listening, reading comprehension and translation are not required to be separate renderer engines.**

They are combinations of:
- objective/skill;
- stimulus modality;
- one of the answer interactions above.

Examples:

```text
Listening comprehension = audio stimulus + MultipleChoice
Dictation                = audio stimulus + ShortAnswer
Reading comprehension    = text context + MultipleChoice
Translation              = source text + ShortAnswer
```

This avoids separate near-duplicate execution engines.

Existing enum values such as `Listening`, `Translation`, `Flashcard`, `Speaking` and `Writing` must be migrated deliberately:
- do not silently reinterpret persisted values;
- append new enum values rather than renumbering old ones;
- author new V1 content using the target renderer model;
- Speaking/handwriting remain unsupported execution capabilities until their own contracts exist.

## 7. Structural Exercise fields

Target structural `CurriculumExercise` responsibility:

```text
CurriculumExercise
- existing Id / LessonId / Key / Ordinal
- Kind
- Phase
- IsRequired
```

The existing free-form `Prompt` must not become the canonical learner-facing prompt for new content.

Migration direction:
- preserve existing persisted value while the current foundation is migrated;
- stop treating it as the rendered language-pair exercise content;
- remove/rename it only through the supported migration window, not through a runtime fallback.

Do not add answer text, choices, hints, scoring or translations directly to the structural blueprint row.

## 8. Shared course content revision

The current `SharedCourseInstance.ContentFingerprint` only represents blueprint fingerprint + language pair. That is insufficient once concrete exercise content exists.

A published shared instance must represent one complete, immutable course-content revision:

```text
SharedCourseInstance
- existing identity / BlueprintId / source / target / title
- ContentVersion          INTEGER >= 1
- ContentFingerprint      SHA-256 of blueprint + specialized content
- IsPublished             BOOLEAN
```

Required uniqueness:
- UNIQUE `ContentFingerprint`;
- UNIQUE (`BlueprintId`, `SourceLanguage`, `TargetLanguage`, `ContentVersion`).

Meaning:
- changing structural blueprint content requires a blueprint version bump;
- changing specialized prompt/answer/example content requires a new shared content version;
- published content is immutable;
- existing learner enrollments stay pinned until the explicit course-update policy moves them.

Do not overwrite published course content in place.

## 9. Shared exercise content

Add one concrete content row per shared instance + structural exercise:

```text
SharedCourseExerciseContent
- Id                  BIGINT identity
- SharedInstanceId    UUID FK
- ExerciseId          UUID FK
- SchemaVersion       INTEGER
- Payload             JSONB
- ContentFingerprint  VARCHAR(64)
- CreatedAt           TIMESTAMPTZ
```

Constraints:
- UNIQUE (`SharedInstanceId`, `ExerciseId`);
- `SchemaVersion > 0`;
- payload NOT NULL;
- no cascading business behavior;
- FK validity enforced by PostgreSQL.

Access path:
- a Lesson load fetches all content for one SharedInstance + the Lesson's Exercise IDs in one query/batch;
- no N+1 query per Exercise.

Do not index arbitrary JSON paths by default. V1 does not search exercises by payload fields.

## 10. JSONB boundary

JSONB is appropriate only for the **kind-specific authored payload**, because answer shapes differ substantially between MultipleChoice, Matching, Cloze and Ordering.

It must not become an untyped dumping ground.

Rules:
- application code exposes typed records per supported Exercise kind;
- no `Dictionary<string, object>` content contract;
- schema version is explicit;
- payload is fully validated before publishing;
- payload is immutable after publishing;
- API/UI receives explicit DTOs, never the EF entity or raw unvalidated JSON;
- server remains the authoritative evaluator for scored answers.

## 11. Common content primitives

All payloads reuse a small set of typed primitives.

### Learning text

```text
LearningText
- LanguageTag
- Text
- optional Reading
```

Rules:
- valid normalized BCP-47 tag;
- plain text, not arbitrary HTML;
- bounded lengths;
- output encoded by the UI.

### Audio stimulus

```text
SpeechStimulus
- LanguageTag
- Text
- HideWrittenTextUntilAnswer
```

V1 resolves speech through the shared TTS contract.

Do not persist provider/model/voice choice inside every exercise. User/system speech settings own voice selection.

### Hint

```text
ExerciseHint
- Kind
- Text or referenced choice/segment where required
```

Allowed V1 hint kinds:
- ConceptReminder;
- RevealReading;
- RevealFirstSegment;
- EliminateChoice;
- RevealAnswer.

No executable expressions or arbitrary scripts.

## 12. Presentation payload

Purpose:
- Orient;
- Introduce;
- Example;
- non-scored reading/context.

Typed shape:

```text
PresentationExerciseContent
- optional Title
- Body blocks
- optional Examples
- optional SpeechStimulus
```

V1 body blocks remain deliberately small:
- paragraph;
- LearningText example;
- emphasized target span by explicit structured field.

No raw authored HTML/JavaScript.

Decorative Original-J mascot/art is theme/UI state, not persisted Exercise content.

## 13. MultipleChoice payload

Typed shape:

```text
MultipleChoiceExerciseContent
- optional Context
- Prompt
- optional SpeechStimulus
- Choices[]
    - stable Key
    - LearningText/Text
- CorrectChoiceKey
- ChoiceOrderPolicy
- Hints[]
- optional CorrectFeedback
- optional IncorrectFeedback
- optional Explanation
```

Rules:
- 2..reasonable bounded number of choices;
- unique choice keys;
- exactly one correct choice in V1;
- correct key must exist;
- no duplicate semantically equivalent options after normalization;
- shuffle policy is explicit;
- answer identity uses stable choice key, never array position.

Multi-select is later scope unless a real authored need appears.

## 14. Matching payload

Typed shape:

```text
MatchingExerciseContent
- optional Context
- Pairs[]
    - stable Key
    - Left
    - Right
- Hints[]
- optional Explanation
```

Rules:
- pair keys unique;
- V1 pairs are one-to-one;
- visual order may shuffle independently;
- scoring is deterministic.

Do not model matching as several fake LearningCards.

## 15. Cloze payload

Do not store a free-form regex replacement exercise.

Typed shape:

```text
ClozeExerciseContent
- ContextSegments[]
    - Text segment
    - Blank with stable Key
- AcceptedAnswers per Blank
- NormalizationPolicy
- Hints[]
- optional Explanation
```

Rules:
- every blank has at least one accepted answer;
- no arbitrary regex supplied by course content;
- answer comparison uses the canonical normalization/evaluation policy;
- multiple blanks may exist, but V1 UI may restrict how many are presented at once for usability.

## 16. Ordering payload

Typed shape:

```text
OrderingExerciseContent
- optional Context
- Items[]
    - stable Key
    - Text
- CorrectOrder[]
- optional Explanation
```

Rules:
- item keys unique even when displayed text repeats;
- `CorrectOrder` contains every key exactly once;
- initial display order is shuffled deterministically per attempt/session;
- server evaluates key order.

## 17. ShortAnswer payload

Typed shape:

```text
ShortAnswerExerciseContent
- optional Context
- Prompt
- optional SpeechStimulus
- AcceptedAnswers[]
- NormalizationPolicy
- Hints[]
- optional CorrectFeedback
- optional IncorrectFeedback
- optional Explanation
```

V1 normalization policies:
- Exact;
- CaseInsensitive;
- LanguageToolkit.

Base sanitation:
- trim surrounding whitespace;
- normalize safe Unicode representation consistently;
- never execute course-provided regex/code.

`LanguageToolkit` delegates language-specific equivalence to the existing toolkit owner. It does not create page-local answer logic.

## 18. Concrete LearningUnit links

Where an exercise teaches/practices canonical learning material, use relational links rather than embedding LearningUnit IDs as the only durable relationship inside JSON.

Target table:

```text
SharedCourseExerciseUnit
- Id                  BIGINT identity
- ExerciseContentId   BIGINT FK
- LearningUnitId      UUID FK
- Role                INTEGER enum
- Ordinal             INTEGER
```

V1 roles:
- Target;
- Prerequisite;
- Context.

Constraints:
- UNIQUE (`ExerciseContentId`, `LearningUnitId`, `Role`);
- `Ordinal > 0`.

Meaning:
- **Target**: the unit this exercise intentionally teaches/practices;
- **Prerequisite**: prior unit expected by the exercise;
- **Context**: unit appears in context but is not itself the learning target.

The Practice/Review policy later decides which Target units become/activate LearningCards. Exercise content itself does not schedule cards.

Grammar/concept objectives do not need a fake LearningUnit just to satisfy this table.

## 19. Content publishing owner

Do not put content publishing into Razor Pages and do not overload `SharedCourseService.EnrollAsync` with authoring behavior.

Target ownership:
- existing `CurriculumBlueprintService`: blueprint structure and structural validation;
- a focused shared-course-content application/domain owner: concrete language-pair content validation, fingerprinting and publishing;
- existing `SharedCourseService`: learner enrollment/personal shared-course relationship.

A separate content owner is justified because publishing specialized immutable content is a real responsibility distinct from enrollment.

Do not introduce interfaces/factories/providers unless a current second implementation requires them.

## 20. Enrollment change

The current behavior creates a `SharedCourseInstance` on first learner enrollment. That is only sufficient for the current empty-content foundation.

Target behavior:

```text
author/build blueprint
-> author + validate specialized content
-> publish SharedCourseInstance revision
-> learner enrolls in that published instance
```

Enrollment must not create an incomplete course shell.

The implementation migration must replace the old create-on-enroll behavior explicitly; do not keep it as a runtime fallback.

## 21. Fingerprinting

Fingerprinting must include:
- blueprint fingerprint/version;
- normalized source/target language tags;
- content version;
- every specialized Exercise payload;
- Exercise target-unit links and roles;
- authored order where order has semantic meaning.

Do not hash raw arbitrary JSON text whose property order can change accidentally.

Use one canonical serialization/fingerprint implementation owned by curriculum content, with deterministic tests proving:
- identical content -> identical fingerprint;
- meaningful content change -> different fingerprint;
- JSON formatting/property-order implementation detail does not alter the result.

## 22. Publishing validation

A shared course revision cannot be published unless:

### Structural
- every required structural Exercise has exactly one specialized content row;
- no specialized content references an Exercise outside the blueprint;
- payload kind matches structural `CurriculumExercise.Kind`;
- schema versions are supported.

### Pedagogy
- every Lesson has at least one objective;
- non-orientation/non-summary required exercises map to objective(s);
- each objective is actually practiced;
- assessed objectives have at least one IndependentRetrieval/Apply/Checkpoint exercise;
- no required future-only Speaking/handwriting exercise exists in V1.

### Answers
- all scored exercises have deterministic answer definitions;
- choice/order keys are valid and unique;
- Cloze/ShortAnswer accepted-answer sets are non-empty;
- answer ambiguity validation passes.

### Language
- all content language tags normalize;
- source/target use the shared instance pair where expected;
- no UI locale is inferred from course language.

### Capability
- a temporary TTS failure cannot make an ordinary V1 Lesson permanently impossible to complete;
- unsupported future capabilities are rejected at publish time.

## 23. Capability failure during execution

Existing UX requires Learning to degrade locally when TTS/optional services fail.

Therefore:
- an unavailable audio stimulus is not recorded as an incorrect learner answer;
- an audio-dependent step may be skipped/degraded according to the execution contract;
- the Lesson can still make progress;
- Summary may state that one capability-dependent activity was unavailable;
- the engine must not invent a fake successful listening result.

Formal exams/mastery gates may define stricter rules later; ordinary V1 Lessons do not.

## 24. Answer evaluation

Server/application layer is authoritative.

Flow:

```text
client submits ExerciseId + attempt payload + idempotency key
-> load published typed Exercise content
-> validate learner/course/session ownership
-> normalize answer using canonical evaluator/toolkit
-> compute objective result
-> persist attempt/progress atomically
-> return feedback DTO
```

The browser may mirror obvious selection state for responsiveness but must not own correctness rules.

No answer keys need to be sent to the client before submission when that would reveal the answer.

## 25. Idempotency

Submitting the same attempt twice because of retry/double tap/network retry must not:
- increment completion twice;
- award XP twice;
- duplicate an attempt;
- create two review events.

The execution contract therefore carries a client-generated attempt/event idempotency key.

Exact attempt persistence belongs to `LEARNING_PROGRESS.md`.

## 26. Security and content safety

- no raw HTML in authored Exercise payloads in V1;
- no script/executable expressions;
- no course-provided regex execution;
- lengths/counts bounded during publishing;
- output encoded;
- authorization enforced server-side;
- exercise content is read from local PostgreSQL state during normal execution, not fetched from arbitrary providers on every question.

AI-generated draft content, if added later, must be validated/published into this same canonical model before normal learner use.

## 27. Query/performance plan

Lesson execution should load:
- Lesson structure/objectives/exercises;
- all specialized Exercise content for those IDs;
- required target-unit variants/state

in bounded set-based queries.

Do not:
- query one payload per Exercise;
- query one LearningUnit/variant per answer choice;
- load all course content when only one Lesson is needed.

Add indexes only for actual access paths. The unique SharedInstance+Exercise index already owns the primary Exercise-content lookup.

## 28. Migration constraints

Implementation must:
1. preserve existing blueprint/course/enrollment/progress data;
2. add new structures through normal PostgreSQL migration;
3. use new relations with NO ACTION/RESTRICT lifecycle semantics;
4. perform dependent cleanup explicitly in backend transactions where deletion is supported;
5. migrate existing foundation Exercise kinds/prompts deliberately;
6. remove obsolete runtime interpretation after the supported upgrade window;
7. not add a legacy fallback reader.

New internal relational row IDs default to BIGINT identity according to repository database rules. Existing UUID identities remain unchanged unless a separate migration has a concrete reason to alter them.

## 29. V1 authored capabilities

Required execution support:
- Presentation;
- MultipleChoice;
- Matching;
- Cloze;
- Ordering;
- ShortAnswer;
- optional TTS stimulus;
- objective mapping;
- LearningUnit target links;
- deterministic hints/feedback.

Not V1:
- free-form essay grading;
- AI answer scoring;
- pronunciation scoring;
- handwriting recognition;
- arbitrary rich HTML exercises;
- executable exercise plugins;
- user/community course scripting.

## 30. Tests required when implemented

### Deterministic unit tests
- each payload validator;
- answer normalization/evaluation;
- choice/order/cloze invariants;
- pedagogy publication rules;
- fingerprint stability;
- idempotency policy.

### PostgreSQL integration tests
- constraints/uniqueness/FKs;
- publishing one complete shared revision;
- rejection of invalid cross-course references;
- set-based Lesson content load;
- immutable published revision behavior;
- explicit dependent cleanup where supported.

### End-to-end
- one Lesson containing Presentation -> GuidedPractice -> Retrieval -> Feedback -> Summary;
- TTS unavailable degradation;
- retry/double-submit does not duplicate outcome.

## 31. Must not implement

- No second curriculum hierarchy.
- No answers directly on the language-neutral blueprint.
- No one-table-per-exercise-kind persistence explosion.
- No untyped arbitrary JSON contract.
- No raw HTML/script/regex execution from course data.
- No page-owned correctness logic.
- No Exercise-owned FSRS scheduler.
- No duplicated LearningUnit/card state.
- No N+1 content loading.
- No create-empty-shared-course-on-first-enrollment end state.
- No mutation of published shared course content in place.
- No Speaking/handwriting fake implementations.
- No runtime legacy fallback once migration is complete.

## 32. Acceptance

The Exercise content plan is complete when implementation can represent a real Lesson where:
- structure remains reusable/language-neutral;
- concrete source/target content belongs to one published shared course revision;
- every scored answer is deterministic and server-evaluated;
- objectives and canonical LearningUnits have real relational links;
- content changes produce a new immutable revision;
- learner progress/FSRS remain separate;
- failure of optional capabilities does not corrupt progress;
- PostgreSQL constraints protect structure without performing business workflows;
- the code can be implemented inside the existing Learning/Curriculum module without a parallel architecture.
