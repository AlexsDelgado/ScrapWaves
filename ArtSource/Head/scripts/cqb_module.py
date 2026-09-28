"""CQB module: casco de motocross con jaula frontal, visera superior y cresta de púas.

Representa el daño a corta distancia (CloseRangeDamageMultiplier x1.2 a x2.2).
"""
import math
import os

_base = os.path.join(os.path.dirname(__file__), "head_base.py")
exec(compile(open(_base, encoding="utf-8").read(), _base, "exec"))

begin("CQBModule")
d = math.radians
helmet(shell="mostaza", chin="metal", stripe="negro")

me = bpy.data.meshes.new("Visera_Sol")
bm = bmesh.new()
rows = []
for i in range(11):
    lo = d(-50 + i * 10)
    inner = on_shell(-50 + i * 10, 28, 0.006)
    reach = 0.065 * math.cos(lo * 0.9)
    outer = (inner[0] + math.cos(lo) * reach, inner[1] + math.sin(lo) * reach * 0.8, inner[2] - 0.018)
    rows.append((bm.verts.new(inner), bm.verts.new(outer)))
for (a0, b0), (a1, b1) in zip(rows, rows[1:]):
    bm.faces.new((a0, a1, b1, b0))
bm.to_mesh(me)
bm.free()
peak = bpy.data.objects.new("Visera_Sol", me)
bpy.context.scene.collection.objects.link(peak)
peak.modifiers.new("Grosor", "SOLIDIFY").thickness = 0.008
smooth(_adopt(peak, "oxidoOsc"))
for lon in (-30, 30):
    rivet(on_shell(lon, 30, 0.012), "Z", 0.005)

for y in (-0.05, -0.025, 0.0, 0.025, 0.05):
    cyl("Jaula_Barra", (0.158, y, 0.13), 0.005, 0.12, "filo", axis="Z", verts=8, bev=0.001)
for z in (0.07, 0.19):
    cyl("Jaula_Travesano", (0.158, 0, z), 0.007, 0.13, "metal", axis="Y", verts=8, bev=0.002)
for s in (-1, 1):
    pivot = on_shell(s * 80, 6, 0.012)
    for z in (0.07, 0.19):
        pipe("Jaula_Brazo", [pivot, (0.1, s * 0.09, (pivot[2] + z) / 2), (0.158, s * 0.065, z)], 0.006, "metal")

for t in (40, 62, 84, 106, 128):
    r = d(t)
    n = (math.cos(r), math.sin(r))
    p = (C[0] + n[0] * (RX + 0.012), 0, C[2] + n[1] * (RZ + 0.012))
    cone("Pua", p, 0.016, 0.002, 0.04, "oxido", axis="Z", verts=8, rot=(0, d(90 - t), 0), bev=0.002)
