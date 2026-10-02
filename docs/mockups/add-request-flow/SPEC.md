# Add / Request Flow — Clean Design

Status: **binding planning specification; visual mockup required before implementation**.

This is the shared consumer **Request** flow from Discover, Search, Calendar, Watchlist and media-detail surfaces.

It must stay media-independent and must not expose indexers, download clients, root paths or release tables to normal users.

## Purpose

One compact flow handles:

- a media item the user has already selected on a calling surface;
- canonical Works already known but not locally available;
- normal-user requests with optional approval;
- privileged requests that may be auto-approved immediately;
- meaningful language/edition choice;
- monitoring scope for structured media;
- confirmation and understandable status.

Simple requests remain simple. A Movie must not use a long wizard just because Series can have granular scope.

## Entry points and identity

The same flow opens from Discover, Search, Detail, Calendar, Watchlist and Related Works.

If a canonical Work already exists, skip identity resolution.

The Request dialog never performs media search or title selection. The calling surface must hand it a resolved canonical Work/target. If a Discover/Search provider result is not yet safely resolved, that resolution happens before opening Request. Ambiguous identity must not open the Request dialog.

If the desired target is already locally available, show Play/Read/Listen instead of pretending it needs acquisition.

## User modes

### Normal user
Primary action is **Request**.

Depending on instance policy:
- request waits for Admin approval; or
- request is auto-approved into canonical Wanted state.

Hide acquisition profiles, indexers, scores, download clients and filesystem paths.

### Owner / Media manager
The primary action is still **Request**.

Their request may be auto-approved immediately according to instance policy/capability and then create/update canonical Wanted state without a moderation stop.

An optional compact Advanced disclosure may expose only allowed acquisition-profile overrides. Manual release search remains Admin-only.

## Dialog model

Desktop:
- centered medium-width modal;
- compact media header;
- adaptive content;
- sticky footer.

Mobile:
- full-height sheet/page;
- one column;
- sticky bottom action.

Tablet:
- modal when wide, sheet when narrow.

TV:
- simplified Request flow only; complex unit selection may hand off to Web/Mobile.

There is no multi-step wizard. The normal flow has exactly two UI states:

1. **Request settings** — all relevant scope/preferences on one page.
2. **Success** — summary of what was requested and its approval state.

Large granular unit selection may temporarily open one focused selector subview, but returning from it restores the same Request settings page.

## Media header

Show:
- small cover/poster;
- canonical title;
- year/author/secondary identity;
- media type only when useful;
- Close.

Never show raw provider IDs.

## Scope selector

Scope is based on canonical structure.

### Movie
- whole Movie

### Anime / TV
- Entire series
- Future episodes only
- Selected seasons
- Selected episodes
- optional Include specials

### Manga
- Entire series
- Future releases only
- Selected volumes
- Selected chapters when canonical structure supports them

### Light Novel
- Entire series
- Future volumes only
- Selected volumes

### Book
- Work / desired edition

### Audiobook
- Work / audiobook edition

Do not expose provider cours/numbering as acquisition identity.

Large episode/chapter selection may open one focused selector subview and return a summary such as `12 episodes selected`.

Do not request chapters already covered by an equivalent selected volume/package unless acquisition rules explicitly distinguish them.

## Preferences

Only show relevant choices.

### Language
- profile default preselected;
- human-readable language name;
- optional fallback where supported.

### Edition / presentation
Written media and audiobooks may expose official/known edition or meaningful format/presentation choice.

Movie/video edition or cut appears only when it is a real user-facing distinction.

Never expose technical release candidates here.

### Acquisition monitoring
Where meaningful:
- monitor future releases within the selected scope;
- search now when approved/permitted;
- monitor only / wait for future availability.

This is **acquisition monitoring only**. It is not the user's personal Watching/Reading/Listening status, Watchlist/Merkliste state, playback progress or external-provider list status.

### Acquisition profile
Normal user: hidden.

Privileged user: optional `Use default` plus allowed override under Advanced.

The flow must always work using defaults.

## Request data contract

A user request stores intent around canonical media:

- requester/profile;
- canonical Work;
- optional canonical structural target;
- requested language;
- requested edition intent;
- monitoring scope;
- timestamps;
- moderation state.

A user request is distinct from technical `WantedItem`.

```text
User Request
→ approval / auto-approval
→ Wanted
→ Search
→ Download
→ Import
→ Available
```

Owner direct Add may create/update Wanted without moderation.

Never create duplicate canonical Works.

## Request settings page

All normal request choices are visible on one page beneath the compact media header.

Recommended order:
1. Scope, only when the media has meaningful selectable structure.
2. Included content tree derived from Scope; collapsed by default where appropriate.
3. Language / edition, only when relevant.
4. Acquisition monitoring for future releases, only when relevant.
5. Optional privileged Advanced override, collapsed by default.
6. Compact request summary near the footer when useful.

Do not include personal media-list controls such as `Watching`, `Planning`, `Completed`, `Add to Watchlist`, `Add to list` or `Start watching automatically`.

Primary button:
- **Request**

Secondary:
- Cancel

There is no Next/Back wizard navigation.

The page must remain short by hiding irrelevant groups. A Movie with no edition/language decision may therefore show only a short summary and Request button.

## Success state

### Submitted
Show:
- Requested;
- Waiting for approval when applicable;
- View request status;
- Done.

### Auto-approved request
Show only true states:
- Request approved;
- Monitoring enabled, if requested;
- Search started, if actually started;
- Open details.

### Already requested
Do not create another request. Show existing state.

### Already monitored
Allow change only with capability.

### Already available
Prefer Play/Read/Listen. A Request action is unnecessary unless the user is requesting a materially different language/edition that is not available.

## Personal media state vs Request state

Request/acquisition state and personal media state are separate concerns.

- **Request/Acquisition state** answers whether Jularr should obtain/monitor media.
- **Personal Jularr media state** answers whether the profile has saved, started, completed, rated or progressed through media.
- **External sync state** (for example AniList) is an adapter over Jularr-owned personal state when the user enables synchronization under Settings > Connections.

The Request dialog never edits AniList directly and never asks for a Watching/Reading status.

External-provider sync must not become canonical ownership of Jularr user state.

## Consumer status projection

Calling surfaces may show:
- Request
- Requested
- Waiting approval
- Searching
- Downloading
- Importing
- Available
- Failed / Needs attention

These project canonical Request/Wanted/Library state. They are not a second state machine.

## Multi-user behavior

Requests remain attributable per profile.

Compatible approved requests converge on shared canonical Wanted state.

Do not acquire the same target twice solely because several profiles requested it.

Materially different language/edition needs may produce distinct acquisition targets.

Canceling one profile's request must not cancel shared acquisition still required by another profile or monitoring rule.

## Permissions

Capabilities control independently:
- Request;
- auto-approved Request behavior where capability/policy permits;
- scope override;
- language/edition override;
- acquisition-profile override;
- cancel own request;
- manage others' requests.

Unavailable privileged controls disappear.

Server authorization is authoritative.

## Desktop layout

One medium modal only:
- MediaIdentityHeader;
- all relevant Request settings in one scrollable content area;
- one Scope control followed by the derived expandable Included content tree;
- compact Language/Edition/Monitoring groups;
- sticky Cancel + Request footer;
- no steps, stepper, wizard rail, Next or Back.

The Included content tree is a preview of the Scope result and can be expanded in place. Detailed checkboxes become editable in Custom mode. Editing a child while using All/Future automatically switches to Custom.

## Mobile layout

One full-height Request sheet/page:
- compact media header;
- all relevant settings in one vertical page;
- touch-sized rows;
- focused selector subview only for long season/episode/chapter lists;
- sticky Cancel/Request actions;
- Close/contextual Back only for leaving the sheet or returning from a temporary selector.

No numbered steps and no Next buttons.

## Loading and errors

Support locally:
- resolving identity;
- loading editions/languages;
- provider unavailable;
- duplicate request;
- permission changed;
- target changed after metadata refresh;
- submit failure.

Render known media identity immediately and preserve valid choices on retry.

## Navigation

Cancel/Close returns to the exact source context.

Preserve where practical:
- Discover/Search filters;
- Calendar period/view;
- detail-page scroll/state.

Success stays in context unless the user explicitly opens Details or Request status.

## Accessibility

- initial focus on dialog heading;
- modal focus trap on Desktop;
- keyboard-operable scope choices;
- textual selected state;
- semantic labels;
- touch-sized Mobile targets;
- screen-reader confirmation contains title, scope and action.

## Shared components

Use one implementation of:
- MediaIdentityHeader
- ScopeSelector
- LanguageSelector
- EditionSelector
- MonitoringChoice
- RequestStatus
- RequestSummary
- Dialog/Sheet shell

Do not create separate Add and Request dialogs and do not create per-media Request dialog implementations.

## Must not implement

- no provider-native permanent identity;
- no separate Add flow or Add state machine;
- no per-media request engine;
- no release/indexer table;
- no downloader selector for normal users;
- no filesystem/root controls;
- no manual-import UI;
- no release-score diagnostics;
- no duplicate equivalent request;
- no duplicate acquisition solely from multiple requesters;
- no ambiguous auto-created Work;
- no forced long wizard for simple media;
- no technical Operations log;
- no Watchlist/Merkliste or Watching/Reading/Listening status controls;
- no AniList/MAL sync controls;
- no `Start watching automatically` or equivalent personal-consumption action.

## Mockup requirement

Before implementation, review:
1. Desktop Light — one-page Anime/Series Request settings modal.
2. Mobile Light — same one-page Request sheet.
3. Simple Movie Request proving irrelevant groups disappear.
4. Success state summarizing exactly what was requested and whether it is Waiting approval or Auto-approved.

Text specification wins over mockup imagery on conflict.


## Explicit flow simplification

The Request dialog always starts from an already selected medium. It never contains Search, Discover results or title selection. Normal request configuration is one page. Successful submission replaces that page with a concise success summary. This is the binding interaction model.
