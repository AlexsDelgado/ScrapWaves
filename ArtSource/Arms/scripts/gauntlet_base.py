"""Guantelete base compartido por los pasivos de brazos. Requiere blender_model_kit ya cargado.

El antebrazo corre sobre X con la mano en +X, arriba es +Z y el costado visible mira a -Y.
El antebrazo ocupa x -0.23..0.08 y su cara superior está en z 0.06.
"""


def gauntlet(hand=True, arm_mat="metal", plate_mat="chapa"):
    cyl("Codo", (-0.2, 0, 0), 0.072, 0.06, "oxidoOsc", verts=16, bev=0.006)
    cyl("Codo_Interior", (-0.232, 0, 0), 0.055, 0.006, "negro", verts=16, bev=0)
    box("Antebrazo", (-0.05, 0, 0), (0.26, 0.12, 0.12), arm_mat, bev=0.015)
    box("Antebrazo_Placa", (-0.06, 0, 0.064), (0.2, 0.1, 0.012), plate_mat, bev=0.004)
    for x in (-0.15, 0.03):
        box("Antebrazo_Correa", (x, 0, 0), (0.026, 0.128, 0.128), "goma", bev=0.004)
        rivet((x, -0.066, 0.0), "Y", 0.007)
    cyl("Muneca", (0.1, 0, 0), 0.052, 0.04, "goma", verts=14, bev=0.004)
    if not hand:
        return
    box("Mano", (0.16, 0, 0), (0.08, 0.1, 0.09), plate_mat, bev=0.01)
    for y in (-0.036, -0.012, 0.012, 0.036):
        box("Dedo", (0.215, y, -0.005), (0.042, 0.022, 0.075), arm_mat, bev=0.005)
    box("Nudillos", (0.215, 0, 0.038), (0.048, 0.104, 0.02), "oxido", bev=0.004)
    box("Pulgar", (0.18, -0.058, -0.012), (0.05, 0.022, 0.032), arm_mat, bev=0.005)
