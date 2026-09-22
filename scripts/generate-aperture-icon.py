#!/usr/bin/env python3
"""Regenerates Aperture's app icon from the Halide palette.

The mark is the thing the app is named after: an iris of six blades, drawn as a
ring with six straight chords wound around a hexagonal opening, in projector
amber on the Halide ground. Drawn here rather than committed as an opaque binary
so the colours stay tied to the theme — change the accent and re-run.

    python scripts/generate-aperture-icon.py

Writes apps/Aperture/Aperture/Assets/aperture.png (256px, for the runtime window
icon) and aperture.ico (16/32/48/64/128/256, for Explorer, the taskbar and an
installer).

Pure standard library, like Meridian's: zlib and struct are enough to write a
PNG, and an ICO is a small header around PNG frames. No Pillow, so this runs on
a bare runner.
"""

import math
import struct
import zlib
from pathlib import Path

BG = (0x0A, 0x0A, 0x0C, 255)        # halide-bg
ACCENT = (0xE8, 0xA3, 0x3D)         # halide-accent
ACCENT_DIM = (0x96, 0x63, 0x1B)     # halide-accent-700

BLADES = 6
SIZES = [16, 32, 48, 64, 128, 256]
ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "apps" / "Aperture" / "Aperture" / "Assets"


def coverage(px, py, inside):
    """Anti-aliasing by 3x3 supersampling: the fraction of a pixel inside the
    shape. At 16px this is the difference between a mark and a smudge."""
    hits = 0
    for sy in range(3):
        for sx in range(3):
            if inside(px + (sx + 0.5) / 3, py + (sy + 0.5) / 3):
                hits += 1
    return hits / 9


def render(size):
    """One RGBA frame. The geometry is in fractions of the canvas, so every size
    is the same drawing rather than a resample of one."""
    cx = cy = size / 2
    radius = size * 0.38
    stroke = max(size * 0.055, 1.0)
    # The opening: a hexagon a little over half the iris across, which is what an
    # aperture at a middle stop looks like.
    inner = radius * 0.46

    def on_ring(x, y):
        return abs(math.hypot(x - cx, y - cy) - radius) <= stroke / 2

    # Each blade runs from one corner of the opening out to the ring, offset by a
    # blade's turn — the wind that makes an iris read as an iris and not a wheel.
    blades = []
    for i in range(BLADES):
        a = 2 * math.pi * i / BLADES
        b = a + 2 * math.pi / BLADES
        blades.append((
            (cx + inner * math.cos(a), cy + inner * math.sin(a)),
            (cx + radius * math.cos(b), cy + radius * math.sin(b)),
        ))

    def on_blade(x, y):
        for (x1, y1), (x2, y2) in blades:
            if point_to_segment(x, y, x1, y1, x2, y2) <= stroke / 2:
                return True
        return False

    def on_opening(x, y):
        # The hexagon's own edges, dimmer: the hole has a rim, not a void.
        for i in range(BLADES):
            a = 2 * math.pi * i / BLADES
            b = 2 * math.pi * (i + 1) / BLADES
            x1, y1 = cx + inner * math.cos(a), cy + inner * math.sin(a)
            x2, y2 = cx + inner * math.cos(b), cy + inner * math.sin(b)
            if point_to_segment(x, y, x1, y1, x2, y2) <= stroke / 2:
                return True
        return False

    pixels = bytearray()
    for y in range(size):
        pixels.append(0)  # PNG filter byte: none
        for x in range(size):
            ring = coverage(x, y, on_ring)
            blade = coverage(x, y, on_blade)
            opening = coverage(x, y, on_opening)

            r, g, b, a = BG
            if opening > 0:
                r, g, b = blend((r, g, b), ACCENT_DIM, opening)
                a = max(a, int(255 * opening))
            strong = max(ring, blade)
            if strong > 0:
                r, g, b = blend((r, g, b), ACCENT, strong)
                a = 255
            pixels += bytes((r, g, b, a))
    return bytes(pixels)


def point_to_segment(px, py, x1, y1, x2, y2):
    dx, dy = x2 - x1, y2 - y1
    length = dx * dx + dy * dy
    if length == 0:
        return math.hypot(px - x1, py - y1)
    t = max(0.0, min(1.0, ((px - x1) * dx + (py - y1) * dy) / length))
    return math.hypot(px - (x1 + t * dx), py - (y1 + t * dy))


def blend(base, over, alpha):
    return tuple(round(b + (o - b) * alpha) for b, o in zip(base, over))


def png(size, raw):
    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body))

    header = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)  # 8-bit RGBA
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", header)
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def ico(frames):
    """An ICO holding PNG-compressed frames — supported since Vista, and how
    every modern icon ships 256px without a 256 KB bitmap."""
    count = len(frames)
    header = struct.pack("<HHH", 0, 1, count)
    offset = 6 + 16 * count
    entries, blobs = b"", b""
    for size, blob in frames:
        entries += struct.pack("<BBBBHHII",
                               0 if size >= 256 else size, 0 if size >= 256 else size,
                               0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
        blobs += blob
    return header + entries + blobs


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    frames = []
    for size in SIZES:
        blob = png(size, render(size))
        frames.append((size, blob))
        if size == 256:
            (OUT_DIR / "aperture.png").write_bytes(blob)
    (OUT_DIR / "aperture.ico").write_bytes(ico(frames))
    print(f"wrote {OUT_DIR / 'aperture.png'} and {OUT_DIR / 'aperture.ico'} "
          f"({', '.join(str(s) for s in SIZES)})")


if __name__ == "__main__":
    main()
