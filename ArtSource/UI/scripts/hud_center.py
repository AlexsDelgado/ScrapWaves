"""HUD central: placa con 6 sockets de pasivos agrupados de a pares (cabeza+pecho, brazos, piernas)."""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "hud_parts.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("HudCenter")
SLOT_W, SLOT_H = 0.184, 0.196
IN_GAP, PAIR_GAP, PAD_X, PAD_Z = 0.026, 0.07, 0.052, 0.05

xs, x = [], PAD_X
for pair in range(3):
    for k in range(2):
        xs.append(x)
        x += SLOT_W + (IN_GAP if k == 0 else 0)
    x += PAIR_GAP
W = x - PAIR_GAP + PAD_X
H = SLOT_H + 2 * PAD_Z

bolted_plate("Placa", 0, 0, W, H, "hierro", rivet_rows=())
for zc in (H - 0.018, 0.018):
    for x0 in xs:
        for dx in (0.03, SLOT_W - 0.03):
            rivet((x0 + dx, -0.004, zc), "Y", 0.0065)
for s, x0 in ((1, 0.0), (-1, W)):
    bracket("Escuadra", x0, H, s, -1, 0.06)
    bracket("Escuadra", x0, 0.0, s, 1, 0.06)

for pair in range(1, 3):
    xc = xs[pair * 2] - PAIR_GAP / 2
    box("Divisor", (xc, -0.008, H / 2), (0.022, 0.014, H - 0.03), "metal", bev=0.004)
    for zc in (H * 0.22, H * 0.78):
        rivet((xc, -0.016, zc), "Y", 0.006)
    for zc in (H * 0.4, H * 0.6):
        box("Divisor_Franja", (xc, -0.016, zc), (0.022, 0.004, 0.012), "mostaza", bev=0)

for i, x0 in enumerate(xs):
    recess(f"PassiveSlot_{i}", x0, PAD_Z, x0 + SLOT_W, PAD_Z + SLOT_H, lip=0.01, lip_mat="goma")
