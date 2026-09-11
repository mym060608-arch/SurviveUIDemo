"""
SurviveUIDemo -- Phase09A_1 Revision02 validation renders.

Opens output/FP_PlayerArms.blend and writes the first-person validation set.

Run:
    blender --background --python scripts/render_validation.py

FIRST-PERSON RENDERS ARE AUTHORITATIVE.  The validation camera reproduces the
game camera contract exactly:

    vertical field of view = 70 deg           (NereidSurfaceBuilder.cs)
    16:9 frame                                (1920x1080 target)
    camera at the player's eye, i.e. the armature origin

The Wrist UI hologram is NEVER rebuilt and NEVER rendered as production
geometry.  A separate, clearly named VALIDATION_UIReference wireframe can be
switched on for one alignment screenshot only; it is not exported.
"""

import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SCRIPT_DIR)
import build_fp_arms as B  # noqa: E402

BLEND = B.BLEND_PATH
VAL_DIR = B.VAL_DIR
REPORT = os.path.join(VAL_DIR, "render_report.json")

FOV_DEG = B.CAM_FOV_DEG
RES_X, RES_Y = 1280, 720

# The game's own clear colour (NereidSurfaceBuilder.cs cam.backgroundColor).
GAME_BACKGROUND = (0.68, 0.71, 0.74)

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
rig = bpy.data.objects["RIG_FPArms"]
meshes = [bpy.data.objects[name] for name in
          ("GEO_Arm_L", "GEO_Hand_L", "GEO_WristDevice_L", "GEO_Arm_R", "GEO_Hand_R")]

scene.render.engine = "BLENDER_EEVEE_NEXT"
scene.render.resolution_x = RES_X
scene.render.resolution_y = RES_Y
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
scene.view_settings.view_transform = "AgX"
scene.render.fps = B.FPS

world = bpy.data.worlds.new("VALIDATION_World")
scene.world = world
world.use_nodes = True
background = world.node_tree.nodes["Background"]
background.inputs[0].default_value = (GAME_BACKGROUND[0], GAME_BACKGROUND[1],
                                       GAME_BACKGROUND[2], 1.0)
background.inputs[1].default_value = 0.45

# ---------------------------------------------------------------------
# Validation camera: origin, looking along -Y, +Z up  (screen right = -X)
# ---------------------------------------------------------------------
BASE_ORIENTATION = Matrix((
    (-1.0, 0.0, 0.0, 0.0),
    (0.0, 0.0, 1.0, 0.0),
    (0.0, 1.0, 0.0, 0.0),
    (0.0, 0.0, 0.0, 1.0),
))

cam_data = bpy.data.cameras.new("VALIDATION_Camera_70deg")
cam = bpy.data.objects.new("VALIDATION_Camera_70deg", cam_data)
scene.collection.objects.link(cam)
cam_data.type = "PERSP"
cam_data.sensor_fit = "VERTICAL"
cam_data.sensor_height = 24.0
cam_data.lens = 12.0 / math.tan(math.radians(FOV_DEG * 0.5))
cam_data.clip_start = 0.01
cam_data.clip_end = 100.0
cam.matrix_world = BASE_ORIENTATION
scene.camera = cam


def aim(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat("-Z", "Y").to_euler()


def add_light(name, location, target, energy, size):
    data = bpy.data.lights.new(name, "AREA")
    data.energy = energy
    data.shape = "DISK"
    data.size = size
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.location = location
    aim(obj, target)
    return obj


add_light("VALIDATION_Key", (0.75, -0.15, 0.55), (0.18, -0.35, -0.25), 58.0, 0.9)
add_light("VALIDATION_Fill", (-1.10, -0.30, 0.05), (-0.05, -0.40, -0.45), 28.0, 1.3)
add_light("VALIDATION_Rim", (0.25, 0.55, 1.05), (0.16, -0.40, -0.35), 42.0, 0.8)
add_light("VALIDATION_Bounce", (0.10, -0.55, -1.30), (0.18, -0.40, -0.35), 22.0, 1.2)

# ---------------------------------------------------------------------
# Wrist UI reference wireframe (validation only; NEVER exported)
# ---------------------------------------------------------------------
guide_collection = bpy.data.collections.new("VALIDATION_UIReference")
scene.collection.children.link(guide_collection)

guide_material = bpy.data.materials.new("VALIDATION_UIReference")
guide_material.use_nodes = True
guide_nodes = guide_material.node_tree.nodes
for node in list(guide_nodes):
    guide_nodes.remove(node)
emission = guide_nodes.new("ShaderNodeEmission")
emission.inputs[0].default_value = (0.05, 0.85, 0.95, 1.0)
emission.inputs[1].default_value = 2.5
output = guide_nodes.new("ShaderNodeOutputMaterial")
guide_material.node_tree.links.new(emission.outputs[0], output.inputs[0])

guide_objects = []


def guide_curve(name, points, bevel=0.0012):
    curve = bpy.data.curves.new(name, "CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth = bevel
    curve.bevel_resolution = 0
    spline = curve.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for point, value in zip(spline.points, points):
        point.co = (value[0], value[1], value[2], 1.0)
    obj = bpy.data.objects.new(name, curve)
    guide_collection.objects.link(obj)
    obj.data.materials.append(guide_material)
    obj.hide_render = True
    guide_objects.append(obj)
    return obj


def ui_circle(radius_px, samples=96):
    return [B.ui_design_to_blender(640.0 + radius_px * math.cos(i * math.tau / samples),
                                   400.0 + radius_px * math.sin(i * math.tau / samples))
            for i in range(samples + 1)]


def ui_polyline(design_points):
    return [B.ui_design_to_blender(px, py) for px, py in design_points]


guide_curve("VALIDATION_MapBounds", ui_circle(B.UI_DESIGN_CENTER[0] * 0.0 + 235.0))
guide_curve("VALIDATION_RingInner", ui_circle(248.0))
guide_curve("VALIDATION_RingOuter", ui_circle(365.0))

for top, bottom, label in ((205.0, 345.0, "upper"), (455.0, 595.0, "lower")):
    for sign, side in ((-1.0, "L"), (1.0, "R")):
        points = []
        for radius in (248.0, 365.0):
            for py in (top, bottom):
                dy = py - 400.0
                if abs(dy) >= radius:
                    continue
                dx = math.sqrt(radius * radius - dy * dy)
                points.append((640.0 + sign * dx, py))
        guide_curve("VALIDATION_PanelBand_%s_%s" % (label, side), ui_polyline(points), 0.0016)

# Hologram plane outline (the WristDisplay quad at its shipped scale).
pivot = B.UI_PIVOT_UNITY + B.UI_DISPLAY_OFFSET_UNITY
half_w = B.UI_DISPLAY_SCALE_UNITY.x * 0.5
half_h = B.UI_DISPLAY_SCALE_UNITY.y * 0.5
guide_curve("VALIDATION_HologramPlane", [
    B.u2b(Vector((pivot.x - half_w, pivot.y - half_h, pivot.z))),
    B.u2b(Vector((pivot.x + half_w, pivot.y - half_h, pivot.z))),
    B.u2b(Vector((pivot.x + half_w, pivot.y + half_h, pivot.z))),
    B.u2b(Vector((pivot.x - half_w, pivot.y + half_h, pivot.z))),
    B.u2b(Vector((pivot.x - half_w, pivot.y - half_h, pivot.z))),
])

# Pivot cross marker.
pivot_marker = B.UI_PIVOT_BLENDER
axis_x = B.u2b(Vector((1.0, 0.0, 0.0))) - B.u2b(Vector((0.0, 0.0, 0.0)))
axis_y = B.u2b(Vector((0.0, 1.0, 0.0))) - B.u2b(Vector((0.0, 0.0, 0.0)))
guide_curve("VALIDATION_UIPivotCrossX",
            [pivot_marker - axis_x * 0.03, pivot_marker + axis_x * 0.03], 0.0022)
guide_curve("VALIDATION_UIPivotCrossY",
            [pivot_marker - axis_y * 0.03, pivot_marker + axis_y * 0.03], 0.0022)


def show_guide(visible):
    for obj in guide_objects:
        obj.hide_render = not visible


# ---------------------------------------------------------------------
# Optional rig overview: simple bone sticks in a VALIDATION collection
# ---------------------------------------------------------------------
rig_collection = bpy.data.collections.new("VALIDATION_Rig")
scene.collection.children.link(rig_collection)
rig_material = bpy.data.materials.new("VALIDATION_Bone")
rig_material.use_nodes = True
rig_bsdf = rig_material.node_tree.nodes.get("Principled BSDF")
rig_bsdf.inputs["Base Color"].default_value = (0.95, 0.62, 0.10, 1.0)
rig_bsdf.inputs["Roughness"].default_value = 0.45

rig_objects = []
for bone in rig.data.bones:
    head = bone.head_local
    tail = bone.tail_local
    direction = (tail - head)
    length = direction.length
    if length < 1e-5:
        continue
    direction.normalize()
    radius = max(0.0055, min(0.012, length * 0.06))
    bpy.ops.mesh.primitive_cone_add(vertices=6, radius1=radius, radius2=radius,
                                    depth=length, location=(0.0, 0.0, 0.0))
    stick = bpy.context.object
    stick.name = "VALIDATION_Bone_" + bone.name
    for collection in list(stick.users_collection):
        collection.objects.unlink(stick)
    rig_collection.objects.link(stick)
    stick.data.materials.append(rig_material)
    stick.matrix_world = (Matrix.Translation((head + tail) * 0.5)
                          @ direction.to_track_quat("Z", "Y").to_matrix().to_4x4())
    stick.hide_render = True
    rig_objects.append(stick)


def show_rig(visible):
    for obj in rig_objects:
        obj.hide_render = not visible


# ---------------------------------------------------------------------
# Render driver
# ---------------------------------------------------------------------
def set_action(name, frame):
    rig.animation_data.action = bpy.data.actions[name]
    scene.frame_set(frame)
    bpy.context.view_layer.update()


def set_pitch(degrees):
    cam.matrix_world = BASE_ORIENTATION @ Matrix.Rotation(math.radians(-degrees), 4, "X")


def landmarks():
    out = {}
    for bone_name in ("Forearm_L", "Hand_L", "WristDevice_L", "Hand_R"):
        pose_bone = rig.pose.bones[bone_name]
        for label, point in (("head", pose_bone.head.copy()),
                             ("tail", pose_bone.tail.copy())):
            ndc = B.camera_ndc(point)
            out[bone_name + "." + label] = [round(v, 4) for v in ndc] if ndc else None
    return out


RENDERS = [
    # Required human-review first-person set.  Normal locomotion uses a modest
    # look-down so the low side-mounted arms are visible without falsifying FOV.
    ("fp_idle", "FP_Idle", 1, ("fp", 40.0),
     "Revision02 FP_Idle: wide lower-left/lower-right EVA arms, clear centre"),
    ("fp_walk_lookdown", "FP_Walk", 11, ("fp", 52.0),
     "Revision02 walk swing at a readable look-down angle"),
    ("wrist_raise", "Wrist_Raise", 22, ("fp", 0.0),
     "Late Wrist_Raise approaching the established UI presentation area"),
    ("wrist_idle", "Wrist_Idle", 46, ("fp", 0.0),
     "Stable raised pose; physical terminal remains below/left of Wrist UI"),
    ("wrist_alignment_guide", "Wrist_Idle", 46, ("fp", 0.0),
     "Read-only shipped Wrist UI geometry reference; never exported"),
]

FP_LENS = 12.0 / math.tan(math.radians(FOV_DEG * 0.5))

results = []
for name, action, frame, view, note in RENDERS:
    guide = name == "wrist_alignment_guide"
    rig_view = name == "rig_overview"

    if view[0] == "fp":
        cam_data.sensor_fit = "VERTICAL"
        cam_data.sensor_height = 24.0
        cam_data.lens = FP_LENS
        set_pitch(view[1])
    else:
        _kind, location, target, lens = view
        cam_data.sensor_fit = "AUTO"
        cam_data.lens = lens
        cam.matrix_world = (Matrix.Translation(Vector(location))
                            @ (Vector(target) - Vector(location))
                            .to_track_quat("-Z", "Y").to_matrix().to_4x4())

    set_action(action, frame)
    show_guide(guide)
    show_rig(rig_view)

    path = os.path.join(VAL_DIR, name + ".png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    results.append({"file": os.path.basename(path), "clip": action, "frame": frame,
                    "view": "first-person" if view[0] == "fp" else "external",
                    "camera_pitch_deg": view[1] if view[0] == "fp" else None,
                    "ui_reference_wireframe": guide, "rig_overlay": rig_view,
                    "note": note, "bytes": os.path.getsize(path),
                    "landmarks_ndc": landmarks() if view[0] == "fp" else None})
    print("RENDER_OK", name, action, frame, os.path.getsize(path))

# ---------------------------------------------------------------------
# Mesh presentation views (neutral studio; not first-person)
# ---------------------------------------------------------------------
show_guide(False)
show_rig(False)
background.inputs[0].default_value = (0.055, 0.065, 0.078, 1.0)
background.inputs[1].default_value = 0.8

set_action("FP_Idle", 1)


def external(name, location, target, lens, clip="FP_Idle", frame=1):
    cam_data.sensor_fit = "AUTO"
    cam_data.lens = lens
    cam.matrix_world = (Matrix.Translation(Vector(location))
                        @ (Vector(target) - Vector(location))
                        .to_track_quat("-Z", "Y").to_matrix().to_4x4())
    path = os.path.join(VAL_DIR, name + ".png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    results.append({"file": os.path.basename(path), "clip": clip, "frame": frame,
                    "camera": "external", "note": "mesh presentation view",
                    "bytes": os.path.getsize(path)})
    print("RENDER_OK", name, "external", os.path.getsize(path))
external("external_front", (0.75, -1.35, -0.10), (0.16, -0.30, -0.48), 50.0)
external("external_side", (1.65, -0.55, -0.30), (0.15, -0.32, -0.46), 50.0)

# Wrist device close-up, viewed along its own rest face normal.  Read from the
# armature's rest bone data so this stays valid without the builder's globals.
device_bone = rig.data.bones["WristDevice_L"]
rest_device = (device_bone.head_local + device_bone.tail_local) * 0.5
rest_normal = device_bone.matrix_local.to_3x3().col[2].normalized()
external("wrist_device_closeup", tuple(rest_device + rest_normal * 0.46),
         tuple(rest_device), 60.0)

# Dedicated right-hand inspection from the palm/thumb side.  This view is
# intentionally different from the external dorsal view so handedness cannot
# be hidden by the back-of-hand armour plate.
set_action("FP_Idle", 1)
right_hand = rig.pose.bones["Hand_R"]
right_center = (right_hand.head + right_hand.tail) * 0.5
right_dorsal = right_hand.matrix.to_3x3().col[2].normalized()
right_side = right_hand.matrix.to_3x3().col[0].normalized()
right_axis = (right_hand.tail - right_hand.head).normalized()
external("right_hand_check",
         tuple(right_center - right_dorsal * 0.44 - right_side * 0.18
               - right_axis * 0.05),
         tuple(right_center + right_axis * 0.015), 50.0,
         clip="FP_Idle", frame=1)

cam_data.sensor_fit = "VERTICAL"
cam_data.sensor_height = 24.0
cam_data.lens = 12.0 / math.tan(math.radians(FOV_DEG * 0.5))
cam.matrix_world = BASE_ORIENTATION
set_pitch(0.0)
set_action("FP_Idle", 1)
scene.frame_set(1)

with open(REPORT, "w", encoding="utf-8") as handle:
    json.dump({
        "camera": {
            "vertical_fov_deg": FOV_DEG,
            "resolution": [RES_X, RES_Y],
            "sensor_fit": "VERTICAL",
            "sensor_height_mm": 24.0,
            "lens_mm": round(12.0 / math.tan(math.radians(FOV_DEG * 0.5)), 6),
            "orientation": "located at the armature origin (the player's eye), "
                           "looking along -Y, +Z up; screen right = -X",
        },
        "engine": "Blender Eevee Next",
        "background": list(GAME_BACKGROUND),
        "note": "No Wrist UI geometry is rendered. The only UI drawing is the "
                "VALIDATION_UIReference wireframe in wrist_idle_guide.png, which "
                "reproduces the shipped pivot/offset/scale constants read from "
                "NereidSurfaceBuilder.cs and is never exported.",
        "renders": results,
    }, handle, indent=1)

print("RENDER_REPORT", REPORT)
print("VALIDATION_RENDER_COMPLETE")
