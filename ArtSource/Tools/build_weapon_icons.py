"""Regenera los íconos `_selected` y `_locked` de las armas a partir de sus renders.

Sobrescribe los PNG manteniendo sus `.meta`, así WeaponUiIconCatalog y los WeaponSO siguen apuntando al mismo GUID.
Uso (desde la raíz del repo):
    python ArtSource/Tools/build_weapon_icons.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from compose_icon import compose  # noqa: E402

ICON_DIR = "Assets/Art/UI/Icons/Weapons"
WEAPONS = ("cannon", "rocket", "flame", "morter", "blade")

for name in WEAPONS:
    render = f"{ICON_DIR}/item_weapon_{name}_render.png"
    compose(render, f"{ICON_DIR}/item_weapon_{name}_selected.png")
    compose(render, f"{ICON_DIR}/item_weapon_{name}_locked.png", locked=True)
    print(f"{name}: selected + locked")
