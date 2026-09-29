# Discover / Search — Clean Design

Status: **approved clean UX direction**.

## Purpose

Discover and Search are one coherent surface.

- **Home** = personal continuation and recommendations from the user's existing context.
- **Discover** = actively finding new media, including titles not yet in the local library.
- Empty search = Discover.
- Typing = live search.
- Submit = full result view.

## Global search bar

At the top of Discover on every platform:

- very wide search bar
- Discover/Compass icon inside or directly attached to it
- searches Anime, Series, Movies, Books & Light Novels, Manga and Audiobooks
- optional compact Filter icon on the right
- the control must clearly communicate both **search** and **discover**

## Media-type switch

Directly below the search bar:

- All
- Anime
- Series
- Movies
- Books & Light Novels
- Manga
- Audiobooks

This is the primary quick scope control.

## Discover content

Use horizontal rows such as:

- Trending
- New Releases
- For You
- Hidden Gems
- Because You Watched / Read / Listened To …
- Popular
- dynamic genre/theme rows when useful

Rows can open a full filtered result surface.

Do **not** add a permanent second chip wall of genres/tags such as Action / Adventure / Fantasy / Sci-Fi / Romance underneath the media-type row. Keep the layout as clean as the approved Light mockup. Genre/theme browsing belongs inside rows or the Filter surface.

## Filter

Filter opens as:

- Desktop: popover / side panel
- Mobile: bottom sheet / fullscreen sheet
- Tablet: adaptive panel
- TV: remote-friendly panel

Possible filter groups:

- media type
- genre
- year
- status
- language availability
- local/request availability
- sort

Do not permanently expose all filter values as chips.

## Discover cards

Discover cards are intentionally simpler than Library cards.

Show:

1. artwork
2. title
3. compact year/type line
4. one compact **language/request status indicator**

Do not show Library-style progress unless the title is already in the user's Library.

## Language / request indicator

The card should answer only the important question: **what is available for me?**

Use one small icon/badge-style state, not verbose text.

Required semantic states:

- **Preferred language available**
- **Only other language(s) available**
- **Requested in preferred language**
- **Requested in another language**
- **Not available / not requested**

The visual system may use icon + short code/color, but must remain understandable in both Light and Dark and must not rely on color alone.

Detailed audio/subtitle/edition language information belongs in the preview/detail surface.

## Desktop preview interaction

On pointer hover after a short delay:

- card expands into a larger rich preview
- trailer plays muted when available
- if no trailer exists, use backdrop/hero artwork
- show short description and useful metadata
- actions:
  - Play / Continue when available
  - Request when unavailable
  - Favorite
  - Add to Collection
  - Details

The expanded preview must not cause chaotic layout shifting.

## Mobile interaction

No hover behavior.

Tap card -> large bottom sheet / dialog:

- large trailer or backdrop preview
- title
- compact metadata
- short description
- language/request state
- actions:
  - Play / Continue or Request
  - Favorite
  - Add to Collection
  - Details

From the preview the user can open the full canonical detail page.

## Tablet interaction

Touch-first like Mobile, using additional width for a larger preview/dialog and more actions visible at once.

## TV interaction

Remote/focus-first:

- focused card gets the same strong clean selected treatment defined for Library TV
- slight scale/lift + clear focus border/glow
- after a short stable focus delay, show trailer/backdrop in a larger preview/hero area
- do not start a new trailer instantly on every focus movement
- actions remain remote-friendly
- Back restores prior row/focus position

## Trailer / artwork fallback

Trailer is optional.

Priority:

1. metadata-provider trailer / known trailer URL or provider ID
2. local/provider backdrop
3. cover/poster with derived blur/gradient background

A title must still look complete without a trailer.

## Light / Dark

Both are first-class:

- same hierarchy and behavior
- separately tuned contrast and surfaces
- no simple color inversion
- approved Light mockup structure is the baseline
- Dark must follow the same clean structure and **must not add the extra genre/tag-chip row shown in the earlier Dark concept**

## States

Discover/Search must cover:

- loading
- no search query / Discover
- live search
- no results
- provider unavailable
- partial provider results
- local title
- discover-only title
- requested title
- downloading/importing title
- trailer unavailable

## Mockup reference naming

Recommended files:

- `discover-clean-light-approved.png`
- `discover-clean-dark-approved.png`

Platform-specific exports may later use:

- `discover-desktop-light.png`
- `discover-desktop-dark.png`
- `discover-mobile-light.png`
- `discover-mobile-dark.png`
- `discover-tablet-*.png`
- `discover-tv-*.png`

## Implementation rule

This specification is binding for Discover/Search UX. Coding agents must not turn Discover into another Home page, add a permanent genre-chip wall, or overload cards with Library/Admin metadata without updating the approved spec.
