#!/usr/bin/env python3
"""Regenerates Reel's app icon.

The mark is a film reel seen face on: a ring, a hub, and five spoke holes
between them. Drawn here rather than committed as an opaque binary, so the
shape and the colours can be changed by editing this and running it again.

    python scripts/generate-reel-icon.py

Writes apps/Reel/Assets/reel.png (256px, the runtime window icon) and reel.ico
(16/32/48/64/128/256, for Explorer, the taskbar and the installer wizard).

Pure standard library, like Aperture's and Meridian's: zlib and struct write a
PNG, and an ICO is a small header around PNG frames. No Pillow, so this runs on
a bare runner.
"""

import math
import struct
import zlib
from pathlib import Path

BG = (0x0B, 0x0B, 0x0E, 255)        # near-black, as the player's own ground
REEL = (0xE6, 0xE8, 0xEC)           # the reel itself: film-stock grey-white
REEL_DIM = (0x8A, 0x8F, 0x99)       # its shaded side

SIZES = [16, 32, 48, 64, 128, 256]
HOLES = 5
ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "apps" / "Reel" / "Assets"


def coverage(px, py, inside):
    """Anti-aliasing by 3x3 supersampling: the fraction of a pixel inside the
    shape. At 16px this is the difference between a mark and a smudge."""
    hits = 0
    for sy in range(3):
        for sx in range(3):
            if inside(px + (sx + 0.5) / 3, py + (sy + 0.5) / 3):
                hits += 1
    return hits / 9


def blend(base, over, alpha):
    return tuple(round(b + (o - b) * alpha) for b, o in zip(base, over))


def render(size):
    """One frame of the icon, as RGBA rows."""
    c = size / 2
    outer = size * 0.42          # outer edge of the reel
    inner = size * 0.33          # inner edge of the rim
    hub = size * 0.10            # the spindle hub
    hole = size * 0.085          # the spoke holes
    ring = size * 0.235          # how far the holes sit from the centre

    holes = [(c + ring * math.cos(math.tau * i / HOLES - math.tau / 4),
              c + ring * math.sin(math.tau * i / HOLES - math.tau / 4))
             for i in range(HOLES)]

    def in_disc(x, y, cx, cy, r):
        return (x - cx) ** 2 + (y - cy) ** 2 <= r * r

    def in_body(x, y):
        """The reel: everything inside the outer edge that is not a spoke hole."""
        if not in_disc(x, y, c, c, outer):
            return False
        if in_disc(x, y, c, c, hub):
            return True
        for hx, hy in holes:
            if in_disc(x, y, hx, hy, hole):
                return False
        return True

    def in_rim(x, y):
        return in_disc(x, y, c, c, outer) and not in_disc(x, y, c, c, inner)

    rows = []
    for py in range(size):
        row = bytearray()
        for px in range(size):
            body = coverage(px, py, in_body)
            if body <= 0:
                row += bytes(BG)
                continue
            # The rim reads a shade darker, so the reel has an edge at 16px
            # rather than dissolving into one flat blob.
            rim = coverage(px, py, in_rim)
            colour = blend(REEL, REEL_DIM, rim)
            row += bytes(blend(BG[:3], colour, body)) + bytes([255])
        rows.append(bytes(row))
    return rows


def png(size, rows):
    raw = b"".join(b"\x00" + row for row in rows)

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def ico(frames):
    """An ICO holding PNG frames — what every Windows since Vista reads."""
    header = struct.pack("<HHH", 0, 1, len(frames))
    offset = len(header) + 16 * len(frames)
    entries, blobs = b"", b""
    for size, blob in frames:
        entries += struct.pack("<BBBBHHII", size % 256, size % 256, 0, 0, 1, 32, len(blob), offset)
        blobs += blob
        offset += len(blob)
    return header + entries + blobs


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    frames = []
    for size in SIZES:
        blob = png(size, render(size))
        frames.append((size, blob))
        if size == 256:
            (OUT_DIR / "reel.png").write_bytes(blob)
    (OUT_DIR / "reel.ico").write_bytes(ico(frames))
    print(f"wrote {OUT_DIR / 'reel.png'} and {OUT_DIR / 'reel.ico'}")


if __name__ == "__main__":
    main()
