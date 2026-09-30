# Book / Light Novel Detail — Clean Design

Status: **approved UX direction**.

## Purpose

Canonical user-facing detail page for Books and Light Novels.

The page should prioritize reading progress, volumes/chapters, language/edition availability and translation state without becoming an admin/import screen.

Light and Dark are both first-class. Desktop and Mobile follow the approved reference; Tablet and TV adapt the same information hierarchy.

## Approved structure

1. Hero
2. Volumes
3. Continue Reading / current position
4. About / Description
5. Versions & Languages
6. Main Characters
7. Related Works
8. More Like This
9. additional Details only where useful

This is a direct scroll page. Do not hide the main content behind unnecessary tabs.

## 1. Hero

Hero contains:

- cover/poster
- title
- optional original/native title
- media type: Book / Light Novel
- publication status
- year
- a small set of genres/themes
- short synopsis
- primary action: Read / Continue Reading
- Favorite / My List
- overflow for secondary actions

### Hero information strip

Keep compact information in the lower Hero area:

- rating
- author
- publisher
- number of volumes / publication state
- original language
- available reading/translation languages

The Hero can use a provider backdrop where available. If none exists, use the cover with the shared derived blur/gradient fallback.

## 2. Volumes

Volumes are the primary structural navigation for Light Novels / multi-volume books.

Desktop:

- horizontal cover row
- selected volume has clear accent border/state
- each volume shows cover, volume number/title and publication date where known
- overflow/context action only when useful

Mobile:

- horizontal swipe row
- selected state remains obvious
- do not squeeze a desktop table onto the phone

For a single-volume book, omit the redundant volume rail and go directly to reading/progress/content.

## 3. Continue Reading / current position

A compact card shows:

- selected/current volume
- chapter/current locator
- reading progress
- Continue Reading / Open in Reader

This should be prominent but smaller than the Hero.

If there is no progress yet, use Read / Start Reading instead of an empty progress card.

## 4. Description / About

Show:

- synopsis/description
- Read More when long
- compact genre/theme labels

Avoid repeating every Hero field.

## 5. Versions & Languages

This section is important for written media and must clearly separate official and generated translations.

Show compactly:

- language
- edition/source type
- availability/completeness
- translation provenance/state

Semantic examples:

- Japanese — Original
- German — Official Translation
- English — Official Translation
- German — Machine Translation
- English — Fan Translation, if the configured source legally/appropriately exposes such metadata

Generated translations must never look identical to official editions.

### Availability states

Support:

- complete
- partial
- requested
- translating
- generated translation available
- unavailable

### Actions

When permitted:

- Request edition/language
- Translate missing content
- switch reading edition/language
- View all versions/languages

Do not expose raw provider/import/release internals on the normal user page.

## Preferred-language behavior

The page should answer quickly:

- Is my preferred reading language available?
- Is it official or generated?
- Is it complete?
- If not, which languages are available?
- Can I request or translate it?

Preferred language appears first.

## 6. Chapters

Chapter navigation lives primarily in the Reader / selected Volume context.

If chapter-level browsing is useful on the detail page, it should be a compact expandable list or selected-volume action, not a giant permanent chapter table beneath every book.

For chapter-heavy web novels, the UX may expose a dedicated chapter list, but it must retain the same canonical Work -> Volume -> Chapter model.

## 7. Main Characters

Optional horizontal row when metadata exists.

Each item:

- portrait
- name
- compact role

For normal prose books without useful character metadata, omit the section entirely.

## 8. Related Works

Use canonical relations:

- Manga adaptation
- Anime adaptation
- sequel / prequel
- side story
- spin-off
- related novel
- source/adaptation relation

Relation type should be understandable but compact.

## 9. More Like This

Recommendation row using simple Discover-style cards.

Keep separate from Related Works.

## Editions vs Translations

Do not collapse all languages into one mutable book record.

The UX represents the underlying canonical structure:

- Work
- Edition
- Version
- language / translation provenance

Switching language/edition must not overwrite the original.

## Mobile

Follow the approved reference direction:

- compact vertical Hero
- cover integrated cleanly
- large Read/Continue action
- horizontal volume row
- Continue Reading card
- Description
- compact Versions & Languages card
- horizontal Characters / Related / Similar rows
- touch-first actions

No dense desktop table.

## Tablet

- adaptive two-column layout where width permits
- touch-first
- volume row remains easy to browse

## TV

Reading itself is usually not a TV-first workflow, but the detail page may still be browsable:

- large focus targets
- cover + title + metadata
- volumes as horizontal rail
- Related Works / recommendations
- Continue Reading may offer handoff to a phone/tablet where supported

Do not force the full Reader onto TV unless separately designed.

## States

Must support:

- loading
- no backdrop
- single-volume work
- no local edition
- preferred language available
- preferred language unavailable
- partial official translation
- machine translation in progress
- generated translation available
- requested edition/language
- no character metadata
- no related works
- storage offline
- metadata/provider partial failure
- error

Sections with no meaningful content should disappear rather than render empty placeholders.

## Visual reference

Recommended approved image filename:

- `book-light-novel-detail-clean-approved.png`

Store under:

- `docs/mockups/book-light-novel-detail/`

The image is the approved visual direction. This specification is authoritative for interaction, language semantics and content hierarchy.

## Implementation rule

Agents must not:

- treat machine translation as an official edition
- overwrite original content when generating translations
- expose acquisition/release internals in normal user UI
- render irrelevant empty sections
- replace the responsive volume rail with an admin-style table
- redesign the page without updating this spec and approved mockup
