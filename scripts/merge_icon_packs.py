import argparse
import math
import struct
import zlib
from pathlib import Path


def read_pack(path):
    data = path.read_bytes()
    if len(data) < 40:
        raise ValueError(f"Truncated icon pack: {path}")
    magic, version, flags, width, height, cell, count = struct.unpack_from("<8s6I", data)
    if magic != b"VPBICON1" or version != 1 or flags != 3:
        raise ValueError(f"Unsupported icon pack: {path}")
    if not 0 < cell <= 512 or not 0 < width <= 8192 or not 0 < height <= 8192 or count > 4096:
        raise ValueError(f"Invalid icon dimensions: {path}")
    offset = 32
    entries = {}
    for _ in range(count):
        length, = struct.unpack_from("<H", data, offset)
        offset += 2
        if not 0 < length <= 256:
            raise ValueError("Invalid icon name length")
        name = data[offset:offset + length].decode("utf-8")
        offset += length
        rect = struct.unpack_from("<4H", data, offset)
        offset += 8
        x, y, w, h = rect
        if name in entries or w == 0 or h == 0 or x + w > width or y + h > height:
            raise ValueError(f"Invalid icon rectangle: {name}")
        entries[name] = rect
    raw_length, packed_length = struct.unpack_from("<2I", data, offset)
    offset += 8
    if raw_length != width * height or offset + packed_length != len(data):
        raise ValueError("Invalid atlas payload length")
    decoder = zlib.decompressobj(-15)
    alpha = decoder.decompress(data[offset:], raw_length + 1)
    if len(alpha) != raw_length or not decoder.eof or decoder.unused_data:
        raise ValueError("Invalid compressed atlas")
    icons = {}
    for name, (x, y, w, h) in entries.items():
        pixels = b"".join(alpha[(y + row) * width + x:(y + row) * width + x + w] for row in range(h))
        icons[name] = (w, h, pixels)
    return cell, icons


def merge(base_path, incoming_path, output_path):
    base_cell, icons = read_pack(base_path)
    incoming_cell, incoming = read_pack(incoming_path)
    icons.update(incoming)
    cell = max(base_cell, incoming_cell, max(max(w, h) for w, h, _ in icons.values()))
    write_pack(icons, cell, output_path)


def write_pack(icons, cell, output_path):
    side = 1
    while side < math.ceil(math.sqrt(len(icons))) * cell:
        side *= 2
    if side > 8192:
        raise ValueError("Merged atlas exceeds runtime size limit")
    columns = side // cell
    alpha = bytearray(side * side)
    index = bytearray()
    for position, (name, (w, h, pixels)) in enumerate(sorted(icons.items())):
        x = position % columns * cell
        y = position // columns * cell
        encoded = name.encode("utf-8")
        index.extend(struct.pack("<H", len(encoded)) + encoded + struct.pack("<4H", x, y, w, h))
        for row in range(h):
            start = (y + row) * side + x
            alpha[start:start + w] = pixels[row * w:(row + 1) * w]
    encoder = zlib.compressobj(9, zlib.DEFLATED, -15)
    payload = encoder.compress(alpha) + encoder.flush()
    output = struct.pack("<8s6I", b"VPBICON1", 1, 3, side, side, cell, len(icons))
    output += index + struct.pack("<2I", len(alpha), len(payload)) + payload
    temporary = output_path.with_suffix(output_path.suffix + ".tmp")
    temporary.write_bytes(output)
    _, verified = read_pack(temporary)
    if verified != icons:
        raise ValueError("Merged icon pixels differ from source packs")
    temporary.replace(output_path)
    print(f"Wrote {len(icons)} icons; verified every icon's pixels")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("base", type=Path)
    parser.add_argument("incoming", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    merge(args.base, args.incoming, args.output)
