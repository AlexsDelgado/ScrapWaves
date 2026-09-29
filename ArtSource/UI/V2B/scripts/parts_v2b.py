"""Piezas de la variante B del HUD, con el lenguaje de los íconos de armas e items: marcos de acero claro con
bulones abombados en las esquinas, panel oscuro hundido, bandas de cobre, acentos mostaza y franjas de peligro.
Los diales son manómetros y las barras se arman como cañones con bocas y bandas.

Se carga encima de ArtSource/UI/scripts/hud_parts.py (mismo plano XZ, cámara en -Y).
"""
import math
import os

import bmesh

_base = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "scripts", "hud_parts.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))


def chamfer_poly(x0, z0, x1, z1, c, corners=(1, 1, 1, 1)):
    """Rectángulo con esquinas cortadas a 45°, siempre 8 vértices (c=0 en una esquina la deja recta).
    Orden de `corners`: abajo-izq, abajo-der, arriba-der, arriba-izq."""
    bl, br, tr, tl = (c * k for k in corners)
    return [(x0 + bl, z0), (x1 - br, z0), (x1, z0 + br), (x1, z1 - tr),
            (x1 - tr, z1), (x0 + tl, z1), (x0, z1 - tl), (x0, z0 + bl)]


def ring_plate(name, outer, inner, thickness, mat, y=0.0, bev=0.004):
    """Marco extruido entre dos contornos con la misma cantidad de vértices (placa con agujero)."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    yf, yb = y - thickness, y
    of = [bm.verts.new((x, yf, z)) for x, z in outer]
    inf = [bm.verts.new((x, yf, z)) for x, z in inner]
    ob = [bm.verts.new((x, yb, z)) for x, z in outer]
    ib = [bm.verts.new((x, yb, z)) for x, z in inner]
    n = len(outer)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((of[i], of[j], inf[j], inf[i]))
        bm.faces.new((ob[j], ob[i], ib[i], ib[j]))
        bm.faces.new((of[j], of[i], ob[i], ob[j]))
        bm.faces.new((inf[i], inf[j], ib[j], ib[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    return _adopt(obj, mat, bev, bevel_angle=30)


def domed_bolt(loc, r=0.011, y_off=0.0):
    x, y, z = loc
    cyl("Bulon_Base", (x, y + y_off, z), r * 1.25, 0.008, "metal", axis="Y", verts=6, bev=0.002)
    sphere("Bulon", (x, y + y_off - 0.006, z), r, "filo", scale=(1, 0.55, 1), segments=14, rings=8)


def frame_panel(name, x0, z0, x1, z1, border=0.026, chamfer=0.03, corners=(1, 1, 1, 1), bolts=True, y=0.0,
                depth=0.028):
    """Marco de acero claro sobre panel oscuro, como el marco de los íconos. Devuelve el interior."""
    outer = chamfer_poly(x0, z0, x1, z1, chamfer, corners)
    ci = max(0.0, chamfer - border * 0.4)
    inner = chamfer_poly(x0 + border, z0 + border, x1 - border, z1 - border, ci, corners)
    plate(f"{name}_Panel", inner, 0.01, "panel", y=y + 0.005, bev=0)
    ring_plate(f"{name}_Marco", outer, inner, depth, "chapa", y=y, bev=0.005)
    if bolts:
        k = border * 0.5 + chamfer * 0.35
        for bx, bz, on in ((x0 + k, z0 + k, corners[0]), (x1 - k, z0 + k, corners[1]),
                           (x1 - k, z1 - k, corners[2]), (x0 + k, z1 - k, corners[3])):
            if on:
                domed_bolt((bx, y - depth, bz), min(0.012, border * 0.42))
    return x0 + border, z0 + border, x1 - border, z1 - border


def copper_band(name, x, zc, r, w=0.012, y=0.0):
    cyl(name, (x, y, zc), r, w, "oxido", axis="X", verts=24, bev=0.002)


def barrel_bar(name, x0, x1, zc, h, y=0.0, left="muzzle", right="breech"):
    """Barra como caño de arma: canal oscuro, boca con anillo de un lado y culata con bandas de cobre del otro."""
    recess(name, x0, zc - h / 2, x1, zc + h / 2, y, lip=0.007, lip_mat="goma", depth=0.01)
    box(f"{name}_Brillo", ((x0 + x1) / 2, y - 0.012, zc + h * 0.33), (x1 - x0 - 0.016, 0.002, h * 0.08),
        "filo", bev=0)
    r = h * 0.66
    yc = y - r * 0.5
    for side, kind, edge in ((-1, left, x0 - 0.007), (1, right, x1 + 0.007)):
        if kind == "muzzle":
            cyl(f"{name}_Boca", (edge + side * 0.018, yc, zc), r, 0.036, "chapa", axis="X", verts=24, bev=0.005)
            cyl(f"{name}_BocaAro", (edge + side * 0.04, yc, zc), r * 1.12, 0.012, "filo", axis="X", verts=24,
                bev=0.003)
            copper_band(f"{name}_Banda", edge + side * 0.008, zc, r * 1.04, 0.01, yc)
        else:
            cyl(f"{name}_Culata", (edge + side * 0.026, yc, zc), r * 1.05, 0.052, "chapa", axis="X", verts=24,
                bev=0.006)
            for dx in (0.01, 0.042):
                copper_band(f"{name}_Banda", edge + side * dx, zc, r * 1.13, 0.009, yc)
            cyl(f"{name}_Tapon", (edge + side * 0.058, yc, zc), r * 0.55, 0.014, "metal", axis="X", verts=6,
                bev=0.002)


def gauge(name, cx, cz, r_out, r_in, y=0.0, ticks=24, hot_from=0.72, bolts=6):
    """Manómetro: bisel grueso de acero claro, aro de cobre, marcas alrededor de la ventana y bulones."""
    depth = 0.05
    tube(f"{name}_Bisel", (cx, y - depth / 2, cz), r_out, depth, r_out - r_in - 0.02, "chapa", axis="Y", verts=64)
    tube(f"{name}_Cara", (cx, y - 0.008, cz), r_in + 0.024, 0.016, 0.026, "panel", axis="Y", verts=64)
    cyl(f"{name}_Fondo", (cx, y - 0.002, cz), r_in + 0.002, 0.004, "negro", axis="Y", verts=64, bev=0)
    tube(f"{name}_Cobre", (cx, y - depth - 0.002, cz), r_in + 0.034, 0.008, 0.01, "oxido", axis="Y", verts=64)
    tube(f"{name}_Labio", (cx, y - 0.018, cz), r_in + 0.008, 0.01, 0.01, "goma", axis="Y", verts=64)
    # Marcas de 220° de barrido en sentido horario, de abajo a la izquierda a abajo a la derecha.
    rm = r_in + 0.014
    for i in range(ticks + 1):
        t = i / ticks
        a = math.radians(200 - 220 * t)
        big = i % 4 == 0
        mat = "rojo" if t > 0.92 else ("mostaza" if t >= hot_from else "filo")
        box(f"{name}_Marca", (cx + math.cos(a) * rm, y - 0.018, cz + math.sin(a) * rm),
            (0.016 if big else 0.009, 0.004, 0.0036 if big else 0.0026), mat, rot=(0, -a, 0), bev=0)
    rb = (r_out + r_in + 0.02) / 2 + 0.006
    for i in range(bolts):
        a = 2 * math.pi * (i + 0.5) / bolts + math.pi / 2
        domed_bolt((cx + math.cos(a) * rb, y - depth, cz + math.sin(a) * rb), 0.0105)
    mark(name, cx - r_in, cz - r_in, cx + r_in, cz + r_in)


def _clip(poly, x0, z0, x1, z1):
    """Sutherland–Hodgman contra un rectángulo alineado."""
    def cut(pts, inside, inter):
        out = []
        for i, p in enumerate(pts):
            q = pts[i - 1]
            if inside(p):
                if not inside(q):
                    out.append(inter(q, p))
                out.append(p)
            elif inside(q):
                out.append(inter(q, p))
        return out

    def ix(v):
        return lambda a, b: (v, a[1] + (b[1] - a[1]) * (v - a[0]) / (b[0] - a[0]))

    def iz(v):
        return lambda a, b: (a[0] + (b[0] - a[0]) * (v - a[1]) / (b[1] - a[1]), v)

    for inside, inter in ((lambda p: p[0] >= x0, ix(x0)), (lambda p: p[0] <= x1, ix(x1)),
                          (lambda p: p[1] >= z0, iz(z0)), (lambda p: p[1] <= z1, iz(z1))):
        poly = cut(poly, inside, inter)
        if not poly:
            break
    return poly


def hazard(name, x0, z0, x1, z1, stripe=0.018, y=0.0):
    """Placa mostaza con franjas negras a 45°, recortadas al rectángulo."""
    plate(f"{name}_Base", rrect(x0, z0, x1, z1, 0.004), 0.008, "mostaza", y=y - 0.004, bev=0.002)
    h = z1 - z0
    x = x0 - h
    while x < x1:
        poly = [(x, z0), (x + stripe, z0), (x + stripe + h, z1), (x + h, z1)]
        poly = _clip(poly, x0 + 0.002, z0 + 0.002, x1 - 0.002, z1 - 0.002)
        if len(poly) >= 3:
            plate(f"{name}_Franja", poly, 0.002, "goma", y=y - 0.009, bev=0)
        x += stripe * 2
