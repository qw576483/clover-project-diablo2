# agent-09：HUD + 全部游戏内面板（原版布局）

## 0. 技能（开工必做）

先读并照做：`<项目根>/docs/agents/_common.md`（**尤其 §3.5**）。
项目根 = `clover-project-diablo2`。
另必读：skill **`patterns/client/ui.md`**、`patterns/client/app-flow.md` §3 模板 4/6；
`docs/步骤文档.md` §3.3（UI 签名）/§3.6（面板清单）；`tools/ai-skill/constraints.md` #3 #7；
`策划/策划案/暗黑破坏神2参考规格.md` §3「HUD / 背包 / 装备栏 / 属性 / 技能树 / 任务」的**原版布局要求**。

## 1. 目标

交付交付三段式里的「HUD + 游戏内全部面板」，**布局按原版暗黑2**（用已到手的原版贴图 + 原版位图字体）。

## 2. 任务边界

- **只做**：`client/Assets/Scripts/UI/**`（**新增文件**）与 `client/Assets/Scripts/UI/D2Text.cs` 之类的 UI 层助手。
- **绝不做**：不改已有 7 个流程面板（`BootPanel/MainMenuPanel/SettingsPanel/CharSelectPanel/CharCreatePanel/LoadingPanel/PausePanel`）
  与 `UI/UiArt.cs`（agent-05 的）—— **只许复用**；不改 `Module/*`、`Core/`、`Def/`、`Contracts.cs`、`App/`、`Editor/`；不改契约；不改 `tools/ai-skill/`。

## 3. 前置依赖（已就绪）

- `UI/UiArt.cs`（agent-05）：铺满根 / 原版贴图背景 / 按钮 / 文本 / 输入框 / **锚点宽度进度条** —— **先看它有没有现成的再用**，不要重复写。
- 原版素材（agent-03，实测尺寸见 `client/_dev/assetreport.txt`）：
  `ResPaths.D2Ui + "Panel/healthbar"`(80×80) / `"Panel/manabar"`(80×80) / `"Panel/ExperienceBar"`(50×5) /
  `"Panel/ExperienceBarOverlay"`(948×160) / `"Panel/ControlPanel"`(948×160) / `"Panel/inventory"`(320×432) /
  `"Panel/charstat"`(320×432) / `"Panel/minipanel"`(173×26) / `"Panel/menubutton__0__{0..3}"`(15×24) /
  `"Panel/minipanelbtn__00__{0..15}"`(20×20) / `"EquipSlot/inv_*"` / `"Cursor/Cursor"`(32×26) / `"SkillIcon/SkilliconAttack"`(48×48)
- **位图字体**（原版，实测切分参数见 `_dev/assetreport.txt`）：`font16`(512², 32×8 格 16×18) / `font24`(512², 21×13 格 24×28) /
  `font30`(512², 16×16 格 31×32) / `font42`(1024², 24×11 格 41×43)；**只有 256 个拉丁字形，没有中文**
  ⇒ 中文文本**必须**用 `CloverEngine.UIFactory.DefaultFont()`（引擎内置字体）渲染；原版位图字体只用于**数字/英文**。
- 模块门面（全部 `internal` 实现由 `AppContext.AutoWire` 装好）：`IPlayerModule` / `IItemModule` / `ISkillModule` / `IQuestModule` / `INpcModule` / `ICameraRig` / `IMapModule` / `ICombatModule` / `IMonsterModule` / `ISaveModule` / `IAudioModule`
  —— **注意：`UI/**` 不许 `using Diablo2.Module`**！只能通过 `Core/Events.cs` 事件 + `OnOpen(param)` 拿数据。
  若某数据没有对应事件可得 → **回报主 agent 加事件**，**不许**私自 `using Module`。

## 4. 产出物（`client/Assets/Scripts/UI/`，**一个文件一个 MonoBehaviour**）

| 面板 | 层 | 原版布局要点 |
| --- | --- | --- |
| `HudPanel` | Normal | 底部左右两个球（左红生命 / 右蓝法力）、中间经验条、技能栏左右两格、上方快捷键提示行；监听 `Events.StageEntered/StageLeft` 自动开关（agent-05 的约定） |
| `MiniMapPanel` | Normal | Tab 开关；从 `Events` 累积已探索格自绘（**不给模块耦合**）；用 `Events.PlayerGridChanged` 之类的格坐标事件 |
| `InventoryPanel` | Popup | 原版 `inventory`(320×432) 背景 + **10×4 背包格** + 装备栏（`EquipSlot/inv_*` 底图）+ 腰带 4 格 + 金币；拖放物品（`IBeginDragHandler/IDragHandler/IEndDragHandler` 或自写）；**格子对齐必须与 `GameConst` 一致** |
| `CharacterPanel` | Popup | 原版 `charstat`(320×432)：四维属性 + 派生属性 + **加点按钮**（剩余点数显示） |
| `SkillTreePanel` | Popup | 5 职业 × 3 系；节点按 `SkillTreeArgs` 渲染可学/锁定；点击学习；右键设为按钮技能 |
| `QuestLogPanel` | Popup | Q 键；任务名/目标/进度（邪恶洞穴：剩余怪物数）；三种状态各不同显示 |
| `NpcDialogPanel` | Popup | 原版对话样式；文本 + 选项按钮（含"接受任务/交任务"）；任务阶段不同文本 |
| `ShopPanel` | Popup | 商品列表 + 玩家物品列表 + 金币；买入/卖出/修理；`buysellbtn`/`goldcoinbtn` 贴图**当前不可用**（多帧条带未切分，见 agent-03 未决 2）⇒ 先用 `UiArt` 按钮，**在回报里登记为待接线** |
| `DeathPanel` | Top | "你死了" + 复活按钮；**定时器必须用 `*Unscaled`** |
| `ItemTooltip.cs`（非 MonoBehaviour 也可） | — | 物品 tooltip：名称按品质配色（白/蓝/金/绿/暗金）+ 属性行；跟随鼠标 |
| `D2Text.cs` | — | 位图字体渲染助手：按 `font{N}` 的**实测格子参数**把数字/英文渲染成 sprite 文本；中英混排时回退 `UIFactory.DefaultFont()`。**标注"本项目新增"** |
| `UiLog.cs` | — | tag = `Ui` |

**硬要求**（违反即返工）

1. **一个 `.cs` 一个 MonoBehaviour**；面板预制体名 = 类名（由 agent-10 生成，你**不要**手工造 `.prefab`）。
2. **状态值一律 `OnOpen(param)` 里刷**，**禁止在 `Awake` 里读上下文**；缺参数 `Log.Warn`。
3. **血球/经验条不许用 `Image.Type=Filled` + 空 sprite**（`constraints.md` #3）—— 用锚点宽度或给 1×1 白 sprite。
4. **面板根节点必须 `UiArt` 的铺满**（不要自己写锚点工具）。
5. **`UI/**` 零 `using Diablo2.Module`**（用锚定版 grep 自检）。
6. 中文文案：来自配表 `name_cn` / NPC 对话已给中文时直接用；其余 UI 文案写中文常量（标注"本项目新增"）。
7. 快捷键集中在 `Def/GameKeyAlias.cs`（已存在，直接用）。

## 5. 验收标准（**离线宿主 + 编辑器由主 agent 后补**）

- [ ] 离线宿主 `dotnet build` 0 错 0 警告（照 `.ai-tmp/hosts/flowcheck/` 的做法建 `.ai-tmp/hosts/uicheck/`）
- [ ] `OnOpen` 参数缺失时**打 Warn 且面板不崩**（断言）
- [ ] 血球/经验条：断言填充值是**通过 `RectTransform.anchorMax.x` 或带 sprite 的 `fillAmount`** 实现的（贴代码位置）
- [ ] 背包格子：`GameConst.InventoryCols/Rows` 与面板格子数一致（断言 40）
- [ ] 品质配色：5 种品质的颜色常量互不相同且与 `策划案` 描述一致（贴颜色值）
- [ ] 分层自检 ②=0 / ③=0（锚定版）/ ④=0 / ⑤=0
- [ ] **回报里逐面板列出**：类名 / 预制体路径 / 层 / 监听的事件 / 需要 `OnOpen` 传入的参数类型
- [ ] **未接线项单独列一节**（例如按钮条带贴图待切分、需要新增事件才能拿到的数据）

## 6. 约束

- 所有非预期分支必须打日志。**禁止** `GameObject.Find` / `FindObjectOfType` / 裸 `Debug.Log`。
- 用户未打开编辑器 ⇒ **禁止** `unity run/test/batchmode`；视觉验收由主 agent 在编辑器里做。
