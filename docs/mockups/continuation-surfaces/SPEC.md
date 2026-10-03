# Continuation Surfaces — Now Playing / Continue Reading

Status: **approved UX direction; binding planning specification**.

This specification defines the persistent compact continuation surfaces used when the full Player or Reader is no longer occupying the screen.

It does not create a second playback/reader state model.

Canonical ownership:

- active video/audio playback -> `ActiveSession`;
- reading position -> canonical `MediaProgress` exact locator;
- Player preferences/tracks -> existing playback/session contracts;
- Reader preferences -> existing Reader contracts.

## 1. Product split

There are exactly two continuation concepts:

### A. Now Playing
For actually active playback:
- video;
- audiobook/audio;
- TTS while it is actively playing.

This is an active transport surface.

### B. Continue Reading
For Book / Light Novel / Manga / other Reader content after leaving the Reader.

This is **not** an active transport surface.

It only offers a direct return to the exact saved reading position.

Do not visually or behaviorally turn Continue Reading into a copied media player.

## 2. Priority

Only one persistent bottom continuation surface may occupy the primary app-bottom continuation slot.

Priority:

1. active Now Playing session;
2. Continue Reading surface;
3. none.

If video/audio/TTS is actively playing, do not stack a second Continue Reading bar above or below it.

Reading remains reachable through Home/Library/Detail Continue Reading while playback owns the persistent slot.

## 3. Placement

Desktop:
- persistent compact bar at the bottom of the app content;
- must not cover important navigation or page actions;
- uses the normal app width/content safe area.

Mobile:
- sits directly above bottom navigation;
- safe-area aware;
- never covers keyboard, sheets, system gesture area or bottom navigation.

Tablet:
- follows Desktop or Mobile composition according to available width/input mode.

TV:
- no persistent floating continuation bar is required;
- use normal Continue Watching / Continue Reading browse surfaces;
- returning to the active Player/Reader still uses canonical session/progress state.

## 4. Video / audio Now Playing — Desktop

The Desktop bar has three horizontal functional zones plus a full-width progress row.

### 4.1 Progress row

At the very top of the bar:

- played portion;
- buffered portion;
- remaining portion;
- chapter/segment markers where available;
- current position indicator.

Pointer behavior:
- hovering the timeline reveals the scrub handle/knob;
- timeline hover may show time and chapter preview;
- dragging the handle seeks the existing ActiveSession;
- chapter markers remain subtle and must not turn the bar into an editor.

The progress line spans the available bar width above the content/control row.

### 4.2 Left media identity

First element is the video/artwork thumbnail.

Next to it:

1. **Work title in bold**;
2. unit context, e.g. `S1 · E4 — Episode name`;
3. current / duration, e.g. `5:29 / 25:25`.

For Movie:
- Work title;
- optional edition/cut only when user-relevant;
- current / duration.

For Audiobook:
- Work title;
- chapter name/number;
- current / duration.

Do not show codecs, release groups, providers, file names or technical playback mode.

### 4.3 Thumbnail hover

Desktop pointer hover over the thumbnail reveals a compact **Open full Player** action.

Selecting it returns to the exact same `ActiveSession`.

The thumbnail itself may also open the full Player.

This is not PiP/Popout.

### 4.4 Center transport

The primary transport group is always geometrically centered in the bar:

```text
Previous · -10 · Play/Pause · +30 · Next
```

Rules:

- Previous and Next mean previous/next playable canonical unit where available;
- seek backward is always 10 seconds;
- seek forward is always 30 seconds;
- Play/Pause is the visual center and may be slightly larger;
- the center group must not shift because the left metadata or right settings width changes.

When Previous or Next is unavailable, preserve the visual centering of the remaining transport controls.

### 4.5 Right settings/actions

Show as many frequently useful direct icons as fit without crowding.

Possible direct actions:
- subtitles;
- audio;
- quality;
- speed;
- chapters;
- Learning On/Off when available;
- fullscreen/open-player action where useful.

When width is insufficient:
- collapse lower-priority actions into one Settings gear/menu;
- do not shrink controls below usable target size.

**Volume belongs inside the Settings gear/menu in this compact Now Playing state.**
Do not place a permanent volume slider in the compact bar.

The Settings surface may contain:
- volume slider/mute;
- subtitles;
- audio;
- quality;
- speed;
- chapters;
- Learning;
- other low-frequency playback options.

This rule is specific to the compact continuation state; the full Desktop Player keeps its own full-player volume/control composition.

## 5. Video / audio Now Playing — Mobile

Mobile keeps the same transport semantics but adapts the layout.

Preferred composition:

### Row 1
- thumbnail;
- title;
- concise unit context;
- return-to-player/open action;
- explicit Close/Stop.

### Row 2
Centered transport:

```text
Previous · -10 · Play/Pause · +30 · Next
```

If the width cannot fit all five controls at valid touch size:
- keep `-10 · Play/Pause · +30` centered;
- Previous/Next move to the compact Settings/action sheet;
- never move Play/Pause away from the center.

Progress stays as a thin line at the top.

Settings open as a touch sheet.

Volume uses system controls and/or the Settings sheet; no permanent slider.

## 6. Tablet Now Playing

Wide landscape may use the Desktop single-row composition.

Narrow/portrait uses the Mobile two-row composition.

Transport remains centered independently from metadata/settings.

## 7. Now Playing state rules

Now Playing is a projection of the existing `ActiveSession`.

It must preserve:

- current position;
- play/pause state;
- selected audio;
- selected subtitles;
- selected quality/version where session-safe;
- playback speed;
- current canonical unit;
- Learning On/Off state where applicable.

Returning to full Player opens the same session without restarting or seeking.

Navigation/refresh may restore the bar when the server-authoritative ActiveSession is still valid.

Do not create browser-local durable playback truth.

### Close / Stop

Close/Stop explicitly ends the current local playback session.

It does not:
- delete progress;
- mark media completed merely because it was closed;
- cancel acquisition;
- change another user's playback session.

Future cross-device/shared-session behavior must clearly identify which ActiveSession is being stopped.

### Popout/PiP

Popout/PiP is separate from Minimize/Now Playing.

- Minimize -> app remains visible + Now Playing bar.
- Popout/PiP -> platform-managed detached playback where supported.
- Close/Stop -> end session.

Never conflate these actions.

## 8. Continue Reading — Desktop

Continue Reading is intentionally simpler.

Desktop composition:

### Progress row

A thin progress line appears at the top.

It may show:
- read/completed portion;
- chapter/section markers where meaningful;
- current logical progress.

This bar is informational in the continuation surface.

**Do not allow direct scrubbing here.**

Changing reading position happens only after reopening the Reader, preventing accidental progress rewrites from a compact bar.

### Left content

Show:

1. cover;
2. **Work title in bold**;
3. current structure, e.g. `Vol. 4 · Ch. 28 — The Ruined Shrine`;
4. exact useful position, e.g. `Page 5 / 29 · 17%`.

Examples:

Manga:
```text
Frieren
Vol. 7 · Ch. 64
Page 18 / 42 · 63%
```

Book/EPUB:
```text
The Journey
Chapter 12 — Arrival
Page 186 / 412 · 45%
```

### Open Reader hover

Hovering the cover may reveal **Open Reader**.

Clicking:
- cover;
- title/content identity;
- Open Reader hover action;
- Continue Reading button;

all return to the same exact canonical saved locator.

### Primary action

Show one clear primary action:

`Continue Reading`

Do not add Previous/Next chapter, page backward/forward, typography, bookmark, TOC or Reader-setting controls to this bar.

Those belong inside the full Reader.

### Right side

Only:
- primary Continue Reading action;
- explicit Dismiss `×`.

No `...` overflow menu.

There is currently no justified generic overflow action for this surface.

## 9. Continue Reading — Mobile

Mobile uses one compact row above bottom navigation.

Show:

- cover;
- Work title;
- concise current chapter/section;
- page/position + percentage where useful;
- compact Continue Reading button/icon;
- Dismiss `×`.

At narrow widths the Continue action may be icon-only if its accessible label remains `Continue Reading`.

The progress line remains at the top.

Do not add a second row of Reader controls.

Tapping cover/title also continues reading.

## 10. Continue Reading dismissal

Dismiss `×` only hides the continuation surface.

It must **not**:

- delete or reset `MediaProgress`;
- remove bookmarks;
- remove history;
- mark content complete;
- forget the exact resume locator.

The item remains available through normal Continue Reading surfaces elsewhere.

If a product later needs `Reset progress`, that is a separate explicit confirmed action.

## 11. TTS

If TTS is merely configured but not playing:
- Continue Reading semantics remain normal.

If TTS is actively playing:
- it becomes an active audio playback session;
- use the **Now Playing** transport surface;
- do not show both Now Playing and Continue Reading simultaneously.

Stopping TTS returns the persistent slot to Continue Reading when applicable.

## 12. Visual styles

Both surfaces use the global shared skin system.

### Clean
- neutral app surface;
- no decorative Japanese/anime background;
- purple default accent;
- restrained elevation/border.

### Original Jularr
- same exact layout and interactions;
- red/pink default accent;
- restrained Japanese ink/watercolor/cherry-blossom decorative treatment may appear inside/around the component where it does not reduce readability.

Do not create separate component implementations.

Accent/hue shifting uses global semantic design tokens.

Third-party logos and semantic error/success colors are not recolored.

## 13. Light / Dark

Both continuation surfaces support:
- Light;
- Dark;
- System.

The layout does not change between brightness modes.

Playback thumbnails/artwork remain visually primary.

## 14. Accessibility

Now Playing:
- all transport actions have explicit accessible names;
- Play/Pause exposes actual state;
- progress/scrub control exposes current/duration;
- chapter markers have meaningful labels when keyboard/focus accessible;
- Settings gear is labeled;
- Open full Player is keyboard reachable where hover exposes it.

Continue Reading:
- Continue action has a textual accessible name even when icon-only;
- Dismiss explicitly says it hides the continuation surface;
- progress line is announced as read progress, not an editable slider;
- cover hover action has keyboard/focus equivalent.

Both:
- visible focus;
- minimum touch targets;
- no meaning by color only.

## 15. Loading / stale / session-end states

Now Playing:
- if ActiveSession ends elsewhere, remove the bar promptly;
- transient reconnect may show a quiet disabled/reconnecting state;
- do not fabricate playback state locally.

Continue Reading:
- if exact locator temporarily cannot resolve, retain Work/chapter identity and open Reader through its normal recovery path;
- if media becomes unavailable, surface remains readable only when a valid resume destination still exists.

## 16. Shared implementation contract

Recommended shared presentation components:

- `ContinuationSlot`
- `NowPlayingBar`
- `ContinueReadingBar`
- `MediaIdentityCompact`
- `PlaybackTransport`
- `PlaybackProgressBar`
- `ReadingProgressBar`

But state ownership remains:

```text
NowPlayingBar       -> ActiveSession
ContinueReadingBar  -> MediaProgress / Reader locator
```

Never create `MiniPlayerSession`, `ReadingBarProgress` or another durable shadow state.

## 17. Must not implement

- no second playback/progress persistence model;
- no stacked Now Playing + Continue Reading bars;
- no copied full Player toolbar inside Now Playing;
- no permanent volume slider in compact Now Playing;
- no off-center main transport;
- no altered -10 / +30 seek increments;
- no Reader transport controls in Continue Reading;
- no `...` overflow in Continue Reading without a newly approved real use case;
- no reading-progress scrubbing from the continuation bar;
- no progress reset on Dismiss;
- no Popout/Minimize ambiguity;
- no decorative background in Clean;
- no separate Clean/Original component logic.

## 18. Approved mockup direction

The owner-approved direction consists of:

### Now Playing
- Desktop Clean and Original references;
- media thumbnail first;
- bold Work title;
- structural unit line;
- current/duration line;
- full-width progress/buffer/chapter line on top;
- centered Previous / -10 / Play-Pause / +30 / Next;
- right-side direct settings where space permits, then Settings gear;
- volume in Settings;
- thumbnail hover -> Open full Player.

### Continue Reading
- Desktop + Mobile Clean reference;
- Desktop + Mobile Original reference;
- cover + title + chapter/section + exact progress;
- one Continue Reading action;
- Dismiss `×`;
- no overflow;
- no playback-like Reader transport.

The owner will upload the approved mockup images into this folder.

Text specification wins over imagery on conflict.
