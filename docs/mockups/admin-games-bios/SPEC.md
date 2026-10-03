# Admin Games BIOS & Firmware

Status: UX and visual direction approved for Desktop, Tablet and Mobile. The owner will upload the approved mockup image into this folder.

Shared Admin contract: `docs/mockups/admin-games/SPEC.md`.
Runtime overview: `docs/mockups/admin-games-runtimes/SPEC.md`.
Runtime editor: `docs/mockups/admin-games-runtime-editor/SPEC.md`.
Games architecture: #725.
UX planning: #729.

## Purpose

Admin surface for inspecting and supplying BIOS/Firmware requirements used by configured Game Runtimes.

The page answers:
- which BIOS/Firmware requirements exist;
- which platform needs them;
- which runtimes consume them;
- which artifacts are present;
- which are missing, invalid, optional or ready.

It is not a file manager, ROM library, downloader or emulator settings page.

## Core model

A BIOS/Firmware requirement is not the uploaded file itself.

Conceptually:

```text
FirmwareRequirement
- RequirementId
- PlatformId
- DisplayName
- RequirementType: Required | Optional
- Region?
- ExpectedChecksums[]
- ExpectedSize?
- RequiredByRuntimeIds[]
- State
- CurrentArtifactId?
- ValidationMessage?

FirmwareArtifact
- ArtifactId
- DisplayName
- Size
- Checksum
- ImportedAt
- ValidationState
```

This allows Jularr to represent "Nintendo DS Firmware is required" before any file exists.

## Information architecture

Desktop/Tablet:
1. page header;
2. status filters;
3. platform/runtime filters;
4. grouped platform sections;
5. one row per requirement.

Mobile:
1. page header;
2. compact status filters;
3. platform summary list;
4. platform detail view;
5. add/replace flow or action sheet as separate surfaces.

## Header

Title:
- `BIOS & Firmware`

Secondary text:
- `Verwalte erforderliche Systemdateien für Game-Runtimes.`

Actions:
- `Alle prüfen`
- `Datei hinzufügen`

`Alle prüfen` is secondary.
`Datei hinzufügen` is primary.

Do not add:
- search/indexer actions;
- automatic BIOS download;
- ROM import;
- runtime installation.

## Status filters

Approved compact filters:
- Alle
- Fehlend
- Ungültig
- Bereit
- Optional

Show counts where available.

These are requirement-state filters, not artifact/file filters.

## Additional filters

Compact selectors:
- Plattformen
- Runtimes

Do not add more filters until a real need appears.

Global text search is not required for the first scope.

## Grouping

Primary grouping is by GamePlatform.

Examples:
- PlayStation 1
- Nintendo DS
- Game Boy Advance

This is easier for Admins than grouping primarily by runtime.

Each platform header may show readiness summary:

```text
PlayStation 1
3/3 bereit
```

```text
Nintendo DS
2/3 bereit
```

```text
Game Boy Advance
1/2 bereit
```

## Requirement rows

Desktop/Tablet columns may include:

- Name
- Region
- Benötigt von
- Status
- Dateigröße
- SHA-1 (gekürzt)
- Aktionen

Tablet may omit checksum and reduce secondary columns.

### Name

Use the requirement's stable user-facing identity.

Examples:
- SCPH-5500
- SCPH-5501
- SCPH-5502
- BIOS7
- BIOS9
- Firmware

Do not use database IDs.

### Region

Show only where meaningful.

Examples:
- Japan
- USA
- Europa
- —

### Benötigt von

Show runtimes using the requirement.

Example:

```text
EmulatorJS · DuckStation
```

If multiple runtimes can share the same BIOS artifact, the requirement is shown once rather than duplicated per runtime.

### Status

Approved states:

- Bereit
- Fehlt
- Ungültig
- Nicht geprüft
- Optional

Meaning:

#### Bereit
Artifact exists and passed current validation.

#### Fehlt
Required artifact does not exist.

#### Ungültig
Artifact exists but failed validation.

Examples:
- checksum mismatch;
- wrong size;
- unreadable;
- incompatible artifact.

#### Nicht geprüft
Artifact exists but current validation has not yet completed or is stale.

#### Optional
Requirement is optional and its absence does not make the runtime/platform unhealthy.

Optional must not use red/error styling.

### Validation message

Show concise actionable text when needed.

Examples:

```text
Ungültig
SHA-1 stimmt nicht überein
```

```text
Fehlt
Für PS1 USA benötigt
```

Do not use vague messages such as `Problem erkannt`.

## Checksum presentation

Show checksum only when the runtime definition provides an expected checksum and validation benefits from exposing it.

Desktop may show a shortened SHA-1 in the table.

Full checksum belongs in details/add/replace validation, not the dense table.

Do not expose internal artifact paths.

## File size

Show known artifact size when useful.

Examples:
- 512 KB
- 16 KB

Missing artifacts show `—`.

Size is informational; checksum/runtime validation remains authoritative.

## Actions

### Missing required requirement

Primary inline action:
- `Datei hinzufügen`

### Missing optional requirement

May show:
- `Datei hinzufügen`

but remains neutral/optional.

### Existing valid artifact

Action menu:
- Prüfen
- Ersetzen
- Details anzeigen
- Entfernen

### Invalid artifact

Prefer direct `Ersetzen` affordance plus action menu.

Do not silently delete or replace an invalid artifact.

### Remove

Destructive and requires normal Jularr confirmation.

Removing a required artifact may make affected runtimes/platforms unready; confirmation should explain this impact.

## Action menu

Desktop/Tablet:
- compact `…` popover.

Mobile:
- bottom sheet.

Approved Mobile menu content:
- Prüfen
- Ersetzen
- Entfernen
- Details anzeigen

Destructive `Entfernen` is visually separated.

## Alle prüfen

Runs validation for known BIOS/Firmware artifacts and missing requirements.

May verify:
- artifact present;
- readable;
- expected size if defined;
- checksum if defined;
- requirement compatibility;
- configured runtimes can resolve the requirement.

It is not:
- an emulator benchmark;
- an internet lookup;
- a BIOS downloader.

Summary example:

```text
7 bereit
2 fehlen
1 ungültig
```

A failure validating one artifact must not blank the page.

## Runtime relationship

The page consumes BIOS/Firmware requirements declared by configured/supported runtime adapters.

Examples:
- EmulatorJS needs Nintendo DS BIOS7/BIOS9/Firmware for a selected DS runtime path;
- DuckStation may use PlayStation BIOS requirements.

The BIOS page does not decide which emulator core is used.

Runtime Editor may deep-link to a missing requirement here.

This page may deep-link back to the related Runtime only when useful.

## Storage relationship

BIOS/Firmware uses restricted Jularr-managed storage.

Rules:
- separate from Games LibraryRoot;
- no arbitrary host path text entry;
- no unrestricted filesystem browser;
- runtime gets only BIOS artifacts explicitly required;
- normal consumer users never browse this storage.

Storage configuration itself remains owned by Admin Storage.

If needed, show a small informational deep link:
- `In Storage öffnen`

Do not add raw path editing here.

## Desktop visual composition

Approved visual direction:

- standard light Jularr Admin shell;
- Admin -> Games -> BIOS / Firmware selected;
- header actions on the right;
- compact filter row;
- platform-grouped cards/tables;
- light neutral surfaces;
- restrained purple accent;
- normal semantic status colors.

Platform blocks use clear headers and readiness counts.

No gaming-themed decorative backgrounds.

## Tablet

Use the same platform-grouped structure.

Adjustments:
- fewer columns;
- checksum may be omitted from dense table;
- actions primarily use `…`;
- filters remain compact;
- no horizontal-scroll-heavy layout.

## Mobile — list

Mobile first view is platform summary, not a squeezed table.

Example:

```text
PlayStation 1       3/3 bereit >
Nintendo DS         2/3 bereit >
Game Boy Advance    1/2 bereit >
Nintendo 3DS        0/2 fehlen >
Sega Mega Drive     1/1 bereit >
```

Top:
- compact status filters;
- `Datei hinzufügen` primary action.

Selecting a platform opens its requirement detail list.

## Mobile — platform detail

Example Nintendo DS:

```text
Nintendo DS
2/3 bereit

BIOS7
Bereit
EmulatorJS
16 KB

BIOS9
Bereit
EmulatorJS
16 KB

Firmware
Fehlt
EmulatorJS
[ Datei hinzufügen ]
```

Include a small informational card when useful:

```text
Diese Dateien werden für die Nutzung von Nintendo DS
Spielen mit EmulatorJS benötigt.
```

Do not include raw BIOS paths.

## Mobile — add entry point

Selecting `Datei hinzufügen` for a known missing requirement opens the focused BIOS/Firmware Add/Replace surface with that requirement preselected.

Global `Datei hinzufügen` may first ask which requirement the Admin wants to satisfy if multiple missing/optional requirements exist.

Do not accept arbitrary unclassified files without a target requirement in V1.

## Empty states

### No requirements

```text
Keine BIOS- oder Firmware-Dateien erforderlich

Für deine aktuell konfigurierten Game-Runtimes
werden keine zusätzlichen Systemdateien benötigt.
```

### Missing requirements exist

Do not use a generic empty state.

Show the platform groups and missing rows so the Admin can act directly.

### No configured runtimes

May show:

```text
Keine Game Runtime konfiguriert

Konfiguriere zuerst eine Runtime, damit Jularr
deren BIOS-/Firmware-Anforderungen ermitteln kann.

[ Runtimes öffnen ]
```

## Loading/error states

Required:
- initial loading;
- validation running;
- partial validation failure;
- missing requirement;
- invalid artifact;
- optional requirement;
- backend unavailable;
- permission denied.

Platform data that loaded successfully remains visible when another platform validation fails.

## Security

Hard rules:
- never automatically search the internet for BIOS/Firmware;
- never automatically download BIOS/Firmware;
- never expose BIOS artifacts to normal users;
- no arbitrary host path text input;
- no broad runtime access to the BIOS storage;
- checksum validation is performed server-side where applicable;
- uploaded files are treated as data, never executed.

## Navigation boundaries

This page may deep-link to:
- BIOS/Firmware Add or Replace;
- Runtime overview/editor;
- Storage;
- Activity/To-Do for validation errors.

It does not embed those other workflows.

## Explicitly not on this page

Do not add:
- ROM/game management;
- Game Library;
- runtime executable configuration;
- emulator settings;
- provider/indexer settings;
- Usenet/download controls;
- automatic BIOS acquisition;
- web BIOS search;
- arbitrary filesystem browser;
- per-Game BIOS selector;
- controller settings;
- Save States;
- performance telemetry.

## Visual baseline

The approved mockup covers:
- Desktop BIOS/Firmware overview;
- Tablet overview;
- status filters;
- platform grouping;
- readiness counts;
- ready/missing/optional states;
- Mobile platform list;
- Mobile Nintendo DS detail;
- Mobile Add Firmware entry flow;
- Mobile action menu.

The owner will upload the approved mockup image into this folder.

The mockup defines visual direction; this text spec defines behavior/data/boundaries and wins on conflict.
