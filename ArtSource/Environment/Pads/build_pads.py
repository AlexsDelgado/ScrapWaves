import bpy, math, random, json, os
from pathlib import Path
from mathutils import Vector
OUT=Path(__file__).resolve().parent
random.seed(175)
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
for c in list(bpy.data.collections):
    if c.name!='Collection':bpy.data.collections.remove(c)
scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1
def material(name,color,metal=0,rough=.65,emit=0):
    m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
    if emit:p.inputs['Emission Color'].default_value=(*color,1);p.inputs['Emission Strength'].default_value=emit
    return m
M={
 'yellow':material('01_PaleSafetyYellow',(.88,.72,.30),.18),
 'black':material('02_CharcoalMarkings',(.045,.045,.035)),
 'steel':material('03_SalvagedSteel',(.34,.38,.38),.65,.48),
 'rust':material('04_EdgeRust',(.30,.105,.035),.25,.83),
 'frame':material('05_DarkOxideFrame',(.115,.08,.055),.45,.72),
 'rubber':material('06_ReusedRubber',(.065,.07,.065),0,.86),
 'spring':material('07_WornMustardMechanics',(.56,.40,.075),.6,.45),
 'light':material('08_WarmServiceLight',(.96,.76,.32),.1,.4,1.4)}
collections={};roots={};allparts={}
def begin(key):
    global current,root
    current=key;c=bpy.data.collections.new(key);scene.collection.children.link(c);collections[key]=c;allparts[key]=[]
    root=bpy.data.objects.new(key+'_ROOT',None);c.objects.link(root);root.empty_display_type='PLAIN_AXES';root.empty_display_size=1;roots[key]=root
    root['dimensions_m']='10 x 10 x 2';root['unity_origin']='center of existing collider';root['function']='automatic full-surface launch' if key=='JumpPlatform' else 'passive receiving platform'
def finish(obj,name,mat):
    obj.name=name
    for c in list(obj.users_collection):c.objects.unlink(obj)
    collections[current].objects.link(obj);obj.parent=root;obj.location.z-=1
    obj.data.materials.append(M[mat]);allparts[current].append(obj)
    return obj
def box(name,pos,size,mat,bev=.025):
    bpy.ops.mesh.primitive_cube_add(size=1,location=pos);o=bpy.context.object;o.scale=size;bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bev:
        b=o.modifiers.new('Small manufactured edge','BEVEL');b.width=bev;b.segments=1
        bpy.context.view_layer.objects.active=o;bpy.ops.object.modifier_apply(modifier=b.name)
    return finish(o,name,mat)
def cyl(name,pos,radius,depth,mat,verts=12):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=radius,depth=depth,location=pos);return finish(bpy.context.object,name,mat)
def polygon(name,points,z,mat):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata([(x,y,z-1)for x,y in points],[],[tuple(range(len(points)))]);mesh.update()
    o=bpy.data.objects.new(name,mesh);collections[current].objects.link(o);o.parent=root;o.data.materials.append(M[mat]);allparts[current].append(o);return o
def ring(name,r1,r2,z,mat,segments=32):
    points=[]
    for r in (r1,r2):points.extend([(r*math.cos(i*2*math.pi/segments),r*math.sin(i*2*math.pi/segments),z-1)for i in range(segments)])
    faces=[(i,(i+1)%segments,(i+1)%segments+segments,i+segments)for i in range(segments)]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(points,[],faces);mesh.update();o=bpy.data.objects.new(name,mesh);collections[current].objects.link(o);o.parent=root;o.data.materials.append(M[mat]);allparts[current].append(o)
def coil(name,x,y):
    verts=[];faces=[];steps=60;sides=6
    for i in range(steps+1):
        a=i*2*math.pi/12;z=.62+i/steps*.88
        for j in range(sides):
            b=j*2*math.pi/sides;r=.30+.055*math.cos(b);verts.append((x+r*math.cos(a),y+r*math.sin(a),z+.055*math.sin(b)-1))
    for i in range(steps):
        for j in range(sides):faces.append((i*sides+j,i*sides+(j+1)%sides,(i+1)*sides+(j+1)%sides,(i+1)*sides+j))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update();o=bpy.data.objects.new(name,mesh);collections[current].objects.link(o);o.parent=root;o.data.materials.append(M['spring']);allparts[current].append(o)
def common(landing=False):
    box('Welded_lower_chassis',(0,0,.18),(10,10,.36),'frame',.08)
    for x in (-4.5,4.5):box('Recovered_channel_beam',(x,0,.40),(.32,9.2,.23),'steel')
    for y in (-4.5,4.5):box('Recovered_channel_beam',(0,y,.40),(8.8,.32,.23),'steel')
    box('Broad_launch_deck' if not landing else 'Reinforced_receiving_deck',(0,0,1.78),(9.55,9.55,.38),'yellow',.08)
    # Four perimeter strips remain below the 2m original upper bound.
    for x in (-4.86,4.86):box('Steel_upper_edge',(x,0,1.79),(.28,10,.40),'steel',.04)
    for y in (-4.86,4.86):box('Steel_upper_edge',(0,y,1.79),(9.44,.28,.40),'steel',.04)
    for x in (-4.40,4.40):
        for y in (-4.40,4.40):cyl('Hex_deck_fastener',(x,y,1.98),.11,.036,'steel',6)
    # Wear is modeled as a few flat paint chips, confined to edges for readability.
    for i in range(28):
        x=random.uniform(-4.55,4.55);y=random.choice((-1,1))*random.uniform(4.18,4.59)
        o=box('Edge_paint_chip_%02d'%i,(x,y,1.974),(random.uniform(.10,.34),random.uniform(.07,.19),.004),'rust',0);o.rotation_euler.z=random.uniform(-.5,.5)
    for side in (-1,1):
        for t in (-3.0,0,3.0):
            box('Service_light_housing',(t,side*4.9,1.70),(1.18,.19,.19),'black')
            box('Warm_edge_lamp',(t,side*4.998,1.73),(.85,.004,.065),'light',0)
    # Practical directional / receiving symbols visible on the near side as well as the top.
    for x in (-1.25,1.25):box('Side_safety_plate',(x,-4.97,.25),(1.6,.03,.22),'yellow',.012)
begin('JumpPlatform');common()
for x in (-3.85,3.85):
    for y in (-3.85,3.85):
        cyl('Spring_lower_seat',(x,y,.56),.43,.14,'rust')
        cyl('Guide_piston_rod',(x,y,1.02),.10,.93,'steel')
        coil('Exposed_compression_spring',x,y)
        cyl('Spring_upper_seat',(x,y,1.54),.43,.12,'steel')
for x in (-4.58,4.58):
    for y in (-2.15,2.15):
        box('Side_piston_bracket',(x,y,.57),(.50,.62,.25),'rust')
        cyl('Hydraulic_barrel',(x,y,.92),.18,.51,'steel')
        cyl('Piston_extension',(x,y,1.38),.08,.41,'steel')
        cyl('Service_collar',(x,y,1.17),.21,.09,'spring')
for y in (-2.7,-.5,1.7):
    polygon('Broad_UP_chevron',[(-2.7,y-.45),(0,y+.85),(2.7,y-.45),(2.7,y+.22),(0,y+1.52),(-2.7,y+.22)],1.977,'black')
begin('LandingPlatform');common(True)
for x in (-3.85,3.85):
    for y in (-3.85,3.85):
        box('Cushion_mount',(x,y,.59),(1.12,1.12,.32),'rust')
        for z in (.86,1.09,1.32):cyl('Reclaimed_rubber_cushion',(x,y,z),.54,.20,'rubber',12)
        box('Receiving_plate_support',(x,y,1.54),(1.10,1.10,.17),'steel')
for x in (-4.77,4.77):
    for y in (-3.0,0,3.0):box('Perimeter_rubber_impact_bumper',(x,y,1.40),(.40,1.22,.28),'rubber',.08)
ring('Receiving_target_outer',2.55,2.83,1.977,'black');ring('Receiving_target_inner',1.18,1.40,1.977,'black')
box('Target_center',(0,0,1.978),(.42,.42,.006),'black',0)
for angle in (0,math.pi/2,math.pi,math.pi*1.5):
    o=box('Target_registration_bar',(math.sin(angle)*3.47,math.cos(angle)*3.47,1.978),(.26,1.05,.006),'black',0);o.rotation_euler.z=angle
# Export static render meshes merged into one mesh per platform; preserve editable source parts.
exports=OUT/'exports';exports.mkdir(exist_ok=True)
stats={}
for key in roots:
    bpy.ops.object.select_all(action='DESELECT');copies=[]
    for original in allparts[key]:
        cp=original.copy();cp.data=original.data.copy();scene.collection.objects.link(cp);cp.parent=None;cp.select_set(True);copies.append(cp)
    bpy.context.view_layer.objects.active=copies[0];bpy.ops.object.join();merged=bpy.context.object;merged.name=key+'_StaticMesh'
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.01);bpy.ops.object.mode_set(mode='OBJECT')
    merged.data.calc_loop_triangles();stats[key]={'dimensions_m':[round(v,3)for v in merged.dimensions],'vertices':len(merged.data.vertices),'triangles':len(merged.data.loop_triangles),'editable_parts':len(allparts[key]),'materials':len(set(m.name for m in merged.data.materials))}
    # Deduplicate joined material slots without changing their assignments.
    mats=[];mapping={}
    for i,m in enumerate(merged.data.materials):
        if m not in mats:mats.append(m)
        mapping[i]=mats.index(m)
    indices=[mapping[p.material_index]for p in merged.data.polygons];merged.data.materials.clear()
    for m in mats:merged.data.materials.append(m)
    for p,i in zip(merged.data.polygons,indices):p.material_index=i
    bpy.ops.export_scene.fbx(filepath=str(exports/(key+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,add_leaf_bones=False,use_mesh_modifiers=True)
    bpy.ops.export_scene.gltf(filepath=str(exports/(key+'.glb')),export_format='GLB',use_selection=True,export_yup=True)
    bpy.data.objects.remove(merged,do_unlink=True)
(OUT/'geometry.json').write_text(json.dumps(stats,indent=2))
# Review stage: both roots retain a clearly labeled display offset, export origins stay centered.
roots['JumpPlatform'].location.x=-6.3;roots['LandingPlatform'].location.x=6.3
stage=bpy.data.collections.new('REVIEW_STAGE_NOT_FOR_EXPORT');scene.collection.children.link(stage)
def stage_obj(o):
    for c in list(o.users_collection):c.objects.unlink(o)
    stage.objects.link(o)
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-1.025));ground=bpy.context.object;ground.name='Brown_black_floor_context';ground.data.materials.append(material('Review_only_brown_ground',(.085,.057,.035)));stage_obj(ground)
world=bpy.data.worlds.new('Review_world');scene.world=world;world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.18,.20,.22,1);world.node_tree.nodes['Background'].inputs[1].default_value=.4
for name,pos,power,size in [('Key',(-8,-10,18),2800,9),('Fill',(10,-4,12),1900,10),('Rim',(0,12,15),2600,8)]:
    bpy.ops.object.light_add(type='AREA',location=pos);l=bpy.context.object;l.name=name;l.data.energy=power;l.data.shape='DISK';l.data.size=size;l.rotation_euler=(Vector((0,0,0))-l.location).to_track_quat('-Z','Y').to_euler();stage_obj(l)
bpy.ops.object.camera_add();camera=bpy.context.object;camera.name='Review_camera';stage_obj(camera);scene.camera=camera;camera.data.type='ORTHO'
scene.render.engine='CYCLES';scene.cycles.samples=48;scene.cycles.use_denoising=True;scene.render.resolution_x=1600;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
renders=OUT/'renders';renders.mkdir(exist_ok=True)
def render(name,pos,target,scale):
    camera.location=pos;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=scale;scene.render.filepath=str(renders/name);bpy.ops.render.render(write_still=True)
render('01-pair-oblique.png',(18,-25,22),(0,0,.1),31)
render('02-pair-low-approach.png',(12,-28,11),(0,0,.1),30)
render('03-pair-side-silhouettes.png',(0,-30,4),(0,0,0),29)
render('04-jump-detail.png',(-15,-14,12),(-6.3,0,.1),15)
render('05-landing-detail.png',(15,-14,12),(6.3,0,.1),15)
camera.location=(18,-25,22);camera.rotation_euler=(Vector((0,0,.1))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=31
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'ScrapWaves_PadPair_Review.blend'))
print('PAD_PAIR_COMPLETE',json.dumps(stats))
