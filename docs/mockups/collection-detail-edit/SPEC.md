# Collection Detail / Edit — Clean Design

Status: **binding planning specification; visual mockup recommended for Detail + Smart Rule Builder**.

Collection Detail lives under `Library -> Collections`. It is not a new top-level destination.

The same detail surface is used for:
- Manual Collections;
- Smart Collections;
- Built-in Collections;
- Franchise/adaptation Collections where supported.

The display grammar stays consistent with the normal Library. Editing behavior changes according to collection kind.

## 1. Purpose

Collection Detail answers:

- What is this collection?
- Which canonical Works are in it?
- How do I filter/sort/view them?
- If I own/edit it, how do I change membership, order, metadata or Smart rules?
- For Smart Collections, why does an item match?

Collections never duplicate media metadata or create alternate Work identity.

## 2. Entry

Entry comes from:
- `Library -> Collections`;
- a collection shelf/card;
- a direct deep link;
- optional media context action `Add to collection`.

Back returns to the Collections landing with previous scroll/focus/filter state where practical.

## 3. Collection kinds

### Manual
Explicit profile-owned list of canonical Work references.

### Smart
Dynamic membership from a persisted rule expression evaluated against canonical/local/profile facts.

### Built-in
System-defined views such as Watchlist/Favorites where product behavior uses them.

Built-in views are not silently converted into editable manual collections.

### Franchise / adaptation
Derived from canonical Work relations where supported.

These are presentation/grouping views, not duplicate Work records.

## 4. Collection Detail — shared layout

### Header

Keep the header compact.

Show:
- collection artwork/mosaic or representative artwork;
- title;
- optional short description;
- item count;
- compact kind label only when useful: Manual / Smart / Franchise;
- Edit action only when editable;
- overflow for secondary actions.

Do not use a huge media-detail hero.

### Toolbar

Below header:
- Filter;
- Sort;
- Grid/List toggle where useful;
- Search within collection when the collection is large;
- for Manual Collections, optional `Add media` action.

Do not create permanent filter-chip walls.

### Content

Render the same canonical MediaCard/ListRow component as Library.

Cross-media Collections can contain:
- Anime;
- Series;
- Movies;
- Manga;
- Light Novels;
- Books;
- Audiobooks.

Cards retain their normal:
- title;
- progress;
- language availability;
- relevant status;
- context actions.

Collection membership must not change card grammar.

## 5. Filter / Sort

Reuse Library filtering.

Useful filters:
- media type;
- progress;
- availability;
- language;
- genre/year/status where relevant.

Useful sort:
- manual order, for Manual Collection;
- title;
- recently added;
- last watched/read/listened;
- release date/year;
- progress;
- user rating.

Smart Collections may also define a default rule sort, but the user can temporarily change presentation sort without rewriting the rule unless explicitly saved as the collection default.

## 6. Manual Collection — normal detail behavior

Manual membership is explicit.

Available actions where permitted:
- Add media;
- Remove media;
- Reorder;
- Edit title/description/artwork;
- Duplicate collection;
- Delete collection.

Removing a Work from a Collection does not:
- delete the Work;
- remove it from Library;
- reset progress;
- cancel Request/Wanted state.

## 7. Manual Collection — Edit mode

Use one focused edit mode, not many tiny dialogs.

### Header fields
- title;
- description;
- artwork/mosaic choice where supported.

### Membership list

Show a compact sortable/reorderable list of current Works.

Each row:
- drag/reorder handle on pointer/touch-capable platforms where appropriate;
- small artwork;
- title;
- media type only when useful;
- Remove action.

### Add media

`Add media` opens one searchable selector dialog/sheet.

The selector:
- searches canonical/resolvable Works;
- supports media-type filtering;
- marks items already in the Collection;
- allows multi-select;
- adds Work references only.

Do not duplicate the full Discover experience inside the selector.

### Save

Manual Edit uses explicit:
- Cancel;
- Save.

This allows title, description, membership and order changes to be reviewed as one draft.

Quick `Add to collection` actions from other media surfaces may save immediately because they represent one explicit action.

## 8. Smart Collection — Detail

The normal Smart Collection detail looks like any other Collection.

Header additionally may show:
- Smart marker;
- concise result count;
- `Edit rules`.

Do not show the complete rule expression permanently above the grid.

For each item, overflow may include:
- `Why is this here?`

That opens a compact explanation sheet/dialog, for example:

```text
Why this matches
✓ Media type: Anime
✓ Local: Yes
✓ Genre: Fantasy
✓ German subtitles: Complete
```

No raw JSON/YAML.

## 9. Smart Collection — Rule Builder

Smart rules are complex enough for a dedicated **edit subpage** inside Collections.

It is not a top-level navigation destination.

### Page structure

1. Back + collection title
2. collection metadata
3. Rules
4. Manual overrides
5. Sort / limit
6. Live preview
7. Cancel + Save

### Rule grammar

Use nested groups:

```text
ALL
├─ Local = true
└─ ANY
   ├─ Genre contains Fantasy
   └─ Franchise = Frieren
```

Each rule row contains:
- field;
- operator;
- value;
- remove.

Group controls:
- ALL / ANY;
- Add rule;
- Add group;
- remove group where valid.

Do not expose raw expression syntax to normal users.

### Supported rule families

Identity / metadata:
- media type;
- franchise/relation group;
- genre/tag;
- studio/author/creator;
- year/decade;
- release/status.

Availability:
- local;
- monitored;
- requested/wanted;
- complete/partial/missing.

Video/language:
- resolution;
- HDR;
- codec only if genuinely useful to the Smart Collection feature;
- audio language;
- subtitle language;
- language coverage.

Written media:
- edition language;
- format;
- volume/chapter completeness.

Profile-scoped:
- not started / in progress / completed;
- rating;
- last activity;
- watched/read/listened state.

Rule options shown must depend on capabilities/media domain. Do not show meaningless fields everywhere.

## 10. Smart manual include / exclude

Smart Collections support explicit overrides.

### Always include
Canonical Work remains in Collection even if it does not match rules.

### Always exclude
Canonical Work stays out even if it matches rules.

Overrides survive rule reevaluation.

Use one compact searchable multi-select for each override list.

Do not duplicate Work data inside the rule expression.

## 11. Live preview

The Rule Builder must update a preview without requiring Save.

Show:
- result count;
- first representative matching items;
- optionally a small `View all preview` when useful.

Preview evaluation should use local/cached canonical facts.

Do not trigger provider network searches merely to preview Smart rules.

When a rule is invalid:
- keep last valid preview where practical;
- show local validation on the broken rule;
- disable Save until valid.

## 12. Smart sort / limit

Optional collection-level output controls:
- default sort;
- ascending/descending;
- optional item limit.

Examples:
- highest rating first;
- most recently active;
- newest release;
- title.

This controls membership presentation/output, not canonical Work data.

## 13. Collection artwork

Manual/Smart Collections may use:
- automatic 2x2 mosaic;
- one representative Work artwork;
- custom collection artwork where supported.

Default should be automatic.

Changing Collection artwork never changes Work artwork.

## 14. Profile ownership / visibility

V1 personal Collections are profile-scoped unless a separate shared-collection capability is explicitly added.

A profile must not see or edit another profile's private Collections.

Future shared Collections need explicit ownership/permission rules rather than silently making personal Collections global.

Smart rules using progress/rating/activity are always evaluated against the owning/current profile context.

## 15. Data contract

Conceptual persistent model:

### Collection
- Id
- ProfileId / owner scope
- Kind: Manual / Smart
- Title
- Description
- Artwork configuration
- default sort
- optional limit
- timestamps

### Manual CollectionEntry
- CollectionId
- WorkId
- manual order
- added timestamp

### Smart rule
- CollectionId
- persisted expression tree
- rule schema/version

### Smart overrides
- include WorkId
- exclude WorkId

Built-in/Franchise views may be derived rather than persisted as normal editable Collections.

Never copy canonical title/metadata/progress into Collection rows as a source of truth.

## 16. Empty states

### Empty Manual Collection
Show:
- `No media yet`;
- Add media.

### Smart rule matches nothing
Show:
- `No matches`;
- Edit rules;
- concise indication that current rules return zero items.

### Filter hides all
Show:
- `No results with these filters`;
- Reset filters.

Do not confuse empty membership with filter-zero state.

## 17. Desktop

### Detail
- normal Jularr sidebar;
- compact Collection header;
- toolbar;
- Library-style grid/list.

### Manual Edit
- focused page/dialog-width editor depending on collection size;
- membership list;
- Add media;
- Cancel/Save.

### Smart Rule Builder
Use a dedicated full content page:
- rule editor on the left/center;
- live preview panel on the right on wide screens;
- preview moves below rules on narrower screens.

Do not squeeze the rule builder into a tiny modal.

## 18. Mobile

### Detail
- compact Collection header;
- Filter/Sort in sheets;
- normal two-column media grid where width permits.

### Manual Edit
- full-width page/sheet;
- reorder handles;
- Add media selector;
- sticky Save.

### Smart Rule Builder
- full-screen edit page;
- one group at a time vertically;
- nested groups use indentation + clear ALL/ANY header;
- preview collapses below rules;
- sticky Save.

Avoid horizontal rule tables.

## 19. Tablet

Portrait follows Mobile editing behavior.

Landscape can use Desktop split view for Smart Rule Builder.

## 20. TV

TV supports Collection browsing/detail only:
- large Collection cards;
- selected Collection opens normal remote-friendly media grid;
- Filter/Sort where useful.

No Smart rule editing, drag reorder, artwork editing or destructive Collection management on TV.

Complex editing hands off to Web/Mobile.

## 21. Delete Collection

Deleting a Manual/Smart Collection deletes only:
- collection metadata;
- membership/rules/overrides.

It never deletes:
- canonical Works;
- Files;
- progress;
- ratings;
- Requests;
- Wanted state.

Require confirmation.

Built-in Collections cannot be deleted unless the product explicitly defines them as removable saved views.

## 22. Shared components

Reuse:
- Library MediaCard/ListRow;
- Filter;
- Sort;
- CollectionHeader;
- CollectionArtwork/Mosaic;
- MediaMultiSelect;
- RuleGroup;
- RuleRow;
- LivePreview;
- ConfirmDialog.

Do not create special media cards for Collections.

## 23. Must not implement

- no duplicate media records inside Collections;
- no separate per-media Collection models;
- no YAML/JSON rule editor for normal users;
- no provider calls during normal Smart evaluation/preview;
- no permanent rule-debug text above Collection media;
- no media-card redesign for Collections;
- no deleting media when removing Collection membership;
- no resetting progress when removing membership;
- no mixing Recommendation Shelf semantics into Smart Collections;
- no TV rule editor;
- no silent sharing of private profile Collections.

## 24. Mockup requirement

Visual review is recommended for:

1. Desktop Light Manual Collection detail.
2. Desktop Light Smart Collection Rule Builder with live preview.
3. Mobile Collection detail.
4. Mobile Smart Rule Builder.

The Manual Collection editor itself can use standard list/form components unless implementation reveals a hierarchy problem.

Text specification wins over imagery on conflict.
