"""Gauge expansion: guantelete que termina en un cañón con boca acampanada y collar de ajuste.

Representa el tamaño de área de los proyectiles (ProjectileAreaSize +0.2 a +1.2).
"""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "gauntlet_base.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("GaugeExpansion")
d = math.radians
gauntlet(hand=False)

cyl("Canon", (0.16, 0, 0), 0.042, 0.1, "metal", verts=16, bev=0.004)
cyl("Canon_Franja", (0.14, 0, 0), 0.044, 0.016, "mostaza", verts=16, bev=0.002)
cyl("Collar", (0.215, 0, 0), 0.058, 0.03, "oxidoOsc", verts=16, bev=0.004)
ring_of(6, 0.052, (0.232, 0, 0), "X", lambda p, a: rivet(p, "X", 0.006))
tube("Campana", (0.29, 0, 0), 0.052, 0.12, 0.012, "chapa", verts=18, r2=0.105)
cone("Campana_Interior", (0.29, 0, 0), 0.04, 0.093, 0.118, "negro", verts=18, bev=0)
tube("Campana_Aro", (0.355, 0, 0), 0.11, 0.022, 0.018, "oxido", verts=18)
for a in (60, 180, 300):
    r = math.radians(a)
    pipe("Tensor", [(0.215, math.cos(r) * 0.058, math.sin(r) * 0.058),
                    (0.345, math.cos(r) * 0.108, math.sin(r) * 0.108)], 0.006, "filo")

with group("Manometro", (-0.04, -0.066, 0.015)):
    cyl("Manometro_Aro", (0, 0, 0), 0.032, 0.014, "oxidoOsc", axis="Y", verts=14, bev=0.003)
    cyl("Manometro_Cara", (0, -0.008, 0), 0.025, 0.004, "chapa", axis="Y", verts=14, bev=0)
    box("Manometro_Aguja", (0.008, -0.011, 0.006), (0.022, 0.003, 0.004), "rojo", rot=(0, d(-30), 0), bev=0)
