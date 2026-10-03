# Admin Users & Permissions — V1

Status: planning baseline for mockups.

## Purpose
Manage accounts/profiles, groups/roles and effective capabilities that derive both API authorization and visible app shell.

## Page structure
User list -> user detail -> groups/roles -> capability matrix -> media/request/learning/AI policies -> login identities/profile policy -> devices/sessions.

## Data / information
Internal Account/Profile IDs, linked Login identities, Profile ownership, groups/roles, effective capabilities, media-type visibility, request/instant rights, profile restrictions and active sessions.

Account and Profile are distinct:
- Account owns authentication/security/roles.
- Profile owns personal media state/preferences.
- External provider IDs never replace the internal Account/Profile IDs.

## Actions
Create/invite where supported, enable/disable, assign groups/roles, edit capabilities, manage allowed Profiles/profile limits, inspect/reset Profile PIN where authorized, inspect/revoke linked Login identities safely, revoke sessions, inspect effective permission explanation.

Instance-wide Login-provider enablement/configuration belongs to provider/auth settings; this page manages which identities belong to a specific Account and the resulting user/profile policy.

## Light / Dark
Both first-class Admin surfaces.

## Platforms
Desktop primary; tablet/mobile stacked user detail and grouped permission editors. TV unsupported.

## States
No users, pending/inactive, permission inherited/overridden, conflicting/invalid policy, active sessions, forbidden action, error.

## Must not implement
No scattered hard-coded `IsAdmin` assumptions, no UI-only authorization, no inferred permissions from hidden navigation, no exposure of another user's personal secrets, no separate AI permission model disconnected from Accounts capabilities.