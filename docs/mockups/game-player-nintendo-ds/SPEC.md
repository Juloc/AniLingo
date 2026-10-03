# Nintendo DS Player

Status: Desktop, Tablet, Mobile Portrait, Mobile Landscape and Screen Layout Editor visual direction approved. The owner will upload the approved mockup image into this folder.

Parent player: `docs/mockups/game-player/SPEC.md`.
Touch controls: `docs/mockups/game-touch-controls/SPEC.md`.
Games architecture: #725.
UX planning: #729.
Browser runtime: #771.

## Visual direction

Nintendo DS must use the normal Jularr product UI. Do **not** visually imitate Nintendo DS hardware.

Hard rules:
- no fake DS shell;
- no fake hinge;
- no decorative console bezel;
- no fake physical speaker holes, cartridge shell, handheld chassis or device silhouette;
- no fake phone/tablet/console frame around the actual application UI;
- no ornamental hardware framing around TopScreen or BottomScreen.

TopScreen and BottomScreen are plain modern content surfaces inside the Jularr layout.

The two screens may use:
- simple neutral background;
- small radius consistent with Jularr cards/player surfaces;
- subtle border/shadow only where needed for separation;
- clear touch-screen indication for BottomScreen.

They must not be wrapped in a reproduction of Nintendo hardware.

### Clean theme

The approved Clean visual direction follows the existing light Jularr Admin/Activity design language:
- white / very light neutral surfaces;
- dark navy text;
- restrained purple accent;
- subtle gray borders;
- soft shadows;
- compact modern controls;
- no decorative gaming skin;
- no oversized neon glow;
- no fake glass/hardware treatment.

### Original Jularr theme

Original Jularr may use the established warm Japanese watercolor/cherry-blossom background and red accent, but the actual player layout remains modern Jularr UI.

Theme changes color/surface treatment only. It does not change screen arrangement, control hierarchy or runtime behavior.

### Content framing

The game screens themselves are the focal content.

Desktop:
- screens use available content width directly;
- Side-by-side is preferred when space permits;
- controls/actions sit in normal Jularr toolbars/cards.

Tablet:
- screens stack or sit side-by-side according to orientation;
- no decorative device frame.

Mobile Portrait:
- TopScreen and BottomScreen stack as clean rectangular surfaces;
- physical touch controls occupy the remaining safe area.

Mobile Landscape:
- screens use the available width directly;
- physical controls may overlay safe empty regions;
- no fake handheld outline.

Screen Layout Editor:
- preview uses plain screen rectangles on a neutral grid/canvas;
- resize handles/selection outlines are functional editor affordances only;
- never use a fake DS chassis as the editor canvas.

## Purpose

Define the Nintendo DS-specific behavior of the shared Jularr Game Player.

This is not a second player implementation.

The normal Game Player shell still owns session lifecycle, header/chrome, save synchronization, Save States, screenshots, fullscreen, controller handling, pause/exit and responsive shell behavior.

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

The bottom/touch screen identity remains known even when screens are visually swapped.

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

Best default for Mobile Portrait, Tablet Portrait and Desktop when vertical space is sufficient.

### Side by side

```text
Top | Bottom
```

Useful for Desktop widescreen, Tablet Landscape and Mobile Landscape.

### Focus + secondary

One screen is large, the other remains visible smaller.

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

For touch-heavy games:
- mouse/pointer acts as stylus over BottomScreen;
- cursor feedback appears only over the active touch surface;
- press maps to stylus down;
- release/cancel maps to stylus up.

Desktop controls:
- keyboard/gamepad for physical buttons;
- mouse/pointer for stylus.

A Screen Layout action belongs in player controls because it is a common DS need.

## Tablet layout

Tablet supports:
- Stacked in portrait;
- Side-by-side or Focus + secondary in landscape;
- direct touch on BottomScreen;
- optional on-screen physical controls when no gamepad is connected.

Tablet does not require a separate virtual stylus tool; direct touch is primary.

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
- D-pad + A/B/X/Y do not cover BottomScreen by default;
- L/R may use edge/shoulder touch targets;
- Start/Select remain compact.

Button layout and DS screen layout are edited separately.

## Mobile Landscape

Default:
- screens side by side or Focus + secondary;
- physical controls overlay safe outer regions;
- touch screen remains directly interactive;
- user can swap which screen is larger.

The player may suggest Focus + secondary when space is constrained, but must not switch layout during play without user action.

## DS touch behavior

Required:
- pointer/finger coordinates map precisely to BottomScreen;
- letterboxing/padding is excluded from touch-coordinate calculation;
- touch remains correct after resize/orientation/layout change;
- pointer cancel releases stylus input;
- no browser page scroll/zoom inside active touch surface;
- optional visual touch indicator;
- browser multi-touch gestures must not corrupt stylus input.

The DS is single-stylus input. Additional fingers on virtual buttons must coexist with one active stylus contact.

## Virtual physical controls

DS-specific mobile preset:
- D-pad;
- A/B/X/Y;
- L/R;
- Start;
- Select.

Controls use the existing Jularr touch-control editor.

Persist separately from screen-layout preferences.

Initial presets:
- DS Standard;
- DS Left-handed;
- DS Compact.

Portrait and Landscape are separately editable.

## Screen-layout editor

Do not overload the button editor with screen sizing.

Add a DS-specific Screen Layout sheet/editor.

Capabilities:
- Stacked / Side-by-side / Focus + secondary / Single-screen focus;
- swap Top/Bottom visual position;
- choose focused screen;
- adjust relative size in Focus mode;
- reset to platform default;
- live preview;
- Save / Cancel.

The editor never lets touch-screen identity become ambiguous.

## Microphone

Nintendo DS games may use microphone input.

Treat microphone as a runtime/browser capability.

When supported and needed:
- request permission only when activated/needed;
- show compact microphone action/status;
- allow temporary mute;
- no background recording outside active session;
- no audio storage unless separately approved.

If runtime support is not stable, do not fake it.

## Saves

Reuse the common Game Player model:
- normal in-game save;
- manual Save States;
- server sync;
- final sync on exit.

DS screen/control preferences are profile preferences, not Save State data.

## Screenshots

Default screenshot behavior captures the complete current DS presentation when supported.

Later options may allow both screens / Top only / Bottom only.

Do not block initial DS support on this.

## Fullscreen

Fullscreen includes both screens and active controls.

Changing fullscreen:
- does not restart emulation;
- preserves screen layout;
- recalculates touch coordinates.

## TV

DS TV play is capability-dependent because TV has no direct touchscreen.

DS on TV requires one of:
- pointer-capable controller/input supported by runtime;
- later paired phone acting as DS touch surface/controller;
- a game that does not require touch for the intended play path.

Do not claim universal DS TV playability.

TV shows a concise compatibility reason when touch input cannot be supplied.

Later phone-companion concept:
- phone represents BottomScreen/touch input;
- TV shows TopScreen large and optionally BottomScreen preview;
- Jularr coordinates the session.

This is later, not DS V1.

## Runtime bridge additions

Conceptual DS bridge capabilities:

```text
SetDualScreenLayout
SwapScreens
FocusScreen
PointerDownOnTouchScreen
PointerMoveOnTouchScreen
PointerUpOnTouchScreen
SetMicrophoneEnabled?   // only when supported
```

Events may include:

```text
DualScreenLayoutChanged
TouchCapabilityChanged
MicrophoneCapabilityChanged
```

These are Jularr contracts, not EmulatorJS API names.

The adapter translates only through stable supported runtime APIs. No DOM scraping.

## Preferences

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

Do not persist runtime-specific core configuration as consumer preferences.

## Required states

In addition to generic player states:
- both screens ready;
- touch unavailable;
- microphone permission needed;
- microphone unavailable;
- restored screen layout;
- invalid preference reset to default;
- TV lacks required touch/pointer input.

## V1 acceptance

Nintendo DS browser play is complete enough when:
- Top and Bottom screens render correctly;
- BottomScreen touch coordinates remain correct across resize/orientation;
- Desktop mouse/pointer works as stylus;
- Tablet/mobile direct touch works;
- D-pad/A/B/X/Y/L/R/Start/Select are usable;
- Stacked and Side-by-side work;
- Swap Screens works;
- Portrait and Landscape are supported;
- layout persists per profile/form factor/orientation;
- normal saves and Save States reuse common player;
- layout/orientation changes do not restart the game;
- BIOS/runtime failures surface through normal PlayCapability;
- no runtime-specific DOM hacks are required.

## Later

Do not block initial DS support on:
- phone-as-TV-touchscreen;
- microphone if runtime API is unstable;
- lid sensor emulation;
- per-game automatic screen-layout profiles;
- advanced screenshot composition;
- AI-derived control layouts.
