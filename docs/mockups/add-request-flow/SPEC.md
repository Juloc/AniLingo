# Add / Request Flow — Clean Design

Status: **binding planning specification; visual mockup required before implementation**.

This is the shared consumer **Request** flow from Discover, Search, Calendar, Watchlist and media-detail surfaces.

It must stay media-independent and must not expose indexers, download clients, root paths or release tables to normal users.

## Purpose

One adaptive flow handles:

- discover-only media not yet in Jularr;
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

If entry is a provider result:
1. resolve the provider candidate;
2. check existing canonical identities;
3. link to an existing Work when matched;
4. create through the canonical identity-resolution path only when safe;
5. continue the flow.

Ambiguous identity must not be auto-created. Normal users see a simple needs-review/unavailable state; Admin resolves ambiguity elsewhere.

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

Logical stages:
1. Scope
2. Preferences
3. Confirm

Stages collapse when unnecessary.

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

### Monitoring
Where meaningful:
- Monitor selected scope;
- Search now when approved/permitted;
- Monitor only / wait for future availability.

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

## Confirmation

Final summary answers:
- what;
- scope;
- language/edition;
- monitoring behavior;
- whether approval is required;
- whether Jularr searches now or only monitors.

Primary button label:
- **Request**

Secondary:
- Back when needed
- Cancel

Do not use generic `Submit`.

## Success and existing states

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

- medium modal;
- MediaIdentityHeader;
- compact Scope/Preferences groups;
- selected-scope summary;
- sticky Cancel + primary action footer;
- no wizard sidebar.

Only show season/episode/chapter lists after the user asks for granular selection.

## Mobile layout

- full-height sheet/page;
- compact media header;
- one-column controls;
- touch-sized rows;
- focused selector subview for long unit lists;
- sticky action;
- contextual Back/Close.

Do not squeeze the Desktop modal onto phone width.

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
- ConfirmationSummary
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
- no technical Operations log.

## Mockup requirement

Before implementation, review:
1. Desktop Light — Anime/Series Request with scope + language; no Add/Request choice.
2. Mobile Light — same Request as full-height sheet.
3. Simple Movie Request showing collapsed one-screen behavior.
4. Submitted state showing both possible outcomes: Waiting approval or Auto-approved.

Text specification wins over mockup imagery on conflict.
