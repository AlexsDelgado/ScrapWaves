"""Regenerative Nano-tech: peto con módulo médico de cruz roja y viales de nanobots conectados por tubos.

Representa la regeneración de vida (HealthRegeneration +1 a +6) y la reducción de su demora.
"""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "chest_base.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("RegenerativeNanotech")
d = math.radians
vest(shell="chapa", back="metal")

with group("Modulo", on_torso(0, 0.165, 0.012)):
    cyl("Modulo_Aro", (0.004, 0, 0), 0.052, 0.016, "oxidoOsc", verts=20, bev=0.003)
    cyl("Modulo_Cara", (0.012, 0, 0), 0.044, 0.006, "filo", verts=20, bev=0.001)
    box("Cruz_V", (0.017, 0, 0), (0.006, 0.02, 0.064), "rojo", bev=0.002)
    box("Cruz_H", (0.017, 0, 0), (0.006, 0.064, 0.02), "rojo", bev=0.002)

with group("Viales", on_torso(-62, 0.2, 0.02), (0, 0, d(-62))):
    box("Viales_Soporte", (0, 0, 0), (0.016, 0.07, 0.07), "oxidoOsc", bev=0.003)
    for y in (-0.022, 0.0, 0.022):
        cyl("Vial", (0.014, y, 0.0), 0.009, 0.05, "rojoInt", axis="Z", verts=10, bev=0.001)
        cyl("Vial_Tapa", (0.014, y, 0.03), 0.01, 0.01, "laton", axis="Z", verts=10, bev=0.001)
        cyl("Vial_Base", (0.014, y, -0.03), 0.01, 0.008, "metal", axis="Z", verts=10, bev=0.001)
for i, y in enumerate((-0.022, 0.0, 0.022)):
    start = on_torso(-62 + y * 250, 0.24, 0.04)
    pipe("Tubo", [start, on_torso(-40 + i * 6, 0.255 - i * 0.01, 0.045), on_torso(-14, 0.2 + i * 0.012, 0.03)],
         0.004, "goma")

with group("Pantalla", on_torso(28, 0.12, 0.022), (0, 0, d(28))):
    box("Pantalla_Marco", (0, 0, 0), (0.008, 0.05, 0.028), "oxidoOsc", bev=0.002)
    box("Pantalla_Cara", (0.005, 0, 0), (0.003, 0.04, 0.018), "negro", bev=0)
    for k, y in enumerate((-0.012, 0.0, 0.012)):
        box("Pantalla_Barra", (0.007, y, -0.004 + k * 0.003), (0.002, 0.008, 0.006 + k * 0.004), "rojo", bev=0)
