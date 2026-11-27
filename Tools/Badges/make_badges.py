"""
Draws the ten player-level tier badges for Hd2Progression.

    python -I Tools/Badges/make_badges.py

Writes Assets/Resources/Badges/Tier01.png .. Tier10.png (512 x 512, transparent).
Tiers 1-3 are roundels, 4-6 shields, 7-9 spiked hexes, 10 (Blitz Master) a winged star.
Chevrons count up within each shape family; tiers 7-9 add a star, tier 10 a lightning bolt.
"""
import math
import os
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

SIZE = 512
SS = 4  # supersampling
S = SIZE * SS
C = S / 2

TIERS = [
    ("Cadet",        (150, 162, 176)),
    ("Pilot",        (84, 196, 116)),
    ("Thruster",     (56, 204, 228)),
    ("Striker",      (72, 124, 240)),
    ("Ace",          (152, 96, 236)),
    ("Vanguard",     (226, 76, 176)),
    ("Commander",    (232, 72, 62)),
    ("Elite",        (246, 150, 42)),
    ("Legend",       (250, 206, 64)),
    ("Blitz Master", (255, 226, 120)),
]

OUTLINE = (14, 16, 22, 255)


def mix(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def lighter(c, t):
    return mix(c, (255, 255, 255), t)


def darker(c, t):
    return mix(c, (0, 0, 0), t)


def scaled(points, k, cx=C, cy=C):
    return [(cx + (x - cx) * k, cy + (y - cy) * k) for x, y in points]


def circle(r):
    return [(C + r * math.cos(a / 180 * math.pi), C + r * math.sin(a / 180 * math.pi)) for a in range(0, 360, 2)]


def shield(r):
    # Heater shield: a gently arched top, straight sides, then curves meeting at the point.
    w = r * 0.88
    top = C - r * 0.86
    side_end = C + r * 0.05
    tip = C + r * 1.0
    pts = []
    for i in range(0, 21):
        t = i / 20
        x = C - w + 2 * w * t
        y = top - r * 0.07 * math.sin(math.pi * t)
        pts.append((x, y))
    pts.append((C + w, side_end))
    for i in range(1, 25):
        t = i / 24
        x = C + w * (1 - t) ** 1.0 * math.cos(t * math.pi / 2) ** 0.3
        y = side_end + (tip - side_end) * math.sin(t * math.pi / 2)
        pts.append((x, y))
    for i in range(23, 0, -1):
        t = i / 24
        x = C - w * (1 - t) ** 1.0 * math.cos(t * math.pi / 2) ** 0.3
        y = side_end + (tip - side_end) * math.sin(t * math.pi / 2)
        pts.append((x, y))
    pts.append((C - w, side_end))
    return pts


def spiked_hex(r):
    pts = []
    for i in range(12):
        a = math.pi / 6 * i - math.pi / 2
        rr = r if i % 2 == 0 else r * 0.80
        pts.append((C + rr * math.cos(a), C + rr * math.sin(a)))
    return pts


def star(r, points=8, inner=0.62, rot=-math.pi / 2):
    pts = []
    for i in range(points * 2):
        a = rot + math.pi / points * i
        rr = r if i % 2 == 0 else r * inner
        pts.append((C + rr * math.cos(a), C + rr * math.sin(a)))
    return pts


def feather(side, index):
    # One swept feather of a wing behind the star: side -1 (left) or 1 (right).
    # Feathers fan out from the star's edge; the top one is the longest.
    root_x = C + side * S * 0.17
    root_y = C - S * 0.05 + index * S * 0.075
    length = S * (0.31 - index * 0.05)
    angle = math.radians(-24 + index * 20)
    half = S * 0.075
    pts = []
    for i in range(0, 13):
        t = i / 12
        bulge = math.sin(math.pi * t) * half * (1 - 0.6 * t)
        x = root_x + side * (math.cos(angle) * length * t - math.sin(angle) * bulge)
        y = root_y + math.sin(angle) * length * t - math.cos(angle) * bulge
        pts.append((x, y))
    for i in range(12, -1, -1):
        t = i / 12
        bulge = math.sin(math.pi * t) * half * (1 - 0.6 * t)
        x = root_x + side * (math.cos(angle) * length * t + math.sin(angle) * bulge)
        y = root_y + math.sin(angle) * length * t + math.cos(angle) * bulge
        pts.append((x, y))
    return pts


def gradient_fill(mask, top, bottom):
    grad = Image.new("RGBA", (S, S))
    draw = ImageDraw.Draw(grad)
    for y in range(0, S, 4):
        c = mix(top, bottom, y / S)
        draw.rectangle([0, y, S, y + 4], fill=c + (255,))
    out = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    out.paste(grad, (0, 0), mask)
    return out


def polygon_mask(points):
    m = Image.new("L", (S, S), 0)
    ImageDraw.Draw(m).polygon(points, fill=255)
    return m


def chevron(draw, cy, w, h, thick, color):
    # An upward-pointing rank chevron: wings at cy, tip h above.
    pts = [(C - w, cy), (C, cy - h), (C + w, cy),
           (C + w, cy + thick), (C, cy - h + thick), (C - w, cy + thick)]
    draw.polygon(pts, fill=color)


def bolt(points_scale=1.0):
    k = S * 0.0021 * points_scale
    raw = [(10, -110), (-52, 14), (-6, 14), (-26, 110), (54, -22), (6, -22), (30, -110)]
    return [(C + x * k, C + y * k) for x, y in raw]


def draw_badge(index):
    name, color = TIERS[index]
    tier = index + 1
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    if tier <= 3:
        outer = circle(S * 0.44)
    elif tier <= 6:
        outer = shield(S * 0.46)
    elif tier <= 9:
        outer = spiked_hex(S * 0.47)
    else:
        outer = star(S * 0.34, points=8, inner=0.74, rot=-math.pi / 2)

    # Soft drop shadow.
    shadow = polygon_mask(scaled(outer, 1.0, C, C + S * 0.012)).filter(ImageFilter.GaussianBlur(S * 0.012))
    img.paste(Image.new("RGBA", (S, S), (0, 0, 0, 150)), (0, 0), shadow)

    # Blitz Master wings sit behind the star.
    if tier == 10:
        for side in (-1, 1):
            for f in (2, 1, 0):
                pts = feather(side, f)
                fm = polygon_mask(pts)
                img.paste(Image.new("RGBA", (S, S), OUTLINE), (0, 0), fm.filter(ImageFilter.MaxFilter(13)))
                img.alpha_composite(gradient_fill(fm, lighter(color, 0.45 - f * 0.1), darker(color, 0.3 + f * 0.05)))

    # Outline, metallic rim, then the colored face.
    outer_mask = polygon_mask(outer)
    rim_mask = polygon_mask(scaled(outer, 0.93))
    face_mask = polygon_mask(scaled(outer, 0.82))

    img.paste(Image.new("RGBA", (S, S), OUTLINE), (0, 0), outer_mask)
    rim_hi = lighter(color, 0.55) if tier < 9 else (255, 244, 200)
    img.alpha_composite(gradient_fill(rim_mask, rim_hi, darker(color, 0.25)))
    inner_ring = polygon_mask(scaled(outer, 0.85))
    img.paste(Image.new("RGBA", (S, S), darker(color, 0.55) + (255,)), (0, 0), inner_ring)
    img.alpha_composite(gradient_fill(face_mask, lighter(color, 0.12), darker(color, 0.42)))

    draw = ImageDraw.Draw(img)
    emblem = (250, 250, 252, 255)
    emblem_shadow = darker(color, 0.65) + (255,)

    # Chevrons: 1-3 within each family (tier 10 uses a bolt instead).
    if tier <= 9:
        count = (tier - 1) % 3 + 1
        w, h, thick, gap = S * 0.17, S * 0.09, S * 0.055, S * 0.085
        # Center the chevron stack (shields sit a little high: their point is low).
        middle = C + S * (0.0 if tier <= 3 else -0.03 if tier <= 6 else 0.05)
        base = middle + h / 2 + (count - 1) * gap / 2
        for i in range(count):
            cy = base - i * gap
            chevron(draw, cy + S * 0.008, w, h, thick, emblem_shadow)
            chevron(draw, cy, w, h, thick, emblem)
        if tier >= 7:
            top_of_stack = base - (count - 1) * gap - h
            st = star(S * 0.07, points=5, inner=0.45)
            st = [(x, y - (C - top_of_stack) - S * 0.1) for x, y in st]
            draw.polygon([(x, y + S * 0.008) for x, y in st], fill=emblem_shadow)
            draw.polygon(st, fill=emblem)
    else:
        b = bolt(0.95)
        draw.polygon([(x + S * 0.008, y + S * 0.01) for x, y in b], fill=emblem_shadow)
        draw.polygon(b, fill=emblem)
        draw.line(b + [b[0]], fill=OUTLINE, width=int(S * 0.006))

    # Gloss: a soft highlight over the top of the face.
    gloss = Image.new("L", (S, S), 0)
    ImageDraw.Draw(gloss).ellipse([C - S * 0.34, C - S * 0.56, C + S * 0.34, C - S * 0.06], fill=48)
    gloss = ImageChops.multiply(gloss.filter(ImageFilter.GaussianBlur(S * 0.07)), face_mask)
    white = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    white.putalpha(gloss)
    img.alpha_composite(white)

    return img.resize((SIZE, SIZE), Image.LANCZOS)


def main():
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
    out_dir = os.path.join(root, "Assets", "Resources", "Badges")
    os.makedirs(out_dir, exist_ok=True)
    sheet = Image.new("RGBA", (SIZE * 5, SIZE * 2), (24, 26, 32, 255))
    for i in range(len(TIERS)):
        badge = draw_badge(i)
        badge.save(os.path.join(out_dir, "Tier%02d.png" % (i + 1)))
        sheet.alpha_composite(badge, ((i % 5) * SIZE, (i // 5) * SIZE))
    if len(sys.argv) > 1:
        sheet.save(sys.argv[1])
    print("wrote", len(TIERS), "badges to", out_dir)


if __name__ == "__main__":
    main()
