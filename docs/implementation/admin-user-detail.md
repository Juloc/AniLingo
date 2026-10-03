# Admin User Detail — implementation slice

Status: in progress

## Canonical sources

- Product/UX spec: `docs/mockups/admin-users-permissions/SPEC.md`
- Visual reference: `docs/mockups/admin-users-permissions/file_0000000016b881f483dfa8a65e4d9ee2.png`
- Parent overview: `/Admin/Users`
- Canonical media capability editor: `/Admin/Capabilities`

## This slice

Only the **single-user Admin detail** is changed here.

The page must:
- remain protected by `JularrPolicies.AdminSystem`;
- focus on account identity, status, role/access and security;
- keep rename, role, enable/disable, password reset, session invalidation and delete behavior;
- never create a second authorization/capability store;
- link non-owner accounts to the canonical capability matrix;
- keep the Owner immutable with respect to role, disable and delete;
- avoid exposing consumption/Learning progress as the primary account-management content;
- remain responsive and use existing Jularr tokens/components.

## Existing implementation audit

The current detail page starts with Anime, Novel and Learning progress. That belongs to progress/activity surfaces rather than Users & Permissions. The PageModel also loads all users plus all progress data merely to resolve one account.

`OwnerAuthService.GetAsync` already provides the canonical account record needed for this page, so the detail page can load exactly one account without querying media/Learning progress.

## Out of scope

- groups/custom roles;
- redesign of the media capability matrix;
- request/Learning/AI policy editors;
- per-device/session list;
- linked external identities beyond existing cleanup behavior.

Those remain later slices from the Users & Permissions spec.
