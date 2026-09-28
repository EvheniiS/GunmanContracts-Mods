"""Export the title-only Mod Settings Nexus artwork at upload sizes."""

from pathlib import Path

from PIL import Image, ImageOps


HERE = Path(__file__).resolve().parent


def export(source, destination, size):
    with Image.open(HERE / source) as image:
        result = ImageOps.fit(
            image.convert("RGB"), size, method=Image.Resampling.LANCZOS
        )
        result.save(HERE / destination, quality=94, subsampling=0)


if __name__ == "__main__":
    export("ModSettings-thumbnail-base.png", "ModSettings-thumbnail.jpg", (1600, 900))
    export("ModSettings-header-base.png", "ModSettings-header.jpg", (1300, 372))
