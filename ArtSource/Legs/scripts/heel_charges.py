"""Heel Charges: bota baja con un cargador de cartuchos en el talón y toberas de disparo hacia abajo.

Cada cartucho es un salto aéreo extra (AirJumps +1 a +4), por eso el cargador lleva cuatro.
Se renderiza con `upright=True`: la puntera apunta a +X y el cargador mira a -Y.
"""
import math

begin("HeelCharges")
d = math.radians

# Suela y banda de desgaste.
box("Suela", (0.06, 0, 0.035), (0.36, 0.15, 0.05), "goma", bev=0.012)
box("Suela_Banda", (0.06, 0, 0.062), (0.365, 0.155, 0.012), "oxidoOsc", bev=0.004)

# Empeine con correas cruzadas y puntera oxidada.
box("Empeine", (0.08, 0, 0.105), (0.2, 0.13, 0.08), "metal", bev=0.012)
cyl("Puntera", (0.175, 0, 0.095), 0.06, 0.136, "oxido", axis="Y", verts=16, bev=0.006)
box("Puntera_Tope", (0.175, 0, 0.095), (0.12, 0.14, 0.05), "oxido", bev=0.006)
for y in (-0.035, 0.035):
    rivet((0.2, y, 0.154), "Z", 0.008)
box("Empeine_Placa", (0.075, 0, 0.148), (0.1, 0.11, 0.012), "chapa", bev=0.004)
for x in (0.035, 0.115):
    rivet((x, -0.04, 0.155), "Z", 0.007)
box("Empeine_Correa", (0.075, 0, 0.11), (0.024, 0.138, 0.085), "goma", bev=0.004)
box("Empeine_Hebilla", (0.075, -0.071, 0.12), (0.032, 0.008, 0.032), "laton", bev=0.003)

# Caña baja con collar y detonador en el costado.
box("Cana", (-0.045, 0, 0.17), (0.16, 0.14, 0.22), "parche", bev=0.012)
box("Cana_Collar", (-0.045, 0, 0.29), (0.176, 0.158, 0.03), "chapa", bev=0.008)
box("Cana_Acolchado", (-0.045, 0, 0.305), (0.13, 0.11, 0.012), "negro", bev=0.003)
box("Cana_Correa", (-0.045, 0, 0.25), (0.168, 0.148, 0.024), "goma", bev=0.004)
box("Cana_Hebilla", (0.041, -0.04, 0.25), (0.008, 0.034, 0.034), "laton", bev=0.003)

box("Detonador", (-0.035, -0.076, 0.17), (0.08, 0.014, 0.06), "mostaza", bev=0.004)
box("Detonador_Visor", (-0.02, -0.084, 0.175), (0.03, 0.004, 0.022), "negro", bev=0)
cyl("Detonador_Boton", (-0.058, -0.086, 0.175), 0.011, 0.01, "rojo", axis="Y", verts=12, bev=0.002)
for x in (-0.07, 0.0):
    rivet((x, -0.084, 0.148), "Y", 0.005)
pipe("Detonador_Cable", [(-0.075, -0.084, 0.15), (-0.1, -0.11, 0.14), (-0.12, -0.12, 0.105)], 0.005, "goma")

# Cargador del talón: carcasa, cuatro cartuchos sujetos con una correa y toberas debajo.
box("Talon", (-0.13, 0, 0.075), (0.11, 0.17, 0.12), "oxidoOsc", bev=0.01)
box("Talon_Tapa", (-0.13, 0, 0.14), (0.116, 0.176, 0.014), "chapa", bev=0.004)
for x in (-0.175, -0.085):
    for y in (-0.07, 0.07):
        rivet((x, y, 0.148), "Z", 0.006)

for x in (-0.174, -0.144, -0.114, -0.084):
    cyl("Cartucho", (x, -0.101, 0.09), 0.015, 0.085, "rojo", axis="Z", verts=12, bev=0.002)
    cyl("Cartucho_Base", (x, -0.101, 0.041), 0.016, 0.018, "laton", axis="Z", verts=12, bev=0.002)
    cyl("Cartucho_Tapa", (x, -0.101, 0.134), 0.011, 0.006, "oxidoOsc", axis="Z", verts=12, bev=0)
box("Cartuchos_Correa", (-0.129, -0.116, 0.095), (0.125, 0.008, 0.018), "goma", bev=0.003)

for x in (-0.16, -0.105):
    for y in (-0.04, 0.04):
        cone("Tobera", (x, y, -0.004), 0.022, 0.015, 0.03, "metal", axis="Z", verts=12, bev=0.002)
        cyl("Tobera_Interior", (x, y, -0.019), 0.016, 0.003, "negro", axis="Z", verts=12, bev=0)
