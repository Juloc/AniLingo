# Request Status / Details — Consumer Surface

Status: **binding planning specification; no dedicated mockup required before first implementation**.

This is the normal-user surface for understanding a previously submitted Request.

It is not an Admin Requests page, not Wanted, and not an acquisition diagnostics view.

## 1. Purpose

The surface answers:

- What did I request?
- What scope/language/Edition did I request?
- Is approval still pending?
- If approved, what simple acquisition state is it in?
- Is it available now?
- Can I cancel my own Request?

The user should understand the outcome without learning Jularr's technical acquisition pipeline.

## 2. Entry points

Open Request Status / Details from:

- the `Requested` / request-state action on a media Detail page;
- Request Success -> `View request status`;
- a future compact `My Requests` list if one is added;
- notifications related to that Request;
- deep link to the user's own Request.

Do not add a new permanent main-navigation destination only for Requests.

## 3. Surface type

Desktop:
- compact side sheet or medium dialog for normal status/detail;
- full page only if a future `My Requests` management surface needs it.

Mobile:
- bottom/full-height sheet depending on content length.

Tablet:
- adaptive dialog/sheet.

TV:
- simplified read-only status view where useful.

## 4. Header

Show:

- small media artwork;
- Work title;
- requested structural scope where relevant;
- concise media type only when useful;
- current human-readable state;
- Close/Back.

Do not show provider IDs, Wanted IDs, job IDs or download-client identifiers.

## 5. Requested configuration

Show exactly what the profile requested:

- Scope;
- selected seasons/episodes/volumes/chapters when Custom;
- language;
- Edition intent;
- future-release monitoring intent where applicable;
- request date/time.

Use the same labels/components as the Request flow.

Do not reinterpret the saved Request using current UI defaults.

## 6. Consumer request states

Use one coherent user-facing state projection.

### Waiting for approval
The Request exists but requires moderation.

Show:
- `Waiting for approval`;
- submitted time;
- Cancel when still allowed.

### Approved
Approval happened but acquisition may not have started yet.

Show:
- `Approved`;
- concise text such as `Jularr will look for this media`.

### Searching
Jularr is looking for an eligible acquisition result.

Show only:
- `Searching`.

Do not show indexers, queries or candidate scores.

### Downloading
Show:
- `Downloading`;
- optional coarse progress percentage only when reliable.

Do not show NZB/client technical fields.

### Importing / Preparing
Show:
- `Preparing` or `Importing`;
- concise explanation that the media is being added to Jularr.

### Monitoring
For Future-only or future portions:
- `Monitoring future releases`.

This may coexist with already completed current acquisition scope.

### Available
Show:
- `Available`;
- primary action Play / Read / Listen / Open details.

### Rejected
Show:
- `Request rejected`;
- optional short moderator/user-readable reason when one was supplied.

Never expose private Admin notes unless explicitly marked user-visible.

### Failed / Needs attention
Show:
- `Could not complete request`;
- user-readable failure category where available;
- Retry Request only when policy/state genuinely allows it.

Do not expose raw stack traces or technical importer/indexer errors.

### Cancelled
Show:
- `Request cancelled`.

If shared acquisition continues because another Request/monitoring rule still requires it, do not misleadingly say the underlying acquisition was cancelled.

## 7. Timeline

Use a compact, consumer-readable timeline when multiple transitions exist.

Example:

```text
Requested          2 Oct 18:42
Approved           2 Oct 18:43
Downloading        2 Oct 18:47
Available          2 Oct 19:12
```

Only show meaningful milestones.

Do not expose every search attempt, release rejection, import sub-job or retry.

The technical event timeline remains in Admin.

## 8. Request vs acquisition identity

The user-facing Request remains attributable to the requesting profile.

After approval, one Request may share canonical Wanted/acquisition state with:
- another user's compatible Request;
- owner monitoring;
- an existing Wanted target.

The Details surface displays the result relevant to this Request without pretending that the technical acquisition belongs exclusively to this user.

## 9. Cancel behavior

Allow `Cancel request` only when state/policy permits.

Cancellation requires confirmation when it may affect an active request intent.

Cancellation removes this profile's Request intent.

It does **not automatically**:
- stop a shared download;
- unmonitor media required elsewhere;
- delete imported media;
- cancel another user's Request.

After cancellation, show the actual resulting Request state.

## 10. Retry behavior

A normal user does not manually choose another release.

When Retry is allowed:
- it means retry the same Request intent through normal policy;
- it never opens Manual Search;
- it never exposes indexer candidates.

If admin action is required, show a concise waiting/needs-attention state instead.

## 11. Edit behavior

Pending Requests may optionally expose `Edit request` when policy allows changing:
- Scope;
- language;
- Edition intent.

Editing reuses the shared Request controls.

Do not create a second request-edit implementation.

Once acquisition is materially underway, changes that would alter target identity may require:
- cancel + new Request; or
- explicit backend-supported retargeting.

Do not silently mutate an in-progress acquisition target.

## 12. Notifications

Request state changes may generate notifications through the normal Notifications system:

- approved;
- rejected;
- available;
- failed/needs attention.

This surface does not own notification delivery configuration.

## 13. Desktop layout

Recommended layout:

- media/request header;
- prominent current state;
- requested configuration summary;
- compact milestone timeline;
- available actions at bottom.

No dense table.

No Admin sidebar.

## 14. Mobile layout

One vertical sheet/page:

- artwork + title;
- state;
- requested configuration;
- timeline;
- primary action;
- overflow/destructive Cancel where applicable.

Use touch-sized actions.

## 15. Loading / errors

Support:

- Request metadata loading;
- linked acquisition state temporarily unavailable;
- Work unavailable/removed;
- stale request projection;
- permission changed.

If technical acquisition state cannot be loaded, still show the user's saved Request and a concise `Status temporarily unavailable`.

## 16. Privacy / permissions

A normal profile can only view its own Requests unless explicitly granted broader capability.

User-visible rejection reason must be stored separately from private Admin notes where both exist.

Server authorization is authoritative.

## 17. Accessibility

- current state announced textually;
- timeline uses semantic ordered structure;
- dates have accessible full timestamps;
- progress is not color-only;
- destructive Cancel is clearly labeled;
- focus returns to source context after close.

## 18. Shared components

Reuse:

- `MediaIdentityHeader`;
- `RequestStatus`;
- `RequestSummary`;
- `RequestTimeline`;
- shared Scope/Language/Edition display;
- ConfirmDialog;
- Dialog/Sheet shell.

The Request flow and Request Details must share the same Request DTO/state projection.

## 19. Must not implement

- no Admin moderation controls;
- no Wanted/profile scoring;
- no indexer candidates;
- no download-client technical details;
- no provider IDs/job IDs;
- no raw logs/errors;
- no separate release-retry picker;
- no cancellation of shared acquisition merely because one profile cancels;
- no duplicate Request editing model;
- no new permanent top-level Requests navigation.

## 20. Mockup policy

A dedicated mockup is not required for the first implementation because this is intentionally a standard status/detail sheet.

Create a mockup later if:
- the consumer timeline becomes visually noisy;
- editing pending Requests is added;
- Mobile state hierarchy is unclear;
- owner review requests a visual revision.

Text specification is authoritative.
