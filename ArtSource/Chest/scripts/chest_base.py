"""Peto de chatarra base compartido por los pasivos de pecho. Requiere blender_model_kit ya cargado.

El personaje es humano: chaleco blindado sobre un traje oscuro, con sisas, escote, tirantes y cinturón lateral.
El pecho mira a +X, arriba es +Z y el costado visible es -Y. El peto va de z 0 a ~0.275.
"""
TOP = 0.275


def rx(z):
    return 0.072 + 0.014 * math.sin(math.pi * min(z / TOP, 1.0))


def ry(z):
    return 0.112 + 0.032 * min(z / TOP, 1.0)


def ztop(a):
    c = abs(math.cos(math.radians(a)))
    return 0.2 + (TOP - 0.2) * c ** 0.6 - 0.035 * c ** 8


def on_torso(a, z, off=0.0):
    """Punto sobre el torso. `a` 0 es el frente y -90 el costado visible."""
    r = math.radians(a)
    return (math.cos(r) * (rx(z) + off), math.sin(r) * (ry(z) + off), z)


def shoulder(s, lift=0.0):
    """Punto sobre el tirante del hombro (`s` -1 es el hombro visible)."""
    return (0.0, s * 0.092, 0.256 + lift)


def smooth(obj):
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def torso_patch(name, a_range, z0, z1, mat, off=0.004, thickness=0.008, seg=(10, 4)):
    """Parche que copia el torso. `z0`/`z1` son alturas o funciones de `a`; `None` en z1 sigue el borde superior."""
    f0 = z0 if callable(z0) else (lambda a: z0)
    f1 = ztop if z1 is None else (z1 if callable(z1) else (lambda a: z1))
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    nu, nv = seg
    grid = []
    for j in range(nv + 1):
        row = []
        for i in range(nu + 1):
            a = a_range[0] + (a_range[1] - a_range[0]) * i / nu
            lo, hi = f0(a), min(f1(a), ztop(a))
            row.append(bm.verts.new(on_torso(a, lo + (hi - lo) * j / nv, off)))
        grid.append(row)
    for j in range(nv):
        for i in range(nu):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    md = obj.modifiers.new("Grosor", "SOLIDIFY")
    md.thickness, md.offset = thickness, 1
    return smooth(_adopt(obj, mat))


def vest(shell="metal", back="parche", trim="goma"):
    smooth(sphere("Traje", (0, 0, 0.14), 1.0, "goma", scale=(0.074, 0.13, 0.138), segments=28, rings=18))
    cyl("Cuello", (0, 0, 0.2745), 1.0, 0.004, "negro", axis="Z", verts=24, bev=0).scale = (0.038, 0.05, 1.0)
    tube("Cuello_Borde", (0, 0, 0.2735), 1.0, 0.006, 0.14, "oxidoOsc", axis="Z", verts=24).scale = (0.044, 0.058, 1.0)
    torso_patch("Peto", (-100, 100), 0.0, None, shell, off=0.0, thickness=0.012, seg=(28, 10))
    torso_patch("Espaldar", (100, 260), 0.0, None, back, off=0.0, thickness=0.012, seg=(20, 10))
    torso_patch("Borde", (-180, 180), lambda a: ztop(a) - 0.014, None, trim, off=0.012, seg=(48, 1))
    torso_patch("Ruedo", (-180, 180), 0.0, 0.016, trim, off=0.012, seg=(48, 1))

    for s in (-1, 1):
        pipe("Tirante", [on_torso(s * 42, ztop(42) - 0.01, 0.018), shoulder(s),
                         on_torso(s * 138, ztop(138) - 0.01, 0.018)], 0.016, trim, resolution=10)
        torso_patch("Cinturon", (s * 70 - 22, s * 70 + 22), 0.06, 0.085, trim, off=0.012, seg=(6, 1))
    box("Cinturon_Hebilla", on_torso(-70, 0.0725, 0.024), (0.03, 0.01, 0.032), "laton",
        rot=(0, 0, math.radians(20)), bev=0.003)

    torso_patch("Faja_Baja", (-50, 50), 0.028, 0.066, "chapa", off=0.012, thickness=0.01, seg=(10, 1))
    torso_patch("Faja_Media", (-46, 46), 0.074, 0.104, "chapa", off=0.012, thickness=0.01, seg=(10, 1))
    for a in (-38, 38):
        for z in (0.047, 0.089):
            rivet(on_torso(a, z, 0.026), "X", 0.005).rotation_euler.z = math.radians(a)
