# Phase 09A 最终收口 — Wrist Raise 运动 / 键盘终端输入 / Frosted Focus

本文件记录 Phase09A 最后一次收口修订（task file: `SurviveUIDemo_Phase09A_3_FinalWristPolish_InputHotfix.md`）的三项修复、实测数据与证据位置。本轮不新增玩法、不重建模型、不重设计 Wrist UI 内容。

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

- **实体手臂**：alpha **0.60**、饱和度 **55%**、亮度 **0.88**、朝**奶白冷灰**（`FrostTint` 0.62/0.68/0.76）去饱和 —— 磨砂玻璃，不是黑影；另加 1.06× / alpha 0.10 的柔边 halo。
  实测 mask 帧：crisp 162,944 px @ 0.507 → frosted 189,840 px @ 0.424（对比度与细节被抹平，仍清晰可见）。
- **材质实现**：frosted 集使用项目自有的透明着色器 `Survive/WristDisplayUnlit`（URP Lit 在本项目中即使把 `_Surface/_SrcBlend/_DstBlend/_ZWRITE` 与透明关键字全部设对，实测仍然按不透明渲染：suit 像素 132→133 完全不变，因此改用已验证可混合的着色器；同时 unlit 的平面化正好是磨砂观感）。
- **全息磨砂透镜（方案 A）**：`Survive/WristDisplayUnlit` 新增 `_BaseColor / _Opacity / _FrostOpacity / _MapCentre / _MapRadius / _FrostSoftness / _FrostTint / _FrostGrain`，在地图圆盘（设计空间 centre 640,400 / r 235 → UV 0.5,0.5 / 0.184,0.294）内部把 sheet 不透明度降到 **0.70**，并加 0.26 奶白提亮与 0.035 静态颗粒。世界与 UI 本身不受影响，只在圆盘内让下方实体腕表透出。
- **层序**：实体手臂 queue **2900** < 全息 **3000**（validator 已固化该不等式），UI 永远合成在手臂之上。
- **世界不模糊**：未引入任何 DOF / 后处理；`20_console_focus_compat` 与 Phase 08 基线一致。

## 5. 证据（Release 构建）

| 项目 | 结果 |
| --- | --- |
| Release | `export/releases/phase09a/SurviveUIDemo.exe`（统一目录，构建时间 2026-09-12 14:27） |
| Release smoke | `export/phase09a_release_smoke.txt` — **207 / 0，RESULT: SUCCESS** |
| Release 视觉探针 | `export/phase09a_final_shots/phase09a3_result.txt` — **89 / 0** |
| PlayMode 全量 | `export/phase09a_playmode.xml` — **161 / 161** |
| 结构 validator | `export/phase09a_validator.txt` — **424 / 0** |
| 运动报告 | `export/phase09a_final_shots/wrist_motion_report.txt` |
| Frosted 报告 | `export/phase09a_final_shots/wrist_focus_report.txt` |

关键实测（Release）：

```text
平视            01_fp_forward_view            手臂像素 0
低头            02_fp_look_down               双手分列两侧，中央带 0
Raise 关键帧    03..06_wrist_raise_20/40/60/80  连续单一轨迹
物理 hold       07_wrist_hold_physical        主轴 -1.8°，腕表 (0.491,0.235)
UI 出现         08/09_wrist_before_ui / ui_reveal   UI 在 raise 0.92 开始出现
焦点 hold       10_wrist_hold_frosted         focusBlend 1.00，arm alpha 0.60
UI-only 参考    11_wrist_hold_ui_only_reference
对比证据板      12_wrist_hold_frosted_comparison  上排 08|10，下排 crisp/frosted 手臂 mask
核心透出        13_wrist_core_through_map     mean|diff| 12.48 / 蓝移 2.44 / 覆盖区变化 79.9%
                                              core 区 28.55 / 蓝移 35.96（16k px）
UI 可读性       地图圆盘亮像素 348,165（有手臂） vs 343,641（隐藏手臂），ratio 1.013，峰值一致
键盘终端        14/15/16_terminal_prompt / open / console_exit_esc
Lower           17_wrist_lower               单一连续反向，回到 Rest 后清晰度恢复
Pause / 返回    18_pause / 19_return_main_menu
```

## 6. 诚实标注

- 磨砂为 **pseudo-frosted**：半透明 + 去饱和 + 奶白 tint + 静态颗粒 + 柔边 halo，**不是** Gaussian blur（原因见 §4 与 `wrist_focus_report.txt`）。真实景深/后处理模糊会同时模糊由同一相机渲染的全息 quad 与整个世界，方案 B（手臂单独 RT 软化）需要改渲染路径并把手臂从 eye camera culling mask 移除，风险高于收益，故采用方案 A+C。
- 自动化能证明结构与数值（层序、alpha/emission、可见性、覆盖率、UI 可读性保持），**不能**证明「磨砂好不好看」；后者需人工运行 Release 判断。
- Standalone smoke 在本轮出现 **1 次间歇性输入投递抖动**（全部输入驱动检查级联 FAIL 并伴随一次 NRE；该 NRE 源于新检查未判空 `Wrist.Display`，已加空值防护）。同一二进制复跑为 207/0 SUCCESS，Development 带符号构建 207/0 无异常；此抖动在更早的 Phase09A_3 记录中亦出现过一次。
