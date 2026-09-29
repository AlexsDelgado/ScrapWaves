"""HUD izquierdo, variante B (estilo armas): manómetro de heat, HP y XP como caños en un marco de acero claro."""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "parts_v2b.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("HudLeft")
GC, GR, GIN = (0.2, 0.2), 0.19, 0.118
X0, X1, Z0, Z1 = 0.3, 1.04, 0.075, 0.325

frame_panel("Marco", X0, Z0, X1, Z1, border=0.026, chamfer=0.045, corners=(0, 1, 1, 0))
hazard("Peligro", X1 - 0.052, Z0 + 0.06, X1 - 0.03, Z1 - 0.06, stripe=0.014)
# Soporte que une el manómetro con el marco: planchuela con dos bulones.
plate("Soporte", rrect(0.33, 0.16, 0.44, 0.24, 0.01), 0.016, "metal", y=-0.026, bev=0.004)
domed_bolt((0.415, -0.034, 0.2), 0.009)

barrel_bar("HpFill", 0.5, 0.9, 0.252, 0.056)
barrel_bar("XpFill", 0.5, 0.9, 0.158, 0.046)
gauge("OverheatFill", *GC, GR, GIN, y=-0.014)
