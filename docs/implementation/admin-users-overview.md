# Admin Users Overview — implementation slice

Status: in progress

## Canonical sources

- Product/UX spec: `docs/mockups/admin-users-permissions/SPEC.md`
- Visual reference: `docs/mockups/admin-users-permissions/file_0000000016b881f483dfa8a65e4d9ee2.png`
- Existing routes: `/Admin/Users`, `/Admin/User/{id}`, `/Admin/Capabilities`

## This slice

Only the **Admin → Users overview** is changed here.

The page must:
- remain owner/`AdminSystem` protected;
- present accounts as account/permission administration, not as a consumption-progress dashboard;
- show the account name, role, enabled/pending state and useful account metadata;
- keep enable/disable/approve and Manage actions;
- keep user creation on the same page;
- link to the canonical media capability matrix instead of duplicating permission state;
- support an empty account-list state;
- stack cleanly on tablet/mobile;
- reuse Jularr shared tokens/components and support light/dark themes.

## Existing implementation audit

Current `/Admin/Users` mixes account administration with Anime/Novel/Learning progress cards. That is not part of the Users & Permissions information model in the V1 spec. Progress remains available to the account/detail/domain surfaces; it should not dominate the Users overview.

The current backend already supplies:
- local account identity;
- role;
- enabled state;
- created timestamp;
- last activity summary;
- create/approve/disable actions.

The canonical capability editor already exists at `/Admin/Capabilities`. This slice does not create a second permissions store.

## Follow-up slices

1. User detail
2. Groups/roles
3. Capability matrix polish and instance-module filtering
4. Media/request/Learning/AI policy surfaces
5. Devices/sessions

Each follow-up starts from current `main` after the previous slice is merged.
