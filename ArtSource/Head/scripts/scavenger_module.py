"""Scavenger module: casco con linterna frontal y un imán de herradura en la coronilla atrayendo chatarra.

Representa la recolección de chatarra (Scavenging +5 a +30).
"""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "head_base.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("ScavengerModule")
d = math.radians
helmet(shell="oxido", chin="metal", stripe="chapa")

shell_patch("Linterna_Correa", (-95, 95), (32, 38), "goma", off=0.006, seg=(16, 1))
with group("Linterna", on_shell(0, 35, 0.018), (0, d(-35), 0)):
    cyl("Linterna_Cuerpo", (0, 0, 0), 0.022, 0.024, "metal", verts=14, bev=0.003)
    cyl("Linterna_Lente", (0.013, 0, 0), 0.017, 0.004, "mostaza", verts=14, bev=0)

Z = 0.285
box("Iman_Base", (-0.01, 0, Z - 0.005), (0.05, 0.05, 0.02), "oxidoOsc", bev=0.004)
pipe("Iman", [(-0.01, -0.045, Z + 0.045), (-0.01, -0.045, Z + 0.022), (-0.01, 0, Z + 0.005),
              (-0.01, 0.045, Z + 0.022), (-0.01, 0.045, Z + 0.045)], 0.016, "rojo", resolution=10)
for y in (-0.045, 0.045):
    cyl("Iman_Polo", (-0.01, y, Z + 0.055), 0.017, 0.024, "chapa", axis="Z", verts=14, bev=0.003)

with group("Tuerca", (0.0, -0.045, Z + 0.1), (d(20), d(35), 0)):
    cyl("Tuerca_Cuerpo", (0, 0, 0), 0.018, 0.012, "laton", axis="Z", verts=6, bev=0.002)
    cyl("Tuerca_Agujero", (0, 0, 0), 0.008, 0.014, "negro", axis="Z", verts=10, bev=0)
with group("Tornillo", (-0.02, 0.045, Z + 0.105), (d(-30), d(20), 0)):
    cyl("Tornillo_Cabeza", (0, 0, 0.02), 0.012, 0.009, "metal", axis="Z", verts=6, bev=0.002)
    cyl("Tornillo_Vastago", (0, 0, -0.004), 0.005, 0.045, "filo", axis="Z", verts=8, bev=0)
with group("Engranaje", (0.005, 0.0, Z + 0.13), (d(60), 0, d(20))):
    cyl("Engranaje_Disco", (0, 0, 0), 0.02, 0.008, "metal", axis="Z", verts=14, bev=0.002)
    ring_of(8, 0.023, (0, 0, 0), "Z", lambda p, a: box("Diente", p, (0.007, 0.007, 0.008), "metal",
                                                       rot=(0, 0, a), bev=0.001))
box("Chapa_Suelta", (0.02, -0.075, Z + 0.13), (0.028, 0.02, 0.004), "parche", rot=(d(40), d(-20), 0), bev=0.001)
