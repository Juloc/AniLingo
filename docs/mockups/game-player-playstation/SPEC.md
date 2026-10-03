# PlayStation 1 Player

Status: platform-specific player planning. Visual mockups pending approval.

Parent player: `docs/mockups/game-player/SPEC.md`.
Touch controls: `docs/mockups/game-touch-controls/SPEC.md`.
Games architecture: #725.
UX planning: #729.
Browser runtime: #771.

## Purpose

Define PlayStation 1-specific behavior of the shared Jularr Game Player.

This is not a separate player implementation.

The shared player still owns:
- session lifecycle;
- save synchronization;
- Save States;
- screenshots;
- fullscreen;
- pause/exit;
- responsive shell;
- physical-controller detection;
- generic touch-control editor.

PS1 adds:
- PS1 control profiles;
- 4:3 display defaults;
- region/BIOS compatibility;
- disc-set awareness;
- optional multi-disc swap when the runtime exposes a stable capability;
- analog/DualShock-style input where supported.

## Visual rule

Use the normal modern Jularr player UI.

Hard rule:
- no fake PlayStation console shell;
- no fake CRT/TV casing;
- no DualShock-shaped page container;
- no decorative hardware bezel.

The game frame is a simple Jularr content surface.

Clean theme follows the light Jularr visual language: white/light neutrals, navy text, restrained purple accent, subtle borders/shadows.

Original Jularr uses the established warm watercolor/cherry-blossom skin and red accent over the same structure.

## EmulatorJS capability baseline

Current EmulatorJS PlayStation support is exposed under `psx`.

Documented cores:
- `pcsx_rearmed`;
- `mednafen_psx_hw`.

`psx` resolves to `pcsx_rearmed` by default.

The runtime/core choice remains adapter-owned.

Jularr must not expose core IDs in normal consumer UI.

## BIOS / region

PlayStation BIOS is a runtime prerequisite.

Jularr Games/Runtime configuration maps the GameRelease region to an available compatible BIOS.

Consumer UI only receives:
- playable;
- incompatible/missing BIOS;
- unsupported runtime/browser.

Do not expose BIOS filenames/paths in consumer Player.

Admin Games BIOS/Firmware owns remediation.

## Display

Default PlayStation presentation:
- preserve runtime-provided aspect ratio;
- prefer 4:3 presentation for normal PS1 titles;
- never stretch to full viewport;
- allow integer/pixel-like scaling only when it produces sensible output;
- otherwise use sharp aspect-correct scaling;
- neutral Jularr background around unused space.

Optional display actions may include:
- Fit;
- Integer/Sharp where useful;
- Original aspect;
- shader/filter options under More only.

Do not force fake CRT scanlines.

## Input profiles

Jularr models PS1 controls independently of the runtime.

### Digital pad profile

Required:
- D-pad;
- Triangle;
- Circle;
- Cross;
- Square;
- L1;
- L2;
- R1;
- R2;
- Start;
- Select.

### Analog / DualShock-style profile

Adds:
- Left Stick;
- Right Stick;
- L3;
- R3.

Analog mode is shown only when the selected GameRelease/runtime supports it or the user explicitly chooses an analog-capable control profile.

Do not force analog sticks onto games that only need digital controls.

## Desktop

Desktop default:
- normal Jularr player shell;
- one large 4:3 game surface centered;
- keyboard/gamepad support;
- bottom action dock inherited from common player;
- controller state kept compact.

Primary actions:
- Pause;
- Save State;
- Load State;
- Screenshot;
- Controls;
- More.

PS1-specific secondary action:
- Disc, only when current release has multiple discs and runtime supports controlled disc switching.

If no physical gamepad is connected, keyboard mapping remains available through Controls.

## Tablet

Tablet follows the same player shell.

Landscape:
- large centered game frame;
- optional touch overlay;
- L/R shoulder controls may sit on outer edges;
- digital or analog preset according to selected control profile.

Portrait:
- game frame above;
- touch controls below;
- analog sticks may be shown only when the selected profile needs them.

## Mobile Portrait

Portrait is supported.

Recommended digital layout:
- game frame upper half;
- D-pad lower-left;
- Triangle/Circle/Cross/Square lower-right;
- L1/L2 and R1/R2 as shoulder/edge controls;
- Start/Select centered;
- no analog sticks unless analog profile is active.

Analog layout:
- Left Stick lower-left;
- face buttons lower-right;
- Right Stick placed where it remains reachable without covering critical UI;
- D-pad remains available but can be smaller/secondary;
- shoulder buttons remain at edges.

All controls remain movable/resizable through the shared editor.

## Mobile Landscape

Landscape is likely the most comfortable PS1 touch mode.

Recommended:
- game surface centered/maximized;
- D-pad or Left Stick on left;
- face buttons on right;
- optional Right Stick lower-right/inner-right;
- L1/L2 and R1/R2 on upper side edges;
- Start/Select lower center;
- player chrome minimized while playing.

Touch overlays use configurable opacity.

The page never draws a fake controller around the screen.

## Touch-control presets

Initial PS1 presets:
- PS1 Digital;
- PS1 Analog;
- Left-handed;
- Compact.

Portrait and Landscape are stored separately.

The canonical Jularr model stores semantic controls, not runtime input numbers.

The adapter translates semantic PS1 controls to the selected runtime.

## Physical controllers

Physical gamepad is the preferred PS1 input when available.

Behavior:
- auto-detect;
- brief connected/disconnected status;
- map through common Controls UI;
- preserve separate keyboard/touch preferences;
- touch controls may auto-hide when a physical controller becomes active;
- user can restore touch controls manually.

Up to runtime-supported player count may be exposed later, but local multiplayer must not be promised until tested for the selected core/runtime.

## Multi-disc model

GameRelease may contain a disc set:

```text
GameRelease
- Disc 1
- Disc 2
- ...
```

The Player knows:
- current disc;
- total discs;
- labels/order;
- whether the active runtime can switch discs safely during a session.

### Disc action

Show `Disc` only if:
- GameRelease has more than one disc; and
- runtime capability says disc switching is supported.

Disc sheet:
- current disc;
- available discs;
- explicit `Change to Disc N`;
- no raw file paths.

Changing disc must not create a new Game session or lose save state.

### Stable API rule

The current Jularr contract must not assume a PlayStation disc-swap API that EmulatorJS does not stably document.

Define a generic runtime capability:

```text
CanSwapDisc
CurrentDisc
AvailableDiscs[]
SwapDisc(DiscId)
```

The EmulatorJS adapter implements this only if the pinned/tested version exposes a stable supported path.

No DOM scraping.

If web runtime cannot safely switch a required disc:
- the Player reports that limitation clearly;
- Jularr does not fake a successful swap;
- another runtime may satisfy the capability later.

## Disc-backed launch assets

Importer/runtime adapter owns disc file/container specifics.

The consumer Player does not care whether the underlying release uses:
- CUE/BIN;
- CHD;
- ISO-like image;
- playlist/manifest;
- another supported disc container.

The LaunchPlan provides opaque approved assets to the runtime adapter.

## Saves and memory-card semantics

Consumer UX continues using Jularr's shared concepts:
- normal in-game save;
- manual Save States.

The runtime adapter may implement normal save persistence using emulated memory-card data.

Do not force the user to manage raw memory-card files in normal Player UI.

Advanced import/export may later expose memory-card backup when there is a clear use case.

## Performance

PS1 games are larger and more demanding than Game Boy/DS content.

Player requirements:
- clear loading state;
- download/cache progress when material;
- no duplicate game download on simple orientation/layout changes;
- runtime crash/out-of-memory reported distinctly;
- cache behavior remains adapter-owned.

Do not label a browser/device as playable until runtime capability checks pass.

## Screenshots / recording

Reuse shared Player behavior.

PS1 does not require a platform-specific screenshot UI.

Screen recording remains secondary under More when runtime/browser supports it.

## TV

PS1 is a strong TV use case.

TV player:
- game frame uses most of screen;
- physical gamepad required/preferred;
- large Pause/More overlay;
- Save State / Load State;
- Disc action when supported;
- no touch controls;
- no persistent controller setup clutter.

If no usable controller is connected:
- show controller-required state before starting gameplay;
- allow phone-as-controller later through shared Games controller-session feature.

## Runtime bridge additions

Generic bridge needs PS1-capability equivalents:

```text
SetControlProfile(Digital | Analog)
SetAnalogEnabled?
SwapDisc?               // capability-gated
GetDiscState?
```

Events may include:

```text
ControlProfileChanged
DiscChanged
DiscSwapFailed
```

Names are Jularr contracts, not EmulatorJS API names.

## Preferences

Conceptually:

```text
PlayStationPlayerPreferences
- PreferredControlProfile
- PhonePortraitControlLayoutId
- PhoneLandscapeControlLayoutId
- TabletPortraitControlLayoutId
- TabletLandscapeControlLayoutId
- DisplayMode
- TouchOpacity
```

Do not store runtime core IDs or BIOS paths as user preferences.

## Required states

In addition to common player states:
- missing/incompatible BIOS;
- digital controls active;
- analog controls active;
- physical controller connected;
- multi-disc release;
- disc swap available;
- disc swap unavailable;
- disc swap failed;
- large game loading/caching;
- browser memory/runtime failure.

## V1 acceptance

PS1 browser Player is complete enough when:
- standard single-disc PS1 Game launches;
- region-compatible BIOS capability is checked before launch;
- 4:3 display is correct;
- Digital control profile works;
- physical gamepad works;
- Mobile Portrait and Landscape touch layouts work;
- L1/L2/R1/R2 are reachable;
- Save/Load State uses common Player;
- normal in-game save persistence works;
- orientation changes do not restart/re-download;
- multi-disc data model exists;
- Disc action appears only when safely supported;
- no DOM hacks or fake hardware UI are used.

## Later

Do not block first PS1 support on:
- universal multi-disc swapping if the current browser runtime lacks a stable API;
- local 2–4 player multiplayer;
- force-feedback/vibration parity;
- per-game controller presets;
- memory-card manager UI;
- advanced PGXP/upscaling/render-core tuning;
- netplay.
