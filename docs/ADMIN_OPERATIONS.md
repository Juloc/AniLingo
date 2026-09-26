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
- `/Admin/Logs` — structured operation logs
- `/Admin/System` — media roots and server integrations
- `/Admin/Subtitles`
- `/Admin/Sonarr`
- `/Admin/Ai`

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

Retry is available while the current process still owns the original retryable local delegate. After a process restart, local history remains but that transient delegate is intentionally unavailable. Durable provider-backed jobs such as SABnzbd instead resume status monitoring from their persisted external reference.

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
- SABnzbd downloads, including live queue/post-processing state when a full SABnzbd API key allows queue/history access
- Sonarr artwork downloads

Manga uploads on `/Manga` and `/Discover/MangaImport` accept up to 200 CBZ/ZIP archives, at most 1 GB each and 4 GB in total. The raised request-body and multipart limits apply only to the owner's `Upload` handler on those two pages; every other request, including uploads attempted by non-owner accounts, keeps the ASP.NET Core defaults. A reverse proxy in front of AniLingo must not cap request bodies below roughly 4 GB for these uploads (Caddy has no body limit by default; nginx needs `client_max_body_size`).

Synchronous request-bound work uses the shared `OperationRunner`, which writes the same `Operations` / `OperationLogs` lifecycle as queued jobs. Long work that can safely outlive the HTTP request continues to use `BackgroundJobQueue`.

Playback remux/transcode is currently streamed live by the media response path rather than pre-generated as a durable background preparation job. It is therefore not recorded as a separate completed operation. If a future UI adds explicit cached playback preparation, that producer must use the existing playback/operation lane instead of creating another task store.

Other job producers should use the same operation descriptor rather than adding their own history table.

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


## External download monitoring

SABnzbd submissions remain canonical in the Books integration. Operations does not implement a second submit path.

Before submission AniLingo snapshots visible SAB job IDs. After the existing submit succeeds it resolves the newly assigned `nzo_id` and stores it on the operation. A hosted monitor then projects SAB queue/history state into the operation's progress, bytes, speed/ETA (when SAB exposes them), and terminal success/failure.

The full SABnzbd API key can read queue/history. An NZB-only key may still submit a job but cannot provide live monitoring; AniLingo records the submission and clearly marks live tracking as unavailable rather than storing the API key in Operations.
