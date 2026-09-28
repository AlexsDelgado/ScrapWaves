"""Bionic Boots: bota articulada con servo en el tobillo, actuador diagonal y suela partida.

Todo el diseño apunta a velocidad de carrera (MovementSpeed x1.1 a x1.6): chevrones y aleta de talón.
Se renderiza con `upright=True`: la puntera apunta a +X y el servo mira a -Y.
"""
import math

begin("BionicBoots")
d = math.radians

# Suela partida en talón y punta, unidas por una bisagra.
box("Suela_Talon", (-0.045, 0, 0.035), (0.17, 0.15, 0.05), "goma", bev=0.012)
box("Suela_Punta", (0.155, 0, 0.035), (0.17, 0.15, 0.05), "goma", bev=0.012)
for x in (-0.045, 0.155):
    box("Suela_Banda", (x, 0, 0.062), (0.172, 0.155, 0.012), "oxidoOsc", bev=0.004)
cyl("Suela_Bisagra", (0.055, 0, 0.045), 0.024, 0.158, "laton", axis="Y", verts=12, bev=0.003)

# Empeine con placas escalonadas y puntera.
box("Empeine", (0.09, 0, 0.11), (0.16, 0.13, 0.09), "metal", bev=0.012)
cyl("Puntera", (0.185, 0, 0.1), 0.058, 0.136, "chapa", axis="Y", verts=16, bev=0.006)
box("Puntera_Tope", (0.185, 0, 0.095), (0.1, 0.14, 0.05), "chapa", bev=0.006)
for i in range(3):
    box("Empeine_Placa", (0.035 + i * 0.045, 0, 0.162 - i * 0.01), (0.05, 0.12, 0.014), "chapa",
        rot=(0, d(12), 0), bev=0.003)

# Caña alta con espinillera, placas de pantorrilla y chevrones de velocidad.
box("Cana", (-0.05, 0, 0.22), (0.14, 0.13, 0.32), "metal", bev=0.012)
box("Cana_Espinillera", (0.028, 0, 0.26), (0.02, 0.12, 0.2), "chapa", bev=0.006)
for z in (0.14, 0.22, 0.3):
    box("Cana_Placa", (-0.125, 0, z), (0.02, 0.12, 0.07), "parche", bev=0.004)
box("Cana_Collar", (-0.05, 0, 0.39), (0.155, 0.145, 0.03), "oxidoOsc", bev=0.006)
box("Cana_Acolchado", (-0.05, 0, 0.405), (0.11, 0.1, 0.012), "negro", bev=0.003)

chevron = [(0.0, 0.035), (0.018, 0.035), (0.042, 0.0), (0.018, -0.035), (0.0, -0.035), (0.024, 0.0)]
for x0 in (-0.075, -0.037):
    plate("Chevron", [(x0 + u, 0.24 + v) for u, v in chevron], 0.004, "mostaza", y=-0.066, bev=0)

# Servo del tobillo con anillo de tornillos y sensor.
with group("Servo", (-0.035, -0.065, 0.12)):
    cyl("Servo_Carcasa", (0, -0.01, 0), 0.052, 0.02, "oxidoOsc", axis="Y", verts=16, bev=0.004)
    cyl("Servo_Anillo", (0, -0.022, 0), 0.038, 0.01, "chapa", axis="Y", verts=16, bev=0.003)
    cyl("Servo_Eje", (0, -0.03, 0), 0.016, 0.014, "laton", axis="Y", verts=8, bev=0.003)
    ring_of(6, 0.045, (0, -0.021, 0), "Y", lambda p, a: rivet(p, "Y", 0.005))
    sphere("Servo_Sensor", (0.024, -0.03, 0.024), 0.008, "rojo", segments=8, rings=6)

# Actuador diagonal de la caña a la punta.
A, B = (0.0, -0.082, 0.31), (0.13, -0.078, 0.135)
length = math.hypot(B[0] - A[0], B[2] - A[2])
with group("Actuador", A, (0, math.atan2(A[2] - B[2], B[0] - A[0]), 0)):
    cyl("Actuador_Camisa", (0.065, 0, 0), 0.016, 0.12, "metal", verts=12, bev=0.003)
    cyl("Actuador_Vastago", (0.17, 0, 0), 0.009, 0.1, "filo", verts=10, bev=0.002)
    for x in (0.0, length):
        cyl("Actuador_Ojo", (x, 0, 0), 0.017, 0.022, "laton", axis="Y", verts=10, bev=0.003)

# Cables del servo a la caña.
pipe("Cable", [(-0.075, -0.08, 0.15), (-0.108, -0.08, 0.22), (-0.105, -0.07, 0.35)], 0.006, "goma")
pipe("Cable", [(-0.07, -0.08, 0.16), (-0.098, -0.084, 0.23), (-0.095, -0.07, 0.36)], 0.005, "rojoInt")

# Espuela aerodinámica en el costado del talón.
plate("Talon_Aleta", [(-0.09, 0.068), (-0.2, 0.068), (-0.105, 0.135)], 0.012, "mostaza", y=-0.072, bev=0.003)
