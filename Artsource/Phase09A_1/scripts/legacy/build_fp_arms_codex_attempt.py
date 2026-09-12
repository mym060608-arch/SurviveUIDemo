import bpy, math, os, json
from mathutils import Vector, Matrix, Quaternion
BASE=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
for folder in ('output','validation'): os.makedirs(os.path.join(BASE,folder),exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
scene=bpy.context.scene; scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
scene.render.fps=30
def material(name,color,metal=0,rough=.5,emit=0):
 m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
 p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*color,1); p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
 if emit: p.inputs['Emission Color'].default_value=(*color,1); p.inputs['Emission Strength'].default_value=emit
 return m
glove=material('MAT_Glove',(.055,.075,.085),.05,.63)
sleeve=material('MAT_Sleeve',(.26,.32,.34),.08,.7)
body=material('MAT_DeviceBody',(.66,.70,.70),.45,.32)
accent=material('MAT_DeviceAccent',(.9,.22,.025),.2,.4)
emissive=material('MAT_DeviceEmissive',(.02,.46,.57),.25,.28,1.2)
root=bpy.data.objects.new('SK_FPArms',None); scene.collection.objects.link(root)
armdata=bpy.data.armatures.new('FPArms_Skeleton'); rig=bpy.data.objects.new('RIG_FPArms',armdata); scene.collection.objects.link(rig); rig.parent=root; rig.show_in_front=True
bpy.context.view_layer.objects.active=rig; rig.select_set(True); bpy.ops.object.mode_set(mode='EDIT')
def bone(n,h,t,parent=None):
 b=armdata.edit_bones.new(n); b.head=h; b.tail=t
 if parent: b.parent=armdata.edit_bones[parent]
 return b
bone('Root',(0,0,0),(0,0,.08))
fingerinfo={}
for side,sign in [('L',-1),('R',1)]:
 x=sign*.32
 bone('Arm_'+side,(x,0,0),(x,.08,0),'Root')
 bone('Forearm_'+side,(x,0,0),(x,.30,0),'Arm_'+side)
 bone('Hand_'+side,(x,.30,0),(x,.395,0),'Forearm_'+side)
 for i,(name,length) in enumerate([('Index',.078),('Middle',.087),('Ring',.080),('Pinky',.062)]):
  fx=x+(-sign)*(.033-i*.022); y=.387
  h=Vector((fx,y,0)); mid=h+Vector((0,length*.55,0)); end=h+Vector((0,length,0))
  bone(name+'_01_'+side,h,mid,'Hand_'+side); bone(name+'_02_'+side,mid,end,name+'_01_'+side)
  fingerinfo[name+'_'+side]=(h,mid,end)
 h=Vector((x-sign*.042,.327,-.008)); mid=h+Vector((-sign*.029,.028,-.004)); end=mid+Vector((-sign*.012,.025,0))
 bone('Thumb_01_'+side,h,mid,'Hand_'+side); bone('Thumb_02_'+side,mid,end,'Thumb_01_'+side); fingerinfo['Thumb_'+side]=(h,mid,end)
bone('WristSocket_L',(-.32,.235,.077),(-.32,.235,.11),'Forearm_L')
bpy.ops.object.mode_set(mode='OBJECT'); rig.select_set(False)
groups={}
def mesh(name,verts,faces,mat,weights,group,bevel=0):
 me=bpy.data.meshes.new(name); me.from_pydata(verts,[],faces); me.update(); o=bpy.data.objects.new(name,me); scene.collection.objects.link(o); o.data.materials.append(mat)
 for bn,items in weights.items():
  vg=o.vertex_groups.new(name=bn)
  for idx,w in items: vg.add([idx],w,'REPLACE')
 if bevel:
  mod=o.modifiers.new('Small manufactured bevel','BEVEL'); mod.width=bevel; mod.segments=2
  bpy.context.view_layer.objects.active=o; o.select_set(True); bpy.ops.object.modifier_apply(modifier=mod.name); o.select_set(False)
 groups.setdefault(group,[]).append(o)
 return o
def tube(name,stations,mat,bn,group,n=10):
 # station = centre, x-radius, z-radius; continuous low-poly rings.
 vs=[]
 for p,rx,rz in stations:
  vs.extend([(p[0]+rx*math.cos(i*2*math.pi/n),p[1],p[2]+rz*math.sin(i*2*math.pi/n)) for i in range(n)])
 fs=[tuple(reversed(range(n)))]
 for j in range(len(stations)-1):
  for i in range(n): fs.append((j*n+i,j*n+(i+1)%n,(j+1)*n+(i+1)%n,(j+1)*n+i))
 fs.append(tuple((len(stations)-1)*n+i for i in range(n)))
 return mesh(name,vs,fs,mat,{bn:[(i,1) for i in range(len(vs))]},group)
def cube(name,loc,scale,mat,bn,group,bevel=.003):
 x,y,z=loc; a,b,c=[v/2 for v in scale]
 vs=[(x+sx*a,y+sy*b,z+sz*c) for sx,sy,sz in [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),(-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
 fs=[(3,2,1,0),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
 return mesh(name,vs,fs,mat,{bn:[(i,1) for i in range(8)]},group,bevel)
for side,sign in [('L',-1),('R',1)]:
 x=sign*.32; bn='Forearm_'+side; ag='GEO_Arm_'+side; hg='GEO_Hand_'+side
 tube('Sleeve_'+side,[((x,y,0),r,r*.86) for y,r in [(-.06,.069),(0,.071),(.08,.064),(.19,.052),(.27,.043),(.30,.04)]],sleeve,bn,ag,12)
 for y in [.035,.258]: tube('SleeveBand_'+side,[((x,y-.008,0),.060 if y<.1 else .047,.055 if y<.1 else .041),((x,y+.008,0),.060 if y<.1 else .047,.055 if y<.1 else .041)],glove,bn,ag,12)
 cube('ForearmPlate_'+side,(x,.11,.052),(.085,.13,.015),body,bn,ag,.009)
 cube('SleeveMark_'+side,(x+.025,.105,.062),(.009,.07,.004),accent,bn,ag,.001)
 tube('GloveCuff_'+side,[((x,.273,0),.045,.038),((x,.305,0),.043,.035)],glove,bn,hg,12)
 palm=tube('Palm_'+side,[((x,.298,0),.037,.028),((x,.326,0),.048,.030),((x,.375,0),.049,.027),((x,.401,0),.043,.023)],glove,'Hand_'+side,hg,12)
 cube('HandGuard_'+side,(x,.352,.027),(.072,.067,.013),sleeve,'Hand_'+side,hg,.01)
 for name in ['Thumb','Index','Middle','Ring','Pinky']:
  h,mid,end=fingerinfo[name+'_'+side]; r=.012 if name!='Thumb' else .015
  # Two connected glove segments weighted smoothly at the only bend seam.
  direction=(end-h).normalized(); cross=Vector((direction.y,-direction.x,0)).normalized(); up=direction.cross(cross).normalized()
  points=[h-direction*.008,h.lerp(mid,.75),mid,mid.lerp(end,.25),end-direction*.008,end]
  radii=[r,r*.96,r*.90,r*.88,r*.75,r*.48]; vs=[]; weights={name+'_01_'+side:[],name+'_02_'+side:[]}
  for j,p in enumerate(points):
   for k in range(8):
    vs.append(p+radii[j]*(cross*math.cos(k*math.tau/8)+up*math.sin(k*math.tau/8)))
    w=[0,0,.5,1,1,1][j]; idx=len(vs)-1
    if w<1: weights[name+'_01_'+side].append((idx,1-w))
    if w>0: weights[name+'_02_'+side].append((idx,w))
  fs=[tuple(reversed(range(8)))]
  for j in range(5):
   for k in range(8): fs.append((j*8+k,j*8+(k+1)%8,(j+1)*8+(k+1)%8,(j+1)*8+k))
  fs.append(tuple(40+k for k in range(8)))
  mesh(name+'Glove_'+side,vs,fs,glove,weights,hg)
  if name!='Thumb': cube(name+'Knuckle_'+side,(h.x,h.y+.007,.013),(.017,.021,.011),sleeve,name+'_01_'+side,hg,.004)
# Compact terminal; its face centre is also the alignment socket.
x=-.32; bn='Forearm_L'; dg='GEO_WristDevice_L'
tube('TerminalStrap',[((x,.192,0),.055,.052),((x,.270,0),.049,.049)],glove,bn,dg,12)
cube('TerminalBase',(x,.235,.047),(.107,.109,.027),body,bn,dg,.011)
def annulus(name,ri,ro,z,depth,mat,start=0,end=math.tau):
 n=max(3,round(32*(end-start)/math.tau)); vs=[]
 for zz,rr in [(z-depth,ro),(z,ro),(z,ri),(z-depth,ri)]:
  for i in range(n+1):
   a=start+(end-start)*i/n; vs.append((x+rr*math.cos(a),.235+rr*math.sin(a),zz))
 fs=[]
 for j in range(3):
  for i in range(n): fs.append((j*(n+1)+i,j*(n+1)+i+1,(j+1)*(n+1)+i+1,(j+1)*(n+1)+i))
 for i in range(n): fs.append((3*(n+1)+i,3*(n+1)+i+1,i+1,i))
 return mesh(name,vs,fs,mat,{bn:[(i,1) for i in range(len(vs))]},dg)
annulus('TerminalBezel',.033,.051,.078,.018,body)
annulus('FaceGlass',0,.033,.077,.005,glove)
for a in [0,math.pi/2,math.pi,3*math.pi/2]:
 annulus('StatusArc',.042,.045,.079,.001,emissive,a+.14,a+.95)
 cube('BezelSupport',(x+.050*math.cos(a),.235+.050*math.sin(a),.078),(.016,.017,.010),body,bn,dg,.002)
annulus('OrangeArc',.047,.050,.079,.001,accent,3.45,4.1)
annulus('FaceReticle',.013,.014,.0775,.001,emissive)
cube('FaceCentre',(x,.235,.0775),(.006,.006,.001),emissive,bn,dg,.001)
# Join into five clean skinned objects; modifier order keeps deformation predictable.
for name,obs in groups.items():
 bpy.ops.object.select_all(action='DESELECT')
 for o in obs: o.select_set(True)
 bpy.context.view_layer.objects.active=obs[0]; bpy.ops.object.join(); o=bpy.context.object; o.name=name; o.parent=rig
 mod=o.modifiers.new('FPArms skin','ARMATURE'); mod.object=rig
 o.modifiers.new('Weighted surface normals','WEIGHTED_NORMAL')
 for p in o.data.polygons: p.use_smooth=False
rig.animation_data_create()
P=Vector((-.265,.69,-.335))
def basis(forward,up):
 y=Vector(forward).normalized(); z=Vector(up); z=(z-y*z.dot(y)).normalized(); x=y.cross(z).normalized()
 return Matrix((x,y,z)).transposed().to_quaternion()
idle_q=basis((0,.85,-.28),(0,.2,1))
hold_q=basis((.93,.16,.30),(0,-.65,.76))
held_elbow=P-(hold_q @ Vector((0,.235,.077)))
idle_elbows={'L':Vector((-.29,.12,-.40)),'R':Vector((.29,.12,-.40))}
clips={'FP_Idle':(61,'loop'),'FP_Walk':(31,'loop'),'Wrist_Raise':(19,'once'),'Wrist_Idle':(61,'loop'),'Wrist_Lower':(16,'once')}
def set_pose(raise01=0,phase=0,walk=False,hold=False):
 for p in rig.pose.bones: p.matrix_basis=Matrix.Identity(4); p.rotation_mode='QUATERNION'
 for side in ['L','R']:
  p=rig.pose.bones['Arm_'+side]; a=raise01 if side=='L' else 0
  q=idle_q.slerp(hold_q,a); loc=idle_elbows[side].lerp(held_elbow,a)
  if hold and side=='L':
   q=hold_q @ Quaternion((0,1,0),math.sin(phase)*.006); loc=P-q@Vector((0,.235,.077))
  else:
   amount=.018 if walk else .0015; sign=1 if side=='L' else -1
   loc+=Vector((0,math.sin(phase)*amount*sign,math.sin(phase*2)*amount*.3))*(1-a)
   if walk: q=q @ Quaternion((1,0,0),math.sin(phase)*.045*sign)
  p.matrix=Matrix.Translation(loc)@q.to_matrix().to_4x4()
 for side in ['L','R']:
  for name in ['Thumb','Index','Middle','Ring','Pinky']:
   for seg in ['01','02']:
    rig.pose.bones[name+'_'+seg+'_'+side].rotation_quaternion=Quaternion((1,0,0),-.16 if seg=='01' else -.38)
 bpy.context.view_layer.update()
for name,(end,mode) in clips.items():
 act=bpy.data.actions.new(name); act.use_fake_user=True; rig.animation_data.action=act
 for f in range(1,end+1):
  t=(f-1)/(end-1); smooth=t*t*(3-2*t)
  a=smooth if name=='Wrist_Raise' else 1-smooth if name=='Wrist_Lower' else 1 if name=='Wrist_Idle' else 0
  set_pose(a,math.tau*t,name=='FP_Walk',name=='Wrist_Idle')
  for p in rig.pose.bones:
   p.keyframe_insert('location',frame=f,group=p.name); p.keyframe_insert('rotation_quaternion',frame=f,group=p.name); p.keyframe_insert('scale',frame=f,group=p.name)
 act['usage']=mode; act['duration_seconds']=(end-1)/30
rig.animation_data.action=bpy.data.actions['Wrist_Idle']; scene.frame_set(1)
# Non-exported camera and exact fixed UI diagnostic geometry.
scene.render.engine='CYCLES'; scene.cycles.samples=32; scene.render.resolution_x=1280; scene.render.resolution_y=720; scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('NeutralStudio'); scene.world.use_nodes=True; scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.04,.055,.073,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=.45
scene.view_settings.view_transform='AgX'
camdata=bpy.data.cameras.new('FP_Camera_70deg'); cam=bpy.data.objects.new('FP_Camera_70deg',camdata); scene.collection.objects.link(cam); cam.rotation_euler=(math.pi/2,0,0); camdata.type='PERSP'; camdata.sensor_fit='VERTICAL'; camdata.sensor_height=24; camdata.lens=12/math.tan(math.radians(35)); camdata.clip_start=.01; scene.camera=cam
def light(n,pos,power,size):
 d=bpy.data.lights.new(n,'AREA'); d.energy=power; d.shape='DISK'; d.size=size; o=bpy.data.objects.new(n,d); scene.collection.objects.link(o); o.location=pos; o.rotation_euler=(Vector((0,.5,-.3))-o.location).to_track_quat('-Z','Y').to_euler()
light('Key',(.2,.35,.55),55,.8); light('Fill',(-.8,.7,.05),35,.7); light('Rim',(.5,1,-.2),40,.5)
def render(name,action,frame=1,down=0):
 rig.animation_data.action=bpy.data.actions[action]; scene.frame_set(frame); cam.rotation_euler=(math.pi/2+math.radians(down),0,0)
 # Looking down rotates around camera-local X in the negative world direction.
 cam.rotation_euler.x=math.pi/2-math.radians(down)
 scene.render.filepath=os.path.join(BASE,'validation',name+'.png'); bpy.ops.render.render(write_still=True)
render('fp_idle','FP_Idle'); render('fp_walk','FP_Walk',9); render('wrist_raise','Wrist_Raise',13); render('wrist_idle','Wrist_Idle')
render('fp_idle_lookdown','FP_Idle',1,32); render('fp_walk_lookdown','FP_Walk',9,32)
# Reference outline only: uses the shipped pivot, display offset/scale and runtime tilt.
def unity_to_b(v): return Vector((v[0],v[2],v[1]))
uP=Vector((-.265,-.335,.69)); uf=uP.normalized(); ur=Vector((0,1,0)).cross(uf).normalized(); uu=uf.cross(ur)
look=Matrix((ur,uu,uf)).transposed()
tilt=Quaternion((0,1,0),math.radians(-4)).to_matrix()@Quaternion((1,0,0),math.radians(8)).to_matrix()
rotation=look@tilt
def ui_point(x,y): return unity_to_b(uP+rotation@Vector((x+.21,y+.31,0)))
guide_mat=material('VALIDATION_ReferenceGuide',(.10,.48,.53),0,.5,1)
guides=[]
def curve(name,pts):
 c=bpy.data.curves.new(name,'CURVE'); c.dimensions='3D'; c.bevel_depth=.0008; c.bevel_resolution=0; s=c.splines.new('POLY'); s.points.add(len(pts)-1)
 for p,v in zip(s.points,pts): p.co=(*v,1)
 o=bpy.data.objects.new(name,c); scene.collection.objects.link(o); o.data.materials.append(guide_mat); guides.append(o)
curve('REFERENCE_MapBounds',[ui_point(math.cos(i*math.tau/96)*235/1280*1.88,math.sin(i*math.tau/96)*235/800*1.175) for i in range(97)])
curve('REFERENCE_RadialExtent',[ui_point(math.cos(i*math.tau/96)*365/1280*1.88,math.sin(i*math.tau/96)*365/800*1.175) for i in range(97)])
curve('REFERENCE_FixedPivot',[P+Vector((-.015,0,0)),P+Vector((.015,0,0))])
render('wrist_alignment_guide','Wrist_Idle')
for o in guides: o.hide_render=True; o.hide_set(True)
render('wrist_lower','Wrist_Lower',16)
rig.animation_data.action=bpy.data.actions['Wrist_Idle']; scene.frame_set(1); cam.rotation_euler=(math.pi/2,0,0)
scene.frame_end=61
bpy.ops.object.select_all(action='DESELECT'); root.select_set(True); rig.select_set(True)
for o in rig.children: o.select_set(True)
bpy.context.view_layer.objects.active=rig
# NLA strip names become exact FBX take names, with independent bounded clips.
rig.animation_data.action=None
for name,(end,mode) in clips.items():
 track=rig.animation_data.nla_tracks.new(); track.name=name; strip=track.strips.new(name,1,bpy.data.actions[name]); strip.action_frame_start=1; strip.action_frame_end=end; track.mute=False
bpy.ops.export_scene.fbx(filepath=os.path.join(BASE,'output','FP_PlayerArms.fbx'),use_selection=True,object_types={'MESH','ARMATURE','EMPTY'},axis_forward='-Z',axis_up='Y',apply_unit_scale=True,add_leaf_bones=False,use_armature_deform_only=False,bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=True,bake_anim_simplify_factor=0,bake_anim_step=1)
for track in rig.animation_data.nla_tracks: track.mute=True
rig.animation_data.action=bpy.data.actions['Wrist_Idle']; scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(BASE,'output','FP_PlayerArms.blend'))
print('ASSET_BUILD_COMPLETE')

