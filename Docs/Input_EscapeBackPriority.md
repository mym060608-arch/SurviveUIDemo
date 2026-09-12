# 输入契约修复 — Escape = Back-or-Pause-fallback

本文件记录「Keyboard Esc 不能无条件等于 Pause」这一全局输入优先级问题的契约、实现与验证证据。范围仅限输入派发；未改动 Block Arms、UI 美术、经济系统或 Phase 08 cinematic。

## 1. 正确契约

```text
Pause Menu open          + Esc -> 关闭 Pause / 继续游戏（不触碰下层）
Active Device Panel      + Esc -> 只关闭该面板，Pause 保持关闭
Wrist UI open            + Esc -> 只关闭腕表终端，Pause 保持关闭
Console / Terminal Focus + Esc -> 退出控制台视图，恢复自由第一人称，Pause 保持关闭
Free First Person        + Esc -> 打开 Pause
```

```text
Back priority
    Pause Menu > Active Modal / Device Panel > Wrist UI > Console Focus
    > Free First Person
```

原则：**先由当前最高优先级的局部状态消费 Back；只有没有任何局部 Back 可执行时，Esc 才映射为 Pause。**
一次 Esc 只 dispatch 一个动作。

Gamepad 不变：

- `Menu/Start` = 全局 Pause（任何层都能在其上方打开，下层保持冻结，继续游戏回到那一层）
- `buttonEast` = 当前拥有 Back 的层的**局部 Back**

## 2. 修复的具体缺陷

修复前：`UiInput.PausePressed(UiLayer)` 认为「Gameplay 层的 Escape 一律是 Pause」，而 ship 侧只有 device panel 会把自己声明成 `Modal`。于是 **Console Focus + Esc → 打开了 Pause**（控制台视图被暂停覆盖，玩家必须先继续游戏再按 B 才能退出），Wrist UI + Esc 同样会打开 Pause（腕表代码刻意排除了 Escape）。

同时，旧实现的「谁先更新谁消费」依赖 MonoBehaviour 更新顺序：`ShipGameController`/pause 的 `Update` 若早于控制台/腕表层，就会先把 Escape 读成 Pause。

## 3. 实现（复用 UiInput / ConsumeBackForFrame / UiLayer）

`Assets/_Project/Scripts/Core/UiInput.cs`：

- `UiLayer` 扩展为优先级枚举：`PauseMenu(0) > Modal(1) > Wrist(2) > ConsoleFocus(3) > Gameplay(4)`；`Gameplay` 表示「没有任何局部层可回退」。
- 新增**状态看板**（不依赖 Update 顺序）：

  ```csharp
  UiInput.SetLayerActive(UiLayer layer, bool active);   // 各层发布自己的权威状态
  UiInput.ActiveBackLayer;                              // 最高优先级的可回退层
  UiInput.IsTopBackLayer(UiLayer layer);                // 我是不是当前拥有 Back 的层
  UiInput.BackPressedForLayer(UiLayer layer);           // 本层本帧是否该处理这次 Back
  UiInput.ResetLayersForAutomation();                   // 测试隔离
  ```

- `PausePressed(UiLayer)`：Menu/Start 永远为真；Escape 仅在 **无局部层 + 本帧未被消费** 时为真。

各层接线：

| 层 | 发布者 | 状态 | Back 行为 |
| --- | --- | --- | --- |
| PauseMenu | `ShipPauseMenu.Open/Close` + `Update` + `OnDisable` | `_open` | `BackPressedForLayer(PauseMenu)` → `ResumeGame()` + `ConsumeBackForFrame()` |
| Modal | `ShipOverlayUi.SetMode(...)`（唯一的 mode 写入点）+ `Update` + `OnDisable` | `_mode != None` | `BackPressedForLayer(Modal)` → `HandleBack()`（内部已消费） |
| Wrist | `ShipGameController.SetWristOpen` + `PollWristKeys` + `OnDisable`；surface 侧同构 | `_isWristOpen` | `BackPressedForLayer(Wrist)` → `SetWristOpen(false)` + 消费 |
| ConsoleFocus | `ShipConsoleFocusController.PublishBackLayer()`（状态变更点 + `Update` + `OnDisable`/`CleanupForSceneChange`） | `FocusActive` | `BackPressedForLayer(ConsoleFocus)` → `RequestExit()` + 消费；进入过渡（TransitionIn）中按 Esc 亦可取消 |

要点：

- 发布点同时放在**状态变更处**与每帧的 `Update`：状态一变，看板同帧精确（不会有一帧延迟把 Escape 吞掉或错判）。
- 局部层一律用 `BackPressedForLayer`，所以 Pause 打开在 panel / wrist / console 之上时，下层不会重复响应同一次按键。
- 控制台视图的 `E` 键退出保留为**历史绑定**，但同样被「本层是否拥有 Back」门控（`IsTopBackLayer`），因此不会在 Pause 覆盖时把下层控制台偷偷关掉。
- `SurfaceGameController` 的腕表 Esc 也改走同一机制（不再直接读 `keyboard.escapeKey`）。

## 4. 验证证据

| 项目 | 结果 |
| --- | --- |
| PlayMode 全量 | `export/phase_escfix_playmode.xml` — **152 / 152 passed**（原 144 + 新增 `SurviveEscPriorityTests` 8 项） |
| 新增测试 | 自由第一人称 Esc→暂停→Esc→继续；装备面板 Esc 只关闭；出征面板 Esc 只关闭；腕表 Esc 只关闭；控制台 Esc 退出且**全程未打开 Pause**、相机/输入恢复；控制台 + Menu/Start → Pause 且控制台保留为下层、继续游戏回到控制台、随后 Esc 才退出；Pause 覆盖面板时 Esc 只恢复 Pause（面板 BackActionCount 不变），下一次 Esc 才关面板；Gamepad B 遵循同一优先级 |
| Standalone smoke（真实输入路径） | `export/phase_escfix_smoke.txt` — **196 checks / 0 failures / 0 exceptions**，含 31 条 Escape 相关断言（腕表、控制台、自由第一人称、两块 Airlock 面板） |
| Phase 09A_3 探针 | `export/phase09a3_shots/phase09a3_result.txt` — **70 / 0**（未回归） |
| 结构 validator | `export/phase_escfix_validator.txt` — **420 / 0** |
| 文档一致性 | `Docs/UI_LANGUAGE_STYLE.md` §64.2 / §64.3 / §65.2 已同步为新契约 |

行为矩阵（自动化逐条断言）：

```text
Pause + Esc                  -> 继续游戏，面板/腕表/控制台不受影响
Device Panel + Esc           -> 面板关闭，1 次 Back action，无 Pause，timeScale=1
Wrist + Esc                  -> 终端关闭并释放，无 Pause，输入恢复
Console Focus + Esc          -> 退出视图（相机回到探索位姿），无 Pause，输入恢复
Free First Person + Esc      -> 打开 Pause（timeScale=0）
Console Focus + Menu/Start   -> Pause 打开且控制台保留为下层；继续游戏回到控制台
Pause(在面板/控制台上方) + Esc -> 只恢复 Pause；下一次 Esc 才关下层
```

## 5. 未改动

Block Arms 表现、UI 美术与 USS/UXML、经济与 GameSession、Phase 08 cinematic、`main` 分支、`phase-08-green` 标签，均未触碰。本提交独立于 Phase 09A_3 的收尾提交（`8e8bbbc`）。
