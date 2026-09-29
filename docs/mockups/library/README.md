# Library — UX specification (draft for mockup approval)

Status: draft based on the current Library review. The previous Library image is **not approved yet**.

## Core idea

Library is a media-first catalog of the user's canonical Jularr library. It must work across Anime, TV Series, Movies, Books, Light Novels, Manga and Audiobooks without turning into a generic grid with random filters.

Light and Dark are both first-class. Desktop, Mobile, Tablet and TV use the same data model with platform-specific interaction.

## Primary structure

At the Library heading, use a clear subview switch:

- **Library**
- **Collections**

Collections are not mixed into the media-type filter because they are not a media type.

### Library media-type switch

- All
- Anime
- Series
- Movies
- Books & Light Novels
- Manga
- Audiobooks

A user preference may later allow Series + Movies or Books + Light Novels to be grouped/separated differently, but the information architecture stays canonical.

## Collections

Collections are first-class and support:
- manual collections
- smart/rule-driven collections
- user favorites / watchlist as built-in views where appropriate
- cross-media collections
- optional franchise/adaptation groupings where the canonical relations support them

Collection cards show artwork mosaic/representative art, title, item count and optional smart/manual marker. Do not show admin rule syntax in normal user UI.

## Filter model

Do **not** render a random row of permanent chips such as In Progress / Completed / By Language / By Genre.

Keep the top bar clean:
- media-type switch
- **Filter** button with active-filter count
- **Sort**
- Grid/List view toggle where useful

Filter opens:
- Desktop: popover/side panel
- Mobile: bottom sheet/fullscreen sheet
- TV: remote-friendly fullscreen/side panel
- Tablet: adaptive sheet/panel

### Filter groups

Progress:
- Not started
- In progress
- Completed

Availability:
- Fully available
- Partially available
- Requested/downloading where the work is already part of the library/monitoring context
- Missing units

Language:
- My preferred language available
- Audio language
- Subtitle language
- Reading/edition language
- Any available language

Metadata:
- Genre
- Year
- Status
- Format where useful

Do not show irrelevant filters for the selected media type.

## Sort

Single Sort menu, not multiple filter chips:
- Title
- Recently added
- Last played/read/listened
- Release date/year
- Progress
- User rating / external rating where configured and useful

## Media card — shared grammar

Every card has:
1. poster/cover
2. title
3. compact media-specific progress/status line
4. **language availability line**
5. optional progress bar
6. overflow/context action

Do not repeat the media type when the user is already inside that media-type filter. In `All`, media type may be shown compactly.

## Preferred-language-first behavior

Cards must answer: **Can I consume this in my preferred language?**

The card derives this from per-user language preferences.

### Video (Anime / Series / Movies)

Show audio and subtitle availability separately because they are different capabilities.

Examples:
- `🎧 DE · CC DE`
- `🎧 JP · CC DE`
- `🎧 JP · CC JP, EN`

Behavior:
- if the preferred audio/subtitle language exists, show it first and visually prioritize it;
- if it does not exist, show the actually available language(s);
- cap visible language labels and use `+N` for more.

### Books / Light Novels / Manga

Show available edition/read languages.

Examples:
- `DE`
- `JP · EN`
- `DE · translated` for a generated translation

Behavior:
- if preferred reading language is available, show it first;
- if it is unavailable, show the available languages instead;
- official and machine-generated translations must be distinguishable, but the card should remain compact.

### Audiobooks

Show spoken language, e.g.:
- `🎧 DE`
- `🎧 EN`

Narrator and detailed edition information belong on detail pages, not every card.

## Media-specific secondary line

Anime / Series:
- next/resume episode, e.g. `S1 E8 · 22 min left`

Movie:
- year/runtime or resume, e.g. `2024 · 48 min left`

Book / Light Novel:
- volume/chapter/progress, e.g. `Vol. 4 · 63%` or `Ch. 18`

Manga:
- volume/chapter/progress

Audiobook:
- chapter / time remaining

The card should tell the next useful action, not expose technical metadata.

## Availability state

A Work can be partially available. The card may use one small status indicator for:
- complete
- partial
- downloading/requested
- attention/missing

Avoid badge clutter. Detailed missing episodes/chapters/releases belong in detail/Admin surfaces.

## TV

The current TV visual direction is preferred.

TV specifics:
- large artwork
- clear focus ring/glow
- top media-type row
- Library / Collections switch
- separate Filter and Sort buttons
- focused card expands/reveals a compact info strip with progress + languages
- no tiny permanent filter chips
- remote-safe focus order

## Mobile

- two-column poster grid where width permits
- title + one compact status/progress line + one language line
- Filter/Sort in sheets
- bottom navigation remains visible unless a fullscreen flow is entered
- no tiny desktop controls squeezed onto phone

## Desktop / Tablet

Desktop may show more metadata and denser grids, but uses the same card semantics. Tablet remains touch-first.

## States

Mockups must include or specify:
- loading
- empty library
- no filter results
- partial/offline storage
- missing artwork fallback
- unavailable preferred language
- requested/downloading
- error

## Next mockup revision

The next Library mockup must show:
- Light + Dark
- Desktop + Mobile + Tablet + TV
- Library / Collections
- cleaned filter model
- language-aware cards for video, written media and audiobook
- at least one example where the preferred language is unavailable and fallback languages are displayed
