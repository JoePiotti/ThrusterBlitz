"""Texture metas and materials for the main menu ship room -> unity_ship/Assets/Resources/MainMenu."""
import os, shutil, hashlib, re
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "out_ship")
ROOT = os.path.join(HERE, "unity_ship")
DST = os.path.join(ROOT, "Assets", "Resources", "MainMenu")
shutil.rmtree(ROOT, ignore_errors=True)
TEX = os.path.join(DST, "Textures")
os.makedirs(TEX)
META = open(os.path.join(HERE, "texmeta.txt")).read()
MAT = open(os.path.join(HERE, "mattemplate.txt")).read()

def guid(name):
    return hashlib.md5(("HD2/MainMenu/" + name).encode()).hexdigest()

def folder_meta(path, name):
    open(path + ".meta", "w").write("fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % guid("folder/" + name))

def tex_meta(g, srgb, normal, size, mips=True, wrap_u=0, wrap_v=0):
    m = META.replace("guid: GUID", "guid: " + g)
    m = m.replace("sRGBTexture: 1", "sRGBTexture: %d" % (1 if srgb else 0))
    m = m.replace("textureType: 0", "textureType: %d" % (1 if normal else 0))
    m = m.replace("maxTextureSize: 2048", "maxTextureSize: %d" % size)
    m = m.replace("filterMode: 1", "filterMode: 2").replace("aniso: 1", "aniso: 4")
    if not mips:
        m = m.replace("enableMipMap: 1", "enableMipMap: 0")
    m = re.sub(r"(\n    wrapU:) \d", r"\1 %d" % wrap_u, m)
    m = re.sub(r"(\n    wrapV:) \d", r"\1 %d" % wrap_v, m)
    return m

folder_meta(DST, "MainMenu")
folder_meta(TEX, "Textures")
guids = {}
for f in sorted(os.listdir(OUT)):
    if not f.endswith(".png"):
        continue
    name = f[:-4]
    g = guid(name)
    guids[name] = g
    shutil.copy(os.path.join(OUT, f), os.path.join(TEX, f))
    if name == "SpaceSky":
        meta = tex_meta(g, True, False, 4096, mips=False, wrap_u=0, wrap_v=1)
    else:
        normal = name.endswith("_Normal")
        srgb = name.endswith("_Albedo") or name.endswith("_Emission")
        meta = tex_meta(g, srgb, normal, 1024)
    open(os.path.join(TEX, f + ".meta"), "w").write(meta)

def ref(g):
    return "{fileID: 2800000, guid: %s, type: 3}" % g if g else "{fileID: 0}"

def material(name, emission=0.0):
    base = guids[name + "_Albedo"]; nrm = guids.get(name + "_Normal"); mask = guids[name + "_Mask"]
    emis = guids.get(name + "_Emission") if emission > 0 else None
    kw = ["_METALLICSPECGLOSSMAP", "_NORMALMAP"] + (["_EMISSION"] if emis else [])
    m = MAT.replace("NAME", name)
    m = m.replace("KEYWORDS", "\n".join("  - " + k for k in sorted(kw))).replace("TILE", "1")
    m = m.replace("BASEMAP", ref(base)).replace("BUMPMAP", ref(nrm)).replace("MASKMAP", ref(mask)).replace("EMISMAP", ref(emis))
    m = m.replace("DETAILMAP", ref(None)).replace("DETAILNORMAL", ref(None)).replace("DETAILSX", "1").replace("DETAILSY", "1")
    m = m.replace("EMISCOLOR", "{r: %g, g: %g, b: %g, a: 1}" % (emission, emission, emission))
    m = m.replace("BUMPSCALE", "1").replace("SMOOTH", "1").replace("LIGHTMAPFLAGS", "2" if emis else "4")
    open(os.path.join(DST, name + ".mat"), "w").write(m)
    open(os.path.join(DST, name + ".mat.meta"), "w").write(
        "fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % guid("mat/" + name))

material("ShipWall")
material("ShipFloor")
material("ShipCeiling", 1.6)
material("ShipTrimCyan", 2.0)
material("ShipTrimViolet", 2.0)
material("ShipConsole", 1.4)

sky = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!21 &2100000
Material:
  serializedVersion: 6
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: SpaceSky
  m_Shader: {fileID: 108, guid: 0000000000000000f000000000000000, type: 0}
  m_ShaderKeywords: 
  m_LightmapFlags: 4
  m_EnableInstancingVariants: 0
  m_DoubleSidedGI: 0
  m_CustomRenderQueue: -1
  stringTagMap: {}
  disabledShaderPasses: []
  m_SavedProperties:
    serializedVersion: 3
    m_TexEnvs:
    - _MainTex:
        m_Texture: {fileID: 2800000, guid: SKYGUID, type: 3}
        m_Scale: {x: 1, y: 1}
        m_Offset: {x: 0, y: 0}
    m_Floats:
    - _Exposure: 1
    - _Rotation: 0
    - _Mapping: 1
    - _ImageType: 0
    - _MirrorOnBack: 0
    - _Layout: 0
    m_Colors:
    - _Tint: {r: 0.5, g: 0.5, b: 0.5, a: 0.5}
""".replace("SKYGUID", guids["SpaceSky"])
open(os.path.join(DST, "SpaceSky.mat"), "w").write(sky)
open(os.path.join(DST, "SpaceSky.mat.meta"), "w").write(
    "fileFormatVersion: 2\nguid: %s\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % guid("mat/SpaceSky"))
print("ok", len(guids))
