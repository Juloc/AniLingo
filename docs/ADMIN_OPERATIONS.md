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

## Non-root container runtime and /data ownership

The AniLingo image runs as the non-root `app` user of the official ASP.NET base image (`APP_UID`, UID/GID `1654:1654`). It needs no privileged mode and no added Linux capabilities; it also runs with `cap_drop: [ALL]` and `security_opt: [no-new-privileges:true]`.

Writable locations:

- `/data` — all persistent state: SQLite database, Data Protection keys (`/data/keys`), protected integration settings, Codex credentials (`CODEX_HOME=/data/codex`), Whisper model and transcription cache, manga imports/cache, artwork, playback/HLS cache and book data.
- `/tmp` — scratch space (for example temporary Codex work directories).

The application under `/app`, the bundled `codex`, `whisper-cli`, `ffmpeg`/`ffprobe`, MeCab dictionary and JMdict data are root-owned and read-only for the runtime user. Media mounts such as `/media/anime:ro` stay read-only; their files only need to be readable by UID `1654` (world-readable, or grant a group with `group_add: ["<media-gid>"]`).

### Startup ownership check

Before starting the application, the container entrypoint checks that every directory and file under `/data` is readable and writable by the runtime user. If not, it exits with a non-zero code and logs the first offending path plus the exact fix command. It never changes ownership itself and never falls back to running as root.

### Fresh installations

A new, empty named volume is initialized from the image with `1654:1654` ownership, so no action is needed. A bind-mounted host directory must be owned by (or writable for) UID/GID `1654:1654`, e.g. `sudo chown -R 1654:1654 /srv/anilingo-data`.

### Upgrading from a root-based image

Images released before the non-root runtime wrote `/data` as root. After upgrading, AniLingo refuses to start and `docker logs` shows `AniLingo startup aborted: ... is not readable and writable by the AniLingo runtime user.` Hand the existing data over once, with AniLingo stopped:

```bash
docker compose stop anilingo
docker volume ls   # find the data volume, e.g. <project>_anilingo-data
docker run --rm --user 0:0 --entrypoint chown -v <project>_anilingo-data:/data ghcr.io/juloc/anilingo:latest -R 1654:1654 /data
docker compose up -d
```

For a bind mount, pass the host path instead of the volume name (or run `chown -R 1654:1654` on the host). The data itself is not modified. Rolling back to an older root-based image keeps working, but files it creates are root-owned again; repeat the command before returning to the current image.

### Custom runtime user

If the deployment sets `user: "<uid>:<gid>"` in Compose (for example to match NAS permissions), `/data` must be owned by that UID/GID instead; the startup check and its fix command use the effective UID/GID of the container.
