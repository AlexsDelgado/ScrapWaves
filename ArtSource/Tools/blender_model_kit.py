"""Primitivas mid poly para modelar items con la paleta SW_* del rig de íconos.

Uso dentro de Blender (con blender_icon_rig.py ya ejecutado para tener la paleta):
    exec(open(r"C:/Alexs/Github/ScrapWaves2/ArtSource/Tools/blender_model_kit.py", encoding="utf-8").read())
    begin("RocketLauncher")
    cyl("Tubo", (0, 0, 0), 0.11, 1.1, "metal")
    with group("Pivote", (0.2, 0, 0), (0, -0.5, 0)):
        box("Pieza", (0.1, 0, 0), (0.1, 0.1, 0.1), "oxido")

Los ejes de `cyl`, `cone` y `tube` son 'X', 'Y' o 'Z'. En `cone`, r1 queda del lado negativo del eje X o Z.
"""
import math
from contextlib import contextmanager

import bmesh
import bpy
from mathutils import Euler, Vector

MAT_ALIASES = {
    "metal": "SW_Metal", "chapa": "SW_ChapaClara", "filo": "SW_Filo", "parche": "SW_Parche",
    "oxido": "SW_Oxido", "oxidoOsc": "SW_OxidoOscuro", "goma": "SW_Goma", "negro": "SW_Negro",
    "barro": "SW_Barro", "mostaza": "SW_Mostaza", "rojo": "SW_RojoSenal", "rojoInt": "SW_RojoInterno",
    "laton": "SW_Laton",
}
_AXIS_ROT = {"X": (0, math.pi / 2, 0), "Y": (math.pi / 2, 0, 0), "Z": (0, 0, 0)}
_ctx = {"col": None, "parents": []}


def _mat(key):
    return bpy.data.materials[MAT_ALIASES.get(key, key)]


def clear_item(name):
    col = bpy.data.collections.get(name)
    if col is None:
        return
    for obj in list(col.all_objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.collections.remove(col)
    for store in (bpy.data.meshes, bpy.data.curves):
        for block in list(store):
            if block.users == 0:
                store.remove(block)


def begin(name):
    """Crea la colección `name` con el Empty `<name>_Root` y lo deja como padre activo."""
    clear_item(name)
    col = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(col)
    root = bpy.data.objects.new(f"{name}_Root", None)
    root.empty_display_type = "ARROWS"
    root.empty_display_size = 0.4
    col.objects.link(root)
    _ctx["col"], _ctx["parents"] = col, [root]
    return root


def _adopt(obj, mat=None, bevel=0.0, bevel_angle=40):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    _ctx["col"].objects.link(obj)
    parent = _ctx["parents"][-1]
    world = obj.matrix_world.copy()
    obj.parent = parent
    obj.matrix_world = parent.matrix_world @ world
    if mat is not None and obj.data is not None:
        obj.data.materials.clear()
        obj.data.materials.append(_mat(mat))
    if bevel > 0:
        md = obj.modifiers.new("Bevel", "BEVEL")
        md.width, md.segments = bevel, 1
        md.limit_method, md.angle_limit = "ANGLE", math.radians(bevel_angle)
    if obj.type == "MESH":
        for p in obj.data.polygons:
            p.use_smooth = False
    return obj


@contextmanager
def group(name, loc=(0, 0, 0), rot=(0, 0, 0)):
    """Empty intermedio: lo modelado dentro del bloque usa coordenadas locales del grupo."""
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_size = 0.1
    _ctx["col"].objects.link(empty)
    empty.parent = _ctx["parents"][-1]
    empty.location, empty.rotation_euler = loc, rot
    bpy.context.view_layer.update()
    _ctx["parents"].append(empty)
    try:
        yield empty
    finally:
        _ctx["parents"].pop()


def box(name, loc, size, mat, rot=(0, 0, 0), bev=0.008):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return _adopt(obj, mat, bev)


def cyl(name, loc, r, depth, mat, axis="X", verts=16, bev=0.005, rot=None):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc,
                                        rotation=rot or _AXIS_ROT[axis])
    obj = bpy.context.active_object
    obj.name = name
    return _adopt(obj, mat, bev)


def cone(name, loc, r1, r2, depth, mat, axis="X", verts=16, bev=0.004, rot=None):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=depth, location=loc,
                                    rotation=rot or _AXIS_ROT[axis])
    obj = bpy.context.active_object
    obj.name = name
    return _adopt(obj, mat, bev)


def tube(name, loc, r_out, depth, thickness, mat, axis="X", verts=16, r2=None, rot=None):
    """Tubo abierto (sin tapas) con grosor real. `r2` permite una boca cónica."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, segments=verts, radius1=r_out, radius2=r2 or r_out, depth=depth)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = loc
    obj.rotation_euler = rot or _AXIS_ROT[axis]
    bpy.context.view_layer.update()
    md = obj.modifiers.new("Grosor", "SOLIDIFY")
    md.thickness, md.offset, md.use_even_offset = thickness, -1, True
    return _adopt(obj, mat, 0.004)


def sphere(name, loc, r, mat, scale=(1, 1, 1), segments=12, rings=8):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=r, location=loc)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return _adopt(obj, mat)


def plate(name, outline_xz, thickness, mat, y=0.0, bev=0.004):
    """Placa extruida desde un contorno en el plano XZ, con grosor sobre Y."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    verts = [bm.verts.new((x, y - thickness / 2, z)) for x, z in outline_xz]
    face = bm.faces.new(verts)
    res = bmesh.ops.extrude_face_region(bm, geom=[face])
    moved = [e for e in res["geom"] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, verts=moved, vec=(0, thickness, 0))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return _adopt(obj, mat, bev, bevel_angle=30)


def pipe(name, points, radius, mat, resolution=8):
    cd = bpy.data.curves.new(name, "CURVE")
    cd.dimensions = "3D"
    cd.bevel_depth, cd.bevel_resolution, cd.resolution_u = radius, 2, resolution
    sp = cd.splines.new("BEZIER")
    sp.bezier_points.add(len(points) - 1)
    for bp, p in zip(sp.bezier_points, points):
        bp.co = p
        bp.handle_left_type = bp.handle_right_type = "AUTO"
    obj = bpy.data.objects.new(name, cd)
    bpy.context.scene.collection.objects.link(obj)
    cd.materials.append(_mat(mat))
    return _adopt(obj)


def rivet(loc, axis="Y", r=0.012, mat="filo"):
    return cyl("Remache", loc, r, 0.012, mat, axis, 8, 0.003)


def ring_of(count, radius, center, axis, make):
    """Llama make(pos, angle) en `count` posiciones alrededor de `center`, en el plano normal a `axis`."""
    cx, cy, cz = center
    for i in range(count):
        a = 2 * math.pi * i / count
        u, v = math.cos(a) * radius, math.sin(a) * radius
        pos = {"X": (cx, cy + u, cz + v), "Y": (cx + u, cy, cz + v), "Z": (cx + u, cy + v, cz)}[axis]
        make(pos, a)
