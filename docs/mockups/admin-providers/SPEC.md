# Admin Provider Settings — V1

Status: approved planning direction; current Provider mockups are the visual baseline.

Global UX rules: `docs/UX.md`.

Approved visual references:
- `file_00000000fa44821084564dade822a7ea.png` — Provider overview + family-specific detail examples
- `file_0000000022e08210874385e435e23251.png` — Provider overview, add wizard, capabilities, filters/search, statistics, rate-limit and logs

If an image and this specification conflict, this specification wins.

## Purpose

Provider Settings is the shared Admin control surface for external sources/services used by Jularr.

It covers provider configuration, capabilities, priority, filters, health, rate limits, statistics, tests and logs without giving every adapter a completely separate UI.

Provider families:
- Identity / Login
- Indexer / Search
- Metadata
- Subtitles
- Translation
- Reading Sources
- Other explicitly supported external services

## Product boundaries

Provider Settings owns external provider adapters and their configuration.

It does not own:
- canonical media identity
- AcquisitionProfile scoring
- native Downloader server configuration
- Storage paths
- global Activity/History
- dedicated AI task policy

Family responsibilities:
- Identity/Login -> external authentication and Account identity linking, only when an adapter explicitly supports it
- Indexer/Search -> normalized ReleaseCandidates for Acquisition
- Metadata -> identity/metadata evidence
- Subtitle -> subtitle candidates/assets
- Translation -> translation execution/capabilities
- Reading Source -> searchable/readable written-content sources

## Overview page

Desktop uses:
- Admin shell
- provider-family switch
- compact summary
- search/filter toolbar
- provider table
- add-provider action

### Family switch

Primary families:
- Alle
- Identität / Login
- Indexer / Suche
- Metadaten
- Untertitel
- Übersetzung
- Lesequellen
- Sonstige

Show useful counts per family.

### Summary

May show:
- total providers
- online
- degraded
- offline
- average response time

Do not turn this into a second Admin Dashboard.

### Provider table

Default columns:
- Name
- Type/family
- Enabled
- Health
- Priority
- Last response
- Rate limit / quota
- Capabilities
- Actions

Optional columns:
- last successful test
- error count
- media/content types
- endpoint
- current quota usage

Per-row actions:
- test
- edit
- enable/disable
- more/remove where allowed

## Add Provider wizard

`Provider hinzufügen` uses five steps:

1. **Typ wählen**
2. **Provider**
3. **Konfiguration**
4. **Fähigkeiten**
5. **Test**

### Typ wählen

Choose the provider family.

### Provider

Choose a known adapter/plugin/provider implementation.

Do not ask the user to invent implementation IDs.

### Konfiguration

Schema-driven provider fields may include:
- endpoint/base URL
- Login enabled / auto-provision policy where the Identity adapter supports it
- API key
- username/password
- timeout
- locale/region
- API version
- provider-specific optional fields

Secrets are masked/write-only after save.

### Fähigkeiten

Choose/confirm capabilities Jularr may use.

Identity/Login examples:
- Account sign-in
- external identity linking
- optional account auto-provisioning
- optional Profile Connection/sync as a separate capability

Media/provider examples:
- Anime
- Series
- Movies
- Manga
- Books
- people
- artwork
- external IDs
- subtitle languages
- translation languages
- reading previews/content

Only adapter-declared capabilities may be enabled.

### Test

Test:
- connection/authentication
- capability discovery where relevant
- one lightweight provider-specific request

Saving must not trigger uncontrolled full searches/scans.

## Provider detail

Use one shared detail shell.

Header:
- name/icon
- family/type
- safe endpoint summary
- enabled state
- health
- Test
- Actions

Capability-dependent tabs:
- Einstellungen
- Fähigkeiten
- Filter & Suche
- Rate-Limit
- Statistiken
- Logs

Only show useful tabs for that provider.

## Einstellungen

Common fields:
- name
- enabled
- priority
- timeout
- endpoint/base URL
- authentication/secrets
- locale/region where relevant

Provider-specific fields come from a validated configuration schema.

Do not build a bespoke layout for every adapter.

## Fähigkeiten

Show supported capabilities with explicit per-capability enablement and priority where meaningful.

Metadata examples:
- Work metadata
- season/episode metadata
- people/cast
- collections/franchises
- discovery/trending
- images/artwork
- external IDs

Indexer/Search examples:
- Anime
- Series
- Movies
- Manga
- Books
- Games
- Software
- Generic

A provider may have different priority for different capabilities/content types.

Priority semantics must be explicit.

### Identity / Login capabilities

Identity/Login providers may expose:
- Account sign-in;
- link/unlink external login identity;
- optional account auto-provisioning;
- optional profile Connection/sync capabilities as a separate capability set.

Examples can include Plex, Jellyfin, Trakt, AniList, MAL, Google and future adapters, but only actual adapter capabilities may be enabled.

Admin decides which configured providers appear on Login. Auto-provisioning must use explicit conservative default roles/capabilities and may never grant Owner/Admin implicitly.

A provider's Login capability is distinct from a user's Profile Connection/synchronization configuration.

## Filter & Search

Only for providers that support search/discovery/indexing.

Possible settings:
- search timeout
- max results
- sorting
- category/content-type filters
- language restrictions
- season/episode formatting behavior
- provider-specific category IDs
- adapter-level duplicate suppression where appropriate

Indexer/Search providers include **Search Test**.

Search Test shows normalized results rather than raw provider objects and does not bypass Acquisition scoring/import policy.

Useful result fields may include:
- title
- type
- size
- age/date
- source/category
- language
- protocol-specific availability where relevant
- diagnostic provider identifier

## Rate-Limit / Quota

Where supported show/configure:
- request limit
- request window
- current usage
- remaining quota
- reset time
- backoff
- automatic backoff on rate-limit responses

Fixed provider limits are read-only.

Do not fabricate configurable limits.

## Statistics

Provider telemetry may include:
- requests
- success/failure count
- success rate
- average response time
- rate-limit events
- auth failures
- timeouts
- cache hit ratio where relevant

Periods:
- 24h
- 7d
- 30d

This is provider telemetry, not general system telemetry.

## Logs

Provider-scoped structured diagnostics:
- timestamp
- level
- operation
- concise message
- correlation/job reference where useful

No credentials, API keys or sensitive payloads.

Allow filtering by level/operation/time.

Deep-link to global diagnostics for deeper logs.

## Health model

Shared states:
- Healthy
- Degraded
- Rate limited
- Authentication failed
- Unavailable
- Disabled
- Unknown

Health should use meaningful provider checks where possible, not just ping.

Partial capability failure may be Degraded rather than fully Offline.

## Priority / fallback

Priority may be scoped by capability/content type.

Example:
- AniList priority 1 for Anime metadata
- TMDB priority 1 for Movies
- another provider priority 2 as fallback

Fallback is deterministic and visible.

Provider priority never creates canonical identity by itself.

## Secrets

Secrets:
- write-only/masked after save
- never returned in clear text
- never included in logs/statistics
- unrelated edits should not require re-entering them

## Light / Dark

Both first-class.

Use compact Admin styling:
- restrained semantic colors
- no decorative artwork
- no saturated badge wall
- compact provider icons/logos allowed
- state never communicated by color only

## Platforms

### Desktop
Primary:
- table
- detail tabs/forms
- Search Test
- Statistics

### Tablet
- stacked list/detail
- scrollable tabs
- touch-friendly forms

### Mobile
- provider cards/list
- full-screen detail/sheet
- stats/search results as stacked rows

### TV
Unsupported.

## Loading / Empty / Error / Partial states

Required:
- no providers
- family empty
- loading
- healthy
- degraded
- offline
- rate limited
- auth failed
- invalid configuration
- capability discovery failed
- test running/success/failure
- statistics unavailable
- logs empty
- disabled
- permission denied
- partial family outage

## Domain / architecture constraints

- Provider/native IDs are provenance/evidence, not canonical Work identity.
- Provider adapters return normalized application contracts.
- Identity/Login providers resolve to internal Jularr Accounts; provider IDs never become canonical Account/Profile IDs.
- Login and Profile Connection/sync are separate provider capabilities.
- UI does not use provider-native DTOs as the product model.
- No provider owns a parallel media hierarchy.
- Search/indexer results remain external candidates until Acquisition resolves them.
- Metadata merge/provenance policy belongs to Metadata.
- Provider tests are explicit application actions, not uncontrolled render-time calls.

## Must not implement

- No giant universal interface pretending all providers are identical.
- No fully bespoke page design for every adapter.
- No cleartext secrets after save.
- No provider ID as canonical identity.
- No uncontrolled external requests during page render.
- No Search Test that directly grabs/downloads.
- No Indexer-specific parallel Acquisition engine.
- No silent fallback with unexplained priority.
- No health based only on ping when stronger checks exist.
- No logs containing secrets or sensitive payloads.
