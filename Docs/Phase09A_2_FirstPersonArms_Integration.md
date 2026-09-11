# Phase 09A_2 — First-Person Arms & Wrist Unity Integration

本文件记录 Phase 09A_2 的 Unity 集成结果、关键决策与验证证据，供人工视觉验收与后续 Phase 参考。

## 1. 范围

- 资产生产（Blender 建模 / Rig / 动画）属于 Phase 09A_1（Codex），本轮 **冻结只读**。
- 本轮只做：Unity 导入、First-Person Arms runtime、Animator、现有 Wrist System 的 presentation bridge、视觉构图调整、验证。
- 现有 Wrist System（`ShipGameController` + `SurfaceWristController` + `WristDisplayAnchor`）继续是唯一 UI / interaction authority；新增组件只负责 presentation。

## 2. 交付物

| 类型 | 路径 |
| --- | --- |
| 运行时模型 | `Assets/_Project/Models/FirstPerson/FP_PlayerArms.fbx`（自 `ArtSource/Phase09A_1/output/` 复制，源文件未改） |
| 材质 | `Assets/_Project/Materials/FirstPerson/MAT_{Glove,Sleeve,DeviceBody,DeviceAccent,DeviceEmissive}.mat` |
| Animator | `Assets/_Project/Animations/FirstPerson/FP_ArmsWatch.controller` |
| Prefab | `Assets/_Project/Resources/FirstPerson/PF_FirstPersonArms.prefab` |
| 导入合同 | `Assets/_Project/Scripts/Editor/FirstPersonArmsImportContract.cs` |
| 导入/资产入口 | `Assets/_Project/Scripts/Editor/SurviveFirstPersonArmsSetup.cs`、`FirstPersonArmsAssetsBuilder.cs` |
| Runtime | `Assets/_Project/Scripts/Gameplay/FirstPersonArmsController.cs`、`WristPresentationBridge.cs` |
| 证据探针 | `Assets/_Project/Scripts/Core/Ship09A2ShotProbe.cs` |
| 测试 | `Assets/_Project/Tests/PlayMode/SurvivePhase09A2Tests.cs` |

Prefab 放在 `Resources/FirstPerson/` 而不是 `Prefabs/FirstPerson/`：本项目既有规范是 "runtime 加载的资产放 `Assets/_Project/Resources/`"（`ShipPauseMenu.uxml`、`M_OverviewPlanetPreview.mat`），且 ship scene 保持冻结、不重新生成。

## 3. 导入合同（HANDOFF 对齐）

- Rig: **Generic**，Avatar `FP_PlayerArmsAvatar`（valid，非 humanoid），Root Motion **OFF**。
- 6 个 clip 名称与 HANDOFF 完全一致：`FP_Idle` / `FP_Walk` / `Wrist_Raise` / `Wrist_Idle` / `Wrist_Lower` / `Interact_Right`。
- loop：`FP_Idle`、`FP_Walk`、`Wrist_Idle` = true；其余 = false。
- 无 collider（`addCollider=false`，运行时也不加）；不导入 camera / light。
- 5 个材质槽按 FBX 实际使用情况映射到项目 URP 材质（每个 mesh 只带自身用到的槽，名称与 HANDOFF 一致）。
- Layer `FirstPersonArms`（index 8）；eye camera cullingMask 已包含该 layer。
- FOV 保持 Phase 08 的 **70**，Wrist UI anchor 未移动。

## 4. 关键几何发现与构图决策

Codex 的 FP_Idle / FP_Walk 姿态是 **平视时完全在画面之外** 的：

- `rest_pose.L.wrist_unity_camera = (-0.278, -0.650, 0.265)`，Codex 自测 `FP_Idle @ pitch 0 => vertices_inside_frustum = 0`；
- HANDOFF 验证渲染中 `fp_idle` 使用 **40° 俯视**、`fp_walk` 使用 **52° 俯视**，而 Wrist 姿态使用 **0°（平视）**；
- 平视下进入画面的临界角为 **34.7°**。

同时 Wrist hold 姿态（`Wrist_Idle`：device 相机局部 `(-0.237, -0.190, 0.34)`，NDC `(-0.56, -0.80)`）是 **为平视 + 现有 Wrist UI 构图** 设计的。二者在 y 方向相差约 0.44 m，而画面在该深度只有约 0.2–0.4 m 高，**单一刚性偏移在数学上无法同时满足**（见任务 §16/§38/§53 允许的 Unity 侧 presentation 调整）。

因此采用 **分状态构图（presentation framing）**：

- Rest / Walk：给手臂挂点一个相机俯仰偏移 `restPitchDegrees = -40`（`FirstPersonArmsController`），等价于 Codex 验证过的 40° 俯视构图，使双手/前臂从画面底部进入、中央保持清晰；
- Wrist Raise/Hold/Lower：偏移归零，即资产自身验证过的 hold 构图；
- 过渡由 `WristPresentationBridge` 控制：抬腕期间保持 rest 构图（抬起动作可见），UI 显现时用 `holdSettleSeconds = 0.45` 缓动到资产构图；收腕期间用 `lowerRestoreSeconds = 0.45` 缓回 rest 构图，使手臂落下时已回到 idle 构图。

这两个参数是 serialized field，人工验收后如需微调只改数值即可（不改模型 / Rig / 动画）。

## 5. Wrist 时序合同

- Authority（`ShipGameController`）：按下 Wrist 后 `IsWristOpen` **立即**为真，输入锁定、pointer ownership、HUD suppression 均沿用 Phase 08 行为。
- Presentation（`WristPresentationBridge`）：`Wrist_Raise` 播到 **0.78** 时调用 `RevealWristTerminal(true)` 显示 **现有** wrist UI（现有 0.2s 弹入动画与之同步）；随后进入 `WristHold`（`Wrist_Idle` 循环）。
- 关闭：现有 UI 先停止输入 / 释放 focus / 隐藏，然后 `Wrist_Lower` 落下，回到 `FP_Idle`。
- 无 arms 时（组件缺失或被禁用）立即显示 UI，即 Phase 08 行为 —— 现有 Wrist System 不依赖手臂（任务 §58）。超时保护 `RaiseRevealTimeoutSeconds = 1.25` 保证 UI 永不被卡住。

## 6. Controller 绑定

- 新增语义动作 `UiAction.Wrist`，物理绑定 `Wrist=Gamepad.buttonNorth,Keyboard.mKey|iKey`（写在 `UiInput.BindingTable`，逻辑层不出现 AButton/BButton 之类命名）。
- 选择 buttonNorth 的原因：Confirm=buttonSouth、Back=buttonEast、Pause=Menu/Start 均被占用，North 是唯一未使用的面键。
- 影响：`SurvivePhase08PauseTests.Pause_BlocksGameplayInteraction_Green` 与 `SurvivePhase08ControllerTests` 中 "Y 无功能" 的旧假设已随绑定更新（暂停仍然必须阻止该动作）。

## 7. 渲染与物理卫生

- 手臂无 collider，不参与世界射线 / 物理 / 碰撞；不投射阴影（Phase 08 已有 URP shadow atlas 警告，不扩大）。
- **必须保留** `SkinnedMeshRenderer.updateWhenOffscreen = true`：冻结 clip 会把前臂抬出绑定姿态包围盒，默认陈旧 bounds 会导致抬起的手臂被视锥剔除（画面里手臂消失，但骨骼位置正确）。
- Wrist 屏幕发光使用 `MaterialPropertyBlock`（不实例化材质）：关闭时暗青、腕表呈现时提亮。

## 8. 验证证据

| 项目 | 结果 |
| --- | --- |
| 导入报告 | `export/phase09a2_import_report.txt`（clip 名称/loop/骨骼/mesh/材质/尺度全部 PASS） |
| 资产报告 | `export/phase09a2_assets_report.txt` |
| 结构 validator | `export/phase09a2_validator.txt` — **410 checks / 0 failures** |
| PlayMode 测试 | `export/phase09a2_playmode.xml` — **144 / 144 passed**（含 9 项 Phase09A2 新增、5 项 Phase 07B/08 更新） |
| Standalone smoke | `export/phase09a2_smoke.txt` — RESULT: SUCCESS（0 FAIL，含手臂 / Walk / 抬腕 / 收腕 / 暂停冻结 / 主菜单清理 / Continue 唯一实例） |
| 截图证据 | `export/phase09a2_shots/`（见下） |

截图清单（1920×1080）：`Phase09A2_FP_Idle_1920x1080.png`、`Phase09A2_FP_Walk_1920x1080.png`、`Phase09A2_WristRaise_1920x1080.png`、`Phase09A2_WristHold_1920x1080.png`、`Phase09A2_WristLower_1920x1080.png`、`Phase09A2_ConsoleFocus_1920x1080.png`，另有 `01…09` 序号帧、`_arms_mask/_arms_dark` 测量帧、`10_fp_idle_2560x1440`、`12_fp_idle_3440x1440`（离屏）、`13_wrist_hold_3440x1440`（离屏）。

测量结论（`phase09a2_result.txt`，全部 PASS）：

- Idle：手臂覆盖 6.1%，bbox 位于画面下方（中心 y=0.13），**中央观察区 0.00%**，双手分别位于左右两侧（x=0.32 / 0.68）。
- Wrist Hold：腕表 cyan 屏幕位于 **左下 x=0.21**、占画面宽 27.4%，中央 0.00%，与 Codex `wrist_idle` 验证构图一致。
- Console Focus / Device Panel：手臂像素 **0**（完全隐藏，不遮挡屏幕 / 面板）。
- 21:9（3440×1440 离屏渲染）：Idle 与 Hold 手臂均在屏内、中央 0.00%、腕表仍在左半。

## 9. 已知限制 / 待人工确认

- **视觉最终验收由人工完成**：本 Phase 的截图与数值均已产出，但“手臂是否自然、抬腕动作是否好看”属于人眼判断（尤其第 4 节的 rest→hold 构图缓动是否可接受）。
- 桌面分辨率上限为 2560×1440，3440×1440 无法以窗口验证，采用 **同一相机离屏 RT 渲染**（同 FOV / 同 aspect 数学，不含 screen-space UI），已在报告中标注。
- 屏幕右下角存在项目既有的小尺寸常驻叠加物（约 68×10 px，主菜单帧中同样存在，与本阶段无关）；测量采用“手臂帧 − 空参考帧”差分，已完全排除其影响。
- `Interact_Right` 按任务 §39 保持 **未接入**：controller 中已保留该 state 与其 clip，但没有任何 transition 驱动它，避免确认动作延迟。
- Surface（Nereid）场景不创建手臂，本阶段仅覆盖 Ship Interior。
