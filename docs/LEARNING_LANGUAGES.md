# Universal Learning languages

Issue #252 tracks the migration from the original Japanese → German Term/Meaning model to language-neutral Learning courses.

## Separate concepts

These values are independent:

- UI locale: which language AniLingo itself uses.
- content language: language of the watched/read source.
- course source language: language shown or heard as one side of a Learning course.
- course target language: the other side of the course.
- card direction/mode: what the learner must recognize or produce.

A German UI can therefore host Japanese → Indonesian and German → Indonesian courses at the same time.

## Canonical data model

`LearningUnit` represents one concept/sense. It is not tied to one language.

`LearningVariant` stores one language-specific representation of that unit: language tag, text, optional reading, role and provenance.

`LearningCourse` is profile-scoped and stores a BCP-47 source/target pair plus enabled practice modes.

`LearningCard` is directional. Prompt language, answer language and practice mode are explicit. Recognition and production therefore have separate card IDs and can develop separate review histories.

`LearningCardReviews` is the new review-history target for directional cards.

`LearningContext` is source-agnostic. It can point a unit at an Anime timestamp, Novel/Book paragraph, Manga page/region or future source without making the scheduler aware of media-specific tables.

## Practice modes

- Recognition: source prompt → target answer.
- Production: target prompt → source answer.
- Listening: source-language audio → answer.
- Writing: target-side prompt → written source-language answer.

A course can enable any combination. Direction is never inferred from UI locale.

## BCP-47

Course languages are normalized culture/BCP-47 tags such as `ja`, `de`, `id`, `ro`, `de-DE`, `pt-BR` or `zh-Hant`.

Do not add enum-based lists of supported languages to the core model. Specialized language toolkits may advertise enhanced capabilities, but a generic course is not blocked merely because no special toolkit exists.

## Legacy bridge

The migration is additive and intentionally keeps the old `Terms`, `UserTerms` and `Reviews` tables while the UI/scheduler transition is completed.

For every existing Term:

- create a language-neutral unit;
- create a source-language variant from Term.Language/Canonical/Reading;
- create a German target variant from the old Meaning field when present.

For every existing profile/user term:

- create an imported source → German course;
- create a Recognition card preserving state, interval, due time, queue position and legacy UserTerm ID.

For every existing Review:

- copy its rating, client event ID, reviewed time and next due time into LearningCardReviews.

This means later code can switch scheduling to directional cards without losing historical FSRS input.

## Next integration

After the foundation is merged, Learning settings and the Hub should expose course creation/editing. Content/player/reader language tools then create or connect units/variants through the selected course rather than writing a single global Meaning string.