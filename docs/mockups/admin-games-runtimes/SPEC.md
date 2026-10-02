# Admin Games Runtimes — Planning Scaffold

Status: scaffold; detailed UX pending review.

Shared Admin contract: `docs/mockups/admin-games/SPEC.md`.

## Purpose

Configure and inspect emulator/runtime adapters available to Games.

## Initial responsibilities

Plan for:
- runtime list
- enabled/disabled
- type: Browser/WASM or explicitly supported local/server runtime
- supported platforms/capabilities
- health/configuration validity
- default/preferred runtime per supported platform where required
- add/edit/test actions
- clear isolation/capability summary

## Data

Expected:
- runtime name/key/type
- supported platforms
- capabilities
- enabled
- health
- configuration status
- BIOS dependency summary
- current/default platform associations

## Boundaries

- no emulator binaries implemented by Jularr
- no broad host permission switch
- no BIOS file management on this page
- no per-game tuning catalogue in V1

## Open for page review

Decide:
- table/card layout
- platform/default mapping UX
- capability presentation
- health/testing behavior
- empty/unconfigured states
