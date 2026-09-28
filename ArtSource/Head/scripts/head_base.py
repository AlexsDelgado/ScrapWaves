"""Casco de moto base compartido por los pasivos de cabeza. Requiere blender_model_kit ya cargado.

El personaje es humano: casco integral con visera curva, mentonera y pivotes laterales.
La visera mira a +X, arriba es +Z y el costado visible es -Y. La cáscara es un elipsoide centrado en C.
"""
C = (0.0, 0.0, 0.16)
RX, RY, RZ = 0.132, 0.108, 0.12


def smooth(obj):
    for p in obj.data.polygons:
        p.use_smooth = True
    return obj


def on_shell(lon, lat, off=0.0):
    """Punto sobre la cáscara. `lon` 0 es el frente y -90 el costado visible; `lat` 90 es la coronilla."""
    lo, la = math.radians(lon), math.radians(lat)
    return (C[0] + math.cos(la) * math.cos(lo) * (RX + off),
            C[1] + math.cos(la) * math.sin(lo) * (RY + off),
            C[2] + math.sin(la) * (RZ + off))


def shell_patch(name, lon, lat, mat, off=0.003, thickness=0.006, seg=(12, 5)):
    """Parche curvo que copia la cáscara entre los rangos de longitud y latitud dados (grados)."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    nu, nv = seg
    grid = [[bm.verts.new(on_shell(lon[0] + (lon[1] - lon[0]) * i / nu, lat[0] + (lat[1] - lat[0]) * j / nv, off))
             for i in range(nu + 1)] for j in range(nv + 1)]
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


def helmet(shell="oxido", visor="negro", chin="metal", stripe="mostaza"):
    cyl("Acolchado", (0, 0, 0.062), 0.07, 0.02, "goma", axis="Z", verts=20, bev=0.006)
    cyl("Abertura", (0, 0, 0.051), 0.058, 0.004, "negro", axis="Z", verts=20, bev=0)
    smooth(sphere("Casco", C, 1.0, shell, scale=(RX, RY, RZ), segments=28, rings=18))

    shell_patch("Visor", (-58, 58), (-12, 24), visor, off=0.003, seg=(16, 6))
    shell_patch("Visor_BordeSup", (-61, 61), (24, 28), "goma", off=0.005, seg=(16, 1))
    shell_patch("Mentonera", (-64, 64), (-55, -12), chin, off=0.012, thickness=0.012, seg=(16, 5))
    shell_patch("Mentonera_Borde", (-66, 66), (-14, -10), "goma", off=0.014, seg=(16, 1))
    for lon in (-14, 0, 14):
        box("Mentonera_Ventila", on_shell(lon, -34, 0.026), (0.006, 0.02, 0.006), "negro",
            rot=(0, math.radians(34), math.radians(lon)), bev=0)

    for s in (-1, 1):
        p = on_shell(s * 80, 6, 0.004)
        cyl("Visor_Pivote", p, 0.022, 0.012, "oxidoOsc", axis="Y", verts=14, bev=0.003)
        cyl("Visor_PivoteEje", (p[0], p[1] + s * 0.008, p[2]), 0.009, 0.008, "laton", axis="Y", verts=8, bev=0.002)

    shell_patch("Franja", (-7, 7), (28, 90), stripe, off=0.004, seg=(2, 10))
    shell_patch("Franja_Atras", (173, 187), (-25, 90), stripe, off=0.004, seg=(2, 14))

    shell_patch("Remiendo", (-150, -118), (-12, 18), "parche", off=0.004, seg=(4, 3))
    for lon, lat in ((-146, -8), (-146, 14), (-122, -8), (-122, 14)):
        rivet(on_shell(lon, lat, 0.009), "Y", 0.005)
