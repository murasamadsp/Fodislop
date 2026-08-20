#!/usr/bin/env python3
import math
import struct
import sys
import zlib

sys.path.insert(0, ".")
from starfield_sim import collect_stars, splat, W, H


def render_png(path, density, core, glow, twinkle, brightness, sky=(0.012, 0.018, 0.032)):
    f = collect_stars(density, core, 0.0, twinkle)
    b = collect_stars(density * 0.43, glow * 0.22, 41.7, twinkle)
    buf, gw, gh = splat(density, f, stride=2)
    buf2, gw2, gh2 = splat(density * 0.43, b, stride=2)
    stride = 2
    # build RGB image: each pixel samples its stride-block in the buffers
    rgb = bytearray(W * H * 3)
    def enc(v):
        v = max(0.0, min(1.0, v))
        if v <= 0.0031308:
            return int(v * 12.92 * 255)
        return int((1.055 * (v ** (1 / 2.4)) - 0.055) * 255)
    for y in range(H):
        gy = min(y // stride, gh - 1)
        for x in range(W):
            gx = min(x // stride, gw - 1)
            i = gy * gw + gx
            lum = (buf[i] + buf2[i] * 1.6) * brightness
            r = sky[0] + lum
            g = sky[1] + lum
            b = sky[2] + lum
            off = (y * W + x) * 3
            rgb[off] = enc(r)
            rgb[off + 1] = enc(g)
            rgb[off + 2] = enc(b)
    # PNG encode (RGB 8-bit)
    raw = bytearray()
    for y in range(H):
        raw += b"\x00" + rgb[y * W * 3:(y + 1) * W * 3]
    def chunk(typ, data):
        c = struct.pack(">I", len(data)) + typ + data
        c += struct.pack(">I", zlib.crc32(typ + data) & 0xFFFFFFFF)
        return c
    ihdr = struct.pack(">IIBBBBB", W, H, 8, 2, 0, 0, 0)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(bytes(raw), 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as fh:
        fh.write(png)
    print(f"wrote {path} ({W}x{H})")


if __name__ == "__main__":
    render_png("starfield_current.png", 68, 0.020, 0.16, 0.45, 1.0)
    render_png("starfield_cand96.png", 96, 0.010, 0.06, 0.30, 1.4)
    render_png("starfield_cand84.png", 84, 0.012, 0.08, 0.35, 1.2)
