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
- `/Settings/Sabnzbd` — the one SABnzbd connection shared by Books and Anime (linked from Admin → System and Books → Acquisition settings)

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

When AniLingo starts, local operations left in `Queued` or `Running` for a worker lane from the previous process are marked `Interrupted`. Anonymous .NET delegates are deliberately not serialized. This prevents phantom running jobs while preserving accurate history.

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
- SABnzbd downloads for Books and Anime, including live queue/post-processing state when a full SABnzbd API key allows queue/history access
- Sonarr artwork downloads

Manga uploads on `/Manga` and `/Discover/MangaImport` accept up to 200 CBZ/ZIP archives, at most 1 GB each and 4 GB in total. The raised request-body and multipart limits apply only to the owner's `Upload` handler on those two pages; every other request, including uploads attempted by non-owner accounts, keeps the ASP.NET Core defaults. A reverse proxy in front of AniLingo must not cap request bodies below roughly 4 GB for these uploads (Caddy has no body limit by default; nginx needs `client_max_body_size`).

Synchronous request-bound work uses the shared `OperationRunner`, which writes the same `Operations` / `OperationLogs` lifecycle as queued jobs. Long work that can safely outlive the HTTP request continues to use `BackgroundJobQueue`.

Playback remux/transcode is currently streamed live by the media response path rather than pre-generated as a durable background preparation job. It is therefore not recorded as a separate completed operation. If a future UI adds explicit cached playback preparation, that producer must use the existing playback/operation lane instead of creating another task store.

Other job producers should use the same operation descriptor rather than adding their own history table.

### Structured details

`Operations.Details` is an optional, kind-specific JSON document (at most 8 000 characters) for the structured facts of one run that the generic columns cannot hold. It is the only place for such data; producers must not add a parallel table for it. Library scans are the first kind that uses it.

## Library scans

Every library scan — startup, the **Queue library scan** button, filesystem-change scans and periodic reconciliation — goes through `LibraryScanCoordinator` (`Features/Library`). There is no separate scan-run store: each run is one operation of kind `library-scan` on the `Maintenance` lane, and `/Admin/Scans` plus the per-root state on `/Admin/System` are views over `Operations`.

`Details` of a scan run carries the root ID, the scanned folder (empty for the whole root), the trigger (`Manual`, `Startup`, `Watch`, `Periodic`, `Retry`), the current phase (`Enumerating`, `Reconciling`, `Metadata`, `Artwork`, `Subtitles`, `Completed`), files processed/total and, once finished, the counters: media files, added, changed, removed, skipped, subtitle files, artwork imported, NFO files ignored, errors and warnings. Progress percent and the message are written through the normal operation progress at most once per second; phase changes are always written.

Item-level findings (unmatched media files, ignored NFO files) are `OperationLogs` warnings of module `Scan` whose paths are relative to the root. Neither the logs nor the persisted error of a failed scan contain the host path of the root; the full exception goes to the application logger only.

Coalescing: while a full scan of a root is queued or running, every further request for that root is rejected with a pointer to the active run (the UI disables the button). A folder scan only blocks the same folder; a full scan requested beside queued folder scans simply runs after them because the maintenance lane is sequential.

Restart and retry: an abandoned running scan becomes `Interrupted` through the normal lane recovery. **Run again** on `/Admin/Scans` queues a fresh run for the root and folder recorded in the finished run's `Details`; this works after a restart, unlike the generic delegate retry. The newest 200 finished scan runs are kept; older ones and their logs are pruned after each completed scan.

### Filesystem watcher and periodic reconciliation

`LibraryWatchService` attaches a `FileSystemWatcher` to every enabled root that is readable and supports events. Callbacks only record the changed top-level folder (the anime directory); after the root has been quiet for 10 seconds the dirty folders are queued as folder scans (`Watch` trigger). A watcher error or buffer overflow queues a full reconciliation of that root instead and the watcher is re-attached on the next sync. Roots on mounts without event support, or roots that are offline, simply have no watcher; the service re-checks the root list every minute and attaches when the root becomes readable.

Each root has one **Periodic reconciliation** interval (`LibraryRoots.ReconciliationIntervalMinutes`, default 30, `0` turns it off, edited on `/Admin/System`). A full scan of the root is queued when the last completed full scan and the last periodic attempt are both older than the interval. An unavailable root is skipped without creating an operation and is not retried before the next interval, so an offline NAS does not fill the history.

## Logs and security

`OperationLogs` contains structured operational events keyed by operation ID.

Persisted operational data must not contain:

- passwords
- API keys or access tokens
- cookies
- raw request headers
- private filesystem paths

Exceptions are persisted as bounded type/message summaries rather than stack traces. Full server diagnostics may continue to use the normal application logger.

## SABnzbd

AniLingo has one SABnzbd integration (`Features/Acquisition/Sabnzbd`) used by Books and Anime.

### Configuration

The owner configures it once under `/Settings/Sabnzbd`: base URL, API key and one category per purpose (Books, Anime; empty means the SABnzbd default category). Settings are stored in `/data/acquisition/sabnzbd.json`; the API key is protected with ASP.NET Core Data Protection. **Test connection** checks both reachability and that the key can read the queue — the NZB-only key can submit but cannot provide progress, so use the full API key.

Supported configuration keys (environment variables use `__`, for example `Sabnzbd__ApiKey`). A set key overrides the matching stored field and the settings page shows the override:

| Key | Field |
| --- | --- |
| `Sabnzbd:BaseUrl` | SABnzbd URL |
| `Sabnzbd:ApiKey` | API key |
| `Sabnzbd:Categories:Books` | Books category |
| `Sabnzbd:Categories:Anime` | Anime category |

The earlier Books-only keys `Books:SABnzbd:BaseUrl`, `Books:SABnzbd:ApiKey` and `Books:SABnzbd:Category` are no longer read; startup logs the replacement key when one is still set.

On startup, SABnzbd fields that earlier builds stored in `/data/books/integrations.json` are moved once into the shared settings (existing shared settings win) and removed from the Books file, which now only holds the Books inbox path.

### Jobs and state

Every submission — Books NZB URL/file or an Anime release — goes through `SabnzbdDownloadService` and creates one canonical operation (`IsDownload`, `ExternalProvider = sabnzbd`, `ExternalId = nzo_id` returned by SABnzbd). Books uses kind `sabnzbd-download`, Anime uses `anime-sabnzbd-download`.

One hosted monitor projects SABnzbd queue/history onto those operations: progress, bytes, queue speed (when a single job is downloading), ETA, post-processing state, completion and failure. Failures are classified (incomplete download, corrupt/repair failed, extraction failed, password-protected, script failure) and the operation error states the reason. A job that disappears from both queue and history for 15 minutes fails. After a restart the monitor continues from the persisted external references.

When a Books download completes, the Books inbox import runs once. Anime completions do not touch the Books inbox.

The acquisition store (`/data/acquisition/sabnzbd-acquisitions.json`) keeps only the durable relation of an anime acquisition (anime, episodes, attempts with their operation IDs and release identities, untried accepted candidates) and the blocklist of failed release identities. Candidate NZB URLs are stored protected because indexer URLs can carry credentials. It never stores job status; that is always read from the operation.

When an anime download fails, its release identity is blocklisted and the next accepted, non-blocklisted candidate is sent, up to the acquisition's attempt limit (default 3). The failed operation's log records the replacement or why the acquisition stopped. On startup, any acquisition whose latest attempt failed before the process stopped is advanced once.

### Cancel and retry

The operation detail page (`/Admin/Operation/{id}`) cancels an active SABnzbd job (removed from SABnzbd queue/history including files) and retries a failed one through SABnzbd's retry, which requeues the same operation with the new `nzo_id`. Retrying an anime attempt is only allowed for the latest attempt of its acquisition and removes that release from the blocklist. Anime operations also show the anime, episodes, attempt number and blocklisted releases.
