# AniLingo localization architecture

AniLingo uses one central UI translation catalog. UI language is independent from content language and from Learning courses.

This document is durable architecture guidance. GitHub Issues remain the backlog and source of task state; do not create a separate AGENDA.md or localization backlog file.

## Core rule

Normal page rendering never calls AI.

English source resources are defined in code with stable semantic keys. Missing or outdated translations are generated only by an explicit Owner action, persisted in SQLite, and reused afterwards.

## Resource metadata

Every translatable resource must carry enough context for a translator or AI model to understand its purpose:

- stable semantic key such as player.repeatLine
- English source text
- feature/domain
- UI surface such as Button, Heading, Tooltip, Status, Error or Confirmation
- semantic description of what the UI element actually does
- tone/register guidance
- optional maximum-length guidance
- placeholder names plus the meaning of every placeholder
- terminology/do-not-translate constraints where required
- deterministic source hash derived from the complete translation context

A source/context change changes the hash. Generated or reviewed translations from the previous hash become Outdated. Manual translations remain manual and are never silently overwritten.

Do not use an English sentence as the resource key. Do not send isolated strings to AI without the metadata above.

## Translation lifecycle

Statuses are Missing, Generated, Reviewed, Manual and Outdated.

The Owner can manage locales at /Admin/Languages, generate Missing entries, regenerate Outdated entries, edit any translation manually, and mark Generated entries Reviewed.

AI output is validated before persistence. Required placeholders and protected terms must remain intact.

## Locale model

Locale identifiers are BCP-47/culture tags such as de, de-CH, id, ro, ja, pt-BR or ar.

Fallback is deterministic:

1. exact locale
2. parent culture/language
3. English source text

Example: de-CH → de → en.

Locale metadata includes native/English names and text direction. UI/CSS work must not assume left-to-right layout so RTL locales can be supported.

Dates, numbers and regional formatting belong to culture/locale formatting rather than AI translation.

## Catalog ownership

UiTranslationResources is the canonical source-resource catalog.

UiTranslationCatalogStore persists locale registrations and generated/reviewed/manual translations. It also synchronizes source metadata into SQLite so Admin UI can inspect the exact context sent to AI.

CodexCliProvider.GenerateUiTranslationsAsync is the first translation generator. The catalog contract is provider-independent; a later provider must consume the same contextual request and produce the same keyed result contract.

## Adding a resource

When adding visible fixed UI copy:

1. create a semantic key in the appropriate feature namespace;
2. provide source text plus semantic context metadata;
3. include placeholder meanings and protected terminology;
4. consume the catalog key from UI code;
5. do not add a second localization store or runtime AI fallback.

Example:

Key: learning.reviewDue
Source: {count} reviews due
Feature: Learning
Surface: Status
Description: Shows how many spaced-repetition review cards are currently due.
Tone: compact status
Placeholder count: current due review count

## Relationship to Learning v2

Localization and learning languages are separate concepts.

A user may use AniLingo UI in German, Japanese → Indonesian as one Learning course, German → Indonesian as another Learning course, or no Learning system at all.

Learning capability visibility is resolved independently through the Learning v2 profile → media type → work/series → content inheritance hierarchy tracked in issue #226.