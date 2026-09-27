# Sonarr → Jularr migration

Jularr migrates anime individually. Do not switch the whole library at once.

## Modes

- **Read-only coexistence** — Sonarr owns acquisition, imports and renames. Jularr only observes the library. This is the default for every anime without an owner decision.
- **Parallel acquisition** — Jularr may acquire content, but it may mutate only paths explicitly owned by an Jularr job. Active Sonarr releases, downloads, episodes and paths block duplicate/conflicting work.
- **Jularr managed** — Jularr owns acquisition/import/naming for that anime. Sonarr should no longer monitor or manage it.

The mode is per anime. One title can remain Sonarr-managed while another is migrated.

## Owner controls

Owners manage migration under **Settings → Sonarr migration** (`/Settings/SonarrMigration`). Each anime row shows the current mode, the linked Sonarr series (suggested by the same matcher the Sonarr artwork import uses), whether Sonarr monitors it, active Sonarr downloads and any conflicts. Actions:

| Action | Mode afterwards | Sonarr side effect |
| --- | --- | --- |
| Keep Sonarr | Read-only coexistence | none |
| Parallel | Parallel acquisition | none (with the monitoring option: restores monitoring Jularr turned off) |
| Hand over to Jularr | Jularr managed | only with the monitoring option: unmonitors the series via `PUT /api/v3/series/editor` |
| Revert | Read-only coexistence | only with the monitoring option: re-enables exactly the monitoring Jularr turned off |

Actions are idempotent (repeating one changes nothing and logs nothing) and reversible (hand over followed by revert restores the Sonarr-managed state and Sonarr monitoring). Hand over is refused while Sonarr has active downloads for the series or Sonarr cannot be observed. Revert is refused while Jularr jobs for the anime are still pending or importing. Every applied action is recorded in the ownership migration log.

Jularr never deletes Sonarr series or files and never calls any other mutating Sonarr endpoint.

## Sonarr observation

Jularr reuses the single Sonarr connection configured under **Admin → Sonarr** (the same connection used for artwork import). It reads the Sonarr v3 API with `GET` requests only:

- `/api/v3/series` — series ids, root folders and monitoring state,
- `/api/v3/episodefile?seriesId=…` — exact Sonarr file paths for linked anime that are not in read-only coexistence,
- `/api/v3/queue` — active releases, download ids (e.g. SABnzbd `nzo_id`), output paths and episodes,
- `/api/v3/history` — recent grabs, imports, renames and failures.

From that observation Jularr recognizes Sonarr-owned series (linked Sonarr series id), paths (queue output, episode file, series folder) and downloads (queue or history download id). Observation is cached for one minute and never transfers ownership. If Sonarr is configured but unreachable, Jularr fails closed: grabs, imports and renames for Sonarr-linked or parallel-mode anime pause until Sonarr can be observed again.

Ownership decisions and Sonarr links persist in the canonical ownership store (`/data/acquisition/ownership.json`).

### Different mount paths

When Sonarr and Jularr see the shared library under different paths (different container
mounts), configure the remote path mappings on `/Settings/Acquisition`: every Sonarr-observed path
(series folder, episode file, queue output path, history source/target path) is rewritten through
that mapping before Jularr compares it to its own paths, so ownership recognition and rename-loop
detection keep working. The same mapping also rewrites completed-download paths reported by the
download client (see [ANIME_ACQUISITION.md](ANIME_ACQUISITION.md#import-mode-and-remote-path-mapping)) — it is one canonical list for both purposes.

## Safe rollout

1. Start every existing title in **Read-only coexistence**.
2. Verify Jularr metadata and episode/AniList mappings.
3. Move one test anime to **Parallel acquisition**.
4. Give Jularr-owned downloads a distinct download-client category and ownership record.
5. Confirm imports and naming are correct and no duplicate Sonarr job exists.
6. Wait until Sonarr's queue for that series is empty.
7. **Hand over to Jularr** with the Sonarr monitoring option (or disable monitoring for the series in Sonarr yourself).
8. Repeat title by title.

To revert, finish or cancel active Jularr jobs first, then **Revert** with the Sonarr monitoring option to return the anime to Sonarr and re-enable Sonarr monitoring.

## Conflict rules

Jularr must not grab a release already active in Sonarr, an episode Sonarr is downloading, or an episode Sonarr grabbed within the last 24 hours (unless that download failed). An Jularr-managed anime whose Sonarr series is still monitored cannot be grabbed or renamed by Jularr. Jularr does not import downloads Sonarr tracks and does not replace files it may not mutate; such imports are ignored or sent to manual review. In parallel mode it must not rename, replace or delete an unowned path. Files inside a Sonarr series folder are only changed for the linked anime after Sonarr stopped monitoring the series. Existing files are never deleted before a replacement import commits successfully. Filesystem watchers may observe Sonarr changes, but observation does not transfer ownership.

Jularr never renames a file Sonarr imported or renamed within the last 24 hours, so the two managers cannot rename the same file back and forth.

When both managers act on the same release, download or path, Jularr surfaces a conflict and stops the Jularr mutation instead of guessing. Conflicts appear on the Sonarr migration page and as warnings in the application log:

- `release` / `path` — both managers report the same active release or path,
- `download` — Sonarr tracks an Jularr download (shared download-client category),
- `monitoring` — an Jularr-managed series is still monitored in Sonarr, or a reverted series is still unmonitored,
- `sonarr-activity` — Sonarr grabbed, imported or renamed after the anime was handed over,
- `rename-loop` — Sonarr renamed an Jularr-owned file,
- `unverified` — Sonarr cannot be observed for a Sonarr-linked anime.

## Integration seams

- Grab: `AnimeMonitoringEngine.EvaluateCandidate(..., ownership)` consults `SonarrParallelSafety.CanGrab`; `AnimeAcquisitionPipeline` refreshes the snapshot before automatic and owner grabs and registers an Jularr job for every grab (see [ANIME_ACQUISITION.md](ANIME_ACQUISITION.md)).
- Import: `CompletedDownloadImportPlanner.Plan(..., ownership)` consults `SonarrParallelSafety.CanImport` and `CanMutateLibraryPath` for replaced files; `AnimeImportExecutor` also checks `CanMutateLibraryPath` for every destination before moving a file.
- Rename: `AnimeRenameService` calls `SonarrParallelSafety.CanRename` for every file, sidecar and series-folder move before anything is moved (see [ANIME_NAMING.md](ANIME_NAMING.md)).
- Executors obtain the snapshot from `SonarrObservationService.GetSnapshotAsync`.
