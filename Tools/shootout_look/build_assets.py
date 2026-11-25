"""Writes texture .meta files and URP Lit materials for the Night League look into ./unity."""
import os, shutil, uuid, json, hashlib
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out")
DST = os.path.join(HERE, "unity")
shutil.rmtree(DST, ignore_errors=True)
TEX = os.path.join(DST, "Textures")
os.makedirs(TEX)
META = open(os.path.join(HERE, "texmeta.txt")).read()
MAT = open(os.path.join(HERE, "mattemplate.txt")).read()

def tex_meta(guid, srgb, normal, size):
    m = META.replace("guid: GUID", "guid: " + guid)
    m = m.replace("sRGBTexture: 1", "sRGBTexture: %d" % (1 if srgb else 0))
    m = m.replace("textureType: 0", "textureType: %d" % (1 if normal else 0))
    m = m.replace("maxTextureSize: 2048", "maxTextureSize: %d" % size)
    m = m.replace("filterMode: 1", "filterMode: 2").replace("aniso: 1", "aniso: 4")
    return m

def stable_guid(name):
    # Same name -> same GUID on every run, so regenerating never breaks scene references.
    return hashlib.md5(("HD2/NightLeague/" + name).encode()).hexdigest()

guids = {}
for f in sorted(os.listdir(OUT)):
    if not f.endswith(".png"):
        continue
    name = f[:-4]
    g = stable_guid(name)
    guids[name] = g
    shutil.copy(os.path.join(OUT, f), os.path.join(TEX, f))
    normal = name.endswith("_Normal")
    srgb = (name.endswith("_Albedo") or name.endswith("_Emission")) and "Detail" not in name
    open(os.path.join(TEX, f + ".meta"), "w").write(tex_meta(g, srgb, normal, 2048))

def ref(g):
    return "{fileID: 2800000, guid: %s, type: 3}" % g if g else "{fileID: 0}"

def material(name, emission=0.0, bump=1.0, smooth=1.0, detail=None, detail_scale=(1, 1), guid=None, folder=DST, normal=True):
    base = guids[name + "_Albedo"]
    nrm = guids.get(name + "_Normal") if normal else None
    mask = guids[name + "_Mask"]
    emis = guids.get(name + "_Emission") if emission > 0 else None
    kw = ["_METALLICSPECGLOSSMAP"]
    if nrm:
        kw.append("_NORMALMAP")
    if emis:
        kw.append("_EMISSION")
    if detail:
        kw.append("_DETAIL_MULX2")
    m = MAT.replace("NAME", "NL_" + name)
    m = m.replace("KEYWORDS", "\n".join("  - " + k for k in sorted(kw)))
    m = m.replace("TILE", "1")
    m = m.replace("BASEMAP", ref(base)).replace("BUMPMAP", ref(nrm)).replace("MASKMAP", ref(mask))
    m = m.replace("EMISMAP", ref(emis))
    m = m.replace("DETAILMAP", ref(guids[detail + "_Albedo"] if detail else None))
    m = m.replace("DETAILNORMAL", ref(guids[detail + "_Normal"] if detail else None))
    m = m.replace("DETAILSX", str(detail_scale[0])).replace("DETAILSY", str(detail_scale[1]))
    e = emission
    m = m.replace("EMISCOLOR", "{r: %g, g: %g, b: %g, a: 1}" % (e, e, e))
    m = m.replace("BUMPSCALE", str(bump)).replace("SMOOTH", str(smooth))
    m = m.replace("LIGHTMAPFLAGS", "2" if emis else "4")
    g = guid or stable_guid("mat/" + name)
    fname = "NL_" + name if folder == DST else "ShootoutButton"
    open(os.path.join(folder, fname + ".mat"), "w").write(m.replace("NL_Button", "ShootoutButton"))
    open(os.path.join(folder, fname + ".mat.meta"), "w").write(
        "fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % g)
    return g

mats = {}
mats["Court"] = material("Court", 1.0, detail="CourtDetail", detail_scale=(20.79, 30), normal=False)
mats["BarrierSide"] = material("BarrierSide", 1.2)
mats["BarrierLogo"] = material("BarrierLogo", 1.0)
mats["BarrierBadge"] = material("BarrierBadge", 1.2)
for p in ("BarrierTeamRed", "BarrierTeamBlue"):
    mats[p] = material(p, 1.2)
mats["BarrierTop"] = material("BarrierTop")
mats["SteelPlain"] = material("SteelPlain")
mats["SteelFascia"] = material("SteelFascia")
for p in ("ColumnWhite", "ColumnRed", "ColumnBlue"):
    mats[p] = material(p, 1.6)
mats["ColumnPlain"] = material("ColumnPlain")
mats["StadiumBeam"] = material("StadiumBeam", 2.0)
mats["Canopy"] = material("Canopy", 2.2)
for p in ("RibbonRed", "RibbonBlue"):
    mats[p] = material(p, 1.6)
mats["Tread"] = material("Tread", 1.2)
mats["TrimAmber"] = material("TrimAmber", 1.8)
mats["WalkDeck"] = material("WalkDeck")
mats["Grate"] = material("Grate", 1.0)
for p in ("SteelPanel", "SteelPanelRed", "SteelPanelBlue"):
    mats[p] = material(p, 1.4)
for p in ("EdgeAmber", "EdgeRed", "EdgeBlue"):
    mats[p] = material(p, 1.5)
mats["BaseDeckRed"] = material("BaseDeckRed", 1.4)
mats["BaseDeckBlue"] = material("BaseDeckBlue", 1.4)
os.makedirs(os.path.join(DST, "Resources"), exist_ok=True)
mats["Button"] = material("Button", 1.2, guid="b19bdf50a10b4efda40a045d6d7848bd", folder=os.path.join(DST, "Resources"))
json.dump(mats, open(os.path.join(HERE, "mats.json"), "w"), indent=1)
print(len(mats), "materials")
