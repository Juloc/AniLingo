# Admin Game Runtime Editor

Status: UX and visual direction approved for Desktop, Tablet and Mobile. The owner will upload the approved mockup image into this folder.

Parent page: `docs/mockups/admin-games-runtimes/SPEC.md`.
Shared Admin contract: `docs/mockups/admin-games/SPEC.md`.
Games architecture: #725.
UX planning: #729.
Browser runtime planning: #771.

## Purpose

Focused add/edit surface for one Game Runtime configuration.

The editor configures one supported runtime adapter instance without exposing emulator internals that Jularr can determine itself.

It is not:
- a generic emulator settings application;
- a shell-command editor;
- a BIOS manager;
- a Game LibraryRoot editor;
- a per-Game tuning surface.

## Add flow

Adding a runtime has at most two lightweight stages:

1. choose a supported runtime adapter;
2. configure/save that runtime.

This is not a numbered setup wizard and must not expand into a 1/2/3/4 onboarding flow.

On Desktop/Tablet the adapter may be chosen directly inside the add dialog.

On Mobile, adapter selection may be a dedicated first sheet because the list needs full-width touch targets.

## Adapter selection

Only show runtime adapters that Jularr explicitly supports.

Initial examples may include:
- EmulatorJS;
- DuckStation;
- RetroArch;
- Dolphin;
- PCSX2.

Each adapter choice shows:
- icon;
- display name;
- short user-facing type/description.

Do not offer:
- Custom command;
- arbitrary executable + argument templates;
- generic shell adapter.

Selecting an adapter opens its validated configuration schema.

## Shared fields

Every runtime editor may show:

### Name
User-facing display name.

Default to the adapter display name.

### Status
- Aktiviert / Deaktiviert

An enabled runtime must be valid enough to use.

A disabled runtime may be saved while incomplete so an Admin can prepare configuration and finish later.

### Runtime identity
Read-only adapter identity/type.

Examples:
- Browser Runtime;
- Lokal;
- Server;
- Extern.

Do not expose internal adapter class names or IDs.

## EmulatorJS

EmulatorJS is treated as an integrated Browser Runtime.

Expected editor behavior:
- no executable path;
- no endpoint field;
- no CDN/core selector in normal UI;
- no install wizard;
- no raw EmulatorJS option list.

Show:
- display name;
- enabled state;
- integrated/browser-runtime status;
- detected supported platforms;
- detected capabilities;
- access/isolation summary;
- test status.

The adapter/runtime version is Jularr-managed.

## Local runtime example: DuckStation

A local runtime may require installation discovery.

### Installation

Preferred modes:

- Automatisch erkennen
- Manuell auswählen

Automatic detection is the default.

When detected:
- show the resolved executable path read-only or semi-read-only;
- show detected runtime version;
- show health state.

Manual selection uses a safe file picker/browse action.

Do not expose a free-form shell command field.

### Detected information

After Test/validation, show:
- runtime version;
- supported platforms;
- detected capabilities;
- readiness.

These values come from the adapter/test result, not arbitrary Admin checkboxes.

## Supported platforms

Read-only.

Examples:

```text
GB · GBC · GBA · NDS · PS1 · +8
```

The runtime adapter determines supported platforms.

The Admin does not manually claim unsupported platforms.

Platform default assignment remains on:

`Admin -> Games -> Runtimes`

not inside this editor.

## Capabilities

Read-only capability summary.

Examples:
- Browser Play;
- Save States;
- Gamepad;
- Touch;
- Screenshots;
- Fullscreen;
- Disc-Wechsel;
- Lokaler Multiplayer.

Only show capabilities reported by the configured runtime/adapter.

Unavailable capabilities use a neutral unavailable state, not a red error unless something is actually broken.

Do not expose dozens of low-level flags.

## Access / isolation

Show a concise explicit access summary.

Possible rows:

```text
Game-Dateien       Session-basiert / Nur Lesen
Spielstände        Aktuelles Profil / Lesen + Schreiben
BIOS / Firmware    Nur wenn benötigt / Nur Lesen
Netzwerk           Nicht benötigt / benötigte Verbindung
Host-Geräte        Gamepads, wenn erforderlich
Datenbank          Kein Zugriff
```

The purpose is transparency, not a generic permission matrix.

Hard rules:
- no implicit Full Host Access;
- no direct PostgreSQL access;
- no Jularr configuration/secrets access;
- no unrelated library access;
- only resources required by the runtime are granted.

Browser/WASM runtimes should normally require no host filesystem access.

## BIOS/Firmware relationship

BIOS/Firmware is not configured here.

If Test reports a missing prerequisite:

```text
Einrichtung nötig
PS1 BIOS fehlt
```

show a deep link:

`BIOS / Firmware öffnen`

The editor must not embed BIOS upload/file management.

## Test

Every editor has a clear `Testen` action.

Test validates only relevant adapter/runtime configuration.

May verify:
- adapter initializes;
- executable/endpoint exists;
- runtime responds;
- version can be detected;
- supported platforms/capabilities can be read;
- required browser/host capability exists;
- required BIOS/Firmware dependency state is known.

It is not a performance benchmark.

### Success

Example:

```text
Bereit
DuckStation 0.1-7096 gefunden und funktionsfähig.
```

### Incomplete

Example:

```text
Einrichtung nötig
Programm nicht gefunden
```

or:

```text
Einrichtung nötig
PS1 BIOS fehlt
```

### Failure

Example:

```text
Fehler
Runtime nicht erreichbar
```

Technical details may deep-link to Activity/diagnostics when needed.

Do not dump terminal logs into the main editor.

## Validation and save behavior

### Enabled runtime

An enabled runtime can be saved only when the required configuration is valid enough for its intended use.

### Disabled runtime

A disabled runtime may be saved incomplete.

This allows staged Admin configuration without pretending the runtime is usable.

### Save actions

Desktop/Tablet:
- Abbrechen
- Speichern

Mobile:
- primary full-width or sticky `Speichern` where useful;
- Cancel/back uses standard sheet/page behavior.

Do not silently enable a runtime after a successful Test.

## Advanced

Optional collapsed section:

`Erweitert`

Only add settings when there is an approved real Jularr use case.

Do not pre-populate with emulator-tuning clutter.

Not V1:
- shader configuration;
- render backend;
- resolution scaling;
- frame skip;
- audio buffer tuning;
- arbitrary CLI arguments;
- raw emulator core selection;
- arbitrary environment variables.

Per-Game tuning is not owned here.

## Desktop — Add EmulatorJS

Approved visual composition:

- centered large modal over the Runtime overview;
- left configuration column;
- right status/capability/access summary;
- adapter selector;
- Name;
- Enabled toggle;
- integrated Browser Runtime summary;
- supported platform chips;
- capability summary;
- access summary;
- Test block;
- footer with Abbrechen / Speichern.

The modal uses standard light Jularr Admin surfaces and restrained purple accent.

## Desktop — Edit local runtime

Approved DuckStation composition:

Left/configuration side:
- runtime adapter;
- Name;
- Enabled;
- installation mode;
- detected/manual executable;
- version/status.

Right/summary side:
- runtime identity;
- platforms;
- capabilities;
- access;
- Test.

Advanced remains collapsed at the bottom.

Do not turn the modal into a multi-tab settings application.

## Tablet

Approved direction:
- same information model;
- large centered dialog;
- compact section navigation may be used when it improves available width:
  - Allgemein;
  - Plattformen;
  - Funktionen;
  - Zugriff;
  - Test;
  - Erweitert.

This is navigation inside one editor, not a wizard.

The current section can use the main content pane.

Do not require sequential completion.

## Mobile — Add runtime

Adapter selection uses a full-width sheet/page.

Show supported adapters as large rows:

```text
EmulatorJS
Browser Runtime

DuckStation
PlayStation 1 Emulator

RetroArch
Multi-System Emulator

Dolphin
GameCube / Wii Emulator

PCSX2
PlayStation 2 Emulator
```

One selected adapter at a time.

Primary action:
- Weiter

`Weiter` opens the actual editor.

This is the only lightweight transition in the add flow, not a numbered onboarding process.

## Mobile — Edit EmulatorJS

Use a vertical single-column surface.

Order:
1. runtime identity/status;
2. Name;
3. Enabled;
4. supported platforms;
5. capabilities;
6. access summary when useful;
7. Test;
8. Save.

No desktop table layout.

## Mobile — Edit local runtime

Order:
1. runtime identity/status;
2. Name;
3. Enabled;
4. installation mode;
5. executable selection;
6. detected version/health;
7. capabilities/access as compact sections;
8. Test;
9. Save.

Use large touch targets.

No tiny nested menus.

## Required states

- add adapter selection;
- integrated runtime ready;
- local runtime auto-detected;
- manual executable selection;
- ready;
- setup required;
- test running;
- test failed;
- missing BIOS/Firmware;
- disabled incomplete runtime;
- enabled invalid runtime;
- save validation error;
- permission denied;
- backend unavailable.

A failed runtime test must not lose entered configuration.

## Read/edit model

Conceptually:

```text
RuntimeEditor
- RuntimeId?
- AdapterKey
- AdapterDisplayName
- RuntimeType
- DisplayName
- Enabled
- ConfigurationSchema
- ConfigurationValues
- DetectedVersion?
- SupportedPlatforms[]
- Capabilities[]
- AccessSummary[]
- HealthState
- HealthMessage?
- LastTestedAt?
- CanSave
- ValidationIssues[]
```

Adapter-specific configuration is validated through typed/declared adapter schema.

Do not make the UI depend on one giant untyped settings dictionary.

## Navigation boundaries

This editor may deep-link to:
- BIOS / Firmware;
- Activity/diagnostics.

It does not embed:
- BIOS management;
- Storage root configuration;
- Game library;
- downloader;
- controller mappings;
- Save States.

## Explicitly not in this editor

- per-platform default runtime assignment;
- per-Game runtime preference;
- BIOS upload;
- LibraryRoot;
- ROM paths;
- Game search;
- controller mapping;
- game launch controls;
- performance charts;
- emulator benchmark;
- raw core selection;
- shell commands;
- arbitrary command-line arguments;
- shader/render tuning.

## Visual baseline

The approved mockup covers:
- Desktop Add Runtime with EmulatorJS;
- Desktop Edit Runtime with DuckStation;
- Mobile adapter selection;
- Mobile Edit EmulatorJS;
- Mobile Edit DuckStation;
- Tablet Edit DuckStation.

The owner will upload the approved image into this folder.

The mockup defines visual direction; this text spec defines behavior/data/boundaries and wins on conflict.
