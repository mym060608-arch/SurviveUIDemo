# Phase09A_1 HANDOFF — First-Person EVA Arms Revision02

Status: **Revision02 asset package complete and ready for visual review.**

This was a **Codex-owned Blender asset workflow**. Unity integration was **NOT**
performed. No runtime prefab, Animator Controller, camera mount, input wiring,
gameplay code, or production Wrist UI object was created or changed.

## 1. Revision02 visual redesign

The previous Phase09A_1 asset was retained only as a rig/export/animation
baseline. Its visible mesh direction was replaced.

- Both forearms now use a tapered pressurised sleeve, three graphite pressure
  bellows, a broad suit ring, three separate dorsal armour plates, side rails,
  and a restrained orange identification strip.
- Both hands now read as thick graphite EVA gloves with a rigid dorsal plate,
  reinforced thumb saddle, padded fingers, and segmented knuckle protection.
- The normal resting pose was widened: shoulders and wrists sit farther left
  and right, leaving a large clear central view.
- The **right glove was re-authored after visual review**. Its lateral finger
  order, index edge, thumb mesh, thumb bones, and thumb reinforcement are on
  the opposite local side from the left glove. Normals were recalculated
  outward. `right_hand_check.png` is a palm/thumb-side verification view.
- The left wrist terminal was redesigned again from the user's latest sketch.
  It is **not an eight-segment mechanical ring**. It has:
  - four diagonal cyan screen-like structures;
  - four long cardinal rectangular divider structures;
  - two fore/aft cylindrical side buttons with narrow orange collars;
  - one large physical off-white centre cap;
  - one small recessed cyan projector aperture;
  - a long layered carrier plate integrated into the left forearm armour.

Palette: off-white/light grey suit shell, graphite pressure/glove areas,
restrained orange ID details, and limited cyan emission on the four small
screens and centre aperture.

## 2. Output paths

| Artifact | Path |
| --- | --- |
| Blender source | `ArtSource/Phase09A_1/output/FP_PlayerArms.blend` |
| FBX export | `ArtSource/Phase09A_1/output/FP_PlayerArms.fbx` |
| Builder | `ArtSource/Phase09A_1/scripts/build_fp_arms.py` |
| Render driver | `ArtSource/Phase09A_1/scripts/render_validation.py` |
| Validation folder | `ArtSource/Phase09A_1/validation/revision02/` |

No production output was placed under `Assets/`.

## 3. Final object package

```text
SK_FPArms
└── RIG_FPArms
    ├── GEO_Arm_L
    ├── GEO_Hand_L
    ├── GEO_WristDevice_L
    ├── GEO_Arm_R
    └── GEO_Hand_R
```

Final geometry is 4,576 triangles:

| Mesh | Triangles |
| --- | ---: |
| `GEO_Arm_L` | 764 |
| `GEO_Hand_L` | 788 |
| `GEO_WristDevice_L` | 1,412 |
| `GEO_Arm_R` | 764 |
| `GEO_Hand_R` | 848 |

All five meshes have applied object transforms, one UV set, stable material
slots, an Armature modifier, and are parented to `RIG_FPArms`.

Material slots, in stable order:

1. `MAT_Glove`
2. `MAT_Sleeve`
3. `MAT_DeviceBody`
4. `MAT_DeviceAccent`
5. `MAT_DeviceEmissive`

## 4. Rig summary

The lightweight 20-bone first-person rig and all public bone names were kept.
No torso, humanoid skeleton, IK constraints, or full-body bones were added.

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

Revision02 rig changes:

- shoulder and rest-wrist positions were widened;
- dorsal roll hints were changed so armour plates face the first-person camera;
- right index/finger lateral positions and fan direction were mirrored;
- right thumb base, direction, mesh weights, and both thumb bones were moved to
  the corrected opposite local side;
- the left wrist hold target was revised for the larger embedded terminal;
- bone names and hierarchy remain stable.

## 5. Animation clips

All clips are baked at 30 FPS. Timing and required clip names remain unchanged.
Raw FBX `AnimationStack` names were verified as exactly the names below.

| Clip | Frames | Duration | Loop |
| --- | ---: | ---: | --- |
| `FP_Idle` | 1–61 | 2.000 s | yes |
| `FP_Walk` | 1–41 | 1.333 s | yes |
| `Wrist_Raise` | 1–25 | 0.800 s | no |
| `Wrist_Idle` | 1–91 | 3.000 s | yes |
| `Wrist_Lower` | 1–21 | 0.667 s | no |
| `Interact_Right` | 1–31 | 1.000 s | no; optional |

- `FP_Idle`: low, side-based arms with subtle breathing and a clear centre.
- `FP_Walk`: mild opposing swing and body bob; no weapon-style exaggeration.
- `Wrist_Raise`: lifts the left arm inward/upward and presents the terminal.
- `Wrist_Idle`: stable UI-support pose with small looped micro motion.
- `Wrist_Lower`: eased return to the new wide rest pose without snapping.

The three loop clips have identical first/last poses. Measured maximum matrix
element deviation is `0.0`.

**Root Motion = OFF.** The `Root` bone remains at identity throughout every
frame of every clip; measured maximum deviation is `0.0`.

## 6. Wrist UI contract

The existing Wrist UI is authoritative and was treated as read-only.

- Camera vertical FOV: **70 degrees**
- Aspect ratio used for validation: **16:9**
- Wrist UI frame reference relative to camera:
  **`(-0.265, -0.335, 0.69)`**
- Wrist UI itself was **not moved, scaled, rebuilt, or redesigned**.
- The arm/device was adapted to the UI.

Final `Wrist_Idle` measured presentation:

- device face NDC: `(-0.56, -0.80)`;
- device centre, Unity camera-local: `(-0.23701, -0.19046, 0.34)`;
- device-to-UI-pivot distance: `0.37971 m`;
- device face aim dot-to-eye: `0.97`;
- device remains below and left of the holographic composition.

198 UI reference samples were ray-tested in the final hold pose:

- map: `0 / 96` occluded;
- status ring: `0 / 54` occluded;
- upper-left, upper-right, and lower-right panels: `0` occluded;
- lower-left edge panel: `7 / 12` samples occluded by the physical wrist;
- total: `7 / 198` (`3.54%`).

The remaining overlap is confined to the lower-left edge where the physical
emitter enters the composition. Major map and status content remain clear.

## 7. Validation renders

All required renders were generated at 1280×720 with Blender Eevee Next and
were visually inspected, not accepted from numeric checks alone.

| Render | Status | Review purpose |
| --- | --- | --- |
| `fp_idle.png` | PASS | wide side placement and clear centre |
| `fp_walk_lookdown.png` | PASS | mild readable locomotion swing |
| `wrist_raise.png` | PASS | smooth approach to presentation area |
| `wrist_idle.png` | PASS | stable below-left emitter composition |
| `wrist_device_closeup.png` | PASS | 4 screens, 4 dividers, 2 cylinder buttons |
| `right_hand_check.png` | PASS | corrected right palm/thumb-side silhouette |
| `external_front.png` | PASS | full spacesuit armour read |
| `external_side.png` | PASS | layered sleeve/device depth |

Additional evidence:

- `wrist_alignment_guide.png` — validation-only read-only UI wireframe;
- `asset_report.json` — geometry, rig, hold and occlusion measurements;
- `render_report.json` — render metadata;
- `validation_analysis.json` — image, loop and root-motion checks;
- `fbx_verify.json` — FBX re-import structure and action ranges.

Validation-only camera, lights, UI guide, and presentation objects are not
included in the FBX export.

## 8. Exact Unity handoff notes for Phase09A_2

These are integration instructions only. They were **not executed** here.

1. Import `FP_PlayerArms.fbx` as a **Generic** rig.
2. Use the project’s established Blender FBX mapping. The export uses
   `axis_forward=-Z`, `axis_up=Y`, metre units, no added leaf bones, and the
   same convention as `MainMenuShip`.
3. Parent the imported asset under `FirstPersonCamera`. Keep the imported root
   conversion transform produced by Unity; do not casually zero its conversion
   rotation/scale.
4. Set `Animator.applyRootMotion = false`.
5. Mark `FP_Idle`, `FP_Walk`, and `Wrist_Idle` as loop clips. Leave
   `Wrist_Raise`, `Wrist_Lower`, and optional `Interact_Right` non-looping.
6. Drive `FP_Idle`/`FP_Walk` from locomotion and
   `Wrist_Raise → Wrist_Idle → Wrist_Lower` from existing Wrist UI events.
7. Do not move or rescale the Wrist UI to compensate for the arm. Preserve FOV
   70 and the reference `(-0.265, -0.335, 0.69)`.
8. Map the five imported material slots to project URP materials by the exact
   `MAT_*` names above.
9. If a projector/VFX anchor is required, expose `WristDevice_L` or disable
   Optimize Game Objects for that transform.
10. Any prefab, Animator Controller, camera attachment, input wiring, or final
    presentation nudge belongs to Phase09A_2.

## 9. Scope confirmation

- Wrist UI was **NOT changed**.
- Unity integration was **NOT performed**.
- No runtime prefab or Animator Controller was created.
- No torso, head, legs, full character, or unrelated environment asset was
  created.
