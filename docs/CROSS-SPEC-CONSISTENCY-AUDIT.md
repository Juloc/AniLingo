# Cross-Spec Consistency Audit

Date: 2026-10-03  
Branch baseline: `dev`  
Status: **core consistency pass complete; one explicit mobile-navigation product decision remains open**.

This audit compares the current canonical domain/architecture/global UX contracts with the binding screen specs. It is a documentation consistency audit, not an implementation audit.

## 1. Authority used

1. `DOMAIN.md`
2. `DOMAIN-AUDIT.md`
3. `ARCHITECTURE.md`
4. `UX.md` + `INFORMATION_ARCHITECTURE.md`
5. binding screen `SPEC.md`
6. approved mockups, with text authoritative on conflict

## 2. Request / acquisition — aligned

Canonical consumer rule:

- exactly one acquisition action: `Request`;
- no consumer Add / Add & Monitor / Instant button;
- approval/auto-approval is backend policy;
- Request begins from an already resolved canonical target;
- personal Watchlist/Reading List/Favorite/Collection state stays separate;
- Manual Search/import/release selection stays Admin.

The global UX had an old multi-step Add flow and exposed Searching/Importing as normal user states. That has been removed.

Consumer state vocabulary is now normalized to:

- Waiting for approval;
- Requested / Approved where useful;
- Looking for media;
- Downloading;
- Preparing;
- Available / Partially available;
- Monitoring future releases;
- Needs attention / Failed;
- Cancelled where relevant.

`Wanted`, search jobs, ImportJob/importer phases, release candidates and downloader internals remain Admin/architecture terminology.

## 3. Request capability / Instant — aligned

The existing authorization capability name `Instant` may remain internally for compatibility.

Binding meaning:

- `Request` capability -> consumer presses Request; approval policy applies;
- `Instant` capability -> consumer still presses Request; the request may proceed immediately/auto-approve;
- Browse/Hidden cannot create acquisition requests.

No UI exposes a second Instant/Add action.

## 4. Canonical media identity — aligned

Watch/read/listen media remain:

`Work -> Structure -> Edition -> Version -> Asset/File -> Track`

Provider-native records never become canonical media identity.

Anime/Series provider season/part splits remain mappings/presentation targets over canonical Work/Season/Episode identity.

## 5. Games identity boundary — aligned

Games is deliberately outside MediaCore.

Shared Search/Discover/Home/Request uses a typed canonical target:

- normal media -> `Work`;
- Games -> Games-owned `Game`.

The shared UI/pipeline does not force Game/GameRelease into Work/Edition merely to reuse Search or Request.

Collections remain Work-based in V1. Games is therefore not silently inserted into `CollectionEntry(WorkId)`; adding cross-domain Collections later requires an explicit typed target contract.

## 6. Discovery / provider persistence — aligned

Discovery/provider responses are evidence, not canonical identity.

Durable provider entity/list data is persisted locally as provider evidence/snapshots where defined by the domain.

Linked Collections follow:

`provider list -> local snapshots -> canonical Work resolution -> local Collection membership -> local rendering`

Normal Linked Collection rendering and Smart Collection evaluation do not require live provider calls.

## 7. Library / Discover / Home boundaries — aligned

### Discover
Find new titles, including non-local titles.

### Library
Browse durable Library/monitoring context. No generic consumer Add/import menu and no Admin dashboard clutter.

### Home
Personal continuation/recommendation surface. It is not another Library index.

### Watchlist / Reading List
No extra top-level app is required. Personal list state is projected through Library filtering/deep links and may appear as a Home shelf.

This avoids a missing Watchlist screen while preserving canonical `WatchlistEntry` state.

## 8. Collections — aligned

Persisted modes:

- Manual;
- Smart;
- Linked.

Cross-media is a capability, not a Collection type.

Franchise/adaptation is not a parallel Collection persistence model.

Manual membership removal and Collection deletion never delete canonical media, progress, ratings or Requests.

## 9. Edition / Track semantics — aligned

Reading and Audiobooks can expose meaningful Editions.

For normal video:

- audio dub language -> audio Track;
- subtitle language -> subtitle Track;
- technical file/quality alternatives do not automatically become Editions;
- a video Edition is only for a genuinely distinct presentation/cut/publication.

Movie Detail and the shared Language/Edition selector now state the same rule.

## 10. Progress / sessions / continuation — aligned

One canonical `MediaProgress` owns consumption progress.

Exact resume and completed-through semantics remain distinct.

Playback uses `PlaybackPlan` + `ActiveSession`.

Compact continuation surfaces project existing state:

- Now Playing -> active playback/TTS;
- Continue Reading -> Reader progress.

They do not create new progress/session stores.

## 11. Profile / Settings ownership — aligned

Profile Activity contains personal media activity only.

Devices & Sessions is owned by:

`Profile -> Settings -> Devices & Sessions`

The Profile spec no longer defines a second embedded device/session manager.

Deleting Activity does not reset Progress.

Ratings use the universal normalized `UserRating` model.

Friends remains hidden until a real Friends/social capability/domain contract exists; no dead tab or UI-only social state is allowed.

## 12. Account / Profile / Login / Connections — aligned

- Account = authentication/security/roles/account sessions;
- Profile = personal media state/preferences;
- Login identity = authentication adapter identity;
- Connection = Profile-scoped sync/import/write-back link.

Signing in through Plex/Jellyfin/Trakt/AniList/MAL/etc. never silently enables sync unless the provider actually supports/configures that separate Connection capability.

## 13. Learning gating — aligned

Resolution remains:

`instance availability -> authorization/capability -> profile module preference -> detailed Learning settings`

Player/Reader Learning modes are optional presentation layers and do not create alternate playback/reading identities.

Turning Learning off preserves stored Learning state.

## 14. Instance modules / Setup Wizard — aligned

Admin -> Instance exposes only canonical modules whose complete runtime gate exists.

Setup Wizard no longer implies that conceptual future items such as Games, AI, Requests, Native Downloader or Generic Downloads automatically have valid independent module switches.

Games setup sections can exist only when Games is actually available in the setup context; the wizard must not fabricate an unenforceable Instance switch.

## 15. Visual skins — aligned

Clean and Original Jularr share layout, behavior and information architecture.

- Clean: neutral/minimal, purple default accent, no anime/sakura/ink decoration.
- Original Jularr: Japanese ink/watercolor/sakura treatment, red/pink default accent.
- Hue shifting uses semantic tokens.
- legacy headings containing `Clean Design` mean visual baseline only, not Clean-only product behavior.

Error/permission states now mark both Original J and Clean directions as approved.

## 16. Consumer/Admin boundary — aligned

Consumer pages never expose:
- provider IDs;
- release scoring;
- indexer results;
- downloader internals;
- raw import paths;
- job/log internals.

Admin Manual Search/Imports/Wanted/Activity/Media Detail own those concerns.

## 17. Calendar gate — intentionally open, not inconsistent

Calendar retains its explicit final owner-review gate for:
- final filter/status semantics;
- in-grid status treatment;
- event interaction;
- responsive behavior;
- Light/Dark visual QA.

Candidate consumer acquisition labels have been normalized, but that gate remains mandatory.

## 18. Open product decision — Mobile primary navigation

This is the remaining direct cross-spec conflict.

`UX.md` and `INFORMATION_ARCHITECTURE.md` currently define Mobile bottom navigation as:

`Home · Library · Calendar · Learning · Profile`

`home/SPEC.md` defines:

`Home · Library · Games · Calendar · Profile`

`games/SPEC.md` also states that Games is a dedicated top-level consumer destination.

The documents do not yet define a concrete mobile overflow/module-slot behavior that makes both variants simultaneously true.

Do not implement Mobile primary navigation until one rule is locked.

Reasonable choices:

1. Games owns the permanent optional-module bottom slot; Learning moves to an explicit mobile overflow/module entry.
2. Learning remains permanent; Games is a dedicated route reached outside the permanent bottom bar.
3. Define one capability-aware/pinnable optional module slot. This is more flexible but adds product/settings complexity.

Desktop is already consistent. TV is aligned so Games appears when the Games destination is actually available.

## 19. Non-blocking planning notes

- `games/SPEC.md` remains a shared planning scaffold while child Games pages are approved individually; this is intentional.
- several specs still have visual-review/status metadata that may be tightened later, but this does not create domain/behavior conflicts.
- historical/current-implementation tables in `INFORMATION_ARCHITECTURE.md` may describe legacy entities/routes. They are implementation inventory only and do not override target contracts.

## 20. Result

Core Media, Request, Library, Collections, Discover, Detail, Player, Reader, Profile, Settings, Learning, provider persistence and Admin/consumer ownership are now mutually consistent at planning-contract level.

No new generic core consumer screen is required by this audit.

The only direct product-level contradiction left is Mobile placement of Games vs Learning.
