"""Piezas compartidas del HUD: placas atornilladas, rebajes, tubos de barra y diales.

Requiere blender_model_kit.py y blender_hud_rig.py cargados. Todo se modela en el plano XZ mirando a -Y;
la cara frontal de la placa base queda en y = 0 y lo que sobresale hacia la cámara va con y negativa.
"""


def bolted_plate(name, x0, z0, x1, z1, mat="hierro", y=0.0, t=0.03, r=0.018, rivet_rows=(), step=0.07,
                 inset=0.022):
    plate(name, rrect(x0, z0, x1, z1, r), t, mat, y=y + t / 2)
    for z in rivet_rows:
        n = max(2, int((x1 - x0 - 2 * inset) / step) + 1)
        for i in range(n):
            rivet((x0 + inset + (x1 - x0 - 2 * inset) * i / (n - 1), y - 0.004, z), "Y", 0.0065)


def recess(name, x0, z0, x1, z1, y=0.0, lip=0.009, lip_mat="goma", fill="negro", depth=0.012):
    """Ventana oscura hundida con un labio alrededor. Registra el interior con `mark(name)`."""
    plate(f"{name}_Fondo", rrect(x0, z0, x1, z1, 0.004, 2), 0.004, fill, y=y - 0.002, bev=0)
    w, h = x1 - x0, z1 - z0
    for cx, cz, sx, sz in ((x0 + w / 2, z1 + lip / 2, w + 2 * lip, lip), (x0 + w / 2, z0 - lip / 2, w + 2 * lip, lip),
                           (x0 - lip / 2, z0 + h / 2, lip, h), (x1 + lip / 2, z0 + h / 2, lip, h)):
        box(f"{name}_Labio", (cx, y - depth / 2, cz), (sx, depth, sz), lip_mat, bev=0.002)
    mark(name, x0, z0, x1, z1)


def tube_bar(name, x0, x1, zc, h, y=0.0, cap="metal", band="laton", cap_w=0.05):
    """Carcasa de barra: canal oscuro entre dos tapas cilíndricas. El fill va en el canal (`mark(name)`)."""
    recess(name, x0, zc - h / 2, x1, zc + h / 2, y, lip=0.008, lip_mat="hierroMed")
    r = h * 0.7
    for side, xc in ((-1, x0 - cap_w / 2 - 0.008), (1, x1 + cap_w / 2 + 0.008)):
        cyl(f"{name}_Tapa", (xc, y - r * 0.4, zc), r, cap_w, cap, axis="X", verts=20, bev=0.004)
        for dx in (-cap_w * 0.28, cap_w * 0.28):
            cyl(f"{name}_Banda", (xc + dx, y - r * 0.4, zc), r * 1.08, 0.008, band, axis="X", verts=20, bev=0.001)
        cyl(f"{name}_Cierre", (xc + side * (cap_w / 2 + 0.006), y - r * 0.4, zc), r * 0.55, 0.012, "oxidoOsc",
            axis="X", verts=14, bev=0.002)


def dial(name, cx, cz, r_out, r_in, y=0.0, blocks=12, block_mat="metal", ring_mat="hierroMed"):
    """Anillo segmentado con ventana circular oscura (`mark(name)` sobre el cuadrado que la contiene)."""
    depth = 0.045
    tube(f"{name}_Anillo", (cx, y - depth / 2, cz), r_out, depth, r_out - r_in, ring_mat, axis="Y", verts=48)
    cyl(f"{name}_Fondo", (cx, y - 0.002, cz), r_in + 0.002, 0.004, "negro", axis="Y", verts=48, bev=0)
    tube(f"{name}_Labio", (cx, y - depth - 0.004, cz), r_in + 0.012, 0.01, 0.014, "goma", axis="Y", verts=48)
    rm, bw = (r_in + r_out) / 2, (r_out - r_in) * 1.08
    for i in range(blocks):
        a = 2 * math.pi * (i + 0.5) / blocks
        box(f"{name}_Bloque", (cx + math.cos(a) * rm, y - depth - 0.006, cz + math.sin(a) * rm),
            (bw, 0.014, 2 * math.pi * rm / blocks * 0.42), block_mat, rot=(0, -a, 0), bev=0.004)
        a2 = 2 * math.pi * i / blocks
        rivet((cx + math.cos(a2) * rm, y - depth - 0.004, cz + math.sin(a2) * rm), "Y", 0.006)
    mark(name, cx - r_in, cz - r_in, cx + r_in, cz + r_in)


def bracket(name, x, z, sx, sz, size=0.05, y=0.0, mat="metal"):
    """Escuadra triangular atornillada en una esquina; (sx, sz) apuntan hacia el interior."""
    plate(name, [(x, z), (x + sx * size, z), (x, z + sz * size)], 0.012, mat, y=y - 0.008, bev=0.003)
    rivet((x + sx * size * 0.3, y - 0.016, z + sz * size * 0.3), "Y", 0.006)


def emblem_cross(name, cx, cz, r=0.028, y=0.0, color="rojo"):
    cyl(f"{name}_Base", (cx, y - 0.008, cz), r, 0.014, "goma", axis="Y", verts=20, bev=0.003)
    box(f"{name}_V", (cx, y - 0.018, cz), (r * 0.36, 0.006, r * 1.2), color, bev=0.001)
    box(f"{name}_H", (cx, y - 0.018, cz), (r * 1.2, 0.006, r * 0.36), color, bev=0.001)


def emblem_chevron(name, cx, cz, r=0.028, y=0.0, color="mostaza"):
    cyl(f"{name}_Base", (cx, y - 0.008, cz), r, 0.014, "goma", axis="Y", verts=20, bev=0.003)
    for dz in (-r * 0.28, r * 0.22):
        for s in (-1, 1):
            box(f"{name}_Chevron", (cx + s * r * 0.24, y - 0.018, cz + dz), (r * 0.62, 0.006, r * 0.2), color,
                rot=(0, s * math.radians(35), 0), bev=0.001)
