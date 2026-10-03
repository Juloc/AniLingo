# Reading Detail — Book / Light Novel / Manga — Clean Design

Status: **approved shared UX direction for planning and mockups**.

This is the binding consumer detail-page family for:

- `Work(MediaType=Book)`
- `Work(MediaType=LightNovel)`
- `Work(MediaType=Manga)`

These media types intentionally share one page architecture and shared components. Differences are variants, not separate independently-designed screens.

The visual reference currently stored as `desktop-light.png` is sufficient as the first approved Light-mode mockup direction. The text specification remains authoritative when image and spec differ.

## Purpose

The page should answer:

1. What is this work?
2. Where did I stop?
3. What part/volume/chapter should I open?
4. Which editions and languages are available?
5. Is the preferred language original, official, generated, partial or unavailable?
6. What related works exist?

The normal user page must remain a reading surface, not an import/provider/admin screen.

## Canonical model

All three variants consume the same canonical hierarchy:

```text
Work
├─ Volume / Chapter structure
├─ Edition
│  └─ Version
│     └─ Asset
│        └─ File
└─ WorkRelation
```

User state is separate:

- MediaProgress
- Bookmark
- Highlight/annotation where applicable
- ReaderPreference
- personal Watchlist/Reading List/Favorite/Collection membership

No Book-, LightNovel- or Manga-specific parallel core may become the permanent source of truth.

## Shared page structure

One direct scrolling page. No major content tabs.

1. Hero
2. Continue Reading
3. Parts / Volumes
4. Selected Part / Volume content
5. Editions & Languages
6. About
7. Optional Characters / Contributors where useful
8. Related Works
9. More Like This
10. Details only when useful

Empty sections disappear.

## 1. Hero

Shared content:

- cover
- optional backdrop/banner
- canonical title
- optional native/original title
- media type
- publication status
- year/range
- small genre/theme set
- short synopsis
- primary action is state-dependent: `Read` / `Continue Reading` when a readable Edition is available; `Request` when unavailable and requestable; otherwise show the existing live Request state
- personal Reading List/Favorite action where supported
- optional Collection action
- overflow for secondary consumer actions

Compact facts may include:

- author/story creator
- illustrator/artist where applicable
- publisher/imprint
- number of parts/volumes
- original language
- available reading languages

Do not duplicate the same cover twice in the Hero.

## 2. Continue Reading

When progress exists show:

- current Part/Volume if meaningful
- current Chapter
- exact resume position
- compact progress indicator
- primary Continue action

Exact resume and completed progress are different.

Examples:

### EPUB/Text
`Part 2 · Chapter 7 · exact text locator`

### Manga
`Vol. 4 · Chapter 18 · Page 12 of 38`

Opening a chapter must never automatically mark it completed.

When there is no progress, the Hero uses `Start Reading` and the empty progress card is omitted.

## 3. Parts / Volumes

This is one shared structural component with context-sensitive labeling.

### Book

A Book may show:

- Parts
- Books/Sections
- Volumes
- or no structural rail at all

If the source work has meaningful named parts, the UI can label the rail **Parts**.

Important: `Part` is a presentation label, not automatically a new canonical entity. It may map to canonical Volume structure or a presentation group. Do not create a separate BookPart core only for UI wording.

Single-volume/single-part books omit the rail.

### Light Novel

Usually label the rail **Volumes**.

Volume covers are prominent when available.

### Manga

Usually label the rail **Volumes**.

Volume covers are prominent and selected state must be obvious.

### Shared behavior

Desktop:
- horizontal rail or compact rail/list according to width
- selected item has clear accent state

Mobile/Tablet:
- horizontal swipe rail
- touch-first
- no compressed desktop table

Presentation Groups may organize canonical chapters/volumes visually but never replace their identity.

## 4. Selected Part / Volume content

Same component family, different density.

### Book

Usually compact chapter/section list.

For a book without meaningful chapter browsing, this can collapse to a simple `Open in Reader` flow.

### Light Novel

Compact chapter list for the selected volume/part.

### Manga

Chapter list may be more visual and can include page count or thumbnail.

Common row state:

- number/order
- title
- read / in progress / unread
- availability
- preferred-language state when relevant
- resume marker
- open/read action

Do not show one giant permanent all-work chapter table for long works.

## 5. Editions & Languages

Shared canonical `Edition -> Version` UI.

Each row can show:

- language
- original / official translation / generated translation
- edition/source label
- complete / partial
- requested/downloading/translating where relevant

Preferred reading language appears first or is clearly marked.

Generated translations must never look identical to official translations.

Actions when allowed:

- switch edition/language
- request missing edition/language
- translate missing content
- view additional editions

Switching language never overwrites the original edition.

## 6. About

Shared:

- description/synopsis
- Read More for long text
- compact genres/themes

Avoid metadata dumps.

## 7. Optional media-specific content

The shared page allows optional blocks without changing the page family.

### Book
Possible:
- author/contributor details
- series/order context

### Light Novel
Possible:
- illustrator
- characters when metadata is useful

### Manga
Possible:
- story creator
- artist
- characters when useful

These sections disappear when metadata is not meaningful.

## 8. Related Works

Canonical WorkRelation only.

Examples:

- Anime adaptation
- Manga adaptation
- Light Novel source
- sequel/prequel
- side story
- spin-off
- alternative version

Keep distinct from recommendations.

## 9. More Like This

Simple Discover-style cards.

Do not use heavy Library progress cards here.

## Reader differences

The detail page stays shared; the dedicated Reader handles format differences.

### Book / Light Novel

Reader may support:

- EPUB/PDF/text
- typography
- highlights/notes
- TTS when supported
- exact text locator

### Manga

Reader may support:

- page/image navigation
- LTR/RTL reading direction
- page fit/zoom
- exact chapter/page position

Reading direction is a Reader preference/presentation concern, not a reason for a separate detail-page design.

## Progress semantics

Canonical progress must support:

- CurrentItem
- ResumePosition
- CompletedThrough
- ProviderProgress where integrated

Example:

```text
Current chapter: 18
Resume: page 12
Completed through: 17
Provider progress: 17
```

Provider state must never replace local exact resume state.

## Availability states

All variants support:

- Available
- Partial
- Requested
- Downloading/Preparing
- Translating
- Unavailable
- Storage offline

These are compact consumer summaries. Do not expose release scores, indexer rows, raw file paths or import diagnostics.

## Light / Dark

Both are first-class.

The current stored mockup establishes the initial **Light Clean Design** direction:

- white/soft-gray surfaces
- purple Jularr accent
- artwork-dominant Hero
- restrained status colors
- compact rounded cards
- readable high-contrast typography

Dark later uses the same hierarchy and component structure, not a redesign.

## Desktop

- wide Hero
- integrated cover
- Continue block near the top
- structural rail visible
- selected content list
- compact Editions & Languages
- Related/Similar rails
- hover may expose secondary actions, never required actions

## Mobile

- compact vertical Hero
- strong Read/Continue action
- swipeable structural rail
- touch-sized chapter rows
- edition/language sheet or compact card
- Related/Similar horizontal rails
- no desktop table

## Tablet

- touch-first
- portrait close to Mobile
- landscape may use adaptive two-pane content
- rail remains swipeable

## TV

TV is browse/focus-first.

Show:

- identity
- Continue/Start state
- Parts/Volumes
- selected chapters where useful
- Related/Similar

Reading should hand off to phone/tablet unless a TV Reader is separately approved.

## Loading / Empty / Error / Partial

### Loading
Skeletons preserve Hero, progress, structural rail and list geometry.

### No local edition
Keep metadata and relations visible; show `Request` when permitted. Never show a separate consumer Add acquisition action.

### No structure
Do not invent Parts/Volumes. Go directly to Reader/action state.

### Partial
Keep usable sections alive if recommendations/provider/storage state is degraded.

### Storage offline
Cached metadata/artwork/progress stays visible; unavailable content is marked clearly.

### Error
Page-level error only when canonical Work resolution fails. Optional section errors stay local.

## Accessibility / input

- semantic headings
- keyboard reachable desktop actions
- visible focus
- touch-sized mobile/tablet controls
- strong TV focus treatment
- progress/state not conveyed by color alone

## Must not implement

- no separate consumer designs for Book, Light Novel and Manga unless a future requirement truly diverges
- no parallel Book/LN/Manga core identities
- no BookPart model created only for the word “Part”
- no giant all-chapter table by default
- no admin/provider matching forms on the consumer page
- no raw paths/files/release candidates/indexer scores
- no generated translation presented as official
- no chapter completion triggered merely by opening it
- no provider progress replacing exact local resume
- no empty placeholder sections
- no compressed desktop tables on Mobile
- no full TV Reader without separate approval

## Visual reference

Current first visual reference:

- `docs/mockups/reading-detail/desktop-light.png`

It is accepted as the initial shared Light-mode direction for Book, Light Novel and Manga.

Future mockups only need to demonstrate genuinely different states/platforms, not three copies of the same page:

- mobile-light
- desktop-dark
- mobile-dark
- tablet where needed
- TV focus state

Text spec wins on conflict.
