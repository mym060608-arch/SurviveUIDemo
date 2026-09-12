# Phase 09A 最终收口 — Wrist Raise 运动 / 键盘终端输入 / Frosted Focus / Surface Map 手柄输入

本文件记录 Phase09A 的收口修订（task file: `SurviveUIDemo_Phase09A_3_FinalWristPolish_InputHotfix.md` 及后续人工验收缺口）的三项修复、Surface Map 手柄输入的回归修复（§12）、实测数据与证据位置。本轮不新增玩法、不重建模型、不重设计 Wrist UI 内容。

## 1. PASS 1 — 键盘 E 无法打开终端（输入回归）

**根因（真实链路，不是猜测）**：`e9edb4f` 的 Esc 优先级改造把「本层 Back 处理」加进了 `ShipConsoleFocusController.TickTransitionIn()`，而 `UiInput.ConsoleExitPressed()` 包含控制台视图的**历史 E 退出绑定**。键盘 E 进入终端时，`FirstPersonInteractor`（E → `TryInteract()` → `RequestEnter()`）与 `ShipConsoleFocusController` 之间**没有确定的 Update 顺序**：若交互器先执行，同一帧 `TickTransitionIn` 立刻读到那次 E 的 `wasPressedThisFrame` → `RequestExit()` → 刚进入就退出。手柄 A 不在 `ConsoleExitPressed()` 里，因此只有键盘 E 回归 —— 与人工观察完全一致。

**修复**：`TryLeaveConsoleView(bool includeLegacyExitKey)`；`TickTransitionIn` 传 `false`（进入过程只允许真正的 Back 语义 B / Esc 取消），`TickFocused` 传 `true`（保留历史 E 退出）。Esc 优先级、手柄 A/B 路径、Pause 行为全部不变。

**验证**：
- `SurvivePhase09AFinalTests.ConsoleEntry_IsNotCancelledByThePressThatStartedIt_Green` —— 该用例在修复前必然失败（同一帧注入的交互按键会被当作 E 退出而取消进入），现在通过。
- `KeyboardE_OpensConsoleFocus_AndEscClosesItWithoutPause_Green`（E 开 → Esc 关且不 Pause → Esc 恢复为 Pause）、`KeyboardE_WithoutATarget_DoesNothing_Green`、`Gamepad_TerminalContract_Remains_Green`。
- 注入缝说明：本 harness **无法**把合成键盘事件送达 `wasPressedThisFrame`（实测：`KeyboardState` 队列事件使控件读数为 1，但边沿恒为 false），因此键盘行使用 `UiInput.InjectInteractForAutomation()`（与既有 Esc/Wrist 注入缝同型，注入的是**物理按键**而非逻辑动作），其下所有层（gaze 门控、focus 控制器、Back 优先级板）都是真实执行；手柄行仍走真实 queued device event。

## 2. PASS 2 — Wrist Raise 改为单一时间轴

- **旧**：`rest → lift → hold` 两段插值（`PathRotation/PathPosition` 在 0.45 处切换），肉眼可见「抬一下 → 变速 → 再转」；时长 0.80 s。
- **新**：单一时长 **0.58 s**，位置与旋转**同一个 normalized t**、同一条 `easeOutCubic`：位置走 **二次 Bezier**（lift 姿态只作曲线控制点，永不成为端点），旋转走 **geodesic `Quaternion.Slerp`**；Lower 0.55 s 严格反向。
- 实测（`wrist_motion_report.txt`，20/40/60/80/100%）：

```text
20%  position=0.490  rotation=0.490  gap=0.000
40%  position=0.786  rotation=0.786  gap=0.000
60%  position=0.938  rotation=0.938  gap=0.000
80%  position=0.993  rotation=0.993  gap=0.000
100% position=0.999  rotation=0.999  gap=0.000
worst |position - rotation| gap: 0.000   （第一帧即同时开始：blend=0.029 / 0.084 / 0.084）
```
- **最终 Hold 姿态未改动**（`holdShoulder`/`holdDir`/`holdFacing` 与上一轮验收值一致）：主轴角 -1.8°、腕表 viewport (0.491, 0.235)、面朝相机 dot 0.997、右手不出框（Release 实测）。

## 3. PASS 3 — UI 提前出现 + Focus 交叉淡入

- `UiRevealNormalizedTime` 1.00 → **0.82**；`HoldSettleSeconds` 0.30 → **0.04 s**：腕表快到位时全息就开始淡入，不再「手到位 → 等一下 → UI 才出现」。Release 实测 reveal 发生在 raise 进度 **0.92**（帧粒度）。
- Focus 不再一帧换材质：`FocusCrossfadeSeconds = 0.14`，由 `FocusBlend` 连续驱动（先进入透明材质集，再逐帧用 MaterialPropertyBlock 把 alpha / 颜色 / 亮度 / 自发光推向 frosted 值）。

## 4. PASS 4 — Frosted 呈现（取消「手臂变黑」）

- **实体手臂**：alpha **0.60**、饱和度 **55%**、亮度 **0.88**、朝**奶白冷灰**（`FrostTint` 0.62/0.68/0.76）去饱和 —— 磨砂玻璃，不是黑影。
  实测 mask 帧：crisp 162,944 px @ 0.507 → frosted 189,840 px @ 0.424（对比度与细节被抹平，仍清晰可见）。
- **不再有灰白轮廓（halo 已删除）**：早期为了软化边缘曾在外层套一圈 1.06× / alpha 0.10 的手臂副本，人工验收时读作「腕表周围一圈明显的灰白色轮廓」。该副本（`BuildSoftHalo` / `_leftHalo` / `_haloRenderers` / `HaloScale` / `HaloAlpha` / `HaloOpacity`）**已整体删除**，手臂渲染器数量回到 12，不存在任何 alpha 描边或放大副本。因此 frost 只由两处产生：**实体材质的柔化**（§4 第一项）+ **全息磨砂透镜**（§4 第三项），UI 始终锐利，青色核心保留。这一约束由测试与报告共同固化：`SurvivePhase09AFinalTests.WristFocus_HasNoGreyOutlineShell_Green` 断言 crisp 与 frosted 两个状态下手臂渲染器数量都为 **12**，且层级中不存在任何名字含 `halo` 的渲染对象；`wrist_focus_report.txt` 也如实记录 `soft edge : none (the oversized halo copy was removed: it read as a grey outline shell)`；探针另有 `block arms renderers=12` 的输出作为对照。
- **材质实现**：frosted 集使用项目自有的透明着色器 `Survive/WristDisplayUnlit`（URP Lit 在本项目中即使把 `_Surface/_SrcBlend/_DstBlend/_ZWRITE` 与透明关键字全部设对，实测仍然按不透明渲染：suit 像素 132→133 完全不变，因此改用已验证可混合的着色器；同时 unlit 的平面化正好是磨砂观感）。
- **全息磨砂透镜（方案 A）**：`Survive/WristDisplayUnlit` 新增 `_BaseColor / _Opacity / _FrostOpacity / _MapCentre / _MapRadius / _FrostSoftness / _FrostTint / _FrostGrain`，在地图圆盘（设计空间 centre 640,400 / r 235 → UV 0.5,0.5 / 0.184,0.294）内部把 sheet 不透明度降到 **0.70**，并加 0.26 奶白提亮与 0.035 静态颗粒。世界与 UI 本身不受影响，只在圆盘内让下方实体腕表透出。
- **层序**：实体手臂 queue **2900** < 全息 **3000**（validator 已固化该不等式），UI 永远合成在手臂之上。
- **世界不模糊**：未引入任何 DOF / 后处理；`20_console_focus_compat` 与 Phase 08 基线一致。

## 5. 证据（Release 构建）

| 项目 | 结果 |
| --- | --- |
| Release | `export/releases/phase09a/SurviveUIDemo.exe`（统一目录，最新构建见 `release_build_result.txt`：errors 0 / warnings 1） |
| Release smoke | `export/evidence/phase09a/smoke/smoke.txt` — **207 / 0，RESULT: SUCCESS**（同一二进制连跑两次均为 207/0） |
| Release 视觉探针 | `export/evidence/phase09a/shots/phase09a3_result.txt` — **147 / 0**（`-stage 6`，含 Ship + Surface + Surface Map） |
| PlayMode 全量 | `export/evidence/phase09a/playmode.xml` — **174 / 174** |
| 结构 validator | `export/evidence/phase09a/validator.txt` — **424 / 0** |
| 运动报告 | `export/evidence/phase09a/shots/wrist_motion_report.txt` |
| Frosted 报告 | `export/evidence/phase09a/shots/wrist_focus_report.txt` |
| 构建 / 运行日志 | `export/logs/phase09a/` |

关键实测（Release）：

```text
平视            01_fp_forward_view            手臂像素 0
低头            02_fp_look_down               双手分列两侧，中央带 0
Raise 关键帧    03..06_wrist_raise_20/40/60/80  连续单一轨迹
物理 hold       07_wrist_hold_physical        主轴 -1.6°，腕表 (0.445,0.235)，facing dot 0.997
UI 出现         08/09_wrist_before_ui / ui_reveal   UI 在 raise 0.92 开始出现
焦点 hold       10_wrist_hold_frosted         focusBlend 1.00，arm alpha 0.60
UI-only 参考    11_wrist_hold_ui_only_reference
对比证据板      12_wrist_hold_frosted_comparison  上排 08|10，下排 crisp/frosted 手臂 mask
核心透出        13_wrist_core_through_map     覆盖区变化 70.4% / mean|diff| 11.03 / 蓝移 0.96
                                               core 区 22.92 / 蓝移 30.61（16,633 px）
UI 可读性       地图圆盘亮像素保持（有手臂 vs 隐藏手臂 ratio ≈ 1.00，峰值一致）
键盘终端        14/15/16_terminal_prompt / open / console_exit_esc
Lower           17_wrist_lower               单一连续反向，回到 Rest 后清晰度恢复
Pause / 返回    18_pause / 19_return_main_menu
地表地图        surface_free_before_map / surface_map_open_gamepad /
                surface_map_closed_back / surface_wrist_no_map_leak（真实 queued gamepad 事件）
```

## 6. 诚实标注

- 磨砂为 **pseudo-frosted**：半透明 + 去饱和 + 奶白 tint + 静态颗粒，**不是** Gaussian blur（原因见 §4 与 `wrist_focus_report.txt`）。真实景深/后处理模糊会同时模糊由同一相机渲染的全息 quad 与整个世界，方案 B（手臂单独 RT 软化）需要改渲染路径并把手臂从 eye camera culling mask 移除，风险高于收益，故采用方案 A+C。
- 自动化能证明结构与数值（层序、alpha/emission、可见性、覆盖率、UI 可读性保持），**不能**证明「磨砂好不好看」；后者需人工运行 Release 判断。
- 手臂 mask 帧在本项目中只反映**对象可见性变化，不反映材质变化**（已实测：三个不同的透明 shader 产生完全相同的 mask 结果），因此 frost 的证明方式是**材质状态读数 + 真实帧的透出测量**，而不是 mask 亮度差。
- Standalone smoke 与视觉探针本轮各暴露过 **1 个 harness 竞态**（探针姿态被实时 look 权威拉走、smoke 入场锁的一帧顺序差），二者都已按「让测试确定性」的方向修掉，细节见 §11；修复后 Release 连跑两次 smoke 均为 207/0，探针为 130/0。此前记录过的「间歇性输入投递抖动」尚未再复现，但它是同一类 harness 时序问题，人工复跑时若再看到级联 FAIL，请先看 `export/logs/phase09a/smoke.log` 里最后一条 `PASS ::`。

## 7. 收口修订 — 删除灰白 halo

**人工反馈**：Wrist Focus 时手臂/腕表周围有一圈明显的灰白色轮廓。

**根因**：上一轮为了软化边缘，在外层套了一圈 **1.06× / alpha 0.10 的手臂副本**。它不是模糊，而是一个略大的半透明壳，在深色舱内背景下读作灰白色描边 —— 恰好与需求相反。

**修复**：该机制**整体删除**（`BuildSoftHalo` / `_leftHalo` / `_haloRenderers` / `HaloScale` / `HaloAlpha` / `HaloOpacity` 全部移除），手臂渲染器数量回到 **12**，不存在任何放大副本或 alpha 描边。Frost 现在只由两处产生：

```text
实体材质的柔化     alpha 0.60 / 去饱和 0.45 / 亮度 0.88 / 奶白冷灰 tint
全息磨砂透镜       _FrostOpacity 0.70 + 奶白提亮 0.26 + 静态颗粒 0.035
```

UI 依然锐利（未被触碰），青色核心依然保留。回归保护见 §4 提到的 `WristFocus_HasNoGreyOutlineShell_Green`。

## 8. 收口修订 — 腕表核心与地图对齐（基于实测，不是盲移）

**人工反馈**：Wrist Focus 时腕表核心与 Wrist UI 地图中心不重合。

**方法**：先用 `WatchCoreViewport`（真实 screen-renderer bounds 中心）**测量**核心在 viewport 中的位置，再换算成世界位移，而不是固定「左移 N 像素」。

```text
测量（改动前）  core viewport x = 0.495   map centre x = 0.442   deltaX = +0.055
换算            FOV 70 / 16:9 -> 单位深度 1.2448 视口宽
                核心深度 ≈ 0.414 m  =>  1 视口单位 ≈ 0.490 m
位移            holdShoulder.x  -0.310 -> -0.367  （Δ = -0.057 m ≈ -0.043 视口）
测量（改动后）  core = (0.441, 0.227, 0.416)   map = (0.440, 0.438, 0.935)
                deltaX = +0.001   deltaY = -0.211
```

**验收门**：`deltaX` 容差 **±0.02 视口**（Release 实测 +0.001，通过）；`deltaY` 容差 ±0.30（腕表必须在画面下半部，通过）。构图约束同时被守住：左前臂主轴仍近水平（Release 实测 -1.6°）、腕表仍朝向相机（facing dot 0.997）、右手仍不出框（0 renderers inside the frustum）、只移动 `LeftArm` presentation，没有把整个 `BlockArmsRoot` 旋入相机。核心没有被推到地图最左侧（`MapCentre.x` = 0.440）。

## 9. 收口修订 — Block Arms 扩展到行星地表

**目标**：手臂不再是 Ship 专属，而是**环境无关**的一层第一人称表现。

**架构**：

```text
FirstPersonBlockArmsController        唯一实现（几何 / Rest / Walk / Raise / Hold / Lower / frosted）
        ▲
        │ 由环境适配器提供上下文，不反向依赖环境
IArmsPresentationContext              Player / IsPaused / HidesFirstPersonPresentation
        │                             SetWristTerminalVisible(bool)
        ├─ ShipGameController         实现（entry lock / console focus / overlay 视为隐藏）
        └─ SurfaceGameController      实现（同一合同，Surface 自己决定 terminal 时机）
WristPresentationBridge               驱动 raise -> UI reveal -> frost crossfade -> lower
```

- 环境只提供 **adapter/context**：player/root transform、eye camera、movement state、wrist authority、visibility。控制器本身不认识 Ship 或 Surface，也不持有它们的引用。
- **Surface 复用同一份几何、同一套 Rest / Walk / Raise / Hold / Lower、同一套 frosted 呈现**；唯一差别是 Surface 的 terminal 依旧即时出现（这是它自己的历史合同，Phase 06 测试锁定），而手臂仍然执行同一次 raise 与 frost。
- **唯一性**：`Active FirstPersonBlockArmsController count == 1` 在任意时刻成立；没有重复实例、没有遗留实例。validator 继续固化 Block Arms unique / no duplicate camera / no duplicate AudioListener。

**测试覆盖**（本轮新增 / 更新共 13 条：`SurvivePhase09ASurfaceArmsTests.cs` 3 条 + `SurvivePhase09AFinalTests.cs` 10 条；Ship 侧的实例数、Return Main 清理、Continue 重建沿用 `SurvivePhase09A3BlockArmsTests`）：

```text
Surface_HasOneBlockArmsPresentation_AndDrivesIt_Green
        地表恰好 1 个实例，且前视 / 低头 / 腕表开合行为与 Ship 一致
Surface_WristRaiseHoldLower_UsesTheSamePresentation_Green
        地表复用同一次 raise / hold / lower 与同一套 frosted 呈现
SceneTransitions_NeverDuplicateTheArms_Green
        Ship <-> Surface 往返后仍恰好 1 个实例（无重复 / 无遗留）
SurvivePhase09A3BlockArmsTests（既有，继续通过）
        ReturnToMainMenu_CleansUp_AndContinueCreatesOne_Green
        Return Main 后实例被清理；Continue 后重新创建且恰好 1 个
```

**Ship 合同未回归**：平视无手、低头出现双手、wrist raise/hold/lower、Console Focus 与设备面板期间手臂隐藏、Pause 冻结当前状态、Return Main 清理、Continue 恰好一个实例 —— 全部沿用并通过。

## 10. export 目录规范

从本轮起新产物统一放：

```text
export/releases/phase09a/               统一人工测试入口（SurviveUIDemo.exe + SurviveUIDemo_Data/）
export/evidence/phase09a/shots/         契约截图、mask 帧、对照板、motion/frosted 报告
export/evidence/phase09a/smoke/         standalone smoke 结果与 smoke 截图
export/logs/phase09a/                   构建 / 探针 / 测试 / validator 日志
```

旧的 `export/phase0X_*`、`export/surviveui_*`、`export/phase09a3_*` 等目录**保留为历史 evidence，不删除、不再写入**。完整约定见 `export/README.md`。

本轮任务要求的十张环境证据帧全部存在（`export/evidence/phase09a/shots/`）：

```text
ship_forward.png              surface_forward.png
ship_lookdown.png             surface_lookdown.png
ship_wrist_hold.png           surface_walk_lookdown.png
wrist_core_map_alignment.png  surface_wrist_raise.png
                              surface_wrist_hold.png
                              surface_wrist_lower.png
```

## 11. 本轮在证据链中发现并修复的两个 harness 竞态

两条都不是产品 bug，但会让证据自己变得不可信，因此都按「让测试确定性」的方向修掉了，而不是重跑碰运气。

**(1) 探针姿态漂移**（`Ship09A3ShotProbe`）

重新生成证据时出现 **107 PASS / 22 FAIL**：`02_fp_look_down` 与 `surface_lookdown` 的手臂像素为 0。诊断输出给出了根因：

```text
ship lookdown diag:    pitch=16.6   <-- 探针要求 45 度，实测只剩 16.6 度
surface lookdown diag: pitch=45.0
```

`SurfacePlayerController.Update` 每帧读取**真实鼠标 delta** 并调用 `ApplyLook`，所以探针「设一次姿态 → 等 0.7 s → 截图」的写法并不可靠：姿态在等待期间被实时 look 权威拉走，45 度俯角衰减到 16.6 度后手臂已经离开画面。哪一次跑偏是随机的（上一轮是 ship 通过、surface 失败，这一轮正好相反）。

修复：探针新增**确定性姿态钉（pose pin）**，在 `LateUpdate`（即所有 `Update` 之后、渲染之前）重新施加姿态，并带过期时间以免泄漏到后面的 stage；行走帧只钉朝向、放开位置，保证真的能走动。修复后同一姿态在 Ship 与 Surface 上读数完全一致：

```text
ship lookdown diag:    pitch=45.0 eyeX=45.0 visible=True state=Rest  rootParent=Player
                       rootWorld=(0.00,1.73,0.00) watchWorld=(0.32,1.27,-0.17)
surface lookdown diag: pitch=45.0 eyeX=45.0 visible=True state=Rest  rootParent=Player
                       rootWorld=(0.00,1.65,0.00) watchWorld=(0.32,1.19,-0.17)
```

（顺带这也成了「Surface 复用同一套几何与姿态」的硬证据：两个环境里腕表在世界空间中的相对位置一致。）

**(2) smoke 的入场锁一帧竞态**（`StandaloneSmoke`）

`control is released after the eyes open` 曾出现过 **1 次 FAIL**（206/1）。日志显示运行时的顺序是：

```text
[ShipEntryBlinkFade] eyelid entry finished (Full)
[ShipGameController] entry eyelids finished - first-person control released
```

眨眼覆层由 blink 组件销毁、输入锁由 Ship 控制器释放，二者在同一帧发生，协程先观察到哪一个取决于脚本执行顺序 —— 因此这是一帧竞态。修复：断言前在**有界窗口**（120 帧）内等待释放，断言本身不变（锁永不释放仍然 FAIL）。修复后同一 Release 连续两次 smoke 均为 **207 / 0 SUCCESS**。

这两处修复都只改证据/harness 代码与被测行为无关的等待策略，没有改动任何玩法、呈现或输入合同。

## 12. 收口修订 — Surface Map 的手柄输入（Xbox X / buttonWest）

**人工反馈**：Planet / Surface gameplay 中，手柄按 X 无法打开地图。

### 12.1 真实根因（代码 + 历史双重确认）

不是「最近某次改动把旧轮询删掉了」，而是 **Surface 从来没有绑过任何手柄面键**：

```csharp
// 修复前 SurfaceGameController.PollWristKeys()
Keyboard keyboard = Keyboard.current;
...
if (keyboard.mKey.wasPressedThisFrame || keyboard.iKey.wasPressedThisFrame) { ToggleWrist(); }
```

- 它**直接读键盘 M / I**，从不消费逻辑动作，因此 `buttonWest`（X）与 `buttonNorth`（Y）在星球地表**都没有任何效果**；
- 全项目里 `buttonWest` 的唯一出现点是 `InputDeviceTracker.GamepadProducedInput()`（判断「玩家是否在用柄」），**从未绑定过任何 action**；
- `UiInput.WristPressed()`（Y / M / I）只有 `ShipGameController` 调用，Surface 不用它；
- `git log -p` 该文件的历史里也不存在被删除的手柄轮询 —— 所以这不是本轮的 `IArmsPresentationContext` 改造造成的回归（该改造的 diff 只新增了 arms/bridge 与显式接口实现，没有动输入）。

### 12.2 修复（只动输入，不动表现 / 美术 / Block Arms）

`UiInput` 新增平台无关的逻辑动作 **`Map`**：

```text
Map=Gamepad.buttonWest,Keyboard.mKey|iKey     <- 星球地表地图（Xbox X / PlayStation Square）
Wrist=Gamepad.buttonNorth,Keyboard.mKey|iKey  <- 腕表（Xbox Y）
```

```csharp
public static bool MapPressed()   // buttonWest 或 M / I；物理映射只活在这一层
```

`SurfaceGameController.PollWristKeys()` 改为消费两个语义动作，并沿用既有 Back 架构与优先级板：

```text
腕表已开：Esc / B -> 本层 Back 关闭（ConsumeBackForFrame，绝不穿透到 Pause）
          X 或 Y   -> 关闭同一个地图（不叠加第二套 UI）
腕表未开：仅当 ActiveBackLayer == Gameplay（自由玩法）才允许打开；
          Pause / Modal / ConsoleFocus 拥有该帧时一律不开
```

地表地图就是腕表中央那张地图（Phase 06 的既有设计），因此 X 与 Y 指向**同一个权威**，不可能叠加出两份地图；键盘路径仍是原来的 M / I。ship 侧不消费 `Map`，所以 X 在舰内是空操作。

### 12.3 测试与证据

PlayMode 新增 `SurvivePhase09ASurfaceMapTests`（9 条，全部走**真实 queued Gamepad / Keyboard 设备事件**）：

```text
SurfaceFree_ButtonWest_OpensTheMap_Green                    X 打开地图 + 绑定表静态钉
Surface_ButtonNorth_StillOpensTheWrist_Green                Y 仍打开腕表（X/Y 未互换）
SurfaceMap_ButtonEast_ClosesTheMap_Green                    B 关闭
SurfaceMap_Escape_ClosesTheMap_WithoutPause_Green           Esc 关闭且不 Pause
SurfaceMap_ButtonWest_TogglesItClosed_WithoutASecondMap_Green  X 不会叠出第二份地图
SurfacePauseLayer_ButtonWest_DoesNotOpenTheMap_Green        Pause 层拥有该帧 -> 不开
SurfaceModalLayer_ButtonWest_DoesNotOpenTheMap_Green        模态层拥有该帧 -> 不开
ShipInterior_ButtonWest_NeverOpensTheSurfaceMap_Green       舰内 X 空操作，Y 仍开腕表
Surface_ConfirmBackAndPause_NeverOpenTheMap_Green           A / B / Menu 都不开地图
```

Release 探针新增 `SurfaceMapStage`，用真实 queued 事件端到端跑一遍并输出四张证据帧（`surface_free_before_map` / `surface_map_open_gamepad` / `surface_map_closed_back` / `surface_wrist_no_map_leak`），同时固化：绑定表包含 `Map=Gamepad.buttonWest` 与键盘路径、任何逻辑行都不出现 `XButton/YButton/PlayStation/StartButton`。

### 12.4 harness 记录：standalone 探针里「运行中途新增的柄收不到事件」

要让探针用**真实** queued 事件，必须先解决一个 harness 现象（已实测，不是推测）：

```text
运行开始时 AddDevice<Gamepad>()：buttonWest/buttonSouth 读数 1.00，edge=True   -> 事件正常投递
运行约 2.5 分钟后（地表场景）AddDevice<Gamepad>()：
  added=True enabled=True current=True focused=True background=IgnoreFocus updateMode=ProcessEventsInDynamicUpdate
  但 buttonWest 读数恒为 0.00，任何 queued 事件都没有送达      -> 事件完全不投递
```

因此探针改为**在 run 开始时创建一次手柄并全程复用**（与 `StandaloneSmoke` 同样的形状），并在 `EnsureGamepadDevice()` 里用 `buttonWest` 自己证明投递（在舰内 X 是空操作，所以这条自证不会扰动产品状态）。`StandaloneSmoke` 与探针也都把 `backgroundBehavior` 设为 `IgnoreFocus`（仅在被 flag 的 harness 运行时生效，正常启动游戏不受影响），以消除「窗口失焦时新增设备被禁用」这一类不确定性。


