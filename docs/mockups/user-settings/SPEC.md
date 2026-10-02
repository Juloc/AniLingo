# User Settings — Clean Design

Status: **planned UX baseline; integrated into the shared Profile / Account shell**.

User Settings is not a separate disconnected application area. It is the **Settings tab** inside the shared Profile/Account page defined by `docs/mockups/profile-activity/SPEC.md`.

## Shared account shell

Above the Settings tab, keep the same account chrome used by Activity / Stats / Ratings / Friends:

- profile hero/banner
- avatar + nickname
- Edit Profile action
- compact lifetime mini stats
- activity heatmap
- tabs: `Activity · Stats · Ratings · Friends · Settings`

Settings does not create another profile header or duplicate account navigation.

On Desktop the sidebar account button at the bottom remains the primary account entry. Its `…` popover may deep-link directly to Settings or a specific account tab.

## Purpose

Settings answers one question: **How should Jularr behave for this profile/account?**

Server-wide configuration, storage, acquisition, global providers, downloader configuration and user administration belong to Admin.

## Settings landing

The Settings tab is a compact searchable list of setting areas, not one giant form.

Recommended Desktop structure:

1. Settings search
2. grouped setting rows
3. account actions at the bottom
4. Jularr version/build information

Each row contains:

- small icon
- title
- one short summary
- current high-level value only when useful
- chevron/open affordance

Do not use dashboard-stat cards for normal settings.

## Setting areas

### General

#### Appearance
- System / Light / Dark
- accent/theme tokens
- density where supported
- reduced motion / animation preference where supported

#### Language & Region
- UI language
- preferred metadata/title language
- locale
- timezone
- date/time format
- number format

#### Accessibility
- text scaling where supported
- reduced motion
- contrast/accessibility preferences
- subtitle accessibility defaults where appropriate

### Media

#### Library & Display
- library layout preferences
- card/list density
- title display preference
- combine/separate related media-type presentation where supported
- default sorting/filter behavior where it is genuinely persistent

#### Playback
- autoplay
- next-episode behavior
- intro/outro skip preference when supported
- default quality constraints
- resume behavior
- playback-speed default

#### Audio & Subtitles
- preferred audio languages
- preferred subtitle languages
- subtitle mode/defaults
- forced/SDH preferences
- subtitle appearance where client permits

#### Reader
- typography defaults
- theme/background
- text size/spacing
- page/scroll mode
- Manga direction and fit behavior
- TTS defaults where supported

#### Ratings
- choose the profile's rating input/display system
- Three-level thumbs: Down / Up / Double Up
- 5 stars
- 0–10 integer
- 0.0–10.0 decimal
- 0–100

The setting changes presentation/input only. Canonical `UserRating` values remain normalized and are never migrated when the user changes scale.

### Personal features

#### Learning
- enable/disable personal Learning experience where instance policy allows
- learning language/preferences
- review/session defaults
- media-derived learning preferences

#### Notifications
- release/activity notification preferences
- channels available to this profile
- quiet hours where the notification system supports them
- notification categories

#### AI & Personal Providers
- personal AI provider/model configuration where instance policy permits
- personal provider credentials remain write-only/masked
- model/task preference
- test connection

Server/shared AI policy stays in Admin.

#### Connections
- connected external media accounts/providers
- progress/rating sync preferences where supported
- friend/social connections to external platforms where supported
- disconnect/reconnect actions

External provider identity never replaces canonical Jularr profile/media identity.

### Account

#### Devices & Sessions
- own devices only
- current device
- active personal sessions
- rename device where supported
- sign out/revoke device
- offline/download device state where supported

#### Profile & Privacy
- profile visibility where social features exist
- friend/discovery preferences
- activity visibility
- review/rating visibility
- history/privacy controls

#### Account & Security
- account email/identifier
- password/authentication actions
- active sign-ins
- security/recovery options where supported
- delete/deactivate account only with explicit confirmation and correct ownership policy

## Rating preference contract

`RatingDisplayPreference` is profile-scoped.

It never changes database schema and never rewrites existing ratings.

All shared rating controls must read this preference so the same representation appears consistently in:

- media detail pages
- Profile > Ratings
- rating dialogs/popovers
- search/library surfaces where ratings are shown

If an external service uses a different score format, provider adapters convert to/from canonical `UserRating`.

## Settings search

Search must find both setting-page titles and individual setting names.

Examples:

- searching `rating` opens/highlights Ratings
- searching `subtitle` can find Audio & Subtitles
- searching `timezone` can find Language & Region

Search results deep-link to the correct setting group rather than duplicating the setting in a second UI.

## Desktop

Settings tab landing remains inside the full Profile/Account page.

Recommended layout:

- shared profile hero + mini stats + heatmap
- shared account tabs
- below tabs: Settings search
- single wide or two-column grouped list depending on available width
- no permanent secondary left settings rail unless the number of settings later makes it clearly superior

Opening a setting area may use a focused subpage within the same Account shell.

For deep setting pages, the large hero may collapse to a compact account header after navigation/scroll so the actual settings are not pushed too far down. This collapse must not create a second navigation model.

## Mobile

Profile remains the bottom-nav destination.

Settings tab shows:

- shared compact profile header
- account tabs or a compact tab selector
- Settings search
- stacked setting rows
- Logout
- version/build text

Opening a setting area uses a dedicated full-width mobile subpage with contextual Back.

Do not squeeze desktop two-column forms onto Mobile.

## Tablet

Portrait follows Mobile list/subpage behavior.

Landscape may use list + selected setting pane where it improves efficiency.

## TV

Only settings meaningful on TV are exposed:

- appearance where applicable
- playback
- audio/subtitles
- basic profile switching/account

Do not expose security forms, provider secrets, broad device administration or complex AI configuration on TV.

## Save behavior

Prefer immediate save for simple reversible preferences.

Use explicit Save only when:

- several fields form one atomic configuration
- validation/testing is required
- credentials/provider setup is being edited

Never show a permanent unsaved state for simple toggles if immediate persistence is safe.

Provide local validation messages next to the affected setting.

## Policy-disabled settings

Instance policy may make some personal features unavailable.

When a setting is unavailable because the instance disables the capability:

- omit it when the user cannot use it at all;
- otherwise show a concise policy explanation only when the user needs to understand why it cannot be changed.

Do not expose Admin policy internals.

## Account actions footer

At the bottom of Settings:

- `Logout` as a clear but non-dominant destructive/account action
- optional `Switch profile` where applicable
- `Jularr vX.Y.Z`
- build identifier only when useful for support

Version information is informational and visually quiet.

## Light / Dark

Both themes are first-class.

Appearance changes should preview/apply safely without making the Settings page unreadable mid-change.

## Loading / Empty / Error

- render the shared account shell independently from settings data
- skeleton only the affected setting list/group
- provider-test state stays local to that provider
- one failing optional integration must not break the whole Settings tab
- failed save shows local retry/error

## Privacy / security

- secrets are masked/write-only
- security-sensitive changes require appropriate re-authentication where supported
- own devices/sessions only
- no other users' account data
- permissions enforced server-side

## Navigation / back

Deep setting page Back returns to Settings with:

- search/filter state preserved
- scroll position preserved where practical
- shared Profile/Account tab state preserved

Direct deep links to a setting page are allowed.

## Must not implement

- no separate disconnected Settings shell
- no second profile hero inside Settings
- no one-page form wall
- no Admin/server settings
- no indexer/downloader/storage/root-folder configuration
- no global provider/API credentials
- no arbitrary raw YAML/JSON configuration
- no duplicate ownership of the same preference across multiple pages
- no separate rating persistence per selected display system
- no hard-coded `IsAdmin` UI branching where capability/policy should be used
- no giant tile dashboard for settings

## Mockup deliverables

First review:

1. Desktop Light Settings tab inside the approved Profile hero/account shell.
2. Mobile Light Settings tab/list.
3. One focused subpage example, preferably Playback or Appearance.
4. Rating-system selector state.

Later:

- Dark derivation
- provider test/error state
- Account & Security confirmation state

Text specification wins over mockup imagery on conflict.