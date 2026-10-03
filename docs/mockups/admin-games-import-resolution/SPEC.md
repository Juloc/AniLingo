# Admin Game Import Resolution — Planning Scaffold

Status: scaffold; detailed UX pending review.

Shared Admin contract: `docs/mockups/admin-games/SPEC.md`.

## Purpose

Resolve a Game import that cannot safely determine its target Game/platform/release automatically.

This is an exception flow reached from Activity / To-Do or a failed/needs-attention import, not a permanent Games administration page.

## Initial responsibilities

Plan for:
- source download/import identity
- detected files/formats
- current evidence
- choose/confirm platform
- choose existing Game or create/link through approved canonical identity flow
- confirm release/region/revision when required
- retry import
- cancel/leave unresolved

## Rules

- known Acquisition target identity should normally avoid this dialog
- no general AI classifier
- no raw filesystem destination selection
- final destination still follows Games LibraryRoot/import rules
- resolution must be auditable through existing Operations/History

## Open for dialog review

Decide:
- evidence presentation
- Game search/selection behavior
- platform/release fields
- retry result state
- Desktop modal/page vs Mobile sheet
