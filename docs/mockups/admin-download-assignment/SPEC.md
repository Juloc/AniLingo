# Admin Download Assignment — V1

Status: approved planning direction. This flow is for manually fixing downloaded episode/unit assignments from Admin Activity / To-Do.

Global UX rules: `docs/UX.md`
Parent screen: `docs/mockups/admin-activity/SPEC.md`

## Purpose

Use this dialog when a completed download cannot be fully or correctly assigned to its canonical media target before import.

Typical case:
- download finished
- Jularr detected part of the metadata
- one or more required fields are missing, ambiguous or wrong
- admin corrects the assignment
- import continues

This flow is specifically for downloaded files/jobs.

It is not the folder/library reconciliation flow. Unmatched folders and files discovered by Library Scan use `docs/mockups/admin-folder-import-mapping/SPEC.md`.

## Entry points

Open from:
- Admin Activity / To-Do
- failed or ambiguous post-download import job
- authorized repair action for the same job

There is no standalone navigation destination.

## Core layout

Desktop uses a centered Admin dialog.

The dialog is deliberately minimal.

Main content is one editable table:
- **one downloaded file / episode = one row**
- if a download contains multiple episode files, show **one row per file**

Do not add a large media header, poster block, technical summary block or side-by-side detail panel.

## Table columns

Default columns:
- Datei
- Serie / Work
- Staffel
- Episode / Unit
- Release Group
- Qualität
- Sprache
- Audio
- Untertitel
- Version
- Quelle / Typ
- Status

Each field is directly editable in the row where correction is allowed.

### Datei

Show the actual downloaded filename.

### Serie / Work

Canonical Work selector/search.

### Staffel

Canonical season/structure selector where applicable.

### Episode / Unit

Canonical episode/unit selector.

Required for episodic media.

### Release Group

Parsed value prefilled where available and manually correctable.

### Qualität

Normalized quality/source classification such as `1080p WEB-DL`.

### Sprache

Normalized language assignment.

### Audio

Detected or corrected audio language/track summary.

### Untertitel

Detected or corrected subtitle language summary.

### Version

Version target where applicable.

### Quelle / Typ

Normalized source/type such as WEB-DL, BluRay, WEBRip, etc.

### Status

Compact row state:
- complete
- missing required field
- ambiguous
- conflict
- ready to import

## Automatic detection

Jularr preselects every value it can detect.

The table shows the final editable assignment directly. Do not create a second permanent `Automatically detected` column.

Missing or ambiguous values remain empty/marked until the admin resolves them.

Examples:
- Work and Season detected, Episode missing
- Release Group detected, Language missing
- Quality detected but admin corrects it manually

## Validation

Required fields are validated per row.

Missing required values:
- use clear error border/state on the affected control
- list the missing fields in a concise message below the table
- block the final import action until resolved

Example:
`Pflichtfelder fehlen: Episode, Sprache`

Do not hide validation in expandable sections.

## Multi-file downloads

If one download contains multiple files:
- keep one shared table
- create one row per file
- allow each file to map to a different Episode/Unit
- shared metadata may be batch-applied where useful, but every row remains individually reviewable

Do not switch to cards on Desktop.

## Actions

Bottom actions:
- `Abbrechen`
- `Ignorieren`
- `Zuordnung speichern und importieren`

### Abbrechen

Close without applying changes.

### Ignorieren

Leave the job unresolved/ignored according to Activity policy.

### Zuordnung speichern und importieren

Persist the chosen canonical assignment and continue the normal import pipeline.

Selection and import must remain explicit.

## File-derived metadata

Technical values reliably probed from the file should not require manual entry by default.

Examples:
- codec
- resolution
- duration
- container
- audio tracks
- subtitle tracks

These may support parsing/validation, but the main dialog stays focused on the mapping table.

## Destination

Do not ask the admin to manually choose an arbitrary destination path.

Destination is derived from:
- canonical assignment
- configured LibraryRoot
- storage policy
- naming/path rules

Path resolution belongs to the import/storage pipeline, not this dialog.

## Responsive

### Desktop

Primary V1 target:
- centered dialog
- dense editable table
- one row per downloaded file
- no decorative sections

### Tablet

Same data model with reduced widths and horizontal table scrolling only if necessary.

### Mobile

Full-screen sheet. May stack fields per file if required for usability, but must preserve the exact same mapping model.

### TV

Unsupported.

## Light / Dark

Both first-class.

Operational Admin styling only:
- clean
- compact
- restrained
- no decorative hero treatment
- error states use border/icon/text, not large filled color panels

## States

Required:
- loading
- one unresolved file
- multiple unresolved files
- missing required fields
- ambiguous field
- conflict
- ready to import
- file disappeared
- import retry failed
- forbidden

## Domain / architecture constraints

- Final assignment maps into `Work -> Structure -> Edition -> Version -> Asset/File -> Track`.
- Download filename/parser output is evidence, not canonical identity.
- Parsed metadata can be corrected without creating parallel media models.
- One-off manual corrections must not silently retrain global parser rules.
- A successful confirmation continues the canonical import pipeline.

## Must not implement

- No folder/library reconciliation in this dialog.
- No large artwork/header block.
- No separate technical-details section dominating the dialog.
- No side-by-side mapping panels.
- No raw database-ID entry.
- No arbitrary server-path entry.
- No hidden missing-field validation.
- No separate media-type-specific mapping cores.
- No silent parser learning from one manual correction.
