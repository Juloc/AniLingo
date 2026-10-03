# Admin Devices & Sessions — V1

Status: approved planning direction. Existing `/Admin/Devices` and `/Admin/Sessions` implementations are the starting point. Approved mockups uploaded to this folder are visual references; this text remains binding.

Global UX rules: `docs/UX.md`.
Global Admin density contract: `docs/mockups/admin-instance/SPEC.md`.
Users & permissions boundary: `docs/mockups/admin-users-permissions/SPEC.md`.

If an image and this specification conflict, this specification wins.

## Purpose

Devices & Sessions is the Admin-wide view for:

- active playback sessions across all users;
- known client devices;
- authentication/security activity.

It consolidates current `/Admin/Sessions` and `/Admin/Devices` behavior into one coherent Admin destination.

It does not replace:
- the Admin Dashboard live summary;
- per-user Devices & Sessions inside User detail;
- a profile's own device/session self-service;
- full audit/event history where a future persistent security-audit subsystem owns it.

## Target navigation

One Admin destination:

`Admin -> Geräte & Sessions`

Tabs:

1. **Live Sessions**
2. **Geräte**
3. **Anmeldungen & Sicherheit**

The shared `Detailliert | Kompakt` Admin mode applies to all three tabs.

## Current implementation

### Existing `/Admin/Sessions`

Current implementation already exposes active playback sessions with:

- user/profile
- title
- client kind
- Direct Play / Direct Stream / Transcode
- source summary
- delivered/output summary
- transcode reason
- started time
- last-seen time
- Stop action

Authorization currently uses `JularrPolicies.SessionsStopOthers`.

Existing service:
- `AdminSessionsService`
- `PlaybackStreamSessionStore`

### Current limitation: media identity

The current `AdminSessionsService` resolves session titles through legacy Anime/Episode tables.

Target implementation must resolve session presentation through the canonical media/session identity contract so Movies, TV, Anime, Audiobooks and future playable media appear without parallel media-specific session services.

Do not preserve the Anime-only join as the long-term Admin contract.

### Existing `/Admin/Devices`

Current implementation already exposes:

- profile/user
- device label
- client kind
- app version
- online/offline/live-playback state
- first seen
- last seen
- revoke/remove action
- recent login success/failure events

Authorization currently uses `JularrPolicies.AdminSystem`.

Existing services:
- `KnownDeviceRegistry`
- `SecurityEventLog`

### Current limitation: device identity

Current device identity is derived from:

- profile
- client kind
- optional label

It is intentionally not a strong hardware/browser fingerprint.

Therefore:
- multiple real devices can collapse if they report the same coarse identity;
- one real device can appear as multiple entries when client identity changes;
- the UI must not claim hardware-level device identity.

The target may later introduce a stronger client-issued device/session identifier, but it must remain privacy-conscious and explicit.

### Current limitation: revoke semantics

Current `KnownDeviceRegistry.RevokeAsync`:

- removes the device row;
- stops matching active playback sessions;
- does **not** invalidate a durable per-device authentication token because the current cookie authentication model does not expose one.

Therefore the current action must not be labeled as if it securely signs that device out forever.

Use wording such as:
- `Gerät entfernen`
- `Aktive Wiedergaben beenden und Gerät vergessen`

A future true `Gerät abmelden` action requires a real per-device authentication/session revocation contract.

### Current limitation: security events

`SecurityEventLog` is currently:

- bounded;
- in-memory;
- limited primarily to login success/failure;
- cleared by application restart.

The UI must not describe it as a complete persistent audit log.

If persistent security history is introduced later, retention/schema/privacy must be defined explicitly.

## 1. Live Sessions

Purpose:
- see and manage every currently active playback session.

### Summary

Optional compact summary:
- active sessions
- Direct Play
- Direct Stream
- Transcode

Do not create large decorative cards when Compact mode is active.

### Filters

Support where useful:
- search by title/user/device
- user
- media type
- playback mode
- client/device
- transcode/direct-play state

### Default table

Columns:

- Benutzer
- Medium
- Gerät
- Wiedergabeart
- Quelle -> Ausgabe
- Position / Laufzeit
- Netzwerk
- Aktionen

Optional columns when reliably available:
- bitrate
- transcode speed
- GPU/encoder
- connection type
- start time

Do not render unavailable values as invented metrics.

### Playback mode

Explicit values:
- Direct Play
- Direct Stream / Remux
- Transcode

The exact naming must match the canonical playback decision model.

### Session row actions

Primary actions:

1. **Stop**
2. **Details**

Stop:
- requires appropriate capability;
- requires confirmation;
- ends the active playback session;
- does not modify media progress unless playback/session semantics already do so.

### Session details drawer

Show when available:

#### Identity
- user/profile
- Work/title
- unit/episode/chapter/track where applicable
- session ID

#### Device
- client kind
- device label
- app version
- platform
- remote/network address only when authorized

#### Playback
- mode
- source container
- delivered container
- source codec
- output codec
- source resolution
- output resolution
- audio source/output
- subtitle selection/burn-in
- transcode reason
- hardware acceleration state

#### Progress
- position
- duration
- started time
- last activity

#### Network
- bitrate/current throughput where measurable
- connection type where known

#### Diagnostics
- related operation/log/correlation links
- playback-plan reasons

The drawer updates while the session remains active.

When the session ends while open:
- show `Session beendet`;
- retain the final visible details until the drawer closes.

## 2. Geräte

Purpose:
- inspect known Jularr clients/devices across users.

### Summary

May show:
- known devices
- currently online
- offline
- recently seen/new

"New device" requires a defined time window and must not imply a security anomaly by itself.

### Filters

Support:
- search
- user
- client/device type
- status
- last-seen window

### Default table

Columns:
- Benutzer
- Gerätename
- Typ
- Client/App
- Version
- Status
- Erste Aktivität
- Letzte Aktivität
- Aktionen

Status examples:
- Online
- Offline
- Direct Play
- Direct Stream
- Transcode

Live playback state may refine the Online state.

### Device detail

Optional drawer shows:
- profile/user
- internal device ID
- reported label
- client kind
- app version
- first seen
- last seen
- online status
- active playback sessions
- reported user agent only where useful and authorized

Do not expose raw device identifiers as the primary user-facing label.

### Current remove action

Until true per-device auth revocation exists:

`Gerät entfernen` means:
- stop matching active playback;
- remove/forget the KnownDevice entry.

Confirmation must explain:
- the client may appear again when it reconnects;
- this is not equivalent to permanent authentication revocation.

### Future true revoke

A real `Gerät abmelden` requires:
- explicit authenticated device/session identity;
- server-side revocable credential/session;
- durable revocation semantics;
- clear effect on playback and API access.

Do not fake this through KnownDevice deletion.

## 3. Anmeldungen & Sicherheit

Purpose:
- show recent authentication/security activity relevant to account access.

### Current V1 data

Current events include:
- successful login
- failed login
- timestamp
- user/account name
- remote address where recorded

Current data is in-memory and not a permanent audit record.

The UI should state that limitation until persistence exists.

### Target event types

Only add event types when the underlying system actually records them.

Potential future events:
- login succeeded
- login failed
- logout
- password changed
- session invalidated
- account disabled/enabled
- device/session revoked
- external login identity linked/unlinked
- 2FA/passkey changes where supported

### Filters

Support:
- time range
- user
- event type
- result
- remote address search where authorized

### Default table

Columns:
- Zeitpunkt
- Benutzer
- Ereignis
- Remote-Adresse
- Gerät/Client where known
- Ergebnis

### Security interpretation

Do not automatically label:
- unknown IP
- new device
- failed login

as a confirmed attack.

The UI may show:
- informational
- warning
- repeated failures / unusual pattern

only when a real detection rule exists.

## Relationship to Admin Dashboard

Dashboard may show:
- current active sessions
- Direct Play/Remux/Transcode
- compact resource/network impact
- Stop + Details

Dashboard is for live operational glance.

Devices & Sessions is the full cross-user management surface.

Dashboard's `Details` opens this area/session drawer without inventing a second session detail model.

## Relationship to User detail

`Admin -> Users & Permissions -> User -> Geräte & Sessions` uses the same underlying services/components, filtered to one Account/Profile.

It must not implement a separate device/session store.

From a user detail page, admin can:
- inspect active sessions
- inspect known devices
- perform the same supported actions
- optionally open the global Devices & Sessions view with the user filter preselected

## Relationship to Profile self-service

Profile/User Settings may expose only the current profile's:
- known devices
- active sessions
- supported self-revoke/stop actions

Profile self-service must never expose another user's sessions/devices.

## Authorization

Keep capabilities explicit.

Possible capability boundaries:
- view all sessions
- stop other users' sessions
- view all devices
- remove known devices
- view security/login events
- invalidate user/account sessions when supported

Do not rely on navigation visibility as authorization.

Existing policies may be reused where appropriate:
- `SessionsStopOthers`
- `AdminSystem`

If capabilities are split later, migrate deliberately rather than adding ad-hoc `IsAdmin` checks.

## Detailed / Compact mode

### Detailed

May show:
- summary cards
- device/media secondary text
- richer session state
- detailed filters
- side drawer with sections

### Compact

Use dense tables:
- one session/device/security event per row
- reduced secondary text
- inline playback/status state
- compact actions
- no loss of warnings or destructive-action meaning

Both modes use the same:
- data
- permissions
- commands
- validation
- detail contracts

## Live updates

Live Sessions should update without full-page refresh where possible.

Updates must not:
- reset filters
- reset scroll
- close an open detail drawer
- lose selected tab
- reorder rows unnecessarily while the admin is interacting

Device online/offline state may update live or on a modest refresh interval.

Security events do not need high-frequency polling.

## Light / Dark / skins

Light and Dark are first-class.

Clean and Original Jularr skins share the same information architecture.

Operational states must remain legible without relying on color alone.

## Platforms

### Desktop
Primary experience for all tabs and detailed session inspection.

### Tablet
Supported with:
- same tabs
- reduced columns
- side/full-height drawer
- compact filter controls

### Mobile
Supported for essential administration:
- Live Sessions list
- session detail
- Stop action
- Devices list
- device detail/remove
- recent security events

Use stacked rows/cards rather than squeezed desktop tables.

### TV
Unsupported.

## Loading / Empty / Error / Partial states

Required:
- no active sessions
- session ended while drawer open
- session data partially unavailable
- live update disconnected
- no known devices
- device online
- device offline
- device removed
- device reappeared after reconnect
- no security events
- security event history reset/not persisted
- action forbidden
- Stop failed
- remove device failed
- stale session/device state

## Security and privacy

Do not expose:
- auth cookies
- access tokens
- refresh tokens
- password hashes
- API keys
- personal provider credentials
- full sensitive headers

Remote addresses are security-sensitive operational data:
- Admin-only;
- only shown when policy allows;
- not exposed to normal profiles for other users.

## Implementation migration

Incremental target:

1. Reuse `AdminSessionsService`, `PlaybackStreamSessionStore`, `KnownDeviceRegistry` and `SecurityEventLog`.
2. Introduce a shared Devices & Sessions shell with tabs.
3. Move current Sessions table into Live Sessions.
4. Move current Devices table into Geräte.
5. Move current recent security events into Anmeldungen & Sicherheit.
6. Replace Anime-specific session title resolution with canonical media/session identity.
7. Make existing `/Admin/Sessions` and `/Admin/Devices` routes compatibility redirects/deep links once feature parity exists.
8. Reuse the same components/data in User detail and profile self-service.
9. Introduce true device-auth revocation only with a real backend session/device credential contract.

No big-bang rewrite is required.

## Must not implement

- No second session/device store.
- No Anime-only long-term session presentation.
- No fake permanent device sign-out through KnownDevice deletion.
- No claim that coarse device identity is a hardware fingerprint.
- No permanent-audit wording for the current in-memory SecurityEventLog.
- No duplicate Dashboard session detail model.
- No separate per-user session/device implementation.
- No exposure of authentication secrets.
- No unsupported IP/geolocation threat claims.
- No UI-only authorization.
- No destructive Stop/remove action without confirmation.
- No TV Admin UI.
