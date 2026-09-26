# Anime naming and renaming

AniLingo names anime folders and files with Sonarr-compatible templates. Naming is plain
configuration: presets are only starting points, and any profile can be created or changed on
`/Settings/Naming` without code changes. Renaming existing files is a separate, explicit action
per anime on `/Library/Rename/{animeId}` (linked as **Rename files** from the anime page).

Code: `Features/Acquisition/Naming` (`AnimeNamingFormatter` renders templates,
`AnimeNamingProfileStore` persists profiles, `AnimeRenameService` plans and executes renames).

## Profiles and selection

A profile holds the Series Folder, Season Folder, Specials Folder, Standard Episode, Daily Episode
and Anime Episode templates, whether season folders are used, the multi-episode style, illegal
character replacement and the colon replacement.

Profiles and assignments are stored in one canonical file, `/data/acquisition/naming-profiles.json`
(like the other acquisition stores). Without that file the built-in default state applies. The
effective profile for an anime is resolved in this order:

1. the anime's own assignment (chosen on its rename page),
2. the profile assigned to the library root that holds the file (`/Settings/Naming`),
3. the default profile.

The anime assignment also stores the **series type** (Anime, Standard, Daily), which selects the
episode template exactly like Sonarr: Anime uses the anime template when every episode of the file
has an absolute number, Daily uses the daily template when an air date is known, and everything
else uses the standard template. Profiles that are the default or assigned anywhere cannot be deleted.

AniList, MAL, TVDB, TMDB and IMDb IDs are metadata/mapping facts. They can appear in names through
tokens, but they never decide the folder structure: one series folder may span several AniList
entries (seasons, cours, parts).

### Presets

| Preset | Purpose |
| --- | --- |
| `sonarr-mediainfo` — Sonarr with media info (default) | Reproduces the current Sonarr-style examples: `The Series Title's! - S01E01 - Episode Title (1) WEBDL-1080p Proper AVC DTS[DE] [EN+DE] RlsGrp tt12345` and `... - S01E01-E03 - Episode Title ...` |
| `sonarr-default` — Sonarr default | Sonarr v4 defaults for series, anime and daily templates: `{Series Title}` folders, `Season {season}`, `Specials`, `{Series Title} - S{season:00}E{episode:00} - {Episode Title} {Quality Full}`, Prefixed Range, Smart Replace |
| `anime-detailed` — Anime detailed | Adds year, absolute number, release details and group |

**New from preset** copies a preset into a new, freely editable profile.

## Template syntax

Tokens are written in braces. Everything outside braces is literal text.

- **Word separator**: the separator inside the token name is used between words of the value:
  `{Series.Title}` gives `The.Series.Title's!`, `{Series_Title}` gives `The_Series_Title's!`.
- **Case**: an all-lowercase token name lowercases the value (`{series title}`), an all-uppercase
  name uppercases it (`{SERIES TITLE}`); mixed case keeps the value as is.
- **Optional prefix/suffix**: characters from ` -._[(` directly after `{` and ` -._)]` directly
  before `}` are emitted only when the value is not empty: `{[Release Group]}`, `{-Release Group}`.
- **Padding**: `{season:00}`, `{episode:00}`, `{absolute:000}` zero-pad numbers. Other tokens do not
  accept a format.
- After rendering, repeated separators (`--`, `..`, `  `) collapse to one, trailing separators are
  removed, leading spaces/dots are trimmed and Windows device names (`CON`, `NUL`, `COM1` ...) get a
  trailing `_`. Names longer than 255 UTF-8 bytes are refused by the rename planner.

Saving validates every template: unknown tokens, tokens that are not available in that template
(for example episode tokens in the series folder), malformed braces, illegal literal characters, a
missing `{season}`/`{episode}` in the standard template, a missing `{season}`+`{episode}` or
`{absolute}` in the anime template, and a missing `{Air-Date}` or `{season}`+`{episode}` in the
daily template are rejected. The preview on `/Settings/Naming` renders Sonarr's sample (series
"The Series Title's!" (2010), episodes "Episode Title (1)".."(3)") for every template before saving.

## Tokens

| Group | Tokens |
| --- | --- |
| Series | `{Series Title}` `{Series CleanTitle}` `{Series TitleYear}` `{Series CleanTitleYear}` `{Series TitleWithoutYear}` `{Series CleanTitleWithoutYear}` `{Series TitleThe}` (sort title, `Series Title, The`) `{Series CleanTitleThe}` `{Series TitleFirstCharacter}` `{Series Year}` |
| Provider IDs | `{AniListId}` `{MalId}` `{TvdbId}` `{TmdbId}` `{ImdbId}` — from AniList metadata and `tvshow.nfo`; empty when unknown |
| Numbering | `{season}` `{episode}` `{absolute}` `{Air-Date}` (`2013-10-30`) `{Air Date}` (`2013 10 30`) |
| Episode | `{Episode Title}` `{Episode CleanTitle}` |
| Quality | `{Quality Full}` (`WEBDL-1080p Proper`) `{Quality Title}` `{Quality Proper}` `{Quality Key}` (the quality key of the shared quality model) |
| Release | `{Release Group}` `{MediaInfo VideoCodec}` `{MediaInfo VideoBitDepth}` `{MediaInfo VideoDynamicRangeType}` `{MediaInfo AudioCodec}` `{MediaInfo AudioChannels}` `{MediaInfo AudioLanguages}` `{MediaInfo AudioLanguagesAll}` `{MediaInfo SubtitleLanguages}` `{MediaInfo Simple}` `{MediaInfo Full}` |

Release tokens always come from the shared `AnimeReleaseParser` result for the current file name
(the same parser and quality model acquisition uses); the naming module has no parser of its own.

- `{Quality Proper}` is the release version (`v2`) for anime series, otherwise `Repack` or `Proper`.
- `{MediaInfo AudioLanguages}` omits the value when the only audio language is English (Sonarr
  behaviour); `{MediaInfo AudioLanguagesAll}` always includes it. Languages render as `[JA+EN]`.
- `{MediaInfo VideoCodec}` keeps the release spelling `x264`/`h264`/`x265`/`h265` and otherwise
  renders `AVC`, `HEVC` or `AV1`.
- A multi-episode `{Episode Title}` joins distinct titles with ` + `, or uses the shared title when
  the titles only differ by a part number (`Title (1)`, `Title (2)` → `Title`).
- `{absolute}` is filled from local numbering: season N episode E is (episodes of seasons 1..N-1) + E
  when every earlier season is present without gaps. Otherwise it is empty and an anime file falls
  back to the standard template.

## Multi-episode styles

For a file containing episodes 1-3 of season 1 with `S{season:00}E{episode:00}`:

| Style | Result | `{absolute:000}` |
| --- | --- | --- |
| Extend | `S01E01-02-03` | `001-002-003` |
| Duplicate | `S01E01 - S01E02 - S01E03` (repeats the pair with the separator written before it) | `001-002-003` |
| Repeat | `S01E01E02E03` | `001-002-003` |
| Scene | `S01E01-E02-E03` | `001-002-003` |
| Range | `S01E01-03` | `001-003` |
| Prefixed Range | `S01E01-E03` | `001-003` |

## Illegal characters

With **Replace illegal characters** on, token values are cleaned per token like Sonarr:
`\` and `/` become `+`, `?` becomes `!`, `*` becomes `-`, and `<`, `>`, `|`, `"` are removed. With
it off, all of these characters are removed. Control characters are always removed, and values are
trimmed of leading spaces/dots and trailing spaces. Literal template text may not contain illegal
characters at all.

## Colon replacement

Colons in token values are handled by the profile's colon strategy (only when illegal character
replacement is on; otherwise every colon is removed):

| Strategy | Rule | `Re:Zero: Starting Life` | `10:30` |
| --- | --- | --- | --- |
| Delete | remove `:` | `ReZero Starting Life` | `1030` |
| Dash | `:` → `-` | `Re-Zero- Starting Life` | `10-30` |
| Space Dash | `:` → ` -` | `Re -Zero - Starting Life` | `10 -30` |
| Space Dash Space | `:` → ` - ` | `Re - Zero -  Starting Life` (the double space collapses in the rendered name) | `10 - 30` |
| Smart Replace | every `": "` (colon followed by a space) → `" - "`, then every remaining `:` → `-` | `Re-Zero - Starting Life` | `10-30` |
| Custom | `:` → the configured text (no illegal characters allowed) | e.g. `~`: `Re~Zero~ Starting Life` | `10~30` |

Smart Replace is Sonarr's rule: a colon that separates a title from a subtitle becomes a spaced
dash, a colon inside a word or number becomes a plain dash.

## Renaming existing files

Saving a profile never renames anything. On an anime's rename page the owner chooses the profile and
series type for that anime, reviews the plan and confirms it. Renaming works in three steps:

1. **Plan** — built from the current library records, filesystem and ownership state. Each media
   file gets a target path (series folder, season/specials folder, file name) plus its sidecars
   (subtitles/NFO named `<media name>.*` next to it or in `Subs`/`Subtitles`).
2. **Preview** — the page shows every file as Unchanged, Rename or Blocked with the reason. Nothing
   on disk changes.
3. **Execute** — the plan is rebuilt from current state and must match the previewed plan exactly
   (fingerprint); otherwise execution is refused and the updated preview is shown.

Optionally the series folder is renamed too. Because that moves every file, it only runs when no file
of the anime is blocked. If the new folder name changes the library's anime key, the key changes
together with the files, and the Sonarr ownership assignment, owned paths, jobs, SABnzbd
acquisitions/blocklist entries, monitoring settings/wanted episodes and import records follow it.

### Safety rules

A file (or the whole plan) is blocked when:

- **Sonarr owns it** — every move (source and target, including a series-folder move) goes through
  `SonarrParallelSafety.CanRename` with a snapshot from `SonarrObservationService.GetSnapshotAsync`
  (refreshed before execution). Anime in read-only coexistence, paths Sonarr tracks, series Sonarr
  still monitors, files Sonarr imported/renamed within 24 hours, and anime whose Sonarr state
  cannot be verified are refused. See [SONARR_MIGRATION.md](SONARR_MIGRATION.md).
- **The library is read-only** — each affected directory is checked (read-only mount in
  `/proc/self/mounts`, then a write probe); a read-only root refuses the whole plan before any move.
- **The target exists** — a different file or folder already has the target name, or two files of
  the plan would get the same name. On case-insensitive filesystems (SMB, NTFS, APFS) a case-only
  rename is not a conflict; it is executed through a temporary name.
- **The move would cross filesystems** — AniLingo renames only within one filesystem, so a move can
  never degrade into a partial copy.
- **Identity would change** — the new path must scan back to the same season/episode and anime key
  (for example a template without `{season}`/`{episode}` in a later season would be scanned as a
  different episode).
- **The name is invalid** — empty, `.`/`..`, or longer than 255 bytes.
- **Library work is running** — a library scan, another rename or an acquisition import is active (imports likewise wait for renames; see [ANIME_ACQUISITION.md](ANIME_ACQUISITION.md)).

### Execution, rollback and records

Execution runs as one `anime-rename` operation (category Library) whose log lists every move. Moves
are journaled; if any move fails, every completed move is undone in reverse order, created folders
are removed and the operation fails with the exact paths of anything that could not be undone.

After all moves succeed, media file paths, subtitle track sources (sidecars and embedded-track
sources) and the anime key are updated in one database transaction; the ownership and SABnzbd
acquisition stores are updated with it and reverted if the commit fails. If the record update
fails, the files are moved back as well. Media file, episode and anime IDs stay the same, so
watch progress, learning data and the media inventory (no new ffprobe run) are preserved and a
later scan finds the files where the records point. Season/sidecar folders left empty are removed;
series folders are never deleted.
