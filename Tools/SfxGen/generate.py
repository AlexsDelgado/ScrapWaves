"""Genera los SFX sintéticos de Dumpster Fire.

Uso:
  python Tools/SfxGen/generate.py                  # todas las familias, 3 tomas
  python Tools/SfxGen/generate.py shot hurt -n 8   # familias elegidas
  python Tools/SfxGen/generate.py --seed 7         # otro set (la semilla fija el resultado)

Escribe Assets/Audio/Synth/syn_<familia>_NN.wav (+ .meta con GUID estable, así regenerar no rompe
las referencias de Unity). Misma semilla y misma receta = mismos bytes.
"""
import argparse
import hashlib
import sys
import zlib
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parent))
from recipes import RECIPES  # noqa: E402
from synth import SR, finish, write_wav  # noqa: E402

LOOPS = {"flame_loop"}  # sin fade-out: deben cerrar el loop sin costura

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "Assets" / "Audio" / "Synth"

META = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 1
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {{}}
  forceToMono: 1
  normalize: 1
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def stable_guid(name):
    return hashlib.md5(f"dumpsterfire-synth/{name}".encode()).hexdigest()


LIB_SCRIPT_GUID = "5d2e8a41c7b94f06a3e1d9b8f0c27a64"  # SynthSfxLibrary.cs.meta
LIB_ASSET_GUID = "a6f3c9d2e1b74085b2d7c4e9f1a30b58"

LIB_HEAD = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: @GUID@, type: 3}
  m_Name: SynthSfxLibrary
  m_EditorClassIdentifier: Assembly-CSharp::SynthSfxLibrary
  _entries:
"""


def write_library():
    """Reescribe SynthSfxLibrary.asset con todas las tomas que haya en disco (clave = familia)."""
    families = {}
    for wav in sorted(OUT.glob("syn_*_[0-9][0-9].wav")):
        family = wav.stem[len("syn_"):].rsplit("_", 1)[0]
        families.setdefault(family, []).append(wav.name)
    nl = chr(10)
    lines = [LIB_HEAD.replace("@GUID@", LIB_SCRIPT_GUID)]
    for family, names in sorted(families.items()):
        lines.append(f"  - Key: {family}" + nl + "    Clips:" + nl)
        for name in names:
            lines.append("    - {fileID: 8300000, guid: " + stable_guid(name) + ", type: 3}" + nl)
    (OUT / "SynthSfxLibrary.asset").write_text("".join(lines), newline=nl)
    meta = nl.join(["fileFormatVersion: 2", "guid: " + LIB_ASSET_GUID, "NativeFormatImporter:",
                    "  externalObjects: {}", "  mainObjectFileID: 11400000", "  userData: ",
                    "  assetBundleName: ", "  assetBundleVariant: ", ""])
    (OUT / "SynthSfxLibrary.asset.meta").write_text(meta, newline=nl)
    print(f"SynthSfxLibrary.asset: {len(families)} familias")


def generate(family, takes, seed):
    recipe = RECIPES[family]
    for i in range(1, takes + 1):
        rng = np.random.default_rng([seed, zlib.crc32(family.encode()), i])
        v = {"a": rng.uniform(-1, 1), "b": rng.uniform(-1, 1)}
        x = finish(recipe(rng, v), fade_ms=0 if family in LOOPS else 4)
        name = f"syn_{family}_{i:02d}.wav"
        write_wav(OUT / name, x)
        (OUT / (name + ".meta")).write_text(META.format(guid=stable_guid(name)), newline="\n")
        print(f"{name}  {len(x) / SR:.2f}s")


def main():
    p = argparse.ArgumentParser()
    p.add_argument("families", nargs="*", help=f"disponibles: {', '.join(RECIPES)}")
    p.add_argument("-n", "--takes", type=int, default=3)
    p.add_argument("--seed", type=int, default=1)
    a = p.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    for fam in a.families or RECIPES:
        if fam not in RECIPES:
            sys.exit(f"familia desconocida: {fam}")
        generate(fam, a.takes, a.seed)
    write_library()


if __name__ == "__main__":
    main()
