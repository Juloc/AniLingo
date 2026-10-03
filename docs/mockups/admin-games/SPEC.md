# Admin Games — Shared Contract

Status: planning scaffold from #725. Detailed Admin Games UX pending page-by-page approval.

Shared Games contract: `docs/mockups/games/SPEC.md`.

## Purpose

Admin Games contains only Games-specific operational configuration that cannot live in existing shared Admin areas.

## Planned surfaces

- Runtimes: `admin-games-runtimes/SPEC.md`
- Runtime editor: `admin-games-runtime-editor/SPEC.md`
- BIOS/Firmware: `admin-games-bios/SPEC.md`
- BIOS/Firmware editor: `admin-games-bios-editor/SPEC.md`
- Import resolution: `admin-games-import-resolution/SPEC.md`

## Existing Admin ownership

Do not duplicate:
- Games LibraryRoot -> Admin Storage
- game metadata provider configuration/health -> Admin Providers
- indexers/search -> Provider/Acquisition settings
- native download queue -> Admin Downloader
- global jobs/failures -> Activity / To-Do / History

Game-specific surfaces may deep-link to these areas.

## Isolation rule

Admin runtime configuration may grant only explicit capabilities/resources.

A runtime must never receive broad Jularr/host access merely because it is configured under Games.

## Platforms

Desktop primary.
Tablet/mobile support essential monitoring/configuration where practical.
TV unsupported for Admin.
