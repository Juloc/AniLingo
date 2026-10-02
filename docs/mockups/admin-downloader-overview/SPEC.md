# Admin Downloader Overview — V1

Status: approved planning direction; current Downloader Übersicht mockup is the visual baseline.

Shared contract: `docs/mockups/admin-downloader/SPEC.md`.

## Purpose

Live operational overview for the native downloader.

This page answers:
- Is the downloader healthy?
- What is it doing now?
- How fast is each pipeline phase?
- Is network, disk, CPU/post-processing or storage the bottleneck?
- Are Usenet servers healthy?
- How large is the queue/workspace pressure?

## Page structure

1. compact KPI row
2. time-series throughput chart
3. current pipeline/activity table
4. workspace utilization
5. server-status summary
6. optional longer-period stats

## KPIs

Default:
- downloader state: Active / Paused / Degraded
- current NNTP download speed
- active downloads
- queue remaining size
- workspace used/free
- usable servers / configured servers
- optional current bottleneck

Do not combine all processing throughput into one fake "download speed".

## Throughput chart

Selectable range:
- 1h
- 24h
- 7d
- 30d

Series where data exists:
- NNTP Download
- Disk Write
- Verify/Repair
- Extract
- Import/Copy/Move

For long ranges, use aggregated samples.

Show:
- current
- average
- peak where useful

## Current activity

Dense table/list:
- Name
- Phase
- Progress
- Processed / Total
- current phase throughput
- ETA
- server where relevant
- compact actions

Possible phases follow the shared pipeline state model.

Clicking a row opens Queue job details.

## Workspace utilization

Show the configured Native Download Workspace:
- total quota or available mount capacity relevant to the workspace
- incomplete
- completed/staging
- verify/repair
- extract/temp
- free/reserved

If metrics are estimated/cached, label them.

## Server summary

Per server:
- name
- role/priority
- active/max connections
- current transfer
- health
- missing article/error indicator
- standby state

Click opens Server page.

## Historical stats

Useful optional summary:
- downloaded today
- downloaded this month
- successful jobs
- failed jobs
- repaired jobs
- repair data ratio
- average download speed
- average end-to-end completion time

These are downloader stats, not library consumption stats.

## Bottleneck indicator

May report:
- Network
- Server
- Disk write
- Verify/Repair CPU
- Extract CPU/Disk
- Import/Copy
- Workspace capacity

It must be evidence-based and may show "Unknown".

## Actions

- Pause/Resume downloader
- open Queue
- open Server health
- open Speed & Schedule
- refresh telemetry

Do not place destructive queue bulk actions on Overview.

## States

Additional:
- no servers configured
- queue empty
- downloader paused
- workspace near full
- one server degraded while downloads continue
- telemetry unavailable but queue still functional

## Must not implement

- No host-wide CPU/RAM dashboard duplication.
- No media-library stats.
- No misleading single speed combining unrelated pipeline stages.
- No permanent job editing forms on Overview.
