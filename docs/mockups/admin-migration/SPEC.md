# Admin Migration Center — V1

Status: planning baseline for mockups.

## Purpose
Safe preview-first migration from Jularr legacy/per-type data and supported external systems into canonical Media Core/Library/Progress/Acquisition state.

## Page structure
Migration sources -> scan -> mapping/conflicts -> dry-run preview -> explicit confirmation -> execution progress -> validation/report.

## Data / information
Source IDs, proposed canonical Work/unit/Edition mappings, file/root mapping, provider identities, progress/history preservation, conflicts, skipped unsupported fields and validation counts.

## Actions
Connect/scan, map profiles/paths, resolve conflicts, run dry run, start migration, retry safe steps, export/view report.

## Light / Dark
Both first-class Admin surfaces.

## Platforms
Desktop primary; tablet/mobile can inspect status/reports but complex mapping is desktop-oriented. TV unsupported.

## States
Not started, scanning, conflicts, dry-run ready, running, paused/retryable, failed, completed with warnings, validated.

## Must not implement
No destructive big-bang migration, no title-only identity matching when stronger IDs exist, no source media deletion/move by default, no uncontrolled dual-write authority, no dropping bridges/tables before preservation validation passes.

## Path mapping ownership

Migration/coexistence adapters may require path translation when the external system and Jularr see the same files under different mount prefixes.

Examples:
- Sonarr series/episode paths;
- Sonarr queue/history paths;
- Radarr-equivalent source paths when that adapter exists;
- legacy Jularr paths during canonical migration.

These mappings belong to the specific migration/integration adapter, not to Storage and not to Acquisition Profiles.

Use the same shared path-mapping semantics as Downloader external clients:
- remote/source prefix;
- local Jularr path inside permitted Storage;
- longest-prefix match;
- boundary-aware normalization;
- live preview/test;
- no unrestricted local filesystem target.

If an integration remains active for coexistence after migration, its mapping remains with that integration. If it is migration-only, the mapping may be retired after validation.

Do not create a standalone `Import & Routing` page merely to host migration path translation.

## Sonarr acquisition-policy migration

Where supported, Sonarr migration maps source policy into canonical Jularr contracts:

- Quality Profile → Acquisition Profile quality/upgrade policy;
- Custom Format definition → shared Jularr Release Rule;
- Custom Format score → profile-specific rule effect/score;
- Release Profile → Jularr Release Rules;
- Delay Profile → Acquisition Profile wait/source policy;
- monitoring state → canonical Jularr monitoring state;
- root folder → Storage LibraryRoot mapping/default/Work override;
- Sonarr-reported filesystem prefixes → migration adapter path mappings.

Preview every conversion before commit. Sonarr concepts remain provenance/import evidence, not parallel runtime models.
