# 设施登记簿（**每次新增对外可见的设施，回来补一行**）

> 保持更新 —— 过期比没有更糟。主 agent 统一维护，**agent 不许各自改它**。

## 消息号

| 消息号 | 名称 | 方向 | 用途 |
| --- | --- | --- | --- |
| — | — | — | **本项目单机，不使用消息号** |

## 场景

| 场景 | 路径 | 用途 | Build Index |
| --- | --- | --- | --- |
| `Boot` | `Assets/Scenes/Boot.unity` | 启动画面（仅 Bootstrap + Canvas） | 0 |
| `Menu` | `Assets/Scenes/Menu.unity` | 纯 UI 场景（主菜单/选角/创角/设置） | 1 |
| `Stage` | `Assets/Scenes/Stage.unity` | 游戏场景（相机 + 地图根 + 实体根 + HUD Canvas） | 2 |

## 流程站点（App Flow）

| 站点 | Fsm 状态 | 面板 | 场景 |
| --- | --- | --- | --- |
| 启动画面 | `Boot` | `BootPanel` | `Boot` |
| 主菜单 | `MainMenu` | `MainMenuPanel` | `Menu` |
| 设置 | `MainMenu`（子面板） | `SettingsPanel` | `Menu` |
| 角色选择 | `CharSelect` | `CharSelectPanel` | `Menu` |
| 创建角色 | `CharCreate` | `CharCreatePanel` | `Menu` |
| 读条 | `Loading` | `LoadingPanel` | 切换中 |
| 游戏内 | `Stage` | `HudPanel`（+ 子面板） | `Stage` |
| 暂停 | `Pause` | `PausePanel` | `Stage` |
| 死亡 | `Stage`（子面板） | `DeathPanel` | `Stage` |

## 客户端模块（门面接口 / 实现）

| 门面接口 | 实现 | 文件 | 职责 |
| --- | --- | --- | --- |
| `IAppFlow` | `AppFlow` | `Module/Flow/AppFlow.cs` | 流程编排（面板 + 场景 + 状态机） |
| `IMapModule` | `MapModule` | `Module/Map/MapModule.cs` | 格子地图 / 随机生成 / 可走 / A* / 等距投影 |
| `IPlayerModule` | `PlayerModule` | `Module/Player/PlayerModule.cs` | 主角属性、位置、移动 |
| `ICombatModule` | `CombatModule` | `Module/Combat/CombatModule.cs` | 伤害/命中/抗性/死亡/复活 |
| `IMonsterModule` | `MonsterModule` | `Module/Monster/MonsterModule.cs` | 怪物 AI 与精英词缀 |
| `ISkillModule` | `SkillModule` | `Module/Skill/SkillModule.cs` | 技能树/学习/施放/投射物 |
| `IItemModule` | `ItemModule` | `Module/Item/ItemModule.cs` | 物品生成/背包/装备/腰带/掉落 |
| `IQuestModule` | `QuestModule` | `Module/Quest/QuestModule.cs` | 任务状态机 |
| `INpcModule` | `NpcModule` | `Module/Npc/NpcModule.cs` | NPC/对话/商店/修理 |
| `ICameraRig` | `CameraRig` | `Module/Camera/CameraRig.cs` | 等距跟随相机 |
| `IViewModule` | `ViewModule` | `Module/View/ViewModule.cs` | 精灵视图（朝向/动画/血条/飘字） |
| `IAudioModule` | `AudioModule` | `Module/Audio/AudioModule.cs` | 音效触发点 |
| `ISaveModule` | `SaveModule` | `Module/Save/SaveModule.cs` | 存档读写 |

## 客户端面板

| 类 | 文件 | 层 | 职责 |
| --- | --- | --- | --- |
| `BootPanel` | `UI/BootPanel.cs` | Normal | 启动画面，任意键继续 |
| `MainMenuPanel` | `UI/MainMenuPanel.cs` | Normal | 单人/多人/设置/退出 |
| `SettingsPanel` | `UI/SettingsPanel.cs` | Popup | 音量 / 全屏 / 分辨率 |
| `CharSelectPanel` | `UI/CharSelectPanel.cs` | Normal | 角色列表、进入、删除 |
| `CharCreatePanel` | `UI/CharCreatePanel.cs` | Normal | 2 职业半身像（原版 5 槽几何保留、3 槽停用）+ 名字。**名字语义（R1-F）**：开屏预填默认名 `Hero`，且**默认名是一个「整体单元」** —— 首次键入任一有效字符时**整个缓冲被该字符替换**（`_nameDefaultPending` + 纯函数 `EditNameDefault`；空字段上退格/Delete 无效果）⇒ 不会再出现 `HeroAma65x`。⛔ 不许绕开 `EditNameDefault` 直连 `EditName`（`tools/probes/hosts/uicheck` 第 ⑱ 节有反向断言） |
| `LoadingPanel` | `UI/LoadingPanel.cs` | System | 真读条 |
| `HudPanel` | `UI/HudPanel.cs` | Normal | 双球 / 经验条 / 技能栏 / 快捷键 |
| `MiniMapPanel` | `UI/MiniMapPanel.cs` | Normal | Tab 自动地图 |
| `InventoryPanel` | `UI/InventoryPanel.cs` | Popup | 10×4 背包 + 装备栏 + 腰带 |
| `CharacterPanel` | `UI/CharacterPanel.cs` | Popup | 四维属性与派生属性 |
| `SkillTreePanel` | `UI/SkillTreePanel.cs` | Popup | 技能树 |
| `QuestLogPanel` | `UI/QuestLogPanel.cs` | Popup | 任务日志 |
| `NpcDialogPanel` | `UI/NpcDialogPanel.cs` | **Normal**（**R1-E 的 S1 改层**：原为 `Popup` —— 与商店同层 ⇒ 引擎 `CloseMutexPanels()` 会把它 `Destroy`，且不发关闭事件；商店仍留 `Popup`） | NPC 对话（与商店并存；选项列常量见「核心常量与路径」） |
| `ShopPanel` | `UI/ShopPanel.cs` | Popup | 买卖与修理 |
| `PausePanel` | `UI/PausePanel.cs` | Top | 继续/选项/保存退出/回主菜单 |
| `DeathPanel` | `UI/DeathPanel.cs` | Top | 死亡与复活 |

## 核心常量与路径

| 类 | 文件 | 内容 |
| --- | --- | --- |
| `GameConst` | `Core/GameConst.cs` | 格子/瓦片/PPU/速度/背包尺寸/腰带格数 |
| `Events` | `Core/Events.cs` | 全部事件名常量（`D2.` 前缀） |
| ↳ **片 8 新增** `Events.PlayerAttacked` | `Core/Events.cs` | 「玩家真的挥出一刀」（参数 `int monsterId`）；**发送方** = `Module/Combat/CombatModule.PlayerBasicAttack`（冷却已扣/距离已够之后，命中与未命中都算一次挥击）；**收方** = `Module/View/ViewModule.OnPlayerAttacked`（播原版 `attack` 动作，时长 = `GameConst.PlayerAttackInterval`）。**只增不改**（与 `AttackRequest` 的区别：那个是"请求"，会被冷却/超距丢弃） |
| `ResPaths` | `Core/ResPaths.cs` | 全部资源路径（原版素材 / 音效 / 面板） |
| `SceneNames` | `Core/SceneNames.cs` | `Boot` / `Menu` / `Stage` |
| `Cfg` | `Core/ClientConfig.cs` | 读 `Assets/Configs/config.json` |
| `Iso`（**门面**；实现在引擎） | `Core/Iso.cs` → `CloverEngine.IsoLayout`（`clover-client-unity-engine/Runtime/Core/IsoLayout.cs`） | 等距正/逆投影 |
| `AStar`（**已下沉引擎**，A2 起） | `clover-client-unity-engine/Runtime/Core/AStar.cs`（`CloverEngine.AStar`） | 格子 A* |
| `Rng`（**已下沉引擎**，B1 起） | `clover-client-unity-engine/Runtime/Core/Rng.cs`（`CloverEngine.Rng`） | 注入式随机（seed 可复现） |
| `Save`（**落盘已下沉引擎**，A6 起） | `Module/Save/SaveModule.cs` → 引擎 `clover-client-unity-engine/Runtime/Data/FileSlotStore.cs`（`CloverEngine.FileSlotStore`） | 角色档 = **一角色一文件** `<SettingDir>/saves/<角色名>.json`（原子写 + 损坏留档 + 枚举）；`char/index` 只留**创建先后**（选角屏顺序）、`char/{名}` 只留**旧档懒迁移** |
| `FramePacing`（**R1-D 新增**） | `Core/FramePacing.cs` | **帧节奏的唯一口径**：`TargetFrameRate = 60` + `VSyncCount = 0`（与画质档位**无关**）。`Pin(reason)` 幂等重钉（随 `Bootstrap` 启动 + **每次改画质档位之后**；`QualitySettings.SetQualityLevel` 会按档位重置 `vSyncCount`）；`ResetStaticsForNewPlaySession()` 复位"只报一次"；非预期分支各只报一次 Warn。⛔ 本类**只**写帧节奏，画质内容（阴影 / 分辨率缩放 / LOD / 贴图限制）一律不碰。`Describe(...)` 的单行文本被 `playercheck` §15 断言用词 |
| ↳ **R1-E 新增** `NpcDialogPanel` 选项列常量 | `UI/NpcDialogPanel.cs` | `Layer => UILayer.Normal`；`OptionW = 66f`（**原版px** = 两雕花方槽之间净宽 72 **内缩 3**）、`OptionSize`（= `OptionW × K`, 高 25.5）、`OptionX = Cx((SlotCellLeftX1 67 + SlotCellRightX0 139) / 2)`（两雕槽中点 = **原版 x 103**；底图横向中线 105 ⇒ 差 3.6 画布px）、`OptionStep = 28.5f`（**行距**）、`OptionOrigY(i)`（行心换算回原版 y，对账/断言用）。判据 `uicheck` 第 ⑬ 节 S6；登记 **E37**（取代原 E17 的"选项与正文同列 / 行宽 334.8"） |
| ↳ **R1-E 新增** `UiLayoutGame` 商店标题/提示两行 | `UI/UiLayoutGame.cs` | `ShopTabBottomY = ShopTabY - ShopTabSize.y * 0.5f`、`ShopGridTopY = ShopGridOrigin.y`、`ShopInfoLineSize = (288 × K, ShopInfoLineH)`、`ShopTitlePos` / `ShopHintPos`（y = **页签带底沿 ↔ 格区顶沿**空带中点 **±15**，x 居中）。实机落位 `titleScreen=960,861` / `hintScreen=960,831`；登记 **E34**（原版页名是运行时文字、本机无串表出处） |
| ↳ **R1-E 新增** `InputReader` 的 UI 命中判定 | `Module/Input/InputReader.cs` | `PointerOverUi`（属性，`Func<bool>`；默认源 = `UiPointerProbe.PointerOverUi` —— 走反射取引擎/Unity 的 UI 命中判定，取不到 ⇒ 按"不在 UI 上"降级并 Warn 一次）、纯函数 `UiEatsIntent(bool pressed, bool pointerOverUi) => pressed && pointerOverUi`。**用途**：指针压在 UI 上时该次按下**不算**地面移动意图（= R1-E 的 S2；实机 `moveCmdDelta=0 gridChanged=0`） |

## 配表登记

> **加载器已下沉引擎（A4，2026-09-19）**：tsv 读取 + 泛型 `Get<T>` 走 **`CloverEngine.CloverTable`**（引擎 `Runtime/Data/CloverTable.cs`，E-core-11）；本项目 `Table/TableLoader.cs` 现为**薄转发**，打表生成的强类型壳 `Tables.Default.*` 保留（它是便捷访问层，不是加载器）。

| 源表 | 后缀 | 用途 | 关键字段 | 生成物 |
| --- | --- | --- | --- | --- |
| `class_c` | `_c` | 5 职业初始与成长 | `name/str/dex/vit/eng/life_per_lvl/mana_per_lvl` | `Assets/Scripts/Table/**` |
| `experience_c` | `_c` | 经验曲线 | `level/exp` | 同上 |
| `monster_c` | `_c` | 怪物属性与 AI | `name/level/hp/ac/ar/dmg_min/dmg_max/exp/ai/speed/sprite` | 同上 |
| `level_c` | `_c` | 区域 | `name/area_level/mon_density/monsters/size_min/size_max/random` | 同上 |
| `skill_c` | `_c` | 技能树 | `class/tree/name/req_level/req_skill/mana_cost/dmg_*` | 同上 |
| `item_c` | `_c` | 武器/防具/杂项 | `name/type/subtype/grid_w/grid_h/lvl_req/dmg_*/def_*/price/stack` | 同上 |
| `affix_c` | `_c` | 魔法词缀 | `name/kind/lvl/mod/min/max/item_types` | 同上 |
| `monumod_c` | `_c` | 精英词缀 | `name/hp_mul/dmg_mul/ac_mul` | 同上 |
| `treasureclass_c` | `_c` | 掉落表 | `picks/drops/next_tc` | 同上 |
| `missile_c` | `_c` | 投射物 | `name/speed/dmg_min/dmg_max/dmg_type/radius` | 同上 |

## 通用函数（后续代码优先复用）

| 函数 / 类 | 位置 | 用途 |
| --- | --- | --- |
| `Iso.GridToWorld(Vector2Int)` | `Core/Iso.cs` | 格 → 世界 |
| `Iso.WorldToGrid(Vector3)` | `Core/Iso.cs` | 世界 → 格（负数 Floor） |
| `Iso.SortOrder(Vector2Int)` | `Core/Iso.cs` | 深度排序 |
| `AStar.Find(map, from, to)` | **引擎** `Runtime/Core/AStar.cs` | 返回路径点（格） |
| `UIFactory.Stretch/CreateCentered/CreateText/CreateButton` | 引擎 | **代码搭 UI 一律用它**，禁止自写锚点工具 |
| `WorldHpBar.Create/SetHp` | 引擎 | 世界空间头顶血条 |

## 资源目录

| 目录 | 内容 | 来源 |
| --- | --- | --- |
| `Resources/Clover/D2/UI/**` | 原版控制面板/血蓝球/经验条/菜单屏/背包/属性/装备栏/光标 | `mofr/Diablerie`（D2 原版素材） |
| `Resources/Clover/D2/Fonts/**` | 原版位图字体 font16/24/30/42 | 同上 |
| `Resources/Clover/D2/Tiles/**` | 等距地形瓦片 | 官方 D2 本体（`.dt1` 解出，后台下载中） |
| `Resources/Clover/D2/Objects/**` | 建筑/树/岩石/栅栏/篝火 | 同上 |
| `Resources/Clover/D2/Chars/**` | 5 职业 8 方向动画帧 | 同上（`.dcc` 解出） |
| `Resources/Clover/D2/Monsters/**` | 怪物动画帧 | 同上 |
| `Resources/Clover/D2/Items/**` | 物品图标 | 同上 |
| `Resources/Clover/Sound/{BGM,SFX}/**` | 音效与 BGM | 同上（d2sfx/d2music） |
| `Resources/UI/{类名}` | 面板预制体 | `Editor/ProjectBuilder.cs` 生成 |
