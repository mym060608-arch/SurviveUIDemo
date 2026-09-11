"""SurviveUIDemo -- Phase09A_1 Revision04
First-person MINIMAL BLOCK arms (Minecraft-like) -- FULL VISUAL RESET.

The Revision02/03 articulated, anatomical hand and arm geometry is intentionally
abandoned.  Revision04 rebuilds the visible asset as deliberately simple
rectangular prisms:

    [ elongated rectangular spacesuit forearm ] + [ short rectangular glove block ]

with a compact left wrist terminal mounted on the distal forearm.

Everything the previous revisions established as a CONTRACT is preserved:

  * authoring frame   : +X = player LEFT, -Y = player FORWARD, +Z = UP
  * validation camera : eye at the origin, looking -Y, +Z up (screen right = -X),
                        vertical FOV 70 deg, 16:9 -- the camera IS the player's eye
  * existing Wrist UI : untouched.  FOV 70, pivot (-0.265, -0.335, 0.69) in Unity
                        camera-local.  The ARM adapts to the UI, never the reverse.
  * clip names        : FP_Idle / FP_Walk / Wrist_Raise / Wrist_Idle /
                        Wrist_Lower / Interact_Right
  * clip ranges       : identical to the shipped contract (30 FPS)
  * Root Motion       : OFF (the Root bone stays at identity in every clip)
  * FBX export        : axis_forward='-Z', axis_up='Y', metre units, no leaf bones,
                        NLA-strip baked animation -- the same Blender -> Unity
                        convention the project already imports.

The Wrist_Raise / Wrist_Idle composition is solved numerically (see
HOLD_AXIS_NDC / SHOULDER): the visible forearm enters the viewport from the
LOWER-LEFT at approximately 30 degrees to the screen bottom edge (25-35 deg
accepted), the wrist device sits on the left, and the holographic UI region stays
above/right of it.

Run:  blender --background --python scripts/build_fp_arms_rev04.py
"""

import json
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

# =====================================================================
# 0. Paths
# =====================================================================
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.dirname(SCRIPT_DIR)
OUT_DIR = os.path.join(BASE, "output")
VAL_DIR = os.path.join(BASE, "validation", "revision04")
for folder in (OUT_DIR, VAL_DIR):
    os.makedirs(folder, exist_ok=True)

BLEND_PATH = os.path.join(OUT_DIR, "FP_PlayerArms.blend")
FBX_PATH = os.path.join(OUT_DIR, "FP_PlayerArms.fbx")
REPORT_PATH = os.path.join(VAL_DIR, "asset_report.json")

# =====================================================================
# 1. Contract constants (unchanged from Revision02/03)
# =====================================================================
FPS = 30
CAM_FOV_DEG = 70.0
CAM_ASPECT = 16.0 / 9.0
TAN_HALF_V = math.tan(math.radians(CAM_FOV_DEG * 0.5))      # 0.70021
TAN_HALF_H = TAN_HALF_V * CAM_ASPECT                        # 1.24481

# Camera-local (Unity) frame of the EXISTING wrist hologram, read from
# Assets/_Project/Scripts/Editor/NereidSurfaceBuilder.cs.  Read-only.
UI_PIVOT_UNITY = Vector((-0.265, -0.335, 0.69))
UI_DISPLAY_OFFSET_UNITY = Vector((0.21, 0.31, 0.0))
UI_DISPLAY_SCALE_UNITY = Vector((1.88, 1.175, 1.0))
UI_DESIGN_W, UI_DESIGN_H = 1280.0, 800.0
UI_DESIGN_CENTER = (640.0, 400.0)
UI_METRES_PER_PX = UI_DISPLAY_SCALE_UNITY.x / UI_DESIGN_W


def u2b(v):
    """Unity camera-local -> Blender authoring space."""
    return Vector((-v[0], -v[2], v[1]))


def u2b_inverse(v):
    """Blender authoring space -> Unity camera-local."""
    return Vector((-v[0], v[2], -v[1]))


UI_PIVOT_BLENDER = u2b(UI_PIVOT_UNITY)
UI_DISPLAY_CENTER_UNITY = UI_PIVOT_UNITY + UI_DISPLAY_OFFSET_UNITY
UI_DISPLAY_CENTER_BLENDER = u2b(UI_DISPLAY_CENTER_UNITY)

# =====================================================================
# 2. Projection helpers (the validation camera IS the player's eye)
# =====================================================================
def to_camera(p):
    """Blender authoring point -> camera coordinates (u right, v up, w forward)."""
    return Vector((-p.x, p.z, -p.y))


def camera_ndc(p):
    """Blender authoring point -> NDC on the 70 deg 16:9 first-person camera."""
    c = to_camera(p)
    if c.z <= 1.0e-4:
        return None
    return Vector((c.x / (TAN_HALF_H * c.z), c.y / (TAN_HALF_V * c.z)))


def ndc_to_blender(ndc_x, ndc_y, depth):
    """Screen NDC + depth in front of the eye -> Blender authoring space."""
    return u2b(Vector((ndc_x * TAN_HALF_H * depth,
                       ndc_y * TAN_HALF_V * depth,
                       depth)))


def ndc_to_viewport(ndc):
    """NDC -> viewport fraction (0..1, y up)."""
    return Vector(((ndc[0] + 1.0) * 0.5, (ndc[1] + 1.0) * 0.5))


def viewport_px(ndc, width=1920.0, height=1080.0):
    v = ndc_to_viewport(ndc)
    return Vector((v.x * width, v.y * height))


def screen_angle_deg(p_from, p_to):
    """Angle of the projected segment against the horizontal screen bottom edge.

    Both points must be in front of the eye (the arm pivot itself sits slightly
    behind the eye plane, so callers measure the VISIBLE portion of the arm).
    In pixels the tangent is exactly d(v/w) / d(u/w) for the 70 deg 16:9 camera,
    so this is the SCREEN-SPACE angle the contract asks about, not a world-axis
    angle.  Returns None when either point is behind the camera plane.
    """
    a = to_camera(p_from)
    b = to_camera(p_to)
    if a.z <= 1.0e-3 or b.z <= 1.0e-3:
        return None
    du = b.x / b.z - a.x / a.z
    dv = b.y / b.z - a.y / a.z
    return math.degrees(math.atan2(dv, du))


def ray_crossing(p_from, p_to, axis, limit):
    """Parameter t in [0,1] where the projected line leaves the viewport.

    axis 'y' checks the bottom edge (ndc_v = -limit), 'x' the left edge
    (ndc_u = -limit).  Returns (t, ndc) or None.
    """
    a = to_camera(p_from)
    b = to_camera(p_to)
    prev_inside = None
    steps = 400
    for i in range(steps + 1):
        t = i / steps
        c = a.lerp(b, t)
        if c.z <= 1.0e-3:
            prev_inside = False
            continue
        ndc = Vector((c.x / (TAN_HALF_H * c.z), c.y / (TAN_HALF_V * c.z)))
        inside = (ndc.x >= -limit and ndc.x <= limit
                  and ndc.y >= -limit and ndc.y <= limit)
        if prev_inside is False and inside:
            # first sample inside the frame: back up one step for the crossing
            t_cross = max(0.0, (i - 1) / steps)
            cc = a.lerp(b, t_cross)
            if cc.z > 1.0e-3:
                return t_cross, Vector((cc.x / (TAN_HALF_H * cc.z),
                                        cc.y / (TAN_HALF_V * cc.z)))
            return t, ndc
        prev_inside = inside
    return None


# =====================================================================
# 3. Design parameters (Revision04 block arms)
# =====================================================================
# The shoulder is the ARM PIVOT of the block arm.  It sits slightly behind and
# below the eye (a real shoulder joint is behind the eye plane) and further out
# than a human shoulder because the visible geometry is a rigid straight block:
# the shallower the required screen angle, the wider the pivot has to be.
SHOULDER = {
    "L": Vector((0.3179, 0.080, -0.180)),
    "R": Vector((-0.3179, 0.080, -0.180)),
}

# Rest (idle / walk) arm direction: hanging at the body side with a mild forward
# tilt, so both block arms enter the frame when the player looks down (contract
# §9) while staying clear of the view at a level gaze.  This is also the
# ARMATURE REST POSE, so FP_Idle is the identity pose.
IDLE_DIR = {
    "L": Vector((0.030, -0.500, -0.866)).normalized(),
    "R": Vector((-0.030, -0.500, -0.866)).normalized(),
}

# Wrist_Raise / Wrist_Idle composition.  The forearm centreline is aimed at this
# screen point at this depth; the arm length is DERIVED from it, so the composed
# pose is exact rather than approximate.
HOLD_AXIS_NDC = (-0.60, -0.76)
HOLD_AXIS_DEPTH = 0.36

# Device facing in the held pose: towards the eye and up, so the terminal face
# and its cyan emitter are readable while the arm is presented.
HOLD_DEVICE_FACING = Vector((-0.35, 0.75, 0.56)).normalized()

# Rest-frame dorsal direction (the face the device is mounted on, and the face
# the glove blocks keep): outward from the body and slightly forward.
REST_DORSAL = {
    "L": Vector((0.62, -0.30, 0.02)).normalized(),
    "R": Vector((-0.62, -0.30, 0.02)).normalized(),
}

BLOCK = {
    "fore_w": 0.104,        # forearm cross-section (lateral)
    "fore_d": 0.104,        # forearm cross-section (dorsal)
    "hand_w": 0.118,
    "hand_d": 0.118,
    "hand_len": 0.165,
    "cuff_w": 0.126,
    "cuff_d": 0.126,
    "cuff_len": 0.046,
    "stripe_w": 0.052,
    "stripe_len": 0.150,
    "stripe_h": 0.010,
    "bevel": 0.006,
}

DEVICE = {
    "standoff": 0.026,      # housing sits on the forearm's dorsal face
    "radius": 0.045,
    "height": 0.026,
    "rim_radius": 0.050,
    "rim_len": 0.028,
    "rim_width": 0.026,
    "rim_height": 0.020,
    "cap_radius": 0.027,
    "cap_height": 0.010,
    "emitter_radius": 0.0145,
    "emitter_height": 0.005,
    "accent_len": 0.030,
    "accent_width": 0.012,
    "accent_height": 0.010,
    "segment_count": 12,
}

# Axis target, derived arm length and the resulting hold direction.
HOLD_AXIS_BLENDER = ndc_to_blender(HOLD_AXIS_NDC[0], HOLD_AXIS_NDC[1], HOLD_AXIS_DEPTH)
ARM_LEN = (HOLD_AXIS_BLENDER - SHOULDER["L"]).length
HOLD_DIR = {
    "L": (HOLD_AXIS_BLENDER - SHOULDER["L"]).normalized(),
    "R": IDLE_DIR["R"],
}

# =====================================================================
# 4. Materials (minimal set, logical slots only)
# =====================================================================
# (name, base colour RGBA, metallic, roughness, emission RGBA, emission strength)
MATERIAL_SPECS = [
    ("MAT_Suit", (0.700, 0.720, 0.735, 1.0), 0.05, 0.42, None, 0.0),
    ("MAT_Glove", (0.105, 0.113, 0.125, 1.0), 0.06, 0.34, None, 0.0),
    ("MAT_Cuff", (0.235, 0.250, 0.270, 1.0), 0.15, 0.40, None, 0.0),
    ("MAT_Accent", (0.720, 0.320, 0.100, 1.0), 0.10, 0.42, None, 0.0),
    ("MAT_Device", (0.520, 0.545, 0.575, 1.0), 0.18, 0.46, None, 0.0),
    ("MAT_DeviceEmissive", (0.035, 0.100, 0.115, 1.0), 0.0, 0.55,
     (0.100, 0.420, 0.500, 1.0), 1.4),
]


def build_materials():
    out = {}
    for name, base, metallic, roughness, emission, strength in MATERIAL_SPECS:
        material = bpy.data.materials.new(name)
        material.use_nodes = True
        nodes = material.node_tree.nodes
        bsdf = nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = base
        bsdf.inputs["Metallic"].default_value = metallic
        bsdf.inputs["Roughness"].default_value = roughness
        if emission is not None:
            if "Emission Color" in bsdf.inputs:
                bsdf.inputs["Emission Color"].default_value = emission
                bsdf.inputs["Emission Strength"].default_value = strength
            else:  # older sockets
                bsdf.inputs["Emission"].default_value = emission
                bsdf.inputs["Emission Strength"].default_value = strength
        out[name] = material
    return out


# =====================================================================
# 5. Geometry builders (rectangular prisms only)
# =====================================================================
class Part:
    """One mesh object assembled from rectangular prisms / low-poly cylinders."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.slots = []

    def slot(self, material_name):
        if material_name not in self.slots:
            self.slots.append(material_name)
        return self.slots.index(material_name)

    @staticmethod
    def frame_matrix(x_axis, y_axis, z_axis):
        matrix = Matrix.Identity(4)
        for row in range(3):
            matrix[row][0] = x_axis[row]
            matrix[row][1] = y_axis[row]
            matrix[row][2] = z_axis[row]
        return matrix

    def box(self, center, size, axes, material, bevel=0.0):
        """Add a rectangular prism: `axes` are the box's local X/Y/Z directions."""
        index = self.slot(material)
        result = bmesh.ops.create_cube(self.bm, size=1.0)
        verts = result["verts"]
        faces = {f for v in verts for f in v.link_faces}
        edges = {e for f in faces for e in f.edges}
        matrix = (Matrix.Translation(Vector(center))
                  @ self.frame_matrix(*axes)
                  @ Matrix.Diagonal(Vector(size).to_4d()))
        bmesh.ops.transform(self.bm, matrix=matrix, verts=verts)
        # Material index is assigned BEFORE the bevel: the bevel inherits the
        # index for the chamfer faces it creates.
        for face in faces:
            face.material_index = index
        if bevel > 0.0:
            try:
                bmesh.ops.bevel(self.bm, geom=list(faces) + list(edges),
                                offset=bevel, segments=1, profile=0.5, affect="EDGES")
            except TypeError:
                bmesh.ops.bevel(self.bm, geom=list(faces) + list(edges),
                                offset=bevel, segments=1)
        return None

    def cylinder(self, center, axis, radius, height, material, segments,
                 bevel=0.0):
        """Add a low-poly cylinder whose own axis is `axis`."""
        index = self.slot(material)
        reference = Vector((0.0, 0.0, 1.0))
        if abs(axis.normalized().dot(reference)) > 0.95:
            reference = Vector((0.0, 1.0, 0.0))
        x_axis = axis.cross(reference).normalized()
        y_axis = axis.cross(x_axis).normalized()
        try:
            result = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False,
                                           segments=segments, radius1=radius,
                                           radius2=radius, depth=height)
        except TypeError:  # very old signature
            result = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False,
                                           segments=segments, diameter1=radius * 2.0,
                                           diameter2=radius * 2.0, depth=height)
        verts = result["verts"]
        faces = {f for v in verts for f in v.link_faces}
        edges = {e for f in faces for e in f.edges}
        matrix = (Matrix.Translation(Vector(center))
                  @ self.frame_matrix(x_axis, y_axis, axis.normalized()))
        bmesh.ops.transform(self.bm, matrix=matrix, verts=verts)
        for face in faces:
            face.material_index = index
        if bevel > 0.0:
            try:
                bmesh.ops.bevel(self.bm, geom=list(faces) + list(edges),
                                offset=bevel, segments=1, profile=0.5, affect="EDGES")
            except TypeError:
                pass
        return None

    def finish(self, materials, collection):
        bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces))
        mesh = bpy.data.meshes.new(self.name)
        for material_name in self.slots:
            mesh.materials.append(materials[material_name])
        self.bm.to_mesh(mesh)
        self.bm.free()
        mesh.uv_layers.new(name="UVMap")
        obj = bpy.data.objects.new(self.name, mesh)
        collection.objects.link(obj)
        project_box_uvs(obj.data)
        return obj


def project_box_uvs(mesh):
    """Deterministic per-face planar UV projection (one UV set, no atlasing)."""
    uv_layer = mesh.uv_layers.active
    if uv_layer is None:
        return
    for polygon in mesh.polygons:
        normal = polygon.normal
        axis = max(range(3), key=lambda i: abs(normal[i]))
        u_index, v_index = ((1, 2), (0, 2), (0, 1))[axis]
        for loop_index in polygon.loop_indices:
            vertex = mesh.vertices[mesh.loops[loop_index].vertex_index].co
            uv_layer.data[loop_index].uv = (vertex[u_index], vertex[v_index])


def frame_axes(axis, dorsal):
    """Orthonormal (lateral, dorsal, axis) frame for one arm block."""
    axis = axis.normalized()
    dorsal = (dorsal - axis * dorsal.dot(axis)).normalized()
    lateral = dorsal.cross(axis).normalized()
    return lateral, dorsal, axis


# =====================================================================
# 6. Rest pose frames (armature rest = the idle hanging pose)
# =====================================================================
REST_FRAME = {}
for _side in ("L", "R"):
    REST_FRAME[_side] = frame_axes(IDLE_DIR[_side], REST_DORSAL[_side])

# Hold frame for the LEFT arm: same axis, device facing the eye/up.
_hold_dorsal = (HOLD_DEVICE_FACING
                - HOLD_DIR["L"] * HOLD_DEVICE_FACING.dot(HOLD_DIR["L"])).normalized()
HOLD_FRAME_L = frame_axes(HOLD_DIR["L"], _hold_dorsal)


def frame_matrix_of(frame):
    lateral, dorsal, axis = frame
    return Matrix((
        (lateral.x, dorsal.x, axis.x),
        (lateral.y, dorsal.y, axis.y),
        (lateral.z, dorsal.z, axis.z),
    ))


REST_BASIS = {side: frame_matrix_of(REST_FRAME[side]) for side in ("L", "R")}
HOLD_BASIS_L = frame_matrix_of(HOLD_FRAME_L)
# World-space rotation that presents the left arm: rest frame -> hold frame.
HOLD_Q = (HOLD_BASIS_L @ REST_BASIS["L"].inverted()).to_quaternion()


def arm_point(side, distance, dorsal_offset=0.0, lateral_offset=0.0):
    """A point on (or offset from) the rest arm axis."""
    lateral, dorsal, axis = REST_FRAME[side]
    return (SHOULDER[side] + axis * distance
            + dorsal * dorsal_offset + lateral * lateral_offset)


DEVICE_AXIAL = -0.062          # distance from the wrist end back up the forearm
DEVICE_CENTER_REST = arm_point("L", ARM_LEN + DEVICE_AXIAL,
                               DEVICE["standoff"] + DEVICE["height"] * 0.5)


# =====================================================================
# 7. Build the meshes
# =====================================================================
def build_forearm(part, side):
    lateral, dorsal, axis = REST_FRAME[side]
    length = ARM_LEN - BLOCK["hand_len"]
    center = SHOULDER[side] + axis * (length * 0.5)
    part.box(center, (BLOCK["fore_w"], BLOCK["fore_d"], length),
             (lateral, dorsal, axis), "MAT_Suit", BLOCK["bevel"])
    # Restrained orange identification stripe on the dorsal face.
    stripe_center = (SHOULDER[side]
                     + axis * (length - BLOCK["stripe_len"] * 0.5 - 0.030)
                     + dorsal * (BLOCK["fore_d"] * 0.5 + BLOCK["stripe_h"] * 0.5 - 0.001))
    part.box(stripe_center,
             (BLOCK["stripe_w"], BLOCK["stripe_h"], BLOCK["stripe_len"]),
             (lateral, dorsal, axis), "MAT_Accent", BLOCK["bevel"] * 0.4)


def build_hand_block(part, side):
    lateral, dorsal, axis = REST_FRAME[side]
    fore_len = ARM_LEN - BLOCK["hand_len"]
    cuff_center = SHOULDER[side] + axis * (fore_len - BLOCK["cuff_len"] * 0.5 + 0.004)
    part.box(cuff_center, (BLOCK["cuff_w"], BLOCK["cuff_d"], BLOCK["cuff_len"]),
             (lateral, dorsal, axis), "MAT_Cuff", BLOCK["bevel"])
    hand_center = SHOULDER[side] + axis * (fore_len + BLOCK["hand_len"] * 0.5)
    part.box(hand_center, (BLOCK["hand_w"], BLOCK["hand_d"], BLOCK["hand_len"]),
             (lateral, dorsal, axis), "MAT_Glove", BLOCK["bevel"] * 1.2)


def build_wrist_device(part):
    """Compact polygonal terminal on the distal LEFT forearm."""
    lateral, dorsal, axis = REST_FRAME["L"]
    base = arm_point("L", ARM_LEN + DEVICE_AXIAL, DEVICE["standoff"])
    part.cylinder(base + dorsal * (DEVICE["height"] * 0.5), dorsal,
                  DEVICE["radius"], DEVICE["height"], "MAT_Device",
                  DEVICE["segment_count"], BLOCK["bevel"] * 0.5)
    # Segmented outer housing: six radial tabs.
    for i in range(6):
        angle = math.radians(60.0 * i + 30.0)
        radial = (lateral * math.cos(angle) + axis * math.sin(angle)).normalized()
        tab_center = (base + dorsal * (DEVICE["height"] * 0.5)
                      + radial * (DEVICE["radius"] + DEVICE["rim_width"] * 0.5 - 0.004))
        part.box(tab_center,
                 (DEVICE["rim_len"], DEVICE["rim_height"], DEVICE["rim_width"]),
                 (radial, dorsal, radial.cross(dorsal).normalized()),
                 "MAT_Cuff", BLOCK["bevel"] * 0.4)
    # Centre cap and the small cyan emitter.
    part.cylinder(base + dorsal * (DEVICE["height"] + DEVICE["cap_height"] * 0.5),
                  dorsal, DEVICE["cap_radius"], DEVICE["cap_height"],
                  "MAT_Device", DEVICE["segment_count"], BLOCK["bevel"] * 0.4)
    part.cylinder(base + dorsal * (DEVICE["height"] + DEVICE["cap_height"]
                                   + DEVICE["emitter_height"] * 0.5 - 0.001),
                  dorsal, DEVICE["emitter_radius"], DEVICE["emitter_height"],
                  "MAT_DeviceEmissive", DEVICE["segment_count"], 0.0)
    # Limited orange detail: two small collars fore/aft of the housing.
    for sign in (1.0, -1.0):
        center = (base + dorsal * (DEVICE["height"] * 0.5)
                  + axis * (sign * (DEVICE["radius"] + DEVICE["accent_len"] * 0.4)))
        part.box(center,
                 (DEVICE["accent_width"], DEVICE["accent_height"], DEVICE["accent_len"]),
                 (lateral, dorsal, axis), "MAT_Accent", BLOCK["bevel"] * 0.4)


# =====================================================================
# 8. Rig -- MINIMAL (Root / Arm_L / WristDevice_L / Arm_R)
# =====================================================================
BONE_NAMES = ("Root", "Arm_L", "Arm_R", "WristDevice_L")


def build_rig(collection):
    armature = bpy.data.armatures.new("RIG_FPArms")
    rig = bpy.data.objects.new("RIG_FPArms", armature)
    collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    bones = armature.edit_bones

    root = bones.new("Root")
    root.head = Vector((0.0, 0.0, 0.0))
    root.tail = Vector((0.0, 0.0, -0.12))

    for side in ("L", "R"):
        bone = bones.new("Arm_" + side)
        bone.head = SHOULDER[side]
        bone.tail = SHOULDER[side] + IDLE_DIR[side] * ARM_LEN
        bone.parent = root

    device = bones.new("WristDevice_L")
    device.head = DEVICE_CENTER_REST - REST_FRAME["L"][1] * 0.030
    device.tail = DEVICE_CENTER_REST + REST_FRAME["L"][1] * 0.030
    device.parent = bones["Arm_L"]

    bpy.ops.object.mode_set(mode="OBJECT")
    for pose_bone in rig.pose.bones:
        pose_bone.rotation_mode = "QUATERNION"
    return rig


def bind_rigid(obj, rig, bone_name):
    """Rigid binding: every vertex follows exactly one bone (no deformation)."""
    group = obj.vertex_groups.new(name=bone_name)
    group.add([v.index for v in obj.data.vertices], 1.0, "REPLACE")
    modifier = obj.modifiers.new("FPArms skin", "ARMATURE")
    modifier.object = rig
    obj.parent = rig


# =====================================================================
# 9. Pose helpers
# =====================================================================
IDENTITY_Q = Quaternion((1.0, 0.0, 0.0, 0.0))


def smoothstep(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def world_to_local_rotation(pose_bone, q_world):
    """Convert an armature-space rotation into the bone's local basis."""
    rest = pose_bone.bone.matrix_local.to_quaternion()
    return rest.inverted() @ q_world @ rest


def set_arm(rig, side, q_world):
    pose_bone = rig.pose.bones["Arm_" + side]
    pose_bone.rotation_quaternion = world_to_local_rotation(pose_bone, q_world)


def key_all(rig, frame):
    """Key every bone.  Root stays at identity (Root Motion OFF, contract §18)."""
    for name in BONE_NAMES:
        pose_bone = rig.pose.bones[name]
        pose_bone.rotation_mode = "QUATERNION"
        if name == "Root":
            pose_bone.location = Vector((0.0, 0.0, 0.0))
            pose_bone.rotation_quaternion = IDENTITY_Q.copy()
            pose_bone.scale = Vector((1.0, 1.0, 1.0))
            pose_bone.keyframe_insert("location", frame=frame)
            pose_bone.keyframe_insert("scale", frame=frame)
        pose_bone.keyframe_insert("rotation_quaternion", frame=frame)


def swing(degrees, axis=Vector((1.0, 0.0, 0.0))):
    return Quaternion(axis, math.radians(degrees))


# =====================================================================
# 10. Animation clips (rigid transform animation only)
# =====================================================================
CLIPS = [
    # name, frame_start, frame_end, loops, usage
    ("FP_Idle", 1, 61, True, "arms resting at the body sides"),
    ("FP_Walk", 1, 41, True, "simple opposite rigid arm swing"),
    ("Wrist_Raise", 1, 25, False, "left arm into the lower-left 30 degree presentation"),
    ("Wrist_Idle", 1, 91, True, "held presentation pose with micro motion"),
    ("Wrist_Lower", 1, 21, False, "reverse of the raise"),
    ("Interact_Right", 1, 31, False, "small rigid right-arm reach (optional)"),
]


def new_action(rig, name):
    if rig.animation_data is None:
        rig.animation_data_create()
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    rig.animation_data.action = action
    return action


def clip_idle(rig):
    action = new_action(rig, "FP_Idle")
    last = CLIPS[0][2]
    for frame in range(1, last + 1):
        t = (frame - 1) / float(last - 1)
        sway = 1.1 * math.sin(2.0 * math.pi * t)
        breathe = 0.7 * math.cos(2.0 * math.pi * t)
        set_arm(rig, "L", swing(sway) @ swing(breathe, Vector((0.0, 1.0, 0.0))))
        set_arm(rig, "R", swing(-sway) @ swing(-breathe, Vector((0.0, 1.0, 0.0))))
        key_all(rig, frame)
    return action


def clip_walk(rig):
    action = new_action(rig, "FP_Walk")
    last = CLIPS[1][2]
    for frame in range(1, last + 1):
        t = (frame - 1) / float(last - 1)
        phase = 2.0 * math.pi * t
        swing_deg = 9.0 * math.sin(phase)
        lift_deg = 1.6 * (1.0 - math.cos(2.0 * phase)) * 0.5
        set_arm(rig, "L", swing(swing_deg + lift_deg * 0.4)
                @ swing(lift_deg, Vector((0.0, 1.0, 0.0))))
        set_arm(rig, "R", swing(-swing_deg + lift_deg * 0.4)
                @ swing(-lift_deg, Vector((0.0, 1.0, 0.0))))
        key_all(rig, frame)
    return action


def clip_raise(rig):
    action = new_action(rig, "Wrist_Raise")
    last = CLIPS[2][2]
    for frame in range(1, last + 1):
        t = (frame - 1) / float(last - 1)
        s = smoothstep(t)
        set_arm(rig, "L", IDENTITY_Q.slerp(HOLD_Q, s))
        set_arm(rig, "R", swing(-2.0 * s))
        key_all(rig, frame)
    return action


def clip_hold(rig):
    action = new_action(rig, "Wrist_Idle")
    last = CLIPS[3][2]
    for frame in range(1, last + 1):
        t = (frame - 1) / float(last - 1)
        micro = 0.9 * math.sin(2.0 * math.pi * t)
        micro2 = 0.6 * math.cos(2.0 * math.pi * t)
        set_arm(rig, "L", HOLD_Q @ swing(micro) @ swing(micro2, Vector((0.0, 1.0, 0.0))))
        set_arm(rig, "R", IDENTITY_Q.copy())
        key_all(rig, frame)
    return action


def clip_lower(rig):
    action = new_action(rig, "Wrist_Lower")
    last = CLIPS[4][2]
    for frame in range(1, last + 1):
        t = (frame - 1) / float(last - 1)
        s = smoothstep(t)
        set_arm(rig, "L", HOLD_Q.slerp(IDENTITY_Q, s))
        set_arm(rig, "R", IDENTITY_Q.copy())
        key_all(rig, frame)
    return action


def clip_interact_right(rig):
    action = new_action(rig, "Interact_Right")
    last = CLIPS[5][2]
    for frame in range(1, last + 1):
        t = (frame - 1) / float(last - 1)
        bump = math.sin(math.pi * t)
        s = smoothstep(bump)
        set_arm(rig, "R", swing(-24.0 * s)
                @ swing(-14.0 * s, Vector((0.0, 1.0, 0.0))))
        set_arm(rig, "L", IDENTITY_Q.copy())
        key_all(rig, frame)
    return action


def build_clips(rig):
    actions = [clip_idle(rig), clip_walk(rig), clip_raise(rig), clip_hold(rig),
               clip_lower(rig), clip_interact_right(rig)]
    # NLA strips so the FBX exporter emits one take per clip (same convention as
    # the shipped Revision02/03 asset).
    rig.animation_data.action = None
    for track in list(rig.animation_data.nla_tracks):
        rig.animation_data.nla_tracks.remove(track)
    for action in actions:
        track = rig.animation_data.nla_tracks.new()
        track.name = action.name
        strip = track.strips.new(action.name, int(action.frame_range[0]), action)
        strip.name = action.name
        track.mute = False
    return actions


# =====================================================================
# 11. Measurement helpers (used by the report and by the render script)
# =====================================================================
def pose_landmarks(rig):
    out = {}
    for bone_name in ("Root", "Arm_L", "WristDevice_L", "Arm_R"):
        pose_bone = rig.pose.bones[bone_name]
        head = pose_bone.head.copy()
        tail = pose_bone.tail.copy()
        out[bone_name] = {
            "head_world": [round(v, 5) for v in head],
            "tail_world": [round(v, 5) for v in tail],
            "head_ndc": _round_ndc(camera_ndc(head)),
            "tail_ndc": _round_ndc(camera_ndc(tail)),
            "head_unity": [round(v, 5) for v in u2b_inverse(head)],
            "tail_unity": [round(v, 5) for v in u2b_inverse(tail)],
        }
    return out


def _round_ndc(ndc):
    return [round(v, 4) for v in ndc] if ndc else None


def device_metrics(rig):
    """Composition metrics for the compact left terminal in the CURRENT pose."""
    pose_bone = rig.pose.bones["WristDevice_L"]
    center = (pose_bone.head + pose_bone.tail) * 0.5
    normal = pose_bone.matrix.to_3x3().col[1].normalized()   # bone Y == device axis
    ndc = camera_ndc(center)
    return center, normal, ndc


def hold_composition(rig):
    """The hard composition gates of contract §22."""
    arm = rig.pose.bones["Arm_L"]
    head = arm.head.copy()
    tail = arm.tail.copy()
    crossing = ray_crossing(head, tail, "y", 1.0)
    device_center, device_normal, device_ndc = device_metrics(rig)
    ui_pivot_ndc = camera_ndc(UI_PIVOT_BLENDER)
    ui_center_ndc = camera_ndc(UI_DISPLAY_CENTER_BLENDER)
    device_to_eye = (Vector((0.0, 0.0, 0.0)) - device_center).normalized()

    # The VISIBLE forearm: from where it crosses into the frame to the wrist end.
    entry_blender = None
    if crossing is not None:
        entry_blender = head.lerp(tail, crossing[0])
    angle = None
    if entry_blender is not None:
        angle = screen_angle_deg(entry_blender, tail)

    out = {
        "forearm_screen_angle_deg": round(angle, 3) if angle is not None else None,
        "forearm_screen_angle_target": 30.0,
        "forearm_screen_angle_accept": [25.0, 35.0],
        "forearm_screen_angle_pass": bool(angle is not None and 25.0 <= angle <= 35.0),
        "forearm_screen_angle_note": "measured from the frame entry point to the "
                                     "wrist end (the visible portion)",
        "arm_head_ndc": _round_ndc(camera_ndc(head)),
        "arm_tail_ndc": _round_ndc(camera_ndc(tail)),
        "entry_point": None,
        "entry_viewport": None,
        "entry_is_lower_left": None,
        "device_center_unity": [round(v, 5) for v in u2b_inverse(device_center)],
        "device_center_ndc": _round_ndc(device_ndc),
        "device_center_viewport": ([round(v, 4) for v in ndc_to_viewport(device_ndc)]
                                   if device_ndc is not None else None),
        "device_face_normal_dot_eye": round(device_normal.dot(device_to_eye), 4),
        "ui_pivot_ndc": _round_ndc(ui_pivot_ndc),
        "ui_display_center_ndc": _round_ndc(ui_center_ndc),
        "device_left_of_ui_pivot": None,
        "device_below_ui_display_center": None,
    }
    if crossing is not None:
        t, ndc = crossing
        viewport = ndc_to_viewport(ndc)
        out["entry_point"] = [round(t, 4), _round_ndc(ndc)]
        out["entry_viewport"] = [round(viewport.x, 4), round(viewport.y, 4)]
        # lower-left: inside the left third and the bottom third of the frame
        out["entry_is_lower_left"] = bool(viewport.x < 0.34 and viewport.y < 0.34)
    if device_ndc is not None and ui_pivot_ndc is not None:
        out["device_left_of_ui_pivot"] = bool(device_ndc.x < ui_pivot_ndc.x)
    if device_ndc is not None and ui_center_ndc is not None:
        out["device_below_ui_display_center"] = bool(device_ndc.y < ui_center_ndc.y)
    return out


def loop_report(rig, action_name, first, last):
    """First/last pose deviation of every keyed channel (0 == clean loop)."""
    rig.animation_data.action = bpy.data.actions[action_name]
    scene = bpy.context.scene
    pose_a = {}
    pose_b = {}
    for frame, store in ((first, pose_a), (last, pose_b)):
        scene.frame_set(frame)
        bpy.context.view_layer.update()
        for name in BONE_NAMES:
            pose_bone = rig.pose.bones[name]
            store[name] = [v for row in pose_bone.matrix for v in row]
    deviation = 0.0
    worst = None
    for name in BONE_NAMES:
        for a, b in zip(pose_a[name], pose_b[name]):
            delta = abs(a - b)
            if delta > deviation:
                deviation = delta
                worst = name
    return {"clip": action_name, "first_frame": first, "last_frame": last,
            "max_element_deviation": round(deviation, 8), "worst_bone": worst}


def root_motion_report(rig):
    worst = 0.0
    for name, first, last, _loop, _usage in CLIPS:
        rig.animation_data.action = bpy.data.actions[name]
        for frame in range(first, last + 1):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            basis = rig.pose.bones["Root"].matrix_basis
            identity = Matrix.Identity(4)
            for row in range(4):
                for col in range(4):
                    worst = max(worst, abs(basis[row][col] - identity[row][col]))
    return {"max_root_basis_deviation_from_identity": round(worst, 8),
            "root_motion": "off" if worst < 1.0e-6 else "NONZERO"}


# =====================================================================
# 12. Export + save + report
# =====================================================================
def export_fbx():
    bpy.ops.export_scene.fbx(
        filepath=FBX_PATH,
        use_selection=False,
        object_types={"MESH", "ARMATURE", "EMPTY"},
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_NONE",
        add_leaf_bones=False,
        use_mesh_modifiers=True,
        bake_anim=True,
        bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=True,
        bake_anim_use_all_bones=True,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0,
        bake_anim_step=1,
        path_mode="AUTO",
    )


def main():
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

    package_root = bpy.data.objects.new("SK_FPArms", None)
    collection.objects.link(package_root)
    package_root.empty_display_type = "PLAIN_AXES"
    package_root.empty_display_size = 0.2

    rig = build_rig(collection)
    rig.parent = package_root

    parts = {
        "GEO_Arm_L": Part("GEO_Arm_L"),
        "GEO_HandBlock_L": Part("GEO_HandBlock_L"),
        "GEO_WristDevice_L": Part("GEO_WristDevice_L"),
        "GEO_Arm_R": Part("GEO_Arm_R"),
        "GEO_HandBlock_R": Part("GEO_HandBlock_R"),
    }
    for side in ("L", "R"):
        build_forearm(parts["GEO_Arm_" + side], side)
        build_hand_block(parts["GEO_HandBlock_" + side], side)
    build_wrist_device(parts["GEO_WristDevice_L"])

    meshes = []
    for name in ("GEO_Arm_L", "GEO_HandBlock_L", "GEO_WristDevice_L",
                 "GEO_Arm_R", "GEO_HandBlock_R"):
        obj = parts[name].finish(materials, collection)
        meshes.append(obj)

    bind_rigid(meshes[0], rig, "Arm_L")
    bind_rigid(meshes[1], rig, "Arm_L")
    bind_rigid(meshes[2], rig, "WristDevice_L")
    bind_rigid(meshes[3], rig, "Arm_R")
    bind_rigid(meshes[4], rig, "Arm_R")

    build_clips(rig)

    # ---- measurements ----
    rig.animation_data.action = bpy.data.actions["Wrist_Idle"]
    scene.frame_set(46)
    bpy.context.view_layer.update()
    hold = hold_composition(rig)
    hold_landmarks = pose_landmarks(rig)

    rig.animation_data.action = bpy.data.actions["FP_Idle"]
    scene.frame_set(1)
    bpy.context.view_layer.update()
    idle_landmarks = pose_landmarks(rig)
    idle_arm = rig.pose.bones["Arm_L"]
    idle_crossing = ray_crossing(idle_arm.head, idle_arm.tail, "y", 1.0)
    idle_wrist = idle_arm.tail.copy()
    idle_visible_angle = None
    idle_entry_viewport = None
    if idle_crossing is not None:
        idle_visible_angle = screen_angle_deg(
            idle_arm.head.lerp(idle_wrist, idle_crossing[0]), idle_wrist)
        idle_entry_viewport = [round(v, 4) for v in ndc_to_viewport(idle_crossing[1])]
    idle_metrics = {
        "forearm_screen_angle_deg": (round(idle_visible_angle, 3)
                                     if idle_visible_angle is not None else None),
        "arm_head_ndc": _round_ndc(camera_ndc(idle_arm.head)),
        "arm_tail_ndc": _round_ndc(camera_ndc(idle_arm.tail)),
        "wrist_end_viewport": ([round(v, 4) for v in
                                ndc_to_viewport(camera_ndc(idle_arm.tail))]
                               if camera_ndc(idle_arm.tail) is not None else None),
        "entry_viewport": idle_entry_viewport,
        "device_center_ndc": _round_ndc(device_metrics(rig)[2]),
        "note": "at a level gaze the arms may be entirely outside the frame "
                "(contract §9); they must appear at the body sides when looking down",
    }

    loops = [loop_report(rig, name, first, last)
             for name, first, last, loop, _usage in CLIPS if loop]
    root_motion = root_motion_report(rig)

    triangles = {obj.name: sum(len(p.vertices) - 2 for p in obj.data.polygons)
                 for obj in meshes}
    bones = []
    for bone in rig.data.bones:
        bones.append({
            "name": bone.name,
            "parent": bone.parent.name if bone.parent else None,
            "head": [round(v, 5) for v in bone.head_local],
            "tail": [round(v, 5) for v in bone.tail_local],
        })

    report = {
        "generated_by": "scripts/build_fp_arms_rev04.py (Phase09A_1 Revision04)",
        "revision": "Revision04 -- full visual reset to Minecraft-like block arms",
        "blender_version": bpy.app.version_string,
        "fps": FPS,
        "units": "metric, 1 Blender unit = 1 m, scale_length = 1.0",
        "authoring_frame": {
            "x": "player LEFT",
            "y": "player FORWARD is -Y",
            "z": "UP",
            "fbx_export": "axis_forward='-Z', axis_up='Y', apply_unit_scale=True",
            "unity_mapping": "UnityX = -BlenderX, UnityY = BlenderZ, UnityZ = -BlenderY",
        },
        "ui_contract": {
            "source": ["Assets/_Project/Scripts/Editor/NereidSurfaceBuilder.cs"],
            "camera_field_of_view_deg": CAM_FOV_DEG,
            "aspect": "16:9",
            "wrist_pivot_camera_local_unity": [round(v, 6) for v in UI_PIVOT_UNITY],
            "wrist_display_offset_unity": [round(v, 6) for v in UI_DISPLAY_OFFSET_UNITY],
            "wrist_display_scale_unity": [round(v, 6) for v in UI_DISPLAY_SCALE_UNITY],
            "wrist_ui_changed": False,
            "note": "The existing Wrist UI is authoritative and untouched; the arm "
                    "was posed to it.",
        },
        "design": {
            "concept": "intentional minimal stylization: rectangular prisms only",
            "arm_pivot_shoulder_blender": [round(v, 5) for v in SHOULDER["L"]],
            "arm_length_m": round(ARM_LEN, 5),
            "blocks": {k: v for k, v in BLOCK.items()},
            "device": {k: v for k, v in DEVICE.items()},
            "fingers": 0,
            "elbow_joint": "none (single rigid block per arm)",
            "bevels": "small chamfers only, no organic rounding",
        },
        "composition": {
            "hold_axis_ndc_target": list(HOLD_AXIS_NDC),
            "hold_axis_depth_m": HOLD_AXIS_DEPTH,
            "hold": hold,
            "idle": idle_metrics,
            "hold_landmarks": hold_landmarks,
            "idle_landmarks": idle_landmarks,
        },
        "bones": bones,
        "bone_count": len(bones),
        "meshes": [{"name": obj.name,
                    "triangles": triangles[obj.name],
                    "materials": [m.name for m in obj.data.materials],
                    "uv_sets": len(obj.data.uv_layers),
                    "vertex_groups": [g.name for g in obj.vertex_groups]}
                   for obj in meshes],
        "triangles_total": sum(triangles.values()),
        "materials": [name for name, *_rest in MATERIAL_SPECS],
        "clips": [{"name": name, "fps": FPS, "frame_start": first, "frame_end": last,
                   "duration_seconds": round((last - first) / float(FPS), 4),
                   "loop": loop, "usage": usage}
                  for name, first, last, loop, usage in CLIPS],
        "loops": loops,
        "root_motion": root_motion,
        "blend": BLEND_PATH,
        "fbx": FBX_PATH,
        "unity_integration": "NOT performed (asset-only task)",
    }

    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    export_fbx()

    with open(REPORT_PATH, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=1, sort_keys=False)

    print("BUILD_OK blend=%s fbx=%s" % (os.path.getsize(BLEND_PATH),
                                        os.path.getsize(FBX_PATH)))
    print("COMPOSITION_ANGLE_DEG", hold["forearm_screen_angle_deg"])
    print("COMPOSITION_ENTRY_VIEWPORT", hold["entry_viewport"])
    print("COMPOSITION_DEVICE_NDC", hold["device_center_ndc"])
    print("TRIANGLES_TOTAL", report["triangles_total"])
    print("BONES", [b["name"] for b in bones])
    print("ROOT_MOTION", root_motion)


if __name__ == "__main__":
    main()
