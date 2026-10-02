# Admin Users & Permissions — V1

Status: approved planning direction; current Users & Permissions mockup is the visual baseline once uploaded to this folder.

Global UX rules: `docs/UX.md`.

If an image and this specification conflict, this specification wins.

## Purpose

Manage Jularr accounts, groups/roles, effective capabilities, media/request policies and active sessions.

Authorization is capability-based. Roles/groups are reusable collections of policy; they are not hard-coded application branches.

## Main surfaces

1. **Benutzer — Übersicht**
2. **Benutzer — Detail**
3. **Gruppen & Rollen**
4. **Gruppe/Rolle — Detail**
5. **Berechtigungen — Matrix**
6. **Medienzugriff, Limits & Sessions**

These are one coherent Admin area, not separate unrelated products.

## 1. Benutzer — Übersicht

Desktop uses a dense table.

Summary may show:
- total users
- active
- locked
- inactive

Default columns:
- username
- display name
- groups/roles
- status
- last login
- created at
- actions

Actions:
- open detail
- edit
- enable/disable or lock/unlock where permitted
- more

Filters:
- search
- group/role
- status

No password, secrets or private tokens are ever shown.

## 2. Benutzer — Detail

Header shows:
- avatar
- display name
- username
- account status
- last login
- primary actions

Primary tabs:
- Allgemein
- Gruppen & Rollen
- Berechtigungen
- Medienzugriff
- Einstellungen
- Anfragen
- Learning
- AI
- Geräte & Sessions

Tabs may be hidden when the feature/module is unavailable.

### Allgemein

Fields may include:
- username
- display name
- e-mail
- locale/language
- timezone
- avatar
- account enabled
- account locked
- optional 2FA/security state
- password reset/admin recovery action
- created at
- last login

### Gruppen & Rollen

Show:
- assigned groups
- assigned roles where separate
- primary/default group if used
- inherited policy summary
- explicit user overrides

Adding/removing a group must immediately show resulting effective permission changes before save where feasible.

### Berechtigungen

Show effective capability state and provenance:
- Allowed
- Denied
- Inherited

Each capability should explain where its effective result comes from:
- direct user override
- group
- role
- global policy

No opaque boolean list.

### Medienzugriff

Possible rules:
- allowed media/content types
- allowed LibraryRoots / logical libraries
- age/content restrictions
- adult-content policy where supported
- language/profile restrictions where applicable
- optional per-profile visibility settings

Media visibility and actual API access must use the same effective policy.

### Anfragen

Possible controls:
- can request
- auto-approve
- daily/monthly request limits
- allowed media types
- allowed acquisition profiles
- max quality/language restrictions where policy requires

### Learning

Possible controls:
- Learning enabled
- course access
- review access
- AI-assisted learning access
- shared learning content access

### AI

AI-specific access integrates with Admin AI policy:
- personal AI allowed
- shared server AI allowed
- allowed AI task categories
- per-user/group limits

Do not duplicate a separate AI authorization system.

### Geräte & Sessions

Show active/recent sessions:
- device/app
- browser/platform
- IP/network information where policy permits
- created/login time
- last activity
- status

Actions:
- revoke one session
- revoke all sessions
- inspect device details where supported

## 3. Gruppen & Rollen

Use a table/list with:
- name
- description
- member count
- associated role/template
- status
- actions

Default examples may include:
- Admins
- PowerUser
- StandardUser
- Kinderprofil
- Gast

Examples are not hard-coded product roles.

Actions:
- add group
- edit
- duplicate/template where useful
- enable/disable
- manage members
- delete when safe

Deletion must be blocked or explicitly handled when users would lose unresolved required policy.

## 4. Gruppe/Rolle — Detail

Tabs may include:
- Allgemein
- Berechtigungen
- Benutzer
- Medienregeln
- Anfragen
- AI
- Einstellungen

### Permission editor

Group capabilities use explicit tri-state policy:
- Erlauben
- Verweigern
- Erben

Capability groups may include:
- Admin
- General
- Media
- Downloads
- Requests
- AI
- Storage/Administration
- System

Examples:
- Admin-Bereich öffnen
- Benutzer verwalten
- System Settings verwalten
- Medien anzeigen
- Medien verwalten
- Downloads starten
- Manual Search
- Requests erstellen
- Requests automatisch genehmigen
- AI benutzen
- Shared AI benutzen
- Storage verwalten
- Provider verwalten
- Downloader verwalten
- Backup/Restore
- Migration
- Logs/Diagnostics

Capability keys are stable application contracts, not display labels.

## 5. Berechtigungen — Matrix

Provide a read-oriented comparison matrix across major groups/roles.

Rows:
- capabilities

Columns:
- groups/roles

Cell states:
- allowed
- denied
- inherited/mixed where relevant

The matrix is for comparison and auditing.

Editing may open the selected group/role detail rather than forcing inline editing for hundreds of cells.

The matrix must remain understandable without color.

## 6. Medienzugriff, Limits & Sessions

This surface groups policy that is easier to review comparatively.

Sections may include:

### Allowed media/content types
- Anime
- Series
- Movies
- Manga
- Books
- Audiobooks
- Games
- Software
- other configured content types

### Libraries / roots
- allow all
- explicit allowed LibraryRoots/logical libraries

### Age/content policy
- max age rating
- explicit adult-content policy
- configured content warnings/restrictions where supported

### Limits
- request limits
- AI limits
- concurrent streams/download permissions where policy owns them
- daily/monthly quotas

### Sessions
- active sessions
- revoke actions

## Effective authorization model

Effective capability evaluation must be deterministic.

Recommended precedence:
1. explicit security/system prohibition
2. direct user deny
3. direct user allow
4. group/role policy evaluation
5. instance default

If group conflicts exist, deny should win unless a documented application rule says otherwise.

The UI must be able to explain the effective result.

Do not make navigation visibility the source of authorization truth.

## Role vs Group

If both concepts exist:

- **Role** = reusable capability/policy template
- **Group** = collection of users that can reference a role and add group-specific policy

If this distinction provides no real value in implementation, V1 may use Groups with capability policy and reserve Role as a template concept.

Do not create two nearly identical authorization hierarchies.

## Account lifecycle

States may include:
- active
- inactive
- locked
- pending/invited where supported

Actions must be explicit.

Disabling a user:
- blocks new authentication
- does not silently delete profile/history
- may optionally revoke active sessions with explicit confirmation

Deleting a user is separate from disabling and requires a clear data-retention policy.

## User creation

If local/invite creation is supported, wizard/form may collect:
- username
- display name
- email
- initial group(s)
- locale/timezone
- activation method

Do not require setting every permission manually during creation.

## Security

- no plaintext passwords
- no session tokens
- no API keys
- no personal AI secrets
- password reset is an action, not password display
- destructive actions require explicit confirmation
- server enforces every capability

## Light / Dark

Both first-class.

Admin visual language:
- compact
- table-oriented
- restrained semantic colors
- status always includes icon/text
- no decorative media artwork

## Platforms

### Desktop
Primary platform for list, detail, matrix and policy editing.

### Tablet
Supported with stacked panels and horizontally scrollable comparison tables where necessary.

### Mobile
Supports:
- user list
- user detail
- simple policy edits
- session revoke

Large capability matrices become grouped lists rather than squeezed desktop tables.

### TV
Unsupported.

## Loading / Empty / Error / Partial states

Required:
- no users
- no groups
- user active/inactive/locked
- no active sessions
- inherited permission
- direct override
- conflicting group policies
- forbidden action
- stale session
- user removed while editing
- save validation failure
- permission change causes own-admin-access warning
- last-admin protection where applicable

## Architecture constraints

- Accounts owns authentication/profile/authorization policy.
- Capabilities are stable application contracts.
- UI visibility derives from effective capability policy.
- API/application services enforce the same policy.
- AI-specific access integrates with Accounts + AI policy.
- Media/request/learning rules reference canonical modules and do not create user-specific parallel media identity.
- Sessions/devices belong to Accounts/Devices infrastructure.

## Must not implement

- No scattered hard-coded `IsAdmin` checks.
- No UI-only authorization.
- No hidden-nav-as-security.
- No duplicate AI permission system.
- No separate permission model per media type.
- No plaintext secrets/passwords/tokens.
- No permission changes without effective-result explanation.
- No automatic user deletion when disabling an account.
- No role/group duplication that provides no distinct semantics.
- No TV Admin permission UI.
