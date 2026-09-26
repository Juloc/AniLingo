# Unified Reader

AniLingo exposes one Reader product. Books, Light Novels and Web Novels use the
same shell, settings surface, preference store, theme runtime and interaction
rules. Source-specific pages are adapters that provide content, navigation,
translations and annotations.

## Document model

`ReaderDocumentDescriptor` describes the content without relying on its URL.
It supplies:

- content type
- layout kind
- genres
- capability flags

Current reflowable types are Book, Light Novel and Web Novel. Manga and fixed
documents have capability presets ready for future image/fixed renderers.

## Capabilities

The settings surface is capability-driven. Reflowable documents expose
typography, continuous and paged reading, text selection, annotations,
auto-scroll and two-page layouts. Future image/fixed documents can omit
typography and expose zoom/page-layout controls instead.

Unsupported controls must not be rendered just because another document type
uses them.

## Preference cascade

Reader settings are stored only in the existing `ReaderPreference` store.
Every field is nullable at a scope so inheritance remains field-level.

Effective order, low to high:

1. system content-type preset
2. profile global scope: `default`
3. profile type scope: `type:<content-type>`
4. matching profile genre scopes: `genre:<priority>:<genre>`
5. profile work scope: `work:<id>`

Matching genre scopes are deterministic: greater numeric priority wins a field;
ties use the normalized genre key. A genre scope affects only fields it
actually overrides.

The UI can reset one field at a scope without removing unrelated overrides.
If the final field is reset, the empty preference row is deleted.

## Built-in presets

System presets provide a useful first experience, not hard limits:

- Book: paged, classic chapter treatment, cream paper, conservative artwork
- Light Novel: paged, Light Novel heading, embedded-image friendly, automatic
  genre artwork
- Web Novel: continuous, auto-scroll capable, modern chapter treatment
- Manga: image-sequence capabilities, page/spread-oriented
- Fixed document: fixed-page capabilities, paging/zoom-oriented

Profiles can override these globally, by type, by genre and per work.

## Shared settings surface

Both `/Novels/Read` and `/Books/Read` render
`Pages/Shared/_ReaderSettingsPanel.cshtml`.

The runtime reorganizes the canonical controls into:

- Lesen
- Text
- Aussehen
- Defaults

Scroll/Pages is a segmented primary choice. Scroll-only and page-only controls
are contextual. Fine visual effects remain secondary. The Defaults tab chooses
where subsequent edits are persisted: this work, this content type, one of the
document genres, or the profile global default.

Each effective setting exposes its source and can be reset to inherit again.

## Chrome behavior

`reader-shell.js` owns chrome visibility for every unified reader:

- visible on initial chapter load
- progress restoration does not count as user scroll
- meaningful downward scroll hides chrome
- upward scroll reveals it
- center tap toggles chrome
- desktop top-edge pointer reveals chrome
- open settings/drawers prevent auto-hide
- mobile uses a bottom action bar with large targets

In paged mode the shared shell routes swipes and left/right edge taps to the
source reader's page-turn handler. Text selection takes precedence.

The thin progress indicator is intentionally independent of chrome.

## State ownership

Durable typography, paper, theme, mode and layout settings belong to
`ReaderPreferenceStore`.

Legacy Novel localStorage ownership for font size, line height, text width and
theme is removed. Local storage may still hold ephemeral/device conveniences
such as the selected language view or wake-lock preference; those do not
compete with ReaderPreferences.

## Source adapters

For now the existing Books and Novels Razor PageModels remain source adapters
because they own different translation and annotation endpoints. They both
supply the shared descriptor and render the same Reader settings/shell.

Further extraction must move common reflow text rendering/navigation behind a
ReaderCore adapter contract rather than creating another reader UI.

## Novel adapter

The Novel adapter keeps the canonical `NovelWorks`, `NovelChapters`,
`NovelTranslations`, `NovelProgress`, `NovelBookmarks` and `NovelHighlights`
tables (shared with Books). Application services are split by responsibility;
Razor PageModels orchestrate them:

| Responsibility | Owner |
| --- | --- |
| Source/import commands, the only code that calls `INovelSourceProvider` | `NovelImportService` |
| Bounded library, work detail and reader projections, chapter navigation | `NovelCatalogQueries` |
| Per-profile reading progress and resume anchors | `NovelProgressService` |
| Profile-scoped bookmarks and highlights | `NovelAnnotationService` |
| Chapter download, AI translation and AI episode mapping jobs (Operations queue) | `NovelJobs` |
| AI translation cache, AniList metadata, episode mappings | `NovelTranslationService`, `NovelMetadataService`, `NovelMappingService` |

Anchors and annotations resolve against the paragraph layout the reader
renders: Japanese source text, or the current German translation (same prompt
version and source hash).

### Bounded reader load

`/Novels/Read/{chapterId}` loads a constant number of queries regardless of
work size: one chapter projection (text, current translation, adjacent
chapter ids), reader preferences, the chapter's anime mappings, progress, the
current chapter's bookmarks/highlights and aggregate counts of notes in other
chapters. It does not embed the chapter index or work-wide notes.

- Chapter drawer: `?handler=Chapters` returns at most 100 chapters around the
  current chapter, pages with `after`/`before` (chapter number) and searches by
  number or title in the database.
- Notes panel: the current chapter's notes render with the page; notes from
  other chapters load on demand with `?handler=WorkNotes&kind=bookmarks|highlights&offset=n`
  in pages of 40.
- Library counts (chapters, cached text, current German translations) are
  database aggregates.

### Chapters without cached text

A reader GET never contacts the source provider. When a chapter's text is not
cached, the page shows a preparation state. Its action (`PrepareChapter`)
queues a `novel-chapter-download` operation and redirects back to the same
reader URL (including `bookmark`/`highlight` jump targets) with the operation
id; the page polls `ChapterStatus` and opens the reader once the text exists.

### Highlights

Overlapping highlights are valid. An exact duplicate range returns the existing
highlight. The client renders each paragraph as flat segments split at every
highlight boundary; overlapping segments carry all highlight ids and a depth
for stronger tinting, so no highlight is dropped by nested DOM ranges.

### Front-end modules

`novel-reader.js` is the single bootstrap: it owns the shared reader context,
language view state and the restore lifecycle, then starts
`novel-position.js`, `novel-annotations.js`, `novel-chapter-drawer.js` and
`novel-translation.js`, which only register factories. Chrome, settings and
paged mode stay in `reader-shell.js` and `reader-personalization.js`.
Styles: `novels.css` (reader surface), `novel-reader-panels.css` (drawer,
notes, highlights, selection) and `novel-library.css` (library, work detail,
chapter preparation).
