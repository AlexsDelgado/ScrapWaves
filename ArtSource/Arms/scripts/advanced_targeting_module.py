"""Advanced targeting module: guantelete con sensor de puntería, mira telescópica y pantalla con retícula.

Representa la probabilidad de crítico (CriticalChance +0.05 a +0.4).
"""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "gauntlet_base.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("AdvancedTargetingModule")
d = math.radians
gauntlet()

box("Sensor", (-0.05, 0, 0.105), (0.13, 0.085, 0.075), "parche", bev=0.008)
box("Sensor_Tapa", (-0.05, 0, 0.146), (0.12, 0.075, 0.01), "chapa", bev=0.003)
box("Pantalla_Marco", (-0.06, -0.044, 0.105), (0.08, 0.006, 0.055), "oxidoOsc", bev=0.002)
box("Pantalla", (-0.06, -0.048, 0.105), (0.066, 0.004, 0.042), "negro", bev=0)
tube("Reticula", (-0.06, -0.051, 0.105), 0.014, 0.003, 0.003, "rojo", axis="Y", verts=16)
box("Reticula_H", (-0.06, -0.051, 0.105), (0.04, 0.002, 0.003), "rojo", bev=0)
box("Reticula_V", (-0.06, -0.051, 0.105), (0.003, 0.002, 0.034), "rojo", bev=0)

cyl("Mira", (0.06, 0, 0.165), 0.024, 0.2, "metal", verts=14, bev=0.003)
cyl("Mira_Ocular", (-0.045, 0, 0.165), 0.029, 0.03, "goma", verts=14, bev=0.003)
tube("Mira_Visera", (0.17, 0, 0.165), 0.031, 0.04, 0.005, "oxido", verts=14)
cyl("Mira_Lente", (0.163, 0, 0.165), 0.025, 0.004, "rojoInt", verts=14, bev=0)
sphere("Mira_Punto", (0.166, 0, 0.165), 0.007, "rojo", segments=8, rings=6)
for x in (-0.02, 0.08):
    cyl("Mira_Anillo", (x, 0, 0.165), 0.028, 0.014, "oxidoOsc", verts=14, bev=0.002)
    box("Mira_Soporte", (x, 0, 0.14), (0.014, 0.02, 0.03), "oxidoOsc", bev=0.002)
cyl("Mira_Perilla", (0.03, -0.03, 0.165), 0.01, 0.014, "laton", axis="Y", verts=10, bev=0.002)

pipe("Antena", [(-0.1, 0.02, 0.15), (-0.11, 0.02, 0.25)], 0.004, "filo")
sphere("Antena_Luz", (-0.11, 0.02, 0.255), 0.011, "rojo", segments=8, rings=6)
