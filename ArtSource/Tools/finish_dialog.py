"""Sprites finales del diálogo de jefes, con el mismo contorno y marco que el HUD V2B y los íconos de items.

Uso (desde la raíz del repo, después de blender_build_hud.py --variant V2B y dialog_portraits.py):
    python ArtSource/Tools/finish_dialog.py

Escribe en Assets/Art/UI/Dialogue/:
- DialogPanel.png: panel con contorno (el lienzo no cambia, así las ventanas de HudLayout.json siguen valiendo).
- DialogSignal.png: LED blanco con halo, se tiñe desde Unity mientras habla el jefe.
- Portrait_Stalker_Revealed.png: render a color dentro del marco de ícono.
- Portrait_Stalker_Hidden.png: silueta oscura con un signo de pregunta, dentro del marco bloqueado.
"""
import os

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

from compose_icon import FRAME_LOCKED, FRAME_SELECTED, compose, grade
from finish_hud import outline

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
V2B = os.path.join(REPO, "ArtSource", "UI", "V2B", "renders")
OUT = os.path.join(REPO, "Assets", "Art", "UI", "Dialogue")
MUSTARD = (216, 192, 48, 255)
QUESTION_FONTS = (r"C:\Windows\Fonts\arialbd.ttf", r"C:\Windows\Fonts\segoeuib.ttf", "DejaVuSans-Bold.ttf")


def _font(size):
    for path in QUESTION_FONTS:
        try:
            return ImageFont.truetype(path, size)
        except OSError:
            continue
    return ImageFont.load_default()


def question_mark(img, center=(0.43, 0.44), size=0.36):
    """Signo de pregunta mostaza con contorno negro grueso, centrado en el rulo del gusano."""
    w, h = img.size
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    font = _font(round(h * size))
    pos = (round(w * center[0]), round(h * center[1]))
    stroke = max(4, round(h * 0.018))
    d.text(pos, "?", font=font, fill=MUSTARD, anchor="mm", stroke_width=stroke, stroke_fill=(12, 11, 10, 255))
    img.alpha_composite(layer)
    return img


def signal(size=64):
    s4 = size * 4
    glow = Image.new("L", (s4, s4), 0)
    ImageDraw.Draw(glow).ellipse((s4 * 0.1, s4 * 0.1, s4 * 0.9, s4 * 0.9), fill=110)
    glow = glow.filter(ImageFilter.GaussianBlur(s4 * 0.08))
    core = Image.new("L", (s4, s4), 0)
    ImageDraw.Draw(core).ellipse((s4 * 0.26, s4 * 0.26, s4 * 0.74, s4 * 0.74), fill=255)
    big = Image.new("RGBA", (s4, s4), (255, 255, 255, 0))
    big.putalpha(ImageChops.lighter(glow, core))
    big.resize((size, size), Image.LANCZOS).save(os.path.join(OUT, "DialogSignal.png"))


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    panel = Image.open(os.path.join(V2B, "DialogPanel.png")).convert("RGBA")
    outline(grade(panel, 1.0, 1.2), 4).save(os.path.join(OUT, "DialogPanel.png"))
    signal()
    compose(os.path.join(V2B, "Portrait_Stalker_revealed.png"), os.path.join(OUT, "Portrait_Stalker_Revealed.png"),
            FRAME_SELECTED, fill=0.9)
    hidden = os.path.join(OUT, "Portrait_Stalker_Hidden.png")
    compose(os.path.join(V2B, "Portrait_Stalker_hidden.png"), hidden, FRAME_LOCKED, fill=0.9, exposure=0.7)
    question_mark(Image.open(hidden).convert("RGBA")).save(hidden)
    print("OK", OUT)
