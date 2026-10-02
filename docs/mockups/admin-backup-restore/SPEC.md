# Admin Backup / Restore — V1

Status: approved planning direction; current Backup & Restore mockup is the visual baseline once uploaded to this folder.

Global UX rules: `docs/UX.md`.

If an image and this specification conflict, this specification wins.

## Purpose

Backup & Restore manages versioned backups of Jularr application state and safely restores them.

Backups cover application/database/configuration state, not canonical media payload files.

Primary goals:
- reliable scheduled backups
- explicit manual backups
- compatibility-aware restore
- pre-restore safety backup
- validation before and after restore
- clear reporting

## Main surfaces

1. **Backup & Restore — Übersicht**
2. **Backup erstellen**
3. **Backup Einstellungen**
4. **Wiederherstellen — Backup auswählen**
5. **Wiederherstellen — Details prüfen**
6. **Wiederherstellen — Fortschritt & Ergebnis**

---

## 1. Backup & Restore — Übersicht

Show compact summary:
- last successful backup
- latest backup size
- current app version
- overall backup status

Main table:
- date/time
- type: automatic/manual
- app version
- size
- included content
- validation/status
- actions

Actions:
- create backup
- start restore
- download/export where supported
- validate
- inspect
- delete with confirmation

Do not show media-file size as if media itself is backed up.

## 2. Backup erstellen

Use a short wizard.

Suggested steps:
1. Optionen
2. Zusammenfassung
3. Erstellen
4. Fertig

### Backup content

Selectable domains:
- database/application state
- configuration
- provider settings
- users/groups/permissions
- other supported configuration/state

Secrets are separate and explicit.

### Secrets policy

Default:
- secrets excluded unless explicitly enabled

If included:
- backup encryption must be required
- UI must explain that credentials/API keys are part of the archive
- secrets are never shown in clear text

### Options

May include:
- name
- description
- backup storage target
- compression
- encryption
- optional validation after creation

Backup target is selected through a configured Storage role/path, not arbitrary unrestricted filesystem entry.

### Summary

Before creation show:
- included domains
- excluded domains
- secret policy
- encryption
- destination
- estimated size if available
- app/schema version

Creation runs as an operational job and may appear in Activity.

## 3. Backup Einstellungen

### Automatic backups

Settings:
- enabled
- schedule/frequency
- execution time
- retention count/days
- backup target
- compression
- encryption
- notify on failure
- optional automatic validation

### Retention

Support policies such as:
- keep last N backups
- keep N days
- optional mixed daily/weekly/monthly policy later

Deletion due to retention must only affect backups managed by this policy.

### Storage

Backup target references Storage.

Required checks:
- writable
- enough free space where measurable
- available
- not the same disposable workspace used for transient download data unless explicitly allowed

## 4. Restore — Backup auswählen

Start restore through an explicit wizard.

Suggested steps:
1. Backup wählen
2. Details prüfen
3. Warnungen
4. Wiederherstellen
5. Fertig

Selection table:
- date/time
- type
- version
- size
- content
- validation status

Only one backup selected.

External/imported backup files may be supported later, but must pass the exact same validation pipeline.

## 5. Restore — Details prüfen

Show:
- backup metadata
- source version
- schema/data version
- size
- included content
- excluded content
- encryption status
- validation result
- compatibility result

### Compatibility

Possible states:
- compatible
- compatible with migration
- warning
- incompatible
- corrupt/invalid

Restore cannot continue for hard-incompatible/corrupt backups.

### Pre-restore actions

Default safe behavior:
- create automatic pre-restore backup
- stop/quiet affected application services/jobs
- validate destination/storage
- restore database/configuration
- run required schema migration
- validate restored state
- restart normal operation

If pre-restore backup cannot be created, block by default unless an explicitly supported emergency mode exists.

## Warnings step

Warnings are explicit and require acknowledgement.

Examples:
- restoring older app/schema state
- current settings/users will be replaced
- secrets not present in backup
- provider credentials must be re-entered
- current active jobs may be cancelled
- migration required
- backup created by newer incompatible build

Do not hide warnings in logs.

## 6. Restore — Fortschritt & Ergebnis

Progress shows ordered phases:
- pre-restore backup
- application quiesce/stop
- database restore
- configuration restore
- schema/data migration
- integrity validation
- service resume

Show:
- current phase
- progress
- elapsed time
- concise live log
- success/failure state

### Result report

On completion provide:
- restored backup version/date
- migration performed
- validation result
- warnings
- failed/skipped components
- pre-restore backup reference
- next action if manual repair is required

A partial restore failure must be explicit.

If rollback is supported, report whether rollback succeeded.

## Backup format

Backup must be versioned.

It should contain a manifest with:
- backup format version
- Jularr app version
- schema/data version
- creation timestamp
- included domains
- secret/encryption policy
- checksums
- optional migration metadata

Legacy database/table layout is never the permanent external backup contract.

## Media payload policy

Normal Jularr application backup does **not** copy:
- movies
- episodes
- manga pages
- book files
- audiobooks
- generic download payloads

It may preserve references/metadata required to reconcile those files after restore.

Media filesystem backup is a separate storage/backup-system concern.

## Validation

Validation can include:
- archive integrity
- manifest checksum
- supported format version
- database dump integrity
- expected files present
- encryption/decryption check
- schema compatibility

Validation result is stored/displayed with the backup where possible.

## Security

- no plaintext secret display
- encrypted backups use authenticated encryption
- secret inclusion is explicit
- restore requires appropriate capability
- downloaded/exported backups are treated as sensitive files
- logs must not contain secrets

## Permissions

Separate capabilities may include:
- view backups
- create backup
- download/export backup
- delete backup
- restore backup
- change backup schedule/settings

Restore is the highest-risk action and requires explicit authorization.

## Light / Dark

Both first-class.

Use compact operational Admin styling:
- restrained semantic colors
- no decorative art
- progress and warnings readable without color alone

## Platforms

### Desktop
Primary platform for management and restore.

### Tablet
Supported with stacked settings and wizard sections.

### Mobile
Allow:
- view backup status/history
- create backup
- inspect restore state

Complex restore confirmation may remain available but should use full-screen wizard layouts.

### TV
Unsupported.

## Loading / Empty / Error / Partial states

Required:
- no backups
- backup creating
- backup successful
- backup failed
- validation running
- valid
- warning
- corrupt
- incompatible
- storage unavailable
- insufficient space
- encryption key unavailable
- restore preparing
- pre-restore backup failed
- restore running
- migration running
- validation after restore
- restore failed
- rollback succeeded/failed where supported
- restore successful
- permission denied

## Architecture constraints

- Backup/Restore owns application-state backup orchestration, not media storage backup.
- Storage owns backup destination paths and safe file operations.
- Database backup/restore must preserve transactional integrity.
- Restore may invoke Migration for schema/data upgrades.
- Backup format is versioned and migration-aware.
- Activity may expose operational job progress but does not own restore semantics.
- Secrets follow central secret-storage policy.

## Must not implement

- No media-file backup masquerading as application backup.
- No unrestricted destination path input.
- No plaintext secrets.
- No restore without preflight validation.
- No silent partial restore.
- No restore without explicit confirmation.
- No destructive restore without pre-restore backup by default.
- No permanent dependency on legacy table names/layout.
- No hidden compatibility migration.
- No deletion of backup history without explicit retention/delete semantics.
