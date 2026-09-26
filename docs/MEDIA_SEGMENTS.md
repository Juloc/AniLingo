# Media segments and seek previews

AniLingo stores one canonical set of skip markers per episode and generates
optional seek-preview thumbnails ("trickplay"). Web, PWA, Android and Android TV
players consume the same server descriptor; clients never compute their own
segment boundaries, confidence rules or thumbnails.

Source media is only read. Markers live in SQLite (`EpisodeMediaSegments`);
generated thumbnails live in a disposable cache under `/data`.

## Segment model

Each marker has:

| Field | Meaning |
| --- | --- |
| `kind` | `intro` (OP), `recap`, `outro` (ED), `preview`, `credits` (credits/other) |
| `startMs`, `endMs` | Episode time in milliseconds; a marker is at least 1 second long |
| `source` | `manual`, `imported`, `provider` or `detector` |
| `method`, `version` | How the marker was produced (for example `manual 1`, `sidecar 1`, a detector name and version) |
| `confidence` | 0–1. Manual markers are always 1 |

There is at most one marker per episode, kind and source.

### Precedence

For each kind the most authoritative source wins, regardless of confidence:

1. `manual` – owner corrections on **Episode → Edit skip segments**
2. `imported` – sidecar files (below)
3. `provider` – reserved for trustworthy metadata markers mapped to the exact episode (none are imported yet)
4. `detector` – automatic detection

Removing a manual marker falls back to the next source. An explicit correction
therefore always replaces an automatic result, and a missing marker is preferred
over a bad guess.

### Skip actions

A player offers **Skip intro / recap / outro / credits / preview** only while the
playback position is inside a resolved marker whose confidence is at least the
configured threshold. The action seeks to the canonical `endMs`. Nothing is
skipped automatically.

The threshold is part of the application configuration:

```json
{
  "MediaSegments": {
    "SkipConfidenceThreshold": 0.8
  }
}
```

(`MediaSegments__SkipConfidenceThreshold` as an environment variable.) Each
descriptor carries the threshold that was applied and a per-segment `canSkip`
flag, so clients do not re-implement the rule.

## Sidecar import format

Sidecar files are read (never written) during every library scan by
`MediaSegmentSidecarImporter`. They are the source of truth for `imported`
markers: a marker removed from the file is removed from AniLingo; manual,
provider and detector markers are never touched. Re-scanning an unchanged file
changes nothing.

Two placements are supported:

**Per episode** – next to the media file, named after it:

```text
Season 01/
├── Frieren - S01E03.mkv
└── Frieren - S01E03.segments.json
```

```json
{
  "version": 1,
  "segments": [
    { "kind": "intro", "startMs": 62000, "endMs": 151500 },
    { "kind": "outro", "start": "22:10", "end": "23:40", "confidence": 0.95 }
  ]
}
```

**Per folder** – `segments.json` in the media folder or the anime's top-level
folder, keyed by the local season and episode numbers:

```json
{
  "version": 1,
  "episodes": [
    { "season": 1, "episode": 3, "segments": [ { "kind": "op", "start": "1:02", "end": "2:31.5" } ] }
  ]
}
```

Rules:

- `kind` accepts `intro`/`op`/`opening`, `recap`, `outro`/`ed`/`ending`,
  `preview`/`next`, `credits`/`other`.
- Times are `startMs`/`endMs` (integers) or `start`/`end` timecodes
  (`m:ss`, `h:mm:ss`, optional fraction, or plain seconds).
- `confidence` defaults to 1.0 and is clamped to 0–1.
- `season` defaults to 1.
- Lookup order per episode: the episode sidecar wins completely, then
  `segments.json` in the media folder, then in the anime folder.
- Invalid entries are skipped with a warning in the scan log. A file that is
  larger than 1 MiB, not valid JSON, has an unsupported `version` or cannot be
  read (for example an offline share) is ignored and the previously imported
  markers are kept.

## Automatic detection hook

`IMediaSegmentDetector` is the extension point for automatic detection. Results
are stored as `detector` markers with the detector's method, version, confidence
and the media identity they were computed against. Detection is skipped while
media identity and detector version are unchanged; **Re-run segment detection**
forces a rebuild. Detector markers never outrank manual, imported or provider
markers, and a detector must not modify source media.

The default detector is a no-op (`method` `none`): until cross-episode
fingerprint detection of repeated OP/ED material exists, episodes without manual
or imported markers have no skip action.

## Seek previews (trickplay)

When a player loads a playable episode, AniLingo queues a background operation
(**Admin → Operations**, kind `trickplay-generation`, maintenance lane) that
extracts keyframe thumbnails with `ffmpeg` into JPEG sprite sheets. Playback
never waits for it.

- **Identity and duration** come from the canonical [media inventory](../README.md#media-inventory):
  the analysed content fingerprint (fallback: size + modification time) and the
  analysed duration. Without a successful analysis there are no previews; the
  generator never probes on its own.
- **Cache**: `/data/playback-cache/trickplay/<media file>-<identity>-v<generator version>/`
  with `index.json` and `sprite-NNN.jpg`. A new identity or generator version
  produces a new directory and prunes the superseded ones of the same media file.
  The cache can be deleted at any time and is regenerated on demand.
- **Bounds**: one thumbnail every 10 seconds or more, at most 720 thumbnails
  (320×180 tiles, 10×10 per sprite), at most two pending generations, a
  20-minute ffmpeg timeout.
- **Degradation**: when ffmpeg or the media file is unavailable the operation
  fails with a readable message, the descriptor reports `unavailable`, and the
  player simply shows no preview. A failed generation is retried after a
  restart, when the media changes, or via **Rebuild seek previews**.

## Client descriptor

The player bootstrap (`GET /api/client/v1/episodes/{id}/player`) and the web
Episode page expose the same additive fields; the capability flags
`mediaSegments` and `trickplay` announce them.

```json
{
  "segments": {
    "skipConfidenceThreshold": 0.8,
    "segments": [
      { "kind": "intro", "startMs": 62000, "endMs": 151500, "source": "imported",
        "method": "sidecar", "version": "1", "confidence": 1.0, "canSkip": true }
    ]
  },
  "trickplay": {
    "state": "ready",
    "message": null,
    "generatorVersion": 1,
    "intervalMs": 10000,
    "tileWidth": 320,
    "tileHeight": 180,
    "columns": 10,
    "rows": 10,
    "thumbnailCount": 142,
    "spriteUrls": [ "/api/client/v1/episodes/{id}/trickplay/sprite-001.jpg" ],
    "descriptorUrl": "/api/client/v1/episodes/{id}/trickplay"
  }
}
```

`state` is `ready`, `generating` or `unavailable`. Thumbnail `i` covers
`[i × intervalMs, (i + 1) × intervalMs)` and sits on sprite
`spriteUrls[i / (columns × rows)]` at column `i % columns`, row
`(i / columns) % rows`. Clients poll `descriptorUrl` while the state is
`generating`.

Separate endpoints:

```text
GET /api/client/v1/episodes/{id}/segments
GET /api/client/v1/episodes/{id}/trickplay
GET /api/client/v1/episodes/{id}/trickplay/{index.json|sprite-NNN.jpg}
```

## Not included

- Cross-episode audio/video fingerprint detection of OP/ED (follow-up on #135)
- Provider/metadata marker import
- Automatic skipping or per-profile auto-skip preferences
- Viewing analytics or behaviour-based detection
