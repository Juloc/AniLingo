# Admin account groups — implementation slice

Status: in progress

## Canonical sources

- `docs/mockups/admin-users-permissions/SPEC.md`
- `docs/UX.md` §26
- account roles: `AccountRole` / `JularrPolicies`

## Model

Built-in roles stay fixed: Owner, Media manager and User. This slice does not turn roles into editable strings.

Groups are reusable account collections for later media/request/Learning/AI policies. Membership itself does not silently grant a permission; feature policies must explicitly target a group.

Persistence is relational:
- `AccountGroups`
- `AccountGroupMembers`
- membership has foreign keys to both the group and the internal Jularr Account ID;
- deleting a group or account cascades its memberships;
- normalized group names are unique.

## UI

- `/Admin/Groups` manages groups.
- Admin → Users links to Groups and Media capabilities.
- A non-owner user detail can assign group memberships alongside the built-in role.
- Owner remains outside group assignment because Owner already bypasses normal capability policy.

## Out of scope

- group-specific media capability values;
- AI/request/Learning group policy editors;
- custom authorization roles;
- external identity/profile restrictions.

Those policies can consume the stable group IDs added here without changing account identity.
