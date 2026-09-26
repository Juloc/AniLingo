# Android, Android TV and Companion Architecture

Status: **accepted future architecture**  
Tracking issue: **#69**  
Decision date: **2026-09-22**

This document is the implementation contract for AniLingo's first-party Android phone and Android TV clients. It intentionally fixes the product and architecture decisions before implementation so future work does not rediscover or reinterpret them.

If an implementation detail below must change because of a platform/API constraint, change this document and #69 in the same PR. Do not silently create an alternative architecture.

## 1. Product model

AniLingo remains one product with one self-hosted server.

The server remains authoritative for:

- library/media identity
- episode/track/cue identity
- vocabulary and learning state
- dictionary and grammar/context data
- metadata
- playback-session state used for TV/phone companionship
- server-side remux/transcode fallback

Clients do not create competing durable copies of those facts.

Supported front ends are:

| Surface | Shell | Playback | Learning interaction |
| --- | --- | --- | --- |
| Browser | existing responsive Razor UI | web player | full |
| Android phone | existing AniLingo web UI inside same-origin WebView | native Media3 player | full |
| Android TV | native Compose for TV UI | native Media3 player + MediaSession | remote-optimized + handoff to phone |

The phone app is deliberately **not** a second implementation of Library, Learn and Settings. The TV app is deliberately **not** a WebView wrapper.

## 2. Non-negotiable rules

1. Original anime media remains read-only.
2. No second database or client-side source of truth for learning/media state.
3. Native clients use a versioned AniLingo API and never scrape Razor HTML.
4. Device playback is preferred. Server video conversion is fallback behavior.
5. No mandatory pre-encode step before Play.
6. Web, phone and TV use one player vocabulary and one visual design contract.
7. Subtitle learning uses normalized AniLingo subtitle cues, not platform-specific subtitle text parsing.
8. TV/phone pairing uses AniLingo as the coordinator. Bluetooth, Chromecast or vendor APIs are not the state layer.
9. Pairing is session-scoped and does not grant general server access.
10. Android APK signing credentials exist only in protected GitHub secrets/environment.
11. Browser use remains fully supported when no Android app is installed.

## 3. Android repository layout

All native Android code lives in one Gradle build:

```text
clients/android/
├── settings.gradle.kts
├── build.gradle.kts
├── gradle.properties
├── gradle/
│   └── libs.versions.toml
├── app-mobile/
├── app-tv/
├── core-api/
├── core-model/
├── core-player/
├── core-session/
└── core-design/
```

Modules:

- `app-mobile`: phone application, WebView shell, native player host, app settings.
- `app-tv`: Android TV application, browse/detail/player surfaces, remote focus handling.
- `core-api`: HTTP + SignalR transport for the versioned AniLingo client API.
- `core-model`: API DTOs and shared immutable client models.
- `core-player`: Media3/ExoPlayer integration, direct/fallback selection, audio tracks, playback state.
- `core-session`: playback-session/pairing/companion protocol.
- `core-design`: generated player theme values and reusable native player controls.

Application IDs:

- phone: `de.juloc.anilingo`
- TV: `de.juloc.anilingo.tv`

Baseline:

- Kotlin
- Gradle Kotlin DSL
- Java 17 toolchain
- minSdk 26
- current stable compile/target SDK when implementation begins
- dependency versions pinned in `libs.versions.toml`
- AndroidX Media3/ExoPlayer
- Jetpack Compose for native UI
- Compose for TV components for TV surfaces

Do not introduce a separate Android repository.

## 4. Server API boundary

Native clients use `/api/client/v1`. Razor page handlers remain for web UI and are not the native-client contract.

Core v1 endpoints:

```text
GET  /api/client/v1/capabilities
GET  /api/client/v1/me
GET  /api/client/v1/library
GET  /api/client/v1/anime/{animeId}
GET  /api/client/v1/episodes/{episodeId}
GET  /api/client/v1/episodes/{episodeId}/player
GET  /api/client/v1/episodes/{episodeId}/cues?trackId={trackId}&fromMs={fromMs}&toMs={toMs}
GET  /api/client/v1/media/{mediaFileId}/availability
GET  /api/client/v1/media/{mediaFileId}/content
GET  /api/client/v1/episodes/{episodeId}/fallback?mode={device|server}&startSeconds={seconds}
GET  /api/client/v1/terms/{termId}
PUT  /api/client/v1/terms/{termId}/state

Owner-only storage operations:

GET  /api/client/v1/library-roots/{rootId}/availability
POST /api/client/v1/library-roots/{rootId}/test
POST /api/client/v1/library-roots/{rootId}/wake
```

All endpoints except `/capabilities` use the normal AniLingo authenticated account and therefore the same profile-scoped learning state as the web UI. API authentication failures return JSON `401/403` responses instead of redirects to Razor login pages.

Playback continuity endpoints (additive v1, advertised by `episodeFlow`, `continueWatching` and `playbackHistory`):

```text
GET    /api/client/v1/episodes/{episodeId}/progress
PUT    /api/client/v1/episodes/{episodeId}/progress        { positionMs, durationMs, completed }
PUT    /api/client/v1/episodes/{episodeId}/watched         { watched }
GET    /api/client/v1/episodes/{episodeId}/flow            previous/next local episode + autoplayNext
GET    /api/client/v1/continue-watching
GET    /api/client/v1/me/playback-preferences
PUT    /api/client/v1/me/playback-preferences              { autoplayNext }
GET    /api/client/v1/me/playback-history
DELETE /api/client/v1/me/playback-history
```

The server is the only durable owner of resume position, watched state, autoplay preference and history; clients must not keep a second durable progress store. Clients should resume from `resumePositionMs` (zero means start from the beginning), send bounded checkpoints (for example every 15 seconds while playing plus pause/stop/end) and use `/flow` instead of computing next/previous episodes locally. The semantics are described in the README section *Playback continuity*.

Bounded offline playback endpoints (additive v1, advertised by `offlineDownloads`; see §8.2):

```text
GET  /api/client/v1/episodes/{episodeId}/offline-download   download descriptor
GET  /api/client/v1/offline/media/{mediaFileId}/content      original bytes, HTTP range + strong ETag
POST /api/client/v1/offline/progress                         { items: [{ episodeId, positionMs, durationMs, completed }] }
```

The first v1 contract deliberately reports not-yet-implemented facilities through capability flags. HLS fallback, playback sessions, pairing and companion control remain `false` until their later delivery slices are merged. Clients must not infer support from route guesses.

Later session/companion endpoints extend the same v1 boundary:

```text
POST /api/client/v1/playback-sessions
GET  /api/client/v1/playback-sessions/{sessionId}
POST /api/client/v1/playback-sessions/{sessionId}/pair-code
POST /api/client/v1/playback-sessions/{sessionId}/commands
POST /api/client/v1/playback-sessions/{sessionId}/leave
POST /api/client/v1/pair
```

Real-time endpoint:

```text
/hubs/playback
```

HTTP is used for bootstrap, lookup and command requests that need explicit success/failure. SignalR is used for session-state broadcasts and immediate companion updates.

### 4.1 Capabilities

`GET /capabilities` returns at least:

- API version
- minimum supported client API version
- server product version
- feature flags for native playback, HLS fallback, pairing and companion control

A client with an incompatible API version stops with a clear **server/app update required** state. It must not fall back to scraping pages.

### 4.2 Player bootstrap

`GET /episodes/{id}/player` returns one canonical bootstrap model:

- episode/anime identity and display title
- duration
- media/container identity
- video codec/profile/level when known
- audio tracks with stable track IDs, language, title and codec
- subtitle tracks with stable track IDs, language, title, text/image capability and active learning-source flag
- direct-content endpoint
- direct content type and range capability
- compatibility fallback descriptor
- selected/default audio/subtitle preferences
- cue endpoint for the active learning subtitle
- active playback-session information when applicable
- canonical skip-segment markers and the seek-preview (trickplay) descriptor, see [MEDIA_SEGMENTS.md](MEDIA_SEGMENTS.md#client-descriptor)

Never expose host filesystem paths to clients.

### 4.3 Media endpoints

Direct media endpoint must support HTTP range requests and read the original file without modification.

Native compatibility fallback is HLS with fragmented MP4 segments:

```text
GET /api/client/v1/media/{mediaFileId}/content
GET /api/client/v1/media/{mediaFileId}/hls/{playbackId}/master.m3u8
GET /api/client/v1/media/{mediaFileId}/hls/{playbackId}/{segment}
```

The HLS cache is bounded and disposable. It belongs under `/data`, never beside source media.

The current v1 API also exposes the existing on-demand fragmented-MP4 compatibility stream at `/episodes/{episodeId}/fallback`. It can restart at a supplied playback position but is not seekable within the live response. This is explicitly advertised as `liveMp4Fallback=true` and `hlsFallback=false`; native clients must switch to HLS behavior only after the HLS capability becomes true.

The existing browser fragmented-MP4 path may remain while native support is added. Long term, web may consume the same HLS compatibility path, but the Android milestone does not block on that migration.

## 5. Playback selection

### 5.1 Native algorithm

For both phone and TV:

1. Fetch player bootstrap.
2. Inspect the device's Media3 decoder/container capabilities.
3. If supported, open the original media endpoint directly.
4. Prefer hardware decoding when Android/Media3 provides it.
5. Apply requested audio track.
6. Render the learning subtitle through AniLingo's cue overlay.
7. If direct playback fails because of codec/container/decoder support, record the current position and track choices.
8. Start the server HLS compatibility stream.
9. Restore position and track choices.
10. Continue automatically; do not ask the user to press Prepare or start again.

A network error is not treated as a codec failure. Network retry and codec fallback are separate states.

### 5.1.1 Media-storage availability and recovery

Before treating a playback failure as a decoder/container problem, clients consume the server's media availability state:

- `available`
- `source_starting`
- `source_offline`
- `source_unreachable`
- `file_missing`
- `unknown`

Temporary source outages are retryable and use the shared bounded recovery cadence: immediate check, then 1s, 2s, 4s, and 5s intervals up to about 60 seconds. The client preserves the requested playback position and whether the user intended playback to continue. Once storage becomes available, it refreshes the player bootstrap and resumes automatically.

`file_missing` is not a Wake-on-LAN or codec-fallback state: the root is readable but the concrete media file is absent. `source_unreachable` is also not blindly retried forever.

Normal users can query only the media availability information needed for playback. They never receive MAC addresses, mount paths or an implicit wake capability. Owners may receive a session-independent `wakeUrl` for the owning root when Wake-on-LAN is configured. Wake remains an explicit owner action; pressing Play never wakes storage automatically.

The server keeps library state while a NAS is sleeping/offline and treats an unexpectedly empty previously-populated root as unavailable instead of a mass deletion. Native clients therefore keep library/detail navigation usable while playback storage is down.

### 5.2 Fallback profile

The first compatibility profile is:

- H.264/AVC video
- yuv420p
- AAC audio when audio conversion is required
- fragmented MP4 HLS segments
- seekable after segments exist
- generated on demand
- bounded cache cleanup

Compatible video/audio streams may be copied rather than re-encoded when the resulting stream is valid.

Hardware server transcoding is a later optimization behind the same fallback contract. It must not change the client API.

### 5.3 Native MediaSession

The TV app exposes its player through Android MediaSession so hardware remotes and Android TV system playback controls can control the active episode.

The phone native player should also use MediaSession for normal Android media controls.

## 6. Subtitle and learning model

The video renderer and learning subtitles are separate concerns.

Media3 may expose embedded subtitle tracks for normal playback selection, but AniLingo's interactive Japanese subtitle is rendered from the server's normalized cue model so behavior is identical for:

- nearby SRT/ASS
- extracted embedded text subtitles
- manually selected embedded text streams
- generated Japanese audio transcription

Each cue has a stable cue ID, start/end time and normalized learning representation.

The client synchronizes cue display against player position. The server remains authoritative for term IDs, readings, meanings and learning state.

### 6.1 Common actions

Every player supports the same semantic actions:

- `playPause`
- `seekBack10`
- `seekForward10`
- `seekTo`
- `selectAudioTrack`
- `selectSubtitleTrack`
- `repeatCurrentCue`
- `learnCurrentCue`
- `openWord`
- `markKnown`
- `addToLearning`
- `openCompanion`
- `closeOverlay`
- `exitPlayer`

Platform controls invoke these actions. Platforms must not invent alternative meanings for the same action.

### 6.2 Learn-current-line behavior

When the user opens learning for the current cue:

1. remember whether playback was playing
2. pause if it was playing
3. freeze the selected cue
4. show tokenized words
5. word selection opens reading, meaning and learning state
6. grammar/context explanation is shown when available
7. Repeat seeks to cue start and plays the cue again
8. closing the learning UI returns to the previous player layer
9. playback resumes only if it was playing before learning opened and the user did not explicitly pause during the learning interaction

This rule is shared by browser, phone and TV.

## 7. Player visual contract

The goal is one AniLingo player, not three unrelated players with similar colors.

Canonical design input lives under:

```text
design/player/
├── player-tokens.json
└── player-icons.json
```

`player-tokens.json` owns:

- overlay/background opacity
- text/background contrast values
- radius scale
- spacing scale
- control sizes
- typography scale
- subtitle typography
- subtitle background/padding
- focus/selected/pressed states
- animation durations
- control auto-hide durations
- TV scaling/safe-area multipliers

`player-icons.json` owns semantic icon IDs and vector path data. Build tooling generates/validates the web and Android forms from these canonical inputs. Do not hand-maintain separate semantic icon sets.

The generated outputs are implementation artifacts; the JSON inputs are the source of truth.

### 7.1 Shared layout

Visual order is consistent:

- video surface
- centered transport affordance when invoked
- Japanese subtitle above the bottom control area
- progress/timeline
- primary controls
- secondary controls for audio, subtitles, playback mode and learning
- sheets/overlays above the player without navigating away from the scene

The TV version scales controls and safe areas for distance viewing but preserves the same hierarchy and icon/label meanings.

### 7.2 Player status

Playback transport is visible as a small status, not a different UI mode:

- **Direct**
- **Remux** when relevant
- **Server**

Users can inspect it, but normal Auto behavior does not require choosing a mode before pressing Play.

## 8. Browser/phone interaction

Browser and phone native player behavior:

- single tap video: show/hide controls
- double tap left video zone: -10 seconds
- double tap right video zone: +10 seconds
- drag/scrub timeline: seek
- tap Japanese subtitle: Learn current line
- tap word in learning sheet: word details
- Repeat line: seek to cue start and replay
- Back/close: close topmost sheet before leaving player

Controls auto-hide while playing after a short idle period. They remain visible while paused or while a menu/sheet is open.

### 8.1 Hybrid phone shell

The phone application's main surface is a WebView loading only the configured AniLingo server origin.

Rules:

- app pages: same-origin WebView
- external links: system browser/custom tab
- TLS errors: never bypassed automatically
- no broad `addJavascriptInterface`
- no arbitrary remote origin inside the app shell
- WebView cookies/session belong to the configured AniLingo origin

AniLingo has one canonical episode Play route. The WebView shell intercepts that same-origin route and opens the native player using the episode ID. The website still handles the route normally in a regular browser.

When native playback closes, the user returns to the same WebView history/navigation state.

The Companion screen itself remains a normal responsive AniLingo web page and therefore appears identically in a browser or inside the phone app.

### 8.2 Bounded offline playback (phone, #225)

The phone can keep explicitly chosen episodes of the user's own library on the device. This is deliberately not a download platform: no DRM, no license server, no automatic/Smart Downloads, no browser/PWA offline video and no bulk mirroring.

Server contract:

- `GET /episodes/{id}/offline-download` returns the episode/anime titles, audio and subtitle track metadata with defaults, the complete active Japanese learning cue set (tokens include reading, meaning and the learning state at download time), the canonical progress snapshot, and the media identity: canonical `sizeBytes` of the file on disk, a strong `eTag` (length + modification time) and a bounded content `fingerprint` (`sha256-length-head-tail-64k`: SHA-256 over the little-endian length, the first and the last 64 KiB; the same fingerprint the media inventory persists). Unavailable storage answers like the content endpoint (`503`/`404` with the availability JSON).
- `GET /offline/media/{id}/content` serves the same read-only original bytes as `/media/{id}/content`, plus the strong `ETag`, so a resumed request uses `Range` + `If-Range` and can never splice bytes of two file versions. Source media is never modified or copied on the server.
- `POST /offline/progress` replays checkpoints recorded while offline (at most 100 per request) through the canonical `EpisodeProgressService`. Reconciliation is monotonic and idempotent without any server-side per-client state: a checkpoint only moves the resume position forward or marks an unwatched episode watched; watched stays sticky (a stale partial checkpoint neither un-watches an episode nor replaces a newer rewatch position; a replayed completion is `unchanged`); the 30 s accidental-start rule applies unchanged. Every item gets a final outcome: `applied`, `completed`, `unchanged`, `ignored_behind`, `ignored_watched`, `ignored_accidental_start` or `episode_not_found`, plus the resulting canonical progress. A live `PUT /progress` keeps its normal semantics (it may rewind during a rewatch).

Phone implementation (`app-mobile`, package `mobile.offline`; TV is out of scope for now):

- The native player shows an explicit **Download** action when the server advertises `offlineDownloads`. Downloads are managed from the **Downloads** screen (player, download notification, launcher shortcut, or the *Server unavailable* screen): per-episode state (queued, downloading, paused, ready, failed) with pause/resume/cancel/retry/remove, storage used against a configurable device limit (default 10 GB) plus free device space, and a Wi-Fi-only switch (default on). Admission rejects a download that would exceed the limit or eat into a 1 GB device safety margin.
- Transfers are WorkManager jobs (data-sync foreground work) that resume from the partial file length with `Range`/`If-Range`, so they survive process death and device restarts. An episode becomes **ready** only after its size and fingerprint match the descriptor; a changed server file fails the download instead of mixing versions.
- Files live in app-private `noBackupFilesDir/offline/<owner>/<episode>/`. Downloads belong to one account on one server: logging out locks them (hidden, not playable, transfers paused) until the same account signs in again; signing in as another account or changing the server deletes them. The account is re-checked via `/me` whenever the server is reachable and after WebView page loads.
- A ready download is played from the local file with Media3, also while online (the server bootstrap and fresh cues are used when reachable). Offline, the player uses the stored descriptor for titles, audio/subtitle selection and the learning overlay; word details come from the stored cue tokens, and learning-state changes wait until the server is reachable.
- The client keeps only a local copy of the canonical progress plus a sync queue (one entry per episode, completion sticky). Checkpoints that cannot be delivered (offline playback or a lost connection) are queued and replayed by a network-constrained WorkManager job, which first confirms via `/me` that the same account is signed in. Every server outcome removes the queued entry; a successful live checkpoint supersedes a queued partial one.

## 9. Android TV interaction

The TV app uses Compose for TV focus semantics and a native Media3 player. It is landscape-only.

### 9.1 Remote behavior

When no modal/sheet is open:

- Play/Pause media key: toggle playback
- OK with controls hidden: show controls and focus the primary transport control
- OK on a focused control: activate that control
- Left/right with controls hidden: -10s/+10s
- Back with controls visible: hide controls
- Back with controls hidden: leave player after normal navigation behavior

Player control row always contains a **Learn this line** action.

When sentence learning is open:

- playback follows the shared pause/resume rule
- left/right: move focused word
- OK: open focused word
- Back: close word detail, then sentence overlay
- **Repeat line** is focusable
- **Open on phone** is focusable

No pointer/cursor emulation is used.

### 9.2 TV subtitle rendering

Use the same semantic subtitle style as web/phone with TV-specific values from the canonical tokens:

- larger type
- safe-area-aware bottom position
- sufficient opaque/translucent background for readability
- no subtitle behind the focused control bar
- selected cue/word state uses the same selected semantic style

## 10. TV ↔ phone playback session

A TV player creates one ephemeral `PlaybackSession` when playback starts.

The server is authoritative for the latest accepted state.

### 10.1 Session state

Minimum state:

```text
sessionId
episodeId
positionMs
durationMs
isPlaying
playbackRate
audioTrackId
subtitleTrackId
currentCueId
currentCueText
selectedTermId?
revision
updatedAtUtc
controllerClientId?
```

`revision` is monotonically increasing per session.

The active player publishes immediately on:

- play
- pause
- seek
- audio/subtitle change
- cue change
- selected-word change

While playing, it also publishes a lightweight position heartbeat. Cue changes are never delayed until the next heartbeat.

Target on a normal LAN: the paired phone displays the new cue within 500 ms of the TV cue transition.

### 10.2 Pairing

TV offers **Connect phone**.

It displays:

- QR code with a high-entropy single-use pairing token
- 6-digit numeric fallback code

Pairing token/code:

- expires after 5 minutes
- is single-use for establishing companion membership
- numeric attempts are rate-limited
- repeated invalid attempts are bounded
- is stored/compared safely server-side
- grants only playback-session companion capability
- never grants general server administration or filesystem access

Multiple phones may be paired with one TV playback session.

The TV can select **Disconnect phones**, which invalidates every companion grant for that session immediately.

There is no permanent trusted-device table in v1. A new playback session requires a new pairing.

### 10.3 Session lifetime

- created when the TV starts a playable episode
- active while the player is alive
- short disconnect/reconnect of the TV transport is tolerated
- ends explicitly when the TV player exits or after server-determined stale-session expiry
- ephemeral session loss after AniLingo server restart is acceptable
- learning state and review data are unaffected because they use existing durable storage

### 10.4 Companion screen

The paired phone shows, without requiring manual refresh:

- anime + episode
- current Japanese sentence
- tokenized words
- reading
- meaning
- known/learning/new state
- grammar/context explanation when available
- selected word sent by TV
- Repeat line
- -10 seconds
- Play/Pause
- +10 seconds
- Open full episode learning view

If TV selects **Open on phone**, the phone opens/focuses that exact cue and, when supplied, the exact term.

### 10.5 Commands

Companion commands include:

```text
commandId
sessionId
expectedRevision
type
payload
sentAtUtc
```

The server rejects stale/inapplicable commands and broadcasts the resulting authoritative state. Duplicate `commandId` values are idempotent.

The phone never optimistically becomes the source of truth for TV playback position.

## 11. Security boundary

v1 pairing is capability-based and session-scoped.

Requirements:

- pairing tokens use cryptographically secure randomness
- numeric code validation is rate-limited
- tokens/codes are never logged
- session grants cannot call unrelated admin/settings APIs
- source filesystem paths are never returned to clients
- WebView only trusts the configured server origin
- future server authentication, if added, must integrate with this contract rather than introducing a second Android-only account system

No cloud relay is required. Phone and TV both need network access to the same AniLingo server endpoint.

## 12. Versioning and compatibility

Server, phone and TV are released from the same repository and use the AniLingo product version.

The native client API begins at `v1`.

Rules:

- additive changes may extend v1
- breaking client-contract changes require a new API version
- clients query capabilities at startup
- unsupported combinations fail clearly
- no HTML scraping fallback
- no hidden legacy API compatibility path outside the repository upgrade policy

The Git tag version is used in:

- server package
- phone APK metadata
- TV APK metadata
- GitHub release asset names

## 13. GitHub CI and release artifacts

Android workflow path scope:

```text
clients/android/**
design/player/**
.github/workflows/android.yml
```

Pull request validation:

1. Gradle wrapper validation
2. Kotlin/Android compile
3. lint
4. unit tests
5. phone debug APK
6. TV debug APK

Debug APKs are workflow artifacts only.

Release workflow on AniLingo release tag:

1. build server as today
2. build phone release APK
3. build TV release APK
4. sign both with the stable AniLingo Android release key
5. verify signatures
6. calculate SHA-256
7. attach APKs/checksums to the same GitHub Release
8. optionally produce AABs for future store distribution

Asset names:

```text
AniLingo-Mobile-<version>.apk
AniLingo-Mobile-<version>.apk.sha256
AniLingo-TV-<version>.apk
AniLingo-TV-<version>.apk.sha256
```

Release secrets:

```text
ANDROID_KEYSTORE_BASE64
ANDROID_KEYSTORE_PASSWORD
ANDROID_KEY_ALIAS
ANDROID_KEY_PASSWORD
```

They belong in a protected GitHub release environment/secrets store. Never commit the keystore or decoded key.

Both apps use the same signing identity unless a future store requirement explicitly requires separation.

## 14. Tests required before first APK release

### Server contract

- API compatibility/capabilities
- player bootstrap does not leak filesystem paths
- Range direct content behavior
- HLS fallback creation/cleanup
- cue identity and timing
- pairing token/code expiry
- pairing rate limiting
- companion session authorization
- revision monotonicity
- duplicate command idempotency
- stale command rejection

### Shared Android

- direct-play capability selection
- fallback only on supported failure classes
- fallback restores playback position
- audio/subtitle choice restoration
- cue synchronization
- shared player action semantics

### Phone

- WebView origin restriction
- Play route opens native player
- returning restores WebView navigation state
- subtitle tap opens learning
- external URL leaves WebView

### TV

- D-pad focus path has no traps
- transport remote keys work
- Learn this line works without touch input
- word navigation works with left/right/OK/Back
- QR/manual pairing
- Open on phone sends exact cue/term
- MediaSession state follows player

### End-to-end

At least these real-media scenarios:

1. browser direct-compatible H.264
2. Android direct-compatible MKV/H.264
3. Android device-direct HEVC where hardware reports support
4. unsupported source -> HLS server fallback
5. external Japanese subtitle
6. embedded Japanese text subtitle
7. generated transcription subtitle
8. TV + phone paired while seeking and changing cues

## 15. Delivery slices

Implementation should remain mergeable and testable in these slices:

### Slice A — contract and design foundation

- `/api/client/v1` capabilities/player/cues
- canonical player actions
- `design/player` tokens/icons
- no Android app required yet

### Slice B — Android build foundation

- Gradle multi-module structure
- phone + TV hello/shell
- shared API/model/design modules
- GitHub PR build artifacts

### Slice C — phone native playback

- WebView shell
- Play-route interception
- Media3 direct play
- HLS fallback
- native learning subtitle overlay

### Slice D — web player alignment

- web consumes canonical player design/action contract
- visual/semantic drift removed
- existing browser behavior preserved

### Slice E — TV

- native library/detail shell
- Compose for TV focus behavior
- Media3 + MediaSession
- shared player visual language
- TV subtitle/learning overlay

### Slice F — companion

- PlaybackSession server model
- SignalR hub
- QR/numeric pairing
- phone Companion page
- remote commands
- Open on phone

### Slice G — signed release

- signing environment
- signed APKs/checksums
- device acceptance tests
- release documentation

Do not start with two complete native apps before the shared contract/player foundation exists.

## 16. Explicit non-goals for v1

The first Android/TV milestone does **not** include:

- a second native implementation of phone Library/Learn/Settings
- WebView as the TV shell
- Bluetooth pairing
- Chromecast as the primary playback architecture
- cloud relay outside the configured AniLingo server
- permanent trusted-device accounts just for pairing
- offline anime downloads (added afterwards as the bounded, explicit phone feature in §8.2)
- copying source media into app storage outside that explicit download feature
- client-side vocabulary database
- server filesystem paths in APIs
- mandatory server transcoding for Android-capable formats

## 17. Definition of done

The Android/TV milestone is complete only when all are true:

- browser AniLingo still works independently
- phone non-player UI is the existing web frontend
- phone player is native Media3
- TV shell/player is native and remote-first
- supported Android media Direct Plays using device decoding
- unsupported media falls back automatically and keeps position
- web/phone/TV player has the same visual/action language
- phone/web subtitle learning is interactive
- TV subtitle learning is usable with D-pad
- TV can send the exact line/word to a paired phone
- paired phone follows the TV's active cue live
- paired phone can issue the defined playback/repeat commands
- pairing is session-scoped and expires
- original media remains read-only
- release workflow publishes signed phone and TV APKs with checksums
- required contract/player/pairing/device tests pass

## 18. Platform references

Architecture choices are aligned with current official Android guidance:

- AndroidX Media3 / ExoPlayer: https://developer.android.com/media/media3/exoplayer/hello-world
- Compose for TV: https://developer.android.com/training/tv/playback/compose
- Android TV app setup: https://developer.android.com/training/tv/get-started/create
- TV playback / MediaSession guidance: https://developer.android.com/training/tv/playback
- WebView application content: https://developer.android.com/develop/ui/views/layout/webapps/embed-web-content-in-app
