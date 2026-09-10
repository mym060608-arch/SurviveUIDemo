import bpy, math, os, json
from mathutils import Vector
BASE=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
for d in ['output','validation']: os.makedirs(os.path.join(BASE,d),exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
def mat(n,c,metal=.0,rough=.4):
 m=bpy.data.materials.new(n); m.diffuse_color=(*c,1); m.use_nodes=True
 p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*c,1); p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
 return m
hull=mat('MAT_Hull',(.66,.69,.70),.38,.32)
secondary=mat('MAT_Secondary',(.075,.09,.105),.65,.34)
glass=mat('MAT_Glass',(.012,.032,.043),.65,.19)
accent=mat('MAT_Accent',(.95,.235,.025),.25,.35)
engine=mat('MAT_Engine',(.012,.016,.022),.55,.48)
root=bpy.data.objects.new('SK_MainMenuShip',None); bpy.context.collection.objects.link(root)
root['orientation']='Front = -Y; rear/exhaust = +Y; top = +Z. VFX local +Z points aft.'
def mesh(n,v,f,m,bevel=.06):
 me=bpy.data.meshes.new(n); me.from_pydata(v,[],f); me.update(); o=bpy.data.objects.new(n,me); bpy.context.collection.objects.link(o); o.parent=root; o.data.materials.append(m)
 if bevel:
  b=o.modifiers.new('Manufactured edge radii','BEVEL'); b.width=bevel; b.segments=3
  o.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL')
 return o
# Octagonal stations: y, half-width, bottom, top. Few continuous manufactured volumes.
def ring(y,w,b,t,x=0):
 h=t-b
 return [(x-w*.70,y,t),(x+w*.70,y,t),(x+w,y,t-h*.23),(x+w,y,b+h*.23),(x+w*.70,y,b),(x-w*.70,y,b),(x-w,y,b+h*.23),(x-w,y,t-h*.23)]
def loft(n,stations,m,x=0,bevel=.10,cap=True,rear=True):
 v=[p for s in stations for p in ring(*s,x=x)]; f=[]
 for j in range(len(stations)-1):
  for i in range(8): f.append((j*8+i,j*8+(i+1)%8,(j+1)*8+(i+1)%8,(j+1)*8+i))
 if cap: f += [tuple(reversed(range(8)))]
 if rear: f += [tuple((len(stations)-1)*8+i for i in range(8))]
 return mesh(n,v,f,m,bevel)
def box(n,loc,scale,m,b=.04):
 bpy.ops.mesh.primitive_cube_add(size=1,location=loc); o=bpy.context.object; o.name=n; o.dimensions=scale; bpy.ops.object.transform_apply(location=False,rotation=False,scale=True); o.parent=root; o.data.materials.append(m)
 if b:
  mod=o.modifiers.new('Soft edges','BEVEL'); mod.width=b; mod.segments=3; o.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
 return o
stations=[(-12,1.05,-.55,.65),(-9.3,2.15,-1.1,1.85),(-6.6,3.12,-1.65,2.75),(-3.2,3.48,-1.85,3.0),(6.8,3.48,-1.85,3.0),(9.9,2.75,-1.55,2.35),(10.7,2.2,-1.15,1.65)]
loft('GEO_Hull',stations,hull)
loft('GEO_Belly',[(-10.8,1.35,-.85,-.48),(-6.4,2.85,-1.95,-.65),(6.8,3.15,-2.02,-.55),(10,2.3,-1.6,-.5)],secondary,bevel=.13)
for side,label in [(-1,'L'),(1,'R')]:
 # Narrow joining shoulder, behind the cockpit; compact engine housings.
 loft('GEO_Shoulder_'+label,[(-4.2,.12,-.55,1.1),(-1.5,.80,-1.15,2.0),(4.8,.82,-1.3,2.25),(8.7,.55,-.95,1.75)],hull,x=side*3.65)
 loft('GEO_Thruster_'+label,[(1.0,.80,-1.1,2.1),(2.15,1.25,-1.45,2.6),(7.7,1.25,-1.45,2.6),(9.7,1.03,-1.12,2.18),(10.35,1.03,-1.12,2.18)],hull,x=side*4.65,rear=False)
 loft('GEO_EngineCollar_'+label,[(.8,.79,-1.14,2.12),(2.05,1.21,-1.43,2.57),(2.5,1.23,-1.44,2.58)],secondary,x=side*4.65,bevel=.04)
 # Actual open annular cavity, with inner walls reaching a dark recessed back plate.
 x=side*4.65; z=.53
 def nozzle_ring(y,rx,rz):
  return [(x+math.sin(2*math.pi*i/8+math.pi/8)*rx,y,z+math.cos(2*math.pi*i/8+math.pi/8)*rz) for i in range(8)]
 v=nozzle_ring(10.40,1.04,1.65)+nozzle_ring(10.43,.77,1.25)+nozzle_ring(9.74,.69,1.10)
 f=[]
 for j in range(2):
  for i in range(8): f.append((j*8+i,j*8+(i+1)%8,(j+1)*8+(i+1)%8,(j+1)*8+i))
 mesh('GEO_ExhaustRecess_'+label,v,f,secondary,.035)
 mesh('GEO_ExhaustDark_'+label,nozzle_ring(9.72,.7,1.12),[tuple(range(8))],engine,0)
 # Forward shell cap remains closed; only rear has exhaust.
 rv=nozzle_ring(9.92,.70,1.12)+nozzle_ring(9.94,.58,.94)
 mesh('GEO_ExhaustInnerRing_'+label,rv,[(i,(i+1)%8,8+(i+1)%8,8+i) for i in range(8)],secondary,.018)
 a=bpy.data.objects.new('VFX_Thruster_'+label,None); bpy.context.collection.objects.link(a); a.parent=root; a.location=(x,10.5,z); a.rotation_euler=(-math.pi/2,0,0); a.empty_display_type='ARROWS'; a.empty_display_size=.65
 a['exhaust_direction']='local +Z / ship +Y'
 box('GEO_OrangeEngine_'+label,(x,5.8,2.615),(1.65,.27,.022),accent,.01)
 box('GEO_EngineAccess_'+label,(x,7.5,2.615),(1.30,1.3,.045),secondary,.06)
 for j in range(5): box('GEO_Vent_'+label+str(j),(x,7.03+j*.22,2.648),(1.05,.08,.04),engine,.01)
 # Large side panels avoid excessive subdivision.
 box('GEO_SidePanel_'+label,(side*3.49,3.0,.5),(.07,6.9,2.4),hull,.13)
 box('GEO_Warning_'+label,(side*3.535,5.5,.70),(.025,.65,.15),accent,.015)
# Glazing follows the upper sloping cockpit and side chamfers, visibly separated by hull framing.
def hull_at(y):
 for a,b in zip(stations,stations[1:]):
  if a[0]<=y<=b[0]:
   t=(y-a[0])/(b[0]-a[0]); return tuple(a[i]+t*(b[i]-a[i]) for i in [1,2,3])
ys=[-11.65,-9.3,-8.05]
v=[]
for y in ys:
 w,b,t=hull_at(y); v.extend([(-w*.64,y,t+.045),(w*.64,y,t+.045)])
mesh('GEO_CockpitGlass',v,[(0,1,3,2),(2,3,5,4)],glass,0)
for s,label in [(-1,'L'),(1,'R')]:
 v=[]
 for y in ys:
  w,b,t=hull_at(y); v.extend([(s*w*.735,y,t-(t-b)*.025+.025),(s*w*.965,y,t-(t-b)*.203+.025)])
 mesh('GEO_CockpitGlass_'+label,v,[(0,1,3,2),(2,3,5,4)],glass,0)
 box('GEO_EngineSidePanel_'+label,(s*5.895,5.1,.57),(.045,4.9,1.9),hull,.09)
 box('GEO_EngineSideMark_'+label,(s*5.925,6.9,.75),(.022,.25,.65),accent,.015)
 # Accent on forward shoulder.
 mesh('GEO_OrangeShoulder_'+label,[(s*3.36,-3.8,2.37),(s*3.33,-4.12,2.32),(s*4.15,-1.7,1.86),(s*4.21,-1.37,1.91)],[(0,1,2,3)],accent,.01)
box('GEO_DorsalPanel',(0,3.0,3.035),(4.75,6.9,.065),hull,.10)
box('GEO_DorsalSeam',(0,-1.05,3.022),(4.76,.10,.026),secondary,.008)
box('GEO_RearAccess',(0,8.28,2.697),(2.5,1.05,.055),secondary,.08).rotation_euler.x=math.radians(-12)
for s in [-1,1]: box('GEO_NoseLight',(s*.77,-11.99,.12),(.3,.04,.12),hull,.02)
# Studio cameras and illumination stay outside exported hierarchy.
scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=32
scene.render.resolution_x=1100; scene.render.resolution_y=850; scene.render.resolution_percentage=100
scene.world.color=(.16,.16,.16)
bpy.context.preferences.filepaths.save_version=0
scene.view_settings.view_transform='AgX'
def aim(o,pt): o.rotation_euler=(Vector(pt)-o.location).to_track_quat('-Z','Y').to_euler()
def light(n,pos,power,size):
 d=bpy.data.lights.new(n,'AREA'); d.energy=power; d.shape='DISK'; d.size=size; o=bpy.data.objects.new(n,d); bpy.context.collection.objects.link(o); o.location=pos; aim(o,(0,0,0))
light('Studio_Key',(4,-10,18),4200,12); light('Studio_Fill',(-13,-4,9),3000,10); light('Studio_Rim',(7,14,13),5000,9)
def camera(n,pos,ortho=None):
 d=bpy.data.cameras.new(n); o=bpy.data.objects.new(n,d); bpy.context.collection.objects.link(o); o.location=pos; aim(o,(0,-.5,.45))
 if ortho: d.type='ORTHO'; d.ortho_scale=ortho
 else: d.lens=48
 return o
cams={'top':camera('CAM_TOP',(0,-.5,40),28),'side':camera('CAM_SIDE',(40,-.5,.45),28),'front':camera('CAM_FRONT',(0,-40,.45),17),'beauty':camera('CAM_BEAUTY',(24,-31,22))}
# Top uses nose to the right, matching the reference sheet.
cams['top'].rotation_euler=(0,0,-math.pi/2)
cams['side'].location.x=-40; aim(cams['side'],(0,-.5,.45))
cams['rear']=camera('CAM_REAR',(0,40,.45),17)
def render(name):
 scene.camera=cams[name]; scene.render.filepath=os.path.join(BASE,'validation',name+'.png'); bpy.ops.render.render(write_still=True)
for name in ['top','side','front','beauty','rear']: render(name)
scene.camera=cams['beauty']
bpy.ops.object.select_all(action='DESELECT'); root.select_set(True)
for o in root.children: o.select_set(True)
bpy.context.view_layer.objects.active=root
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BASE,'output','MainMenuShip.blend'))
bpy.ops.export_scene.fbx(filepath=os.path.join(BASE,'output','MainMenuShip.fbx'),use_selection=True,object_types={'MESH','EMPTY'},apply_unit_scale=True,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False)
report={'mesh_count':sum(o.type=='MESH' for o in root.children),'anchors':[o.name for o in root.children if o.name.startswith('VFX_')],'orientation':root['orientation'],'dimensions_guidance':'24m length / 11.8m width / 5.0m hull height; reference proportions prioritized'}
with open(os.path.join(BASE,'validation','asset_checks.json'),'w') as f: json.dump(report,f,indent=2)
