# Admin Games BIOS & Firmware — Planning Scaffold

Status: scaffold; detailed UX pending review.

Shared Admin contract: `docs/mockups/admin-games/SPEC.md`.

## Purpose

Show BIOS/firmware requirements and availability for configured game runtimes/platforms.

## Initial responsibilities

Plan for:
- required/optional BIOS entries
- platform/runtime association
- status: present / missing / invalid
- expected checksum/version evidence when provided by runtime definition
- configured restricted BIOS storage
- add/replace/remove actions
- validation/test

## Rules

- BIOS/firmware is not part of normal Games LibraryRoot content.
- Downloader does not automatically search/acquire BIOS.
- Runtime receives only BIOS files it explicitly requires.
- Do not expose unrestricted filesystem browsing.

## Boundaries

- no general ROM/game library management
- no acquisition search for BIOS
- no runtime executable configuration
- no metadata-provider settings

## Open for page review

Decide:
- grouping by platform/runtime
- status density
- missing requirement warnings
- storage presentation/deep-link
- add/replace flow
