# Player — Cross-platform Clean Design

Status: **binding planning specification for Player mockups**. This document refines the baseline in `UX.md` and issue #403. It does not authorize feature implementation before the canonical Playback/File/Track/Progress contracts are ready.

Architecture source of truth:
- `Work -> Structure -> Edition -> Version -> Asset -> File -> Track`
- `PlaybackPlan` decides Direct Play / Direct Stream or Remux / Transcode / Unavailable.
- `ActiveSession` owns the active playback session.
- canonical `MediaProgress` owns resume/completion state.
- learning subtitles are a presentation/learning layer over canonical subtitle cues, not another playback model.

## 1. Purpose

Provide one clean playback experience for canonical playable Assets while preserving the same server-owned playback/session semantics across Web/PWA, Android phone/tablet, TV and iOS/iPadOS WebKit.

The Player must:
- start or resume the selected canonical Work/unit;
- expose only the controls relevant to normal playback;
- make audio, subtitle, quality and speed choices easy to reach without permanent technical clutter;
- support interactive learning subtitles without coupling Learning to player chrome;
- recover from buffering, storage and playback-plan changes without losing meaningful progress;
- keep platform interaction deliberately different where touch, pointer/keyboard, remote and WebKit require it.

The Player is not an Admin diagnostics page and does not choose its own media-processing strategy.

## 2. Route / entry contract

Entry can come from:
- Continue / Up Next;
- Anime/Series episode action;
- Movie Play/Continue;
- Audiobook Listen/Continue where the shared playback shell is appropriate;
- a compact Now Playing continuation surface;
- a deep link to a canonical playable unit.

The route receives canonical identity, never a raw filesystem path or legacy Anime/Episode-only playback identity.

Before playback the application resolves:
1. canonical Work/unit;
2. user/profile and playback preferences;
3. available Version/Asset/File/Track choices;
4. client capability document;
5. PlaybackPlan;
6. ActiveSession;
7. exact resume position.

Opening an item sets/updates the current item and resume state but **does not mark it completed**.

## 3. Primary page structure

The full Player is a media-first surface with five conceptual layers. These layers must stay independent so hiding controls never hides subtitles or learning UI.

### Layer A — media surface

Full available viewport behind all controls.

Video:
- preserve source aspect ratio by default;
- support Fit / Fill / Zoom;
- no decorative poster frame while video is playing;
- letterbox/pillarbox uses player background, not application-page chrome.

Audio-only mode:
- use a restrained artwork/backdrop treatment;
- keep the same transport/session model;
- do not invent a separate audio progress store.

### Layer B — subtitle layer

Normal subtitle rendering is centered in the safe subtitle zone and remains independent of transient controls.

Requirements:
- readable against both bright and dark frames;
- safe distance from bottom timeline/control chrome;
- preserve positioning/styling where supported and appropriate;
- embedded, external and generated subtitle provenance is not shown as permanent badges.

### Layer C — interactive learning subtitle layer

When learning mode is enabled, interactive cues sit in their own hit-test layer.

Rules:
- remain visible when normal player controls auto-hide;
- tapping a term/sentence must never trigger generic player tap/click gestures;
- opening a word/sentence detail must preserve playback position, selected tracks and paused/playing state;
- returning closes the learning detail without recreating the playback session;
- if learning text is unavailable, normal playback remains fully usable.

Desktop can use a compact anchored popover/side panel. Tablet landscape may use a side sheet. Mobile uses a bottom sheet. TV uses a remote-focusable overlay/panel with large targets and no pointer assumptions.

### Layer D — transient player chrome

Top chrome:
- Back/Close;
- concise Work + unit title;
- optional episode/chapter context;
- Cast / PiP / overflow only when actually supported;
- no permanent technical playback details.

Center:
- large Play/Pause affordance while controls are visible;
- contextual +/-10 second feedback;
- buffering indicator only when buffering is real.

Bottom:
- current time;
- timeline / seek bar;
- duration / remaining time where appropriate;
- primary transport controls;
- volume where platform/input supports it;
- secondary actions for audio, subtitles, quality, speed and display mode;
- fullscreen where platform supports custom fullscreen.

Controls auto-hide only during active playback and only when no menu, sheet, learning surface or focused TV control requires them.

### Layer E — sheets / menus / diagnostics

Menus never navigate away from the active Player.

Shared menu groups:
- Audio;
- Subtitles;
- Quality;
- Speed;
- Fit / Fill / Zoom;
- Chapters / skip segments where available;
- Diagnostics in overflow;
- Learning toggle/tools only when capability and content allow it.

Only one secondary panel should be open at a time.

## 4. Desktop composition

Pointer + keyboard first.

### Layout
- media fills viewport;
- top title row is compact;
- timeline spans most of the bottom width;
- primary controls sit below/around timeline without a second heavy toolbar;
- audio/subtitle/quality/speed use compact anchored menus;
- diagnostics opens as an on-demand side panel or modal, not permanent text.

### Pointer behavior
- single click on unobstructed video = Play/Pause;
- double click on unobstructed video = enter/exit fullscreen;
- moving pointer reveals controls;
- moving pointer out / inactivity hides controls while playing;
- click on subtitle-learning content never falls through to video;
- timeline hover shows time preview and thumbnail when available;
- chapter/segment markers may appear on the timeline without turning it into a dense editor.

### Keyboard baseline
- Space / K: Play/Pause;
- Left/Right: short seek;
- J/L may map to backward/forward seek where supported by the global shortcut policy;
- Up/Down: volume;
- M: mute;
- F: fullscreen;
- Escape: close menus/fullscreen before leaving Player.

Do not introduce shortcuts that conflict with text input or accessibility behavior.

### Desktop mockup states required
1. playing with chrome visible;
2. playing with chrome hidden + learning subtitles visible;
3. audio/subtitle menu open;
4. diagnostics panel;
5. buffering/error overlay.

## 5. Mobile composition

Touch-first. Portrait and landscape are intentionally different compositions.

### Portrait
- video/media surface occupies the upper available area when not fullscreen;
- large center Play/Pause;
- bottom transport uses large touch targets;
- secondary controls go into a bottom sheet/overflow instead of a dense toolbar;
- safe areas and browser/PWA chrome are respected;
- compact title/context at top.

### Landscape / fullscreen
- media uses the full safe viewport;
- controls overlay the media;
- timeline stays reachable without crowding subtitle safe zones;
- learning details use a bottom/side sheet depending on available width.

### Touch behavior
- single tap toggles controls only;
- double tap left = -10 seconds;
- double tap right = +10 seconds;
- double tap center must not be overloaded;
- left vertical gesture may control brightness only where the client can reliably support it;
- right vertical gesture may control volume only where supported;
- pinch controls Fit/Fill/Zoom where supported;
- screen/control lock disables accidental transport gestures and exposes a clear unlock affordance;
- interactive subtitle taps take precedence over player gestures.

No gesture is allowed to silently perform an unsupported platform action.

### Mobile mockup states required
1. portrait paused;
2. portrait playing + controls;
3. landscape playing + controls;
4. locked-controls state;
5. learning bottom sheet;
6. track/quality bottom sheet;
7. recoverable playback error.

## 6. Tablet composition

Tablet keeps the touch interaction model but uses the additional width.

Portrait:
- close to Mobile with wider bottom sheets;
- title and transport may remain visible with more breathing room.

Landscape:
- full media-first composition;
- learning details may use a right side sheet while video remains visible;
- audio/subtitle/quality sheets can be narrower anchored side sheets rather than full-width mobile sheets;
- no desktop-only hover assumptions.

iPadOS Safari/PWA follows the WebKit compatibility rules below.

## 7. TV composition

Remote-first. TV is not a scaled desktop Player.

### Layout
- media fills TV safe area;
- very large center Play/Pause when controls are shown;
- one clear bottom timeline row;
- one clear row of primary/secondary actions;
- title/context is short and readable at distance;
- no tiny status icons or pointer-only tooltips.

### Focus
Focused controls use the approved TV language:
- strong accent focus ring/glow;
- modest scale/lift;
- high contrast;
- predictable D-pad path with no traps.

### Remote behavior
- Play/Pause key directly toggles playback;
- media seek keys seek directly where available;
- Left/Right while timeline/seek mode is focused moves through time with visible feedback;
- Up/Down moves between control rows/panels;
- Back closes the deepest open panel first, then player controls/fullscreen context, then exits Player;
- no action requires a mouse or hover state.

Audio/subtitle/quality/speed open large focusable panels.

Learning mode must be usable with D-pad focus. Dense dictionary content should not cover the whole media surface unless the user explicitly opens details.

### TV mockup states required
1. playing with controls visible;
2. strong focused transport action;
3. seek state;
4. subtitle/audio panel;
5. learning overlay;
6. playback failure/retry state.

## 8. iOS / iPadOS WebKit path

Use capability detection and progressive enhancement. Do not fork server/domain playback rules.

Where WebKit permits:
- keep Jularr inline controls;
- use normal PlaybackPlan/ActiveSession;
- support fullscreen, PiP, Media Session and wake-lock-like behavior only when the platform actually exposes it.

Where WebKit requires system surfaces:
- degrade deliberately to supported system playback/fullscreen controls;
- preserve canonical progress, track preferences and session identity;
- never claim a custom control exists when WebKit does not allow it;
- return from system fullscreen/native surfaces to the same logical ActiveSession.

The mockup represents the intended Jularr inline state, but implementation may use a system-control fallback for unsupported WebKit capabilities.

## 9. Timeline, buffer, chapters and skip segments

The timeline is the primary playback-position surface and must clearly separate **played**, **buffered** and **remaining** media.

Timeline must show:
- played position;
- buffered range whenever the delivery layer can report it reliably;
- remaining/unbuffered range;
- current time and duration;
- hover/seek preview on Desktop where available;
- chapter markers;
- canonical intro/recap/outro/credits/preview markers only when resolved and above the configured confidence threshold.

### Buffer visualization

Buffer must be visible directly in the timeline as a secondary fill/range behind the played position.

When playback actually stalls waiting for media:
- preserve the current frame where possible;
- show a centered buffering indicator after a short delay so tiny network fluctuations do not flash UI;
- if the stall becomes prolonged, add a concise `Buffering…` status;
- keep the timeline and valid controls accessible;
- do not show a fake percentage unless the delivery layer exposes a meaningful buffer/download value.

Buffer display is distinct from acquisition/download progress. Normal playback buffering must never expose indexer or download-client internals.

### Chapters

Chapters are visible in two places:
1. discrete markers on the timeline;
2. a **Chapters** menu/sheet listing chapter title and start time.

Behavior:
- selecting a chapter seeks to its canonical start position;
- the current chapter is clearly selected;
- Desktop hover/focus may show the chapter title near its marker;
- Mobile/Tablet use a touch-friendly sheet;
- TV uses a large D-pad-focusable chapter list;
- chapter markers must remain visually subordinate to the main played/buffered timeline.

### Manual skip actions

Eligible canonical segments expose contextual, real buttons:
- **Skip intro**
- **Skip recap**
- **Skip outro**
- **Skip credits**
- **Skip preview** where applicable

Rules:
- button appears only while current playback position is inside the corresponding eligible segment;
- button is directly reachable in player chrome, not hidden only in a settings menu;
- Desktop keeps it compact but obvious;
- Mobile/Tablet use a large touch target;
- TV exposes it as a first-class focusable action;
- the segment is also marked on the timeline;
- button disappears after leaving/skipping the segment;
- segment editing never happens inside the normal Player.

### Optional Auto-Skip

Playback settings include per-profile/user options:
- **Auto-skip intro** — Off/On;
- **Auto-skip recap** — Off/On;
- **Auto-skip outro/credits** — Off/On.

Default for all automatic skip options is **Off**.

When Auto-Skip is enabled:
- only canonical resolved segment markers may trigger it;
- configured confidence/policy thresholds still apply;
- Jularr seeks to the segment end automatically;
- show a brief non-blocking confirmation such as `Intro automatically skipped`;
- expose **Undo** for a short period, returning to the segment start;
- auto-skip must never mark an episode completed by itself;
- an unavailable/ambiguous segment is never guessed client-side.

These preferences belong to normal Playback settings and are reused across clients. They must not be implemented as separate per-platform rules.

Segment correction/editing is an Admin/Episode-detail concern.

## 10. Audio, subtitles and quality

### Audio
Each option shows user-meaningful data:
- language;
- track title when useful;
- channel/layout where helpful.

Prefer the profile/user default, then the PlaybackPlan-selected compatible track.

### Subtitles
Options:
- Off;
- available canonical subtitle tracks/files;
- learning subtitle mode where available.

Normal subtitle selection and learning subtitle interaction remain distinct concepts.

If the selected subtitle forces remux/transcode/burn-in, the server resolves a new PlaybackPlan. The client does not decide this locally.

### Quality
Normal choices:
- Automatic;
- Original;
- configured bitrate presets such as 20 / 12 / 8 / 4 / 2 / 1 Mbps where enabled.

Do not present resolution-only labels if the actual decision is bitrate/network constrained.

Changing quality requests a new plan/session delivery while preserving position and track selections.

## 11. Playback mode status and diagnostics

Normal UI may show a compact status in overflow/details:
- Direct Play;
- Direct Stream / Remux;
- Transcode;
- Automatic quality.

Detailed diagnostics are explicitly secondary.

Diagnostics may show:
- PlaybackPlan mode;
- exact reasons / “Why not Direct Play?”;
- source/delivered container and codecs;
- resolution/bitrate;
- selected audio/subtitle;
- throughput and buffer health;
- transcode encoder/speed when applicable;
- dropped frames/segment status where available.

Diagnostics must never expose raw filesystem paths, secrets or arbitrary FFmpeg command strings.

## 12. Progress and completion semantics

The Player writes through canonical session/progress contracts.

Keep separate:
- CurrentItem;
- ResumePosition;
- CompletedThrough;
- external ProviderProgress.

Rules:
- opening or starting item N does not mark N completed;
- pause/seek/resume does not mark N completed;
- periodic heartbeats update exact resume position;
- meaningful state flushes on pause, seek completion, visibility/background transition, route leave and Player close;
- completion occurs only through the canonical completion policy, explicit Mark Watched, or a safe sequential-next transition;
- provider write-back uses completed progress, never merely the current item/resume position;
- progress survives delivery changes between Direct Play/Remux/Transcode.

Example:
`Episode 12 at 08:31` may coexist with `CompletedThrough = 11`.

## 13. Previous / Next behavior

Previous/Next targets canonical structure.

Series/Anime:
- Next resolves the next canonical episode according to selected context and policy;
- beginning playback of the next episode may finalize the previous episode only when the canonical completion policy allows it.

Movie:
- no fake Next button unless there is an explicit playlist/queue context.

Audio/Audiobook:
- previous/next may target canonical chapter/track boundaries.

When the next item is not locally ready but user policy allows instant acquisition:
- show Preparing state;
- keep current/ended context visible;
- auto-start only after the normal Play/Read acquisition contract says the requested item is ready.

## 14. Preparing / loading / partial states

### Resolving
Short initial state while capabilities, file availability and PlaybackPlan are resolved.

Show:
- media title/context;
- neutral progress indicator;
- no fake timeline.

### Preparing / acquiring
Used when content is not ready locally but the play action triggered the acquisition flow.

Show compact user information such as:
- Preparing;
- requested language/profile summary if useful;
- progress only when reliable;
- Cancel/Back only when policy permits.

Do not expose indexer/download-client internals.

### Remux/transcode startup
Show a normal loading state. Only show technical reason inside diagnostics.

### Buffering
Keep the current frame when possible, show a center spinner after a short delay, keep controls available, and continue to show the buffered range in the timeline. For prolonged stalls add a concise `Buffering…` label. Do not show unreliable percentages.

### Storage waking/offline
Explain that media storage is unavailable/waking and expose Retry/Back. Do not classify it as codec failure.

## 15. Empty / unavailable states

A full Player should rarely have a classic empty state. Use explicit unavailable states instead:

- no playable Asset/File;
- selected File disappeared;
- storage root unavailable;
- no compatible playback plan;
- permission revoked;
- requested track no longer available.

Each state must offer only valid next actions:
- Retry;
- choose another version/track;
- Request/Prepare when allowed;
- Back.

Never show raw server exceptions.

## 16. Error and recovery behavior

Errors are categorized, not collapsed into “Playback failed”.

Recoverable examples:
- network interruption;
- expired delivery session;
- failed Direct Play followed by server-resolved fallback plan;
- storage wake delay;
- selected track becoming unavailable.

Recovery rules:
- preserve position and track choices;
- report failed playback mode to the server;
- ask the server for a replacement PlaybackPlan;
- do not locally invent a fallback matrix;
- a network failure is not automatically a codec/capability failure.

Permanent/unsupported errors provide a concise reason and Back/Diagnostics actions.

## 17. Ended state

At completion:
- keep final frame/backdrop;
- clearly show Replay;
- show Next Episode/Next Chapter only when a real canonical next target exists;
- optionally show brief Up Next countdown only when autoplay is enabled;
- allow cancellation of autoplay;
- update completion through canonical policy before provider write-back.

No recommendation wall should obscure completion controls.

## 18. Continuation / mini-player

Leaving the full Player through normal Jularr navigation should preserve the ActiveSession where technically feasible.

Desktop/tablet:
- compact Now Playing bar with thumbnail, title/unit, progress, Play/Pause and return-to-player action.

Phone:
- compact bar above bottom navigation;
- thumbnail/title + Play/Pause + close;
- never cover navigation, keyboard, sheets or safe areas.

Rules:
- return opens the same ActiveSession;
- progress updates live;
- Close/Stop is explicit;
- selected audio/subtitle/quality state is preserved;
- no separate mini-player progress/session store.

TV baseline does not require a floating mini-player overlay across browse screens; any continuation behavior must still use the same ActiveSession contract.

## 19. Light / Dark

The media surface is intentionally dark/media-led in both application themes.

Dark application theme:
- dark neutral overlays/sheets;
- Fluent-style elevation;
- subtle translucent control backdrop.

Light application theme:
- do **not** turn the video surface white;
- player chrome remains dark/translucent for contrast;
- secondary dialogs/sheets opened outside the media surface may use light theme tokens;
- text/icons meet contrast requirements in both modes.

Accent is used for:
- timeline played state;
- focus/selected state;
- active menu item;
- TV focus ring.

No neon/glass-heavy styling and no theme-specific layout fork.

## 20. Accessibility

Required:
- keyboard operability on desktop;
- visible focus;
- screen-reader labels for icon-only actions;
- no color-only state communication;
- minimum touch targets on touch devices;
- captions/subtitles remain readable with user text scaling where technically possible;
- reduced-motion preference disables unnecessary scale/animation while retaining clear focus;
- TV focus remains obvious without relying only on animation.

## 21. Required mockup set

Create mockups in this order:

1. **Desktop Dark — primary playing state**  
   Establish hierarchy, timeline, transport and secondary controls.

2. **Desktop Dark — learning state**  
   Controls mostly hidden, learning subtitle + word/sentence interaction visible.

3. **Mobile Dark — portrait paused/controls**  
   Establish touch sizing and compact hierarchy.

4. **Mobile Dark — landscape playback**  
   Establish gesture zones, safe areas and subtitle placement.

5. **TV Dark — focused controls**  
   Establish remote focus language and seek hierarchy.

6. **Tablet Dark — landscape learning side sheet**  
   Only after Mobile/Desktop interaction is stable.

7. **Light-theme validation references**  
   Desktop and Mobile secondary sheet/dialog states. The media surface itself remains dark.

8. **Error/Preparing reference**  
   One platform reference is enough if state treatment is shared; platform-specific composition rules still apply.

Do not produce decorative variants before these behavioral references are approved.

## 22. Data / information contract shown by the UI

The screen consumes view data derived from:
- canonical Work and structural unit;
- selected Edition/Version when user-relevant;
- Asset/File availability;
- canonical MediaTrack list;
- duration/current position;
- PlaybackPlan and safe user-facing reasons;
- ActiveSession;
- canonical progress/completion state;
- buffered time ranges when the delivery/client can report them;
- chapters/segments and their confidence/policy eligibility;
- profile/user Auto-Skip playback preferences;
- subtitle cues / learning capability;
- client feature capabilities;
- preparing/acquisition state when Play triggered missing-media acquisition.

The Player must not query legacy Anime/Episode-only tables as its permanent source of truth.

## 23. Must not implement

- No client-specific playback decision engine.
- No second Player progress or session database.
- No permanent dependency on legacy `Anime.Id`, legacy `Episode.Id` or anime-only `MediaFile` ownership.
- No arbitrary filesystem path or FFmpeg command supplied by the client.
- No `iOS = transcode`, `MKV = transcode` or similar hard-coded platform decision matrix in UI code.
- No desktop “single click toggles controls” behavior; desktop single click on unobstructed video is Play/Pause.
- No learning subtitle hit target that falls through to generic player gestures.
- No hiding learning subtitles merely because transient player controls hide.
- No TV mouse/hover assumptions.
- No dense technical codec/bitrate panel in the default Player.
- No admin release/indexer/download information in normal playback UI.
- No raw exception text.
- No automatic completion merely because an item was opened or started.
- No provider write-back from CurrentItem/ResumePosition.
- No separate subtitle identity model for learning.
- No permanent mini-player state disconnected from ActiveSession.
- No duplicated platform pages that reimplement business rules independently.
- No client-guessed intro/outro/chapter boundaries.
- No Auto-Skip enabled by default.
- No Auto-Skip without a canonical eligible segment marker.

## 24. Mockup acceptance checklist

A Player mockup is acceptable only when:
- media remains visually primary;
- normal, subtitle and learning layers are visibly separable;
- Desktop click behavior can be implemented without ambiguity;
- Mobile gesture/tap regions do not conflict with learning subtitles;
- TV has a complete D-pad focus path;
- audio/subtitle/quality are accessible without persistent clutter;
- Light/Dark contrast behavior is defined;
- played/buffered/remaining timeline states are visually distinct;
- chapters and segment markers are visible without making the timeline noisy;
- manual Skip actions are direct contextual buttons;
- Auto-Skip has explicit settings, defaults Off and provides temporary Undo feedback;
- preparing/loading/buffering/error states have clear treatment;
- completion vs resume semantics are not visually conflated;
- no legacy media model is implied;
- no control depends on a capability the platform may not have without a fallback state.
