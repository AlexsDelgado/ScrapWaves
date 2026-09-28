"""Flamethrower: depósito mostaza con manómetro, mangueras, camisa perforada y llama piloto."""
import math

begin("Flamethrower")
d = math.radians

# Cuerpo.
box("Cuerpo", (0, 0, 0), (0.36, 0.12, 0.15), "metal", bev=0.012)
box("Cuerpo_Placa", (-0.02, -0.064, -0.005), (0.24, 0.01, 0.1), "chapa", bev=0.003)
for x in (-0.12, 0.08):
    for z in (-0.035, 0.03):
        rivet((x, -0.07, z), "Y", 0.009)
box("Cuerpo_Culata", (-0.2, 0, -0.01), (0.05, 0.1, 0.12), "goma", bev=0.01)

# Caño, camisa perforada y boquilla.
cyl("Cano", (0.47, 0, 0.0), 0.034, 0.6, "parche", verts=12)
cyl("Collar", (0.2, 0, 0.0), 0.08, 0.05, "oxidoOsc", verts=12, bev=0.006)
tube("Camisa", (0.42, 0, 0.0), 0.072, 0.36, 0.01, "metal", verts=12)
for i, x in enumerate((0.3, 0.37, 0.44, 0.51)):
    for a in (d(95), d(130), d(165), d(200), d(235)):
        a2 = a + (d(17) if i % 2 else 0)
        y, z = math.cos(a2) * 0.0725, math.sin(a2) * 0.0725
        cyl("Camisa_Agujero", (x, y, z), 0.014, 0.003, "negro", verts=8, bev=0, rot=(math.atan2(-y, z), 0, 0))
cyl("Camisa_Aro", (0.61, 0, 0.0), 0.08, 0.025, "oxido", verts=12, bev=0.004)
tube("Boquilla", (0.79, 0, 0.0), 0.042, 0.08, 0.01, "filo", verts=12, r2=0.058)
cyl("Boquilla_Fondo", (0.76, 0, 0.0), 0.036, 0.01, "negro", verts=12, bev=0)

# Llama piloto bajo la boquilla.
box("Piloto_Soporte", (0.72, 0, -0.055), (0.04, 0.03, 0.05), "goma", bev=0.004)
cyl("Piloto_Tubo", (0.78, 0, -0.07), 0.013, 0.1, "filo", verts=8)
cyl("Piloto_Luz", (0.832, 0, -0.07), 0.012, 0.008, "rojo", verts=8, bev=0)

# Depósito de combustible (mostaza = precaución), con tapas redondeadas y cinchos.
tx, tz, tr, tl = -0.02, 0.21, 0.1, 0.46
cyl("Deposito", (tx, 0, tz), tr, tl, "chapa", verts=14, bev=0.004)
cyl("Deposito_Franja", (tx - 0.015, 0, tz), tr + 0.003, 0.2, "mostaza", verts=14, bev=0.003)
for s in (-1, 1):
    sphere("Deposito_Tapa", (tx + s * tl / 2, 0, tz), tr, "chapa", scale=(0.45, 1, 1), segments=14, rings=8)
for x in (-0.15, 0.12):
    cyl("Deposito_Cincho", (x, 0, tz), tr + 0.008, 0.04, "oxido", verts=14, bev=0.004)
    box("Deposito_Soporte", (x, 0, 0.1), (0.05, 0.06, 0.06), "metal", bev=0.006)
cyl("Deposito_Boca", (-0.04, 0, tz + 0.1), 0.028, 0.04, "metal", axis="Z", verts=10)
cyl("Deposito_Tapon", (-0.04, 0, tz + 0.125), 0.034, 0.018, "goma", axis="Z", verts=10)

# Manómetro mirando al costado visible.
cyl("Manometro_Cuello", (0.16, -0.08, tz + 0.06), 0.012, 0.06, "filo", axis="Y", verts=8)
cyl("Manometro", (0.16, -0.12, tz + 0.06), 0.045, 0.025, "metal", axis="Y", verts=14, bev=0.004)
cyl("Manometro_Cara", (0.16, -0.133, tz + 0.06), 0.036, 0.004, "chapa", axis="Y", verts=14, bev=0)
box("Manometro_Aguja", (0.172, -0.137, tz + 0.07), (0.028, 0.003, 0.006), "rojo", rot=(0, d(-40), 0), bev=0)

# Mangueras: del depósito a la culata y del depósito al collar.
pipe("Manguera_Trasera", [(-0.3, 0, tz), (-0.37, -0.02, 0.1), (-0.3, -0.02, -0.03), (-0.22, 0, -0.03)], 0.02, "goma")
pipe("Manguera_Frontal", [(0.24, 0, tz - 0.02), (0.3, -0.04, 0.12), (0.22, -0.05, 0.06)], 0.016, "goma")

# Empuñaduras.
box("Empunadura", (-0.1, 0, -0.14), (0.07, 0.06, 0.16), "goma", rot=(0, d(-16), 0), bev=0.01)
box("Guardamonte", (-0.03, 0, -0.12), (0.1, 0.02, 0.014), "oxido", bev=0.003)
box("Gatillo", (-0.045, 0, -0.1), (0.014, 0.014, 0.04), "filo", rot=(0, d(12), 0), bev=0.002)
box("Empunadura_Frontal", (0.4, 0, -0.12), (0.055, 0.05, 0.13), "goma", rot=(0, d(10), 0), bev=0.01)
box("Empunadura_Frontal_Brida", (0.4, 0, -0.06), (0.07, 0.07, 0.02), "oxidoOsc", bev=0.004)
