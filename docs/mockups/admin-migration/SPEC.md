# Admin Migration Center — V1

Status: approved planning direction; current Migration Center mockup is the visual baseline once uploaded to this folder.

Global UX rules: `docs/UX.md`.

If an image and this specification conflict, this specification wins.

## Purpose

Migration Center performs preview-first migration from older Jularr data and supported external systems into the canonical Jularr model.

All imported media must end in:

`Work -> Structure -> Edition -> Version -> Asset/File -> Track`

Migration never keeps a legacy source model as a second long-term authority.

## Main flow

The migration engine uses one normalized plan, but the wizard is **adaptive by source**.

Conceptual phases:
1. Quelle
2. Verbindung / Scan
3. Medien & Identitäten
4. Policies / Profile / Monitoring
5. Dateien & Benutzer
6. Konflikte
7. Optionen
8. Dry Run
9. Migration / Report

The UI does not force every source through nine visible pages.

Examples:
- Folder migration skips Users, Progress and Acquisition Profiles.
- Jellyfin/Plex/Emby skip Acquisition Profiles and Custom Formats.
- Sonarr/Radarr expose Monitoring, Profiles and Custom Formats prominently.
- Old Jularr may expose nearly all phases.

The existing mockup stepper is a visual baseline; implementation may combine adjacent phases into one screen when that keeps the flow clearer.

## Supported migration source families

### Alte Jularr Version

Purpose:
- migrate old Jularr/per-media-type databases and configuration into the current canonical model

May import:
- media/work identity
- seasons/episodes/volumes/chapters
- files/assets/tracks
- provider IDs/provenance
- library membership
- monitoring/acquisition state
- user accounts/groups where compatible
- progress/history
- requests
- collections/lists
- acquisition profiles/rules where compatible
- provider/settings mappings
- configuration

Legacy tables are source evidence only and are not preserved as permanent runtime models.

### Sonarr

Purpose:
- migrate Series/Anime automation state into Jularr without losing acquisition behavior

The importer should use Sonarr API/data rather than infer everything from filenames.

#### Sonarr media import

Import where available:
- Series identity and provider IDs
- Series type
- Seasons
- Episodes
- Episode files
- root folder/source path
- file path
- quality
- release group
- language/media-info evidence where available
- tags
- alternate titles/provider identifiers where useful for resolution

Series/season/episode data is mapped into the canonical Jularr hierarchy.

#### Sonarr monitoring state

Monitoring must be preserved where a meaningful Jularr equivalent exists.

Import:
- Series monitored/unmonitored state
- Season monitored/unmonitored state
- Episode monitored/unmonitored state
- monitor-new-items / future-episode behavior where available
- series-level monitoring mode when Sonarr exposes one

Migration preview must show how Sonarr monitoring maps to Jularr monitoring/acquisition policy.

Do not collapse all Sonarr monitoring into a single Work boolean if Sonarr has more specific season/episode state.

#### Sonarr Quality Profiles

Import Sonarr Quality Profiles into Jularr AcquisitionProfiles where semantically compatible.

Preserve:
- profile name
- enabled/allowed qualities
- quality ordering/preference
- grouped/equivalent quality groups where representable
- upgrades allowed
- upgrade/cutoff quality
- language preference where present
- minimum/custom-format score thresholds where supported
- upgrade-until custom-format score where supported

Every imported profile must be previewed before creation/merge.

If Jularr has a richer language/profile model, migration may split one Sonarr profile into:
- a reusable quality/release profile
- explicit target language settings

The dry run must show this transformation.

#### Sonarr Custom Formats / custom rules

Import Sonarr Custom Formats and their profile-specific scores.

Every source Custom Format/condition must receive one explicit conversion result:
- **1:1 übernommen** — native Jularr rule has equivalent semantics
- **Übersetzt** — converted into one or more Jularr-native scoring rules with equivalent intent
- **Nicht unterstützt** — no safe equivalent; not imported automatically

Approximate/heuristic conversion without a visible warning is not allowed.

Preserve, where supported:
- Custom Format name
- condition groups
- release-title regex/terms
- source
- resolution
- quality modifier
- release group
- language
- size ranges
- indexer flags
- negate
- required
- other Sonarr condition attributes that have a safe Jularr equivalent
- include-in-renaming metadata only if Jularr naming policy supports an equivalent

Also import the score of each Custom Format **per Quality Profile**.

Important:
- the Custom Format definition and its score are separate concepts
- score 0 remains informational where equivalent
- negative/positive scores must preserve intent
- minimum score / upgrade-until score belongs to the imported profile
- unsupported conditions are not silently discarded; they appear in the migration report

Jularr may translate Sonarr Custom Formats into native AcquisitionProfile scoring rules, but must not retain Sonarr-specific runtime logic as a parallel scoring engine.

#### Sonarr tags and linked behavior

Import tags and source associations where useful.

Potential mappings include:
- series tags
- indexer/tag associations
- delay-profile/tag relationships
- other source policy associations

Tags are not automatically treated as Jularr user-facing media tags.

The importer resolves their operational meaning first.

#### Sonarr paths

Root folders are mapped to existing/new Jularr LibraryRoots.

Do not blindly persist Sonarr paths as canonical identity.

Remote/path mappings must be reviewed against Jularr Storage.

**Default behavior is link/reconcile in place.**

Sonarr migration itself does not rename/move the media library by default.

If the admin wants cleanup/reorganization after migration, hand the resulting library to the dedicated **Library Reconciliation** flow for explicit rename/move preview and execution.

#### Sonarr configuration migration — separate from data migration

Sonarr **data/policy migration** and **instance-configuration migration** are separate selections.

Normal Sonarr data/policy migration includes:
- Series/Season/Episode identity
- files/path links
- monitoring state
- Quality Profiles
- Custom Formats + per-profile scores
- operational tag relationships where meaningful

Optional configuration migration may additionally propose:
- naming policy
- quality definitions/size limits
- delay/release timing policy
- indexer configuration
- external download-client adapter configuration
- selected media-management/import settings

Configuration is imported only when Jularr has a clear semantic equivalent.

Indexer/download-client credentials are never silently copied as trusted working configuration. Imported connection settings are shown as proposals and must be reviewed/tested; secrets require explicit protected handling.

Unsupported settings are listed in the final report rather than silently ignored.

### Radarr

Purpose:
- movie acquisition/library migration

May import:
- movie identity/provider IDs
- existing files
- monitored state
- root folders
- Quality Profiles
- Custom Formats + per-profile scores
- tags/policy associations
- quality definitions
- naming/import settings where compatible
- indexer/download-client configuration where explicitly selected

Movie data maps to canonical Work/Edition/Version/File structures rather than a dedicated Radarr-compatible core.

### Jellyfin

Purpose:
- use a media server as evidence for existing libraries and user playback state

May import:
- library items
- file paths
- provider/external IDs
- collections/playlists where compatible
- watched/unwatched state
- resume position
- play count/history where supported
- artwork/metadata references where useful

Source users are **never silently created as Jularr accounts**.

For each source profile/user, the wizard must offer:
- map to existing Jularr user
- explicitly create a new Jularr user
- skip this user

Progress/history is imported only after that mapping is resolved.

Jellyfin is not an acquisition-policy source.

Do not invent Quality Profiles/monitoring rules from Jellyfin playback data.

### Plex

Purpose:
- migrate media-library identity and playback state

May import:
- library items
- file locations
- provider IDs/metadata evidence
- collections/playlists where compatible
- watched state
- resume position
- playback history where available

Plex libraries must be mapped to Jularr LibraryRoots/content types.

Source users/profiles use the same explicit mapping flow:
- existing Jularr user
- create new Jularr user
- skip

Plex labels/collections are imported only when semantics are clear.

### Emby

Same general migration family as Jellyfin:
- existing library/file identity
- provider IDs
- watched/resume/history
- collections/playlists where compatible

Emby users/profiles must be explicitly mapped to an existing/new Jularr user or skipped.

Do not treat Emby metadata as a second permanent authority after canonical resolution.

### Ordnerstruktur

Purpose:
- migrate an existing filesystem library without a source application

Input:
- configured safe Storage path / LibraryRoot

Scan:
- folders
- files
- sidecars
- embedded metadata
- filenames
- media probe data

Use the existing Library Reconciliation flow for unresolved mappings.

This source can:
- link files in place
- optionally organize/rename via explicit preview
- create canonical Work/Structure/File mappings

It cannot import:
- users
- playback history
- acquisition profiles
unless provided by another source.

### JSON / CSV

Purpose:
- structured import from exported data or custom migration datasets

Supported conceptual records may include:
- Works/external IDs
- lists/collections
- users
- progress/history
- requests
- acquisition-profile mapping
- path/file mapping

Use a schema-mapping step:
- source column
- target field
- transformation
- required/optional
- validation result

Unknown columns are ignored only after explicit preview.

### Andere Quelle / Expert Adapter

Purpose:
- migration extension point for sources not covered by built-in adapters

A migration adapter must declare:
- source type/version
- supported entities
- required credentials/input
- normalized scan contract
- mapping capabilities
- validation rules

It must output the same normalized migration plan as built-in sources.

No adapter may write directly into legacy/per-source tables as a permanent model.

## Connection & scan

Source-specific connection page may include:
- URL/endpoint
- API key/token
- local backup/file selection
- safe folder selection
- connection test
- source-version detection

Scan options depend on source.

Examples:
- media
- seasons/episodes
- files
- metadata
- users
- progress
- requests
- profiles/rules
- settings

## Analyze contents

Before mapping, show counts by entity.

Examples:
- Works/Series/Movies
- Seasons
- Episodes
- Files
- Users
- Progress entries
- Requests
- Profiles
- Custom Formats
- Conflicts
- Unknown/unmapped items

The admin can inspect samples and unresolved records.

## Mapping

Mapping operates on normalized source records.

Use tabs such as:
- Automatic
- Manual
- Conflicts

Mappings can include:
- source Work -> canonical Work
- source season/episode -> canonical Structure/unit
- source root folder -> LibraryRoot
- source user -> Jularr user
- source Quality Profile -> AcquisitionProfile
- source Custom Format -> native scoring rule
- source monitoring state -> Jularr monitoring policy

Never identify media by title alone when stronger IDs are available.

## Import options

Separate options into two conceptual groups.

### A. Daten & Policies

Selectable entity groups:
- Media
- Metadata/provenance
- Files/path links
- Monitoring state
- Acquisition Profiles
- Custom Formats/rules
- Users
- Progress/history
- Requests
- Lists/collections

### B. Instanz-Konfiguration

Optional, separately enabled:
- Provider/indexer settings
- External download-client settings
- Naming/media-management settings
- Quality definitions/size limits
- Delay/release timing policy
- other source settings with a clear Jularr equivalent

Configuration import is never implied by selecting media migration.

Default file behavior:
- link/reconcile files in place
- never move/delete/rename source files during normal application migration

Optional:
- import missing metadata after migration
- store detailed migration report
- hand off library cleanup to Library Reconciliation after migration

## Conflict handling

Conflict classes:
- duplicate Work
- multiple canonical matches
- missing provider ID
- source path unavailable
- same file already linked
- profile name collision
- Custom Format/rule collision
- incompatible rule condition
- user collision
- progress conflict
- request conflict

Resolution:
- merge
- use existing
- create new
- skip
- manual mapping

Rules are entity-specific; do not use one global overwrite switch for everything.

## Dry Run

Dry run is mandatory before mutating persistent state.

Summary should include:
- created
- merged/updated
- skipped
- conflicts
- manual mappings
- unsupported source fields/settings

Expandable sections show detailed planned changes.

## Execution

Migration runs as a resumable operational job where feasible.

Show:
- current phase
- progress
- counts
- warnings/errors
- concise log

The source remains untouched by default.

No source deletion or move unless explicitly configured for a filesystem migration step.

## Result report

Report:
- created
- updated/merged
- skipped
- conflicts remaining
- unsupported fields
- failed records
- manual follow-up required
- validation result

For Sonarr/Radarr specifically, report profile/rule conversion separately:
- Quality Profiles imported
- Custom Formats imported
- Custom Formats converted 1:1
- Custom Formats translated to native Jularr rules
- unsupported conditions/rules
- score mappings imported
- monitoring states imported by Work/Season/Episode scope
- tags/policies imported/skipped
- optional configuration proposed/imported/skipped

## Light / Dark

Both first-class Admin surfaces.

## Platforms

Desktop primary.

Tablet/mobile may:
- start simple migrations
- inspect scan/results
- review status/report

Complex mapping/profile-rule conversion is desktop-oriented.

TV unsupported.

## States

Required:
- not started
- connecting
- connection failed
- unsupported source version
- scanning
- scan partial
- conflicts
- mapping incomplete
- dry run ready
- running
- paused/retryable
- failed
- completed with warnings
- validated
- source disappeared
- permission denied

## Architecture constraints

- All media resolves into canonical Media Core.
- Legacy/source-native IDs remain provenance/evidence.
- Migration adapters emit normalized migration records/plans.
- Acquisition settings migrate into native Jularr AcquisitionProfile/scoring contracts.
- No Sonarr/Radarr scoring engine survives as runtime dependency.
- Progress imports into Progress domain.
- Users import through Accounts.
- Files/paths import through Library + Storage contracts.
- Migration may invoke Library Reconciliation for unresolved filesystem mapping or post-migration organization.
- Data migration and instance-configuration migration are separate plan sections.
- Source users require explicit account mapping before their progress/history is imported.
- Dry run and validation are required before destructive mutation.

## Must not implement

- No destructive big-bang migration.
- No source-media deletion/move by default.
- No title-only identity matching when stronger IDs exist.
- No uncontrolled dual-write authority.
- No permanent legacy/source tables as runtime authority.
- No silent dropping of unsupported Custom Format/rule conditions.
- No flattening all Sonarr monitor state into one Work boolean.
- No copying Sonarr/Radarr settings blindly when Jularr lacks a semantic equivalent.
- No automatic trust/import of indexer/download-client secrets without explicit protected review/test.
- No silent creation of Jularr users from Jellyfin/Plex/Emby source profiles.
- No rename/move of source media as part of normal Sonarr/Radarr/media-server migration.
- No approximate Custom Format conversion without an explicit conversion result/warning.
- No plaintext secret exposure.
- No skipping dry run for full migrations.
