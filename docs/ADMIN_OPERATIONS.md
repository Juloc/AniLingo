# Admin operations

AniLingo keeps personal user settings and server administration separate.

## Routes

Personal settings:

- `/Settings` — signed-in user's learning preferences and personal integrations
- `/Settings/Learning`
- `/Settings/AniList`

Owner-only administration:

- `/Admin` — operational overview
- `/Admin/Users` — local account management and progress
- `/Admin/Operations` — active work, queue, downloads and history
- `/Admin/Scans` — library scan runs per media root with phase, counters and warnings
- `/Admin/Logs` — structured operation logs
- `/Admin/System` — media roots and server integrations
- `/Admin/Subtitles`
- `/Admin/Sonarr`
- `/Admin/Ai`
- `/Settings/DownloadClients` — the canonical download client list (SABnzbd, qBittorrent), shared by Books and Anime, with priority, enable/disable, test and health (linked from Admin → System and Books → Acquisition settings)
- `/Settings/SonarrMigration` — per-anime Sonarr/AniLingo ownership (linked from Admin → Sonarr)
- `/Settings/Naming` — anime naming profiles, default and per-library selection (linked from Admin → Sonarr); per-anime selection and the rename preview live on `/Library/Rename/{animeId}` (see [ANIME_NAMING.md](ANIME_NAMING.md))
- `/Settings/Indexers` — the canonical indexer list (Prowlarr, direct Newznab/Torznab) for anime acquisition, with priority, enable/disable, test and health (linked from Admin → System)
- `/Acquisition` — anime acquisition overview: schedule, wanted episodes, downloads, imports that need a decision, interactive search and recent decisions (linked from Admin → System and each anime page; see [ANIME_ACQUISITION.md](ANIME_ACQUISITION.md))

Legacy owner routes under `/Settings` redirect to their `/Admin` counterparts.

## Canonical operation state

The SQLite tables `Operations` and `OperationLogs` are the canonical durable operational history.

The existing `BackgroundJobQueue` and `PlaybackJobQueue` remain the in-process dispatch lanes, but every queued work item receives a durable operation ID before entering the channel.

Operation lanes:

- `Interactive` — latency-sensitive playback work
- `Normal` — ordinary user/admin-triggered background work
- `Maintenance` — scans, batch preparation and maintenance work

The queues do not maintain a second durable status store.

## Lifecycle

Normal lifecycle:

`Queued → Running → Succeeded`

Other terminal states:

- `Failed`
- `Cancelled`
- `Interrupted`

When AniLingo starts, local operations left in `Queued` or `Running` for a worker lane from the previous process are marked `Interrupted`. Anonymous .NET delegates are deliberately not serialized. This prevents phantom running jobs while preserving accurate history. Only operations created before the current process owned the queue are recovered, so work queued during startup (for example the startup library scan) is never mistaken for abandoned work.

External operations can persist an `ExternalProvider` + `ExternalId`. Those jobs are not marked interrupted by the local worker reconciliation because their authoritative work continues outside AniLingo. Provider monitors resume after restart and keep the same canonical operation record current.

Retry is available while the current process still owns the original retryable local delegate. After a process restart, local history remains but that transient delegate is intentionally unavailable. Durable provider-backed jobs such as SABnzbd instead resume status monitoring from their persisted external reference, and library scans can be run again from their persisted `Details` (see below).

## Downloads

Downloads are ordinary operations with `IsDownload = true` and optional byte/progress fields. This allows one Downloads view without a parallel download database.

Tracked network/import work includes:

- Japanese subtitle/transcript preparation and explicit embedded-subtitle extraction
- episode learning preparation
- batch learning-text preparation
- novel source imports, table-of-contents/chapter refreshes, chapter downloads and AI chapter translation
- manual AniList episode/novel progress sync
- anime metadata match, episode-range match and refresh
- Manga CBZ/ZIP upload, mounted-path import, source refresh and AniList metadata match
- Discover handoffs for novel and Manga imports
- local EPUB upload, Books inbox import and remote EPUB import
- light-novel EPUB volume uploads and light-novel inbox imports (`<Books inbox>/light-novels`)
- SABnzbd downloads for Books and Anime, including live queue/post-processing state when a full SABnzbd API key allows queue/history access
- Sonarr artwork downloads

Light-novel EPUB uploads on `/Novels` and on an EPUB series page accept up to 20 files of at most 100 MB each; the raised limits apply only to the owner's upload handlers.

Manga uploads on `/Manga` and `/Discover/MangaImport` accept up to 200 CBZ/ZIP archives, at most 1 GB each and 4 GB in total. The raised request-body and multipart limits apply only to the owner's `Upload` handler on those two pages; every other request, including uploads attempted by non-owner accounts, keeps the ASP.NET Core defaults. A reverse proxy in front of AniLingo must not cap request bodies below roughly 4 GB for these uploads (Caddy has no body limit by default; nginx needs `client_max_body_size`).

Synchronous request-bound work uses the shared `OperationRunner`, which writes the same `Operations` / `OperationLogs` lifecycle as queued jobs. Long work that can safely outlive the HTTP request continues to use `BackgroundJobQueue`.

Playback remux/transcode is currently streamed live by the media response path rather than pre-generated as a durable background preparation job. It is therefore not recorded as a separate completed operation. If a future UI adds explicit cached playback preparation, that producer must use the existing playback/operation lane instead of creating another task store.

Other job producers should use the same operation descriptor rather than adding their own history table.

### Structured details

`Operations.Details` is an optional, kind-specific JSON document (at most 8 000 characters) for the structured facts of one run that the generic columns cannot hold. It is the only place for such data; producers must not add a parallel table for it. Library scans are the first kind that uses it.

## Library scans

Every library scan — startup, the **Queue library scan** button, filesystem-change scans and periodic reconciliation — goes through `LibraryScanCoordinator` (`Features/Library`). There is no separate scan-run store: each run is one operation of kind `library-scan` on the `Maintenance` lane, and `/Admin/Scans` plus the per-root state on `/Admin/System` are views over `Operations`. While a run is queued or running, `/Admin/Scans` refreshes its history every few seconds.

`Details` of a scan run carries the root ID, the scanned folders (`null` for the whole root), the trigger (`Manual`, `Startup`, `Watch`, `Periodic`, `Retry`), the current phase (`Enumerating`, `Reconciling`, `Metadata`, `Artwork`, `Analyzing`, `Subtitles`, `Completed`), files processed/total and, once finished, the counters: media files, added, changed, removed, skipped, subtitle files, artwork imported, NFO files ignored, media analysed, media analyses failed, errors and warnings. Progress percent and the message are written through the normal operation progress at most once per second; phase changes are always written.

Item-level findings (unmatched media files, ignored NFO files) are `OperationLogs` warnings of module `Scan` whose paths are relative to the root. Neither the logs nor the persisted error of a failed scan contain the host path of the root; the full exception goes to the application logger only.

Coalescing: a root has at most one active scan run, whatever its trigger. A request for a root whose run is still queued is merged into that run: a folder is added to its scope, a whole-root request widens it to the whole root, and the result is `Merged` with the ID of that run. While the run is executing, further requests are rejected as `AlreadyActive` (the **Queue library scan** button is disabled); the watcher keeps such changes pending and offers them again after the next quiet period, and the periodic pass retries on its next minute instead of waiting a whole interval. A run that starts while a file rename (`anime-rename`, see `ANIME_NAMING.md`) is active waits for it to finish ("Waiting for a file rename to finish."); the rename in turn refuses to start while a scan is queued or running, so a scan never sees a half-renamed folder.

Restart and retry: an abandoned running scan becomes `Interrupted` through the normal lane recovery. **Run again** on `/Admin/Scans` queues a fresh run for the root and folders recorded in the finished run's `Details`; this works after a restart, unlike the generic delegate retry. The newest 200 finished scan runs are kept; older ones and their logs are pruned after each completed scan.

### Filesystem watcher and periodic reconciliation

`LibraryWatchService` attaches a `FileSystemWatcher` to every enabled root that is readable and supports events. Callbacks only record the changed top-level folder (the anime directory); after the root has been quiet for 10 seconds its dirty folders are queued as one folder scan run (`Watch` trigger). A watcher error or buffer overflow queues a full reconciliation of that root instead and the watcher is re-attached on the next sync. Roots on mounts without event support, or roots that are offline, simply have no watcher; the service re-checks the root list every minute and attaches when the root becomes readable.

A folder scan run reconciles each of its folders through `LibraryScanner.ScanFolderAsync` one after another and records the summed counters. `ScanFolderAsync` runs the same reconciliation code as a full scan, restricted to media below that folder: media, episodes, NFO metadata, local artwork, media inventory analysis and subtitles of the folder end up exactly as a full scan would leave them, while media elsewhere in the root are untouched and `LastScannedAt` (the anchor of the periodic schedule) only moves on full scans. The library-wide Sonarr artwork sync runs only when the folder scan discovered a new anime, and learning-text preparation is queued when it added or changed media. A vanished folder reconciles as deletion of its media unless the whole root is empty, which is treated like the full-scan mass-deletion guard (an unmounted NAS).

Each root has one **Periodic reconciliation** interval (`LibraryRoots.ReconciliationIntervalMinutes`, default 30, `0` turns it off, edited on `/Admin/System`). A full scan of the root is queued when the last completed full scan and the last periodic attempt are both older than the interval. An unavailable root is skipped without creating an operation and is not retried before the next interval, so an offline NAS does not fill the history.

## Local-first page loads

An ordinary page GET renders from SQLite and local files only. It must not contact AniList, OpenLibrary, Gutendex, Jimaku, Codex, Prowlarr, SABnzbd or Sonarr, and must not start ffprobe, ffmpeg or Whisper, import files or run a library scan just because the page was opened.

- Optional remote state loads after first paint from a lazy page handler that sends `Cache-Control: no-store` and degrades to an "unavailable" state instead of failing the page. The AniList progress card (`_ExternalProgress` / `OnGetExternalProgressAsync`) on Anime, Episode, Manga series and Novel work pages is the reference pattern. The Manga and Novel readers do not load remote progress at all; it lives on the series/work page.
- Discover renders its shell locally; trending, top, My AniList and search results come from its `Results` handler, whose external lookup is the explicit purpose of that request.
- Explicit Search, Refresh, Sync, Import, Scan, Prepare and Translate actions do their intended external or heavy work, through `OperationRunner` or the queues described above.
- `LocalFirstPageGetTests` runs page GETs with external clients that fail when called. Add a case there when a page gains a new dependency on a remote service.

To measure page timings temporarily, set the log level `Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics` to `Information` (environment variable `Logging__LogLevel__Microsoft.AspNetCore.Hosting.Diagnostics`); ASP.NET Core then logs every request with its elapsed time. Leave it at the default `Warning` otherwise.

## Media inventory

`MediaAnalyses` / `MediaAnalysisStreams` are the canonical technical analysis of each `MediaFiles` row and are owned by `MediaInventoryService`. Library reconciliation, playback, the client API and embedded subtitle/Whisper stream selection read them; `FfprobeMediaProbeRunner` is the only code that invokes `ffprobe`.

- Each analysis records the source identity it describes (size, modification time and a SHA-256 fingerprint of the length plus the first and last 64 KiB) and the code-level probe version. It is re-probed only when the size changes, the modification time changes and the fingerprint differs, or `MediaInventoryService.CurrentProbeVersion` is raised. Raise that constant whenever the `ffprobe` arguments or the parser output change; every file is then re-analysed on the next scan or first use.
- States: `Succeeded`; `Failed` (ffprobe rejected the file — the bounded diagnostic stays until the file changes, and the scan continues); `Pending` (ffprobe could not start or timed out — retried by the next scan or use once five minutes have passed).
- Each library scan logs how many files were analysed, failed, deferred or unchanged. Deleting a media file removes its analysis through the foreign-key cascade.
- Diagnostics replace the media path with the file name. There is no manual re-analyse action yet; touching or replacing the file (a changed size or content) forces a new analysis.

## Logs and security

`OperationLogs` contains structured operational events keyed by operation ID.

Persisted operational data must not contain:

- passwords
- API keys or access tokens
- cookies
- raw request headers
- private filesystem paths

Exceptions are persisted as bounded type/message summaries rather than stack traces. Full server diagnostics may continue to use the normal application logger.

## SABnzbd and qBittorrent (download clients)

Books and Anime submit downloads through one abstraction, `IDownloadClient`
(`Features/Acquisition/DownloadClients`), with SABnzbd (usenet) and qBittorrent (torrent) as its
two implementations; `Features/Acquisition/Sabnzbd` keeps SABnzbd's own protocol client and the
anime-specific attempt/blocklist relation. The pipeline and Books submissions pick the
highest-priority enabled, healthy client that supports a release's protocol and fail over to the
next client of that protocol if a submission is rejected.

### Configuration

The owner configures every download client under `/Settings/DownloadClients`: name, type
(SABnzbd/qBittorrent), base URL, API key or password, categories (SABnzbd has separate Books and
Anime categories; qBittorrent's one category is used for Anime), qBittorrent save path, priority
and enabled. Settings are stored in `/data/acquisition/download-clients.json`; the secret is
protected with ASP.NET Core Data Protection. **Test** on each entry checks reachability and
authentication and records the result for the periodic health check (see
[ANIME_ACQUISITION.md](ANIME_ACQUISITION.md)) — for SABnzbd, use the full API key rather than the
NZB-only key so AniLingo can also track progress.

On startup, a SABnzbd connection from the earlier single-connection settings
(`/data/acquisition/sabnzbd.json`, including the environment-variable overrides below and the
still-earlier Books-only settings) is moved once into the canonical download client list; the
legacy file is then removed. The previously supported `Sabnzbd:*` / `Books:SABnzbd:*` environment
keys are only read by that one-time migration, not afterward.

### Jobs and state

Every submission — Books NZB URL/file or an Anime release — goes through `SabnzbdDownloadService` and creates one canonical operation (`IsDownload`, `ExternalProvider = sabnzbd`, `ExternalId = nzo_id` returned by SABnzbd). Books uses kind `sabnzbd-download`, Anime uses `anime-sabnzbd-download`.

One hosted monitor projects SABnzbd queue/history onto those operations: progress, bytes, queue speed (when a single job is downloading), ETA, post-processing state, completion and failure. Failures are classified (incomplete download, corrupt/repair failed, extraction failed, password-protected, script failure) and the operation error states the reason. A job that disappears from both queue and history for 15 minutes fails. After a restart the monitor continues from the persisted external references.

When a Books download completes, the Books inbox import runs once. Anime completions do not touch the Books inbox.

The acquisition store (`/data/acquisition/sabnzbd-acquisitions.json`) keeps only the durable relation of an anime acquisition (anime, episodes, attempts with their operation IDs and release identities, untried accepted candidates) and the blocklist of failed release identities. Candidate NZB URLs are stored protected because indexer URLs can carry credentials. It never stores job status; that is always read from the operation.

When an anime download fails, its release identity is blocklisted and the next accepted, non-blocklisted candidate is sent, up to the acquisition's attempt limit (default 3). The failed operation's log records the replacement or why the acquisition stopped. On startup, any acquisition whose latest attempt failed before the process stopped is advanced once.

### Cancel and retry

The operation detail page (`/Admin/Operation/{id}`) cancels an active SABnzbd job (removed from SABnzbd queue/history including files) and retries a failed one through SABnzbd's retry, which requeues the same operation with the new `nzo_id`. Retrying an anime attempt is only allowed for the latest attempt of its acquisition and removes that release from the blocklist. Anime operations also show the anime, episodes, attempt number and blocklisted releases.
