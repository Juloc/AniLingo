# Profile / Activity — Clean Design

Status: planning baseline for mockups.

## Purpose
Personal identity, profile switching where allowed, consumption history, devices/sessions and links to Settings/Admin.

## Page structure
Profile summary -> Continue/Activity history -> Watch/Read/Listen history filters -> Devices/Sessions -> Settings/Admin links according to capability.

## Data / information
Profile identity, canonical Playback/Reading history, Work/unit progress, active/recent devices and session metadata.

## Actions
Switch profile, resume media, open history item, remove personal history item where supported, manage own session/device, open Settings.

## Light / Dark
Both first-class; activity is chronological and compact rather than analytics-heavy.

## Platforms
Desktop/tablet: grouped history and device panels. Mobile: Profile is navigation hub; stacked activity cards. TV: profile switch, recent activity and device-safe account actions only.

## States
New profile/no history, loading, active session, stale/offline device, partial history, forbidden admin link, error.

## Must not implement
No Admin operations/jobs mixed into personal Activity, no separate per-media history stores in UI, no exposure of other users' sessions without permission, no dashboard statistics clutter.