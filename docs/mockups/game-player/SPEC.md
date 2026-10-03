# Game Browser Player — Planning Scaffold

Status: scaffold; detailed UX pending review.

Shared contract: `docs/mockups/games/SPEC.md`.

## Purpose

Focused browser-play surface for a LaunchPlan produced by the Games module.

## Initial responsibilities

Plan for:
- emulator/runtime viewport
- loading/initialization state
- fullscreen
- pause/resume where runtime supports it
- exit/back to Game Detail
- minimal runtime/player menu
- save persistence feedback
- keyboard/gamepad input where runtime supports it
- multiple player/input slots where runtime supports local multiplayer
- clear failure reason when launch cannot continue

## TV and controller sessions

TV is a first-class future Games playback surface.

Input sources may include:
- gamepads connected to the TV/device;
- TV remote for shell/navigation, not as a forced gameplay controller;
- paired phones acting as temporary controllers;
- other explicitly supported runtime input adapters.

For phone-as-controller:
- TV shows a short-lived pairing flow, for example QR/code;
- phone joins the specific game session;
- server coordinates the session and input channel;
- each phone/gamepad can be assigned to a player slot where supported;
- disconnect/reconnect is handled without granting general account/server access.

Pairing is session-scoped and capability-driven. It is not required for V1 Games playback.

## Runtime contract

The page receives a controlled LaunchPlan. It does not browse the filesystem or construct emulator paths itself.

The runtime receives only the selected game assets and current-profile save scope exposed through approved Games APIs.

## Boundaries

- no direct database access
- no unrestricted library filesystem access
- no Admin runtime settings
- no downloader/acquisition controls
- no universal emulator settings engine

## Platform behavior

Desktop/browser:
- keyboard and browser Gamepad API where supported.

TV:
- remote-friendly launch/exit UI;
- connected controller discovery/assignment;
- later phone-controller pairing;
- gameplay chrome should disappear once play starts unless explicitly opened.

Mobile:
- normal browser-compatible play where supported;
- later controller-only companion mode for a TV session.

## Open for page review

Decide:
- viewport/chrome composition
- desktop/mobile controls
- fullscreen behavior
- runtime menu
- save indication
- controller mapping UX only if needed by the selected runtime
- failure/recovery states
