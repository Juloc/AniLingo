# Jularr UX mockups

Approved and work-in-progress UX mockups live here.

## Canonical structure

Use one folder per substantial screen. `SPEC.md` is the binding screen-specific behavior/information hierarchy/state contract; images are visual references once approved. If text and image conflict, the text spec wins. Light and Dark are always first-class. `docs/UX.md` owns global/shared rules.

Agents must not redesign an approved screen during implementation without updating its spec and approval.

## User screen contracts

- `home/SPEC.md`
- `library/README.md` — current binding draft; normalize to `SPEC.md` after approval
- `discover/SPEC.md`
- `anime-series-detail/SPEC.md`
- `movie-detail/SPEC.md`
- `reading-detail/SPEC.md` — shared Book / Light Novel / Manga detail family
- `audiobook-detail/SPEC.md`
- `player/SPEC.md`
- `reader/SPEC.md`
- `calendar/SPEC.md`
- `learning-home/SPEC.md`
- `lesson-review/SPEC.md`
- `user-settings/SPEC.md`
- `profile-activity/SPEC.md`
- `add-request-flow/SPEC.md`

## Admin screen contracts

- `admin-dashboard/SPEC.md`
- `admin-media-detail/SPEC.md`
- `admin-wanted/SPEC.md`
- `admin-requests/SPEC.md`
- `admin-activity/SPEC.md`
- `admin-history/SPEC.md`
- `admin-manual-search/SPEC.md`
- `admin-imports/SPEC.md`
- `admin-storage/SPEC.md`
- `admin-providers/SPEC.md`
- `admin-ai/SPEC.md`
- `admin-users-permissions/SPEC.md`
- `admin-backup-restore/SPEC.md`
- `admin-migration/SPEC.md`
- `admin-acquisition-settings/SPEC.md`
- `admin-system-diagnostics/SPEC.md`
- `setup-wizard/SPEC.md`

## Planning references

- `../PLANNING-AUDIT.md` — coverage, architecture consistency and open-issue audit.
- `../IMPLEMENTATION-ROADMAP.md` — dependency-ordered implementation sequence.

Only create platform images that are actually needed. Important screens should receive approved Desktop/Mobile/Tablet/TV references according to their SPEC before their implementation is considered complete.
