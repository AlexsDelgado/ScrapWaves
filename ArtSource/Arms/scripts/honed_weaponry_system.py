"""Honed Weaponry System: guantelete con una hoja afilada sobre el antebrazo y una piedra de amolar.

Representa el multiplicador de daño (DamageMultiplier +0.15 a +0.9).
"""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "gauntlet_base.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("HonedWeaponrySystem")
d = math.radians
gauntlet()

plate("Hoja", [(-0.13, 0.075), (0.3, 0.075), (0.38, 0.11), (0.3, 0.135), (-0.11, 0.135)], 0.012, "filo")
box("Hoja_Lomo", (0.08, 0, 0.14), (0.4, 0.02, 0.014), "metal", bev=0.003)
for x in (-0.09, 0.0):
    box("Hoja_Abrazadera", (x, 0, 0.1), (0.03, 0.036, 0.06), "oxidoOsc", bev=0.004)
    rivet((x, -0.019, 0.11), "Y", 0.006)

with group("Amoladora", (-0.03, -0.085, 0.04)):
    cyl("Piedra", (0, 0, 0), 0.05, 0.022, "parche", axis="Y", verts=16, bev=0.004)
    cyl("Piedra_Cara", (0, -0.012, 0), 0.036, 0.004, "chapa", axis="Y", verts=16, bev=0)
    cyl("Piedra_Eje", (0, -0.016, 0), 0.013, 0.014, "laton", axis="Y", verts=8, bev=0.002)
    box("Amoladora_Brazo", (0, 0.02, -0.02), (0.02, 0.03, 0.05), "oxidoOsc", bev=0.003)

for i, (x, z, s) in enumerate(((0.03, 0.09, 0.012), (0.06, 0.12, 0.009), (0.02, 0.14, 0.007))):
    box("Chispa", (x, -0.09, z), (s * 2.2, 0.004, s), "mostaza", rot=(0, d(-30 - i * 15), 0), bev=0)
