# Admin Manual Search / Acquisition Dialog — V1

Status: approved planning direction; mockup required before implementation.

Global UX rules: `docs/UX.md`.
Wanted contract: `docs/mockups/admin-wanted/SPEC.md`.

## Purpose

Manual Search is the **Search tab of the reusable Admin Acquisition dialog** for one canonical acquisition target.

It is reachable from:
- Admin Wanted
- Admin Media Detail
- other authorized Admin acquisition actions

It is not a separate acquisition pipeline and does not need a standalone full-page route for V1.

## Dialog tabs

Exactly three primary tabs:
1. Search
2. Current
3. History

Search opens by default when the dialog is launched through Manual Search.

## Search header

Always show:
- target Work
- target Structure/unit
- effective acquisition profile
- target language(s)
- monitored/acquisition state
- Search/Refresh

Profile and language may be temporarily changed for the current search. Temporary values re-score results but do not silently mutate the saved target configuration.

## Candidate table

Desktop uses a dense configurable table.

Default columns:
- decision state
- effective-profile score
- title
- source/indexer
- age
- size
- quality
- language
- audio
- subtitles
- release group
- release type
- parsed target
- match confidence
- action

Release type explicitly distinguishes:
- single unit
- multi-unit
- season/collection pack

Optional columns may expose additional indexer metadata.

A column chooser controls optional columns. Do not force every available provider field into the default layout.

## Filtering and sorting

All meaningful candidate fields must be filterable where useful:
- decision state
- score
- quality
- language
- audio
- subtitles
- size
- age
- source/indexer
- group
- release type / season pack
- match confidence
- rejection reason

Default order:
1. effective-profile score
2. eligible before warning before rejected
3. configured source preference

## Score model

The main score is always contextual to the selected **profile + language target**.

It may include:
- quality preference
- language policy
- audio/subtitle requirements
- custom release preferences
- source preference
- season-pack preference
- size/age rules
- other profile-owned criteria

Candidate detail shows the full score breakdown.

Optional comparison columns can show scores from other configured profiles. These are secondary comparison data and never replace the main effective score.

## Rejected and suspicious candidates

Rejected results remain visible by default.

This includes candidates Jularr believes are:
- wrong episode/unit
- wrong season
- ambiguous
- already satisfied by a better local file
- below profile minimum
- wrong language
- blocked
- previously failed

Rows show a compact warning/rejection indicator and exact reasons are available in row detail.

For identity mismatch, show:
- requested canonical unit
- parsed candidate unit
- confidence
- reason for mismatch

Automatic acquisition cannot choose an identity-mismatched candidate.

An authorized manual override may be possible for reviewable warnings. Identity override requires explicit confirmation and explicit target mapping.

Hard safety/integrity failures remain non-overridable.

## Candidate detail drawer

Shows:
- raw release title
- normalized parsed fields
- provider/indexer
- age/publish time
- size
- quality
- languages
- audio
- subtitles
- group
- release type
- parsed units
- target match
- score breakdown
- rejection/warning reasons
- prior failure/blocklist evidence
- grab action

Raw provider data is diagnostic only.

## Current tab

Shows existing target state:
- canonical identity
- monitored state
- active profile
- target languages
- desired Version/Edition
- current local Asset/File if present
- existing quality/audio/subtitle/language state
- why this target is Wanted

## History tab

Shows target-scoped acquisition history:
- search
- automatic decision
- manual grab
- override
- download handoff
- import
- failure
- blocklist

Each event records profile/language context, actor and timestamp.

## Responsive

Desktop:
- large dialog
- full configurable table
- detail drawer

Tablet:
- wide sheet
- reduced default columns
- candidate detail sheet

Mobile:
- full-screen sheet
- candidate cards instead of dense table
- filters in sheet
- same scoring/rejection information
- no hover-only affordances

TV:
- unsupported

## Light / Dark

Both first-class.

Keep Admin styling compact and restrained. Warning/rejection colors supplement text/icons; color is never the only signal.

## Loading / Empty / Error / Partial

Required states:
- searching
- no candidates
- mixed eligible/rejected
- all rejected
- partial provider failure
- provider timeout/rate limit
- malformed candidate
- ambiguous target
- grab queued
- grab error

Successful results from one source remain usable when another source fails.

## Domain constraints

- Search target references the canonical media hierarchy.
- ReleaseCandidate remains temporary external acquisition evidence.
- A release does not become a Version/Asset/File until the later acquisition/import workflow establishes it.
- Identity acceptance precedes profile ranking.
- Scoring must not create media identity.

## Must not implement

- No hidden rejected rows by default.
- No quality score used to override wrong identity automatically.
- No global intrinsic release score.
- No silent persistent profile/language change from temporary search controls.
- No provider-specific parallel release models.
- No automatic parser learning from a force-grab.
- No desktop-only hover dependency.
