# Phase09A_1 HANDOFF — First-Person EVA Arms Revision03

Status: **Revision03 asset package complete and visually validated.**

This revision is a Codex-owned Blender asset correction. It changes only the
Phase09A_1 source package. **Unity integration was NOT performed.** No Unity
scene, prefab, Animator Controller, camera mount, input binding, gameplay code,
Wrist UI object, ProjectSettings, or Packages file was created or changed.

## 1. Authoritative outputs

| Artifact | Path |
| --- | --- |
| Blender source | `ArtSource/Phase09A_1/output/FP_PlayerArms.blend` |
| FBX export | `ArtSource/Phase09A_1/output/FP_PlayerArms.fbx` |
| Deterministic builder | `ArtSource/Phase09A_1/scripts/build_fp_arms.py` |
| Validation renderer | `ArtSource/Phase09A_1/scripts/render_validation.py` |
| Validation analysis | `ArtSource/Phase09A_1/scripts/analyze_validation.py` |
| FBX verifier | `ArtSource/Phase09A_1/scripts/verify_fbx.py` |
| Revision03 evidence | `ArtSource/Phase09A_1/validation/revision03/` |

The builder starts from an empty Blender file, recreates the five runtime mesh
objects and 20-bone rig, overwrites the two authority outputs, and creates no
duplicate runtime objects or Actions when rerun.

## 2. Revision03 visual redesign

### EVA arms and gloves

- Off-white tapered pressure sleeves retain segmented dorsal armour, graphite
  bellows, rigid wrist rings, side rails, and restrained orange ID strips.
- Graphite EVA gloves retain reinforced dorsal plates, thumb saddles, padded
  fingers, and low-poly knuckle protection.
- The corrected right-hand construction from Revision02 is preserved: its
  thumb/index side is authored opposite the left hand. In the 52-degree
  look-down validation, both thumbs face naturally inward and the right hand is
  not mirrored.
- No torso, head, legs, full character, or humanoid rig was added.

### Compact wrist terminal

Revision02's long carrier slab, large circular under-frame, and shield-like
outer structure were removed rather than scaled down.

The new left-wrist device uses:

- one dominant cut-corner central cyan main screen;
- four small inset cyan function panes;
- one thin **green upper status arc**;
- one thin **orange lower status arc**;
- four short protective separators;
- two compact fore/aft cylindrical side buttons with narrow orange collars;
- a shallow graphite/off-white chassis and a narrow clamp centred on the wrist
  axis;
- rigid `WristDevice_L` weighting, with the terminal moved 58 mm toward the
  elbow so it does not overlap the hand armour.

Measured design comparison:

| Measure | Revision02 reference | Revision03 | Reduction |
| --- | ---: | ---: | ---: |
| Face/chassis width | 130 mm | 84 mm | 35.38% |
| Face stack thickness | 46 mm | 17.5 mm | 61.96% |

The terminal now reads as a compact physical support for the Wrist UI, not a
large mechanical shield or Pip-Boy-sized interface.

## 3. Runtime object and material package

```text
SK_FPArms
└── RIG_FPArms
    ├── GEO_Arm_L
    ├── GEO_Hand_L
    ├── GEO_WristDevice_L
    ├── GEO_Arm_R
    └── GEO_Hand_R
```

Geometry total: **4,604 triangles**.

| Mesh | Triangles |
| --- | ---: |
| `GEO_Arm_L` | 764 |
| `GEO_Hand_L` | 788 |
| `GEO_WristDevice_L` | 1,440 |
| `GEO_Arm_R` | 764 |
| `GEO_Hand_R` | 848 |

Stable material-slot order:

1. `MAT_Glove`
2. `MAT_Sleeve`
3. `MAT_DeviceBody`
4. `MAT_DeviceAccent`
5. `MAT_DeviceEmissive`
6. `MAT_DeviceGreen`

`MAT_DeviceEmissive` is used by the main screen and four cyan function panes.
`MAT_DeviceGreen` is the independently controllable green upper arc;
`MAT_DeviceAccent` contains the restrained orange lower arc and ID accents.

## 4. Rig contract

**Rig contract preserved from Revision02.** Bone names and hierarchy are
unchanged; FBX raw readback confirms exactly 20 limb nodes.

```text
Root
├── UpperArm_L
│   └── Forearm_L
│       ├── Hand_L
│       │   ├── Thumb_L → Thumb_02_L
│       │   ├── Index_L → Index_02_L
│       │   └── Fingers_L → Fingers_02_L
│       └── WristDevice_L
└── UpperArm_R
    └── Forearm_R
        └── Hand_R
            ├── Thumb_R → Thumb_02_R
            ├── Index_R → Index_02_R
            └── Fingers_R → Fingers_02_R
```

Revision03 changes bone placement only where required by the new deformation
and presentation:

- both rest wrists were placed at body-space coordinates
  `L=(0.255,-0.110,-0.715)`, `R=(-0.255,-0.110,-0.715)` in the documented
  Blender authoring frame;
- elbow poles were revised for a relaxed natural bend;
- `WristDevice_L` was moved toward the elbow with the compact face;
- the device bone now preserves its exact rest-relative transform to
  `Forearm_L`, preventing roll flips and keeping the rigid terminal stable.

There are no renamed or removed bones, no hierarchy changes, and no breaking
rig change.

## 5. Animation contract

All clips are baked at **30 FPS**, Generic-rig compatible, and FBX raw readback
confirms the six exact public take names.

| Clip | Frames | Duration | Loop | Revision03 presentation |
| --- | ---: | ---: | --- | --- |
| `FP_Idle` | 1–61 | 2.000 s | yes | natural hanging body-space rest, subtle micro motion |
| `FP_Walk` | 1–41 | 1.333 s | yes | restrained opposing body-space swing |
| `Wrist_Raise` | 1–25 | 0.800 s | no | lifts from hanging rest, bends elbow, turns watch toward eye |
| `Wrist_Idle` | 1–91 | 3.000 s | yes | stable raised hold with small looped motion |
| `Wrist_Lower` | 1–21 | 0.667 s | no | smooth return to the new hanging rest pose |
| `Interact_Right` | 1–31 | 1.000 s | no | preserved optional right-hand interaction |

Loop validation for `FP_Idle`, `FP_Walk`, and `Wrist_Idle`: first/last pose
maximum matrix-element deviation = **0.0**.

**Root Motion = OFF.** `Root` remains at identity for every frame in every
clip; measured maximum root-basis deviation = **0.0**.

## 6. FIRST-PERSON BODY-SPACE CONTRACT

**The rest arms are authored in BODY SPACE.**

- Validation camera vertical FOV: **70 degrees**, aspect **16:9**.
- At camera pitch `0°`, `FP_Idle` and `FP_Walk` show no hand, glove, sleeve, or
  watch pixels. Projected runtime vertices inside the frustum: `0 / 2596`.
- At camera pitch `52°` down, the same camera position naturally reveals the
  left arm at lower-left and right arm at lower-right.
- Look-down visible vertex fractions: left `91.54%`, right `84.95%`.
- Look-down projected bounding boxes remain separated by a clear centre:
  left NDC x `[-0.8675,-0.1715]`, right NDC x `[0.1715,0.8675]`.
- The rendered central 40% safe area remains **93.77% clear** in Idle and
  **94.87% clear** in Walk.

### Critical Phase09A_2 integration instruction

Do **NOT** permanently pitch or rotate the entire arm rig into the forward
camera view. Do **NOT** reproduce a fixed `restPitchDegrees=-40` (or similar)
presentation workaround.

Recommended Unity behavior:

- the arms root follows player body/root position and **yaw**;
- the resting arms do not blindly inherit Eye Camera pitch;
- camera pitch down naturally reveals the hanging body-space arms;
- `Wrist_Raise` independently brings the left wrist into forward view;
- no fixed rest rotation should be added merely to keep hands on-screen.

The previous Unity checkpoint is intentionally untouched. Removing/replacing
its temporary rest-pitch workaround belongs to the next DSH Phase09A_2 asset
integration revision, not this asset commit.

## 7. WRIST PRESENTATION CONTRACT

The existing Wrist UI is authoritative and remains unchanged.

- Camera FOV: **70°**.
- Wrist UI reference relative to camera: **`(-0.265, -0.335, 0.69)`**.
- `Wrist_Raise` starts from the body-space hanging pose.
- `Wrist_Idle` holds the physical device at NDC `(-0.56,-0.80)`, below/left
  and behind the intended UI presentation region.
- Device face-to-eye alignment dot at Hold: `0.9684`; the main screen is
  readable with a small amount of real 3D perspective.
- Device viewport NDC bbox: `[-0.7466,-1.0909,-0.4117,-0.5478]`.
- UI central proxy NDC bbox: `[-0.4659,-0.7661,0.3378,0.6627]`.
- Device/UI-central bbox overlap is **1.03% of the UI central bbox**.
- Ray testing of 198 authoritative UI samples found `4 / 198` blocked
  (`2.02%`), confined to the lower-left panel edge. Map `0 / 96`, status ring
  `0 / 54`, and all other panel samples remain clear.

Unity should render the authoritative Wrist UI above the physical watch. The
3D watch is presentation support, not UI authority. Physical arm/watch
dimming, softening, blur, and final compositing may be implemented by DSH in
the next Unity revision; the UI itself should remain sharp. **No blur or Unity
composition work was attempted in Blender or Unity in this revision.**

## 8. Revision03 validation evidence

All 16 required PNGs were generated at 1280×720 and actually inspected. The
three acceptance-gate images pass visually, not only numerically.

| File | Visual result |
| --- | --- |
| `01_wrist_device_top.png` | PASS — main screen, four cyan panes, green top/orange bottom arcs |
| `02_wrist_device_front.png` | PASS — compact controls and reduced footprint |
| `03_wrist_device_side.png` | PASS — shallow layered face profile |
| `04_wrist_device_on_arm.png` | PASS — integrated forearm scale, no shield silhouette |
| `05_fp_forward_view.png` | PASS — level 70° view contains no arms |
| `06_fp_look_down.png` | PASS — both hanging arms visible at 52°, centre clear |
| `07_fp_walk_forward.png` | PASS — level walk contains no arms |
| `08_fp_walk_look_down.png` | PASS — mild side-based swing, centre clear |
| `09_wrist_raise_25.png` | PASS — fixed external path view leaving hanging rest |
| `10_wrist_raise_50.png` | PASS — elbow flexion and upward/forward travel |
| `11_wrist_raise_75.png` | PASS — late raise approaches eye-facing Hold |
| `12_wrist_hold.png` | PASS — small readable device in lower-left |
| `13_wrist_hold_ui_proxy.png` | PASS — UI centre unobstructed above/forward of watch |
| `14_wrist_lower_50.png` | PASS — clean return path, no snap |
| `15_external_front.png` | PASS — arms hang under their respective shoulders |
| `16_external_side.png` | PASS — body-space placement rather than camera-space reach |

Machine-readable evidence in the same folder:

- `asset_report.json` — geometry, materials, rig, clips, hold and ray checks;
- `render_report.json` — camera/view metadata for all 16 images;
- `validation_analysis.json` — image coverage, side visibility, safe area,
  loop, root-motion, and proxy-overlap checks;
- `fbx_verify.json` — FBX 7400 raw take names, 20 raw limb nodes, hierarchy,
  action ranges, mesh/material contents, and bounds.

`BodyReference`, `HeadReference`, `Shoulder_L`, `Shoulder_R`,
`EyeCameraReference`, `CAM_FP_Forward`, `CAM_FP_LookDown`, and `WristUIProxy`
are validation-only objects created by the render script. They are not saved as
runtime content and are not exported in the FBX.

## 9. Exact Unity handoff notes for the next DSH pass

These instructions were **not executed** in Revision03:

1. Import the authority `FP_PlayerArms.fbx` as a **Generic** rig and preserve
   the existing Blender FBX conversion (`axis_forward=-Z`, `axis_up=Y`, metre
   authoring units, no added leaf bones).
2. Preserve all 20 bone names and use `WristDevice_L` as the optional exposed
   physical-device/VFX anchor.
3. Set `Animator.applyRootMotion = false`.
4. Loop only `FP_Idle`, `FP_Walk`, and `Wrist_Idle`.
5. Anchor the arm root to player body position/yaw. Do not force the body-space
   rest pose into the camera with `restPitchDegrees=-40` or any fixed pitch.
6. Let camera pitch reveal the rest arms naturally; use the authored
   `Wrist_Raise → Wrist_Idle → Wrist_Lower` sequence for wrist presentation.
7. Preserve the authoritative Wrist UI, FOV `70°`, and reference
   `(-0.265,-0.335,0.69)`. Do not move/rescale UI to chase the arm.
8. Map the six imported material slots by exact `MAT_*` names. The central
   display/four cyan panes, green arc, and orange accents have separate control
   channels as described above.
9. Render the UI sharply above the physical watch. Any physical-only
   blur/softening/compositing is DSH work in Phase09A_2 Revision.

## 10. Compatibility and scope confirmation

- Bone names changed: **NO**
- Bone hierarchy changed: **NO**
- Animation names changed: **NO**
- Breaking change: **NO**
- Wrist UI changed: **NO**
- Unity integration performed: **NO**
- Files outside `ArtSource/Phase09A_1`: **NO**
