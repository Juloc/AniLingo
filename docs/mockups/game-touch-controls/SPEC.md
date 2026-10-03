# Game Touch Controls

Status: interaction direction approved from the current Game Boy mobile mockup. The owner will upload the approved image into this folder.

Parent player: `docs/mockups/game-player/SPEC.md`.
Runtime plan: #771.

## Purpose

Provide a first-class mobile touch controller instead of blindly exposing the runtime's default virtual gamepad.

Game Boy is the first control scheme. The same editor/data model must support later platforms.

## Game Boy default controls

Required:
- D-pad;
- A;
- B;
- Select;
- Start.

No shoulder/analog buttons appear in the Game Boy preset.

## Separate orientation layouts

Portrait and Landscape are stored separately.

Rotation:
- switches to matching saved layout;
- does not restart the game;
- does not overwrite the other orientation;
- falls back to platform default when no user layout exists.

Tablet may have its own form-factor layout separate from Phone.

## Layout identity

Persist by:

```text
Profile
+ ControlScheme / Platform
+ FormFactor (Phone / Tablet)
+ Orientation (Portrait / Landscape)
```

Positions are normalized relative to the safe play/control area, not device-specific pixels.

## Editable controls

Movable Game Boy elements:
- D-pad as one group;
- A;
- B;
- Select;
- Start.

Every element supports:
- drag/move;
- scale;
- visibility where safe;
- opacity;
- reset.

A/B and Start/Select may be visually paired by a preset, but each remains independently movable.

Reset is always reachable.

## Safe areas

Respect:
- notches/cutouts;
- browser chrome;
- iOS/Android home gesture region;
- rounded corners;
- Jularr pause/menu affordances.

Dragging cannot leave a control fully outside the usable safe area.

Screen-size/orientation changes normalize/clamp positions instead of losing them.

## Editor interaction

Entry:
`Pause -> Adjust Controls`.

Editor:
- gameplay remains visible as a dimmed preview;
- optional alignment grid;
- safe-area boundary visible;
- controls show selection/drag affordances;
- tap selects;
- drag moves;
- size slider affects selected control, with optional global-size shortcut;
- opacity can apply globally or to selected control;
- visibility toggle for selected optional element;
- haptic/touch-feedback toggle;
- Save;
- Cancel;
- Reset.

Changes preview live but persist only on Save.

Cancel restores previous layout.

## Presets

Initial Game Boy presets:
- Standard;
- Left-handed;
- Compact.

Preset selection updates preview immediately.

Saving after selecting a preset creates the user's editable copy; later preset changes never silently overwrite custom layouts.

Reset options:
- selected control;
- current orientation;
- both orientations to platform defaults.

## Portrait default

Recommended:
- viewport above;
- D-pad lower-left;
- A/B lower-right;
- Select/Start centered near bottom;
- enough distance from home indicator.

Controls normally sit outside important gameplay content.

## Landscape default

Recommended:
- viewport maximized;
- D-pad overlay left;
- A/B overlay right;
- Select/Start lower center;
- configurable translucency.

## Touch behavior

Required:
- multi-touch for D-pad + A/B combinations;
- no page scroll/zoom inside active controller surface;
- finger leaving/canceling releases input;
- no stuck inputs;
- optional haptic feedback;
- obvious pressed state;
- practical minimum touch target;
- no hover dependency.

## Physical gamepad interaction

When a physical controller is active:
- touch controls may auto-dim/hide;
- a small action restores them;
- saved touch layout is unchanged.

If the controller disconnects, touch controls return immediately.

## Runtime mapping

Jularr stores its own normalized touch-layout model.

The EmulatorJS adapter converts this to the runtime's virtual-gamepad configuration.

For Game Boy:
- use Game Boy control scheme;
- map D-pad/A/B/Select/Start to correct runtime inputs;
- keep runtime-specific input numbers out of Jularr profile/UI models.

Do not store raw `EJS_VirtualGamepadSettings` as the canonical preference.

## Accessibility / usability

- high-contrast outlines independent of game artwork;
- configurable opacity;
- works in both Jularr themes;
- labels readable at normal scale;
- obvious pressed state;
- left-handed preset;
- editor usable one-handed where practical.

## Required states

- default layout;
- custom layout;
- preset preview;
- unsaved changes;
- invalid/clamped position;
- physical controller active;
- orientation changed;
- runtime without touch-control support.

## V1 acceptance

For Game Boy:
- Portrait and Landscape playable;
- every required control movable;
- size/opacity adjustable;
- Standard/Left-handed/Compact presets;
- layout saved per profile/orientation/form factor;
- safe-area handling;
- reset;
- no game restart on edit/orientation change;
- runtime mapping through adapter.
