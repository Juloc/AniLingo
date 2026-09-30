# Admin Backup / Restore — V1

Status: planning baseline for mockups.

## Purpose
Create and restore versioned Jularr application-state backups while excluding canonical media payload files.

## Page structure
Backup status/history -> Create Backup -> backup detail -> Restore wizard: select -> validate -> preview -> confirm -> execute -> validation report.

## Data / information
Backup version, created time, app/schema version, included domains/config, size, secret policy, validation result and restore compatibility.

## Actions
Create/download/export where supported, validate, restore, cancel before mutation, inspect report.

## Light / Dark
Both first-class Admin surfaces.

## Platforms
Desktop primary; tablet/mobile supports essential wizard/reports. TV unsupported.

## States
No backups, creating, ready, validation warning/failure, incompatible version, restoring, restore failed/rolled back, success.

## Must not implement
No media-file backup masquerading as app backup, no plaintext secret exposure, no restore without preflight validation/explicit confirmation, no partial silent restore, no dependence on legacy tables as permanent format.