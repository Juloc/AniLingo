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
