# agent-15：UI **1:1 复刻**轮（两个任务包，按 section 分派）

> **用户投诉原话**：「我要的 1:1 复刻……素材都是错了，**按钮样式地图什么都是乱七八糟的**」。
> 病根：之前只做到"能看/像"，没做**逐元素对齐**。本轮把它补齐。
> 规则见 skill `SKILL.md::★★ 1:1 复刻硬标准`（布局/素材/字体/色调/交互/节奏六项，允许差异清单默认为**空**）。
> 通用前置按 `docs/agents/_common.md`（**尤其 §3.5**）。
> 改完必须回归：`tools/run_all_hosts.ps1`（10 宿主）+ `.ai-tmp/hosts/fullcheck`。

---

## 0. 1:1 的**权威依据**（不许自己"设计一版顺眼的"）

原版暗黑2 的界面基准分辨率是 **800×600**。社区复刻工程 `mofr/Diablerie` 里有**原版面板的 Unity prefab**，
里面是**原版元素的精确 RectTransform**（位置/尺寸/层级）——这是本轮唯一依据。

⛔ **只许读**（`c:\Work\Server\full-dev\_assets_tmp\d2src\Diablerie\Assets\` 下的这些文件）：

| 用途 | 依据文件 |
| --- | --- |
| 主菜单 | `Prefabs/Menu/MainMenu.prefab`(38KB) + `Scenes/MainMenu.unity` |
| 选角 / 创角 | `Prefabs/Menu/ClassSelectMenu.prefab`(45KB) |
| 按钮 | `Prefabs/Menu/WideButton.prefab` + `Prefabs/Menu/MediumButton.prefab` |
| HUD（控制面板） | `Prefabs/ControlPanel.prefab`(87KB) |
| 背包 | `Prefabs/InventoryPanel.prefab`(92KB) |
| 人物属性 | `Prefabs/CharstatPanel.prefab`(29KB) |
| 技能树 | `Prefabs/SkillPanel.prefab`(44KB) + `Prefabs/AvailableSkillsPanel.prefab` + `Prefabs/SkillSlot.prefab` |
| 地图进入标题 | `Prefabs/LevelEntryTitle.prefab` |
| 怪物血条 | `Prefabs/EnemyBar.prefab` |

**换算口径**：原版坐标是 800×600 基准 ⇒ 我们的 Canvas 是 **1920×1080**（引擎固定，
见 `clover-client-unity-engine/Runtime/Presentation/UI.cs:52-56`）⇒ 缩放系数 **2.4**。
**每个元素的坐标/尺寸 = 原版值 × 2.4**，并**在代码里写成具名常量**（`UiLayout` 表），
**不许散落在各处**、不许"看着差不多"。

**要求**：把「面板名 → 原版坐标 → 我们的坐标」整理成一张对照表放进回报（这是本轮的验收凭证）。

---

## §A 交给 `agent-05`：**流程面板 1:1**（主菜单 / 选角 / 创角 / 设置 / 读条 / 启动）

**只许改**：`client/Assets/Scripts/UI/{BootPanel,MainMenuPanel,SettingsPanel,CharSelectPanel,CharCreatePanel,LoadingPanel,PausePanel}.cs`
（**新增 `UI/UiLayoutFlow.cs`** 放你自己的布局常量表；`UiArt.cs` 只读引用，要改先回报）。

> ⚠️ **并发写冲突规避**：§B 也在加布局常量。**你只许新建/修改 `UI/UiLayoutFlow.cs`**，
> **绝对不许**新建或修改 `UiLayout.cs` / `UiLayoutGame.cs`（那是 §B 的）。两边**互不碰文件**。

**要做**：
1. 新建 `UI/UiLayoutFlow.cs`：原版 800×600 → 1920×1080 的**坐标常量表**（至少覆盖下面 2~5 项），
   每个常量注释里写「原版值 → ×2.4」，并注明依据的 prefab 与节点名。
2. **主菜单**：按 `MainMenu.prefab` 逐元素对齐 —— 背景 `main_screen` 铺满、
   **按钮的 x/y/宽高/间距照原版**（原版按钮在画面**左侧偏下**、宽 272×35 原版像素）、
   按钮底图用原版帧（`ResPaths.Frame(ResPaths.MenuButtonWide, i)`，i=0/1/2 = 常态/悬停/按下）。
3. **选角 / 创角**：按 `ClassSelectMenu.prefab` —— 背景 `class_select_screen`、职业按钮位置与尺寸、
   属性行/加减按钮/确定返回的位置、名字输入框位置，全部照原版。
4. **字体**：**英文/数字一律走原版位图字体**（`UI/D2Text.cs` 已有；`font16/24/30/42`）；
   中文才回退 `UIFactory.DefaultFont()`。字号按原版字号（16/24/30）对应到最近的位图字体档位。
5. **色调**：一律原版亮度（白），**不许**自己提亮/压暗。

**验收**：
- [ ] 对照表（面板 → 原版坐标 → 我们的坐标 → 依据节点名）贴进回报
- [ ] 进 Play 截图（`Assets/Screenshots/fix_ui_layout_menu.png` / `_charcreate.png`）**与原版图并排逐项比对**，
      每项只写 `一致` / `不一致（差在哪）`
- [ ] 离线断言（`.ai-tmp/hosts/uicheck`）：布局常量 = 原版值 × 2.4（逐条）
- [ ] 10 宿主全绿

---

## §B 交给 `agent-09`：**游戏内面板 1:1**（HUD / 背包 / 属性 / 技能树 / 任务 / 对话 / 商店）

**只许改**：`client/Assets/Scripts/UI/**`（`HudPanel/InventoryPanel/CharacterPanel/SkillTreePanel/QuestLogPanel/NpcDialogPanel/ShopPanel/MiniMapPanel/DeathPanel/ItemTooltip.cs/UiArt.cs`）。

**要做**：
1. 用 `§0` 的依据表，把**已实现的面板逐元素对齐原版**：
   - **HUD**：`ControlPanel.prefab` 的元素坐标（agent-09 上一轮已取过一部分，本轮**逐个复核并补齐**：
     血球/蓝球/经验条/小面板/左右技能格/金币/腰带/菜单按钮，含**尺寸**与**层级顺序**）；
   - **背包**：`InventoryPanel.prefab` —— **每个格子与每个装备槽的精确矩形**（原版格子步进约 29.2 原版像素 ⇒ ×2.4）；
   - **属性面板**：`CharstatPanel.prefab` —— 每个属性行、加减按钮、文字基线位置；
   - **技能树**：`SkillPanel.prefab` + `SkillSlot.prefab` —— 技能格位置、连线、图标尺寸；
   - **怪物血条**：`EnemyBar.prefab` 的尺寸与偏移。
2. 新建 **`UI/UiLayoutGame.cs`** 放你自己的布局常量表。
   > ⚠️ **并发写冲突规避**：§A 在建 `UI/UiLayoutFlow.cs`。**你只许建/改 `UiLayoutGame.cs`**，
   > **绝对不许**碰 `UiLayoutFlow.cs` 或新建 `UiLayout.cs`。两边**互不碰文件**。
3. 字体与色调规则同 §A（英文数字走原版位图字体、原版亮度）。

**验收**：
- [ ] 对照表（每个面板的每个元素）贴进回报
- [ ] 进 Play 截图（`fix_ui_hud.png` / `_inventory.png` / `_charstat.png` / `_skill.png`）逐项比对
- [ ] 离线断言（`.ai-tmp/hosts/uicheck`）：HUD 元素两两不重叠、格子总数 = 40、装备槽 = 10、与原版值 ×2.4 一致
- [ ] 10 宿主全绿

---

## 共用硬要求

- ⛔ **不许自己发明布局**；依据只有 `§0` 的原版 prefab。
- ⛔ **不许用占位色块顶替原版贴图**（原版 PNG 已在 `Resources/Clover/D2/UI/**`；
  若某个元素的原版贴图确实不在手上，**写进 `client/资源欠缺清单.md`** 并在回报里点名，**不许静默用色块**）。
- ⛔ **不许提亮/压暗**原版图；色调 = 白。
- 驱动编辑器前**必须先跑** `client/_dev/p_runbg.cs`（失焦不 tick，见 `constraints.md` #11）；
  截图前等 ≥6 秒否则拿过期帧；面板关闭判定用 `Game.UI.IsOpen<T>()`（不要用 `FindObjectsByType`）。
- ⛔ 不许读工作区里其它 `clover-project-*`。
