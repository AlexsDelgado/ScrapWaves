"""Tecla 3D "E" que flota sobre la crafting station (y cualquier cosa con la que se interactúa), con el lenguaje
del HUD V2B: base de acero claro con esquinas cortadas y bulones abombados, tecla oscura biselada sobre un aro
de cobre y la letra en relieve mostaza.

Uso (desde la raíz del repo, sin UI):
    blender -b --factory-startup --python ArtSource/UI/V2B/scripts/interact_key.py
Escribe Assets/Art/Props/InteractKey/InteractKey_E.fbx, el preview ArtSource/UI/V2B/renders/InteractKey_E.png
y guarda ArtSource/UI/V2B/InteractKey.blend. La cara frontal mira a -Y en Blender.
"""
import math
import os

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", "..", ".."))
TOOLS = os.path.join(REPO, "ArtSource", "Tools")
V2B = os.path.join(REPO, "ArtSource", "UI", "V2B")
OUT_DIR = os.path.join(REPO, "Assets", "Art", "Props", "InteractKey")
LETTER = "E"
FONTS = (r"C:\Windows\Fonts\arialbd.ttf", r"C:\Windows\Fonts\segoeuib.ttf")


def _exec(path):
    exec(compile(open(path, encoding="utf-8").read(), path, "exec"), globals())


def _letter(text, size, depth, y, mat):
    """Letra en relieve: curva de texto convertida a malla, centrada en X/Z y apoyada en la tecla."""
    curve = bpy.data.curves.new(f"Letra_{text}", "FONT")
    curve.body = text
    curve.size = size
    curve.extrude = depth / 2
    curve.bevel_depth = depth * 0.12
    curve.align_x, curve.align_y = "CENTER", "CENTER"
    for path in FONTS:
        if os.path.exists(path):
            curve.font = bpy.data.fonts.load(path)
            break
    obj = bpy.data.objects.new(f"Letra_{text}", curve)
    bpy.context.scene.collection.objects.link(obj)
    obj.rotation_euler = (math.radians(90), 0, 0)  # el texto nace en XY; lo paramos en XZ mirando a -Y
    obj.location = (0, y, 0)
    bpy.context.view_layer.objects.active = obj
    for o in bpy.context.selected_objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.ops.object.convert(target="MESH")
    obj = bpy.context.active_object
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    obj.data.materials.clear()
    obj.data.materials.append(bpy.data.materials[MAT_ALIASES[mat]])
    return _adopt(obj)


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for f in ("blender_icon_rig.py", "blender_model_kit.py", "blender_hud_rig.py"):
        _exec(os.path.join(TOOLS, f))
    ensure_palette()
    ensure_hud_rig()
    _exec(os.path.join(HERE, "parts_v2b.py"))
    _exec(os.path.join(HERE, "style.py"))
    apply_style()
    # El rig del HUD solo aporta paleta y helpers: se renderiza con el rig de íconos (3/4), y blender_hud_rig.py
    # pisa MARGIN con el margen en unidades del HUD.
    global MARGIN
    MARGIN = 1.06
    bpy.data.collections[HUD_RIG].hide_render = True

    begin("InteractKey")
    # A escala del HUD (las piezas de parts_v2b tienen bulones y biseles absolutos); Unity la escala después.
    S = 0.12  # medio lado de la base
    frame_panel("Base", -S, -S, S, S, border=0.026, chamfer=0.036, depth=0.03, y=0.0)
    plate("Base_Fondo", chamfer_poly(-S, -S, S, S, 0.036), 0.03, "panel", y=0.015, bev=0.004)
    tube("Aro", (0, -0.03, 0), 0.084, 0.014, 0.008, "oxido", axis="Y", verts=48)
    box("Tecla", (0, -0.042, 0), (0.132, 0.034, 0.132), "panel", bev=0.013)
    box("Tecla_Cara", (0, -0.059, 0), (0.108, 0.004, 0.108), "negro", bev=0.003)
    _letter(LETTER, 0.13, 0.012, -0.064, "mostaza")

    os.makedirs(os.path.join(V2B, "renders"), exist_ok=True)
    render_icon("InteractKey_Root", os.path.join(V2B, "renders", f"InteractKey_{LETTER}.png"), upright=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(V2B, "InteractKey.blend"))

    os.makedirs(OUT_DIR, exist_ok=True)
    root = bpy.data.objects["InteractKey_Root"]
    for o in bpy.context.selected_objects:
        o.select_set(False)
    for o in [root, *root.children_recursive]:
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT_DIR, f"InteractKey_{LETTER}.fbx"), use_selection=True,
                             apply_scale_options="FBX_SCALE_ALL", use_mesh_modifiers=True, mesh_smooth_type="FACE",
                             bake_space_transform=True, object_types={"EMPTY", "MESH"})


if bpy.app.background:
    build()
