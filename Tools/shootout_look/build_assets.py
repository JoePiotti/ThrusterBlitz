"""Writes texture .meta files and URP Lit materials for the Shootout look into ./unity."""
import os
import shutil
import uuid
import json

HERE = os.path.dirname(__file__)
OUT = os.path.join(HERE, "out")
DST = os.path.join(HERE, "unity")
TEX = os.path.join(DST, "Textures")
os.makedirs(TEX, exist_ok=True)

META_TEMPLATE = open(os.path.join(HERE, "texmeta.txt")).read()


def tex_meta(guid, srgb, normal, size):
    m = META_TEMPLATE
    m = m.replace("guid: GUID", "guid: " + guid)
    m = m.replace("sRGBTexture: 1", "sRGBTexture: %d" % (1 if srgb else 0))
    m = m.replace("textureType: 0", "textureType: %d" % (1 if normal else 0))
    m = m.replace("maxTextureSize: 2048", "maxTextureSize: %d" % size)
    # repeat wrap, trilinear, some anisotropy for floors seen at grazing angles
    m = m.replace("filterMode: 1", "filterMode: 2").replace("aniso: 1", "aniso: 4")
    m = m.replace("wrapU: 0", "wrapU: 0")
    return m


guids = {}
for f in sorted(os.listdir(OUT)):
    if not f.endswith(".png"):
        continue
    name = f[:-4]
    g = uuid.uuid4().hex
    guids[name] = g
    shutil.copy(os.path.join(OUT, f), os.path.join(TEX, f))
    normal = name.endswith("_Normal")
    srgb = name.endswith("_Albedo") or name.endswith("_Emission")
    with open(os.path.join(TEX, f + ".meta"), "w") as fh:
        fh.write(tex_meta(g, srgb, normal, 1024))

MAT_TEMPLATE = open(os.path.join(HERE, "mattemplate.txt")).read()


def material(name, tiling, emission_color=None, bump=1.0, smooth=1.0):
    base = guids[name + "_Albedo"]
    nrm = guids[name + "_Normal"]
    mask = guids[name + "_Mask"]
    emis = guids.get(name + "_Emission")
    keywords = ["_METALLICSPECGLOSSMAP", "_NORMALMAP"]
    if emis and emission_color:
        keywords.append("_EMISSION")
    m = MAT_TEMPLATE
    m = m.replace("NAME", "Shootout" + name)
    m = m.replace("KEYWORDS", "\n".join("  - " + k for k in keywords))
    m = m.replace("TILE", str(tiling))

    def tex(guid):
        return "{fileID: 2800000, guid: %s, type: 3}" % guid if guid else "{fileID: 0}"

    m = m.replace("BASEMAP", tex(base)).replace("BUMPMAP", tex(nrm)).replace("MASKMAP", tex(mask))
    m = m.replace("EMISMAP", tex(emis if emission_color else None))
    c = emission_color or (0, 0, 0)
    m = m.replace("EMISCOLOR", "{r: %g, g: %g, b: %g, a: 1}" % c)
    m = m.replace("BUMPSCALE", str(bump)).replace("SMOOTH", str(smooth))
    m = m.replace("LIGHTMAPFLAGS", "2" if emission_color else "4")
    g = uuid.uuid4().hex
    with open(os.path.join(DST, "Shootout" + name + ".mat"), "w") as fh:
        fh.write(m)
    with open(os.path.join(DST, "Shootout" + name + ".mat.meta"), "w") as fh:
        fh.write("fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % g)
    return g


mats = {
    "ArenaFloor": material("ArenaFloor", 0.25, (1.2, 1.2, 1.2)),
    "WallPanel": material("WallPanel", 0.5, (1.5, 1.5, 1.5)),
    "SupportMetal": material("SupportMetal", 0.5),
    "RampTread": material("RampTread", 1.0, bump=0.8),
    "Grate": material("Grate", 0.5, (1, 1, 1), bump=1.0),
    "BaseDeckRed": material("BaseDeckRed", 0.5, (1.6, 1.6, 1.6)),
    "BaseDeckBlue": material("BaseDeckBlue", 0.5, (1.6, 1.6, 1.6)),
    "Button": material("Button", 1.0, (1.2, 1.2, 1.2)),
}
json.dump(mats, open(os.path.join(HERE, "mats.json"), "w"), indent=1)
print(mats)
