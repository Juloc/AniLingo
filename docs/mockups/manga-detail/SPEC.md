# Manga Detail — Clean Design

Status: **planned UX baseline; ready for visual mockup review**.

This specification is authoritative for the Manga detail screen. Visual mockups may refine spacing, composition and artwork treatment, but must not change the information hierarchy, canonical identity model or state semantics defined here.

## Purpose

Canonical consumer detail page for a single `Work(MediaType=Manga)`.

The page answers, in this order:

1. What is this manga?
2. Where did I stop reading?
3. Which volume/chapter should I read next?
4. Which editions/languages are available?
5. Is my preferred language official, generated, partial or unavailable?
6. What related canonical works exist?

The screen is a consumer surface. Provider matching, file paths, rescans, rename/reorganize, mapping repair and other owner operations belong to Admin/Manage surfaces.

## Canonical data contract

The target page consumes the universal media model:

```text
Work(Manga)
├─ Volume
│  └─ Chapter
├─ Edition
│  └─ Version
│     └─ Asset
│        └─ File
└─ WorkRelation
```

User state is separate:

- canonical `MediaProgress`
- bookmarks/highlights where supported
- favorite/watchlist/collection membership
- reader preferences

The page must not treat legacy `MangaSeriesItem`, `MangaChapterItem` or `MangaProgressItem` as the permanent target identity model. Existing routes/services may act as migration adapters until canonical Work/Volume/Chapter/progress wiring is complete.

### Progress semantics

Exact resume state and completed progress are different.

Example:

```text
Current chapter: 18
Resume position: page 12
Completed through: chapter 17
Provider progress: chapter 17
```

Opening chapter 18 must not mark chapter 18 completed.

The primary Continue action resumes the exact saved chapter/page position. Provider progress, where shown at all, is secondary and must never replace local resume state.

## Page structure

Single scroll page. No main tab bar.

Recommended order:

1. Hero
2. Continue Reading
3. Volumes
4. Selected Volume / Chapters
5. Editions & Languages
6. About
7. Related Works
8. More Like This
9. Details, only when useful

Sections with no meaningful content disappear instead of rendering empty cards.

## 1. Hero

The Hero establishes identity and the primary reading action.

### Content

- cover artwork
- optional backdrop/banner
- canonical title
- optional native/original title
- media type: Manga
- publication status
- start year / relevant publication range where known
- compact genre/theme set
- short synopsis
- primary Read / Continue Reading action
- Favorite / My List
- Collection action when useful
- overflow for secondary consumer actions

### Compact information strip

Show only high-value facts:

- author / story creator
- artist where distinct
- publisher where useful
- total known volumes
- total known chapters
- original language
- preferred reading language availability

Do not repeat the same values later unless the later section adds detail.

### Artwork fallback

Priority:

1. canonical/provider backdrop when available;
2. derived blurred/gradient treatment from the cover;
3. clean neutral gradient.

Do not stretch low-resolution page art into a large backdrop.

## 2. Continue Reading

This is the main personal-state block below the Hero.

When progress exists, show:

- current volume if known
- current chapter
- exact page progress, e.g. `Page 12 of 38`
- compact percentage/progress bar
- last-read context only if useful
- primary `Continue Reading`

The action opens the Reader at the exact canonical resume locator.

When no progress exists:

- do not show an empty progress card;
- Hero primary action becomes `Start Reading`;
- optionally show a compact `Start with Volume 1 / Chapter 1` destination when structure is known.

If the current chapter/file is unavailable but another edition can satisfy the same canonical chapter, resolve through availability rather than resetting progress.

## 3. Volumes

Volumes are the primary structural navigation when real volume structure exists.

Each volume item may show:

- cover/artwork when available
- volume number
- short title when useful
- publication date/year when known
- chapter range/count
- availability state
- compact read progress

### Desktop

Use a horizontal volume rail or compact vertical rail depending on available width. The selected volume has a clear accent/focus state.

### Mobile

Use a horizontal swipe rail. Do not compress the desktop presentation into tiny cards.

### No-volume manga

If the work genuinely has no useful volume structure, omit the volume rail and show the chapter section directly.

### Presentation Groups

Optional presentation groups from the shared media-type-agnostic presentation model may organize volumes/chapters for display.

Examples:

- Part 1 / Part 2
- Story Arc
- Omnibus grouping

Presentation groups only change consumer grouping. They must never create or replace canonical Volume/Chapter identity.

## 4. Selected Volume / Chapters

Show chapters scoped to the selected volume/group by default.

Each chapter row/card should answer:

- chapter number
- title, if meaningful
- read/in-progress/unread state
- local availability
- preferred language availability where relevant
- current/resume marker
- primary open/read action

Optional secondary data:

- page count
- release/publication date
- compact translation state

### Chapter density

Desktop may use a compact list.

Mobile uses touch-sized stacked rows.

Do not render one giant permanent all-series chapter table for long-running manga.

For very large chapter counts:

- virtualize/paginate if implementation needs it;
- preserve the selected volume/group and scroll context;
- provide quick jump/search only when it solves a real navigation problem.

## 5. Editions & Languages

This section represents canonical `Edition -> Version` availability, not arbitrary language labels on the Work.

Preferred language appears first.

Each compact row shows:

- language
- edition/source label
- official/generated distinction
- complete/partial availability
- local/requested/downloading/translating state where applicable

Examples:

- Japanese — Original — Complete
- German — Official Translation — Vol. 1–8 available
- English — Official Translation — Complete
- German — Machine Translation — Vol. 9 partial

Generated translations must be visually distinguishable from official editions without loud badge clutter.

### Actions

When permitted:

- switch reading edition/language
- request missing edition/language
- translate missing content
- view additional editions

Switching edition/language never overwrites the original edition or progress identity. Resume should resolve to the equivalent canonical chapter where mapping is known.

Do not expose raw release candidate names, indexer scores, storage paths or import details.

## 6. About

Show:

- synopsis/description
- compact genre/theme list
- creator credits where useful

Long text uses `Read more`.

Avoid turning this into a metadata dump.

## 7. Related Works

Use canonical `WorkRelation` links.

Examples:

- source Light Novel
- Anime adaptation
- sequel
- prequel
- side story
- spin-off
- alternative version

Show relation type compactly and keep this distinct from recommendations.

## 8. More Like This

Simple recommendation row using Discover-style cards.

No progress-heavy Library-card design here.

## 9. Details

Only render useful secondary facts, for example:

- original publication dates
- serialization/publication status
- publishers/imprints
- alternate titles

Raw provider IDs and technical storage information are not consumer details.

## User actions

Primary:

- Start Reading
- Continue Reading
- choose volume
- open chapter
- switch edition/language

Secondary:

- Favorite / My List
- add/remove Collection
- Request missing content
- Translate missing content when policy allows
- open Related Work
- open recommendation

Navigation that visually means Back must use shared context-aware back behavior and preserve originating Library/Discover context where practical. A fixed destination link must be labeled as that destination, not `Back`.

## Availability and request semantics

The page may represent:

- Available
- Partial
- Requested
- Downloading/Importing
- Translating
- Unavailable
- Storage offline

These are consumer summaries over canonical acquisition/library state.

Do not expose the Admin acquisition pipeline itself.

For partial works, the page should make the boundary understandable, e.g. `Volumes 1–8 available`, without listing raw missing-file diagnostics.

## Light / Dark

Both themes are first-class.

### Light

- neutral elevated surfaces
- strong readable title/body contrast
- artwork supplies most visual color
- subtle accent on selected volume/current chapter
- generated/partial states remain semantic but restrained

### Dark

- avoid pure-black card walls
- retain separation between Hero, rails and content surfaces
- maintain readable muted metadata
- artwork gradients must not reduce text contrast

No theme-specific information differences.

## Desktop

Target composition:

- wide Hero with cover integrated into the left/content edge, not a duplicated second cover
- title/actions/content to the right
- Continue Reading directly after Hero
- visible volume rail
- selected-volume chapter list below/beside depending on width
- Editions & Languages as compact rows/cards
- Related/Similar horizontal rails

Mouse hover may reveal secondary chapter/card actions, but every important action must also be reachable without hover.

## Mobile

Target composition:

- compact vertical Hero
- cover integrated cleanly
- large Read/Continue action
- concise metadata; overflow for secondary actions
- swipeable volume rail
- selected-volume chapters as touch rows
- edition/language selector as sheet when multiple choices exist
- related/recommendation rails remain horizontal

No dense desktop tables, tiny metadata columns or hover-only controls.

Reader opens as the dedicated Reader experience rather than embedding full reading controls in the detail page.

## Tablet

Touch-first adaptive layout.

Preferred behavior:

- wider Hero than phone
- volume rail remains easy to swipe
- chapter section may use two-pane behavior when width supports it
- edition/language selection can use anchored popover or sheet
- no assumption of mouse hover

Portrait tablet should remain closer to Mobile; landscape may approach Desktop.

## TV

TV detail is browse/focus-first, not a full manga Reader.

Show:

- strong cover/title identity
- compact metadata
- Continue/Start state
- volume rail
- selected volume chapters where useful
- Related Works / recommendations

Focus state requires obvious ring/glow plus scale/lift consistent with the TV shell.

Primary reading action should offer handoff to phone/tablet where supported. Do not ship a full TV manga Reader without a separately approved Reader-TV design.

## Loading state

Use skeletons that preserve final geometry:

- Hero artwork/title/action skeleton
- Continue card placeholder when user-state load is pending
- volume rail skeleton
- short chapter-list skeleton

Do not block the entire page on optional recommendation/provider metadata.

## Empty states

### No local edition

Still show canonical metadata and relations.

Primary state becomes:

- Request / Add when permitted
- unavailable explanation when user cannot request

Do not render an empty chapter/file table.

### No volume metadata

Fall back to ordered chapters.

### No chapters yet

Show concise availability/request state. Do not invent Volume 1 / Chapter 1.

### No related/recommendation data

Hide the sections.

## Partial states

Must support independently:

- metadata loaded while artwork is missing
- canonical structure loaded while local availability is still loading
- some volumes available, others missing
- preferred language partial
- preferred language unavailable but another edition available
- translation queued/running
- storage root offline while cached metadata/artwork remains usable
- recommendation provider unavailable
- provider progress unavailable while local progress remains valid

Optional failures must not collapse the whole detail page.

## Error states

### Page-level error

Only for failure to resolve the canonical Work itself.

Provide:

- concise error
- Retry
- context-aware Back

### Section-level error

For editions, recommendations, provider sync or optional metadata:

- keep the rest of the page usable
- show a compact retry state only inside the affected section

Never replace valid local progress with a provider error.

## Accessibility and input behavior

- semantic heading order
- all actions keyboard reachable on Desktop
- visible focus states
- touch targets sized for Mobile/Tablet
- TV remote focus order follows visual order
- artwork has useful alt text only where it conveys content; decorative backdrop remains decorative
- progress is not conveyed by color alone
- official/generated/partial state has textual meaning, not only icons/colors

## Current-route migration notes

The current consumer route `/Manga/Series/{id}` contains several legacy/owner concerns:

- owner AniList match search
- source refresh
- reading-direction mutation
- mapping management
- legacy file-derived series/chapter presentation

These are not part of the target consumer Manga Detail mockup.

Migration may retain the route temporarily, but its data source must converge on canonical Work/Volume/Chapter, Edition/Version availability and unified MediaProgress.

Reader direction is a Reader preference/presentation concern. It should not appear as a general owner mutation beside the normal Manga detail primary actions.

## Dependency notes

Before implementation, the screen depends on:

- canonical Manga `Work -> Volume -> Chapter` reads
- Edition/Version/Asset/File availability queries
- unified MediaProgress with exact resume + CompletedThrough semantics
- canonical WorkRelation
- permission/capability-derived request actions
- Reader route/session contract

Optional:

- presentation groups (#567)
- external progress display/sync
- recommendations
- generated translation actions

The detail screen must not block its basic metadata/reading flow on optional integrations.

## Must not implement

- no permanent parallel `MangaSeries` / `MangaChapter` core beside canonical Work/Volume/Chapter
- no consumer dependence on filename-derived identity
- no giant all-series chapter table by default
- no tab-heavy page hiding main content
- no second cover duplicated inside/under the Hero
- no owner/provider matching forms in the normal consumer action row
- no raw paths, files, indexer candidates, release scores or import diagnostics
- no treating machine translation as official
- no marking the currently opened chapter complete merely because it was opened
- no provider progress replacing local exact resume state
- no empty placeholder sections
- no desktop table squeezed onto Mobile
- no full TV Reader without separate approval
- no Presentation Group changing canonical Chapter/Volume identity

## Mockup deliverables

Create visual references after this spec is accepted:

- `desktop-light.png`
- `desktop-dark.png`
- `mobile-light.png`
- `mobile-dark.png`
- `tablet-light.png`
- `tablet-dark.png`
- `tv-light.png`
- `tv-dark.png`

At minimum the first review should show:

1. Desktop ready state with progress, multiple volumes and multiple languages.
2. Mobile ready state with the same information hierarchy.
3. One partial/unavailable state showing preferred-language semantics.
4. TV focus/selected treatment.

Text specification wins over images on conflict.
