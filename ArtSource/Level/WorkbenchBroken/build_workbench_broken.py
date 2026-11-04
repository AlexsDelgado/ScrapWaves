"""Author an editable damaged variant of the existing workbench and its review render.

Run with Blender 5.1+: blender --background --python build_workbench_broken.py
The existing FBX is read only. Export coordinates match its source pivot and units;
use the original Unity ModelImporter globalScale of 0.5 on WorkbenchBroken.fbx.
"""
import bpy
import bmesh
import json
import math
from pathlib import Path
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent
MODEL = ROOT / 'Assets/Art/Level/WorkbenchBroken.fbx'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(ROOT / 'Assets/Art/Level/Workbench.fbx'))
scene = bpy.context.scene
broken = list(scene.objects)
original_meshes = [o for o in broken if o.type == 'MESH']
collection = bpy.data.collections.new('WorkbenchBroken_EDITABLE')
scene.collection.children.link(collection)
for o in broken:
    for c in list(o.users_collection): c.objects.unlink(o)
    collection.objects.link(o)

def material(name, color, metal=0.0, rough=.8):
    mat=bpy.data.materials.new(name);mat.diffuse_color=(*color,1);mat.use_nodes=True
    shader=mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value=(*color,1)
    shader.inputs['Metallic'].default_value=metal
    shader.inputs['Roughness'].default_value=rough
    return mat

for m in list(bpy.data.materials):
    m.name = 'Broken ' + m.name.replace('Plain ', '')
    shader = m.node_tree.nodes.get('Principled BSDF') if m.use_nodes else None
    if shader:
        shader.inputs['Roughness'].default_value=max(.75,shader.inputs['Roughness'].default_value)
        if 'Emission Strength' in shader.inputs: shader.inputs['Emission Strength'].default_value=0
    if 'ivory lamp' in m.name:
        m.diffuse_color=(.035,.04,.04,1)
        if shader: shader.inputs['Base Color'].default_value=m.diffuse_color
    if 'ochre' in m.name:
        m.diffuse_color=(.32,.22,.07,1)
        if shader: shader.inputs['Base Color'].default_value=m.diffuse_color

steel=bpy.data.materials.get('Broken steel work surface')
rust=material('Broken exposed rust',(.23,.09,.035),.8,.95)
rubber=material('Broken cable rubber',(.012,.014,.015),.05,.95)
copper=material('Broken cable conductor',(.3,.14,.065),.8,.78)

def crease(p):
    # Keep all four foot plates and the original X/Y footprint. The frame buckles inward.
    x,y,z=p
    right=max(0,min(1,(x+.35)/2.75))
    height=max(0,min(1,(z-.17)/2.4))
    x -= .16*right*height
    z -= .63*right*height
    if y<-.45 and x>1.4 and .35<z<2.4:
        x-=.12*math.sin(math.pi*min(1,max(0,(z-.25)/2.2)))
    return Vector((x,y,z))

lamp_parts=('lamp','Arm threaded','Curved upper','Short curved electric','Tapered lamp','Vertical lamp','Pipe coupling collar')
lamp_pivot=Vector((2.04,.67,2.97))
lamp_rotation=Matrix.Rotation(math.radians(-56),4,'Y')

for o in original_meshes:
    if o.name == 'Thick solid worktop':
        bpy.data.objects.remove(o,do_unlink=True)
        continue
    # Bake all transforms into editable, individual mesh parts before deforming.
    o.data.transform(o.matrix_world)
    o.matrix_world=Matrix.Identity(4)
    is_lamp=any(key.lower() in o.name.lower() for key in lamp_parts)
    for v in o.data.vertices:
        p=v.co.copy()
        if is_lamp and p.z>2.97:
            p=lamp_pivot + lamp_rotation @ (p-lamp_pivot)
        v.co=crease(p)
    # Disturb a front crate plank without moving the model's footprint.
    if o.name == 'Crate horizontal long plank.002':
        center=sum((v.co for v in o.data.vertices),Vector())/len(o.data.vertices)
        rot=Matrix.Rotation(math.radians(-18),4,'Y')
        for v in o.data.vertices: v.co=center+rot@(v.co-center)+Vector((.09,-.12,-.10))
    # Reduce dense tiny round hardware; retain the original readable large shapes.
    if len(o.data.vertices)>40:
        bpy.context.view_layer.objects.active=o
        mod=o.modifiers.new('Game mesh reduction','DECIMATE');mod.ratio=.18
        bpy.ops.object.modifier_apply(modifier=mod.name)
    o.name='Damaged '+o.name

def mesh_object(name,verts,faces,mat):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    ob=bpy.data.objects.new(name,mesh);collection.objects.link(ob);ob.data.materials.append(mat)
    return ob

def split_plate(name,outline,right=False):
    # Jagged opposing edges leave an actual hole through the slab, visible from above.
    verts=[]
    for z in (2.55,2.77):
        for x,y in outline:
            p=crease(Vector((x,y,z)))
            if right:
                p.z-=.12 + .18*max(0,1-abs(x-.20)/.7)
            verts.append(tuple(p))
    n=len(outline)
    faces=[tuple(reversed(range(n))),tuple(range(n,2*n))]
    faces.extend((i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n))
    ob=mesh_object(name,verts,faces,steel)
    ob.data.materials.append(rust)
    for poly in ob.data.polygons:
        if poly.index>1 and any(abs(ob.data.vertices[v].co.x)<.6 for v in poly.vertices):poly.material_index=1
    bevel=ob.modifiers.new('Soft worn outer edges','BEVEL');bevel.width=.016;bevel.segments=1
    return ob

seam=[(.10,-1.075),(-.16,-.70),(.14,-.31),(-.12,.05),(.17,.42),(-.08,.73),(.10,1.075)]
split_plate('Split worktop left',[(-2.425,-1.075),*seam,(-2.425,1.075)])
split_plate('Split worktop right',[(x+.18,y) for x,y in reversed(seam)]+[(2.425,-1.075),(2.425,1.075)],True)

def cable(name,coords,radius,mat):
    curve=bpy.data.curves.new(name,'CURVE');curve.dimensions='3D';curve.resolution_u=2
    curve.bevel_depth=radius;curve.bevel_resolution=0;curve.resolution_u=1
    spline=curve.splines.new('POLY');spline.points.add(len(coords)-1)
    for pt,co in zip(spline.points,coords):pt.co=(*co,1)
    ob=bpy.data.objects.new(name,curve);collection.objects.link(ob);ob.data.materials.append(mat)
    return ob

# A torn pair hangs from the buckled lamp mount, inside the bench's collision footprint.
anchor=crease(Vector((2.04,.67,2.95)))
cable('Severed power lead',[anchor,(1.72,.48,2.35),(1.64,.22,1.84),(1.48,-.02,1.45),(1.26,-.19,1.32)],.035,rubber)
cable('Exposed copper tip',[(1.26,-.19,1.32),(1.15,-.25,1.29)],.018,copper)
cable('Loose ground wire',[anchor+Vector((-.05,0,0)),(1.52,.38,2.2),(1.43,.16,1.82),(1.56,-.07,1.64)],.025,rubber)
cable('Ground conductor',[(1.56,-.07,1.64),(1.65,-.15,1.61)],.012,copper)

# Apply exportable modifiers/curves while leaving each part separate in the .blend source.
for o in list(collection.objects):
    if o.type not in {'MESH','CURVE'}:continue
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    if o.type=='CURVE':bpy.ops.object.convert(target='MESH')
    for mod in list(o.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)

source_objects=[o for o in collection.objects if o.type=='MESH']
points=[o.matrix_world@Vector(c) for o in source_objects for c in o.bound_box]
lo=Vector([min(p[i] for p in points) for i in range(3)])
hi=Vector([max(p[i] for p in points) for i in range(3)])

# Export a combined mesh to keep the authored prefab's renderer count low.
bpy.ops.object.select_all(action='DESELECT')
export_copies=[]
for o in source_objects:
    copy=o.copy();copy.data=o.data.copy();scene.collection.objects.link(copy);copy.select_set(True);export_copies.append(copy)
bpy.context.view_layer.objects.active=export_copies[0];bpy.ops.object.join()
merged=bpy.context.object;merged.name='WorkbenchBroken';merged.data.name='WorkbenchBrokenMesh'
scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
bm=bmesh.new();bm.from_mesh(merged.data)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
bm.to_mesh(merged.data);bm.free();merged.data.update()
merged.data.calc_loop_triangles()
stats={'source_bounds_blender':[list(lo),list(hi)],'dimensions_blender':list(hi-lo),'vertices':len(merged.data.vertices),'triangles':len(merged.data.loop_triangles),'materials':[m.name for m in merged.data.materials],'unity_global_scale':.5,'axis_forward':'-Z','axis_up':'Y'}
bpy.ops.export_scene.fbx(filepath=str(MODEL),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,add_leaf_bones=False)
bpy.data.objects.remove(merged,do_unlink=True)
(OUT/'geometry.json').write_text(json.dumps(stats,indent=2))
scene.unit_settings.system='METRIC'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'WorkbenchBroken.blend'))
print('EXPORTED '+json.dumps(stats))

# Build an ephemeral side-by-side review stage. The exported source stays at its true origin.
right_axis=Vector((.88,.475,0)).normalized()
offset=right_axis*3.6
for o in source_objects:o.location+=offset
before=set(scene.objects)
bpy.ops.import_scene.fbx(filepath=str(ROOT/'Assets/Art/Level/Workbench.fbx'))
reference=[o for o in scene.objects if o not in before]
for o in reference:
    if not o.parent:o.location-=offset

floor=material('Review charcoal floor',(.022,.027,.031),.05,.9)
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,.025));bpy.context.object.data.materials.append(floor)
def aim(o,p):o.rotation_euler=(Vector(p)-o.location).to_track_quat('-Z','Y').to_euler()
target=Vector((0,0,2.20));camera_direction=Vector((.475,-.88,.55)).normalized()
bpy.ops.object.camera_add(location=target+camera_direction*22)
camera=bpy.context.object;aim(camera,target);camera.data.type='ORTHO';camera.data.ortho_scale=14.3;scene.camera=camera
for location,power,scale in [((-5,-6,10),2100,7),((6,-2,7),1500,6),((0,5,9),2600,5)]:
    bpy.ops.object.light_add(type='AREA',location=location);light=bpy.context.object;light.data.energy=power;light.data.shape='DISK';light.data.size=scale;aim(light,(0,0,2))
world=bpy.data.worlds.new('Workbench Review Studio');world.use_nodes=True
world.node_tree.nodes['Background'].inputs['Color'].default_value=(.10,.12,.14,1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value=.45;scene.world=world
labelmat=material('Review label',(.72,.77,.78),0,1)
def label(body,center,size):
    bpy.ops.object.text_add(location=center);o=bpy.context.object;o.data.body=body;o.data.align_x='CENTER';o.data.align_y='CENTER';o.data.size=size;o.rotation_euler=camera.rotation_euler;o.data.materials.append(labelmat)
up=camera.rotation_euler.to_matrix()@Vector((0,1,0))
label('ACTIVE',target-offset+up*3.1,.26)
label('BROKEN / INACTIVE',target+offset+up*3.1,.26)
scene.render.engine='CYCLES';scene.cycles.samples=40
scene.render.resolution_x=1800;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(OUT/'workbench-comparison.png');bpy.ops.render.render(write_still=True)
print('PREVIEW '+scene.render.filepath)
