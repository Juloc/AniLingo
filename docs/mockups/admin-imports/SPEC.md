# Admin Imports — V1

Status: **superseded as a standalone page**.

There is no permanent Admin Imports page in V1.

Import workflow is integrated into:
- `docs/mockups/admin-activity/SPEC.md` — live downloads/imports, To-Do problems and Import Review / Assignment dialog
- `docs/mockups/admin-history/SPEC.md` — completed/resolved import outcomes

## Product decision

Flow:

1. A download/processing job is visible in **Activity** while running.
2. If download/import succeeds, its result moves to **History**.
3. If import needs human intervention, it moves to **To-Do**.
4. From To-Do, the admin opens the **Import Review / Assignment dialog**.
5. The admin can correct the canonical media assignment and supported parsed metadata, then retry/confirm import.

This avoids a duplicate queue/page for the same operational state.

## Import Review capabilities

The dialog may correct:
- Work
- Structure/unit
- season/episode
- volume/chapter/part
- Edition
- Version target
- release group
- quality/source classification
- languages
- audio/subtitle interpretation
- other supported normalized acquisition metadata

Jularr should prefill detected values.

Destination is normally derived from the selected canonical media assignment and configured LibraryRoot/storage policy.

The admin may preview the destination or choose among permitted configured roots/policies where supported, but arbitrary server path entry is not the normal UX.

## Constraints

- No raw database IDs.
- No filename-only canonical identity.
- No separate media-type import queues.
- No silent global parser learning from one manual correction.
- Prefer probed technical file facts where reliable.
- No destructive source-file action without explicit semantics.
