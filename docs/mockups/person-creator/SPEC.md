# Person / Creator View — Clean Design

Status: **approved UX direction; binding planning specification**.

This is a secondary consumer page opened from media credits, creator links or provider-backed discovery. It is not a top-level navigation destination.

The approved visual direction is the Creator/Person overview with a compact hero, role labels, biography, Works, role-specific filtering and related people.

## 1. Purpose

The page answers:

- Who is this person?
- What roles are they known for?
- Which canonical Works are connected to them?
- What role do they have in each Work?
- Which related people/credits are useful to explore?

Supported examples:
- actor;
- voice actor;
- director;
- author;
- mangaka;
- illustrator;
- screenwriter;
- producer;
- composer/musician;
- narrator;
- other creator/staff roles represented by provider metadata.

## 2. Identity and persistence

Jularr uses a local provider-independent Person identity for durable UI references.

Conceptually:
- Person
- PersonName / alternate names
- PersonExternalIdentity
- PersonArtwork
- PersonWorkCredit

Provider records such as AniList/TMDB/OpenLibrary are evidence/sources, not the permanent Person ID.

After external person/credit data has been fetched, Jularr persists the useful provider snapshot locally so this page can render without requiring a live API call every time.

Ambiguous cross-provider persons must not be silently merged.

## 3. Entry points

Open from:
- media Detail cast/staff/creator section;
- Work credits;
- Search/Discover person result where supported;
- another Person's related-people list.

Back returns to the exact previous media/person context where practical.

## 4. Desktop layout

Use the normal Jularr shell.

### Compact hero
Show:
- person portrait;
- name;
- useful alternate/native name;
- restrained backdrop/artwork when available;
- compact role labels;
- optional overflow.

Do not use a giant cinematic hero.

### Primary tabs
Use:
- Overview
- Works
- Characters, only when meaningful
- Related People, only when meaningful

External links do not need their own primary tab unless the amount of data later justifies it.

## 5. Overview

### Biography
Show:
- concise biography;
- Read more when long;
- useful facts such as birthplace/date/language only when provider data is credible.

Do not fill the page with trivia fields.

### Highlighted Works
Show a small row of representative canonical Works using the normal Jularr media-card grammar.

### External links
Optional compact provider/source links:
- AniList
- TMDB
- IMDb
- OpenLibrary
- other supported sources.

They are secondary navigation, never the canonical identity.

## 6. Works

Works is the main data-heavy view.

Show canonical Jularr Works connected through PersonWorkCredit.

Cross-media is allowed:
- Anime;
- Series;
- Movies;
- Manga;
- Light Novels;
- Books;
- Audiobooks.

Each card/list item uses the normal Jularr media component and may add one concise role line such as:
- Director
- Writer
- Voice Actor · Character Name
- Author
- Illustrator
- Narrator

Do not redesign media cards.

## 7. Works filters

Keep controls compact.

Useful filters:
- media type;
- role;
- year/decade;
- availability/local state where useful.

Sort:
- release date newest/oldest;
- title;
- role prominence only if a stable definition exists.

Do not create a permanent chip wall.

Desktop can use Filter + Sort with a compact type row only if the list benefits from it.

Mobile uses a filter sheet.

## 8. Role-specific behavior

### Voice actor / actor
A Work credit can include:
- role;
- character;
- language/dub context where known.

Selecting a character may open Character context only if that surface exists later.

Do not create a separate media identity for a cast credit.

### Author / writer / mangaka / illustrator
Works list emphasizes:
- Work title;
- media type;
- credited role;
- release year where useful.

### Director / producer / composer
Same shared Works page, filtered by role.

No separate Creator page implementation per profession.

## 9. Work credit detail

When the user needs the exact credit context, use a compact detail sheet/dialog or the Work's normal Details/Cast & Staff section.

Show:
- Work;
- Person;
- role;
- optional character;
- optional specific season/episode/unit only when the provider/canonical credit is truly unit-scoped.

Avoid a new full page just for one credit.

## 10. Related people

Show only meaningful relations inferred from shared Work credits or explicit provider relationships.

Examples:
- frequent collaborator;
- co-author;
- director/producer collaborator;
- recurring composer;
- related creator.

Do not invent social relationships.

Each row:
- portrait;
- name;
- concise relation/context;
- open Person view.

## 11. Characters

Only show this tab when the Person has character/voice/acting credits that benefit from it.

Useful row/card:
- character artwork when available;
- character name;
- Work;
- role/language where relevant.

Character identity may remain provider-backed metadata unless/until Jularr introduces a canonical Character domain. Do not let this page force a parallel media identity model.

## 12. Local vs external Works

A Person page may show Works that are:
- local/available;
- requested;
- known to Jularr from provider metadata only.

Every displayed Work must resolve to a local canonical Work identity or a safely persisted provider-backed Work candidate that can be resolved before durable user actions.

Normal media-card actions remain consistent:
- Play/Read/Listen when available;
- Request when unavailable and allowed;
- Add to Collection where supported.

Do not invent Person-specific Request behavior.

## 13. Mobile

Use:
- compact portrait/header;
- role labels;
- tabs/segmented navigation;
- Biography;
- Works grid/list;
- Filter sheet;
- Related People list.

Do not squeeze Desktop side information into narrow columns.

## 14. Tablet

Portrait follows Mobile.

Landscape can use Desktop composition.

## 15. TV

TV support is optional/secondary.

If exposed:
- overview;
- Works;
- large remote-safe cards;
- simple role filtering.

No dense biography metadata or external link management.

## 16. Loading / partial / errors

Support:
- Person identity loaded while Works are still loading;
- missing portrait;
- provider temporarily unavailable;
- partial credits;
- stale locally cached provider data.

Previously persisted Person/Work credit data remains renderable when providers are offline.

## 17. Accessibility

- semantic Person heading;
- role labels available as text;
- keyboard/touch/remote navigation;
- portrait has meaningful alt text when appropriate;
- external source links are labeled with provider name;
- filter state is not color-only.

## 18. Must not implement

- no top-level Creators navigation destination;
- no Follow/social system solely because the mockup visually suggested one;
- no separate page implementation per profession;
- no provider ID as canonical Person ID;
- no duplicate Work objects per provider;
- no live provider dependency for normal rendering;
- no Person-specific media-card design;
- no invented collaborator/social relationship;
- no Request/acquisition internals.

## 19. Approved visual reference

Approved direction:
- Desktop Creator overview with compact hero;
- Works grid with filters;
- role-specific presentation;
- related people;
- Mobile equivalents;
- standard Jularr media cards.

The owner will upload the approved mockup image into this folder.

The mockup's visual `Follow` action is **not part of the approved functional contract** unless a future explicit social/follow feature is planned.

Text specification wins over imagery on conflict.
