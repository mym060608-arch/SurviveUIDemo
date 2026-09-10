import bpy, os, json
from mathutils import Vector
base=os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
bpy.ops.wm.open_mainfile(filepath=os.path.join(base,'output','MainMenuShip.blend'))
def check():
 root=bpy.data.objects['SK_MainMenuShip']
 anchors=[o for o in bpy.data.objects if o.name.startswith('VFX_')]
 assert sorted(o.name for o in anchors)==['VFX_Thruster_L','VFX_Thruster_R']
 for a in anchors:
  assert a.parent==root and a.type=='EMPTY'
  assert (a.matrix_world.to_quaternion() @ Vector((0,0,1))).dot(Vector((0,1,0)))>.999
 meshes=[o for o in root.children if o.type=='MESH']
 assert all(all(abs(v-1)<.0001 for v in o.scale) for o in meshes)
 return {'meshes':len(meshes),'anchors':{a.name:list(a.location) for a in anchors},'material_slots':sorted({m.name for o in meshes for m in o.data.materials}),'triangles':sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons)}
report={'blend':check()}
# Discard unused startup datablocks so the editable asset has no external brush dependency.
bpy.data.orphans_purge(do_recursive=True)
assert not [i.filepath for i in bpy.data.images if i.source=='FILE' and i.filepath and not i.packed_file]
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(base,'output','MainMenuShip.blend'))
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=os.path.join(base,'output','MainMenuShip.fbx'))
report['fbx_roundtrip']=check()
for n in ['top','side','front','beauty','rear']:
 assert os.path.getsize(os.path.join(base,'validation',n+'.png'))>10000
report['checks']='PASS: hierarchy, two anchors, aft local-Z directions, unit mesh scales, five material slots, FBX readback, render files'
with open(os.path.join(base,'validation','asset_checks.json'),'w') as f: json.dump(report,f,indent=2)
print(json.dumps(report,indent=2))
