# Profile / Activity — Clean Design

Status: **planned UX baseline; ready for Light-mode mockup review**.

This is the binding consumer specification for the signed-in user's own Profile, media Activity and personal Devices/Sessions surface.

It is not a social profile, analytics dashboard or Admin session monitor.

## Purpose

The Profile area should answer:

1. Which profile/account am I using?
2. What have I recently watched, read or listened to?
3. What am I currently consuming?
4. Which of my devices/sessions are active?
5. Where do I reach personal Settings and account actions?
6. If authorized, where do I enter Admin without mixing Admin data into Profile?

Only the signed-in user's own personal state is shown.

## Information architecture

Profile is one coherent consumer area with shared profile chrome and tabs:

1. **Activity** — default
2. **Stats**
3. **Ratings**
4. **Friends**
5. **Settings**

The profile hero, mini stats and activity heatmap stay above the tabs.

Do not create many tiny account pages.

### Desktop / wide tablet

Prefer one direct page: compact Profile header, Current Sessions, chronological Activity, Devices and personal links. Activity is the dominant content.

### Mobile

Profile acts as the account/navigation hub. It contains profile identity, current-session summary, recent Activity preview, Downloads/Offline when implemented, Devices, Settings and Admin only when authorized.

Activity and Devices may open dedicated mobile subviews because long lists should not be squeezed into the Profile landing screen.

### Desktop Activity navigation

If Desktop retains a direct Activity destination, it must render the same canonical Activity surface/data as Profile Activity rather than creating a second history implementation.

## Canonical data contract

Use shared personal-state/session concepts:

- `Profile`
- canonical `MediaProgress`
- canonical consumption / playback / reader history
- `ActiveSession`
- registered/recent device identity where supported
- offline/download state when implemented

Activity entries reference canonical Work plus optional Episode / Volume / Chapter targets.

No UI-specific AnimeHistory, MangaHistory, BookHistory or AudiobookHistory stores.

Admin Jobs/Operations history is unrelated and must never be mixed into personal Activity.

## 1. Profile header

Keep compact.

Show:

- avatar
- display/profile name
- account identifier/email only when useful
- profile switch action when multiple profiles are allowed
- Edit Profile where supported
- Settings shortcut

Optional role/restriction information appears only when useful for navigation. Do not show permission dumps, database IDs, server information or giant statistics.

### Profile switching

If several local profiles are available, the current profile is obvious and switching uses a compact dialog/sheet. Switching changes all profile-scoped progress, activity and preferences. Histories are never merged between profiles.

TV may prioritize profile switching more strongly than Desktop.

## 2. Current Sessions

Show only the signed-in user's active playback, reading or listening sessions.

Each row/card may contain:

- media artwork
- Work title
- episode/chapter context
- current progress
- device/client name
- last active/current state
- Resume/Open action
- terminate own session where supported

Normal users must not see IP address, transcoding reason, codecs/bitrates, CPU/GPU, other users or Admin session diagnostics.

If no session is active, omit the section or use only a minimal empty state.

## 3. Activity / History

Chronological personal media history. This is not an analytics feed and not a server event log.

One visual grammar handles Watched, Read, Listened, Completed and meaningful Resume activity.

Do not log every seek, pause or autosave as a visible Activity entry.

Each entry may show:

- small cover/poster
- Work title
- season/episode, volume/chapter or audiobook chapter context
- action/result
- date/time
- final/current progress where useful
- device only when useful
- Resume/Open when still resumable

Group chronologically: Today, Yesterday, This week, then older dates/months. Do not group primarily by media type.

### Filters

Keep compact: All, Watching, Reading, Listening, Completed. Date/search can live in a filter panel/sheet if needed. Do not create a chip wall.

### History actions

Where supported: open media, resume, remove one personal history item, or clear history through a Settings/privacy flow with confirmation.

Deleting visible history must not silently reset canonical progress. History deletion and progress reset are separate actions.

## 4. Ratings

Ratings are a first-class personal media feature and use one universal canonical rating model across all media types.

The Ratings tab shows the signed-in profile's ratings in a compact sortable/filterable list or grid.

Useful information:

- cover/poster
- Work title
- media type only where context requires it
- user's rating rendered in the user's selected rating system
- date rated / last changed
- optional short review/comment when that feature exists

Actions:

- change rating
- remove rating
- open Work
- filter/sort by media type, score and date

Do not create separate AnimeRating, MovieRating, MangaRating, BookRating or AudiobookRating stores.

### Rating display system

Each profile can choose how ratings are entered and displayed.

Supported presentation/input systems:

- **Three-level thumbs**: Thumbs Down / Thumbs Up / Double Thumbs Up
- **5 stars**
- **0–10 integer**
- **0.0–10.0 decimal**
- **0–100**

The selected system is a **profile preference**, not a database schema choice.

Changing the display system must never rewrite all stored ratings. A canonical normalized score is converted only for display/input.

For discrete systems such as thumbs:

- existing canonical values are bucketed for display;
- choosing a thumb state writes a defined canonical anchor value;
- `No rating` remains distinct from the lowest possible rating.

The exact visual control for each rating system belongs to shared components so Detail pages, Ratings tab and Activity use the same behavior.

### Rating visibility in Activity

Activity may show a rating only when the activity item genuinely includes a rating action/change.

Normal Watch/Read/Listen Activity rows must not automatically show rating badges.

## 5. Devices

Personal devices/clients only.

Each device may show friendly name, device/client type, last active, current-session state, offline/download capability where applicable, Rename, and Sign out/Revoke where supported.

Current device should be identifiable. Stale devices can move behind Show inactive devices. Never show another user's devices.

## 6. Personal links

Use compact navigation rows, not dashboard tiles:

- Settings
- Downloads / Offline when implemented
- Account & Security
- Admin only if authorized

Entering Admin changes to the Admin information architecture. Admin widgets never render inside Profile.

## Activity vs Progress

`MediaProgress` answers where the user currently is and what is completed. Activity/history answers what consumption happened over time.

Deleting Activity does not automatically reset Progress. Reset Progress is an explicit separate confirmed action.

## Light / Dark

Both are first-class. Light uses white/soft-gray surfaces and restrained Jularr accent. Dark keeps the same hierarchy on deep neutral surfaces. Media artwork provides most visual color.

## Desktop

Recommended first mockup:

- standard Jularr left sidebar + top global search
- page title `Profile`
- compact profile identity row
- two-column layout
- large left column: Current Session when active + Activity timeline
- compact right column: Devices + personal links
- Activity filters directly above the timeline
- small artwork, compact rows, no giant hero/banner

The page should look like a clean account/history surface, not an Admin dashboard.

## Mobile

Profile is a primary navigation destination.

Landing order:

1. profile identity
2. active session if present
3. recent Activity preview
4. Activity / Downloads / Devices / Settings / Admin navigation rows as capabilities allow

Full Activity uses chronological stacked rows with compact filtering. Full Devices uses touch-sized rows and places revoke/sign-out behind overflow/confirmation.

## Tablet

Portrait stays close to Mobile. Landscape may use the Desktop two-column layout. Touch remains primary.

## TV

TV Profile is deliberately limited to current profile, profile switching, recent/continue activity, current TV session and TV-relevant Settings.

Account-security forms, dense history management, broad device administration and Admin hand off to Web/Mobile.

## Loading / Empty / Partial / Error

- Use skeletons for identity, active session, several Activity rows and Devices.
- New profile: concise `No activity yet` plus Home/Discover destination.
- No active session: omit the section.
- No extra devices: show current device only.
- Zero-result filter: explain filters are hiding history and offer Reset.
- Section failures stay local; Profile identity should not block on optional history/device systems.
- Old history may remain visible even when media/storage is currently unavailable.

## Privacy / permissions

- only own Activity/Sessions/Devices
- same restriction enforced server-side
- Admin link only with capability
- hidden capabilities disappear rather than rendering disabled clutter

## Accessibility

Semantic headings, keyboard access, visible focus, accessible full timestamps, textual progress/session state, touch-sized Mobile targets and predictable TV focus order.

## Navigation / back

Opening media from Activity and returning should preserve Activity filter, scroll position and source context where practical. Visible Back uses shared contextual navigation.

## Must not implement

- no social followers/friend profile system
- no XP/gamification dashboard
- no separate rating tables or scales per media type
- no storing a user's chosen visual rating scale as the canonical score itself
- no watch-time statistics wall
- no Admin operations/jobs/history
- no other users' sessions/devices
- no IP/codec/transcode/server-load details
- no parallel per-media history stores
- no giant dashboard tiles
- no separate Activity implementation for Desktop nav vs Profile
- no progress reset when merely deleting history
- no Admin widgets embedded in Profile
- no dense account/security forms on Profile overview
- no disabled forbidden actions shown as clutter

## Mockup deliverables

First review:

1. Desktop Light: shared profile hero + mini stats + heatmap, then Activity as the default tab.
2. Desktop/Mobile Stats tab.
3. Desktop/Mobile Ratings tab using the selected profile rating system.
4. Desktop/Mobile Friends tab.
5. Desktop/Mobile Settings tab.
6. Mobile Activity state.

Later only where useful: Dark derivation, new-profile empty state and TV profile/focus state.

Text specification wins over images on conflict.