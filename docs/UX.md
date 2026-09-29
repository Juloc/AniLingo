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

Book/Light Novel:
- volumes/chapters or publication structure
- edition/language selector
- Read/Continue
- translation state where applicable

Manga:
- volume/chapter structure
- Read/Continue
- edition/language selector

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
- double tap left/right: -10/+10 seconds
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

`Add` is contextual from Library/Search/Discover/Admin and opens one coherent flow rather than permanent import forms on normal pages.

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
- Activity / Jobs

### Media
- Library
- Wanted / Missing
- Manual Search
- Imports

### Acquisition
- Indexers / release search providers
- Download clients
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

## 18. Admin dashboard

The Admin Dashboard is the live operational/health surface for Jularr. It is not a media-library statistics page.

Binding screen specification:
- `docs/mockups/admin-dashboard/SPEC.md`

Approved current visual reference:
- `docs/mockups/admin-dashboard-clean-live.png`

The detailed screen spec defines live service health, CPU/RAM/GPU/network/load, active streams with Direct Play/Remux/Transcode diagnostics and controls, downloads, Jularr tasks, storage, warnings and live activity. Global UX rules in this document still apply.

## 19. Admin media detail

Admin media detail is substantially richer than User detail.

For Anime/Series/Movie:
- canonical metadata and external IDs
- episodes/seasons
- local files
- technical tracks
- versions/releases
- audio/subtitle languages
- release group/source/quality
- acquisition profile and scoring result
- remux/transcode-relevant information
- subtitle state
- metadata provenance
- AniList/TMDB/etc. links
- refresh/reidentify actions

For Book/LN/Manga:
- editions/languages
- volumes/chapters
- source/imported files
- translation provenance/status
- artwork
- metadata identities

Admin actions must be explicit and destructive actions require confirmation.

## 20. Wanted / Missing

Unified across media types.

List/table supports:
- Work/unit
- media type
- desired language/edition
- profile
- status
- last search/result
- next action

Actions:
- Search now
- Manual Search
- change profile
- pause/unmonitor
- inspect history/failure

Desktop can use dense table/list. Mobile uses stacked rows/cards without losing actions.

## 21. Manual Search

Sonarr/Radarr-like capability but Jularr visual language.

Shows normalized candidates with:
- title
- source/indexer
- age
- size
- quality/format
- languages
- release group
- score
- rejection reasons

Default ordering follows profile score. User can inspect why a candidate is accepted/rejected and manually grab when authorized.

## 22. Imports

Queue/history of downloads waiting for or completing import.

Shows:
- source download
- detected Work/unit
- destination
- state
- identification confidence/problem
- failure reason

Ambiguous imports provide a safe manual match UI rather than editing database IDs.

## 23. Storage admin

Storage page provides:
- roots/NAS locations
- online/offline
- free/used capacity
- media distribution
- wake/retry state
- path browser for selecting configured roots

Path selection uses a server-side safe file browser limited to permitted roots; users never type arbitrary server paths as the primary UX.

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

## 27. Activity / Jobs

Unified operational timeline/queue:
- acquisition
- imports
- metadata refresh
- subtitle jobs
- translations
- AI generation
- maintenance

Filters by type/state/work/user where permitted. Failed items expose understandable reason, attempts and Retry when safe.

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
6. Book/LN detail — user
7. Manga detail — user
8. Audiobook detail — user
9. Player — mobile/desktop/tablet/TV
10. Reader — mobile/desktop
11. Calendar — desktop/mobile
12. Learning home + lesson/review
13. Settings — desktop/mobile
14. Admin dashboard
15. Admin media detail
16. Wanted/Missing + Manual Search
17. Imports
18. Storage/path browser
19. Providers/settings
20. AI admin
21. Users/permissions
22. Activity/jobs

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
