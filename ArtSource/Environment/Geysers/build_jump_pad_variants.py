"""Approved optional hardware: same draft proportions, uniformly reduced to 75%.
Run in a disposable Blender process. Exports ONLY FloorFan/FloorVent FBXs and this source blend.
Never touches existing geysers, prefabs or scene placements.
"""
import bpy, math, json
from pathlib import Path
from mathutils import Vector, Matrix

OUT = Path(__file__).resolve().parent
REPO = OUT.parents[2]
EXPORT = REPO / "Assets/Art/Environment/Geysers/Models"
EXPORT.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
bpy.context.preferences.filepaths.save_version = 0

def material(name, color, metal=.35, rough=.85):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    shader = m.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color, 1)
    shader.inputs['Metallic'].default_value = metal
    shader.inputs['Roughness'].default_value = rough
    return m

MAT = {
    'dark': material('Geyser_HeatDarkenedMetal', (.225,.235,.25)),
    'steel': material('Geyser_DullScrap', (.37,.405,.42), .45),
    'bare': material('Geyser_BareSteel', (.62,.66,.67), .5),
    'rust': material('Geyser_RustScrap', (.40,.245,.16), .15),
    'pale': material('Geyser_PaleScrap', (.66,.65,.57), .1),
    'black': material('Geyser_DeepOpening', (.025,.025,.019), 0),
}
studio = bpy.data.collections.new('PREVIEW_ONLY_Studio')
scene.collection.children.link(studio)

def collection(name):
    c = bpy.data.collections.new(name)
    scene.collection.children.link(c)
    return c

def link(obj, col):
    for c in list(obj.users_collection): c.objects.unlink(obj)
    col.objects.link(obj)

def empty(name, col, parent=None):
    obj = bpy.data.objects.new(name, None)
    col.objects.link(obj)
    obj.parent = parent
    obj.empty_display_type = 'PLAIN_AXES'
    obj.empty_display_size = .4
    return obj

def mesh(name, vertices, faces, key, col, parent):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    col.objects.link(obj)
    obj.data.materials.append(MAT[key])
    obj.parent = parent
    return obj

def bevel(obj, amount=.025):
    mod = obj.modifiers.new('Small stamped-metal edge', 'BEVEL')
    mod.width = amount
    mod.segments = 1
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)

def box(name, center, dims, key, col, parent=None, angle=0, bevel_width=.025):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    obj = bpy.context.object
    obj.name = name
    link(obj, col)
    obj.dimensions = dims
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel_width: bevel(obj, bevel_width)
    obj.rotation_euler.z = angle
    obj.data.materials.append(MAT[key])
    obj.parent = parent
    return obj

def cylinder(name, center, radius, depth, key, col, parent=None, sides=16):
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius, depth=depth, location=center)
    obj = bpy.context.object
    obj.name = name
    link(obj, col)
    obj.data.materials.append(MAT[key])
    obj.parent = parent
    return obj

def annulus(name, outer, inner, z0, z1, key, col, parent, segments=32):
    vertices = []
    for z, r in [(z0,outer),(z0,inner),(z1,outer),(z1,inner)]:
        for i in range(segments):
            a = 2*math.pi*i/segments
            vertices.append((r*math.cos(a),r*math.sin(a),z))
    faces = []
    for i in range(segments):
        j = (i+1)%segments
        faces += [(i,j,2*segments+j,2*segments+i),
                  (segments+j,segments+i,3*segments+i,3*segments+j),
                  (2*segments+i,2*segments+j,3*segments+j,3*segments+i),
                  (j,i,segments+i,segments+j)]
    return mesh(name, vertices, faces, key, col, parent)

def guard_ring(name, radius, z, col, parent, wire=.027):
    n, sides = 32, 4
    vertices = []
    for i in range(n):
        a = 2*math.pi*i/n
        for j in range(sides):
            b = 2*math.pi*j/sides + math.pi/4
            r = radius + wire*math.cos(b)
            vertices.append((r*math.cos(a),r*math.sin(a),z+wire*math.sin(b)))
    faces = []
    for i in range(n):
        for j in range(sides):
            faces.append((i*sides+j,((i+1)%n)*sides+j,((i+1)%n)*sides+(j+1)%sides,i*sides+(j+1)%sides))
    return mesh(name, vertices, faces, 'rust', col, parent)

def rod(name, a, b, width, key, col, parent):
    a,b = Vector(a),Vector(b)
    obj = box(name, (a+b)*.5, (width,width,(b-a).length), key, col, parent, bevel_width=0)
    obj.rotation_euler = (b-a).to_track_quat('Z','Y').to_euler()
    return obj

def screw(name, x, y, z, col, parent, radius=.10):
    cylinder(name, (x,y,z), radius, .045, 'bare', col, parent, sides=8)
    box(name+'_Slot', (x,y,z+.023), (radius*1.4,.025,.003), 'dark', col, parent, bevel_width=0)

fan_col = collection('FloorFan_EDITABLE')
fan = empty('FloorFan', fan_col)
fan['note'] = 'Approved shape at 75% draft size. Floor-centre origin; separate rotor and fixed guard.'
box('Fan_DarkMountingPlate', (0,0,.07), (4.8,4.8,.14), 'dark', fan_col, fan, bevel_width=.065)
annulus('Fan_RustyCircularHousing', 1.93, 1.72, .14, .46, 'rust', fan_col, fan)
annulus('Fan_ExposedRim', 1.95, 1.68, .46, .50, 'rust', fan_col, fan)
cylinder('Fan_RecessShadow', (0,0,.16), 1.72, .025, 'black', fan_col, fan, sides=32)
rotor = empty('FanRotor_PIVOT', fan_col, fan)
rotor.location.z = .31
rotor['animation_axis'] = 'Local Z in Blender; Unity Y after FBX axis conversion. Rotor geometry remains separate.'
cylinder('Rotor_Hub', (0,0,.035), .29, .16, 'steel', fan_col, rotor, sides=16)
profile = [(.28,-.12),(.71,-.39),(1.40,-.43),(1.61,-.22),(1.47,.25),(.70,.24),(.28,.13)]
for i in range(5):
    a = math.radians(i*72)
    vertices = []
    for dz in (-.025,.025):
        for x,y in profile:
            vertices.append((x*math.cos(a)-y*math.sin(a),x*math.sin(a)+y*math.cos(a),y*.20+dz))
    n = len(profile)
    faces = [tuple(reversed(range(n))),tuple(range(n,2*n))]
    faces += [(j,(j+1)%n,(j+1)%n+n,j+n) for j in range(n)]
    mesh('Rotor_Blade_'+str(i+1), vertices, faces, 'pale' if i==1 else 'steel', fan_col, rotor)
guard = empty('FanGuard_STATIC', fan_col, fan)
for r in (.62,1.18,1.68): guard_ring('Guard_Ring_'+str(r),r,.64-.12*(r/1.75),fan_col,guard)
for i in range(6):
    a = i*math.pi/3
    rod('Guard_RadialBrace_'+str(i+1),(0,0,.65),(1.76*math.cos(a),1.76*math.sin(a),.515),.055,'rust',fan_col,guard)
cylinder('Guard_CentreFastener',(0,0,.68),.105,.055,'bare',fan_col,guard,sides=8)
for x in (-2.08,2.08):
    for y in (-2.08,2.08): screw('Fan_PlateFastener',x,y,.165,fan_col,fan)
box('Fan_SalvagedCornerPatch',(-1.68,-1.75,.153),(.50,.34,.025),'steel',fan_col,fan,angle=.08,bevel_width=.01)

vent_col = collection('FloorVent_EDITABLE')
vent = empty('FloorVent', vent_col)
vent['note'] = 'Approved shape at 75% draft size. Four louvers; Unity uses a separate smooth support collision envelope.'
box('Vent_BaseFlange',(0,0,.065),(4.8,4.8,.13),'dark',vent_col,vent,bevel_width=.055)
box('Vent_RecessShadow',(0,0,.16),(4.02,4.02,.12),'black',vent_col,vent,bevel_width=0)
for x in (-2.15,2.15): box('Vent_SideRim',(x,0,.26),(.48,4.5,.32),'steel',vent_col,vent,bevel_width=.045)
for y in (-2.15,2.15): box('Vent_EndRim',(0,y,.26),(3.86,.48,.32),'steel',vent_col,vent,bevel_width=.045)
for i,y in enumerate((-1.38,-.46,.46,1.38)):
    louver = box('Vent_Louver_'+str(i+1),(0,y,.39),(3.93,.86,.065),'bare' if i!=2 else 'steel',vent_col,vent,bevel_width=.022)
    louver.rotation_euler.x = math.radians(-25)
    # Folded front lip gives a stamped-sheet silhouette without decorative machinery.
    lip = box('Louver_Fold_'+str(i+1),(0,0,0),(3.90,.045,.055),'steel',vent_col,louver,bevel_width=.008)
    lip.location = (0,-.407,-.034)
for x in (-2.15,2.15):
    for y in (-2.15,2.15): screw('Vent_CornerScrew',x,y,.444,vent_col,vent)
# Restrained surface repair on one edge, using the existing muted rust palette.
box('Vent_WeatheredRimPatch',(2.15,.75,.427),(.31,.62,.012),'rust',vent_col,vent,bevel_width=.005)

def model_stats(col, root):
    bpy.context.view_layer.update()
    meshes = [o for o in col.objects if o.type=='MESH']
    triangles = 0
    points = []
    for obj in meshes:
        obj.data.calc_loop_triangles()
        triangles += len(obj.data.loop_triangles)
        points += [root.matrix_world.inverted() @ obj.matrix_world @ Vector(p) for p in obj.bound_box]
    dimensions = [round(max(v[a] for v in points)-min(v[a] for v in points),3) for a in range(3)]
    return {'triangles':triangles,'mesh_objects':len(meshes),'dimensions_m':dimensions,'materials':sorted({m.name for o in meshes for m in o.data.materials}),'root_pivot':'floor centre','status':'approved optional model at 75% draft dimensions'}

# Bake the approved 25% reduction into local geometry/translations; prefab roots stay unit scale.
for col in (fan_col,vent_col):
    for obj in col.objects:
        obj.location *= .75
        if obj.type == 'MESH': obj.data.transform(Matrix.Scale(.75,4))
# Join static geometry for economical draw calls; preserve the rotor pivot as a separate editable assembly.
def combine(objects, name):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects: obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.object.join()
    bpy.context.object.name=name
combine([o for o in fan_col.objects if o.type=='MESH' and o.parent==rotor], 'FanRotor_BladesAndHub')
combine([o for o in fan_col.objects if o.type=='MESH' and o.parent!=rotor], 'FanHousingAndGuard')
combine([o for o in vent_col.objects if o.type=='MESH'], 'VentFrameAndLouvers')
stats = {'Fan':model_stats(fan_col,fan),'Vent':model_stats(vent_col,vent)}
for col,root,name in [(fan_col,fan,'FloorFan'),(vent_col,vent,'FloorVent')]:
    bpy.ops.object.select_all(action='DESELECT')
    for obj in col.objects: obj.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=str(EXPORT/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,add_leaf_bones=False)
(OUT/'hardware-geometry.json').write_text(json.dumps(stats,indent=2))


scene['approval']='Shapes approved 2026-10-31; uniformly reduced to75% of preview dimensions.'
scene['scale_note']='3.6m square hardware. Gameplay launch values remain authored inUnity, not scaled with art.'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'ScrapWaves_Fan_Vent.blend'))
print('HARDWARE_MODELS_COMPLETE',json.dumps(stats))
