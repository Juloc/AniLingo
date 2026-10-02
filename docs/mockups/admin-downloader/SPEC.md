# Admin Downloader — Shared Contract

Status: approved planning direction for Jularr's native Usenet downloader Admin area.

Global UX rules: `docs/UX.md`.
Architecture baseline: `docs/ARCHITECTURE.md`.
Storage contract: `docs/mockups/admin-storage/SPEC.md`.

## Product role

Jularr owns a native Usenet downloader. Normal operation must not require SABnzbd or NZBGet.

The Downloader Admin area is a dedicated operational/settings area with seven screens:

1. `admin-downloader-overview/SPEC.md`
2. `admin-downloader-queue/SPEC.md`
3. `admin-downloader-servers/SPEC.md`
4. `admin-downloader-processing/SPEC.md`
5. `admin-downloader-speed-schedule/SPEC.md`
6. `admin-downloader-settings/SPEC.md`
7. `admin-downloader-external-clients/SPEC.md`

## Navigation

Downloader is one Admin destination with its own secondary navigation:

- Übersicht
- Queue
- Server
- Verarbeitung
- Geschwindigkeit & Zeitplan
- Einstellungen
- Externe Clients

The default route opens Übersicht.

## Ownership boundaries

### Downloader-specific

Owned by this Admin area:
- NNTP server configuration and health
- native download queue and technical history
- article/segment progress
- download speed and ETA
- server usage/failover
- verify/repair/extract/post-processing behavior
- downloader concurrency and bandwidth limits
- downloader schedule
- downloader duplicate/retry/history policy
- native downloader workspace selection from configured Storage roles
- external downloader compatibility adapters

### Remain global Admin features

Do not duplicate:
- physical Mounts, paths, capacity and workspace creation -> Storage
- Wanted items -> Wanted
- release candidate selection/scoring -> Manual Search / Acquisition
- AcquisitionProfiles -> Acquisition settings
- Indexers -> Indexer/provider settings
- post-download media assignment problems -> Activity / To-Do
- cross-system operations -> Activity
- cross-system audit/history -> History
- system CPU/RAM/GPU -> Admin Dashboard/System diagnostics
- global notifications -> Admin/global settings
- global logs -> System diagnostics

Downloader screens may deep-link to these surfaces.

## Native pipeline states

Use a common state vocabulary:

```text
Queued
-> Downloading
-> Verifying
-> Repairing (optional)
-> Extracting (optional)
-> Post-processing
-> Identify / Handoff
-> Import/Copy/Move
-> Completed
```

Failure/review states:
- Paused
- Waiting
- Retry scheduled
- Password required
- Missing articles
- Repair failed
- Extract failed
- Workspace full
- Storage unavailable
- Cancelled
- Failed

UI wording may be localized but state meaning is shared.

## Global vs local history

Downloader Queue may include a technical `Completed/Failed` history for downloader-specific details such as server/article/repair/extract diagnostics.

Admin History remains the canonical cross-system operational history.

Do not build two independent event stores.

## Telemetry

Downloader telemetry may include:
- NNTP receive throughput
- disk write throughput
- verify/repair throughput
- extract throughput
- import/copy/move throughput
- active connections
- per-server transfer
- missing article/error rate
- workspace usage
- queue size
- successful/failed jobs

Metrics must be labeled by phase. Do not call verify/extract/copy throughput "download speed".

## Storage integration

Downloader references Storage roles by ID/configuration:
- Native Download Workspace
- optional Repair/Extract Workspace

It does not own arbitrary filesystem path configuration.

Final specialized LibraryRoots and Generic Downloads Root are selected by Acquisition/import routing, not by individual queue jobs unless an authorized manual override flow explicitly permits it.

## Secrets

Passwords/API keys are:
- masked after save
- write-only where possible
- never rendered back in clear text
- omitted from logs/history

## Light / Dark

Both first-class.

Operational visual language:
- compact
- information-dense
- no decorative hero imagery
- restrained semantic colors
- tables on Desktop
- no badge wall
- no color-only state communication

## Platforms

Desktop is primary.

Tablet:
- adaptive tables/detail sheets

Mobile:
- monitoring and essential controls supported
- dense configuration may use full-screen sheets
- no squeezed Desktop tables

TV:
- unsupported

## Shared states

Every Downloader screen must handle:
- loading
- empty/unconfigured
- healthy
- degraded
- offline
- partial server failure
- workspace unavailable
- permission denied
- stale telemetry
- save/test in progress
- recoverable error
- forbidden

## Must not implement

- No separate downloader engine per media type.
- No SABnzbd/NZBGet requirement for normal operation.
- No arbitrary native downloader paths bypassing Storage.
- No duplicate global Activity/History stores.
- No raw provider/server credentials in logs.
- No torrent-client assumptions unless torrent support is separately approved.
- No raw config/YAML as primary UX.
