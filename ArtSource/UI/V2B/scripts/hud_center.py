"""HUD central, variante B (estilo armas): seis marcos tipo ícono de a pares, montados sobre dos caños de acero
con bandas de cobre, y franjas de peligro entre pares. Sin placa de fondo: la silueta es el riel."""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "parts_v2b.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("HudCenter")
SLOT_W, SLOT_H = 0.184, 0.196
BORDER = 0.024
IN_GAP, PAIR_GAP, PAD_X, PAD_Z = 0.07, 0.11, 0.05, 0.04

xs, x = [], PAD_X
for pair in range(3):
    for k in range(2):
        xs.append(x)
        x += SLOT_W + (IN_GAP if k == 0 else 0)
    x += PAIR_GAP
W = x - PAIR_GAP + PAD_X
H = SLOT_H + 2 * PAD_Z

# Riel: dos caños horizontales por detrás de los marcos, con tapas y bandas de cobre en los extremos.
for zc in (H * 0.3, H * 0.7):
    cyl("Riel", (W / 2, 0.03, zc), 0.02, W - 0.02, "metal", axis="X", verts=20, bev=0.003)
    for xe in (0.02, W - 0.02):
        cyl("Riel_Tapa", (xe, 0.03, zc), 0.024, 0.02, "chapa", axis="X", verts=20, bev=0.004)
        copper_band("Riel_Banda", xe + (0.02 if xe < W / 2 else -0.02), zc, 0.021, 0.008, 0.03)

# Dentro del par, una abrazadera de cobre sobre cada caño.
for pair in range(3):
    xc = xs[pair * 2] + SLOT_W + IN_GAP / 2
    for zc in (H * 0.3, H * 0.7):
        cyl("Abrazadera", (xc, 0.03, zc), 0.024, 0.014, "oxido", axis="X", verts=20, bev=0.002)
        domed_bolt((xc, 0.004, zc), 0.007)

for pair in range(1, 3):
    xc = xs[pair * 2] - PAIR_GAP / 2
    hazard("Peligro", xc - 0.022, PAD_Z + 0.02, xc + 0.022, H - PAD_Z - 0.02, stripe=0.014, y=0.0)
    for zc in (PAD_Z + 0.006, H - PAD_Z - 0.006):
        domed_bolt((xc, -0.012, zc), 0.01)

for i, x0 in enumerate(xs):
    frame_panel(f"Marco_{i}", x0 - BORDER, PAD_Z - BORDER, x0 + SLOT_W + BORDER, PAD_Z + SLOT_H + BORDER,
                border=BORDER, chamfer=0.022, y=-0.01)
    recess(f"PassiveSlot_{i}", x0, PAD_Z, x0 + SLOT_W, PAD_Z + SLOT_H, y=-0.004, lip=0.006, lip_mat="goma",
           fill="panel", depth=0.008)
