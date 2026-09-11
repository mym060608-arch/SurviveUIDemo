"""Decisive Blender -> FBX axis mapping test.

Builds a single non-symmetric triangle with known Blender coordinates, exports
it with the exact settings build_fp_arms.py uses, then reads the raw vertex
numbers back out of the binary FBX.  The printed numbers ARE the numbers
Unity's importer consumes.
"""

import os
import struct
import sys

import bpy

addons_root = os.path.join(bpy.utils.resource_path("LOCAL"), "scripts", "addons_core")
if addons_root not in sys.path:
    sys.path.insert(0, addons_root)
import io_scene_fbx.parse_fbx as parse_fbx  # noqa: E402

OUT = "C:/Windows/Temp/_axis_probe.fbx"

# Blender authoring space: +X = player LEFT, -Y = player FORWARD, +Z = UP.
KNOWN = {
    "LEFT_FORWARD_UP": (0.30, -0.50, 0.70),
    "RIGHT_FORWARD_DOWN": (-0.25, -0.40, -0.60),
    "LEFT_BACK_UP": (0.15, 0.20, 0.90),
}


def build(use_space_transform, bake_space_transform):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mesh = bpy.data.meshes.new("PROBE")
    verts = [KNOWN["LEFT_FORWARD_UP"], KNOWN["RIGHT_FORWARD_DOWN"], KNOWN["LEFT_BACK_UP"]]
    mesh.from_pydata(verts, [], [(0, 1, 2)])
    mesh.update()
    obj = bpy.data.objects.new("PROBE_MESH", mesh)
    bpy.context.scene.collection.objects.link(obj)
    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=OUT,
        use_selection=True,
        object_types={"MESH", "ARMATURE", "EMPTY"},
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_NONE",
        add_leaf_bones=False,
        use_mesh_modifiers=True,
        use_space_transform=use_space_transform,
        bake_space_transform=bake_space_transform,
        bake_anim=False,
    )


def read_raw(path):
    root, version = parse_fbx.parse(path)
    out = {}
    for elem in root.elems:
        if elem.id != b"Objects":
            continue
        for child in elem.elems:
            if child.id != b"Geometry":
                continue
            for sub in child.elems:
                if sub.id != b"Vertices":
                    continue
                data = sub.props[0]
                values = None
                for stride, code in ((8, "d"), (4, "f")):
                    if len(data) % stride:
                        continue
                    candidate = struct.unpack("<%d%s" % (len(data) // stride, code), data)
                    if candidate and max(abs(v) for v in candidate) < 1.0e4:
                        values = candidate
                        out["vert_stride"] = stride
                        break
                if values is None:
                    print("    UNPARSED_VERTEX_ARRAY type=%r len=%d"
                          % (sub.props_type[0], len(data)))
                    continue
                out["verts"] = [tuple(round(v, 5) for v in values[i:i + 3])
                                for i in range(0, len(values), 3)]
        for child in elem.elems:
            if child.id != b"Model":
                continue
            name = child.props[1].decode("utf-8", "replace").split("\x00")[0]
            translation = None
            for sub in child.elems:
                if sub.id != b"Properties70":
                    continue
                for prop in sub.elems:
                    vals = list(prop.props)
                    if vals and vals[0] == b"Lcl Translation":
                        translation = tuple(round(float(v), 5) for v in vals[4:7])
            out.setdefault("models", []).append((name, translation))
        for child in elem.elems:
            if child.id != b"GlobalSettings":
                continue
            settings = {}
            for sub in child.elems:
                if sub.id != b"Properties70":
                    continue
                for prop in sub.elems:
                    vals = list(prop.props)
                    if vals and vals[0].startswith(b"UpAxis") or \
                       (vals and vals[0] in (b"FrontAxis", b"CoordAxis",
                                             b"UpAxisSign", b"FrontAxisSign",
                                             b"CoordAxisSign", b"UnitScaleFactor")):
                        settings[vals[0].decode()] = vals[4]
            out["global"] = settings
    return version, out


print("BLENDER_INPUT", KNOWN)
for ust, bst in ((True, False), (False, False), (True, True), (False, True)):
    build(ust, bst)
    version, raw = read_raw(OUT)
    print("--- use_space_transform=%s bake_space_transform=%s (fbx v%d)" % (ust, bst, version))
    print("    raw_vertices:", raw.get("verts"))
    print("    models:", raw.get("models"))
    print("    global:", raw.get("global"))
print("AXIS_PROBE_DONE")
