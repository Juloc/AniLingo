# Reader background prompt contract

Prompt spec version: `reader-theme-v1`.

Every theme folder contains `prompt.yaml`. The prompts are intentionally stored in the repository so a missing or upgraded asset can be regenerated on demand with the same intent.

## Non-negotiable visual rules

Every generated image must:
- be a reading-app background, not cover art or a poster
- have no characters, faces, readable text, typography, logos or UI
- keep the central reading zone low-contrast and uncluttered
- bias stronger detail toward outer edges/corners
- avoid a strong focal point in the center
- preserve calm top and bottom zones for reader controls
- remain usable behind selectable body text
- use the genre palette/mood from its own prompt spec

## Page prompt

Page assets should feel like a designed book surface. Texture, framing, vignette and edge detail are allowed, but the center must remain quiet.

## Scroll-static prompt

Scroll assets are taller and should avoid hard scene breaks. Ask for vertical continuity and seam-friendly top/bottom composition.

## Parallax prompts

Back, mid and front are generated separately.

Back:
- broad atmosphere only
- least detail
- very low contrast

Mid:
- medium-depth environmental silhouettes
- still no central focal subject

Front:
- sparse foreground edge elements
- transparent background preferred
- keep central 55% mostly empty

## Responsive variants

Generate one high-quality master first. Derive smaller variants by resizing/cropping rather than asking the model to reinvent the scene for every screen size. This keeps the same visual identity on phone, tablet and desktop.
