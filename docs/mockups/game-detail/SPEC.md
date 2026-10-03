# Game Detail

Status: planning direction for page-by-page review; visual mockups pending approval.

Shared contract: `docs/mockups/games/SPEC.md`.
Games page: `docs/mockups/games-library/SPEC.md`.
Games architecture: #725.
Browser runtime plan: #771.

## Purpose

Canonical consumer detail page for one Game.

It answers:
- what is this Game;
- which platform/release is locally available;
- can the current user play/continue it now;
- what save/recent state exists;
- which materially different releases are available.

It is not an emulator configuration or download-management page.

## Page skeleton

Reuse Jularr's canonical detail-page visual grammar rather than inventing a separate product.

Desktop order:

1. hero/backdrop;
2. cover/artwork;
3. title + concise metadata;
4. primary action area;
5. short description;
6. content tabs/sections;
7. related/recommendation content only where useful.

Clean and Jularr Original are visual skins over the same structure.

## Hero/header

Show:
- cover;
- optional backdrop;
- canonical Game title;
- optional alternate title only when useful;
- primary platform;
- release year;
- developer/publisher where available;
- concise genre/tags where useful;
- current availability/runtime state.

Do not show provider IDs, hashes, ROM filenames or technical core names in the hero.

## Primary action

Action priority:

### Local + unambiguous playable release
- `Play`
- `Continue` if current profile has resumable save/recent state

### Local + multiple meaningful playable choices
- `Play`
- opens Play Options

### Local but not currently playable
Show disabled/unavailable state with one concise reason, e.g.:
- compatible runtime unavailable;
- required BIOS missing;
- browser/device unsupported.

Admin remediation may deep-link to the relevant Admin Games page for authorized users, but normal users do not see Admin forms inline.

### Not local
Use the shared `Request` flow.

Do not create a Games-specific acquisition dialog.

## Overview

Default section/tab.

Show:
- description;
- developer;
- publisher;
- release date/year;
- genres;
- platform;
- optional player count if metadata is reliable;
- local availability summary.

Keep metadata concise.

## Releases

Use a dedicated section/tab only when more than one materially useful `GameRelease` exists or when the user needs to inspect them.

Each release may show:
- platform;
- region;
- revision/version;
- language where known;
- disc/set summary;
- local/available state;
- playable state;
- preferred/default indicator where relevant.

Do not expose:
- raw paths;
- hashes by default;
- emulator core IDs;
- downloader/indexer technical data.

Selecting a different release changes the intended play target but does not create another canonical Game page.

## Saves

Per-profile section/tab.

Show only useful consumer information:
- latest save timestamp;
- save present/not present;
- optional multiple save slots/states only if the selected runtime genuinely supports them;
- continue action where appropriate.

V1 should not become a complex cloud-save manager.

## Play history

Do not require a separate full tab initially.

Useful information such as last played and recent session can appear in the hero/Save area. Full operational history stays in shared Activity/History where appropriate.

## Related / recommendations

May appear after primary content using existing Jularr recommendation/card grammar.

Do not block V1 on a Games-specific recommendation engine.

## Runtime abstraction

#771 makes EmulatorJS the first concrete browser runtime because it supports a broad retro platform set with low integration cost.

The detail page must remain runtime-neutral.

Conceptual flow:

```text
GET Game Detail
 -> GameDetail read model
 -> available GameReleases
 -> runtime capability service

Play
 -> ResolveLaunch(GameId, optional GameReleaseId)
 -> LaunchPlan
 -> Game Player
```

The page receives only a consumer-facing capability:

```text
PlayNow
NeedsChoice
Unavailable(reason)
```

It does not know:
- EmulatorJS CDN/core IDs;
- ROM/disc URLs;
- BIOS file paths;
- native process commands.

Those are built behind the runtime adapter/application layer.

## EmulatorJS first integration

For #771, the first browser implementation can map Jularr `GamePlatform` IDs to supported EmulatorJS core IDs.

The adapter owns:
- core mapping;
- ROM/disc asset URLs;
- BIOS requirements;
- browser/device capability checks;
- save persistence integration;
- gamepad/touch/fullscreen capabilities.

Game Detail only consumes the result.

Unsupported systems remain valid Games in the library/detail model; they simply have no browser LaunchPlan until another runtime supports them.

## Tabs

Initial Desktop proposal:

- Übersicht
- Versionen, only when useful
- Spielstände, only when useful

Do not show empty tabs.

This deliberately avoids copying Anime's Characters/Staff structure when it is not useful for Games.

## Mobile

Use the same hierarchy:
- compact hero;
- sticky/obvious Play/Continue;
- sections or horizontally scrollable tabs;
- no squeezed Desktop metadata table.

## TV

Use a reduced detail presentation:
- large artwork/title;
- primary Play/Continue focus;
- essential metadata;
- release choice only when necessary;
- controller/session setup happens at launch/player boundary, not as permanent page clutter.

## Required states

- loading;
- local and playable;
- local and needs release/runtime choice;
- local but unsupported runtime;
- missing BIOS;
- browser/device unsupported;
- requested;
- downloading/importing;
- not local/requestable;
- metadata partial;
- save unavailable;
- error/offline;
- permission denied.

## Read model

Conceptually:

```text
GameDetail
- GameId
- Title / AlternateTitle
- Description
- Cover / Backdrop
- Developer / Publisher
- ReleaseYear
- Genres
- Platforms[]
- PreferredRelease
- Releases[]
- PlayCapability
- RecentPlay
- SaveSummary

GameReleaseSummary
- GameReleaseId
- Platform
- Region
- Revision
- LanguageSummary
- DiscSummary
- Availability
- PlayCapability

PlayCapability
- State: PlayNow | NeedsChoice | Unavailable
- Reason?
```

Do not bind Razor/native clients directly to EmulatorJS configuration objects.

## Visual direction for mockups

Produce both existing Jularr skins using the same information architecture:

### Clean
- dark/navy surfaces;
- purple accent;
- restrained glow;
- same shell/card/detail language as the approved Clean Discover design.

### Jularr Original
- warm light/cream surfaces;
- Japanese watercolor/cherry-blossom background;
- red accent;
- same shell/detail language as the approved Original Library/detail references.

The images are visual references. This text spec remains authoritative.

## Must not implement

- no emulator settings on consumer detail;
- no BIOS upload on consumer detail;
- no raw file/path display as primary UX;
- no EmulatorJS-specific UI dependency;
- no second Request/Search flow;
- no duplicate canonical Game per region/revision;
- no forced release chooser when one obvious playable target exists;
- no empty tabs;
- no achievement/mod/DLC scope in V1.
