# Reading acquisition

Manga and Light Novels use the same acquisition boundaries as the rest of Jularr. Razor pages only create or request a canonical target; they never submit directly to SABnzbd and never import a completed download themselves.

## Lifecycle

```text
catalog result
  -> AcquisitionRequest
  -> Searching
  -> Downloading
  -> Importing
  -> Completed / In library
```

A request can also be `Pending`, `Approved`, `Rejected`, or `Failed`.

`AcquisitionRequestService` is the authoritative add/request and permission boundary. The Manga and Light-Novel executors search enabled Newznab/Prowlarr Usenet indexers and submit the selected release through `DownloadClientSubmissionService`. The selected download-client entry and media kind are persisted on the Operation.

The SABnzbd monitor is the only component that projects external queue/history state onto Operations. It does not need a second reading-specific polling loop.

`WantedAcquisitionService` consumes persisted request and Operation state in bounded batches. For Manga and Light Novels it:

- searches an `Approved` request only when its persisted backoff is due;
- continues a failed download with the next untried matching release;
- treats a download the owner cancelled as "stop": the request fails with a cancelled note and no other release is grabbed;
- changes a successful download to the real `Importing` state;
- resumes `Importing` after restart;
- keeps infrastructure/storage failures (no completed path, files not reachable) in `Importing` instead of grabbing a duplicate release, and fails the request with the reason when the files are still missing 24 hours after the download finished;
- returns an unsuitable downloaded package to Wanted so another release can be tried.

The reason a release was dropped is shown once in the request status and then cleared, so repeated searches do not repeat it. Retrying a failed download under Operations is refused once the request has moved on to a newer release; retrying the last download of a request that is waiting or failed puts the request back to `Downloading`.

## Completed downloads

`CompletedDownloadLocationResolver` resolves the exact originating download client recorded on the Operation, reads the completed storage path, and applies the existing canonical acquisition remote-path mapping.

`CompletedDownloadDispatcher` is the shared dispatch boundary. Media-specific adapters normalize the files but do not own download polling or retry scheduling:

- Manga -> `MangaCompletedDownloadImportAdapter` -> existing `MangaImportService`
- Light Novel -> `LightNovelCompletedDownloadImportAdapter` -> existing `NovelEpubImportService`

After import, the adapter reconciles AniList metadata when applicable and returns the local library URL. Importers remain idempotent, so a restart during `Importing` can safely retry the same completed path.

### Manga library

Settings → Acquisition → **Manga library** sets the final Manga folder on the NAS (for example `/data/media/manga`) and, optionally, its own import mode. Without one, Move / Copy / Hardlink / Hardlink or copy comes from the default import mode. The file operation is the shared `ImportFileTransfer`, the same one Anime uses.

- A completed download goes to `<library>/<series>/<release folder>/`. Only CBZ/ZIP archives and page images are placed. Existing files are never overwritten: an identical file is skipped, so a retry is safe, and a different one gets a numbered name.
- A series is identified by its AniList entry. When a series already matched to the requested AniList id exists, the new volume joins that series instead of creating one series per release. It uses the series' own folder when that folder is inside the Manga library.
- Without a Manga library folder, the download is read where the download client put it, as before. It still joins the matched series.
- `/data/manga-cache` stays a disposable page cache. It is not the library.

### Light Novel downloads

`NovelEpubImportService.ImportDownloadAsync` finds EPUBs at any depth and ignores folder names; the series comes from each EPUB's metadata. Every EPUB is parsed first. A package with EPUBs of several series, or with no usable EPUB, is refused before anything is stored, and Wanted tries the next release. The inbox import (`ImportInboxAsync`) keeps its folder-name series hints.

An approved Syosetu (`ncode`) request is imported directly with `NovelImportService` and never searched on Usenet.

## Release safety

Reading acquisition is Usenet-only. Release identity is checked before ranking. Tried release identities are persisted so a failed or unsuitable NZB is not submitted again. Manga matching understands volume/chapter/range and CBZ/ZIP; Light Novel matching understands volume, language and EPUB. Unsupported formats and known wrong title/volume/chapter/language candidates are rejected before submission.

Public Syosetu/Ncode imports are separate legal web-source imports and do not bypass this Usenet lifecycle for published Light Novel acquisitions.

A Light Novel request keeps the author in `Subtitle` and the native title as a search alias in the payload, so a native title is never used as an author in queries.

## Download clients

The SABnzbd monitor polls each download client on its own. An unreachable client only delays its own downloads. Downloads pinned to a disabled client stay active, and the warning about them is logged at most every 15 minutes. Downloads pinned to a removed client fail, so their request can move on.
