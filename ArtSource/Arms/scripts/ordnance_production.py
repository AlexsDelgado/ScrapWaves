"""Ordnance production: guantelete con una mini fábrica de munición montada sobre el antebrazo.

Representa el multiplicador de munición (AmmoMultiplier +0.2 a +1.2): tolva, chimenea, engranaje y proyectiles listos.
"""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "gauntlet_base.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("OrdnanceProduction")
d = math.radians
gauntlet()

box("Fabrica", (-0.07, 0, 0.115), (0.15, 0.1, 0.09), "oxido", bev=0.008)
box("Fabrica_Techo", (-0.07, 0, 0.164), (0.158, 0.108, 0.012), "chapa", bev=0.004)
cone("Tolva", (-0.1, 0.0, 0.2), 0.018, 0.04, 0.05, "metal", axis="Z", verts=12, bev=0.003)
cyl("Chimenea", (-0.03, 0.025, 0.21), 0.014, 0.08, "parche", axis="Z", verts=10, bev=0.002)
cyl("Chimenea_Tapa", (-0.03, 0.025, 0.252), 0.019, 0.01, "oxidoOsc", axis="Z", verts=10, bev=0.002)
box("Panel", (-0.09, -0.052, 0.115), (0.06, 0.006, 0.045), "negro", bev=0.002)
for x in (-0.105, -0.09, -0.075):
    box("Panel_Luz", (x, -0.056, 0.125), (0.008, 0.003, 0.008), "mostaza", bev=0)
cyl("Panel_Boton", (-0.09, -0.057, 0.103), 0.007, 0.006, "rojo", axis="Y", verts=10, bev=0.001)
for x in (-0.14, 0.0):
    rivet((x, -0.052, 0.145), "Y", 0.006)

with group("Engranaje", (-0.03, -0.056, 0.11)):
    cyl("Engranaje_Disco", (0, 0, 0), 0.026, 0.01, "laton", axis="Y", verts=14, bev=0.002)
    ring_of(8, 0.029, (0, 0, 0), "Y", lambda p, a: box("Diente", p, (0.009, 0.01, 0.009), "laton",
                                                       rot=(0, -a, 0), bev=0.001))

box("Rampa", (0.04, 0, 0.085), (0.08, 0.07, 0.01), "metal", rot=(0, d(12), 0), bev=0.002)


def shell(name, loc):
    with group(name, loc):
        cyl(name + "_Cuerpo", (0, 0, 0), 0.015, 0.045, "parche", verts=12, bev=0.002)
        cyl(name + "_Franja", (0.005, 0, 0), 0.016, 0.01, "mostaza", verts=12, bev=0.001)
        cone(name + "_Ojiva", (0.034, 0, 0), 0.015, 0.004, 0.024, "rojo", verts=12, bev=0)
        cyl(name + "_Base", (-0.024, 0, 0), 0.016, 0.006, "laton", verts=12, bev=0.001)


shell("Proyectil_A", (0.03, -0.018, 0.104))
shell("Proyectil_B", (0.03, 0.018, 0.104))
shell("Proyectil_C", (0.035, 0.0, 0.132))
