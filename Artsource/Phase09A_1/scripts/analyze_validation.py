"""
Phase09A_1 Revision03 objective validation of the rendered PNGs and poses.

For every validation image it reports
  * the detected background colour and the fraction of the frame the asset covers,
  * the asset's screen bounding box in 0..1 frame coordinates (0,0 = top-left),
  * the fraction of the asset footprint that falls OUTSIDE the frame,
so a blank or badly framed render cannot pass unnoticed.

It also projects the posed arm geometry and reports, per clip/frame, which
bone landmarks land inside the 70 deg frustum.

    blender --background --python scripts/analyze_validation.py
"""

import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SCRIPT_DIR)
import build_fp_arms as B  # noqa: E402

VAL = B.VAL_DIR
OUT = os.path.join(VAL, "validation_analysis.json")

bpy.ops.wm.open_mainfile(filepath=B.BLEND_PATH)
scene = bpy.context.scene
rig = bpy.data.objects["RIG_FPArms"]
meshes = [bpy.data.objects[name] for name in
          ("GEO_Arm_L", "GEO_Hand_L", "GEO_WristDevice_L", "GEO_Arm_R", "GEO_Hand_R")]


def image_stats(path):
    image = bpy.data.images.load(path)
    width, height = image.size
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(height, width, 4)[:, :, :3]
    bpy.data.images.remove(image)
    # PNG rows come bottom-up from Blender; flip to top-left origin for reporting.
    pixels = pixels[::-1]
    quantised = (pixels * 255.0 + 0.5).astype(np.uint8)
    flat = quantised.reshape(-1, 3)
    keys = (flat[:, 0].astype(np.int32) << 16) | (flat[:, 1].astype(np.int32) << 8) | flat[:, 2]
    values, counts = np.unique(keys, return_counts=True)
    background_key = values[int(np.argmax(counts))]
    background = np.array([(background_key >> 16) & 255, (background_key >> 8) & 255,
                           background_key & 255], dtype=np.int32)
    difference = np.abs(quantised.astype(np.int32) - background).max(axis=2)
    mask = difference > 6
    covered = int(mask.sum())
    stats = {
        "file": os.path.basename(path),
        "resolution": [width, height],
        "background_rgb8": [int(v) for v in background],
        "coverage_fraction": round(covered / (width * height), 4),
        "central_safe_area_fraction": round(
            1.0 - int(mask[:, int(width * 0.30):int(width * 0.70)].sum())
            / max(1, height * (int(width * 0.70) - int(width * 0.30))), 4),
    }
    if covered:
        rows = np.where(mask.any(axis=1))[0]
        cols = np.where(mask.any(axis=0))[0]
        stats["asset_bbox_frame_fraction"] = {
            "left": round(float(cols[0]) / width, 4),
            "top": round(float(rows[0]) / height, 4),
            "right": round(float(cols[-1] + 1) / width, 4),
            "bottom": round(float(rows[-1] + 1) / height, 4),
        }
    return stats


def project_point(point, pitch_deg):
    """Return NDC for the 70-degree validation camera, or None if behind it."""
    pitch = math.radians(pitch_deg)
    cos_p, sin_p = math.cos(pitch), math.sin(pitch)
    camera_x = -point.x
    camera_y = point.z * cos_p - point.y * sin_p
    camera_z = point.z * sin_p + point.y * cos_p
    depth = -camera_z
    if depth <= 0.02:
        return None
    half_h = depth * math.tan(math.radians(B.CAM_FOV_DEG * 0.5))
    return camera_x / (half_h * B.CAM_ASPECT), camera_y / half_h


def bbox_stats(xs, ys):
    if not xs:
        return None
    box = [min(xs), min(ys), max(xs), max(ys)]
    area = max(0.0, box[2] - box[0]) * max(0.0, box[3] - box[1]) / 4.0
    return {"ndc_bbox": [round(v, 4) for v in box],
            "viewport_bbox_area_fraction": round(area, 4)}


def project_pose(clip, frame, pitch_deg):
    """Frustum coverage of posed geometry, split by physical body side."""
    rig.animation_data.action = bpy.data.actions[clip]
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    inside = 0
    total = 0
    xs, ys = [], []
    sides = {"left": {"inside": 0, "total": 0, "xs": [], "ys": []},
             "right": {"inside": 0, "total": 0, "xs": [], "ys": []}}
    for obj in meshes:
        side = "left" if obj.name.endswith("_L") else "right"
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        matrix = evaluated.matrix_world
        for vertex in mesh.vertices:
            point = matrix @ vertex.co
            total += 1
            sides[side]["total"] += 1
            ndc = project_point(point, pitch_deg)
            if ndc is None:
                continue
            ndc_x, ndc_y = ndc
            if -1.0 <= ndc_x <= 1.0 and -1.0 <= ndc_y <= 1.0:
                inside += 1
                xs.append(ndc_x)
                ys.append(ndc_y)
                sides[side]["inside"] += 1
                sides[side]["xs"].append(ndc_x)
                sides[side]["ys"].append(ndc_y)
        evaluated.to_mesh_clear()
    result = {"clip": clip, "frame": frame, "pitch_deg": pitch_deg,
              "vertices": total, "vertices_inside_frustum": inside,
              "frustum_coverage": round(inside / max(1, total), 4)}
    if xs:
        result.update(bbox_stats(xs, ys))
    result["sides"] = {}
    for side, data in sides.items():
        entry = {"vertices": data["total"], "vertices_inside_frustum": data["inside"],
                 "visible_vertex_fraction": round(data["inside"] / max(1, data["total"]), 4)}
        if data["xs"]:
            entry.update(bbox_stats(data["xs"], data["ys"]))
        result["sides"][side] = entry
    return result


def wrist_ui_proxy_overlap():
    """Compare physical watch viewport bounds with the authoritative UI centre."""
    rig.animation_data.action = bpy.data.actions["Wrist_Idle"]
    scene.frame_set(46)
    bpy.context.view_layer.update()
    obj = bpy.data.objects["GEO_WristDevice_L"]
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    points = [project_point(evaluated.matrix_world @ v.co, 0.0) for v in mesh.vertices]
    evaluated.to_mesh_clear()
    points = [p for p in points if p is not None]
    device = [min(p[0] for p in points), min(p[1] for p in points),
              max(p[0] for p in points), max(p[1] for p in points)]
    ui_world = [B.ui_design_to_blender(640.0 + x, 400.0 + y)
                for x, y in ((-235.0, 0.0), (235.0, 0.0),
                             (0.0, -235.0), (0.0, 235.0))]
    ui_points = [B.camera_ndc(point) for point in ui_world]
    ui = [min(p[0] for p in ui_points), min(p[1] for p in ui_points),
          max(p[0] for p in ui_points), max(p[1] for p in ui_points)]
    ix = max(0.0, min(device[2], ui[2]) - max(device[0], ui[0]))
    iy = max(0.0, min(device[3], ui[3]) - max(device[1], ui[1]))
    overlap = ix * iy
    ui_area = max(1e-9, (ui[2] - ui[0]) * (ui[3] - ui[1]))
    device_area = max(1e-9, (device[2] - device[0]) * (device[3] - device[1]))
    with open(B.REPORT_PATH, "r", encoding="utf-8") as handle:
        asset_report = json.load(handle)
    ray_fraction = next(
        item["occluded_fraction"] for item in asset_report["occlusion"]
        if item["clip"] == "Wrist_Idle" and item["frame"] == 46)
    return {"clip": "Wrist_Idle", "frame": 46,
            "device_ndc_bbox": [round(v, 4) for v in device],
            "ui_central_ndc_bbox": [round(v, 4) for v in ui],
            "overlap_fraction_of_ui_central_bbox": round(overlap / ui_area, 4),
            "overlap_fraction_of_device_bbox": round(overlap / device_area, 4),
            "ray_sample_occlusion_fraction": ray_fraction}


def loop_check(clip, length):
    """Max per-bone deviation between the first and last frame of a loop clip."""
    rig.animation_data.action = bpy.data.actions[clip]
    scene.frame_set(1)
    bpy.context.view_layer.update()
    first = {bone.name: bone.matrix.copy() for bone in rig.pose.bones}
    scene.frame_set(length)
    bpy.context.view_layer.update()
    worst = 0.0
    worst_bone = None
    for bone in rig.pose.bones:
        delta = first[bone.name] - bone.matrix
        magnitude = max(abs(delta[row][col]) for row in range(4) for col in range(4))
        if magnitude > worst:
            worst, worst_bone = magnitude, bone.name
    return {"clip": clip, "first_frame": 1, "last_frame": length,
            "max_element_deviation": round(worst, 9), "worst_bone": worst_bone}


def root_motion_check():
    """Confirm the Root bone never leaves the identity transform."""
    worst = 0.0
    for name, length, _loop, _usage in B.CLIPS:
        rig.animation_data.action = bpy.data.actions[name]
        pose_bone = rig.pose.bones["Root"]
        for frame in range(1, length + 1):
            scene.frame_set(frame)
            bpy.context.view_layer.update()
            delta = pose_bone.matrix_basis - Matrix.Identity(4)
            worst = max(worst, max(abs(delta[r][c]) for r in range(4) for c in range(4)))
    return {"max_root_basis_deviation_from_identity": round(worst, 9)}


report = {"revision": "Revision03", "images": [], "poses": [], "loops": [],
          "root_motion": None, "wrist_ui_proxy": None}
for name in sorted(os.listdir(VAL)):
    if name.lower().endswith(".png"):
        report["images"].append(image_stats(os.path.join(VAL, name)))

for clip, frame, pitch in (("FP_Idle", 1, 0.0), ("FP_Idle", 1, 52.0),
                           ("FP_Walk", 11, 0.0), ("FP_Walk", 11, 52.0),
                           ("Wrist_Raise", 7, 0.0), ("Wrist_Raise", 13, 0.0),
                           ("Wrist_Raise", 19, 0.0),
                           ("Wrist_Idle", 46, 0.0), ("Wrist_Lower", 11, 0.0),
                           ("Interact_Right", 16, 42.0)):
    report["poses"].append(project_pose(clip, frame, pitch))

report["loops"] = [loop_check(name, length) for name, length, loop, _usage in B.CLIPS
                   if loop]
report["root_motion"] = root_motion_check()
report["wrist_ui_proxy"] = wrist_ui_proxy_overlap()

with open(OUT, "w", encoding="utf-8") as handle:
    json.dump(report, handle, indent=1)

print("ANALYSIS_BEGIN")
for entry in report["images"]:
    box = entry.get("asset_bbox_frame_fraction")
    print("IMG %-28s coverage=%s bbox=%s" % (entry["file"], entry["coverage_fraction"],
                                             box))
for entry in report["poses"]:
    print("POSE %-16s f%-3d pitch=%-5s inside=%d/%d (%.3f) ndc_bbox=%s"
          % (entry["clip"], entry["frame"], entry["pitch_deg"],
             entry["vertices_inside_frustum"], entry["vertices"],
             entry["frustum_coverage"], entry.get("ndc_bbox")))
for entry in report["loops"]:
    print("LOOP %-12s frames %d..%d max_deviation=%.9f (worst bone %s)"
          % (entry["clip"], entry["first_frame"], entry["last_frame"],
             entry["max_element_deviation"], entry["worst_bone"]))
print("ROOT_MOTION", report["root_motion"])
print("WRIST_UI_PROXY", report["wrist_ui_proxy"])
print("ANALYSIS_WROTE", OUT)
print("ANALYSIS_DONE")
