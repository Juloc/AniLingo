# Player — Cross-platform Clean Design

Status: planning baseline for mockups. Architecture source: canonical PlaybackPlan/ActiveSession.

## Purpose
Play canonical video/audio Assets while preserving progress and exposing platform-appropriate controls over one playback contract.

## Page structure
Media surface + persistent subtitle/learning layer + transient controls + timeline + track/quality menus. Technical diagnostics are secondary/on-demand.

## Data / information
Canonical Work/unit, selected Version/Asset/File, duration/position, PlaybackPlan mode/reasons, audio/subtitle tracks, chapters/segments, quality, ActiveSession and progress.

## Actions
Play/pause, seek, previous/next, audio/subtitle, quality, speed, Fit/Fill/Zoom, fullscreen/PiP where supported, learning interaction, diagnostics, close/return.

## Light / Dark
Player is primarily dark/media-led; overlays and dialogs still use theme-aware accessible tokens. Light application theme must not produce low-contrast player chrome.

## Platforms
Desktop: click video=play/pause, double-click fullscreen, keyboard, visible volume slider, hover timeline preview. Mobile: tap toggles controls; double-tap +/-10s; touch gestures for supported brightness/volume; lock; landscape/fullscreen. Tablet: mobile interaction with wider side sheets. TV: D-pad/media-key remote-first, strong focus, safe areas. iOS/WebKit: explicit compatibility path and deliberate system-control fallback where required.

## States
Preparing/acquiring, loading/buffering, playing/paused, ended, storage unavailable, track unavailable, remux/transcode startup, recoverable playback error, unsupported capability, offline, session reconnect.

## Must not implement
No client-specific playback decision engine, no second progress/session store, no arbitrary filesystem/FFmpeg inputs, no click-to-toggle-controls behavior on Desktop that violates click=play/pause, no hiding learning subtitles with transient controls, no TV mouse assumptions.