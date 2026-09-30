# Admin Users & Permissions — V1

Status: planning baseline for mockups.

## Purpose
Manage accounts/profiles, groups/roles and effective capabilities that derive both API authorization and visible app shell.

## Page structure
User list -> user detail -> groups/roles -> capability matrix -> media/request/learning/AI policies -> devices/sessions.

## Data / information
Internal account/profile IDs, linked login identities, groups/roles, effective capabilities, media-type visibility, request/instant rights, profile restrictions and active sessions.

## Actions
Create/invite where supported, enable/disable, assign groups/roles, edit capabilities, revoke sessions, inspect effective permission explanation.

## Light / Dark
Both first-class Admin surfaces.

## Platforms
Desktop primary; tablet/mobile stacked user detail and grouped permission editors. TV unsupported.

## States
No users, pending/inactive, permission inherited/overridden, conflicting/invalid policy, active sessions, forbidden action, error.

## Must not implement
No scattered hard-coded `IsAdmin` assumptions, no UI-only authorization, no inferred permissions from hidden navigation, no exposure of another user's personal secrets, no separate AI permission model disconnected from Accounts capabilities.