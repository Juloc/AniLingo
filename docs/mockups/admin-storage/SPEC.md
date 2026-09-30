# Admin Storage + Safe Path Browser — V1

Status: planning baseline for mockups.

## Purpose
Manage LibraryRoots/storage health and select permitted server paths without exposing arbitrary filesystem access.

## Page structure
Storage roots -> capacity/health -> root detail -> wake/retry/configuration -> safe Path Browser -> optional usage/cleanup preview.

## Data / information
Root identity, permitted base path, online/offline, free/used, media distribution where cached, WOL/retry state, last check, filesystem capabilities and Jularr-owned cache/temp usage.

## Actions
Add/edit/test root, wake/retry, browse permitted directories, choose root/path, refresh health, preview safe cleanup of disposable Jularr data.

## Light / Dark
Both first-class Admin surfaces.

## Platforms
Desktop primary; tablet/mobile use stacked root cards and fullscreen browser. TV unsupported.

## States
Loading, online, sleeping/offline, permission denied, nearly full, unavailable mount, empty permitted directory, stale cached metrics, error.

## Must not implement
No unrestricted `/` browser, no user-controlled traversal, no media identity stored as path, no automatic canonical-media deletion, no NAS wake solely for passive analytics unless explicitly requested.