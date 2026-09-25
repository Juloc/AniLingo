#!/usr/bin/env python3
"""Validate and prepare responsive reader-theme images.

Requires Pillow:
    python -m pip install pillow

Examples:
    python docs/reader-backgrounds/prepare_images.py art/page-master.png --stem page
    python docs/reader-backgrounds/prepare_images.py art/front.png --stem parallax-front
    python docs/reader-backgrounds/prepare_images.py art/page.webp --validate-only
"""

from __future__ import annotations

import argparse
from pathlib import Path
from PIL import Image, UnidentifiedImageError

TARGETS = {
    "mobile": (720, 1280),
    "tablet": (1200, 1600),
    "desktop": (1600, 1000),
    "wide": (1920, 1080),
}

PREVIEW = (320, 512)
MAX_BYTES = 450_000
MIN_PAGE = (900, 1400)
MIN_TALL = (900, 1600)
TALL_STEMS = {
    "scroll",
    "parallax-back",
    "parallax-mid",
    "parallax-front",
}


def cover_crop(image: Image.Image, width: int, height: int) -> Image.Image:
    source_ratio = image.width / image.height
    target_ratio = width / height

    if source_ratio > target_ratio:
        crop_width = round(image.height * target_ratio)
        left = (image.width - crop_width) // 2
        box = (left, 0, left + crop_width, image.height)
    else:
        crop_height = round(image.width / target_ratio)
        top = (image.height - crop_height) // 2
        box = (0, top, image.width, top + crop_height)

    return image.crop(box).resize((width, height), Image.Resampling.LANCZOS)


def normalized_mode(image: Image.Image) -> str:
    # Foreground parallax artwork may carry alpha. Do not flatten it.
    return "RGBA" if "A" in image.getbands() else "RGB"


def validate_source(image: Image.Image, stem: str, path: Path) -> None:
    min_width, min_height = MIN_TALL if stem in TALL_STEMS else MIN_PAGE

    if image.width < min_width or image.height < min_height:
        raise SystemExit(
            f"{path}: {image.width}x{image.height} is too small for {stem}; "
            f"minimum is {min_width}x{min_height}"
        )

    if image.height <= image.width and stem in {"page", *TALL_STEMS}:
        raise SystemExit(
            f"{path}: reader-theme masters must be portrait/tall, got "
            f"{image.width}x{image.height}"
        )

    # Extremely narrow/wide portrait sources make the central reading safe-zone
    # impossible to preserve across phone/tablet/desktop crops.
    ratio = image.width / image.height
    if ratio < 0.38 or ratio > 0.78:
        raise SystemExit(
            f"{path}: aspect ratio {ratio:.3f} is outside the supported "
            "reader master range 0.38..0.78"
        )


def write_webp(
    image: Image.Image,
    path: Path,
    quality: int,
    max_bytes: int,
) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(
        path,
        "WEBP",
        quality=quality,
        method=6,
        lossless=False,
    )
    size = path.stat().st_size
    if size > max_bytes:
        raise SystemExit(
            f"{path}: {size} bytes exceeds configured maximum {max_bytes}"
        )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("master", type=Path)
    parser.add_argument(
        "--stem",
        default="page",
        choices=[
            "page",
            "scroll",
            "parallax-back",
            "parallax-mid",
            "parallax-front",
        ],
    )
    parser.add_argument("--quality", type=int, default=80)
    parser.add_argument("--max-bytes", type=int, default=MAX_BYTES)
    parser.add_argument("--output", type=Path)
    parser.add_argument(
        "--validate-only",
        action="store_true",
        help="Validate dimensions/aspect and decodeability without writing files.",
    )
    args = parser.parse_args()

    if not 1 <= args.quality <= 100:
        raise SystemExit("--quality must be between 1 and 100")
    if args.max_bytes < 10_000:
        raise SystemExit("--max-bytes must be at least 10000")

    try:
        with Image.open(args.master) as opened:
            opened.load()
            validate_source(opened, args.stem, args.master)
            if args.validate_only:
                print(
                    f"OK {args.master}: {opened.width}x{opened.height}, "
                    f"mode={opened.mode}, stem={args.stem}"
                )
                return

            source = opened.convert(normalized_mode(opened))
    except (FileNotFoundError, UnidentifiedImageError, OSError) as error:
        raise SystemExit(f"{args.master}: cannot read image: {error}") from error

    output = args.output or args.master.parent
    write_webp(
        source,
        output / f"{args.stem}.webp",
        args.quality,
        args.max_bytes,
    )

    for name, (width, height) in TARGETS.items():
        derivative = cover_crop(source, width, height)
        write_webp(
            derivative,
            output / f"{args.stem}-{name}.webp",
            args.quality,
            args.max_bytes,
        )

    preview = cover_crop(source, *PREVIEW)
    write_webp(
        preview,
        output / f"{args.stem}-preview.webp",
        min(args.quality, 76),
        min(args.max_bytes, 120_000),
    )


if __name__ == "__main__":
    main()
