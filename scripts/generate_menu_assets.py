import os
import math
import random
from PIL import Image, ImageDraw, ImageFilter

OUTPUT_DIR = "Assets/Textures/UI"
os.makedirs(OUTPUT_DIR, exist_ok=True)

def generate_exact_planet(size=1024):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    pixels = img.load()

    cx = size * 0.5
    cy = size * 0.5
    radius = size * 0.40
    atmo_outer = size * 0.48

    # Sun highlight origin (32% from left, 30% from top of planet bounding box)
    sx = cx - radius * 0.36
    sy = cy - radius * 0.40

    # Colors from web CSS
    # radial-gradient(circle at 32% 30%, #e07a48 0%, #b85028 46%, #4a1c0d 72%, #080f15 100%)
    c_highlight = (235, 142, 86)  # #eb8e56
    c_mid = (195, 96, 48)         # #c36030
    c_deep = (96, 32, 14)         # #60200e
    c_night = (3, 6, 10)          # #03060a deep shadow
    c_atmo = (112, 229, 221)      # #70e5dd glowing cyan

    # Multi-octave smooth noise for realistic planetary terrain banding
    random.seed(999)
    p_grid = [[random.random() for _ in range(64)] for _ in range(64)]

    def noise2d(u, v):
        gu = (u * 6) % 64
        gv = (v * 6) % 64
        x0, y0 = int(gu), int(gv)
        x1, y1 = (x0 + 1) % 64, (y0 + 1) % 64
        fx = gu - x0
        fy = gv - y0
        fx = fx * fx * (3 - 2 * fx)
        fy = fy * fy * (3 - 2 * fy)
        top = p_grid[y0][x0] * (1 - fx) + p_grid[y0][x1] * fx
        bottom = p_grid[y1][x0] * (1 - fx) + p_grid[y1][x1] * fx
        return top * (1 - fy) + bottom * fy

    for y in range(size):
        for x in range(size):
            dx = x - cx
            dy = y - cy
            dist = math.hypot(dx, dy)

            # Atmosphere corona outer glow
            if dist > radius:
                if dist <= atmo_outer:
                    t = (dist - radius) / (atmo_outer - radius)
                    # Smooth soft atmospheric falloff
                    alpha = int(255 * math.exp(-t * 4.5) * 0.9)
                    pixels[x, y] = (c_atmo[0], c_atmo[1], c_atmo[2], alpha)
                continue

            # Normalized distance from light highlight
            dist_sun = math.hypot(x - sx, y - sy) / (radius * 1.6)
            dist_sun = min(1.0, dist_sun)

            # Terrain texture modulation
            nx = dx / radius
            ny = dy / radius
            nz = math.sqrt(max(0.0, 1.0 - nx * nx - ny * ny))
            u_coord = (math.atan2(nx, nz) / math.pi) * 0.5 + 0.5
            v_coord = math.asin(max(-1.0, min(1.0, ny))) / math.pi + 0.5

            geo = noise2d(u_coord, v_coord) * 0.18

            # Color interpolation matching CSS gradient
            t = min(1.0, max(0.0, dist_sun + geo - 0.09))
            if t < 0.46:
                k = t / 0.46
                r = int(c_highlight[0] + (c_mid[0] - c_highlight[0]) * k)
                g = int(c_highlight[1] + (c_mid[1] - c_highlight[1]) * k)
                b = int(c_highlight[2] + (c_mid[2] - c_highlight[2]) * k)
            elif t < 0.72:
                k = (t - 0.46) / 0.26
                r = int(c_mid[0] + (c_deep[0] - c_mid[0]) * k)
                g = int(c_mid[1] + (c_deep[1] - c_mid[1]) * k)
                b = int(c_mid[2] + (c_deep[2] - c_deep[2]) * k)
            else:
                k = (t - 0.72) / 0.28
                r = int(c_deep[0] + (c_night[0] - c_deep[0]) * k)
                g = int(c_deep[1] + (c_night[1] - c_deep[1]) * k)
                b = int(c_deep[2] + (c_night[2] - c_deep[2]) * k)

            # Atmospheric Fresnel rim light (cyan glancing angle)
            fresnel = math.pow(1.0 - nz, 2.6)
            rim_factor = fresnel * 0.95
            r = int(r * (1 - rim_factor) + c_atmo[0] * rim_factor)
            g = int(g * (1 - rim_factor) + c_atmo[1] * rim_factor)
            b = int(b * (1 - rim_factor) + c_atmo[2] * rim_factor)

            # Thin bright 1.5px atmosphere rim line
            edge_dist = radius - dist
            if edge_dist < 3.0:
                edge_t = edge_dist / 3.0
                r = int(c_atmo[0] * (1 - edge_t) + r * edge_t)
                g = int(c_atmo[1] * (1 - edge_t) + g * edge_t)
                b = int(c_atmo[2] * (1 - edge_t) + b * edge_t)

            pixels[x, y] = (min(255, r), min(255, g), min(255, b), 255)

    img.save(os.path.join(OUTPUT_DIR, "mm_planet.png"), "PNG")
    print("Generated perfect mm_planet.png")

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

            # Soft ambient cyan nebula behind planet
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
    generate_exact_planet(1024)
    generate_brand_logo(128)
    generate_clean_space_bg(1920, 1080)
