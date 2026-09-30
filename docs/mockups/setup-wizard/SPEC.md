# Setup Wizard — V1

Status: planning baseline for mockups. Show only for an unconfigured/new instance or explicit admin re-entry.

## Purpose
Establish a safe minimal Jularr instance without exposing every advanced setting.

## Page structure
Welcome -> database/system validation -> owner account -> instance/region -> storage root(s) -> optional provider/download configuration -> initial media scan/import choice -> review -> finish.

## Data / information
Environment readiness, PostgreSQL connectivity, owner/profile, locale/timezone, permitted LibraryRoots, optional provider health and migration/import choices.

## Actions
Validate, configure minimum required values, test connections, skip optional integrations, finish and enter user/admin UI.

## Light / Dark
Both first-class; theme can be selected during setup but defaults remain usable.

## Platforms
Desktop/tablet primary; mobile supported for simple setup. TV setup is not required beyond pairing/server selection in native client flows.

## States
Fresh instance, validation warning/error, storage unavailable, optional provider failure, resumable incomplete setup, completed.

## Must not implement
No SQLite production setup path, no requirement to configure every provider, no arbitrary filesystem access, no default broad permissions for non-owner users, no media-type-specific database setup.