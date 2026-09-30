# Admin Migration Center — V1

Status: planning baseline for mockups.

## Purpose
Safe preview-first migration from Jularr legacy/per-type data and supported external systems into canonical Media Core/Library/Progress/Acquisition state.

## Page structure
Migration sources -> scan -> mapping/conflicts -> dry-run preview -> explicit confirmation -> execution progress -> validation/report.

## Data / information
Source IDs, proposed canonical Work/unit/Edition mappings, file/root mapping, provider identities, progress/history preservation, conflicts, skipped unsupported fields and validation counts.

## Actions
Connect/scan, map profiles/paths, resolve conflicts, run dry run, start migration, retry safe steps, export/view report.

## Light / Dark
Both first-class Admin surfaces.

## Platforms
Desktop primary; tablet/mobile can inspect status/reports but complex mapping is desktop-oriented. TV unsupported.

## States
Not started, scanning, conflicts, dry-run ready, running, paused/retryable, failed, completed with warnings, validated.

## Must not implement
No destructive big-bang migration, no title-only identity matching when stronger IDs exist, no source media deletion/move by default, no uncontrolled dual-write authority, no dropping bridges/tables before preservation validation passes.