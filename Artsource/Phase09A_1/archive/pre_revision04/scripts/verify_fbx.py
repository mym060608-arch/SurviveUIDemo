"""Re-import an exported FBX into a clean Blender scene and report its real contents.

Usage:
    blender --background --python verify_fbx.py -- <path-to-fbx> [report.json]

Reads back exactly what Unity's importer will see:
  * object names / hierarchy / emptiness
  * armature bone names and hierarchy
  * per-take frame ranges, FPS and F-curve counts
  * raw FBX-space vertex bounds, so the Blender -> FBX axis mapping is MEASURED
    instead of assumed (import uses axis_forward='Y', axis_up='Z', i.e. the file
    is interpreted in its own axes with no conversion applied).
"""
import bpy, json, os, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if not argv:
    raise SystemExit("verify_fbx.py: missing fbx path")
fbx_path = argv[0]
report_path = argv[1] if len(argv) > 1 else None

# Read exact raw FBX object names before Blender's importer folds terminal
# finger bones into end points or prefixes imported actions with object names.
addons_root = os.path.join(bpy.utils.resource_path("LOCAL"), "scripts", "addons_core")
if addons_root not in sys.path:
    sys.path.insert(0, addons_root)
import io_scene_fbx.parse_fbx as parse_fbx  # noqa: E402

raw_root, raw_version = parse_fbx.parse(fbx_path)
raw_objects = next((elem for elem in raw_root.elems if elem.id == b"Objects"), None)


def raw_name(elem):
    value = elem.props[1] if len(elem.props) > 1 else b""
    if isinstance(value, bytes):
        return value.split(b"\x00\x01", 1)[0].decode("utf-8", "replace")
    return str(value)


raw_animation_stacks = []
raw_limb_nodes = []
if raw_objects is not None:
    for elem in raw_objects.elems:
        if elem.id == b"AnimationStack":
            raw_animation_stacks.append(raw_name(elem))
        elif elem.id == b"Model" and len(elem.props) > 2 and elem.props[2] == b"LimbNode":
            raw_limb_nodes.append(raw_name(elem))

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# Identity axis conversion: the file's own axes are read verbatim, so the numbers
# below are the numbers stored in the FBX container.
bpy.ops.import_scene.fbx(
    filepath=fbx_path,
    axis_forward="Y",
    axis_up="Z",
    automatic_bone_orientation=False,
    use_anim=True,
    ignore_leaf_bones=True,
    global_scale=1.0,
)

objects = []
bounds_min = Vector((1e9, 1e9, 1e9))
bounds_max = Vector((-1e9, -1e9, -1e9))
triangles = 0
for obj in scene.objects:
    entry = {
        "name": obj.name,
        "type": obj.type,
        "parent": obj.parent.name if obj.parent else None,
        "location": [round(v, 6) for v in obj.location],
        "scale": [round(v, 6) for v in obj.scale],
    }
    if obj.type == "MESH":
        entry["vertices"] = len(obj.data.vertices)
        entry["polygons"] = len(obj.data.polygons)
        entry["materials"] = [m.name for m in obj.data.materials]
        triangles += sum(len(p.vertices) - 2 for p in obj.data.polygons)
        for corner in obj.bound_box:
            world = obj.matrix_world @ Vector(corner)
            for i in range(3):
                bounds_min[i] = min(bounds_min[i], world[i])
                bounds_max[i] = max(bounds_max[i], world[i])
    if obj.type == "ARMATURE":
        entry["bones"] = [
            {
                "name": bone.name,
                "parent": bone.parent.name if bone.parent else None,
                "head": [round(v, 6) for v in bone.head_local],
                "tail": [round(v, 6) for v in bone.tail_local],
            }
            for bone in obj.data.bones
        ]
    objects.append(entry)

actions = []
for action in bpy.data.actions:
    start, end = action.frame_range
    fcurves = 0
    for layer in action.layers:
        for strip in layer.strips:
            for slot in action.slots:
                bag = strip.channelbag(slot)
                if bag:
                    fcurves += len(bag.fcurves)
    actions.append({
        "name": action.name,
        "frame_start": round(start, 4),
        "frame_end": round(end, 4),
        "fcurves": fcurves,
    })

report = {
    "fbx": os.path.abspath(fbx_path),
    "raw_fbx_version": raw_version,
    "raw_animation_stacks": raw_animation_stacks,
    "raw_limb_nodes": raw_limb_nodes,
    "size_bytes": os.path.getsize(fbx_path),
    "scene_fps": scene.render.fps,
    "objects": objects,
    "actions": sorted(actions, key=lambda a: a["name"]),
    "triangles_total": triangles,
    "fbx_space_bounds_min": [round(v, 6) for v in bounds_min],
    "fbx_space_bounds_max": [round(v, 6) for v in bounds_max],
}

print("VERIFY_FBX_JSON_BEGIN")
print(json.dumps(report, indent=1))
print("VERIFY_FBX_JSON_END")

if report_path:
    with open(report_path, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=1)
    print("VERIFY_FBX_WROTE", report_path)
print("VERIFY_FBX_DONE")
