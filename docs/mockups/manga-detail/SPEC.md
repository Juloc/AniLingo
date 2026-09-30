# Manga Detail — Clean Design

Status: planning baseline for mockups.

## Purpose
Canonical user detail for `Work(MediaType=Manga)` using Work -> Volume -> Chapter plus Edition/Version availability.

## Page structure
Hero -> Continue Reading -> Volumes/chapters -> Editions & Languages -> Related Works -> More Like This -> Details. Volumes are primary structure; chapter list is scoped to the selected volume and may be expandable.

## Data / information
Title/artwork/synopsis/status, progress, volume/chapter ordering, available editions/languages, official vs generated translation provenance, local/partial/request state and canonical relations.

## Actions
Read/Continue, choose volume/chapter, switch edition/language, Favorite/Collection, Request missing content, translate when policy permits.

## Light / Dark
Both first-class; artwork remains dominant, generated translations remain distinguishable without saturated badge clutter.

## Platforms
Desktop: volume rail + scoped chapter list. Mobile: swipe volume rail and sheet-based selectors. Tablet: touch-first adaptive two-pane. TV: browse/focus only; reading may hand off unless a TV Reader is separately approved.

## States
Loading, empty/no local edition, partial volumes, preferred language unavailable, requested/downloading, translating, storage offline, provider partial, error.

## Must not implement
No parallel MangaSeries/MangaChapter identity, no giant all-series chapter table by default, no admin release/file internals, no treating machine translation as official, no unnecessary tabs.