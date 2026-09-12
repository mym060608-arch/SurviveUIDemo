# Phase 09A_3 — Block Arms Runtime Finalization

> **Note:** the wrist motion, UI reveal timing and focus presentation described
> here were revised once more in the Phase 09A final polish round — see
> `Docs/Phase09A_FinalWristPolish.md` (single-timeline raise, reveal at 0.82,
> frosted focus with the cyan core readable through the map). The Hold pose
> itself is unchanged.

本文件记录 Phase 09A_3 的最终收束：把第一人称手臂的**运行时表现**从 Codex 的 FBX/Animator 资产换成 DSH 直接生成的 voxel / block-style 手臂，并与既有 Wrist System、控制台、暂停、返回主菜单等契约共存。Phase 09A_2 的资产与文档保留为历史，不再参与运行时。

## 1. 决策与范围

- 冻结继续优化旧 Codex 第一人称双手资产，改为在 Unity 内直接用简单几何体搭建 block arms，作为 Phase 09A 的最终收束方案。
- 旧资产仍留在仓库作为历史参考（`Resources/FirstPerson/PF_FirstPersonArms.prefab`、导入 FBX、`Animator`、材质），**运行时不再创建**；`FirstPersonArmsController.cs` 顶部已加历史说明。
- 现有 Wrist System（`ShipGameController` → `SurfaceWristController` → `WristDisplayAnchor`）仍是唯一 UI / interaction authority；block arms 只做 presentation，**不读取任何设备输入**（不碰 `Gamepad.current` / `Keyboard.current`）。

## 2. 交付物

| 类型 | 路径 |
| --- | --- |
| 运行时手臂 | `Assets/_Project/Scripts/Gameplay/FirstPersonBlockArmsController.cs` |
| 表现桥（Stage A/B 时序 + 柔化开关） | `Assets/_Project/Scripts/Gameplay/WristPresentationBridge.cs` |
| 控制器接入 | `Assets/_Project/Scripts/Gameplay/ShipGameController.cs` |
| 证据探针 | `Assets/_Project/Scripts/Core/Ship09A3ShotProbe.cs` |
| UI 完整性审计 | `Assets/_Project/Scripts/Core/UiIntegrityAudit.cs` |
| 测试 | `Assets/_Project/Tests/PlayMode/SurvivePhase09A3BlockArmsTests.cs` |
| 场景 | `00_Boot.unity` 上的 `Ship09A3ShotProbe` |

## 3. 手臂实现（纯 transform 表现）

- 代码生成几何体：`Forearm` / `Cuff` / `Hand` / `Thumb` / 腕表（`WatchBase` + `WatchScreen` + 两个按键），URP Lit 材质在代码中创建。
- 无 `Animator`、无剪辑、无 SkinnedMesh、无 collider；状态切换是**位置与旋转插值**（`Mathf.MoveTowards` 推 blend + smoothstep 缓动），`Time.timeScale = 0` 天然冻结。
- 状态：`Rest` / `Walk` / `WristRaise`（0.8 s） / `WristHold` / `WristLower`（0.667 s）。

## 4. 构图契约（本轮修订）

- 手臂根节点是 player rig 的子节点（**身体空间**，跟随 yaw、不跟随 camera pitch）：平视时双手不可见，低头时左右手臂从下方两侧进入画面。
- **共享 root 永不旋转**（不再把整个 `BlockArmsRoot` 旋进相机）。抬腕时只由**左臂自己的 presentation root** 补偿 camera pitch（位置与旋转同时按 `Quaternion.Euler(pitch * presentBlend,0,0)` 补偿），因此腕表在任意俯仰下都稳定对位于相机锁定的全息 UI。
- **Hold 姿态：左前臂横放在胸前 / 视线下方**
  - 肘部 `(-0.310, -0.170, 0.480)`，前臂轴向 `(+0.997, -0.050, +0.060)`（屏幕水平的世界轴 = 玩家右手方向），
  - 腕表面朝向相机（local +Z ≈ 指向眼睛），
  - 实测：屏幕主轴角 **-1.8°**（近水平，契约允许 ±15°），腕表 viewport **(0.491, 0.235)**（自下而上；自上而下 ≈ 0.765，落在 x 0.40~0.50 / y 0.67~0.80 目标带内），腕表面朝向点积 **0.997**，
  - 右手在呈现期间**完全在画面外**（frustum 内 renderer 数 = 0）。
- Raise 走 `rest → lift → hold` 三段单一缓动路径（lift = 抬肘中间键），旋转均匀分布，末段不会突转；Lower 严格反向。

## 5. Stage A / Stage B 与 Wrist Focus 视觉层级

```text
Stage A  Raise    前臂清晰抬起 → 到达水平 hold 姿态（保持 0.30 s 的 crisp 停顿）
Stage B  Focus    Wrist UI 完全出现 → 物理手臂/腕表退为次级视觉层（ghost）
关闭              先释放 UI → 手臂立即恢复清晰 → Wrist Lower
```

- reveal 阈值 `UiRevealNormalizedTime = 1f`（抬腕完全到达）+ `HoldSettleSeconds = 0.30f`：UI 在 hold 姿态稳定后才出现，Stage A/B 因此是**真实存在的两个阶段**，而不是只存在于参数里。
- 柔化实现（Option C，见 §8）：`SetSoftened(true)` 把每个 block 换成**透明 ghost 材质**——alpha 0.38、向背景灰去饱和 70%、亮度 0.52、去掉自发光强调色，并显示一份 1.075 倍、alpha 0.11 的柔边 halo 副本。
- **层序保证**：ghost 材质进入透明队列但使用 **queue 2900**，低于腕表全息材质的 3000 —— 手臂距相机更近，仅靠距离排序会把手臂盖在 UI 上；显式队列确保 **UI 永远合成在物理手臂之上**，可读内容不被遮挡。
- 世界与 UI 均**不做任何全屏模糊**：`08_console_focus` 帧统计 140,144,149 与 Phase 08 基线 `05b_ship_terminal_overview.png` 完全一致。

## 6. 本轮改动文件

| 文件 | 改动 |
| --- | --- |
| `FirstPersonBlockArmsController.cs` | 新 hold/lift 姿态、三段缓动路径、左臂 pitch 补偿、ghost + halo 材质与柔化切换、移除共享 root 旋转 |
| `WristPresentationBridge.cs` | Stage A/B 时序（reveal 等到 raise 完成 + 0.30 s hold 停顿）、柔化跟随**全息是否可见**（关闭即恢复清晰） |
| `SurviveProjectValidator.cs` | 新增「柔化手臂层序早于全息」结构校验；修正此前被静默跳过的腕表层序校验（Editor 下 `wrist.Display` 为 null → 改为显式解析 `WristDisplayAnchor` + 校验 shader 队列） |
| `Ship09A3ShotProbe.cs` | 帧集与命名对齐人工验收清单；新增主轴角 / 腕表位置 / 朝向 / 右手出框 / 焦点权重 / UI 可读性门限；生成 2×2 对比证据板 |
| `SurvivePhase09A3BlockArmsTests.cs` | 腕表姿态断言改为相机空间契约（跨画面轴、面朝相机、16:9 视口带）、右手出框、层序断言 |

## 7. 验证证据

| 项目 | 结果 |
| --- | --- |
| **Release Standalone** | `export/phase09a3_release_player/SurviveUIDemo.exe`，构建时间 **2026-09-12 11:59:52**，0 compile errors |
| Release smoke（真实输入路径） | `export/phase_a3b_release_smoke.txt` — **196 / 0，RESULT: SUCCESS**，0 exceptions（含 New Game → Ship Interior、Esc 优先级、Wrist Raise/Hold/Lower、暂停、设备面板） |
| Release 视觉探针 | `export/phase_a3b_release_shots/phase09a3_result.txt` — **75 / 0** |
| PlayMode 全量 | `export/phase_a3b_playmode.xml` — **152 / 152** |
| 结构 validator | `export/phase_a3b_validator.txt` — **424 / 0** |
| UI 热修回归 | 见 `Docs/Phase09A_3_UiRegression_Hotfix.md` |
| 输入契约 | 见 `Docs/Input_EscapeBackPriority.md` |

关键门限实测（Release 构建）：

```text
平视           01_fp_forward_view          手臂像素 0
低头           02_fp_look_down             298k px，左右手分列两侧，中央带 0
Raise 中段     03_wrist_raise_mid          手臂清晰（crisp），UI 尚未出现
物理 hold      04_wrist_hold_physical      主轴 -1.8°，腕表 (0.491,0.235)，softened=False
焦点 hold      05_wrist_hold_focused_ui    主轴 -1.8°，腕表 (0.491,0.234)，softened=True
焦点权重       crisp 164,115 px @ 0.511 → focused 190,768 px @ 0.227，ratio 0.52
UI 可读性      地图圆盘亮像素 330,396（有手臂） vs 330,398（隐藏手臂），ratio 1.000；峰值 0.989 = 0.989
全息中心       display viewport y 0.437 > 物理腕表 y 0.234（UI 在腕表之上）
右手           呈现期间 frustum 内 renderer 数 = 0
控制台/面板    08/09 手臂像素 0；世界未模糊（140,144,149 = Phase 08 基线）
```

人工验收截图（`export/phase_a3b_release_shots/`）：

- `01_fp_forward_view.png`、`02_fp_look_down.png`、`02b_fp_walk_look_down.png`
- `03_wrist_raise_mid.png`（Stage A：手臂清晰抬起）
- `04_wrist_hold_physical.png`（前臂近水平、腕表朝 Camera、UI 尚未出现）
- `05_wrist_hold_focused_ui.png`（UI 最上层锐利，物理手臂 ghost 化）
- `05b_wrist_hold_ui_only.png`（隐藏手臂的对照帧，用于 UI 可读性测量）
- `06_wrist_focus_comparison.png`（**2×2 证据板**：上排 = 04 | 05 真实画面，下排 = crisp / ghost 手臂 mask）
- `07_wrist_lower.png`、`08_console_focus.png`、`09_device_panel.png`、`10_pause_menu.png`、`11_return_main_menu.png`

## 8. 为什么用「强烈伪柔化」而不是真 blur

- 腕表全息是一个**世界空间 quad**，由同一台 eye camera 渲染：任何后处理 / DOF 模糊都会同时模糊 UI，违反「UI 必须锐利」（§7），DOF 还会把整个船内世界一起糊掉。
- 把手臂单独渲染到低分辨率 RT 再合成（真 blur）需要改渲染路径并让 UI 后绘制，且必须把手臂从 eye camera 的 culling mask 移除 —— 会与现有手臂 mask 证据链、控制台/腕表 RT 链冲突，风险远高于收益。
- 因此采用允许的 Option C，并把强度做到肉眼明显：透明度 0.38 + 去饱和 70% + 亮度 0.52 + 移除自发光 + 1.075× 柔边 halo；焦点权重实测降到 **0.52**（手臂像素平均亮度 0.511 → 0.227），同时仍能读出「可见的物理支撑」。

## 9. 已知限制 / 待人工确认

- **视觉最终验收由人工完成**：姿态美观度、ghost 强度是否合适、halo 是否自然，需要人眼判断。
- ghost 材质 `ZWrite Off`：手臂自身面片按物体排序混合（同一 cube 的面序不保证），呈现为半透明块体；这是 ghost 观感的一部分。若人工觉得边缘不自然，只需调 `SoftenedAlpha` / `HaloAlpha` 两个常量。
- 腕表 hold 构图在 16:9（产品目标 1920×1080）下定标；更窄宽高比下断言改为相机空间契约（跨画面轴 + 面朝相机 + 画面内）。
- Surface（Nereid）场景不创建手臂，本阶段仅覆盖 Ship Interior。
