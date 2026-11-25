"""Procedural, tileable textures for the Shootout arena (Thruster Blitz).

Each surface gets: _Albedo (sRGB), _Normal (OpenGL tangent space), _Mask (R metallic,
A smoothness, linear) and, where it glows, _Emission (sRGB).
"""
import os
import numpy as np
from PIL import Image
from scipy import ndimage

OUT = os.path.join(os.path.dirname(__file__), "out")
os.makedirs(OUT, exist_ok=True)
N = 1024
rng = np.random.default_rng(7)


# ---------- helpers ----------
def noise(n, cells, seed, octaves=4, gain=0.5):
    """Tileable fBm in 0..1 (value noise upscaled with wrap)."""
    r = np.random.default_rng(seed)
    total = np.zeros((n, n))
    amp, norm = 1.0, 0.0
    for o in range(octaves):
        c = cells * (2 ** o)
        if c > n:
            break
        grid = r.random((c, c))
        up = ndimage.zoom(grid, n / c, order=3, mode="grid-wrap")[:n, :n]
        total += up * amp
        norm += amp
        amp *= gain
    total /= norm
    total -= total.min()
    return total / max(total.max(), 1e-6)


def blur(a, s):
    return ndimage.gaussian_filter(a, s, mode="wrap")


def grid_coords(n=N):
    y, x = np.mgrid[0:n, 0:n].astype(np.float64)
    return x / n, y / n  # u right, v down (image space)


def seam(coord, cells, width):
    """1 on lines every 1/cells, soft edge, 0 elsewhere."""
    f = (coord * cells) % 1.0
    d = np.minimum(f, 1 - f) / cells  # distance to nearest line in uv
    return np.clip(1 - d / width, 0, 1)


def bevel(coord, cells, width):
    """Height falling into seams: 0 at the seam, 1 inside the panel."""
    f = (coord * cells) % 1.0
    d = np.minimum(f, 1 - f) / cells
    return np.clip(d / width, 0, 1)


def normal_map(h, strength):
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    dy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    nx = -dx * strength
    ny = dy * strength
    nz = np.ones_like(h)
    l = np.sqrt(nx * nx + ny * ny + nz * nz)
    rgb = np.stack([nx / l, ny / l, nz / l], -1) * 0.5 + 0.5
    return (rgb * 255).clip(0, 255).astype(np.uint8)


def save_rgb(name, rgb):
    Image.fromarray((np.clip(rgb, 0, 1) * 255).astype(np.uint8), "RGB").save(os.path.join(OUT, name + ".png"))


def save_mask(name, metal, smooth):
    m = np.zeros((N, N, 4))
    m[..., 0] = metal
    m[..., 1] = 1.0  # occlusion-ish (unused)
    m[..., 3] = smooth
    Image.fromarray((np.clip(m, 0, 1) * 255).astype(np.uint8), "RGBA").save(os.path.join(OUT, name + ".png"))


def save_normal(name, h, strength):
    Image.fromarray(normal_map(h, strength), "RGB").save(os.path.join(OUT, name + ".png"))


def lerp(a, b, t):
    return a + (b - a) * t[..., None] if np.ndim(t) == 2 else a + (b - a) * t


def col(*c):
    return np.array(c, dtype=np.float64) / 255.0


def paint(base, tint, amount):
    return base * (1 - amount[..., None]) + tint * amount[..., None]


def cell_rand(u, v, cells, seed):
    r = np.random.default_rng(seed).random((cells, cells))
    iu = np.floor(u * cells).astype(int) % cells
    iv = np.floor(v * cells).astype(int) % cells
    return r[iv, iu]


u, v = grid_coords()

# ---------- 1. Arena floor: graphite composite tiles, glowing cyan seams ----------
# One texture = 4 m: 4 x 4 one-meter tiles, every second seam glows faintly.
grain = noise(N, 8, 1, 6)
fine = noise(N, 64, 2, 3)
tile_var = cell_rand(u, v, 4, 3)
h = bevel(u, 4, 0.006) * bevel(v, 4, 0.006)
h = h * (0.97 + 0.03 * fine)
# small recessed squares in each tile's corner (anchor points)
cu, cv = (u * 4) % 1, (v * 4) % 1
anchor = ((np.abs(cu - 0.08) < 0.03) & (np.abs(cv - 0.08) < 0.03)).astype(float)
h -= anchor * 0.25
base = col(38, 41, 47)
alb = np.ones((N, N, 3)) * base
alb *= (0.85 + 0.25 * tile_var)[..., None]
alb *= (0.88 + 0.2 * grain)[..., None]
scuff = np.clip((noise(N, 16, 4, 5) - 0.62) * 3, 0, 1)
alb = paint(alb, col(70, 72, 78), scuff * 0.5)
seams = seam(u, 4, 0.004) + seam(v, 4, 0.004)
alb = paint(alb, col(16, 18, 22), np.clip(seams, 0, 1))
glow_lines = np.clip(seam(u, 2, 0.0022) + seam(v, 2, 0.0022), 0, 1)
emis = np.zeros((N, N, 3)) + col(40, 200, 255)[None, None, :] * glow_lines[..., None]
emis += col(40, 200, 255) * anchor[..., None] * 0.6
save_rgb("ArenaFloor_Albedo", alb)
save_normal("ArenaFloor_Normal", h, 6.0)
save_mask("ArenaFloor_Mask", 0.15 + 0.0 * h, 0.42 + 0.18 * (1 - scuff) - 0.15 * grain)
save_rgb("ArenaFloor_Emission", emis)

# ---------- 2. Wall panels: light composite armor, panel seams, bolts, accent strip ----------
# One texture = 2 m square: two panel rows, offset vertical seams.
row = np.floor(v * 2)
shift = np.where(row % 2 == 0, 0.0, 0.5)
pu = (u + shift * 0.5) % 1.0
h = bevel(v, 2, 0.008) * bevel(pu, 2, 0.008)
# inset rectangle in each panel
lu, lv = (pu * 2) % 1, (v * 2) % 1
inset = ((lu > 0.12) & (lu < 0.88) & (lv > 0.18) & (lv < 0.82)).astype(float)
inset = blur(inset, 1.2)
h = h - inset * 0.12
# bolts near panel corners
bolts = np.zeros((N, N))
for bu in (0.05, 0.95):
    for bv in (0.08, 0.92):
        d = np.sqrt(((lu - bu) * 512) ** 2 + ((lv - bv) * 512) ** 2)
        bolts += np.clip(1 - d / 7, 0, 1)
h += bolts * 0.3
g = noise(N, 6, 11, 6)
alb = np.ones((N, N, 3)) * col(168, 174, 182)
alb *= (0.9 + 0.12 * g)[..., None]
alb = paint(alb, col(132, 138, 148), inset * 0.55)
alb = paint(alb, col(60, 64, 72), np.clip(seam(v, 2, 0.003) + seam(pu, 2, 0.003), 0, 1))
grime = np.clip((noise(N, 12, 12, 5) - 0.55) * 2.5, 0, 1) * (0.4 + 0.6 * (1 - v))
alb = paint(alb, col(96, 92, 88), grime * 0.35)
# thin cyan accent strip across the inset middle (every panel)
strip = ((lv > 0.485) & (lv < 0.515) & (lu > 0.2) & (lu < 0.8)).astype(float)
alb = paint(alb, col(30, 40, 50), strip)
emis = col(40, 200, 255)[None, None, :] * blur(strip, 0.6)[..., None]
alb = paint(alb, col(90, 94, 100), bolts.clip(0, 1) * 0.8)
save_rgb("WallPanel_Albedo", alb)
save_normal("WallPanel_Normal", h, 5.0)
save_mask("WallPanel_Mask", 0.1 + 0.6 * bolts.clip(0, 1), 0.5 - 0.2 * grime + 0.1 * inset)
save_rgb("WallPanel_Emission", emis)

# ---------- 3. Support metal: dark gunmetal, vertical ribs, orange warning bands ----------
ribs = 0.5 + 0.5 * np.cos(u * 2 * np.pi * 8)
ribs = np.clip(ribs * 1.4 - 0.2, 0, 1)
brushed = blur(noise(N, 128, 21, 2), (6, 0.3))  # streaks along v
h = 0.6 * ribs + 0.05 * brushed
band = ((v % 0.5) < 0.04).astype(float)  # every 1 m (texture = 2 m)
h += band * 0.3
alb = np.ones((N, N, 3)) * col(52, 56, 64)
alb *= (0.85 + 0.3 * brushed)[..., None]
alb *= (0.85 + 0.15 * ribs)[..., None]
stripes = (((u * 16 + v * 16) % 2) < 1).astype(float)
alb = paint(alb, paint(np.ones((N, N, 3)) * col(230, 130, 30), np.ones((N, N, 3)) * col(30, 30, 32), stripes), band)
save_rgb("SupportMetal_Albedo", alb)
save_normal("SupportMetal_Normal", h, 4.0)
save_mask("SupportMetal_Mask", 0.85 - 0.6 * band, 0.55 + 0.15 * brushed - 0.25 * band)

# ---------- 4. Ramp tread: diamond plate steel with hazard edges ----------
# one texture = 1 m
k = 12
a = ((u + v) * k) % 1
b = ((u - v) * k) % 1
diamond = np.clip(1 - (np.abs(a - 0.5) * 4 + np.abs(b - 0.5) * 0.8), 0, 1)
diamond2 = np.clip(1 - (np.abs(b - 0.5) * 4 + np.abs(a - 0.5) * 0.8), 0, 1)
cellsel = (np.floor(u * k * 2) + np.floor(v * k * 2)) % 2
d = np.where(cellsel == 0, diamond, diamond2)
d = blur(np.clip(d * 2.5, 0, 1), 0.8)
wear = noise(N, 8, 31, 5)
h = d * 0.8 + 0.05 * wear
alb = np.ones((N, N, 3)) * col(120, 124, 128)
alb *= (0.8 + 0.25 * wear)[..., None]
alb = paint(alb, col(170, 172, 176), d * 0.5)
save_rgb("RampTread_Albedo", alb)
save_normal("RampTread_Normal", h, 7.0)
save_mask("RampTread_Mask", 0.9 + 0 * h, 0.35 + 0.35 * d * (1 - wear))

# ---------- 5. Grate floor (high platforms): heavy bar grating over a dark void ----------
bars_u = seam(u, 16, 0.012)
bars_v = seam(v, 4, 0.006)
grate = np.clip(bars_u + bars_v, 0, 1)
h = grate * 1.0
alb = np.ones((N, N, 3)) * col(14, 16, 20)
metal = col(150, 154, 160) * (0.85 + 0.2 * noise(N, 16, 41, 4))[..., None]
alb = paint(alb, metal, grate)
# faint amber light from below through the gaps
emis = col(255, 150, 40)[None, None, :] * ((1 - grate) * 0.18)[..., None]
save_rgb("Grate_Albedo", alb)
save_normal("Grate_Normal", h, 10.0)
save_mask("Grate_Mask", grate * 0.9, 0.2 + 0.4 * grate)
save_rgb("Grate_Emission", emis)

# ---------- 6. Base decks: team-colored deck plates with chevrons ----------
def base_deck(name, team):
    g2 = noise(N, 8, 51, 5)
    h2 = bevel(u, 2, 0.006) * bevel(v, 2, 0.006)
    alb2 = np.ones((N, N, 3)) * col(34, 36, 42)
    alb2 *= (0.85 + 0.25 * g2)[..., None]
    # chevrons pointing "out" (along -v)
    cv2 = (v * 4 + np.abs(u - 0.5) * 2) % 1
    chev = ((cv2 > 0.0) & (cv2 < 0.18) & (np.abs(u - 0.5) < 0.42)).astype(float)
    chev = blur(chev, 0.8)
    teamc = col(*team)
    alb2 = paint(alb2, teamc * 0.55, chev)
    edge = np.clip(seam(u, 2, 0.003) + seam(v, 2, 0.003), 0, 1)
    alb2 = paint(alb2, col(12, 12, 14), edge)
    h2 = h2 - chev * 0.08
    em = teamc[None, None, :] * (chev * 0.35 + np.clip(seam(u, 1, 0.0025) + seam(v, 1, 0.0025), 0, 1) * 0.9)[..., None]
    save_rgb(name + "_Albedo", alb2)
    save_normal(name + "_Normal", h2, 6.0)
    save_mask(name + "_Mask", 0.6 + 0.0 * h2, 0.5 + 0.15 * g2)
    save_rgb(name + "_Emission", em)


base_deck("BaseDeckRed", (255, 60, 50))
base_deck("BaseDeckBlue", (50, 120, 255))

# ---------- 7. Control point button: polished dark pedestal with a glowing ring ----------
r = np.sqrt((u - 0.5) ** 2 + (v - 0.5) ** 2)
rings = np.clip(1 - np.abs(r - 0.38) / 0.012, 0, 1) + np.clip(1 - np.abs(r - 0.18) / 0.006, 0, 1)
spokes = (np.abs(np.sin(np.arctan2(v - 0.5, u - 0.5) * 6)) < 0.08).astype(float) * (r < 0.36) * (r > 0.2)
h = 1 - np.clip(rings + spokes, 0, 1) * 0.5
alb = np.ones((N, N, 3)) * col(28, 30, 36)
alb *= (0.9 + 0.15 * noise(N, 8, 61, 4))[..., None]
alb = paint(alb, col(220, 230, 240), np.clip(rings, 0, 1) * 0.4)
save_rgb("Button_Albedo", alb)
save_normal("Button_Normal", h, 4.0)
save_mask("Button_Mask", 0.9 + 0 * h, 0.75 + 0 * h)
save_rgb("Button_Emission", np.ones((N, N, 3)) * np.clip(rings + spokes * 0.5, 0, 1)[..., None])
print("done")
