# Add / Request Flow — Clean Design

Status: planning baseline for mockups.

## Purpose
One contextual flow from Discover/Library/detail into canonical Work resolution, request/monitoring and acquisition intent.

## Page structure
Identify Work -> choose desired Edition/language/scope where relevant -> choose allowed request/monitoring behavior -> optional profile override -> review/confirm -> status.

## Data / information
Resolved canonical Work/target, existing library state, allowed capabilities, desired language/edition/version intent, effective acquisition profile and duplicate/request state.

## Actions
Add/monitor, Request, choose scope, confirm, cancel, open resulting status/media.

## Light / Dark
Both first-class; use dialog/sheet/fullscreen flow based on platform.

## Platforms
Desktop: focused dialog/side sheet. Mobile/tablet: bottom/fullscreen sheet. TV: simplified request/add flow with remote-safe choices.

## States
Resolving identity, already available, already requested, permission-limited, no eligible profile/language, provider unavailable, submitting, success, error.

## Must not implement
No direct provider record as permanent identity, no raw indexer/release table, no arbitrary filesystem import in consumer flow, no duplicate Add forms per media type, no silent profile/default mutation.