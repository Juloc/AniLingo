# Anime acquisition

AniLingo can find, download and import missing anime episodes itself: wanted episode → indexer
search (Prowlarr and/or direct Newznab) → release parsing and scoring → ownership check → a
download client (SABnzbd) → Operations tracking → import into the library with the naming
profile → library reconciliation. Everything runs in-process; there is no separate service.

AniLingo is usenet-only by design: torrent acquisition (Torznab indexers, qBittorrent or any other
torrent client, magnet links or `.torrent` handling) is intentionally unsupported. Prowlarr may
still aggregate torrent indexers on its own side; a torrent-protocol result it returns is scored
but never grabbed (see Limits).

Code: `Features/Acquisition/Pipeline` (inventory, pipeline, scheduler) and
`Features/Acquisition/Import` (import store, destination, executor). The pipeline only wires the
existing cores: release parser, quality profiles and scorer (`Quality`), indexer search
(`Indexers`, one `IIndexer` interface with Prowlarr and direct Newznab implementations),
monitoring engine (`Monitoring`), Sonarr ownership (`Ownership`), download client submission
(`DownloadClients`, one `IDownloadClient` interface with SABnzbd as its only implementation;
`Sabnzbd` keeps the anime-specific attempt/blocklist relation and the raw SABnzbd protocol client),
completed-download planner (`Import`) and naming (`Naming`). Periodic health checks
(`Features/Acquisition/Health`) test every enabled indexer and download client; an unhealthy entry
is skipped by the search coordinator/client selector with a logged reason.
`Features/Acquisition/Api` exposes the owner-only automation API described below; it calls the same
services and adds no state of its own beyond the API keys themselves.

## Setup (owner)

1. **Indexers** — `/Settings/Indexers`: add Prowlarr and/or direct Newznab connections (URL, API
   key stored encrypted, categories, priority, enable/disable). The search coordinator queries
   every enabled, healthy indexer and merges the results; only Prowlarr honors a per-anime
   indexer-ID restriction.
2. **Download clients** — `/Settings/DownloadClients`: add one or more SABnzbd connections (URL,
   API key stored encrypted, categories, priority, enable/disable). The pipeline and Books
   submissions pick the highest-priority enabled, healthy client and fail over to the next one on
   submission failure. SABnzbd's completed-job folder must be visible to AniLingo under the path
   SABnzbd reports (see [ADMIN_OPERATIONS.md](ADMIN_OPERATIONS.md#sabnzbd)).
3. **Management mode** — anime start in read-only Sonarr coexistence, where AniLingo never
   searches, grabs or imports. Choose *Parallel acquisition* or *AniLingo-managed* per anime under
   `/Settings/SonarrMigration` (see [SONARR_MIGRATION.md](SONARR_MIGRATION.md)).
4. **Per anime** — the **Acquisition** section on the anime page (`/Library/Anime/{id}`):
   *Monitored*, *Search when monitoring starts*, quality profile and optional Prowlarr indexer IDs.
5. **Naming** — imported files are named with the anime's naming profile
   ([ANIME_NAMING.md](ANIME_NAMING.md)).

The overview is `/Acquisition` (linked from Admin → System and from every anime's acquisition
section): schedule, connections, imports that need a decision, active downloads, wanted episodes,
monitored anime, recent decisions and recent imports.

## Where state lives

| Fact | Canonical store |
| --- | --- |
| Monitored flag, search-on-add, per-anime indexer IDs, tags, target root, wanted episodes, search attempts/backoff, schedule | `/data/acquisition/monitoring.json` (`AnimeMonitoringStore`) |
| Quality profile per anime | `/data/acquisition/quality-profiles.json` (`AnimeQualityProfileStore`; the default profile is not stored as an assignment) |
| Indexer connections (Prowlarr, direct Newznab) | `/data/acquisition/indexers.json` (`IndexerStore`) |
| Download client connections (SABnzbd) | `/data/acquisition/download-clients.json` (`DownloadClientStore`) |
| Indexer/download-client health (reachable, auth ok, last error, last check) | `/data/acquisition/health.json` (`AcquisitionHealthStore`) |
| Acquisition ↔ anime/episodes/attempts, untried candidates, blocklist | `/data/acquisition/sabnzbd-acquisitions.json` |
| Download status, progress, failure reason | the download client's Operation (`anime-sabnzbd-download`) |
| Import plan, per-file result, manual-import state | `/data/acquisition/imports.json` (`AnimeImportStore`) |
| Ownership (mode, AniLingo/Sonarr jobs, owned paths) | `/data/acquisition/ownership.json` |
| Import mode (global/per root), remote path mappings | `/data/acquisition/import-settings.json` (`AnimeImportSettingsStore`) |
| Tag catalog, delay profiles, tag-scoped indexer restrictions | `/data/acquisition/acquisition-policy.json` (`AcquisitionPolicyStore`) |
| Per-profile AniList Current/Planning auto-monitor opt-in | `/data/acquisition/anilist-auto-monitor.json` (`AniListAutoMonitorSettingsStore`) |
| Per-episode grab/delay/import/upgrade history | the database (`AcquisitionHistoryEntry`, via `AcquisitionHistoryService`) |
| Automation API keys (name, SHA-256 hash, created/last-used/revoked) — never the raw key | the database (`AcquisitionApiKey`, via `AcquisitionApiKeyService`) |
| Episodes and files | the library database, updated only by the library scanner |

`/Settings/Acquisition` is the one place to edit import mode, remote path mappings, tags, delay
profiles, indexer restrictions, the current profile's AniList auto-monitor rule, and to export or
restore a JSON backup of every store above except `health.json` (runtime health state, not a
setting, so it is never part of the backup). Indexer and download client API keys/passwords travel
in that backup encrypted with this installation's Data Protection keys, never in plain text.

When a rename changes an anime's key (series folder rename), the monitoring and import stores
are rekeyed together with the ownership and SABnzbd acquisition stores.

## Wanted episodes

`AnimeAcquisitionInventory` builds the expected episodes of an anime from the library and the
AniList data: explicit AniList episode-range mappings first (extended to the AniList episode count),
otherwise the matched AniList entry's episode count for a single local season. Several local
seasons without mappings, or an anime without AniList match, only track existing files (the anime
page and the run notes say why). Each expected episode keeps its local season/episode and the
AniList (absolute) episode number, and is searched with the titles of the AniList entry it maps to.

An episode is wanted when it is monitored and has no file (*Missing*), or when its file's parsed
quality is below the profile's upgrade cutoff and upgrades are allowed (*Upgrade wanted*). A file
whose quality cannot be parsed is never offered for upgrade.

## Scheduler

`AnimeAcquisitionScheduler` is a hosted service with one canonical interval
(`AnimeMonitoringSchedule`, default every 30 minutes, 5 minutes to 24 hours, editable on
`/Acquisition`; switching it off keeps owner-requested searches working). The first run starts
45 seconds after startup.

- Runs never overlap: periodic runs, **Search all now**, **Search wanted now** for one anime,
  search-on-add and interactive grabs all go through one gate.
- Each run searches at most 30 episodes, 6 per anime; the rest wait for the next run.
- Owner requests are queued (up to 50) and processed in order; a full run covers queued per-anime
  requests.
- A failed search (no accepted release, Prowlarr error, rejected submission) backs off
  exponentially (5 minutes, doubling up to 160 minutes). **Search wanted now** ignores the backoff but never
  searches an episode that is pending or already grabbed.

## Search, scoring and grab

For each wanted episode the pipeline creates an `anime-search` operation, queries every enabled,
healthy indexer entry (Prowlarr and direct Newznab, narrowed by any tag-scoped indexer
restriction) with the episode's titles, and evaluates every result:

1. usenet with an NZB link, otherwise rejected;
2. the parsed series title must match one of the anime's titles;
3. the release must cover the wanted episode (season/episode, or the AniList absolute number);
4. the quality profile must accept it (allowed qualities, sizes, required/forbidden terms, score);
5. the release must not already be pending/grabbed and `SonarrParallelSafety.CanGrab` (with a fresh
   Sonarr snapshot) must allow it;
6. for an upgrade, it must be better than the current file.

Every decision is written to the search operation's log (module `Acquisition`) with its reason,
quality, score and indexer; `/Acquisition` shows the recent ones. Accepted releases, best first, go
to `SabnzbdAcquisitionService.StartAsync`, which sends the first non-blocklisted one and keeps the
rest as fallbacks. The pipeline then registers an AniLingo ownership job for the acquisition and
marks the covered episodes as grabbed.

A new grab for an episode is refused while an earlier acquisition for it is still downloading or
its completed download waits for (manual) import. This check reads the acquisition relation and
Operations, so it also holds after a restart or with a lost monitoring state.

**Interactive search** (`/Acquisition?search=…`, from a wanted episode or an anime) shows the same
evaluation. **Grab** sends a release; **Grab anyway** sends a release the profile rejected. Ownership
and duplicate protection always apply.

## Download tracking

The existing SABnzbd monitor projects queue/history onto the download operation (progress, ETA,
failure reason). A failed download is blocklisted and the next candidate is sent, up to 3 attempts
(see [ADMIN_OPERATIONS.md](ADMIN_OPERATIONS.md#sabnzbd)). When all candidates are exhausted the
episode backs off and is searched again later. Cancelling a download on its operation page stops it without trying
another candidate; the episode also backs off and is searched again later unless it is unmonitored.

## Import

When an anime download completes, the SABnzbd monitor hands it to `AnimeImportExecutor`:

1. Wait if a library scan or rename is running (the import stays *Importing* and continues before
   the next scheduler run). Renames likewise refuse to start while an import runs.
2. Enumerate the completed folder SABnzbd reports.
3. Plan with `CompletedDownloadImportPlanner` (#298): map every file to the requested local
   episodes (season/episode or AniList absolute number), score it, detect existing files and apply
   `SonarrParallelSafety.CanImport`/`CanMutateLibraryPath`. Only confident single-anime matches are
   imported automatically; everything else needs a decision.
4. Build the destination with the naming profile resolved for the anime (anime, library root,
   default): the anime's existing series folder (a new folder is named by the series folder
   template), the season folder and the episode template. The name must scan back to the same
   anime key and episode; otherwise the file needs a decision.
5. Claim the destination as an AniLingo path and check `CanMutateLibraryPath` again; Sonarr-owned
   or Sonarr-active paths are never touched. An existing destination is never overwritten.
6. Move the file (and matching subtitle/NFO sidecars). An existing worse file is deleted only after
   the new file is in place.
7. Reconcile only that anime's folder with the library scanner, so the episode appears with the
   planned numbering.

The result is kept as an import record and logged on an `anime-import` operation (module `Import`).

### Needs a decision

Files the planner could not map confidently, destinations that exist or are owned by Sonarr,
names that would scan as another episode, and failed moves (read-only or unwritable library
folders) are listed under **Needs a decision** on `/Acquisition` with the reason. The owner can
import a file as a chosen season/episode (ownership and destination checks still apply) or dismiss
the import; dismissing leaves the downloaded files untouched. The episode stays grabbed while its
import waits for a decision, so it is not downloaded again.

## Restart recovery

On startup, and before every scheduler run, the scheduler:

- resumes imports that were interrupted or deferred,
- imports anime downloads that completed within the last 7 days while AniLingo was not running
  (the storage path is read from SABnzbd history),
- reconciles search attempts with the acquisition relation and Operations: an interrupted search
  whose release SABnzbd already accepted is recorded as grabbed (and its ownership job registered)
  instead of being searched again, finished imports clear the attempt, and failed or exhausted
  acquisitions back off.

The SABnzbd monitor separately advances failed downloads that were not handled before a restart.

## Import mode and remote path mapping

`/Settings/Acquisition` chooses how a completed download becomes a library file, globally and
per library root: **Move** (the historical default, removes the source), **Copy** (keeps the
source), **Hardlink** (no extra disk space, keeps the download seeding; fails the import with a
clear reason when the source and the library root are on different filesystems) or **Hardlink or
copy** (the explicit choice to fall back to a copy only on a different filesystem — no other mode
falls back silently). The same page's remote path mappings (`RemotePrefix` → `LocalPrefix`) rewrite
a path reported by the download client before anything reads it, and rewrite every Sonarr-observed
path (series folder, episode file, queue output, history) the same way, so Sonarr ↔ AniLingo path
matching (ownership checks, rename-loop detection) still works when the two containers mount the
shared storage differently — closing the "different mount paths" gap from
[SONARR_MIGRATION.md](SONARR_MIGRATION.md).

## Multiple root folders

An anime's imports go to the library root its existing files already live in. A brand-new anime
with no folder yet uses its assigned **target root** (anime page → Acquisition, defaulting to
"anime's current root") if one is set, otherwise the first enabled root.

## Tags, delay profiles and indexer restrictions

Anime carry simple owner-defined tags (catalog on `/Settings/Acquisition`, assignment on the anime
page). A **delay profile** waits N minutes after an episode becomes wanted before grabbing a
release that does not yet meet the quality profile's upgrade cutoff, so a preferred release has a
chance to appear first; the most specific matching profile (quality profile + tag, then either
alone, then an unscoped default) applies, and a release that already meets the cutoff always skips
the wait. A held-back release is logged as *Delayed* (recent decisions, acquisition history) and
does not count against the search-failure backoff. A **tag-scoped indexer restriction** narrows
(never widens) which of the canonical indexer entries (`/Settings/Indexers`: a Prowlarr entry as a
whole, or a direct Newznab entry) are searched at all for the tagged anime; several
applicable restrictions intersect. When a restriction applies but has no overlap with any currently
enabled entry, the anime is not silently searched with its unrestricted selection: no indexer is
searched that pass, and a *Skipped* entry with the reason is recorded (recent decisions, acquisition
history) instead — same as a delayed decision, not a search failure. (Separately, and unaffected by
this restriction, an anime's own `IndexerIds` only ever narrows within a single Prowlarr entry's own
sub-indexer aggregation — see Limits.)

## Richer acquisition history

Every grab, delay, import and upgrade is recorded per episode (release, score, quality, indexer,
reason, timestamp) and never trimmed by rekey (it is keyed by the anime's database ID, not its
string key). Shown on `/Acquisition` (recent, across anime) and on the anime page's Acquisition
section (that anime only).

## AniList list auto-monitor

Per profile, `/Settings/Acquisition` can opt in to monitoring anime that are on that profile's
AniList Current/Planning lists and already exist locally, checked once per full acquisition run.
Off by default; never adds an anime that is not local yet, and never touches an anime Sonarr
manages (read-only coexistence).

## Backup and restore

`/Settings/Acquisition` exports one JSON bundle of every canonical acquisition store listed above
(indexers, download clients, monitoring, import settings, policy, ownership, etc. — but never
`health.json`, which is runtime state, not a setting) and restores it with a dry-run preview
(per-file exists/valid-JSON/would-change) before anything is written; an invalid or
unsupported-version bundle is refused entirely. Indexer and download client API keys/passwords
travel in the bundle already encrypted with this installation's Data Protection keys; restoring on a
different installation keeps everything else but may require re-entering those credentials if they
cannot be decrypted there.

## Automation API

`Features/Acquisition/Api` exposes an owner-only REST API at `/api/acquisition/v1` for an external
automation client (a script, a Sonarr-style scheduler, a dashboard). Every write goes through the
exact same services the pages above call (`AnimeAcquisitionPipeline`, `AnimeAcquisitionScheduler`,
`AnimeImportExecutor`) — there is no second code path, so the API can never desync from the owner
UI. Errors are RFC 7807 problem-details JSON (`application/problem+json`) with a clear `detail`.

### Authentication

Two ways to authenticate, both resolving to the owner account:

- **Cookie**: the normal browser session (`/Account/Login`), same as every other owner page.
- **API key**: an `X-Api-Key: <key>` header. Keys are created, listed and revoked on
  `/Settings/ApiKeys` (owner only). The raw key is shown exactly once, right after creation; only a
  SHA-256 hash is ever stored, so it cannot be recovered or shown again — only revoked and
  replaced. A key is scoped to this API alone: it is wired into no other endpoint group, so a
  leaked key cannot sign into the browser UI or the native client API.

An invalid, malformed or revoked key returns `401`; an authenticated non-owner session returns
`403`. Requests are rate-limited (`acquisitionApi` policy, 60/minute per key or per IP for a cookie
session).

### Endpoints

| Method & path | Purpose |
| --- | --- |
| `GET /monitored` | Monitored anime with management mode, assigned quality profile, wanted-episode count and Prowlarr indexer-id restriction. |
| `GET /anime/{animeId}/monitoring` | One anime's monitoring settings: monitored, search-on-add, quality profile, indexer ids, tags, target root, management mode. |
| `PUT /anime/{animeId}/monitoring` | Sets the same fields (same call as the owner's Acquisition settings form). Refused with `409` for an anime in read-only Sonarr coexistence. |
| `GET /wanted?animeId=` | Wanted episodes (missing or upgrade-wanted), optionally filtered to one anime. |
| `POST /search` | Queues a search for every monitored anime (same request "Search now" sends) and returns a tracking operation id. `409` when the scheduler's request queue is full. |
| `POST /anime/{animeId}/search` | Queues a search for one anime's wanted episodes. `409` for a read-only Sonarr-coexistence anime or a full queue. |
| `GET /operations?status=&kind=&limit=` | Recent search/grab operations (add `kind=anime-import` for import operations). |
| `GET /operations/{operationId}` | One operation's snapshot and log entries. |
| `GET /history?animeId=&limit=` | Richer per-episode acquisition history (grab/delay/import/upgrade/skip), optionally for one anime. |
| `GET /imports?attention=true` | Manual-import records; `attention=true` limits to those needing a decision. |
| `POST /imports/{recordId}/resolve` | Imports a file as a chosen season/episode (`{"sourcePath","season","episode"}`), same as the owner's manual-import form. |
| `POST /imports/{recordId}/dismiss` | Dismisses a manual-import record; downloaded files are left untouched. |
| `GET /health` | Indexer and download-client health summary (enabled, reachable/auth-ok, last error, last checked). |

### Examples

```bash
curl -H "X-Api-Key: $KEY" https://anilingo.example/api/acquisition/v1/monitored

curl -H "X-Api-Key: $KEY" -X POST https://anilingo.example/api/acquisition/v1/anime/$ANIME_ID/search
# => 202 {"operationId":"...","message":"Search for 'frieren' queued. ..."}

curl -H "X-Api-Key: $KEY" -H "Content-Type: application/json" \
  -X PUT https://anilingo.example/api/acquisition/v1/anime/$ANIME_ID/monitoring \
  -d '{"monitored":true,"searchOnAdd":true,"qualityProfileId":null,"indexerIds":[],"tagIds":["dub"],"targetRootId":null}'

# Sonarr-owned anime: refused, not silently ignored.
curl -i -H "X-Api-Key: $KEY" -X POST https://anilingo.example/api/acquisition/v1/anime/$ANIME_ID/search
# HTTP/1.1 409 Conflict
# Content-Type: application/problem+json
# {"type":"...","title":"Refused.","status":409,
#  "detail":"'frieren' is in ReadOnlyCoexistence mode; Sonarr owns this anime, ..."}
```

## Limits

- AniLingo is usenet-only by owner decision: torrent acquisition (Torznab indexers, qBittorrent or
  any other torrent client, magnet links or `.torrent` handling) is intentionally unsupported and
  will not be added. The anime pipeline searches every enabled, healthy indexer (Prowlarr and
  direct Newznab) and scores every result, but a release is only ever grabbed when its protocol is
  usenet; a torrent-protocol release Prowlarr itself returns (from a torrent indexer configured on
  Prowlarr's side, outside AniLingo) is always shown as rejected ("not a usenet release").
- No RSS feed polling: wanted episodes are found by the scheduled search.
- Quality profiles can be assigned per anime; editing profiles has no UI yet.
- AniList auto-monitor only enables monitoring for anime that already exist locally; it does not add
  a new anime to the library.
- An anime's own `IndexerIds` selection (set on the anime page) only ever narrows within a single
  Prowlarr entry's own sub-indexer aggregation; it does not choose which whole indexer entries
  (Prowlarr/Newznab) are searched. Only a tag-scoped indexer restriction operates at that entry
  level.
