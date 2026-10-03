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

Needed only when this external client and Jularr see the same underlying storage under different path prefixes.

Example:

`/downloads/complete/tv` reported by the external client
→ `/media/downloads/complete/tv` as visible to Jularr.

Remote path mappings are owned **per external client adapter**. Do not create a second global Acquisition/Import mapping list.

Mapping fields:
- external/remote path prefix
- Jularr-local target selected from permitted Storage
- optional media/category scope when one client exposes different mounts per category

The local side should be selected through Storage-aware controls rather than unrestricted filesystem text entry.

### Matching semantics

Use one canonical shared path-mapping implementation:
- longest matching remote prefix wins;
- path-boundary aware matching;
- deterministic ordering;
- slash/backslash normalization where applicable;
- no mapping means the reported path remains unchanged.

### Live test

The editor provides a test input:

`reported path → resolved Jularr path → reachable/unreachable`

The test uses the exact same resolver as completed-download import.

Validation:
- local side must resolve inside a permitted Storage Mount;
- overlapping mappings must remain deterministic;
- resolved target must not escape the configured local root;
- unmapped/unreadable completed path blocks import and creates actionable To-Do;
- test failures do not mutate Storage.

The native Jularr downloader does not use these mappings in normal operation.

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
- No duplicate global remote-path mapping page.
- No remote-path mapping for the native downloader.
- No cleartext API keys after save.
- No assumption every client supports identical actions.
- No torrent-client UI unless separately approved.
