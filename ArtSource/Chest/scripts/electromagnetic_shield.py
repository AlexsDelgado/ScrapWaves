"""Electromagnetic Shield: peto con una bobina emisora en el pecho, cuatro celdas de carga y bornes en los hombros.

Representa las cargas de escudo (ShieldCharges +1 a +4) y su recarga (ShieldRechargeDelay).
"""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "chest_base.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("ElectromagneticShield")
d = math.radians
vest(shell="parche", back="metal")

with group("Emisor", on_torso(0, 0.165, 0.01)):
    cyl("Emisor_Base", (0.005, 0, 0), 0.058, 0.014, "oxidoOsc", verts=20, bev=0.003)
    cyl("Emisor_Disco", (0.014, 0, 0), 0.048, 0.008, "chapa", verts=20, bev=0.002)
    for i, r in enumerate((0.04, 0.03, 0.021)):
        tube("Bobina", (0.02 + i * 0.008, 0, 0), r, 0.008, 0.005, "laton", verts=20)
    sphere("Nucleo", (0.036, 0, 0), 0.016, "mostaza", segments=12, rings=8)
    ring_of(4, 0.05, (0.024, 0, 0), "X", lambda p, a: cyl("Celda", p, 0.008, 0.02, "mostaza", verts=10, bev=0.002))

for s in (-1, 1):
    base = shoulder(s, 0.018)
    cyl("Borne_Base", base, 0.016, 0.014, "oxidoOsc", axis="Z", verts=12, bev=0.002)
    cyl("Borne_Vara", (base[0], base[1], base[2] + 0.035), 0.005, 0.06, "filo", axis="Z", verts=8, bev=0)
    for k in range(3):
        cyl("Borne_Espira", (base[0], base[1], base[2] + 0.018 + k * 0.012), 0.01, 0.004, "laton",
            axis="Z", verts=10, bev=0)
    sphere("Borne_Esfera", (base[0], base[1], base[2] + 0.07), 0.013, "mostaza", segments=10, rings=6)
pipe("Cable", [shoulder(-1, 0.012), on_torso(-38, 0.235, 0.03), on_torso(-16, 0.18, 0.03)], 0.006, "goma")
