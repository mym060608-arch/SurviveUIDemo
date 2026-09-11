"""
SurviveUIDemo -- Phase09A_1 Revision03 validation renders.

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

cam_data = bpy.data.cameras.new("CAM_FP_Forward")
cam = bpy.data.objects.new("CAM_FP_Forward", cam_data)
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
guide_collection = bpy.data.collections.new("WristUIProxy")
scene.collection.children.link(guide_collection)

guide_material = bpy.data.materials.new("MAT_WristUIProxy_ValidationOnly")
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
guide_curve("WristUIProxy", [
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


FP_LENS = 12.0 / math.tan(math.radians(FOV_DEG * 0.5))
results = []


def render_image(name, action, frame, pitch=None, guide=False, note=""):
    """Render one authoritative 70-degree first-person validation image."""
    scene.camera = cam
    cam_data.sensor_fit = "VERTICAL"
    cam_data.sensor_height = 24.0
    cam_data.lens = FP_LENS
    set_pitch(0.0 if pitch is None else pitch)
    set_action(action, frame)
    show_guide(guide)
    show_rig(False)
    path = os.path.join(VAL_DIR, name + ".png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    results.append({"file": os.path.basename(path), "clip": action, "frame": frame,
                    "view": "first-person", "camera_pitch_deg": pitch,
                    "ui_proxy_wireframe": guide, "note": note,
                    "bytes": os.path.getsize(path), "landmarks_ndc": landmarks()})
    print("RENDER_OK", name, action, frame, os.path.getsize(path))


def external(name, location, target, lens, note, clip="FP_Idle", frame=1,
             guide=False, visible_meshes=None):
    scene.camera = cam
    set_action(clip, frame)
    show_guide(guide)
    show_rig(False)
    for mesh_obj in meshes:
        mesh_obj.hide_render = (visible_meshes is not None
                                and mesh_obj.name not in visible_meshes)
    cam_data.sensor_fit = "AUTO"
    cam_data.lens = lens
    cam.matrix_world = (Matrix.Translation(Vector(location))
                        @ (Vector(target) - Vector(location))
                        .to_track_quat("-Z", "Y").to_matrix().to_4x4())
    path = os.path.join(VAL_DIR, name + ".png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    results.append({"file": os.path.basename(path), "clip": clip, "frame": frame,
                    "view": "external", "ui_proxy_wireframe": guide,
                    "note": note, "bytes": os.path.getsize(path)})
    print("RENDER_OK", name, "external", os.path.getsize(path))
    for mesh_obj in meshes:
        mesh_obj.hide_render = False


# Validation-only named camera references.  They share one eye position and
# differ only by pitch; neither object exists in the exported FBX.
look_data = cam_data.copy()
look_data.name = "CAM_FP_LookDown"
look_camera = bpy.data.objects.new("CAM_FP_LookDown", look_data)
scene.collection.objects.link(look_camera)
look_camera.matrix_world = BASE_ORIENTATION @ Matrix.Rotation(math.radians(-52.0), 4, "X")

# Validation-only body-space hierarchy.  Empties are intentionally non-rendering
# and never saved/exported; they make the shoulder/eye contract explicit.
body_root = bpy.data.objects.new("BodyReference", None)
head_ref = bpy.data.objects.new("HeadReference", None)
shoulder_l = bpy.data.objects.new("Shoulder_L", None)
shoulder_r = bpy.data.objects.new("Shoulder_R", None)
eye_ref = bpy.data.objects.new("EyeCameraReference", None)
for obj in (body_root, head_ref, shoulder_l, shoulder_r, eye_ref):
    scene.collection.objects.link(obj)
head_ref.parent = body_root
shoulder_l.parent = body_root
shoulder_r.parent = body_root
eye_ref.parent = head_ref
head_ref.location = (0.0, 0.0, -0.08)
eye_ref.location = (0.0, 0.0, 0.08)
shoulder_l.location = B.SHOULDER["L"]
shoulder_r.location = B.SHOULDER["R"]

# ---------------------------------------------------------------------
# PASS 1: compact wrist-device inspection views.
# ---------------------------------------------------------------------
background.inputs[0].default_value = (0.055, 0.065, 0.078, 1.0)
background.inputs[1].default_value = 0.8
set_action("FP_Idle", 1)
device_bone = rig.pose.bones["WristDevice_L"]
rest_device = (device_bone.head + device_bone.tail) * 0.5
rest_axis = (device_bone.tail - device_bone.head).normalized()
rest_normal = B.forearm_dorsal("L", rest_axis)
rest_side = device_bone.matrix.to_3x3().col[0].normalized()

external("01_wrist_device_top", tuple(rest_device + rest_normal * 0.31),
         tuple(rest_device), 57.0,
         "Face-on: central main screen, four cyan panes and restrained status arcs",
         visible_meshes={"GEO_WristDevice_L"})
external("02_wrist_device_front",
         tuple(rest_device + rest_normal * 0.28 - rest_axis * 0.15),
         tuple(rest_device - rest_axis * 0.005), 60.0,
         "Three-quarter front view showing the compact fore/aft button layout",
         visible_meshes={"GEO_WristDevice_L"})
external("03_wrist_device_side",
         tuple(rest_device + rest_side * 0.30 + rest_normal * 0.035),
         tuple(rest_device), 62.0,
         "Side profile proving reduced face thickness and close forearm integration",
         visible_meshes={"GEO_WristDevice_L"})
left_mid = (rig.pose.bones["Forearm_L"].head + rig.pose.bones["Hand_L"].tail) * 0.5
external("04_wrist_device_on_arm",
         tuple(rest_device + rest_normal * 0.48 + rest_axis * 0.18 + rest_side * 0.10),
         tuple(left_mid), 58.0,
         "Compact terminal in proportion to the left EVA forearm",
         visible_meshes={"GEO_Arm_L", "GEO_WristDevice_L"})

# ---------------------------------------------------------------------
# PASS 2: body-space rest and walk.  Forward images intentionally contain no
# arms; the same eye position at 48 degrees down naturally reveals both sides.
# ---------------------------------------------------------------------
background.inputs[0].default_value = (GAME_BACKGROUND[0], GAME_BACKGROUND[1],
                                       GAME_BACKGROUND[2], 1.0)
background.inputs[1].default_value = 0.45
render_image("05_fp_forward_view", "FP_Idle", 1, 0.0, False,
             "Hard gate: body-space resting arms are invisible at level 70-degree FOV")
render_image("06_fp_look_down", "FP_Idle", 1, 52.0, False,
             "Same eye position, camera pitched down 52 degrees; both side-hanging arms appear")
render_image("07_fp_walk_forward", "FP_Walk", 11, 0.0, False,
             "Forward walk hard gate: restrained swing remains out of level view")
render_image("08_fp_walk_look_down", "FP_Walk", 11, 52.0, False,
             "Look-down walk: mild body-space counter-swing with an open centre")

# ---------------------------------------------------------------------
# PASS 3: natural raise/hold/lower and read-only UI proxy alignment.
# ---------------------------------------------------------------------
# A fixed external left-front camera makes the path readable while its early
# frames are still correctly below the level first-person frustum.
raise_camera = (1.24, -1.48, -0.16)
raise_target = (0.14, -0.19, -0.40)
external("09_wrist_raise_25", raise_camera, raise_target, 48.0,
         "Fixed path-review view: quarter point leaving body-space rest",
         clip="Wrist_Raise", frame=7)
external("10_wrist_raise_50", raise_camera, raise_target, 48.0,
         "Fixed path-review view: elbow flexion and forward lift at midpoint",
         clip="Wrist_Raise", frame=13)
external("11_wrist_raise_75", raise_camera, raise_target, 48.0,
         "Fixed path-review view: wrist rotates toward the eye near hold",
         clip="Wrist_Raise", frame=19)
render_image("12_wrist_hold", "Wrist_Idle", 46, 0.0, False,
             "Stable hold: compact physical device below the authoritative UI region")
render_image("13_wrist_hold_ui_proxy", "Wrist_Idle", 46, 0.0, True,
             "Read-only WristUIProxy; physical watch remains below/behind its central area")
external("14_wrist_lower_50", raise_camera, raise_target, 48.0,
         "Same fixed path-review view: lower midpoint returning to body-space rest",
         clip="Wrist_Lower", frame=11)

# Neutral external body-space views.
background.inputs[0].default_value = (0.055, 0.065, 0.078, 1.0)
background.inputs[1].default_value = 0.8
external("15_external_front", (0.78, -1.40, -0.18), (0.0, -0.04, -0.48), 52.0,
         "External front: arms hang under their respective shoulders")
external("16_external_side", (1.55, -0.18, -0.36), (0.04, -0.02, -0.49), 52.0,
         "External side: wrists remain in body space rather than projected before the camera")

# Return to a stable, reproducible scene state.
show_guide(False)
cam_data.sensor_fit = "VERTICAL"
cam_data.sensor_height = 24.0
cam_data.lens = FP_LENS
cam.matrix_world = BASE_ORIENTATION
scene.camera = cam
set_action("FP_Idle", 1)

with open(REPORT, "w", encoding="utf-8") as handle:
    json.dump({
        "revision": "Revision03",
        "camera": {
            "vertical_fov_deg": FOV_DEG,
            "resolution": [RES_X, RES_Y],
            "sensor_fit": "VERTICAL",
            "sensor_height_mm": 24.0,
            "lens_mm": round(FP_LENS, 6),
            "forward_pitch_deg": 0.0,
            "look_down_pitch_deg": 52.0,
            "orientation": "eye at armature origin, looking -Y, +Z up; screen right=-X",
        },
        "validation_only_scene_objects": [
            "BodyReference", "HeadReference", "Shoulder_L", "Shoulder_R",
            "EyeCameraReference", "CAM_FP_Forward", "CAM_FP_LookDown", "WristUIProxy"
        ],
        "engine": "Blender Eevee Next",
        "background": list(GAME_BACKGROUND),
        "note": "WristUIProxy is a read-only validation wireframe reconstructed from "
                "the shipped UI constants. It is not saved into the authority blend, "
                "not exported, and does not modify the Unity Wrist UI.",
        "renders": results,
    }, handle, indent=1)

print("RENDER_REPORT", REPORT)
print("VALIDATION_RENDER_COMPLETE", len(results))
