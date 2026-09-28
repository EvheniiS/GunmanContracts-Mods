# Initial 48-sided blockout stage only. Final edited 64-sided model is in billy_club.blend.
# Run in the starter file; do not run over the finished scene.
import bpy, math
from mathutils import Vector, Quaternion
scene=bpy.context.scene
root='C:/Users/Evhenii/VR/GunmanContracts/BlenderRefs/'
def collection(name,parent=None):
    c=bpy.data.collections.get(name)
    if not c:
        c=bpy.data.collections.new(name)
        (parent or scene.collection).children.link(c)
    return c
source=collection('BillyClubs')
studio=collection('BC_Studio')
tests=collection('BC_MaterialTests')
for name in ['GameRefs','DesignRefs']:
    c=bpy.data.collections[name]
    c.hide_render=True
    c.hide_viewport=True
def material(name,color,metal,rough):
    m=bpy.data.materials.new(name)
    m.diffuse_color=(*color,1)
    n=next(n for n in m.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    n.inputs['Base Color'].default_value=(*color,1)
    n.inputs['Metallic'].default_value=metal
    n.inputs['Roughness'].default_value=rough
    return m
red=material('BC_RedGrip',(0.145,0.0065,0.012),0.12,0.36)
silver=material('BC_Silver',(0.55,0.59,0.64),1,0.285)
def lathe(name,profile,mat,kind,capindex,col=source):
    N=48
    verts=[(x,r*math.cos(2*math.pi*i/N),r*math.sin(2*math.pi*i/N)) for x,r in profile for i in range(N)]
    faces=[]; coords=[]
    def uv(x,t):
        if kind=='grip': return (0.03+0.94*(x+0.294)/0.444,0.03+0.23*t)
        if kind=='metal': return (0.03+0.94*(x-0.15)/0.15,0.38+0.23*t)
        return (0.03+0.03*(x+0.30)/0.006,0.70+0.23*t)
    for j in range(len(profile)-1):
        for i in range(N):
            ni=(i+1)%N
            faces.append((j*N+i,j*N+ni,(j+1)*N+ni,(j+1)*N+i))
            coords.append([uv(profile[j][0],i/N),uv(profile[j][0],(i+1)/N),uv(profile[j+1][0],(i+1)/N),uv(profile[j+1][0],i/N)])
    for end in [0,len(profile)-1]:
        indices=list(range(end*N,(end+1)*N))
        if end==0: indices.reverse()
        faces.append(indices)
        cx=0.16+capindex*0.20+(0.09 if end else 0)
        coords.append([(cx+0.038*math.cos(2*math.pi*(i%N)/N),0.81+0.038*math.sin(2*math.pi*(i%N)/N)) for i in indices])
    mesh=bpy.data.meshes.new(name+'_Mesh')
    mesh.from_pydata(verts,[],faces); mesh.update()
    layer=mesh.uv_layers.new(name='BC_Atlas')
    for poly,uvs in zip(mesh.polygons,coords):
        poly.use_smooth=len(poly.vertices)==4
        for li,co in zip(poly.loop_indices,uvs): layer.data[li].uv=co
    ob=bpy.data.objects.new(name,mesh); col.objects.link(ob); mesh.materials.append(mat)
    return ob
cap=lathe('BC_GripCap',[(-.3,.0155),(-.2997,.0162),(-.2992,.0167),(-.2948,.0167),(-.294,.0162)],silver,'cap',0)
grip=lathe('BC_Grip',[(-.294,.0162),(-.2935,.017),(-.1185,.017),(-.118,.0167),(-.1175,.0162),(-.1165,.0162),(-.116,.0167),(-.1155,.017),(.1495,.017),(.15,.0165)],red,'grip',1)
profile=[(.15,.0165),(.1505,.0174),(.155,.0174),(.1556,.017)]
for x in [.159,.170,.218,.229,.276,.286]:
    profile += [(x-.0007,.017),(x-.00035,.01665),(x+.00035,.01665),(x+.0007,.017)]
profile +=[(.2985,.017),(.2993,.0167),(.3,.0157)]
tip=lathe('BC_MetalTip',profile,silver,'metal',2)
# Exactly periodic crossed groove families, calibrated in physical metres.
nt=red.node_tree; ns=nt.nodes; links=nt.links
tex=ns.new('ShaderNodeTexCoord'); sep=ns.new('ShaderNodeSeparateXYZ'); links.new(tex.outputs['UV'],sep.inputs[0])
def calc(op,a,b=None):
    n=ns.new('ShaderNodeMath'); n.operation=op
    if isinstance(a,(int,float)): n.inputs[0].default_value=a
    else: links.new(a,n.inputs[0])
    if b is not None:
        if isinstance(b,(int,float)): n.inputs[1].default_value=b
        else: links.new(b,n.inputs[1])
    return n.outputs[0]
# 80 complete cycles around the 106.8 mm circumference, 1.335 mm axial pitch.
u=calc('MULTIPLY',calc('SUBTRACT',sep.outputs['X'],.03),.444/.94)
v=calc('MULTIPLY',calc('SUBTRACT',sep.outputs['Y'],.03),80/.23)
a=calc('MULTIPLY',u,80/(2*math.pi*.017))
waves=[]
for op in ['ADD','SUBTRACT']:
    phase=calc('MULTIPLY',calc(op,a,v),2*math.pi)
    waves.append(calc('POWER',calc('ADD',calc('MULTIPLY',calc('COSINE',phase),.5),.5),.35))
height=calc('MULTIPLY',waves[0],waves[1])
bump=ns.new('ShaderNodeBump'); bump.name='BC_FineDiamondBump'
bump.inputs['Strength'].default_value=.32; bump.inputs['Distance'].default_value=.000035
links.new(height,bump.inputs['Height'])
bsdf=next(n for n in ns if n.type=='BSDF_PRINCIPLED'); links.new(bump.outputs['Normal'],bsdf.inputs['Normal'])
rough=calc('ADD',calc('MULTIPLY',height,.035),.34); links.new(rough,bsdf.inputs['Roughness'])
red['pattern_circumference_cycles']=80; red['pattern_pitch_mm']=2*math.pi*17/80
red['surface']='burgundy painted coating, low metallic response'
# Small sample with identical physical mapping; parked outside the export collection.
sample=lathe('BC_GripTest',[(-.08,.017),(-.02,.017)],red,'grip',3,tests)
sample.location=(0,0,.1); sample.hide_render=True; sample.hide_set(True)
# Camera and broad neutral area lights.
def aim(ob,point): ob.rotation_euler=(Vector(point)-ob.location).to_track_quat('-Z','Y').to_euler()
camdata=bpy.data.cameras.new('BC_Camera'); cam=bpy.data.objects.new('BC_Camera',camdata); studio.objects.link(cam)
cam.location=(.31,-.86,.46); aim(cam,(0,0,0)); camdata.type='ORTHO'; camdata.ortho_scale=.74; camdata.clip_start=.001
scene.camera=cam
def light(name,loc,power,size):
    d=bpy.data.lights.new(name,'AREA'); d.energy=power; d.size=size
    o=bpy.data.objects.new(name,d); studio.objects.link(o); o.location=loc; aim(o,(0,0,0))
light('BC_Key',(-.15,-.30,.45),22,.45)
light('BC_Rim',(.1,.20,.3),28,.35)
light('BC_Fill',(.2,-.2,-.15),8,.35)
if not scene.world: scene.world=bpy.data.worlds.new('BC_World')
scene.world.color=(.055,.055,.055)
scene.render.resolution_x=1600; scene.render.resolution_y=700; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.render.filepath=root+'previews/01_blockout.png'
for area in bpy.context.screen.areas:
    if area.type=='VIEW_3D':
        area.spaces.active.region_3d.view_rotation=cam.rotation_euler.to_quaternion()
        area.spaces.active.region_3d.view_distance=.8
        area.spaces.active.region_3d.view_location=(0,0,0)
        area.spaces.active.clip_start=.001
        area.spaces.active.shading.type='MATERIAL'
bpy.ops.wm.save_as_mainfile(filepath=root+'billy_club.blend')
print('SOURCE',[(o.name,len(o.data.polygons)) for o in [grip,tip,cap]])
