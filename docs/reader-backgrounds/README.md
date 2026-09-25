# AniLingo reader backgrounds

Reader artwork is repository content. Deployment/Docker configuration does not select or enumerate images.

## Canonical root

`src/AniLingo.Web/wwwroot/reader-backgrounds/`

A theme has a stable id:

```text
<genre>/<variant>
```

Example:

```text
reader-backgrounds/
  horror/
    fog-forest/
      page.webp
      theme.json
      prompt.yaml
```

The id is always `horror/fog-forest`.

A legacy/simple theme may also be a single file:

```text
reader-backgrounds/horror/fog-forest.webp
```

It receives the same stable id. That means a simple image can later be upgraded to a full folder with page/scroll/parallax assets without breaking persisted user selections.

## Auto discovery

No genre or image list exists in C# or JavaScript.

AniLingo scans folders and recognizes these optional files:

- `page.webp`
- `scroll.webp`
- `parallax-back.webp`
- `parallax-mid.webp`
- `parallax-front.webp`

Responsive variants use suffixes:

- `-mobile`
- `-tablet`
- `-desktop`
- `-wide`

Example: `page-mobile.webp`.

The preparation tool also emits `<stem>-preview.webp` (320×512) for compact selectors/admin previews. Preview files are not treated as reader layers.

WebP is preferred. AVIF, PNG, JPG and JPEG are also accepted.

## Theme files

`theme.json` contains bounded declarative appearance/motion values only. It cannot inject CSS or JavaScript.

`prompt.yaml` is the reproducible image-generation specification. It contains full prompts for every supported visual mode.

## Reader behavior

- Paged reader uses `page`.
- Continuous reader uses `scroll` when available, otherwise `page`.
- 2.5D mode uses the parallax layers when available.
- If parallax is requested but layers are absent, the reader falls back to static.
- `prefers-reduced-motion` always disables parallax and animated effects.
- User/book adjustments multiply the safe values from the theme manifest; raw CSS is never persisted.

## Settings precedence

The existing canonical ReaderPreferences model remains the source of truth:

1. AniLingo defaults
2. genre/mood suggestion
3. user defaults
4. per-work override
5. unsaved live preview

Resetting a work deletes only that work's explicit overrides.


## Preparing and validating assets

Use the repository helper instead of manually inventing responsive crops:

```bash
python docs/reader-backgrounds/prepare_images.py page-master.png --stem page
python docs/reader-backgrounds/prepare_images.py scroll-master.png --stem scroll
python docs/reader-backgrounds/prepare_images.py front-master.png --stem parallax-front
```

It validates decodeability, portrait aspect ratio and minimum master dimensions, preserves alpha for transparent foreground layers, writes bounded WebP files, responsive variants and a compact preview.

Validation without writing derivatives:

```bash
python docs/reader-backgrounds/prepare_images.py page.webp --stem page --validate-only
```

Accepted minimum prepared sizes are 720×1280 for page assets and 720×1440 for scroll/parallax assets. Generation masters should still use the larger preferred sizes in `MODES.md`; the smaller limits exist for optimized bundled/runtime derivatives.


## Book / light-novel specific themes

A work can persist any stable theme id as its per-work override. For a special book-specific look, add a normal theme below a neutral `books` genre and inherit from an existing genre theme:

```text
reader-backgrounds/
  fantasy/
    enchanted-parchment/
      page.webp
      theme.json
  books/
    sample-light-novel/
      theme.json
      page-mobile.webp
```

`books/sample-light-novel/theme.json`:

```json
{
  "label": "Sample Light Novel",
  "extends": "fantasy/enchanted-parchment",
  "appearance": {
    "saturation": 0.72,
    "tint": "#241a32",
    "tintStrength": 0.08
  }
}
```

Inheritance is field-by-field:

- missing page/scroll/parallax assets fall back to the parent theme
- responsive child files override only that breakpoint
- omitted appearance/motion properties inherit the parent value
- child aliases are not inherited, so a book-specific theme cannot accidentally become the automatic default for the parent's whole genre
- invalid or circular inheritance is ignored safely

The normal ReaderPreferences precedence still decides whether this theme is used for a particular work; no book title or theme id is hard-coded in application code.
