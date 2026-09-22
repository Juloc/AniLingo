# AniLingo

AniLingo is a Docker-first Japanese learning companion for an existing anime library. It scans media from a read-only NAS mount, imports nearby Japanese subtitles, builds episode vocabulary, and lets you mark terms as known or review them before watching.

## v0.1

The first vertical slice includes:

- NAS library roots and background scanning
- deterministic anime / season / episode discovery from paths and filenames
- external Japanese SRT and ASS subtitle import
- episode vocabulary frequency and preparation progress
- known / learning term state
- simple spaced review flow behind a replaceable scheduler interface
- responsive Razor Pages UI with a Jellyfin/Plex-style shell
- embedded SQLite persistence in the single application container

AniList, AI enrichment, embedded subtitle extraction and the integrated player are later phases. See issue #1 for the staged roadmap.

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

- `/data` stores the SQLite database and is the only persistent application-data volume.
- `/media/anime` is the read-only anime library mount.

For an existing Docker stack, replace `default` with that stack's network if needed. No connection string, database password, media environment variable or second service is required.

## Expected media layout

A common layout works directly:

```text
Anime/
└── Sousou no Frieren/
    └── Season 01/
        ├── Sousou no Frieren - S01E03.mkv
        └── Sousou no Frieren - S01E03.ja.srt
```

Recognized Japanese subtitle suffixes include `.ja`, `.jpn` and `.japanese` with `.srt` or `.ass`.

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

Core areas live under `Features/Library`, `Features/Subtitles`, `Features/Vocabulary` and `Features/Learning`. Future playback, AniList metadata and AI enrichment extend those boundaries instead of replacing them.

## v0.1 limitations

Vocabulary extraction is deliberately lightweight in the first slice. It identifies Japanese text candidates but is not yet a full morphological tokenizer/dictionary pipeline, so readings, meanings and lemma normalization are incomplete.

The review scheduler is intentionally behind `IReviewScheduler`; FSRS is planned for the learning-quality phase.

The pre-release database currently uses a fresh-schema bootstrap and is considered disposable. The persisted-state support boundary is recorded in `.agent/upgrade-policy.yaml`; a migration-backed baseline will replace the prototype bootstrap before a stable release.
