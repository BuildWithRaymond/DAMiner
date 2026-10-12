"""Build app branding from DAMerchant's gold bar sprite, inventory ID 4529.

The supplied PNG is copied unchanged for the web UI. Native icon sizes use
nearest-neighbor scaling to preserve its pixel art. Requires Pillow.
"""

import argparse
import shutil
import struct
from io import BytesIO
from pathlib import Path

from PIL import Image


def make_icon(ingot, size):
    canvas = Image.new("RGBA", (size, size), "#1b2738")
    # Keep each original pixel square, including at small taskbar sizes.
    scale = max(1, int(size * 0.8 / max(ingot.size)))
    scaled = ingot.resize((ingot.width * scale, ingot.height * scale), Image.Resampling.NEAREST)
    if scaled.width > size:
        scaled.thumbnail((size, size), Image.Resampling.NEAREST)
    position = ((size - scaled.width) // 2, (size - scaled.height) // 2)
    canvas.alpha_composite(scaled, position)
    return canvas


def write_ico(path, ingot):
    sizes = (16, 24, 32, 48, 64, 128, 256)
    payloads = []
    for size in sizes:
        buffer = BytesIO()
        make_icon(ingot, size).save(buffer, format="PNG")
        payloads.append(buffer.getvalue())
    offset = 6 + 16 * len(sizes)
    directory = bytearray(struct.pack("<HHH", 0, 1, len(sizes)))
    for size, payload in zip(sizes, payloads):
        directory.extend(struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(payload), offset))
        offset += len(payload)
    path.write_bytes(directory + b"".join(payloads))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("sprite_path", type=Path, help="Path to DAMerchant/public/items/4529.png")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    ingot = Image.open(args.sprite_path).convert("RGBA")
    web_directory = root / "src/DAGoldManager.UI/wwwroot/images"
    icon_directory = root / "src/DAGoldManager.Desktop/Resources/AppIcon"
    web_directory.mkdir(parents=True, exist_ok=True)
    icon_directory.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(args.sprite_path, web_directory / "gold-bar.png")
    make_icon(ingot, 512).save(icon_directory / "appicon.png")
    write_ico(icon_directory / "appicon.ico", ingot)
    print(f"Copied gold bar sprite 4529 ({ingot.width} x {ingot.height}); wrote web and Windows icons.")


if __name__ == "__main__":
    main()
