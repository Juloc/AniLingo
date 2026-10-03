# Admin Games Runtimes

Status: UX and visual direction approved for Desktop, Tablet and Mobile. The owner will upload the approved mockup image into this folder.

Shared Admin contract: `docs/mockups/admin-games/SPEC.md`.
Games architecture: #725.
UX planning: #729.
Browser runtime planning: #771.

## Purpose

Admin surface for configuring and inspecting the runtime adapters Jularr can use to launch Games.

The page answers only:

- which runtimes exist;
- whether they are enabled and healthy;
- which platforms they support;
- which runtime is preferred/default for each platform.

It is not an emulator dashboard and not a BIOS, library, downloader or performance page.

## Information architecture

The page has exactly two primary sections:

1. `Game Runtimes`
2. `Standard-Runtime pro Plattform`

Do not add statistic cards, FPS charts, CPU/RAM charts or game-library summaries above them.

## Header

Title:
- `Game Runtimes`

Secondary text:
- concise explanation that these runtimes are used to start Games.

Primary actions:
- `Alle testen`
- `Runtime hinzufügen`

`Alle testen` is secondary.
`Runtime hinzufügen` is primary.

No BIOS upload or Game import action in the header.

## Runtime list

### Desktop

Use the standard Jularr Admin table language.

Columns:

- Runtime
- Typ
- Plattformen
- Status
- Standard für
- Aktionen

Do not add more columns unless a real Admin need appears.

### Runtime cell

Show:

```text
[Icon] EmulatorJS
       Browser Runtime
```

or equivalent for another runtime.

Allowed secondary text:
- user-facing runtime category/description.

Do not show:
- implementation class names;
- internal adapter IDs;
- executable paths;
- container IDs;
- raw command lines.

### Type

Use only user-facing runtime types:

- Browser
- Lokal
- Server
- Extern

Examples:
- EmulatorJS -> Browser
- DuckStation on host -> Lokal
- remote worker -> Server

Do not expose implementation transport details here.

### Platforms

Show compact platform chips.

Example:

```text
GB  GBC  GBA  NDS  PS1  +8
```

Rules:
- show only a few chips inline;
- collapse remaining platforms behind `+N`;
- full list may appear in tooltip/popover/details;
- do not create dozens of permanent badges.

### Status

Supported top-level states:

- Bereit
- Einrichtung nötig
- Fehler
- Deaktiviert
- Nicht verfügbar

Optional secondary reason:

```text
Bereit
Zuletzt getestet vor 12 Min.
```

or:

```text
Einrichtung nötig
BIOS fehlt (PS1, NDS)
```

or:

```text
Fehler
Abhängigkeit fehlt
```

Status wording must be actionable and concise.

### Standard für

Show only the current platform assignments.

Example:

```text
GB · GBC · GBA · NDS
```

or:

```text
PS1
```

or:

```text
—
```

Do not place multiple runtime dropdowns directly inside each runtime row.

### Actions

Desktop/tablet may show:
- `Testen`
- `…`

Runtime action menu:

```text
Bearbeiten
Testen
Deaktivieren / Aktivieren
────────
Entfernen
```

Delete/remove is destructive and uses normal Jularr confirmation behavior.

Do not place configuration forms inline in the table.

## Runtime test

`Testen` is a capability/configuration health test, not a benchmark.

Test may verify:

- configuration valid;
- adapter initializes;
- runtime reachable/available;
- required host/browser capability available;
- runtime reports supported platforms/capabilities;
- required executable/endpoint available where applicable.

It does not measure:
- FPS;
- CPU;
- RAM;
- latency benchmark;
- game-specific performance score.

### Test result

Use concise states.

Examples:

```text
Bereit
```

```text
Einrichtung nötig
PS1 BIOS fehlt
```

```text
Fehler
Runtime nicht erreichbar
```

`Alle testen` runs the same normal tests across enabled/configured runtimes and shows a compact summary.

Example:

```text
3 bereit
1 Einrichtung nötig
1 deaktiviert
```

## BIOS relationship

This page may surface BIOS/Firmware problems as status reasons.

Example:

```text
EmulatorJS
Einrichtung nötig
BIOS fehlt (PS1, NDS)
```

Authorized Admin may follow a deep link to:

`Admin -> Games -> BIOS / Firmware`

Do not:
- upload BIOS here;
- list BIOS files here;
- show BIOS filesystem paths here.

## Standard-Runtime pro Plattform

Second major page section.

Purpose:
define which valid runtime Jularr should prefer per GamePlatform.

### Desktop/tablet columns

- Plattform
- Standard Runtime
- Weitere verfügbar
- Status
- Aktionen/detail affordance

### Platform row

Example:

```text
PlayStation 1
Standard: DuckStation
Weitere verfügbar: EmulatorJS
Status: Bereit
```

### Automatic resolution

If exactly one valid runtime is available:

```text
Game Boy
Standard: EmulatorJS
```

The Admin does not need to manually configure an obvious one-runtime case.

Implementation may display this as:
- selected runtime;
- or `Automatisch -> EmulatorJS`.

Do not require unnecessary setup.

### Multiple valid runtimes

When multiple valid runtimes exist, the Admin may choose the preferred/default runtime.

Example:

```text
PlayStation 1
[ DuckStation ▾ ]
Weitere verfügbar: EmulatorJS
```

Changing this assignment affects platform-level default resolution only.

It does not mutate per-Game remembered choices from Play Options.

### No runtime

Example:

```text
GameCube
Keine Runtime
Nicht verfügbar
[ Runtime hinzufügen ]
```

Do not create fake fallback behavior.

### Failed preferred runtime

If the preferred/default runtime becomes unavailable:

```text
PlayStation 1
DuckStation
Standard-Runtime nicht verfügbar
EmulatorJS verfügbar
```

V1 must not silently change a deliberate Admin default to another runtime.

Provide a clear `Standard ändern` path.

Automatic fallback can be a later explicit policy if required.

## Runtime selection rules

The Games application layer owns compatibility.

The Admin page consumes already-normalized runtime/platform compatibility.

The UI does not calculate whether EmulatorJS, DuckStation, RetroArch, Dolphin, PCSX2 or another runtime supports a platform.

Conceptually:

```text
RuntimeSummary
- RuntimeId
- DisplayName
- RuntimeType
- Enabled
- SupportedPlatforms[]
- Capabilities[]
- HealthState
- HealthMessage?
- LastTestedAt?
- DefaultForPlatforms[]

PlatformRuntimeAssignment
- PlatformId
- PlatformName
- PreferredRuntimeId?
- AvailableRuntimeIds[]
- ResolutionState
```

Do not bind Admin UI directly to EmulatorJS-specific config objects.

## EmulatorJS row

For the integrated/browser runtime, the list may show:

```text
EmulatorJS
Browser
GB · GBC · GBA · NDS · PS1 · +N
Bereit
Standard für GB · GBC · GBA · NDS
```

Do not show the core list here:

- mgba;
- gambatte;
- melonDS;
- pcsx_rearmed;
- etc.

Core/runtime-engine choice stays inside the runtime adapter unless a later approved Admin requirement introduces a real advanced override.

## Desktop visual composition

Approved direction:

- normal light Jularr Admin shell;
- left Admin navigation;
- top search/account shell consistent with existing Admin pages;
- `Game Runtimes` table first;
- `Standard-Runtime pro Plattform` table second;
- restrained purple accent;
- normal status colors;
- no gaming-themed decorative background;
- no dashboard statistic cards.

The page should visually match existing Admin Activity/To-Do and related Jularr Admin surfaces.

## Tablet visual composition

Use the same two-section hierarchy.

Adjustments:
- fewer platform chips inline;
- actions collapse primarily into `…`;
- no horizontal-scroll-heavy table;
- platform/default rows remain readable at tablet width.

Do not transform Tablet into a totally different product.

## Mobile — runtime list

Use cards instead of the Desktop table.

Each runtime card may show:

```text
EmulatorJS                         …
Browser Runtime

GB · GBC · GBA · +3

● Bereit

Standard
GB · GBC · GBA · NDS
```

Top actions:
- `Runtime hinzufügen`
- `Alle testen`

Runtime card action opens a bottom sheet.

## Mobile — platform defaults

Use a separate compact mobile view/section:

```text
Standard-Runtime pro Plattform

Game Boy          EmulatorJS       Bereit >
Game Boy Color    EmulatorJS       Bereit >
Nintendo DS       EmulatorJS       Bereit >
PlayStation 1     DuckStation      Bereit >
GameCube          Keine Runtime    !      >
```

Selecting a row opens the platform-default chooser/details.

Do not squeeze Desktop dropdown tables into mobile.

## Mobile runtime action sheet

Approved actions:

- Testen
- Bearbeiten
- Deaktivieren / Aktivieren
- Entfernen

Header shows:
- runtime icon;
- display name;
- type;
- current status.

Destructive `Entfernen` is visually separated.

## Desktop/tablet runtime action menu

Compact popover:

- Bearbeiten
- Testen
- Deaktivieren / Aktivieren
- Entfernen

Same semantics as Mobile.

## Empty state

When no runtimes are configured:

```text
Keine Game Runtime konfiguriert

Füge eine Runtime hinzu, damit Jularr unterstützte
Spiele direkt starten kann.

[ Runtime hinzufügen ]
```

If Jularr ships with a usable integrated EmulatorJS runtime, this state may rarely occur. The UI must still support it correctly.

## Loading and error states

Required:
- initial loading;
- partial runtime health unavailable;
- platform assignments still available while one runtime test fails;
- no runtimes;
- no platforms requiring assignment;
- permission denied;
- backend unavailable.

A failure to test one runtime must not blank the entire page.

## Navigation boundaries

This page may deep-link to:

- Runtime Editor
- BIOS / Firmware
- Activity / To-Do for correlated failures

It does not embed those workflows inline.

## Explicitly not on this page

Do not add:

- BIOS file management;
- emulator binary installation wizard;
- ROM/game-library browsing;
- Games LibraryRoot configuration;
- controller mappings;
- Save States;
- game launch controls;
- FPS/CPU/RAM charts;
- game-specific compatibility database;
- downloader queue;
- per-game runtime tuning;
- shaders;
- raw emulator cores;
- raw startup arguments;
- arbitrary shell commands.

## Visual baseline

The approved mockup covers:

- Desktop Runtime table;
- Desktop platform-default table;
- Tablet layout;
- Mobile runtime list;
- Mobile platform-default list;
- Mobile runtime action bottom sheet;
- Desktop/Tablet runtime action menu;
- Empty State.

The owner will upload the approved image into this folder.

The mockup defines visual direction; this text spec defines behavior/data/boundaries and wins on conflict.
