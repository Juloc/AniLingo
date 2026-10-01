# Admin Library Reconciliation / Folder Import Mapping — V1

Status: approved planning direction for a dedicated multi-step Admin page. Mockup set is being added as visual reference.

Global UX rules: `docs/UX.md`
Parent operational screen: `docs/mockups/admin-activity/SPEC.md`

## Purpose

This page reconciles existing folders and files inside a configured LibraryRoot when Jularr cannot reliably map them to canonical media.

Typical causes:
- random or inconsistent folder names
- missing provider IDs
- filenames without useful season/episode/volume information
- mixed naming schemes
- migrated libraries
- specials/OVAs mixed into normal folders
- nested folders where some subfolders are recognized correctly and others are not
- one folder containing content that actually belongs to multiple canonical groups

This is not the compact post-download assignment dialog.

Download Assignment handles one completed download job.
Library Reconciliation handles an existing folder/subtree and may involve many folders/files, batch decisions, rename/move policy and dry-run execution.

## Entry points

Open as a dedicated temporary Admin route from:
- Admin Activity / To-Do after a Library Scan/Reconciliation problem
- Admin Storage / LibraryRoot scan or reconcile action
- explicit authorized repair action for a folder/subtree

Suggested route shape:
- `/Admin/LibraryReconciliation/{jobId}`

It is not a permanent top-level navigation destination.

## Core interaction model

This is a wizard because the admin may need to:
- inspect many nested folders
- review automatic recognition
- correct folder-level canonical mapping
- correct individual files
- split one physical folder into multiple logical groups
- merge multiple physical subfolders into one logical canonical group
- decide whether files/folders are renamed or moved
- preview all resulting filesystem changes before execution

The wizard must preserve state when moving backward/forward.

Recognized folders are never hidden. They may be collapsed by default and expanded for review/correction.

## Wizard steps

Use five primary steps:

1. **Scan-Übersicht**
2. **Ordnerstruktur & Erkennung**
3. **Mapping**
4. **Organisation**
5. **Vorschau & Ausführen**

Do not add extra steps unless a future requirement cannot fit cleanly into these five.

---

## Step 1 — Scan-Übersicht

### Purpose

Choose what part of a configured library should be scanned/reconciled and understand the scan result before editing mappings.

### Inputs

- LibraryRoot / Bibliothek
- optional start folder within the permitted root
- include subfolders
- skip already confidently assigned files
- optionally show only unclear/unmatched items
- optional deeper filename/metadata analysis
- supported media/file type filters where useful

Path selection uses the safe Path Browser. No unrestricted arbitrary server path entry.

### Result summary

After the scan, show compact counts:
- files found
- automatically assigned
- unclear / needs assignment
- hard errors

Optionally show example/problem folders in a small table.

Primary action:
- `Weiter`

The page should not force the admin to rescan when a valid scan result already exists.

---

## Step 2 — Ordnerstruktur & Erkennung

### Purpose

Review the complete scanned hierarchy and see which folders Jularr understands.

### Main structure

Use an expandable/collapsible tree for physical folders.

Each folder shows a compact state:
- recognized
- partially recognized
- unclear
- error
- ignored

Behavior:
- confidently recognized folders are collapsed by default
- unclear/problem folders are expanded automatically
- any recognized folder can be expanded and corrected
- parent/child hierarchy remains visible

### Selected folder detail

Selecting a folder shows its detected interpretation and contained files.

Do not use a decorative side-by-side media presentation.

Useful data:
- physical path
- detected Work
- detected Structure/Season/Volume
- confidence
- child folders
- file count
- unresolved count

### Split / Merge

The admin can explicitly change grouping.

Supported concepts:
- **Split** one physical folder into several logical groups
- **Merge** multiple physical subfolders into one canonical group
- keep unmatched remainder as its own unresolved group

Example:
- `/random/`
  - mapped group: Solo Leveling Season 1
  - mapped group: Solo Leveling Specials
  - unresolved remainder

Split/Merge affects the working mapping plan only until final execution.

---

## Step 3 — Mapping

### Purpose

Assign the selected logical folder/group and its files to canonical media.

This is the main editing step.

### Folder/group defaults

Editable canonical defaults may include:
- Work
- Structure / Season / Volume / Part
- Edition
- Version
- language defaults
- audio/subtitle defaults
- quality/source defaults where appropriate

Child files inherit defaults unless explicitly overridden.

### File table

Use one row per media file.

Recommended columns:
- Datei
- erkannt
- finale Unit
- Qualität
- Sprache
- Audio
- Untertitel
- Status

For episodic media:
- finale Unit = Episode

For other media:
- Volume
- Chapter
- Part
- Track
- other canonical Structure unit

### Batch actions

Provide efficient batch actions:
- apply Work/Structure defaults
- apply language/audio/subtitle defaults
- assign sequential units by filename/order
- assign a unit range
- skip selected files
- mark extras/non-media
- reset selected overrides

### Sequence proposals

For files such as:
- `01.mkv`
- `02.mkv`
- `03.mkv`

Jularr may propose:
- E01
- E02
- E03

The proposal remains reviewable.

Do not treat physical order or filename numbering as canonical truth when confidence is insufficient.

### Missing fields

Unresolved required fields are visibly marked in the table.

The admin can continue reviewing other folders, but final execution is blocked while required mappings remain unresolved unless those items are explicitly ignored/skipped.

---

## Step 4 — Organisation

### Purpose

Decide what should physically happen to already discovered files after canonical mapping is known.

Provide clear mutually exclusive organization modes:

1. **Nur zuordnen**
   - keep current filenames and folders
   - save only canonical assignment

2. **Dateien umbenennen**
   - keep folder structure
   - rename media files using configured naming rules

3. **Ordner + Dateien umbenennen**
   - normalize names while preserving the same general hierarchy

4. **In kanonische Jularr-Struktur organisieren**
   - move/rename into the configured canonical layout for that LibraryRoot

### Naming preview

Show the active naming rule and an example output.

Do not require arbitrary path entry.

Destination derives from:
- LibraryRoot
- canonical Work/Structure/Edition/Version mapping
- naming/path policy

### Additional options

Where supported:
- remove empty old folders after successful move
- verify destination conflicts
- keep unknown/extras in place
- skip sidecar files
- include configured sidecars/artwork/subtitles

Potentially destructive operations must be explicit.

---

## Step 5 — Vorschau & Ausführen

### Purpose

Provide a complete dry-run before filesystem changes are applied.

### Summary

Show counts such as:
- assignments only
- renamed files
- moved files
- ignored files
- conflicts
- unresolved blockers

### Change table

Show a concise old -> new plan.

Recommended columns:
- Alt
- Neu
- Aktion
- Status

Examples:
- Assign only
- Rename
- Move + Rename
- Ignore
- Conflict

### Conflicts

Conflicts must be resolved before execution.

Examples:
- destination file already exists
- two source files map to the same canonical target
- target folder not writable
- storage root unavailable
- canonical mapping incomplete

### Execution

Primary action:
- `Änderungen ausführen`

Secondary safe action may exist:
- `Nur zuordnen (ohne Dateiangaben)` only when the current plan allows it and the semantics are clear

Execution runs as an operational job visible in Activity.

The wizard should display progress or hand off cleanly to Activity after starting.

---

## Folder inheritance

Settings assigned to a parent logical group may be inherited by children.

Inheritance must be visible and reversible.

A child file/folder may override:
- unit mapping
- language
- audio/subtitles
- quality/source classification
- version/edition where valid

Do not silently overwrite explicit child overrides when a parent value changes; prompt or mark inherited values distinctly.

## Recognition and confidence

Automatic recognition should expose enough evidence to understand why Jularr made a suggestion.

Possible evidence:
- folder name
- filename
- provider IDs
- embedded metadata
- sidecar metadata
- existing canonical assignments
- media probe results

Confidence is advisory.

It must not create canonical identity by itself.

## Existing assignments

Already mapped files/folders:
- may be skipped during scan
- may remain visible in the tree
- can be reopened for correction if the admin chooses

Do not silently remap already valid assignments because a new heuristic scores slightly higher.

## Destination / storage rules

The wizard operates only inside configured, permitted LibraryRoots.

No unrestricted filesystem browser.

If a file will move across filesystems or roots, the preview must make the operation explicit and use the configured safe move/copy semantics.

## Light / Dark

Both first-class.

Operational Admin visual language:
- compact
- table/tree oriented
- restrained
- no decorative hero art
- semantic state via icon/text/border, not large saturated backgrounds

## Platforms

### Desktop

Primary platform.

Use:
- full Admin page
- persistent wizard progress
- tree/table-oriented workflow
- large working area

### Tablet

Supported with adaptive single/two-pane layouts.

### Mobile

Not a primary bulk-management platform.

If supported:
- full-screen wizard
- one logical section at a time
- no squeezed desktop tree/table
- complex reconciliation may recommend continuing on Desktop without blocking access

### TV

Unsupported.

## Loading / Empty / Error / Partial states

Required:
- scan running
- scan complete
- nothing unresolved
- no files found
- partial recognition
- unresolved parent but recognized children
- recognized parent with unresolved children
- storage offline
- permission denied
- file disappeared during review
- conflict
- stale scan result
- save failure
- execution queued
- execution running
- execution partially failed
- forbidden

## Domain / architecture constraints

- Reconciliation maps existing files into `Work -> Structure -> Edition -> Version -> Asset/File -> Track`.
- Physical folder hierarchy is not canonical media identity.
- Folder/filename parsing is evidence only.
- Provider/native IDs remain provenance/evidence, not separate media ownership.
- Split/Merge changes the working reconciliation plan, not the core domain hierarchy itself.
- File-probed technical metadata and parsed release metadata remain distinct.
- Final execution creates/updates canonical Asset/File/Track links only after explicit mapping decisions.
- No separate Anime/Series/Movie reconciliation engines.

## Must not implement

- No modal-only flow for large library reconciliation.
- No reuse of the compact Download Assignment dialog as the main UI.
- No raw database-ID editing.
- No unrestricted server path browsing.
- No canonical identity derived from path or filename alone.
- No hidden recognized folders; collapse them instead.
- No irreversible automatic split/merge.
- No silent global parser learning from one manual mapping.
- No automatic destructive rename/move without dry-run preview.
- No automatic deletion of unknown files.
- No implementation that hides unresolved child files because the parent folder matched.
