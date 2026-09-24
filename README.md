# AniLingo

Current prerelease: **0.1.0-alpha.22**.

AniLingo is a Docker-first Japanese learning companion for an existing anime library. It scans media from a read-only NAS mount, imports nearby Japanese subtitles, builds episode vocabulary, and lets you mark terms as known or review them before watching.

## v0.1

The first vertical slice includes:

- NAS library roots and background scanning
- deterministic anime / season / episode discovery from paths and filenames
- external Japanese SRT and ASS subtitle import
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

### Sleeping / unavailable NAS and Wake-on-LAN

AniLingo treats temporary media-storage outages separately from deleted files. A sleeping or unavailable NAS does not remove the persisted Library, and an unexpectedly empty previously-populated root is rejected as unsafe reconciliation rather than interpreted as a mass deletion.

Under **Settings → Media storage**, the owner can test each configured root without running a library scan. Wake-on-LAN can optionally be configured per root with a MAC address and, when needed from Docker networking, the LAN broadcast IPv4 address such as `192.168.178.255`. Wake actions are owner-only and rate-limited. Pressing Play as a normal user never sends a magic packet automatically.

Playback checks the owning storage before codec selection. If storage is temporarily unavailable, the player preserves playback intent/position and retries with bounded backoff for up to about one minute. It resumes automatically when storage returns; after the automatic window it offers **Try again**. An owner also gets an explicit **Wake NAS** action in the player when Wake-on-LAN is configured. A root that is online while one concrete file is absent is reported as a real missing-file condition instead of being retried as a NAS outage.

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

AniList account connection is available under **Settings → AniList**. Self-hosted instances use AniList's Auth PIN flow with the user's own client ID; the access token is protected before it is written under `/data`, and the Data Protection key ring is persisted under `/data/keys`.

Episode pages can explicitly sync watched progress for an already-existing AniList entry. AniLingo reloads the remote entry immediately before each write, never lowers progress, and sends only the list-entry `id` plus `progress`. It does not send score, notes, repeat count, priority, privacy, custom-list membership, dates or list status. Sync is blocked for ambiguous multi-season local groupings, non-`CURRENT` entries and the final episode to avoid completion-status/date side effects. A pre-write snapshot is appended under `/data/integrations` before every mutation; if that backup cannot be written, AniList is not modified.

## Web / Light Novels

Open **Novels** to import a supported Japanese web novel. The first source provider is **Shōsetsuka ni Narō / ncode.syosetu.com**. AniLingo stores the work and chapter index in the existing SQLite database; Japanese chapter text is fetched and cached when a chapter is opened. **Cache all Japanese text** can queue the remaining chapters through the existing in-process background worker.

The reader works without AI and provides Japanese-only reading, chapter navigation, persisted per-profile reading position, adjustable text size/line spacing/width and mobile-friendly layout. When Codex is connected under **Settings → AI**, a chapter can be translated to German explicitly. AniLingo translates bounded chapter segments, stores the completed result only after every segment succeeds, and keys reuse to the chapter's exact source hash + provider + prompt version. Refreshing changed Japanese source text therefore makes an older translation stale instead of silently showing it.

Novel metadata is separate from anime metadata. AniLingo searches AniList's novel media entries and stores the provider-neutral match on the imported novel. A novel can also map chapter ranges to an already-matched local anime's season/episode ranges. Manual mappings remain authoritative; optional Codex suggestions are stored as AI suggestions and never replace manual mappings.

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

## Expected media layout

A common layout works directly:

```text
Anime/
└── Sousou no Frieren/
    └── Season 01/
        ├── Sousou no Frieren - S01E03.mkv
        └── Sousou no Frieren - S01E03.ja.srt
```

Recognized nearby Japanese subtitle suffixes include `.ja`, `.jpn` and `.japanese` with `.srt`, `.ass` or `.ssa`. A nearby external Japanese subtitle is preferred. If none exists, AniLingo probes the media container and extracts the preferred embedded Japanese text track in memory. On the episode page, all embedded subtitle streams are visible; when tags are missing or wrong, any supported text stream can be explicitly selected as the Japanese learning source and vocabulary is rebuilt immediately.

The learning-text fallback order is **nearby text subtitle → embedded text subtitle → optional Jimaku lookup → local Whisper transcription**. Configure Jimaku under **Settings → Subtitles** with an API key generated by the Jimaku account. AniLingo tests the key before saving it and protects it with ASP.NET Core Data Protection under `/data/integrations`. AniList episode mappings are reused for Jimaku matching when available; otherwise AniLingo falls back to the local anime title and episode number.

If no usable subtitle is found, AniLingo transcribes the preferred Japanese audio stream through `whisper.cpp`. The quantized multilingual small model is downloaded once into `/data/whisper`, verified, and reused. Generated SRT lives under `/data/transcription-cache` and is imported through the same subtitle/vocabulary pipeline; source media is never modified. ASS/SSA and SubRip text are supported by the learning parser. Image subtitle formats such as PGS/DVD/DVB do not require OCR for learning text because Whisper remains the final fallback.

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
