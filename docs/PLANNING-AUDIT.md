# Jularr planning audit

Status: planning baseline. No feature implementation is authorized by this document.

## 1. Authority order

1. `DOMAIN.md` defines canonical domain meaning.
2. `DOMAIN-AUDIT.md` defines legacy transition/classification.
3. `ARCHITECTURE.md` defines module ownership/contracts.
4. `UX.md` and `INFORMATION_ARCHITECTURE.md` define global navigation/product behavior.
5. Screen `SPEC.md`/README files define screen behavior; their text wins over visual references on conflict.

## 2. Current approved/planned screen coverage

Existing screen contracts: Home, Library/Collections, Discover/Search, Anime/Series Detail, Movie Detail, Reading Detail (Book/Light Novel/Manga), Audiobook Detail, Calendar, Profile/Activity, User Settings, Add/Request Flow, Admin Dashboard, Admin Media Detail, Admin Wanted, Admin Requests, Admin Activity and Admin History.

Library currently uses `README.md` as its binding draft instead of `SPEC.md`; this should be normalized after approval without changing its current decisions.

## 3. Missing user screens/flows

Required before broad feature implementation:

- Player
- Reader
- Learning Home
- Lesson / Review

Secondary user surfaces that should normally be dialogs/sheets or states rather than independent destinations: media preview, collection detail/edit, person/creator view, request status/details, active-session mini player/reader, permission/forbidden state, login/profile selection.

## 4. Missing Admin screens/flows

Required:

- Manual Search
- Imports
- Storage + safe Path Browser
- Provider Settings
- AI Admin
- Users & Permissions
- Backup / Restore
- Migration Center
- Setup Wizard
- Acquisition Settings (profiles/scoring, indexers, download clients)
- System / Diagnostics (logs, health detail, general operational settings)

Already covered by existing specs and therefore not duplicated: Dashboard, Media Detail, Wanted, Requests, Activity/Jobs, History.

## 5. Domain/architecture consistency findings

All new screens must use `Work -> Structure -> Edition -> Version -> Asset/File -> Track`. No screen may persist Anime/Movie/Novel/Audiobook-specific identity as a competing source of truth.

User progress must target canonical Work/structural units through unified progress semantics. Player and Reader share canonical identity/session/progress contracts but retain platform-specific presentation.

Discovery/provider results are candidates until resolved. AniList season/part records remain provider presentation/mapping targets over canonical Anime Work/Season/Episode identity.

Admin acquisition screens operate on `WantedItem -> ReleaseCandidate -> DownloadJob -> ImportJob -> Version/Asset/File/Track`. Manual Search is not a second acquisition path.

Storage screens operate on `LibraryRoot` and physical files only. They must not move logical media identity into paths.

## 6. Open-issue audit

### Aligns with target architecture

- #403 playback: aligns with canonical PlaybackPlan/ActiveSession; implementation must wait for Player spec and canonical File/Track/progress contracts.
- #662 progress: aligns strongly; exact resume and completed-through must be separate states inside the canonical progress contract, not new per-media stores.
- #389 acquisition/import: direction is valid but proposed generic `MediaType` routing must resolve to canonical Work/unit/Edition targets and the shared ImportJob pipeline.
- #396 Wanted: valid goal; legacy examples naming independent `Movie`, `Anime`, `Book` entities are conceptual only and must not become parallel models.
- #440 Audiobooks: product requirements remain useful, but the suggested `Audiobook Release` hierarchy is legacy terminology. Target is Work/Edition/Version/Asset/File/Track with audiobook-specific edition metadata.
- #441 Learning v3: bounded domain remains valid; media links must reference canonical Work/Episode/Chapter only.
- #638 AI: valid bounded capability/policy work; persistent outputs need canonical source identity and provenance.
- #433 Migration Center: valid and required before destructive legacy removal.
- #416 Backup/Restore: valid but depends on stable canonical schema/config ownership.

### Legacy/conflicting assumptions to correct before implementation

- #395 is superseded in several UX details: no sidebar media-type duplication, no stats side panel, no tab-heavy detail baseline. Current Library/detail specs win.
- #396 sections that imply separate first-class persistence roots such as `Movie`, `Anime`, `LightNovelSeries` must be interpreted as canonical Work + Structure/Edition specializations.
- #440 must not create a parallel Audiobook/AudiobookFile core or audiobook-specific acquisition engine.
- #69 predates current Jularr naming/UX and contains old AniLingo terminology. Its client architecture can be reused only where consistent with current Player/TV specs and canonical contracts.
- #413 overlaps current Admin Activity spec and should not create a second job store/UI model.
- #421 must use canonical Version/Asset/File and acquisition/import history rather than add another version model.
- #434/#595 are consistent after the 2026-09-30 canonical identity decisions; provider-native Anime view remains presentation only.

### Dependencies that must be explicit

- Player: MediaCore + Library File/Track + Progress + Playback + Devices/capabilities.
- Reader: MediaCore + Library Asset/File + Progress + Reader + Translation; Learning optional.
- Wanted/Manual Search/Imports: MediaCore + Acquisition + Storage + Jobs + provider health.
- Backup/Restore: stable PostgreSQL schema + configuration/secrets policy + migration versioning.
- Migration: canonical schema + backfill validators + progress/file/provider-identity preservation.
- Users/Permissions: explicit Profile/account/capability model before permission-derived navigation.

## 7. Planning gate

Do not begin broad feature implementation until the missing major screen specs exist, the implementation roadmap is dependency ordered, and unresolved canonical Profile/Asset/File/Track/progress contracts are settled. Bug/security fixes and migration/audit tests remain outside this gate.
