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
- PostgreSQL persistence

AniList, AI enrichment, embedded subtitle extraction and the integrated player are later phases. See issue #1 for the staged roadmap.

## Run with Docker

Set the host path that contains the anime library and start the stack:

```bash
ANIME_PATH=/path/to/anime docker compose up --build
```

Open `http://localhost:8097`.

Optional environment variables:

- `ANILINGO_PORT` changes the published HTTP port.
- `ANILINGO_DB_PASSWORD` changes the PostgreSQL password.

The anime path is mounted read-only at `/media/anime`.

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
