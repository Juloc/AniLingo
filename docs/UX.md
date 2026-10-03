# Jularr UX specification

Status: planning baseline. This document defines information architecture, navigation, platform behavior and required screens before major feature implementation resumes. Visual details are finalized through approved mockups.

## 1. UX principles

- Clean, compact Fluent 2 / Windows 11-inspired Jularr design language.
- No unnecessary explanatory text, duplicated headings or nested pages when a direct interaction works.
- Media is the visual focus; administration is information-dense but structured.
- User UI and Admin UI are distinct modes.
- Responsive behavior is intentional for Desktop, Tablet, Mobile and TV.
- Light and dark themes are first-class.
- User-selectable accent/theme colors use shared design tokens; derived colors must remain accessible.
- Every screen defines loading, empty, partial, error and ready states.
- Permission-restricted actions disappear from UI but are also enforced server-side.
- Never expose half-implemented controls.

## 2. Global user navigation

### Desktop/tablet wide
Persistent left navigation:
- Home
- Library
- Calendar
- Learning
- Search/Discover is globally available at the top rather than a redundant permanent destination where possible.

Bottom/profile area:
- Profile
- Settings
- Admin, only when authorized

Library contains media rather than giving every media type a permanent top-level sidebar entry.

### Mobile
Bottom navigation optimized for frequent use:
- Home
- Library
- Calendar
- Learning
- Profile

Search is globally accessible from the top/app bar. Activity belongs under Profile rather than occupying bottom navigation.

### TV
Remote-first primary destinations:
- Home
- Library
- Calendar where useful
- Search
- Profile

Learning appears when the TV interaction is meaningful; detailed learning workflows may hand off to phone/tablet.

## 3. Search and Discover

Search and Discover are one coherent surface.

Interaction:
- tapping/clicking global search opens search UI immediately;
- empty query shows Discover content;
- typed query shows live/local/provider results;
- submitting an empty query remains Discover;
- filters can transition into a full Discover result page.

Discover rows resemble modern streaming discovery:
- For You
- Trending
- Anime
- Series
- Movies
- Books / Light Novels
- Manga
- genres/themes such as Horror

Rows scroll horizontally. Selecting a row title opens the corresponding filtered Discover view.

Discovery includes items not yet in the local library. Detail pages clearly distinguish available, requested/downloading and discover-only states.

## 4. Home

Home is personalized, not a duplicate library index.

Priority sections:
- Continue Watching / Reading / Listening
- Up Next
- Recommendations based on the user's library/progress
- Recently relevant additions only when useful
- media-type/genre recommendation rows

Avoid a generic `Recently Added` section dominating Home.

Cards show only useful glanceable information: artwork, title, progress and a small amount of media-specific status. Avoid tag clutter.

## 5. Library

One Library supports all media types.

Desktop/tablet:
- title + compact controls
- type switch/filter: All, Anime, Series, Movies, Manga, Books/Light Novels, Audiobooks
- optional user preference to combine or separate related types
- filters/sort/view controls
- responsive grid/list presentation

Mobile:
- compact filter chips/dropdown
- poster grid optimized for thumb use
- no desktop table squeezed onto phone

Library cards use one shared visual grammar with media-specific secondary information.

## 6. Canonical media detail page

All media detail pages share a common skeleton while adapting content.

### Header/hero
- artwork/backdrop
- poster/cover
- title and useful alternate title where configured
- concise metadata
- progress/status
- primary action: Play / Continue / Read / Listen / Request
- secondary actions: add/remove library/watchlist, options

### Content
Media-specific sections appear only when relevant.

Anime/Series:
- season selector
- episode list/cards
- availability/language state
- progress

Movie:
- single primary playback action
- available versions/languages

Book / Light Novel / Manga:
- one shared Reading Detail page family
- Parts/Volumes/Chapter structure where meaningful
- context-sensitive labels: Book may use Parts, Light Novel/Manga usually Volumes
- edition/language selector
- Read/Continue
- translation state where applicable
- format-specific behavior moves to the Reader rather than creating separate detail-page designs

Audiobook:
- chapters/tracks
- Listen/Continue
- edition/language/narration details

### Related
- related/adapted works
- recommendations

User pages do not expose release-group/import internals by default.

## 7. Episode / chapter / volume interaction

Do not force unnecessary intermediate pages.

Episode selection can open a compact detail surface or start playback depending on explicit user action. Chapter selection opens Reader. Admin-only diagnostics are separate.

Progress, availability and language are visible without noisy badges everywhere.

## 8. Video player

The detailed platform contract belongs to issue #403; UX baseline:

### Mobile
- single tap toggles controls only
- large central Play/Pause
- double tap left/right: -10/+30 seconds
- left vertical gesture: brightness where supported
- right vertical gesture: volume where supported
- pinch + Fit/Fill/Zoom
- real landscape fullscreen where platform permits
- screen/control lock
- learning subtitles independent from transient controls

### Desktop
- single click video = Play/Pause
- large central Play/Pause safe hit target
- double click = fullscreen
- visible volume slider
- keyboard controls
- timeline hover preview and chapters

### Tablet
Touch model from Mobile but layout uses extra space. Learning details may use a side sheet in landscape.

### TV
Remote-first focus model, large controls, direct media keys, seek via directional input, TV-safe layout.

### iOS/iPadOS
Explicit Safari/PWA compatibility path; preserve custom inline Jularr UI where WebKit permits and degrade deliberately when system surfaces are required.

Shared menus:
- quality
- speed
- audio
- subtitles
- Fit/Fill/Zoom

Technical Direct Play/Remux/Transcode diagnostics stay outside normal user controls unless requested.

## 9. Learning subtitles

Player controls, normal subtitles and interactive learning subtitles are separate layers.

Learning subtitles:
- remain visible while player controls hide;
- never accidentally trigger generic player tap gestures;
- allow word/sentence interaction;
- preserve playback position and state when entering/exiting learning details;
- adapt layout to Mobile/Tablet/Desktop/TV.

## 10. Reader

Reader prioritizes content and removes application chrome while reading.

Core:
- typography controls
- theme/background
- font size/spacing
- chapter navigation
- progress
- table of contents
- language/edition switch
- translation availability/action
- annotations/highlights/bookmarks
- optional learning interaction

Mobile controls appear on tap and otherwise disappear. Desktop can expose compact side controls without shrinking the reading column unnecessarily.

## 11. Calendar

Unified calendar for:
- upcoming anime/TV episodes
- expected/release dates
- relevant book/manga volume/chapter releases when known

Views:
- responsive month/week/list depending on device
- filters by media type/library status
- selecting item opens canonical detail

Mobile defaults toward a useful agenda/list presentation when month grid becomes cramped.

## 12. Learning

User Learning home:
- Continue Learning
- due reviews
- courses/languages
- media-derived learning
- progress

Course detail:
- units/lessons
- progress
- resume action

Lesson/review surfaces are distraction-minimized and touch/keyboard friendly.

Personal AI/provider configuration belongs in Settings, not inside every learning screen.

## 13. Profile

Contains:
- user identity/profile switch where applicable
- Activity/history
- watch/read/listen history
- devices/sessions where appropriate
- quick link to Settings

Activity is not a main mobile navigation item.

## 14. User Settings

Grouped, searchable settings rather than one long form.

Sections:
- Appearance
- Language & regional
- Library/display preferences
- Playback
- Audio & subtitles
- Reader
- Learning
- AI / personal provider
- Devices
- Account/security

Appearance supports Light/Dark/System and configurable accent/color scheme through shared tokens.

## 15. Add media flow

Binding specification: `docs/mockups/add-request-flow/SPEC.md`

`Add` / `Request` is contextual from Library/Search/Discover/Calendar/detail surfaces and opens one coherent flow rather than permanent import forms on normal pages.

Flow:
1. search/identify work
2. choose desired edition/language where relevant
3. choose monitoring/request behavior
4. optional acquisition profile override
5. confirm

Manual local import is an Admin/advanced path, not dominant user UI.

## 16. Request / missing media UX

User detail pages show a simple state:
- Available
- Request
- Requested
- Searching
- Downloading
- Importing
- Failed with understandable retry/details where permitted

Normal users do not need Sonarr-like release tables.

## 17. Admin mode/navigation

Entering Admin expands/replaces navigation with explicit admin destinations while preserving a clear way back to normal Jularr.

Admin navigation groups:

### Overview
- Dashboard
- Activity / To-Do
- History

### Media
- Library
- Wanted
- Requests
- Manual Search

### Acquisition
- Indexers / release search providers
- Native Usenet
- External download clients
- Profiles / scoring

### Metadata & Providers
- Metadata providers
- Subtitle providers
- Translation providers
- AI providers

### System
- Storage
- Users & permissions
- Devices
- Tasks/jobs
- Migration
- Backup/restore
- Logs/diagnostics
- General settings

Only show destinations actually implemented and permitted.

### Admin UI density

Binding specification:
- `docs/mockups/admin-instance/SPEC.md`

The entire Admin UI has one profile-scoped presentation preference:
- **Detailliert**
- **Kompakt**

This is a shared Admin-shell preference, not a per-page setting and not an instance-wide module setting.

Compact mode is table/list-oriented and reduces repeated descriptions, card padding, row height and form spacing where safe. Detailed mode exposes more inline explanation and context. Both modes use the same data, permissions, validation and actions; business behavior may never depend on the selected density.

The preference applies across Admin Dashboard, Instance, Storage, Downloader, Providers, AI, Users/Permissions, Activity/History, Wanted/Requests, Backup/Restore, Migration, Diagnostics and future Admin screens using the shared Admin shell.

Compact mode must not hide errors, warnings, destructive-action context or required information. On touch/mobile layouts, minimum touch-target sizes remain intact even when Compact is selected.

## 18. Admin dashboard

The Admin Dashboard is the live operational/health surface for Jularr. It is not a media-library statistics page.

Binding screen specification:
- `docs/mockups/admin-dashboard/SPEC.md`

Approved current visual reference:
- `docs/mockups/admin-dashboard-clean-live.png`

The detailed screen spec defines live service health, CPU/RAM/GPU/network/load, active streams with Direct Play/Remux/Transcode diagnostics and controls, downloads, Jularr tasks, storage, warnings and live activity. Global UX rules in this document still apply.

## 19. Admin media detail

Admin Media Detail uses a direct hierarchy-first V1 instead of distributing the same information across many technical tabs.

Binding screen specification:
- `docs/mockups/admin-media-detail/SPEC.md`

V1 centers on monitoring, automatic/manual acquisition, expandable Anime/Series seasons and episodes, and real per-file details. Anime may switch between Standard and AniList display groupings, but provider groupings are views/mappings over the canonical stored episode structure and never create parallel persisted episode models.

## 20. Wanted / Missing

Binding screen specification:
- `docs/mockups/admin-wanted/SPEC.md`

Wanted is the technical acquisition worklist for requested/approved, missing, searching and failed acquisition needs across all media types. User request moderation is separate in Admin Requests.

Selecting a Wanted target opens the reusable Acquisition dialog with exactly three primary tabs:
- Search
- Current
- History

Search is the default/main tab. Current explains the active profile/language/desired version and existing local state. History shows target-scoped search, grab, import and failure events.

## 21. Manual Search

Binding screen specification:
- `docs/mockups/admin-manual-search/SPEC.md`

Manual Search is the Search tab of the reusable Admin Acquisition dialog rather than an independent acquisition workflow.

It is Sonarr/Radarr-like in diagnostic depth but uses Jularr's visual language and canonical identity model.

Normalized candidates may expose:
- title
- source/indexer
- age
- size
- quality/format
- languages
- audio/subtitles
- release group
- release type such as single/multi/season pack
- parsed target and match confidence
- effective-profile score
- rejection/warning reasons

The primary score is contextual to the selected acquisition profile + language target. Optional comparison columns may show scores for other profiles.

Rejected and suspicious candidates remain visible by default so the admin can understand why automatic acquisition did not choose them. This includes likely wrong-episode/unit matches. Identity mismatches are clearly marked and can never be automatically grabbed; any permitted manual override requires explicit confirmation and target mapping.

Candidate columns are configurable and all meaningful fields are filterable. Default ordering follows the effective profile score, then decision quality/source preference.

## 22. Downloads / Imports inside Activity

There is no standalone permanent Imports page in V1.

Operational flow:
- active downloads and import processing appear in Admin Activity;
- successful completion appears in History;
- failed/ambiguous work that requires human intervention appears in To-Do.

To-Do can open two distinct mapping flows:

1. **Download Assignment** — for one or more files from a completed download job. Desktop is a compact editable table with one file/episode per row. Binding spec: `docs/mockups/admin-download-assignment/SPEC.md`.
2. **Library Reconciliation / Folder Import Mapping** — a dedicated multi-step Admin page for folders/files discovered by Library Scan / Reconciliation that cannot be associated reliably. It supports expandable folder trees, split/merge, batch/per-file mapping, organization policy, rename/move dry-run and execution. Binding spec: `docs/mockups/admin-folder-import-mapping/SPEC.md`.

Both flows map into the canonical media hierarchy. Neither asks the admin to type raw database IDs or arbitrary destination paths as the normal UX.

## 23. Storage admin

Binding screen specification:
- `docs/mockups/admin-storage/SPEC.md`

Storage separates **physical Mounts** from **logical storage roles**.

Physical Mounts own capacity/health. Multiple LibraryRoots may live on the same Mount without pretending to be separate disks.

Storage roles include:
- LibraryRoots for final specialized libraries;
- Native Download Workspace for Jularr's built-in Usenet downloader;
- Generic Downloads Root for content that has no specialized library;
- optional future managed workspaces such as backup/cache/transcode.

The Native Download Workspace is temporary operational storage for incomplete download, verification/repair, extraction and staging. It is distinct from final Generic Downloads.

Path selection uses a server-side safe browser restricted to permitted Mounts. Native downloader transport/server settings live in Acquisition/Usenet settings, not Storage.

## 23a. Native Downloader Admin

Binding specifications:
- `docs/mockups/admin-downloader/SPEC.md`
- `docs/mockups/admin-downloader-overview/SPEC.md`
- `docs/mockups/admin-downloader-queue/SPEC.md`
- `docs/mockups/admin-downloader-servers/SPEC.md`
- `docs/mockups/admin-downloader-processing/SPEC.md`
- `docs/mockups/admin-downloader-speed-schedule/SPEC.md`
- `docs/mockups/admin-downloader-settings/SPEC.md`
- `docs/mockups/admin-downloader-external-clients/SPEC.md`

Downloader is one Admin destination with secondary navigation:
- Übersicht
- Queue
- Server
- Verarbeitung
- Geschwindigkeit & Zeitplan
- Einstellungen
- Externe Clients

The Downloader area owns native Usenet transport/download mechanics, technical queue/history, server health, verify/repair/extract behavior, bandwidth/concurrency/scheduling and optional external-client adapters.

It does **not** duplicate:
- Storage mounts/workspaces;
- Wanted or Manual Search;
- AcquisitionProfile scoring;
- Indexers;
- post-download manual assignment;
- global Activity/History;
- host-wide system telemetry.

Overview may show downloader-specific throughput, workspace pressure and bottlenecks. Queue owns deep per-job diagnostics. Server owns NNTP configuration/health. Processing owns Verify/Repair/Extract/Cleanup. Speed & Schedule owns bandwidth, concurrency and timed actions. Settings owns general retry/duplicate/retention/cache behavior. External Clients is compatibility-only; native Usenet remains the normal/default path.

## 24. Provider settings

All provider types use a common configuration pattern:
- provider card/list
- enabled state
- health
- configuration
- Test button
- capabilities
- priority/order where relevant

Secrets are write-only/masked.

Do not build completely different settings UI for every provider.

## 25. AI admin

Dedicated Admin AI surface:
- configured providers/models
- server default model per task/category
- availability policy: disabled, admin-only, shared instance, user/group policy where supported
- task permissions
- health/test
- generated artifact visibility policy

Admin can invoke appropriate server AI tools from Admin workflows. Normal users use personal AI unless instance policy explicitly grants shared AI.

## 26. Users & permissions

User list -> user detail.

User detail:
- roles/groups
- capabilities
- media/request permissions
- learning permissions
- AI policy
- profile restrictions
- active devices/sessions where appropriate

UI is capability-based; avoid scattering hard-coded `IsAdmin` assumptions through pages.

## 27. Admin Requests, Activity and History

Binding screen specifications:
- `docs/mockups/admin-requests/SPEC.md`
- `docs/mockups/admin-activity/SPEC.md`
- `docs/mockups/admin-history/SPEC.md`

Requests moderates user requests and hands approved acquisition needs into Wanted.

Activity / To-Do is the live/pending operational work queue for imports, remux, repack/replace, subtitles, translations, metadata, AI and maintenance.

History is the past operational record with category/date filters and actor/result details.

## 28. Responsive profiles

### Mobile
- bottom navigation
- one-column flows
- sheets/fullscreen dialogs rather than tiny desktop modals
- >=44px touch targets
- thumb-friendly primary actions
- no hover-only behavior

### Tablet
- adaptive two-pane layouts where useful
- touch remains primary
- sidebar may collapse/expand based on width

### Desktop
- persistent sidebar
- keyboard/mouse optimized
- denser tables and hover affordances allowed
- resizable/split views only where they improve real workflows

### TV
- focus navigation
- large typography/targets
- safe areas
- no dense admin UI; Admin remains web-first unless a TV-specific need exists

## 29. Shared components

Before page implementation, define/reuse:
- AppShell / navigation
- PageHeader
- SearchBox
- MediaCard
- MediaRow
- Poster/Cover
- ProgressIndicator
- StatusIndicator
- FilterBar
- EmptyState
- ErrorState
- Skeleton/loading state
- Button/IconButton
- Menu
- Dialog/Sheet
- Tabs/segmented control
- DataTable/admin list
- Form fields
- Toast/notification
- ProviderCard
- JobStatus

No local clone of a component just to alter spacing/color.

## 30. Visual rules

### Admin light baseline

For the currently approved Admin planning mockups:
- use a clean light theme;
- use the compact Jularr Admin shell/sidebar;
- avoid decorative background artwork on operational Admin pages;
- status/type tags use borders and only lightly tinted backgrounds;
- small category/status icons may use accent colors;
- avoid fully saturated colored pills/badges;
- keep dense information structured and calm;
- Mobile keeps the same hierarchy but uses larger touch-friendly cards/controls.


- Shared spacing/radius/type/color tokens.
- Robotic/tag-heavy metadata presentation is avoided.
- Use icons where meaning is established; pair with labels when ambiguity exists.
- Accent color is used selectively, not as large saturated surfaces everywhere.
- Cards remain visually calm and consistent.
- Media artwork keeps consistent aspect ratios.
- Focus, hover, pressed, disabled and selected states are all defined.
- Dark mode is designed, not generated by simply inverting colors.

## 31. State requirements for every screen

Each mockup/spec must explicitly account for:
- loading
- empty
- ready
- partial/degraded provider/storage state
- recoverable error
- forbidden/permission state where relevant
- offline/PWA state where relevant

An agent may not treat only the ideal populated state as complete.

## 32. Navigation/back behavior

- Browser Back works predictably.
- Closing a detail sheet returns to prior context/scroll position.
- Mobile back never unexpectedly exits playback/reader without expected platform behavior.
- Admin mode retains its own navigation context.
- Deep links to Work/Episode/Chapter/Admin diagnostics are valid where authorized.

## 33. Mockups required before implementation

Approve at minimum:
1. Home — desktop/mobile
2. Library — desktop/mobile
3. Search/Discover — desktop/mobile
4. Anime/Series detail — user
5. Movie detail — user
6. Reading detail (Book / Light Novel / Manga) — user
7. Audiobook detail — user
8. Player — mobile/desktop/tablet/TV
9. Reader — mobile/desktop
11. Calendar — desktop/mobile
12. Learning home + lesson/review
13. Settings — desktop/mobile
14. Admin dashboard
15. Admin media detail
16. Wanted/Missing + Manual Search
17. Activity / To-Do + Download Assignment + Library Reconciliation wizard
18. Storage/path browser
19. Native Downloader — Overview/Queue/Server/Processing/Speed/Settings/External Clients
20. Providers/settings
21. AI admin
22. Users/permissions

Mockups are binding UX references. Agents must not redesign them during implementation without updating/approving the spec.

### Mockup repository convention

Each substantial screen should use its own folder:

```text
docs/mockups/<screen>/
  SPEC.md
  desktop.png
  mobile.png
  tablet.png
  tv.png
```

Only create platform images that are actually needed. `SPEC.md` contains the binding screen-specific requirements; `docs/UX.md` contains global/shared UX rules. Existing root-level mockup assets may remain temporarily until moved without losing binary history.

## 34. Implementation gate

A vertical slice may start only when:
- canonical domain ownership is known;
- relevant architecture contracts are known;
- screen/flow is described here or in a linked spec;
- required major mockup is approved;
- loading/empty/error/responsive states are defined;
- acceptance criteria exist.

This prevents implementation agents from inventing product structure while coding.

### Canonical search identity and Anime provider view

Search is canonical-first across the product. Series/Anime use one Jularr work with seasons/episodes even when a provider such as AniList models seasons, parts or specials as separate media entries.

Default search groups results by media type and deduplicates provider hits into canonical Jularr works. When the Anime filter is active, the result surface may switch between **Jularr** (canonical grouped work, default) and **AniList** (provider-native entries). A specific season/part query may deep-link directly to that season/provider presentation, but still opens the same canonical work. Home, Discover, Library, Calendar and Requests must resolve through the same identity layer rather than inventing surface-specific duplicates.

