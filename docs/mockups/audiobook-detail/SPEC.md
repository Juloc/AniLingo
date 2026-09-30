# Audiobook Detail — Clean Design

Status: planning baseline for mockups.

## Purpose
Canonical user detail for Audiobook works/editions without a separate audiobook core.

## Page structure
Hero -> Continue Listening -> Chapters -> Editions/Narration & Languages -> Related Book/Works -> More Like This -> Details.

## Data / information
Cover/title/author, narrator, spoken language, duration, chapter structure, progress/time remaining, Edition/Version availability, request state and canonical relations to written works.

## Actions
Listen/Continue, chapter jump, playback speed entry to Player, choose narration/edition, Favorite/Collection, Request missing language/edition.

## Light / Dark
Both first-class; calm media-first cards, compact spoken-language indicator.

## Platforms
Desktop/tablet: chapter list with current position. Mobile: touch-first stacked chapters. TV: remote-friendly browse and audio playback with strong focus; no dense metadata tables.

## States
Loading, no local audio, partial/multi-file edition, requested/downloading/importing, preferred language unavailable, missing chapters, storage offline, metadata partial, error.

## Must not implement
No `Audiobook`/`AudiobookFile` parallel source of truth, no raw file/release diagnostics in user UI, no per-audiobook progress store, no assumption that narrator/language variants are separate Works.