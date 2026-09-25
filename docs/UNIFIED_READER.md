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
