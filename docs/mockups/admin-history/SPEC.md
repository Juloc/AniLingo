# Admin History — V1

Status: approved UX direction from planning mockups.

Global UX rules: `docs/UX.md`

## Purpose

History is the immutable/append-style Admin operational record of completed or attempted actions.

It answers:
- What happened?
- When?
- To which media/item?
- What was the result?
- Was it automatic or manual?
- Who/what performed it?

History is not the same as Activity:
- Activity = active/pending operational work.
- History = past events/results.

## Category filters

Primary category filters:
- All
- Acquisition
- Imports
- Remux
- Repack/Replace where applicable
- Subtitle
- Translation
- Metadata
- AI
- Maintenance

Categories may expand over time.

Use a date-range filter and sort order.

## Desktop layout

Use:
- Admin sidebar
- category filter pills
- search
- date-range control
- sort
- structured history table
- pagination

Recommended columns:
- Time
- Type
- Title / details
- Result
- Performed by
- Actions

Results may include:
- Success
- Warning
- Failed
- Cancelled

Performed by distinguishes:
- system/automatic
- named admin/user
- provider/job where useful

## Mobile layout

Use chronological stacked cards.

Each card shows:
- timestamp
- event type
- media/title
- concise result/details
- result state
- actor/system
- overflow/details

Date/category filtering remains accessible without tiny controls.

## Details

A history entry may open:
- related Work/unit
- source/target
- before/after where meaningful
- provider/indexer/download client
- job/request/import linkage
- result/error message
- actor
- timestamps
- relevant diagnostics/logs

## Visual language

- Light Admin design.
- Use subtle outlined/tinted category and result tags.
- Do not fill entire pills with saturated colors.
- Keep the page visually quiet so errors/warnings stand out.

## Retention

History retention/storage policy is a backend/system setting and must not be implied by the mockup. The UI should handle large histories through pagination/virtualization and filtering.

## Acceptance criteria

- History is separate from live Activity.
- Category and date filters exist.
- Actor and result are visible.
- Desktop table and Mobile cards present the same underlying events.
- Detail links preserve context and return to the previous filter/scroll state.
