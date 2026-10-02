# Admin Downloader Queue — V1

Status: approved planning direction; current Queue mockup is the visual baseline.

Shared contract: `docs/mockups/admin-downloader/SPEC.md`.

## Purpose

Deep operational control of native download jobs.

Global Activity shows cross-system jobs. Queue shows downloader-specific internals.

## Primary tabs

- Active
- Waiting
- Completed
- Failed

Completed/Failed are downloader-local technical history views backed by the same operational/event data used by global History.

## Queue table

Default columns:
- drag/order
- selection
- Name
- Phase
- Progress
- Size / Remaining
- Speed
- ETA
- Server/route
- Category
- Priority
- Health
- Actions

Optional columns:
- age
- added by
- workspace
- missing articles
- retry count
- password state

## Ordering / priority

Support:
- drag reorder
- bulk reorder
- priority levels
- Force / Top priority where technically meaningful
- pause/resume selected

Priority must not bypass hard integrity/safety checks.

## Filters / search

Filter:
- phase
- category
- priority
- server
- health
- failed reason
- date/time
- source/acquisition target where linked

Search by:
- job name
- NZB/release name
- linked Work/target

## Job detail

Selecting a job opens inline detail/drawer.

Tabs:
- Details
- Files
- Articles/Segments
- Pipeline
- Logs

### Details

Show:
- original NZB/release name
- linked Acquisition/Work where available
- category/route
- priority
- created/started time
- total/remaining
- current phase
- phase speed/ETA
- workspace
- retry count
- password state
- current server usage

### Files

Show contained files:
- name
- expected size
- completed size
- state
- optional/ignored status

Allow safe file selection only if the downloader architecture supports skipping optional files before assembly.

### Articles / Segments

Diagnostic view:
- available/missing
- downloaded/retried
- server/failover source
- error reason

Do not render millions of raw rows at once; aggregate/paginate.

### Pipeline

Visual phase sequence:
- Download
- Verify
- Repair
- Extract
- Post-process
- Handoff/Import

Each phase shows:
- state
- duration
- throughput
- result/error

### Logs

Job-scoped structured logs only.

No secrets.

## Health

Use an explicit download health concept:
- Healthy
- Degraded
- At risk
- Failed

Health may consider missing articles, PAR availability, retry/failover state and repairability.

Exact algorithm belongs to backend spec; UI shows evidence/reasons.

## Job actions

Depending on state:
- Pause
- Resume
- Retry
- Change priority
- Move in queue
- Change category/route
- supply archive password
- retry verification/repair/extract
- cancel
- delete job
- optionally retry with additional recovery data if supported
- open linked acquisition/media

Destructive delete must clarify whether temporary downloaded data is also removed.

## Bulk actions

For compatible selected jobs:
- Pause
- Resume
- Priority
- Category
- Cancel/Delete

## Completed / Failed

Show:
- result
- total duration
- download duration
- verify/repair/extract duration
- bytes downloaded
- servers used
- missing article count
- repair result
- import/handoff result
- retry action where possible

Global cross-system history remains elsewhere.

## States

Additional:
- queue empty
- active but speed zero
- waiting on workspace
- waiting on schedule
- waiting on password
- missing articles
- retry/failover
- partial server outage
- verify failed
- repair failed
- extract failed
- import handoff failed

## Must not implement

- No separate media-type queues.
- No hidden failed jobs without filters.
- No raw per-article mega-table without aggregation.
- No queue action that silently deletes canonical library files.
