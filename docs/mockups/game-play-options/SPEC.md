# Game Play Options Dialog — Planning Scaffold

Status: scaffold; detailed UX pending review.

Shared contract: `docs/mockups/games/SPEC.md`.

## Purpose

Conditional dialog/sheet shown only when Play cannot be resolved to one obvious release/runtime automatically.

## Possible reasons to show

- multiple materially different local GameReleases
- multiple compatible runtimes with no configured default
- required user choice such as disc/release variant
- selected runtime needs a supported launch option that cannot be derived safely

## Expected choices

Only show choices that are actually ambiguous:
- release
- runtime
- region/revision where it changes the playable release

Defaults should avoid this dialog when one clear launch path exists.

## Boundaries

- not a permanent pre-launch wizard
- no Admin emulator configuration
- no BIOS upload
- no technical path selection
- no acquisition settings

## Open for dialog review

Decide:
- when it appears
- ordering/defaults
- remembered preference semantics
- Desktop modal vs Mobile sheet
