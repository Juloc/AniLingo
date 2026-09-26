# Offline Library & Reader Sync

Tracking issue: **#221** (parent context: #69, Android/TV architecture in
[ANDROID_CLIENTS.md](ANDROID_CLIENTS.md); reader model in
[UNIFIED_READER.md](UNIFIED_READER.md)).

This document is the contract for AniLingo's offline Book/Novel library: a
canonical versioned package a client downloads, verifies and reads without
network access, plus offline-first reading-state sync. It intentionally
reuses one server model instead of separate PWA/Android business logic
(matching the bounded offline playback design of #225/PR #341).

Status: **part 1 delivered** (server contract, sync endpoint, PWA download
manager foundation, Settings → Offline page). Part 2 (reader repository
abstraction, placing the "Save offline" action on Novel/Book/Manga detail
pages, Android Room/WorkManager) is future work; see [Part 2](#part-2-todo)
below.

## Why a separate contract from bounded offline playback (#225)

#225's `/api/client/v1/offline/*` endpoints exist to let the phone play the
*same* self-hosted media file it would stream, verified by a content
fingerprint of one file. Offline library content is structurally different:
one work is a tree of volumes and chapters, each with its own version, that
must be downloaded and updated *differentially* — re-fetching one changed
chapter must never require re-fetching the whole book. Reusing the playback
contract would either bolt a tree/version model onto a single-file descriptor
or duplicate progress reconciliation with different semantics for the same
underlying idea. Instead, `Features/OfflineLibrary/**` and
`ClientApiOfflineLibrary*.cs` mirror #225's *shape* (one additive capability
flag, a read-only descriptor endpoint, a monotonic progress reconciler) while
having their own chapter/volume/hash contract.

## Canonical model

Books and Novels already share one table set (`NovelWork` → `NovelVolume` →
`NovelChapter`, plus `NovelProgress`/`NovelBookmark`/`NovelHighlight`), owned
by `Features/Novels/**`. The offline library adds **no parallel content
tables** — it only adds two sync-related columns on `NovelBookmark`
(`SyncUpdatedAt`, `ClientEventId`) and one tombstone table
(`NovelBookmarkTombstone`). Manifest and chapter payload generation
(`Features/OfflineLibrary/OfflineLibraryQueries.cs`) is a read-only
projection over the existing tables — exactly like `NovelCatalogQueries` and
`NovelChapterText`, which it reuses directly (both are `internal`, and
`internal` is assembly-scoped, not namespace-scoped, so this is intentional
reuse rather than a workaround).

## Server contract (`/api/client/v1/offline-library`, capability `offlineLibrary`)

All endpoints are additive v1, authenticated, and advertised by the new
`offlineLibrary` capability flag (`ClientApiContracts.cs`). Manifest/chapter/
asset reads are shared library content (same authorization as
`/api/client/v1/anime/{id}`); the sync endpoint is profile-scoped.

### `GET /works/{workId}/manifest` → `ClientOfflineLibraryManifest`

```
workId, schemaVersion, contentVersion, title, author, description,
coverAssetUrl, issuedAtUtc,
volumes:  [{ volumeId, number, title, kind, coverAssetUrl }]
chapters: [{ chapterId, volumeId, number, title, hash, hasContent, hasTranslation }]
```

- `schemaVersion` is `OfflineLibraryContract.SchemaVersion` (currently `1`):
  bump it for a breaking wire-shape change; older clients can refuse to
  parse a manifest whose `schemaVersion` they do not understand.
- `contentVersion` (`OfflineLibraryContract.ComputeWorkContentVersion`) is a
  SHA-256 over title/author/cover and every chapter hash **in reading order**.
  It changes when a chapter is added, removed, reordered or edited, or when
  cached title/author/cover metadata changes. A client can skip a manifest
  fetch entirely when it already has this exact version cached (subject to
  its own revalidation policy), and always knows *something* changed without
  inspecting every chapter.
- Each chapter's `hash` (`OfflineLibraryContract.ComputeChapterHash`) is a
  SHA-256 of the chapter's `SourceHash` plus the identity
  (`language:provider:promptVersion:sourceHash`) of every *current* cached
  translation (order-independent). "Current" means the translation's
  `SourceHash` still matches the chapter's — the same rule the reader itself
  uses to pick which cached translation to render
  (`NovelChapterText.LoadAsync`), so the offline package can never disagree
  with what the online reader would show. **Differential download**: a
  client compares this hash per chapter against what it already stored; only
  a changed or new hash needs `GET /chapters/{id}`, and a chapter no longer
  listed is removed locally.

### `GET /chapters/{chapterId}` → `ClientOfflineLibraryChapterPayload`

```
chapterId, workId, volumeId, number, title, hash,
originalText,
blocks:       [{ kind, level, runs: [{ text, ruby, emphasis, strong }], imageAssetUrl, imageAlt }]
translations: [{ targetLanguage, text }]
```

- `blocks` mirrors `NovelContentBlock`/`NovelReaderBlock` (paragraph/heading/
  image with inline runs); an image block's `imageAssetUrl` is one of the
  asset URLs below, never raw bytes inline.
- `translations` includes only the current translation per language (see
  above) — never every historical translation attempt.
- `hash` is repeated here (equal to the manifest's ref for this chapter) so a
  client can immediately detect a version race: if the manifest was fetched,
  then the chapter changed again before the payload request landed, the
  returned `hash` will differ from what the client expected and the client
  should treat the chapter as still pending (fetch again later) rather than
  mark it verified with a mismatched hash.

### `GET /assets/{volumeId}/{asset}` — cover/illustration bytes

Reuses `NovelVolumeAssetStore` (`Features/Novels`) exactly as `/Novels/Asset/`
does. **Path safety**: the store only resolves a content-addressed file name
matching `^[a-f0-9]{32}\.(jpg|png|gif|webp)$`
(`NovelVolumeAssetStore.IsAssetName`/`.Resolve`); anything else — traversal
sequences, encoded traversal, arbitrary extensions, or a name that was never
saved for that volume — resolves to `null` and the endpoint answers `404`.
No host filesystem path is ever part of the URL or the response.

### `POST /sync` — reading-state batch (profile-scoped)

```json
{
  "progress":  [{ "clientEventId", "workId", "chapterId", "positionPermille",
                  "anchorLanguage", "anchorParagraphIndex", "anchorOffset",
                  "clientTimestampUtc" }],
  "bookmarks": [{ "clientEventId", "bookmarkId", "type": "upsert" | "remove",
                  "workId", "chapterId", "language", "positionPermille",
                  "paragraphIndex", "characterOffset", "anchorText",
                  "label", "style", "color", "clientTimestampUtc" }]
}
```

At most `OfflineLibraryContract.MaxSyncBatchItems` (200) items per list per
request. Response mirrors each event back with an `outcome` and, for
progress, the resulting canonical position.

#### Progress: forward-only (mirrors #341's `OfflineProgressReconciler`)

Novel/Book reading position is a `(chapter number, position‰)` tuple instead
of episode milliseconds, but the rule is the same shape as offline playback
progress: **`OfflineLibrarySyncRules.DecideProgress`** only ever moves the
tuple forward (later chapter, or same chapter/later position). An identical
replay is `unchanged`; anything behind the stored position is
`ignored_behind`; an unknown chapter (or one that does not belong to the
given work) is `chapter_not_found`. Applying a checkpoint calls the exact
same `NovelProgressService.SaveProgressAsync` the online reader uses —
there is no second progress writer.

#### Bookmarks: last-writer-wins with tombstones

Bookmarks can be added, edited or removed while offline, on potentially more
than one device, so a simple "newest wins" per field is not enough — a
device must also be able to tell a genuine deletion apart from "I haven't
synced this add yet". `OfflineLibrarySyncRules.DecideBookmark` treats a live
bookmark's `SyncUpdatedAt` and a tombstone's `DeletedAtUtc` as one shared
clock and takes whichever is later:

- An event older than that clock is `ignored_stale` — it never overwrites a
  newer edit and never resurrects a bookmark removed later.
- `upsert` (add and edit share one event shape) creates or updates the
  bookmark and clears any tombstone (a later add after a remove
  *resurrects* the bookmark — deterministic, not a race, because it is
  strictly newer).
- `remove` deletes the bookmark and records/updates a tombstone
  (`NovelBookmarkTombstone`, keyed by the same bookmark id).
- An event that exactly repeats the currently-known timestamp is
  `unchanged` (idempotent replay, no duplicate writes, no extra history).

The **bookmark id is client-supplied** (`NovelBookmark.Id`'s setter, already
public) so a device can create a bookmark offline and reference it in a
later edit/removal before the server has ever seen it, and so two devices
converge on the same row instead of creating duplicates. `ClientEventId` is
stamped on the bookmark for auditing which event last touched it; it is not
used for idempotency by itself — the last-writer-wins timestamp is.

Both reconcilers are profile-scoped throughout (every query/write filters by
`ProfileId`); one profile's sync batch can never see or affect another's
progress, bookmarks or tombstones.

## PWA download manager

Three new `wwwroot/js/` modules, split the same way `tts.js`/`reader-tts.js`
already are — pure engine vs. browser I/O vs. DOM wiring — so the pure parts
run under the existing Jint test harness
(`tests/AniLingo.Tests/OfflineLibraryEngineTests.cs`, mirroring
`DeviceSpeechEngineTests.cs`):

- **`offline-library.js`** (`window.AniLingoOfflineLibrary`) — pure, no
  IndexedDB/OPFS/network: `diffManifest` (differential detection),
  `isBookComplete` (finalization rule — a book is "available offline" only
  once every selected chapter's *verified local hash* matches the manifest),
  `transitionQueueItem`/`nextEligibleItem` (the queue state machine:
  `queued → downloading → verified`, with `paused`/`failed`/`cancelled`
  branches and bounded exponential backoff via `computeBackoffMs`),
  `isNetworkEligible` (Wi-Fi-only, explicitly best-effort — see below),
  `namespaceKey`, `formatBytes`/`totalStorageBytes`.
- **`offline-library-storage.js`** (`window.AniLingoOfflineLibraryStorage`)
  — IndexedDB (manifests, download queue/state, verified chapter hashes,
  local settings, the reading-state sync queue) and OPFS (chapter JSON,
  covers/illustrations), namespaced per profile (see below). Requests
  `navigator.storage.persist()` once per store and exposes both that result
  and whether OPFS itself is available as `isDegraded`/`persisted`; when
  OPFS is unavailable, chapter payload JSON is kept in IndexedDB instead
  (**degraded mode** — the UI surfaces this explicitly, see the Settings
  page below) and images are simply not cached rather than exhausting
  IndexedDB with binary blobs.
- **`offline-library-manager.js`** (`window.AniLingoOfflineLibraryManager`)
  — wires the two together: `enqueueBook` (fetch manifest, diff, queue the
  difference), `processQueue`/`pause`/`resume`/`retryFailed`,
  `removeChapter`/`removeBook`, `storageUsage`, the Wi-Fi-only setting, and
  `queueSyncEvent`/`drainSyncQueue` for the reading-state sync endpoint
  above (queued here so the transport and conflict handling already exist
  once the reader, part 2, starts calling into it).

### Atomic finalization

A chapter is written to OPFS/IndexedDB, then **read back and compared**
before it is recorded as verified — a truncated or corrupted write is
caught immediately rather than surfacing as a broken chapter later. A book's
manifest status only flips to `"available"` when `isBookComplete` is true
for every chapter the user selected. There is no intermediate state where a
partially-downloaded book looks complete.

### Storage ownership (PWA)

| Data | Store |
| --- | --- |
| Manifests, download queue/state, verified chapter hashes, Wi-Fi-only setting, reading-state sync queue | IndexedDB |
| Chapter payload JSON, cover/illustration bytes | OPFS (falls back to IndexedDB for chapter JSON only, in degraded mode) |
| App shell / static assets | Cache Storage via `service-worker.js` (unchanged) |

`service-worker.js`'s `isStaticAsset` allowlist is unchanged and explicitly
documented to never include `/api/client/v1/offline-library/**`: books never
enter the generic cache, and the service worker's own version bump/cleanup
(`CACHE_VERSION`) therefore cannot purge downloaded books — they live in a
completely separate storage area.

### Per-user isolation

Every IndexedDB database name and OPFS root directory is namespaced by the
signed-in profile id, read from `document.body.dataset.profileId`
(rendered by `Pages/Shared/_Layout.cshtml`, already the existing pattern —
see `pwa.js`'s own `data-offline-logout` handling). **No profile id means no
database is opened at all** — signing out hides every account's offline
state immediately (nothing to enumerate, nothing to accidentally show)
without deleting it, so a user who signs back in on the same device gets
their downloads back. Deleting a book's local files permanently is only ever
an explicit action in Settings → Offline or the "Save offline" control,
never an implicit side effect of signing out.

### Wi-Fi-only

`isNetworkEligible` uses the (Chromium-only) Network Information API
(`navigator.connection`) and is explicitly best-effort per #221: when the
API is unavailable, download eligibility is *not* blocked — silently never
starting a download because a feature detection failed would be worse than
occasionally downloading on cellular. The Settings → Offline page's toggle
label says so.

### Settings → Offline (`Pages/Settings/Offline.cshtml`)

Storage usage (with the degraded-mode notice when OPFS is unavailable), the
Wi-Fi-only toggle, and per-book remove. The reusable **"Save offline"**
action (`Pages/Shared/_OfflineLibraryAction.cshtml`,
`OfflineLibraryActionViewModel`) is included once, on the Books library
detail page (`Pages/Books/Library.cshtml`) — see [Part 2](#part-2-todo) for
where it still needs to be placed. Whole-book download only for part 1;
per-chapter selection reuses the same manifest/diff/queue machinery and is
part 2 scope.

## Migration

One migration, `20260926144053_AddOfflineLibrarySync`: two columns on
`NovelBookmarks` (`SyncUpdatedAt`, `ClientEventId`) and the new
`NovelBookmarkTombstones` table (cascade-deleted with its `NovelWork`).
`SyncUpdatedAt` defaults to a constant far-past timestamp for existing rows
(SQLite `ADD COLUMN` cannot default to another column's value) — existing
bookmarks predate offline sync entirely, so any real client event outranks
that default under last-writer-wins.

## Tests

- `tests/AniLingo.Tests/OfflineLibraryTests.cs`: chapter/work hash
  determinism and order-(in)dependence, differential manifest detection
  against a real SQLite database, chapter payload translation filtering and
  image asset URL resolution, asset path-safety (traversal, wrong
  extension, unknown/unsaved names, cross-volume isolation), the pure
  `DecideProgress`/`DecideBookmark` rules, and full reconciler runs proving
  idempotent replay, forward-only progress, tombstoned removal +
  resurrection, and profile isolation.
- `tests/AniLingo.Tests/OfflineLibraryEngineTests.cs`: the JS engine
  (`offline-library.js`) under Jint — manifest diffing, the finalization
  rule, queue state transitions (including invalid/no-op transitions),
  eligible-item selection with backoff, Wi-Fi-only eligibility, namespacing
  and storage formatting.

## Part 2 TODO

Left for the reader-integration slice (explicitly out of scope here because
another agent was editing the Novel reader concurrently):

1. **Reader repository abstraction.** Introduce the
   `Reader → BookRepository → {local source first, server source when
   required}` seam the issue describes, so `/Novels/Read` and `/Books/Read`
   can serve a downloaded chapter/progress/bookmarks from
   `offline-library-storage.js` while offline, with **no separate
   offline/online rendering path** in the reader itself.
2. Wire the reader's existing progress/bookmark writes to
   `manager.queueSyncEvent(...)` first (offline-first: local write always
   succeeds, then queued), and call `manager.drainSyncQueue()` on
   reconnect/interval. The sync endpoint and conflict rules already exist
   and are tested; only the reader-side call sites are missing.
3. Place the `_OfflineLibraryAction` partial (and, once selection UI
   exists, per-chapter selection reusing `enqueueBook`'s `chapterIds`
   option) on `Pages/Novels/**` detail pages — explicitly not touched here.
4. Library "Offline" filter/view and a compact global download-queue
   indicator (issue's UX section); the manager already exposes
   `listBooks()`/`onChange(...)` for this.
5. Fuller localization of the JS-rendered strings in
   `offline-library-ui.js` through the same catalog mechanism `pwa.js` uses
   (`shellText`/`app-shell-text`) — left as plain English source strings for
   part 1, consistent with the rest of the codebase's untranslated JS
   surfaces, but not yet wired to `UiTranslationResources.cs`.
6. **Android** (`app-mobile`, per `ANDROID_CLIENTS.md`'s architecture): Room
   entities mirroring the IndexedDB stores above (manifests, queue,
   verified chapters, bookmarks/progress sync queue), app-private storage
   for chapter/asset bytes (mirroring OPFS), and WorkManager jobs for
   persistent, resumable downloads — consuming the exact same
   `/api/client/v1/offline-library/**` contract and
   `OfflineLibrarySyncRules` semantics (server-side; Android only needs to
   replay the same event shapes, not reimplement the conflict rules).
7. **Manga reuse**: the manifest/chapter/asset/sync contract here is
   already generic over "work → volume → chapter" content; a Manga chapter
   payload would swap `originalText`/`blocks` for an ordered page-image
   list while keeping the same manifest hash/diff/sync machinery. No second
   download engine should be built for it.
