"""Bake numerical periodic phase vectors, not a color texture. No source artwork."""
from pathlib import Path
import math

WIDTH, HEIGHT = 160, 128
TARGET = Path(__file__).resolve().parents[2] / 'Assets/Resources/PrismaticFlowMap.bytes'


def field(u, v):
    a, b = math.tau * u, math.tau * v
    # Integer harmonics close exactly across both boundaries. RG stores a phase
    # vector, avoiding hue-wrap interpolation and RGB->HSV work in the shader.
    phase = 2 * a + b + .65 * math.sin(a - 2 * b) + .3 * math.cos(3 * a + b)
    local = max(0., math.cos(3 * a - b) * math.cos(a + 4 * b)) ** 8
    return .5 + .5 * math.cos(phase), .5 + .5 * math.sin(phase), local, 1.


def bake():
    return bytes(round(c * 255) for y in range(HEIGHT) for x in range(WIDTH)
                 for c in field((x + .5) / WIDTH, (y + .5) / HEIGHT))


if __name__ == '__main__':
    TARGET.write_bytes(bake())
