"""Plated Reactor: peto con placas pectorales gruesas, hombreras y un reactor hexagonal blindado en el centro.

Representa la vida máxima (MaxHealth +30 a +200).
"""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "chest_base.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("PlatedReactor")
d = math.radians
vest(shell="oxido", back="parche")

for a_range in ((-72, -12), (12, 72)):
    torso_patch("Pectoral", a_range, 0.115, lambda a: ztop(a) - 0.018, "chapa", off=0.012, thickness=0.014,
                seg=(8, 4))
for a in (-62, -22, 22, 62):
    for z in (0.13, 0.215):
        rivet(on_torso(a, z, 0.03), "X", 0.005).rotation_euler.z = d(a)

for s in (-1, 1):
    smooth(sphere("Hombrera", (0.0, s * 0.118, 0.236), 1.0, "metal", scale=(0.078, 0.056, 0.04),
                  segments=18, rings=10))
    smooth(sphere("Hombrera_Capa", (0.0, s * 0.128, 0.212), 1.0, "oxidoOsc", scale=(0.07, 0.05, 0.034),
                  segments=18, rings=10))
    rivet((0.0, s * 0.118, 0.277), "Z", 0.006)

with group("Reactor", on_torso(0, 0.165, 0.018)):
    cyl("Reactor_Marco", (0.004, 0, 0), 0.05, 0.02, "oxidoOsc", verts=6, bev=0.004, rot=(0, d(90), d(0)))
    cyl("Reactor_Ventana", (0.012, 0, 0), 0.036, 0.008, "rojoInt", verts=6, bev=0, rot=(0, d(90), 0))
    sphere("Reactor_Nucleo", (0.016, 0, 0), 0.015, "rojo", segments=12, rings=8)
    for z in (-0.012, 0.012):
        box("Reactor_Reja", (0.02, 0, z), (0.004, 0.06, 0.004), "filo", bev=0)
    ring_of(6, 0.043, (0.016, 0, 0), "X", lambda p, a: cyl("Reactor_Perno", p, 0.006, 0.008, "laton",
                                                          verts=6, bev=0.001))
