# Admin Games BIOS/Firmware Add or Replace

Status: UX and visual direction approved for Desktop, Tablet and Mobile. The owner will upload the approved mockup image into this folder.

Parent page: `docs/mockups/admin-games-bios/SPEC.md`.
Shared Admin contract: `docs/mockups/admin-games/SPEC.md`.
Games architecture: #725.
UX planning: #729.

## Purpose

Focused Admin surface for satisfying one known BIOS/Firmware requirement or replacing its current artifact.

The flow must be small and safe:

```text
Requirement
 -> select file
 -> validate
 -> upload/store
 -> requirement becomes ready
```

Replacement:

```text
Existing valid artifact
 -> select new file
 -> validate new file
 -> store successfully
 -> atomically replace current artifact
```

The old valid artifact remains active until the replacement has been fully uploaded and validated.

## Entry points

### Known requirement

Preferred path.

Examples:
- Nintendo DS Firmware row -> `Datei hinzufügen`
- SCPH-5501 row -> `Ersetzen`
- Runtime Editor missing-BIOS state -> deep link to this requirement

The requirement is already selected and the user does not choose it again.

### Global `Datei hinzufügen`

If opened without a requirement, first show a compact requirement chooser.

Only known unsatisfied or optional requirements are selectable.

Do not accept arbitrary unclassified BIOS files in V1.

## Requirement chooser

Mobile may use a dedicated first sheet/page.

Each row shows:
- platform;
- requirement display name;
- Required / Optional;
- current state where relevant.

Example:

```text
Nintendo DS Firmware
Nintendo DS · Erforderlich

PlayStation 1 BIOS (SCPH-5501)
PlayStation 1 · Erforderlich

Game Boy Advance BIOS
Game Boy Advance · Optional
```

Primary action:
- `Weiter`

This is a lightweight target selection, not a numbered setup wizard.

Desktop/Tablet may use a compact select inside the dialog when opened globally.

## Requirement summary

Once a requirement is selected, show:

- platform icon/name;
- requirement display name;
- Required / Optional;
- short explanation;
- expected size if known;
- expected checksum(s) if known;
- runtime(s) that consume it.

Example:

```text
Nintendo DS
Nintendo DS Firmware
Erforderlich

Erwartete Größe
~256 KB

Erwartete Prüfsumme (SHA-1)
6a7b3c9e...
```

Do not show:
- internal requirement ID;
- database ID;
- storage path;
- runtime executable path.

## File selection

Primary dropzone / picker:

```text
Datei auswählen
oder hier ablegen
```

Desktop:
- click picker;
- drag/drop.

Mobile:
- native safe file picker;
- no reliance on drag/drop.

Do not provide:
- arbitrary host path text field;
- unrestricted server filesystem browser;
- internet URL input;
- search/download button.

## Selected file

After selection show:

- filename;
- size;
- `Andere Datei`.

The selected file is staged for validation.

Do not immediately replace the existing artifact.

## Validation

Validation occurs before final activation.

Checks may include:
- readable upload;
- expected size where known;
- cryptographic checksum where known;
- requirement-specific file format/signature where available;
- compatibility with the requirement/runtime definition.

Server-side validation is authoritative.

The UI may compute a client-side checksum for responsiveness, but final acceptance depends on server validation.

## Valid file state

Approved presentation:

```text
firmware.bin
256 KB

✓ Prüfsumme stimmt
✓ Dateityp gültig
✓ Kompatibel mit EmulatorJS
```

Primary action becomes enabled:
- `Hochladen`
- or `Ersetzen`

## Invalid file state

For a required requirement with a known authoritative checksum mismatch:

```text
Prüfsumme stimmt nicht

Erwartet (SHA-1)
6a7b3c...

Gefunden (SHA-1)
91ff28...
```

Primary upload/replace remains disabled.

Action:
- `Andere Datei`

Do not provide:
- Trotzdem verwenden;
- Ignore checksum;
- Force install.

If the requirement permits multiple known checksums, any accepted checksum is valid.

If checksum is unavailable and validation is heuristic, the requirement definition must explicitly decide whether warning-only acceptance is allowed. The UI does not invent bypass behavior.

## Upload / activation

### Add

On `Hochladen`:
1. upload/stage artifact;
2. validate server-side;
3. store in restricted BIOS/Firmware storage;
4. bind artifact to requirement;
5. mark requirement ready;
6. refresh affected runtime capability/health.

If any step fails, the requirement remains missing/unchanged.

### Replace

On `Ersetzen`:
1. existing artifact stays active;
2. upload/stage new artifact;
3. validate new artifact;
4. persist new artifact successfully;
5. atomically switch requirement to new artifact;
6. remove/retain old artifact according to retention policy.

A failed replacement must never destroy the currently valid artifact.

## Replace presentation

Show two clear sections:

### Aktuelle Datei

- filename;
- size;
- ready state;
- current checksum.

### Neue Datei

- filename;
- size;
- validation result.

Information callout:

`Die vorhandene Datei wird erst nach erfolgreichem Upload und erfolgreicher Validierung ersetzt.`

Primary action:
- `Ersetzen`

## Optional requirements

Optional BIOS/Firmware uses neutral styling.

Missing optional artifact does not block runtime readiness unless the runtime specifically declares otherwise.

The add flow is otherwise identical.

## Runtime compatibility

The requirement definition determines which runtimes consume the artifact.

Show a concise consumer-facing line such as:

```text
Unterstützt von
EmulatorJS · Nintendo DS
```

Do not let the Admin manually attach one BIOS artifact to arbitrary unrelated runtimes.

## Desktop visual composition

Approved states:
- Add requirement with empty dropzone;
- valid selected file;
- invalid selected file.

Use:
- centered modal over the BIOS/Firmware page;
- standard light Jularr Admin surfaces;
- concise requirement summary at top;
- expected size/checksum card;
- large file picker/dropzone;
- validation card after selection;
- footer actions.

No multi-step wizard chrome.

## Tablet

Approved Replace composition:
- centered dialog;
- current artifact section;
- new artifact section;
- validation state;
- replacement safety information;
- footer with Abbrechen / Ersetzen.

Tablet may use the same Add dialog structure as Desktop at narrower width.

## Mobile — requirement chooser

Use a full-width sheet/page.

Show requirement rows with large touch targets.

Primary action:
- `Weiter`

No tiny dropdowns.

## Mobile — add file

Vertical single-column surface:

1. requirement summary;
2. expected size/checksum;
3. file picker/dropzone-style button;
4. runtime/compatibility summary;
5. validation result after selection;
6. bottom actions.

The mockup's dropzone represents the file selection affordance; native mobile implementation may use a normal large file-picker button where drag/drop is not meaningful.

## Mobile — valid file

Show:
- selected file;
- checksum valid;
- file type valid;
- runtime compatibility valid;
- enabled `Hochladen`.

## Mobile — invalid file

Show:
- selected file;
- red validation card;
- expected checksum;
- found checksum;
- disabled `Hochladen`;
- `Andere Datei` action.

Do not allow bypass.

## Loading / progress

During upload:
- disable duplicate submission;
- show upload progress where meaningful;
- keep requirement identity visible;
- Cancel only when cancellation is safe.

After upload, show validation state separately if validation continues server-side.

Do not claim success before storage + validation + binding complete.

## Success

On success:
- close/return to BIOS/Firmware page;
- refresh affected requirement/platform readiness;
- optionally show a concise success toast.

Do not require a separate success page.

## Error handling

Distinguish:
- upload failed;
- file unreadable;
- size mismatch;
- checksum mismatch;
- incompatible file;
- storage unavailable;
- validation service/runtime definition error.

Entered/selected requirement context remains intact after recoverable failure.

## Security

Hard rules:
- no automatic internet acquisition;
- no web BIOS search;
- no URL import in V1;
- no arbitrary host filesystem path;
- uploaded file treated as data, never executed;
- storage restricted to BIOS/Firmware area;
- runtime access granted only to explicitly required artifact;
- normal users cannot access this flow;
- filename is not trusted as validation evidence.

## Read/edit model

Conceptually:

```text
FirmwareUploadContext
- RequirementId
- Platform
- DisplayName
- RequirementType
- ExpectedSize?
- ExpectedChecksums[]
- RequiredByRuntimes[]
- ExistingArtifact?

SelectedArtifactCandidate
- ClientFilename
- Size
- ComputedChecksums[]
- ValidationState
- ValidationIssues[]
- CanCommit
```

The client never decides requirement compatibility from filename alone.

## Explicitly not in this flow

- ROM upload;
- Game import;
- runtime executable configuration;
- emulator settings;
- BIOS download/search;
- provider/indexer integration;
- raw storage paths;
- free-form checksum override;
- force-accept invalid required file;
- arbitrary runtime assignment.

## Visual baseline

The approved mockup covers:
- Desktop Add Firmware initial state;
- Desktop valid selected file;
- Desktop invalid selected file;
- Mobile requirement chooser;
- Mobile Add file;
- Mobile valid file;
- Mobile invalid file;
- Tablet Replace flow.

The owner will upload the approved image into this folder.

The mockup defines visual direction; this text spec defines behavior/data/boundaries and wins on conflict.
