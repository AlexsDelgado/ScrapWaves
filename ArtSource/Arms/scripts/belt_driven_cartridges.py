"""Belt-driven cartridges: guantelete con tambor de munición y cinta de cartuchos hacia la muñeca.

Representa la velocidad de ataque (AttackSpeedMultiplier +0.15 a +0.9): alimentación continua y engranaje motor.
"""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "gauntlet_base.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("BeltDrivenCartridges")
d = math.radians
gauntlet()

cyl("Tambor", (-0.15, 0, 0.125), 0.065, 0.1, "oxido", axis="Y", verts=18, bev=0.006)
for y in (-0.052, 0.052):
    cyl("Tambor_Tapa", (-0.15, y, 0.125), 0.058, 0.006, "chapa", axis="Y", verts=18, bev=0.002)
cyl("Tambor_Eje", (-0.15, -0.058, 0.125), 0.016, 0.012, "laton", axis="Y", verts=8, bev=0.002)
ring_of(6, 0.044, (-0.15, -0.056, 0.125), "Y", lambda p, a: rivet(p, "Y", 0.005))

box("Alimentador", (0.09, 0, 0.09), (0.07, 0.08, 0.05), "metal", bev=0.005)
box("Alimentador_Boca", (0.05, 0, 0.09), (0.012, 0.07, 0.03), "negro", bev=0)

for i in range(6):
    x = -0.085 + i * 0.024
    z = 0.083 + 0.018 * math.cos((i / 5) * math.pi / 2 + 0.2)
    cyl("Cartucho", (x, 0.005, z), 0.0095, 0.055, "laton", axis="Y", verts=10, bev=0.002)
    cone("Cartucho_Punta", (x, -0.03, z), 0.0095, 0.003, 0.016, "oxido", axis="Y", verts=10, bev=0)
    box("Eslabon", (x + 0.012, 0.0, z - 0.004), (0.006, 0.05, 0.006), "goma", bev=0)

with group("Engranaje", (0.0, -0.066, -0.01)):
    cyl("Engranaje_Disco", (0, 0, 0), 0.034, 0.012, "laton", axis="Y", verts=16, bev=0.002)
    ring_of(10, 0.038, (0, 0, 0), "Y", lambda p, a: box("Diente", p, (0.01, 0.012, 0.01), "laton",
                                                        rot=(0, -a, 0), bev=0.001))
    cyl("Engranaje_Eje", (0, -0.008, 0), 0.01, 0.008, "oxidoOsc", axis="Y", verts=8, bev=0.001)
