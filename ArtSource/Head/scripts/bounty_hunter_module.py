"""Bounty-hunter module: casco con lente rastreadora sobre el ojo, estrella de sheriff, marcas de caza y antena.

Representa el daño contra élites (EliteDamageMultiplier +0.2 a +1.2).
"""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "head_base.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("BountyHunterModule")
d = math.radians
helmet(shell="oxidoOsc", chin="parche", stripe="rojo")

with group("Lente", on_shell(-24, 6, 0.008), (0, 0, d(-24))):
    cyl("Lente_Aro", (0, 0, 0), 0.03, 0.014, "metal", verts=18, bev=0.003)
    cyl("Lente_Iris", (0.007, 0, 0), 0.022, 0.004, "rojoInt", verts=18, bev=0)
    cyl("Lente_Pupila", (0.01, 0, 0), 0.008, 0.004, "rojo", verts=12, bev=0)
    for a in range(0, 360, 90):
        r = d(a)
        box("Lente_Marca", (0.011, math.cos(r) * 0.015, math.sin(r) * 0.015),
            (0.002, 0.004 if a % 180 else 0.008, 0.008 if a % 180 else 0.004), "chapa", bev=0)

star = []
for i in range(10):
    a = d(90 + i * 36)
    r = 0.034 if i % 2 == 0 else 0.015
    star.append((-0.05 + math.cos(a) * r, 0.165 + math.sin(a) * r))
plate("Placa_Sheriff", star, 0.012, "laton", y=-0.103, bev=0.002)
cyl("Placa_Centro", (-0.05, -0.11, 0.165), 0.009, 0.006, "oxidoOsc", axis="Y", verts=10, bev=0.001)

for i in range(4):
    box("Marca", (-0.035 + i * 0.013, -0.097, 0.098), (0.005, 0.006, 0.026), "chapa", bev=0)
box("Marca_Tachada", (-0.016, -0.1, 0.098), (0.055, 0.004, 0.005), "rojo", rot=(0, d(-25), 0), bev=0)

cyl("Rastreador_Mastil", (-0.07, 0.02, 0.28), 0.007, 0.06, "filo", axis="Z", verts=8, bev=0.001)
with group("Rastreador", (-0.07, 0.02, 0.315), (0, d(-35), 0)):
    tube("Rastreador_Plato", (0.012, 0, 0), 0.012, 0.03, 0.004, "chapa", verts=16, r2=0.042)
    cyl("Rastreador_Emisor", (0.035, 0, 0), 0.004, 0.04, "filo", verts=6, bev=0)
    sphere("Rastreador_Luz", (0.055, 0, 0), 0.008, "rojo", segments=8, rings=6)
