"""HUD izquierdo: dial de heat/overheat, barra de HP (arriba) y barra de XP (abajo)."""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "hud_parts.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("HudLeft")
DIAL_C, DIAL_R, DIAL_IN = (0.2, 0.2), 0.19, 0.136
X0, X1, Z0, Z1 = 0.3, 1.02, 0.06, 0.34

bolted_plate("Placa", X0, Z0, X1, Z1, "hierro", rivet_rows=())
for zc in (Z1 - 0.022, Z0 + 0.022):
    bolted_plate("Placa_Tira", X0 + 0.04, zc - 0.018, X1 - 0.01, zc + 0.018, "metal", y=-0.004, t=0.01, r=0.006,
                 rivet_rows=(zc,), step=0.055, inset=0.03)
bracket("Escuadra", X1, Z1, -1, -1, 0.07)
bracket("Escuadra", X1, Z0, -1, 1, 0.07)

tube_bar("HpFill", 0.47, 0.94, 0.245, 0.064)
tube_bar("XpFill", 0.47, 0.94, 0.152, 0.048)
dial("OverheatFill", *DIAL_C, DIAL_R, DIAL_IN, y=-0.012)
for deg in (180, 210, 240):
    a = math.radians(deg)
    rr = (DIAL_R + DIAL_IN) / 2
    p = (DIAL_C[0] + math.cos(a) * rr, DIAL_C[1] + math.sin(a) * rr)
    cyl("Luz_Base", (p[0], -0.066, p[1]), 0.012, 0.01, "goma", axis="Y", verts=12, bev=0.002)
    sphere("Luz", (p[0], -0.072, p[1]), 0.0085, "mostaza", segments=10, rings=6)
