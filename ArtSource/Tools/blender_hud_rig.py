"""Rig de render frontal para las piezas del HUD. Se ejecuta dentro de Blender, después de blender_icon_rig.py
(paleta SW_*) y blender_model_kit.py (primitivas).

Convenciones:
- Cada pieza se modela en el plano XZ (X a la derecha, Z arriba) y mira a -Y, hacia la cámara.
- 1 unidad = PX_PER_UNIT píxeles de render en todas las piezas, así comparten escala en Unity.
- `mark(name, x0, z0, x1, z1)` registra una ventana (fill, slot, texto) de la pieza en curso. `render_piece`
  la devuelve en píxeles desde la esquina superior izquierda del PNG.
"""
import math

import bpy
from mathutils import Vector

HUD_RIG = "HudRig"
HUD_CAM = "HudCam"
PX_PER_UNIT = 1000
MARGIN = 0.014  # deja lugar para el contorno que se agrega al post-procesar

_marks = {}

# Complementa la paleta SW_* con el hierro oscuro del marco de los íconos, para las placas del HUD.
HUD_PALETTE = {
    "hierro": ("SW_HierroOscuro", "#3a3a3a", 0.85, 0.38),
    "hierroMed": ("SW_HierroMedio", "#6a6a6a", 0.92, 0.32),
}


def ensure_hud_palette():
    for alias, (name, hex_color, metallic, roughness) in HUD_PALETTE.items():
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.use_nodes = True
        mat.use_fake_user = True
        bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        color = _srgb_to_linear(hex_color)
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        mat.diffuse_color = color
        MAT_ALIASES[alias] = name


def rrect(x0, z0, x1, z1, r, seg=4):
    """Contorno de rectángulo con esquinas redondeadas en el plano XZ."""
    pts = []
    for cx, cz, a0 in ((x1 - r, z0 + r, -90), (x1 - r, z1 - r, 0), (x0 + r, z1 - r, 90), (x0 + r, z0 + r, 180)):
        for i in range(seg + 1):
            a = math.radians(a0 + 90 * i / seg)
            pts.append((cx + math.cos(a) * r, cz + math.sin(a) * r))
    return pts


def mark(name, x0, z0, x1, z1):
    _marks[name] = (min(x0, x1), min(z0, z1), max(x0, x1), max(z0, z1))


def reset_marks():
    _marks.clear()


def _light(col, name, energy, direction, color=(1, 1, 1)):
    obj = bpy.data.objects.get(name)
    if obj is None:
        obj = bpy.data.objects.new(name, bpy.data.lights.new(name, "SUN"))
        col.objects.link(obj)
    obj.data.energy, obj.data.color, obj.data.use_shadow = energy, color, False
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector(direction).normalized().to_track_quat("-Z", "Y")
    return obj


def ensure_hud_rig(scene=None):
    scene = scene or bpy.context.scene
    ensure_hud_palette()
    col = bpy.data.collections.get(HUD_RIG) or bpy.data.collections.new(HUD_RIG)
    if col.name not in scene.collection.children:
        scene.collection.children.link(col)
    cam = bpy.data.objects.get(HUD_CAM)
    if cam is None:
        cam = bpy.data.objects.new(HUD_CAM, bpy.data.cameras.new(HUD_CAM))
        col.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.clip_start, cam.data.clip_end = 0.01, 50
    cam.rotation_mode = "XYZ"
    cam.rotation_euler = (math.radians(90), 0, 0)
    scene.camera = cam

    # Luz rasante: el HUD es un relieve visto de frente, así que el contraste sale de arriba y de la oclusión.
    _light(col, "HudKey", 6.5, (0.25, 0.35, -1.0), (1.0, 0.97, 0.9))
    _light(col, "HudRim", 2.4, (-0.85, 0.25, 0.45), (0.72, 0.8, 1.0))
    _light(col, "HudFill", 0.28, (0.1, 1.0, 0.05))

    world = scene.world or bpy.data.worlds.new("HudWorld")
    scene.world = world
    world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.45, 0.45, 0.48, 1)
    bg.inputs[1].default_value = 0.18

    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        scene.render.engine = "BLENDER_EEVEE"
    scene.eevee.taa_render_samples = 64
    scene.eevee.use_fast_gi = True
    scene.eevee.fast_gi_method = "AMBIENT_OCCLUSION_ONLY"
    scene.eevee.fast_gi_distance = 0.06
    scene.eevee.fast_gi_quality = 1.0
    scene.eevee.fast_gi_bias = 0.02
    scene.render.film_transparent = True
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    return cam


def _bounds_xz(root):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    xs, zs = [], []
    for obj in [root, *root.children_recursive]:
        if obj.type not in {"MESH", "CURVE"} or obj.hide_render:
            continue
        ev = obj.evaluated_get(depsgraph)
        mesh = ev.to_mesh()
        for v in mesh.vertices:
            p = ev.matrix_world @ v.co
            xs.append(p.x)
            zs.append(p.z)
        ev.to_mesh_clear()
    return min(xs) - MARGIN, min(zs) - MARGIN, max(xs) + MARGIN, max(zs) + MARGIN


def render_piece(root_name, out_path, scene=None):
    """Renderiza la pieza con escala fija y devuelve {tamaño, ventanas en px (x, y, w, h) desde arriba-izquierda}."""
    scene = scene or bpy.context.scene
    cam = ensure_hud_rig(scene)
    root = bpy.data.objects[root_name]
    bpy.context.view_layer.update()
    x0, z0, x1, z1 = _bounds_xz(root)
    w_px, h_px = round((x1 - x0) * PX_PER_UNIT), round((z1 - z0) * PX_PER_UNIT)
    x1, z1 = x0 + w_px / PX_PER_UNIT, z0 + h_px / PX_PER_UNIT
    scene.render.resolution_x, scene.render.resolution_y = w_px, h_px
    cam.data.ortho_scale = max(x1 - x0, z1 - z0)
    cam.location = ((x0 + x1) / 2, -5.0, (z0 + z1) / 2)

    hidden = []
    for col in scene.collection.children_recursive:
        if col.name != HUD_RIG and root_name not in col.all_objects and not col.hide_render:
            col.hide_render = True
            hidden.append(col)
    try:
        scene.render.filepath = out_path
        bpy.ops.render.render(write_still=True)
    finally:
        for col in hidden:
            col.hide_render = False

    rects = {}
    for name, (mx0, mz0, mx1, mz1) in _marks.items():
        rects[name] = [round((mx0 - x0) * PX_PER_UNIT), round((z1 - mz1) * PX_PER_UNIT),
                       round((mx1 - mx0) * PX_PER_UNIT), round((mz1 - mz0) * PX_PER_UNIT)]
    return {"size": [w_px, h_px], "rects": rects}
