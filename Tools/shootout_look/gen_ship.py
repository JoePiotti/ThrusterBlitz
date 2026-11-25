"""Thruster Blitz main menu: spaceship interior textures and the star-field skybox.
Writes into out_ship/. Uses the helpers in gen.py."""
import os
import sys
import numpy as np
from PIL import Image

import gen
from gen import C, uv, noise, band, line, periodic_line, paint, fill, blur, surface, text_mask

HERE = os.path.dirname(os.path.abspath(__file__))
gen.OUT = os.path.join(HERE, "out_ship")
os.makedirs(gen.OUT, exist_ok=True)

HULL = C("aeb6c2")        # light hull panels
HULL_SHADE = C("8b94a1")
HULL_DARK = C("2a3039")
DECK = C("23272e")
CYAN = C("4fd8ff")
VIOLET = C("7a66ff")      # logo purple
PINK = C("f06ba8")        # logo pink
AMBER = C("ffae2b")


def wall():
    """2 x 2 m hull panel: four framed panels, a recessed centre, vents low, seams dark."""
    N = 1024
    u, v = uv(N, N)
    grain = noise(N, N, 16, 701, 4)
    alb = fill(N, N, HULL) * (0.92 + 0.1 * grain)[..., None]
    seam = np.clip(periodic_line(u, 0.5, 0.006, 0.003) + periodic_line(v, 0.5, 0.006, 0.003), 0, 1)
    pu, pv = (u * 2) % 1, (v * 2) % 1
    bevel = np.clip(band(pu, 0.02, 0.05, 0.004) + band(pu, 0.95, 0.98, 0.004) + band(pv, 0.02, 0.05, 0.004) + band(pv, 0.95, 0.98, 0.004), 0, 1)
    inset = band(pu, 0.14, 0.86, 0.004) * band(pv, 0.14, 0.86, 0.004)
    inset_edge = np.clip(inset - band(pu, 0.16, 0.84, 0.004) * band(pv, 0.16, 0.84, 0.004), 0, 1)
    alb = paint(alb, HULL_SHADE, inset * 0.55)
    alb = paint(alb, C("dfe4ea"), bevel * 0.6)
    alb = paint(alb, HULL_DARK, seam)
    alb = paint(alb, HULL_DARK, inset_edge * 0.6)
    vents = np.zeros((N, N))
    for k in range(5):
        y0 = 0.06 + k * 0.022
        vents += band(v, y0, y0 + 0.01, 0.002) * band(u, 0.62, 0.86, 0.003)
    vents = np.clip(vents, 0, 1)
    alb = paint(alb, C("15181d"), vents)
    bolts = np.zeros((N, N))
    for a in (0.04, 0.46, 0.54, 0.96):
        for b in (0.04, 0.46, 0.54, 0.96):
            d = np.sqrt(((u - a) * N) ** 2 + ((v - b) * N) ** 2)
            bolts += np.clip(1 - d / 6, 0, 1)
    alb = paint(alb, C("6d7682"), np.clip(bolts, 0, 1))
    hgt = 0.5 + 0.25 * bevel - 0.15 * inset - 0.5 * seam - 0.3 * vents + 0.02 * grain + 0.15 * np.clip(bolts, 0, 1)
    surface("ShipWall", alb, hgt, 4.0, np.full((N, N), 0.15), 0.38 + 0.12 * bevel - 0.1 * vents, mask_size=(512, 512))


def floor():
    """1 m deck plate, dark, fine tread, worn edges, bolts in the corners."""
    N = 1024
    u, v = uv(N, N)
    grain = noise(N, N, 20, 711, 5)
    alb = fill(N, N, DECK) * (0.85 + 0.25 * grain)[..., None]
    seam = np.clip(line(u, 0, 0.008, 0.004) + line(u, 1, 0.008, 0.004) + line(v, 0, 0.008, 0.004) + line(v, 1, 0.008, 0.004), 0, 1)
    gx, gy = (u * 24) % 1, (v * 24) % 1
    nub = np.clip(1 - np.sqrt((gx - 0.5) ** 2 + (gy - 0.5) ** 2) / 0.22, 0, 1)
    alb = paint(alb, C("3a404a"), nub * 0.6)
    alb = paint(alb, C("0c0e11"), seam)
    wear = np.clip((noise(N, N, 6, 712, 4) - 0.55) * 3, 0, 1)
    alb = paint(alb, C("4a515c"), wear * nub * 0.5)
    bolts = np.zeros((N, N))
    for a in (0.04, 0.96):
        for b in (0.04, 0.96):
            d = np.sqrt(((u - a) * N) ** 2 + ((v - b) * N) ** 2)
            bolts += np.clip(1 - d / 9, 0, 1)
    alb = paint(alb, C("737c88"), np.clip(bolts, 0, 1))
    hgt = 0.5 + 0.35 * nub - 0.6 * seam + 0.03 * grain + 0.2 * np.clip(bolts, 0, 1)
    surface("ShipFloor", alb, hgt, 5.0, 0.55 + 0.2 * nub, 0.3 + 0.2 * nub, mask_size=(512, 512))


def ceiling():
    """1.5 m ceiling tile with a soft light panel in the middle of each."""
    N = 1024
    u, v = uv(N, N)
    grain = noise(N, N, 14, 721, 4)
    alb = fill(N, N, C("8d96a3")) * (0.92 + 0.1 * grain)[..., None]
    seam = np.clip(line(u, 0, 0.008, 0.004) + line(u, 1, 0.008, 0.004) + line(v, 0, 0.008, 0.004) + line(v, 1, 0.008, 0.004), 0, 1)
    housing = band(u, 0.18, 0.82, 0.004) * band(v, 0.36, 0.64, 0.004)
    lamp = band(u, 0.2, 0.8, 0.004) * band(v, 0.38, 0.62, 0.004)
    alb = paint(alb, HULL_DARK, seam)
    alb = paint(alb, C("1a1d22"), housing)
    alb = paint(alb, C("f4f8ff"), lamp)
    glow = C("dfeeff") * lamp[..., None]
    hgt = 0.5 - 0.5 * seam + 0.2 * housing - 0.1 * lamp + 0.02 * grain
    surface("ShipCeiling", alb, hgt, 4.0, np.full((N, N), 0.1), 0.35 + 0.4 * lamp, glow, mask_size=(512, 512))


def trim(name, color):
    """Window frames and edge strips: dark metal, chamfered edges, one LED line along it.
    u runs along the strip (1 m), v spans across it."""
    W, H = 1024, 256
    u, v = uv(W, H)
    grain = blur(noise(W, H, 24, 731, 3), (0.3, 6))
    alb = fill(H, W, HULL_DARK) * (0.9 + 0.15 * grain)[..., None]
    chamfer = np.clip(band(v, -1, 0.1, 0.006) + band(v, 0.9, 2, 0.006), 0, 1)
    channel = band(v, 0.38, 0.62, 0.006)
    led = band(v, 0.45, 0.55, 0.006)
    alb = paint(alb, C("596270"), chamfer)
    alb = paint(alb, C("08090c"), channel)
    alb = paint(alb, color, led)
    glow = color * (led * 1.0 + channel * (1 - led) * 0.15)[..., None]
    hgt = 0.6 + 0.3 * chamfer - 0.4 * channel + 0.3 * led
    surface(name, alb, hgt, 4.0, np.full((H, W), 0.5) * (1 - led), 0.4 + 0.2 * chamfer + 0.4 * led, glow, mask_size=(256, 64))


def console():
    """Console body: dark glossy panels, a row of glowing keys and indicator lights (1 m tile)."""
    N = 1024
    u, v = uv(N, N)
    grain = noise(N, N, 16, 741, 4)
    alb = fill(N, N, C("1b1f26")) * (0.9 + 0.12 * grain)[..., None]
    seam = np.clip(line(u, 0, 0.006, 0.003) + line(u, 1, 0.006, 0.003) + line(v, 0, 0.006, 0.003) + line(v, 1, 0.006, 0.003), 0, 1)
    alb = paint(alb, C("0a0b0e"), seam)
    keys = np.zeros((N, N))
    colors = np.zeros((N, N, 3))
    rng = np.random.default_rng(742)
    palette = [CYAN, VIOLET, PINK, AMBER]
    for row in range(3):
        for col in range(10):
            x0 = 0.1 + col * 0.08
            y0 = 0.2 + row * 0.08
            k = band(u, x0, x0 + 0.06, 0.004) * band(v, y0, y0 + 0.05, 0.004)
            lit = rng.random() < 0.45
            c = palette[rng.integers(0, len(palette))] if lit else C("2c323b")
            keys = np.maximum(keys, k)
            colors += k[..., None] * (c if lit else 0)
            alb = paint(alb, c if lit else C("2c323b"), k)
    leds = np.zeros((N, N))
    for i in range(14):
        a = 0.1 + i * 0.058
        d = np.sqrt(((u - a) * N) ** 2 + ((v - 0.75) * N) ** 2)
        leds += np.clip(1 - d / 7, 0, 1)
    leds = np.clip(leds, 0, 1)
    alb = paint(alb, CYAN, leds)
    glow = colors * 0.9 + CYAN * leds[..., None]
    hgt = 0.5 - 0.5 * seam + 0.25 * keys + 0.02 * grain
    surface("ShipConsole", alb, hgt, 4.0, np.full((N, N), 0.2), 0.55 + 0.2 * keys, glow, mask_size=(512, 512))


def sky():
    """Equirectangular star field for Skybox/Panoramic (Unity's mapping): dense stars,
    a violet-pink nebula band, a ringed gas giant off the right side, a small moon left."""
    W, H = 4096, 2048
    j, i = np.mgrid[0:H, 0:W].astype(np.float64)
    u = (i + 0.5) / W
    v = 1.0 - (j + 0.5) / H
    y = -np.cos(np.pi * v)
    r = np.sqrt(np.clip(1 - y * y, 0, 1))
    phi = (0.5 - u) * 2 * np.pi
    x = r * np.cos(phi)
    z = r * np.sin(phi)
    d = np.stack([x, y, z], -1)

    rng = np.random.default_rng(751)
    img = np.zeros((H, W, 3))
    img += np.array([0.004, 0.005, 0.012])

    # nebula: a band around a tilted great circle, fbm shaped
    axis = np.array([0.25, 0.92, -0.3]); axis /= np.linalg.norm(axis)
    off = np.abs(d @ axis)
    n1 = noise(W, H, 12, 752, 6)
    n2 = noise(W, H, 24, 753, 5)
    bandm = np.exp(-(off / 0.28) ** 2)
    neb = np.clip((n1 - 0.38) * 2.2, 0, 1) * bandm
    tint = np.clip(n2, 0, 1)[..., None]
    color = VIOLET * (1 - tint) + PINK * tint
    img += color * (neb ** 1.3)[..., None] * 0.55
    dust = np.clip((noise(W, H, 40, 754, 4) - 0.55) * 4, 0, 1) * bandm
    img *= (1 - 0.6 * dust)[..., None]

    # stars: random points, brightness power law, a few tinted, denser in the band
    count = 34000
    sx = rng.integers(0, W, count)
    sy = rng.integers(0, H, count)
    keep = rng.random(count) < (0.35 + 0.65 * bandm[sy, sx]) * np.sqrt(np.clip(1 - (2 * (sy + 0.5) / H - 1) ** 2, 0.05, 1))
    sx, sy = sx[keep], sy[keep]
    bright = rng.pareto(2.0, sx.size) * 0.35 + 0.12
    tints = np.array([[1, 1, 1], [0.8, 0.88, 1.0], [1.0, 0.9, 0.75], [0.85, 0.8, 1.0]])
    tsel = tints[rng.integers(0, len(tints), sx.size)]
    stars = np.zeros((H, W, 3))
    np.add.at(stars, (sy, sx), tsel * np.clip(bright, 0, 3)[:, None])
    halo = np.stack([blur(stars[..., c], 1.2) for c in range(3)], -1)
    img += stars + halo * 2.5

    # gas giant
    pc = np.array([0.9, 0.12, 0.42]); pc /= np.linalg.norm(pc)
    pr = np.radians(16.0)
    ang = np.arccos(np.clip(d @ pc, -1, 1))
    disk = ang < pr
    sun = np.array([-0.4, 0.35, 0.85]); sun /= np.linalg.norm(sun)
    # local surface normal on the sphere seen from the inside: approximate by offset direction
    t = np.clip(ang / pr, 0, 1)
    perp = d - (d @ pc)[..., None] * pc
    pn = np.linalg.norm(perp, axis=-1, keepdims=True) + 1e-9
    perp = perp / pn
    normal = -pc[None, None, :] * np.sqrt(np.clip(1 - t * t, 0, 1))[..., None] + perp * t[..., None]
    light = np.clip((normal @ sun) * 1.1 + 0.04, 0, 1)
    lat = (d @ np.array([0.0, 1.0, 0.0]))
    bands = 0.5 + 0.5 * np.sin(lat * 140 + noise(W, H, 30, 755, 3) * 3)
    pcol = C("c98a5b") * (1 - bands[..., None]) + C("e8c9a2") * bands[..., None]
    planet = pcol * light[..., None]
    rim = np.exp(-((ang - pr) / np.radians(0.6)) ** 2) * (light > -1)
    atmo = C("ffb47a") * (rim * np.clip(perp @ sun + 0.25, 0, 1))[..., None] * 0.9
    img = np.where(disk[..., None], planet, img) + atmo

    # small moon
    mc = np.array([-0.85, 0.32, -0.25]); mc /= np.linalg.norm(mc)
    mr = np.radians(2.6)
    mang = np.arccos(np.clip(d @ mc, -1, 1))
    mdisk = mang < mr
    mt = np.clip(mang / mr, 0, 1)
    mperp = d - (d @ mc)[..., None] * mc
    mperp /= np.linalg.norm(mperp, axis=-1, keepdims=True) + 1e-9
    mnorm = -mc[None, None, :] * np.sqrt(np.clip(1 - mt * mt, 0, 1))[..., None] + mperp * mt[..., None]
    mlight = np.clip((mnorm @ sun) + 0.03, 0, 1)
    crater = 0.8 + 0.2 * noise(W, H, 200, 756, 3)
    img = np.where(mdisk[..., None], C("b9bcc4") * (mlight * crater)[..., None], img)

    img = np.clip(img, 0, 1) ** (1 / 1.0)
    Image.fromarray((img * 255).astype(np.uint8)).save(os.path.join(gen.OUT, "SpaceSky.png"))


if __name__ == "__main__":
    only = set(sys.argv[1:])
    for key, fn in [("wall", wall), ("floor", floor), ("ceiling", ceiling),
                    ("trim", lambda: (trim("ShipTrimCyan", CYAN), trim("ShipTrimViolet", VIOLET))),
                    ("console", console), ("sky", sky)]:
        if not only or key in only:
            fn()
            print("made", key, flush=True)
