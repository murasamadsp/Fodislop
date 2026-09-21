import os
import math
import random
from PIL import Image, ImageDraw, ImageFilter

OUTPUT_DIR = "Assets/Textures/UI"
os.makedirs(OUTPUT_DIR, exist_ok=True)

def generate_brand_logo(size=128):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    cx, cy = size / 2, size / 2
    r = size * 0.44

    # Hexagon points
    points = []
    for i in range(6):
        angle = math.radians(60 * i - 30)
        points.append((cx + r * math.cos(angle), cy + r * math.sin(angle)))

    # Outer gold hexagon outline
    draw.polygon(points, outline=(245, 197, 66, 255), width=4)

    # Inner cyber cube / crystal
    r_inner = r * 0.62
    inner_points = []
    for i in range(6):
        angle = math.radians(60 * i - 30)
        inner_points.append((cx + r_inner * math.cos(angle), cy + r_inner * math.sin(angle)))

    draw.polygon(inner_points, fill=(14, 22, 38, 240), outline=(86, 221, 212, 220), width=2)

    # Center gold diamond
    r_core = r * 0.28
    core_points = []
    for i in range(4):
        angle = math.radians(90 * i)
        core_points.append((cx + r_core * math.cos(angle), cy + r_core * math.sin(angle)))
    draw.polygon(core_points, fill=(245, 197, 66, 255))

    img.save(os.path.join(OUTPUT_DIR, "mm_logo.png"), "PNG")
    print("Generated cyber mm_logo.png")

def generate_clean_space_bg(width=1920, height=1080):
    img = Image.new("RGBA", (width, height), (3, 6, 10, 255))
    pixels = img.load()

    ncx, ncy = width * 0.74, height * 0.50
    nebula_rad = width * 0.45

    random.seed(4242)
    stars = []
    for _ in range(220):
        sx = random.randint(int(width * 0.15), width - 1)
        sy = random.randint(0, height - 1)
        brightness = random.choice([140, 180, 220, 255])
        size_pt = 1 if brightness < 220 else 2
        stars.append((sx, sy, brightness, size_pt))

    star_map = {}
    for sx, sy, b, sz in stars:
        for ox in range(sz):
            for oy in range(sz):
                if 0 <= sx + ox < width and 0 <= sy + oy < height:
                    star_map[(sx + ox, sy + oy)] = b

    for y in range(height):
        for x in range(width):
            t_vert = y / height
            base_r = int(3 * (1 - t_vert) + 1 * t_vert)
            base_g = int(6 * (1 - t_vert) + 2 * t_vert)
            base_b = int(10 * (1 - t_vert) + 4 * t_vert)

            # Soft ambient cyan nebula behind the menu scene
            d_nebula = math.hypot(x - ncx, y - ncy)
            if d_nebula < nebula_rad:
                t_nebula = (1.0 - (d_nebula / nebula_rad)) ** 2.0
                base_r = int(base_r + 14 * t_nebula)
                base_g = int(base_g + 48 * t_nebula)
                base_b = int(base_b + 56 * t_nebula)

            # Left shadow gradient (deep space left side)
            t_left = max(0.0, 1.0 - (x / (width * 0.55)))
            left_shade = t_left ** 1.5
            base_r = int(base_r * (1 - left_shade * 0.75) + 3 * left_shade * 0.75)
            base_g = int(base_g * (1 - left_shade * 0.75) + 6 * left_shade * 0.75)
            base_b = int(base_b * (1 - left_shade * 0.75) + 10 * left_shade * 0.75)

            if (x, y) in star_map:
                sb = star_map[(x, y)]
                base_r = min(255, base_r + sb)
                base_g = min(255, base_g + sb)
                base_b = min(255, base_b + sb)

            pixels[x, y] = (base_r, base_g, base_b, 255)

    img.save(os.path.join(OUTPUT_DIR, "mm_space_bg.png"), "PNG")
    print("Generated mm_space_bg.png")

if __name__ == "__main__":
    generate_brand_logo(128)
    generate_clean_space_bg(1920, 1080)
