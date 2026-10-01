# Calendar — Clean Design

Status: **planned UX baseline; ready for visual mockup review**.

This is the binding consumer specification for Jularr's unified cross-media release calendar.

The Calendar visualizes known upcoming or recently released canonical media units. It does not become a second scheduler, a second acquisition model or a provider-native media browser.

## Purpose

One clean calendar for:

- Anime episodes / season or cour premieres
- TV episodes / season premieres
- Movie release milestones where known
- Manga chapters / volumes
- Light Novel volumes
- Book publication / edition releases
- Audiobook releases later when reliable metadata exists

The user should answer quickly:

1. What releases today / this week?
2. Which of those matter to me?
3. Is an item already available, requested or still missing?
4. What date is actually known, and how precise is it?
5. Can I open the canonical Work/unit directly?

## Canonical data contract

Calendar items reference canonical media identity:

```text
Work
└─ optional canonical unit
   ├─ Episode
   ├─ Volume
   └─ Chapter
```

Calendar-specific release information is a projection over canonical media plus provider metadata/provenance.

A calendar event may contain:

- canonical Work ID
- optional canonical structural-unit ID
- release kind
- normalized release date/time
- date precision
- timezone/region where meaningful
- source/provenance
- canonical library/request/monitoring state projection

It must not create duplicate Anime/TV/Manga/Book identities.

Provider-native entries are evidence/presentation data only.

## Date precision

Never invent a more precise date than the source provides.

Supported presentation examples:

- `1 Oct · 20:00`
- `1 Oct`
- `October 2026`
- `Q4 2026`
- `2026`
- `Date unknown`

Imprecise items do not get assigned to fake days.

They appear in a dedicated **Later / Date TBA** section or other honest coarse grouping.

Timezone-sensitive events use the user's configured/local timezone for display while preserving source timing semantics.

## Release kinds

Release kind is compact secondary context, not a tag cloud.

Examples:

### Anime / TV
- Episode
- Season premiere
- Special

### Movie
- Cinema
- Digital
- Streaming
- Physical

Do not imply that Cinema release means downloadable/local availability.

### Manga
- Chapter
- Volume

### Light Novel
- Volume

### Book
- Publication
- Edition

### Audiobook
- Audiobook edition/release only where source data is reliable

## Page structure

Desktop baseline:

1. Page header
2. date navigation
3. view switch: Month / Week / Agenda
4. compact filters
5. primary calendar surface
6. optional selected-day / selected-event detail pane
7. Later / Date TBA section

No duplicated month title, duplicate prev/next controls, duplicate mini-calendar or redundant legend.

## Header

Show:

- `Calendar`
- current period label
- previous / Today / next controls
- view switch
- Filter action

Keep one authoritative period navigation row.

Do not repeat the same month/week title elsewhere.

## Views

## 1. Month

Desktop-oriented overview.

Each day cell can contain a small number of compact event rows/cards.

Each event shows only:

- artwork thumbnail/icon where useful
- title
- unit label
- time when exact
- compact state indicator

Overflow uses `+N more` and opens that day's agenda/detail rather than making cells unreadable.

Month view must remain a calendar, not a wall of full media cards.

## 2. Week

Useful for denser upcoming schedules.

Desktop/Tablet may use:

- seven-day columns; or
- a horizontal day header + vertical agenda list.

Exact timed events can show time.

All-day/date-only releases remain visually distinct from time-specific airing events.

## 3. Agenda

Primary Mobile view and valid Desktop alternative.

Group events by:

- Today
- Tomorrow
- weekday/date
- Later / Date TBA

Each event row/card may show:

- cover/poster thumbnail
- canonical title
- unit/release label
- date/time
- media type
- availability/request state
- preferred-language relevance only when it materially helps

Agenda should be the clearest view for mixed media.

## Selected event / day detail

Desktop/Tablet may show a compact right-side pane when an event is selected.

It may contain:

- artwork
- title
- release/unit label
- exact/coarse release date
- release kind
- compact availability state
- preferred language where relevant
- primary action: Open Details / Open Episode / Open Volume
- Request/Monitor only when permitted

This is not a full media-detail page.

Mobile opens the same content as a bottom sheet or compact detail screen.

## Filters

Keep V1 compact.

Primary media-type filters:

- All
- Anime
- Series / TV
- Movies
- Manga
- Light Novels
- Books
- Audiobooks when supported

State filters belong in a clean filter panel/sheet:

- Mine / relevant to me
- Available
- Requested / Wanted
- Missing
- Needs attention

Do not place a permanent chip for every possible state.

Permission-derived media visibility applies: hidden media types do not appear as disabled filters.

## User relevance

Calendar can combine:

- library items
- monitored/wanted items
- followed/watchlist items where product rules allow
- discoverable provider-backed upcoming media

These states must remain semantically distinct.

**Following/Watchlist is not the same as Monitored/Wanted.**

Do not label a followed item as acquisition-monitored unless canonical acquisition state actually says so.

## Availability state

Compact consumer states:

- Available
- Partial
- Requested
- Waiting
- Searching
- Downloading
- Importing
- Missing
- Needs attention

State comes from canonical Library/Acquisition services.

Calendar does not own or persist a second status state machine.

Normal users do not see release scores, indexers, download-client details or import diagnostics.

## Actions

Primary:

- select day
- select event
- change Month / Week / Agenda
- previous / Today / next
- filter
- open canonical media detail/unit

Optional when permitted:

- Request
- Monitor / Unmonitor
- Search now only for appropriately privileged users and only via canonical acquisition services

Calendar never implements acquisition itself.

## Deduplication

The same canonical release must render once.

Deduplicate using canonical identity + release kind/unit/date/source mapping rather than provider card identity.

If the same release comes from both:

- local-library event source; and
- followed/provider event source,

the canonical/local resolved event wins and state is merged into that one row.

Never show duplicate events because a followed Work later entered the local library.

## Provider behavior

External providers normalize into the Calendar query contract.

Expected sources may include:

- AniList
- TMDB
- book metadata providers
- future audiobook metadata providers

Calendar UI never consumes raw provider response shapes.

Provider calls must be bounded/cached.

The page should render from local normalized/cached state and remain useful during provider outages.

## Upcoming detail-page integration

Media detail pages may show a compact `Next release` / `Upcoming` section.

Examples:

- `Episode 31 · 2 Oct · 23:00`
- `Vol. 8 · 14 Nov`

This links into Calendar with relevant Work/date context.

Do not embed the full Calendar inside media detail pages.

## Light / Dark

Both first-class.

### Light

- white/soft-gray base
- restrained purple accent
- calm calendar grid
- artwork used sparingly
- state indicators subtle and readable

### Dark

- deep neutral surfaces
- grid boundaries remain visible without excessive borders
- event cards stay distinct
- no pure-black wall
- same hierarchy as Light

Media type/state is never represented by color alone.

## Desktop

Recommended composition:

- normal Jularr sidebar + top search
- header with period navigation and view switch
- Month or Week as primary central surface
- optional right-side selected event/day pane
- filters open as compact panel rather than permanent sidebar clutter
- Agenda available through view switch

Do not add a second mini month calendar beside the main month grid.

## Mobile

Default to **Agenda**.

Composition:

- app header
- compact date/period control
- Today shortcut
- filter button
- optional compact horizontal date strip
- agenda grouped by day
- event tap opens bottom sheet/detail
- Month view may exist as an optional compact navigation view, but it is not the only usable view

No cramped desktop month grid squeezed onto the phone.

## Tablet

Touch-first adaptive.

Portrait:
- Agenda or compact Month

Landscape:
- Month/Week plus optional selected-event pane

No hover assumptions.

## TV

Calendar is optional/simplified on TV.

Use:

- `Today`
- `This Week`
- upcoming horizontal/vertical rails
- large focusable events
- direct open to media detail

Dense Month grid is not required.

Focus uses the standard TV ring/glow + modest scale/lift.

## Loading

Preserve layout geometry.

Desktop:

- header controls render immediately
- calendar cell/agenda skeletons
- optional event-pane skeleton

Do not block the whole Calendar while optional provider data refreshes.

## Empty states

### No releases in period

Show concise:

`No known releases in this period.`

Offer:

- Today
- next period
- change filters

Do not show the same empty message twice.

### Filters produce zero results

State clearly that filters are hiding events and provide Reset filters.

### No media types visible

Respect permissions and show a neutral empty state rather than disabled categories.

## Partial / degraded states

Support:

- cached calendar data while provider refresh fails
- some providers unavailable
- release date known but exact time unknown
- conflicting provider dates
- canonical Work known but child-unit mapping unresolved
- library/acquisition state temporarily unavailable
- storage offline while release schedule remains visible

Provider failure must not erase valid cached events.

## Conflicting dates

Do not silently choose arbitrary precision.

If authoritative resolution is not possible:

- use the resolved canonical metadata policy where one source is preferred; or
- show a compact uncertainty state such as `Date may change`.

Raw provider conflict diagnostics stay in Admin/metadata tools.

## Notifications

Calendar may emit typed `newly known release` domain/application events for a separate notification system.

Calendar does not own:

- push notifications
- email/webhooks
- quiet hours
- notification subscriptions

Do not mix notification settings into the Calendar page.

## ICS

Private ICS subscription is optional later.

If implemented:

- profile-scoped
- revocable token
- read-only
- selectable media types
- no credentials/internal-sensitive data in URLs

ICS is not required for the main Calendar mockup.

## Accessibility

- semantic view switch and buttons
- view switch has accessible labels
- keyboard navigation on Desktop
- visible focus
- event date/time conveyed in text
- states not color-only
- screen readers receive full title/unit/date
- Today is programmatically identifiable
- touch targets meet Mobile requirements

## Navigation / back

Opening an event and returning preserves:

- selected date
- current view
- filters
- scroll position where practical

Visible Back behavior uses shared contextual navigation.

## Must not implement

- no provider-native duplicate Work identities
- no second release/acquisition status machine
- no duplicate events from watchlist + local sources
- no fake exact dates for month/quarter/year precision
- no assumption that theatrical Movie release equals acquisition availability
- no admin acquisition queue in consumer Calendar
- no raw provider IDs/indexer/download diagnostics
- no duplicated month title/navigation
- no mini calendar duplicating the main Month grid
- no permanent type legend duplicating the type filter
- no repeated empty-state text
- no Mobile desktop-grid squeeze
- no provider API fan-out on every render
- no notification-delivery system inside Calendar

## Mockup deliverables

First review should show:

1. Desktop Light Month view with:
   - mixed media
   - one selected event/day pane
   - compact filters
   - exact and date-only events
2. Mobile Light Agenda view
3. one `Date TBA / imprecise` state
4. one empty/filter-zero state
5. TV only later if Calendar is retained as a meaningful TV destination

After Light approval, derive Dark from the same structure.

Text specification wins over visual references on conflict.
