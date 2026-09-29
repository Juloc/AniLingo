# Admin Wanted — V1

Status: approved UX direction from planning mockups.

Global UX rules: `docs/UX.md`

## Purpose

Wanted is the Admin acquisition worklist for content Jularr still needs.

It answers:
- What is missing?
- What is being searched for?
- What was requested and approved for acquisition?
- What searches failed?
- What profile/language/version is desired?
- What should the admin do next?

Wanted is technical acquisition state, not user request moderation.

## Tabs

Primary tabs:
- All
- Requested
- Missing
- Searching
- Failed

`Requested` here means an approved acquisition need that originated from a user/admin request and is now part of the acquisition pipeline.

User approval/moderation itself belongs to Admin Requests.

## Desktop layout

Use:
- persistent Admin sidebar
- title
- state tabs with small count pills
- search
- filters
- structured Wanted table
- sort and pagination

Recommended filters:
- media type
- language
- profile
- priority
- status
- requester/source where useful

Recommended columns:
- Work / unit
- Media type
- Desired language/profile/version
- Status
- Last search/result
- Actions

Examples of units:
- Anime/Series episode
- Movie version such as 4K HDR Remux
- Manga volume
- Light Novel volume
- Book edition

## Actions

Per item:
- Automatic search
- Automatic… with temporary profile/language options
- Manual search
- Open media detail
- Pause/unmonitor where applicable
- Inspect last failure/history

Bulk actions are allowed for compatible selected items.

## Mobile layout

Wanted items become large stacked cards.

Each card prioritizes:
- artwork/title
- unit identity
- media type
- desired language/profile/version
- acquisition status
- last search age/result
- overflow actions

Search/filter controls remain easy to use with >=44px touch targets.

## Visual language

- Light Admin design.
- No full-color pills.
- Status/type tags use border + lightly tinted background.
- Small icon accents are allowed.
- Avoid large decorative artwork/backgrounds.

## Relationship to Manual Search

Manual Search is a drilldown from a Wanted item.

Wanted identifies the need.
Manual Search shows the concrete release candidates and their scores/rejection reasons.

## Acceptance criteria

- Requested, Missing, Searching and Failed acquisition states are distinguishable.
- Wanted supports all media types and version/edition granularity.
- Movie Wanted can represent a missing monitored version, not only a missing whole movie.
- Last search/result is visible.
- Automatic and Manual Search are directly reachable.
- Desktop and Mobile use the same data/state model with different layouts.
