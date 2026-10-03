# User Settings — Clean Design

Status: **binding planning specification; no dedicated mockup required before implementation**.

User Settings is the **Settings tab** inside the shared Profile / Account page defined by `docs/mockups/profile-activity/SPEC.md`.

The implementation should use the normal Jularr form/list components. A separate visual mockup is only needed later if the implemented result is unsatisfactory or a page introduces a genuinely new interaction pattern.

## 1. Shared Account shell

The Settings landing page keeps the same shared profile chrome as the other account tabs:

- profile hero/banner
- avatar + nickname
- Edit Profile
- compact lifetime mini stats
- activity heatmap
- tabs: `Activity · Stats · Ratings · Friends · Settings`

Settings must not create a second account shell.

Desktop entry:
- bottom sidebar account control: avatar/initials + nickname + `…`
- clicking avatar/name opens the Profile/Account page
- `…` opens a small account popover with direct links to Activity, Stats, Ratings, Friends, Settings and Logout

Mobile entry:
- Profile remains the account destination in bottom navigation
- Settings is a tab/section inside Profile

## 2. Settings landing page

The Settings tab itself is a searchable list of setting pages.

Order:

1. search
2. General
3. Media
4. Personal features
5. Account
6. Logout
7. Jularr version/build

Do not show every individual setting on the landing page.

### Setting-page row

Every row uses the same component:

- small leading icon
- page title
- one-line description
- optional compact current-state summary
- chevron

Example:

```text
Playback
Autoplay, skip behavior, quality and speed
                                      >
```

No large dashboard tiles.

### Groups

#### General
- Appearance
- Language & Region
- Accessibility

#### Media
- Library & Display
- Playback
- Audio & Subtitles
- Reader
- Ratings

#### Personal features
- Learning
- Notifications
- AI & Personal Providers
- Connections

#### Account
- Devices & Sessions
- Profile & Privacy
- Account & Security

## 3. Standard setting-page layout

Every setting page follows one layout contract.

### Header
- contextual Back
- page title
- optional one-line description only when needed
- no duplicate page title lower down

### Content
- one or more `SettingGroup` sections
- groups separated by spacing and optional short heading
- no nested card-inside-card layouts
- controls aligned consistently

### Setting row
A normal row contains:

- label
- optional concise description
- control on the right on Desktop
- control below/right on Mobile when width requires it

Supported shared row types:

- Toggle
- Segmented choice
- Single select
- Multi-select / ordered language list
- Slider + numeric value
- Text field
- Secret field
- Action row
- Destructive action row
- Read-only information row

Do not invent page-specific control styles for equivalent settings.

### Save behavior
Use immediate persistence for simple reversible preferences.

Use explicit `Save` only for:
- credential/provider forms
- several fields that must change atomically
- security-sensitive changes
- forms requiring validation/test before commit

Show save errors locally.

### Mobile
- one column
- controls can move below labels
- selects may use bottom sheets
- no desktop settings table squeezed onto phone

### Deep navigation
A setting page is one level below Settings.

Do not create:
`Settings → Playback → Advanced → Codec → More`.

If a complex item needs editing, use a dialog/sheet or one focused editor reached from the setting page.

## 4. Appearance

Purpose: visual application appearance only.

### Visual style
- Clean
- Original Jularr

Clean is the neutral/minimal skin and uses no decorative Japanese/anime application backgrounds.
Original Jularr is the Japanese ink/watercolor/cherry-blossom skin over the same components/layout.

Changing visual style must not change navigation, feature availability or page structure.

### Brightness
- System
- Light
- Dark

Use segmented choice or radio group.

### Accent
- Jularr default for the selected visual style
- supported accent presets
- custom accent only if the shared token system supports it safely

Clean defaults to purple. Original Jularr defaults to the established red/pink treatment.

Accent changes use semantic design tokens and may hue-shift permitted branded/decorative elements coherently. Do not recolor third-party provider logos or semantic success/warning/error colors merely to match the accent.

Show a small live preview, not a separate preview page.

### Interface density
- Comfortable
- Compact

Only expose this if shared components actually support both densities.

### Visual effects
- background/transparency effects where supported
- reduce visual effects only if separate from accessibility motion settings

Do not duplicate `Reduced motion`; that belongs to Accessibility.

## 5. Language & Region

### Interface
- UI language
- locale/region

### Media metadata
- preferred title/metadata language
- optional fallback language
- title presentation preference where globally applicable:
  - Preferred
  - Original
  - Romanized

Do not create provider-specific language settings here.

### Time & numbers
- timezone
- 12/24-hour clock or locale default
- date format or locale default
- number format or locale default

Timezone changes affect Calendar/activity display only; stored canonical timestamps remain unchanged.

## 6. Accessibility

### Motion
- Reduce motion

### Text / interface
- text scaling if supported independently from OS/browser scaling
- larger interface targets where supported

### Contrast
- high-contrast preference where supported
- stronger focus indicators where needed

### Media accessibility defaults
Only preferences that apply across clients:
- prefer SDH/CC subtitles
- prefer audio description where available

Detailed subtitle styling remains under Audio & Subtitles.

Do not duplicate OS accessibility controls that Jularr cannot meaningfully override.

## 7. Library & Display

### Library presentation
- default view: Grid / List
- card size/density where supported
- remember last selected view/filter

### Titles
- use the shared title preference from Language & Region
- per-page override only where explicitly supported; no duplicate global setting

### Grouping
- combine/separate related reading types where product behavior supports it
- presentation-group display preferences where useful

### Progress display
- show/hide progress on library cards
- show/hide completed items only if this is a genuine persistent preference

Do not store a separate preference for every individual Library filter unless there is a real use case.

## 8. Playback

This page contains consumer playback behavior, not transcoder/server settings.

### Continue / autoplay
- Resume from last position
- Autoplay next episode
- autoplay countdown duration if autoplay is enabled

### Skip behavior
When media segments exist:
- Intro: Never / Show button / Auto-skip
- Recap: Never / Show button / Auto-skip
- Outro/Credits: Never / Show button / Auto-skip

Do not invent segment timestamps here. Settings only choose behavior for known segments.

### Playback defaults
- default playback speed
- default quality: Auto or user-visible quality cap where supported
- prefer original quality only as a user-facing choice, never expose Direct Play/Remux/Transcode rules

### Completion / resume
If configurable:
- resume behavior
- completion threshold

Use safe bounded presets rather than arbitrary technical values unless a real product need exists.

### Platform notes
Device-specific behavior may override unsupported settings, but the effective result should be understandable.

## 9. Audio & Subtitles

### Audio
- ordered preferred audio languages
- prefer original language when available
- prefer audio description when available

### Subtitle behavior
- Off
- Automatic
- Always
- Forced only

### Subtitle languages
- ordered preferred subtitle languages
- fallback language
- prefer SDH/CC
- forced subtitle preference

### Subtitle appearance
Where the client supports custom subtitles:
- text size
- font family from supported set
- text/background opacity
- edge/shadow style
- vertical position when safe

Use a small sample preview in the same page.

Do not expose codec/container/track-index internals.

## 10. Reader

### Reading mode
- Paged
- Continuous scroll

### Text
For text-capable books/novels:
- font family
- font size
- line height
- paragraph spacing
- page/column width

### Reader theme
- System
- Light
- Sepia
- Dark

This is a content-reading theme and may differ from application Appearance.

### Manga/comic behavior
- reading direction: Auto / LTR / RTL
- fit: Width / Height / Page
- single page / spread where supported

### Interaction
- tap-zone behavior where supported
- remember reader controls state only when useful

### TTS
Where available:
- voice
- speed
- pitch only if supported cleanly

Do not create separate Manga Settings and Book Settings pages when shared Reader settings suffice.

## 11. Ratings

Purpose: choose how the profile enters and sees ratings.

### Rating system
Exactly one active display/input system:

- Three-level thumbs: Down / Up / Double Up
- 5 stars
- 0–10 integer
- 0.0–10.0 decimal
- 0–100

Show a live preview of the selected shared `RatingControl`.

### Behavior
- changing the system does not rewrite canonical `UserRating`
- existing ratings are converted only for display
- no rating remains distinct from minimum rating
- all rating surfaces use this same preference

Optional:
- show ratings on Library/Discover cards, only if that display option is later useful

Do not add per-media rating systems.

## 12. Learning

Only visible when the instance enables Learning and the profile may use it.

### Personal Learning
- enable/show Learning for this profile where personal opt-out is supported
- target/learning languages
- preferred explanation language

### Reviews
- default review/session size where supported
- review reminders if Notifications supports them

### Media learning
- enable media-derived learning interactions
- subtitle/reader learning interaction preferences

No XP/gamification configuration unless that product direction is explicitly added later.

## 13. Notifications

This page configures delivery preferences, not release/acquisition logic.

### Channels
Only show configured/available channels:
- In-app
- Push
- Email
- other supported adapters

### Categories
Examples:
- releases/calendar
- requested media status
- download/import completion where user-visible
- friend/social activity
- Learning reminders
- account/security

### Quiet hours
- enabled
- start
- end
- timezone follows Language & Region unless explicitly overridden

### Delivery mode
Where supported:
- Immediate
- Digest

Instance-disabled channels do not appear as broken toggles.

## 14. AI & Personal Providers

Only visible when personal AI/providers are allowed by instance policy.

### Provider list
Each configured personal provider row shows:
- provider name
- connected/configured state
- selected default model where applicable
- Test
- Edit
- Remove

### Add/Edit provider
Focused form:
- provider type
- endpoint only where applicable
- API key/secret as write-only
- model discovery/selection
- Test connection
- Save

### Task preferences
Where supported:
- translation model
- explanation/learning model
- other personal task defaults

Server/shared AI configuration remains Admin-only.

Never reveal stored secret values after save.

## 15. Connections

External **Profile Connections** for sync/import/write-back.

Examples may include Plex, Jellyfin, Trakt, AniList, MAL or future compatible services; only actually implemented connectors appear.

A Connection is not automatically an Account login identity. The same provider may expose Login capability, Connection capability, both or neither. Signing in through a provider does not silently enable profile sync.

### Connection row
- service
- connected account identity
- status
- Connect / Reconnect / Disconnect

### Sync options per connection
Only supported capabilities appear:
- progress sync
- rating sync
- list/watchlist sync
- friend/social import/linking

For bidirectional sync, define conflict policy explicitly rather than silently overwriting newer Jularr state.

External IDs never replace canonical Work/Profile identity.

## 16. Devices & Sessions

Own devices and own sessions only.

### Current device
Show first:
- friendly name
- client/platform
- last active
- current indicator
- Rename where supported

### Active sessions
- media title/context
- progress
- device
- last active
- Resume/Open
- terminate own session where supported

No IP/codec/transcode diagnostics.

### Other devices
- friendly name
- platform/client
- last active
- offline-download state where supported
- Rename
- Sign out/Revoke

Optional inactive devices can be collapsed.

This page owns device/session management; Activity tab does not duplicate Current Session cards.

## 17. Profile & Privacy

### Profile
- nickname/display name
- avatar
- banner
- short bio/about where supported

Editing avatar/banner may open a media picker/crop dialog.

### Visibility
Only when Friends/social features exist:
- profile visibility
- activity visibility
- Ratings visibility
- Stats visibility
- friend/discovery visibility

Use understandable choices such as:
- Private
- Friends
- Instance users / Public only if the product actually supports those scopes

### History/privacy
- clear Activity history
- clear search history where stored
- reset recommendations/profile signals only if such a feature exists

Clearing history must not automatically reset MediaProgress.

Destructive actions require confirmation with explicit scope.

## 18. Account & Security

Authentication/account controls only.

### Account
- account email/identifier
- account ownership/profile relation where useful

### Authentication
Depending on supported auth:
- change password
- passkeys/security keys
- two-factor authentication
- recovery options
- linked Login identities/providers

Linked Login identities belong to the Account. Removing one must not strand the Account without a valid authentication/recovery path.

Profile PIN management is separate from Account authentication and only protects Profile activation/switching.

### Sign-in security
- recent sign-ins/security events where available
- link to Devices & Sessions rather than duplicating the full device list

### Account actions
- sign out all devices
- deactivate/delete account only when the account model supports it

Destructive account actions:
- separate danger section
- explicit confirmation
- re-authentication where appropriate
- explain whether profile data, ratings, history and progress are deleted

## 19. Settings search

Search indexes:
- setting page names
- group names
- individual setting labels
- useful synonyms

Examples:
- `rating` → Ratings
- `subtitle` → Audio & Subtitles
- `timezone` → Language & Region
- `autoplay` → Playback
- `AniList` → Connections when available

Selecting a result opens the owning setting page and focuses/highlights the row.

Search never creates a second editable copy of the setting.

## 20. Account footer

At the end of Settings landing:

- `Logout`
- optional `Switch profile`
- `Jularr vX.Y.Z`
- build identifier only when useful for support

Logout is also available from the Desktop account popover.

## 21. Policy behavior

Instance policy always wins.

If a whole capability is unavailable:
- hide its setting page.

If a user can see a feature but cannot change one policy-controlled value:
- show the effective value with a short explanation.

Do not expose raw policy names, authorization internals or Admin configuration.

## 22. Loading / errors

Settings shell and page navigation should load independently.

- skeleton only the setting page/list that is loading
- local save failure stays on the affected row/form
- provider Test has Running / Success / Failure locally
- one failed optional integration does not break all Settings
- invalid data never silently resets to defaults

## 23. Navigation / Back

- Settings landing preserves search and scroll state
- setting-page Back returns to Settings landing
- direct deep links to each setting page are valid
- Mobile uses normal contextual Back
- browser/system Back matches visible Back behavior

## 24. Light / Dark

Every standard setting component must work in Light and Dark through shared tokens.

Appearance changes can apply immediately.

No setting page requires its own bespoke theme styling.

## 25. Mockup policy

**No dedicated Settings mockup is required before implementation.**

The standard component contract in this SPEC is sufficient for the first implementation because Settings is intentionally a conventional list/form surface.

Create a mockup later only when:
- implementation review finds the hierarchy unsatisfactory;
- a new non-standard interaction is introduced;
- Mobile/Tablet behavior cannot be resolved from shared components;
- the owner requests a visual redesign.

This exception does not waive final UX review before merging a materially different Settings implementation.

## 26. Must not implement

- no disconnected Settings shell
- no second profile/account navigation system
- no giant one-page form
- no per-setting sub-subpage maze
- no Admin/server settings
- no indexer/downloader/storage/root configuration
- no global provider credentials
- no raw YAML/JSON editor
- no duplicated preference ownership
- no per-media rating persistence
- no server/transcode internals in Playback
- no other users' Devices/Sessions
- no provider secret readback
- no giant dashboard tiles
- no bespoke control design for each page

Text specification wins over implementation interpretation if a conflict appears.
