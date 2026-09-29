"""HUD derecho, variante A (Player_Bars): dial de bloques para la Q, nombre y munición en tubo de vidrio, socket
del arma actual y dos sockets chicos para las siguientes."""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "parts_v2a.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("HudRight")
X0, X1, Z0, Z1 = 0.19, 1.1, 0.02, 0.32
WEAPON = (0.705, 0.07, 0.905, 0.27)
NEXT_W, NEXT_H = 0.1, 0.104

bolted_plate("Placa", X0, Z0, X1, Z1, "hierro", r=0.012, rivet_rows=())
riveted_strip("Placa_TiraSup", X0 + 0.07, X1 - 0.004, Z1 - 0.018, h=0.03)
riveted_strip("Placa_TiraInf", X0 + 0.07, X1 - 0.004, Z0 + 0.018, h=0.03)
corner_bracket("Escuadra", X1 + 0.006, Z1 + 0.006, -1, -1, 0.07)
corner_bracket("Escuadra", X1 + 0.006, Z0 - 0.006, -1, 1, 0.07)

recess("WeaponName", 0.3, 0.232, 0.66, 0.27, lip=0.007, lip_mat="hierroMed")
glass_tube("AmmoFill", 0.37, 0.59, 0.168, 0.046, right="bolts")
recess("AmmoLabel", 0.33, 0.07, 0.62, 0.104, lip=0.006, lip_mat="goma")

socket("WeaponSlot_0", *WEAPON, bezel=0.012)
recess("WeaponSlot_1", 0.955, 0.176, 0.955 + NEXT_W, 0.176 + NEXT_H, lip=0.01, lip_mat="hierroMed")
recess("WeaponSlot_2", 0.955, 0.06, 0.955 + NEXT_W, 0.06 + NEXT_H, lip=0.01, lip_mat="hierroMed")
for zc in (0.228, 0.112):
    for dz in (-0.008, 0.008):
        box("Flecha", (0.932, -0.012, zc + dz), (0.018, 0.006, 0.006), "led",
            rot=(0, math.radians(35 if dz > 0 else -35), 0), bev=0)

chunky_dial("AbilityCooldown", 0.14, 0.17, 0.152, 0.082, y=-0.012, blocks=12, big_every=3)
leds("Luz", 0.14, 0.17, 0.152 - 0.012, (297, 309, 321), y=-0.09)
