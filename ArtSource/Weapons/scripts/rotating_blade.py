"""RotatingBlade: hoja de chapa con filo y dientes, montada en un rotor con engranaje y contrapeso."""
import math

begin("RotatingBlade")
d = math.radians
BY = -0.03  # plano de la hoja

# Filo: contorno completo y más fino, asoma en los dientes, el vientre y la punta.
edge = [(0.05, 0.08), (0.3, 0.09)]
for i in range(8):
    x = 0.32 + i * 0.06
    edge += [(x, 0.09), (x + 0.03, 0.128)]
edge += [(0.8, 0.09), (0.95, 0.085), (1.13, -0.01), (0.95, -0.115), (0.7, -0.135), (0.45, -0.125),
         (0.25, -0.105), (0.05, -0.085)]
plate("Hoja_Filo", edge, 0.014, "filo", y=BY)

body = [(0.05, 0.08), (0.95, 0.085), (1.04, 0.015), (0.95, -0.078), (0.7, -0.098), (0.45, -0.09),
        (0.25, -0.072), (0.05, -0.052)]
plate("Hoja", body, 0.026, "metal", y=BY)

# Agujeros de alivianado, parche soldado y placa de sujeción.
for x in (0.34, 0.46):
    cyl("Hoja_Agujero", (x, BY - 0.0135, -0.005), 0.024, 0.002, "negro", axis="Y", verts=10, bev=0)
box("Parche", (0.66, BY - 0.015, 0.0), (0.12, 0.006, 0.09), "parche", rot=(0, d(8), 0), bev=0.002)
for dx, dz in ((-0.045, 0.03), (0.045, -0.03), (-0.045, -0.03), (0.045, 0.03)):
    rivet((0.66 + dx, BY - 0.02, dz), "Y", 0.008)
box("Sujecion", (0.17, BY - 0.018, 0.0), (0.16, 0.012, 0.15), "oxido", bev=0.004)
for dx in (-0.05, 0.05):
    for dz in (-0.05, 0.05):
        rivet((0.17 + dx, BY - 0.026, dz), "Y", 0.01)

# Rotor: carcasa, engranaje, tapa con tuerca central.
cyl("Rotor", (0, 0.03, 0), 0.16, 0.09, "metal", axis="Y", verts=16, bev=0.008)


def tooth(pos, a):
    box("Diente", pos, (0.045, 0.07, 0.035), "parche", rot=(0, -a, 0), bev=0.004)


ring_of(18, 0.175, (0, 0.03, 0), "Y", tooth)
cyl("Separador", (0, -0.045, 0), 0.08, 0.03, "goma", axis="Y", verts=12, bev=0.003)
cyl("Tapa", (0, -0.07, 0), 0.12, 0.025, "chapa", axis="Y", verts=14, bev=0.005)
cyl("Tuerca", (0, -0.09, 0), 0.042, 0.03, "oxidoOsc", axis="Y", verts=6, bev=0.004)
cyl("Tuerca_Eje", (0, -0.106, 0), 0.016, 0.01, "filo", axis="Y", verts=8, bev=0.002)
ring_of(6, 0.088, (0, -0.085, 0), "Y", lambda p, a: rivet(p, "Y", 0.01))

# Contrapeso del lado opuesto: deja claro que la pieza gira.
box("Contrapeso_Brazo", (-0.24, BY, 0.0), (0.22, 0.03, 0.07), "metal", bev=0.006)
box("Contrapeso", (-0.4, BY, 0.0), (0.12, 0.08, 0.16), "goma", bev=0.012)
box("Contrapeso_Banda", (-0.4, BY, 0.0), (0.03, 0.088, 0.168), "oxido", bev=0.004)

# Motor detrás del rotor, con cable.
cyl("Motor", (0, 0.13, 0), 0.1, 0.12, "goma", axis="Y", verts=12, bev=0.008)
for y in (0.1, 0.13, 0.16):
    cyl("Motor_Aleta", (0, y, 0), 0.11, 0.008, "metal", axis="Y", verts=12, bev=0)
pipe("Cable", [(-0.06, 0.18, -0.06), (-0.14, 0.22, -0.2), (-0.02, 0.2, -0.3)], 0.014, "goma")
