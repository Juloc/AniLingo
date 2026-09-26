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
- Vorlesen (only when the document supports read-aloud)
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

Shared Reader extensions mount through `root.readerShell` (settings command,
reset/source badges, overflow and mobile actions) instead of patching source
readers. Read-aloud (`reader-tts.js`, see `docs/TTS.md`) is the first one.

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
Highlight marks wrap the text nodes of a pristine copy of the paragraph, so
inline EPUB markup (emphasis, ruby) survives highlighting.

### Series → volume → chapter

Every `NovelWork` is a series, every chapter belongs to exactly one
`NovelVolume` (`NovelChapters.VolumeId`, required, cascade). There is no
chapter without a volume and no second runtime path:

| Source | Volumes |
| --- | --- |
| Narou/Ncode web novel | one implicit `web` volume (the chapter index) |
| Books catalog import | one implicit `book` volume |
| EPUB light novel | one `epub` volume per imported EPUB file |

Migration `AddNovelVolumes` moved every existing work into one implicit volume
once (NovelChapters is rebuilt with foreign keys disabled so no translation,
progress, bookmark or highlight is touched). Chapter `Number` stays the
series-wide reading order (volume order, then spine order) and is renumbered
when a volume is inserted between existing ones; progress and annotations
reference chapter ids and survive renumbering. Manual segment and anime
mappings are keyed by local chapter numbers and may need review after a volume
is inserted in the middle of a series; automatic segments are reconciled on
every import.

**Decision: `NovelVolume`, not Book Edition/File.** A Book Edition (#291) is an
alternative manifestation of the *same* content (language/ISBN, one primary
edition per work); a volume is a *sequential part* of a series. Reusing
Editions for volumes would break the primary-edition semantics. What is shared
is the single EPUB path: `EpubBookParser` (Features/Books) is the only EPUB
parser, and `NovelVolumeContent.SyncChaptersAsync` is the only writer of
volume chapters — used by the Books import (implicit volume) and by
`NovelEpubImportService` (EPUB volumes).

### EPUB light-novel import

`NovelEpubImportService` is the one import path for uploads (Novels → Add
novel, or "Add or replace volumes" on an EPUB series) and for the reading
inbox: the Books inbox path (Books → Integrations / `Books:InboxPath`) with a
`light-novels` subfolder. EPUBs directly in `light-novels` resolve their series
from metadata; EPUBs in `light-novels/<Series>/` belong to that series.

- Series: explicit target series, else inbox folder name, else calibre
  `series` / EPUB 3 `belongs-to-collection`, else the title without its volume
  marker. Series identity is a hash of the normalized name.
- Volume number: calibre `series_index` / `group-position`, else parsed from the
  title (`第3巻`, `Vol. 3`, `（3）` …), else next free number.
- Volume identity: package unique identifier, else ISBN, else title+author.
  A file with an unknown identity but an existing EPUB volume number replaces
  that volume. An unchanged file (same SHA-256) is a no-op.
- Chapter identity inside a volume: spine document path, then identical text,
  then unique title. Changed chapters are updated in place (translations are
  invalidated by source hash), unchanged ones are not touched, removed ones are
  deleted with their notes.
- Every file succeeds or fails on its own with a diagnostic (not a ZIP,
  missing container/package, malformed XML, no readable text, too large,
  DRM-encrypted). Encrypted EPUBs are rejected; DRM is never removed.
- Source files are only opened for reading. Normalized chapter text and block
  structure live in SQLite; covers and illustrations are cached
  content-addressed under `/data/novels/volumes/<volume>/` and served by
  `/Novels/Asset/{volume}/{file}` (raster images only, `nosniff`).
- After import the series goes through the canonical AniList matching
  (`NovelMetadataService.AutoMatchAsync`). EPUB volume numbers feed the existing
  reading-segment planner/resolver, so a segment with a volume range maps local
  volumes to AniList `progressVolumes`; implicit web/book volumes never do.

### EPUB content rendering

`EpubBookParser` converts chapter XHTML by whitelist into paragraphs plus
`NovelContentBlock`s (paragraph, heading, image) with inline runs (text,
ruby reading, emphasis, strong). Scripts, styles, event handlers, links,
iframes, SVG documents and external/`data:` resources never survive; only
internal JPEG/PNG/GIF/WebP images are kept. The text blocks equal the
paragraphs of `OriginalText`, so anchors, highlights and translations keep
their paragraph indexes and offsets. The reader renders runs with encoded text,
`<em>` (sesame emphasis dots in Japanese), `<strong>` and `<ruby>`; readings
are drawn from `rt[data-rt]` by CSS so the paragraph DOM text stays the plain
text. Illustration-only pages open the next text chapter.

### Front-end modules

`novel-reader.js` is the single bootstrap: it owns the shared reader context,
language view state and the restore lifecycle, then starts
`novel-position.js`, `novel-annotations.js`, `novel-chapter-drawer.js` and
`novel-translation.js`, which only register factories. Chrome, settings and
paged mode stay in `reader-shell.js` and `reader-personalization.js`.
Styles: `novels.css` (reader surface), `novel-reader-panels.css` (drawer,
notes, highlights, selection) and `novel-library.css` (library, work detail,
chapter preparation).
