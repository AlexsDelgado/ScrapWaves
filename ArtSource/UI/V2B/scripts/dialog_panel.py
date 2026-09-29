"""Panel de diálogo de jefes, variante B: marco de acero claro, socket para el retrato (el sprite trae su propio
marco de ícono), placa de nombre con filete mostaza, área de texto hundida, rejilla de parlante y LED de señal.

Ventanas que lee Unity (DialogueBoxUI): Portrait, Speaker, Text, Signal.
"""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "parts_v2b.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("DialogPanel")
W, H = 1.9, 0.44
PORTRAIT = (0.05, 0.05, 0.39, 0.39)
TEXT_X0, TEXT_X1 = 0.46, 1.72

frame_panel("Marco", 0.0, 0.0, W, H, border=0.034, chamfer=0.055, corners=(1, 1, 1, 1))

# Socket del retrato: canal oscuro con bisel de acero y abrazaderas de cobre a los lados.
frame_panel("MarcoRetrato", PORTRAIT[0] - 0.012, PORTRAIT[1] - 0.012, PORTRAIT[2] + 0.012, PORTRAIT[3] + 0.012,
            border=0.012, chamfer=0.012, bolts=False, y=-0.006)
recess("Portrait", *PORTRAIT, y=-0.002, lip=0.005, lip_mat="goma", fill="panel", depth=0.008)
for zc in (0.13, 0.31):
    box("Abrazadera", (PORTRAIT[2] + 0.026, -0.014, zc), (0.016, 0.012, 0.05), "oxido", bev=0.003)
    domed_bolt((PORTRAIT[2] + 0.026, -0.022, zc), 0.006)

# Placa de nombre arriba del texto, con filete mostaza abajo y franja de peligro al final.
recess("Speaker", TEXT_X0, 0.33, 0.98, 0.382, lip=0.006, lip_mat="goma", fill="panel")
box("Speaker_Filete", ((TEXT_X0 + 0.98) / 2, -0.008, 0.322), (0.98 - TEXT_X0, 0.004, 0.004), "mostaza", bev=0)
hazard("Peligro", 1.0, 0.338, 1.14, 0.374, stripe=0.012)

recess("Text", TEXT_X0, 0.066, TEXT_X1, 0.305, lip=0.008, lip_mat="goma", fill="negro", depth=0.012)

# Rejilla de parlante y LED de señal a la derecha: el LED lo enciende Unity mientras habla el jefe.
GX0, GX1 = 1.76, 1.85
plate("Rejilla", rrect(GX0, 0.07, GX1, 0.3, 0.012), 0.012, "metal", y=-0.012, bev=0.003)
for i in range(7):
    z = 0.095 + i * 0.03
    box("Rejilla_Ranura", ((GX0 + GX1) / 2, -0.02, z), (GX1 - GX0 - 0.03, 0.004, 0.009), "goma", bev=0)
cyl("Signal_Base", (1.805, -0.012, 0.36), 0.03, 0.014, "chapa", axis="Y", verts=24, bev=0.004)
cyl("Signal_Fondo", (1.805, -0.022, 0.36), 0.02, 0.006, "negro", axis="Y", verts=24, bev=0)
mark("Signal", 1.785, 0.34, 1.825, 0.38)
