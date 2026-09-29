# Information architecture

Canonical reference for the consumer/admin split, the media model, provider mapping, admin
navigation and the Sonarr/Radarr/Bazarr/Readarr parity gap. Source: [#510](https://github.com/Juloc/Jularr/issues/510)
(epic) and its comments. Implementation is split into [#517](https://github.com/Juloc/Jularr/issues/517)
(navigation), [#518](https://github.com/Juloc/Jularr/issues/518) (admin dashboard, sessions),
[#519](https://github.com/Juloc/Jularr/issues/519) (consumer pages), [#520](https://github.com/Juloc/Jularr/issues/520)
(Home rows/Discover filters), [#521](https://github.com/Juloc/Jularr/issues/521) (roles/permissions),
[#522](https://github.com/Juloc/Jularr/issues/522) (Android TV navigation). This document (#516)
does not change code.

Related docs, not repeated here: [ADMIN_OPERATIONS.md](ADMIN_OPERATIONS.md),
[ANIME_ACQUISITION.md](ANIME_ACQUISITION.md), [ANIME_NAMING.md](ANIME_NAMING.md),
[READING_ACQUISITION.md](READING_ACQUISITION.md), [MEDIA_SEGMENTS.md](MEDIA_SEGMENTS.md),
[ANDROID_CLIENTS.md](ANDROID_CLIENTS.md).

## 1. Consumer vs Admin

Rule (#510): a normal user must never need to understand Sonarr/Radarr/Bazarr/Readarr concepts —
provider IDs, root folders, naming profiles, indexers, download clients, scans, mapping conflicts,
remux jobs, transcodes or acquisition pipelines. Everything that exposes those concepts is Admin.
Admin and consumer UI are visually related but structurally separate; no raw technical metadata
appears on consumer pages unless a user genuinely needs it (dub/sub availability, progress).

### Desktop/tablet sidebar

Base navigation stays visible; Admin and Settings expand inline instead of opening a disconnected
shell:

- Home, Library, Watchlist, Calendar, Activity, Admin (permission-gated), Settings, Profile.
- Selecting **Admin** expands the sidebar section directly under it with the Admin navigation.
  Selecting **Settings** does the same for personal settings.
- Only one large contextual section expands at a time on narrower layouts; the last selected
  child page is remembered.
- Today's implementation (`UiShellNavigation.Build`, `Features/Localization/UiShellNavigation.cs`)
  already renders Admin/Settings as an inline expanding `Context` under the app shell rather than a
  separate shell — the structural piece exists. What #517 must still finish: an explicit
  **Activity** primary destination (today Activity-equivalent pages sit only under Admin →
  Operations/Scans/Logs) and the mobile Profile grouping below.

### Mobile bottom navigation

Netflix-style consumer app, not a management console:

- Bottom bar: Home, Calendar, Watchlist, Profile. Search stays globally accessible at the top.
- No separate Library tab: Home and Library are one experience with type filters
  (All/Movies/TV/Anime/Manga/Novels/Books).
- Activity, Downloads, Devices, Settings and Admin (permission-gated) all live under **Profile**,
  which drills into dedicated navigation screens rather than permanent nested menus.
- Today's mobile bar (`UiNavigationCatalog.MobilePrimarySlots`) is
  `home, library, reading, learn|discover` with no Profile slot and no Calendar slot — this is the
  concrete gap #517 closes.

### TV sidebar

Aggressively simplified for D-pad use, intentionally different from desktop:

- Home, Watchlist, Activity, Profile/Settings. No dedicated Library page (availability is a
  state/filter inside Home, not a destination) and no separate Search item — Home carries a
  search field at the top and combines search, discovery and Continue Watching in one surface.
  No separate Movies/TV/Anime destinations: content filters instead.
- Trailer preview on focus (no hover, no mouse dependency); strong focus state; Back restores
  previous view/focus; remembers last focused item per screen.
- None of this exists yet for Android TV nav; it is entirely #522's scope (the current Android TV
  contract in [ANDROID_CLIENTS.md](ANDROID_CLIENTS.md) §9 only fixes
  Library → Anime → Episode → Player browse layering and remote key semantics, not the sidebar
  itself).

### Home as Discover entry

Home's Netflix-style rows are dual-purpose: scrolling browses the row, activating the row heading
opens Discover pre-filtered to that row's facet (media type, genre, "Trending Anime", franchise,
etc.). Discover/Search must therefore be filter-driven and able to receive a filter from Home,
Calendar, Watchlist, franchises, genres and tags alike. `/Discover` exists today
(`Features/Discovery`) but only as an AniList-backed browse/import surface; it does not yet accept
row-sourced filters or show local availability/watchlist state on cards. That wiring is #520.

## 2. Media model

Four layers per #510. Mapped to what exists in `src/Jularr.Web/Data` and `Features/` today:

| Layer | #510 description | State | Where |
| --- | --- | --- | --- |
| 1. Storage/Admin structure | Root, work folder, season/special folder, media file/sidecars | **Exists** | `LibraryRoot`, `MediaFile` (`Data/AppDbContext.cs`); root config and wake state on `/Admin/System` |
| 2. Jularr internal structure | Work, season/unit, episode/chapter/volume, stable internal IDs | **Exists** | `Anime`, `Episode`, `MediaFile` for video; `NovelWork`/`NovelVolume`/`NovelChapter`, `BookEdition`/`BookFile` for reading. Manga is file-based (`MangaModels.cs`: `MangaSeriesItem`/`MangaChapterItem`), not a DB entity — its "stable ID" is a derived series key, not a row id |
| 3. Provider mappings | AniList, TVDB, TMDb, IMDb, MAL, future providers | **Exists** | `AnimeMetadata` (AniList match: provider+external id, cover/banner, unique per anime) and `AnimeLocalMetadata` (TVDB/MAL ids, NFO-sourced) exist; `NovelAnimeMapping` cross-references novel↔anime. Provider **roles are now independently configurable** (display metadata, episode structure, acquisition identity, progress tracking, artwork, cross-reference IDs — `Features/Mapping/MappingProviderRoles.cs`) with a global default per role plus a per-anime override, stored via `ProviderRoleAssignmentStore` (raw-SQL migration `20260929150000`, no EF entity). With nothing stored the resolved roles reproduce Jularr's implicit behaviour (AniList = display/progress/artwork, local numbering = structure/acquisition, TVDB = cross-reference); assigned on `/Settings/MappingReview`. See [#525](https://github.com/Juloc/Jularr/issues/525) |
| 4. User presentation groups | Seasons, parts, cours, story arcs, specials, person/week/round groups (reality shows), independent of files/provider coordinates | **Partial** | The presentation-group layer now exists (#524). A `PresentationGroup` is a per-work free-text name plus an ordered list of inclusive internal-unit ranges (episode `Number` / chapter / volume number) with a group order; it is keyed by (`MediaType`, `WorkId`) and is media-type-agnostic. Stored in `PresentationGroups`/`PresentationGroupRanges` (raw-SQL migration `20260929130000_AddPresentationGroups`, accessed via `Features/Presentation/PresentationGroupStore.cs` as derived state — no EF entity, so it never touches episode identity, file paths or provider mappings). `PresentationGrouping.Arrange` derives the display sections; the anime detail page (`Pages/Library/Anime.cshtml`) renders episodes under collapsible group headings when groups exist and falls back to the plain list otherwise, and an owner-only editor (`Pages/Library/PresentationGroups.cshtml`, linked from the `_ManageSheet` Files group) creates/reorders/deletes groups and assigns ranges with a preview before apply. **Follow-up:** the model and store are ready for reading media, but the editor and consumer wiring for manga/novel volumes/chapters are not built yet. See [#524](https://github.com/Juloc/Jularr/issues/524) |

Reality-show example ("Anna: E01-E05" over S01E01-E20) and anime-cour example (AniList Part 1
E01-E11 / Part 2 E01-E12 over one local season) are both now expressible through layer 4 for anime;
the same mechanism is designed to cover reading media (manga/novel volumes and chapters) once its
editor UI is wired.

**Universal media core (#592) and its workflow layer.** A provider-independent `Work` (movie, series,
anime, book, manga, light novel) carries a stable internal id with external provider identities,
titles, structure, editions/versions, typed relations and per-field provenance hanging off it
(`Features/MediaCore`: `Work`, `WorkExternalIdentity`, `WorkTitle`, `WorkFieldProvenance`,
`WorkService`/`WorkQueryService`; migration `MediaCoreFoundation`). Legacy per-type records
(Anime/NovelWork/BookEdition/MangaSeries) are bridged non-invasively through `WorkSourceLink`, so
watch progress, notes, wanted and collections stay attached to the legacy id.

- **Identity correction / merge / split (#432).** `WorkService.ReassignExternalIdentityAsync` moves a
  provider id to another work; `SplitExternalIdentityToNewWorkAsync` peels one into a fresh work;
  `MergeWorksAsync` absorbs one work into another (moving its bridges, identities, titles, relations,
  structure and provenance) and preserves progress by repointing the `WorkSourceLink`. Every change
  is written to the append-only `WorkIdentityChange` log (migration `WorkIdentityChanges`, no FK so it
  outlives an absorbed work). Duplicate suggestions come from
  `WorkQueryService.FindDuplicateSuggestionsAsync` (shared normalized title within one media type;
  already-related pairs suppressed). **Exists.**
- **Field-level provenance (#435).** Each displayed field records its source and precedence
  (`WorkFieldProvenance` + `MetadataFieldSources`: manual > preferred provider > secondary >
  local/NFO > filename). `LegacyWorkBridge` records provenance as it mirrors per-type titles, and the
  owner can pin a field as a manual override (`WorkService.SetManualFieldOverrideAsync`) so a provider
  refresh cannot overwrite it. **Exists** at the bridge/core level; routing every legacy per-type
  provider write through the ladder lands with the individual #556 library children.
- **Merge/Duplicate Review Center (#437).** Owner-only `/Admin/MergeReview`
  (`Pages/Admin/MergeReview/Index.cshtml(.cs)`, `mapping.edit` policy) lists duplicate suggestions and
  flagged identity conflicts with merge / split / reassign / confirm actions plus the field-source
  pin, and shows the identity-change history. Linked from Metadata & Mapping
  (`/Settings/MappingReview`). **Exists.**

## 3. Anime multi-provider mapping

**Provider roles** (display metadata, episode structure, acquisition identity, progress tracking,
artwork, cross-reference IDs) are now an owner-configurable setting, not just de facto behaviour.
`Features/Mapping/MappingProviderRoles.cs` defines the six roles and their allowed providers;
`ProviderRoleAssignmentStore` (raw-SQL table `ProviderRoleAssignments`, migration `20260929150000`,
no EF entity) stores a global default per role and a per-anime override, resolved most-specific-first
(work override → global default → built-in). The built-ins reproduce the previous implicit behaviour
(AniList = display/progress/artwork, local/absolute numbering = structure/acquisition, TVDB =
cross-reference), so nothing changes until a role is reassigned on `/Settings/MappingReview`.
**Exists.**

**Range mapping**: `AnimeSequenceMappingPlanner` (`Features/MediaMapping/AnimeSequenceMapping.cs`)
plans local-season-to-AniList-part ranges from contiguous local numbering and an anchor AniList
entry; `AnimeSpecialMapping.cs` covers specials/OVA/ONA separately. `NovelAnimeMapping` and
`ReadingSegmentMappingStore` (chapter-range ↔ external id) do the equivalent for reading media.
**Exists** for the planning/automatic-match mechanics; **partial** for the owner-facing workflow
(below).

**`/Settings/MappingReview`** (`Pages/Settings/MappingReview.cshtml(.cs)`, backed by
`MediaMappingReviewStore`) still lists `MediaMappingReviewTask` items (provider, external id, title,
score and evidence per candidate) and keeps **Dismiss**, but it is now also the anime range-mapping
apply workspace (`?animeId=`). For a selected anime it shows the match candidates with confidence,
a local-vs-provider side-by-side coverage table with per-episode **exact/partial/missing/conflict/
unmapped** states (classified by the pure `AnimeMappingPlanner`), a range form for per-episode
overrides and specials (season 0), an explicit **Mark unmapped** action, **Preview** before **Apply**,
and the per-anime provider-role overrides. Applying goes through `AnimeMappingApplyService`, which
replaces the work's ranges in the canonical episode-mapping store (`AniListAccountStore`, which
`AnimeMetadataService.ResolveEpisodeAsync` already consumes — no second source of truth) and writes
a durable **audit** entry (`MappingAuditStore`, table `AnimeMappingAuditEntries`). Remapping is
progress-safe: watch progress keys on the stable `EpisodeId` while mappings key on `AnimeId` + local
range, so changing a mapping only rewrites provider coordinates. **Exists**
([#525](https://github.com/Juloc/Jularr/issues/525)).

**`/Settings/MappingSegments`** (`Pages/Settings/MappingSegments.cshtml(.cs)`, backed by
`ReadingSegmentMappingStore`) maps local chapter ranges (`LocalChapterStart`/`End`) to an external
provider's chapter numbering (`RemoteChapterStart`) for Manga/Light Novels. It is **not** an anime
episode-range mapping tool despite the adjacent name — anime range mapping now has its own owner-facing
workflow on `/Settings/MappingReview?animeId=` (see above); this page remains the reading-media
equivalent. **Exists** (reading media here, anime on Mapping Review).

## 4. Admin navigation

Target groups per #510 ("Dashboard, Library, Acquisition, Wanted/Missing, Queue, Downloads,
Metadata & Mapping, Subtitles, Media Processing, Playback & Sessions, Storage, Calendar/Releases,
Jobs/Activity, Integrations, Users & Permissions, Settings, Diagnostics") against the current
catalog in `src/Jularr.Web/Features/Localization/UiShellNavigation.cs`:

| Target group | Current route | State |
| --- | --- | --- |
| Dashboard | `/Admin` (admin-overview) | Exists, but not the "is anything broken / who's watching / what's transcoding" landing page #510 wants — see §6/§7. #518 |
| Library | `/Library`, `/Library/AnimeRepair/{id}` | Exists as consumer+admin hybrid (repair tools are owner-only but live under the consumer Library route) |
| Acquisition | `/Acquisition` (admin-anime-acquisition), `/Settings/Acquisition` (admin-import) | Exists |
| Wanted / Missing | inside `/Acquisition` | Exists (not a separate nav entry, but present as a section) |
| Queue / Downloads | inside `/Acquisition`, Operations `IsDownload` rows | Exists (no standalone "Downloads" nav entry; folded into Acquisition and Operations) |
| Metadata & Mapping | `/Settings/MappingReview`, `/Settings/MappingSegments` (admin-mapping) | Exists — configurable provider roles and the anime range-mapping apply/preview/audit workflow, see §3 |
| Subtitles | `/Admin/Subtitles`, `/Settings/Subtitles` (admin-subtitles) | Exists |
| Media Processing | no dedicated nav entry (optimizer/trickplay/segments run as background Operations, surfaced only in `/Admin/Operations`) | Partial |
| Playback & Sessions | **missing** — no admin sessions page exists (`Features/PlaybackSessions` has the hub/coordinator/store but no admin view) | Missing. #518 |
| Storage | `/Admin/System` (admin-system, roots/wake/health); the container-aware folder browser behind the path fields of `/Settings/Acquisition` (`Features/Storage/FolderBrowse`, #604) | Exists |
| Calendar / Releases | consumer `/Calendar` only; no admin releases nav entry | Partial |
| Jobs / Activity | `/Admin/Operations`, `/Admin/Scans`, `/Admin/Logs` (admin-operations, admin-scans, admin-logs) | Exists |
| Integrations | `/Admin/Usenet`, `/Admin/Sonarr` (admin-usenet, admin-sonarr) | Partial — Usenet/Sonarr only, no general integrations hub (Prowlarr health lives under Usenet) |
| Users & Permissions | `/Admin/Users`, `/Admin/Requests`, `/Admin/Capabilities` (admin-users, admin-requests) | Partial — user management, request policy and a per-media-type capability matrix (Hidden/Browse/Request/Instant, §8) exist; role assignment lives on the per-user page. `/Admin/Capabilities` is linked from `/Admin/Users`, not from the shell nav catalog. #521 #436 |
| Settings | `/Settings/*` section (Admin AI, API keys, Localization) | Exists |
| Diagnostics | `/Admin/Logs`, `/Admin/Ai`, `/Admin/Health` (admin-ai, admin-health) | Exists — operation logs plus dependency/service health and version/update diagnostics (§5, Sonarr/Radarr table). #528 |

## 5. Parity matrices

State legend: **exists** (built and reachable today), **partial** (built but incomplete against
the #510 description), **missing** (not built). Verified by grep against `src/Jularr.Web` — see
each row's Where.

### 5.1 Sonarr/Radarr

| Capability | State | Where | Issue |
| --- | --- | --- | --- |
| Root folders / storage state | Exists | `LibraryRoot`, `/Admin/System` | — |
| Monitored/unmonitored works | Exists | `AnimeMonitoringStore` (`/data/acquisition/monitoring.json`) | — |
| Monitored seasons/episodes (granular) | Partial — monitoring is per-anime, not per-season | `AnimeAcquisitionInventory` | #396 |
| Missing / cutoff unmet | Exists | Wanted-episode logic, quality-profile upgrade cutoff | — |
| Rescan/refresh | Exists | `LibraryScanCoordinator`, per-anime repair (`AnimeRepairService`) | — |
| Rename preview + execute | Exists | `/Library/Rename/{animeId}`, `AnimeRenameService` | — |
| Manual import | Exists | `/Acquisition` "Needs a decision", `AnimeImportExecutor` | — |
| Move/organize files | Exists | Naming profile + `ImportFileTransfer` | — |
| Quality/language inventory (dedicated report) | Partial — data exists per file (`MediaAnalysis`) but no cross-library inventory view | `MediaInventoryService` | #421 |
| Duplicate detection | Partial — media-core duplicate/merge review exists (`/Admin/MergeReview`: shared-title suggestions, manual merge/split/reassign, identity-change history); import-time existing-file detection still separate, and detection is title-based (no cross-provider evidence merge yet) | `WorkQueryService.FindDuplicateSuggestionsAsync`, `WorkService.MergeWorksAsync`, `Pages/Admin/MergeReview`, `AnimeImportPlanner` | #437 |
| Health/problems (unified) | Partial — per-root and per-indexer/client health exist separately, no single "problems" view | `/Admin/System`, `AcquisitionHealthStore` | #518 |
| Indexers/Prowlarr integration | Exists | `/Settings/Indexers` | — |
| Download clients/SABnzbd | Exists | `/Settings/DownloadClients` | — |
| Automatic + interactive search | Exists | `AnimeAcquisitionScheduler`, `/Acquisition?search=` | — |
| Release scoring | Exists | `ReleaseParser` + `ReleaseScorer` (media-type-agnostic; anime is one registration) | — |
| Quality profiles (assign) | Exists | `QualityProfileStore` (keyed by media type) | — |
| Quality profiles (edit UI) | Missing — documented limit, assignment only | ANIME_ACQUISITION.md "Limits" | #396 |
| Language profiles (acquisition scoring) | Missing — no separate language-weighted profile beyond naming tokens | — | #396 |
| Preferred/rejected terms, upgrade rules | Exists | Quality profile required/forbidden terms, upgrade cutoff | — |
| Queue / history / blocklist / retries | Exists | Operations (`IsDownload`), `AcquisitionHistoryEntry`, blocklist | — |
| Import decisions / rejected reasons | Exists | "Needs a decision", search operation log | — |
| Naming profiles, preview, collision checks | Exists (anime only) | `/Settings/Naming`, `AnimeNamingFormatter` | — |
| Naming for Movies/TV/Books/Manga/Novels | Exists — Books/Manga/Novels have per-kind naming profiles (#529); Movies/TV place files with built-in defaults (`Title (Year)`, `Series/Season NN/Series - S00E00`) on the shared `NamingTemplateEngine` (#593/#594); configurable Movie/TV profiles are a follow-up | `Features/Movies/MovieNaming`, `Features/Tv/TvNaming`, `Features/Naming` | #396 |
| Sidecar handling on rename | Exists | subtitle/NFO sidecars follow the plan | — |
| Media processing: ffprobe inventory, remux, verification, rollback | Exists | `MediaInventoryService`, `MediaContainerOptimizer`, `MediaRemuxVerifier` | — |
| Media processing: dedicated transcode/optimize job (beyond lossless remux) | Partial — only the lossless MP4 remux is a durable job; lossy transcode happens live during playback, not as a background optimization job | `MediaContainerOptimizer`, playback-plan `transcode` mode | #403 |
| **Movies and TV library** | **Partial — first-class `Movie` and `TvSeries` entities bridged to the universal media core, completed-download + inbox import adapters on the shared spine, and per-kind naming/library roots exist (#593/#594); the consumer library grid + discovery UI (#595/#395) and playback wiring (#403) are pending** | `Features/Movies/**`, `Features/Tv/**`, `AppDbContext` `Movies`/`TvSeries` `DbSet` | #593 #594 |
| Requests & approvals (Overseerr/Jellyseerr-style) | Partial — request lifecycle, the owner queue with auto-approval rules (`/Admin/Requests`), a per-user request history (`/Requests`), anime request options (whole series, seasons or episodes, audio/subtitle preference, requester-selectable quality profile at `/Requests/New`) and an availability badge (requested / in library / available) on Media Banner cards exist; request versus instant follows the per-media-type capability. Audio/subtitle preferences are shown to the approver but not yet enforced in release scoring; `/Requests/New` is not yet linked from the Discover cards | `AcquisitionRequestService`, `AutoApprovalEvaluator`, `RequestHistoryQuery`, `MediaAvailability`, `/Admin/Requests`, `/Requests` | #597 #436 |
| Notifications (events/destinations) | Missing | — | #429 |
| Clients & devices inventory (admin) | Partial — `/Admin/Devices` lists known clients/devices across every account (kind, label, app version, first/last seen, online state, live playback method) with a Revoke action that ends the device's live session and forgets it; `/Profile/Devices` lets a user self-manage their own devices the same way. No capability/app-version negotiation beyond what a client already reports, and Jularr's cookie auth has no per-device token, so revoke cannot block a future reconnect from the same browser/app | `Features/Devices/KnownDeviceRegistry`, `Pages/Admin/Devices`, `Pages/Profile/Devices` | #510 |
| Transcoder resources dashboard | Missing | — | #403 |
| Remote access & security overview | Partial — `/Admin/Devices` shows recent sign-in success/failure activity (user name, remote address, timestamp) from an in-memory ring, alongside the devices inventory above; no persisted audit log, IP geolocation, trusted-device flagging or access-policy controls yet | `Features/Devices/SecurityEventLog`, `Pages/Admin/Devices` | #510 |
| Migration/coexistence: Sonarr | Exists | SONARR_MIGRATION.md, `SonarrParallelSafety` | — |
| Migration/coexistence: Radarr/Bazarr/Readarr/Plex/Jellyfin | Missing | — | #433 |
| Backup/export/restore (full app state) | Partial — acquisition-store bundle only | `/Settings/Acquisition` export/restore | #416 |
| Retention & cleanup (recycle/trash, orphaned files) | Partial — scan prunes its own old runs/logs only, no library-wide cleanup preview | `LibraryScanCoordinator` retention | #414 |
| API/webhooks/automation | Partial — REST automation API exists for acquisition only, no generic webhooks | `Features/Acquisition/Api` | #438 (provider framework) / #429 (webhook destinations) |
| System health & updates | Exists | `/Admin/Health` | — |

### 5.2 Bazarr

| Capability | State | Where | Issue |
| --- | --- | --- | --- |
| Sidecar subtitle import (per-episode, per-folder) | Exists | `MediaSegmentSidecarImporter`-adjacent `SubtitleImportService`, sidecar formats | — |
| Embedded subtitle extraction | Exists | `EmbeddedSubtitleExtractor` | — |
| Generated transcription subtitle | Exists | Whisper-based transcription (`LearningTextPreparation.cs`) | — |
| Forced/SDH detection | Exists — owner-facing forced/SDH preference per wanted language, on top of the existing extraction-ordering detection | `SubtitleLanguageProfileItem` (`Forced`/`Sdh`), `EmbeddedSubtitleExtractor.cs` (`IsForced`), `Settings/Subtitles.cshtml(.cs)` | — |
| Missing-subtitle tracking | Exists — per-episode complete/cutoff-met/missing-N state against the resolved language profile, from embedded + external tracks | `SubtitleCompletenessService.cs`, `/Admin/Subtitles` completeness panel | — |
| Subtitle language profiles | Exists — owner-managed ordered wanted-language profiles with forced/SDH preference and a cutoff, assignable per media type and per library root (fallback media-type → global default) | `SubtitleLanguageProfile(Item)`, `SubtitleLanguageProfileService.cs`, `Settings/Subtitles.cshtml(.cs)` | — |
| External subtitle provider search/download | Partial — Jimaku still covers only the Japanese learning subtitle; a general `ISubtitleProvider` search/download abstraction and an owner-only manual-search UI now exist, but ship with zero registered providers (no credential-free provider could be added without an account/API key) | `SubtitleProviders.cs`, `SubtitleManualSearchService.cs`, `Pages/Admin/Subtitles.cshtml.cs` (manual search panel) | #560 |
| Subtitle sync/validation | Missing — no timing-sync or validation tool; not part of #526's scope | — | — |
| Replace/remove subtitle | Exists | Rename/repair "Refresh subtitles" path re-imports; per-track removal via subtitle sources | — |
| Per-media subtitle diagnostics | Exists | Episode subtitle sources partial (`_EpisodeSubtitleSources.cshtml`) | — |

### 5.3 Readarr (books/manga/novels)

| Capability | State | Where | Issue |
| --- | --- | --- | --- |
| Work/volume/chapter identity — Novels | Exists | `NovelWork`, `NovelVolume`, `NovelChapter`, `NovelTranslation` | — |
| Work/edition/file identity — Books | Exists | `BookEdition`, `BookFile` | — |
| Series/chapter identity — Manga | Partial — file-derived (`MangaSeriesItem`/`MangaChapterItem`), not a durable DB entity like anime/novels; blocks a Manga equivalent of `/Library/Rename` for *existing* files | `Features/Manga/MangaModels.cs`, `MangaRepository` | #563 |
| Acquisition (search/download/import) | Exists | `Features/ReadingAcquisition`, `AcquisitionRequestService` | — |
| Monitoring/wanted | Exists | `WantedAcquisitionService` | — |
| Metadata/provider mapping | Exists (AniList) | Manga/Novel AniList match services | — |
| Configurable Light Novel search sources | Exists — the owner enables and prioritises sources (Narou, AniList, BOOK☆WALKER, WebNovel, Internet Archive) at `/Admin/ReadingSources`; each has a capability (public full text, published edition, preview, external reference), a licensing note and in-memory health. Discovery-only sources list results and link out, and cannot be added; Internet Archive lists only open or lendable items. See [READING_ACQUISITION.md](READING_ACQUISITION.md#reading-sources). **Follow-up:** the settings page is only linked from the Light Novel Add dialog, not from the Admin navigation | `Features/ReadingSources`, `Features/ReadingDiscovery`, `Pages/Admin/ReadingSources.cshtml` | #477 |
| Naming/organization | Exists — one naming-template profile per reading media type (Books, Manga, Light Novels), applied when a release is placed into its NAS library root; live preview and token reference on `/Settings/ReadingNaming` (sibling of anime's `/Settings/Naming`) | `Features/Naming`, `Pages/Settings/ReadingNaming.cshtml(.cs)` | — |
| Reading progress | Exists | `NovelProgress`, `MangaProgressItem`, bookmarks/highlights | — |
| Multiple editions/formats | Exists (Books) | `BookEdition`/`BookFile` (EPUB, PDF) | — |
| Chapter-range provider mapping | Exists | `ReadingSegmentMappingStore`, `/Settings/MappingSegments` | — |
| Cross-media anime↔novel mapping | Exists | `NovelAnimeMapping` | — |

Radarr's Movies/TV capabilities: Jularr now models Movies and TV as first-class media types —
`Movie` and `TvSeries` entities bridged to the universal media core (`WorkSourceKind.Movie`/`.Series`,
TV reusing `WorkSeason`/`WorkEpisode`), completed-download + inbox import adapters on the shared
`ICompletedDownloadImportAdapter`, and per-kind naming/library roots (#593/#594). The remaining
Radarr-parity rows are the consumer library grid + discovery (#595/#395), playback polish (#403) and
richer metadata providers (#438); #396 delivered the shared, media-type-agnostic acquisition engine
they build on.

## 6. Pipeline observability

Target timeline (#510): release found → scored → accepted/rejected → sent to downloader →
download started/completed → import matched → target path calculated → file moved → ffprobe →
subtitle discovery → metadata mapping → artwork refresh → optional remux/optimization → library
reconciliation → ready.

What `Operations`/`OperationLogs` (see [ADMIN_OPERATIONS.md](ADMIN_OPERATIONS.md)) record today,
by operation kind:

| Pipeline step | Recorded today | Operation kind / lane |
| --- | --- | --- |
| Release found / scored / accepted-rejected | Yes, in the search operation's log (module `Acquisition`) | `anime-search` |
| Sent to downloader / download started-completed | Yes | `anime-sabnzbd-download` (Anime), `sabnzbd-download` (Books) |
| Import matched / target path / file moved | Yes | `anime-import` (module `Import`) |
| ffprobe | Yes, as part of library reconciliation, not its own operation | folded into `library-scan` / `anime-repair-reanalyze-media` |
| Subtitle discovery | Yes, folded into scan | `library-scan` |
| Metadata mapping | Yes | `anime-metadata-match`, `anime-metadata-refresh` |
| Artwork refresh | Yes, folded into scan's Artwork phase | `library-scan` |
| Remux/optimization | Yes | `media-optimization` |
| Library reconciliation / ready | Yes | `library-scan` |

Every step above already has a durable operation and log; what is **not** built is a single
cross-operation timeline view that stitches one release's full journey (search → download →
import → optimize) into one visual sequence — today an admin must find the related operations
separately (by anime, by time). That combined view is part of the admin dashboard scope (#518).

## 7. Sessions

**Admin view (missing today):** #510 wants a Plex-style live sessions view — user, media/episode,
client/device, IP, start time, position, bitrate/resolution, tracks, playback method (Direct
Play/Direct Stream/Transcode), transcode reason/speed, bandwidth, errors/rebuffers, and an
authorized stop-session action. `Features/PlaybackSessions` (`PlaybackSessionStore`,
`PlaybackSessionCoordinator`, `PlaybackSessionHub`) already models session state for the TV↔phone
companion feature (`sessionId`, position, tracks, revision — see
[ANDROID_CLIENTS.md](ANDROID_CLIENTS.md) §10.1), but there is no admin page rendering it and no
transcode-diagnostics fields on the model. #518 builds this.

**User view (missing today):** a restricted Plex-like self-service view of the user's own sessions/
devices, playback history, progress, downloads/offline devices, with the ability to end their own
sessions — no other user's or admin's data. `EpisodePlaybackHistoryEntry` and
`ProfilePlaybackPreferences` already hold the durable history/preference data this page would read;
no page exposes it as a session/account view yet. #518 builds this.

## 8. Permissions matrix

Today only two roles exist (`Features/Auth/OwnerAccount.cs`): `AccountRole.Owner` and
`AccountRole.User`. There is no `Media manager` role. #521 implements the middle tier and the
policy checks below; the table states the target state, not today's binary Owner/User split.

| Area / action | Owner | Media manager | User |
| --- | --- | --- | --- |
| Consumer pages (Home, Library, Watchlist, Calendar, Discover, playback, reading) | Full | Full | Full |
| Own account settings, own sessions/history | Full | Full | Full |
| Request media (Requests queue, own history at `/Requests`) | Full | Full | Allowed per the media-type capability (Request creates a request, Instant adds at once); auto-approval rules can approve a request without the owner |
| Per-media-type capability: Hidden/Browse/Request/Instant (`/Admin/Capabilities`, #436) | Unrestricted (always Instant) | Configurable (default Instant) | Configurable (default Request), with per-user overrides |
| Approve/reject requests | Full | Full | No |
| View admin dashboard, Operations, Scans, Logs | Full | Full (read) | No |
| Delete media / library files | Full | No (per #510's sensitive-action list) | No |
| Rename/move files | Full | No | No |
| Mapping changes, merge/duplicate review (MappingReview/MappingSegments, MergeReview) | Full | Full | No |
| Acquisition settings (indexers, download clients, quality profiles) | Full | Full | No |
| User management (create/disable accounts, roles) | Full | No | No |
| Stop another user's playback session | Full | Full | No |
| Storage/root/integration settings | Full | No | No |
| API keys / automation | Full | No | No |

`Media manager` is intended to run the day-to-day media-operations workflows (acquisition,
mapping, request approval, session moderation) without the account-management and system-settings
authority reserved for Owner. Sensitive-action gating listed above follows #510's own list
verbatim (delete, rename/move, mapping changes, acquisition settings, user management, stopping
another user's session, storage/settings/integration changes).

### 8.1 Per-media-type capability matrix (#436)

Orthogonal to the role/policy table above, each media type (`WorkMediaType`: Movie, Series/TV,
Anime, Book, Manga, Light Novel) resolves to one ordered capability per profile:
`Hidden < Browse < Request < Instant`. The owner edits the matrix on `/Admin/Capabilities`
(owner-only, `admin.system`): a default per configurable role (Media manager, User) plus sparse
per-user overrides. The policy is the canonical JSON settings store
`Features/Auth/MediaCapabilityStore` (`/data/auth/media-capabilities.json`) — no EF table.
Resolution precedence: **Owner is always Instant (unrestricted); otherwise a per-user override
wins over the role default.** Features consume the resolved capability through
`IMediaCapabilityService` (`Features/Auth/MediaCapabilityService`) instead of re-deriving rules:
`GetViewAsync`/`GetEffectiveCapabilityAsync` (request experience #597 — Instant vs Request gating),
`GetVisibleMediaTypesAsync` (permission-derived shell #598 and discovery categories #595 — Hidden
removes a media type entirely), and `EnsureCapabilityAsync` (server-side per-media-type guard).

`AcquisitionRequestService` (#597) is the request-side consumer: Request creates a request, Instant
adds at once, Browse/Hidden cannot add. This replaces the former per-media-type "adding from
search" rule (`UserAddMode`), which #597 retired in favour of the capability matrix; its
`AcquisitionAccessPolicies.UserAddMode` table column was dropped by the Movie/TV schema migration
(#593/#594). On top of a Request capability the
owner's auto-approval rules (`Features/Acquisition/Access/AcquisitionRequestSettingsStore`,
`/data/acquisition/request-settings.json`, edited on `/Admin/Requests`) can approve a request without
the queue: a rule matches on media type and requester and may carry a per-requester quota within a
number of days; the first matching rule with quota left approves, otherwise the owner decides.

### 8.2 Permission-derived shell (#598)

Navigation is derived from what a profile may browse, per media type, on top of the policy checks
that already gate Admin and Settings. A **Hidden** media type is *absent*, never greyed out: a
Books-only profile sees a pure book app.

- **One resolution per request.** `IAppShellService` (`Features/Shell/AppShellService.cs`, scoped)
  turns `IMediaCapabilityService.GetViewAsync` into a `ShellMediaAccess`: `VisibleMediaTypes`,
  `Capability(type)`, `IsVisible(type)`, `IsAnyVisible(types)` and `CanOpen(routeRoot)`. The sidebar,
  the Library tabs, Profile and the route gate share that one answer. **Discovery (#595) and the
  request experience (#597) should consume `IAppShellService.GetMediaAccessAsync(User)`** instead of
  re-reading the policy: `VisibleMediaTypes` is "which media types exist for this user".
- **One route table.** `UiNavigationCatalog.LibraryTabs` (`Features/Localization/UiShellNavigation.cs`)
  ties each consumer route root to the media types it serves (`UiMediaRoute`): `/Library` → Anime,
  `/Novels` → Light Novel, `/Manga` → Manga, `/Books` → Book, and the `/Reading` hub → Manga or Light
  Novel. The `library` sidebar destination is the hub of those tabs: shown while any tab is
  reachable, opening the first reachable one. The Library tab strip lists only reachable tabs and is
  hidden when fewer than two remain. Movies and series have no consumer pages yet; a tab for them is
  one more catalog entry and the sidebar, tabs and gate follow.
- **Route gate.** `Program.cs` registers `Conventions.AddMediaTypeGates()` (`Features/Shell/MediaTypeRouteGate.cs`),
  which attaches an authorization filter to every page folder in that table. A profile that cannot
  at least browse the type gets **404** (the type does not exist for them) before the page model is
  constructed; the owner is unrestricted through the capability policy. The gate is not a second
  policy: it reads the same `MediaCapabilityView`.
- **Not yet media-scoped (follow-ups).** Home type chips and Continue rows, `/Discover` categories
  (#595), Watchlist/Calendar/Franchise content, and the ClientApi surface (`/api/client/v1/...`) still
  list every media type the data contains; they should narrow by `ShellMediaAccess`.
