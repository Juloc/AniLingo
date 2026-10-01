# Admin Activity / To-Do — V1

Status: planning direction updated. This screen owns live operational work and manual attention flows, including import review.

Global UX rules: `docs/UX.md`

## Purpose

Activity / To-Do is the Admin operational surface for work that is currently happening or requires human intervention.

It is not a media library page, not a raw log viewer, and there is no separate permanent Imports page in V1.

The screen must answer:
- What is currently downloading or processing?
- What requires admin attention?
- What failed and can be repaired?
- What completed successfully?
- Which concrete media/file/job is affected?

## Primary tabs

Use three primary tabs:

1. **Activity**
   - currently downloading
   - queued/running processing
   - import in progress
   - remux/repack
   - subtitle/translation work
   - metadata/AI/maintenance work where relevant

2. **To-Do**
   - jobs that require manual attention
   - failed imports that can be repaired
   - ambiguous media/file identification
   - conflicts
   - missing/invalid metadata required before import
   - storage/path availability problems that require action
   - retryable failures

3. **History**
   - completed/successful work
   - resolved failures
   - cancelled work
   - past import outcomes

Do not create separate top-level tabs for Running, Failed, Imports and History when the same information can be expressed through Activity / To-Do / History plus filters.

Filters may still expose states such as Running, Failed, Queued, Importing, Downloading, Completed and Cancelled.

## Activity tab

Activity is live.

Typical rows/cards:
- Download
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

For downloads show, where available:
- media target
- release/download name
- download client
- progress
- speed
- ETA
- size
- state
- source acquisition link
- post-download/import state

A completed download normally transitions into import processing without creating a second unrelated user workflow.

Live behavior:
- jobs appear/update without reload
- progress updates in place
- state transitions preserve scroll/filter context
- a completed successful job leaves Activity and appears in History
- a problem that requires intervention leaves Activity and appears in To-Do

## To-Do tab

To-Do is for actionable problems, not every technical warning.

Examples:
- import could not identify the Work/unit confidently
- episode/season/volume/version is ambiguous
- file metadata is incomplete or parser output needs correction
- target storage unavailable
- duplicate/conflict requires decision
- download completed but import failed
- manual mapping required
- retryable provider/download/import failure

Each To-Do item shows:
- job/problem type
- media/download title
- concise reason
- current detected target if any
- confidence/problem state
- age
- priority where meaningful
- actions

Primary actions may include:
- Resolve / Assign
- Retry
- Ignore / Dismiss where safe
- Cancel
- Open related media
- View logs/details

## Import Review / Assignment dialog

A failed or ambiguous import opens a dedicated dialog/sheet from To-Do.

This dialog replaces a standalone Imports page.

### Purpose

Let an admin safely correct Jularr's interpretation before the file is committed into canonical media/storage state.

The dialog should prefill everything Jularr could detect automatically and allow explicit correction.

### Editable canonical assignment

The admin may choose/correct, depending on media type:
- Work
- Structure/unit
- season
- episode
- volume
- chapter
- part
- Edition
- Version / version target where applicable

The UI must use canonical pickers/search/selectors. Never ask the admin to type database IDs.

### Editable parsed release metadata

Where automatic parsing/probing is incomplete or wrong, the admin may correct:
- release group
- quality / source / format classification
- language
- audio language
- subtitle language
- release type
- other normalized acquisition metadata supported by the canonical model

These corrections apply to this import decision unless a separate explicit parser/rule-management feature exists.

Do not silently train/change global parser rules from a one-off manual correction.

### File-derived metadata

Where metadata can be reliably probed from the file itself, prefer detected technical facts over freeform manual entry.

Examples:
- codec
- resolution
- bitrate
- audio tracks
- subtitle tracks
- container
- duration

Manual correction is allowed only where Jularr cannot determine the value reliably or where an authorized override is explicitly supported.

### Destination

The admin normally does **not** manually choose an arbitrary destination path.

Once the canonical Work/Structure/Edition/Version assignment is known, Jularr derives the destination from:
- configured LibraryRoot / storage policy
- canonical media assignment
- naming/path rules

The dialog may preview the resolved destination and allow choosing among configured valid roots/policies where permitted.

Never use arbitrary server-path text entry as the normal import-resolution flow.

### File list

Show:
- source download/job
- source file(s)
- detected media file(s)
- ignored/extras where applicable
- size
- current parser interpretation
- target mapping
- conflicts/duplicates
- destination preview

For multi-file downloads/season packs, allow per-file unit mapping when automatic mapping is ambiguous.

### Actions

Depending on state/permission:
- Import / Confirm assignment
- Retry automatic detection
- Save corrected mapping and import
- Skip file
- Ignore job
- Cancel
- Open logs
- Open related media

Destructive source-file actions require explicit semantics/confirmation.

## History tab

History is the completed/resolved operational record.

Successful downloads/imports appear here after completion.

History rows/cards show:
- time
- type
- media/title
- result
- actor/system
- source/download client/provider where relevant
- concise details
- link to resulting media/file or failure details

Import history may show:
- source download
- detected/resolved canonical target
- resulting Version / Asset / File
- final destination
- whether the resolution was automatic or manual

## Desktop layout

Use:
- persistent Admin sidebar
- title
- Activity / To-Do / History tabs
- search/filter bar
- dense table
- detail drawer/dialog for jobs/problems

Recommended columns vary by tab, but may include:
- Type
- Title / medium
- Step / problem
- Progress
- Status
- Age/time
- Priority
- Actions

Do not create a permanent column wall. Use configurable columns where needed.

## Mobile layout

Do not squeeze the desktop table.

Use touch-friendly cards with:
- media artwork/icon where useful
- type
- title
- current step/problem
- progress/status
- concise key metadata
- overflow/details

Import Review becomes a full-screen sheet on Mobile.

## Visual language

- operational Admin styling
- compact, calm, information-dense
- no decorative hero artwork
- warnings/errors use color sparingly
- important state always has text/icon, not color only
- avoid badge walls

## States

Required:
- loading
- empty Activity
- empty To-Do
- empty History
- live/running
- queued
- downloading
- importing
- needs attention
- ambiguous
- conflict
- storage unavailable
- retrying
- completed
- cancelled
- partial provider/download-client failure
- forbidden/permission error

## Domain / architecture constraints

- Import resolution maps into the canonical `Work -> Structure -> Edition -> Version -> Asset/File -> Track` hierarchy.
- Download/import jobs are operational state, not parallel media models.
- A filename/parser result is evidence, not canonical identity.
- The canonical media assignment is explicit before final import.
- Parsed release metadata and file-probed technical metadata remain distinct concepts.
- Successful import creates/links the appropriate canonical Version/Asset/File/Track state.
- History records the result; Activity/To-Do do not become permanent media ownership stores.

## Must not implement

- No separate permanent Imports navigation/page in V1.
- No separate media-type import queues.
- No raw database-ID editing.
- No arbitrary destination path entry as normal UX.
- No canonical identity derived from filename alone.
- No silent parser learning from one-off manual corrections.
- No silent destructive source-file operations.
- No duplicate Activity/Failed/Imports pages for the same job states.
