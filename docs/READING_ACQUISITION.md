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
- changes a successful download to the real `Importing` state;
- resumes `Importing` after restart;
- keeps infrastructure/storage failures in `Importing` instead of grabbing a duplicate release;
- returns an unsuitable downloaded package to Wanted so another release can be tried.

## Completed downloads

`CompletedDownloadLocationResolver` resolves the exact originating download client recorded on the Operation, reads the completed storage path, and applies the existing canonical acquisition remote-path mapping.

`CompletedDownloadDispatcher` is the shared dispatch boundary. Media-specific adapters normalize the files but do not own download polling or retry scheduling:

- Manga -> `MangaCompletedDownloadImportAdapter` -> existing `MangaImportService`
- Light Novel -> `LightNovelCompletedDownloadImportAdapter` -> existing `NovelEpubImportService`

After import, the adapter reconciles AniList metadata when applicable and returns the local library URL. Importers remain idempotent, so a restart during `Importing` can safely retry the same completed path.

## Release safety

Reading acquisition is Usenet-only. Release identity is checked before ranking. Tried release identities are persisted so a failed or unsuitable NZB is not submitted again. Manga matching understands volume/chapter/range and CBZ/ZIP; Light Novel matching understands volume, language and EPUB. Unsupported formats and known wrong title/volume/chapter/language candidates are rejected before submission.

Public Syosetu/Ncode imports are separate legal web-source imports and do not bypass this Usenet lifecycle for published Light Novel acquisitions.
