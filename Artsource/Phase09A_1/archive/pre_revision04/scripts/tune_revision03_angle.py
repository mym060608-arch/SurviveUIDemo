"""Sweep raised-left-arm targets for the Revision03 screen composition.

This is an asset-only diagnostic.  It projects the solved Forearm_L bone into
the authoritative 70-degree, 16:9 first-person camera and keeps candidates
whose extended centreline enters through the left edge at a 25-35 degree
screen-space angle.
"""

import math
import os
import sys

from mathutils import Vector

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SCRIPT_DIR)
import build_fp_arms as B  # noqa: E402


def edge_and_angle(elbow, wrist):
    e = B.camera_ndc(elbow)
    w = B.camera_ndc(wrist)
    if not e or not w:
        return None
    dx = w[0] - e[0]
    dy = w[1] - e[1]
    if dx <= 1e-6 or dy <= 1e-6:
        return None
    angle = math.degrees(math.atan2(dy, dx))
    tx = (w[0] + 1.0) / dx
    ty = (w[1] + 1.0) / dy
    if tx <= ty:
        entry = (-1.0, w[1] - tx * dy)
        edge = "left"
    else:
        entry = (w[0] - ty * dx, -1.0)
        edge = "bottom"
    return e, w, angle, edge, entry


rows = []
for nx in (-0.66, -0.60, -0.54, -0.48, -0.42, -0.36):
    for ny in (-0.74, -0.66, -0.58, -0.50, -0.42, -0.34):
        for depth in (0.28, 0.32, 0.36, 0.40, 0.44):
            target = B.ndc_to_blender(nx, ny, depth)
            for px in (0.50, 1.00, 1.50, 2.00, 2.50, 3.00):
                for py in (-1.00, -0.25, 0.50, 1.25, 2.00, 2.75):
                    for pz in (-1.00, -0.50, 0.00, 0.50, 1.00):
                        pole = Vector((px, py, pz))
                        pose = B.solve_device_hold(target, B.SHOULDER["L"], pole)
                        result = edge_and_angle(pose["elbow"], pose["wrist"])
                        if not result:
                            continue
                        elbow_ndc, wrist_ndc, angle, edge, entry = result
                        device_ndc = B.camera_ndc(pose["device_center"])
                        if edge != "left" or not 25.0 <= angle <= 35.0:
                            continue
                        if not (-0.72 <= device_ndc[0] <= -0.32 and
                                -0.82 <= device_ndc[1] <= -0.26):
                            continue
                        score = (abs(angle - 30.0) * 0.04
                                 + abs(device_ndc[0] + 0.52)
                                 + abs(device_ndc[1] + 0.58)
                                 + abs(entry[1] + 0.78) * 0.8
                                 + abs(depth - 0.36) * 0.5)
                        rows.append((score, nx, ny, depth, pole, pose,
                                     elbow_ndc, wrist_ndc, device_ndc,
                                     angle, entry))

rows.sort(key=lambda item: item[0])
print("ANGLE_SWEEP_KEPT", len(rows))
for row in rows[:40]:
    score, nx, ny, depth, pole, pose, e, w, d, angle, entry = row
    print("score=%.4f target=(%+.2f,%+.2f,%.2f) pole=(%+.2f,%+.2f,%+.2f) "
          "angle=%.2f entry=(%+.3f,%+.3f) elbow=(%+.3f,%+.3f) "
          "wrist=(%+.3f,%+.3f) device=(%+.3f,%+.3f)" %
          (score, nx, ny, depth, pole.x, pole.y, pole.z, angle,
           entry[0], entry[1], e[0], e[1], w[0], w[1], d[0], d[1]))
