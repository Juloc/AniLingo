# Admin Storage + Safe Path Browser — V1

Status: planning baseline updated for Jularr's native downloader.

Global UX rules: `docs/UX.md`.

## Purpose

Storage manages **physical mounts** and the **logical storage roles** Jularr places on them.

It answers:
- Which filesystems/mounts are available?
- How much physical capacity is actually free?
- Which LibraryRoots live on each mount?
- Where does Jularr's native downloader keep incomplete/completed/work files?
- Where do generic/unclassified downloads end up?
- Which paths may Jularr safely browse/write?

Do not model every LibraryRoot as if it were its own physical disk.

## Storage model

### 1. Physical Mount

Examples:
- `/media` -> `/dev/sdb1` -> ext4 -> 8 TB
- `/nas/media` -> NFS -> 16 TB
- `/fast` -> SSD -> 2 TB

Capacity, free space, filesystem health and mount availability belong to the Mount.

If Anime, Movies and Downloads are all under `/media`, capacity is shown once for `/media`.

### 2. Storage Role / Root

A path inside an allowed Mount can be assigned one or more Jularr roles.

Required role families:

#### LibraryRoot
Final canonical library destination.

Examples:
- Anime -> `/media/anime`
- Series -> `/media/series`
- Movies -> `/media/movies`
- Manga -> `/media/manga`
- Books -> `/media/books`
- Audiobooks -> `/media/audiobooks`

A LibraryRoot may support one or multiple media/content types.

#### Native Download Workspace
Working storage for Jularr's built-in Usenet downloader.

Example:
- `/media/.jularr/downloads`

Jularr manages internal subdirectories beneath the workspace, such as:
- incomplete/article assembly
- completed/staging
- verification/repair
- extraction/post-processing

The normal UI configures the workspace root, not every internal subdirectory separately.

Advanced configuration may allow the heavy repair/extract workspace to live on a different permitted Mount, e.g. a fast SSD.

#### Generic Downloads Root
Final destination for downloads that do not map to a specialized library.

Examples:
- games
- software/installers
- archives
- datasets
- other generic content

Example path:
- `/media/downloads`

This is different from the temporary Native Download Workspace.

#### Other managed roles
Future/optional roles may include:
- backup target
- cache/temp
- transcode workspace

These must remain explicit roles rather than being confused with LibraryRoots.

## Native downloader storage flow

Target flow:

```text
NZB / Usenet
  -> Native Download Workspace
     -> incomplete/article assembly
     -> verify/repair
     -> extract/post-process
  -> identify
  -> import
     -> specialized LibraryRoot
     OR
     -> Generic Downloads Root
```

Storage owns the physical paths and safe file operations.

The Usenet/downloader module owns queueing, NNTP transport, verification/extraction orchestration and download state.

Acquisition owns why the item is wanted and the transition into import.

## Same-mount optimization

The UI should make it visible when:
- download workspace and target LibraryRoot are on the same filesystem
- target is on a different filesystem/mount

Same-filesystem placement may allow cheaper/safer move semantics.

Cross-filesystem import may require copy + verify + delete semantics.

The UI should not expose implementation details as mandatory user choices, but should surface warnings when a selected layout causes expensive copies or insufficient temporary space.

## Page structure

Desktop:

1. Mount list
2. Selected Mount detail
3. capacity/health
4. roles/roots on this Mount
5. add/edit role/root
6. safe Path Browser

The selected Mount should show a single table of paths/roles rather than duplicating physical capacity per role.

Recommended role table columns:
- Name
- Role
- Path
- Content types / purpose
- Used size where known/cached
- Last check/scan
- Status
- Actions

## Mount data

Show:
- mount point
- device/share identity
- filesystem/protocol
- online/offline/sleeping
- read/write capability
- total/used/free capacity
- reserved space where configured
- last health check
- optional wake/retry state
- filesystem capabilities useful for safe file operations

Do not imply separate free-space numbers for child LibraryRoots.

## LibraryRoot configuration

Allow:
- name
- path
- supported content/media types
- enabled state
- scan/reconciliation policy
- naming/organization policy reference
- write/import capability

A content type may have a default LibraryRoot and future routing rules may select alternatives where configured.

## Native Download Workspace configuration

Allow:
- workspace path
- enabled/healthy state
- minimum free-space/reserve policy
- optional maximum workspace quota
- optional separate repair/extract workspace
- cleanup retention for completed temporary data
- test write/delete capability

Do not configure NNTP servers, connection counts, bandwidth or queue policy here. Those belong to downloader/Usenet settings.

## Generic Downloads configuration

Allow:
- final Generic Downloads Root path
- organization/naming behavior for generic content
- optional category subfolders
- scan/index behavior where supported

Generic Downloads is a final destination, not the native downloader's staging folder.

## External download clients

External download clients remain optional compatibility/migration adapters.

Their own local/remote download paths and remote-path mappings belong to their client configuration.

Storage may validate that a resolved external-client path maps to a permitted Jularr path, but external clients must not define the native Storage model.

## Safe Path Browser

The browser is server-side and restricted to configured/permitted Mount boundaries.

Behavior:
- user selects a Mount first
- browser starts inside that Mount
- user can navigate only permitted descendants
- current path is always visible
- show writable/readable state
- prevent traversal outside the permitted root
- allow creating a directory only when policy permits

No unrestricted filesystem root browser.

No arbitrary path text field as the primary UX.

## Actions

Mount:
- refresh/test
- wake/retry where supported
- inspect health

Role/root:
- add
- edit
- disable
- test read/write
- browse/select path
- scan/reconcile LibraryRoot
- remove role mapping without silently deleting media

Workspace:
- test
- cleanup disposable completed/temp data with preview
- move workspace only through an explicit migration operation

## Light / Dark

Both first-class Admin surfaces.

Use compact operational styling:
- no decorative hero art
- restrained status colors
- capacity bars belong to physical Mounts
- child roles use simple table/list rows

## Platforms

Desktop primary.

Tablet:
- Mount cards/list + selected detail

Mobile:
- stacked Mounts and role lists
- Path Browser as full-screen sheet
- advanced bulk storage management remains desktop-oriented

TV unsupported.

## States

Required:
- loading
- online
- sleeping/offline
- read-only
- permission denied
- nearly full
- insufficient workspace free space
- unavailable mount
- stale metrics
- workspace unhealthy
- LibraryRoot unavailable
- cross-filesystem import warning
- empty Mount/no roles
- path no longer exists
- test running/failure
- forbidden

## Domain / architecture constraints

- Storage owns physical placement, paths, capacity and safe file operations.
- LibraryRoot belongs to Storage infrastructure and is referenced by Library/import workflows.
- Native Download Workspace is temporary operational storage, not canonical media identity.
- Generic Downloads is a final content destination, not a staging area.
- Paths must never become canonical Work identity.
- The native downloader must not require SABnzbd/NZBGet for normal operation.
- External download clients are optional adapters only.

## Must not implement

- No separate physical-capacity card for every LibraryRoot on the same mount.
- No assumption that each content type requires its own filesystem.
- No use of a LibraryRoot as the native downloader's incomplete/staging workspace implicitly.
- No mixing temporary download workspace with final Generic Downloads.
- No NNTP/server/bandwidth settings on the Storage page.
- No unrestricted `/` browser.
- No user-controlled traversal outside permitted Mounts.
- No arbitrary path entry as the primary path UX.
- No media identity stored as path.
- No automatic canonical-media deletion from removing a Storage role.
- No destructive cleanup without explicit preview/semantics.
- No NAS wake solely for passive analytics unless explicitly requested.
