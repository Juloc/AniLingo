# Admin System / Diagnostics — V1

Status: planning baseline for mockups.

## Purpose
Detailed operational diagnostics and general system settings behind the live Dashboard, without turning Dashboard into a log console.

## Page structure
System overview -> health details -> logs/diagnostics -> media-tool capabilities -> job/runtime limits -> general server settings.

## Data / information
App/version, PostgreSQL health, filesystem/media-tool capability, structured logs with correlation IDs, provider/storage/job links, resource limits and safe environment diagnostics.

## Actions
Filter/search logs, copy/export sanitized diagnostics, test dependencies, open correlated Work/Job/Session, change supported system limits/settings.

## Light / Dark
Both first-class Admin surfaces.

## Platforms
Desktop primary; tablet/mobile essential diagnostics only. TV unsupported.

## States
Healthy, degraded dependency, disconnected live data, no logs matching filter, redacted secret fields, error.

## Must not implement
No secret values in logs/export, no arbitrary shell execution, no duplicate Dashboard metrics as decorative cards, no direct EF/database editor, no normal-user access.