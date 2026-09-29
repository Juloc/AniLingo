# Jularr UX mockups

Approved and work-in-progress UX mockups live here.

## Canonical structure

Use one folder per substantial screen:

```text
docs/
  UX.md
  mockups/
    README.md
    home/
      SPEC.md
      desktop.png
      mobile.png
      tablet.png
      tv.png
    admin-dashboard/
      SPEC.md
      desktop.png
    admin-media-detail/
      SPEC.md
      ...
```

Only add platform images that are actually needed.

- `SPEC.md` = binding screen-specific behavior, information architecture, states and acceptance criteria.
- images = binding visual references once approved.
- Light and Dark are always first-class. A platform image may show both variants side-by-side.
- `docs/UX.md` = global/shared UX rules, navigation, responsive principles and the master mockup checklist.

Agents must not redesign an approved screen during implementation without updating its spec and obtaining a new approved mockup.

## Current screen specs

- `home/SPEC.md` — approved clean Home UX baseline.
- `admin-dashboard/SPEC.md` — Admin Dashboard live operations/health contract.
- `admin-media-detail/SPEC.md` — Admin media monitoring/acquisition hierarchy V1.

## Existing image assets

Older root-level mockup binaries may remain temporarily until replaced by the canonical platform files inside their screen folders.
