"""HUD derecho: dial de habilidad (Q), nombre y munición del arma, socket del arma actual y dos sockets
chicos para las siguientes armas de la rotación."""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "hud_parts.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("HudRight")
X0, X1, Z0, Z1 = 0.2, 1.1, 0.02, 0.32
WEAPON = (0.7, 0.058, 0.91, 0.281)
NEXT_W, NEXT_H = 0.1, 0.106

bolted_plate("Placa", X0, Z0, X1, Z1, "hierro", rivet_rows=())
bolted_plate("Placa_Tira", X0 + 0.07, Z1 - 0.034, WEAPON[0] - 0.03, Z1 - 0.004, "metal", y=-0.004, t=0.01,
             r=0.006, rivet_rows=(Z1 - 0.019,), step=0.06, inset=0.02)
bracket("Escuadra", X1, Z1, -1, -1, 0.06)
bracket("Escuadra", X1, Z0, -1, 1, 0.06)

recess("WeaponName", 0.3, 0.228, 0.66, 0.272, lip=0.007, lip_mat="hierroMed")
tube_bar("AmmoFill", 0.33, 0.62, 0.165, 0.046)
recess("AmmoLabel", 0.33, 0.074, 0.62, 0.108, lip=0.006, lip_mat="goma")

recess("WeaponSlot_0", *WEAPON, lip=0.012, lip_mat="goma")
recess("WeaponSlot_1", 0.955, 0.175, 0.955 + NEXT_W, 0.175 + NEXT_H, lip=0.009, lip_mat="goma")
recess("WeaponSlot_2", 0.955, 0.058, 0.955 + NEXT_W, 0.058 + NEXT_H, lip=0.009, lip_mat="goma")
for zc in (0.228, 0.111):
    for dz in (-0.008, 0.008):
        box("Flecha", (0.932, -0.012, zc + dz), (0.018, 0.006, 0.006), "mostaza",
            rot=(0, math.radians(35 if dz > 0 else -35), 0), bev=0)

dial("AbilityCooldown", 0.13, 0.17, 0.128, 0.088, y=-0.012, blocks=10)
