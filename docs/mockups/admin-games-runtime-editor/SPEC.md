# Admin Game Runtime Editor — Planning Scaffold

Status: scaffold; detailed UX pending review.

Parent page: `docs/mockups/admin-games-runtimes/SPEC.md`.

## Purpose

Focused add/edit dialog for one runtime adapter.

## Initial fields/capabilities

Plan for:
- runtime adapter/type
- display name
- enabled
- supported/configured platforms
- adapter-specific validated configuration
- required executable/endpoint only when the runtime type actually needs one
- explicit filesystem/resource grants
- explicit device/host grants only when required
- test/validate action

## Security boundary

Configuration must show exactly what the runtime can access.

Never offer an implicit "full host access" default.

Browser/WASM runtimes should normally require no host filesystem/database access.

## Boundaries

- no game library path configuration
- no provider/indexer configuration
- no BIOS file upload inside unrelated fields
- no arbitrary shell command template as the primary runtime model

## Open for dialog review

Decide:
- step vs one-page form
- adapter picker
- permission/capability wording
- test result presentation
- advanced section
