"""Íconos de materiales del HUD a partir de las capturas de los pickups (Unity → ScrapWaves → UI → Capture
Material Pickup Icons). Recorta cada captura, la centra en un cuadrado y le pone el contorno de los íconos.

Uso (desde la raíz del repo):
    python ArtSource/Tools/finish_material_icons.py
Escribe Assets/Art/UI/Icons/Materials/Material_<Tipo>.png.
"""
import glob
import os

from PIL import Image

from compose_icon import grade, stylize

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
RENDERS = os.path.join(REPO, "ArtSource", "UI", "MaterialIcons", "renders")
OUT = os.path.join(REPO, "Assets", "Art", "UI", "Icons", "Materials")
SIZE = 128
FILL = 0.86


def finish(path):
    img = Image.open(path).convert("RGBA")
    img = grade(img.crop(img.getchannel("A").getbbox()), 1.05, 1.15)
    scale = SIZE * FILL / max(img.width, img.height)
    img = img.resize((max(1, round(img.width * scale)), max(1, round(img.height * scale))), Image.LANCZOS)
    img = stylize(img, outline_px=3, shadow_px=0)
    img.thumbnail((SIZE, SIZE), Image.LANCZOS)
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    canvas.alpha_composite(img, ((SIZE - img.width) // 2, (SIZE - img.height) // 2))
    canvas.save(os.path.join(OUT, os.path.basename(path)))


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for path in sorted(glob.glob(os.path.join(RENDERS, "Material_*.png"))):
        finish(path)
    print("OK", OUT)
