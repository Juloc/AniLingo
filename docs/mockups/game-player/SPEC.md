# Game Browser Player — Planning Scaffold

Status: scaffold; detailed UX pending review.

Shared contract: `docs/mockups/games/SPEC.md`.

## Purpose

Focused browser-play surface for a LaunchPlan produced by the Games module.

## Initial responsibilities

Plan for:
- emulator/runtime viewport
- loading/initialization state
- fullscreen
- pause/resume where runtime supports it
- exit/back to Game Detail
- minimal runtime/player menu
- save persistence feedback
- keyboard/gamepad input where runtime supports it
- clear failure reason when launch cannot continue

## Runtime contract

The page receives a controlled LaunchPlan. It does not browse the filesystem or construct emulator paths itself.

The runtime receives only the selected game assets and current-profile save scope exposed through approved Games APIs.

## Boundaries

- no direct database access
- no unrestricted library filesystem access
- no Admin runtime settings
- no downloader/acquisition controls
- no universal emulator settings engine

## Open for page review

Decide:
- viewport/chrome composition
- desktop/mobile controls
- fullscreen behavior
- runtime menu
- save indication
- controller mapping UX only if needed by the selected runtime
- failure/recovery states
