# Language / Edition Selector — Shared Consumer Surface

Status: **binding planning specification; mockup optional unless implementation deviates from standard Jularr components**.

This is a reusable consumer dialog/sheet for selecting a user-facing language and Edition of a canonical Work.

It is not a standalone navigation destination and it is not a technical Version/File selector.

## 1. Purpose

The selector answers:

**Which readable/playable/listenable presentation of this Work do I want to use?**

Examples:
- original Japanese Light Novel;
- official German Book translation;
- generated English translation;
- English Manga edition;
- German Audiobook edition;
- alternate user-facing Movie cut where Jularr actually models one.

The selector must keep the canonical distinction:

`Work -> Edition -> Version -> Asset/File`

Normal users primarily select **Edition**.

Technical Version/File choice remains automatic unless a Version represents a deliberate user-facing distinction.

## 2. Where it is used

Reuse the same surface from:
- Reading Detail;
- Reader;
- Audiobook Detail / listening surface;
- Movie Detail only when meaningful Editions/cuts exist;
- other media Detail pages where multiple user-facing Editions exist.

Request uses the same language/edition concepts, but its controls remain embedded in the Request dialog.

Player audio/subtitle Track selection is **not** this selector.

## 3. When to show it

Show the selector only when there is something meaningful to choose.

Do not show it when:
- exactly one usable Edition exists;
- alternatives differ only by technical encoding/quality;
- only Files/Tracks differ;
- the difference belongs to Player Audio/Subtitles instead.

If there is one active Edition plus unavailable/requestable alternatives, the selector may still appear when requesting another language/Edition is useful.

## 4. Surface type

Desktop:
- compact anchored popover for a small option set;
- medium dialog when many languages/Editions need browsing.

Mobile:
- bottom sheet or full-height sheet when the list is large.

Tablet:
- popover/dialog on wide layouts;
- sheet on narrow layouts.

TV:
- simple remote-safe list.

This must not become a full standalone Settings-style page.

## 5. Header

Show:
- title: `Language & Edition`;
- optional compact Work title when context could be ambiguous;
- Close/Back according to shell.

Do not repeat large artwork/detail-page hero content.

## 6. Primary hierarchy

The selector uses one clear hierarchy:

1. current selection
2. available languages
3. Editions inside the selected language
4. unavailable/requestable alternatives only when useful

Do not use independent controls that can contradict each other.

## 7. Language selection

Language is a presentation filter over Editions.

Show human-readable names:
- English
- German
- Japanese

Optional secondary native label may be shown where useful.

Do not make raw locale/language codes the primary text.

When many languages exist:
- use one compact language selector/search;
- avoid a giant chip wall.

Changing language updates the Edition list below.

## 8. Edition list

Each Edition row may show:

- Edition/display title when needed;
- language;
- publisher/label when useful;
- publication year where useful;
- format where useful: EPUB, Manga digital, Audiobook, etc.;
- narrator for Audiobooks where it materially distinguishes the Edition;
- `Official` or `Generated` provenance when relevant;
- availability state;
- current-selection indicator.

Keep rows compact.

Do not show:
- provider IDs;
- release group;
- filename;
- codec;
- resolution;
- hash;
- LibraryRoot;
- acquisition score.

## 9. Official vs generated

Generated/machine-translated Editions must never look identical to official Editions.

Use a restrained textual distinction such as:
- Official
- Generated translation

Do not use alarm styling merely because an Edition is generated.

Where provenance detail matters, a secondary info action may explain source/engine/version, but normal selection must stay simple.

An official Edition is never overwritten by a generated Edition.

## 10. Availability states

An Edition can be presented as:

- **Available**
- **Current**
- **Requested**
- **Downloading**
- **Preparing / Generating**
- **Unavailable**

Only show states that are actually known.

### Available
Selecting switches to that Edition.

### Requested / Downloading / Preparing
Show state; do not create another duplicate request.

### Unavailable
If the profile may Request it, expose a compact `Request` action.

That action opens the shared Request dialog prefilled with:
- current Work;
- target language;
- Edition intent where known.

Do not embed acquisition controls inside this selector.

## 11. Selection behavior

Selecting an available Edition should normally apply immediately.

No extra Save button is needed.

Close the popover/sheet after selection when the result is obvious.

For Reader:
- keep the same canonical Work/chapter context where possible;
- map to the equivalent structural target in the chosen Edition;
- restore the best valid locator;
- if exact locator mapping is unavailable, use a safe logical/fallback position;
- never mark content completed merely because Edition changed.

For Detail pages:
- update displayed availability/language/primary action;
- do not navigate to a duplicate Work.

For Audiobook:
- switching Edition may change narrator/language/runtime presentation;
- progress remains attached to canonical logical identity and maps safely to the selected playable structure where possible.

## 12. Remembering preference

The active selection is profile-scoped.

Jularr may remember:
- explicit preferred Edition per Work;
- last-used Edition per Work;
- profile preferred language as fallback.

Priority should be deterministic:

1. explicit per-Work user choice;
2. currently active valid Edition;
3. profile preferred language;
4. Work/default preferred Edition;
5. first usable fallback.

Do not silently rewrite the profile's global language preference when the user changes one Work.

## 13. Edition vs Version

Normal selector rows represent Edition.

A Version must only become user-selectable when it represents a real consumer-facing difference that cannot be modeled adequately as an Edition.

Examples that should **not** create visible choices by default:
- two equivalent 1080p files;
- different release groups;
- remux vs encode;
- identical EPUB replacements.

Quality/File/Track selection belongs to playback/reader resolution logic or Admin diagnostics.

## 14. Video rule

For normal Anime/TV playback:
- dubbed audio language belongs to Player Audio Track selection;
- subtitle language belongs to Player Subtitle Track selection.

Do not create fake video Editions merely to represent audio/subtitle Tracks.

A video Edition choice is only appropriate for genuinely distinct presentations such as an alternate cut/edition when the domain models it that way.

## 15. Reading rule

For Book / Light Novel / Manga the selector is especially important.

Useful distinctions include:
- source/original language;
- official translations;
- publisher Editions;
- generated translations;
- materially distinct publication formats where user-facing.

Reader should expose the same selector without leaving the reading session.

## 16. Audiobook rule

Useful distinctions may include:
- spoken language;
- narrator;
- abridged/unabridged;
- publisher/edition;
- official/generated where applicable.

Do not create a separate Audiobook language selector implementation.

## 17. Request integration

Unavailable language/Edition Request flow:

```text
Language / Edition Selector
→ Request
→ shared Request dialog prefilled
→ Request submitted / auto-approved
→ selector reflects Requested/Downloading/Available state
```

Do not duplicate Request state locally.

## 18. Loading / errors

Support:
- Editions loading;
- one Edition temporarily unavailable;
- Request state unavailable;
- switching failed;
- selected Edition removed/invalidated after metadata refresh.

Failures should stay local.

If the active Edition becomes invalid:
- keep the Work open;
- choose the safest valid fallback;
- tell the user only when the change is meaningful.

## 19. Desktop layout

Small set:
- current language at top;
- list of available Editions;
- optional `Other languages` row;
- compact Request action for unavailable alternatives.

Large set:
- language selector/search at top;
- Edition list below;
- scroll inside dialog;
- no tabs required.

## 20. Mobile layout

Use a bottom/full-height sheet:

- title;
- language selector;
- Edition rows;
- current checkmark;
- status/action;
- Close/Back.

Tap an available row to switch.

No tiny desktop dropdown inside a phone sheet.

## 21. Accessibility

- active Edition exposed as selected/current;
- provenance and availability have textual labels;
- keyboard/remote selection works;
- focus returns to the trigger after close;
- no information depends on flag icon/color alone;
- screen reader announces language, Edition name, provenance and state.

## 22. Shared components

Use shared:
- `LanguageSelector`
- `EditionList`
- `EditionRow`
- `AvailabilityState`
- `ProvenanceLabel`
- Dialog/Popover/Sheet shell

The Request dialog and media-detail pages should consume the same language/Edition models rather than reimplementing conversion logic.

## 23. Must not implement

- no standalone top-level Language page;
- no duplicate Work per language;
- no provider-native identity as Edition identity;
- no raw File/Track selector;
- no release-group/codec/filename details;
- no separate Book/Manga/Audiobook language selector implementations;
- no direct acquisition configuration;
- no duplicate Request engine;
- no global profile-language mutation from one local switch;
- no progress completion caused by Edition switching;
- no treating audio/subtitle Tracks as fake video Editions.

## 24. Mockup policy

A dedicated mockup is **optional** because this surface should use standard Jularr list/select/sheet components.

Create a mockup only if:
- implementation becomes visually unclear;
- many languages/Editions create hierarchy problems;
- generated-vs-official presentation needs owner review;
- Mobile behavior differs materially from this specification.

Text specification is authoritative.
