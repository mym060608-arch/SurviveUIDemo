"""
Phase09A_1 diagnostics: sweep candidate raised-wrist hold targets and measure

  * how much of the EXISTING Wrist UI hologram the arm occludes, and
  * where the wrist device itself lands on screen.

Read-only with respect to the UI: it never touches Unity or the UI contract, it
only re-poses the already-built arm and ray-tests the hologram sample grid.

    blender --background --python scripts/tune_wrist_hold.py
"""

import math
import os
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SCRIPT_DIR)
import build_fp_arms as B  # noqa: E402

BLEND = B.BLEND_PATH

bpy.ops.wm.open_mainfile(filepath=BLEND)
scene = bpy.context.scene
rig = bpy.data.objects["RIG_FPArms"]
meshes = [bpy.data.objects[name] for name in
          ("GEO_Arm_L", "GEO_Hand_L", "GEO_WristDevice_L", "GEO_Arm_R", "GEO_Hand_R")]

# Re-derive the deterministic globals the builder uses.
B.REST = B.resolve_rest_pose()
B.CURL_SIGN = {}
for side in ("L", "R"):
    for group in B.FINGER_GROUPS:
        for suffix in ("", "_02"):
            name = group + suffix + "_" + side
            if name in rig.data.bones:
                B.CURL_SIGN[name] = B.curl_sign_for(rig, name)

SAMPLES = B.ui_sample_points()
REGION_WEIGHT = {"map": 3.0, "status": 1.0}
for region in {r for _p, r in SAMPLES}:
    if region.startswith("panel_"):
        REGION_WEIGHT[region] = 3.0


def measure(target_face_center):
    B.REST["hold"] = B.solve_device_hold(target_face_center, B.SHOULDER["L"], B.RAISE_POLE)
    B.apply_pose(rig, lift=1.0, swing_l=0.0, swing_r=0.0, bob=0.0, breathe=0.0,
                 curl_l=0.85, curl_r=0.18)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    trees = [(obj.name, BVHTree.FromObject(obj, depsgraph)) for obj in meshes]

    hits, total, weighted_hits, weighted_total = {}, {}, 0.0, 0.0
    for world_point, region in SAMPLES:
        total[region] = total.get(region, 0) + 1
        weight = REGION_WEIGHT.get(region, 1.0)
        weighted_total += weight
        direction = world_point.normalized()
        for _name, tree in trees:
            location, _n, _f, _d = tree.ray_cast(Vector((0.0, 0.0, 0.0)), direction,
                                                 world_point.length * 0.995)
            if location is not None:
                hits[region] = hits.get(region, 0) + 1
                weighted_hits += weight
                break

    device = rig.pose.bones["WristDevice_L"]
    face = (device.head + device.tail) * 0.5
    normal = device.matrix.to_3x3().col[2].normalized()
    ndc = B.camera_ndc(face)
    depth = -face.y
    half_h = depth * math.tan(math.radians(35.0))
    radius_ndc_y = 0.050 / half_h
    shoulder_gap = (face - B.SHOULDER["L"]).length
    return {
        "occluded": sum(hits.values()),
        "samples": sum(total.values()),
        "weighted": weighted_hits / weighted_total,
        "by_region": {r: (hits.get(r, 0), total[r]) for r in sorted(total)},
        "device_ndc": ndc,
        "device_depth": depth,
        "device_radius_ndc_y": radius_ndc_y,
        "face_dot_eye": normal.dot((-face).normalized()),
        "shoulder_gap": shoulder_gap,
    }


def to_target(ndc_x, ndc_y, depth):
    """NDC + depth (Unity camera-local) -> Blender device-face-centre target."""
    half_h = depth * math.tan(math.radians(35.0))
    half_w = half_h * (16.0 / 9.0)
    cam = Vector((ndc_x * half_w, ndc_y * half_h, depth))
    return B.u2b(cam)


CANDIDATES = []
_x = -0.62
while _x <= -0.20 + 1e-9:
    _y = -1.00
    while _y <= -0.68 + 1e-9:
        for depth in (0.28, 0.30, 0.32):
            CANDIDATES.append((round(_x, 3), round(_y, 3), depth))
        _y += 0.04
    _x += 0.04


def device_visibility(ndc, radius_x, radius_y):
    """Fraction of the device's screen footprint that lies inside the frame."""
    inside_x = max(0.0, min(1.0, ndc[0] + radius_x) - max(-1.0, ndc[0] - radius_x))
    inside_y = max(0.0, min(1.0, ndc[1] + radius_y) - max(-1.0, ndc[1] - radius_y))
    return (inside_x / (2.0 * radius_x)) * (inside_y / (2.0 * radius_y))


rows = []
for ndc_x, ndc_y, depth in CANDIDATES:
    result = measure(to_target(ndc_x, ndc_y, depth))
    ndc = result["device_ndc"]
    radius_y = result["device_radius_ndc_y"]
    radius_x = radius_y * (9.0 / 16.0)
    visibility = device_visibility(ndc, radius_x, radius_y)
    result["visibility"] = visibility
    if visibility < 0.60:
        continue
    if abs(ndc[0]) > 0.60:
        continue
    rows.append((ndc_x, ndc_y, depth, result))

rows.sort(key=lambda row: (row[3]["weighted"], -row[3]["visibility"]))

print("TUNE_BEGIN")
print("kept=%d of %d candidates (visibility>=0.60, |ndc_x|<=0.60)" % (len(rows), len(CANDIDATES)))
print("req_x req_y depth | occl/samples weighted visibility | achieved_ndc radiusY depth "
      "dotEye gap | map status panels")
summary = []
for ndc_x, ndc_y, depth, result in rows[:30]:
    achieved = result["device_ndc"]
    map_pair = result["by_region"].get("map", (0, 0))
    status_pair = result["by_region"].get("status", (0, 0))
    panel_hits = sum(v[0] for k, v in result["by_region"].items() if k.startswith("panel_"))
    panel_total = sum(v[1] for k, v in result["by_region"].items() if k.startswith("panel_"))
    print("%+.2f %+.2f %.2f | %3d/%3d %.3f %.2f | (%+.3f,%+.3f) %.3f %.3f %.3f %.3f | "
          "%d/%d %d/%d %d/%d"
          % (ndc_x, ndc_y, depth, result["occluded"], result["samples"],
             result["weighted"], result["visibility"], achieved[0], achieved[1],
             result["device_depth"], result["device_radius_ndc_y"],
             result["face_dot_eye"], result["shoulder_gap"],
             map_pair[0], map_pair[1], status_pair[0], status_pair[1],
             panel_hits, panel_total))
    summary.append({"requested": [ndc_x, ndc_y, depth],
                    "occluded": result["occluded"],
                    "weighted": result["weighted"],
                    "visibility": round(result["visibility"], 4),
                    "device_ndc": [round(v, 4) for v in achieved],
                    "face_dot_eye": result["face_dot_eye"],
                    "shoulder_gap": round(result["shoulder_gap"], 4),
                    "by_region": {k: {"occluded": v[0], "samples": v[1]}
                                  for k, v in result["by_region"].items()}})

with open(os.path.join(B.SCRIPT_DIR, "wrist_hold_sweep.json"), "w", encoding="utf-8") as handle:
    import json
    json.dump(summary, handle, indent=1)
print("TUNE_WROTE", os.path.join(B.SCRIPT_DIR, "wrist_hold_sweep.json"))
HAND_CANDIDATES = [
    (0.20, -0.60, -0.77), (0.10, -0.45, -0.88), (0.05, -0.30, -0.95),
    (0.00, -0.20, -0.98), (-0.05, -0.55, -0.83), (0.15, -0.30, -0.94),
    (-0.10, -0.35, -0.93), (0.05, -0.60, -0.80), (0.20, -0.35, -0.92),
]
for hand in HAND_CANDIDATES:
    B.RAISE_HAND = Vector(hand).normalized()
    B.REST["hold"] = B.solve_device_hold(B.DEVICE_FACE_RAISED, B.SHOULDER["L"],
                                         B.RAISE_POLE)
    result = measure(B.DEVICE_FACE_RAISED)
    map_pair = result["by_region"].get("map", (0, 0))
    status_pair = result["by_region"].get("status", (0, 0))
    panel_hits = sum(v[0] for k, v in result["by_region"].items() if k.startswith("panel_"))
    print("HAND %-24s occluded=%d/%d weighted=%.3f map=%d/%d status=%d/%d panels=%d/%d "
          "dotEye=%.3f"
          % (hand, result["occluded"], result["samples"], result["weighted"],
             map_pair[0], map_pair[1], status_pair[0], status_pair[1], panel_hits, 48,
             result["face_dot_eye"]))
print("HAND_SWEEP_DONE")
print("TUNE_END")
