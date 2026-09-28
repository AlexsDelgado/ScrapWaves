"""RocketLauncher: tubo con cohete asomando, módulo de lock-on múltiple y tobera trasera abierta."""
import math

begin("RocketLauncher")
d = math.radians

# Tubo principal abierto, con interior negro para que se vea la boca.
tube("Tubo", (0.05, 0, 0), 0.1, 1.1, 0.014, "metal", verts=16)
cyl("Tubo_Interior", (0.05, 0, 0), 0.084, 1.08, "negro", verts=16, bev=0)
tube("Boca_Labio", (0.61, 0, 0), 0.118, 0.06, 0.03, "filo", verts=16)

# Cohete cargado: la cabeza con franja de precaución marca la explosión.
cyl("Cohete_Cuerpo", (0.6, 0, 0), 0.068, 0.12, "parche", verts=14)
cyl("Cohete_Franja", (0.675, 0, 0), 0.071, 0.03, "mostaza", verts=14, bev=0.002)
cone("Cohete_Ojiva", (0.77, 0, 0), 0.07, 0.014, 0.16, "metal", verts=14)
cyl("Cohete_Espoleta", (0.855, 0, 0), 0.014, 0.02, "goma", verts=8)

# Tobera trasera abierta.
tube("Tobera", (-0.56, 0, 0), 0.1, 0.09, 0.012, "parche", verts=16, r2=0.128, rot=(0, -math.pi / 2, 0))
cyl("Tobera_Fondo", (-0.5, 0, 0), 0.086, 0.01, "negro", verts=16, bev=0)
cyl("Tobera_Aro", (-0.505, 0, 0), 0.106, 0.03, "oxidoOsc", verts=16, bev=0.004)

cyl("Cinta_Goma", (-0.12, 0, 0), 0.104, 0.12, "goma", verts=16, bev=0.004)

# Cinchos de óxido con tornillo arriba.
for x in (-0.32, 0.36):
    cyl("Cincho", (x, 0, 0), 0.11, 0.045, "oxido", verts=16, bev=0.005)
    box("Cincho_Tornillo", (x, 0, 0.115), (0.03, 0.035, 0.025), "filo", bev=0.004)

# Parche soldado torcido sobre el tubo.
box("Parche", (0.08, -0.085, 0.03), (0.16, 0.03, 0.09), "parche", rot=(d(-30), d(6), 0), bev=0.004)
for dx in (-0.06, 0.06):
    rivet((0.08 + dx, -0.1, 0.055), "Y", 0.009)

# Cuerpo inferior, empuñaduras y hombrera.
box("Receptor", (0.02, 0, -0.105), (0.26, 0.085, 0.06), "metal", bev=0.008)
box("Empunadura", (-0.02, 0, -0.2), (0.07, 0.06, 0.16), "goma", rot=(0, d(-14), 0), bev=0.01)
box("Guardamonte", (0.06, 0, -0.18), (0.1, 0.02, 0.014), "oxido", bev=0.003)
box("Gatillo", (0.045, 0, -0.155), (0.014, 0.014, 0.04), "filo", rot=(0, d(12), 0), bev=0.002)
box("Empunadura_Frontal", (0.3, 0, -0.17), (0.06, 0.055, 0.14), "goma", rot=(0, d(8), 0), bev=0.01)
box("Hombrera", (-0.34, 0, -0.115), (0.18, 0.075, 0.05), "goma", bev=0.012)

# Módulo de lock-on: caja de sensores con tres lentes (una roja) y antena.
with group("LockOn", (0.1, 0, 0.17)):
    box("LockOn_Base", (0, 0, -0.055), (0.14, 0.07, 0.06), "goma", bev=0.006)
    box("LockOn_Caja", (0, 0, 0.03), (0.22, 0.11, 0.1), "chapa", bev=0.01)
    box("LockOn_Visera", (0.12, 0, 0.075), (0.05, 0.12, 0.012), "metal", bev=0.003)
    for (y, z, m) in ((-0.03, 0.035, "rojo"), (0.03, 0.035, "negro"), (0.0, 0.0, "negro")):
        cyl("LockOn_Aro", (0.112, y, z), 0.022, 0.012, "oxidoOsc", verts=12, bev=0.002)
        cyl("LockOn_Lente", (0.119, y, z), 0.016, 0.006, m, verts=12, bev=0)
    for x in (-0.08, 0.06):
        rivet((x, -0.057, 0.06), "Y", 0.008)
    cyl("Antena", (-0.08, 0.03, 0.17), 0.006, 0.18, "filo", axis="Z", verts=6, bev=0)
    sphere("Antena_Punta", (-0.08, 0.03, 0.265), 0.014, "goma", segments=8, rings=6)

# Mira plegable delantera.
for y in (-0.03, 0.03):
    box("Mira_Poste", (0.47, y, 0.135), (0.012, 0.01, 0.06), "filo", bev=0.002)
box("Mira_Barra", (0.47, 0, 0.163), (0.012, 0.07, 0.01), "filo", bev=0.002)

pipe("Cable", [(0.0, -0.05, 0.16), (-0.08, -0.12, 0.02), (-0.04, -0.06, -0.12)], 0.011, "goma")
