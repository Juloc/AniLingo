# Calendar — Clean Design

Status: **approved planning direction for Desktop Month; implementation requires a final pre-merge UX approval**.

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

Approved Desktop Month baseline:

1. normal Jularr left sidebar
2. compact Calendar toolbar
3. media-type filter row
4. full-width month grid
5. Later / Date TBA section only when needed

The Calendar itself should use the available content width. There is **no permanent right-side event-detail pane** in the approved Month direction.

Do not add decorative header art, redundant top navigation, duplicate period controls, a mini-calendar or a permanent legend.

## Toolbar

Keep the Calendar chrome compact and functional.

Approved Desktop order:

1. Calendar search field on the **left**
2. `All Status` filter directly after the search
3. view switch: `Month / Week / Agenda`
4. previous period button
5. current period label, e.g. `October 2026`
6. next period button
7. `Today`

A separate `All Media` dropdown is not used in this baseline because media type is already handled by the filter row below.

The current period label sits between previous and next.

Do not repeat the same month/week title elsewhere.

## Views

## 1. Month

Desktop-oriented overview and the currently approved visual direction.

### Grid density

The month grid must remain recognizably a normal calendar:

- realistic distribution of **0–6 events per day**;
- many days may be empty;
- some days may contain several releases;
- do not artificially place exactly one event on nearly every day;
- day cells must be tall enough to show up to six compact rows before overflow is considered.

If more than six events exist, use `+N more` or open the day's Agenda rather than shrinking rows further.

### Event row

Each event is a compact horizontal row, not a poster card.

Show:

- small cover/artwork thumbnail on the left;
- title;
- unit/release context, e.g. `Ep 4`, `Vol 9`, `Movie`, `Book 1`;
- time when exact, otherwise `All day`/date-only semantics.

Do **not** show separate Anime/Manga/Movie text badges inside every event.

Media type is communicated through the shared media-type color system:

- title uses the media-type accent color;
- row gets a very light tint of the same media color;
- tint stays subtle enough for dense calendar reading.

The cover should be sized to fit the row and remain closer to a small square/compact crop than a tall poster. If a day contains only one event, the artwork may be slightly larger, but the row still belongs to the calendar grid.

Do not use extra event icons merely to restate media type, time or generic state.

### Release significance

Premiere, finale/ending, new volume/publication and similar release significance may be represented only with a **small, low-noise marker/text treatment** when needed. It must not become another colored tag system or materially increase row height.

The exact marker treatment remains subject to the final pre-merge UX review.

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

## Event interaction

The approved Desktop Month baseline has **no permanent right-side detail pane**.

Selecting an event should use one of these lightweight patterns:

- open the canonical detail/unit directly; or
- open a compact popover/sheet when quick actions are useful.

The final interaction choice must be reviewed before merge.

Any quick detail surface may include:

- title and cover
- release/unit label
- exact/coarse release date
- release kind
- compact availability/monitoring state
- primary Open action
- Request/Monitor only when permitted

It must not duplicate the full media-detail page.

## Filters

Keep V1 compact.

### Media-type row

Directly under the toolbar:

- All
- Anime
- Series / TV
- Movies
- Manga
- Books
- Light Novels
- Audiobooks when supported

These are text filter controls, not icon buttons.

### Status filter

`All Status` sits in the top toolbar immediately after Calendar search.

Its semantics are about **Jularr state**, not media type.

Candidate states include:

- Available
- Monitored
- Requested / Wanted
- Missing
- Downloading / Importing
- Needs attention

The exact final state list, grouping and naming are **not merge-approved yet** and require final owner review during implementation.

Do not place a permanent chip for every state.

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

## Availability / monitoring state

Calendar status always comes from canonical Library/Acquisition/Monitoring state.

Relevant consumer semantics include:

- Available
- Partial
- Monitored
- Requested / Wanted
- Waiting
- Searching
- Downloading
- Importing
- Missing
- Needs attention

The Month grid should not be overloaded with large status badges. Status can be exposed through the status filter and, where useful, a restrained row state/text treatment.

The exact visible in-grid treatment for `Monitored` vs `Available` remains part of the mandatory pre-merge UX review.

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

Approved Month composition:

- normal Jularr **left sidebar**;
- no redundant top Home/Browse/Discover/Calendar navigation row;
- no decorative Calendar subtitle/header copy;
- Calendar search begins the toolbar at the upper left;
- `All Status` follows search;
- Month/Week/Agenda follows;
- previous → current period → next → Today are grouped clearly;
- media-type text filters sit on the next row;
- full-width Month grid uses the remaining content area;
- no permanent right-side detail panel;
- no extra list/view icon beside the filters;
- realistic sparse/dense days with up to six visible events;
- each event uses a compact cover + light media-color tinted row.

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
- no redundant top consumer-navigation row above the Calendar
- no decorative Calendar subtitle/header copy in the approved Desktop baseline
- no `All Media` dropdown duplicating the media-type filter row
- no extra generic list/view icon beside the toolbar
- no permanent right-side event detail pane
- no duplicated month title/navigation
- no mini calendar duplicating the main Month grid
- no permanent type legend duplicating the type filter
- no per-event media-type badge/tag clutter
- no per-event icon clutter
- no tall poster cards inside normal Month cells
- no artificial one-event-per-day distribution
- no repeated empty-state text
- no Mobile desktop-grid squeeze
- no provider API fan-out on every render
- no notification-delivery system inside Calendar

## Current mockup direction

The current Desktop Light Month mockup direction is accepted as the planning baseline.

It establishes:

- normal calendar proportions;
- search + status + view/date toolbar;
- separate media-type filter row;
- full-width grid without a permanent detail pane;
- realistic 0–6 events per day;
- small cover before each event;
- compact horizontal event rows;
- light media-type background tint and media-colored title;
- no per-event icon/tag clutter.

This is **not yet final merge approval** for all interaction details.

## Mandatory pre-merge UX approval

Calendar implementation may be developed against this specification, but **must not be merged as complete without a final owner UX review**.

That review must explicitly cover at least:

- final status filter values and naming;
- distinction between `Monitored`, `Available`, `Requested/Wanted` and related states;
- final in-grid status treatment;
- premiere/finale/release-significance treatment;
- Calendar search behavior;
- Month/Week/Agenda switching;
- event click/open behavior;
- overflow behavior for >6 events;
- responsive Mobile/Tablet layout;
- Light/Dark consistency.

If the implementation materially differs from the approved planning mockup, update this SPEC before merge.

Text specification wins over visual references on conflict.
