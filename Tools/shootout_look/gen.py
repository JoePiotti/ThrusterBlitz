"""Thruster Blitz - "Night League" stadium look for the Shootout arena.

Theme: a pro arena sport at night. A dark navy court with painted lines and team end
zones, crisp white crash barriers carrying the league name, dark stadium steel for every
structure, amber safety lighting on every lip and drop, team colours on each base, and a
ring of stadium floodlights overhead.

Every texture here is meant for ONE kind of face, mapped by Hd2ShootoutLook (editor tool):
  fit      - the whole face shows the whole texture (court, logo walls, base decks)
  bands    - u repeats along the face in whole tiles, v spans the face bottom to top
  across   - u spans the face's width, v repeats along it in metres (ramp tread)
  metres   - planar, repeats every `tile` metres in both directions
Outputs <Name>_Albedo/_Normal/_Mask/_Emission PNGs into ./out.
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
os.makedirs(OUT, exist_ok=True)

FONT_LOGO = "/usr/share/fonts/opentype/inter/InterDisplay-BlackItalic.otf"
FONT_BOLD = "/usr/share/fonts/opentype/inter/InterDisplay-ExtraBoldItalic.otf"

# ---------------- palette ----------------
def C(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], dtype=np.float64) / 255.0

STEEL = C("2c323b")
STEEL_LIGHT = C("3f4752")
STEEL_DARK = C("1b1f26")
WHITE = C("e6e9ed")
WHITE_SHADE = C("c9ced5")
AMBER = C("ffae2b")
RED = C("ff3a30")
BLUE = C("2f7dff")
COURT = C("141b27")
LINE = C("eef1f4")
COOL_LIGHT = C("cfe8ff")
WARM_LIGHT = C("fff1d6")


# ---------------- helpers ----------------
def noise(w, h, cells, seed, octaves=4, gain=0.5):
    """Tileable fBm 0..1 of shape (h, w)."""
    r = np.random.default_rng(seed)
    total = np.zeros((h, w))
    amp, norm = 1.0, 0.0
    cx = max(1, int(round(cells * w / max(w, h))))
    cy = max(1, int(round(cells * h / max(w, h))))
    for _ in range(octaves):
        grid = r.random((cy, cx))
        up = ndimage.zoom(grid, (h / cy, w / cx), order=3, mode="grid-wrap")[:h, :w]
        total += up * amp
        norm += amp
        amp *= gain
        cx *= 2
        cy *= 2
        if cx > w or cy > h:
            break
    total /= norm
    total -= total.min()
    return total / max(total.max(), 1e-6)


def uv(w, h):
    y, x = np.mgrid[0:h, 0:w].astype(np.float64)
    return (x + 0.5) / w, 1.0 - (y + 0.5) / h  # u right, v UP (texture space)


def band(t, a, b, soft):
    """1 between a and b with soft edges (in t units)."""
    return np.clip((t - a) / soft + 0.5, 0, 1) * np.clip((b - t) / soft + 0.5, 0, 1)


def line(t, at, width, soft=None):
    soft = soft or width * 0.5
    return np.clip(1 - (np.abs(t - at) - width * 0.5) / soft, 0, 1)


def periodic_line(t, period, width, soft=None):
    f = (t / period) % 1.0
    d = np.minimum(f, 1 - f) * period
    soft = soft or width * 0.5
    return np.clip(1 - (d - width * 0.5) / soft, 0, 1)


def paint(base, color, amount):
    amount = np.clip(amount, 0, 1)[..., None]
    return base * (1 - amount) + np.asarray(color) * amount


def fill(h, w, color):
    return np.ones((h, w, 3)) * np.asarray(color)


def blur(a, s):
    return ndimage.gaussian_filter(a, s, mode="wrap")


def normal_from(hgt, strength):
    dx = (np.roll(hgt, -1, 1) - np.roll(hgt, 1, 1)) * 0.5
    dy = (np.roll(hgt, -1, 0) - np.roll(hgt, 1, 0)) * 0.5
    nx = -dx * strength
    ny = dy * strength  # rows go down, v goes up: OpenGL-style green
    nz = np.ones_like(hgt)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5


def save(name, kind, data, size=None):
    if kind == "mask":
        metal, smooth = data
        arr = np.zeros(metal.shape + (4,))
        arr[..., 0] = metal
        arr[..., 1] = 1
        arr[..., 2] = 0
        arr[..., 3] = smooth
        img = Image.fromarray((np.clip(arr, 0, 1) * 255).astype(np.uint8), "RGBA")
    else:
        img = Image.fromarray((np.clip(data, 0, 1) * 255).astype(np.uint8), "RGB")
    if size:
        img = img.resize(size, Image.LANCZOS)
    img.save(os.path.join(OUT, "%s_%s.png" % (name, kind.capitalize())))


def surface(name, albedo, height, strength, metal, smooth, emission=None, size=None, mask_size=None):
    save(name, "albedo", albedo, size)
    save(name, "normal", normal_from(height, strength), size)
    save(name, "mask", (metal, smooth), mask_size or size)
    if emission is not None:
        save(name, "emission", emission, size)


def text_mask(w, h, text, font_path, height_frac, center=(0.5, 0.5), rotate=0, tracking=0.0, max_w_frac=0.95):
    """Antialiased 0..1 mask of `text` on a w x h canvas (true aspect)."""
    canvas = Image.new("L", (w, h), 0)
    size = int(h * height_frac)
    font = ImageFont.truetype(font_path, size)
    # shrink to fit width
    while True:
        bbox = font.getbbox(text)
        tw = bbox[2] - bbox[0] + tracking * size * (len(text) - 1)
        if tw <= w * max_w_frac or size < 8:
            break
        size = int(size * 0.95)
        font = ImageFont.truetype(font_path, size)
    layer = Image.new("L", (int(tw) + size * 2, int(size * 1.6)), 0)
    d = ImageDraw.Draw(layer)
    x = size
    for ch in text:
        d.text((x, size * 0.1), ch, font=font, fill=255)
        cb = font.getbbox(ch)
        x += (cb[2] - cb[0]) + font.getlength(" ") * 0.08 + tracking * size
        if ch == " ":
            x += font.getlength(" ") * 0.6
    layer = layer.crop(layer.getbbox())
    if rotate:
        layer = layer.rotate(rotate, expand=True, resample=Image.BICUBIC)
    cx, cy = int(center[0] * w - layer.width / 2), int((1 - center[1]) * h - layer.height / 2)
    canvas.paste(layer, (cx, cy), layer)
    return np.asarray(canvas, dtype=np.float64) / 255.0


# =====================================================================
# 1. COURT FLOOR (fit to the floor face: x -20.32..21.26, z -30..30)
# =====================================================================
def court():
    X0, X1, Z0, Z1 = -20.32, 21.26, -30.0, 30.0
    CX, CZ = -0.2, 0.0
    W, H = 1400, 2020  # true aspect canvas (41.58 x 60 m), saved as 1024 x 2048
    u, v = uv(W, H)
    x = X0 + u * (X1 - X0)
    z = Z0 + v * (Z1 - Z0)
    px = (X1 - X0) / W  # metres per pixel

    big = noise(W, H, 6, 101, 5)
    alb = fill(H, W, COURT)
    alb *= (0.88 + 0.2 * big)[..., None]
    # team halves: a faint wash toward each end
    red_w = np.clip((z - 4) / 26, 0, 1) ** 1.5 * 0.10
    blue_w = np.clip((-z - 4) / 26, 0, 1) ** 1.5 * 0.10
    alb = paint(alb, RED * 0.5, red_w)
    alb = paint(alb, BLUE * 0.5, blue_w)
    # darker near the walls
    edge = np.minimum.reduce([x - X0, X1 - x, z - Z0, Z1 - z])
    alb *= (0.75 + 0.25 * np.clip(edge / 3, 0, 1))[..., None]

    lines = np.zeros((H, W))
    glow = np.zeros((H, W, 3))
    lw = 0.13
    # boundary 1 m in from the walls
    inset = 1.0
    bx0, bx1, bz0, bz1 = X0 + inset, X1 - inset, Z0 + inset, Z1 - inset
    inside = (x > bx0 - lw) & (x < bx1 + lw) & (z > bz0 - lw) & (z < bz1 + lw)
    bnd = (line(x, bx0, lw, px) + line(x, bx1, lw, px)) * ((z > bz0 - lw / 2) & (z < bz1 + lw / 2)) \
        + (line(z, bz0, lw, px) + line(z, bz1, lw, px)) * ((x > bx0 - lw / 2) & (x < bx1 + lw / 2))
    lines += bnd
    # centre line and circles
    lines += line(z, CZ, 0.16, px) * ((x > bx0) & (x < bx1))
    r = np.sqrt((x - CX) ** 2 + (z - CZ) ** 2)
    lines += line(r, 6.5, 0.16, px) + line(r, 3.6, 0.1, px)
    # tick marks around the outer circle
    ang = np.arctan2(z - CZ, x - CX)
    ticks = (periodic_line(ang, np.pi / 12, 0.02, 0.01) * band(r, 6.6, 7.3, px))
    lines += ticks
    # dashed lane lines
    for lx in (-13.0, 13.0):
        dash = ((z % 2.0) < 1.1).astype(float)
        lines += line(x, lx + CX, 0.12, px) * dash * (np.abs(z) < 14.5)
    # team zones in front of each base: z 15..22.25, x -10.5..10.5
    zone_glow_r = np.zeros((H, W))
    zone_glow_b = np.zeros((H, W))
    for sign, col, glow_acc in ((1, RED, zone_glow_r), (-1, BLUE, zone_glow_b)):
        zz = z * sign
        zx = np.abs(x - CX)
        inzone = (zz > 15.0) & (zz < 22.25) & (zx < 10.5)
        hatch = periodic_line((x - CX) + zz, 1.2, 0.35, px) * inzone
        alb = paint(alb, col * 0.55, hatch * 0.35)
        alb = paint(alb, col * 0.35, inzone * 0.18)
        border = (line(zz, 15.0, 0.18, px) * (zx < 10.6) + line(zx, 10.5, 0.18, px) * ((zz > 15.0) & (zz < 22.25)))
        alb = paint(alb, col, border)
        glow_acc += border
        # arc at the zone front ("key")
        rk = np.sqrt((x - CX) ** 2 + (zz - 15.0) ** 2)
        arc = line(rk, 4.0, 0.14, px) * (zz < 15.0)
        alb = paint(alb, col, arc)
        glow_acc += arc * 0.8
    # league name along both sidelines, reading along z (white, faded paint)
    name = text_mask(W, H, "THRUSTER BLITZ", FONT_LOGO, 0.045, center=((bx0 + 2.0 - X0) / (X1 - X0), 0.5), rotate=90, max_w_frac=0.95)
    name2 = text_mask(W, H, "THRUSTER BLITZ", FONT_LOGO, 0.045, center=((bx1 - 2.0 - X0) / (X1 - X0), 0.5), rotate=-90, max_w_frac=0.95)
    # keep the text inside the lane (between the boundary and the dashed lines); it is
    # tall, so scale it by height instead: done by the rotate+fit above
    letters = np.clip(name + name2, 0, 1)
    wear = noise(W, H, 40, 102, 4)
    letters *= (0.55 + 0.35 * wear)
    alb = paint(alb, LINE * 0.8, letters * 0.55)
    # team words in the zones, reading from the centre toward the base
    red_word = text_mask(W, H, "RED", FONT_LOGO, 0.03, center=((CX - X0) / (X1 - X0), (18.6 - Z0) / (Z1 - Z0)), rotate=180)
    blue_word = text_mask(W, H, "BLUE", FONT_LOGO, 0.03, center=((CX - X0) / (X1 - X0), (-18.6 - Z0) / (Z1 - Z0)))
    alb = paint(alb, RED, red_word * 0.85)
    alb = paint(alb, BLUE, blue_word * 0.85)
    zone_glow_r += red_word * 0.4
    zone_glow_b += blue_word * 0.4

    # centre emblem inside the small circle: a lighter disc and the TB monogram
    disc = band(r, -1, 3.5, px * 2)
    alb = paint(alb, C("1c2638"), disc * 0.9)
    mono = text_mask(W, H, "TB", FONT_LOGO, 0.048, center=((CX - X0) / (X1 - X0), (CZ - Z0) / (Z1 - Z0)), rotate=90)
    alb = paint(alb, C("33435c"), mono * 0.95)
    lines = np.clip(lines, 0, 1) * (0.85 + 0.15 * wear)
    alb = paint(alb, LINE, lines)
    glow = LINE * (lines * 0.35)[..., None] + RED * zone_glow_r[..., None] * 0.9 + BLUE * zone_glow_b[..., None] * 0.9
    smooth = 0.34 + 0.08 * big - 0.12 * lines - 0.06 * letters + 0.06 * mono
    metal = np.full((H, W), 0.05)
    save("Court", "albedo", alb, (1024, 2048))
    save("Court", "emission", glow, (1024, 2048))
    save("Court", "mask", (metal, smooth), (512, 1024))
    flat = np.full((64, 64, 3), 0.5)
    flat[..., 2] = 1.0
    save("Court", "normal", flat)


# Court detail (multiplied over the court): 2 m composite tiles, one texture = one tile.
def court_detail():
    N = 512
    u, v = uv(N, N)
    grain = noise(N, N, 16, 111, 5)
    speck = np.clip((noise(N, N, 128, 112, 2) - 0.5) * 4, -1, 1)
    g = 0.5 + 0.05 * (grain - 0.5) + 0.025 * speck
    save("CourtDetail", "albedo", np.repeat(g[..., None], 3, -1))
    hgt = 0.04 * grain + 0.01 * speck
    save("CourtDetail", "normal", normal_from(hgt, 3.0))


# =====================================================================
# 2. BARRIERS (white crash barriers): bands across the full face height
# =====================================================================
def barrier_bands(W, H, u, v, field_alb, field_h, field_glow=None):
    """Kick plate, amber line, white field, light line, cap."""
    alb = field_alb.copy()
    hgt = field_h.copy()
    glow = np.zeros((H, W, 3)) if field_glow is None else field_glow.copy()
    kick = band(v, -1, 0.11, 0.002)
    grooves = periodic_line(u * W / H * 2.0, 0.08, 0.012, 0.006)
    alb = paint(alb, STEEL * (0.9 + 0.2 * grooves[..., None] * 0), kick)
    hgt = hgt * (1 - kick) + kick * (0.2 + 0.05 * grooves)
    amber = band(v, 0.112, 0.126, 0.002)
    alb = paint(alb, AMBER, amber)
    glow += AMBER * amber[..., None] * 1.0
    hgt += amber * 0.05
    cap = band(v, 0.905, 2, 0.002)
    alb = paint(alb, STEEL, cap)
    hgt = hgt * (1 - cap) + cap * 0.35
    light = band(v, 0.93, 0.942, 0.002)
    alb = paint(alb, COOL_LIGHT, light)
    glow += COOL_LIGHT * light[..., None]
    chamfer = band(v, 0.985, 2, 0.003)
    alb = paint(alb, STEEL_LIGHT, chamfer)
    hgt += chamfer * 0.1
    return alb, hgt, glow


def barrier_side():
    W = H = 1024  # one 2 m panel across, full wall height
    u, v = uv(W, H)
    pad = 0.5 + 0.5 * np.cos(np.clip((u - 0.5) * 2, -1, 1) * np.pi / 2)
    pad = np.clip(np.sin(np.clip(u, 0, 1) * np.pi), 0, 1) ** 0.25 * np.clip(np.sin(np.clip((v - 0.126) / (0.905 - 0.126), 0, 1) * np.pi), 0, 1) ** 0.25
    grain = noise(W, H, 24, 201, 4)
    alb = fill(H, W, WHITE) * (0.93 + 0.08 * grain)[..., None]
    alb = paint(alb, WHITE_SHADE, (1 - pad) * 0.6)
    seam = line(u, 0, 0.006, 0.004) + line(u, 1, 0.006, 0.004)
    stitch = line(v, 0.52, 0.004, 0.002) * ((u * 60) % 1 < 0.6)
    alb = paint(alb, STEEL_DARK, seam)
    alb = paint(alb, WHITE_SHADE * 0.85, stitch)
    hgt = 0.6 + 0.4 * pad - seam * 0.6 - stitch * 0.1
    grime = np.clip((noise(W, H, 10, 202, 5) - 0.5) * 2, 0, 1) * np.clip(1 - (v - 0.12) * 3, 0, 1)
    alb = paint(alb, C("8a8580"), grime * 0.18)
    alb, hgt, glow = barrier_bands(W, H, u, v, alb, hgt)
    painted = band(v, 0.126, 0.905, 0.002)
    surface("BarrierSide", alb, hgt, 6.0, 0.05 + 0.5 * (1 - painted), 0.35 + 0.2 * painted, glow, mask_size=(512, 512))


def barrier_logo():
    # Mid walls' long faces: 4.94 m x 3 m. Designed at true aspect, saved square-ish.
    W, H = 1648, 1000
    u, v = uv(W, H)
    grain = noise(W, H, 24, 211, 4)
    alb = fill(H, W, WHITE) * (0.94 + 0.07 * grain)[..., None]
    hgt = np.full((H, W), 0.9) + 0.02 * grain
    # banner field: a dark slanted band with the league name
    slant = u + (v - 0.5) * 0.18
    banner = band(v, 0.33, 0.72, 0.004)
    alb = paint(alb, STEEL_DARK, banner)
    # amber swoosh on the left and team stripes at the right
    swoosh = band(slant, 0.03, 0.09, 0.003) * banner
    alb = paint(alb, AMBER, swoosh)
    r_stripe = band(slant, 0.905, 0.93, 0.003) * banner
    b_stripe = band(slant, 0.94, 0.965, 0.003) * banner
    alb = paint(alb, RED, r_stripe)
    alb = paint(alb, BLUE, b_stripe)
    name = text_mask(W, H, "THRUSTER BLITZ", FONT_LOGO, 0.27, center=(0.5, 0.525), max_w_frac=0.74)
    alb = paint(alb, WHITE, name)
    tag = text_mask(W, H, "NIGHT LEAGUE", FONT_BOLD, 0.065, center=(0.5, 0.25), max_w_frac=0.4)
    alb = paint(alb, STEEL, tag)
    glow = WHITE * (name * 0.55)[..., None] + AMBER * swoosh[..., None] + RED * r_stripe[..., None] + BLUE * b_stripe[..., None]
    hgt -= banner * 0.1
    edge = line(u, 0, 0.004, 0.003) + line(u, 1, 0.004, 0.003)
    alb = paint(alb, STEEL_DARK, edge)
    alb, hgt, glow = barrier_bands(W, H, u, v, alb, hgt, glow)
    painted = band(v, 0.126, 0.905, 0.002)
    surface("BarrierLogo", alb, hgt, 5.0, 0.05 + 0.5 * (1 - painted), 0.35 + 0.2 * painted + 0.15 * banner, glow,
            size=(2048, 1024), mask_size=(1024, 512))


def cube_side():
    # The centre monument's wide faces (about 4 x 4 m): the league badge.
    W = H = 1024
    u, v = uv(W, H)
    grain = noise(W, H, 24, 221, 4)
    alb = fill(H, W, WHITE) * (0.94 + 0.07 * grain)[..., None]
    hgt = np.full((H, W), 0.9)
    panel = band(u, 0.1, 0.9, 0.003) * band(v, 0.2, 0.86, 0.003)
    alb = paint(alb, STEEL_DARK, panel)
    hgt -= panel * 0.15
    frame = (line(u, 0.1, 0.012, 0.003) + line(u, 0.9, 0.012, 0.003)) * band(v, 0.2, 0.86, 0.003) \
        + (line(v, 0.2, 0.012, 0.003) + line(v, 0.86, 0.012, 0.003)) * band(u, 0.1, 0.9, 0.003)
    frame = np.clip(frame, 0, 1)
    alb = paint(alb, AMBER, frame)
    tb = text_mask(W, H, "TB", FONT_LOGO, 0.42, center=(0.5, 0.6), max_w_frac=0.7)
    alb = paint(alb, WHITE, tb)
    sub = text_mask(W, H, "THRUSTER BLITZ", FONT_BOLD, 0.06, center=(0.5, 0.31), max_w_frac=0.68)
    alb = paint(alb, AMBER, sub)
    red_bar = band(u, 0.2, 0.48, 0.003) * band(v, 0.245, 0.255, 0.002)
    blue_bar = band(u, 0.52, 0.8, 0.003) * band(v, 0.245, 0.255, 0.002)
    alb = paint(alb, RED, red_bar)
    alb = paint(alb, BLUE, blue_bar)
    glow = WHITE * (tb * 1.2)[..., None] + AMBER * (frame * 0.9 + sub * 0.8)[..., None] + RED * red_bar[..., None] + BLUE * blue_bar[..., None]
    alb, hgt, glow = barrier_bands(W, H, u, v, alb, hgt, glow)
    painted = band(v, 0.126, 0.905, 0.002)
    surface("CubeSide", alb, hgt, 5.0, 0.05 + 0.5 * (1 - painted), 0.4 + 0.25 * panel, glow, mask_size=(512, 512))


def barrier_top():
    # Tops of barriers, 1 m tiles: dark non-slip rubber with a raised diamond grid.
    N = 512
    u, v = uv(N, N)
    grid = np.clip(periodic_line(u + v, 0.125, 0.012, 0.01) + periodic_line(u - v, 0.125, 0.012, 0.01), 0, 1)
    grain = noise(N, N, 16, 231, 4)
    alb = fill(N, N, STEEL) * (0.85 + 0.2 * grain)[..., None]
    alb = paint(alb, STEEL_LIGHT, grid * 0.5)
    hgt = grid * 0.4 + 0.05 * grain
    surface("BarrierTop", alb, hgt, 3.0, np.full((N, N), 0.05), 0.25 + 0.1 * grid)


# =====================================================================
# 3. STRUCTURE STEEL
# =====================================================================
def steel_plain():
    N = 512
    u, v = uv(N, N)
    brushed = blur(noise(N, N, 64, 301, 3), (0.3, 5))
    alb = fill(N, N, STEEL) * (0.85 + 0.25 * brushed)[..., None]
    seam = line(u, 0, 0.005, 0.004) + line(u, 1, 0.005, 0.004) + line(v, 0, 0.005, 0.004) + line(v, 1, 0.005, 0.004)
    alb = paint(alb, STEEL_DARK, np.clip(seam, 0, 1))
    hgt = 1 - np.clip(seam, 0, 1) * 0.5 + brushed * 0.03
    surface("SteelPlain", alb, hgt, 3.0, np.full((N, N), 0.3), 0.32 + 0.1 * brushed)


def steel_fascia():
    # Ramp and structure sides, 2 m tiles: panels with a raised X brace and rivets.
    N = 1024
    u, v = uv(N, N)
    frame = np.clip(band(u, -1, 0.05, 0.004) + band(u, 0.95, 2, 0.004) + band(v, -1, 0.05, 0.004) + band(v, 0.95, 2, 0.004), 0, 1)
    brace = np.clip(line(u - v, 0, 0.05, 0.008) + line(u + v - 1, 0, 0.05, 0.008), 0, 1) * (1 - frame)
    rivets = np.zeros((N, N))
    for t in np.linspace(0.08, 0.92, 9):
        for (a, b) in ((t, 0.025), (t, 0.975), (0.025, t), (0.975, t)):
            d = np.sqrt((u - a) ** 2 + (v - b) ** 2) * N
            rivets += np.clip(1 - d / 6, 0, 1)
    grain = noise(N, N, 12, 311, 5)
    alb = fill(N, N, STEEL) * (0.85 + 0.2 * grain)[..., None]
    alb = paint(alb, STEEL_LIGHT, np.clip(frame + brace, 0, 1) * 0.8)
    alb = paint(alb, C("5a636f"), np.clip(rivets, 0, 1))
    grime = np.clip((noise(N, N, 8, 312, 5) - 0.55) * 2, 0, 1) * np.clip(1 - v * 1.5, 0, 1)
    alb = paint(alb, C("1a1c20"), grime * 0.4)
    hgt = 0.3 + 0.5 * np.clip(frame + brace, 0, 1) + 0.2 * np.clip(rivets, 0, 1)
    surface("SteelFascia", alb, hgt, 6.0, np.full((N, N), 0.3), 0.32 + 0.1 * grain - 0.15 * grime, mask_size=(512, 512))


def pillar(name, light_color):
    # Tall supports, 1 m wide x 2 m tall: ribbed steel with a light strip up the middle.
    W, H = 512, 1024
    u, v = uv(W, H)
    ribs = np.clip(0.5 + 0.5 * np.cos(u * 2 * np.pi * 6), 0, 1) ** 2
    channel = band(u, 0.42, 0.58, 0.004)
    strip = band(u, 0.47, 0.53, 0.003)
    joint = line(v, 0, 0.01, 0.004) + line(v, 1, 0.01, 0.004)
    bolts = np.zeros((H, W))
    for a in (0.15, 0.85):
        for b in (0.04, 0.96):
            d = np.sqrt(((u - a) * W) ** 2 + ((v - b) * H) ** 2)
            bolts += np.clip(1 - d / 9, 0, 1)
    grain = blur(noise(W, H, 32, 321, 3), (4, 0.3))
    alb = fill(H, W, STEEL) * (0.85 + 0.2 * grain + 0.1 * ribs)[..., None]
    alb = paint(alb, STEEL_DARK, channel)
    alb = paint(alb, light_color, strip)
    alb = paint(alb, STEEL_DARK, np.clip(joint, 0, 1))
    alb = paint(alb, C("5a636f"), np.clip(bolts, 0, 1))
    hgt = 0.6 + 0.3 * ribs * (1 - channel) - channel * 0.4 + strip * 0.2 - np.clip(joint, 0, 1) * 0.4 + 0.2 * np.clip(bolts, 0, 1)
    glow = light_color * strip[..., None]
    surface(name, alb, hgt, 5.0, np.full((H, W), 0.85) - strip * 0.8, 0.5 + 0.1 * ribs, glow)


def stadium_beam():
    # Ring beam faces (3 m tall), one floodlight panel every 3 m.
    W = H = 1024
    u, v = uv(W, H)
    flange = np.clip(band(v, -1, 0.12, 0.003) + band(v, 0.88, 2, 0.003), 0, 1)
    web_ribs = periodic_line(u, 1 / 6, 0.012, 0.006) * (1 - flange)
    housing = band(u, 0.22, 0.78, 0.004) * band(v, 0.3, 0.7, 0.004)
    lamp = band(u, 0.25, 0.75, 0.004) * band(v, 0.33, 0.67, 0.004)
    cells = (periodic_line(u, 0.0625, 0.01, 0.004) + periodic_line(v, 0.085, 0.01, 0.004)) * lamp
    lamp_lit = np.clip(lamp - cells, 0, 1)
    grain = noise(W, H, 12, 331, 4)
    alb = fill(H, W, STEEL) * (0.85 + 0.2 * grain)[..., None]
    alb = paint(alb, STEEL_DARK, flange * 0.6)
    alb = paint(alb, STEEL_LIGHT, web_ribs * 0.7)
    alb = paint(alb, STEEL_DARK, housing)
    alb = paint(alb, WARM_LIGHT, lamp_lit)
    # warm wash on the beam around each lamp
    wash = np.exp(-(((u - 0.5) / 0.35) ** 2 + ((v - 0.5) / 0.45) ** 2)) * (1 - housing)
    glow = WARM_LIGHT * (lamp_lit * 1.6)[..., None] + WARM_LIGHT * (wash * 0.05)[..., None]
    hgt = 0.5 + 0.3 * flange + 0.2 * web_ribs + 0.2 * housing - 0.1 * cells
    surface("StadiumBeam", alb, hgt, 5.0, np.full((H, W), 0.3) - lamp * 0.3, 0.34 + 0.4 * lamp, glow, mask_size=(512, 512))


# =====================================================================
# 4. WALKING SURFACES
# =====================================================================
def tread():
    # Ramp slopes: u spans the ramp width (about 3.1 m), v repeats every metre.
    W, H = 1550, 500  # true aspect 3.1 x 1 m
    u, v = uv(W, H)
    xm = u * 3.1
    ym = v * 1.0
    hazard = np.clip(band(u, -1, 0.09, 0.002) + band(u, 0.91, 2, 0.002), 0, 1)
    edge_light = np.clip(band(u, 0.093, 0.104, 0.001) + band(u, 0.896, 0.907, 0.001), 0, 1)
    plate = band(u, 0.105, 0.895, 0.001)
    # diamond plate: alternating raised lozenges, 8 per metre
    k = 8
    a = ((xm + ym) * k) % 1
    b = ((xm - ym) * k) % 1
    cell = (np.floor((xm + ym) * k) + np.floor((xm - ym) * k)) % 2
    lozA = np.clip(1 - (np.abs(a - 0.5) * 3.2 + np.abs(b - 0.5) * 0.7), 0, 1)
    lozB = np.clip(1 - (np.abs(b - 0.5) * 3.2 + np.abs(a - 0.5) * 0.7), 0, 1)
    loz = np.where(cell == 0, lozA, lozB)
    loz = np.clip(loz * 3, 0, 1)
    wear = noise(W, H, 12, 401, 5)
    stripes = (((xm * 2.5 + ym * 2.5) % 1) < 0.5).astype(float)
    alb = fill(H, W, C("858b93")) * (0.8 + 0.25 * wear)[..., None]
    alb = paint(alb, C("aeb3ba"), loz * plate * 0.6)
    haz = paint(fill(H, W, C("17181b")), AMBER, stripes)
    alb = paint(alb, haz, hazard)
    alb = paint(alb, AMBER, edge_light)
    worn = np.clip((wear - 0.6) * 3, 0, 1) * hazard
    alb = paint(alb, C("6e7076"), worn * 0.5)
    hgt = loz * plate * 0.8 + hazard * 0.3 + edge_light * 0.4 + 0.04 * wear
    glow = AMBER * edge_light[..., None] * 1.2 + AMBER * (hazard * stripes * 0.12)[..., None]
    surface("Tread", alb, hgt, 6.0, plate * 0.75, 0.28 + 0.3 * loz * plate * (1 - wear * 0.5), glow,
            size=(1024, 512), mask_size=(512, 256))


def walk_deck():
    # Flat walkways on the ramp structure: 1 m steel plates, bolted, anti-slip dots.
    N = 1024
    u, v = uv(N, N)
    seam = np.clip(line(u, 0, 0.008, 0.004) + line(u, 1, 0.008, 0.004) + line(v, 0, 0.008, 0.004) + line(v, 1, 0.008, 0.004), 0, 1)
    dots = np.clip(1 - (np.sqrt(((u * 24) % 1 - 0.5) ** 2 + ((v * 24) % 1 - 0.5) ** 2) - 0.18) / 0.06, 0, 1)
    bolts = np.zeros((N, N))
    for a in (0.04, 0.96):
        for b in (0.04, 0.96):
            d = np.sqrt((u - a) ** 2 + (v - b) ** 2) * N
            bolts += np.clip(1 - d / 10, 0, 1)
    wear = noise(N, N, 8, 411, 5)
    alb = fill(N, N, STEEL_LIGHT) * (0.82 + 0.25 * wear)[..., None]
    alb = paint(alb, C("596270"), dots * 0.5)
    alb = paint(alb, STEEL_DARK, seam)
    alb = paint(alb, C("6b7480"), np.clip(bolts, 0, 1))
    hgt = 0.6 + 0.2 * dots - 0.5 * seam + 0.3 * np.clip(bolts, 0, 1)
    surface("WalkDeck", alb, hgt, 5.0, np.full((N, N), 0.45), 0.3 + 0.2 * dots * (1 - wear), mask_size=(512, 512))


def grate():
    N = 1024
    u, v = uv(N, N)
    bars_u = periodic_line(u, 1 / 12, 0.012, 0.004)
    bars_v = periodic_line(v, 1 / 3, 0.006, 0.003)
    frame = np.clip(line(u, 0, 0.02, 0.004) + line(u, 1, 0.02, 0.004) + line(v, 0, 0.02, 0.004) + line(v, 1, 0.02, 0.004), 0, 1)
    gr = np.clip(bars_u + bars_v + frame, 0, 1)
    metal = fill(N, N, C("7d848d")) * (0.85 + 0.2 * noise(N, N, 16, 421, 4))[..., None]
    alb = paint(fill(N, N, C("0b0d10")), metal, gr)
    glow = AMBER * ((1 - gr) * 0.09)[..., None]
    hgt = gr + frame * 0.3
    surface("Grate", alb, hgt, 8.0, gr * 0.6, 0.2 + 0.3 * gr, glow, mask_size=(512, 512))


def edge_trim(name, color):
    # Lips of platforms and walkways (about 0.5 m tall): u every 1 m, v spans the face.
    W, H = 1024, 512
    u, v = uv(W, H)
    stripes = ((((u * 2.0) + (v * H / W) * 2.0 * 2) % 1) < 0.5).astype(float)
    chev = band(v, 0.18, 0.7, 0.003)
    light = band(v, 0.76, 0.86, 0.003)
    housing = band(v, 0.72, 0.9, 0.003)
    top = band(v, 0.9, 2, 0.003)
    alb = fill(H, W, STEEL)
    alb = paint(alb, paint(fill(H, W, C("15161a")), color, stripes), chev)
    alb = paint(alb, STEEL_DARK, housing)
    alb = paint(alb, color, light)
    alb = paint(alb, STEEL_LIGHT, top)
    hgt = 0.5 + 0.2 * chev - 0.2 * housing + 0.35 * light + 0.3 * top
    glow = color * (light * 1.3 + chev * stripes * 0.18)[..., None]
    surface(name, alb, hgt, 5.0, np.full((H, W), 0.3) - light * 0.3, 0.34 + 0.4 * light, glow, mask_size=(512, 256))


def base_deck(name, color, word):
    # Base platform tops: fit; v = 0 at the edge facing the arena. 24.7 x 7.75 m.
    W, H = 2048, 643
    u, v = uv(W, H)
    xm = u * 24.7
    ym = v * 7.75
    seams = np.clip(periodic_line(xm + 0.35, 2.0, 0.03, 0.02) + periodic_line(ym, 2.0, 0.03, 0.02), 0, 1)
    grain = noise(W, H, 24, 431 + len(word), 5)
    alb = fill(H, W, C("232831")) * (0.85 + 0.22 * grain)[..., None]
    alb = paint(alb, STEEL_DARK, seams * 0.45)
    # front lip: hazard band + light
    front_haz = band(ym, -1, 0.35, 0.02)
    stripes = (((xm + ym) * 1.6 % 1) < 0.5).astype(float)
    alb = paint(alb, paint(fill(H, W, C("15161a")), color, stripes), front_haz)
    front_light = band(ym, 0.38, 0.48, 0.01)
    alb = paint(alb, color, front_light)
    # three chevrons pointing at the arena, centred
    cx = 24.7 / 2
    chev = np.zeros((H, W))
    for i, y0 in enumerate((1.2, 2.2, 3.2)):
        d = ym - y0 - np.abs(xm - cx) * 0.4
        chev += band(d, 0, 0.45, 0.02) * (np.abs(xm - cx) < 2.6)
    chev = np.clip(chev, 0, 1)
    alb = paint(alb, color * 0.85, chev * 0.85)
    # team word near the back wall
    txt = text_mask(W, H, word + " BASE", FONT_LOGO, 0.11, center=(0.5, 0.9), max_w_frac=0.3)
    alb = paint(alb, color * 0.7, txt * 0.7)
    # side rails of light
    side = np.clip(band(xm, 0.15, 0.3, 0.02) + band(xm, 24.4, 24.55, 0.02), 0, 1) * (ym > 0.5)
    alb = paint(alb, color, side)
    glow = color * (front_light * 1.4 + chev * 0.35 + side * 1.0 + txt * 0.25 + front_haz * stripes * 0.15)[..., None]
    hgt = 0.6 - 0.4 * seams + 0.15 * front_haz - 0.05 * chev + 0.1 * grain * 0.3
    surface(name, alb, hgt, 4.0, np.full((H, W), 0.25), 0.3 + 0.1 * grain + 0.15 * chev, glow,
            size=(2048, 512), mask_size=(1024, 256))


def button():
    N = 1024
    u, v = uv(N, N)
    r = np.sqrt((u - 0.5) ** 2 + (v - 0.5) ** 2)
    ang = np.arctan2(v - 0.5, u - 0.5)
    rings = np.clip(line(r, 0.4, 0.02, 0.006) + line(r, 0.24, 0.01, 0.004), 0, 1)
    spokes = periodic_line(ang, np.pi / 4, 0.05, 0.02) * band(r, 0.26, 0.38, 0.004)
    knurl = periodic_line(u * 40, 1, 0.3) * 0.0
    grain = noise(N, N, 16, 441, 4)
    alb = fill(N, N, STEEL_DARK) * (0.85 + 0.2 * grain)[..., None]
    alb = paint(alb, WHITE, rings * 0.5 + spokes * 0.3)
    hgt = 0.7 - 0.4 * rings - 0.2 * spokes + 0.02 * grain
    glow = np.ones((N, N, 3)) * np.clip(rings + spokes * 0.5, 0, 1)[..., None]
    surface("Button", alb, hgt, 4.0, np.full((N, N), 0.9), np.full((N, N), 0.75), glow, mask_size=(256, 256))


# =====================================================================
# 5. v2 PIECES: columns, canopy, ribbon boards, ramp trim, support panels,
#    and the two barrier graphics (Low 45 badge, half-wall team boards)
# =====================================================================
def column(name, light_color):
    """Column face: u spans the whole face width, v repeats every 2 m.
    Chamfered edges, two flutes, and (when lit) one LED strip up the middle."""
    W, H = 512, 1024
    u, v = uv(W, H)
    grain = blur(noise(W, H, 32, 501, 3), (6, 0.3))
    alb = fill(H, W, STEEL) * (0.86 + 0.18 * grain)[..., None]
    edge = np.clip(band(u, -1, 0.03, 0.004) + band(u, 0.97, 2, 0.004), 0, 1)
    flutes = np.clip(band(u, 0.28, 0.31, 0.004) + band(u, 0.69, 0.72, 0.004), 0, 1)
    alb = paint(alb, STEEL_LIGHT, edge * 0.9)
    alb = paint(alb, STEEL_DARK, flutes * 0.8)
    hgt = 0.6 + 0.25 * edge - 0.3 * flutes + 0.03 * grain
    glow = np.zeros((H, W, 3))
    strip = np.zeros((H, W))
    metal = np.full((H, W), 0.3)
    smooth = 0.32 + 0.12 * edge + 0.05 * grain
    if light_color is not None:
        channel = band(u, 0.43, 0.57, 0.003)
        strip = band(u, 0.475, 0.525, 0.002)
        alb = paint(alb, STEEL_DARK * 0.6, channel)
        alb = paint(alb, light_color, strip)
        hgt += -0.45 * channel + 0.35 * strip
        halo = channel * (1 - strip) * 0.12
        glow = light_color * (strip + halo)[..., None]
        metal = metal * (1 - strip)
        smooth = smooth + strip * 0.45
    joint = np.clip(line(v, 0, 0.008, 0.003) + line(v, 1, 0.008, 0.003), 0, 1) * (1 - strip)
    bolts = np.zeros((H, W))
    for a in (0.14, 0.86):
        for b in (0.035, 0.965):
            d = np.sqrt(((u - a) * W) ** 2 + ((v - b) * H) ** 2)
            bolts += np.clip(1 - d / 7, 0, 1)
    alb = paint(alb, STEEL_DARK, joint)
    alb = paint(alb, C("5a636f"), np.clip(bolts, 0, 1))
    hgt += -0.35 * joint + 0.2 * np.clip(bolts, 0, 1)
    surface(name, alb, hgt, 5.0, metal, smooth, glow if light_color is not None else None, mask_size=(256, 512))


def canopy():
    """Underside of the roofs over the bases (and under the base decks): 3 m coffers,
    each with a warm downlight."""
    N = 1024
    u, v = uv(N, N)
    beam = np.clip(band(u, -1, 0.055, 0.003) + band(u, 0.945, 2, 0.003) + band(v, -1, 0.055, 0.003) + band(v, 0.945, 2, 0.003), 0, 1)
    lip = np.clip(band(u, 0.055, 0.075, 0.003) + band(u, 0.925, 0.945, 0.003) + band(v, 0.055, 0.075, 0.003) + band(v, 0.925, 0.945, 0.003), 0, 1) * (1 - beam)
    housing = band(u, 0.37, 0.63, 0.003) * band(v, 0.37, 0.63, 0.003)
    lamp = band(u, 0.4, 0.6, 0.003) * band(v, 0.4, 0.6, 0.003)
    cells = np.clip(periodic_line(u - 0.4, 0.05, 0.006, 0.003) + periodic_line(v - 0.4, 0.05, 0.006, 0.003), 0, 1) * lamp
    lit = np.clip(lamp - cells * 0.6, 0, 1)
    rr = np.sqrt((u - 0.5) ** 2 + (v - 0.5) ** 2)
    halo = np.exp(-(rr / 0.2) ** 2) * (1 - housing) * (1 - beam)
    grain = noise(N, N, 12, 511, 4)
    alb = fill(N, N, STEEL_DARK) * (0.9 + 0.15 * grain)[..., None]
    alb = paint(alb, STEEL, beam)
    alb = paint(alb, STEEL_LIGHT, lip * 0.8)
    alb = paint(alb, C("0c0e12"), housing)
    alb = paint(alb, WARM_LIGHT, lit)
    glow = WARM_LIGHT * (lit * 1.0 + halo * 0.06)[..., None]
    hgt = 0.3 + 0.5 * beam + 0.2 * lip + 0.15 * housing - 0.05 * cells
    surface("Canopy", alb, hgt, 5.0, np.full((N, N), 0.25) * (1 - lamp), 0.3 + 0.5 * lamp, glow, mask_size=(512, 512))


def diamond(w, h, cx, cy, r):
    y, x = np.mgrid[0:h, 0:w].astype(np.float64)
    d = np.abs(x - cx) + np.abs(y - cy) * 1.0
    return np.clip(r - d + 0.5, 0, 1)


def ribbon(name, color, word):
    """LED ribbon board along the front edge of each base roof (0.5 m tall).
    u repeats every 10 m: league name, a diamond, the team's base name."""
    W, H = 2560, 128
    u, v = uv(W, H)
    alb = fill(H, W, C("07080b"))
    rails = np.clip(band(v, 0.05, 0.13, 0.01) + band(v, 0.87, 0.95, 0.01), 0, 1)
    housing = np.clip(band(v, -1, 0.05, 0.01) + band(v, 0.95, 2, 0.01), 0, 1)
    t1 = text_mask(W, H, "THRUSTER BLITZ", FONT_LOGO, 0.5, center=(0.27, 0.5), max_w_frac=0.4)
    t2 = text_mask(W, H, word + " BASE", FONT_LOGO, 0.5, center=(0.74, 0.5), max_w_frac=0.3)
    dm = diamond(W, H, 0.5 * W, 0.5 * H, H * 0.16) + diamond(W, H, 0.02 * W, 0.5 * H, H * 0.12) + diamond(W, H, 0.98 * W, 0.5 * H, H * 0.12)
    dm = np.clip(dm, 0, 1)
    # LED pixels: a dot grid that the lit parts show through
    dots = np.clip(1 - (np.sqrt(((u * W / 4) % 1 - 0.5) ** 2 + ((v * H / 4) % 1 - 0.5) ** 2) - 0.28) / 0.12, 0, 1)
    lit_w = t1 * (0.35 + 0.65 * dots)
    lit_c = np.clip(t2 + dm, 0, 1) * (0.35 + 0.65 * dots)
    alb = paint(alb, C("1a1d24"), dots * 0.5)
    alb = paint(alb, WHITE, lit_w * 0.8)
    alb = paint(alb, color, lit_c * 0.9)
    alb = paint(alb, color, rails)
    alb = paint(alb, STEEL, housing)
    glow = WHITE * lit_w[..., None] * 0.9 + color * (lit_c * 1.1 + rails * 0.9)[..., None]
    hgt = 0.5 + 0.3 * housing + 0.1 * dots
    surface(name, alb, hgt, 3.0, np.full((H, W), 0.2), 0.55 - 0.2 * housing, glow, size=(2048, 128), mask_size=(512, 32))


def trim(name, color):
    """Long trim for the edges of the ramp slabs: u runs along the slab every 2 m,
    v spans its thickness. Dark steel with one segmented LED line down the middle."""
    W, H = 1024, 256
    u, v = uv(W, H)
    grain = blur(noise(W, H, 24, 521, 3), (0.3, 6))
    alb = fill(H, W, STEEL_DARK) * (0.9 + 0.15 * grain)[..., None]
    chamfer = np.clip(band(v, -1, 0.09, 0.006) + band(v, 0.91, 2, 0.006), 0, 1)
    channel = band(v, 0.36, 0.64, 0.006)
    led = band(v, 0.43, 0.57, 0.006)
    gaps = periodic_line(u, 1 / 16, 0.006, 0.002)
    led_lit = led * (1 - gaps)
    alb = paint(alb, STEEL_LIGHT, chamfer)
    alb = paint(alb, C("08090c"), channel)
    alb = paint(alb, color, led_lit)
    glow = color * (led_lit * 1.0 + channel * (1 - led) * 0.12)[..., None]
    hgt = 0.6 + 0.3 * chamfer - 0.4 * channel + 0.35 * led_lit
    surface(name, alb, hgt, 4.0, np.full((H, W), 0.3) * (1 - led), 0.32 + 0.2 * chamfer + 0.4 * led, glow, mask_size=(256, 64))


def steel_panel(name, light_color, word=None, word_color=None):
    """Support blocks under the ramp walkways: one framed panel per face (bands mapping).
    A light line near the top runs edge to edge so neighbouring faces join up; vents low
    on the right; optional stencilled word."""
    N = 1024
    u, v = uv(N, N)
    grain = noise(N, N, 14, 531, 5)
    alb = fill(N, N, STEEL) * (0.86 + 0.18 * grain)[..., None]
    frame = np.clip(band(u, -1, 0.045, 0.003) + band(u, 0.955, 2, 0.003) + band(v, 0.78, 0.83, 0.003) + band(v, 0.075, 0.11, 0.003), 0, 1)
    inset = band(u, 0.045, 0.955, 0.003) * band(v, 0.11, 0.78, 0.003)
    alb = paint(alb, STEEL_LIGHT, frame * 0.7)
    alb = paint(alb, STEEL * 0.8, inset * 0.6)
    kick = band(v, -1, 0.075, 0.003)
    alb = paint(alb, STEEL_DARK, kick)
    light = band(v, 0.86, 0.885, 0.002)
    housing = band(v, 0.845, 0.9, 0.002)
    cap = band(v, 0.9, 2, 0.003)
    alb = paint(alb, C("0b0d10"), housing)
    alb = paint(alb, light_color, light)
    alb = paint(alb, STEEL_LIGHT, cap * 0.6)
    vents = np.zeros((N, N))
    for k in range(6):
        y0 = 0.16 + k * 0.035
        vents += band(v, y0, y0 + 0.016, 0.003) * band(u, 0.62, 0.9, 0.003)
    vents = np.clip(vents, 0, 1)
    alb = paint(alb, C("0b0d10"), vents)
    rivets = np.zeros((N, N))
    for t in np.linspace(0.08, 0.92, 8):
        for (a, b) in ((t, 0.093), (t, 0.805), (0.0225, 0.12 + t * 0.66), (0.9775, 0.12 + t * 0.66)):
            d = np.sqrt((u - a) ** 2 + (v - b) ** 2) * N
            rivets += np.clip(1 - d / 5, 0, 1)
    alb = paint(alb, C("5a636f"), np.clip(rivets, 0, 1))
    stencil = np.zeros((N, N))
    if word:
        stencil = text_mask(N, N, word, FONT_LOGO, 0.2, center=(0.33, 0.47), max_w_frac=0.5)
        # stencil bridges
        stencil *= (1 - band((u * 18) % 1, 0.47, 0.53, 0.01) * 0)
        alb = paint(alb, word_color * 0.75, stencil * 0.85)
    grime = np.clip((noise(N, N, 8, 532, 5) - 0.55) * 2, 0, 1) * np.clip(1 - v * 2.2, 0, 1)
    alb = paint(alb, C("15171b"), grime * 0.35)
    hgt = 0.5 + 0.3 * frame - 0.15 * inset - 0.3 * vents - 0.2 * housing + 0.3 * light + 0.15 * np.clip(rivets, 0, 1)
    glow = light_color * (light * 1.2)[..., None] + (word_color * 0.0 if word_color is not None else 0)
    if word:
        glow = glow + word_color * (stencil * 0.12)[..., None]
    surface(name, alb, hgt, 5.0, np.full((N, N), 0.3) * (1 - light), 0.32 + 0.1 * frame + 0.4 * light - 0.1 * grime, glow,
            mask_size=(512, 512))


def white_field(W, H, u, v, seed, panels):
    """Padded white barrier field with vertical panel seams (`panels` across)."""
    grain = noise(W, H, 24, seed, 4)
    alb = fill(H, W, WHITE) * (0.93 + 0.08 * grain)[..., None]
    pu = (u * panels) % 1.0
    pad = np.clip(np.sin(pu * np.pi), 0, 1) ** 0.25 * np.clip(np.sin(np.clip((v - 0.126) / (0.905 - 0.126), 0, 1) * np.pi), 0, 1) ** 0.25
    alb = paint(alb, WHITE_SHADE, (1 - pad) * 0.6)
    seam = periodic_line(u * panels, 1.0, 0.012, 0.006)
    alb = paint(alb, STEEL_DARK, seam * 0.9)
    hgt = 0.6 + 0.4 * pad - seam * 0.6
    grime = np.clip((noise(W, H, 10, seed + 1, 5) - 0.5) * 2, 0, 1) * np.clip(1 - (v - 0.12) * 3, 0, 1)
    alb = paint(alb, C("8a8580"), grime * 0.18)
    return alb, hgt


def barrier_badge():
    """Low 45 barriers' long faces (3.69 x 2.45 m): the TB badge on padded white."""
    W, H = 1476, 980
    u, v = uv(W, H)
    alb, hgt = white_field(W, H, u, v, 541, 3)
    ar = W / H
    # rounded badge
    cx, cy, hw, hh, rad = 0.5, 0.52, 0.2, 0.3, 0.06
    dx = np.maximum(np.abs(u - cx) * ar - (hw * ar - rad), 0)
    dy = np.maximum(np.abs(v - cy) - (hh - rad), 0)
    rd = np.sqrt(dx ** 2 + dy ** 2)
    badge = np.clip((rad - rd) * H / 2 + 0.5, 0, 1)
    ring = np.clip(1 - np.abs(rd - rad + 0.012) * H / 6, 0, 1) * (rd < rad + 0.01)
    alb = paint(alb, STEEL_DARK, badge)
    alb = paint(alb, AMBER, ring)
    hgt = hgt * (1 - badge) + badge * 0.75
    tb = text_mask(W, H, "TB", FONT_LOGO, 0.34, center=(0.5, 0.6), max_w_frac=0.3)
    alb = paint(alb, WHITE, tb)
    sub = text_mask(W, H, "THRUSTER BLITZ", FONT_BOLD, 0.055, center=(0.5, 0.36), max_w_frac=0.3)
    alb = paint(alb, AMBER, sub)
    red_bar = band(u, 0.36, 0.495, 0.002) * band(v, 0.29, 0.305, 0.002)
    blue_bar = band(u, 0.505, 0.64, 0.002) * band(v, 0.29, 0.305, 0.002)
    alb = paint(alb, RED, red_bar)
    alb = paint(alb, BLUE, blue_bar)
    glow = WHITE * (tb * 0.9)[..., None] + AMBER * (ring * 0.9 + sub * 0.6)[..., None] + RED * red_bar[..., None] + BLUE * blue_bar[..., None]
    edge = line(u, 0, 0.004, 0.003) + line(u, 1, 0.004, 0.003)
    alb = paint(alb, STEEL_DARK, edge)
    alb, hgt, glow = barrier_bands(W, H, u, v, alb, hgt, glow)
    painted = band(v, 0.126, 0.905, 0.002)
    surface("BarrierBadge", alb, hgt, 5.0, 0.05 + 0.25 * (1 - painted), 0.3 + 0.15 * painted + 0.15 * badge, glow,
            size=(2048, 1024), mask_size=(512, 256))


def barrier_team(name, color, word):
    """Half walls by the side platforms (both long sides, about 6.1 x 3.5 m, the inner end
    sloping down to 2.3 m): which half of the arena you are in. Symmetric, so it reads the
    same from either side; kept low so the slope never cuts it."""
    W, H = 1744, 1000
    u, v = uv(W, H)
    alb, hgt = white_field(W, H, u, v, 551 + len(word), 4)
    lo, hi = 0.19, 0.55
    slope = 0.22
    left = u - (0.05 + (hi - v) * slope * 0.35)
    right = (0.95 - (hi - v) * slope * 0.35) - u
    plate = band(v, lo, hi, 0.003) * np.clip(left * W / 2, 0, 1) * np.clip(right * W / 2, 0, 1)
    alb = paint(alb, STEEL_DARK, plate)
    hgt = hgt * (1 - plate) + plate * 0.72
    slashes = np.zeros((H, W))
    for k in range(3):
        o = 0.075 + k * 0.03
        a = u - (o + (v - lo) * 0.12)
        b = (1 - u) - (o + (v - lo) * 0.12)
        slashes += (band(a, 0, 0.014, 0.002) + band(b, 0, 0.014, 0.002)) * band(v, lo + 0.03, hi - 0.03, 0.003)
    slashes = np.clip(slashes, 0, 1)
    alb = paint(alb, color, slashes)
    txt = text_mask(W, H, word, FONT_LOGO, 0.27, center=(0.5, 0.375), max_w_frac=0.5)
    alb = paint(alb, color, txt)
    edge_l = line(v, hi - 0.004, 0.006, 0.002) * plate + line(v, lo + 0.004, 0.006, 0.002) * plate
    alb = paint(alb, color * 0.8, edge_l)
    glow = color * (txt * 0.85 + slashes * 0.9 + edge_l * 0.6)[..., None]
    alb, hgt, glow = barrier_bands(W, H, u, v, alb, hgt, glow)
    painted = band(v, 0.126, 0.905, 0.002)
    surface(name, alb, hgt, 5.0, 0.05 + 0.25 * (1 - painted), 0.3 + 0.15 * painted + 0.1 * plate, glow,
            size=(2048, 1024), mask_size=(512, 256))


if __name__ == "__main__":
    import sys
    only = set(sys.argv[1:])
    jobs = [
        ("court", court), ("court_detail", court_detail),
        ("barrier_side", barrier_side), ("barrier_logo", barrier_logo), ("barrier_badge", barrier_badge),
        ("barrier_team", lambda: (barrier_team("BarrierTeamRed", RED, "RED"), barrier_team("BarrierTeamBlue", BLUE, "BLUE"))),
        ("barrier_top", barrier_top), ("steel_plain", steel_plain), ("steel_fascia", steel_fascia),
        ("column", lambda: (column("ColumnWhite", COOL_LIGHT), column("ColumnRed", RED), column("ColumnBlue", BLUE), column("ColumnPlain", None))),
        ("stadium_beam", stadium_beam), ("canopy", canopy),
        ("ribbon", lambda: (ribbon("RibbonRed", RED, "RED"), ribbon("RibbonBlue", BLUE, "BLUE"))),
        ("tread", tread), ("trim", lambda: trim("TrimAmber", AMBER)), ("walk_deck", walk_deck), ("grate", grate),
        ("steel_panel", lambda: (steel_panel("SteelPanel", COOL_LIGHT), steel_panel("SteelPanelRed", RED, "RED", RED),
                                 steel_panel("SteelPanelBlue", BLUE, "BLUE", BLUE))),
        ("edge", lambda: (edge_trim("EdgeAmber", AMBER), edge_trim("EdgeRed", RED), edge_trim("EdgeBlue", BLUE))),
        ("base_deck", lambda: (base_deck("BaseDeckRed", RED, "RED"), base_deck("BaseDeckBlue", BLUE, "BLUE"))),
        ("button", button),
    ]
    for key, fn in jobs:
        if not only or key in only:
            fn()
            print("made", key, flush=True)
    print("done", len(os.listdir(OUT)))
