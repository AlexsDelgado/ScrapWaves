"""Construye las piezas del HUD, guarda ArtSource/UI/HUD.blend, renderiza cada pieza y escribe el layout.

Uso (desde la raíz del repo, sin UI):
    blender -b --factory-startup --python ArtSource/Tools/blender_build_hud.py
o por MCP:  exec(open(r"<repo>/ArtSource/Tools/blender_build_hud.py", encoding="utf-8").read())

Los PNG crudos van a ArtSource/UI/renders/ y el layout (tamaños y ventanas en px desde arriba-izquierda) a
ArtSource/UI/HudLayout.json, que lee el menú ScrapWaves → UI → Apply HUD Art de Unity.
Después hay que correr `python ArtSource/Tools/finish_hud.py` para el contorno y los sprites finales.
"""
import json
import os

import bpy

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")) \
    if "__file__" in globals() else r"D:/Projects/ScrapWaves"
TOOLS = os.path.join(REPO, "ArtSource", "Tools")
UI_DIR = os.path.join(REPO, "ArtSource", "UI")
PIECES = (("hud_left.py", "HudLeft"), ("hud_center.py", "HudCenter"), ("hud_right.py", "HudRight"),
          ("hud_badge.py", "HudBadge"))


def _reset_scene():
    """Vacía la escena sin recargar el archivo, para no cortar la sesión del MCP."""
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for col in list(bpy.data.collections):
        bpy.data.collections.remove(col)
    for store in (bpy.data.meshes, bpy.data.curves, bpy.data.lights, bpy.data.cameras):
        for block in list(store):
            if block.users == 0:
                store.remove(block)


def build_hud(pieces=PIECES):
    _reset_scene()
    ns = {"__name__": "__hud__"}
    for f in ("blender_icon_rig.py", "blender_model_kit.py", "blender_hud_rig.py"):
        path = os.path.join(TOOLS, f)
        exec(compile(open(path, encoding="utf-8").read(), path, "exec"), ns)
    ns["ensure_palette"]()
    ns["ensure_hud_rig"]()

    os.makedirs(os.path.join(UI_DIR, "renders"), exist_ok=True)
    layout_path = os.path.join(UI_DIR, "HudLayout.json")
    old = json.load(open(layout_path, encoding="utf-8")) if os.path.exists(layout_path) else {"pieces": []}
    layout = {p["name"]: p for p in old["pieces"]}
    for script, root in pieces:
        ns["reset_marks"]()
        path = os.path.join(UI_DIR, "scripts", script)
        ns["__file__"] = path
        exec(compile(open(path, encoding="utf-8").read(), path, "exec"), ns)
        info = ns["render_piece"](f"{root}_Root", os.path.join(UI_DIR, "renders", f"{root}.png"))
        layout[root] = {"name": root, "size": info["size"],
                        "rects": [{"name": k, "rect": v} for k, v in info["rects"].items()]}

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(UI_DIR, "HUD.blend"))
    # Formato de listas para que Unity lo lea con JsonUtility (HudArtApplier).
    with open(layout_path, "w", encoding="utf-8") as fh:
        json.dump({"pieces": list(layout.values())}, fh, indent=2)
    return layout


if __name__ in ("__main__", "<run_path>") or bpy.app.background:
    print(json.dumps(build_hud()))
