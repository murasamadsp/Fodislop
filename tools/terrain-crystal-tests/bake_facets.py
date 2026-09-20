"""Bake discrete facet normals/material mask from the authored X-green sheet."""
from pathlib import Path
from PIL import Image
import math

ROOT = Path(__file__).resolve().parents[2]
TARGET = ROOT / 'Assets/Resources/XGreenFacets.bytes'


def bake():
    im = Image.open(ROOT / 'Assets/Textures/Cells/71.png').convert('RGB')
    assert im.size == (320, 320)
    w, h = im.size
    rgb = list(im.getdata())
    height = [g / 255 for r, g, b in rgb]
    # Partition authored color plateaus, then give each plateau a single plane.
    seen = set()
    result = bytearray(w*h*4)
    for seed in range(w*h):
        if seed in seen:
            continue
        color = rgb[seed]
        region = [seed]
        seen.add(seed)
        for i in region:
            x, y = i % w, i // w
            for xx, yy in ((x-1,y),(x+1,y),(x,y-1),(x,y+1)):
                j = (yy % h)*w + xx % w
                if j not in seen and rgb[j] == color:
                    seen.add(j)
                    region.append(j)
        gx = gy = 0.
        for i in region:
            x, y = i % w, i // w
            gx += height[y*w+(x+2)%w] - height[y*w+(x-2)%w]
            gy += height[((y-2)%h)*w+x] - height[((y+2)%h)*w+x]
        length = math.hypot(gx, gy)
        angle = round(math.atan2(-gy, -gx) / (math.pi/4)) * math.pi/4
        slope = .65 if length > .01 else 0.
        nx, ny = slope*math.cos(angle), slope*math.sin(angle)
        r, g, b = color
        # Restrict reflections to green mineral, excluding blue/dark matrix.
        mask = max(0., min(1., (g-max(r*.7,b)-12)/90)) * min(1., g/100)
        for i in region:
            x,y = i%w,i//w
            offset = ((h-1-y)*w+x)*4
            result[offset:offset+4] = bytes((round((nx*.5+.5)*255), round((ny*.5+.5)*255), round(mask*255),255))
    return bytes(result)


if __name__ == '__main__':
    TARGET.write_bytes(bake())
