# Games — Library/Home — Planning Direction

Status: navigation/product direction approved; detailed visual mockup pending review.

Shared contract: `docs/mockups/games/SPEC.md`.

## Purpose

Primary consumer Games destination.

Games is a dedicated top-level consumer area, not a `Games` type tab inside the normal Library. It still reuses Jularr's shared shell, cards, Search/Discover, Request, profile and acquisition infrastructure.

## Desktop/tablet structure

Initial page order:

1. page header / compact controls
2. `Continue Playing`, only when the current profile has resumable game activity
3. Games library grid/list
4. platform/filter/sort controls integrated into the library toolbar

Do not create a second dashboard full of statistics.

## Header and controls

Plan for:
- title: `Games`
- local library search/filter
- platform filter
- sort
- grid/list toggle where useful
- global Search/Discover remains the entry point for finding/requesting games not yet owned

No permanent downloader/import buttons in the consumer header.

## Continue Playing

Show only when useful.

Each item may show:
- cover/artwork
- title
- platform
- last played / save availability when meaningful
- `Continue`

Do not invent media-style percentage progress for games.

Home may also show a shared `Continue Playing` row; the Games page remains the full Games collection.

## Games library

Default presentation is cover/grid-first.

One card represents the canonical Game, not one ROM/file.

Card information stays compact:
- cover
- title
- primary platform or compact platform summary
- `Play` / `Continue` only when launch is unambiguous and available
- concise unavailable state only when useful

Do not create a badge wall for region, revision, runtime, BIOS, hashes or file formats.

Multiple GameReleases/regions/revisions are handled on Game Detail.

## Platform filtering

Platform is the primary Games-specific filter.

Do not use a permanent horizontal tab for every possible console once the platform count grows.

Desktop/tablet:
- compact platform selector/filter
- common/recent platforms may optionally surface as quick choices

TV may use horizontal platform rows because that interaction fits remote navigation better.

## Availability

The Games collection primarily represents locally available/imported Games.

Requested/downloading/importing states may be surfaced contextually when the Game is already known, but the page must not become a second Wanted/Downloader queue.

Discovery/request of missing games remains in shared Search/Discover + Request.

## Navigation

- card -> Game Detail
- unambiguous Play/Continue -> launch directly
- ambiguous release/runtime -> Game Play Options dialog
- unavailable/missing game -> shared Search/Request path where appropriate

## TV direction

The dedicated Games destination is first-class on TV.

TV layout may differ from Desktop:
- large focusable game cards
- `Continue Playing` first
- horizontal rows such as platform collections
- remote/gamepad focus navigation
- clear controller/session state only when entering play

Do not squeeze the Desktop grid/filter toolbar onto TV.

## Mobile

Mobile remains useful for:
- browsing Games
- detail/request
- continuing browser-compatible games where supported
- later acting as a paired TV controller

Use compact filters and a thumb-friendly grid/list.

Phone-controller mode is a separate play-session capability, not normal Games-page navigation.

## Boundaries

- no downloader controls
- no BIOS configuration
- no runtime administration
- no raw file browser
- no separate acquisition engine
- no duplicate Search/Discover catalogue
- Request uses the shared Request flow

## Open for visual review

Still decide in the first mockup:
- exact Desktop header composition
- exact Continue Playing card size
- grid card density/aspect ratio
- platform filter presentation
- whether list view is useful enough to keep
- empty/loading/error states
- responsive breakpoints
