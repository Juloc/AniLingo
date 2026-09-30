# Reader — Clean Design

Status: planning baseline for mockups.

## Purpose
Distraction-minimized reading of canonical written Assets with exact resumable position, edition/language switching and annotations.

## Page structure
Reading canvas -> transient top/bottom controls -> TOC/chapter navigation -> typography/theme panel -> edition/language panel -> annotations/learning sheets.

## Data / information
Work/Volume/Chapter, Edition/Version/Asset, stable locator plus fallback percentage, reading progress, bookmarks/highlights, available languages, official/generated translation provenance.

## Actions
Navigate chapters/pages, save/restore exact position automatically, typography/spacing/theme, TOC, bookmark/highlight/note, edition/language switch, request/translate, optional learning/TTS.

## Light / Dark
Reader themes are deliberate reading surfaces; Light/Dark/System plus reader-specific paper/background options may exist without changing app identity.

## Platforms
Desktop: centered readable column and compact side controls. Mobile: chrome hidden while reading; tap reveals controls; sheets for settings/TOC. Tablet: wider margins/two-pane where format supports it. TV: not a full reading target unless separately approved; handoff is preferred.

## States
Loading document, exact resume restored, first read, partial chapter/edition, translation queued/running, storage offline, malformed/unsupported document, recoverable render error, offline cached reading.

## Must not implement
No Novel-specific identity/progress store, no manual-save requirement for progress, no destructive translation overwrite, no giant persistent toolbars, no imported HTML without sanitization.