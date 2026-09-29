# Admin Media Detail — V1

Status: planning baseline for the first Admin Media Detail implementation.

Global UX rules: `docs/UX.md`

The V1 goal is deliberately simple: one clear hierarchy, direct monitoring/search actions at the level the admin is looking at, and expandable local-file details. Do not turn this screen into a collection of unrelated Sonarr-style tabs.

## 1. Core hierarchy

The UI follows the media structure rather than splitting the same information across many pages.

For Anime / Series:

```text
Medium
  Season
    Episode
      File / Version
      File / Version
```

For Movie / Book / Light Novel / Manga / Audiobook in V1, monitoring/search defaults to the whole medium. Media-specific finer-grained monitoring can be added later only when it provides real value.

The underlying data remains canonical and provider-independent. External providers may change how episodes are grouped or numbered for display, but they never create a second stored episode universe.

## 2. Header

The header identifies the medium and contains the medium-level operational controls.

Show:
- artwork/poster where useful
- title
- concise type/year/status metadata
- monitoring state
- active acquisition state when relevant
- current default acquisition profile
- desired/default language policy in compact form

Primary monitoring control:
- Anime/Series: monitor/unmonitor the whole series
- Movie/Book/LN/Manga/Audiobook: monitor/unmonitor the medium

Monitoring in the header is a real operational control, not a passive badge.

## 3. Anime / Series display mode

Anime may be displayed in different provider-derived groupings.

V1 selector:

`View: Standard | AniList`

### Standard
Classic series grouping such as:
- Season 1
- Season 2
- Specials

This is the normal season/episode view using canonical series semantics and provider mappings.

### AniList
Display/group episodes according to AniList anime entries/relations where useful for Anime.

Important rules:
- this is display/grouping only;
- switching view never moves files;
- switching view never creates duplicate episodes;
- monitoring remains attached to the same canonical episode;
- acquisition/import/progress state remains unchanged;
- only grouping, numbering and provider-facing labels may change.

Provider structures are mappings/views over the canonical structure, not persistence models.

V1 does not require additional display modes such as Absolute numbering. They may be added later.

## 4. Monitoring hierarchy

### Medium level
The whole work can be monitored or unmonitored.

### Season level — Anime/Series only
Each season is independently monitorable.

Season monitoring states:
- On
- Off
- Partial

`Partial` means only some child episodes are monitored.

### Episode level — Anime/Series only
Each episode has a one-click monitor toggle.

### Inheritance
Season/episode acquisition settings normally inherit from the parent medium.

The UI must make inheritance understandable instead of copying independent settings everywhere.

Examples:
- `Inherited: Anime 1080p`
- `Inherited languages: DE + JA`
- explicit override only when the admin deliberately changes it

An override is stored only when required. Removing an override returns the unit to inherited settings.

## 5. Season rows

Anime/Series seasons are expandable.

Collapsed season row should show only useful merged information:
- season name/number
- monitoring state: On / Off / Partial
- available episodes, e.g. `12/13`
- missing episodes
- compact desired/available language coverage where useful
- active search/download/import state
- effective profile
- actions

Season actions:
- monitor/unmonitor
- Automatic search
- Automatic…
- Manual search
- Delete where semantically valid

Searching from a season applies to the canonical child episodes represented by that season/view.

Default automatic season search targets monitored items that are missing or eligible for an upgrade. It must not blindly reacquire every episode.

## 6. Episode rows

Expanding a season shows episodes.

Each episode row should provide a merged operational summary:
- episode number
- title
- air/release date
- monitor toggle
- overall state
- effective quality
- available audio languages
- available subtitle languages
- number of local files/versions
- active acquisition/import state
- actions

Overall states may include:
- Available
- Missing
- Searching
- Downloading
- Importing
- Failed

Do not flood the row with many decorative tags. Language/quality information must remain compact and readable.

## 7. Expand episode → local files

An episode row can be expanded inline.

The expanded area shows each actual local file/version separately.

Per file show where available:
- filename
- size
- container
- quality/resolution
- video codec
- audio tracks/languages
- subtitle tracks/languages
- release group
- source/release
- path/location
- import/source information
- technical state if analysis failed

File actions may include:
- view technical details
- re-analyse
- rename/organize where supported
- re-match / fix assignment
- delete this file

Multiple files for one episode are first-class and must not be collapsed into one fake file.

## 8. Shared acquisition actions

The same acquisition grammar should be reused at Medium, Season and Episode level where the action makes sense.

### 1. Automatic search
One-click action.

Uses the effective inherited settings immediately:
- acquisition/quality profile
- desired languages
- monitoring rules

No configuration dialog.

### 2. Automatic…
Opens a small focused dialog before searching.

V1 options:
- profile
- desired language(s)
- scope when needed, e.g. missing only / upgrades allowed

Primary action: `Search`

These selections are temporary for this search by default.

If the UI allows persistence, it must be an explicit separate choice such as:
`Save as override`

Temporary search options must never silently rewrite the medium/season/episode defaults.

### 3. Manual search
Opens the full normalized release candidate view.

Manual search can show:
- source/indexer
- quality
- languages
- release group
- size
- score
- rejection reasons
- manual grab

The detailed Manual Search screen is specified separately.

## 9. Edit / incorrect assignment

`Edit` on a file/unit is not a raw database editor.

For an incorrectly assigned file, open a safe Re-match flow.

Possible target changes:
- another episode/chapter/unit in the same medium
- another Work
- another edition/version where applicable

The UI should explain the current assignment and the proposed new assignment before applying it.

Canonical IDs are not directly typed/edited as the normal UX.

## 10. Delete semantics

Delete must never be ambiguous.

Depending on context, distinguish explicitly between actions such as:
- Delete this local file
- Delete all local files for this episode
- Remove/unmonitor this season or medium
- Remove medium from Jularr

Do not use one generic trash icon for several destructive meanings.

All destructive filesystem/library actions require confirmation and must state what data/files will actually be removed.

## 11. Live operational state

Search/download/import state should update live where the application already supports live events.

Examples:
- `Searching`
- `Downloading 63%`
- `Importing`
- `Failed`

These states should replace or augment the normal availability state without requiring page reload.

Incoming live updates must not collapse an expanded season/episode or reset scroll position.

## 12. V1 media-type behavior

### Anime / Series
Full V1 hierarchy:
- Medium monitoring
- Season monitoring
- Episode monitoring
- expandable seasons
- merged episode summary
- expandable episode files
- acquisition actions on Medium / Season / Episode

Anime additionally supports the Standard/AniList display grouping.

### Movie
V1 uses medium-level monitoring/search.

Show the local file/version(s) directly beneath the medium when expanded or in the main content area.

Shared actions:
- monitoring
- Automatic search
- Automatic…
- Manual search
- Edit/Re-match
- explicit Delete

### Book / Light Novel
V1 uses medium-level monitoring/search.

Show local editions/files and language/version information without requiring chapter-level monitoring in V1.

### Manga
V1 uses medium-level monitoring/search.

Show local editions/files/volumes as content information, but do not require chapter-level monitoring for V1.

### Audiobook
V1 uses medium-level monitoring/search.

Show local audio version/files and language/narration information. Track-level monitoring is not required for V1.

These V1 limits prevent the first implementation from creating separate complex monitoring models for every media type.

## 13. Information density

The main screen should remain understandable without many separate tabs.

Prefer:
- hierarchy
- expandable rows
- compact merged state
- contextual dialogs/drawers for deeper details

Avoid:
- duplicate Files/Languages/Releases surfaces that repeat the same information
- badge/tag walls
- permanent technical metadata in the main row
- separate pages for simple per-file actions

Additional tabs should exist only when a workflow cannot be represented clearly in the hierarchy.

## 14. State requirements

The screen must account for:
- loading
- no local files yet
- fully available
- partially available
- missing
- searching
- downloading
- importing
- failed acquisition
- provider unavailable
- storage unavailable
- unauthorized action

## 15. V1 acceptance criteria

V1 is complete only when:

- the medium can be monitored/unmonitored from the header;
- Anime/Series seasons can be expanded and independently monitored;
- season monitoring can represent Partial;
- Anime/Series episodes can be monitored with one click;
- episode rows merge the useful availability/quality/language state;
- episodes can expand to real individual local files/versions;
- every acquisition level exposes consistent Automatic / Automatic… / Manual actions where applicable;
- Automatic uses inherited defaults immediately;
- Automatic… can temporarily choose profile/languages without silently persisting them;
- incorrect assignments use a safe Re-match flow;
- destructive actions clearly state their scope;
- Anime supports Standard and AniList display grouping;
- switching Anime grouping changes only the view/mapping, never canonical stored episode identity;
- searches/actions from provider-derived groups resolve back to canonical episodes;
- active search/download/import states can update live without resetting the page;
- Movie/Book/LN/Manga/Audiobook stay medium-level for monitoring in V1 instead of inventing unnecessary child-monitoring models.
