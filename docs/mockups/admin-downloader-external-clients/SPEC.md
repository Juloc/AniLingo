# Admin Downloader External Clients — V1

Status: approved planning direction; current Externe Clients mockup is the visual baseline.

Shared contract: `docs/mockups/admin-downloader/SPEC.md`.

## Purpose

Optional compatibility/migration adapters for external download clients.

Native Jularr Usenet remains the default path.

## Supported model

Each external client adapter declares capabilities.

Examples may include:
- SABnzbd-compatible
- NZBGet-compatible
- other explicitly supported downloader adapters

Do not imply torrent support from placeholder mockups unless torrent support is separately approved.

## Client list

Show:
- Name
- Type
- Endpoint
- Health
- enabled state
- category mapping summary
- remote path mapping summary
- capabilities
- actions

## Client editor

Common:
- Name
- Type
- URL/host
- authentication/API key
- enabled
- test connection
- default category
- category mappings
- optional fallback eligibility
- capability display

Secrets are masked/write-only.

## Remote path mapping

Needed when external client and Jularr see different filesystem paths.

Mapping fields:
- external/remote path prefix
- Jularr-local permitted path
- optional host/client scope

Validation:
- local side must resolve inside a permitted Storage Mount
- overlapping mappings must be deterministic
- unmapped completed path blocks import and creates actionable To-Do

## Category mapping

Map Jularr acquisition routes/categories to external client categories.

Do not encode canonical media identity in category strings.

## Capabilities

Display whether adapter supports:
- add NZB
- pause/resume
- delete
- priority
- category change
- queue query
- history query
- per-job progress
- speed limit
- remote path reporting

Unsupported controls are hidden/disabled.

## Fallback

If external-client fallback is supported:
- explicitly opt in
- define eligibility/order
- surface every automatic fallback in job diagnostics
- never silently switch on permanent auth/config failure

Native downloader remains first-class.

## Actions

- add
- edit
- enable/disable
- test
- remove
- inspect health

## States

Additional:
- auth failed
- endpoint unavailable
- capability mismatch
- remote path unmapped
- category missing
- partial API support
- disabled

## Must not implement

- No external client requirement for normal Jularr operation.
- No arbitrary local path outside Storage.
- No cleartext API keys after save.
- No assumption every client supports identical actions.
- No torrent-client UI unless separately approved.
