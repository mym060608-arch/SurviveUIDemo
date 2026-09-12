# Phase 09A_3 热修 — UI 回归隔离与修复（Pause Menu 叠加 / 未上样式）

本文件记录「Phase09A 自动化自测期间出现的严重 UI 回归」的定位、修复与证据。它是本次热修的唯一记录，产品 UI 本身未做任何重新设计。

结论先行：

- 回归**不在产品 UI 代码里**，而在 Phase 09A_2 引入、Phase 09A_3 沿用的**证据探针（shot probe）遮罩测量**里。
- 归因：探针用 `UIDocument.enabled = false / true` 屏蔽屏幕空间 UI。本项目**没有任何 UXML 内嵌 `<Style>`**，所有 USS 与 display 状态都是控制在绑定时代码设置的，因此一次 disable/enable 就会从 UXML 重建视觉树，**样式表与 display 状态全部丢失**。
- 症状与截图记录完全对应：PauseRoot 绑定时被设为 `display: None`，重建后恢复成 UXML 默认可见 → 未上样式的暂停菜单常驻在画面上，同时 HUD / 各面板全部以默认 UI Toolkit 样式铺满 → “Pause Menu 可见 + 多个产品页同时出现 + 行高异常 / 文字重叠 / 间距错乱”。
- 修复方式：探针改为**非破坏性**隐藏（只改可完全还原的 `visibility`），并补上 UI 完整性断言与结构守卫；**产品 UI 未改动一行**。

## 1. 范围判定（正常启动 vs 自动化启动）

| 运行方式 | 结果 | 判定依据 |
| --- | --- | --- |
| 正常启动（无任何命令行 flag） | **PASS** | `00_Boot` 上的 `Ship09A3ShotProbe` 在无 `-ship09A3Shot` 时于 `Awake` 立即 `enabled = false` 并返回，不注册任何对象、不改 UI、不路由场景；`Ship09A3ShotProbe` 之外的代码库中不存在任何 `UIDocument.enabled` 写入（见 `SurviveProjectValidator.ValidateEvidenceProbeHygiene`） |
| 自动化启动（`-ship09A3Shot`） | 修复前 **FAIL** → 修复后 **PASS** | 修复前：第一个遮罩帧之后的每一帧都被未上样式的 UI 覆盖；修复后：`phase09a3_result.txt` 每帧 `the product UI is intact` PASS，且帧统计与 Phase 07B.23 / Phase 08 基线一致 |

即：这是**自动化证据基础设施造成的破坏**，不是产品回归；但它污染了证据帧，并且只在自测运行中出现（正常启动不受影响）。

## 2. 最小可疑改动集

用 `git log -S` 定位（只读，未改写历史）：

```powershell
git log --oneline -S "document.enabled = false" -- Assets/_Project/Scripts/Core/
# 1593844 feat(gameplay): integrate first-person arms with the wrist system
```

- **引入 commit**：`1593844` —— 新增 `Ship09A2ShotProbe.cs`，其 arms-only 遮罩帧会把每个屏幕空间 `UIDocument` 关掉再打开。
- `95474fa`（Phase 09A_2 收尾）只把渲染到 RenderTexture 的 document（控制台屏、腕表 UI）移出该列表，**屏幕空间 document 的开关仍然保留**，所以破坏依旧存在。
- Phase 09A_3 的 `Ship09A3ShotProbe` 沿用了同一模式，并且**丢掉了** `95474fa` 同时加入的 `WorldUiPointerRouter` 停靠（复现了旧注释里提到的 UI Toolkit 指针异常风险）。

## 3. 代码级证据链

1. `Assets/_Project/UI/UXML/*.uxml`、`Assets/_Project/Resources/*.uxml`：**没有任何 `<Style>` 引用**（`Select-String "<Style"` 为空）。
2. 所有样式都在代码里挂载：`AppController.cs:153-173`（theme + 四个 screen sheet）、`ShipHudController.cs:78`、`ShipPauseMenu.cs:162`、`ShipConsoleScreenHost.cs:159/172`、`ShipLoadoutPage.cs:235`、`ShipWarehouseScreenHost.cs:88`、`MainMenuController.cs:213/218` 等。
3. 屏幕可见性同样在代码里：`AppController.ShowScreen()` 用 `container.style.display = Flex/None`（`AppController.cs:380`），`ShipPauseMenu` 在初始化时 `_overlay.style.display = None`（`ShipPauseMenu.cs:186`），而 `AppController.Start()`、`ShipHudController.EnsureBound()` 都只跑一次（`_bound` 守卫）。
4. 因此 `enabled = false → true` 的等价效果是：**UXML 默认状态 + 零样式表**。UXML 中四个 screen 容器没有默认隐藏，PauseRoot 也没有默认隐藏。

## 4. 像素级证据（修复前 → 修复后）

同一台机器、同一构建流程、1920×1080：

| 帧 | 修复前 | 修复后 | 已知良好基线 |
| --- | --- | --- | --- |
| `01_fp_forward_view` | 82,91,102 / 2917 色（首个遮罩帧之前，正常） | 82,91,102 / 2917 色 | Phase 07B.23 ship 帧 115,124,132 / 3376 色 |
| `02_fp_look_down` | 171,172,174 / 18 桶（平灰） | 51,59,69 / 1266 色 | — |
| `06_wrist_hold_ui_above_watch` | 177,178,179 / 35 桶（平灰） | 97,106,114 / 3418 色 | 115,124,132 / 3376 色 |
| `08_console_focus` | 183,184,184 / 30 桶 | **140,144,149** | Phase 08 `05b_ship_terminal_overview` = **140,144,149**（完全相同） |
| `10_pause_menu` | 179,180,181，其中 **85.86% 为同一纯色 rgb(188,188,188)** | 48,54,60 / 989 色 / 8..255 | Phase 08 `04e_pause_menu` 78,84,90 / 1296 色 |

修复后的暂停帧与 Phase 08 已知良好暂停帧同属“暗色、有结构”的画面；控制台页帧的统计值与 Phase 08 基线**逐位相同**。

## 5. 修复内容（只动探针与测试工具）

| 文件 | 改动 |
| --- | --- |
| `Assets/_Project/Scripts/Core/Ship09A3ShotProbe.cs` | 遮罩测量不再触碰 `UIDocument.enabled`：改为保存每个屏幕空间根元素的 `style.visibility` 后设为 `Hidden`，测量结束**原值还原**；恢复 `WorldUiPointerRouter` 与 `ShipGameController` 的临时停靠；每帧新增「测量未改动 UI」与「UI 完整性」两条断言 |
| `Assets/_Project/Scripts/Core/UiIntegrityAudit.cs` | 新增（只读）：`Snapshot()` 记录 hub 屏数 / 各 document 的 enabled / 样式表数量 / display；`Problems()` 断言三条契约 —— hub 同时可见屏 ≤ 1、屏幕空间 document 必须带样式表、暂停遮罩可见性必须与 `PauseMenu.IsOpen` 一致 |
| `Assets/_Project/Scripts/Core/StandaloneSmoke.cs` | 进入船内与暂停时各加一条 UI 完整性断言 + `INFO` 状态转储；新增可选 `-smokeShots <folder>`（缺省时完全惰性）用于采集正常流程的 1920×1080 证据帧 |
| `Assets/_Project/Scripts/Editor/SurviveProjectValidator.cs` | 新增 `ValidateEvidenceProbeHygiene`：扫描 Core/Gameplay 下的 Probe/Smoke/Capture 源文件，**禁止任何探针写 `document.enabled = false`** |

产品运行时代码（`AppController` / `ShipOverlayUi` / `ShipPauseMenu` / `SurfaceWristController` / USS / UXML / PanelSettings）**未做任何修改**。

## 6. 验收证据

| 项目 | 结果 |
| --- | --- |
| 自动化（`-ship09A3Shot … -stage 5`） | `export/phase09a3_shots/phase09a3_result.txt` —— **70 checks / 0 failures**，含每帧 `the arms measurement leaves the UI untouched`、`the product UI is intact` |
| 自动化的 UI 状态转储 | `INFO :: ship interior UI state: … ShipPauseMenu on sheets=1 … ShipHUD on sheets=1 …`（样式表在位，无 0 样式表面板） |
| 正常流程（standalone smoke，真实输入路径） | `export/phase09a3_smoke.txt` —— **162 checks / 0 failures / 0 exceptions**；`the ship interior UI is intact`、`the pause overlays the current context only` 均 PASS |
| 正常流程视觉证据 | `export/phase09a3_smoke_shots/smoke_ship_interior.png`（151,161,170 / 3136 色）、`smoke_pause_gameplay.png`（46,52,58 / 976 色） |
| 结构 validator | `SurviveProjectValidator.ValidateEvidenceProbeHygiene` 两项检查（探针数量 + 无 `document.enabled` 写入） |
| 偶发记录 | `logs/phase09a3_smoke.log` 出现过 1 次 `NullReferenceException`，随后该次运行所有输入驱动检查级联 FAIL；紧接的复跑（`phase09a3_smoke2.log`）与带符号 Development 复跑均无异常，未复现，详见 §7 |

## 7. 已知未复现项

`logs/phase09a3_smoke.log`（修复后第一次 smoke 运行）出现 1 次 `NullReferenceException`，堆栈落在 `StandaloneSmoke+<RunProbe>d__10.MoveNext`，同一时刻起**所有输入驱动检查**（Walk、Wrist、控制台进入、暂停）级联 FAIL，而状态类检查正常；随后流程在收尾步骤停滞，由 300s watchdog 以 exit 2 结束（`export/phase09a3_smoke.txt` 那次为 77 PASS / 24 FAIL）。

判定：这是自动化运行期的一次性输入/协程异常，**未能在随后 3 次运行中复现**（`phase09a3_smoke2.log` 162/0、Development 复跑 2 次），且与本次 UI 回归的成因（样式/display 丢失）无关。已保留原始日志作为证据，后续若再次出现，Development player 的带符号堆栈可直接定位到行号。
