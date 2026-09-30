# Admin Manual Search — V1

Status: planning baseline for mockups.

## Purpose
Inspect normalized ReleaseCandidates for one canonical Wanted/Work/unit target and manually grab when authorized.

## Page structure
Target summary -> effective profile/languages -> candidate filters/sort -> candidate table/cards -> candidate detail drawer.

## Data / information
Indexer/source, title, age, size, parsed identity, quality/format, languages, group/source, score, acceptance/rejection reasons, prior failures/blocklist and target match confidence.

## Actions
Refresh search, inspect scoring/reasons, grab candidate, open provider health, return to Wanted/Media Detail.

## Light / Dark
Both first-class Admin surfaces; restrained status styling.

## Platforms
Desktop primary dense table. Mobile/tablet stacked candidate cards/detail sheets. TV unsupported.

## States
Searching, no candidates, accepted/rejected mix, partial indexer failure, rate limited, target ambiguous, grab queued, error.

## Must not implement
No separate search/acquisition pipeline, no quality scoring before identity validity, no raw provider model persistence as Version before import, no hidden rejection reasons, no consumer exposure.