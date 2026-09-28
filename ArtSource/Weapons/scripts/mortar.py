"""Mortar: tubo grueso inclinado sobre placa base y bípode, con granadas apiladas al lado."""
import math

begin("Mortar")
d = math.radians
TILT = d(52)
PIVOT = (-0.12, 0.0, 0.1)


def along_tube(t):
    return (PIVOT[0] + math.cos(TILT) * t, PIVOT[1], PIVOT[2] + math.sin(TILT) * t)


# Placa base con remaches y púas de anclaje.
cyl("Placa", (0, 0, 0.02), 0.3, 0.04, "parche", axis="Z", verts=10, bev=0.008)
cyl("Placa_Borde", (0, 0, 0.005), 0.31, 0.012, "oxidoOsc", axis="Z", verts=10, bev=0.003)
for a in range(0, 360, 72):
    x, y = math.cos(d(a + 18)) * 0.24, math.sin(d(a + 18)) * 0.24
    rivet((x, y, 0.043), "Z", 0.013)
for a in range(0, 360, 120):
    x, y = math.cos(d(a + 60)) * 0.27, math.sin(d(a + 60)) * 0.27
    cone("Pua", (x, y, -0.04), 0.02, 0.0, 0.08, "oxido", axis="Z", verts=6, rot=(math.pi, 0, 0))
box("Placa_Refuerzo", (0, 0, 0.05), (0.36, 0.05, 0.03), "metal", bev=0.006)
box("Placa_Refuerzo2", (0, 0, 0.05), (0.05, 0.36, 0.03), "metal", bev=0.006)

# Rótula y tubo inclinado.
sphere("Rotula", PIVOT, 0.08, "goma", segments=10, rings=6)
with group("Tubo_Pivote", PIVOT, (0, -TILT, 0)):
    cyl("Recamara", (0.04, 0, 0), 0.145, 0.1, "oxidoOsc", verts=14, bev=0.01)
    tube("Tubo", (0.45, 0, 0), 0.13, 0.78, 0.02, "metal", verts=16)
    cyl("Tubo_Interior", (0.45, 0, 0), 0.11, 0.76, "negro", verts=16, bev=0)
    tube("Boca", (0.83, 0, 0), 0.148, 0.07, 0.035, "filo", verts=16)
    for x in (0.28, 0.62):
        cyl("Banda", (x, 0, 0), 0.138, 0.04, "oxido", verts=16, bev=0.004)
    cyl("Collar_Bipode", (0.5, 0, 0), 0.142, 0.06, "goma", verts=16, bev=0.004)
    # Dial de alcance al costado visible.
    box("Dial_Soporte", (0.2, -0.14, 0.0), (0.06, 0.03, 0.04), "goma", bev=0.004)
    cyl("Dial", (0.2, -0.165, 0.0), 0.05, 0.02, "chapa", axis="Y", verts=14, bev=0.003)
    box("Dial_Marca", (0.2, -0.177, 0.025), (0.006, 0.004, 0.022), "rojo", bev=0)
    for dx in (-0.035, 0.035):
        rivet((0.2 + dx, -0.177, -0.028), "Y", 0.006)

# Bípode con travesaño y manivela de elevación.
top = along_tube(0.5)
for s in (-1, 1):
    foot = (0.42, s * 0.3, 0.02)
    pipe("Pata", [(top[0] + 0.02, s * 0.1, top[2] - 0.05), foot], 0.02, "chapa")
    box("Pata_Pie", (foot[0], foot[1], 0.012), (0.07, 0.05, 0.025), "oxido", bev=0.004)
brace_z = 0.2
brace_x = 0.42 + (top[0] + 0.02 - 0.42) * ((brace_z - 0.02) / (top[2] - 0.07))
cyl("Travesano", (brace_x, 0, brace_z), 0.014, 0.44, "filo", axis="Y", verts=8)
pipe("Tornillo_Elevacion", [(brace_x, 0, brace_z), (top[0] + 0.03, 0, top[2] - 0.11)], 0.016, "parche")
cyl("Manivela_Rueda", (brace_x, -0.24, brace_z), 0.04, 0.015, "oxidoOsc", axis="Y", verts=10, bev=0.003)
box("Manivela_Brazo", (brace_x + 0.03, -0.25, brace_z), (0.06, 0.012, 0.012), "filo", bev=0.002)
cyl("Manivela_Perilla", (brace_x + 0.055, -0.265, brace_z), 0.01, 0.03, "goma", axis="Y", verts=8)


def shell(name, loc, rot=(0, 0, 0)):
    with group(name, loc, rot):
        cyl(name + "_Cuerpo", (0, 0, 0.07), 0.055, 0.12, "parche", axis="Z", verts=12)
        cyl(name + "_Franja", (0, 0, 0.1), 0.057, 0.025, "mostaza", axis="Z", verts=12, bev=0.002)
        cone(name + "_Ojiva", (0, 0, 0.17), 0.055, 0.015, 0.08, "metal", axis="Z", verts=12)
        cyl(name + "_Espoleta", (0, 0, 0.215), 0.016, 0.02, "laton", axis="Z", verts=8)
        cone(name + "_Cola", (0, 0, -0.02), 0.022, 0.05, 0.06, "goma", axis="Z", verts=10)
        for a in range(0, 360, 90):
            box(name + "_Aleta", (math.cos(d(a)) * 0.035, math.sin(d(a)) * 0.035, -0.05),
                (0.035 if a % 180 == 0 else 0.006, 0.006 if a % 180 == 0 else 0.035, 0.05), "goma", bev=0.002)


shell("Granada_A", (0.08, -0.4, 0.08))
shell("Granada_B", (0.22, -0.38, 0.08), (0, 0, d(30)))
shell("Granada_C", (0.12, -0.52, 0.06), (d(90), 0, d(-20)))
