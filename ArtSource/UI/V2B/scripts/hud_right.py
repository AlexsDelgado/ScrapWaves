"""HUD derecho, variante B (estilo armas): manómetro de la Q, nombre y munición en un marco de acero claro,
marco tipo ícono para el arma actual y dos marcos chicos para las siguientes."""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "parts_v2b.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("HudRight")
X0, X1, Z0, Z1 = 0.22, 1.1, 0.03, 0.31
WEAPON = (0.705, 0.07, 0.905, 0.27)
NEXT_W, NEXT_H = 0.1, 0.104

frame_panel("Marco", X0, Z0, X1, Z1, border=0.024, chamfer=0.04, corners=(0, 1, 1, 0))

recess("WeaponName", 0.31, 0.228, 0.66, 0.268, lip=0.006, lip_mat="goma", fill="panel")
box("WeaponName_Filete", (0.485, -0.008, 0.222), (0.35, 0.004, 0.004), "mostaza", bev=0)
barrel_bar("AmmoFill", 0.36, 0.59, 0.168, 0.044)
recess("AmmoLabel", 0.33, 0.07, 0.62, 0.104, lip=0.006, lip_mat="goma", fill="panel")
hazard("Peligro", 0.33, 0.052, 0.62, 0.062, stripe=0.01)

frame_panel("MarcoArma", WEAPON[0] - 0.02, WEAPON[1] - 0.02, WEAPON[2] + 0.02, WEAPON[3] + 0.02, border=0.02,
            chamfer=0.02, y=-0.006)
recess("WeaponSlot_0", *WEAPON, y=-0.002, lip=0.006, lip_mat="goma", fill="panel", depth=0.008)
for zb, name in ((0.176, "WeaponSlot_1"), (0.06, "WeaponSlot_2")):
    frame_panel(f"Marco{name}", 0.955 - 0.014, zb - 0.014, 0.955 + NEXT_W + 0.014, zb + NEXT_H + 0.014,
                border=0.014, chamfer=0.012, bolts=False, y=-0.006)
    recess(name, 0.955, zb, 0.955 + NEXT_W, zb + NEXT_H, y=-0.002, lip=0.005, lip_mat="goma", fill="panel",
           depth=0.008)
for zc in (0.228, 0.112):
    for dz in (-0.008, 0.008):
        box("Flecha", (0.932, -0.014, zc + dz), (0.018, 0.006, 0.006), "mostaza",
            rot=(0, math.radians(35 if dz > 0 else -35), 0), bev=0)

gauge("AbilityCooldown", 0.14, 0.17, 0.142, 0.086, y=-0.014, ticks=20, hot_from=1.1, bolts=5)
