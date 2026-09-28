"""Rig de render para íconos de items. Se ejecuta dentro de Blender (4.5).

Convenciones de modelado para que el encuadre sea consistente entre items:
- Todo el item cuelga de un Empty raíz (p. ej. `AutomaticCannon_Root`) sin rotación.
- La boca o el frente del item apunta a +X, arriba es +Z y la cara lateral más interesante mira a -Y.
- Escala real aproximada en metros; el rig encuadra solo, así que el tamaño absoluto no importa.
- Colores planos con Principled BSDF (paleta `SW_*` de la plantilla, ver docs/09-Coherencia-estetica.md).

Uso desde Blender o por MCP:
    exec(open(r"C:/Alexs/Github/ScrapWaves2/ArtSource/Tools/blender_icon_rig.py").read())
    render_icon("AutomaticCannon_Root", r"C:/.../item_weapon_cannon_render.png")
    render_icon("Mortar_Root", r"C:/.../item_weapon_morter_render.png", upright=True)  # items apoyados en el suelo

Las primitivas de modelado están en blender_model_kit.py y cada arma tiene su script en ArtSource/Weapons/scripts/.
"""
import math

import bpy
from mathutils import Quaternion, Vector

RIG_COLLECTION = "IconRig"
CAMERA_NAME = "IconCam"
VIEW_DIR = Vector((1.5, -1.0, 0.45)).normalized()  # del item hacia la cámara: 3/4 frontal, algo elevada
SCREEN_AXIS_DEG = 25.0  # ángulo en pantalla del eje +X del item: la boca sube en diagonal hacia la derecha
RESOLUTION = 1024
MARGIN = 1.06

PALETTE = {
    "SW_Metal": ("#7E7E7E", 1.0, 0.6),
    "SW_ChapaClara": ("#ABABAB", 1.0, 0.8),
    "SW_Filo": ("#B2B2B2", 1.0, 0.64),
    "SW_Parche": ("#747474", 0.5, 0.45),
    "SW_Oxido": ("#8C5032", 1.0, 1.0),
    "SW_OxidoOscuro": ("#642E0D", 1.0, 1.0),
    "SW_Goma": ("#101010", 0.0, 0.75),
    "SW_Negro": ("#000000", 0.0, 1.0),
    "SW_Barro": ("#422821", 0.0, 0.5),
    "SW_Mostaza": ("#D0BD2F", 1.0, 0.55),
    "SW_RojoSenal": ("#E71D23", 0.0, 0.0),
    "SW_RojoInterno": ("#9D3F48", 0.0, 0.5),
    "SW_Laton": ("#A0782E", 1.0, 0.5),
}


def _srgb_to_linear(hex_color):
    h = hex_color.lstrip("#")
    rgb = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb] + [1.0]


def ensure_palette():
    for name, (hex_color, metallic, roughness) in PALETTE.items():
        mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.use_nodes = True
        mat.use_fake_user = True
        bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        color = _srgb_to_linear(hex_color)
        bsdf.inputs["Base Color"].default_value = color
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        mat.diffuse_color = color


def _rig_collection(scene):
    col = bpy.data.collections.get(RIG_COLLECTION)
    if col is None:
        col = bpy.data.collections.new(RIG_COLLECTION)
    if col.name not in scene.collection.children:
        scene.collection.children.link(col)
    return col


def _sun(col, name, energy, rotation_deg, color):
    obj = bpy.data.objects.get(name)
    if obj is None:
        data = bpy.data.lights.new(name, "SUN")
        obj = bpy.data.objects.new(name, data)
        col.objects.link(obj)
    obj.data.energy = energy
    obj.data.color = color
    obj.data.angle = math.radians(5)
    obj.data.use_shadow = False
    obj.rotation_euler = [math.radians(a) for a in rotation_deg]
    return obj


def ensure_rig(scene=None):
    scene = scene or bpy.context.scene
    col = _rig_collection(scene)

    cam = bpy.data.objects.get(CAMERA_NAME)
    if cam is None:
        cam = bpy.data.objects.new(CAMERA_NAME, bpy.data.cameras.new(CAMERA_NAME))
        col.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.clip_start, cam.data.clip_end = 0.01, 100
    scene.camera = cam

    _sun(col, "IconKey", 4.0, (50, 10, -35), (1.0, 0.96, 0.9))
    _sun(col, "IconRim", 3.0, (-60, 0, 160), (0.85, 0.9, 1.0))
    _sun(col, "IconFill", 1.0, (70, 0, -150), (1.0, 1.0, 1.0))

    world = scene.world or bpy.data.worlds.new("IconWorld")
    scene.world = world
    world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs[0].default_value = (0.45, 0.45, 0.48, 1)
    bg.inputs[1].default_value = 0.6

    try:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    except TypeError:
        scene.render.engine = "BLENDER_EEVEE"
    scene.eevee.taa_render_samples = 64
    scene.render.film_transparent = True
    scene.render.resolution_x = scene.render.resolution_y = RESOLUTION
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.view_settings.view_transform = "Standard"
    return cam


def _camera_rotation(upright=False):
    """`upright=True` mantiene +Z vertical en pantalla, para items que se apoyan en el suelo."""
    base = (-VIEW_DIR).to_track_quat("-Z", "Y")
    if upright:
        return base
    axis = base.inverted() @ Vector((1, 0, 0))
    roll = math.atan2(axis.y, axis.x) - math.radians(SCREEN_AXIS_DEG)
    return base @ Quaternion((0, 0, 1), roll)


def frame(root_name, scene=None, upright=False):
    scene = scene or bpy.context.scene
    cam = ensure_rig(scene)
    root = bpy.data.objects[root_name]
    depsgraph = bpy.context.evaluated_depsgraph_get()

    cam.rotation_mode = "QUATERNION"
    cam.rotation_quaternion = _camera_rotation(upright)
    cam.location = root.matrix_world.translation + VIEW_DIR * 10
    bpy.context.view_layer.update()

    inv = cam.matrix_world.inverted()
    xs, ys = [], []
    for obj in [root, *root.children_recursive]:
        if obj.type not in {"MESH", "CURVE"} or obj.hide_render:
            continue
        ev = obj.evaluated_get(depsgraph)
        mesh = ev.to_mesh()
        for v in mesh.vertices:
            p = inv @ (ev.matrix_world @ v.co)
            xs.append(p.x)
            ys.append(p.y)
        ev.to_mesh_clear()
    if not xs:
        raise ValueError(f"{root_name} no tiene mallas renderizables")

    center_local = Vector(((min(xs) + max(xs)) / 2, (min(ys) + max(ys)) / 2, 0))
    cam.location = cam.matrix_world @ center_local
    cam.data.ortho_scale = max(max(xs) - min(xs), max(ys) - min(ys)) * MARGIN
    return cam


def render_icon(root_name, out_path, scene=None, upright=False):
    scene = scene or bpy.context.scene
    frame(root_name, scene, upright)
    hidden = []
    for col in scene.collection.children_recursive:
        if col.name == RIG_COLLECTION:
            continue
        owns_root = root_name in col.all_objects
        if not owns_root and not col.hide_render:
            col.hide_render = True
            hidden.append(col)
    try:
        scene.render.filepath = out_path
        bpy.ops.render.render(write_still=True)
    finally:
        for col in hidden:
            col.hide_render = False
    return out_path
