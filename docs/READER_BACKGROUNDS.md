# Reader backgrounds

Reader artwork is a repository asset, not deployment configuration.

## Root

`src/AniLingo.Web/wwwroot/reader-backgrounds/`

The application has only this root convention. There is no Docker setting and no hard-coded list of genres or image files.

## Layout

Use one first-level folder per genre and one or more image variants inside it:

```text
reader-backgrounds/
  horror/
    fog-forest.webp
  romance/
    blossom.webp
    soft-hearts.webp
  sci-fi/
    starchart.webp
```

The folder name is the genre key. File names are variant keys. Use short lowercase kebab-case names.

Adding `reader-backgrounds/western/desert-map.webp` is enough to create the new `western` genre and make that background available. No C#, JavaScript, manifest, database migration, environment variable, or Compose change is required.

Supported image extensions are WebP, AVIF, PNG, JPG and JPEG. WebP is preferred for bundled reader artwork.

## Discovery and selection

`ReaderBackgroundCatalog` scans the root and derives:

- stable id: `genre/variant`
- genre key from the folder
- display labels from the slug
- static URL from the relative file path

Genre metadata is normalized before automatic matching, so values such as `Sci-Fi`, `Crime / Detective` and `Slice of Life` match folders `sci-fi`, `crime-detective` and `slice-of-life`.

All variants remain selectable. Automatic genre selection uses the first variant in stable filename order; a user-selected background should persist the stable `genre/variant` id.

The authenticated endpoint `GET /api/reader-backgrounds` exposes the current catalog. Repeated `genre` query parameters can be supplied to receive a `suggestedId`.

Example:

```text
/api/reader-backgrounds?genre=Dark%20Fantasy&genre=Fantasy
```

## Artwork guidelines

Reader backgrounds should be atmospheric rather than poster-like:

- portrait artwork around 9:16 works well across phones and tablets
- keep the center low-contrast and visually quiet
- place stronger details near edges
- do not bake text into artwork
- keep text contrast controlled by the reader UI, not by the image itself
- prefer optimized WebP assets to large source PNGs
