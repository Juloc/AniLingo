# Admin Folder / Library Import Mapping — V1

Status: planning baseline; dedicated mockup required before implementation.

Global UX rules: `docs/UX.md`
Parent screen: `docs/mockups/admin-activity/SPEC.md`

## Purpose

Use this flow when Library Scan / Reconciliation finds an existing folder or group of files that Jularr cannot reliably associate with canonical media.

Typical causes:
- random or inconsistent folder names
- filenames without usable IDs
- mixed naming schemes
- missing season/episode numbers
- existing libraries migrated from another system
- specials/OVAs or multi-file folders that do not parse cleanly

This is distinct from `admin-download-assignment`.

Download Assignment fixes files from one completed download job.
Folder / Library Import Mapping reconciles existing files already discovered inside a configured library root.

## Entry points

Open from:
- Admin Activity / To-Do after Library Scan/Reconciliation
- Storage/library reconciliation problem
- explicit authorized repair action for an unmatched folder

There is no normal consumer entry point.

## Core layout

Desktop should use a single large dialog/sheet with tables, not side-by-side fancy panels.

Primary sections:
1. compact folder/source context
2. folder-level assignment table
3. files-in-folder mapping table
4. batch actions
5. destination/assignment preview where useful

The file table is the main working area.

## Folder context

Show compactly:
- source folder
- LibraryRoot
- file count
- scan/reconciliation source
- detected hints
- problem reason

No decorative artwork is required.

## Folder-level assignment

Editable fields may include:
- Work
- Structure / Season
- Edition
- Version
- language defaults
- audio/subtitle defaults

Jularr prefills detected values where available.

Missing required values are visibly marked.

## Files table

Use one row per discovered media file.

Recommended columns:
- Datei
- automatisch erkannt
- finale Episode / Unit
- Qualität
- Sprache
- Audio/Subs
- Status

For episodic media, final Episode/Unit is directly editable per row.

For other media types, the Unit selector adapts to the canonical structure:
- Volume
- Chapter
- Part
- Track
- other canonical Structure unit

## Batch mapping

Provide efficient batch actions for messy folders:
- apply Work/Season to all
- apply language/audio/subtitle defaults
- map sequential filenames by order
- assign episode range
- skip selected files
- mark extras/non-media files

Batch suggestions must always remain reviewable before save.

## Sequence mapping

For folders such as:
- `01.mkv`
- `02.mkv`
- `03.mkv`

Jularr may propose:
- E01
- E02
- E03

The admin can accept/correct each row.

Do not automatically treat file order as canonical truth without confirmation when identity is uncertain.

## Destination

Files already inside a LibraryRoot are normally reconciled in place.

The UI may show the resolved canonical location/naming result if a rename/move would occur.

Any rename/move must be explicit and previewable when destructive or potentially disruptive.

## Actions

Depending on context:
- save mapping
- save and reconcile
- retry detection
- skip folder
- skip selected files
- ignore
- cancel

## Responsive

### Desktop

Primary platform:
- large table-oriented dialog/sheet
- one row per file
- batch controls above/below table

### Tablet

Same model, reduced columns and detail expansion.

### Mobile

Full-screen sheet with file rows adapted for touch. Large folder reconciliation is still Admin-web-first.

### TV

Unsupported.

## Light / Dark

Both required.

Use operational Admin styling:
- clean
- dense
- restrained
- no decorative hero layout

## States

Required:
- unmatched folder
- partial automatic match
- no Work match
- Work found / units unknown
- some files matched / some unresolved
- duplicate/conflict
- storage unavailable
- save/reconcile in progress
- completed
- error
- forbidden

## Domain / architecture constraints

- Reconciliation maps existing files into `Work -> Structure -> Edition -> Version -> Asset/File -> Track`.
- Folder and filename structure are evidence, not canonical identity.
- No parallel persisted media hierarchy may be created for scanned folders.
- Existing file paths do not define Work identity.
- Batch mapping must still produce explicit canonical assignments.

## Must not implement

- No reuse of the compact Download Assignment layout for large folder reconciliation.
- No raw database-ID entry.
- No filename-only canonical identity.
- No silent global parser learning.
- No automatic destructive rename/move without explicit preview/semantics.
- No separate Anime/Series/Movie reconciliation cores.
