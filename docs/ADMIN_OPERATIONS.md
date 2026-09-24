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

When AniLingo starts, operations left in `Queued` or `Running` for a worker lane from the previous process are marked `Interrupted`. Anonymous .NET delegates are deliberately not serialized. This prevents phantom running jobs while preserving accurate history.

Retry is available while the current process still owns the original retryable delegate. After a process restart, history remains but that transient delegate is intentionally unavailable. Durable resumable handler payloads can be introduced later for individual job kinds without creating a second queue model.

## Downloads

Downloads are ordinary operations with `IsDownload = true` and optional byte/progress fields. This allows one Downloads view without a parallel download database.

Initial tracked network/import work includes:

- Japanese subtitle/transcript preparation
- batch learning-text preparation
- novel chapter downloads
- remote novel imports
- remote EPUB imports
- SABnzbd submissions
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
