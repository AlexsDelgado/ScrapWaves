"""Piston-assisted Jumping: bota industrial con un pistón hidráulico vertical junto al talón.

El pistón baja por debajo de la suela con su resorte comprimido (JumpHeight x1.3 a x2.3).
Se renderiza con `upright=True`: la puntera apunta a +X y el pistón queda en el costado -Y, hacia cámara.
"""
import math

begin("PistonAssistedJumping")
d = math.radians

# Suela gruesa con banda de desgaste y tacos en la puntera.
box("Suela", (0.055, 0, 0.035), (0.37, 0.15, 0.05), "goma", bev=0.012)
box("Suela_Banda", (0.055, 0, 0.062), (0.375, 0.155, 0.012), "oxidoOsc", bev=0.004)
for x in (0.14, 0.19):
    box("Suela_Taco", (x, -0.077, 0.03), (0.025, 0.008, 0.03), "negro", bev=0.002)

# Empeine y puntera reforzada.
box("Empeine", (0.07, 0, 0.11), (0.2, 0.13, 0.09), "metal", bev=0.012)
cyl("Puntera", (0.17, 0, 0.1), 0.065, 0.136, "chapa", axis="Y", verts=16, bev=0.006)
box("Puntera_Tope", (0.17, 0, 0.1), (0.13, 0.14, 0.05), "chapa", bev=0.006)
box("Empeine_Placa", (0.075, 0, 0.158), (0.13, 0.11, 0.012), "parche", bev=0.004)
for x in (0.025, 0.125):
    for y in (-0.04, 0.04):
        rivet((x, y, 0.165), "Z", 0.008)
box("Empeine_Correa", (0.04, 0, 0.11), (0.03, 0.138, 0.095), "goma", bev=0.004)
box("Empeine_Hebilla", (0.04, -0.071, 0.12), (0.034, 0.008, 0.034), "laton", bev=0.003)

# Caña con correas, collar superior y placa de peligro al frente.
box("Cana", (-0.05, 0, 0.2), (0.15, 0.14, 0.28), "oxido", bev=0.012)
box("Cana_Collar", (-0.05, 0, 0.345), (0.168, 0.158, 0.035), "chapa", bev=0.008)
box("Cana_Acolchado", (-0.05, 0, 0.36), (0.12, 0.11, 0.012), "negro", bev=0.003)
for z in (0.175, 0.3):
    box("Cana_Correa", (-0.05, 0, z), (0.158, 0.148, 0.026), "goma", bev=0.004)
    box("Cana_Hebilla", (0.03, -0.045, z), (0.008, 0.036, 0.036), "laton", bev=0.003)
box("Cana_Placa", (0.028, 0.012, 0.24), (0.008, 0.09, 0.08), "mostaza", bev=0.003)
tri = [(-0.03, -0.028), (0.03, -0.028), (0.0, 0.026)]
for i, (u, v) in enumerate(tri):
    tri[i] = (u + 0.012, v + 0.24)
with group("Peligro", (0.033, 0, 0), (0, 0, d(90))):
    plate("Cana_Peligro", tri, 0.003, "negro", bev=0)
for y in (-0.024, 0.048):
    for z in (0.21, 0.27):
        rivet((0.033, y, z), "X", 0.006)

# Pistón del talón: soportes, camisa, vástago cromado, resorte y pie de impacto bajo la suela.
PX, PY = -0.075, -0.115
R = 0.034
with group("Piston", (PX, PY, 0)):
    for z in (0.1, 0.24):
        box("Piston_Soporte", (0, 0.03, z), (0.04, 0.035, 0.03), "oxidoOsc", bev=0.004)
    cyl("Piston_Camisa", (0, 0, 0.2), R, 0.14, "metal", axis="Z", verts=16, bev=0.004)
    cyl("Piston_Tapa", (0, 0, 0.28), R + 0.006, 0.02, "oxidoOsc", axis="Z", verts=16, bev=0.004)
    cyl("Piston_Tuerca", (0, 0, 0.296), 0.015, 0.014, "laton", axis="Z", verts=6, bev=0.003)
    for z in (0.17, 0.245):
        cyl("Piston_Franja", (0, 0, z), R + 0.002, 0.013, "mostaza", axis="Z", verts=16, bev=0.002)
    cyl("Piston_Collar", (0, 0, 0.125), R + 0.007, 0.024, "chapa", axis="Z", verts=16, bev=0.004)
    cyl("Piston_Vastago", (0, 0, 0.055), 0.015, 0.13, "filo", axis="Z", verts=12, bev=0.002)

    turns, steps, z0, z1 = 5, 10, -0.005, 0.11
    coil = []
    for i in range(turns * steps + 1):
        a = 2 * math.pi * i / steps
        coil.append((math.cos(a) * 0.029, math.sin(a) * 0.029, z0 + (z1 - z0) * i / (turns * steps)))
    pipe("Piston_Resorte", coil, 0.0065, "mostaza", resolution=4)

    cyl("Piston_PiePlaca", (0, 0, -0.012), 0.04, 0.014, "oxido", axis="Z", verts=16, bev=0.003)
    cyl("Piston_Pie", (0, 0, -0.032), 0.046, 0.028, "goma", axis="Z", verts=16, bev=0.006)

    # Manómetro en la cara de la camisa que mira a cámara.
    face = (R * 0.83, -R * 0.55)
    with group("Manometro", (face[0], face[1], 0.205), (0, 0, d(56))):
        cyl("Manometro_Aro", (0, 0, 0), 0.02, 0.012, "oxidoOsc", axis="Y", verts=14, bev=0.003)
        cyl("Manometro_Cara", (0, -0.007, 0), 0.015, 0.004, "chapa", axis="Y", verts=14, bev=0)
        box("Manometro_Aguja", (0.005, -0.0095, 0.004), (0.014, 0.003, 0.003), "rojo", rot=(0, d(-35), 0), bev=0)

    for z in (0.145, 0.265):
        rivet((face[0], face[1], z), "Y", 0.005).rotation_euler.z = d(56)
    cyl("Manguera_RacorPiston", (R + 0.004, 0.008, 0.25), 0.012, 0.018, "laton", axis="X", verts=8, bev=0.002)

# Manguera hidráulica del pistón al empeine.
pipe("Manguera", [(PX + R + 0.004, PY + 0.008, 0.25), (0.0, -0.1, 0.21), (0.06, -0.095, 0.16),
                  (0.1, -0.075, 0.12)], 0.009, "goma")
cyl("Manguera_Racor", (0.1, -0.07, 0.12), 0.016, 0.02, "laton", axis="Y", verts=8, bev=0.002)

# Luz de presión al frente del collar.
cyl("Luz_Base", (0.036, -0.045, 0.345), 0.014, 0.01, "goma", axis="X", verts=10, bev=0.002)
sphere("Luz", (0.042, -0.045, 0.345), 0.011, "rojo", segments=10, rings=6)
