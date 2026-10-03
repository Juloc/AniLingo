# Nintendo DS Player

Status: platform-specific player planning. Visual mockups pending approval.

Parent player: `docs/mockups/game-player/SPEC.md`.
Touch controls: `docs/mockups/game-touch-controls/SPEC.md`.
Games architecture: #725.
UX planning: #729.
Browser runtime: #771.

## Purpose

Define the Nintendo DS-specific behavior of the shared Jularr Game Player.

This is not a second player implementation.

The normal Game Player shell still owns:
- session lifecycle;
- header/chrome;
- save synchronization;
- Save States;
- screenshots;
- fullscreen;
- controller handling;
- pause/exit;
- responsive shell behavior.

Nintendo DS adds only the capabilities that differ materially from Game Boy:
- two displays;
- one touch-sensitive lower display;
- DS-specific physical controls;
- screen arrangement/swap/focus;
- optional microphone capability when the selected runtime/browser exposes it.

## EmulatorJS capability baseline

EmulatorJS exposes Nintendo DS through the `nds` system and currently documents these cores:
- `melonds`;
- `desmume`;
- `desmume2015`.

Using `nds` selects melonDS by default.

The runtime/core choice remains adapter-owned.

For melonDS, Jularr must account for the documented BIOS/firmware requirements through Admin Games BIOS/Firmware. Consumer Player UI only receives a playable/unavailable capability and never BIOS paths.

## DS input model

Required physical inputs:
- D-pad;
- A;
- B;
- X;
- Y;
- L;
- R;
- Start;
- Select;
- touchscreen/stylus.

Optional/capability-driven:
- microphone;
- lid-close/open behavior if a runtime later exposes a stable need/API.

Do not show controls unsupported by the selected runtime.

## Dual-screen model

The player treats the DS as two logical screens:

```text
TopScreen
BottomScreen (touch-capable)
```

The bottom/touch screen identity must remain known even when screens are visually swapped.

Touch coordinates are always translated to the actual DS touch screen, not simply whichever visual rectangle is lowest.

## Screen-layout modes

Jularr owns a platform-neutral DS layout preference.

Initial modes:

### Stacked
Default DS-like layout.

```text
Top
Bottom
```

Best default for:
- Mobile Portrait;
- Tablet Portrait;
- Desktop when vertical space is sufficient.

### Side by side

```text
Top | Bottom
```

Useful for:
- Desktop widescreen;
- Tablet Landscape;
- Mobile Landscape.

### Focus + secondary

One screen is large, the other remains visible smaller.

Examples:

```text
[ Main screen large ] [ secondary ]
```

or stacked picture-in-picture style where practical.

Useful for games where one screen carries most gameplay.

### Single-screen focus

Show one screen maximized, with a quick action to swap/focus the other.

The hidden screen remains emulated; this is visual focus only.

Use only as an explicit user choice, never as the default if it would hide required gameplay information.

## Screen actions

Quick player actions:
- change Screen Layout;
- Swap Screens;
- Focus Top;
- Focus Bottom;
- reset screen layout.

Screen layout changes:
- never restart the game;
- never change save/session identity;
- preserve touch-screen mapping;
- persist per profile + platform + form factor + orientation.

## Desktop layout

Default:
- Jularr player shell;
- two screens centered;
- Stacked or Side-by-side chosen from available aspect/space;
- normal bottom player action dock.

For games using touch heavily:
- mouse/pointer acts as stylus over BottomScreen;
- cursor feedback appears only over the active touch surface;
- click/touch press maps to stylus down;
- release/cancel maps to stylus up.

Desktop controls:
- keyboard/gamepad for physical buttons;
- mouse/pointer for stylus.

A Screen Layout quick action belongs in the primary/secondary player controls because it is a common DS need.

## Tablet layout

Tablet supports:
- Stacked in portrait;
- Side-by-side or Focus + secondary in landscape;
- touch directly on BottomScreen;
- optional on-screen physical controls when no gamepad is connected.

Tablet must not require a separate virtual stylus tool; direct touch on the DS touch screen is the primary input.

## Mobile Portrait

Portrait is the preferred default for Nintendo DS.

Recommended structure:

```text
compact player chrome

┌──────────────┐
│  Top Screen  │
└──────────────┘
┌──────────────┐
│ Bottom/Touch │
└──────────────┘

physical touch controls below / around screens
```

Rules:
- both screens remain readable;
- BottomScreen receives direct finger/stylus input;
- D-pad + A/B/X/Y must not cover the BottomScreen touch area by default;
- L/R may use edge/shoulder touch targets;
- Start/Select remain compact.

The player may allow custom control positioning, but DS screen rectangles themselves are edited separately from button layout.

## Mobile Landscape

Default:
- screens side by side or Focus + secondary;
- physical controls overlay safe outer regions;
- touch screen remains directly interactive;
- user can swap which screen is larger.

Because space is constrained, the player may automatically suggest Focus + secondary, but must not switch modes during play without user action.

## DS touch behavior

Required:
- pointer/finger coordinates map precisely to BottomScreen;
- letterboxing/padding is excluded from touch-coordinate calculation;
- touch remains correct after resize/orientation/layout change;
- pointer cancel releases stylus input;
- no browser page scroll/zoom inside the active touch surface;
- visual touch feedback can be optionally shown;
- multi-touch gestures from the browser must not corrupt stylus input.

The DS itself is fundamentally single-stylus input. Additional fingers used on virtual buttons must coexist with one active stylus contact.

## Virtual physical controls

DS-specific mobile preset includes:
- D-pad;
- A/B/X/Y;
- L/R;
- Start;
- Select.

Controls are movable through the existing Jularr touch-control editor.

Persist separately from screen-layout preferences.

Initial presets:
- DS Standard;
- DS Left-handed;
- DS Compact.

Portrait and Landscape remain separately editable.

## Screen-layout editor

Do not overload the button editor with screen sizing.

Add a small DS-specific Screen Layout sheet/editor.

Capabilities:
- choose Stacked / Side-by-side / Focus + secondary / Single-screen focus;
- swap Top/Bottom visual position;
- choose focused screen;
- adjust relative screen size in Focus mode;
- reset to platform default;
- live preview;
- Save / Cancel.

The editor must never let the touch-screen transform become ambiguous.

## Microphone

Nintendo DS games may use microphone input.

Treat microphone as a runtime/browser capability, not an unconditional control.

When supported and required:
- request browser microphone permission only when the user activates the feature or the game needs it;
- show a compact microphone action/status;
- allow temporary mute;
- no background recording outside the active game session;
- no audio storage unless a separate approved feature explicitly requires it.

If the runtime lacks stable microphone support, do not fake it.

## Saves

Reuse the normal Game Player save model:
- in-game save;
- manual Save States;
- server sync;
- final sync on exit.

DS-specific screen layout/control preferences are profile preferences, not Save State data.

## Screenshots

Default screenshot behavior should capture the complete current DS presentation when supported.

A later option may allow:
- both screens;
- Top only;
- Bottom only.

Do not block initial DS support on advanced screenshot selection.

## Fullscreen

Fullscreen includes both screens and active controls.

Changing fullscreen:
- does not restart emulation;
- preserves the selected DS screen layout;
- recalculates touch coordinates after layout.

## TV

DS is playable on TV only when the selected runtime/device combination is practical.

TV has no direct touchscreen.

Therefore DS TV play requires one of:
- pointer-capable controller/input supported by runtime;
- later paired phone acting as DS touch surface/controller;
- a game that does not require touch for the intended play path.

Do not claim universal DS TV playability.

TV detail/player should show a concise compatibility reason when touch input cannot be provided.

Later phone companion concept:
- phone can represent BottomScreen/touch input;
- TV can show TopScreen large and optionally BottomScreen preview;
- session remains coordinated by Jularr.

This is later capability, not DS V1.

## Runtime bridge additions

The generic player bridge needs DS capabilities conceptually equivalent to:

```text
SetDualScreenLayout
SwapScreens
FocusScreen
PointerDownOnTouchScreen
PointerMoveOnTouchScreen
PointerUpOnTouchScreen
SetMicrophoneEnabled?   // only when supported
```

Runtime events may include:

```text
DualScreenLayoutChanged
TouchCapabilityChanged
MicrophoneCapabilityChanged
```

Names are conceptual contracts, not EmulatorJS API names.

The EmulatorJS adapter translates them only through stable supported runtime APIs.

No DOM scraping.

## Read/config model

Conceptually:

```text
NintendoDsPlayerPreferences
- ScreenLayout
- ScreenOrder
- FocusedScreen
- FocusRatio
- PhonePortraitControlLayoutId
- PhoneLandscapeControlLayoutId
- TabletPortraitControlLayoutId
- TabletLandscapeControlLayoutId
- ShowTouchIndicator
```

Do not persist EmulatorJS core configuration as consumer preferences.

## Required states

In addition to generic Game Player states:
- both screens ready;
- touch unavailable;
- microphone permission needed;
- microphone unavailable;
- selected screen layout restored;
- invalid layout preference reset to default;
- TV lacks required touch/pointer input.

## V1 acceptance

Nintendo DS browser play is complete enough for first scope when:
- Top and Bottom screens render correctly;
- BottomScreen touch coordinates remain correct across resize/orientation;
- Desktop mouse/pointer works as stylus;
- Tablet/mobile direct touch works;
- D-pad/A/B/X/Y/L/R/Start/Select are usable;
- Stacked and Side-by-side layouts work;
- Swap Screens works;
- Portrait and Landscape are supported;
- screen layout persists per profile/form factor/orientation;
- normal saves and Save States reuse the common player;
- no game restart occurs from layout/orientation changes;
- BIOS/runtime failures surface through normal PlayCapability;
- no runtime-specific DOM hacks are required.

## Later

Do not block initial DS support on:
- phone-as-TV-touchscreen;
- microphone if the runtime API is not stable;
- lid sensor emulation;
- per-game automatic screen-layout profiles;
- advanced screenshot composition;
- AI-derived control layouts.
