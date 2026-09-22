# AniLingo

AniLingo is a Docker-first Japanese learning companion for an existing anime library. It scans media from a read-only NAS mount, imports nearby Japanese subtitles, builds episode vocabulary, and lets you mark terms as known or review them before watching.

## v0.1

The first vertical slice includes:

- NAS library roots and background scanning
- deterministic anime / season / episode discovery from paths and filenames
- external Japanese SRT and ASS subtitle import
- automatic embedded Japanese text-subtitle extraction through ffmpeg
- real Japanese morphological analysis with base forms and readings
- local JMdict meanings with German-first / common-English fallback
- episode vocabulary frequency and preparation progress
- direct-play episode player with synced, clickable Japanese subtitles
- cached browser fallback for common H.264 8-bit MKV files without video re-encoding
- known / learning term state
- FSRS-6 spaced repetition with Again / Hard / Good / Easy interval previews
- responsive Razor Pages UI with a Jellyfin/Plex-style shell
- embedded SQLite persistence in the single application container
- optional Codex CLI connection for later AI-assisted features

AniList, AI enrichment, image-based subtitle OCR and video transcoding are later phases. See issue #1 for the staged roadmap.

## Docker stack

AniLingo runs as one container. There is no database sidecar and no required environment configuration.

The minimal stack is:

```yaml
services:
  anilingo:
    image: ghcr.io/juloc/anilingo:latest
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

Replace `/path/to/anime` with the host path of the existing anime library, then run:

```bash
docker compose up -d
```

Open `http://localhost:8097`.

Runtime paths are fixed and intentionally simple:

- `/data` stores the SQLite database, Codex authentication state and prepared browser-playback cache.
- `/media/anime` is the read-only anime library mount.

For an existing Docker stack, replace `default` with that stack's network if needed. No connection string, database password, media environment variable or second service is required.

## Optional Codex connection

The image includes the Codex CLI, but AniLingo does not require AI to scan media or learn vocabulary.

Open **Settings → AI** and choose **Connect with ChatGPT**. AniLingo starts the Codex device-code flow inside the container and shows the OpenAI login link and one-time code. The resulting Codex credentials are kept under `/data/codex`, so they survive normal container recreation as part of the existing data volume.

Device-code authorization may need to be enabled in the ChatGPT security settings or workspace permissions. AniLingo never reads or displays the stored credential file itself; status and logout are delegated to the Codex CLI.

The initial integration deliberately exposes no generic prompt or agent execution endpoint. AI capabilities will be added only for narrow learning tasks where they are useful. The provider boundary allows later API-key or other-engine providers without coupling them to the learning pages.

## Playback

Episode pages include an integrated HTML5 direct-play player. AniLingo streams the registered media file with HTTP range support, synchronizes the imported Japanese cue track, and exposes local reading, meaning and learning state when a highlighted subtitle word is clicked. The lookup path is deterministic and does not call AI.

AniLingo does not transcode in this slice. MP4/WebM remain direct-play paths. For common H.264 8-bit MKV files, AniLingo can prepare a seekable MP4 under `/data/playback-cache`: the video stream is copied without re-encoding and non-AAC first audio is converted to AAC. The original NAS media stays read-only. The cache identity includes the media-file id, source size and source timestamp, so changed source files do not reuse stale prepared media.

HEVC/H.265 and H.264 10-bit video currently remain outside the fallback because they require real video transcoding for broad browser compatibility. AniLingo reports that clearly instead of silently consuming CPU with an unexpected encode.

## Expected media layout

A common layout works directly:

```text
Anime/
└── Sousou no Frieren/
    └── Season 01/
        ├── Sousou no Frieren - S01E03.mkv
        └── Sousou no Frieren - S01E03.ja.srt
```

Recognized Japanese subtitle suffixes include `.ja`, `.jpn` and `.japanese` with `.srt` or `.ass`. A nearby external Japanese subtitle is preferred. If none exists, AniLingo probes the media container and extracts the preferred embedded Japanese text track in memory. ASS/SSA, SubRip, WebVTT and mov_text are supported; image subtitle formats such as PGS/DVD/DVB are not OCR'd. The NAS media mount remains read-only.

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

The pre-release database currently uses a fresh-schema bootstrap and is considered disposable. The persisted-state support boundary is recorded in `.agent/upgrade-policy.yaml`; a migration-backed baseline will replace the prototype bootstrap before a stable release.

## Dictionary data

Japanese lexical data is derived from the JMdict project maintained by the Electronic Dictionary Research and Development Group (EDRDG), via the jmdict-simplified JSON distribution. AniLingo pins the dictionary snapshot used for each image build and verifies the downloaded archives by SHA-256.

- JMdict project: https://www.edrdg.org/jmdict/j_jmdict.html
- jmdict-simplified: https://github.com/scriptin/jmdict-simplified
- JMdict data license/conditions: https://www.edrdg.org/edrdg/licence.html

The bundled morphological dictionary is the NAIST-JDIC data distributed by Debian as `open-jtalk-mecab-naist-jdic`.
