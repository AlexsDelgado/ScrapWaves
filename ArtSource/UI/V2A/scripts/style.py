"""Estilo de la variante A: hierro más oscuro y mate, luz más frontal para un sombreado de cartoon plano
(como Player_Bars.png) y LEDs naranjas emisivos."""


def _set_mat(name, hex_color, metallic, roughness, emission=0.0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.use_fake_user = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    color = _srgb_to_linear(hex_color)
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Emission Color"].default_value = color
    bsdf.inputs["Emission Strength"].default_value = emission
    mat.diffuse_color = color
    return name


def apply_style():
    MAT_ALIASES["hierro"] = _set_mat("SW_V2A_Hierro", "#343331", 0.7, 0.55)
    MAT_ALIASES["hierroMed"] = _set_mat("SW_V2A_HierroMedio", "#5b5a57", 0.8, 0.45)
    MAT_ALIASES["metal"] = _set_mat("SW_V2A_Metal", "#6f6e6b", 0.85, 0.42)
    MAT_ALIASES["led"] = _set_mat("SW_V2A_Led", "#f07a10", 0.0, 0.35, emission=1.2)
    bpy.data.objects["HudKey"].data.energy = 5.0
    bpy.data.objects["HudKey"].rotation_quaternion = Vector((0.15, 0.6, -1.0)).normalized().to_track_quat("-Z", "Y")
    bpy.data.objects["HudFill"].data.energy = 0.9
