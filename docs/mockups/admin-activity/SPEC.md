# Admin Activity / To-Do — V1

Status: approved UX direction from planning mockups.

Global UX rules: `docs/UX.md`

## Purpose

Activity / To-Do is the Admin operational work queue for everything Jularr is currently processing, waiting to process, or requiring attention.

It is not a media library page and not a raw log viewer.

Typical work includes:
- Import
- Remux
- Repack / Replace
- Subtitle processing
- Translation
- Metadata refresh
- AI generation
- Scan / analysis
- Rename / organize
- Maintenance

## Tabs

Primary state tabs:
- To-Do
- Running
- Failed
- History
- All

Counts may appear as small neutral count pills.

Tabs filter the same canonical job/task dataset; they do not represent separate storage models.

## Desktop layout

Use the light Admin design:
- persistent Admin sidebar
- top search
- title + concise explanation
- state tabs
- search/filter bar
- dense but readable task table
- pagination where required

Recommended columns:
- Job type
- Title / medium
- Step / details
- Progress
- Status
- Priority
- Actions

Optional configurable columns may include:
- created time
- started time
- ETA
- worker
- user/request source
- attempts
- last error

## Mobile layout

Do not squeeze the desktop table.

Each job becomes a touch-friendly card showing:
- media artwork/icon where applicable
- job type
- title
- current step
- progress
- status
- priority only when relevant
- overflow actions

Tabs remain horizontally scrollable or compact.

Filters open as a mobile sheet/drawer when space is limited.

## Job details

Opening a job shows:
- concrete target/work/unit
- current step
- progress
- created/started time
- ETA where reliable
- attempts
- worker/provider where useful
- originating request/acquisition/import where applicable
- related logs
- last error
- dependencies / blocked reason where applicable

Actions may include:
- Retry
- Pause/Resume where supported
- Cancel
- Change priority
- Open related media
- Open source download/import
- View logs

Never show an action the backend cannot actually perform.

## Visual language

- Light theme is the approved visual baseline for this screen.
- No full saturated status chips.
- Status/type labels use subtle tinted backgrounds or outlined pills.
- Icons may carry the category color.
- Healthy/normal states remain quiet.
- Error/destructive states use red sparingly.
- Avoid badge walls.

## Live behavior

Activity is live:
- new jobs appear without reload;
- progress updates in place;
- completed/failed jobs move state without losing scroll/filter context;
- opened job details stay open while data updates.

## Acceptance criteria

- To-Do, Running, Failed, History and All states are available.
- Remux and Repack/Replace are first-class job types.
- Jobs explain what is being done and to which media object.
- Progress/status updates live.
- Desktop uses a structured table.
- Mobile uses touch-friendly cards.
- Retry/cancel/details are capability-aware.
- Styling follows the restrained light Admin design.
