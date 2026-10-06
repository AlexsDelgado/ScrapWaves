"""Editable compacted-scrap geysers. Normal regeneration preserves approved collision FBXs."""
import bpy, math, random, json, sys
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree

OUT=Path(__file__).resolve().parent
UNITY=OUT.parents[2]/'Assets'/'Art'/'Environment'/'Geysers'/'Models'
UNITY.mkdir(parents=True,exist_ok=True)
PROFILES=json.loads((OUT/'collision-profiles.json').read_text())
stats=json.loads((OUT/'geometry.json').read_text()) if (OUT/'geometry.json').exists() else {}
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
scene=bpy.context.scene
scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
bpy.context.preferences.filepaths.save_version=0

def material(name,color,metallic=0,rough=.85):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
    node=m.node_tree.nodes.get('Principled BSDF')
    node.inputs['Base Color'].default_value=(*color,1)
    node.inputs['Metallic'].default_value=metallic; node.inputs['Roughness'].default_value=rough
    return m

MAT={
    'junk':material('Geyser_DarkCompactedScrap',(.22,.235,.24),.25),
    'rust':material('Geyser_RustScrap',(.40,.245,.16),.15),
    'paper':material('Geyser_PaleScrap',(.66,.65,.57),.10),
    'steel':material('Geyser_DullScrap',(.37,.405,.42),.45),
    'heat':material('Geyser_HeatDarkenedMetal',(.225,.235,.25),.35),
    'bare':material('Geyser_BareSteel',(.62,.66,.67),.50,.72),
    'mouth':material('Geyser_DeepOpening',(.025,.025,.019)),
}

def mesh_object(name,vertices,faces,key,collection):
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(vertices,[],faces); mesh.update()
    obj=bpy.data.objects.new(name,mesh); collection.objects.link(obj); obj.data.materials.append(MAT[key])
    return obj

def surface_height(bvh,x,y):
    hit=bvh.ray_cast(Vector((x,y,3)),Vector((0,0,-1)),6)[0]
    return hit.z if hit else -1.0

def shell(name,vertices,faces,key,collection,thickness=.025):
    count=len(vertices); bottom=[(x,y,z-thickness) for x,y,z in vertices]
    all_faces=list(faces)+[tuple(i+count for i in reversed(face)) for face in faces]
    edges={}
    for face in faces:
        for a,b in zip(face,face[1:]+face[:1]):
            edge=tuple(sorted((a,b))); edges.setdefault(edge,[]).append((a,b))
    for uses in edges.values():
        if len(uses)==1:
            a,b=uses[0]; all_faces.append((a,a+count,b+count,b))
    return mesh_object(name,vertices+bottom,all_faces,key,collection)

def bent_sheet(name,bvh,angle,radius,width,length,key,collection,corrugated=False):
    columns=9 if corrugated else 2; rows=2 if corrugated else 3
    tangent=Vector((-math.sin(angle),math.cos(angle))); radial=Vector((math.cos(angle),math.sin(angle)))
    vertices=[]
    for row in range(rows):
        y=(row/(rows-1)-.5)*length
        for col in range(columns):
            x=(col/(columns-1)-.5)*width; point=radial*(radius+y)+tangent*x
            fold=(.025 if col%2 else -.005) if corrugated else (.045 if row==1 else 0)
            vertices.append((point.x,point.y,surface_height(bvh,point.x,point.y)+.055+fold))
    faces=[]
    for row in range(rows-1):
        for col in range(columns-1):
            a=row*columns+col; faces.append((a,a+1,a+columns+1,a+columns))
    return shell(name,vertices,faces,key,collection)

def rim_piece(name,bvh,angle,span,key,collection):
    vertices=[]
    for radius in (1.48,2.03):
        for index in range(3):
            a=angle+(index-1)*span*.5; x,y=radius*math.cos(a),radius*math.sin(a)
            vertices.append((x,y,surface_height(bvh,x,y)+.055+(.02 if index==1 else 0)))
    return shell(name,vertices,[(0,3,4,1),(1,4,5,2)],key,collection)

def pipe_piece(name,bvh,angle,radius,length,diameter,collection,key='rust'):
    center=Vector((radius*math.cos(angle),radius*math.sin(angle),surface_height(bvh,radius*math.cos(angle),radius*math.sin(angle))+.10))
    along=Vector((-math.sin(angle),math.cos(angle),-.16)).normalized()
    side=Vector((math.cos(angle),math.sin(angle),0)); up=along.cross(side).normalized()
    vertices=[]; segments=8
    for end in (-.5,.5):
        for ring in (diameter*.5,diameter*.5-.035):
            for i in range(segments):
                a=i*2*math.pi/segments
                vertices.append(tuple(center+along*length*end+side*math.cos(a)*ring+up*math.sin(a)*ring))
    faces=[]
    for i in range(segments):
        j=(i+1)%segments
        faces.extend([(i,j,16+j,16+i),(8+j,8+i,24+i,24+j),(i,8+i,8+j,j),(16+j,24+j,24+i,16+i)])
    return mesh_object(name,vertices,faces,key,collection)

def crushed_pail(name,bvh,collection):
    angle=4.55; radius=3.50; x,y=radius*math.cos(angle),radius*math.sin(angle)
    base=surface_height(bvh,x,y)+.015; vertices=[]
    for r,z in ((.32,0),(.39,.26),(.345,.26),(.28,.05)):
        for i in range(8):
            a=i*2*math.pi/8
            vertices.append((x+math.cos(a)*r,y+math.sin(a)*r*.74,base+z+.035*math.sin(i*1.7)))
    faces=[]
    for band in range(3):
        for i in range(8):
            j=(i+1)%8; faces.append((band*8+i,band*8+j,(band+1)*8+j,(band+1)*8+i))
    faces.append(tuple(range(24,32)))
    return mesh_object(name,vertices,faces,'paper',collection)

def export_objects(objects,name):
    bpy.ops.object.select_all(action='DESELECT'); copies=[]
    for source in objects:
        obj=source.copy(); obj.data=source.data.copy()
        scene.collection.objects.link(obj); obj.select_set(True); copies.append(obj)
    bpy.context.view_layer.objects.active=copies[0]; bpy.ops.object.join()
    merged=bpy.context.object; merged.name=name
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.02)
    bpy.ops.object.mode_set(mode='OBJECT'); mats=[]; mapping={}
    for index,mat in enumerate(merged.data.materials):
        if mat not in mats:mats.append(mat)
        mapping[index]=mats.index(mat)
    indices=[mapping[p.material_index] for p in merged.data.polygons]; merged.data.materials.clear()
    for mat in mats:merged.data.materials.append(mat)
    for polygon,index in zip(merged.data.polygons,indices):polygon.material_index=index
    merged.data.calc_loop_triangles()
    stats[name]={'vertices':len(merged.data.vertices),'triangles':len(merged.data.loop_triangles),'material_slots':len(mats),'dimensions_m':[round(v,4) for v in merged.dimensions]}
    bpy.ops.export_scene.fbx(filepath=str(UNITY/(name+'.fbx')),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,bake_anim=False,add_leaf_bones=False)
    bpy.data.objects.remove(merged,do_unlink=True)

for index,name in enumerate(('TrashGeyser','HotAirGeyser')):
    random.seed(175+index)
    collection=bpy.data.collections.new(name+'_EDITABLE'); scene.collection.children.link(collection)
    profile=PROFILES[name]
    core=mesh_object(name+'_CompactedScrapCore',profile['vertices'],profile['faces'],'junk' if index==0 else 'heat',collection)
    keys=['junk','steel','rust','paper','bare','mouth'] if index==0 else ['heat','steel','rust','bare','mouth']
    core.data.materials.clear()
    for key in keys:core.data.materials.append(MAT[key])
    for p in core.data.polygons:
        if p.index>=240:p.material_index=keys.index('mouth')
        elif p.index>=144:p.material_index=keys.index('bare' if p.index//2%3 else 'rust')
        else:p.material_index=keys.index(['steel','rust',keys[0]][(p.index//2*7+p.index//48)%3])
    bvh=BVHTree.FromPolygons([Vector(v) for v in profile['vertices']],profile['faces']); pieces=[core]
    for ring in range(2):
        for i in range(12):
            angle=i*2*math.pi/12+ring*.19+random.uniform(-.07,.07)
            key=random.choice(['steel','rust','paper']) if index==0 else random.choice(['steel','rust','heat'])
            pieces.append(bent_sheet(name+'_BentSheet_'+str(ring*12+i),bvh,angle,3.82 if ring==0 else 2.82,random.uniform(1.35,1.65),1.5 if ring==0 else 1.3,key,collection))
    for i in range(12):
        pieces.append(rim_piece(name+'_RoughMetalLip_'+str(i),bvh,i*2*math.pi/12+random.uniform(-.035,.035),.45,'bare' if i%3 else 'rust',collection))
    for i,angle in enumerate((3.55,5.25)):
        pieces.append(bent_sheet(name+'_CorrugatedRoofOffcut_'+str(i),bvh,angle,3.1,1.35,1.25,'bare' if i==0 else 'rust',collection,True))
    pieces.append(pipe_piece(name+'_DiscardedPipe',bvh,3.95,3.65,1.15,.34,collection))
    if index==0:pieces.append(crushed_pail(name+'_CrushedHouseholdPail',bvh,collection))
    else:pieces.append(pipe_piece(name+'_ShortDuctOffcut',bvh,4.8,3.35,.72,.48,collection,'steel'))
    export_objects(pieces,name)
    # Visual refinement never touches the approved collision FBXs without explicit opt-in.
    if '--export-collision' in sys.argv:export_objects([core],name+'Collision')

(OUT/'geometry.json').write_text(json.dumps(stats,indent=2))
scene.world.color=(.15,.15,.15)
scene['authoring_note']='Compacted ordinary scrap: bent sheets, roof offcuts, rough metal lip and discarded pipe/pail/duct. Collision profiles retained. Normal exports are visual-only. Hide the other collection when editing.'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'ScrapWaves_Geysers.blend'))
print('SCRAPPUNK_GEYSER_MODELS_COMPLETE',json.dumps(stats))
