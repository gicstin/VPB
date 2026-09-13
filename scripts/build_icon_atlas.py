#!/usr/bin/env python3
"""Pack VPB's icon set into a single vpb_icons.pack atlas.

Sources live in assets/icons and are listed by assets/icons/icon_map.json. Keys are Tabler
source ids (the names C# passes to LoadIconSprite). A bare value resolves under tabler/;
a value with a slash is taken as-is under assets/icons.

    "shirt-off":   "shirt-off"     ->  assets/icons/tabler/shirt-off.svg
    "filled/star": "filled/star"   ->  assets/icons/filled/star.svg

Keys equal values. The map exists to name which glyphs ship in the atlas, not to invent
a second PNG-era nickname for each file.

SVG sources are rasterised straight to the target cell size, so glyph quality is set by the
vector, not by downsampling a fixed-size PNG. Stroke widths are left exactly as the source
defines them.

Every glyph is whitened (RGB -> 255, alpha preserved) so the runtime tints via Image.color on
the GPU rather than running a CPU GetPixels/SetPixels recolor per tint. Because RGB is then
uniform, the pack ships the alpha channel only under raw deflate and the loader expands it
back to RGBA32 white -- keeping the shipped file small while avoiding Unity's inconsistent
Alpha8 RGB behaviour with the default UI shader.

Output format (little-endian):

    magic        8 bytes   "VPBICON1"
    version      uint32    1
    flags        uint32    bit0 = payload raw-deflated, bit1 = payload alpha-only
    atlasWidth   uint32
    atlasHeight  uint32
    cellSize     uint32    glyph edge in px (padding excluded)
    iconCount    uint32
    index        iconCount entries, sorted by name:
                   nameLen  uint16
                   name     nameLen bytes UTF-8, the role name (no extension)
                   x,y,w,h  uint16 each, origin bottom-left (Unity convention)
    rawBytes     uint32    payload length after decompression
    compBytes    uint32    payload length as stored
    payload      compBytes bytes, bottom-up rows (Unity LoadRawTextureData order)
"""

import argparse
import io
import json
import math
import struct
import sys
import zlib
from pathlib import Path

try:
    from PIL import Image
except ImportError:
    sys.exit("Pillow is required: pip install pillow")

try:
    import cairosvg
except ImportError:
    cairosvg = None

MAGIC = b"VPBICON1"
FORMAT_VERSION = 1

FLAG_DEFLATED = 1
FLAG_ALPHA_ONLY = 2

PADDING = 2
SUPERSAMPLE = 2


def whiten(img):
    img = img.convert("RGBA")
    alpha = img.getchannel("A")
    white = Image.new("RGBA", img.size, (255, 255, 255, 0))
    white.putalpha(alpha)
    return white


def resolve(icons_root, ref):
    rel = ref if "/" in ref else "tabler/" + ref
    for ext in (".svg", ".png"):
        p = icons_root / (rel + ext)
        if p.exists():
            return p
    return None


def render(path, cell):
    if path.suffix.lower() == ".svg":
        if cairosvg is None:
            sys.exit("cairosvg is required for SVG sources: pip install cairosvg")
        big = cell * SUPERSAMPLE
        png = cairosvg.svg2png(url=str(path), output_width=big, output_height=big, background_color=None)
        img = whiten(Image.open(io.BytesIO(png)))
        return img.resize((cell, cell), Image.LANCZOS)

    img = whiten(Image.open(path))
    if img.size != (cell, cell):
        img = img.resize((cell, cell), Image.LANCZOS)
    return img


def build(icons_root, out_path, cell_size, verbose):
    map_path = icons_root / "icon_map.json"
    if not map_path.exists():
        sys.exit("Missing %s" % map_path)
    icon_map = json.loads(map_path.read_text(encoding="utf-8"))
    if not icon_map:
        sys.exit("icon_map.json is empty")

    missing = []
    resolved = []
    for role in sorted(icon_map):
        src = resolve(icons_root, icon_map[role])
        if src is None:
            missing.append((role, icon_map[role]))
        else:
            resolved.append((role, src))
    if missing:
        for role, ref in missing:
            print(f"  MISSING source for '{role!s}' -> {ref!s}", file=sys.stderr)
        sys.exit("%d icon source(s) missing" % len(missing))

    count = len(resolved)
    stride = cell_size + PADDING * 2
    cols = int(math.ceil(math.sqrt(count)))
    rows = int(math.ceil(count / float(cols)))
    atlas_w = cols * stride
    atlas_h = rows * stride

    atlas = Image.new("RGBA", (atlas_w, atlas_h), (255, 255, 255, 0))
    entries = []
    svg_count = 0

    for i, (role, src) in enumerate(resolved):
        glyph = render(src, cell_size)
        if src.suffix.lower() == ".svg":
            svg_count += 1
        col = i % cols
        row = i // cols
        x = col * stride + PADDING
        top = row * stride + PADDING
        atlas.paste(glyph, (x, top))
        y_bottom = atlas_h - top - cell_size
        entries.append((role, x, y_bottom, cell_size, cell_size))
        if verbose:
            print("  %-30s <- %-40s (%4d,%4d)" % (role, src.name, x, y_bottom))

    flipped = atlas.transpose(Image.FLIP_TOP_BOTTOM)
    pixels = flipped.getchannel("A").tobytes()
    expected = atlas_w * atlas_h
    if len(pixels) != expected:
        sys.exit("Alpha buffer %d bytes, expected %d" % (len(pixels), expected))

    comp = zlib.compressobj(9, zlib.DEFLATED, -15)
    payload = comp.compress(pixels) + comp.flush()
    flags = FLAG_DEFLATED | FLAG_ALPHA_ONLY

    blob = bytearray()
    blob += MAGIC
    blob += struct.pack("<IIIIII", FORMAT_VERSION, flags, atlas_w, atlas_h, cell_size, count)
    for name, x, y, w, h in entries:
        raw = name.encode("utf-8")
        blob += struct.pack("<H", len(raw))
        blob += raw
        blob += struct.pack("<HHHH", x, y, w, h)
    blob += struct.pack("<II", len(pixels), len(payload))
    blob += payload

    out_path.parent.mkdir(parents=True, exist_ok=True)
    prev = out_path.read_bytes() if out_path.exists() else None
    if prev == bytes(blob):
        print("Atlas unchanged: %s" % out_path)
    else:
        out_path.write_bytes(blob)
        print("Wrote %s" % out_path)

    print("  %d icons (%d vector, %d raster), %dpx cells, atlas %dx%d"
          % (count, svg_count, count - svg_count, cell_size, atlas_w, atlas_h))
    print("  shipped: %.2f MB in 1 file" % (len(blob) / 1048576.0))
    print("  runtime texture memory: %.2f MB (single RGBA32 texture)"
          % (atlas_w * atlas_h * 4 / 1048576.0))
    return 0


def default_pack_path(project):
    return project / "vam_patch" / "BepInEx" / "plugins" / "VPB" / "assets" / "icons.pack"


def main():
    here = Path(__file__).resolve().parent
    project = here.parent
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--icons", type=Path, default=project / "assets" / "icons",
                    help="icon source root containing icon_map.json (default: assets/icons)")
    ap.add_argument("--out", type=Path, default=default_pack_path(project),
                    help="output pack path")
    ap.add_argument("--cell", type=int, default=128,
                    help="glyph edge in px (default 128)")
    ap.add_argument("-v", "--verbose", action="store_true")
    args = ap.parse_args()

    if not args.icons.is_dir():
        sys.exit("Icon source root not found: %s" % args.icons)
    return build(args.icons, args.out, args.cell, args.verbose)


if __name__ == "__main__":
    sys.exit(main())
