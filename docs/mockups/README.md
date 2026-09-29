# Jularr UX mockups

Approved and work-in-progress UX mockups live here.

## Canonical structure

Use one folder per substantial screen:

```text
docs/mockups/<screen>/
  SPEC.md
  desktop.png
  mobile.png
  tablet.png
  tv.png
```

Only add platform images that are actually needed.

- `SPEC.md` = binding screen-specific behavior, information architecture, states and acceptance criteria.
- images = binding visual references once approved.
- `docs/UX.md` = global/shared UX rules, navigation, responsive principles and the master mockup checklist.

Agents must not redesign an approved screen during implementation without updating its spec and obtaining a new approved mockup.

## Current screen specs

- `admin-dashboard/SPEC.md` — Admin Dashboard live operations/health contract.
- `home.md` — existing Home screen spec from the parallel planning stream; it can be migrated to `home/SPEC.md` when that stream updates its files.

## Existing image assets

- `admin-dashboard-clean-live.png` — current approved Admin Dashboard desktop mockup. It remains at the root for now to preserve the already-uploaded binary; future replacement/move target is `admin-dashboard/desktop.png`.
