# Admin Imports — V1

Status: planning baseline for mockups.

## Purpose
Queue/history and safe resolution for ImportJobs between completed downloads/local sources and canonical Version/Asset/File state.

## Page structure
State tabs -> filters/search -> import queue -> import detail/match drawer -> history link.

## Data / information
Source download/path, detected Work/unit/Edition, confidence, destination LibraryRoot, import mode, state/progress, conflicts, failure reason and resulting Version/Asset/File IDs after success.

## Actions
Retry/cancel where safe, inspect, choose safe canonical match for ambiguous import, preview destination, approve import, open resulting media/history.

## Light / Dark
Both first-class; operational Admin styling.

## Platforms
Desktop table/detail drawer; mobile/tablet cards and fullscreen detail. TV unsupported.

## States
Queued, identifying, ambiguous/needs attention, importing, completed, failed, storage offline, source missing, conflict, unauthorized.

## Must not implement
No direct editing/typing of database IDs, no arbitrary destination path entry, no separate media-type import queues, no destructive source changes without explicit semantics, no Work identity derived from filename alone.