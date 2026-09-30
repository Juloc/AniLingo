# Calendar — Clean Design

Status: planning baseline for mockups.

## Purpose
Unified release/airing calendar over canonical Works and structural units.

## Page structure
Header/date navigation -> view switch -> filters -> calendar/agenda -> selected-item preview.

## Data / information
Canonical Work/unit, release/air date/time/timezone, media type, local/request/availability state and preferred-language relevance where known.

## Actions
Change date/view, filter media/library status, open canonical detail/unit, request/add where permitted.

## Light / Dark
Both first-class; state is not color-only.

## Platforms
Desktop: month/week plus agenda detail. Mobile: agenda/list default with optional compact calendar. Tablet: adaptive split. TV: simplified upcoming rail/list with strong focus; dense month grid is not required.

## States
Loading, no events, partial provider dates, unknown timezone/time, provider unavailable, offline cached data, error.

## Must not implement
No provider-native duplicate Anime entries, no second release identity model, no admin acquisition queue on user Calendar, no cramped mobile month grid as the only view.