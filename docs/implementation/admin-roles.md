# Admin Roles — implementation slice

Status: in progress

## Canonical sources

- Product/UX spec: `docs/mockups/admin-users-permissions/SPEC.md`
- Role model: `AccountRole` / `AccountRoles`
- Named authorization policy table: `JularrPolicies.Roles`
- Media role defaults: `MediaCapabilityPolicy`

## This slice

Expose the **existing built-in account roles** as one owner-only Admin surface without inventing a
second authorization model.

The page must:
- show Owner, Media manager and User from the canonical enum;
- show how many accounts currently have each role;
- derive named Admin permissions from `JularrPolicies.Roles`, not hard-coded page lists;
- keep Owner special/immutable and make role assignment stay on the individual user page;
- link media capability defaults to the existing `/Admin/Capabilities` editor;
- remain part of the Users & Permissions navigation context.

## Groups

The V1 planning spec names groups, but the repository does not yet define group membership,
multi-group conflict resolution, group-vs-user precedence or which policy families a group can own.
This slice deliberately does **not** create a placeholder group table/store or invent that precedence.
Groups can be implemented once those semantics are specified; built-in roles remain usable and
canonical meanwhile.
