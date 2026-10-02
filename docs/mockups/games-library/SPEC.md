# Games Library — Planning Scaffold

Status: scaffold; detailed UX pending review.

Shared contract: `docs/mockups/games/SPEC.md`.

## Purpose

Primary consumer collection view for games already known to Jularr.

## Initial responsibilities

Plan for:
- cover/grid-first library
- search
- platform filtering
- installed/available/requested state
- recent/continue-playing state where data exists
- Play action only when a playable local release/runtime exists
- navigation to Game Detail

## Data

Expected read model:
- Game identity/title/artwork
- platform(s)
- local release count
- availability/acquisition state
- runtime/play capability
- per-profile recent/save summary where useful

## Boundaries

- no downloader controls
- no BIOS configuration
- no runtime administration
- no raw file browser
- no separate acquisition engine
- Request uses the shared Request flow

## Open for page review

Decide:
- exact page composition
- platform selector/filter behavior
- card information density
- continue-playing placement
- sorting/filtering
- empty/loading/error states
- mobile/tablet layout
