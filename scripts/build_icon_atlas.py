import io
import json
from pathlib import Path

import cairosvg
from PIL import Image

from merge_icon_packs import write_pack


def main():
    root = Path(__file__).resolve().parent.parent
    sources = root / "assets" / "icons"
    mapping = json.loads((sources / "icon_map.json").read_text(encoding="utf-8-sig"))
    icons = {}
    cell = 128
    for name, source in sorted(mapping.items()):
        svg = sources / (source + ".svg")
        if not svg.is_file():
            svg = sources / "tabler" / (source + ".svg")
        png = cairosvg.svg2png(bytestring=svg.read_bytes(), output_width=cell, output_height=cell)
        with Image.open(io.BytesIO(png)) as image:
            alpha = image.convert("RGBA").getchannel("A").transpose(Image.Transpose.FLIP_TOP_BOTTOM)
            icons[name] = (cell, cell, alpha.tobytes())
    output = root / "vam_patch" / "BepInEx" / "plugins" / "VPB" / "assets" / "icons.pack"
    write_pack(icons, cell, output)


if __name__ == "__main__":
    main()
