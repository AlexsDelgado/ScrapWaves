"""Sharpshooter module: casco con una mira telescópica abatible frente al ojo derecho y telémetro con antena.

Representa el daño a larga distancia (LongRangeDamageMultiplier x1.2 a x2.2).
"""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "head_base.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("SharpshooterModule")
d = math.radians
helmet(shell="parche", stripe="mostaza")

pivot = on_shell(-80, 6, 0.012)
pipe("Mira_Brazo", [pivot, (0.07, -0.112, 0.2), (0.135, -0.06, 0.19)], 0.006, "filo")
with group("Mira", (0.14, -0.04, 0.17)):
    cyl("Mira_Ocular", (0.0, 0, 0), 0.026, 0.02, "goma", verts=16, bev=0.003)
    cyl("Mira_Tubo", (0.06, 0, 0), 0.021, 0.11, "metal", verts=16, bev=0.003)
    for x in (0.03, 0.09):
        cyl("Mira_Anillo", (x, 0, 0), 0.025, 0.012, "oxidoOsc", verts=16, bev=0.002)
    tube("Mira_Visera", (0.125, 0, 0), 0.028, 0.03, 0.004, "oxido", verts=16)
    cyl("Mira_Lente", (0.118, 0, 0), 0.023, 0.004, "rojoInt", verts=16, bev=0)
    sphere("Mira_Punto", (0.121, 0, 0), 0.006, "rojo", segments=8, rings=6)
    cyl("Mira_Perilla", (0.06, 0, 0.026), 0.009, 0.012, "laton", axis="Z", verts=10, bev=0.002)
    box("Mira_Union", (0.02, -0.018, 0.018), (0.02, 0.012, 0.012), "oxidoOsc", bev=0.002)

with group("Telemetro", (-0.045, -0.114, 0.19)):
    box("Telemetro_Caja", (0, 0, 0), (0.055, 0.02, 0.042), "mostaza", bev=0.004)
    box("Telemetro_Visor", (0.01, -0.011, 0.006), (0.026, 0.004, 0.014), "negro", bev=0)
    for x in (-0.02, 0.02):
        rivet((x, -0.011, -0.013), "Y", 0.004)
pipe("Antena", [(-0.06, -0.112, 0.21), (-0.09, -0.1, 0.33)], 0.004, "filo")
sphere("Antena_Luz", (-0.091, -0.1, 0.335), 0.01, "rojo", segments=8, rings=6)
