#!/usr/bin/env python3
"""Regenerates Meridian's app icon from the Azimuth palette.

The mark is the meridian line itself: a cyan circle crossed by the vertical
great-circle arc a meridian traces on a globe, on the Azimuth ground. Drawn
here rather than committed as an opaque binary so the colours stay tied to the
theme — change az-accent-500 and re-run.

    python scripts/generate-meridian-icon.py

Writes apps/Meridian/Meridian/Assets/meridian.png (256px, for the runtime
window icon) and meridian.ico (16/32/48/64/128/256, for Explorer, the taskbar
and the installer).

Pure standard library on purpose: zlib and struct are enough to write a PNG,
and an ICO is a small header around PNG frames. No Pillow, so this runs on a
bare runner.
"""

import math
import struct
import zlib
from pathlib import Path

BG = (0x0C, 0x11, 0x18, 255)        # az-bg
ACCENT = (0x38, 0xC5, 0xD8)         # az-accent-500
ACCENT_DIM = (0x1E, 0x7E, 0x8D)     # az-accent-700

SIZES = [16, 32, 48, 64, 128, 256]
ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "apps" / "Meridian" / "Meridian" / "Assets"


def coverage(px, py, inside):
    """Anti-aliasing by 3x3 supersampling: the fraction of a pixel inside the
    shape. Cheap, and at 16px the difference between this and a hard edge is
    the difference between a mark and a smudge."""
    hits = 0
    for sy in range(3):
        for sx in range(3):
            if inside(px + (sx + 0.5) / 3, py + (sy + 0.5) / 3):
                hits += 1
    return hits / 9


def render(size):
    """One RGBA frame. The geometry is expressed in fractions of the canvas so
    every size is the same drawing, not a resample."""
    cx = cy = size / 2
    radius = size * 0.36
    stroke = max(size * 0.055, 1.0)
    # The meridian: an ellipse as wide as the globe is tall, squeezed to 38%.
    meridian_rx = radius * 0.38

    def on_circle(x, y):
        d = math.hypot(x - cx, y - cy)
        return abs(d - radius) <= stroke / 2

    def on_meridian(x, y):
        if meridian_rx <= 0:
            return False
        nx = (x - cx) / meridian_rx
        ny = (y - cy) / radius
        d = math.hypot(nx, ny)
        # Convert the normalised distance back to pixels along the x axis so
        # the stroke stays even instead of pinching at the poles.
        return abs(d - 1.0) * meridian_rx <= stroke / 2

    def on_equator(x, y):
        return abs(y - cy) <= stroke / 2 and abs(x - cx) <= radius

    pixels = bytearray()
    for y in range(size):
        pixels.append(0)  # PNG filter byte: none
        for x in range(size):
            circle = coverage(x, y, on_circle)
            meridian = coverage(x, y, on_meridian)
            equator = coverage(x, y, on_equator)

            # The circle and the meridian are the accent; the equator is the
            # dimmer ramp step, so the mark reads as a globe and not a target.
            r, g, b, a = BG
            if equator > 0:
                r, g, b = blend((r, g, b), ACCENT_DIM, equator)
                a = max(a, int(255 * equator))
            strong = max(circle, meridian)
            if strong > 0:
                r, g, b = blend((r, g, b), ACCENT, strong)
                a = 255
            pixels += bytes((r, g, b, a))
    return bytes(pixels)


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
    """An ICO holding PNG-compressed frames — supported since Vista and how
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
            (OUT_DIR / "meridian.png").write_bytes(blob)
    (OUT_DIR / "meridian.ico").write_bytes(ico(frames))
    print(f"wrote {OUT_DIR / 'meridian.png'} and {OUT_DIR / 'meridian.ico'} "
          f"({', '.join(str(s) for s in SIZES)})")


if __name__ == "__main__":
    main()
