# Admin Provider Settings — V1

Status: planning baseline for mockups.

## Purpose
Common configuration pattern for Metadata, Discovery, Subtitle, Translation and other capability-specific providers.

## Page structure
Provider-family switch -> provider list/cards -> provider detail/config -> capabilities/priority -> health/test.

## Data / information
Stable provider key, enabled state, capabilities, priority/order, configuration schema, health/latency/rate-limit state and last test. Secrets are masked/write-only.

## Actions
Enable/disable, configure, test, reorder where meaningful, inspect health and capability support.

## Light / Dark
Both first-class Admin surfaces.

## Platforms
Desktop primary; tablet/mobile stacked provider cards/forms. TV unsupported.

## States
Unconfigured, healthy, degraded, rate limited, auth failure, unavailable, test running/success/failure, invalid config.

## Must not implement
No one giant universal provider interface, no provider-specific page design for every adapter, no secrets returned to normal DTO/UI, no provider ID as canonical Work identity, no uncontrolled external calls during page render.