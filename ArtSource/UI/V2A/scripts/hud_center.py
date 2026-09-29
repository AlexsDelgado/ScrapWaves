"""HUD central, variante A (Player_Bars): placa de hierro con tiras remachadas y 6 sockets de pasivos de a pares,
separados por columnas de caño con bandas."""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "parts_v2a.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("HudCenter")
SLOT_W, SLOT_H = 0.184, 0.196
IN_GAP, PAIR_GAP, PAD_X, PAD_Z = 0.05, 0.09, 0.07, 0.07

xs, x = [], PAD_X
for pair in range(3):
    for k in range(2):
        xs.append(x)
        x += SLOT_W + (IN_GAP if k == 0 else 0)
    x += PAIR_GAP
W = x - PAIR_GAP + PAD_X
H = SLOT_H + 2 * PAD_Z

bolted_plate("Placa", 0, 0, W, H, "hierro", r=0.012, rivet_rows=())
riveted_strip("Placa_TiraSup", 0.03, W - 0.03, H - 0.019, h=0.026)
riveted_strip("Placa_TiraInf", 0.03, W - 0.03, 0.019, h=0.026)
for sx, x0 in ((1, -0.006), (-1, W + 0.006)):
    corner_bracket("Escuadra", x0, H + 0.006, sx, -1, 0.07)
    corner_bracket("Escuadra", x0, -0.006, sx, 1, 0.07)

# Columnas de caño entre pares: cilindro vertical con bandas y un tornillo arriba y abajo.
for pair in range(1, 3):
    xc = xs[pair * 2] - PAIR_GAP / 2
    cyl("Columna", (xc, -0.02, H / 2), 0.022, H - 0.07, "metal", axis="Z", verts=20, bev=0.004)
    for zc in (H * 0.2, H * 0.5, H * 0.8):
        cyl("Columna_Banda", (xc, -0.02, zc), 0.025, 0.012, "hierroMed", axis="Z", verts=20, bev=0.002)
    for zc in (0.03, H - 0.03):
        cyl("Columna_Tuerca", (xc, -0.02, zc), 0.02, 0.016, "hierroMed", axis="Z", verts=6, bev=0.002)

for i, x0 in enumerate(xs):
    socket(f"PassiveSlot_{i}", x0, PAD_Z, x0 + SLOT_W, PAD_Z + SLOT_H)
