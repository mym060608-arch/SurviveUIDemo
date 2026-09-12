"""Ground truth for the Blender -> FBX axis mapping.

Reads the raw Numbers stored inside a binary FBX (Geometry Vertices + Model
node Lcl Translation) using Blender's own FBX parser, so the numbers printed
are the bytes Unity's importer will consume, with no round-trip conversion.

Usage:
    blender --background --python fbx_axis_probe.py -- <fbx> [model-name-filter]
"""

import os
import sys

import bpy

addons_root = os.path.join(bpy.utils.resource_path("LOCAL"), "scripts", "addons_core")
if addons_root not in sys.path:
    sys.path.insert(0, addons_root)
import io_scene_fbx.parse_fbx as parse_fbx  # noqa: E402


def props_of(elem):
    return list(zip(elem.props_type, elem.props))


def as_str(value):
    if isinstance(value, bytes):
        return value.split(b"\x00")[0].decode("utf-8", "replace")
    return str(value)


path = sys.argv[sys.argv.index("--") + 1]
name_filter = sys.argv[sys.argv.index("--") + 2] if len(sys.argv) > sys.argv.index("--") + 2 else None

root, version = parse_fbx.parse(path)
print("PROBE_VERSION", version)
print("PROBE_TOPLEVEL", [elem.id.decode("utf-8", "replace") for elem in root.elems])

objects = None
connections = None
for elem in root.elems:
    if elem.id == b"Objects":
        objects = elem
    elif elem.id == b"Connections":
        connections = elem

if objects is None:
    raise SystemExit("no Objects node")

parents = {}
if connections is not None:
    for child in connections.elems:
        values = [value for _kind, value in props_of(child)]
        if len(values) >= 3 and values[0] in (b"OO", b"OP"):
            parents[values[2]] = values[1]


def geometry_bounds(geometry, label):
    for child in geometry.elems:
        if child.id == b"Vertices":
            data = child.props[0]
            count = len(data) // 8 if isinstance(data, bytes) and len(data) % 8 == 0 else 0
            if count:
                import struct
                values = struct.unpack("<%dd" % count, data)
            else:
                values = list(data)
            xs = values[0::3]
            ys = values[1::3]
            zs = values[2::3]
            print("PROBE_GEOM %s verts=%d x=[%.4f,%.4f] y=[%.4f,%.4f] z=[%.4f,%.4f]"
                  % (label, len(xs), min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
            return


for elem in objects.elems:
    kind = elem.id
    if kind not in (b"Model", b"Geometry", b"NodeAttribute"):
        continue
    props = [value for _t, value in props_of(elem)]
    if not props:
        continue
    raw_name = as_str(props[0])
    name, _, cls = raw_name.partition("\x00\x01")
    if name_filter and name_filter.lower() not in name.lower():
        continue
    uid = props[1] if len(props) > 1 else None
    parent = parents.get(uid)
    if kind == b"Model":
        translation = None
        for child in elem.elems:
            if child.id == b"Properties70":
                for prop in child.elems:
                    values = [value for _t, value in props_of(prop)]
                    if values and values[0] == b"Lcl Translation":
                        translation = [round(float(v), 5) for v in values[4:7]]
        print("PROBE_MODEL %-28s cls=%-10s parentUid=%s LclTranslation=%s"
              % (name, cls, parent, translation))
    elif kind == b"Geometry":
        geometry_bounds(elem, name)

print("PROBE_DONE")
