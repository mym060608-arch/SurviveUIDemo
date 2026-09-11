"""
SurviveUIDemo -- Phase09A_1 Revision03
First-person EVA arms + left survival terminal: geometry, rig, animation and export.

Run:
    blender --background --python scripts/build_fp_arms.py

--------------------------------------------------------------------------
AUTHORING FRAME (Blender, metres, 1 Blender unit = 1 m)
--------------------------------------------------------------------------
    +X = player's LEFT
    -Y = player's FORWARD (the direction the player camera looks)
    +Z = UP

That is the frame the project's FBX convention converts into Unity space.
The exporter call at the bottom of this file uses the same settings the project
already uses for ArtSource/MainMenuShip
(``axis_forward='-Z', axis_up='Y', apply_unit_scale=True``), which maps

    Unity X = -Blender X
    Unity Y =  Blender Z
    Unity Z = -Blender Y

A mesh authored facing -Y therefore arrives in Unity facing +Z with +Y up and
the player's LEFT on Unity -X -- exactly where the existing, authoritative
Wrist UI already lives (WristProjectionPivot localPosition.x = -0.265).

--------------------------------------------------------------------------
WRIST UI CONTRACT (read-only; never modified by this task)
--------------------------------------------------------------------------
    Assets/_Project/Scripts/Editor/NereidSurfaceBuilder.cs
        cam.fieldOfView                = 70f          (vertical)
        WristPivotLocalPosition        = (-0.265, -0.335, 0.69)   camera-local
        WristDisplayLocalOffset        = ( 0.21,   0.31,   0.0 )
        WristDisplayLocalScale         = ( 1.88,   1.175,  1.0 )
    Assets/_Project/Scripts/UI/RadialWristElement.cs
        DesignWidth = 1280, DesignHeight = 800, centre (640, 400)
        MapRadius 235, PanelInnerRadius 248, PanelOuterRadius 365

The hologram is a camera-relative plane.  The UI does not chase the arm; this
arm is composed so that the raised wrist device reads as the emitter under the
existing hologram origin.
"""

import json
import math
import os

import bpy
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

TAU = math.tau
rad = math.radians

# =====================================================================
# 0. Paths
# =====================================================================
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.dirname(SCRIPT_DIR)
OUT_DIR = os.path.join(BASE, "output")
VAL_DIR = os.path.join(BASE, "validation", "revision03")
for folder in (OUT_DIR, VAL_DIR):
    os.makedirs(folder, exist_ok=True)

BLEND_PATH = os.path.join(OUT_DIR, "FP_PlayerArms.blend")
FBX_PATH = os.path.join(OUT_DIR, "FP_PlayerArms.fbx")
REPORT_PATH = os.path.join(VAL_DIR, "asset_report.json")

# =====================================================================
# 1. Contract constants
# =====================================================================
FPS = 30
CAM_FOV_DEG = 70.0
CAM_ASPECT = 16.0 / 9.0

# Camera-local (Unity) frame of the EXISTING wrist hologram, read from
# NereidSurfaceBuilder.cs.  Not invented, not modified.
UI_PIVOT_UNITY = Vector((-0.265, -0.335, 0.69))
UI_DISPLAY_OFFSET_UNITY = Vector((0.21, 0.31, 0.0))
UI_DISPLAY_SCALE_UNITY = Vector((1.88, 1.175, 1.0))
UI_DESIGN_W = 1280.0
UI_DESIGN_H = 800.0
UI_DESIGN_CENTER = (640.0, 400.0)


def u2b(v):
    """Unity camera-local -> Blender authoring space."""
    return Vector((-v[0], -v[2], v[1]))


def u2b_inverse(v):
    """Blender authoring space -> Unity camera-local."""
    return Vector((-v[0], v[2], -v[1]))


UI_PIVOT_BLENDER = u2b(UI_PIVOT_UNITY)
UI_METRES_PER_PX = 1.88 / UI_DESIGN_W   # == 1.175 / 800 == 0.00146875


def ui_design_to_blender(px, py):
    """A UI design-space pixel -> Blender authoring space on the hologram plane."""
    local_x = UI_PIVOT_UNITY.x + UI_DISPLAY_OFFSET_UNITY.x \
        + (px - UI_DESIGN_CENTER[0]) * UI_METRES_PER_PX
    local_y = UI_PIVOT_UNITY.y + UI_DISPLAY_OFFSET_UNITY.y \
        - (py - UI_DESIGN_CENTER[1]) * UI_METRES_PER_PX
    local_z = UI_PIVOT_UNITY.z
    return u2b(Vector((local_x, local_y, local_z)))


# =====================================================================
# 2. Body parameters (metres)
# =====================================================================
UPPER_LEN = 0.300
FORE_LEN = 0.275
HAND_LEN = 0.185
DEVICE_STANDOFF = 0.043     # compact terminal mount above the forearm axis
DEVICE_AXIAL = 0.058        # compact face sits fully on the forearm, clear of hand armour
DEVICE_BONE_HALF = 0.034

SHOULDER = {
    "L": Vector((0.225, 0.060, -0.205)),
    "R": Vector((-0.225, 0.060, -0.205)),
}
IDLE_WRIST = {
    "L": Vector((0.255, -0.110, -0.715)),
    "R": Vector((-0.255, -0.110, -0.715)),
}
IDLE_POLE = {
    "L": Vector((0.405, 0.080, -0.510)),
    "R": Vector((-0.405, 0.080, -0.510)),
}
IDLE_HAND = {
    "L": Vector((0.025, -0.120, -0.992)).normalized(),
    "R": Vector((-0.025, -0.120, -0.992)).normalized(),
}

# Wrist_Raise end pose.  Stated in the SAME terms as the UI contract -- screen
# NDC on a 70 deg vertical-FOV 16:9 camera plus a depth in front of the eye --
# so the pose is directly comparable with the hologram pivot, which projects to
# NDC (-0.3085, -0.6934) at z = 0.69 m.
#
# The device cannot sit exactly on the pivot: the hologram plane is 0.81 m from
# the eye, which is past the 0.575 m shoulder-to-wrist reach of any real arm.
# It is therefore placed on the same lower-left screen ray, at a reachable
# 0.30 m, BELOW/LEFT of the hologram origin so the interface floats above and
# to the right of it.  Chosen by scripts/tune_wrist_hold.py, which swept 297
# candidates and ray-tested every UI content sample: this one keeps the device
# 92 % inside the frame while occluding only 3 of 198 UI samples (1.5 %), all of
# them the extreme lower-left edge of the Navigation panel.
HOLD_DEVICE_NDC = (-0.56, -0.80)
HOLD_DEVICE_DEPTH = 0.34


def ndc_to_blender(ndc_x, ndc_y, depth):
    """Screen NDC + depth (Unity camera-local) -> Blender authoring space."""
    half_h = depth * math.tan(math.radians(CAM_FOV_DEG * 0.5))
    half_w = half_h * CAM_ASPECT
    return u2b(Vector((ndc_x * half_w, ndc_y * half_h, depth)))


DEVICE_FACE_RAISED = ndc_to_blender(HOLD_DEVICE_NDC[0], HOLD_DEVICE_NDC[1],
                                    HOLD_DEVICE_DEPTH)
RAISE_POLE = Vector((0.30, 0.30, -0.90))
RAISE_HAND = Vector((0.20, -0.60, -0.77)).normalized()

INTERACT_WRIST_R = Vector((-0.118, -0.400, -0.432))
INTERACT_POLE_R = Vector((-0.30, 0.32, -0.90))
INTERACT_HAND_R = Vector((0.18, -0.80, -0.57)).normalized()

# =====================================================================
# 3. Materials
# =====================================================================
# (name, base colour, metallic, roughness, emission colour, emission strength)
MATERIAL_SPECS = [
    # Graphite pressure glove, flexible bellows and all mechanical gaps.
    ("MAT_Glove", (0.030, 0.038, 0.048), 0.08, 0.56, None, 0.0),
    # Warm off-white pressure-suit shell: the dominant spacesuit read.
    ("MAT_Sleeve", (0.430, 0.445, 0.425), 0.05, 0.60, None, 0.0),
    # Cooler light grey used by rigid armour and the terminal frame.
    ("MAT_DeviceBody", (0.245, 0.275, 0.305), 0.32, 0.35, None, 0.0),
    ("MAT_DeviceAccent", (0.900, 0.245, 0.035), 0.16, 0.38, None, 0.0),
    # Kept deliberately dim and small; the housing, not the glow, leads.
    ("MAT_DeviceEmissive", (0.015, 0.185, 0.250), 0.12, 0.25,
     (0.02, 0.62, 0.82), 1.35),
    # Dedicated restrained green status channel for the upper watch arc.
    ("MAT_DeviceGreen", (0.025, 0.255, 0.120), 0.10, 0.30,
     (0.03, 0.72, 0.28), 1.15),
]
MAT = {spec[0]: index for index, spec in enumerate(MATERIAL_SPECS)}


def build_materials():
    materials = []
    for name, colour, metallic, roughness, emission, strength in MATERIAL_SPECS:
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        mat.diffuse_color = (colour[0], colour[1], colour[2], 1.0)
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = (colour[0], colour[1], colour[2], 1.0)
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        if emission is not None:
            bsdf.inputs["Emission Color"].default_value = (
                emission[0], emission[1], emission[2], 1.0)
            bsdf.inputs["Emission Strength"].default_value = strength
        materials.append(mat)
    return materials


# =====================================================================
# 4. Small maths helpers
# =====================================================================
def smoothstep(e0, e1, x):
    t = min(1.0, max(0.0, (x - e0) / (e1 - e0)))
    return t * t * (3.0 - 2.0 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def vlerp(a, b, t):
    return Vector(a) + (Vector(b) - Vector(a)) * t


def slerp_dir(a, b, t):
    a = Vector(a).normalized()
    b = Vector(b).normalized()
    dot = max(-1.0, min(1.0, a.dot(b)))
    if dot > 0.9999:
        return b
    if dot < -0.9999:
        axis = a.cross(Vector((0.0, 0.0, 1.0)))
        if axis.length < 1e-6:
            axis = a.cross(Vector((0.0, 1.0, 0.0)))
        return (Quaternion(axis.normalized(), math.pi * t) @ a).normalized()
    theta = math.acos(dot)
    return (a * math.sin((1.0 - t) * theta) / math.sin(theta)
            + b * math.sin(t * theta) / math.sin(theta)).normalized()


def perpendicular_to(direction, hint):
    """Component of `hint` perpendicular to `direction`, normalised."""
    direction = Vector(direction).normalized()
    out = Vector(hint) - direction * Vector(hint).dot(direction)
    if out.length < 1e-6:
        fallback = direction.cross(Vector((0.0, 0.0, 1.0)))
        if fallback.length < 1e-6:
            fallback = direction.cross(Vector((0.0, 1.0, 0.0)))
        out = fallback
    return out.normalized()


def frame(origin, ydir, zref):
    """Orthonormal 4x4 with column Y = `ydir` and column Z closest to `zref`."""
    y = Vector(ydir).normalized()
    z = perpendicular_to(y, zref)
    x = y.cross(z)
    m = Matrix.Identity(4)
    for row in range(3):
        m[row][0] = x[row]
        m[row][1] = y[row]
        m[row][2] = z[row]
        m[row][3] = origin[row]
    return m


def two_bone_ik(shoulder, target, pole, l1, l2):
    """Elbow position for a 2-bone chain; the pole vector picks the bend plane."""
    delta = Vector(target) - Vector(shoulder)
    reach = min(max(delta.length, abs(l1 - l2) + 1e-4), (l1 + l2) * 0.9995)
    d = delta.normalized()
    cos_a = (l1 * l1 + reach * reach - l2 * l2) / (2.0 * l1 * reach)
    angle = math.acos(max(-1.0, min(1.0, cos_a)))
    p = perpendicular_to(d, pole)
    direction = d * math.cos(angle) + p * math.sin(angle)
    return Vector(shoulder) + direction * l1


# =====================================================================
# 5. Primitive builders (local space; callers transform)
# =====================================================================
def ring(n, rx, rz, y, phase=0.0):
    return [(rx * math.cos(phase + TAU * i / n), y, rz * math.sin(phase + TAU * i / n))
            for i in range(n)]


def loft(stations, n=12, phase=0.0, cap_start=True, cap_end=True):
    """stations: [(y, rx, rz)] around local +Y.  Returns verts, faces, smooth mask."""
    verts = []
    for y, rx, rz in stations:
        verts.extend(ring(n, rx, rz, y, phase))
    faces, smooth = [], []
    for j in range(len(stations) - 1):
        for i in range(n):
            a, b = j * n + i, j * n + (i + 1) % n
            c, d = (j + 1) * n + (i + 1) % n, (j + 1) * n + i
            faces.append((a, b, c, d))
            smooth.append(True)
    if cap_start:
        faces.append(tuple(reversed(range(n))))
        smooth.append(False)
    if cap_end:
        base = (len(stations) - 1) * n
        faces.append(tuple(base + i for i in range(n)))
        smooth.append(False)
    return verts, faces, smooth


def rect_profile(hw, hh, chamfer):
    """Chamfered rectangle in XY (CCW)."""
    c = min(chamfer, hw * 0.8, hh * 0.8)
    return [
        (hw - c, -hh), (hw, -hh + c), (hw, hh - c), (hw - c, hh),
        (-hw + c, hh), (-hw, hh - c), (-hw, -hh + c), (-hw + c, -hh),
    ]


def shrink_profile(profile, amount):
    cx = sum(p[0] for p in profile) / len(profile)
    cy = sum(p[1] for p in profile) / len(profile)
    out = []
    for x, y in profile:
        dx, dy = x - cx, y - cy
        length = math.hypot(dx, dy)
        if length < 1e-9:
            out.append((x, y))
        else:
            out.append((x - dx / length * amount, y - dy / length * amount))
    return out


def poly_ngon(radius, n, phase=0.0):
    return [(radius * math.cos(phase + TAU * i / n),
             radius * math.sin(phase + TAU * i / n)) for i in range(n)]


def arc_profile(radius, a0, a1, segments):
    return [(radius * math.cos(a0 + (a1 - a0) * i / segments),
             radius * math.sin(a0 + (a1 - a0) * i / segments))
            for i in range(segments + 1)]


def plate(profile, z0, z1, chamfer=0.0):
    """Extrude a 2D profile along Z, optionally with a chamfered rim."""
    layers = []
    if chamfer > 1e-6:
        layers.append((shrink_profile(profile, chamfer), z0))
        layers.append((profile, z0 + chamfer))
        layers.append((profile, z1 - chamfer))
        layers.append((shrink_profile(profile, chamfer), z1))
    else:
        layers.append((profile, z0))
        layers.append((profile, z1))
    n = len(profile)
    verts = []
    for prof, z in layers:
        for x, y in prof:
            verts.append((x, y, z))
    faces, smooth = [], []
    for layer in range(len(layers) - 1):
        for i in range(n):
            a = layer * n + i
            b = layer * n + (i + 1) % n
            c = (layer + 1) * n + (i + 1) % n
            d = (layer + 1) * n + i
            faces.append((a, b, c, d))
            smooth.append(False)
    faces.append(tuple(reversed(range(n))))
    smooth.append(False)
    base = (len(layers) - 1) * n
    faces.append(tuple(base + i for i in range(n)))
    smooth.append(False)
    return verts, faces, smooth


def band(outer, inner, z0, z1, closed=True):
    """Ring band between two concentric profiles, extruded along Z."""
    n = len(outer)
    assert n == len(inner)
    verts = []
    for z in (z0, z1):
        for x, y in outer:
            verts.append((x, y, z))
        for x, y in inner:
            verts.append((x, y, z))

    def idx(layer, k):
        return layer * 2 * n + k

    faces, smooth = [], []
    span = n if closed else n - 1
    for i in range(span):
        j = (i + 1) % n
        faces.append((idx(1, i), idx(1, j), idx(1, n + j), idx(1, n + i)))
        smooth.append(False)
        faces.append((idx(0, n + i), idx(0, n + j), idx(0, j), idx(0, i)))
        smooth.append(False)
        faces.append((idx(0, i), idx(0, j), idx(1, j), idx(1, i)))
        smooth.append(True)
        faces.append((idx(0, n + j), idx(0, n + i), idx(1, n + i), idx(1, n + j)))
        smooth.append(True)
    if not closed:
        faces.append((idx(0, 0), idx(0, n), idx(1, n), idx(1, 0)))
        smooth.append(False)
        faces.append((idx(0, 2 * n - 1), idx(0, n - 1), idx(1, n - 1), idx(1, 2 * n - 1)))
        smooth.append(False)
    return verts, faces, smooth


def band_around_y(outer_xz, inner_xz, y0, y1):
    """Ring band whose axis is local Y; profiles are lists of (x, z)."""
    n = len(outer_xz)
    verts = []
    for y in (y0, y1):
        for x, z in outer_xz:
            verts.append((x, y, z))
        for x, z in inner_xz:
            verts.append((x, y, z))

    def idx(layer, k):
        return layer * 2 * n + k

    faces, smooth = [], []
    for i in range(n):
        j = (i + 1) % n
        faces.append((idx(0, i), idx(0, j), idx(1, j), idx(1, i)))
        smooth.append(True)
        faces.append((idx(0, n + j), idx(0, n + i), idx(1, n + i), idx(1, n + j)))
        smooth.append(True)
        faces.append((idx(1, i), idx(1, j), idx(1, n + j), idx(1, n + i)))
        smooth.append(False)
        faces.append((idx(0, n + i), idx(0, n + j), idx(0, j), idx(0, i)))
        smooth.append(False)
    return verts, faces, smooth


def tube_polyline(points, radii, n=6, phase=0.0):
    """Tube along a 3D polyline."""
    points = [Vector(p) for p in points]
    verts = []
    for index, point in enumerate(points):
        if index == 0:
            tangent = points[1] - points[0]
        elif index == len(points) - 1:
            tangent = points[-1] - points[-2]
        else:
            tangent = points[index + 1] - points[index - 1]
        tangent.normalize()
        zref = Vector((0.0, 0.0, 1.0))
        if abs(tangent.dot(zref)) > 0.95:
            zref = Vector((1.0, 0.0, 0.0))
        side = perpendicular_to(tangent, zref)
        up = tangent.cross(side)
        for i in range(n):
            a = phase + TAU * i / n
            verts.append(tuple(point + side * (radii[index] * math.cos(a))
                               + up * (radii[index] * math.sin(a))))
    faces, smooth = [], []
    for j in range(len(points) - 1):
        for i in range(n):
            a, b = j * n + i, j * n + (i + 1) % n
            c, d = (j + 1) * n + (i + 1) % n, (j + 1) * n + i
            faces.append((a, b, c, d))
            smooth.append(True)
    faces.append(tuple(reversed(range(n))))
    smooth.append(False)
    base = (len(points) - 1) * n
    faces.append(tuple(base + i for i in range(n)))
    smooth.append(False)
    return verts, faces, smooth


def transform_verts(matrix, verts):
    return [tuple(matrix @ Vector(v)) for v in verts]


# =====================================================================
# 6. Weighted mesh accumulator
# =====================================================================
class Part:
    def __init__(self, name):
        self.name = name
        self.verts = []
        self.faces = []
        self.face_mat = []
        self.face_smooth = []
        self.groups = {}

    def add(self, verts, faces, mat_index, weights, smooth=None):
        """weights: {bone: w} for every vertex, or a per-vertex list of dicts."""
        base = len(self.verts)
        self.verts.extend(verts)
        if smooth is None:
            smooth = [False] * len(faces)
        for index, face in enumerate(faces):
            self.faces.append(tuple(base + k for k in face))
            self.face_mat.append(mat_index)
            self.face_smooth.append(bool(smooth[index]))

        if isinstance(weights, dict):
            for bone, weight in weights.items():
                if isinstance(weight, (int, float)):
                    if weight > 0.0:
                        bucket = self.groups.setdefault(bone, {})
                        for i in range(len(verts)):
                            bucket[base + i] = float(weight)
                else:
                    bucket = self.groups.setdefault(bone, {})
                    for i, w in enumerate(weight):
                        if w > 0.0:
                            bucket[base + i] = float(w)
        else:
            for i, entry in enumerate(weights):
                for bone, weight in entry.items():
                    if weight > 0.0:
                        self.groups.setdefault(bone, {})[base + i] = float(weight)
        return base

    def to_object(self, materials, collection):
        mesh = bpy.data.meshes.new(self.name)
        mesh.from_pydata(self.verts, [], self.faces)
        mesh.update()
        for mat in materials:
            mesh.materials.append(mat)
        for index, polygon in enumerate(mesh.polygons):
            polygon.material_index = self.face_mat[index]
            polygon.use_smooth = self.face_smooth[index]
        obj = bpy.data.objects.new(self.name, mesh)
        collection.objects.link(obj)
        for bone, mapping in self.groups.items():
            group = obj.vertex_groups.new(name=bone)
            for vertex_index, weight in mapping.items():
                group.add([vertex_index], weight, "REPLACE")
        return obj


def box_project_uvs(mesh):
    """Cheap deterministic box projection so the FBX always carries a UV set."""
    uv = mesh.uv_layers.new(name="UVMap")
    coords = [v.co for v in mesh.vertices]
    if not coords:
        return
    lo = Vector((min(v.x for v in coords), min(v.y for v in coords),
                 min(v.z for v in coords)))
    hi = Vector((max(v.x for v in coords), max(v.y for v in coords),
                 max(v.z for v in coords)))
    size = hi - lo
    scale = 1.0 / max(size.x, size.y, size.z, 1e-6)
    for polygon in mesh.polygons:
        normal = polygon.normal
        axis = max(range(3), key=lambda i: abs(normal[i]))
        u_axis, v_axis = ((1, 2), (0, 2), (0, 1))[axis]
        for loop_index in polygon.loop_indices:
            co = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            uv.data[loop_index].uv = ((co[u_axis] - lo[u_axis]) * scale,
                                      (co[v_axis] - lo[v_axis]) * scale)


# =====================================================================
# 7. Geometry
# =====================================================================
def forearm_dorsal(side, forearm_dir):
    # Dorsal armour faces generally upward/toward the player's eye in the
    # hanging pose, with a slight outward cant to preserve handedness.
    hint = Vector((0.28 if side == "L" else -0.28, 0.0, 1.0))
    return perpendicular_to(forearm_dir, hint)


def build_forearm(part, side, elbow, wrist, fdir, dorsal):
    """Pressurised EVA sleeve with bellows, suit ring and segmented armour."""
    fr = frame(elbow, fdir, dorsal)
    stations = [
        (-0.145, 0.080, 0.076),
        (-0.075, 0.085, 0.080),
        (-0.015, 0.082, 0.076),
        (0.055, 0.076, 0.070),
        (0.120, 0.071, 0.064),
        (0.178, 0.065, 0.059),
        (0.224, 0.059, 0.054),
        (0.262, 0.055, 0.050),
    ]
    verts, faces, smooth = loft(stations, n=12, phase=TAU / 24.0)
    weights = []
    for y, _rx, _rz in stations:
        w_fore = smoothstep(-0.055, 0.055, y)
        for _ in range(12):
            weights.append({"UpperArm_" + side: 1.0 - w_fore,
                            "Forearm_" + side: w_fore})
    part.add(transform_verts(fr, verts), faces, MAT["MAT_Sleeve"], weights, smooth)

    # Three dark pressure bellows before the rigid wrist ring.
    for centre, radius in ((0.176, 0.0645), (0.194, 0.0625), (0.212, 0.0605)):
        bellows = loft([(centre - 0.006, radius, radius * 0.91),
                       (centre, radius + 0.004, radius * 0.95),
                       (centre + 0.006, radius, radius * 0.91)],
                      n=12, phase=TAU / 24.0, cap_start=False, cap_end=False)
        part.add(transform_verts(fr, bellows[0]), bellows[1], MAT["MAT_Glove"],
                 {"Forearm_" + side: 1.0}, bellows[2])

    # A broad off-white suit ring with graphite gaskets clearly separates glove
    # and sleeve.  This is the most important EVA silhouette cue at the wrist.
    for y0, y1, rx, rz, mat in (
            (0.218, 0.226, 0.061, 0.056, "MAT_Glove"),
            (0.226, 0.252, 0.065, 0.059, "MAT_DeviceBody"),
            (0.252, 0.262, 0.058, 0.053, "MAT_Glove")):
        cuff = loft([(y0, rx, rz), (y1, rx, rz)], n=12, phase=TAU / 24.0,
                    cap_start=False, cap_end=False)
        part.add(transform_verts(fr, cuff[0]), cuff[1], MAT[mat],
                 {"Forearm_" + side: 1.0}, cuff[2])

    # Three separate dorsal hard-shell panels make the forearm read as layered
    # equipment instead of a tube.  Gaps expose the darker pressure layer.
    panel_specs = [
        (-0.040, 0.056, 0.050, 0.078),
        (0.026, 0.050, 0.045, 0.074),
        (0.086, 0.042, 0.039, 0.069),
    ]
    for idx, (cy, half_len, half_w, z0) in enumerate(panel_specs):
        taper = 0.008 if idx == 0 else 0.006
        profile = [(-half_w + taper, -half_len), (half_w - taper, -half_len),
                   (half_w, -half_len + taper), (half_w, half_len - taper),
                   (half_w - taper, half_len), (-half_w + taper, half_len),
                   (-half_w, half_len - taper), (-half_w, -half_len + taper)]
        panel = plate(profile, z0, z0 + 0.010, chamfer=0.0025)
        panel_frame = fr @ Matrix.Translation(Vector((0.0, cy, 0.0)))
        part.add(transform_verts(panel_frame, panel[0]), panel[1],
                 MAT["MAT_DeviceBody"], {"Forearm_" + side: 1.0}, panel[2])

    # Low side rails add a segmented silhouette without tiny greebles.
    for sign in (-1.0, 1.0):
        rail = plate(rect_profile(0.010, 0.072, 0.004), 0.025, 0.038,
                     chamfer=0.002)
        rail_frame = fr @ Matrix.Translation(Vector((sign * 0.061, 0.020, 0.0)))
        part.add(transform_verts(rail_frame, rail[0]), rail[1],
                 MAT["MAT_DeviceBody"], {"Forearm_" + side: 1.0}, rail[2])

    # One restrained longitudinal ID stripe on each arm.
    stripe = plate(rect_profile(0.006, 0.034, 0.002), 0.087, 0.091,
                   chamfer=0.001)
    stripe_frame = fr @ Matrix.Translation(Vector((0.026 if side == "L" else -0.026,
                                                    -0.040, 0.0)))
    part.add(transform_verts(stripe_frame, stripe[0]), stripe[1],
             MAT["MAT_DeviceAccent"], {"Forearm_" + side: 1.0}, stripe[2])


def finger_specs():
    """(bone group, local x, length1, length2, radius, fan deg, curl1, curl2)."""
    return [
        ("Index", 0.0360, 0.044, 0.037, 0.0140, 6.0, 10.0, 20.0),
        ("Fingers", 0.0125, 0.047, 0.041, 0.0146, 2.0, 12.0, 22.0),
        ("Fingers", -0.0125, 0.044, 0.038, 0.0138, -3.0, 14.0, 24.0),
        ("Fingers", -0.0360, 0.036, 0.030, 0.0125, -8.0, 17.0, 27.0),
    ]


def finger_points(fx, l1, l2, fan, c1, c2):
    base = Vector((fx, 0.096, 0.002))
    d1 = Quaternion(Vector((0.0, 0.0, 1.0)), rad(fan)) @ Vector((0.0, 1.0, 0.0))
    d1 = Quaternion(Vector((1.0, 0.0, 0.0)), rad(-c1)) @ d1
    d2 = Quaternion(Vector((1.0, 0.0, 0.0)), rad(-c2)) @ d1
    mid = base + d1 * l1
    tip = mid + d2 * l2
    return base, mid, tip


def build_hand(part, side, wrist, hand_dir, dorsal):
    """Side-aware padded EVA glove with an unmistakable dorsal shell/thumb."""
    fh = frame(wrist, hand_dir, dorsal)
    hand_sign = 1.0 if side == "L" else -1.0

    cuff = loft([(-0.058, 0.057, 0.052), (-0.040, 0.060, 0.055),
                 (-0.020, 0.058, 0.053), (0.004, 0.055, 0.050)],
                n=12, phase=TAU / 24.0, cap_start=False, cap_end=False)
    weights = []
    for y in (-0.058, -0.040, -0.020, 0.004):
        w_hand = smoothstep(-0.050, -0.005, y)
        for _ in range(12):
            weights.append({"Forearm_" + side: 1.0 - w_hand, "Hand_" + side: w_hand})
    part.add(transform_verts(fh, cuff[0]), cuff[1], MAT["MAT_Glove"], weights, cuff[2])

    palm = loft([
        (0.000, 0.055, 0.050),
        (0.030, 0.058, 0.048),
        (0.070, 0.057, 0.043),
        (0.102, 0.052, 0.036),
    ], n=10, phase=TAU / 20.0)
    part.add(transform_verts(fh, palm[0]), palm[1], MAT["MAT_Glove"],
             {"Hand_" + side: 1.0}, palm[2])

    back_profile = [(-0.042, -0.032), (0.042, -0.032), (0.047, -0.020),
                    (0.043, 0.032), (0.031, 0.041), (-0.031, 0.041),
                    (-0.043, 0.032), (-0.047, -0.020)]
    back = plate(back_profile, 0.038, 0.054, chamfer=0.004)
    fh_back = fh @ Matrix.Translation(Vector((0.0, 0.048, 0.0)))
    part.add(transform_verts(fh_back, back[0]), back[1], MAT["MAT_Sleeve"],
             {"Hand_" + side: 1.0}, back[2])

    for group, fx, l1, l2, radius, fan, c1, c2 in finger_specs():
        fx *= hand_sign
        fan *= hand_sign
        base, mid, tip = finger_points(fx, l1, l2, fan, c1, c2)
        d1 = (mid - base).normalized()
        d2 = (tip - mid).normalized()
        points = [base, mid - d1 * 0.004, mid, tip - d2 * 0.004, tip]
        radii = [radius, radius * 0.95, radius * 0.92, radius * 0.82, radius * 0.55]
        verts, faces, smooth = tube_polyline(points, radii, n=6)
        station_weights = [
            {group + "_" + side: 1.0},
            {group + "_" + side: 1.0},
            {group + "_" + side: 0.5, group + "_02_" + side: 0.5},
            {group + "_02_" + side: 1.0},
            {group + "_02_" + side: 1.0},
        ]
        per_vertex = []
        for entry in station_weights:
            per_vertex.extend([dict(entry)] * 6)
        part.add(transform_verts(fh, verts), faces, MAT["MAT_Glove"], per_vertex, smooth)

        knuckle = plate(rect_profile(radius * 0.88, 0.012, 0.003),
                        radius * 0.76, radius * 1.22, chamfer=0.0018)
        fh_knuckle = fh @ Matrix.Translation(base + d1 * (l1 * 0.45))
        part.add(transform_verts(fh_knuckle, knuckle[0]), knuckle[1], MAT["MAT_Sleeve"],
                 {group + "_" + side: 1.0}, knuckle[2])

    # Revision02 correction: the right glove uses the opposite local lateral
    # side for both its index edge and thumb chain.  This is authored in the
    # mesh and repeated in the rig below; it is not a normals-only workaround.
    tb = Vector((hand_sign * 0.049, 0.034, -0.012))
    td1 = Vector((hand_sign * 0.72, 0.57, -0.39)).normalized()
    td2 = Vector((hand_sign * 0.55, 0.52, -0.65)).normalized()
    tp1 = tb + td1 * 0.041
    tp2 = tp1 + td2 * 0.034
    points = [tb, tp1 - td1 * 0.004, tp1, tp2 - td2 * 0.004, tp2]
    radii = [0.0170, 0.0160, 0.0148, 0.0130, 0.0088]
    verts, faces, smooth = tube_polyline(points, radii, n=6)
    station_weights = [
        {"Thumb_" + side: 1.0},
        {"Thumb_" + side: 1.0},
        {"Thumb_" + side: 0.5, "Thumb_02_" + side: 0.5},
        {"Thumb_02_" + side: 1.0},
        {"Thumb_02_" + side: 1.0},
    ]
    per_vertex = []
    for entry in station_weights:
        per_vertex.extend([dict(entry)] * 6)
    part.add(transform_verts(fh, verts), faces, MAT["MAT_Glove"], per_vertex, smooth)

    # Reinforced thumb saddle and small right-hand ID plate make dorsal/palm
    # orientation legible in first-person review.
    saddle = plate(rect_profile(0.016, 0.018, 0.004), 0.025, 0.040, chamfer=0.003)
    saddle_frame = fh @ Matrix.Translation(Vector((hand_sign * 0.039, 0.032, 0.0)))
    part.add(transform_verts(saddle_frame, saddle[0]), saddle[1],
             MAT["MAT_DeviceBody"], {"Thumb_" + side: 1.0}, saddle[2])
    if side == "R":
        id_mark = plate(rect_profile(0.005, 0.017, 0.0015), 0.0535, 0.057,
                        chamfer=0.001)
        mark_frame = fh @ Matrix.Translation(Vector((0.024, 0.047, 0.0)))
        part.add(transform_verts(mark_frame, id_mark[0]), id_mark[1],
                 MAT["MAT_DeviceAccent"], {"Hand_R": 1.0}, id_mark[2])


def build_wrist_device(part, device_center, fdir, dorsal):
    """Revision03 compact terminal: main screen, 4 status panes and 2 buttons.

    The face is rebuilt instead of uniformly scaling Revision02.  It has no
    carrier slab and no large mechanical ring; every surrounding element is a
    shallow inset, short separator, or small side control close to the screen.
    """
    fd = frame(device_center, fdir, dorsal)
    weights = {"WristDevice_L": 1.0}

    # A narrow flexible clamp integrates the terminal into the forearm armour.
    # Profiles are centred on the wrist axis (the device frame origin is
    # already DEVICE_STANDOFF above that axis), so the clamp cannot pass over
    # and occlude the thin screen assembly.
    outer = [(0.050 * math.cos(TAU * i / 12),
              -DEVICE_STANDOFF + 0.041 * math.sin(TAU * i / 12))
             for i in range(12)]
    inner = [(0.044 * math.cos(TAU * i / 12),
              -DEVICE_STANDOFF + 0.035 * math.sin(TAU * i / 12))
             for i in range(12)]
    strap = band_around_y(outer, inner, -0.027, 0.027)
    part.add(transform_verts(fd, strap[0]), strap[1], MAT["MAT_Glove"], weights, strap[2])

    # Thin cut-corner chassis: only slightly larger than its central screen.
    chassis = plate(rect_profile(0.042, 0.048, 0.008), -0.002, 0.007,
                    chamfer=0.0025)
    part.add(transform_verts(fd, chassis[0]), chassis[1], MAT["MAT_DeviceBody"],
             weights, chassis[2])
    face_inset = plate(rect_profile(0.038, 0.044, 0.007), 0.007, 0.010,
                       chamfer=0.002)
    part.add(transform_verts(fd, face_inset[0]), face_inset[1], MAT["MAT_Glove"],
             weights, face_inset[2])

    # Central main display is the dominant visual element and a dedicated
    # material region suitable for later Unity emission/RenderTexture use.
    main_bezel = plate(rect_profile(0.0295, 0.0305, 0.006), 0.010, 0.013,
                       chamfer=0.002)
    part.add(transform_verts(fd, main_bezel[0]), main_bezel[1], MAT["MAT_DeviceBody"],
             weights, main_bezel[2])
    main_screen = plate(rect_profile(0.0260, 0.0270, 0.005), 0.013, 0.0155,
                        chamfer=0.0015)
    part.add(transform_verts(fd, main_screen[0]), main_screen[1],
             MAT["MAT_DeviceEmissive"], weights, main_screen[2])

    # Four thin cyan function panes echo the sketch without forming a heavy
    # segmented frame.  Their shallow graphite bezels remain close to the face.
    for x, y, angle_deg in ((-0.0325, 0.0335, -12.0),
                            (0.0325, 0.0335, 12.0),
                            (-0.0325, -0.0335, 12.0),
                            (0.0325, -0.0335, -12.0)):
        pane_frame = fd @ Matrix.Translation(Vector((x, y, 0.0))) \
                     @ Matrix.Rotation(rad(angle_deg), 4, "Z")
        bezel = plate(rect_profile(0.0080, 0.0065, 0.0018), 0.010, 0.013,
                      chamfer=0.001)
        part.add(transform_verts(pane_frame, bezel[0]), bezel[1], MAT["MAT_DeviceBody"],
                 weights, bezel[2])
        pane = plate(rect_profile(0.0062, 0.0048, 0.0012), 0.013, 0.015,
                     chamfer=0.0008)
        part.add(transform_verts(pane_frame, pane[0]), pane[1],
                 MAT["MAT_DeviceEmissive"], weights, pane[2])

    # Thin top/bottom status arcs are display marks, never armour segments.
    upper_arc = band(arc_profile(0.040, rad(233.0), rad(307.0), 7),
                     arc_profile(0.0365, rad(237.0), rad(303.0), 7),
                     0.0125, 0.0152, closed=False)
    part.add(transform_verts(fd, upper_arc[0]), upper_arc[1],
             MAT["MAT_DeviceGreen"], weights, upper_arc[2])
    lower_arc = band(arc_profile(0.040, rad(53.0), rad(127.0), 7),
                     arc_profile(0.0365, rad(57.0), rad(123.0), 7),
                     0.0125, 0.0152, closed=False)
    part.add(transform_verts(fd, lower_arc[0]), lower_arc[1],
             MAT["MAT_DeviceAccent"], weights, lower_arc[2])

    # Four very short protective separators connect the display assembly to
    # the chassis.  They are compact ribs, not an eight-part mechanical cage.
    short_rib = plate(rect_profile(0.0026, 0.0055, 0.001), 0.010, 0.014,
                      chamfer=0.0007)
    for x, y, angle_deg in ((-0.037, 0.0, 90.0), (0.037, 0.0, 90.0),
                            (0.0, -0.042, 0.0), (0.0, 0.042, 0.0)):
        rib_frame = fd @ Matrix.Translation(Vector((x, y, 0.0))) \
                    @ Matrix.Rotation(rad(angle_deg), 4, "Z")
        part.add(transform_verts(rib_frame, short_rib[0]), short_rib[1],
                 MAT["MAT_DeviceBody"], weights, short_rib[2])

    # Fore/aft cylindrical side buttons (local +/-Y along the forearm).  Their
    # orange collars remain readable in side view without widening the body.
    for sign in (-1.0, 1.0):
        y = sign * 0.0505
        bracket = plate(rect_profile(0.010, 0.0035, 0.0015), 0.002, 0.010,
                        chamfer=0.001)
        bracket_frame = fd @ Matrix.Translation(Vector((0.0, y, 0.0)))
        part.add(transform_verts(bracket_frame, bracket[0]), bracket[1],
                 MAT["MAT_Glove"], weights, bracket[2])
        button = tube_polyline([Vector((-0.011, y, 0.0115)),
                               Vector((0.011, y, 0.0115))],
                              [0.0050, 0.0050], n=10, phase=TAU / 20.0)
        part.add(transform_verts(fd, button[0]), button[1], MAT["MAT_DeviceBody"],
                 weights, button[2])
        collar = tube_polyline([Vector((-0.003, y, 0.0115)),
                               Vector((0.003, y, 0.0115))],
                              [0.0056, 0.0056], n=10, phase=TAU / 20.0)
        part.add(transform_verts(fd, collar[0]), collar[1],
                 MAT["MAT_DeviceAccent"], weights, collar[2])


# =====================================================================
# 8. Rig
# =====================================================================
def build_rig(collection, rest):
    armature = bpy.data.armatures.new("FPArms_Skeleton")
    rig = bpy.data.objects.new("RIG_FPArms", armature)
    collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")

    def add_bone(name, head, tail, parent, zref):
        bone = armature.edit_bones.new(name)
        bone.head = head
        bone.tail = tail
        if parent:
            bone.parent = armature.edit_bones[parent]
        bone.use_connect = False
        if (Vector(tail) - Vector(head)).length > 1e-5:
            bone.align_roll(Vector(zref))
        return bone

    add_bone("Root", Vector((0.0, 0.0, 0.0)), Vector((0.0, -0.10, 0.0)), None,
             Vector((0.0, 0.0, 1.0)))

    for side in ("L", "R"):
        s = rest[side]
        hand_sign = 1.0 if side == "L" else -1.0
        add_bone("UpperArm_" + side, s["shoulder"], s["elbow"], "Root", s["dorsal"])
        add_bone("Forearm_" + side, s["elbow"], s["wrist"], "UpperArm_" + side,
                 s["dorsal"])
        hand_tail = s["wrist"] + s["hand_dir"] * HAND_LEN
        add_bone("Hand_" + side, s["wrist"], hand_tail, "Forearm_" + side, s["dorsal"])

        fh = frame(s["wrist"], s["hand_dir"], s["dorsal"])
        # One bone pair per GROUP (Index / Fingers / Thumb).  The grouped
        # middle+ring+pinky geometry is rigidly bound to the single Fingers
        # chain, matching the "Thumb / Index / Remaining Fingers" rig scope.
        seen_groups = set()
        for group, fx, l1, l2, _radius, fan, c1, c2 in finger_specs():
            if group in seen_groups:
                continue
            seen_groups.add(group)
            fx *= hand_sign
            fan *= hand_sign
            base, mid, tip = finger_points(fx, l1, l2, fan, c1, c2)
            add_bone(group + "_" + side, fh @ base, fh @ mid, "Hand_" + side, s["dorsal"])
            add_bone(group + "_02_" + side, fh @ mid, fh @ tip,
                     group + "_" + side, s["dorsal"])

        tb = Vector((hand_sign * 0.049, 0.034, -0.012))
        td1 = Vector((hand_sign * 0.72, 0.57, -0.39)).normalized()
        td2 = Vector((hand_sign * 0.55, 0.52, -0.65)).normalized()
        add_bone("Thumb_" + side, fh @ tb, fh @ (tb + td1 * 0.041),
                 "Hand_" + side, s["dorsal"])
        add_bone("Thumb_02_" + side, fh @ (tb + td1 * 0.041),
                 fh @ (tb + td1 * 0.041 + td2 * 0.034), "Thumb_" + side, s["dorsal"])

    head = rest["L"]["device_center"] - rest["L"]["fdir"] * DEVICE_BONE_HALF
    tail = rest["L"]["device_center"] + rest["L"]["fdir"] * DEVICE_BONE_HALF
    add_bone("WristDevice_L", head, tail, "Forearm_L", rest["L"]["dorsal"])

    bpy.ops.object.mode_set(mode="OBJECT")
    rig.select_set(False)
    rig.show_in_front = True
    return rig


# =====================================================================
# 9. Rest pose resolution
# =====================================================================
EYE = Vector((0.0, 0.0, 0.0))


def solve_device_hold(target_face_center, shoulder, pole, iterations=8):
    """Place the wrist so the DEVICE FACE CENTRE lands on `target_face_center`
    and the device face aims at the eye.  Iterated because the face centre
    depends on the forearm direction, which depends on the wrist position."""
    target = Vector(target_face_center)
    dorsal = perpendicular_to(-target, Vector((0.0, 0.0, 1.0)))
    wrist = target - dorsal * DEVICE_STANDOFF
    elbow = wrist
    fdir = Vector((0.0, 0.0, -1.0))
    for _ in range(iterations):
        elbow = two_bone_ik(shoulder, wrist, pole, UPPER_LEN, FORE_LEN)
        fdir = (wrist - elbow).normalized()
        guess_center = wrist - fdir * DEVICE_AXIAL + dorsal * DEVICE_STANDOFF
        dorsal = perpendicular_to(fdir, (EYE - guess_center).normalized())
        wrist = target + fdir * DEVICE_AXIAL - dorsal * DEVICE_STANDOFF
    elbow = two_bone_ik(shoulder, wrist, pole, UPPER_LEN, FORE_LEN)
    fdir = (wrist - elbow).normalized()
    provisional = wrist - fdir * DEVICE_AXIAL + dorsal * DEVICE_STANDOFF
    dorsal = perpendicular_to(fdir, (EYE - provisional).normalized())
    center = wrist - fdir * DEVICE_AXIAL + dorsal * DEVICE_STANDOFF
    return {"wrist": wrist, "elbow": elbow, "fdir": fdir, "dorsal": dorsal,
            "device_center": center}


def resolve_rest_pose():
    rest = {}
    for side in ("L", "R"):
        shoulder = SHOULDER[side]
        wrist = IDLE_WRIST[side]
        elbow = two_bone_ik(shoulder, wrist, IDLE_POLE[side], UPPER_LEN, FORE_LEN)
        fdir = (wrist - elbow).normalized()
        dorsal = forearm_dorsal(side, fdir)
        rest[side] = {
            "shoulder": shoulder, "elbow": elbow, "wrist": wrist,
            "fdir": fdir, "dorsal": dorsal,
            "hand_dir": IDLE_HAND[side], "pole": IDLE_POLE[side],
        }
    rest["L"]["device_center"] = (rest["L"]["wrist"]
                                  - rest["L"]["fdir"] * DEVICE_AXIAL
                                  + rest["L"]["dorsal"] * DEVICE_STANDOFF)
    rest["hold"] = solve_device_hold(DEVICE_FACE_RAISED, SHOULDER["L"], RAISE_POLE)
    return rest


# =====================================================================
# 10. Posing
# =====================================================================
def set_bone_world(rig, name, head, ydir, zref):
    rig.pose.bones[name].matrix = frame(head, ydir, zref)
    bpy.context.view_layer.update()


def reset_pose(rig):
    for pose_bone in rig.pose.bones:
        pose_bone.rotation_mode = "QUATERNION"
        pose_bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


def curl_sign_for(rig, bone_name):
    """+1/-1 so that a NEGATIVE angle about local X folds the finger palm-ward."""
    bone = rig.data.bones[bone_name]
    y_world = (bone.tail_local - bone.head_local).normalized()
    x_world = bone.matrix_local.to_3x3().col[0].normalized()
    z_world = bone.matrix_local.to_3x3().col[2].normalized()
    rotated = Quaternion(x_world, rad(-20.0)) @ y_world
    return 1.0 if rotated.dot(-z_world) > 0 else -1.0


FINGER_GROUPS = ("Thumb", "Index", "Fingers")


def apply_fingers(rig, side, curl01, index_extend=0.0):
    """curl01: 0 = relaxed open glove, 1 = closed fist."""
    base = {"Thumb": (14.0, 26.0), "Index": (16.0, 26.0), "Fingers": (20.0, 32.0)}
    for group in FINGER_GROUPS:
        c1, c2 = base[group]
        scale = curl01
        if group == "Index" and index_extend > 0.0:
            scale = max(0.0, curl01 - 2.0 * index_extend)
        for suffix, amount in (("", c1 * scale), ("_02", c2 * scale)):
            name = group + suffix + "_" + side
            pose_bone = rig.pose.bones[name]
            pose_bone.rotation_quaternion = Quaternion(
                Vector((1.0, 0.0, 0.0)), rad(CURL_SIGN.get(name, 1.0) * amount))


def arm_state(side, lift, swing, bob, breathe, interact):
    """Resolve the world-space chain targets for one arm."""
    rest = REST[side]
    shoulder = SHOULDER[side] + Vector((0.0, 0.0, bob + breathe))
    hold = REST["hold"]

    if side == "L":
        wrist_target = vlerp(rest["wrist"], hold["wrist"], lift)
        pole = vlerp(rest["pole"], RAISE_POLE, lift)
        hand_dir = slerp_dir(rest["hand_dir"], RAISE_HAND, lift)
        dorsal_hint = slerp_dir(rest["dorsal"], hold["dorsal"], lift)
    else:
        wrist_target = vlerp(rest["wrist"], INTERACT_WRIST_R, interact)
        pole = vlerp(rest["pole"], INTERACT_POLE_R, interact)
        hand_dir = slerp_dir(rest["hand_dir"], INTERACT_HAND_R, interact)
        dorsal_hint = rest["dorsal"]

    radius = wrist_target - shoulder
    radius = Matrix.Rotation(swing, 3, "X") @ radius
    radius = Matrix.Rotation(swing * 0.20, 3, "Z") @ radius
    wrist_target = shoulder + radius

    max_reach = (UPPER_LEN + FORE_LEN) * 0.9995
    if (wrist_target - shoulder).length > max_reach:
        wrist_target = shoulder + (wrist_target - shoulder).normalized() * max_reach

    elbow = two_bone_ik(shoulder, wrist_target, pole, UPPER_LEN, FORE_LEN)
    fdir = (wrist_target - elbow).normalized()
    dorsal = perpendicular_to(fdir, dorsal_hint)
    device_center = wrist_target - fdir * DEVICE_AXIAL + dorsal * DEVICE_STANDOFF
    return {
        "shoulder": shoulder, "elbow": elbow, "wrist": wrist_target,
        "fdir": fdir, "dorsal": dorsal, "hand_dir": hand_dir,
        "device_center": device_center,
    }


def apply_pose(rig, lift, swing_l, swing_r, bob, breathe, curl_l, curl_r,
               interact=0.0, index_extend_r=0.0):
    reset_pose(rig)
    for side, swing in (("L", swing_l), ("R", swing_r)):
        state = arm_state(side, lift if side == "L" else 0.0, swing, bob,
                          breathe, interact)
        set_bone_world(rig, "UpperArm_" + side, state["shoulder"],
                       state["elbow"] - state["shoulder"], state["dorsal"])
        set_bone_world(rig, "Forearm_" + side, state["elbow"],
                       state["wrist"] - state["elbow"], state["dorsal"])
        set_bone_world(rig, "Hand_" + side, state["wrist"],
                       state["hand_dir"], state["dorsal"])
        if side == "L":
            # The terminal is a rigid forearm-mounted part.  Preserve its edit-
            # bone rest transform relative to Forearm_L instead of rebuilding a
            # new roll from a direction vector every frame.  The latter could
            # choose the opposite roll hemisphere and expose the chassis back
            # plate to the camera even though the forearm dorsal normal was
            # correct.  This keeps weights rigid and the face consistently out.
            fore_rest = rig.data.bones["Forearm_L"].matrix_local
            device_rest = rig.data.bones["WristDevice_L"].matrix_local
            relative = fore_rest.inverted() @ device_rest
            rig.pose.bones["WristDevice_L"].matrix = (
                rig.pose.bones["Forearm_L"].matrix @ relative)
    apply_fingers(rig, "L", curl_l)
    apply_fingers(rig, "R", curl_r, index_extend=index_extend_r)
    bpy.context.view_layer.update()


# =====================================================================
# 11. Animation clips
# =====================================================================
# name, frames (inclusive), loop, usage
CLIPS = [
    ("FP_Idle", 61, True, "default first-person resting state"),
    ("FP_Walk", 41, True, "low-intensity exploration locomotion"),
    ("Wrist_Raise", 25, False, "left arm raises the device into the Wrist UI pose"),
    ("Wrist_Idle", 91, True, "hold pose while the Wrist UI is open"),
    ("Wrist_Lower", 21, False, "returns from Wrist_Idle to FP_Idle"),
    ("Interact_Right", 31, False, "small right-hand reach / press (optional)"),
]


def clip_pose(clip, frame):
    """Pose parameters for one frame of one clip."""
    if clip == "FP_Idle":
        t = (frame - 1) / 60.0
        return dict(lift=0.0,
                    swing_l=rad(0.9) * math.sin(TAU * t),
                    swing_r=rad(-0.9) * math.sin(TAU * t),
                    bob=0.0, breathe=0.0045 * math.sin(TAU * t),
                    curl_l=0.18 + 0.02 * math.sin(TAU * t),
                    curl_r=0.18 + 0.02 * math.sin(TAU * t),
                    interact=0.0, index_extend_r=0.0)
    if clip == "FP_Walk":
        t = (frame - 1) / 40.0
        swing = rad(6.5) * math.sin(TAU * t)
        return dict(lift=0.0,
                    swing_l=swing, swing_r=-swing,
                    bob=0.011 * math.sin(2.0 * TAU * t - math.pi * 0.5),
                    breathe=0.0,
                    curl_l=0.20 + 0.04 * math.sin(TAU * t),
                    curl_r=0.20 - 0.04 * math.sin(TAU * t),
                    interact=0.0, index_extend_r=0.0)
    if clip == "Wrist_Raise":
        t = (frame - 1) / 24.0
        # A gentle early forearm arc lets the wrist emerge from below the
        # forward frustum before the final face-to-eye rotation settles.
        lift = min(1.0, smoothstep(0.0, 1.0, t)
                   + 0.28 * math.sin(math.pi * t) ** 2)
        return dict(lift=lift,
                    swing_l=0.0, swing_r=0.0,
                    bob=0.0, breathe=0.0015 * math.sin(TAU * t),
                    curl_l=lerp(0.18, 0.55, smoothstep(0.15, 0.85, t)),
                    curl_r=0.18,
                    interact=0.0, index_extend_r=0.0)
    if clip == "Wrist_Idle":
        t = (frame - 1) / 90.0
        return dict(lift=1.0,
                    swing_l=rad(0.35) * math.sin(TAU * t),
                    swing_r=rad(-0.5) * math.sin(TAU * t),
                    bob=0.0, breathe=0.0022 * math.sin(TAU * t),
                    curl_l=0.55 + 0.02 * math.sin(TAU * t),
                    curl_r=0.18 + 0.03 * math.sin(TAU * t),
                    interact=0.0, index_extend_r=0.0)
    if clip == "Wrist_Lower":
        t = (frame - 1) / 20.0
        return dict(lift=1.0 - smoothstep(0.0, 1.0, t),
                    swing_l=0.0, swing_r=0.0,
                    bob=0.0, breathe=0.0012 * math.sin(TAU * t),
                    curl_l=lerp(0.55, 0.18, smoothstep(0.1, 0.9, t)),
                    curl_r=0.18,
                    interact=0.0, index_extend_r=0.0)
    if clip == "Interact_Right":
        t = (frame - 1) / 30.0
        if t < 0.35:
            amount = smoothstep(0.0, 1.0, t / 0.35)
        elif t < 0.60:
            amount = 1.0
        else:
            amount = 1.0 - smoothstep(0.0, 1.0, (t - 0.60) / 0.40)
        return dict(lift=0.0,
                    swing_l=rad(0.4) * math.sin(TAU * t),
                    swing_r=0.0,
                    bob=0.0, breathe=0.0018 * math.sin(TAU * t),
                    curl_l=0.18,
                    curl_r=lerp(0.18, 0.45, amount),
                    interact=amount,
                    index_extend_r=0.55 * amount)
    raise ValueError(clip)


def build_clips(rig):
    rig.animation_data_create()
    actions = {}
    for name, frames, _loop, _usage in CLIPS:
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        rig.animation_data.action = action
        for frame in range(1, frames + 1):
            apply_pose(rig, **clip_pose(name, frame))
            for pose_bone in rig.pose.bones:
                pose_bone.keyframe_insert("location", frame=frame, group=pose_bone.name)
                pose_bone.keyframe_insert("rotation_quaternion", frame=frame,
                                          group=pose_bone.name)
        actions[name] = action
    rig.animation_data.action = None
    return actions


# =====================================================================
# 12. Validation maths
# =====================================================================
def camera_ndc(point):
    """Blender authoring space -> NDC for the 70 deg validation camera at origin.

    The validation camera sits at the origin looking along -Y with +Z up, so its
    screen-right axis is -X (this is the same handedness the FBX export maps to
    Unity's camera-local frame, where the player's left is -X).
    """
    point = Vector(point)
    depth = -point.y
    if depth <= 1e-6:
        return None
    half_h = depth * math.tan(rad(CAM_FOV_DEG * 0.5))
    half_w = half_h * CAM_ASPECT
    return (-point.x / half_w, point.z / half_h)


def ui_sample_points():
    """Design-space samples of the existing hologram content, in Blender space."""
    points = []
    cx, cy = UI_DESIGN_CENTER
    for radius in (60.0, 120.0, 180.0, 232.0):
        for i in range(24):
            angle = TAU * i / 24.0
            points.append((cx + radius * math.cos(angle),
                           cy + radius * math.sin(angle), "map"))
    for band_name, top, bottom in (("upper", 205.0, 345.0), ("lower", 455.0, 595.0)):
        for side_name, side_sign in (("left", -1.0), ("right", 1.0)):
            for radius in (260.0, 300.0, 340.0, 362.0):
                for py in (top + 8.0, (top + bottom) * 0.5, bottom - 8.0):
                    dy = py - cy
                    if abs(dy) >= radius - 1.0:
                        continue
                    dx = math.sqrt(radius * radius - dy * dy)
                    points.append((cx + side_sign * dx, py,
                                   "panel_" + band_name + "_" + side_name))
    for start_deg in (55.0, 235.0):
        for step in range(0, 9):
            angle = rad(start_deg + 70.0 * step / 8.0)
            for radius in (252.0, 258.0, 261.0):
                points.append((cx + radius * math.cos(angle),
                               cy - radius * math.sin(angle), "status"))
    return [(ui_design_to_blender(px, py), region) for px, py, region in points]


def occlusion_report(rig, clip_name, frame, sample_points, meshes):
    rig.animation_data.action = bpy.data.actions[clip_name]
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    trees = [(obj.name, BVHTree.FromObject(obj, depsgraph)) for obj in meshes]
    hits, total, examples = {}, {}, []
    for index, (world_point, region) in enumerate(sample_points):
        total[region] = total.get(region, 0) + 1
        direction = world_point.normalized()
        distance = world_point.length * 0.995
        blocked = None
        for name, tree in trees:
            location, _normal, _face, _dist = tree.ray_cast(
                Vector((0.0, 0.0, 0.0)), direction, distance)
            if location is not None:
                blocked = name
                break
        if blocked:
            hits[region] = hits.get(region, 0) + 1
            if len(examples) < 8:
                examples.append({"region": region, "sample": index,
                                 "ndc": [round(v, 4) for v in camera_ndc(world_point)],
                                 "blocked_by": blocked})
    return {
        "clip": clip_name,
        "frame": frame,
        "samples": sum(total.values()),
        "occluded": sum(hits.values()),
        "occluded_fraction": round(sum(hits.values()) / max(1, sum(total.values())), 4),
        "by_region": {region: {"samples": total[region],
                               "occluded": hits.get(region, 0)}
                      for region in sorted(total)},
        "examples": examples,
    }


def landmark_report(rig, clip_name, frames):
    rig.animation_data.action = bpy.data.actions[clip_name]
    out = []
    for frame in frames:
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        entry = {"frame": frame, "points": {}}
        for bone_name in ("UpperArm_L", "Forearm_L", "Hand_L", "WristDevice_L",
                          "Forearm_R", "Hand_R"):
            pose_bone = rig.pose.bones[bone_name]
            for label, point in (("head", pose_bone.head.copy()),
                                 ("tail", pose_bone.tail.copy())):
                ndc = camera_ndc(point)
                entry["points"][bone_name + "." + label] = {
                    "world": [round(v, 4) for v in point],
                    "ndc": [round(v, 4) for v in ndc] if ndc else None,
                }
        out.append(entry)
    return out


# =====================================================================
# 13. Main
# =====================================================================
REST = {}
CURL_SIGN = {}


def main():
    global REST, CURL_SIGN

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version = 0
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.render.fps = FPS
    scene.render.fps_base = 1.0
    scene.frame_start = 1
    scene.frame_end = 91

    collection = scene.collection
    materials = build_materials()

    REST = resolve_rest_pose()

    root = bpy.data.objects.new("SK_FPArms", None)
    collection.objects.link(root)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.2

    rig = build_rig(collection, REST)
    rig.parent = root

    parts = {
        "GEO_Arm_L": Part("GEO_Arm_L"),
        "GEO_Hand_L": Part("GEO_Hand_L"),
        "GEO_WristDevice_L": Part("GEO_WristDevice_L"),
        "GEO_Arm_R": Part("GEO_Arm_R"),
        "GEO_Hand_R": Part("GEO_Hand_R"),
    }
    for side in ("L", "R"):
        s = REST[side]
        build_forearm(parts["GEO_Arm_" + side], side, s["elbow"], s["wrist"],
                      s["fdir"], s["dorsal"])
        build_hand(parts["GEO_Hand_" + side], side, s["wrist"], s["hand_dir"],
                   s["dorsal"])
    build_wrist_device(parts["GEO_WristDevice_L"], REST["L"]["device_center"],
                       REST["L"]["fdir"], REST["L"]["dorsal"])

    meshes = []
    for name in ("GEO_Arm_L", "GEO_Hand_L", "GEO_WristDevice_L",
                 "GEO_Arm_R", "GEO_Hand_R"):
        obj = parts[name].to_object(materials, collection)
        box_project_uvs(obj.data)
        bpy.ops.object.select_all(action="DESELECT")
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.normals_make_consistent(inside=False)
        bpy.ops.object.mode_set(mode="OBJECT")
        obj.select_set(False)
        obj.parent = rig
        modifier = obj.modifiers.new("FPArms skin", "ARMATURE")
        modifier.object = rig
        meshes.append(obj)

    CURL_SIGN = {}
    for side in ("L", "R"):
        for group in FINGER_GROUPS:
            for suffix in ("", "_02"):
                bone_name = group + suffix + "_" + side
                CURL_SIGN[bone_name] = curl_sign_for(rig, bone_name)

    actions = build_clips(rig)

    sample_points = ui_sample_points()
    occlusions = []
    for clip_name, frame in (("FP_Idle", 1), ("FP_Walk", 11), ("FP_Walk", 21),
                             ("Wrist_Raise", 19), ("Wrist_Idle", 1),
                             ("Wrist_Idle", 46), ("Wrist_Lower", 1),
                             ("Interact_Right", 16)):
        occlusions.append(occlusion_report(rig, clip_name, frame, sample_points, meshes))

    landmarks = {
        name: landmark_report(rig, name, [1, max(1, (length + 1) // 2), length])
        for name, length, _loop, _usage in CLIPS
    }

    rig.animation_data.action = bpy.data.actions["Wrist_Idle"]
    scene.frame_set(46)
    bpy.context.view_layer.update()
    device_bone = rig.pose.bones["WristDevice_L"]
    device_face_world = (device_bone.head + device_bone.tail) * 0.5
    device_face_normal = device_bone.matrix.to_3x3().col[2].normalized()
    to_eye = (-device_face_world).normalized()
    device_face_unity = u2b_inverse(device_face_world)

    triangles = {obj.name: sum(len(p.vertices) - 2 for p in obj.data.polygons)
                 for obj in meshes}
    materials_used = sorted({obj.data.materials[p.material_index].name
                             for obj in meshes for p in obj.data.polygons})

    report = {
        "generated_by": "scripts/build_fp_arms.py (Phase09A_1 Revision03)",
        "revision": "Revision03 compact watch and body-space presentation",
        "blender_version": bpy.app.version_string,
        "fps": FPS,
        "units": "metric, 1 Blender unit = 1 m, scale_length = 1.0",
        "authoring_frame": {
            "x": "player LEFT",
            "y": "player FORWARD is -Y",
            "z": "UP",
            "fbx_export": "axis_forward='-Z', axis_up='Y', apply_unit_scale=True",
            "unity_mapping": "UnityX = -BlenderX, UnityY = BlenderZ, UnityZ = -BlenderY",
            "unity_mapping_evidence": [
                "MainMenuShip.fbx (this project's existing Blender-built asset) is "
                "exported with identical settings and identical GlobalSettings "
                "(UpAxis=Y, FrontAxis=+Z, CoordAxis=X, UnitScaleFactor=1.0).",
                "In Assets/_Project/Scenes/04_MainMenu.unity the imported model root "
                "'SK_MainMenuShip' carries m_LocalRotation (x=-0.7071068, w=0.7071067) "
                "= -90 deg about X and m_LocalScale (100,100,100), while its child "
                "nodes hold (-BlenderX, +BlenderY, +BlenderZ)*0.01.  Composition "
                "yields Unity = (-Bx, +Bz, -By).",
                "Cross-checked on 8 ship parts, e.g. GEO_EngineSideMark_L at Blender "
                "(-5.925, 6.9, 0.75) -> Unity (0.05925, 0.069, 0.0075).",
                "Consequence: a mesh authored facing -Y arrives in Unity facing +Z "
                "with +Y up, and Blender +X (the player's LEFT) arrives on Unity -X, "
                "which is exactly where WristProjectionPivot lives (x = -0.265).",
            ],
        },
        "ui_contract": {
            "source": ["Assets/_Project/Scripts/Editor/NereidSurfaceBuilder.cs",
                       "Assets/_Project/Scripts/UI/RadialWristElement.cs"],
            "camera_field_of_view_deg": 70.0,
            "wrist_pivot_camera_local": list(UI_PIVOT_UNITY),
            "wrist_display_local_offset": list(UI_DISPLAY_OFFSET_UNITY),
            "wrist_display_local_scale": list(UI_DISPLAY_SCALE_UNITY),
            "radial_design_size": [UI_DESIGN_W, UI_DESIGN_H],
            "ui_pivot_ndc": [round(v, 4) for v in camera_ndc(UI_PIVOT_BLENDER)],
            "ui_pivot_blender": [round(v, 4) for v in UI_PIVOT_BLENDER],
        },
        "objects": {
            "root": root.name,
            "armature": rig.name,
            "meshes": [obj.name for obj in meshes],
            "triangles": triangles,
            "triangles_total": sum(triangles.values()),
            "materials": [spec[0] for spec in MATERIAL_SPECS],
            "materials_used_by_geometry": materials_used,
        },
        "wrist_device_revision03": {
            "construction": "compact cut-corner terminal integrated into left forearm",
            "removed": ["Revision02 long carrier slab", "large circular under-frame",
                        "eight-segment / shield-like outer cage"],
            "main_screen": "dominant independent MAT_DeviceEmissive face region",
            "cyan_function_panels": 4,
            "upper_status_arc": "MAT_DeviceGreen",
            "lower_status_arc": "MAT_DeviceAccent",
            "fore_aft_cylindrical_buttons": 2,
            "revision02_reference_face_width_m": 0.130,
            "revision03_chassis_face_width_m": 0.084,
            "width_reduction_fraction": 0.3538,
            "revision02_reference_face_stack_thickness_m": 0.046,
            "revision03_face_stack_thickness_m": 0.0175,
            "face_stack_thickness_reduction_fraction": 0.6196,
            "rigid_weight_bone": "WristDevice_L",
        },
        "body_space_contract": {
            "rest_pose": "arms hang below the shoulders beside the player body",
            "forward_camera_pitch_deg": 0.0,
            "look_down_camera_pitch_deg": 52.0,
            "arms_root_behavior": "follow player body position/yaw, not eye-camera pitch",
            "fixed_rest_pitch_workaround_allowed": False,
        },
        "bones": [
            {"name": bone.name,
             "parent": bone.parent.name if bone.parent else None,
             "head": [round(v, 5) for v in bone.head_local],
             "tail": [round(v, 5) for v in bone.tail_local]}
            for bone in rig.data.bones
        ],
        "curl_sign": CURL_SIGN,
        "rest_pose": {
            side: {
                "shoulder": [round(v, 5) for v in REST[side]["shoulder"]],
                "elbow": [round(v, 5) for v in REST[side]["elbow"]],
                "wrist": [round(v, 5) for v in REST[side]["wrist"]],
                "wrist_unity_camera": [round(v, 5)
                                       for v in u2b_inverse(REST[side]["wrist"])],
                "device_center": ([round(v, 5) for v in REST[side]["device_center"]]
                                  if "device_center" in REST[side] else None),
            }
            for side in ("L", "R")
        },
        "hold_pose_target": {
            "device_center_blender": [round(v, 5) for v in REST["hold"]["device_center"]],
            "device_center_unity_camera": [
                round(v, 5) for v in u2b_inverse(REST["hold"]["device_center"])],
            "device_center_ndc": [round(v, 4)
                                  for v in camera_ndc(REST["hold"]["device_center"])],
            "wrist_blender": [round(v, 5) for v in REST["hold"]["wrist"]],
            "elbow_blender": [round(v, 5) for v in REST["hold"]["elbow"]],
        },
        "measured_wrist_idle_f46": {
            "device_face_center_blender": [round(v, 5) for v in device_face_world],
            "device_face_center_unity_camera": [round(v, 5) for v in device_face_unity],
            "device_face_center_ndc": [round(v, 4)
                                       for v in camera_ndc(device_face_world)],
            "device_face_normal_world": [round(v, 4) for v in device_face_normal],
            "dot_face_normal_to_eye": round(device_face_normal.dot(to_eye), 4),
            "ui_pivot_minus_device_face_unity_camera": [
                round(v, 5) for v in (UI_PIVOT_UNITY - device_face_unity)],
            "distance_device_face_to_ui_pivot_m": round(
                (UI_PIVOT_UNITY - device_face_unity).length, 5),
        },
        "clips": [
            {"name": name, "fps": FPS, "frame_start": 1, "frame_end": length,
             "duration_seconds": round((length - 1) / FPS, 4), "loop": loop,
             "usage": usage,
             "root_motion": "off (Root bone keyed at identity in every clip)"}
            for name, length, loop, usage in CLIPS
        ],
        "occlusion": occlusions,
        "landmarks": landmarks,
    }

    for name, length, _loop, _usage in CLIPS:
        track = rig.animation_data.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, 1, actions[name])
        strip.action_frame_start = 1
        strip.action_frame_end = length
        track.mute = False

    bpy.ops.object.select_all(action="DESELECT")
    root.select_set(True)
    rig.select_set(True)
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig

    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=True,
        object_types={"MESH", "ARMATURE", "EMPTY"},
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_NONE",
        add_leaf_bones=False,
        use_armature_deform_only=False,
        use_mesh_modifiers=True,
        bake_anim=True,
        bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=True,
        bake_anim_use_all_bones=True,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0,
        bake_anim_step=1,
    )
    report["fbx"] = {
        "path": FBX_PATH,
        "size_bytes": os.path.getsize(FBX_PATH),
        "objects_exported": ["SK_FPArms", "RIG_FPArms"] + [obj.name for obj in meshes],
    }

    for track in rig.animation_data.nla_tracks:
        track.mute = True
    rig.animation_data.action = bpy.data.actions["FP_Idle"]
    scene.frame_set(1)
    bpy.context.view_layer.update()

    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    report["blend"] = {"path": BLEND_PATH, "size_bytes": os.path.getsize(BLEND_PATH)}

    with open(REPORT_PATH, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=1)

    print("BUILD_TRIANGLES_TOTAL", report["objects"]["triangles_total"])
    print("BUILD_DEVICE_FACE_NDC",
          report["measured_wrist_idle_f46"]["device_face_center_ndc"])
    print("BUILD_UI_PIVOT_NDC", report["ui_contract"]["ui_pivot_ndc"])
    print("BUILD_FACE_NORMAL_DOT_EYE",
          report["measured_wrist_idle_f46"]["dot_face_normal_to_eye"])
    print("BUILD_OCCLUSION", [(o["clip"], o["frame"], o["occluded_fraction"])
                              for o in occlusions])
    print("BUILD_REPORT", REPORT_PATH)
    print("ASSET_BUILD_COMPLETE")


if __name__ == "__main__":
    main()
