# Anime acquisition

AniLingo can find, download and import missing anime episodes itself: wanted episode → indexer
search (Prowlarr and/or direct Newznab/Torznab) → release parsing and scoring → ownership check →
a download client (SABnzbd or qBittorrent) → Operations tracking → import into the library with
the naming profile → library reconciliation. Everything runs in-process; there is no separate
service.

Code: `Features/Acquisition/Pipeline` (inventory, pipeline, scheduler) and
`Features/Acquisition/Import` (import store, destination, executor). The pipeline only wires the
existing cores: release parser, quality profiles and scorer (`Quality`), indexer search
(`Indexers`, one `IIndexer` interface with Prowlarr and direct Newznab/Torznab implementations),
monitoring engine (`Monitoring`), Sonarr ownership (`Ownership`), download client submission
(`DownloadClients`, one `IDownloadClient` interface with SABnzbd and qBittorrent implementations;
`Sabnzbd` keeps the anime-specific attempt/blocklist relation and the raw SABnzbd protocol client),
completed-download planner (`Import`) and naming (`Naming`). Periodic health checks
(`Features/Acquisition/Health`) test every enabled indexer and download client; an unhealthy entry
is skipped by the search coordinator/client selector with a logged reason.

## Setup (owner)

1. **Indexers** — `/Settings/Indexers`: add Prowlarr and/or direct Newznab/Torznab connections
   (URL, API key stored encrypted, categories, priority, enable/disable). The search coordinator
   queries every enabled, healthy indexer and merges the results; only Prowlarr honors a
   per-anime indexer-ID restriction.
2. **Download clients** — `/Settings/DownloadClients`: add SABnzbd (usenet) and/or qBittorrent
   (torrent) connections (URL, API key/password stored encrypted, categories, save path, priority,
   enable/disable). The pipeline and Books submissions pick the highest-priority enabled, healthy
   client that supports a release's protocol and fail over to the next client of that protocol on
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
| Monitored flag, search-on-add, per-anime indexer IDs, wanted episodes, search attempts/backoff, schedule | `/data/acquisition/monitoring.json` (`AnimeMonitoringStore`) |
| Quality profile per anime | `/data/acquisition/quality-profiles.json` (`AnimeQualityProfileStore`; the default profile is not stored as an assignment) |
| Indexer connections (Prowlarr, direct Newznab/Torznab) | `/data/acquisition/indexers.json` (`IndexerStore`) |
| Download client connections (SABnzbd, qBittorrent) | `/data/acquisition/download-clients.json` (`DownloadClientStore`) |
| Indexer/download-client health (reachable, auth ok, last error, last check) | `/data/acquisition/health.json` (`AcquisitionHealthStore`) |
| Acquisition ↔ anime/episodes/attempts, untried candidates, blocklist | `/data/acquisition/sabnzbd-acquisitions.json` |
| Download status, progress, failure reason | the download client's Operation (`anime-sabnzbd-download`) |
| Import plan, per-file result, manual-import state | `/data/acquisition/imports.json` (`AnimeImportStore`) |
| Ownership (mode, AniLingo/Sonarr jobs, owned paths) | `/data/acquisition/ownership.json` |
| Episodes and files | the library database, updated only by the library scanner |

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

For each wanted episode the pipeline creates an `anime-search` operation, queries Prowlarr with the
episode's titles, and evaluates every result:

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

## Limits

- The anime pipeline searches every enabled, healthy indexer (Prowlarr and direct
  Newznab/Torznab) and scores usenet and torrent releases alike, but only grabs and downloads
  usenet releases through a SABnzbd-compatible client today; torrent releases are shown as
  rejected ("not a usenet release") until the grab path also submits through a torrent-capable
  client (qBittorrent). qBittorrent's client (login, add, status, delete) and the
  priority/failover client selection are implemented and used by Books-style usenet submissions
  and the download client health checks; wiring the anime grab path to it is a follow-up.
- No RSS feed polling: wanted episodes are found by the scheduled search.
- Imports move files; copy/hardlink modes are not configurable yet.
- Quality profiles can be assigned per anime; editing profiles has no UI yet.
- Per-anime indexer restriction (`IndexerIds`) only applies to Prowlarr's own indexer
  aggregation, not to individually restricting direct Newznab/Torznab entries.
