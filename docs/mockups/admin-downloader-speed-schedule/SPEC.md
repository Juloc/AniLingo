# Admin Downloader Speed & Schedule — V1

Status: approved planning direction; current Geschwindigkeit & Zeitplan mockup is the visual baseline.

Shared contract: `docs/mockups/admin-downloader/SPEC.md`.

## Purpose

Control bandwidth, pipeline parallelism and time-based downloader behavior.

## Sections

- Geschwindigkeit
- Parallelität
- Zeitplan

## Speed limits

Configure:
- global download limit
- unlimited mode
- temporary speed override
- optional per-server caps under Server Advanced settings

Show units explicitly.

## Parallelism

Configure limits for:
- parallel downloads
- parallel verify jobs
- parallel repairs
- parallel extracts
- parallel import/copy/move handoffs
- total NNTP connections ceiling
- optional CPU-heavy task ceiling

Per-server connection counts remain on Server page.

Warn when configured per-server totals exceed the global connection ceiling.

## Scheduler

A schedule contains:
- days
- start/end local time
- action(s)

Supported actions may include:
- set speed limit
- unlimited
- pause downloads
- resume downloads
- enable/disable selected server(s)
- pause/resume verify/repair
- pause/resume extract/post-processing
- change concurrency preset

Rules may overlap only with deterministic precedence.

UI must preview:
- active rule now
- next rule
- effective speed/concurrency state

## Timezone / DST

Use configured instance timezone.

Scheduler must behave predictably across DST changes.

## Manual override

Admin can temporarily override:
- current speed
- pause/resume state
- concurrency preset

Show whether override lasts:
- until next schedule rule
- for a duration
- until manually cleared

## Actions

- add/edit/delete rule
- enable/disable scheduler
- duplicate rule
- apply temporary override
- clear override
- reset to configured defaults

## States

Additional:
- scheduler disabled
- no rules
- currently throttled
- paused by schedule
- manual override active
- conflicting rule validation error
- server action references removed server

## Must not implement

- No silent schedule precedence.
- No server credentials/settings here.
- No storage path settings here.
- No hidden timezone behavior.
