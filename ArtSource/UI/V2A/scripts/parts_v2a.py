"""Piezas de la variante A del HUD, tomada de Assets/Arte/Player_Bars.png: hierro oscuro, dial de bloques
gruesos, tubos de vidrio con tapas macizas, caños que unen el dial con las barras y tiras remachadas.

Se carga encima de ArtSource/UI/scripts/hud_parts.py (mismo plano XZ, cámara en -Y).
"""
import math
import os

_base = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "scripts", "hud_parts.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))


def riveted_strip(name, x0, x1, zc, h=0.036, step=0.034, y=-0.004):
    """Tira de acero atornillada a lo largo de la placa, con remaches chicos y parejos."""
    plate(name, rrect(x0, zc - h / 2, x1, zc + h / 2, 0.005), 0.012, "metal", y=y - 0.006, bev=0.003)
    n = max(2, int((x1 - x0 - 0.03) / step) + 1)
    for i in range(n):
        rivet((x0 + 0.015 + (x1 - x0 - 0.03) * i / (n - 1), y - 0.014, zc), "Y", 0.0058)


def corner_bracket(name, x, z, sx, sz, size=0.075, y=0.0):
    """Escuadra triangular gruesa, como las esquinas derechas de la referencia."""
    plate(name, [(x, z), (x + sx * size, z), (x, z + sz * size)], 0.018, "metal", y=y - 0.016, bev=0.004)
    rivet((x + sx * size * 0.3, y - 0.028, z + sz * size * 0.3), "Y", 0.0075)


def chunky_dial(name, cx, cz, r_out, r_in, y=0.0, blocks=12, big_every=3):
    """Aro de bloques macizos: uno grande cada `big_every`, anillo interno remachado y ventana oscura."""
    depth = 0.05
    tube(f"{name}_Anillo", (cx, y - depth / 2, cz), r_out * 0.94, depth, r_out * 0.94 - r_in, "hierroMed",
         axis="Y", verts=64)
    cyl(f"{name}_Fondo", (cx, y - 0.002, cz), r_in + 0.002, 0.004, "negro", axis="Y", verts=64, bev=0)
    tube(f"{name}_Labio", (cx, y - depth - 0.004, cz), r_in + 0.016, 0.012, 0.018, "goma", axis="Y", verts=64)
    ring_r = r_in + 0.028
    tube(f"{name}_AroInt", (cx, y - depth - 0.01, cz), ring_r + 0.012, 0.01, 0.022, "hierro", axis="Y", verts=64)
    for i in range(16):
        a = 2 * math.pi * (i + 0.5) / 16
        rivet((cx + math.cos(a) * ring_r, y - depth - 0.018, cz + math.sin(a) * ring_r), "Y", 0.0052)
    inner = ring_r + 0.014
    rm, radial = (inner + r_out) / 2, r_out - inner
    for i in range(blocks):
        a = 2 * math.pi * i / blocks + math.pi / 2
        big = i % big_every == 0
        w = 2 * math.pi * rm / blocks * (0.9 if big else 0.74)
        rad = radial * (1.18 if big else 0.96)
        off = radial * 0.08 if big else 0.0
        box(f"{name}_Bloque", (cx + math.cos(a) * (rm + off), y - depth - 0.014, cz + math.sin(a) * (rm + off)),
            (rad, 0.026 if big else 0.02, w), "metal", rot=(0, -a, 0), bev=0.011)
        rivet((cx + math.cos(a) * (rm + off), y - depth - 0.03, cz + math.sin(a) * (rm + off)), "Y", 0.0058)
    mark(name, cx - r_in, cz - r_in, cx + r_in, cz + r_in)


def glass_tube(name, x0, x1, zc, h, y=0.0, left_bolts=True, right="bolts"):
    """Barra de vidrio oscuro entre dos tapas cilíndricas macizas con bandas y tornillos laterales."""
    recess(name, x0, zc - h / 2, x1, zc + h / 2, y, lip=0.009, lip_mat="hierroMed")
    # Brillo del vidrio por encima del fill: una franja fina arriba, sin tapar el color.
    box(f"{name}_Brillo", ((x0 + x1) / 2, y - 0.014, zc + h * 0.34), (x1 - x0 - 0.02, 0.002, h * 0.08),
        "chapa", bev=0)
    r = h * 0.72
    cap_w = 0.056
    for side, xc in ((-1, x0 - cap_w / 2 - 0.006), (1, x1 + cap_w / 2 + 0.006)):
        cyl(f"{name}_Tapa", (xc, y - r * 0.45, zc), r, cap_w, "metal", axis="X", verts=24, bev=0.006)
        for dx in (-cap_w * 0.36, cap_w * 0.36):
            cyl(f"{name}_Banda", (xc + dx, y - r * 0.45, zc), r * 1.1, 0.011, "hierroMed", axis="X", verts=24,
                bev=0.002)
        kind = ("bolts" if left_bolts else None) if side < 0 else right
        edge = xc + side * (cap_w / 2 + 0.004)
        if kind == "bolts":
            for dz in (-r * 0.42, r * 0.42):
                cyl(f"{name}_Tornillo", (edge + side * 0.006, y - r * 0.45, zc + dz), r * 0.3, 0.014, "metal",
                    axis="X", verts=6, bev=0.002)
        elif kind == "plug":
            cyl(f"{name}_Buje", (edge + side * 0.01, y - r * 0.45, zc), r * 0.5, 0.02, "hierroMed", axis="X",
                verts=16, bev=0.003)
            pipe(f"{name}_Lazo", [(edge + side * 0.018, y - r * 0.45, zc + r * 0.3),
                                  (edge + side * 0.04, y - r * 0.45, zc + r * 0.2),
                                  (edge + side * 0.04, y - r * 0.45, zc - r * 0.2),
                                  (edge + side * 0.018, y - r * 0.45, zc - r * 0.3)], 0.0055, "metal")


def cable(name, points, radius=0.0085, y=-0.012, mat="hierroMed"):
    pipe(name, [(x, y, z) for x, z in points], radius, mat)


def leds(name, cx, cz, radius, degrees, y=-0.07):
    for deg in degrees:
        a = math.radians(deg)
        p = (cx + math.cos(a) * radius, cz + math.sin(a) * radius)
        cyl(f"{name}_Base", (p[0], y + 0.006, p[1]), 0.0105, 0.008, "goma", axis="Y", verts=12, bev=0.002)
        sphere(name, (p[0], y, p[1]), 0.0072, "led", segments=10, rings=6)


def seams(name, x0, x1, z0, z1, count=3, y=-0.001):
    """Juntas verticales finas que parten la placa en chapas, como la madera/hierro del fondo."""
    for i in range(1, count + 1):
        x = x0 + (x1 - x0) * i / (count + 1)
        box(name, (x, y, (z0 + z1) / 2), (0.004, 0.002, z1 - z0), "negro", bev=0)


def socket(name, x0, z0, x1, z1, bezel=0.016, y=0.0):
    """Socket hundido con bisel de metal macizo y un remache en cada esquina."""
    recess(name, x0, z0, x1, z1, y, lip=0.008, lip_mat="goma", depth=0.012)
    b, w, h = bezel, x1 - x0, z1 - z0
    for cx, cz, sx, sz in ((x0 + w / 2, z1 + 0.008 + b / 2, w + 2 * (b + 0.008), b),
                           (x0 + w / 2, z0 - 0.008 - b / 2, w + 2 * (b + 0.008), b),
                           (x0 - 0.008 - b / 2, z0 + h / 2, b, h + 0.016),
                           (x1 + 0.008 + b / 2, z0 + h / 2, b, h + 0.016)):
        box(f"{name}_Bisel", (cx, y - 0.012, cz), (sx, 0.022, sz), "metal", bev=0.005)
    for px in (x0 - 0.008 - b / 2, x1 + 0.008 + b / 2):
        for pz in (z0 - 0.008 - b / 2, z1 + 0.008 + b / 2):
            rivet((px, y - 0.026, pz), "Y", 0.0062)
