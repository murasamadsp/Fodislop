import os
import math
from PIL import Image, ImageDraw

OUTPUT_DIR = "Assets/Textures/UI"
os.makedirs(OUTPUT_DIR, exist_ok=True)

def create_icon(filename, draw_func, color=(86, 221, 212, 255), size=128):
    # Render at 4x (512x512) and downsample with Lanczos for super-crisp antialiased edges
    hi_size = size * 4
    img = Image.new("RGBA", (hi_size, hi_size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    draw_func(draw, hi_size, color)

    img = img.resize((size, size), Image.Resampling.LANCZOS)
    img.save(os.path.join(OUTPUT_DIR, filename), "PNG")
    print(f"Generated {filename}")

def draw_chronicle(draw, s, c):
    pad = s * 0.22
    # Document outline
    x0, y0, x1, y1 = pad, pad * 0.8, s - pad, s - pad * 0.8
    draw.rounded_rectangle([x0, y0, x1, y1], radius=s * 0.05, outline=c, width=int(s * 0.04))
    # Lines
    lw = int(s * 0.035)
    draw.line([pad + s * 0.1, y0 + s * 0.18, x1 - s * 0.1, y0 + s * 0.18], fill=c, width=lw)
    draw.line([pad + s * 0.1, y0 + s * 0.34, x1 - s * 0.1, y0 + s * 0.34], fill=c, width=lw)
    draw.line([pad + s * 0.1, y0 + s * 0.50, x1 - s * 0.22, y0 + s * 0.50], fill=c, width=lw)

def draw_settings(draw, s, c):
    cx, cy = s / 2, s / 2
    r_outer = s * 0.34
    r_inner = s * 0.24
    r_hole = s * 0.12
    teeth = 6
    # Gear teeth
    for i in range(teeth):
        angle = (2 * math.pi / teeth) * i
        # Draw tooth rect
        tx = cx + (r_outer + s * 0.04) * math.cos(angle)
        ty = cy + (r_outer + s * 0.04) * math.sin(angle)
        draw.circle((tx, ty), radius=s * 0.07, fill=c)
    # Outer circle
    draw.circle((cx, cy), radius=r_outer, fill=c)
    # Inner hole
    draw.circle((cx, cy), radius=r_hole, fill=(0, 0, 0, 0))

def draw_repair(draw, s, c):
    # Wrench
    cx, cy = s / 2, s / 2
    w = int(s * 0.05)
    # Diagonal handle
    draw.line([s * 0.28, s * 0.72, s * 0.62, s * 0.38], fill=c, width=int(s * 0.09))
    # Top head
    draw.circle((s * 0.68, s * 0.32), radius=s * 0.16, outline=c, width=int(s * 0.07))
    # Cutout
    draw.line([s * 0.68, s * 0.32, s * 0.82, s * 0.18], fill=(0, 0, 0, 0), width=int(s * 0.09))
    # Bottom head
    draw.circle((s * 0.26, s * 0.74), radius=s * 0.08, fill=c)

def draw_update(draw, s, c):
    # Notification Bell
    cx, cy = s / 2, s / 2
    gold = (245, 197, 66, 255)
    # Bell dome
    w = int(s * 0.045)
    draw.arc([s * 0.28, s * 0.24, s * 0.72, s * 0.68], start=180, end=0, fill=gold, width=w)
    draw.line([s * 0.28, s * 0.46, s * 0.22, s * 0.66], fill=gold, width=w)
    draw.line([s * 0.72, s * 0.46, s * 0.78, s * 0.66], fill=gold, width=w)
    draw.line([s * 0.18, s * 0.66, s * 0.82, s * 0.66], fill=gold, width=w)
    # Clapper
    draw.circle((cx, s * 0.75), radius=s * 0.06, fill=gold)

def draw_discord(draw, s, c):
    cx, cy = s / 2, s / 2
    # Controller shape
    pad_x = s * 0.20
    pad_y = s * 0.28
    draw.rounded_rectangle([pad_x, pad_y, s - pad_x, s - pad_y], radius=s * 0.12, outline=c, width=int(s * 0.045))
    # Eyes
    draw.circle((cx - s * 0.12, cy), radius=s * 0.05, fill=c)
    draw.circle((cx + s * 0.12, cy), radius=s * 0.05, fill=c)

def draw_telegram(draw, s, c):
    # Paper plane
    pts = [
        (s * 0.80, s * 0.22), # Top right tip
        (s * 0.22, s * 0.52), # Left
        (s * 0.44, s * 0.60), # Bottom inner
        (s * 0.52, s * 0.78), # Bottom tip
        (s * 0.62, s * 0.62), # Fold
    ]
    draw.polygon([pts[0], pts[1], pts[2]], fill=c)
    draw.polygon([pts[0], pts[2], pts[3]], fill=(int(c[0]*0.8), int(c[1]*0.8), int(c[2]*0.8), 255))
    draw.polygon([pts[0], pts[3], pts[4]], fill=c)

def draw_vk(draw, s, c):
    cx, cy = s / 2, s / 2
    # Shield outline
    pad = s * 0.22
    draw.rounded_rectangle([pad, pad, s - pad, s - pad], radius=s * 0.08, outline=c, width=int(s * 0.045))
    # Stylized VK letter
    lw = int(s * 0.05)
    draw.line([s * 0.38, s * 0.32, s * 0.38, s * 0.68], fill=c, width=lw)
    draw.line([s * 0.38, s * 0.50, s * 0.62, s * 0.32], fill=c, width=lw)
    draw.line([s * 0.44, s * 0.46, s * 0.62, s * 0.68], fill=c, width=lw)

def draw_exit(draw, s, c):
    red = (255, 85, 85, 255)
    # Door frame
    lw = int(s * 0.045)
    draw.line([s * 0.54, s * 0.22, s * 0.24, s * 0.22], fill=red, width=lw)
    draw.line([s * 0.24, s * 0.22, s * 0.24, s * 0.78], fill=red, width=lw)
    draw.line([s * 0.24, s * 0.78, s * 0.54, s * 0.78], fill=red, width=lw)
    # Arrow pointing right
    draw.line([s * 0.40, s * 0.50, s * 0.76, s * 0.50], fill=red, width=lw)
    draw.line([s * 0.62, s * 0.36, s * 0.76, s * 0.50], fill=red, width=lw)
    draw.line([s * 0.62, s * 0.64, s * 0.76, s * 0.50], fill=red, width=lw)

if __name__ == "__main__":
    c_cyan = (86, 221, 212, 255)
    c_gold = (245, 197, 66, 255)
    c_red = (255, 85, 85, 255)

    create_icon("mm_icon_chronicle.png", draw_chronicle, c_cyan)
    create_icon("mm_icon_settings.png", draw_settings, c_cyan)
    create_icon("mm_icon_repair.png", draw_repair, c_cyan)
    create_icon("mm_icon_update.png", draw_update, c_gold)
    create_icon("mm_icon_discord.png", draw_discord, c_cyan)
    create_icon("mm_icon_telegram.png", draw_telegram, c_cyan)
    create_icon("mm_icon_vk.png", draw_vk, c_cyan)
    create_icon("mm_icon_exit.png", draw_exit, c_red)
