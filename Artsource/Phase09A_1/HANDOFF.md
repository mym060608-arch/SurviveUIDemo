# Phase09A_1 HANDOFF — First-Person Minimal Block Arms (Revision04)

Status: **Revision04 asset package complete and ready for visual review.**

This is an **asset-only** delivery. Unity integration was **NOT** performed: no
prefab, Animator Controller, camera attachment, input binding or Wrist UI change
exists in this revision. The existing Wrist UI was treated as read-only and is
untouched.

---

## 1. Revision04 Design Decision

**The previous articulated / anatomical hand design was intentionally abandoned.**

Revision02 tuned an anatomical glove (thumb saddle, knuckle protection, finger
order, dorsal plates) and Revision03 moved it into body space with a two-bone IK
arm. Both were rejected on visual grounds: the hand kept consuming project time
and still read as uncanny in first person.

Revision04 is a **full visual reset** to deliberately simple stylized geometry:

```text
[ elongated rectangular spacesuit forearm ] + [ short rectangular glove block ]
```

- rectangular prisms only; each arm is ONE rigid block (no elbow joint),
- no fingers, no palm, no thumb, no knuckle mesh, no wrist anatomy,
- no bending or deforming hand geometry anywhere,
- small chamfers only, to avoid raw CG edges — never organic rounding,
- spacesuit identity comes from **material and panel language**, not geometry:
  off-white shell, graphite glove block, dark cuff, one restrained orange stripe,
  and the left wrist terminal.

The arm renders at 1,028 triangles total (previous revisions: 4,576+).

---

## 2. Output paths

| Artifact | Path |
| --- | --- |
| Blender source | `ArtSource/Phase09A_1/output/FP_PlayerArms.blend` |
| FBX export | `ArtSource/Phase09A_1/output/FP_PlayerArms.fbx` |
| Builder | `ArtSource/Phase09A_1/scripts/build_fp_arms_rev04.py` |
| Render driver | `ArtSource/Phase09A_1/scripts/render_validation_rev04.py` |
| Analysis | `ArtSource/Phase09A_1/scripts/analyze_validation_rev04.py` |
| FBX verifier | `ArtSource/Phase09A_1/scripts/verify_fbx.py` (unchanged, generic) |
| Validation folder | `ArtSource/Phase09A_1/validation/revision04/` |
| Previous revisions | `ArtSource/Phase09A_1/archive/pre_revision04/` |

Nothing was written under `Assets/`.

---

## 3. Object package

```text
SK_FPArms                      (empty, package root)
└── RIG_FPArms                 (armature: 4 bones)
    ├── GEO_Arm_L              (forearm sleeve block + orange stripe)
    ├── GEO_HandBlock_L        (cuff + glove block)
    ├── GEO_WristDevice_L      (compact polygonal terminal)
    ├── GEO_Arm_R              (forearm sleeve block + orange stripe)
    └── GEO_HandBlock_R        (cuff + glove block)
```

| Mesh | Triangles | Material slots |
| --- | ---: | --- |
| `GEO_Arm_L` | 88 | `MAT_Suit`, `MAT_Accent` |
| `GEO_HandBlock_L` | 88 | `MAT_Cuff`, `MAT_Glove` |
| `GEO_WristDevice_L` | 676 | `MAT_Device`, `MAT_Cuff`, `MAT_DeviceEmissive`, `MAT_Accent` |
| `GEO_Arm_R` | 88 | `MAT_Suit`, `MAT_Accent` |
| `GEO_HandBlock_R` | 88 | `MAT_Cuff`, `MAT_Glove` |
| **Total** | **1,028** | |

All meshes carry applied object transforms, one UV set, an Armature modifier and
are parented to `RIG_FPArms`.

Dimensions (metres): forearm cross-section `0.104 x 0.104`, glove block
`0.118 x 0.118 x 0.165`, cuff `0.126 x 0.126 x 0.046`, arm length (pivot to the
wrist/device reference) `0.443`.

---

## 4. Rig

Minimal by design — 4 bones, no finger / thumb / hand chains, no elbow chain:

```text
Root                     identity in every clip (Root Motion OFF)
├── Arm_L                shoulder pivot -> wrist end, one rigid block
│   └── WristDevice_L    mount transform of the terminal (rigid child)
└── Arm_R                shoulder pivot -> wrist end, one rigid block
```

- Shoulder pivot (Blender authoring space, +X = player LEFT, -Y = forward,
  +Z = up): `(±0.3179, 0.080, -0.180)`.
- Rest pose = the idle hanging pose, so `FP_Idle` is the identity pose.
- Skinning is **rigid**: every vertex is weighted 1.0 to exactly one bone
  (`GEO_Arm_*`/`GEO_HandBlock_*` -> `Arm_*`, `GEO_WristDevice_L` ->
  `WristDevice_L`). There is no deformation to go wrong.

---

## 5. Animation clips

All clips are baked at 30 FPS. Clip names and ranges are unchanged from the
shipped contract, so an existing importer keeps working.

| Clip | Frames | Duration | Loop | Content |
| --- | ---: | ---: | --- | --- |
| `FP_Idle` | 1–61 | 2.000 s | yes | arms at the body sides, ±1.1° idle sway |
| `FP_Walk` | 1–41 | 1.333 s | yes | ±9° opposite rigid swing, mild lift |
| `Wrist_Raise` | 1–25 | 0.800 s | no | left arm into the lower-left 30° presentation |
| `Wrist_Idle` | 1–91 | 3.000 s | yes | held pose, ±0.9° micro motion |
| `Wrist_Lower` | 1–21 | 0.667 s | no | reverse of the raise |
| `Interact_Right` | 1–31 | 1.000 s | no | small rigid right-arm reach (optional) |

- Measured first/last pose deviation of the three loop clips: **0.0**.
- Animation is transform-only (bone rotation); no morphs, no IK, no constraints.

## Root Motion

**OFF.** The `Root` bone is keyed at identity in every frame of every clip;
measured maximum deviation from identity is `0.0`.

---

## 6. UI Contract (existing Wrist UI — untouched)

| Contract | Value |
| --- | --- |
| Camera vertical FOV | **70°** |
| Aspect | 16:9 |
| Wrist UI pivot, camera-local (Unity) | **`(-0.265, -0.335, 0.69)`** |
| Display offset / scale | `(0.21, 0.31, 0.0)` / `(1.88, 1.175, 1.0)` |
| Wrist UI changed? | **No — not moved, not scaled, not redesigned** |

The arm was posed to the UI, never the reverse. `wrist_idle_ui_reference.png`
draws the hologram frame, the map disc and the pivot as a **read-only**
validation wireframe (never exported) so the relationship can be reviewed.

---

## 7. Composition (the critical presentation gate)

Revision04 target: the left arm enters the viewport from the **LOWER-LEFT**, at
approximately **30°** against the screen bottom edge (25°–35° accepted), with the
terminal on the left and the holographic UI region above/right.

Measured in the held pose (`Wrist_Idle`, frame 46):

| Metric | Value |
| --- | --- |
| Visible forearm screen angle | **30.45°** (target 30°, accept 25–35) |
| Frame entry point (viewport, y-up, 0..1) | **(0.079, -0.002)** — bottom edge, far left |
| Entry region | lower-left (not bottom-centre) |
| Device centre, camera-local (Unity) | `(-0.231, -0.146, 0.294)` |
| Device centre, viewport | **(0.185, 0.146)** — lower-left |
| Device face normal · direction to eye | 0.725 (readable while presented) |
| UI pivot / display centre, viewport | (0.346, 0.153) / (0.468, 0.474) |
| Device left of the UI pivot | yes |
| Device below the UI display centre | yes |
| Arm pixels inside the hologram map disc | **0** (closest approach 1.225 disc radii) |
| Arm pixels inside the hologram panel band | 1,431 px (2.24 % of the arm) — the physical emitter grazing the lower-left panel band |

**Revision04 polish pass.** The only correction after visual review: the held
device sat a little too far from the hologram to read as its emitter, so the
whole presentation target was nudged **right by 2.5 % and up by 2.3 % of the
screen** (`HOLD_AXIS_NDC` `(-0.60, -0.76)` -> `(-0.55, -0.71)`). The shoulder
pivot was deliberately left untouched, which keeps the projected forearm at
~30.5°, keeps the lower-left entry where it was (0.078 -> 0.079 of the screen
width) and grows the derived arm length by only 3 mm, so the normal walk/idle arm
placement is unchanged. Nothing else in the asset moved: same block geometry,
same rig, same clip names, ranges and Root Motion contract.

The composition is solved, not eyeballed: the forearm centreline is aimed at NDC
`(-0.55, -0.71)` at 0.36 m depth and the shoulder pivot is chosen so the
projected angle lands on 30°; the arm length is derived from that target.

Normal look: the arms hang at the body sides and are **outside the frame at a
level gaze** (contract §9 allows this: measured 0 arm pixels in
`fp_forward_mask`). Both block arms enter the frame around a 42–45° look-down and
are clearly visible while walking at 45°.

---

## 8. Validation renders (`validation/revision04/`)

Rendered at 1280×720, Blender EEVEE, vertical FOV 70°, 16:9. Every first-person
pose is rendered twice: a review frame on the game-like grey background, and a
**mask frame on black** used for the numeric measurements.

| Render | Purpose |
| --- | --- |
| `fp_forward.png` | normal forward view (arms may be outside the frame) |
| `fp_walk_lookdown.png`, `fp_walk_lookdown_b.png` | walk look-down, opposite swing phases |
| `wrist_raise_mid.png` | left arm entering from the lower-left (frame 22) |
| `wrist_idle.png` | final held presentation pose |
| `wrist_idle_ui_reference.png` | held pose with the read-only Wrist UI wireframe |
| `wrist_device_closeup.png` | physical terminal |
| `external_overview.png`, `external_overview_raise.png` | both arms externally |
| `rig_overview.png` | the 4-bone minimal rig (validation overlay) |

Machine-readable evidence:

- `asset_report.json` — geometry, rig, clips, loops, root motion, composition;
- `render_report.json` — camera, background, per-render landmarks;
- `validation_analysis.json` — silhouette measurements and the PASS/FAIL gates;
- `fbx_verify.json` — FBX re-import: stacks, bones, hierarchy, ranges, bounds.

**Validation result: 19 / 19 gates PASS** (screen angle, lower-left entry, device
left/below the UI, hologram map disc clear, panel-band graze bounded, idle
out-of-frame, walk both-arms-visible, walk centre open, opposite swing, raise
mid-frame entry, rendered wrist composition, loop closure x3, Root Motion OFF).

---

## 9. Materials

| Blender material | Role | Unity mapping used by the current Phase09A_2 importer |
| --- | --- | --- |
| `MAT_Suit` | off-white / light grey arm shell | (was `MAT_Sleeve`) |
| `MAT_Glove` | dark graphite glove block | `MAT_Glove` |
| `MAT_Cuff` | graphite / medium grey cuff | (new slot) |
| `MAT_Accent` | restrained orange stripe / collars | (was `MAT_DeviceAccent`) |
| `MAT_Device` | light grey + graphite terminal body | (was `MAT_DeviceBody`) |
| `MAT_DeviceEmissive` | small cyan emitter | `MAT_DeviceEmissive` |

The emissive part is deliberately small: a recessed 29 mm aperture on a 90 mm
terminal — never a giant cyan disc.

---

## 10. Unity notes (integration guidance ONLY — nothing was integrated)

1. Import `FP_PlayerArms.fbx` as a **Generic** rig; the FBX uses the same
   `axis_forward='-Z'`, `axis_up='Y'`, metre-unit convention as `MainMenuShip`
   and as the Revision02/03 exports, so the existing importer settings still
   apply unchanged.
2. `Animator.applyRootMotion = false`; the clip names and ranges are unchanged.
3. The rig is now `Root / Arm_L / WristDevice_L / Arm_R`. Any integration that
   referenced `UpperArm_L`, `Forearm_L`, `Hand_L`, `Hand_R`, `Index_*`,
   `Fingers_*` or `Thumb_*` must be updated — those bones no longer exist.
4. **Material slots changed** (6 Blender materials, see §9). The Phase09A_2
   import contract expects `MAT_Glove / MAT_Sleeve / MAT_DeviceBody /
   MAT_DeviceAccent / MAT_DeviceEmissive`; either rename the Rev04 materials to
   that set on import or extend the import contract with the mapping in §9.
5. **The unit currently imported in `Assets/_Project/Models/FirstPerson/` is
   still the Revision02 asset.** Re-importing this FBX will change the arms'
   on-screen composition, so the Phase09A_2 presentation framing must be
   re-evaluated at the same time (in particular the `restPitchDegrees = -40`
   rest framing that existed only to lift the old low posture into view: Rev04
   deliberately keeps the arms out of frame at a level gaze, so that offset is no
   longer needed and should be removed or reduced).
6. The Wrist UI anchor, scale and radial layout must not be touched; the arm
   composition in §7 was solved against them.
7. Keep writing the arms to a non-world layer with no colliders (as Phase09A_2
   already does): the block arms must never join physics or interaction rays.

---

## 11. Scope confirmation

- The wrist terminal was kept and redesigned to a compact low-poly housing.
- The existing Wrist UI was **not** changed (no position, scale, radial layout,
  map, status arc or widget edit).
- Unity integration was **not** performed: no prefab, no Animator Controller, no
  camera attachment, no input binding, no Wrist UI binding.
- Previous revisions are preserved under
  `ArtSource/Phase09A_1/archive/pre_revision04/` (Blender file, FBX, HANDOFF,
  scripts and the revision03 validation set as they were at the time).
- Final art remains subject to **human visual review**; nothing here is a claim
  of artistic approval.
