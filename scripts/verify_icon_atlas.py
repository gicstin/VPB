#!/usr/bin/env python3
"""Verify the shipped icon atlas and audit the icon set.

Pack path comes from build_icon_atlas.default_pack_path so the two scripts cannot drift;
override with --pack. The legacy plugins/vpb_icons.pack location is dead.

Four checks:
  1. Map health  - every icon_map.json entry resolves to a source file; every source file
                   under assets/icons is referenced by at least one role.
  2. Round-trip  - decode the pack and compare each glyph against a freshly rendered source,
                   so a stale pack cannot pass silently.
  3. Link check  - every icon key used in src/ exists in the pack, and every packed icon is
                   used (no dangling refs, no orphans). See check_links for why the two
                   directions are matched with different strictness.
  4. Contact sheet (--sheet) - render the decoded atlas as a labelled PNG grid, so a glyph can
                   be audited by what it depicts rather than by its role name.
"""

import argparse
import json
import re
import struct
import sys
import zlib
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:
    sys.exit("Pillow is required: pip install pillow")

sys.path.insert(0, str(Path(__file__).resolve().parent))
from build_icon_atlas import default_pack_path, render, resolve  # noqa: E402

MAGIC = b"VPBICON1"
FLAG_DEFLATED = 1 << 0
FLAG_ALPHA_ONLY = 1 << 1

LEGACY_REF_RE = re.compile(rb"vpb_icons/([A-Za-z0-9_\-/]+)\.png")
KEY_REF_RE = re.compile(rb"LoadIconSprite\(\s*\"([A-Za-z0-9_\-/]+)\"")
DYNAMIC_REF_RE = re.compile(rb"LoadIconSprite\(\s*(?!\")")
ANY_LITERAL_RE = re.compile(rb"\"([A-Za-z0-9_\-/]+)\"")


def unpack(pack_path):
    blob = pack_path.read_bytes()
    if blob[:8] != MAGIC:
        sys.exit("Bad magic in %s" % pack_path)
    off = 8
    version, flags, w, h, cell, count = struct.unpack_from("<IIIIII", blob, off)
    off += 24
    if version != 1:
        sys.exit("Unsupported pack version %d" % version)

    entries = []
    for _ in range(count):
        (name_len,) = struct.unpack_from("<H", blob, off)
        off += 2
        name = blob[off:off + name_len].decode("utf-8")
        off += name_len
        x, y, gw, gh = struct.unpack_from("<HHHH", blob, off)
        off += 8
        entries.append((name, x, y, gw, gh))

    raw_len, comp_len = struct.unpack_from("<II", blob, off)
    off += 8
    payload = blob[off:off + comp_len]
    if len(payload) != comp_len:
        sys.exit("Truncated payload: %d of %d bytes" % (len(payload), comp_len))
    if off + comp_len != len(blob):
        sys.exit("Trailing data: payload ends at %d, file is %d bytes"
                 % (off + comp_len, len(blob)))

    if flags & FLAG_DEFLATED:
        payload = zlib.decompress(payload, -15)
    if len(payload) != raw_len:
        sys.exit("Payload %d bytes after inflate, header said %d" % (len(payload), raw_len))

    if flags & FLAG_ALPHA_ONLY:
        if raw_len != w * h:
            sys.exit("Alpha payload %d bytes, expected %d" % (raw_len, w * h))
        alpha = Image.frombytes("L", (w, h), payload)
        atlas = Image.new("RGBA", (w, h), (255, 255, 255, 0))
        atlas.putalpha(alpha)
    else:
        atlas = Image.frombytes("RGBA", (w, h), payload)

    atlas = atlas.transpose(Image.FLIP_TOP_BOTTOM)
    return {"w": w, "h": h, "cell": cell, "entries": entries, "atlas": atlas,
            "size": pack_path.stat().st_size}


def entry_role(entry):
    """Pack entries store the bare role name. Older packs wrote '<role>.png'; tolerate both
    rather than blind-slicing 4 chars, which silently truncated every modern name."""
    name = entry[0]
    return name[:-4] if name.lower().endswith(".png") else name


def crop(info, entry):
    _, x, y_bottom, gw, gh = entry
    top = info["h"] - y_bottom - gh
    return info["atlas"].crop((x, top, x + gw, top + gh))


def check_map(icons_root):
    print("Map health:")
    map_path = icons_root / "icon_map.json"
    if not map_path.exists():
        print("  FAIL missing %s" % map_path)
        return None, False
    icon_map = json.loads(map_path.read_text(encoding="utf-8"))

    unresolved = []
    used = set()
    for role in sorted(icon_map):
        src = resolve(icons_root, icon_map[role])
        if src is None:
            unresolved.append((role, icon_map[role]))
        else:
            used.add(src.resolve())

    on_disk = set()
    for sub in ("tabler", "filled"):
        d = icons_root / sub
        if d.is_dir():
            for p in d.iterdir():
                if p.suffix.lower() in (".svg", ".png"):
                    on_disk.add(p.resolve())

    orphans = sorted(p.name for p in (on_disk - used))
    ok = True
    if unresolved:
        ok = False
        for role, ref in unresolved:
            print("  FAIL '%s' -> %s (no .svg or .png)" % (role, ref))
    if orphans:
        ok = False
        print("  FAIL source files no role points at: %s" % ", ".join(orphans))
    if ok:
        shared = len(icon_map) - len(used)
        print("  OK %d roles -> %d source files (%d shared by >1 role)"
              % (len(icon_map), len(used), shared))
    return icon_map, ok


def check_roundtrip(info, icons_root, icon_map, tolerance):
    print("Round-trip:")
    worst_name, worst = None, 0.0
    missing = []
    for entry in info["entries"]:
        role = entry_role(entry)
        ref = icon_map.get(role)
        if ref is None:
            missing.append(role)
            continue
        src = resolve(icons_root, ref)
        if src is None:
            missing.append(role)
            continue
        want = render(src, info["cell"]).getchannel("A")
        got = crop(info, entry).getchannel("A")
        wb, gb = want.tobytes(), got.tobytes()
        diff = sum(abs(a - b) for a, b in zip(wb, gb)) / float(len(wb))
        if diff > worst:
            worst_name, worst = role, diff

    if missing:
        print("  FAIL packed icons with no mapped source: %s" % ", ".join(missing))
        return False
    print("  %d icons decoded, worst mean alpha delta %.3f (%s)"
          % (len(info["entries"]), worst, worst_name))
    if worst > tolerance:
        print("  FAIL delta exceeds tolerance %.3f - pack is stale, rebuild it" % tolerance)
        return False
    print("  OK")
    return True


def check_links(info, src_root):
    """Atlas keys are bare Tabler ids ("grid-scan", "filled/star"), legacy vpb_icons/<name>.png
    still resolves at runtime. The two directions need different strictness:

      dangling - strict. A literal handed straight to LoadIconSprite must be in the pack,
                 otherwise that button silently falls back to its text label.
      orphan   - loose. Icon names travel through helpers and lookup tables
                 (case "maintenance": return "tools";), so any matching string literal in src/
                 counts as a use. A coincidental literal can mask a real orphan; that is the
                 accepted trade for not drowning the check in false alarms.
    """
    print("Two-way link check:")
    packed = set(entry_role(e).lower() for e in info["entries"])
    direct = set()
    any_literal = set()
    dynamic_sites = 0
    for path in src_root.rglob("*.cs"):
        blob = path.read_bytes()
        for m in KEY_REF_RE.finditer(blob):
            direct.add(m.group(1).decode("ascii").lower())
        for m in LEGACY_REF_RE.finditer(blob):
            direct.add(m.group(1).decode("ascii").lower())
        for m in ANY_LITERAL_RE.finditer(blob):
            any_literal.add(m.group(1).decode("ascii").lower())
        dynamic_sites += len(DYNAMIC_REF_RE.findall(blob))

    dangling = sorted(direct - packed)
    orphans = sorted(packed - direct - any_literal)
    indirect = len((packed & any_literal) - direct)
    if dangling:
        print("  FAIL referenced in src/ but not in pack: %s" % ", ".join(dangling))
    if orphans:
        print("  FAIL in pack but never referenced: %s" % ", ".join(orphans))
    if dynamic_sites:
        print("  note %d LoadIconSprite call(s) pass a non-literal key" % dynamic_sites)
    if not dangling and not orphans:
        print("  OK %d packed, %d direct, %d via helper/lookup literals"
              % (len(packed), len(packed & direct), indirect))
        return True
    return False


def write_sheet(info, out_path, cols):
    entries = info["entries"]
    thumb = 64
    label_h = 18
    pad = 6
    tile_w = max(thumb, 120) + pad * 2
    tile_h = thumb + label_h + pad * 2
    rows = (len(entries) + cols - 1) // cols

    sheet = Image.new("RGB", (cols * tile_w, rows * tile_h), (32, 34, 38))
    draw = ImageDraw.Draw(sheet)

    for i, entry in enumerate(entries):
        glyph = crop(info, entry).resize((thumb, thumb), Image.LANCZOS)
        cx = (i % cols) * tile_w
        cy = (i // cols) * tile_h
        sheet.paste(glyph, (cx + (tile_w - thumb) // 2, cy + pad), glyph)
        name = entry_role(entry)
        if len(name) > 18:
            name = name[:17] + "…"
        tw = draw.textlength(name)
        draw.text((cx + (tile_w - tw) / 2, cy + pad + thumb + 2), name, fill=(170, 176, 188))

    sheet.save(out_path)
    print("Contact sheet: %s (%dx%d)" % (out_path, sheet.width, sheet.height))


def main():
    here = Path(__file__).resolve().parent
    project = here.parent
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pack", type=Path, default=default_pack_path(project))
    ap.add_argument("--icons", type=Path, default=project / "assets" / "icons")
    ap.add_argument("--code", type=Path, default=project / "src")
    ap.add_argument("--tolerance", type=float, default=2.0,
                    help="max mean per-pixel alpha delta (0-255) allowed")
    ap.add_argument("--sheet", type=Path, nargs="?",
                    const=project / "scripts" / "_icon_contact_sheet.png",
                    help="also render a labelled contact sheet")
    ap.add_argument("--sheet-cols", type=int, default=14)
    args = ap.parse_args()

    if not args.pack.exists():
        sys.exit("Pack not found: %s (run build_icon_atlas.py)" % args.pack)

    info = unpack(args.pack)
    print("Pack: %s" % args.pack)
    print("  %d icons, atlas %dx%d, cell %d, %.2f MB on disk\n"
          % (len(info["entries"]), info["w"], info["h"], info["cell"],
             info["size"] / 1048576.0))

    icon_map, ok = check_map(args.icons)
    print()
    if icon_map is not None:
        ok = check_roundtrip(info, args.icons, icon_map, args.tolerance) and ok
        print()
    ok = check_links(info, args.code) and ok

    if args.sheet:
        print()
        write_sheet(info, args.sheet, args.sheet_cols)

    print("\n%s" % ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
