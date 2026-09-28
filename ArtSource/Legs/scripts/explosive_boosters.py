"""Explosive Boosters: bota con dos cohetes en el costado, toberas hacia atrás para impulsar el dash.

El panel de cuatro luces al frente de la caña representa las cargas de dash extra (DashCharges +1 a +4).
Se renderiza con `upright=True`: la puntera y las ojivas apuntan a +X y los cohetes quedan en -Y.
"""
import math

begin("ExplosiveBoosters")
d = math.radians

# Suela y banda de desgaste.
box("Suela", (0.055, 0, 0.035), (0.37, 0.15, 0.05), "goma", bev=0.012)
box("Suela_Banda", (0.055, 0, 0.062), (0.375, 0.155, 0.012), "oxidoOsc", bev=0.004)

# Empeine y puntera reforzada.
box("Empeine", (0.07, 0, 0.11), (0.2, 0.13, 0.09), "oxido", bev=0.012)
cyl("Puntera", (0.17, 0, 0.1), 0.065, 0.136, "chapa", axis="Y", verts=16, bev=0.006)
box("Puntera_Tope", (0.17, 0, 0.1), (0.13, 0.14, 0.05), "chapa", bev=0.006)
box("Empeine_Placa", (0.075, 0, 0.158), (0.13, 0.11, 0.012), "parche", bev=0.004)
for x in (0.025, 0.125):
    for y in (-0.04, 0.04):
        rivet((x, y, 0.165), "Z", 0.008)

# Caña con correas y panel de cargas al frente.
box("Cana", (-0.05, 0, 0.2), (0.15, 0.14, 0.28), "metal", bev=0.012)
box("Cana_Collar", (-0.05, 0, 0.345), (0.168, 0.158, 0.035), "chapa", bev=0.008)
box("Cana_Acolchado", (-0.05, 0, 0.36), (0.12, 0.11, 0.012), "negro", bev=0.003)
for z in (0.12, 0.3):
    box("Cana_Correa", (-0.05, 0, z), (0.158, 0.148, 0.026), "goma", bev=0.004)
    box("Cana_Hebilla", (0.03, 0.04, z), (0.008, 0.036, 0.036), "laton", bev=0.003)
box("Cargas_Panel", (0.029, -0.02, 0.21), (0.01, 0.05, 0.13), "goma", bev=0.003)
for z in (0.165, 0.195, 0.225, 0.255):
    cyl("Carga_Luz", (0.035, -0.02, z), 0.01, 0.006, "rojo", axis="X", verts=10, bev=0.001)

# Cohetes gemelos, levemente inclinados con las ojivas hacia arriba.
with group("Cohetes", (-0.04, -0.125, 0.2), (0, d(-12), 0)):
    for zz in (-0.04, 0.04):
        cyl("Cohete_Cuerpo", (0, 0, zz), 0.034, 0.2, "metal", verts=16, bev=0.004)
        cyl("Cohete_Franja", (0.055, 0, zz), 0.036, 0.018, "mostaza", verts=16, bev=0.002)
        cone("Cohete_Ojiva", (0.13, 0, zz), 0.034, 0.008, 0.06, "rojo", verts=16, bev=0.003)
        cone("Cohete_Tobera", (-0.125, 0, zz), 0.043, 0.028, 0.05, "oxidoOsc", verts=16, bev=0.003)
        cyl("Cohete_Fuego", (-0.148, 0, zz), 0.033, 0.004, "rojoInt", verts=16, bev=0)
    box("Cohetes_Soporte", (0, 0.045, 0), (0.07, 0.04, 0.07), "oxidoOsc", bev=0.005)
    for x in (-0.055, 0.02):
        box("Cohetes_Abrazadera", (x, 0, 0), (0.024, 0.08, 0.16), "goma", bev=0.004)
        for zz in (-0.06, 0.06):
            rivet((x, -0.041, zz), "Y", 0.006)

# Línea de combustible de los cohetes al talón.
pipe("Combustible", [(-0.17, -0.11, 0.17), (-0.15, -0.095, 0.1), (-0.11, -0.076, 0.085)], 0.009, "goma")
cyl("Combustible_Valvula", (-0.11, -0.072, 0.085), 0.015, 0.02, "laton", axis="Y", verts=8, bev=0.002)
