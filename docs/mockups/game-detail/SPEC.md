# Game Detail — Planning Scaffold

Status: scaffold; detailed UX pending review.

Shared contract: `docs/mockups/games/SPEC.md`.

## Purpose

Canonical consumer detail page for one Game.

## Initial responsibilities

Plan for:
- artwork/title/description
- developer/publisher/year where available
- platform identity
- available GameReleases
- region/revision/language only where useful
- primary Play action
- Request/acquisition state when not available
- runtime availability
- concise per-profile save/recent state
- related versions/releases without duplicating the canonical Game

## Actions

Potential:
- Play
- Request through shared Request flow
- select another release when multiple materially different local releases exist
- inspect release/file information at an appropriate detail level

## Boundaries

- no emulator configuration
- no BIOS upload
- no indexer/release-candidate table
- no technical downloader controls
- no provider-native identity as the main UI

## Open for page review

Decide:
- hero/header composition
- tabs/sections
- release presentation
- Play/Request state rules
- metadata depth
- save/history presentation
- mobile/tablet behavior
