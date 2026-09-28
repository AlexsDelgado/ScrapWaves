"""Construye un item desde su script de modelado, guarda el .blend y renderiza el ícono, sin abrir la UI.

Uso (desde la raíz del repo):
    blender -b --factory-startup --python ArtSource/Tools/blender_build_item.py -- \
        <script_modelo.py> <Root> <salida.blend> <render.png> [--upright]
"""
import os
import sys

import bpy

TOOLS = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index("--") + 1:]
model_script, root_name, blend_out, render_out = (os.path.abspath(p) if i != 1 else p for i, p in enumerate(argv[:4]))
upright = "--upright" in argv

for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj, do_unlink=True)

ns = {"__name__": "__item__"}
for path in (os.path.join(TOOLS, "blender_icon_rig.py"), os.path.join(TOOLS, "blender_model_kit.py")):
    exec(compile(open(path, encoding="utf-8").read(), path, "exec"), ns)
ns["ensure_palette"]()
ns["__file__"] = model_script
exec(compile(open(model_script, encoding="utf-8").read(), model_script, "exec"), ns)

ns["ensure_rig"]()
ns["frame"](root_name, upright=upright)
os.makedirs(os.path.dirname(blend_out), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=blend_out)
ns["render_icon"](root_name, render_out, upright=upright)
print(f"OK {root_name}: {blend_out} | {render_out}")
