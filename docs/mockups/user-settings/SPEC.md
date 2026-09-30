# User Settings — Clean Design

Status: planning baseline for mockups.

## Purpose
Searchable personal settings separated from Admin/server configuration.

## Page structure
Settings search -> section navigation -> focused setting groups: Appearance; Language & Regional; Library/Display; Playback; Audio & Subtitles; Reader; Learning; AI/Personal Provider; Devices; Account/Security.

## Data / information
Profile-scoped preferences and effective policy/permission state. Secrets are masked/write-only.

## Actions
Search settings, change preferences, test personal provider where allowed, manage own devices/sessions, security/account actions.

## Light / Dark
Appearance includes System/Light/Dark and shared accent tokens; the page itself is complete in both themes.

## Platforms
Desktop: section rail + content. Mobile: searchable list -> section pages/sheets. Tablet: adaptive two-pane. TV: only TV-relevant playback/subtitle/account preferences; no dense server settings.

## States
Loading, saved/unsaved, validation error, policy-disabled option, provider test running/failure, offline, forbidden section.

## Must not implement
No one-page form wall, no server/provider admin credentials, no arbitrary raw configuration/YAML, no hard-coded IsAdmin UI logic, no duplicate setting ownership.