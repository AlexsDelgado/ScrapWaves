"""Post-proceso de los renders del HUD y sprites auxiliares, con el mismo contorno que los íconos de items.

Uso (desde la raíz del repo, después de blender_build_hud.py):
    python ArtSource/Tools/finish_hud.py

Escribe en Assets/Art/UI/HUD/:
- HudLeft/HudCenter/HudRight/HudBadge.png: paneles con contorno (el lienzo no cambia, así el layout sigue valiendo).
- HudCircleMask.png: círculo blanco para los fills radiales y verticales de los diales.
- HudBarFill.png: fill blanco con brillo de vidrio, se tiñe desde Unity.
- HudDashPip.png: carga de dash para la fila junto a la retícula.
- HudSlotEmpty_<Head|Core|Arm|Leg>.png: marco de slot vacío con la silueta del tipo de pasivo.
"""
import os

from PIL import Image, ImageDraw, ImageFilter

from compose_icon import FRAME_LOCKED, clean_frame, grade

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
RENDERS = os.path.join(REPO, "ArtSource", "UI", "renders")
OUT = os.path.join(REPO, "Assets", "Art", "UI", "HUD")
ICONS = os.path.join(REPO, "Assets", "Art", "UI", "Icons")
PANELS = ("HudLeft", "HudCenter", "HudRight", "HudBadge")
OUTLINE_PX = 6
SILHOUETTES = {
    "Head": "Head/CQB module_render.png",
    "Core": "Chest/Plated Reactor_render.png",
    "Arm": "Arms/Honed Weaponry System_render.png",
    "Leg": "Legs/Bionic Boots_render.png",
}


def outline(img, px):
    alpha = img.getchannel("A")
    grown = alpha.filter(ImageFilter.MaxFilter(px * 2 + 1))
    out = Image.new("RGBA", img.size, (12, 11, 10, 0))
    out.putalpha(grown)
    out.alpha_composite(img)
    return out


def finish_panel(name):
    img = Image.open(os.path.join(RENDERS, f"{name}.png")).convert("RGBA")
    outline(grade(img, 0.9, 1.12), OUTLINE_PX).save(os.path.join(OUT, f"{name}.png"))


def circle_mask(size=256):
    big = Image.new("L", (size * 4, size * 4), 0)
    ImageDraw.Draw(big).ellipse((0, 0, size * 4 - 1, size * 4 - 1), fill=255)
    img = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    img.putalpha(big.resize((size, size), Image.LANCZOS))
    img.save(os.path.join(OUT, "HudCircleMask.png"))


def dash_pip(size=64, ring=0.16):
    """Carga de dash: disco blanco (se tiñe) con aro oscuro, que queda oscuro al teñir."""
    s4 = size * 4
    big = Image.new("RGBA", (s4, s4), (0, 0, 0, 0))
    d = ImageDraw.Draw(big)
    d.ellipse((0, 0, s4 - 1, s4 - 1), fill=(12, 11, 10, 255))
    inset = round(s4 * ring)
    d.ellipse((inset, inset, s4 - 1 - inset, s4 - 1 - inset), fill=(255, 255, 255, 255))
    hl = round(s4 * 0.3)
    d.ellipse((hl, round(s4 * 0.22), s4 - hl, round(s4 * 0.42)), fill=(255, 255, 255, 255))
    big.resize((size, size), Image.LANCZOS).save(os.path.join(OUT, "HudDashPip.png"))


def bar_fill(w=64, h=32):
    img = Image.new("RGBA", (w, h))
    for y in range(h):
        t = y / (h - 1)
        v = 1.0 if 0.18 < t < 0.34 else (0.86 if t < 0.6 else 0.86 - (t - 0.6) * 0.6)
        c = round(255 * v)
        for x in range(w):
            img.putpixel((x, y), (c, c, c, 255))
    img.save(os.path.join(OUT, "HudBarFill.png"))


def empty_slot(kind, rel_render, fill=0.72, opacity=0.28):
    frame, (x0, y0, x1, y1) = clean_frame(Image.open(FRAME_LOCKED))
    src = Image.open(os.path.join(ICONS, rel_render)).convert("RGBA")
    alpha = src.getchannel("A").crop(src.getchannel("A").getbbox())
    scale = min((x1 - x0) * fill / alpha.width, (y1 - y0) * fill / alpha.height)
    alpha = alpha.resize((round(alpha.width * scale), round(alpha.height * scale)), Image.LANCZOS)
    sil = Image.new("RGBA", alpha.size, (150, 150, 150, 0))
    sil.putalpha(alpha.point(lambda v: round(v * opacity)))
    frame.alpha_composite(sil, ((x0 + x1 - sil.width) // 2, (y0 + y1 - sil.height) // 2))
    frame.save(os.path.join(OUT, f"HudSlotEmpty_{kind}.png"))


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for name in PANELS:
        finish_panel(name)
    circle_mask()
    dash_pip()
    bar_fill()
    for kind, rel in SILHOUETTES.items():
        empty_slot(kind, rel)
    print("OK", OUT)
