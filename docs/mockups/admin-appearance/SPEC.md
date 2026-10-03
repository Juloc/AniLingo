# Admin Appearance — V1

Status: approved planning direction. Existing `/Admin/Appearance` implementation is the starting point. Approved mockups uploaded to this folder are visual references; this text remains binding.

Global UX rules: `docs/UX.md`.
Global Admin density contract: `docs/mockups/admin-instance/SPEC.md`.
User appearance settings: `docs/mockups/user-settings/SPEC.md`.

If an image and this specification conflict, this specification wins.

## Purpose

Admin Appearance owns only:

- instance-wide default visual style;
- whether profiles may override theme/style;
- whether profiles may override accent color;
- the current admin's own Admin UI density shortcut;
- a small live preview of the visual styles.

It must not become a generic user-preference editor or a second theme engine.

## Current implementation

The existing `/Admin/Appearance` implementation already provides:

- instance default theme from `ThemeCatalog`;
- `AllowProfileThemeOverride`;
- `AllowProfileAccentOverride`;
- persistence through `InstanceAppearanceSettingsStore`;
- authorization through `JularrPolicies.AdminSystem`.

These contracts remain canonical.

The target UI is a presentation/refinement of the existing settings, plus the already-approved shared Admin density preference.

## Page structure

One page, not a tab maze.

Desktop order:

1. **Instanz-Standardtheme**
2. **Benutzer-Freigaben**
3. **Meine Admin-Oberfläche**
4. **Vorschau**

Tablet/mobile stack the same sections vertically.

## 1. Instanz-Standardtheme

Show the available visual styles as selectable theme cards.

Current first-class styles:

- **Original Jularr**
- **Clean**

Each theme card includes:

- theme name;
- selected/default state;
- short description;
- small representative preview;
- radio/select control.

The chosen value persists through the existing instance appearance settings store.

### Original Jularr

Original Jularr is the established expressive Jularr skin:

- Japanese ink / watercolor influence;
- cherry blossom / brush / paper-style decorative treatment;
- red/pink default accent;
- anime/Japanese visual references are allowed as decorative/theme examples;
- the same information architecture/components as Clean.

It must not become a separate application layout.

### Clean

Clean is the neutral/minimal Jularr skin:

- compact Fluent-2 / Windows-11-like visual language;
- light/dark neutral surfaces;
- purple Jularr default accent;
- no decorative anime/Japanese background art;
- no anime character artwork used merely to demonstrate the theme.

For preview/example media in Clean:

- use ordinary real-world media content representative of a normal mixed library;
- film/series artwork is preferred in examples;
- preview content comes from configured/demo library data where possible;
- do not hard-code one anime identity into the Clean skin.

Important distinction:

**Media artwork shown because it is actual library content is allowed in Clean. Decorative anime artwork that exists only as theme decoration is not.**

## 2. Benutzer-Freigaben

These are instance policy switches controlling what users may customize in their personal Appearance settings.

Current switches:

- **Eigenes Theme erlauben**
- **Eigene Accent-Farbe erlauben**

Rules:

- this page does not directly set another user's personal appearance;
- disabling an override means profiles fall back to the effective instance default for that setting;
- existing stored personal preferences may be retained internally for later re-enable, unless product policy explicitly requires clearing them;
- server policy remains authoritative.

Do not add additional policy switches until backed by a real settings contract.

Examples of future policies that require backend support before appearing:

- allow brightness override;
- allow density override;
- allow custom accent values.

## 3. Meine Admin-Oberfläche

This section is explicitly personal to the signed-in admin.

Show the global Admin density selector:

`Detailliert | Kompakt`

This uses the same persisted profile-scoped `AdminUiDensity` contract defined in `admin-instance/SPEC.md`.

It does not write to `InstanceAppearanceSettingsStore`.

### Detailliert

- more inline explanations/context;
- slightly larger spacing;
- richer summary cards where useful.

### Kompakt

- denser tables/lists;
- reduced vertical spacing;
- fewer repeated descriptions;
- same information/actions/permissions.

Changing it updates the entire Admin UI immediately.

This control may also appear in the shared Admin shell; both surfaces edit the same preference.

## 4. Vorschau

Show a small representative live preview for each visual style.

The preview should demonstrate shared components rather than invent a fake application.

Useful elements:

- sidebar/navigation;
- page header;
- media row/card;
- table/list row;
- primary/secondary button;
- progress indicator;
- status indicator;
- Light/Dark rendering.

### Original Jularr preview

May use:

- cherry blossom/ink decorative treatment;
- anime/Japanese-style sample media;
- red/pink default accent.

### Clean preview

Must show:

- neutral/light or dark surfaces;
- purple accent;
- no decorative anime character/background art;
- film/series-style example media or real configured demo/library items.

The preview must make clear that theme affects skin/tokens, not navigation or feature availability.

## Light / Dark

Both themes support:

- System
- Light
- Dark

Admin Appearance may preview Light/Dark.

The instance-level page must not silently overwrite a user's allowed personal brightness preference unless such an instance policy is explicitly added later.

## Accent behavior

Accent colors use shared semantic design tokens.

Rules:

- Clean default: purple;
- Original Jularr default: red/pink;
- permitted accent changes may hue-shift Jularr decorative/brand tokens;
- third-party provider logos are never recolored;
- semantic success/warning/error colors are never recolored just to match the accent;
- contrast requirements remain valid in Light and Dark.

## Theme architecture

Clean and Original Jularr are skins over one component/layout system.

They share:

- routes;
- navigation;
- page hierarchy;
- forms;
- dialogs;
- tables;
- responsive behavior;
- permissions;
- business logic.

A theme may change:

- color tokens;
- background/decorative assets;
- borders/shadows;
- surface treatment;
- typography details within shared design rules;
- branding/logo treatment where defined.

A theme must not change:

- what features exist;
- what actions a user can perform;
- data model;
- route structure;
- permissions;
- layout information architecture.

## Relationship to User Settings

Admin Appearance owns:

- instance default style;
- instance override permissions.

User Settings -> Appearance owns the current profile's allowed personal choices:

- visual style;
- brightness;
- accent;
- user-interface density where supported for consumer UI;
- visual effects.

The Admin density selector is a separate Admin-specific profile preference.

Do not conflate consumer UI density with Admin UI density unless a future product decision explicitly unifies them.

## Detailed / Compact behavior on this page

The global Admin density applies here too.

Detailed mode:
- theme cards with descriptions/previews;
- explanatory text under policy switches.

Compact mode:
- smaller theme cards;
- denser policy rows;
- same preview options;
- no loss of policy meaning.

The Appearance page itself must not define another independent compact-mode state.

## Platforms

### Desktop

Primary management surface.

Use a single-page layout with the four sections above.

### Tablet

Same content, stacked/two-column where space permits.

### Mobile

Supported:

- theme selection;
- override switches;
- Admin density;
- preview.

Use one-column cards and large touch targets.

### TV

No Admin Appearance page required.

## Loading / states

Required:

- settings loading;
- ready;
- save in progress;
- save success;
- save failure;
- invalid/missing theme definition;
- profile overrides disabled;
- preview unavailable;
- permission denied.

Do not reset settings silently on load failure.

## Implementation alignment

### Keep

- `ThemeCatalog`;
- `InstanceAppearanceSettingsStore`;
- `DefaultThemeId`;
- `AllowProfileThemeOverride`;
- `AllowProfileAccentOverride`;
- `AdminSystem` authorization.

### Add / refine

- approved theme-card presentation;
- representative previews;
- explicit Original-vs-Clean visual contract;
- global Admin density shortcut using the shared profile preference;
- responsive presentation.

### Do not add without a backend contract

- per-user admin overrides stored in instance settings;
- brightness instance policy;
- arbitrary CSS/theme editor;
- custom theme upload;
- raw token editor;
- page-specific theme overrides.

## Must not implement

- No separate layout implementation per theme.
- No decorative anime/Japanese artwork in Clean.
- No hard-coded anime-only sample identity in Clean previews.
- No duplicate Admin density setting.
- No storing Admin density in `InstanceAppearanceSettingsStore`.
- No editing another user's personal appearance from this page.
- No recoloring provider logos.
- No recoloring semantic error/warning/success states to match accent.
- No raw CSS/JSON/theme-token editor.
- No theme change that alters permissions/features/navigation semantics.
