# Phase09A_1 HANDOFF — First-Person EVA Arms Revision03

Status: **Revision03 block-hand asset package complete and visually inspected.**

This is an asset-only Blender revision. It changes only
`ArtSource/Phase09A_1`. **Unity integration was NOT performed.** The existing
Wrist UI, Unity scenes, prefabs, Animator Controllers, camera mounts, input,
gameplay, `Assets`, `ProjectSettings`, and `Packages` were not modified.

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

The builder recreates five runtime meshes and the lightweight arm rig,
overwrites the authority `.blend`/`.fbx`, and exports no validation cameras or
UI proxy objects.

## 2. Revision03 hand simplification

The articulated human-hand approach was intentionally removed.

- Each hand is now one rigid, eight-sided tapered mitten/wedge volume.
- There are **no visible fingers, palm anatomy, knuckles, articulated thumb, or
  finger deformation**.
- The main graphite pressure-glove body ends in a compact light-grey hard toe.
- One broad off-white dorsal guard and one centred orange ID bar make the design
  look manufactured and provide the same forward-orientation cue on both sides.
- Left and right share the same forward construction; there is no mirrored
  left-hand thumb/palm silhouette to misread on the right.
- The main mitten and hard toe meet at an open shared boundary rather than
  overlapping finger/palm shells. The previous hand-mesh penetration path is
  therefore eliminated by construction.
- Each visible hand is weighted only to `Hand_L` or `Hand_R`.

This is a deliberate production-simplicity decision, not an unfinished human
hand.

## 3. Spacesuit forearm design

The EVA identity remains the dominant arm read:

- tapered off-white pressure sleeves;
- segmented light-grey dorsal hard-shell armour;
- graphite bellows and undersuit transitions;
- rigid wrist rings and side rails;
- restrained orange identification strips;
- clean low-poly silhouettes and body-side spacing.

No torso, head, legs, full character, humanoid body rig, or unrelated asset was
added.

## 4. Compact wrist device

The left wrist device remains integrated into the forearm and is now visually
led by its physical housing:

1. six separated light-grey mechanical outer modules;
2. wide graphite structural gaps and a dark recessed centre;
3. one small cyan emitter/display window;
4. one short orange identification tab;
5. two low-profile fore/aft service buttons;
6. one narrow graphite cuff clamp.

The chassis is approximately **94 mm wide** and the face stack approximately
**21 mm thick**, versus the 130 mm / 46 mm Revision02 reference. It is neither a
giant cyan disc, smartwatch, nor Pip-Boy-sized unit. The six outer modules, not
the cyan element, are the primary visual read. `WristDevice_L` provides rigid
weighting and the physical emitter/VFX anchor.

`MAT_DeviceGreen` remains in the stable six-slot material order for import
compatibility but is unused by Revision03 geometry.

## 5. Runtime package

```text
SK_FPArms
└── RIG_FPArms
    ├── GEO_Arm_L
    ├── GEO_Hand_L
    ├── GEO_WristDevice_L
    ├── GEO_Arm_R
    └── GEO_Hand_R
```

Geometry total: **2,884 triangles**.

| Mesh | Triangles |
| --- | ---: |
| `GEO_Arm_L` | 764 |
| `GEO_Hand_L` | 260 |
| `GEO_WristDevice_L` | 836 |
| `GEO_Arm_R` | 764 |
| `GEO_Hand_R` | 260 |

Stable material-slot order:

1. `MAT_Glove`
2. `MAT_Sleeve`
3. `MAT_DeviceBody`
4. `MAT_DeviceAccent`
5. `MAT_DeviceEmissive`
6. `MAT_DeviceGreen`

## 6. Simplified rig

Revision03 removes the twelve legacy finger/thumb bones. FBX raw readback
confirms exactly **8 limb nodes**:

```text
Root
├── UpperArm_L
│   └── Forearm_L
│       ├── Hand_L
│       └── WristDevice_L
└── UpperArm_R
    └── Forearm_R
        └── Hand_R
```

This is an intentional rig simplification. The arm, forearm, hand, and device
names remain production-clean. The terminal preserves its rigid rest-relative
transform to `Forearm_L`, preventing roll flips. No finger bones or curl tracks
are required by the visible block mittens.

## 7. Animation contract

All clips are baked at **30 FPS**. FBX raw readback confirms these exact take
names and ranges:

| Clip | Frames | Duration | Loop | Revision03 presentation |
| --- | ---: | ---: | --- | --- |
| `FP_Idle` | 1–61 | 2.000 s | yes | low body-side rest, stable mittens, micro motion |
| `FP_Walk` | 1–41 | 1.333 s | yes | mild opposing body-side swing |
| `Wrist_Raise` | 1–25 | 0.800 s | no | left arm travels from hanging rest into the lower-left diagonal |
| `Wrist_Idle` | 1–91 | 3.000 s | yes | stable 29.93° hold with subtle micro motion |
| `Wrist_Lower` | 1–21 | 0.667 s | no | smooth return to lower-left body-side rest |
| `Interact_Right` | 1–31 | 1.000 s | no | optional simple right-hand interaction |

The three loop clips have first/last maximum matrix-element deviation **0.0**.

**Root Motion = OFF.** The `Root` bone remains at identity in every frame of
every clip; measured maximum root-basis deviation is **0.0**.

## 8. BODY-SPACE CONTRACT

The resting arms belong to player body space.

- Validation camera: **70° vertical FOV**, **16:9**.
- Rest wrists remain at body-side Blender coordinates
  `L=(0.255,-0.110,-0.715)` and `R=(-0.255,-0.110,-0.715)`.
- At level pitch `0°`, both `FP_Idle` and `FP_Walk` contain `0 / 1688`
  projected asset vertices: hands and arms are not visible in normal forward
  view.
- At `52°` look-down, both sides naturally enter the lower corners. Idle left
  and right NDC x ranges are `[-0.8675,-0.2330]` and `[0.2330,0.8675]`, leaving
  a clear central corridor.
- Hands keep the same simple forward toe/ID orientation; neither turns inward
  into a false mirrored-hand read.

### Mandatory Phase09A_2 rule

Future Unity integration must anchor the arms root to player body position and
**yaw**. Do not permanently rotate the rest asset into the eye camera. Do not
use fixed `restPitchDegrees=-40` (or a similar pitch workaround). Camera
look-down should reveal the body-space rest arms naturally; the authored wrist
clips independently bring the left arm into UI presentation.

## 9. Wrist_Raise / Wrist_Idle screen composition

The first-person result is authoritative; the values below are projected from
the final posed `Forearm_L` bone in the 70° validation camera.

- `Wrist_Raise` frame 25 forearm angle from screen bottom: **29.93°**.
- `Wrist_Idle` frame 46 forearm angle from screen bottom: **29.93°**.
- Required target: approximately **30°**; accepted range **25°–35°**.
- Projected elbow → wrist: `(-1.7687,-1.2529)` → `(-0.5252,-0.5369)` NDC.
- Extended visible centreline enters the **left edge** at
  `(-1.0000,-0.8103)` NDC.
- Therefore the arm enters from the **LOWER-LEFT SIDE**, not bottom-centre and
  not near-vertical.
- Device face centre: `(-0.6000,-0.5800)` NDC, on the left side.
- `Wrist_Idle` preserves the same composition without vertical drift.

The simplified hand remains secondary and quiet beyond the device.

## 10. Wrist UI contract

The existing Wrist UI is authoritative and was **NOT modified**.

- Camera FOV: **70°**.
- Wrist UI reference relative to camera:
  **`(-0.265, -0.335, 0.69)`**.
- The physical device adapts to the UI; the UI does not chase the arm.
- Device NDC bbox at Hold: `[-0.7588,-0.8444,-0.4688,-0.3816]`.
- Authoritative UI central proxy bbox:
  `[-0.4659,-0.7661,0.3378,0.6627]`.
- Device/UI-central bbox overlap: **0.0%**.
- Ray testing found `7 / 198` samples blocked (`3.54%`), limited to the
  lower-left panel edge. Map `0 / 96` and status `0 / 54` remain clear.

The wrist device sits left/below and physically behind the holographic layout;
the UI remains open above/right. Final UI-over-watch blur/compositing belongs
to DSH during Phase09A_2 and was not attempted here.

## 11. Visual validation

All required renders were generated at **1280×720** and actually inspected.

| Required file | Visual status |
| --- | --- |
| `fp_idle.png` | PASS — look-down shows both body-side EVA arms and open centre |
| `fp_walk_lookdown.png` | PASS — mild separated swing; mitten orientation stable |
| `wrist_raise.png` | PASS — shallow lower-left entry; device leads the hand |
| `wrist_idle.png` | PASS — 29.93° hold, watch left, UI proxy above/right |
| `wrist_device_closeup.png` | PASS — six housing modules dominate; cyan centre is small |
| `left_hand_shape.png` | PASS — complete single-volume mitten/wedge silhouette |
| `right_hand_shape.png` | PASS — complete matching forward silhouette; no mirrored thumb |
| `external_front.png` | PASS — arms remain under their respective body sides |
| `external_side.png` | PASS — tapered sleeves, cuffs, and body-space placement |

Additional acceptance-gate evidence remains in the same folder, including
`05_fp_forward_view.png`, `06_fp_look_down.png`, and
`13_wrist_hold_ui_proxy.png`.

Machine-readable evidence:

- `asset_report.json` — geometry, rig, clips, UI occlusion and 29.93° metric;
- `render_report.json` — camera and human-review render metadata;
- `validation_analysis.json` — image, frustum, loops, Root Motion, UI overlap,
  left-edge entry and screen-angle checks;
- `fbx_verification.json` — FBX 7400 raw take/bone names, ranges, geometry and
  bounds.

Validation-only cameras, body references, rig sticks, and `WristUIProxy` are
created by the render script and are not saved/exported as runtime content.

## 12. Exact Unity handoff notes for Phase09A_2

These instructions were documented only and **not executed**:

1. Import `FP_PlayerArms.fbx` as a **Generic** rig; preserve the authored FBX
   conversion (`axis_forward=-Z`, `axis_up=Y`, metre units, no leaf bones).
2. Update the integration for the intentional 8-bone skeleton; do not expect
   legacy finger bones. Use `WristDevice_L` as the optional physical-device/VFX
   anchor.
3. Set `Animator.applyRootMotion = false`.
4. Loop only `FP_Idle`, `FP_Walk`, and `Wrist_Idle`.
5. Anchor the arms to body position/yaw. Do not add a fixed rest-pitch
   workaround.
6. Use `Wrist_Raise → Wrist_Idle → Wrist_Lower` for UI presentation and keep
   the authored lower-left 29.93° composition.
7. Preserve the existing Wrist UI, FOV `70°`, and reference
   `(-0.265,-0.335,0.69)` exactly; do not move or scale UI to chase the arm.
8. Keep the six material slots by exact `MAT_*` names. The small cyan emitter
   is `MAT_DeviceEmissive`; `MAT_DeviceGreen` is intentionally unused.
9. Any physical-only blur/softening/compositing is DSH Phase09A_2 work. Keep
   the UI itself sharp.

## 13. Scope confirmation

- Block/wedge hand simplification: **YES**
- Articulated finger complexity removed: **YES**
- Right-hand mirrored reading removed: **YES**
- Wrist_Raise lower-left entry: **YES**
- Wrist_Raise / Wrist_Idle 25°–35° target: **YES, 29.93°**
- Animation names preserved: **YES**
- Root Motion off: **YES**
- Wrist UI changed: **NO**
- Unity integration performed: **NO**
- Files outside `ArtSource/Phase09A_1` modified: **NO**
