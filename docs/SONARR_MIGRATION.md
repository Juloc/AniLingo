# Sonarr → AniLingo migration

AniLingo migrates anime individually. Do not switch the whole library at once.

## Modes

- **Read-only coexistence** — Sonarr owns acquisition, imports and renames. AniLingo only observes the library.
- **Parallel acquisition** — AniLingo may acquire content, but it may mutate only paths explicitly owned by an AniLingo job. Active Sonarr releases and paths block duplicate/conflicting work.
- **AniLingo managed** — AniLingo owns acquisition/import/naming for that anime. Sonarr should no longer monitor or manage it.

The mode is per anime. One title can remain Sonarr-managed while another is migrated.

## Safe rollout

1. Start every existing title in **Read-only coexistence**.
2. Verify AniLingo metadata and episode/AniList mappings.
3. Move one test anime to **Parallel acquisition**.
4. Give AniLingo-owned downloads a distinct download-client category and ownership record.
5. Confirm imports and naming are correct and no duplicate Sonarr job exists.
6. Disable monitoring for that anime in Sonarr.
7. Change the anime to **AniLingo managed**.
8. Repeat title by title.

To revert, finish or cancel active AniLingo jobs first, then return the anime to Read-only coexistence and re-enable Sonarr monitoring.

## Conflict rules

AniLingo must not grab a release already active in Sonarr. In parallel mode it must not rename, replace or delete an unowned path. Existing files are never deleted before a replacement import commits successfully. Filesystem watchers may observe Sonarr changes, but observation does not transfer ownership.

When both managers report the same active release or path, surface a conflict and stop the AniLingo mutation instead of guessing.
