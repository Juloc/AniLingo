# Universal Learning languages

Issue #252 replaced the original Japanese → German Term/UserTerm/Review learning state with language-neutral Learning courses and directional cards.

## Separate concepts

These values are independent:

- UI locale: which language AniLingo itself uses.
- content language: language of the watched/read source.
- course source language: the language that is read, heard or written in a Learning course.
- course target language: the other side of the course, used for meanings and prompts.
- card mode: what the learner must recognize or produce.

A German UI can therefore host Japanese → Indonesian and German → Indonesian courses at the same time.

## Canonical data model

Per-profile learning state has exactly one store: `LearningCards` plus `LearningCardReviews`. Nothing else holds Saved/Learning/Known state, intervals, due times or review history.

`LearningUnit` represents one concept/sense. It is not tied to one language. Units created from catalog words keep a link to their `Term` through `LearningUnit.TermId` (unique).

`LearningVariant` stores one language-specific representation of a unit: language tag, text, optional reading, role (`Primary` or `Meaning`) and provenance (`Term`, `Dictionary`, `ScriptCatalog`, `Manual`).

`LearningCourse` is profile-scoped and stores a BCP-47 source/target pair plus enabled practice modes. At most one course per profile and source language is *primary*; catalog words from content in that language are saved into it. The first course created for a source language becomes primary; Settings → Learning → Courses can switch it.

`LearningCard` is directional: prompt language, answer language and mode are explicit, and each card has its own state, interval, due time, queue position and FSRS history. The unique key is course + unit + mode.

`LearningContext` is source-agnostic. It stores `SourceType`, `SourceKey`, optional `PositionKey`, language and text, so a unit can point at an Anime timestamp, Novel/Book paragraph or Manga page/region without making the scheduler media-aware.

### Terms are the lexical catalog, not learning state

`Terms` and `EpisodeTerms` remain the profile-independent subtitle/dictionary catalog: canonical form, reading, the bundled dictionary gloss (`Term.Meaning`, language `Term.MeaningLanguage` = `de`) and per-episode occurrences. They carry no per-profile state.

When a profile acts on a catalog word (Episode Known/Learn, native client API, vocabulary preparation), `LearningService` resolves the profile's primary course for the term language, creates or reuses the term's unit (source variant from the term, meaning variant from the dictionary gloss) and applies the state to every card of that unit in the course.

The "state of a term" shown on content surfaces (preparation coverage, subtitle word colouring, Home/Anime coverage, native client coverage) is the Recognition card of the term's unit in the primary course for the term language: `LearningQueries.TermStates`.

## Practice modes and card existence

- Recognition: source prompt → target answer. Always exists for every unit in a course; it is the unit's anchor card and carries its word state.
- Production: target prompt → source answer. Exists while the course enables it and the unit has a target-language variant to prompt with.
- Listening: source-language audio (on-device TTS) → answer. Exists while the course enables it.
- Writing: target prompt → typed source answer. Exists while the course enables it and a target variant exists.

Enabling a mode creates the missing cards for every unit already in the course: they copy the anchor's Saved/Ignored/Suspended state, and anchors that are Learning or Known queue the new card as new learning, so the daily new-card limit applies. Disabling a mode (or the whole course) keeps the cards and their review history but removes them from scheduling, due counts and review sessions (`LearningQueries.ScheduledCards`).

Direction is never inferred from UI locale.

## BCP-47

Course languages are normalized culture/BCP-47 tags such as `ja`, `de`, `id`, `ro`, `de-DE`, `pt-BR`, `zh-Hant` or `ja-Latn`.

Do not add enum-based lists of supported languages to the core model. Specialized language toolkits advertise enhanced capabilities, but a generic course is not blocked merely because no special toolkit exists.

`LearningLanguageToolkitRegistry` returns the specialized Japanese toolkit for `ja` and a generic toolkit for every other valid language tag. It is the single place that decides which implementation backs a language tag; other features query capability through it (`registry.Get(tag).Supports(...)` or the dependency-free `LearningLanguageToolkitRegistry.Supports(tag, capability)`) instead of re-implementing their own "is this Japanese" check.

### Toolkit capabilities are pluggable, not hardcoded to Japanese

`ILearningLanguageToolkit` (`Features/Learning/Courses/LearningLanguageToolkits.cs`) exposes, alongside `Supports(LearningLanguageCapability)`:

- `TermExtractor` (`Features/Learning/Toolkits/ITermExtractor.cs`): tokenization/term extraction, non-null exactly when `Tokenization` is supported. Japanese resolves to the existing MeCab-based `JapaneseTermExtractor`; every other language resolves to `GenericTermExtractor`, which splits on Unicode word boundaries, case-folds and drops words in a small, explicitly-curated stopword list (`GenericStopwords`) for the few languages that have one (`de`, `id`, `ro`, `en` today) — a language without a curated list gets no stopword filtering rather than a guessed one.
- `Dictionary` (`Features/Learning/Toolkits/IDictionaryLookup.cs`): bundled dictionary lookup, non-null exactly when `Dictionary` is supported. Japanese resolves to the bundled JMdict lookup (`JapaneseDictionary`); every other language has none — lookup shows the word itself (plus optional AI/translation where those capabilities exist) rather than pretending a dictionary exists.

`VocabularyService.RebuildEpisodeAsync` extracts subtitle terms per subtitle track using that track's own `Language`, resolving the toolkit (and therefore the extractor/dictionary) per track instead of assuming `ja`. Every subtitle track the import pipeline produces today is Japanese, so Japanese stays the content language in practice and the existing behaviour is unchanged; the extraction path itself no longer hardcodes it, so a track carrying another language is picked up the same way.

Readings/furigana/transliteration and the AI sentence explainer are gated by `toolkit.Supports(LearningLanguageCapability.Readings)` (`SentenceExplanationSupport.Supports`, `LanguageTextAnalyzer.Analyze`/`LookUp`, `SentencePracticeService.BuildCloze`, `SentencePracticeText.IsSuitable`, the canonical-form normalization in `LanguageInspectorService.SetStateAsync`), not a scattered `language == "ja"` check. Only the Japanese toolkit supports `Readings` today.

`ReviewAnimeContext` carries the `Language` of the sentence it quotes — the term's own source language from the subtitle track it came from, never an assumed Japanese one. `SentencePracticeService`'s no-tracked-word fallback picks subtitle sentences in the profile's enabled courses' source languages, falling back to Japanese only when the profile has no enabled course (the existing canonical default, not a hardcoded assumption).

## Kana

Kana practice uses the same model: every Kana symbol is a `Script` unit with a stable ID (`KanaCatalog.IdFor`), a `ja` variant and a `ja-Latn` romaji variant. A profile practices them in its non-primary `ja → ja-Latn` course named "Kana", created on first practice, so Japanese words are never saved into it implicitly. Kana cards are regular cards and appear in Reviews while that course is enabled. The Kana trainer page itself is available only while the ScriptTrainer capability resolves on and the profile has an enabled course with `ja` as source language (the Japanese toolkit is the one that supports `ScriptTrainer`); see `LearningModuleResolver`.

## Reviews and offline sync

FSRS replays each card's own review history (`LearningCardReviews`). The Review page, the offline review queue in the browser and the sync endpoint address cards by `CardId`.

Offline review events are idempotent per profile through the unique `(ProfileId, ClientEventId)` index. An event that carries only a `TermId` (queued by a browser before the upgrade) rates the Recognition card of that term in the profile's primary course.

The native client API `/api/client/v1` keeps its term-based contracts: term detail and `PUT /terms/{termId}/state` resolve through the same catalog-word path as the web UI.

## One-time conversion of legacy learning state

Databases created before this change stored learning state in `UserTerms`/`Reviews` keyed to `Terms`. The upgrade converts it exactly once at a migration boundary:

1. `20260926080000_AddUniversalLearningCourses` creates the Learning tables.
2. While `20260926080500_RetireLegacyLearningState` is still pending, `DatabaseMigrationBridge` migrates up to step 1 and copies the legacy state in one transaction with deterministic IDs (card = UserTerm ID, unit = Term ID, review = Review ID), so an interrupted upgrade can be retried.
3. `RetireLegacyLearningState` drops `UserTerms` and `Reviews`. It refuses to run (and rolls back) if any UserTerm was not converted, so applying migrations without the bridge can never discard progress.

Mapping:

- every learned Term becomes a `Word` unit with a source variant (canonical form + reading) and, when present, a `de` meaning variant;
- every profile gets a primary `<term language> → de` Recognition course for the languages it studied;
- every UserTerm becomes that course's Recognition card with the same state, interval, due time, learning start and queue position;
- every Review becomes a card review with the same ID, rating, times and offline `ClientEventId`; reviews without a UserTerm had no schedulable card and are not carried over;
- legacy Kana Terms (language `ja-kana`) become `Script` units in the profile's Kana course and are removed from the word catalog.

Data of the pre-account `default` profile is moved to the owner once the owner account exists; owner data wins where both profiles hold the same course pair and card.

After the conversion there is no runtime read or write path to the legacy tables.

## Subtitle acquisition follows the content language

Issue #231's remaining part (noted in PR #354) was that subtitle *acquisition* - `SubtitleImportService`, `SubtitleSidecarLocator`, embedded-track selection and Jimaku - stayed hardcoded to Japanese even though Learning courses already support arbitrary BCP-47 pairs. It now resolves and follows the same content language everywhere, defaulting to Japanese only when nothing is configured.

### One resolver, reused everywhere

`LearningContentLanguageResolver` (`Features/Learning/Courses/LearningContentLanguageResolver.cs`) is the single place that answers "what language is this content in". Media files, episodes and subtitle tracks are shared across every profile (they are not partitioned per profile), so acquisition needs one answer even when profiles disagree:

1. Consider every enabled, *primary* `LearningCourse` across every profile - a primary course is the one its profile designated to receive catalog words from content in that source language, so its `SourceLanguage` is the language that profile is acquiring subtitles to learn from.
2. Group by `SourceLanguage`; the language used by the most such courses wins.
3. Ties break on the language whose oldest matching course was created first, then on ordinal string comparison of the tag, so the result is fully deterministic.
4. With no enabled primary course anywhere, the canonical default is Japanese.

`SubtitleImportService` resolves the target language once per operation (import, queue, coverage, missing-episode listing) and threads it through sidecar lookup, embedded-track selection and the track's stored `Language`; nothing downstream re-implements its own "which language" check.

### BCP-47 alias table

`SubtitleLanguageAliases` (`Features/Subtitles/SubtitleLanguageAliases.cs`) maps a BCP-47 tag to the extra tokens that name it in file names and embedded-track language tags - ISO 639-1/639-2 codes and common English/native names (`ja` → `jp`/`jpn`/`japanese`/`日本語`, `de` → `deu`/`ger`/`german`/`deutsch`, `id` → `ind`/`indonesian`, `en` → `eng`/`english`, `ro` → `ron`/`rum`/`romanian`, plus entries for the other languages the sidecar locator already recognized). Extend it by adding an entry; a language without one still matches its bare tag alone. `NameTokensFor` exposes the subset safe to look for as a free-form substring of a stream title (short ISO codes are excluded - they collide with ordinary words; full names and non-ASCII scripts are kept regardless of length).

`SubtitleSidecarLocator.Classify`/`FindCandidates` and `EmbeddedSubtitleExtractor.MatchesLanguage`/`SelectPreferredTextStream` both take a `targetLanguageTag` and call into this table instead of keeping their own Japanese/English/... token lists. The Japanese-specific static overloads (`SelectPreferredJapaneseTextStream`, `IsJapanese`, `ExtractPreferredJapaneseAsync`) remain as thin wrappers over the generalized ones so existing Japanese-path call sites and tests are unaffected.

### Untagged sidecar files

Japanese keeps its long-standing kana heuristic (`SubtitleImportService.ContainsJapaneseDialogue`): an untagged file is trusted when at least a third of its cues contain kana, alongside explicitly `ja`-tagged files. No equivalent content heuristic exists for other languages, so for every other target language an untagged file is only offered as a candidate when no file tagged for that target language was found for the same episode, and never when the file is explicitly tagged for a *different* known language (`SubtitleLanguageAliases.IsTaggedAsOtherLanguage`) - this never guesses a language by script.

### Embedded tracks

`EmbeddedSubtitleExtractor.SelectPreferredTextStream`/`MatchesLanguage` prefer a track whose language tag (or, absent one, title) matches the target language over an untagged/other-language one, then a full dialogue track over forced/signs-only, the default track, and finally stream order - the same preference Japanese always used, generalized to any target language via the alias table. Whisper's local audio transcription fallback and its audio-stream selection (`SelectPreferredJapaneseAudioStreamIndex`, `TranscribeJapaneseAudioAsync`) stay Japanese-only: the bundled model and its `-l ja` invocation are not generalized, so that stage is skipped (see below) once the resolved content language is not Japanese, rather than mis-transcribing other-language audio as Japanese.

### Jimaku and Whisper stay Japanese-only, by design

Jimaku indexes Japanese fansub releases; it is not generalized to other languages. `LearningTextFallbackPolicy.Build(jimakuConfigured, jimakuEligibleForLanguage)` excludes the Jimaku stage whenever the resolved content language is not Japanese, even if an API key is configured, and `SubtitleImportService.PrepareLearningTextAsync` skips the Whisper stage the same way and logs the reason once per attempt. Local subtitles and embedded tracks are unaffected and are tried for every language.
