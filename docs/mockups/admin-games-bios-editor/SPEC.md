# Admin Games BIOS/Firmware Add or Replace — Planning Scaffold

Status: scaffold; detailed UX pending review.

Parent page: `docs/mockups/admin-games-bios/SPEC.md`.

## Purpose

Focused dialog/sheet for supplying or replacing one BIOS/firmware file required by a configured runtime/platform.

## Initial flow

Plan for:
- selected BIOS requirement identity
- upload/select through permitted mechanism
- filename/size
- checksum validation where known
- compatibility result
- replace confirmation when an existing file is present
- final save into restricted BIOS storage

## Rules

- no automatic internet acquisition
- no arbitrary host path text entry
- no cleartext secrets involved
- reject or warn on checksum mismatch according to runtime definition

## Open for dialog review

Decide:
- upload vs safe-path selection options
- mismatch handling
- replacement confirmation
- successful validation state
