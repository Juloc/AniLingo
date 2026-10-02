# Games — Shared UX Contract

Status: planning scaffold from #725. Detailed page/dialog UX is intentionally pending page-by-page approval.

Global UX rules: `docs/UX.md`.
Architecture planning: #725.
Acquisition/import foundations: #389, #697, #715.

## Purpose

Games is a consumer-facing library/play area backed by a strongly isolated Games module.

Shared Jularr infrastructure may search, acquire, download and hand off a completed download. Game-specific identity, releases, platforms, import, runtimes, BIOS/firmware, saves and play behavior remain inside Games.

## Consumer navigation

Planned game-specific surfaces:
- `games-library/SPEC.md`
- `game-detail/SPEC.md`
- `game-player/SPEC.md`
- `game-play-options/SPEC.md`

Games uses existing shared Jularr surfaces where possible:
- global Search / Discover
- shared Request flow
- shared Activity/Operations status

Do not create a second Games-only search/request/acquisition stack.

## Admin navigation

Planned game-specific Admin surfaces:
- `admin-games/SPEC.md`
- `admin-games-runtimes/SPEC.md`
- `admin-games-runtime-editor/SPEC.md`
- `admin-games-bios/SPEC.md`
- `admin-games-bios-editor/SPEC.md`
- `admin-games-import-resolution/SPEC.md`

Existing shared Admin areas remain authoritative for:
- Games LibraryRoot -> Storage
- game metadata providers -> Providers
- acquisition/indexers/downloader -> Acquisition / Downloader
- cross-system failures -> Activity / To-Do / History

Game-specific surfaces may deep-link to these areas.

## Core identity

Consumer UX may expose:
- Game
- Platform
- GameRelease
- region/revision/language where useful
- local availability
- runtime/play availability
- per-profile save state

Do not expose provider-native IDs as product identity.

## Play contract

```text
Game detail
 -> choose playable release/runtime only when needed
 -> Games creates LaunchPlan
 -> Browser/approved runtime receives only required game assets
 -> current profile save scope
```

No emulator core is implemented by Jularr.

## Isolation

Browser runtimes receive controlled asset/save access only.

Server/local runtimes, if supported later, receive only explicitly required:
- selected game files
- required BIOS/firmware
- current profile save directory
- explicitly granted device/host capability

They get no direct PostgreSQL, Jularr configuration/secrets or unrelated library access.

## Shared states

Every relevant surface must account for:
- available/playable
- available but no compatible runtime
- requested/downloading/importing
- metadata incomplete
- missing BIOS/firmware
- runtime unavailable/degraded
- save unavailable/conflict only if the save layer reports one
- permission denied
- loading/error/offline

## V1 exclusions

Do not make V1 depend on:
- achievements
- mod managers
- DLC management
- cheat databases
- netplay
- server-side gameplay streaming
- automatic emulator installation for every OS
- large controller-profile engines
- modern storefront integrations

## Planning rule

These files are scaffolds only. Each page/dialog is reviewed separately. Once a surface is approved, its own SPEC becomes authoritative for that surface.
