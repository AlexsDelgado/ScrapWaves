"""HUD izquierdo, variante A (Player_Bars): dial de bloques con el heat, HP arriba y XP abajo en tubos de vidrio."""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "parts_v2a.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("HudLeft")
DIAL_C, DIAL_R, DIAL_IN = (0.2, 0.2), 0.195, 0.118
X0, X1, Z0, Z1 = 0.26, 1.04, 0.055, 0.345

bolted_plate("Placa", X0, Z0, X1, Z1, "hierro", r=0.012, rivet_rows=())
seams("Placa_Junta", X0 + 0.1, X1, Z0 + 0.04, Z1 - 0.04, count=4)
riveted_strip("Placa_TiraSup", X0 + 0.04, X1 - 0.004, Z1 - 0.02)
riveted_strip("Placa_TiraInf", X0 + 0.04, X1 - 0.004, Z0 + 0.02)
corner_bracket("Escuadra", X1 + 0.006, Z1 + 0.006, -1, -1)
corner_bracket("Escuadra", X1 + 0.006, Z0 - 0.006, -1, 1)

glass_tube("HpFill", 0.5, 0.93, 0.256, 0.054, right="bolts")
glass_tube("XpFill", 0.5, 0.93, 0.144, 0.054, right="plug")

# Caños del dial a las tapas izquierdas, como en la referencia.
cable("Cano", [(0.36, 0.29), (0.4, 0.315), (0.43, 0.27), (0.45, 0.262)])
cable("Cano", [(0.385, 0.215), (0.415, 0.215), (0.43, 0.2), (0.45, 0.19)], radius=0.007)
cable("Cano", [(0.39, 0.15), (0.45, 0.15)], radius=0.0062)
cable("Cano", [(0.385, 0.135), (0.45, 0.135)], radius=0.0062)

chunky_dial("OverheatFill", *DIAL_C, DIAL_R, DIAL_IN, y=-0.012)
leds("Luz", *DIAL_C, DIAL_R - 0.012, (207, 219, 231, 243), y=-0.09)
