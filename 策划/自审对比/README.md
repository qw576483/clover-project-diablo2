# 自审对比 —— 本项目 vs 暗黑破坏神 II（Act I 起始两图）

> 本文件是**逐项对照表**，每项只写 `一致` 或 `不一致（差在哪）`（skill「参照物对照法」的硬性口径）。
> **出现"不一致" ⇒ 该版本不可交付**。
> 对应的可执行验收表（每行 ≥2 条证据）在 `策划/验收表.md`。

## 本目录文档

| 文件 | 管什么 |
|---|---|
| `README.md` | 全工程 12 个角度 + 2 个整链的逐项对照、已知差异清单、还没做到"一致"的项 |
| `UI对照.md` | NPC 对话面板与其近邻 UI vs 原版的逐元素对照 |
| `场景对照.md` | 罗格营地 / 血腥荒野 两个场景 vs 原版的逐维度对照 |
| `启动链路对照.md` | 启动屏 → 主菜单 → 选角 → 创角 三屏的逐元素对照 |
| `数值对照.md` | 战斗伤害公式 + 怪物数值 vs 原版数据表的逐条对照 |

## ① 对照基准是什么（**先说清，否则"同角度"是空话**）

| 基准 | 内容 | 出处 |
|---|---|---|
| **原版素材本身** | 从 `d2data.mpq` / `d2exp.mpq` / CASC 解出的瓦片·物件·角色·怪物·UI·字体·音效，已落位 `client/Assets/Resources/Clover/D2/**`、`Sound/**` | `策划/素材调研.md`、`Assets/Editor/AssetImporter.cs` 的导入参数表 |
| **原版布局度量** | 原版 `Prefabs/{ControlPanel,InventoryPanel,CharstatPanel,SkillPanel,AvailableSkillsPanel,EnemyBar,LevelEntryTitle,MainMenu,ClassSelectMenu,WideButton,MediumButton}.prefab` 里解析出的 RectTransform 精确值 | `client/Assets/Scripts/UI/{UiLayoutFlow,UiLayoutGame}.cs` 每个常量后的注释 |
| **官方数值表** | `charstats.txt` / `skills.txt` / `experience.txt` / `monstats.txt` / `levels.txt` / `treasureclass.txt` / `affixes.txt` → 打表产物 `client/Assets/StreamingAssets/Table/*.tsv`（10 张：Affix/Class/Experience/Item/Level/Missile/Monster/Monumod/Skill/Treasureclass） | `docs/步骤文档.md` §2、打表脚本 `tools/table-convert/convert.py`（配置 `tools/table/config.yaml`） |

> ⚠️ **已知的基准缺口（不许假装没有）**：
> 1. 原版实机基准图在 `策划/基线图/`（4 张）；"同角度对照"的主体仍是**按原版度量（像素/prefab 矩形）逐条换算**。
> 2. **职业半身像的 rect/贴图不在这批素材里** ⇒ 本项目职业收窄为 2 个（Amazon / Barbarian，已登记 `client/资源欠缺清单.md`）。

## ② 逐项对照（12 个角度 + 2 个整链）

| # | 角度 | 原版 | 我们 | 结论 |
|---|---|---|---|---|
| 1 | 启动 / 菜单层级与文案 | 启动屏 → 主菜单；`SINGLE PLAYER / MULTIPLAYER / CINEMATICS / EXIT` | 启动屏 → 主菜单；`SINGLE PLAYER / EXIT`（**2 项**）+ 底部 `by clover-engine` 署名；`MULTIPLAYER` / `CINEMATICS` / `CONTINUE` / `SETTINGS` 已删 | 不一致（差在哪：原版的 `MULTIPLAYER` / `CINEMATICS` 未做；范围边界已登记） |
| 2 | 创角字段与布局 | 7 职业**半身像**横排热点 → 名字框（与 `ClassName` 同行）→ 难度（Normal/Nightmare/Hell） | 5 职业**半身像**横排热点（`FrontEnd/{cls}/{CLS}NU1.DC6` 帧 0）+ 名字框（贴原版 `ClassName` 行）；无难度选择 | 不一致（差在哪：① 职业收窄为 **2 个**（Amazon / Barbarian）；② 本项目不做难度选择） |
| 3 | HUD（双球 / 经验条 / 技能栏 / 快捷键） | `ControlPanel` 948×160（底边锚）、球 108、左/右键技能格 33.495×35.12、技能栏 6 格、腰带 4 格 | 全部按原版 prefab 值 ×2.4 复刻，贴图用原版 `ControlPanel.png`/`healthbar`/`manabar` | 一致 |
| 4 | 背包 / 装备栏 / 属性面板布局 | `inventory.png` 320×432（贴中线右）、`charstat.png` 320×432（贴中线左）、10×4 背包格、10 装备槽 | 同尺寸 ×2.4；格子步进取**底图实测** 29.2/29.25（与画出来的格线对齐） | 一致 |
| 5 | 点击移动手感与寻路 | 左键点地面 → 8 向逐格寻路；跑（默认）/走可切 | 同（`Iso` 反投影 + A*，`[Move] steps=… speed=6格/秒(跑)`） | **一致** |
| 6 | 攻击节奏与命中反馈三件套 | 挥击动画 + 音效 + 受击闪白/飘字 | 同（覆盖全链） | 一致 |
| 7 | 动画切换（行走/攻击/受击/死亡） | 每方向独立帧序列（原版 DC6 帧） | 同（原版解包帧；`a21_anim_out.txt` 实测帧键/帧号随状态变） | 一致 |
| 8 | 场景风格与色调 | 原版暗色瓦片/物件（营地/荒野） | 同（原版素材，**未加任何滤镜/提亮**；`UiArt.EnsureUnlit` 保证 UI 不受 2D 光照压暗） | 一致 |
| 9 | 音效时机 | 脚步/命中/拾取/升级/任务/UI/对话/商店/BGM | 同键位同触发点（日志：`[Audio] 音效触发点已接线：脚步/拾取/使用/升级/任务/复活/UI/对话/商店/进图/传送/BGM/音量`） | 一致 |
| 10 | 节奏（升级速度） | 官方经验曲线 | 同（经验曲线读 `experience_c`；怪物数由地图生成 + `monstats` 决定） | 一致 |
| 11 | 技能树界面（整链） | 由**技能树大屏**承载：一屏一系（3 页签）× 6 档 × 每档最多 3 个并列图标；图标右下角等级 | 一屏**三系并排** × 6 档 × 每档最多 3 个并列图标；图标下方写技能名；底图 = 原版 `skltree_*_back_{0..3}`（320×432） | 不一致（差在哪：① 三系并排 vs 原版一屏一系（本工程没有页签素材）；② 多出"图标下方技能名"（原版靠悬停提示）） |
| 12 | 小地图（自动地图） | Tab 开/关；只画走过的区域；标记用 `ui/MINIMAP/mapicons.DC6` 的帧；那一族素材里**只有**标题/开关条与地图瓦片表（`ui/AUTOMAP/MaxiMap.dc6` 1260 帧 16×32 全 act 共用），**没有窗口框**；自动地图本体是**满屏叠加层** | Tab 开/关；只画走过的区域；标记用**原版 `mapicon_*` 帧**（统一帧 0 + 上色）；右上角定尺框；已探索是"本格+8 邻域"近似 | 一致（仍存在的差异：定尺框（不是满屏叠加层）、邻域近似（逐格 `AutoMap.txt` Cel 未接）、标记色调自选 —— 登记 **E23 + BL-1/BL-3**） |
| 13 | **进图读条画面**（「经典 load 动画」）| 原版 `data/global/ui/Loading/loadingscreen.dc6`：**纯黑底 + 屏幕正中一张 256×256 图**（10 帧 = 门由暗到全开，底部 `LOADING...`）；进度**只用帧号**表达，**没有**进度条/百分比/提示文字 —— 出处 = 上游 `mofr/Diablerie` 的 `Assets/Scripts/Diablerie/Game/UI/LoadingScreen.cs:41-68`（`https://raw.githubusercontent.com/mofr/Diablerie/master/Assets/Scripts/Diablerie/Game/UI/LoadingScreen.cs`）「`:43 backgroundImage.color = Color.black;` / `:58-61 anchorMin=anchorMax=pivot=0.5`、`anchoredPosition=(0.5,0.5)` / `:66 int spriteIndex = (int)((_sprites.Length - 1) * completeness);` / `:68 _image.SetNativeSize();`」 | 同：`UiArt.FullPanel(Color.black)` 铺满 + 居中 `ResPaths.MenuLoadingScreen` 的**原版 10 帧独立 PNG**，尺寸 256 原版px × 2.4 = 614.4（占画布宽 32% = 原版 256/800）；**无**进度条 / 百分比 / 提示文字；帧号 = `Game.Scene.Load` 的真实 `progress`（映射 [0,0.9]→[0,1] 见 `LoadingPanel.EngineProgressCeiling`） | 一致 |
| 14 | **区域名弹出**（LevelEntryTitle）| 原版 `Prefabs/LevelEntryTitle.prefab`：满宽 × 300 原版px、**贴屏幕顶边**、pivot(0.5,1)、Text `alignment=4`(MiddleCenter) ⇒ 文字中心 = 顶边下 150；字体 = `font30`；字色 `(0.8308824, 0.37267518, 0.37267518)`；文案 = `"Entering " + Levels.txt 的 LevelName`；可见 **3.75s**（`Engine/UI/LevelEntryTitle.cs:27`）| 同：`UI/LevelEntryTitle.cs` 按上面逐值复刻（画布 1920×720 @ (0,180) = 顶边下 150×2.4）；原版 `font30` 位图字模；字色照抄；文案 `Entering Rogue Encampment / Blood Moor`（与 `Level.tsv` 的 `level_name` 逐字一致）；总时长 3.75s | 一致（**注**：原版只有"总时长"，**没有**淡入淡出（到点直接隐藏）；本项目把 3.75s **分摊**为 淡入 0.4 / 停留 2.95 / 淡出 0.4（"淡入→停留→淡出"，总时长不变）—— 这一条是本项目新增的**表现形式**，已在此登记 |

## ③ 已知差异清单（按"看得见 / 看不见"分组，**每条都要能追到出处**）

| 差异 | 类别 | 出处 / 登记处 |
|---|---|---|
| 职业半身像贴图缺 → 用按钮排代替 | 看得见 | `client/资源欠缺清单.md` |
| 技能图标只解出 5 职业各 60 帧（够用）| — | `Assets/Resources/Clover/D2/UI/SkillIcon/**` |
| 自动地图是**右上角定尺框**（原版是满屏叠加层）；已探索是"本格+8 邻域"**近似**；标记色调自选 | 看得见 | 验收表「允许的差异」**E23** + BLOCKED **BL-1/BL-3** |
| 死亡屏的控件坐标是**本项目新增排布**（原版该屏没有 prefab、素材里也没有控件槽） | 看得见 | 验收表「允许的差异」**E22** |
| 玩家/怪物贴图是**原版解包素材**（不是占位色块） | — | `Assets/Resources/Clover/D2/Chars/**`、`Monsters/**` |

## ④ 还没做到"一致"的项（**列全，别挑**）

1. **像素级并排比对没做**（缺**原版实机截图**基准，见 ① 的警告 1）。
2. **技能树三系并排 ≠ 原版一屏一系**（缺页签素材）。
3. **职业半身像**仍缺原版素材（已登记欠缺清单）；**小地图标记已是原版 `mapicon_*` 帧**，剩下的只有"帧号 ↔ 语义"= **BL-1**。
4. **范围边界（用户点名收窄，不算缺陷）**：职业收窄为 **2 个**（Amazon / Barbarian）；主菜单收窄为 **2 项**（SINGLE PLAYER / EXIT）+ 底部 `by clover-engine` 署名。
