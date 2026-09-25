#!/usr/bin/env python3
"""Prepare responsive WebP derivatives for AniLingo reader themes.

Requires Pillow:
    python -m pip install pillow
"""

from __future__ import annotations

import argparse
from pathlib import Path
from PIL import Image

TARGETS = {
    "mobile": (720, 1280),
    "tablet": (1200, 1600),
    "desktop": (1600, 1000),
    "wide": (1920, 1080),
}

MAX_BYTES = 450_000


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


def write_webp(image: Image.Image, path: Path, quality: int) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    image.save(path, "WEBP", quality=quality, method=6)
    size = path.stat().st_size
    if size > MAX_BYTES:
        raise SystemExit(f"{path}: {size} bytes exceeds {MAX_BYTES}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("master", type=Path)
    parser.add_argument("--stem", default="page")
    parser.add_argument("--quality", type=int, default=80)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()

    output = args.output or args.master.parent
    with Image.open(args.master) as opened:
        source = opened.convert("RGB")
        write_webp(source, output / f"{args.stem}.webp", args.quality)
        for name, (width, height) in TARGETS.items():
            derivative = cover_crop(source, width, height)
            write_webp(
                derivative,
                output / f"{args.stem}-{name}.webp",
                args.quality,
            )


if __name__ == "__main__":
    main()
