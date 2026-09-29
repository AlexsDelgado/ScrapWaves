"""Retratos del diálogo de jefes: el Stalker (Assets/Arte/Gusano.fbx) enrollado en forma de signo de pregunta,
en dos variantes, oculto (silueta oscura) y revelado (colores de la paleta SW_*).

Uso (desde la raíz del repo, sin UI):
    blender -b --factory-startup --python ArtSource/UI/V2B/scripts/dialog_portraits.py
Escribe ArtSource/UI/V2B/renders/Portrait_Stalker_<hidden|revealed>.png y guarda ArtSource/UI/V2B/Portraits.blend.
Después `python ArtSource/Tools/finish_dialog.py` los mete en el marco de los íconos.
"""
import math
import os

import bpy
from mathutils import Vector

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", ".."))
TOOLS = os.path.join(REPO, "ArtSource", "Tools")
V2B = os.path.join(REPO, "ArtSource", "UI", "V2B")
FBX = os.path.join(REPO, "Assets", "Arte", "Gusano.fbx")

# Recorrido del cuerpo en el plano XZ, de la cola a la cabeza: rulo arriba a la izquierda y la cabeza sale
# hacia la derecha, como el boceto del diálogo.
PATH = [(-1.6, -4.2), (-2.3, -1.8), (-2.7, 0.8), (-1.6, 2.8), (0.6, 3.3), (2.3, 2.0), (2.1, 0.0), (0.6, -0.9),
        (0.4, -2.4), (2.0, -3.1), (4.0, -2.9)]
THICKNESS = 0.75  # el gusano real es muy grueso para enrollarlo; en el retrato se afina la sección

REVEALED = {"Metal.001": "metal", "Goma.002": "goma", "Estomago.001": "rojoInt", "Material.001": "chapa"}
HIDDEN = {"Metal.001": "sombra", "Goma.002": "goma", "Estomago.001": "sombraOsc", "Material.001": "sombraOsc"}


def _load_tools():
    for f in ("blender_icon_rig.py", "blender_model_kit.py"):
        path = os.path.join(TOOLS, f)
        exec(compile(open(path, encoding="utf-8").read(), path, "exec"), globals())
    ensure_palette()
    global VIEW_DIR
    VIEW_DIR = Vector((0.35, -1.0, 0.2)).normalized()  # casi de frente para que se lea el signo de pregunta
    for name, hex_color, metallic, rough in (("SW_Sombra", "#3b3b3a", 0.6, 0.6), ("SW_SombraOscura", "#242423", 0.3, 0.8)):
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.use_nodes = True
        bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        bsdf.inputs["Base Color"].default_value = _srgb_to_linear(hex_color)
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = rough
    MAT_ALIASES.update({"sombra": "SW_Sombra", "sombraOsc": "SW_SombraOscura"})


def _import_worm():
    bpy.ops.import_scene.fbx(filepath=FBX)
    worm = next(o for o in bpy.context.selected_objects if o.type == "MESH")
    for o in list(bpy.context.selected_objects):
        if o is not worm:
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.objects.active = worm
    worm.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for p in worm.data.polygons:
        p.use_smooth = True
    return worm


def _long_axis(obj):
    xs = [v.co for v in obj.data.vertices]
    ext = [max(v[i] for v in xs) - min(v[i] for v in xs) for i in range(3)]
    return max(range(3), key=lambda i: ext[i]), ext


def _head_sign(obj, axis):
    """+1 si la boca (material Estomago) está del lado positivo del eje largo."""
    idx = next(i for i, m in enumerate(obj.data.materials) if m.name.startswith("Estomago"))
    pts = [obj.data.vertices[v].co[axis] for p in obj.data.polygons if p.material_index == idx for v in p.vertices]
    return 1 if sum(pts) / len(pts) > 0 else -1


def _coil(worm):
    axis, ext = _long_axis(worm)
    sign = _head_sign(worm, axis)
    # El Curve deforma sobre +eje: la cola tiene que arrancar en el mínimo del eje largo.
    if sign < 0:
        worm.scale[axis] = -1
        bpy.ops.object.transform_apply(scale=True)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.flip_normals()
        bpy.ops.object.mode_set(mode="OBJECT")
    lo = min(v.co[axis] for v in worm.data.vertices)
    for v in worm.data.vertices:
        v.co[axis] -= lo
        for i in range(3):
            if i != axis:
                v.co[i] *= THICKNESS

    cd = bpy.data.curves.new("StalkerPath", "CURVE")
    cd.dimensions = "3D"
    sp = cd.splines.new("BEZIER")
    sp.bezier_points.add(len(PATH) - 1)
    for bp, (x, z) in zip(sp.bezier_points, PATH):
        bp.co = (x, 0, z)
        bp.handle_left_type = bp.handle_right_type = "AUTO"
    path = bpy.data.objects.new("StalkerPath", cd)
    path.hide_render = True
    bpy.context.scene.collection.objects.link(path)
    bpy.context.view_layer.update()
    # Escala el recorrido para que mida lo mismo que el gusano.
    length = sum((Vector((b[0], 0, b[1])) - Vector((a[0], 0, a[1]))).length for a, b in zip(PATH, PATH[1:]))
    path.scale = [ext[axis] / (length * 1.06)] * 3
    # Loops extra a lo largo para que los segmentos rígidos se doblen en vez de abrirse en las curvas.
    sub = worm.modifiers.new("Loops", "SUBSURF")
    sub.subdivision_type, sub.levels, sub.render_levels = "SIMPLE", 3, 3
    md = worm.modifiers.new("Enrollar", "CURVE")
    md.object = path
    md.deform_axis = ("POS_X", "POS_Y", "POS_Z")[axis]
    return worm, path


def _assign(worm, mapping):
    for slot in worm.material_slots:
        key = next((k for k in mapping if slot.material and slot.material.name.startswith(k.split(".")[0])), None)
        if key:
            slot.material = bpy.data.materials[MAT_ALIASES.get(mapping[key], mapping[key])]


def build():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _load_tools()
    worm, path = _coil(_import_worm())
    root = bpy.data.objects.new("Stalker_Root", None)
    bpy.context.scene.collection.objects.link(root)
    worm.parent = path.parent = root
    os.makedirs(os.path.join(V2B, "renders"), exist_ok=True)
    originals = [s.material for s in worm.material_slots]
    for variant, mapping in (("revealed", REVEALED), ("hidden", HIDDEN)):
        for slot, mat in zip(worm.material_slots, originals):
            slot.material = mat
        _assign(worm, mapping)
        out = os.path.join(V2B, "renders", f"Portrait_Stalker_{variant}.png")
        render_icon("Stalker_Root", out, upright=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(V2B, "Portraits.blend"))


if bpy.app.background:
    build()
