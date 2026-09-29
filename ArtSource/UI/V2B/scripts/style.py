"""Estilo de la variante B: acero claro y pulido como los íconos de armas, panel oscuro casi mate y luz de
arriba a la izquierda con más especular, para que el HUD haga juego con el marco de los items."""


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
    MAT_ALIASES["chapa"] = _set_mat("SW_V2B_Chapa", "#8e8e8b", 1.0, 0.42)
    MAT_ALIASES["filo"] = _set_mat("SW_V2B_Filo", "#b4b4b0", 1.0, 0.32)
    MAT_ALIASES["metal"] = _set_mat("SW_V2B_Metal", "#7a7a77", 1.0, 0.42)
    MAT_ALIASES["panel"] = _set_mat("SW_V2B_Panel", "#2c2c2a", 0.3, 0.7)
    MAT_ALIASES["oxido"] = _set_mat("SW_V2B_Cobre", "#9a5530", 1.0, 0.45)
    MAT_ALIASES["mostaza"] = _set_mat("SW_V2B_Mostaza", "#d8c030", 0.6, 0.45)
    MAT_ALIASES["led"] = _set_mat("SW_V2B_Led", "#ffc53a", 0.0, 0.35, emission=1.5)
    bpy.data.objects["HudKey"].data.energy = 6.0
    bpy.data.objects["HudKey"].rotation_quaternion = Vector((0.45, 0.7, -1.0)).normalized().to_track_quat("-Z", "Y")
    bpy.data.objects["HudRim"].data.energy = 2.8
    bpy.data.objects["HudFill"].data.energy = 0.6
