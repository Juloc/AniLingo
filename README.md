# AniLingo

Current release: **0.1.0-alpha.12**

AniLingo is a Docker-first Japanese learning companion for an existing anime library. It scans media from a read-only NAS mount, imports nearby Japanese subtitles, builds episode vocabulary, and lets you mark terms as known or review them before watching.

## v0.1

The first vertical slice includes:

- NAS library roots and background scanning
- deterministic anime / season / episode discovery from paths and filenames
- external Japanese SRT and ASS subtitle import
- automatic embedded Japanese text-subtitle extraction through ffmpeg
- local Japanese audio transcription fallback through whisper.cpp when no Japanese text source is available
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
- known / learning term state
- FSRS-6 spaced repetition with Again / Hard / Good / Easy interval previews
- responsive Razor Pages UI with a Jellyfin/Plex-style shell
- built-in single-owner login for safe reverse-proxy exposure
- embedded SQLite persistence in the single application container
- optional Codex CLI connection for later AI-assisted features

AniList watch-progress synchronization, AI enrichment and image-based subtitle OCR are later phases. See issue #1 for the staged roadmap.

## Docker stack

AniLingo runs as one container. There is no database sidecar and no required environment configuration.

The minimal stack is:

```yaml
services:
  anilingo:
    image: ghcr.io/juloc/anilingo:0.1.0-alpha.12
    volumes:
      - anilingo-data:/data
      - /path/to/anime:/media/anime:ro
    networks:
      - default
    ports:
      - "8097:8080"

volumes:
  anilingo-data:
```

The example is pinned to the current alpha release. Replace `/path/to/anime` with the host path of the existing anime library, then run:

```bash
docker compose up -d
```

Open `http://localhost:8097`. On the first visit AniLingo redirects to **Create owner account**. After that, every application page requires the owner login; there is no public registration.

### Public URL / Caddy

For Internet access, terminate HTTPS at Caddy (or another trusted reverse proxy) and keep AniLingo itself on the private Docker network. Do not forward port 8097 directly from the router to the Internet. AniLingo accepts `X-Forwarded-For` and `X-Forwarded-Proto` from loopback and private RFC1918/ULA proxy networks, so HTTPS cookies are marked correctly when Caddy terminates TLS.

The authentication cookie is HttpOnly, SameSite=Lax and secure whenever the original request is HTTPS. Login attempts are rate-limited. Passwords are stored only as ASP.NET Core Identity password hashes in the existing SQLite database, while the existing Data Protection key ring under `/data/keys` keeps authentication cookies valid across normal container recreation.

When Caddy shares a Docker network with AniLingo, Caddy can proxy directly to `anilingo:8080`; publishing `8097:8080` is only needed when you also want direct host access.

Runtime paths are fixed and intentionally simple:

- `/data` stores the SQLite database, Codex authentication state, the persistent Whisper model and generated transcription cache.
- `/media/anime` is the read-only anime library mount.

For an existing Docker stack, replace `default` with that stack's network if needed. No connection string, database password, media environment variable or second service is required.

## AniList metadata

AniLingo can match each locally discovered anime to AniList without requiring an AniList account. Open an anime in **Library**, search AniList, and explicitly choose the correct result. The local NAS grouping remains the source of truth for files and episodes; the AniList match only supplies cached display metadata such as titles, cover art, banner art, format, year and episode count.

Normal Home/Library browsing reads the cached SQLite metadata and makes no AniList request. AniList is queried only when you search, match or refresh metadata. The integration is behind `IAnimeMetadataProvider`, and the database stores `Provider` + `ExternalId` instead of an AniList-specific column on the core `Anime` entity.

AniList account connection is available under **Settings → AniList**. Self-hosted instances use AniList's Auth PIN flow with the user's own client ID; the access token is protected before it is written under `/data`, and the Data Protection key ring is persisted under `/data/keys`. Watch-progress and list mutations are intentionally not part of this slice.

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

Recognized Japanese subtitle suffixes include `.ja`, `.jpn` and `.japanese` with `.srt` or `.ass`. A nearby external Japanese subtitle is preferred. If none exists, AniLingo probes the media container and extracts the preferred embedded Japanese text track in memory. On the episode page, all embedded subtitle streams are visible; when tags are missing or wrong, any supported text stream can be explicitly selected as the Japanese learning source and vocabulary is rebuilt immediately.

If no Japanese text subtitle is detected, AniLingo queues a local transcription of the preferred Japanese audio stream through `whisper.cpp`. The quantized multilingual small model is downloaded once into `/data/whisper`, verified, and reused. Generated SRT lives under `/data/transcription-cache` and is imported through the same subtitle/vocabulary pipeline; source media is never modified. ASS/SSA, SubRip, WebVTT and mov_text are supported directly. Image subtitle formats such as PGS/DVD/DVB are still shown as image subtitles, but the audio-transcription fallback can provide learning text without OCR.

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

Core areas live under `Features/Library`, `Features/Subtitles`, `Features/Vocabulary`, `Features/Learning`, `Features/Playback` and the optional `Features/Ai` provider boundary. Future playback, AniList metadata and AI enrichment extend those boundaries instead of replacing them.

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
