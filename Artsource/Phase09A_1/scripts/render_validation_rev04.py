"""SurviveUIDemo -- Phase09A_1 Revision04 validation renders.

Opens the Revision04 .blend, builds the SAME first-person validation camera the
previous revisions used (eye at the origin, looking -Y, +Z up, vertical FOV 70,
16:9) and renders the contract's evidence set into validation/revision04/.

Two kinds of frame are produced for every first-person pose:

  * REVIEW frame  - the game-like light-grey background, for human inspection;
  * MASK frame    - the identical pose on a BLACK background, so the arm
                    silhouette can be measured exactly (screen angle, entry
                    side, coverage, device position) by analyze_validation_rev04.py.

The Wrist UI is present only as a read-only wireframe overlay in ONE extra frame
(`wrist_idle_ui_reference.png`); it is never exported and never modified.

Run:  blender --background --python scripts/render_validation_rev04.py
"""

import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)
import build_fp_arms_rev04 as B  # noqa: E402

BLEND = B.BLEND_PATH
VAL_DIR = B.VAL_DIR
REPORT = os.path.join(VAL_DIR, "render_report.json")
FOV_DEG = B.CAM_FOV_DEG

GAME_BACKGROUND = (0.660, 0.690, 0.720)

MESH_NAMES = ("GEO_Arm_L", "GEO_HandBlock_L", "GEO_WristDevice_L",
              "GEO_Arm_R", "GEO_HandBlock_R")

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
rig = bpy.data.objects["RIG_FPArms"]
meshes = [bpy.data.objects[name] for name in MESH_NAMES]

# ---------------------------------------------------------------------
# Render settings
# ---------------------------------------------------------------------
scene.render.resolution_x = 1280
scene.render.resolution_y = 720
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = "PNG"
scene.render.film_transparent = False
for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
    try:
        scene.render.engine = engine
        break
    except TypeError:
        continue
scene.view_settings.view_transform = "Standard"
scene.view_settings.look = "None"

world = bpy.data.worlds.new("VALIDATION_World")
scene.world = world
world.use_nodes = True
background = world.node_tree.nodes["Background"]
background.inputs[0].default_value = (*GAME_BACKGROUND, 1.0)
background.inputs[1].default_value = 0.45

# ---------------------------------------------------------------------
# First-person validation camera (the player's eye)
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
cam_data.clip_start = 0.01
cam_data.clip_end = 100.0
FP_LENS = 12.0 / math.tan(math.radians(FOV_DEG * 0.5))
cam_data.lens = FP_LENS
cam.matrix_world = BASE_ORIENTATION
scene.camera = cam


def set_pitch(degrees):
    cam.matrix_world = BASE_ORIENTATION @ Matrix.Rotation(math.radians(-degrees), 4, "X")


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


add_light("VALIDATION_Key", (0.70, -0.55, 0.60), (0.20, -0.30, -0.35), 46.0, 0.9)
add_light("VALIDATION_Fill", (-1.05, -0.45, 0.00), (-0.10, -0.35, -0.45), 24.0, 1.3)
add_light("VALIDATION_Rim", (0.30, 0.60, 0.95), (0.20, -0.35, -0.30), 34.0, 0.8)
add_light("VALIDATION_Bounce", (0.05, -0.60, -1.20), (0.20, -0.40, -0.40), 20.0, 1.2)

# ---------------------------------------------------------------------
# Read-only Wrist UI reference wireframe (validation only, NEVER exported)
# ---------------------------------------------------------------------
guide_collection = bpy.data.collections.new("WristUIProxy_ValidationOnly")
scene.collection.children.link(guide_collection)

guide_material = bpy.data.materials.new("MAT_WristUIProxy_ValidationOnly")
guide_material.use_nodes = True
guide_nodes = guide_material.node_tree.nodes
for node in list(guide_nodes):
    guide_nodes.remove(node)
guide_output = guide_nodes.new("ShaderNodeOutputMaterial")
guide_emission = guide_nodes.new("ShaderNodeEmission")
guide_emission.inputs[0].default_value = (0.05, 0.85, 0.95, 1.0)
guide_emission.inputs[1].default_value = 1.6
guide_material.node_tree.links.new(guide_emission.outputs[0], guide_output.inputs[0])

guide_objects = []


def guide_polyline(name, points, bevel=0.0016):
    curve = bpy.data.curves.new(name, "CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth = bevel
    curve.bevel_resolution = 1
    spline = curve.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for index, point in enumerate(points):
        spline.points[index].co = (point[0], point[1], point[2], 1.0)
    obj = bpy.data.objects.new(name, curve)
    obj.data.materials.append(guide_material)
    guide_collection.objects.link(obj)
    guide_objects.append(obj)
    return obj


def ui_plane_point(local_x, local_y):
    """A point on the existing hologram plane, in Blender authoring space."""
    return B.u2b(B.UI_PIVOT_UNITY + B.UI_DISPLAY_OFFSET_UNITY
                 + Vector((local_x, local_y, 0.0)))


half_w = B.UI_DISPLAY_SCALE_UNITY.x * 0.5
half_h = B.UI_DISPLAY_SCALE_UNITY.y * 0.5
guide_polyline("WristUI_Frame", [
    ui_plane_point(-half_w, -half_h), ui_plane_point(half_w, -half_h),
    ui_plane_point(half_w, half_h), ui_plane_point(-half_w, half_h),
    ui_plane_point(-half_w, -half_h)])

disc_radius = 235.0 / B.UI_DESIGN_W * B.UI_DISPLAY_SCALE_UNITY.x
guide_polyline("WristUI_MapDisc", [
    ui_plane_point(math.cos(2.0 * math.pi * i / 96.0) * disc_radius,
                   math.sin(2.0 * math.pi * i / 96.0) * disc_radius)
    for i in range(97)])

guide_polyline("WristUI_PivotMarker", [
    B.UI_PIVOT_BLENDER + Vector((-0.03, 0.0, 0.0)),
    B.UI_PIVOT_BLENDER + Vector((0.03, 0.0, 0.0))])


def show_guide(visible):
    for obj in guide_objects:
        obj.hide_render = not visible


# ---------------------------------------------------------------------
# Rig overlay (validation only)
# ---------------------------------------------------------------------
rig_collection = bpy.data.collections.new("VALIDATION_Rig")
scene.collection.children.link(rig_collection)
rig_material = bpy.data.materials.new("VALIDATION_Bone")
rig_material.use_nodes = True
rig_bsdf = rig_material.node_tree.nodes.get("Principled BSDF")
rig_bsdf.inputs["Base Color"].default_value = (1.0, 0.45, 0.05, 1.0)
if "Emission Color" in rig_bsdf.inputs:
    rig_bsdf.inputs["Emission Color"].default_value = (1.0, 0.45, 0.05, 1.0)
    rig_bsdf.inputs["Emission Strength"].default_value = 0.8

rig_objects = []


def refresh_rig_overlay():
    for obj in rig_objects:
        data = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        if isinstance(data, bpy.types.Mesh):
            bpy.data.meshes.remove(data)
    rig_objects.clear()
    for bone in rig.pose.bones:
        head = bone.head.copy()
        tail = bone.tail.copy()
        direction = (tail - head)
        length = direction.length
        if length < 1.0e-5:
            continue
        curve = bpy.data.curves.new("RIGVIS_" + bone.name, "CURVE")
        curve.dimensions = "3D"
        curve.bevel_depth = 0.0035
        spline = curve.splines.new("POLY")
        spline.points.add(1)
        spline.points[0].co = (head.x, head.y, head.z, 1.0)
        spline.points[1].co = (tail.x, tail.y, tail.z, 1.0)
        obj = bpy.data.objects.new("RIGVIS_" + bone.name, curve)
        obj.data.materials.append(rig_material)
        rig_collection.objects.link(obj)
        rig_objects.append(obj)
    # Newly created objects default to renderable: hide them unless a caller
    # asked for the rig overlay.  (Getting this order wrong puts orange bone
    # lines into the measurement masks.)
    for obj in rig_objects:
        obj.hide_render = not _rig_visible


_rig_visible = False


def show_rig(visible):
    global _rig_visible
    _rig_visible = visible
    for obj in rig_objects:
        obj.hide_render = not visible


# ---------------------------------------------------------------------
# Render driver
# ---------------------------------------------------------------------
def set_action(name, frame):
    rig.animation_data.action = bpy.data.actions[name]
    scene.frame_set(frame)
    bpy.context.view_layer.update()


def landmarks():
    out = {}
    for bone_name in ("Arm_L", "WristDevice_L", "Arm_R"):
        pose_bone = rig.pose.bones[bone_name]
        for label, point in (("head", pose_bone.head.copy()),
                             ("tail", pose_bone.tail.copy())):
            ndc = B.camera_ndc(point)
            out[bone_name + "." + label] = [round(v, 4) for v in ndc] if ndc else None
    return out


results = []


def render(name, action, frame, pitch=None, mode="review", guide=False,
           note="", rig_visible=False):
    scene.camera = cam
    cam_data.sensor_fit = "VERTICAL"
    cam_data.sensor_height = 24.0
    cam_data.lens = FP_LENS
    set_pitch(0.0 if pitch is None else pitch)
    set_action(action, frame)
    show_guide(guide)
    show_rig(rig_visible)
    refresh_rig_overlay()
    if mode == "mask":
        background.inputs[0].default_value = (0.0, 0.0, 0.0, 1.0)
        background.inputs[1].default_value = 0.0
    else:
        background.inputs[0].default_value = (*GAME_BACKGROUND, 1.0)
        background.inputs[1].default_value = 0.45

    path = os.path.join(VAL_DIR, name + ".png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    entry = {"file": os.path.basename(path), "clip": action, "frame": frame,
             "view": "first-person", "mode": mode,
             "camera_pitch_deg": pitch if pitch is not None else 0.0,
             "ui_proxy_wireframe": guide, "note": note,
             "bytes": os.path.getsize(path),
             "landmarks_ndc": landmarks()}
    if mode == "review":
        entry["arm_angle_deg"] = round(B.screen_angle_deg(
            rig.pose.bones["Arm_L"].head.lerp(rig.pose.bones["Arm_L"].tail, 0.9),
            rig.pose.bones["Arm_L"].tail) or 0.0, 3)
    results.append(entry)
    print("RENDER_OK", name, action, frame, os.path.getsize(path))


def external(name, location, target, lens, note, clip="FP_Idle", frame=1,
             rig_visible=False):
    scene.camera = cam
    set_action(clip, frame)
    show_guide(False)
    show_rig(rig_visible)
    refresh_rig_overlay()
    background.inputs[0].default_value = (0.055, 0.065, 0.078, 1.0)
    background.inputs[1].default_value = 0.8
    cam_data.sensor_fit = "AUTO"
    cam_data.lens = lens
    cam.matrix_world = (Matrix.Translation(Vector(location))
                        @ (Vector(target) - Vector(location))
                        .to_track_quat("-Z", "Y").to_matrix().to_4x4())
    path = os.path.join(VAL_DIR, name + ".png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    results.append({"file": os.path.basename(path), "clip": clip, "frame": frame,
                    "view": "external", "mode": "review",
                    "ui_proxy_wireframe": False, "note": note,
                    "bytes": os.path.getsize(path)})
    print("RENDER_OK", name, "external", os.path.getsize(path))


# =====================================================================
# 1. First-person forward view (arms legitimately outside the frame)
# =====================================================================
render("fp_forward", "FP_Idle", 1, pitch=0.0,
       note="Revision04 normal forward view; arms may be outside the frame")
render("fp_forward_mask", "FP_Idle", 1, pitch=0.0, mode="mask",
       note="black-background mask for measurement")

# =====================================================================
# 2. Walk, looking down: both block arms at the body sides
# =====================================================================
for suffix, frame in (("a", 11), ("b", 31)):
    render("fp_walk_lookdown" + ("" if suffix == "a" else "_" + suffix),
           "FP_Walk", frame, pitch=45.0,
           note="Revision04 walk, look-down 45 deg, opposite swing")
    render("fp_walk_lookdown" + ("" if suffix == "a" else "_" + suffix) + "_mask",
           "FP_Walk", frame, pitch=45.0, mode="mask",
           note="black-background mask for measurement")

# =====================================================================
# 3. Wrist raise / hold composition (THE critical gates)
# =====================================================================
render("wrist_raise_mid", "Wrist_Raise", 22, pitch=0.0,
       note="Revision04 mid raise: left arm entering from the lower-left "
            "(the arm only reaches the frame around frame 19)")
render("wrist_raise_mid_mask", "Wrist_Raise", 22, pitch=0.0, mode="mask",
       note="black-background mask for the entry/angle measurement")
render("wrist_idle", "Wrist_Idle", 46, pitch=0.0,
       note="Revision04 held presentation pose (device left, UI above/right)")
render("wrist_idle_mask", "Wrist_Idle", 46, pitch=0.0, mode="mask",
       note="black-background mask for measurement")
render("wrist_idle_ui_reference", "Wrist_Idle", 46, pitch=0.0, guide=True,
       note="read-only wireframe of the EXISTING Wrist UI (never exported)")

# =====================================================================
# 4. Device close-up and external overviews
# =====================================================================
device_bone = rig.pose.bones["WristDevice_L"]
set_action("Wrist_Idle", 46)
device_center = (device_bone.head + device_bone.tail) * 0.5
device_axis = device_bone.matrix.to_3x3().col[1].normalized()
device_bias = device_axis * 0.03

external("wrist_device_closeup",
         device_center + device_axis * 0.30 + Vector((0.02, -0.05, 0.05)),
         device_center + device_bias, 62.0,
         "Revision04 compact polygonal terminal on the distal left forearm",
         clip="Wrist_Idle", frame=46)

external("external_overview", (0.62, -1.05, 0.42), (0.16, -0.20, -0.38), 40.0,
         "Revision04 both block arms externally (simple rectangular prisms)",
         clip="FP_Idle", frame=1)

external("external_overview_raise", (0.70, -0.95, 0.30), (0.16, -0.28, -0.30), 40.0,
         "Revision04 raised left arm externally", clip="Wrist_Idle", frame=46)

external("rig_overview", (0.62, -1.05, 0.42), (0.16, -0.20, -0.38), 40.0,
         "Revision04 minimal 4-bone rig (Root / Arm_L / WristDevice_L / Arm_R)",
         clip="FP_Idle", frame=1, rig_visible=True)

# =====================================================================
# Report
# =====================================================================
report = {
    "generated_by": "scripts/render_validation_rev04.py (Phase09A_1 Revision04)",
    "camera": {
        "vertical_fov_deg": FOV_DEG,
        "aspect": "16:9",
        "resolution": [scene.render.resolution_x, scene.render.resolution_y],
        "position": "player eye at the origin",
        "orientation": "looking -Y, +Z up (screen right = -X)",
        "lens_mm": round(FP_LENS, 6),
        "engine": scene.render.engine,
    },
    "background": {"review_rgb": list(GAME_BACKGROUND),
                   "mask_rgb": [0.0, 0.0, 0.0]},
    "ui_reference": {
        "wrist_pivot_camera_local_unity": [round(v, 6) for v in B.UI_PIVOT_UNITY],
        "wrist_display_offset_unity": [round(v, 6) for v in B.UI_DISPLAY_OFFSET_UNITY],
        "wrist_display_scale_unity": [round(v, 6) for v in B.UI_DISPLAY_SCALE_UNITY],
        "note": "read-only validation wireframe; the shipped Wrist UI is unchanged",
    },
    "renders": results,
    "note": "Validation-only cameras, lights, wireframe and rig overlay are never "
            "exported to the FBX.",
}

with open(REPORT, "w", encoding="utf-8") as handle:
    json.dump(report, handle, indent=1, sort_keys=False)
print("RENDER_REPORT", REPORT, len(results))
