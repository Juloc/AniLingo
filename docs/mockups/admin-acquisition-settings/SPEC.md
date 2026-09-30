# Admin Acquisition Settings — V1

Status: planning baseline for mockups.

## Purpose
Configure shared acquisition infrastructure: quality/language profiles, scoring rules, indexers/release-search providers, download clients, categories/routing and import defaults.

## Page structure
Profiles & Scoring -> Indexers -> Download Clients -> Routing/Categories -> Import defaults. Each uses list -> detail/config -> Test/Preview.

## Data / information
Reusable AcquisitionProfiles, provider/client capabilities and health, per-media routing/category mappings, delay/priority rules, language/quality constraints and safe import-mode defaults.

## Actions
Create/edit/clone profile, reorder rules, test indexer/client, configure routing, preview scoring, set defaults.

## Light / Dark
Both first-class Admin surfaces.

## Platforms
Desktop primary; tablet/mobile supports stacked editors. TV unsupported.

## States
Unconfigured, healthy/degraded provider, invalid rule, no eligible client, test running/failure, unsaved changes, permission denied.

## Must not implement
No Anime-only acquisition settings, no `IsBooks`-style routing booleans, no separate per-media acquisition engines, no raw YAML as primary UX, no hidden scoring rules.