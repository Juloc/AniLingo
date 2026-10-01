# Reader — Cross-platform Clean Design

Status: **binding planning specification for Reader mockups**. This document refines `UX.md`, `UNIFIED_READER.md`, the canonical Media Core and issue #662. Existing Reader behavior that already matches this contract should be preserved rather than redesigned.

Architecture source of truth:
- `Work -> Structure -> Edition -> Version -> Asset -> File`
- written structure uses canonical Volume/Chapter where applicable;
- canonical `MediaProgress` owns current item, exact resume locator and completed-through state;
- `ReaderPreference` remains the one durable reader preference store;
- Translation produces a derived Edition/Version and never overwrites the source;
- offline reading reuses the same document/progress/bookmark semantics through repository/sync adapters;
- Learning and TTS are optional capability layers over Reader content, not separate Reader identities.

## 1. Purpose

Provide one distraction-minimized reading product for Books, Light Novels, Web Novels, Manga and fixed documents while adapting controls to the content type.

The Reader must:
- open at the exact canonical resume position;
- save progress automatically without a manual Save action;
- keep reading content visually dominant;
- expose TOC, search, language/edition, appearance and annotations without permanent clutter;
- support paged and continuous reading where the document capability allows it;
- support fixed-page/image content without showing irrelevant typography controls;
- distinguish official editions from generated/machine-translated derivatives;
- continue to function with verified offline content;
- reuse the same shell, preference model and progress semantics across media types.

The Reader is one product with source/render adapters, not separate Book, Novel and Manga applications.

## 2. Entry / route contract

Reader entry can come from:
- Continue Reading;
- Book / Light Novel / Manga detail;
- chapter selection;
- bookmark/highlight/search result;
- offline library;
- compact Continue Reading surface;
- deep link to a canonical chapter/page/locator.

The Reader resolves:
1. canonical Work;
2. selected Edition and language;
3. Volume/Chapter or fixed-page target;
4. readable Asset/File/document descriptor;
5. profile Reader preferences;
6. exact canonical resume locator;
7. annotations/bookmarks relevant to the target;
8. translation and offline availability.

A URL may carry a jump target, but canonical progress identity must not depend on URL structure.

## 3. Shared Reader frame

All supported content types use the same conceptual frame.

### Top bar

Contains:
- context-aware Back/Close;
- Work title;
- current chapter/section title when useful;
- Contents;
- Search where supported;
- Language/Edition when more than one readable view exists;
- Appearance;
- More.

On Mobile, only the highest-frequency actions stay directly visible. Secondary actions move into More.

### Content surface

The reading document occupies the dominant visual area.

The shell must not force every source into the same rendering model:
- reflowable text uses readable column/page layout;
- Manga uses page/image layout;
- PDF/fixed documents use fixed-page rendering;
- unsupported controls disappear through capabilities.

### Bottom frame / progress

Desktop/Tablet:
- slim progress indicator/slider;
- page/chapter position text;
- optional previous/next section transport;
- TTS transport only while relevant.

Mobile:
- compact page/progress row;
- previous/next when applicable;
- direct tool row for Contents, Language, Appearance and TTS when supported.

The progress indicator remains readable even when transient chrome hides.

## 4. Content capability model

The UI is driven by document capabilities, not by media-type conditionals spread across pages.

### Reflowable text

Typical for Book, Light Novel and Web Novel.

May expose:
- continuous Scroll;
- paged reading;
- one/two-page layout;
- font family;
- font size;
- line height;
- paragraph spacing/indent;
- text width;
- hyphenation;
- chapter styling;
- illustrations;
- selection;
- highlights/bookmarks/notes;
- search;
- TTS;
- Learning interaction.

### Manga / comic image sequence

Expose:
- single page;
- spread / two-page where meaningful;
- right-to-left / left-to-right direction;
- fit width / fit page;
- zoom;
- continuous vertical mode where supported;
- page number;
- chapter navigation;
- bookmarks;
- optional OCR/Learning only when an explicit capability exists.

Do **not** show text font/line-height controls for image pages.

### Fixed document / PDF

Expose:
- page navigation;
- single/spread/continuous layout;
- zoom / page width / whole page;
- outline/contents where present;
- text search/selection/TTS only when the document exposes reliable text;
- bookmarks.

Do not emulate reflowable typography for fixed pages.

## 5. Exact progress and completion semantics

Reader progress follows the canonical split from issue #662.

Keep separate:
- `CurrentItem`;
- `ResumePosition`;
- `CompletedThrough`;
- external `ProviderProgress`.

Opening Chapter 12 does not automatically mean Chapter 12 is completed.

### Exact locator by content type

EPUB/eBook:
- chapter/content identity;
- stable CFI/paragraph/block anchor or equivalent;
- character/offset where useful;
- fallback percentage.

Light Novel / Web text:
- chapter;
- stable block/paragraph identity or index;
- character offset/anchor text;
- fallback percentage.

Manga:
- chapter;
- page/index;
- optional intra-page position/zoom only when needed for resume;
- fallback percentage.

PDF/fixed:
- page;
- optional vertical/viewport offset;
- fallback document percentage.

### Auto-save rules

Progress saves automatically:
- debounce/throttle during normal reading;
- flush on page turn;
- flush on chapter change;
- flush before route navigation;
- flush on visibility/background transition;
- flush on Reader close;
- queue locally first when offline support is active.

The Reader must restore the exact stable locator first and use fallback percentage only when the exact anchor can no longer be resolved.

There is no manual Save Progress requirement.

## 6. Desktop composition

Desktop is reading-first, pointer/keyboard friendly.

### Layout

Default:
- app sidebar may remain part of the application shell;
- Reader top/bottom chrome stays compact;
- centered readable content column;
- generous margins;
- Contents can become a collapsible side panel on wide layouts;
- opening a panel must not unnecessarily shrink text below a comfortable reading width.

Wide-screen reflowable text may use one or two pages depending on Reader preferences and document capability.

### Desktop interactions

- center/content-safe click may toggle transient chrome only where it does not interfere with text selection;
- text selection always wins over chrome gestures;
- pointer near the top edge may reveal hidden chrome;
- keyboard page navigation works only outside text input/editing;
- Escape closes the deepest menu/sheet first;
- search result/bookmark/highlight jumps preserve the active Reader session.

### Required Desktop mockup states

1. reflowable Book/LN reading;
2. Contents side panel open;
3. Appearance/settings sheet;
4. language/edition menu;
5. annotation selection state;
6. translation/preparing state;
7. offline/degraded state.

## 7. Mobile composition

Mobile is touch-first and content-dominant.

### Chrome

- top bar stays minimal;
- chrome hides while reading when appropriate;
- center tap reveals/hides chrome;
- downward scroll may hide chrome;
- upward scroll reveals it;
- open menus/sheets prevent auto-hide;
- bottom tool row uses large touch targets.

### Paged mode

- edge tap/swipe turns page;
- selection/annotation gesture takes priority over page turn;
- page transition should be fast and restrained, not decorative;
- chapter boundary can continue to next chapter only according to the configured auto-continue behavior.

### Scroll mode

- normal platform scrolling;
- progress follows stable anchors rather than only raw scroll percentage;
- restoring after font/layout changes must remain anchored to the same logical text.

### Mobile sheets

Use bottom sheets/fullscreen sheets for:
- Contents;
- Search;
- Appearance;
- Language/Edition;
- Bookmarks/Highlights;
- Learning details;
- TTS settings.

Avoid tiny desktop popovers on phones.

### Required Mobile mockup states

1. clean reading with chrome hidden;
2. chrome visible;
3. Contents sheet;
4. Appearance sheet;
5. text selection + Highlight/Bookmark action;
6. language/edition sheet;
7. offline unavailable chapter;
8. Manga single-page state.

## 8. Tablet composition

Tablet remains touch-first but can use more width.

Portrait:
- similar to Mobile;
- wider readable margins;
- larger sheets.

Landscape:
- optional two-page/spread view;
- Contents can use a side panel;
- annotation/Learning details may use a side sheet;
- do not adopt hover-only Desktop behavior.

iPadOS/WebKit follows the same Reader semantics and uses platform-safe selection, fullscreen and storage behavior.

## 9. TV

TV is **not** a primary full reading target in the current planning baseline.

Allowed:
- browse Work/Volume/Chapter;
- show reading progress;
- Continue on phone/tablet/desktop;
- optional large-page Manga/fixed-image viewing only if separately approved.

Do not create a dense TV text Reader simply for platform parity.

## 10. Reading modes

### Scroll

- continuous content;
- exact anchor follows the visible reading position;
- thin progress remains available;
- chapter boundary behavior follows user preference.

### Pages

- content flows into stable pages;
- layout changes must preserve the logical anchor;
- one/two-page choice is capability- and width-aware;
- page number reflects the rendered document state, not a fake fixed page count before layout completes.

### Manga page/spread

- single page;
- spread;
- continuous vertical where supported;
- reading direction explicit and persistent through Reader preferences;
- cover/single first page must not be incorrectly paired in spread mode.

### Fixed/PDF

- whole page;
- page width;
- explicit zoom;
- single/spread/continuous modes as supported.

## 11. Contents and navigation

Contents can include:
- volumes;
- chapters;
- prologue/epilogue/side stories exactly as source/canonical metadata provides;
- PDF outline/sections;
- Manga chapters/pages where applicable.

Rules:
- current item clearly selected;
- large works load lists lazily/paged;
- search/filter within Contents is allowed;
- do not load an entire 100k-chapter structure into the Reader page;
- previous/next resolves canonical structure;
- browser/system Back remains navigation history, not a hard-coded Library link.

## 12. Search

Where source text supports it:
- bounded in-work search;
- search original and currently available readable translations where appropriate;
- result shows chapter/section + concise context;
- selecting a result jumps to a stable locator;
- search panel does not replace the whole Reader route.

For Manga/image-only content, Search appears only if OCR/text capability actually exists.

## 13. Language, Edition and Translation

Language and Edition selection are user-facing reading concepts, not provider/debug data.

The menu may show:
- Original;
- official localized edition(s);
- generated/machine-translated derivative(s);
- side-by-side/Both only where the renderer explicitly supports it.

The UI must clearly distinguish:
- **Official translation**
- **Machine/generated translation**

Generated translation must never masquerade as an official edition.

### Missing translation

If no target edition exists and policy allows generation:
- expose `Translate` / `Prepare translation`;
- show queued/running state;
- keep source content readable while generation runs where possible;
- cache/store the result as the derived canonical Edition/Version;
- switching language later reuses the stored derivative.

Do not translate on every page render.

## 14. Reader appearance and preferences

Reader settings use the existing preference cascade rather than per-page localStorage copies.

Scopes:
1. system content-type preset;
2. profile global;
3. profile media/content type;
4. matching genre scope;
5. Work-specific override.

The UI may allow the user to choose where a change is saved.

### Reflowable settings

May include:
- Scroll / Pages;
- one/two pages;
- font;
- font size;
- line height;
- text width;
- paragraph spacing/indent;
- hyphenation;
- chapter presentation;
- illustration visibility;
- page number visibility;
- auto-continue chapter.

### Manga settings

May include:
- reading direction;
- single/spread/continuous;
- fit width/page;
- page gap;
- background;
- auto-advance only if explicitly approved later.

### Fixed/PDF settings

May include:
- single/spread/continuous;
- zoom;
- page width/whole page;
- background.

Unsupported controls must not render.

## 15. Reader themes / Light and Dark

Reader has deliberate reading surfaces independent from application chrome.

Required base modes:
- System;
- Light;
- Dark.

Reader-specific surfaces may include:
- white;
- warm/cream paper;
- sepia-like neutral;
- dark gray;
- near-black.

Rules:
- changing Reader paper does not change global Jularr theme;
- application chrome still respects global Light/Dark tokens;
- no low-contrast decorative backgrounds behind text;
- artwork/background themes stay in page margins and never reduce text readability;
- Dark mode is designed, not an inversion filter.

## 16. Bookmarks, highlights and notes

### Bookmark

Can target:
- current reading position;
- selected text start;
- Manga/PDF page.

May include a user label.

### Highlight

Only where reliable text selection/anchors exist.

Supports:
- selected range;
- optional note;
- overlapping highlights where the renderer supports it.

### Notes/annotation panel

- current chapter annotations readily available;
- work-wide annotations load on demand;
- selecting one jumps to the saved stable anchor;
- large annotation sets are paged/searchable;
- offline support follows the explicit sync contract; do not pretend unsynced capabilities exist for types that are not offline-enabled.

Do not require annotations to mutate source HTML/content.

## 17. Learning interaction

Learning is optional and capability-gated.

For readable/selectable text:
- tap/select term;
- show reading/meaning/explanation;
- Save / Learn / Known / Ignore where authorized;
- preserve Reader position when the Learning sheet opens/closes.

Learning interaction must not create another copy of chapter identity or reading progress.

For Manga, Learning/OCR is absent unless a real OCR/text capability exists.

## 18. TTS / Read aloud

TTS is an optional Reader extension.

Normal controls:
- Read aloud / Play;
- Pause/Resume;
- Stop;
- voice/speed settings.

Rules:
- current selection can be read;
- otherwise continue from visible/current text;
- Reader may follow spoken content without redefining reading progress;
- TTS cursor is ephemeral session state;
- switching visible language stops/restarts according to explicit TTS behavior;
- auto-continue into next chapter is a Reader preference and defaults conservatively.

TTS controls must not become a second permanent toolbar when inactive.

## 19. Offline reading

Offline content uses the same Reader UI and source semantics.

Requirements:
- verified local content opens without network;
- chapter navigation works for downloaded adjacent content;
- exact progress writes locally first and synchronizes later;
- bookmarks use the same canonical semantics;
- Reader preferences remain available;
- downloaded translation/edition identity remains explicit;
- stale/corrupt/incomplete local content is not presented as valid.

### Partial offline state

If current chapter exists locally but next chapter does not:
- current chapter remains fully readable;
- Next shows a clear unavailable/offline message;
- do not navigate to a browser/network error page.

If the whole requested Work is not cached:
- show a Reader-specific offline unavailable state with Back and downloaded-content choices when available.

## 20. Preparing / loading / partial states

### Document loading

Show:
- restrained skeleton/progress;
- title/chapter context;
- no fake page count before layout is known.

### Exact resume restore

Avoid visible jump where possible:
1. render enough document structure;
2. resolve exact locator;
3. position reader;
4. then settle chrome/progress.

### Missing chapter content

When canonical chapter metadata exists but content is not ready:
- show `Preparing chapter`;
- offer Prepare/Retry when permitted;
- poll/refresh through normal Job state;
- do not call providers synchronously from the Reader GET.

### Translation running

Show source content plus compact translation state where possible.

### Partial edition

Reader remains usable for available chapters and clearly marks unavailable next/previous targets.

## 21. Error states

Explicit categories:
- content unavailable;
- storage offline;
- unsupported/corrupt format;
- failed translation;
- failed document render;
- permission revoked;
- offline chapter missing;
- stale/corrupt offline package.

Recovery actions depend on the error:
- Retry;
- Prepare;
- switch Edition/language;
- use original;
- remove/re-download offline content;
- Back.

Never show raw provider/server exceptions.

## 22. Empty states

Reader itself normally opens a specific target, so classic empty pages are rare.

Valid empty/unavailable cases:
- Work has no readable Edition;
- selected Edition has no readable chapter/file;
- no downloaded chapter while offline;
- search has no results;
- annotations list has no items.

Use small contextual empty states inside the relevant sheet/panel rather than replacing the full Reader where possible.

## 23. Continuation surface

Leaving the full Reader through normal navigation should retain a compact Continue Reading surface where technically feasible.

Desktop/Tablet:
- cover;
- Work title;
- chapter/section;
- progress;
- Continue.

Mobile:
- compact bar above bottom navigation;
- title + current chapter/page;
- Continue;
- explicit close/dismiss.

Rules:
- return restores the exact canonical locator;
- no second progress store;
- continuation derives from canonical Reader/MediaProgress state.

TV may expose Continue Reading as a browse card rather than a persistent mini surface.

## 24. Accessibility

Required:
- keyboard operation on Desktop;
- visible focus;
- minimum touch targets;
- screen-reader labels for icons;
- semantic headings for reflowable text;
- selectable text remains selectable;
- user text scaling remains usable;
- reduced motion disables decorative transitions;
- themes meet contrast requirements;
- focus trapping for modal panels/sheets;
- Reader controls do not steal standard browser/system selection behavior unnecessarily.

## 25. Required mockup set

Create in this order:

1. **Desktop Light — reflowable Book/LN primary reading state**  
   Establish content width, top/bottom frame and calm reading hierarchy.

2. **Desktop Light — Contents side panel + progress**  
   Establish large-work navigation.

3. **Mobile Light — clean reading with chrome visible**  
   Establish touch targets and bottom tool row.

4. **Mobile Light — chrome hidden reading state**  
   Validate content-first behavior.

5. **Mobile Light — Appearance bottom sheet**  
   Validate Reader settings density.

6. **Mobile Light — annotation/text selection state**  
   Highlight / Bookmark / Learning interaction.

7. **Desktop/Mobile — language & translation state**  
   Clearly distinguish Original / Official / Generated.

8. **Manga Mobile — primary single-page state**  
   Validate shared Reader frame with image-specific controls.

9. **Tablet — landscape two-page / side-panel state**  
   Only after Desktop/Mobile hierarchy is approved.

10. **Dark validation**  
    At minimum Desktop reflowable + Mobile Manga/reading.

11. **Offline/Preparing/Error reference**  
    One shared state reference plus responsive notes.

TV Reader mockup is not required in this phase.

## 26. Data / information contract

Reader consumes view data derived from:
- canonical Work;
- Volume/Chapter or fixed-page structure;
- Edition/Version;
- readable Asset/File/document descriptor;
- content/layout capability flags;
- exact canonical progress locator;
- completed-through state;
- ReaderPreference effective values + inheritance source;
- available official/generated languages/editions;
- Translation state;
- bookmarks/highlights/notes;
- Learning capability/state;
- TTS capability/preferences;
- offline availability/package verification state.

The new Reader UI must not permanently query legacy `NovelWork`, `NovelProgress`, `MangaProgress` etc. as separate domain truths once canonical migration is complete. Existing services are migration/source adapters until moved behind canonical contracts.

## 27. Must not implement

- No separate Book, Novel and Manga Reader shells.
- No second progress database/store per media type.
- No manual Save Progress requirement.
- No progress represented only as percentage when a stable locator is available.
- No automatic completion merely because a chapter was opened.
- No provider write-back from CurrentItem/ResumePosition.
- No destructive overwrite of original content during translation.
- No machine translation presented as official.
- No translation request on every render.
- No whole-work chapter list embedded into every Reader page for large works.
- No typography controls on Manga/PDF when they are meaningless.
- No giant persistent toolbar around the reading surface.
- No TV text Reader purely for parity.
- No unsanitized imported HTML.
- No annotation implementation that mutates canonical source text.
- No duplicate online/offline Reader rendering paths.
- No separate offline progress semantics.
- No page-local preference system competing with `ReaderPreference`.
- No decorative theme/background behind text that reduces readability.
- No source-provider network call during normal Reader GET solely to make missing chapter content appear synchronously.

## 28. Mockup acceptance checklist

A Reader mockup is acceptable only when:
- content is visually dominant;
- Book/LN/Manga can share the frame without sharing inappropriate controls;
- exact resume/progress semantics are representable;
- Desktop, Mobile and Tablet behaviors are intentional;
- Light/Dark reading surfaces are deliberate;
- Contents works for very large structures;
- language/edition clearly distinguishes official and generated content;
- annotations do not overwhelm the reading surface;
- Loading/Preparing/Offline/Error states are defined;
- no manual progress-save control is required;
- no legacy parallel core model is implied;
- TV is not forced into an unsupported reading UX.
