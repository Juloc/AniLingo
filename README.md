# AniLingo

AniLingo is in prerelease. The canonical version is the `<Version>` property in `src/AniLingo.Web/AniLingo.Web.csproj`; published builds are listed under [GitHub Releases](https://github.com/Juloc/AniLingo/releases), and the running build shows its exact version to the Owner in the app sidebar.

AniLingo is a Docker-first Japanese learning companion for an existing anime library. It scans media from a read-only NAS mount, imports nearby Japanese subtitles, builds episode vocabulary, and lets you mark terms as known or review them before watching.

## v0.1

The first vertical slice includes:

- NAS library roots and background scanning
- deterministic anime / season / episode discovery from paths and filenames
- external Japanese SRT, ASS, SSA and WebVTT subtitle import, including `Subs/` and `Subtitles/` folders
- automatic embedded Japanese text-subtitle extraction through ffmpeg
- optional Jimaku Japanese subtitle lookup when no local/embedded text source exists
- local Japanese audio transcription fallback through whisper.cpp when subtitle lookup is unavailable or has no usable match
- manual embedded subtitle-stream selection when language tags are missing or wrong
- real Japanese morphological analysis with base forms and readings
- local JMdict meanings with German-first / common-English fallback
- episode vocabulary frequency and preparation progress
- direct-play episode player with synced, clickable Japanese subtitles
- instant device-first playback with direct play or live fragmented-MP4 remux
- instant server H.264 compatibility streaming when the client cannot decode the source
- local Japanese audio transcription fallback through whisper.cpp when no Japanese text subtitles are available
- provider-neutral anime metadata with explicit AniList matching and cached artwork
- secure self-hosted AniList account connection via the official Auth PIN flow
- conservative AniList episode-progress sync with read-before-write, monotonic progress-only updates and local pre-write backups
- known / learning term state
- FSRS-6 spaced repetition with Again / Hard / Good / Easy interval previews
- responsive Razor Pages UI with a Jellyfin/Plex-style shell
- built-in local accounts with owner-only user management and per-user anime, novel and learning progress
- embedded SQLite persistence in the single application container
- optional Codex CLI connection for AI-assisted features
- Japanese Web/Light Novel library with Narou import, on-demand chapter caching, a responsive reader and persisted reading position
- optional one-time German AI translation cached against the exact Japanese source text
- independent AniList novel matching plus manual or optional AI-assisted novel-chapter → anime-episode mappings

AI enrichment and image-based subtitle OCR are later phases. See issue #1 for the staged roadmap.

## Docker stack

AniLingo runs as one container. There is no database sidecar and no required environment configuration.

The minimal stack is:

```yaml
services:
  anilingo:
    image: ghcr.io/juloc/anilingo:latest
    volumes:
      - anilingo-data:/data
    networks:
      - default
    ports:
      - "8097:8080"

volumes:
  anilingo-data:
```

This minimal stack starts without a NAS or anime mount:

```bash
docker compose up -d
```

Open `http://localhost:8097`. On the first visit AniLingo redirects to **Create owner account**. Additional local users can request an account and remain blocked until the owner approves them.

To scan an anime library, add a read-only media mount:

```yaml
services:
  anilingo:
    volumes:
      - anilingo-data:/data
      - /path/to/anime:/media/anime:ro
```

Then add `/media/anime` as a library root in AniLingo. Existing persisted library roots remain unchanged when upgrading.

### Library scans

Every root is reconciled once at startup, on **Queue library scan** under Admin → System, shortly after filesystem changes, and periodically. Where the mount raises filesystem events, AniLingo watches each root and reconciles only the changed anime folders about ten seconds after a burst of activity (for example a Sonarr import) has settled; lost events fall back to a full pass. Because network mounts often raise no events, each root also has a **Periodic reconciliation** interval (default every 30 minutes, `0` turns it off) that repairs anything missed. Periodic runs are skipped while the storage is offline.

Duplicate requests for a root are coalesced while a scan is queued or running. Admin → Scans shows every run with its state, root, trigger, phase, timing and counters (media, added, changed, removed, skipped, subtitles, artwork, ignored NFO files, media analysed or failed to analyse, errors, warnings); item-level warnings such as unmatched files are listed with root-relative paths in the run's log. A run left running by a restart is marked interrupted and can be run again from the same page.

### Sleeping / unavailable NAS and Wake-on-LAN

AniLingo treats temporary media-storage outages separately from deleted files. A sleeping or unavailable NAS does not remove the persisted Library, and an unexpectedly empty previously-populated root is rejected as unsafe reconciliation rather than interpreted as a mass deletion.

Under **Admin → System**, the owner can test each configured root without running a library scan. Wake-on-LAN can optionally be configured per root with a MAC address and, when needed from Docker networking, the LAN broadcast IPv4 address such as `192.168.178.255`. Wake actions are owner-only and rate-limited. Pressing Play as a normal user never sends a magic packet automatically.

Playback checks the owning storage before codec selection. If storage is temporarily unavailable, the player preserves playback intent/position and retries with bounded backoff for up to about one minute. It resumes automatically when storage returns; after the automatic window it offers **Try again**. An owner also gets an explicit **Wake NAS** action in the player when Wake-on-LAN is configured. A root that is online while one concrete file is absent is reported as a real missing-file condition instead of being retried as a NAS outage.

### Playback continuity

Each signed-in profile has its own playback state on the server; web/PWA, Android and Android TV use the same state through `/api/client/v1`. One canonical owner (`EpisodeProgressService`) holds every fact:

- **Resume position** — players send bounded checkpoints (web: at most one every 15 seconds while playing, plus pause, end, restart and page close). A first start below 30 seconds is treated as accidental and stores nothing. **Restart from beginning** clears the resume position.
- **Watched** — reaching 95% of the duration (or the end) marks an episode watched and clears its resume position. Watched is sticky: rewatching a watched episode updates the resume position but never flips it back to unwatched. **Mark watched** / **Mark unwatched** on the Episode and Anime pages are the only way to change it manually; both also clear the resume position.
- **Previous / next** — resolved from local season/episode numbers of episodes with a media file. AniLingo only moves to the directly adjacent number, crosses from the last local episode of season N to S(N+1)E01 (and back), never crosses into or out of specials (season 0), and shows no neighbor when numbering is duplicated or has a local gap.
- **Continue Watching** (Home) — at most one card per anime, anchored on that anime's most recently updated resumable or watched episode, newest first with the episode id as tie-break. An unfinished anchor resumes that episode; a watched anchor shows the next local episode as **Up next** unless it is already watched. Finished items disappear automatically.
- **End of episode** — the player shows **Replay**, **Next episode** and **Back to episodes**. **Autoplay next episode** is an optional per-profile preference (default off); when enabled a cancellable 10-second countdown starts the next episode.
- **Recent playback history** — the last 50 playback sessions of the own profile, shown collapsed on Home and clearable by the user. Clearing history keeps watched state and resume positions. History is never shown to the owner or other users.

AniList sync stays a separate, explicit integration and never becomes the local source of truth.

### Public URL / Caddy

For Internet access, terminate HTTPS at Caddy (or another trusted reverse proxy) and keep AniLingo itself on the private Docker network. Do not forward port 8097 directly from the router to the Internet. AniLingo accepts `X-Forwarded-For` and `X-Forwarded-Proto` from loopback and private RFC1918/ULA proxy networks, so HTTPS cookies are marked correctly when Caddy terminates TLS.

The authentication cookie is HttpOnly, SameSite=Lax and secure whenever the original request is HTTPS. Login attempts are rate-limited. Passwords are stored only as ASP.NET Core Identity password hashes in the existing SQLite database, while the existing Data Protection key ring under `/data/keys` keeps authentication cookies valid across normal container recreation.

When Caddy shares a Docker network with AniLingo, Caddy can proxy directly to `anilingo:8080`; publishing `8097:8080` is only needed when you also want direct host access.

Runtime paths are fixed and intentionally simple:

- `/data` stores the SQLite database, protected integration settings, Codex authentication state, the persistent Whisper model and generated transcription cache.
- `/media/anime` is the optional conventional read-only anime library mount; AniLingo also starts without it.

For an existing Docker stack, replace `default` with that stack's network if needed. No connection string, database password, media environment variable or second service is required.

## AniList metadata

AniLingo can match each locally discovered anime to AniList without requiring an AniList account. Open an anime in **Library**, search AniList, and explicitly choose the correct result. The local NAS grouping remains the source of truth for files and episodes; the AniList match only supplies cached display metadata such as titles, cover art, banner art, format, year and episode count.

Normal Home/Library browsing reads the cached SQLite metadata and makes no AniList request. AniList is queried only when you search, match or refresh metadata. The integration is behind `IAnimeMetadataProvider`, and the database stores `Provider` + `ExternalId` instead of an AniList-specific column on the core `Anime` entity.

AniList account connection is available under **Settings → AniList** for every local AniLingo account. Each profile connects its own AniList account through AniList's Auth PIN flow with its own client ID; tokens are protected and stored separately per AniLingo profile under `/data`, while the Data Protection key ring remains under `/data/keys`. Anime metadata and episode-range mappings remain shared library state managed independently from personal AniList credentials.

## Discover

Open **Discover** for one fast search surface across AniList anime, light novels/manga and the existing Books catalog. Search is debounced and cancels stale requests while typing; category filters plus **Trending**, **Top** and **My AniList** browse modes update without full page reloads. Provider failures are isolated so one unavailable catalog does not blank the whole page.

**My AniList** always uses the currently signed-in AniLingo profile's own AniList connection. Results already present in AniLingo link directly to the canonical local anime, novel or Manga entry. Unmatched light novels can hand off to the existing authorized source importer and retain the AniList metadata match. Owner users can also continue an unmatched Manga result into the canonical CBZ/ZIP or mounted-path Manga importer; the selected AniList identity is applied to the imported Manga series automatically. AniLingo does not include a piracy/shadow-library indexer or DRM bypass; user-owned or otherwise authorized source URLs/files remain the fallback when automatic acquisition is unavailable.

Episode pages can explicitly sync watched progress for the currently signed-in AniLingo profile's connected AniList account. AniLingo reloads that profile's remote entry immediately before each write, never lowers progress, and sends only the list-entry `id` plus `progress`. It does not send score, notes, repeat count, priority, privacy, custom-list membership, dates or list status. Sync is blocked for ambiguous multi-season local groupings, non-`CURRENT` entries and the final episode to avoid completion-status/date side effects. A pre-write snapshot containing the local profile ID is appended under `/data/integrations` before every mutation; if that backup cannot be written, AniList is not modified.

Automatic sync is a per-profile choice under **Settings → AniList** and is stored with that profile's AniList connection: **Off** (default, manual Sync buttons only), **On completion** (shortly after an episode, chapter or volume is finished) or **Continuous** (forward progress once watching/reading of a work has paused for two minutes). It only considers local progress made after it was turned on. An in-process background service checks once a minute, reads the canonical local progress (anime episodes, Manga reader position, novel reader position) that changed since the last successful sync of each work, and hands it to exactly the same state/sync path as the manual buttons, so the same rules apply: no writes through unmatched, ambiguous or review-pending mappings, AniList progress that is already ahead is never lowered, only `progress` (and mapped `progressVolumes`) is sent, and protected list fields are verified. Each pass evaluates at most five works across all profiles; AniList failures are retried with exponential backoff (1 minute up to 6 hours), safety blocks are rechecked hourly up to daily, and an HTTP 429 pauses automatic sync server-wide until AniList's `Retry-After`. Per-work cursors and the last outcome are stored privately per profile under `/data/integrations/anilist/sync`; the AniList settings page lists the profile's recent sync activity with any error and the next retry time.

### Books For You

**Books → For you** (`/Books/ForYou`) shows Continue reading plus “Because you read …”, “More by …”, similar-subject and popular free-book shelves. Recommendations are deterministic and content-based: seeds are the current profile's most recent meaningful reads, then recently imported library books; same author scores strongest, shared stored subjects add to the score, duplicates and books already in the local library are suppressed, and each book appears on only one shelf. Only the current profile's reading state is used; there is no household profiling and nothing is persisted. The normal `/Books` page stays local-only — catalog searches (a bounded handful per visit, with timeouts) run only when For you is opened, and a failing provider just removes its shelf.

### Download clients (SABnzbd, qBittorrent)

Books and Anime submit downloads through one download-client abstraction, configured by the owner under **Settings → Download clients** (`/Settings/DownloadClients`): SABnzbd (usenet) and qBittorrent (torrent) connections, each with its own category, priority and enable/disable. The pipeline and Books submissions pick the highest-priority enabled, healthy client that supports a release's protocol and fail over to the next client of that protocol on submission failure. Every job appears under **Admin → Operations → Downloads** with progress, ETA, a clear failure reason (incomplete, corrupt, password-protected or failed extraction, for SABnzbd) and cancel/retry. When the anime acquisition service sends a release and it fails, that release is blocklisted and the next accepted release is tried within a bounded number of attempts. The one-time move of the earlier single SABnzbd connection (and, before that, older Books SABnzbd settings) into the canonical download client list is described in [docs/ADMIN_OPERATIONS.md](docs/ADMIN_OPERATIONS.md#sabnzbd-and-qbittorrent-download-clients).

### Anime acquisition

The owner can let AniLingo acquire missing anime episodes without Sonarr: monitor an anime on its page, and the in-process scheduler searches every enabled, healthy indexer (Prowlarr and/or direct Newznab/Torznab) for wanted episodes, scores the releases with the anime's quality profile, sends the best accepted usenet release to a SABnzbd-compatible download client, imports the completed download with the naming profile and reconciles the anime's library folder. Sonarr-owned anime, releases and paths are never touched, every accept/reject decision is logged with its reason, uncertain or blocked imports wait for a manual decision, and restarts neither lose nor duplicate downloads. Overview and manual actions: `/Acquisition`; indexers: `/Settings/Indexers`. Details: [docs/ANIME_ACQUISITION.md](docs/ANIME_ACQUISITION.md).

### Anime naming

Folder and file names use Sonarr-compatible templates (series, season, specials, standard, daily and anime episode formats, multi-episode styles, illegal-character and colon replacement, release/quality/provider-ID tokens). The owner edits naming profiles under `/Settings/Naming` (presets include the Sonarr default and the current Sonarr-with-media-info scheme) and picks a default, per-library or per-anime profile. Existing files are only renamed from an anime's **Rename files** page after a preview; Sonarr-owned files, read-only libraries, conflicts and cross-filesystem moves are refused, failed renames roll back, and episode identity and progress are kept. Details: [docs/ANIME_NAMING.md](docs/ANIME_NAMING.md).

## Web / Light Novels

Open **Novels** to import a supported Japanese web novel. The first source provider is **Shōsetsuka ni Narō / ncode.syosetu.com**. AniLingo stores the work and chapter index in the existing SQLite database; Japanese chapter text is fetched and cached when a chapter is opened. **Cache all Japanese text** can queue the remaining chapters through the existing in-process background worker.

The reader works without AI and provides Japanese-only reading, chapter navigation, persisted per-profile reading position, profile-scoped browser appearance preferences, adjustable text size/line spacing/width and mobile-friendly layout. When Codex is connected under **Settings → AI**, a chapter can be translated to German explicitly. AniLingo translates bounded chapter segments, stores the completed result only after every segment succeeds, and keys reuse to the chapter's exact source hash + provider + prompt version. Refreshing changed Japanese source text therefore makes an older translation stale instead of silently showing it.

Novel metadata is separate from anime metadata. AniLingo searches AniList's novel media entries and stores the provider-neutral match on the imported novel. Each user can explicitly sync completed chapter progress to their own AniList entry; sync is monotonic, only updates existing `CURRENT` entries, snapshots the remote entry before writing, and leaves final-chapter completion/status changes to AniList. A novel can also map chapter ranges to an already-matched local anime's season/episode ranges. Manual mappings remain authoritative; optional Codex suggestions are stored as AI suggestions and never replace manual mappings.

## Optional Codex connection

The image includes the Codex CLI, but AniLingo does not require AI to scan media or learn vocabulary.

Open **Settings → AI** and choose **Connect with ChatGPT**. AniLingo starts the Codex device-code flow inside the container and shows the OpenAI login link and one-time code. The resulting Codex credentials are kept under `/data/codex`, so they survive normal container recreation as part of the existing data volume.

Device-code authorization may need to be enabled in the ChatGPT security settings or workspace permissions. AniLingo never reads or displays the stored credential file itself; status and logout are delegated to the Codex CLI.

The initial integration deliberately exposes no generic prompt or agent execution endpoint. AI capabilities will be added only for narrow learning tasks where they are useful. The provider boundary allows later API-key or other-engine providers without coupling them to the learning pages.

## Playback

Episode pages include an integrated HTML5 player. AniLingo serves media with HTTP range support, synchronizes the imported Japanese cue track, and exposes local reading, meaning and learning state when a highlighted subtitle word is clicked. The lookup path is deterministic and does not call AI.

Playback is **device-first and instant**. Each browser stores its own preference in local storage:

- **Auto** (default): prefer direct play or a live video-copy remux that the device can decode; use the live server H.264 fallback when the browser does not report the required codec support or device playback fails.
- **Device only**: never video-transcode on the server. Compatible H.264 is remuxed to fragmented MP4 as it is watched, and HEVC/H.265 can be remuxed with `hvc1` tagging while keeping the video stream unchanged.
- **Server**: stream a broadly compatible H.264 `yuv420p` MP4 directly from ffmpeg when video conversion is required. Compatible H.264 is still copied rather than wastefully encoded again.

There is no prepare-playback step. Direct-play files retain HTTP range support; remux/transcode paths emit fragmented MP4 to the browser as ffmpeg produces it, so the whole episode is never encoded before playback starts. The NAS media stays read-only. The current server fallback uses software `libx264`; hardware acceleration can be added later without changing the device-first selection model.

Players show **Skip intro / recap / outro** only for canonical per-episode segment markers above the configured confidence (never automatically), and timeline seek previews once they have been generated in the background into a disposable `/data` cache. Markers come from owner corrections (**Edit skip segments** on the Episode page) or `*.segments.json` / `segments.json` sidecars read during library scans; see [docs/MEDIA_SEGMENTS.md](docs/MEDIA_SEGMENTS.md).

### Player controls

Below the video the player offers **−10 s / Repeat line / +10 s**, **Speed** (0.5×–2.0×), **Audio** (shown when the file has more than one audio track), **Subtitles** and **Quality**. Tracks are addressed by the stable canonical id `stream:{index}` from the [media inventory](#media-inventory) on every client.

- **Speed** changes only the playback rate. Both subtitle layers follow the media clock, so cue timing stays exact at any speed.
- **Audio**: the file's default track keeps direct play; another track is delivered as a live video-copy remux (video is never re-encoded just to switch audio). The selection survives a device → server fallback and every stream restart.
- **Subtitles**: **Off**, **Japanese · learning** (the interactive AniLingo cue overlay) or any embedded text track. When the chosen playback subtitle differs from the learning text, both are shown: the plain playback line below the interactive Japanese line. Choosing the embedded stream that already is the learning source simply shows the learning overlay. Image subtitles (PGS/VobSub) are listed but cannot be rendered.
- **Quality** is an optional remote-bandwidth cap: **Auto · original**, **1080p**, **720p** or **Lower bandwidth** (≤480p). It never transcodes when direct play already satisfies it: the server compares the cap with the source height from the media inventory, and only a source that is proven taller is converted (Server mode, or Auto when the server fallback can honour the cap). **Device only** never converts video, so a cap it cannot honour is explained instead of silently transcoding.
- Fullscreen, Picture-in-Picture, Wake Lock and system media controls (Media Session, including ±10 s) are used when the browser supports them and are simply absent otherwise.

**Save as my defaults** stores the current audio language, subtitle choice and speed for the profile.

| Preference | Owner | Where it lives |
| --- | --- | --- |
| Preferred audio language | Profile | `ProfilePlaybackPreferences` (server), `/api/client/v1/me/playback-preferences` |
| Preferred subtitle language or Off | Profile | `ProfilePlaybackPreferences` |
| Default playback speed | Profile | `ProfilePlaybackPreferences` (default 1.0×) |
| Autoplay next episode | Profile | `ProfilePlaybackPreferences` |
| Playback mode (Auto / Device only / Server) | Device | Browser local storage per profile; native device settings |
| Quality cap | Device | Browser local storage per profile; native device settings |
| Decoder support (for example HEVC) | Device | Detected at runtime, never stored |
| Current audio/subtitle track, speed in this playback | Session | Player memory only; kept across fallback/restart, dropped when the page closes |

The server resolves the initial selection once (file default, overridden by a matching profile language) for both the web page and `/episodes/{id}/player`, so clients do not re-implement track or codec rules.

## Expected media layout

A common layout works directly:

```text
Anime/
└── Sousou no Frieren/
    └── Season 01/
        ├── Sousou no Frieren - S01E03.mkv
        └── Sousou no Frieren - S01E03.ja.srt
```

Nearby subtitles are `.srt`, `.ass`, `.ssa` or `.vtt` files that start with the episode file name next to the episode or in a `Subs/` or `Subtitles/` folder beside it, plus any subtitle file in `Subs/<episode file name>/`; other folders are not searched. Language and flag tokens such as `.ja`, `.jpn`, `.japanese`, `[ja]`, `.forced`, `.default` and `.sdh` are recognized, and files tagged with another language are ignored. Candidates are chosen deterministically: explicit Japanese tags before untagged files (which are only used when their text contains Japanese kana), full subtitles before forced/signs-only files, then non-SDH, default-flagged, nearest folder, SRT → ASS → SSA → VTT and file name. Changed or deleted sidecars are reconciled on every library scan. A nearby external Japanese subtitle is preferred. If none exists, AniLingo selects the preferred embedded Japanese text track from the [media inventory](#media-inventory) and extracts it in memory. On the episode page, all embedded subtitle streams are visible; when tags are missing or wrong, any supported text stream can be explicitly selected as the Japanese learning source and vocabulary is rebuilt immediately.

Kodi/Jellyfin/Sonarr-style NFO files are read (never written) during library scans: `tvshow.nfo` in the series folder and an episode NFO with the same base name as the media file. Only a small subset is read — title, original title, plot/outline, year, premiered/aired date, season/episode numbers and provider IDs (`<uniqueid type="anilist|mal|tvdb|tmdb|imdb">`, falling back to legacy `<anilistid>`, `<malid>`, `<tvdbid>`, `<tmdbid>`, `<imdb_id>`). Of that, AniLingo currently uses:

- the `tvshow.nfo` title as the local anime title (matched AniList metadata still wins for display) and as the search text for automatic matching;
- the `tvshow.nfo` AniList ID to match a newly discovered anime directly instead of searching by title. It never replaces an existing (manual or automatic) metadata match; an unknown or already used ID falls back to normal automatic matching;
- the episode NFO title instead of the file-name title, only when the NFO's season/episode numbers agree with the file name. The file name always decides episode identity, so NFO numbering never moves learning progress.

NFO files with a DTD, external entities, malformed XML, an unsupported root element or more than 1 MiB are ignored with a warning in the scan log, and the last known titles are kept. The other parsed fields (plot, dates, MAL/TVDB/TMDB/IMDb IDs) are not stored yet, and season-level `season.nfo` files are not read.

### Media inventory

Every library scan also maintains a persisted technical analysis per media file (SQLite tables `MediaAnalyses` and `MediaAnalysisStreams`): container, duration, video codec/profile, resolution, bit depth, dynamic range (SDR, HDR10, HLG, Dolby Vision), audio tracks (codec, language, title, channels, default/forced) and subtitle streams (codec, language, title, default/forced, text or image). Playback decisions, the client API player metadata, the episode page's embedded subtitle list and embedded subtitle/Whisper stream selection all read this one inventory; there is no second probe cache.

`ffprobe` only runs when a file is new, its size or modification time changed, or the analysis logic changed (a code-level probe version). A file whose modification time changed but whose length and first/last 64 KiB are identical (a touched or re-copied file) keeps its analysis. An unchanged library therefore performs no `ffprobe` work on reconciliation. Media that `ffprobe` rejects is recorded as a failed analysis with a diagnostic, stays in the library and does not fail the scan; it is analysed again only once the file changes. If `ffprobe` cannot run at all (missing binary or timeout), the analysis stays pending and is retried by the next scan or playback once five minutes have passed. The first scan of a large library probes every file once, one file at a time.

The learning-text fallback order is **nearby text subtitle → embedded text subtitle → optional Jimaku lookup → local Whisper transcription**. Configure Jimaku under **Settings → Subtitles** with an API key generated by the Jimaku account. AniLingo tests the key before saving it and protects it with ASP.NET Core Data Protection under `/data/integrations`. AniList episode mappings are reused for Jimaku matching when available; otherwise AniLingo falls back to the local anime title and episode number.

If no usable subtitle is found, AniLingo transcribes the preferred Japanese audio stream through `whisper.cpp`. The quantized multilingual small model is downloaded once into `/data/whisper`, verified, and reused. Generated SRT lives under `/data/transcription-cache` and is imported through the same subtitle/vocabulary pipeline; source media is never modified. ASS/SSA, SubRip and WebVTT text are supported by the learning parser. Image subtitle formats such as PGS/DVD/DVB do not require OCR for learning text because Whisper remains the final fallback.

**Settings → Subtitles** shows ready/queued/processing/failed coverage, can prepare every episode that is still missing learning text, and can retry individual failures. Jimaku is optional: removing or never configuring the key does not disable the local Whisper fallback.

## Architecture

The application is a modular monolith:

```text
Razor Page
   ↓
Feature service
   ↓
EF Core / filesystem / external integration
```

Pages are page boundaries. Shared visual patterns stay in shared partials/components. JavaScript is reserved for interactions that require it; v0.1 does not use a client-side application shell.

Core areas live under `Features/Library`, `Features/Subtitles`, `Features/Vocabulary`, `Features/Learning`, `Features/Playback`, `Features/Novels` and the optional `Features/Ai` provider boundary. Future playback, AniList metadata and AI enrichment extend those boundaries instead of replacing them.

## v0.1 limitations

Japanese vocabulary is analyzed locally with a MeCab-compatible tokenizer and a bundled NAIST-JDIC dictionary. Meanings come from a pinned local JMdict snapshot, preferring German entries and falling back to common English entries. No dictionary network request or AI call is required at runtime.

The review scheduler uses FSRS-6 behind `IReviewScheduler`. FSRS state is reconstructed from the durable review history, so scheduler internals do not add database columns.

SQLite now uses an EF Core migration baseline. Existing epoch-2 pre-release databases created by the earlier `EnsureCreated` bootstrap are detected once, stamped as the baseline after the expected core tables are verified, and then upgraded through normal migrations. Fresh databases are created entirely through migrations. The supported persistence boundary is recorded in `.agent/upgrade-policy.yaml`.

## Dictionary data

Japanese lexical data is derived from the JMdict project maintained by the Electronic Dictionary Research and Development Group (EDRDG), via the jmdict-simplified JSON distribution. AniLingo pins the dictionary snapshot used for each image build and verifies the downloaded archives by SHA-256.

- JMdict project: https://www.edrdg.org/jmdict/j_jmdict.html
- jmdict-simplified: https://github.com/scriptin/jmdict-simplified
- JMdict data license/conditions: https://www.edrdg.org/edrdg/licence.html

The bundled morphological dictionary is the NAIST-JDIC data distributed by Debian as `open-jtalk-mecab-naist-jdic`.
