# Admin Acquisition / Downloader Settings — V1

Status: planning baseline updated for Jularr's native Usenet downloader.

Global UX rules: `docs/UX.md`.

## Purpose

Configure the universal acquisition stack:
- acquisition profiles/scoring
- indexers/release-search providers
- Jularr native Usenet downloader
- optional external download-client adapters
- routing/categories
- import defaults

Normal Jularr operation must not require SABnzbd or NZBGet.

## Primary sections

1. Profiles & Scoring
2. Indexers
3. Native Usenet
4. External Download Clients
5. Routing / Categories
6. Import Defaults

Native Usenet is the default downloader path.

External clients are compatibility/migration integrations, not the center of the product.

## Native Usenet settings

Configure downloader behavior such as:
- Usenet servers
- TLS
- credentials/secrets
- server priority/failover
- connection limits
- bandwidth limits
- queue defaults
- retry policy
- verification/repair policy
- extraction policy
- duplicate/history policy
- categories/routes
- health/test

Storage paths are not configured as arbitrary strings here.

The downloader selects a configured Native Download Workspace from Storage.

## Storage integration

Binding storage spec:
- `docs/mockups/admin-storage/SPEC.md`

Acquisition/downloader settings reference:
- Native Download Workspace
- optional repair/extract workspace
- Generic Downloads Root
- LibraryRoots through routing/import policy

Physical Mount/path management remains in Storage.

## External download clients

Optional adapters may include SABnzbd/NZBGet-compatible or other supported clients.

Client configuration may include:
- endpoint
- authentication
- category mapping
- remote path mapping
- health/test
- capabilities

External client paths must resolve into permitted Storage paths before import.

## Profiles & scoring

Reusable AcquisitionProfiles include:
- quality
- language
- release type
- custom/release rules
- score contributions
- upgrade thresholds
- size/age limits where applicable

No media-specific parallel scoring engines.

## Routing

Routing can decide:
- specialized LibraryRoot destination by canonical content type
- Generic Downloads fallback
- category/priority
- preferred downloader path where external adapters are explicitly enabled

Routing must not duplicate canonical media identity.

## Light / Dark

Both first-class.

## Platforms

Desktop primary; tablet/mobile may use stacked settings editors. TV unsupported.

## States

Required:
- unconfigured native downloader
- healthy/degraded Usenet server
- no usable Usenet server
- workspace unavailable
- insufficient workspace capacity
- external client unavailable
- invalid rule
- no eligible route
- test running/failure
- unsaved changes
- permission denied

## Must not implement

- No Anime-only acquisition settings.
- No requirement for SABnzbd/NZBGet in normal operation.
- No external Download Clients section presented as the only downloader.
- No arbitrary native downloader filesystem paths that bypass Storage.
- No `IsBooks`-style routing booleans.
- No separate per-media acquisition engines.
- No raw YAML as primary UX.
- No hidden scoring rules.
