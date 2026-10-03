# Game Browser Player

Status: visual direction approved from the current Desktop / Tablet / Mobile Game Boy mockup set. The owner will upload the approved images into this folder.

Shared contract: `docs/mockups/games/SPEC.md`.
Touch controls: `docs/mockups/game-touch-controls/SPEC.md`.
Nintendo DS extension: `docs/mockups/game-player-nintendo-ds/SPEC.md`.
Games architecture: #725.
UX planning: #729.
Initial browser runtime: #771.

## Global visual rule for all platform players

All platform-specific Game Player mockups use the Jularr UI, not a visual recreation of the original console hardware.

Forbidden unless explicitly required by a real interaction:
- fake console shells;
- fake handheld/device frames;
- fake bezels that imitate original hardware;
- ornamental hinges, speaker holes, plastic bodies or controller-shaped page containers;
- decorative phone/tablet frames inside the actual app screen.

Use modern Jularr content surfaces, responsive layout and real controls.

Platform identity comes from:
- game content;
- platform badge;
- control scheme;
- layout behavior;
- platform-specific capabilities.

It does **not** come from drawing a fake Game Boy, DS, PSP, console or TV around the emulator.

For Clean theme, use the existing light Jularr visual language from Admin/Activity as the baseline: white/light surfaces, navy text, restrained purple accent, subtle borders/shadows and compact controls.

For Original Jularr, use the established warm Japanese watercolor/cherry-blossom skin and red accent over the same structure.

This rule applies to all future platform-player specs and mockups unless a later approved platform requirement explicitly overrides it.

## Purpose

Focused play surface for a `LaunchPlan` produced by the Games module.

The page must feel like Jularr, not like an unstyled embedded EmulatorJS page.

Jularr owns page chrome, navigation/exit, play-session state, save-sync status, quick actions, touch-control presets/editor and responsive behavior. The runtime adapter owns the actual emulation and translates the Jularr player contract to EmulatorJS or another future runtime.

## First implementation target

Game Boy / Game Boy Color first.

The same player shell remains reusable for later platforms.

For EmulatorJS:
- Game Boy uses the Game Boy control scheme;
- runtime/core choice stays inside the adapter;
- the page does not know core names, CDN paths, ROM paths or BIOS paths.

## Header

Desktop/tablet:
- Back;
- Game title;
- compact platform badge;
- compact runtime badge such as `Web`;
- save-sync state;
- Fullscreen;
- overflow menu;
- Exit Game.

Mobile:
- very compact top chrome;
- back/menu;
- title only when space allows;
- save/runtime state moves into the pause sheet when necessary;
- pause/menu and fullscreen remain easy to reach.

Game chrome may auto-hide while actively playing, but returns immediately on pointer/touch/key interaction.

Touch controls themselves do not auto-hide while touch mode is active.

## Game viewport

Rules:
- preserve native aspect ratio;
- never stretch to fill;
- center inside available play area;
- crisp/pixel-preserving scaling by default for Game Boy;
- neutral Jularr player background outside the frame;
- resize without restarting the emulation session;
- orientation changes never reload the ROM or lose unsynced state.

For Game Boy portrait, the viewport occupies the upper/main part and leaves a dedicated lower control zone.

For landscape, the viewport may sit behind/among touch controls only when the chosen touch layout intentionally overlays them.

## Desktop

Approved composition:
1. Jularr shell visible outside fullscreen;
2. compact player header;
3. large centered game viewport;
4. bottom action dock;
5. compact save-sync status;
6. overflow menu for secondary actions.

Primary dock:
- Pause / Resume;
- Save State;
- Load State;
- Screenshot;
- Controls;
- More.

More may contain:
- Restart Game;
- Cheats when supported;
- save-file import/export;
- recording when supported;
- Audio;
- Display;
- cache/runtime diagnostics only when genuinely useful;
- Exit Game.

Do not expose raw EmulatorJS setting IDs.

## Tablet

Same hierarchy as Desktop, but:
- narrower header;
- larger touch targets;
- action dock below viewport;
- settings/overflow use a sheet or popover appropriate to width;
- touch controls may be explicitly enabled.

Portrait and landscape are both supported.

## Mobile — before start

Portrait may show the normal Game Detail / Play state before launch.

Starting a Game enters the dedicated player without forcing landscape.

The UI may recommend landscape when useful, but Game Boy must be playable in portrait.

## Mobile — portrait in play

Portrait is a supported play mode.

Composition:
- compact top chrome;
- Game Boy viewport in upper/main area;
- touch controls below;
- D-pad left;
- A/B right;
- Select/Start centered;
- no controls in notch/home-indicator unsafe zones.

Portrait and landscape layouts are saved independently.

## Mobile — landscape in play

Composition:
- game viewport maximized;
- compact pause/menu control;
- D-pad left;
- A/B right;
- Select/Start lower center;
- optional minimal fullscreen/audio action;
- other chrome hidden while actively playing.

Landscape uses its own saved touch layout.

## Mobile quick menu / pause sheet

Opening Pause/Menu pauses the game where supported.

Primary actions:
- Continue;
- Create Save State;
- Load Save State;
- Screenshot;
- Adjust Controls;
- Audio;
- Display;
- Exit Game.

Secondary features such as Restart, Cheats, save import/export or recording may live under Advanced/More.

Do not put every runtime feature on the main play screen.

## Save model and status

Jularr distinguishes:
- normal in-game save/SRAM;
- manual save states.

Player status:
- Saving…;
- Saved;
- Save failed / retry needed;
- offline/local-only where relevant.

Never show successful server sync before server acknowledgement.

Normal exit:
1. request final runtime save flush where supported;
2. persist changed save data;
3. complete session/last-played state;
4. return to Game Detail/Games.

Failed final sync must produce retry/keep-local behavior rather than silently losing data.

## Runtime bridge

EmulatorJS is embedded behind a Jularr-owned runtime page/adapter.

For SPA-style integration, keep the emulator in its own embedded document/iframe.

The outer Player uses a narrow Jularr runtime bridge rather than reading runtime DOM.

Conceptual commands:

```text
Pause
Resume
Restart
CreateSaveState
LoadSaveState
CaptureScreenshot
SetVolume
SetFullscreen
ApplyInputLayout
Exit
```

Conceptual events:

```text
Ready
Started
Paused
Resumed
SaveChanged
SaveSyncStateChanged
StateCreated
StateLoaded
ControllerChanged
Exited
RuntimeError
```

The EmulatorJS adapter translates these to the pinned/tested runtime version.

Do not make Jularr's Player contract equal to EmulatorJS internals.

## EmulatorJS feature mapping

Primary Jularr UI:
- pause/resume;
- save state;
- load state;
- screenshot;
- fullscreen;
- controller mapping;
- normal save synchronization;
- exit.

Secondary / More:
- restart;
- cheats;
- save-file import/export;
- screen recording;
- audio;
- display/shader options where useful;
- cache management only if needed;
- platform-specific disc controls later.

Later/capability-driven:
- netplay;
- multi-disc controls;
- rewind;
- platform-specific advanced settings.

Show features only when the selected runtime/platform reports support.

## EmulatorJS configuration rules

For the first adapter:
- production uses a tested/pinned EmulatorJS release from the stable channel;
- `EJS_gameID` maps to stable Jularr release/session identity suitable for save separation;
- `EJS_gameName` uses canonical display name;
- Game Boy uses the documented Game Boy control scheme;
- Jularr touch layouts are converted to `EJS_VirtualGamepadSettings`;
- keyboard/gamepad defaults may use `EJS_defaultControls`;
- save callbacks/events feed the Jularr save service;
- runtime controls/settings replaced by Jularr are hidden;
- no advertising surface is configured;
- cache behavior is adapter-owned, not page-owned.

If a feature has no stable documented programmatic API in the pinned EmulatorJS version, the adapter must not fake it. It may temporarily expose the supported runtime control or defer that feature.

## Physical controllers

Desktop/tablet/mobile may use browser gamepad support through the runtime.

Rules:
- detect connect/disconnect;
- brief non-blocking status;
- mapping through Controls;
- if a physical controller becomes active on mobile, touch controls may fade/hide;
- user can explicitly restore touch controls;
- saved touch layout remains unchanged.

## TV and controller sessions

TV is a first-class future surface.

Input may include:
- connected gamepads;
- TV remote for shell/navigation;
- later paired phones as temporary controllers;
- other explicit runtime adapters.

Phone pairing:
- short-lived QR/code;
- session-scoped;
- player-slot assignment where supported;
- reconnect handling;
- no general Jularr/server access.

Not required for the first Game Boy browser-player implementation.

## Required states

- loading runtime;
- loading game;
- ready to start;
- playing;
- paused;
- saving;
- save synced;
- save sync failed;
- controller connected/disconnected;
- unsupported browser/runtime;
- runtime crashed;
- game asset unavailable;
- permission denied;
- exit confirmation only when unsaved work requires it.

## Responsive acceptance

Desktop, Tablet, Mobile Portrait and Mobile Landscape are intentional layouts, not scaled copies.

Changing viewport/orientation:
- keeps the same emulation session;
- keeps save state;
- reapplies the correct control layout;
- never downloads/restarts the ROM solely because orientation changed.

## Boundaries

- no direct DB access;
- no unrestricted library filesystem access;
- no Admin runtime settings;
- no downloader/acquisition controls;
- no raw BIOS/path/core settings in consumer UI;
- no runtime-specific DOM scraping;
- no universal emulator settings engine.

## Visual baseline

The current approved mockups cover Desktop, Tablet, Mobile Landscape, Mobile Portrait, Pause/Quick Menu and Touch Layout Editor.

The owner will upload those images into the relevant mockup folders. Text spec wins on conflict.
