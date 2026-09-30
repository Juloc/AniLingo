# Admin AI — V1

Status: planning baseline for mockups.

## Purpose
Admin control center for server AI providers/models, feature access policy, shared generated-content policy and AI jobs/usage.

## Page structure
Overview -> Server AI -> Image AI -> Models -> Features & Access -> Shared content policies/Chapter Images -> Usage & Jobs.

## Data / information
Provider/model health, task defaults, reasoning/options, limits/concurrency, feature policy, allowed users/groups/admins, personal-vs-server availability, shared/personal result policy, provenance and job status.

## Actions
Configure/test providers, choose task defaults, edit feature policy, manage publish/review rules, cancel/retry supported jobs, inspect usage/failures.

## Light / Dark
Both first-class Admin surfaces; policy matrices remain readable without color dependence.

## Platforms
Desktop primary; tablet/mobile use section navigation and stacked policy editors. TV unsupported.

## States
No provider, healthy/degraded, model discovery loading/failure, feature disabled, policy conflict/validation, job running/failed, quota/budget warning.

## Must not implement
No media identity ownership in AI, no silent personal->server fallback, no silent publishing/replacement of shared artifacts, no exposure of server credentials to users, no feature-specific duplicated permission system.