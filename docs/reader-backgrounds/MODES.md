# Reader theme modes

## Page

Use `page.*` for the paged/book reader.

Design requirements:
- quiet center for selectable text
- details concentrated toward edges
- no baked-in text or characters
- compatible with one-page and two-page spreads
- page-turn shadow/curl is CSS, not rasterized into the image

Recommended master target: about 1600×2560 portrait.

## Scroll static

Use `scroll.*` for long continuous reading.

Design requirements:
- vertically spacious
- no hard horizon through the body-text area
- low contrast
- visually safe when fixed behind long content
- top/bottom should be compatible enough to crop/repeat without an obvious cut

Recommended master target: about 1600×3200.

If missing, AniLingo uses the page image.

## Scroll 2.5D / parallax

Optional files:

- `parallax-back.*`
- `parallax-mid.*`
- `parallax-front.*`

Back: sky, distant atmosphere, broad fog, distant silhouettes.
Mid: environmental structures, trees, ruins, skyline.
Front: sparse branches, particles, foreground fog/ornament near edges.

The layer images should be vertically repeatable/seam-friendly because AniLingo moves them at bounded independent speeds.

Foreground transparency is preferred where image generation supports it.

The text remains normal HTML and never moves with the background layers.
