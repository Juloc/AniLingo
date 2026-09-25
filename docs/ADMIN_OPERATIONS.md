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

Initial tracked network/import work includes:

- Japanese subtitle/transcript preparation
- batch learning-text preparation
- novel chapter downloads
- remote novel imports
- remote EPUB imports
- SABnzbd downloads, including live queue/post-processing state when a full SABnzbd API key allows queue/history access
- Sonarr artwork downloads

Other job producers should use the same operation descriptor rather than adding their own history table.

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
