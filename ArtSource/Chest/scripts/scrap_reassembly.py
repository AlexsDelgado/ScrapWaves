"""Scrap Reassembly: peto remendado con chapas soldadas y un brazo soldador en el hombro reparando el pecho.

Representa el robo de vida (Lifesteal): el daño se convierte en chatarra que reconstruye al personaje.
"""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "chest_base.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("ScrapReassembly")
d = math.radians
vest(shell="metal", back="oxidoOsc")

PATCHES = (((-58, -22), 0.18, 0.245, "oxido"), ((6, 40), 0.13, 0.215, "parche"),
           ((-30, 4), 0.115, 0.16, "mostaza"), ((34, 66), 0.17, 0.235, "chapa"))
for a_range, z0, z1, mat in PATCHES:
    torso_patch("Remiendo", a_range, z0, z1, mat, off=0.013, thickness=0.006, seg=(5, 2))
    torso_patch("Soldadura", (a_range[0] - 2, a_range[1] + 2), z0 - 0.006, z0, "laton", off=0.015,
                thickness=0.004, seg=(5, 1))
    for a in a_range:
        rivet(on_torso(a + (3 if a == a_range[0] else -3), z1 - 0.01, 0.022), "X", 0.005).rotation_euler.z = d(a)

base = shoulder(-1, 0.02)
cyl("Brazo_Base", base, 0.024, 0.02, "oxidoOsc", axis="Z", verts=12, bev=0.003)
elbow = (0.07, -0.12, 0.37)
tip = on_torso(-40, 0.225, 0.045)
pipe("Brazo_Superior", [base, (0.03, -0.11, 0.36), elbow], 0.011, "metal")
sphere("Brazo_Codo", elbow, 0.018, "laton", segments=10, rings=6)
pipe("Brazo_Inferior", [elbow, (0.11, -0.1, 0.3), tip], 0.009, "metal")
cone("Soplete", (tip[0] + 0.012, tip[1] + 0.006, tip[2] - 0.01), 0.012, 0.004, 0.03, "filo",
     axis="Z", verts=10, rot=(0, d(150), d(-30)), bev=0.001)
sphere("Soplete_Llama", (tip[0] + 0.022, tip[1] + 0.012, tip[2] - 0.03), 0.009, "mostaza", segments=8, rings=6)
for i, (dx, dz, s) in enumerate(((0.035, -0.02, 0.008), (0.045, -0.045, 0.006), (0.025, -0.055, 0.005))):
    box("Chispa", (tip[0] + dx, tip[1] - 0.01, tip[2] + dz), (s * 2.2, 0.003, s), "mostaza",
        rot=(0, d(-20 - i * 25), 0), bev=0)
