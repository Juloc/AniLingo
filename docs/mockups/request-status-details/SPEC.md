# Request Status / Details — Consumer Surface

Status: **approved UX direction; binding planning specification**.

This is the normal-user surface for understanding a previously submitted Request.

It is not an Admin Requests page, not Wanted, and not an acquisition diagnostics view.

The approved visual direction is a compact status dialog/sheet centered on the **current user-relevant state**, with Request details and technical-history milestones collapsed behind optional disclosure.

## 1. Purpose

The surface answers:

- What did I request?
- What is happening now?
- What scope/language/Edition did I request?
- Is Jularr waiting for approval, looking, downloading, preparing, monitoring or finished?
- What is the next useful thing I need to know?
- Can I edit or cancel my Request?

The page must not feel like a mini Admin job tracker.

## 2. Entry points

Open from:

- the Request/status action on a media Detail page;
- Library/Calendar/other media cards when a Request state is shown;
- Request Success -> `View request status`;
- a notification linked to that Request;
- a future `My Requests` list if one is added;
- a deep link to the current profile's Request.

Do not add a permanent main-navigation destination solely for Requests.

## 3. Surface type

Desktop:
- compact medium modal/dialog.

Mobile:
- bottom/full-height sheet depending on available content.

Tablet:
- adaptive dialog/sheet.

TV:
- simplified read-only status surface where useful.

The default surface should remain compact. Optional details expand in place.

## 4. Header

Show:

- small media artwork;
- canonical Work title;
- concise year/type only when useful;
- Close/Back.

Do not repeat a large media hero.

Do not show:
- provider IDs;
- Wanted IDs;
- job IDs;
- downloader identifiers;
- indexer information.

## 5. Primary current-state block

The current state is the visual focus.

Show:

- one clear state icon;
- one state label;
- one concise explanation;
- optional progress only when meaningful and reliable;
- optional next-release information when monitoring future content.

Examples:

### Waiting for approval
`Waiting for approval`

`Your request has been submitted and is waiting for approval.`

### Looking for media
`Looking for media`

`Jularr is looking for a suitable release.`

This replaces exposing separate Approved/Search internals as dominant user-facing stages.

### Downloading
`Downloading · 32%`

Optional secondary context:
`4 of 12 episodes downloaded`

Only show percentage/count when trustworthy.

### Preparing
`Preparing`

`Processing downloaded media and adding it to Jularr.`

Do not expose extraction/import substeps.

### Monitoring future releases
`Monitoring future releases`

If known, show a compact next-release card:
`Next release · 8 Oct 2026`

If unknown:
`No upcoming release announced`

### Available
`Available`

Primary action:
- Play
- Read
- Listen
- Open details

depending on media type/state.

### Rejected
`Request rejected`

Optional short **user-visible** reason only.

### Needs attention / Failed
`Could not complete request`

Show one concise user-readable reason category when available.

### Cancelled
`Request cancelled`

Never imply that a shared technical acquisition was cancelled unless it actually was.

## 6. User-facing state model

The normal consumer progression is intentionally simplified:

```text
Waiting approval
→ Looking for media
→ Downloading
→ Preparing
→ Available
```

`Monitoring future releases` is a separate ongoing state for future content and can remain active after currently requested content becomes available.

Exceptional terminal/interruption states:
- Rejected
- Needs attention
- Cancelled

Backend/Admin can retain more granular technical states, but they must project into these simpler consumer states.

## 7. Request details block

Below the current state, show a compact **Request details** disclosure/card.

It contains the exact saved Request intent:

- Scope;
- selected units when Custom;
- language;
- Edition intent;
- requested date/time;
- future-release intent where relevant.

Use the same wording as the Request flow.

Examples:
- `Whole series · Current + future`
- `German`
- `Official`

Do not derive the display from current profile defaults after the fact.

On Mobile this block may default collapsed when the current-state section already communicates enough.

## 8. Timeline

Timeline is **secondary** and collapsed by default.

Trigger examples:
- `Show timeline · 4 events`
- `History`

Expanded timeline contains only meaningful consumer milestones, for example:

```text
Requested      2 Oct 18:42
Approved       2 Oct 18:43
Downloading    2 Oct 18:47
Available      2 Oct 19:12
```

Approved may appear here as historical context even though it is not a dominant current state.

Do not show:
- every search attempt;
- rejected release candidates;
- grab scoring;
- downloader retry details;
- extraction steps;
- import sub-jobs;
- raw acquisition events.

Those belong to Admin.

## 9. Edit Request

Where editing is still permitted, show `Edit request`.

This **reopens the same shared Request dialog** with saved values prefilled.

There is no separate Edit Request form implementation.

Editable fields follow the Request spec:
- Scope;
- Included content;
- Language/Edition;
- allowed privileged options.

If changing the Request would invalidate an already-running acquisition target, backend policy decides whether editing is allowed, requires cancellation/new Request, or can safely retarget.

The UI must not silently mutate incompatible in-progress acquisition.

## 10. Cancel Request

Show `Cancel request` only when allowed.

Use a confirmation dialog.

Cancellation removes this profile's Request intent.

It does not automatically:
- stop acquisition needed by another profile;
- stop owner monitoring;
- delete downloaded/imported media;
- delete canonical Work data;
- reset progress;
- cancel another user's Request.

After cancellation, show the actual resulting Request state.

## 11. Retry / failure behavior

Normal users do not choose another release candidate.

If Retry is allowed:
- retry the same Request intent through normal policy;
- use a simple action such as `Retry request`.

Do not open Manual Search.

If Admin intervention is required:
- show `Needs attention`;
- optionally explain that an administrator needs to resolve it.

## 12. Monitoring behavior

For Requests containing future content, monitoring is represented as a user-relevant ongoing result, not as a technical Wanted dashboard.

Show:
- `Monitoring future releases`;
- next known release/date when available;
- otherwise a quiet unknown/no-announcement state.

Do not show scheduled search jobs or polling intervals.

## 13. Available behavior

When all immediately requested content is available:

Show:
- `Available`;
- primary media action;
- optional `View in Library` / `Open details`.

If future monitoring is also active, the surface may show:

`Available now`
and below:
`Monitoring future releases`

These are not contradictory states.

## 14. Request vs acquisition identity

The Request belongs to the requesting profile.

Approved Requests may converge on shared canonical Wanted/acquisition state.

The consumer surface must not pretend a shared download belongs exclusively to one user.

If this user cancels but acquisition continues because another Request/monitoring rule still needs it, show the user's Request as Cancelled without falsely reporting that the shared acquisition stopped.

## 15. Notifications

Request transitions may produce normal notifications for:

- approved;
- rejected;
- available;
- failed/needs attention.

Notification delivery settings remain under User Settings.

## 16. Desktop layout

Approved structure:

1. compact media identity header;
2. prominent current-state block;
3. optional progress / next-release card;
4. Request details block;
5. collapsed Timeline disclosure;
6. bottom actions.

Actions depend on state:
- Edit request;
- Cancel request;
- Retry request;
- Play/Read/Listen;
- View media;
- Close.

Do not draw a permanent multi-step pipeline across the main content.

## 17. Mobile layout

Approved structure mirrors Desktop vertically:

1. compact media header;
2. prominent current state;
3. optional progress / next release;
4. collapsible Request details;
5. collapsible Timeline;
6. touch-sized bottom actions.

Keep the current state visible without requiring expansion.

Do not squeeze desktop horizontal process diagrams onto Mobile.

## 18. Tablet / TV

Tablet:
- same dialog/sheet grammar;
- no additional complexity.

TV:
- read-only/simplified state;
- primary available-media action;
- no complex editing/cancellation if remote UX would become cumbersome.

## 19. Loading / partial / errors

Support:

- Request data loading;
- acquisition projection temporarily unavailable;
- Work metadata partial;
- stale local status;
- permission changed.

If acquisition projection fails, still show the saved Request intent and:
`Status temporarily unavailable`

Do not replace saved Request data with a generic error page.

## 20. Privacy / permissions

A normal profile sees only its own Requests unless explicitly granted broader capability.

Moderator/Admin notes are private by default.

A rejection reason must be explicitly marked user-visible before it appears here.

Server authorization is authoritative.

## 21. Accessibility

- current state has textual label;
- progress has semantic percentage/value;
- timeline is an ordered semantic list;
- full timestamps are accessible;
- collapse/expand controls expose state;
- destructive Cancel is clearly named;
- focus returns to source after close;
- status is not conveyed by color alone.

## 22. Shared components

Reuse:

- `MediaIdentityHeader`
- `RequestStatus`
- `RequestSummary`
- `RequestTimeline`
- `ProgressIndicator`
- `NextReleaseCard`
- ConfirmDialog
- shared Request Dialog/Sheet shell where appropriate

Request creation, editing and status must use one canonical Request DTO/state projection.

## 23. Must not implement

- no permanent technical process pipeline as main content;
- no Admin moderation controls;
- no Wanted/profile scoring;
- no release/indexer candidate details;
- no downloader/client technical fields;
- no provider IDs/job IDs;
- no raw logs/errors;
- no manual release picker;
- no separate Edit Request form;
- no duplicated Request state machine;
- no cancellation of shared acquisition merely because one profile cancels;
- no permanent top-level Requests navigation.

## 24. Approved visual reference

The approved mockup direction is:

- compact Desktop modal and Mobile sheet;
- strong current-state block;
- Downloading progress only when useful;
- Monitoring state with next-release information;
- Request details below;
- Timeline collapsed by default;
- Edit/Cancel as secondary actions;
- Available state with direct media action;
- no Admin-like technical workflow presentation.

The owner will upload the approved image into this folder.

Implementation may not materially redesign this interaction without updating this SPEC and receiving UX approval.

Text specification wins over imagery on conflict.
