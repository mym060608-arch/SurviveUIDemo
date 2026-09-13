# Survive UI Demo — UI Language & Visual Style Guide
## 中文主界面语言规范 / Chinese-First Interface Standard

> Project: SurviveUIDemo  
> Status: ACTIVE  
> Scope: All player-facing UI  
> Established: Phase 04  
> Purpose: Maintain one consistent language, typography, terminology, and information-design system across all game interfaces.

---

# 0. Purpose

本文件是 `SurviveUIDemo` 的长期 UI 语言与视觉表达规范。

它负责统一：

- 中文 / 英文的使用边界
- 固定术语
- 标题和按钮写法
- 数值与单位格式
- 中英文混排
- 中文字体策略
- 字号与字距原则
- 状态文案
- UI 信息层级
- 后续界面的语言一致性
- 人工素材与程序化占位的边界

本文件不是某一个 Phase 的临时任务说明。

除非用户明确修改规则，否则后续所有：

```text
Overview
Preparation
Processing
Outpost
Gameplay HUD
Mission UI
System Menu
Final Polish
```

均应遵循本文件。

---

# 1. Core Language Strategy

项目正式采用：

```text
中文 = 主要信息与交互语言
英文 = 品牌、编号、单位、少量技术辅助信息
```

核心原则：

> 玩家必须理解的信息，应优先使用中文。  
> 英文负责强化世界观、工业感、系统标识，而不是承担主要阅读任务。

---

# 2. Language Priority

## 中文优先

以下内容原则上必须使用中文：

- 页面名称
- 区域标题
- 当前任务
- 任务说明
- 玩家操作按钮
- 装备名称
- 装备说明
- 资源名称
- 环境信息
- 系统状态
- 准备状态
- 加工状态
- 错误状态
- 原料不足提示
- 升级提示
- 玩家需要做决策的信息

---

## 英文可以保留

以下内容可保持英文：

### 品牌

```text
SURVIVE // DRIFT
```

### 世界观编号

```text
NEREID-04
ARK-07
UNIT-01
MK-I
```

### 单位

```text
KG
AU
G
%
LV.
```

### 少量辅助技术标签

例如：

```text
SHIP STATUS
CURRENT DESTINATION
CURRENT OBJECTIVE
MISSION DATA
RESOURCE PROCESSING
REFINERY
SYSTEM ONLINE
```

这些英文只能作为 secondary / metadata。

不得压过中文主标题。

---

# 3. Do Not Fully Duplicate Every Label

禁止机械地把每一项都写成：

```text
能源 ENERGY
食物 FOOD
矿物 MINERALS
燃料 FUEL
合金 ALLOY
```

这样会产生不必要的信息噪音。

推荐：

```text
舰船状态
SHIP STATUS

能源
食物
矿物
燃料
合金
```

也就是说：

```text
中文主标题
+
少量英文辅助标题
+
中文具体信息
```

---

# 4. Navigation Terminology

以下术语固定，不应在后续页面自行换词。

```text
01  总览
02  出发准备
03  资源加工
04  据点
```

Internal enum / code may remain:

```text
Overview
Preparation
Processing
Outpost
```

但玩家界面统一使用中文。

---

# 5. Global Brand Terminology

Game brand:

```text
SURVIVE // DRIFT
```

暂不翻译品牌名。

Supporting brand line:

```text
远征控制
EXPEDITION CONTROL
```

Ship:

```text
ARK-07
```

Destination:

```text
NEREID-04
```

---

# 6. Resource Terminology

Canonical terms:

| Internal | 中文 UI |
|---|---|
| Energy | 能源 |
| Food | 食物 |
| Minerals | 矿物 |
| Fuel | 燃料 |
| Alloy | 合金 |
| Cargo | 载荷 |
| Raw Materials | 原料 |
| Inventory | 库存 |
| Output | 产出 |
| Expected Output | 预计产出 |

不得混用：

```text
矿石 / 矿物 / 原矿
```

如果指 GameSession 中当前 `Minerals` 资源：

统一：

```text
矿物
```

如果后续出现真正不同类型的 raw ore，才另外定义。

---

# 7. Ship Status Terminology

Canonical:

```text
舰船状态
SHIP STATUS
```

Subgroup:

```text
生存指标
```

Resource subgroup:

```text
资源库存
```

Values:

```text
能源
食物
矿物
燃料
合金
```

Status:

```text
系统正常
系统警告
临界状态
```

当前 Overview 可继续使用少量英文：

```text
SYSTEM ONLINE
```

---

# 8. Mission Terminology

Canonical:

```text
当前任务
CURRENT OBJECTIVE

当前目的地
CURRENT DESTINATION

任务
任务数据
任务类型
主要目标
任务状态
任务准备度
```

Suggested statuses:

```text
进行中
待处理
已完成
已锁定
已获取
未知
稳定
```

Do not mix:

```text
ACTIVE
READY
PENDING
```

as primary player-facing state when a Chinese equivalent exists.

English may remain as tiny supporting metadata only.

---

# 9. Overview Canonical Copy

> Phase 09B PASS 7 (2026-09-13) rewrote this page against the user's confirmed
> reference and sketch: the decorative system strip (`SURVIVE // DRIFT` /
> `SHIP // ARK-07` / `CYCLE` / `SYSTEM ONLINE`) is gone, the planet display name
> lives in ONE place (`PlanetIdentity.DisplayName`), and the page key line lists
> only the keys that really work here. The copy below is the current contract.

Page title (top-left, the only text in the strip):

```text
星球总览
```

Left column:

```text
舰船状态

生存指标

能源
食物

资源库存

矿物
燃料
合金

舰船系统 正常
```

Center:

```text
当前目的地

AURELIA-01

未知地表 · 资源候选区

类型    冰原行星
地表    寒带 / 山脉

探索度
73%
```

Mission:

```text
当前目标

活下去

寻找可利用资源，并维持舰船能源
高于临界阈值。
```

Objective list:

```text
01  勘测 AURELIA-01      进行中
02  获取原始资源          未完成
03  恢复燃料储备          未完成
```

Mission status:

```text
任务状态

阶段    已锁定
准备度  72%
```

Primary CTA (legacy Hub host only; the ship terminal hides it):

```text
准备远征 »
```

Controller key line (bottom of the page, device-aware):

```text
gamepad   [RS] 旋转星球   [B] 返回
keyboard  [鼠标] 旋转星球 [Esc] 返回
```

The Hub host is the root of the four tabs and has no step back, so it prints the
rotation row only - a key line never advertises a key that does nothing. The
ship's physical Overview terminal prints both rows, because B really leaves that
console view there.

Planet display name: `AURELIA-01` is the CURRENT expedition target. It is
presentation copy with a single source
(`Assets/_Project/Scripts/UI/PlanetIdentity.cs`); other screens (Preparation,
the airlock prompt, the wrist terminal, the surface HUD) keep their own authored
copy and their internal NEREID IDs until a later pass renames them together.

---

# 10. Preparation Terminology

Canonical:

```text
出发准备
MISSION PREPARATION
```

Mission data:

```text
任务
目标类型
距离
任务类型
信号
```

Values:

```text
未知地表
资源勘测
稳定
```

---

# 11. Environment Terminology

Canonical:

```text
环境数据
ENVIRONMENT
```

Fields:

| Internal | 中文 |
|---|---|
| Temperature | 温度 |
| Gravity | 重力 |
| Radiation | 辐射 |
| Atmosphere | 大气 |
| Surface | 地表 |
| Visibility | 能见度 |

Suggested values:

```text
低温
稳定
中等
注意
稀薄
未知
较低
```

Avoid awkward machine translations such as:

```text
辐射：MEDIUM
大气：THIN
```

Primary display should be Chinese.

---

# 12. Equipment Terminology

Canonical section:

```text
装备配置
EQUIPMENT // LOADOUT
```

Equipment names:

```text
采集器 MK-I
备用电池组
勘测无人机
```

Descriptions:

### Harvester

```text
可部署矿物采集模块
```

### Battery

```text
为远程设备提供辅助能源
```

### Survey Drone

```text
近距离地形与资源扫描
```

States:

```text
必需
已选择
未选择
不可用
```

Avoid using English primary chips:

```text
REQUIRED
SELECTED
```

once Chinese migration is complete.

---

# 13. Cargo Terminology

Section:

```text
载荷
CARGO
```

Example:

```text
90 / 100 KG
```

Do not translate `KG`.

Recommended secondary states:

```text
容量充足
接近上限
超出容量
```

Do not add these states unless actually needed.

---

# 14. Readiness Terminology

Canonical:

```text
任务准备度
MISSION READINESS
```

Primary states:

```text
未就绪
就绪
已确认
```

Rows:

```text
目标数据
采集器
能源模块
勘测系统
载荷
```

Values:

```text
就绪
可选
未就绪
90 / 100
```

---

# 15. Confirmation Terminology

Preparation CTA:

Before confirmation:

```text
确认配置
```

Confirmed:

```text
配置已确认
```

Future gameplay phase may introduce:

```text
开始远征
```

Do not use `开始远征` until a real expedition transition exists.

---

# 16. Processing Terminology

Screen:

```text
资源加工
RESOURCE PROCESSING
```

Core section terms:

```text
原料库存
加工批次
精炼流程
精炼模块
预计产出
当前库存
加工进度
加工状态
```

Resource names:

```text
矿物
燃料
合金
```

Actions:

```text
开始加工
再次加工
```

States:

```text
待机
准备就绪
加工中
加工完成
原料不足
```

---

# 17. Processing Flow Copy

Recommended visual flow:

```text
矿物
  │
  ▼
精炼模块
  ├────────► 燃料
  └────────► 合金
```

Batch:

```text
加工批次

05
10
15

单位：矿物
```

Expected output example:

```text
预计产出

燃料 +4
合金 +2
```

Progress:

```text
加工进度
68%
```

Complete:

```text
加工完成
```

---

# 18. Outpost Terminology

Canonical:

```text
据点
OUTPOST
```

Future production UI terminology:

```text
据点状态
核心模块
采集模块
精炼模块
能源系统
升级
升级需求
当前等级
运行效率
```

Levels:

```text
LV.1
LV.2
LV.3
```

Do not translate `LV.`.

Current Phase 04 placeholder may use only:

```text
据点
升级精炼模块
```

Full redesign belongs to later Phase.

---

# 19. Status Vocabulary

Use consistently.

## Positive

```text
正常
稳定
就绪
已确认
已完成
运行中
```

## Neutral

```text
未知
待处理
可选
未选择
```

## Negative / blocked

```text
未就绪
原料不足
容量不足
不可用
```

Avoid unnecessary dramatic wording.

This game should feel technical and controlled.

---

# 20. Button Writing Rules

Buttons should normally use:

```text
verb + object
```

Examples:

```text
准备远征
确认配置
开始加工
升级精炼模块
重置状态
```

Avoid:

```text
确定
确认
继续
下一步
```

when a more specific action is available.

---

# 21. Button Punctuation

Primary action may use:

```text
»
```

or:

```text
→
```

sparingly.

Example:

```text
准备远征 »
```

Do not append arrows to every button.

---

# 22. Chinese Punctuation

Body copy uses standard Chinese punctuation:

```text
，。；：
```

Do not mix English commas/periods inside Chinese sentences unless required by code identifiers or units.

Example correct:

```text
寻找可利用资源，并维持舰船能源高于临界阈值。
```

---

# 23. Technical Separators

Technical / brand labels may retain:

```text
//
·
—
```

Examples:

```text
SURVIVE // DRIFT
EQUIPMENT // LOADOUT
未知地表 · 资源候选区
```

Do not overuse `//` in ordinary Chinese descriptions.

---

# 24. Number Formatting

Use Arabic numerals:

```text
01
02
03
04

74%
51%

2.47 AU
0.72 G
-37°C
90 / 100 KG
```

Do not write:

```text
百分之七十四
九十千克
```

inside technical UI.

---

# 25. Temperature Formatting

Preferred:

```text
-37°C
```

not:

```text
-37 C
```

when font support permits the degree symbol.

If degree glyph causes fallback instability, temporary:

```text
-37 °C
```

is acceptable.

---

# 26. Unit Formatting

Preferred:

```text
2.47 AU
0.72 G
90 / 100 KG
74%
```

Rules:

- value and alphabetic unit use one space;
- `%` directly follows number;
- `LV.` directly precedes number;
- do not translate technical units.

---

# 27. Typography Principle

Current design language:

```text
Swiss Grid
+
Industrial Aerospace
+
Minimal Information Design
```

Chinese must inherit this.

Hierarchy is created through:

```text
font size
font weight
whitespace
alignment
hairline rules
contrast
```

NOT excessive letter spacing.

---

# 28. Chinese Letter Spacing

English labels may use moderate tracking.

Chinese:

```text
letter-spacing ≈ 0
```

or only a very small positive value.

Do not use large tracking such as:

```text
舰 船 状 态
资 源 加 工
```

unless explicitly designed as a decorative headline.

---

# 29. Chinese Font Weight

Use restrained hierarchy:

```text
Hero / main state:
medium / semibold

Section headings:
medium

Body:
regular

Metadata:
regular
```

Avoid excessive bold Chinese throughout the interface.

---

# 30. Recommended Size Hierarchy

At 1920×1080, approximate:

```text
Planet / destination hero        48–72 px
Major Chinese screen title       32–48 px
Major state / numeric data       28–40 px
Equipment / module title         17–22 px
Section title                    14–18 px
Body copy                        13–16 px
Metadata                         10–13 px
```

These are guidelines, not absolute pixel contracts.

---

# 31. Minimum Readability

At:

```text
1280×720
```

player-facing Chinese text must remain readable.

Do not reduce critical text below practical readability simply to preserve layout.

Prefer:

```text
reflow
spacing reduction
column compression
```

before aggressive font shrinking.

---

# 32. Temporary Chinese Font Strategy

During development:

Use a legally available CJK-capable font from the local system or another explicitly permitted source.

Acceptable examples when installed/licensed appropriately:

```text
Microsoft YaHei UI
Microsoft YaHei
Noto Sans CJK SC
Source Han Sans SC
```

Important:

- Do NOT copy operating-system font files into the repository.
- Do NOT redistribute system font files.
- Do NOT download fonts from unofficial mirrors.
- Do NOT add a font to the project unless its license is known and compatible.

Current development font solution may remain temporary.

---

# 33. Font Validation Requirements

Any temporary or final CJK font setup must be verified in:

```text
Unity Editor
PlayMode
Standalone
Editor clean screenshot capture
```

Check:

```text
no □ glyph
no missing Chinese
no broken punctuation
no unexpected fallback
no inconsistent line height
```

---

# 34. Future Final Font

Final submission may later use a properly licensed embedded CJK font.

This will be handled during final asset/polish phase.

Until then:

```text
font choice must not block code or UI production
```

---

# 35. Visual System — Background

Inherited from Phase 02.

Primary canvas:

```text
off-white / light cool neutral
```

Current reference:

```text
#E9EBED
```

Do not switch to dark sci-fi UI without explicit user approval.

> **Approved exception (Phase 08 Main Menu Revision):** the user explicitly
> required the Main Menu cover to be a deep-space shot (left hero ship / right
> menu / star field), so `04_MainMenu` uses a dark canvas. See §63. Every other
> screen keeps this light canvas.

---

# 36. Visual System — Text

Reference hierarchy:

```text
Primary ink:
#161B20

Secondary:
#4A525C

Functional metadata:
approximately #5A6570

Decorative metadata:
lighter neutral gray
```

Exact values may follow existing USS tokens.

Do not create page-specific arbitrary grays.

---

# 37. Visual System — Accent

Only one primary accent family:

```text
teal / cyan
```

Current reference:

```text
#0E7F8C
```

Use for:

- active navigation
- selected state
- readiness markers
- progress
- important system markers
- hover emphasis

Do NOT use accent on every label.

---

# 38. Warning Colors

Do not automatically introduce:

```text
red
orange
yellow
```

for every risk state.

The visual system currently prefers:

```text
text
contrast
status word
accent
```

If a future safety-critical state genuinely requires another color, define it deliberately in this guide first.

---

# 39. Hairline System

Main dividers:

```text
1 px
```

Use:

- vertical separators
- section rules
- progress bases
- selected-state rules

Avoid heavy boxes.

---

# 40. Card Policy

Do not convert the project into a card-based dashboard.

Prefer:

```text
open whitespace
hairline division
grid
aligned data
```

Cards are permitted only where containment is semantically useful, such as equipment rows.

---

# 41. Corner Radius

Keep restrained:

```text
0–6 px
```

Avoid large consumer/mobile app rounded rectangles.

---

# 42. Navigation Style

Always retain:

```text
index + Chinese label
```

Example:

```text
01 总览
02 出发准备
03 资源加工
04 据点
```

Active state:

```text
light panel
+
teal top rule
+
accent index
```

or equivalent existing Phase 02 style.

Do not redesign navigation per screen.

---

# 43. Top Bar Style

Keep global top bar stable.

Recommended:

```text
SURVIVE // DRIFT

远征控制
EXPEDITION CONTROL
```

Right side:

```text
SHIP // ARK-07
CYCLE 1842
SYSTEM ONLINE
```

It is acceptable for this global system metadata to remain mainly English.

---

# 44. Writing Tone

Game UI tone:

```text
冷静
精确
简短
工程化
非戏剧化
```

Avoid:

```text
太棒了！
恭喜！
警告！！！
快去收集资源！
```

Prefer:

```text
原料不足
任务已确认
加工完成
目标已锁定
```

---

# 45. Sentence Length

UI body copy should generally stay within:

```text
1–2 short lines
```

Avoid paragraphs.

Example:

Good:

```text
寻找可利用资源，并维持舰船能源
高于临界阈值。
```

Bad:

```text
为了确保本次远征的顺利完成，玩家需要寻找足够的资源，同时还需要注意……
```

---

# 46. Metadata Density

Technical metadata should be sparse and meaningful.

Avoid fake decorative numbers such as:

```text
SYS 484.22
NODE 4498
AXIS 82.201
```

unless they have a real world-building or system purpose.

---

# 47. English Capitalization

English metadata:

```text
UPPERCASE
```

is generally preferred.

Examples:

```text
SHIP STATUS
CURRENT OBJECTIVE
RESOURCE PROCESSING
SYSTEM ONLINE
```

Chinese obviously has no capitalization rule.

---

# 48. Screen Language Hierarchy

Recommended structure:

```text
中文主标题
ENGLISH SECONDARY LABEL

中文主要信息

technical values / identifiers
```

Example:

```text
当前目的地
CURRENT DESTINATION

NEREID-04

未知地表 · 资源候选区
```

---

# 49. UXML / Code Naming

Code stays English.

Examples:

```text
OverviewScreen
PreparationScreen
ProcessingScreen
OutpostScreen
GameSession
ProcessingState
CargoWeight
LoadoutConfirmed
```

Do NOT rename runtime C# APIs into Chinese identifiers.

Only player-facing copy changes.

---

# 50. Comments and Internal Logs

Internal:

```text
English preferred
```

Human-facing final DSH reports:

```text
Chinese
```

Debug UI may remain English.

---

# 51. Debug UI

Developer-only controls are exempt from Chinese-first requirement.

Examples may remain:

```text
RESET SESSION
+10 MINERALS
+10 ALLOY
```

Because Debug UI:

```text
hidden by default
not part of submission
```

---

# 52. Screenshot Standard

Competition-facing screenshots must use:

```text
Chinese-first interface
Debug hidden
No Development Build watermark
No trialVersion watermark
No Editor chrome
No TODO / placeholder development copy
```

English brand and technical metadata are allowed.

---

# 53. Responsive Chinese Layout

At:

```text
1920×1080
1600×900
1280×720
```

verify:

- Chinese title not clipped
- Chinese body wraps naturally
- units stay attached to values where possible
- navigation labels remain readable
- mixed Chinese/English line-height remains stable
- no fallback glyphs

---

# 54. Manual Asset Policy

Current production priority:

```text
code
state
interaction
scene
layout
validation
```

Do not stop development to request manual art.

Use:

```text
text
UI Toolkit geometry
procedural placeholders
hairlines
simple shapes
```

until unified asset production phase.

---

# 55. Future Manual Asset Categories

Accumulate, do not request yet:

```text
正式中文字库
Logo / Wordmark
NEREID-04 行星表面
资源图标
装备 pictogram
任务图标
环境风险图形
精炼模块图形
据点模块图形
飞船徽记
装饰技术标识
```

A complete asset sheet will be created later.

---

# 56. AI Asset Constraint

Do not use generative-AI visual assets in final submission.

This includes:

```text
AI-generated icons
AI-generated textures
AI-generated backgrounds
AI-generated planet art
AI-generated logos
```

Temporary native/procedural UI is preferred.

---

# 57. Terminology Change Procedure

If a later Phase needs to change an established Chinese term:

1. identify the reason;
2. update this guide;
3. update all affected player-facing screens;
4. run language regression checks.

Do not silently introduce synonyms.

Example:

Do not mix:

```text
出发准备
任务准备
远征准备
```

across different screens unless each has a clearly different meaning.

---

# 58. Stable Terms — Quick Reference

```text
总览
出发准备
资源加工
据点

舰船状态
生存指标
资源库存

能源
食物
矿物
燃料
合金

当前目的地
当前任务

环境数据
温度
重力
辐射
大气
地表

装备配置
采集器 MK-I
备用电池组
勘测无人机
载荷

任务准备度
未就绪
就绪
已确认

原料库存
加工批次
精炼模块
预计产出
当前库存
加工进度
加工中
加工完成
原料不足

升级精炼模块
```

---

# 59. Stable English Technical Terms

```text
SURVIVE // DRIFT
EXPEDITION CONTROL
SHIP // ARK-07
NEREID-04
MK-I
UNIT-01
SHIP STATUS
CURRENT DESTINATION
CURRENT OBJECTIVE
ENVIRONMENT
EQUIPMENT // LOADOUT
RESOURCE PROCESSING
REFINERY
SYSTEM ONLINE
KG
AU
G
LV.
%
```

---

# 60. Definition of Success

The language system is successful when:

```text
中国用户不依赖英文即可理解玩法
+
英文仍然保留世界观与工业技术质感
+
界面没有中英重复堆砌
+
四个核心页面术语完全统一
+
中文排版不破坏 Phase 02 建立的视觉质量
```

最终目标不是：

```text
“把英文全部翻译成中文”
```

而是：

```text
建立一套真正为中文玩家设计的航天工业信息界面。
```

---

# 61. Current Implementation Record (Phase 04)

Phase 04 实际采用的字体现状：

```text
方案：运行时引用系统 CJK 字体（不复制、不分发字体文件）
位置：AppController.ApplyUiCjkFont()
机制：Font.CreateDynamicFontFromOSFont(候选列表)
候选：Microsoft YaHei UI / Microsoft YaHei / SimHei
      / Noto Sans CJK SC / Source Han Sans SC
```

- 该方案仅在本机 Editor / PlayMode / Standalone / Editor 干净截图流程中有效；
- 未向仓库复制任何字体文件，未引入授权不明的字体资产；
- 本机验证链路：微软雅黑（Regular/Bold）与黑体均安装可用。

正式提交版如需跨机器分发：

```text
必须使用授权清晰、可嵌入的 CJK 字体（如思源黑体 / Noto Sans SC，
OFL 许可），在最终资产阶段统一嵌入。
```

中文缺字 / 方框风险会在“字体验证检查”中以人工视觉审核为准。

此记录将随后续 Phase 保持更新，直到正式字体落地。

---

# 62. Wrist Terminal Terms (Phase 06 Revision)
Field/personal terminal canonical terms. Chinese remains the primary player
language; English stays as secondary metadata only.

```text
腕部终端
WRIST TERMINAL

地表地图
LOCAL MAP

健康度
饱食度
当前装备
携带状态
载荷
当前导航
导航目标
资源节点
目标已到达
未设置
采集中
无线传输
传输完成
采集器 MK-I
备用电池组
勘测无人机
已装备
已选择
未选择
0 = 极度饥饿 · 100 = 完全饱腹
```

Semantics notes:

- 饱食度 (PlayerSatiety) higher = better: 0 = 极度饥饿, 100 = 完全饱腹;
  the default is a normal high value (82). Never display satiety as hunger
  (lower-is-worse) on the wrist.
- Normal exploration HUD shows only: crosshair, interaction prompt,
  navigation readout, temporary harvest/transfer feedback, return action.
- Wrist shows: map, personal status, equipment, cargo, mission/node count.
- 腕部终端 must never be presented as a miniature of the four ship screens.

---

# 63. Title Screen Terms (Phase 08, Revision 2)

Main Menu (`04_MainMenu`) is the product's console-style TITLE SCREEN and the
only human entry into the demo. Revision 2 reduced it to the minimum:

```text
活下去              ← the only headline

新游戏              ← focus starts here; resets the authoritative session
继续游戏            ← disabled until a run has started in this process
选项                ← opens the options modal
退出
```

Product flow (binding):

```text
Boot → Title Screen（深空 + 左侧飞船 + 右侧菜单）
     → 新游戏 → 菜单淡出 / 推进器增强 / 飞船从左向右加速离场 / 黑场
     → 飞船内部（上下黑色眼帘：小幅 → 更大幅 → 完全睁开）
     → 第一人称可控 → 走到终端 → 交互 → 总览 / Overview
     → 继续游戏：不 Reset session，短黑场 + 较短眼帘睁眼
```

Removed from the title screen in Revision 2 (kept nowhere else on this screen):
the top system bar, the footer, the hero telemetry strip, the mission/sector
codes, the annotation rails, the corner brackets, the wordmark block, the
primary CTA copy 进入任务 and the 制作信息 modal. The title screen must read as
"deep space + ship + four options", never as a control panel.

Canonical copy:

```text
活下去
新游戏
继续游戏
选项
退出

选项 modal:
选项
动态效果      开启 / 关闭
界面提示      显示 / 隐藏
返回
```

Controller (Phase 08 Revision 2 scope only — the full console UX is Phase 09):

```text
D-Pad ↑↓ / 左摇杆 ↑↓   → 切换 Focus（跳过 disabled 项）
A                      → 确认
B / ESC                → 关闭 选项（返回）
提示                   → 手柄操作时显示 "(A) 确认"，鼠标键盘时不显示
```

- Focus is the PRIMARY state: the focused row brightens, gains a `›` marker and
  a 1 px accent hairline. Hover feeds the focus instead of being a separate
  treatment.
- 继续游戏 is disabled (weaker colour, not focusable) until 新游戏 has started a
  run in the current process. It is a runtime-session continue only: no disk,
  JSON, slot, cloud or profile persistence exists or should be added.

Visual notes (binding for this screen; §35 exception approved for the cover):

- Deep-space canvas, the real 3D hero ship on the LEFT with its nose pointing
  screen RIGHT, the starfield behind it, and the menu column on the RIGHT.
- The ship cruises left to right and departs left to right; both thruster plumes
  stream backwards to the left. Screen-space direction is a hard requirement.
- The star plates are generated once; drift and twinkle are shader-side, so the
  sky never rebuilds a texture at runtime.
- Ship interior entry uses two black curtains (TopEyelid / BottomEyelid) that
  really change height: closed → small crack → closed → bigger crack → closed →
  fully open, then they are removed. Movement, look and interaction stay locked
  until the eyes are open.

---

# 64. Pause Menu & Console View (Phase 08 pause work)

The ship interior now has the minimum complete way back out of the game.

## 64.1 Pause menu copy

```text
暂停

继续游戏
返回主菜单
退出游戏

<device hint line>
```

Device hint (presentation only):

```text
gamepad   [LS / D-Pad] 选择    [A] 确认    [B] 返回
keyboard  [↑/↓] 选择          [Enter] 确认  [Esc] 返回
```

## 64.2 Input semantics

Logical actions and physical controls stay separated:

```text
Confirm   = Gamepad.buttonSouth  | E / Enter / Space
Interact  = Gamepad.buttonSouth  | E
Back      = Gamepad.buttonEast   | Escape
Pause     = Gamepad.startButton  | Escape
Navigate  = Gamepad.dpad / leftStick | arrow keys
```

`UiInput.BindingTable` is the authoritative table; `AButton` / `BButton` /
`StartButton` names must never appear as logical semantics. Escape is shared
between Pause and Back on purpose, and it is resolved as
**Back-or-Pause-fallback**: the highest-priority LOCAL layer that can go back
consumes it, and only when no such layer exists is Escape the global Pause key.

```text
Escape priority (UiInput.UiLayer, lowest value wins)
    PauseMenu     the pause is open              -> Escape resumes
    Modal         device panel / deployment      -> Escape closes it
    Wrist         the wrist terminal is open     -> Escape closes it
    ConsoleFocus  a console view is focused      -> Escape leaves the view
    Gameplay      free first person, nothing open -> Escape is Pause
```

Every layer publishes its own state through `UiInput.SetLayerActive`, and the
answer is read from that state (`UiInput.ActiveBackLayer`) - never from
MonoBehaviour update order. A layer takes a press only via
`UiInput.BackPressedForLayer(layer)` and consumes it with
`UiInput.ConsumeBackForFrame()`, so one Escape press can never dispatch two
actions. Menu/Start is unambiguous: it is always the GLOBAL pause, in every
layer.

## 64.3 Behaviour

```text
free first person (nothing open) + Escape, or any layer + Menu/Start
    -> 暂停 opens: Time.timeScale = 0, movement/look/world interaction/wrist
       and the world-console pointer frozen, cursor free, focus on 继续游戏
       (the layer underneath stays frozen and is what 继续游戏 returns to)
继续游戏 / B / Menu-Start again / Escape (while the pause is open)
    -> closes and restores the previous timeScale and the exact gameplay state
device panel + Escape / B
    -> closes that panel only; the pause never opens in the same press
wrist terminal + Escape / B
    -> closes the terminal only
console view + Escape / B
    -> leaves the view and restores free first person; the pause never opens
返回主菜单
    -> timeScale 1, pause and console view cleaned, SceneRouting to 04_MainMenu,
       GameSession untouched so 继续游戏 stays enabled and returns here
退出游戏 (confirm only)
    -> the ONE pause path that may terminate the process
```

While the pause is open the gameplay layer underneath never receives the same
input (one press, one action). `ShipGameController.SetPaused` is the single
freeze point; `ShipPauseMenu` is created at runtime and renders above the ship
HUD (1) and the shipboard modals (30/31).

## 64.4 Console view (view-only in this phase)

```text
自由第一人称 + 靠近控制台     [A] / [E] 查看 <终端名>终端
控制台注视状态                [B] / [E] 返回
```

- The camera moves smoothly (0.26 s, unscaled) from the player's exact pose to
  the terminal's authored `ConsoleFocusAnchor` and back; no teleport, no console
  transform change.
- Movement, look and other world interaction are disabled while viewing.
- Menu/Start still pauses during the view; 继续游戏 returns to the view, and
  返回主菜单 cleans the view before leaving.
- Console-internal navigation (D-Pad page selection, A on the terminal UI, B
  stepping back inside it, LB/RB tabs) is deliberately NOT implemented here -
  that is Phase 09. The prompt therefore only promises 查看 / 返回.

---

# 65. Airlock Device Panels (Phase 08 device-panel hotfix)

The two devices in the 气闸舱 are the SAME panel state as Phase 07B.2, now with a
complete controller loop. No second panel, no second modal manager and no second
input system exists:

```text
装备整理    EquipmentLockerInteractable -> ShipOverlayUi.OpenLoadoutFromLocker
            -> ShipLoadoutPage (the shared loadout view, GameSession authority)
出征前准备  ShipAirlockInteractable     -> ShipOverlayUi.OpenDeploymentConfirm
            -> ShipDeployConfirmPage    (no scene load until 确认降临)
```

## 65.1 Panel copy

```text
装备整理 (ShipLoadoutPage)     top-right: [B] 返回   +  返回 button
出征前准备 (ShipDeployConfirm) bottom:    [B] 返回   (device line)
```

The hint is device-aware (`InputDeviceTracker.FormatPrompt(UiAction.Back, "返回")`):
`[B] 返回` on a controller, `[Esc] 返回` on the keyboard. `[A] 确认` is NOT shown -
the panels have no focus navigation yet (Phase 09), so the copy must not promise
it.

## 65.2 Back contract

```text
open panel + B / Escape
    -> the panel closes, exactly ONE action, no quit, no title screen,
       no scene change, GameSession untouched
    -> movement, look, world interaction, the gaze prompt and the cursor are
       exactly what they were before the panel opened
出征前准备 --调整装备--> 装备整理 + B
    -> one step back to 出征前准备 (not straight out)
open panel + Menu/Start
    -> 暂停 opens ABOVE the panel: Time.timeScale = 0, the panel stays as the
       frozen underlying state
    -> 继续游戏 / B / Menu-Start returns to that same panel (it does not close it)
    -> 返回主菜单 cleans the panel and its UI focus before leaving the ship
```

Dispatch chain (one press = one action, the consumer consumes it):

```text
Pause Menu  >  Active Device Panel / Modal  >  Wrist Terminal  >  Console Focus
            >  Free First Person (Escape falls back to the global Pause)
```

The chain is resolved from published STATE (`UiInput.SetLayerActive` /
`UiInput.ActiveBackLayer`), not from script order, and every local layer takes
its press through `UiInput.BackPressedForLayer`, so a lower layer can never act
on a press that belongs to a higher one.

## 65.3 Implementation notes

- `ShipOverlayUi` is the single authority: `HandleBack()` is the layer's Back
  action and `BackActionCount` counts consumed presses (tests pin one action per
  press).
- The pause is checked FIRST: a panel may stay open underneath it, and Back there
  must only resume the game.
- Closing a panel releases any UI Toolkit focus it held (`ReleaseFocus`), so no
  hidden element can keep swallowing input.
- The gamepad confirm press that OPENS a panel is also turned into a synthetic
  pointer click by Unity's runtime UI; on the opening frame that click is the
  opening press, not a second action, so the panels drop it
  (`IsOpeningPressEcho`). This is the same defect class as the title screen's
  退出 activation.



