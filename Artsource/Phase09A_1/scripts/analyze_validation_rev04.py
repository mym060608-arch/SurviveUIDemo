"""SurviveUIDemo -- Phase09A_1 Revision04 validation analysis.

Measures the rendered mask frames (black background => the arm silhouette is
exactly the lit pixels) and the posed geometry, then applies the contract's hard
gates:

  * Wrist_Raise / Wrist_Idle: the left arm enters from the LOWER-LEFT and the
    visible forearm rises at 25-35 degrees (target 30) against the screen bottom
    edge, the compact device stays on the left, and the region the existing Wrist
    UI occupies (above/right) stays clear of arm pixels;
  * FP_Walk look-down: both block arms are visible at the body sides with an
    open centre and an opposite swing;
  * FP_Idle level gaze: the arms may be absent from the frame (contract §9);
  * loops and Root Motion come from asset_report.json.

Run:  blender --background --python scripts/analyze_validation_rev04.py
"""

import json
import math
import os
import sys

import bpy
from mathutils import Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)
import build_fp_arms_rev04 as B  # noqa: E402

VAL_DIR = B.VAL_DIR
OUT = os.path.join(VAL_DIR, "validation_analysis.json")
ASSET_REPORT = os.path.join(VAL_DIR, "asset_report.json")

bpy.ops.wm.open_mainfile(filepath=B.BLEND_PATH)
scene = bpy.context.scene
rig = bpy.data.objects["RIG_FPArms"]

LUMINANCE_THRESHOLD = 0.05
# The emissive terminal material is strongly cyan (base 0.10/0.42/0.50); the
# light suit shell is only faintly blue-tinted, so the margin has to be wide
# enough to tell them apart in the rendered frame.
CYAN_MARGIN = 0.15
CYAN_MARGIN_G = 0.08
# The region the existing hologram occupies (its map disc is centred near
# ndc (-0.06, -0.05) and spans roughly +-0.45 ndc in x and +-0.60 in y).
UI_REGION_NDC = (-0.52, 0.52, -0.62, 0.70)


def load_mask(name):
    path = os.path.join(VAL_DIR, name)
    if not os.path.exists(path):
        return None
    image = bpy.data.images.load(path, check_existing=False)
    width, height = image.size
    pixels = list(image.pixels)
    bpy.data.images.remove(image)
    return {"width": width, "height": height, "pixels": pixels,
            "file": name}


def analyse(mask, ui_region=UI_REGION_NDC):
    """Silhouette metrics of one mask frame (row 0 is the BOTTOM of the image)."""
    width = mask["width"]
    height = mask["height"]
    pixels = mask["pixels"]
    count = 0
    min_x, max_x = width, -1
    min_y, max_y = height, -1
    sum_x = sum_y = 0.0
    left_count = right_count = 0
    centre_count = 0
    ui_region_count = 0
    cyan_count = 0
    cyan_sum_x = cyan_sum_y = 0.0
    points = []
    bottom_entry_x = []
    grid_x, grid_y = 64, 20
    grid = [0] * (grid_x * grid_y)
    for y in range(height):
        row = y * width * 4
        for x in range(width):
            index = row + x * 4
            r = pixels[index]
            g = pixels[index + 1]
            b = pixels[index + 2]
            luminance = 0.299 * r + 0.587 * g + 0.114 * b
            if luminance <= LUMINANCE_THRESHOLD:
                continue
            count += 1
            sum_x += x
            sum_y += y
            min_x = min(min_x, x)
            max_x = max(max_x, x)
            min_y = min(min_y, y)
            max_y = max(max_y, y)
            grid[(y * grid_y // height) * grid_x + (x * grid_x // width)] += 1
            if y < 3:
                bottom_entry_x.append(x)
            nx = x / float(width - 1)
            ny = y / float(height - 1)
            if nx < 0.5:
                left_count += 1
            else:
                right_count += 1
            if 0.40 <= nx <= 0.60:
                centre_count += 1
            # ndc
            ndc_x = nx * 2.0 - 1.0
            ndc_y = ny * 2.0 - 1.0
            if (ui_region[0] <= ndc_x <= ui_region[1]
                    and ui_region[2] <= ndc_y <= ui_region[3]):
                ui_region_count += 1
            if b > r + CYAN_MARGIN and g > r + CYAN_MARGIN_G:
                cyan_count += 1
                cyan_sum_x += x
                cyan_sum_y += y
            if count % 7 == 0:
                points.append((x, y))

    result = {
        "file": mask["file"],
        "size": [width, height],
        "arm_pixels": count,
        "coverage_fraction": round(count / float(width * height), 5),
        "left_pixels": left_count,
        "right_pixels": right_count,
        "centre_band_pixels": centre_count,
        "ui_region_pixels": ui_region_count,
        "ui_region_fraction_of_arms": round(ui_region_count / float(count), 5)
        if count else 0.0,
        "cyan_pixels": cyan_count,
    }
    # Text occupancy map so the composition can be reviewed without an image
    # viewer (row 0 of the grid is the TOP of the frame).
    lines = []
    cell_area = max(1.0, (width / float(grid_x)) * (height / float(grid_y)))
    for gy in range(grid_y - 1, -1, -1):
        line = []
        for gx in range(grid_x):
            density = grid[gy * grid_x + gx] / cell_area
            line.append("." if density <= 0.01 else
                        ":" if density < 0.2 else
                        "+" if density < 0.5 else
                        "*" if density < 0.8 else "#")
        lines.append("".join(line))
    result["occupancy_grid"] = lines
    if count:
        result["centroid_px"] = [round(sum_x / count, 1), round(sum_y / count, 1)]
        result["centroid_viewport"] = [round(sum_x / count / (width - 1), 4),
                                       round(sum_y / count / (height - 1), 4)]
        result["bbox_px"] = [min_x, min_y, max_x, max_y]
        result["bbox_viewport"] = [round(min_x / (width - 1), 4),
                                   round(min_y / (height - 1), 4),
                                   round(max_x / (width - 1), 4),
                                   round(max_y / (height - 1), 4)]
        if bottom_entry_x:
            result["bottom_entry_x_px"] = [min(bottom_entry_x), max(bottom_entry_x)]
            result["bottom_entry_viewport_x"] = [
                round(min(bottom_entry_x) / (width - 1), 4),
                round(max(bottom_entry_x) / (width - 1), 4)]
        if cyan_count:
            result["cyan_centroid_viewport"] = [
                round(cyan_sum_x / cyan_count / (width - 1), 4),
                round(cyan_sum_y / cyan_count / (height - 1), 4)]
        if len(points) >= 8:
            mean_x = sum(p[0] for p in points) / float(len(points))
            mean_y = sum(p[1] for p in points) / float(len(points))
            sxx = sum((p[0] - mean_x) ** 2 for p in points) / len(points)
            syy = sum((p[1] - mean_y) ** 2 for p in points) / len(points)
            sxy = sum((p[0] - mean_x) * (p[1] - mean_y) for p in points) / len(points)
            angle = 0.5 * math.atan2(2.0 * sxy, sxx - syy)
            result["silhouette_pca_angle_deg"] = round(math.degrees(angle), 2)
    return result


def mask_difference(a, b):
    if not a or not b:
        return None
    count = 0
    total = a["width"] * a["height"]
    pa = a["pixels"]
    pb = b["pixels"]
    for i in range(0, total * 4, 4):
        la = 0.299 * pa[i] + 0.587 * pa[i + 1] + 0.114 * pa[i + 2]
        lb = 0.299 * pb[i] + 0.587 * pb[i + 1] + 0.114 * pb[i + 2]
        if abs(la - lb) > 0.05:
            count += 1
    return round(count / float(total), 5)


def raise_frame_profile():
    """Per-frame visibility of the left arm through Wrist_Raise."""
    rig.animation_data.action = bpy.data.actions["Wrist_Raise"]
    profile = []
    for frame in range(1, 26):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        arm = rig.pose.bones["Arm_L"]
        head = arm.head.copy()
        tail = arm.tail.copy()
        crossing = B.ray_crossing(head, tail, "y", 1.0)
        tail_ndc = B.camera_ndc(tail)
        entry = None
        if crossing is not None:
            entry = [round(v, 4) for v in B.ndc_to_viewport(crossing[1])]
        profile.append({
            "frame": frame,
            "wrist_end_ndc": [round(v, 4) for v in tail_ndc] if tail_ndc else None,
            "entry_viewport": entry,
            "visible_fraction": None if crossing is None
            else round(1.0 - crossing[0], 4),
        })
    return profile


def main():
    with open(ASSET_REPORT, "r", encoding="utf-8") as handle:
        asset = json.load(handle)

    masks = {name: load_mask(name + ".png")
             for name in ("fp_forward_mask", "fp_walk_lookdown_mask",
                          "fp_walk_lookdown_b_mask", "wrist_raise_mid_mask",
                          "wrist_idle_mask")}

    analysis = {
        "generated_by": "scripts/analyze_validation_rev04.py (Phase09A_1 Revision04)",
        "thresholds": {"luminance": LUMINANCE_THRESHOLD, "cyan_margin": CYAN_MARGIN,
                       "ui_region_ndc": list(UI_REGION_NDC)},
        "frames": {name: (analyse(mask) if mask else None)
                   for name, mask in masks.items()},
        "walk_swing_mask_difference": mask_difference(masks["fp_walk_lookdown_mask"],
                                                      masks["fp_walk_lookdown_b_mask"]),
        "raise_frame_profile": raise_frame_profile(),
        "asset_report_composition": asset["composition"],
        "asset_report_loops": asset["loops"],
        "asset_report_root_motion": asset["root_motion"],
    }

    gates = []
    hold = asset["composition"]["hold"]
    frames = analysis["frames"]
    idle = frames["fp_forward_mask"]
    walk = frames["fp_walk_lookdown_mask"]
    raise_mid = frames["wrist_raise_mid_mask"]
    wrist = frames["wrist_idle_mask"]

    def gate(name, passed, detail):
        gates.append({"gate": name, "pass": bool(passed), "detail": detail})

    gate("wrist composition: forearm screen angle 25-35 deg",
         hold["forearm_screen_angle_pass"],
         "geometry %.2f deg" % hold["forearm_screen_angle_deg"])
    gate("wrist composition: enters from the lower-left",
         bool(hold["entry_is_lower_left"]),
         "entry viewport %s" % (hold["entry_viewport"],))
    gate("wrist composition: device left of the UI pivot",
         bool(hold["device_left_of_ui_pivot"]),
         "device ndc %s vs ui pivot ndc %s" % (hold["device_center_ndc"],
                                               hold["ui_pivot_ndc"]))
    gate("wrist composition: device below the UI display centre",
         bool(hold["device_below_ui_display_center"]),
         "device ndc %s vs ui centre ndc %s" % (hold["device_center_ndc"],
                                                hold["ui_display_center_ndc"]))
    gate("idle level gaze: arms may be outside the frame",
         idle is not None and idle["arm_pixels"] == 0,
         "arm pixels %s" % (idle["arm_pixels"] if idle else "missing"))
    gate("walk look-down: both arms visible",
         bool(walk and walk["arm_pixels"] > 4000
              and walk["left_pixels"] > 500 and walk["right_pixels"] > 500),
         "pixels=%s left=%s right=%s" % (walk["arm_pixels"] if walk else None,
                                         walk["left_pixels"] if walk else None,
                                         walk["right_pixels"] if walk else None))
    gate("walk look-down: centre stays open",
         bool(walk and walk["centre_band_pixels"] <= walk["arm_pixels"] * 0.02),
         "centre band pixels %s" % (walk["centre_band_pixels"] if walk else None))
    gate("walk look-down: opposite swing between the two frames",
         bool(analysis["walk_swing_mask_difference"]
              and analysis["walk_swing_mask_difference"] > 0.004),
         "mask difference %s" % analysis["walk_swing_mask_difference"])
    gate("wrist raise mid frame: arm already entering the frame",
         bool(raise_mid and raise_mid["arm_pixels"] > 3000),
         "pixels %s" % (raise_mid["arm_pixels"] if raise_mid else None))
    gate("wrist idle (rendered): arm visible",
         bool(wrist and wrist["arm_pixels"] > 8000),
         "pixels %s" % (wrist["arm_pixels"] if wrist else None))
    gate("wrist idle (rendered): silhouette confined to the lower-left",
         bool(wrist and wrist["bbox_viewport"][2] < 0.55
              and wrist["bbox_viewport"][3] < 0.60),
         "bbox viewport %s" % (wrist["bbox_viewport"] if wrist else None))
    gate("wrist idle (rendered): UI region stays clear of arm pixels",
         bool(wrist and wrist["ui_region_fraction_of_arms"] < 0.02),
         "arms inside the UI region %s%%"
         % (round(wrist["ui_region_fraction_of_arms"] * 100.0, 3) if wrist else None))
    gate("wrist idle (rendered): cyan device visible on the left",
         bool(wrist and wrist["cyan_pixels"] > 150
              and wrist.get("cyan_centroid_viewport", [1.0])[0] < 0.45),
         "cyan pixels %s centroid %s"
         % (wrist["cyan_pixels"] if wrist else None,
            wrist.get("cyan_centroid_viewport") if wrist else None))
    gate("wrist idle (rendered): silhouette rises to the right",
         bool(wrist and wrist.get("silhouette_pca_angle_deg", -90.0) > 0.0),
         "pca angle %s" % (wrist.get("silhouette_pca_angle_deg") if wrist else None))
    for loop in asset["loops"]:
        gate("loop %s returns to its first pose" % loop["clip"],
             loop["max_element_deviation"] <= 1.0e-6,
             "deviation %s" % loop["max_element_deviation"])
    gate("Root Motion is OFF in every clip",
         asset["root_motion"]["root_motion"] == "off",
         "max deviation %s"
         % asset["root_motion"]["max_root_basis_deviation_from_identity"])

    analysis["gates"] = gates
    analysis["gates_passed"] = sum(1 for g in gates if g["pass"])
    analysis["gates_total"] = len(gates)
    analysis["result"] = "PASS" if all(g["pass"] for g in gates) else "FAIL"

    with open(OUT, "w", encoding="utf-8") as handle:
        json.dump(analysis, handle, indent=1, sort_keys=False)

    print("ANALYSIS", OUT)
    for gate_entry in gates:
        print(("PASS" if gate_entry["pass"] else "FAIL"), "::",
              gate_entry["gate"], "::", gate_entry["detail"])
    print("RESULT", analysis["result"], analysis["gates_passed"],
          "/", analysis["gates_total"])


if __name__ == "__main__":
    main()
