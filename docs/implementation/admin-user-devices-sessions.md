# Admin User Devices & Sessions — implementation slice

Status: in progress

## Canonical sources

- Product/UX spec: `docs/mockups/admin-users-permissions/SPEC.md`
- Account detail: `/Admin/User/{id}`
- Known devices: `KnownDeviceRegistry`
- Live playback sessions: `AdminSessionsService` + `PlaybackStreamSessionStore`

## This slice

Add per-account devices and live sessions to the existing Admin user detail without creating another session/device store.

The page must:
- show only the selected account's live sessions and known devices;
- allow stopping one selected account session;
- allow revoking one selected account device;
- enforce the selected account ID server-side for both actions;
- keep the existing account-wide "sign out all sessions" security action separate;
- avoid exposing global security-event/IP diagnostics on the per-user page;
- reuse the existing responsive sessions table styling.

## Security rule

The route account is part of authorization for each action:
- playback stop uses `PlaybackStreamSessionStore.Remove(sessionId, accountId)`, never `RemoveAny`;
- device revoke uses `KnownDeviceRegistry.RevokeAsync(deviceId, requesterProfileId: accountId, ...)`.

A forged form therefore cannot stop or revoke another account's state.

## Out of scope

- persistent per-device authentication tokens;
- sign-in/security-event history (remains on `/Admin/Devices`);
- Account/Profile model migration;
- custom groups/roles and group conflict semantics.
