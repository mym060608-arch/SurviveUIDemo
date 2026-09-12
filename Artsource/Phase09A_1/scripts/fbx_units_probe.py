"""Read the FBX GlobalSettings unit/axis declaration of an exported file."""

import os
import sys

import bpy

addons_root = os.path.join(bpy.utils.resource_path("LOCAL"), "scripts", "addons_core")
if addons_root not in sys.path:
    sys.path.insert(0, addons_root)
import io_scene_fbx.parse_fbx as parse_fbx  # noqa: E402

WANTED = {b"UpAxis", b"UpAxisSign", b"FrontAxis", b"FrontAxisSign",
          b"CoordAxis", b"CoordAxisSign", b"UnitScaleFactor",
          b"OriginalUnitScaleFactor"}

for path in sys.argv[sys.argv.index("--") + 1:]:
    root, version = parse_fbx.parse(path)
    settings = {}
    for elem in root.elems:
        if elem.id != b"GlobalSettings":
            continue
        for child in elem.elems:
            if child.id != b"Properties70":
                continue
            for prop in child.elems:
                values = list(prop.props)
                if values and values[0] in WANTED:
                    settings[values[0].decode()] = values[4]
    print("SETTINGS", os.path.basename(path), "fbxVersion=", version, settings)
print("UNITS_PROBE_DONE")
