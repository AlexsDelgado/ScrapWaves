"""Compone un render 3D (PNG con alpha) dentro del marco metálico de los íconos de items.

Uso:
    python compose_icon.py <render.png> <salida.png> [--locked] [--frame <marco.png>] [--fill 0.94] [--shadow 0]

`--locked` usa el marco desaturado y apaga el render, como los íconos `_locked` del juego.
"""
import argparse
import os

from PIL import Image, ImageChops, ImageEnhance, ImageFilter, ImageOps

_TEMPLATES = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Templates")
FRAME_SELECTED = os.path.join(_TEMPLATES, "icon_frame_selected.png")
FRAME_LOCKED = os.path.join(_TEMPLATES, "icon_frame_locked.png")

# Interior del panel, medido sobre el marco de 364x386 y escalado al tamaño real. Las esquinas
# llevan placas atornilladas que se solapan con el panel, por eso se preservan.
REF_W, REF_H = 364, 386
INSET_X, INSET_TOP, INSET_BOTTOM = 31, 34, 33
CORNER = 78


def clean_frame(frame: Image.Image) -> tuple[Image.Image, tuple[int, int, int, int]]:
    frame = frame.convert("RGBA")
    w, h = frame.size
    sx, sy = w / REF_W, h / REF_H
    ix, it, ib = round(INSET_X * sx), round(INSET_TOP * sy), round(INSET_BOTTOM * sy)
    cx_, cy_ = round(CORNER * sx), round(CORNER * sy)
    x0, y0, x1, y1 = ix, it, w - ix, h - ib
    px = frame.load()
    out = frame.copy()
    opx = out.load()
    top_safe, bottom_safe = y0 + cy_ - it, y1 - (cy_ - ib)

    # El perfil de fondo se toma de las columnas pegadas al marco, fuera de las esquinas, y se
    # filtra con mediana: si algún píxel del arma original toca el borde, no deja rayas.
    corner_off = cx_ - ix + 2

    def column_profile(edge_x, corner_x):
        vals = {}
        for y in range(y0, y1):
            x = corner_x if (y < top_safe or y >= bottom_safe) else edge_x
            vals[y] = px[x, y][:3]
        smooth = {}
        for y in range(y0, y1):
            win = sorted(vals[max(y0, min(y1 - 1, y + d))] for d in range(-12, 13))
            smooth[y] = win[len(win) // 2]
        return smooth

    left_p = column_profile(x0 + 2, x0 + corner_off)
    right_p = column_profile(x1 - 3, x1 - 1 - corner_off)
    for y in range(y0, y1):
        in_corner_rows = y < top_safe or y >= bottom_safe
        lx = x0 + (cx_ - ix if in_corner_rows else 1)
        rx = x1 - 1 - (cx_ - ix if in_corner_rows else 1)
        left, right = left_p[y], right_p[y]
        for x in range(lx, rx + 1):
            t = (x - x0) / max(1, x1 - x0)
            opx[x, y] = tuple(int(left[i] * (1 - t) + right[i] * t) for i in range(3)) + (255,)
    return out, (x0, y0, x1, y1)


def stylize(render: Image.Image, outline_px: int, shadow_px: int) -> Image.Image:
    render = render.convert("RGBA")
    bbox = render.getchannel("A").getbbox()
    render = render.crop(bbox)
    pad = outline_px + shadow_px + 4
    canvas = Image.new("RGBA", (render.width + pad * 2, render.height + pad * 2), (0, 0, 0, 0))
    canvas.alpha_composite(render, (pad, pad))
    alpha = canvas.getchannel("A")

    outline_a = alpha.filter(ImageFilter.MaxFilter(outline_px * 2 + 1))
    outline = Image.new("RGBA", canvas.size, (12, 11, 10, 255))
    outline.putalpha(outline_a)

    layers = [outline, canvas]
    if shadow_px > 0:
        shadow_a = outline_a.filter(ImageFilter.GaussianBlur(shadow_px)).point(lambda v: int(v * 0.55))
        shadow = Image.new("RGBA", canvas.size, (0, 0, 0, 255))
        shadow.putalpha(ImageChops.offset(shadow_a, shadow_px // 2, shadow_px))
        layers.insert(0, shadow)

    result = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    for layer in layers:
        result.alpha_composite(layer)
    return result


def grade(render: Image.Image, exposure: float, contrast: float) -> Image.Image:
    rgb, a = render.convert("RGB"), render.getchannel("A")
    rgb = ImageEnhance.Contrast(ImageEnhance.Brightness(rgb).enhance(exposure)).enhance(contrast)
    rgb.putalpha(a)
    return rgb


def desaturate(render: Image.Image, brightness: float = 0.8) -> Image.Image:
    a = render.getchannel("A")
    gray = ImageEnhance.Brightness(ImageOps.grayscale(render.convert("RGB"))).enhance(brightness).convert("RGB")
    gray.putalpha(a)
    return gray


def compose(render_path, out_path, frame_path=None, fill=0.94, outline=True, exposure=0.82, contrast=1.15,
            shadow_px=0, locked=False):
    frame_path = frame_path or (FRAME_LOCKED if locked else FRAME_SELECTED)
    frame, (x0, y0, x1, y1) = clean_frame(Image.open(frame_path))
    render = Image.open(render_path).convert("RGBA")
    render = grade(render.crop(render.getchannel("A").getbbox()), exposure, contrast)
    if locked:
        render = desaturate(render)

    inner_w, inner_h = x1 - x0, y1 - y0
    scale = min(inner_w * fill / render.width, inner_h * fill / render.height)
    render = render.resize((max(1, round(render.width * scale)), max(1, round(render.height * scale))), Image.LANCZOS)
    if outline:
        render = stylize(render, outline_px=3, shadow_px=shadow_px)

    cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
    frame.alpha_composite(render, (cx - render.width // 2, cy - render.height // 2))
    frame.save(out_path)
    return out_path


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("render")
    ap.add_argument("out")
    ap.add_argument("--frame", default=None)
    ap.add_argument("--locked", action="store_true")
    ap.add_argument("--fill", type=float, default=0.94)
    ap.add_argument("--exposure", type=float, default=0.82)
    ap.add_argument("--contrast", type=float, default=1.15)
    ap.add_argument("--no-outline", action="store_true")
    ap.add_argument("--shadow", type=int, default=0, help="radio de la sombra en px; 0 la desactiva")
    a = ap.parse_args()
    compose(a.render, a.out, a.frame, a.fill, not a.no_outline, a.exposure, a.contrast, a.shadow, a.locked)
