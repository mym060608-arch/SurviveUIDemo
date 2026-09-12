# Phase 09A_3 — Block Arms Runtime Finalization

本文件记录 Phase 09A_3 的最终收束：把第一人称手臂的**运行时表现**从 Codex 的 FBX/Animator 资产换成 DSH 直接生成的 voxel / block-style 手臂，并与既有 Wrist System、控制台、暂停、返回主菜单等契约共存。Phase 09A_2 的资产与文档保留为历史，不再参与运行时。

## 1. 决策与范围

- 冻结继续优化旧 Codex 第一人称双手资产，改为在 Unity 内直接用简单几何体搭建 block arms，作为 Phase 09A 的最终收束方案。
- 旧资产仍然留在仓库里作为历史参考（`Resources/FirstPerson/PF_FirstPersonArms.prefab`、导入的 FBX、`Animator`、材质），但**运行时不再创建**；`FirstPersonArmsController.cs` 顶部已加历史说明。
- 现有 Wrist System（`ShipGameController` → `SurfaceWristController` → `WristDisplayAnchor`）仍然是唯一 UI / interaction authority，手臂只做表现。

## 2. 交付物

| 类型 | 路径 |
| --- | --- |
| 运行时手臂（新建） | `Assets/_Project/Scripts/Gameplay/FirstPersonBlockArmsController.cs` |
| 表现桥（改造） | `Assets/_Project/Scripts/Gameplay/WristPresentationBridge.cs` |
| 控制器接入 | `Assets/_Project/Scripts/Gameplay/ShipGameController.cs`（`EnsureFirstPersonArms()`） |
| 证据探针 | `Assets/_Project/Scripts/Core/Ship09A3ShotProbe.cs`（替代已删除的 `Ship09A2ShotProbe.cs`） |
| UI 完整性审计 | `Assets/_Project/Scripts/Core/UiIntegrityAudit.cs` |
| 测试 | `Assets/_Project/Tests/PlayMode/SurvivePhase09A3BlockArmsTests.cs`（替代已删除的 A2 测试） |
| 场景 | `00_Boot.unity` 上的探针对象由 `Ship09A2ShotProbe` 换为 `Ship09A3ShotProbe` |

## 3. 手臂实现（纯 transform 表现）

- 由代码生成几何体：`Forearm` / `Cuff` / `Hand` / `Thumb` / 腕表（`WatchBase` + `WatchScreen` + 两个按键），材质用 URP Lit 在代码中创建（suit / glove / cuff / accent / device / screen）。
- 无 `Animator`、无动画剪辑、无 `SkinnedMeshRenderer`：状态切换是**位置与旋转的插值**（`Mathf.MoveTowards` 推 blend + smoothstep 缓动），因此 `Time.timeScale = 0`（暂停）天然冻结。
- 无 collider、不投影、不参与物理或交互射线；整组挂在 `FirstPersonArms` layer，eye camera 的 culling mask 单独渲染该层。
- 状态：`Rest` / `Walk` / `WristRaise`（0.8s） / `WristHold` / `WristLower`（0.667s），`CurrentStateName` 供测试与探针读取。

## 4. 构图决策（相机空间 vs 身体空间）

- 手臂根节点是 player rig 的子节点（**身体空间**，跟随 yaw、不跟随 camera pitch）：平视时双手完全在画面之外，低头时左右手臂分别从下方左右进入画面。
- 只有在腕表呈现期间，根节点才按 `Quaternion.Euler(pitch * presentBlend, 0, 0)` 混入相机空间；此时视角冻结，UI 与手臂一起稳定在左下角。
- Hold 姿态是**向前并偏向身体左侧**伸出的（`holdShoulder ≈ (-0.260, -0.219, 0.173)`、`holdDir ≈ (-0.583, -0.086, 0.808)`）：这条跨身体的夹角是构图关键 —— 若前臂直直向前伸，投影会沿对角线爬到全息面板环里；偏向视线轴外侧后才稳定停在全息 UI 的**下方支撑位**。
- Wrist UI（既有全息终端）在相机空间锚定，物理手腕/手臂在其下方作为支撑：`wrist display viewport ≈ (0.442, 0.440)`、`pivot viewport ≈ (0.346, 0.153)`、`physical watch viewport ≈ (0.080, 0.086)`。
- 全息呈现期间手臂被**柔化**（`MaterialPropertyBlock` 压暗：基础色 ×0.62、自发光 ×0.5），不实例化材质；实测手臂平均亮度 0.318（柔化）vs 0.438（未柔化），即压暗到约 73%，仍清晰可见。

## 5. 时序与权限划分

- Authority（`ShipGameController`）：按下 Wrist 后 `IsWristOpen` 立即为真，输入锁定 / pointer ownership / 暂停行为沿用 Phase 08。
- Presentation（`WristPresentationBridge`）：只决定**何时**揭示既有终端（`UiRevealNormalizedTime = 0.78`、`RaiseRevealTimeoutSeconds = 1.25`）以及柔化开关；无手臂时立即揭示，现有 Wrist System 不依赖手臂。
- 收腕：既有 UI 先释放，再播放 Lower，回到 `Rest`。

## 6. 验证证据

| 项目 | 结果 |
| --- | --- |
| 契约帧探针 | `export/phase09a3_shots/phase09a3_result.txt` —— **70 checks / 0 failures**（11 个契约帧 + 遮罩测量 + UI 完整性断言） |
| 帧清单（1920×1080） | `01_fp_forward_view`、`02_fp_look_down`、`03_fp_walk_look_down`、`04_wrist_raise`、`05_wrist_hold`、`06_wrist_hold_ui_above_watch`、`07_wrist_lower`、`08_console_focus`、`09_device_panel`、`10_pause_menu`、`11_return_main_menu`（另有 `06b_hold_unsoftened` 柔化对照帧与 `_arms_mask/_arms_dark` 测量帧） |
| 构图测量 | 平视 **0 px**；低头 298,274 px（左 155,143 / 右 143,131，中央 0）；Walk 298,625 px（中央 0）；腕表 hold 81,481 px，bbox x 0..321 / y 0..286，中央 0；控制台 / 设备面板 **0 px** |
| 全息让位 | 地图圆盘 **0 px**、面板环 **0 px**（修复后再次确认） |
| PlayMode | `export/phase09a3_playmode.xml` —— **144 / 144 passed**（含 11 项 Phase09A_3） |
| 结构 validator | `export/phase09a3_validator.txt` —— **420 checks / 0 failures** |
| Standalone smoke | `export/phase09a3_smoke.txt` —— **162 checks / 0 failures / 0 exceptions**（真实输入路径：手臂 / Walk / 腕表升起-保持-落下 / 控制台 / 暂停 / 返回主菜单 / Continue 唯一实例） |
| 正常流程视觉证据 | `export/phase09a3_smoke_shots/smoke_ship_interior.png`、`smoke_pause_gameplay.png` |

## 7. 与 UI 热修的关系

本阶段收尾时发现并修复了证据探针造成的 UI 破坏（暂停菜单常驻 + 多屏叠加 + 未上样式）：成因是遮罩测量用 `UIDocument.enabled` 屏蔽 UI，而本项目所有 USS / display 状态都是代码挂载，重建视觉树即全部丢失。修复细节与证据见 `Docs/Phase09A_3_UiRegression_Hotfix.md`。

要点：**产品 UI 未改动一行**。工作区 diff 中 `*.uxml` / `*.uss` / `*.asset` / `*.tss` / `*.prefab` 与 `Assets/_Project/UI/`、`Assets/_Project/Resources/` 全部为空；`AppController` / `ShipOverlayUi` / `ShipPauseMenu` / `SurfaceWristController` 未被修改。

## 8. 已知限制 / 待人工确认

- **视觉最终验收由人工完成**：构图、手臂比例、柔化强度、全息与手臂的层次是否“好看”属于人眼判断。
- 腕表 hold 构图在 16:9（产品目标分辨率 1920×1080）下验证；更窄的宽高比下物理腕表会更靠近屏幕左下边缘（PlayMode 测试因此改为断言相机空间左下象限 + 16:9 时断言视口坐标）。
- `Interact_Right`（旧资产动作）随旧 Animator 一起不再使用，block arms 不含该动作。
- Surface（Nereid）场景不创建手臂，本阶段仅覆盖 Ship Interior。
- 单次自动化 smoke 运行出现过一次未复现的 `NullReferenceException`（伴随输入驱动检查级联失败），随后 3 次运行均为 162/0 且无异常，记录在热修文档 §7。
