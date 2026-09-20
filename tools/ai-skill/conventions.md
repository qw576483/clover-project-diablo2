# 本项目约定

## 形态

**单机**（`策划案/暗黑破坏神2参考规格.md` §0 判定，只判一次）。因此：

- **不建 `server/`**，不写任何 Go 代码；
- 初始化只走 `Game.Launch` + `CloverRes.Init("Clover")` + `CloverInput.Init()`（+ 可选 `CloverData.InitDataTable`），
  **不调 `CloverNet.Init`**、不查服务器环境、不跑 `env.exe`；
- **禁止调用** `Game.Net` / `Game.Sync` / `Game.Alert` / `Game.CloverScene` / `Game.FrameRoom` / `Game.Schema` / `Game.Http` / `Game.LanBrowser`（单机下全为 null，调用必崩）；
- 交付说明首行标注「**单机版**」。

## 消息号段分配

**本项目不用消息号**（单机无网络）。此节保留占位：若将来加联机，按全局 skill 的分段重新分配。
`client/Assets/Scripts/Def/MsgDef.cs` **不创建**（避免误用）；`Def/` 只放枚举与纯数据定义。

## 命名

| 对象 | 约定 | 例 |
| --- | --- | --- |
| 程序集 | `Diablo2` / `Diablo2.Editor` / `Diablo2.Tests.PlayMode` | — |
| 命名空间 | `Diablo2.{层}`（`Core` / `Def` / `Module.X` / `UI` / `App`） | `Diablo2.Module.Map` |
| 模块门面 | `I{名}` 接口 + `{名}Module` 实现，实现 **internal** | `IMapModule` / `MapModule` |
| 面板 | `{名}Panel`，**一个文件一个类** | `HudPanel.cs` |
| 常量类 | 静态类 + `const`，**不写裸字面量** | `GameConst.TileSize` |
| 事件名 | 常量放 `Core/Events.cs`，命名法 `D2.域.动作` | `Events.MoveCommand` |
| 资源路径 | 常量化在 `Core/ResPaths.cs`，**禁止散落字面量** | `ResPaths.D2Ui + "healthbar"` |
| 日志 | `Game.Logger.Info/Warn/Error(tag, msg)`，tag 用模块名 | `Game.Logger.Info("Map", ...)` |
| 随机 | 一律 `CloverEngine.Rng`（**已下沉引擎**；B1 起项目不再自写）的**注入式** `System.Random`（seed 可复现），**禁止 `UnityEngine.Random`** | `_rng.Next(0, 4)` |
| 存档 | 角色档 = 引擎 `CloverEngine.FileSlotStore` 的**槽位** `saves/{角色名}.json`（**A6 起**，见 `Module/Save/SaveModule.cs`）；⚠️ **角色名即文件名** ⇒ 只收字母 / 数字 / 中文 / `_` / `-`（`CharCreatePanel.IsNameCharAllowed`）；`char/index` 只留**创建先后**（选角屏顺序） | `save.Save(data)` |

## 目录边界

```
client/Assets/
├── Scripts/
│   ├── Diablo2.asmdef
│   ├── Def/          # 枚举与纯数据定义（无逻辑、无 Unity 依赖）
│   ├── Core/         # 常量 / 事件 / 场景名 / 资源路径 / 配置（等距数学 / A* / 随机已下沉引擎，这里只留门面）
│   ├── Module/
│   │   ├── Flow/     # 流程编排（启动→主菜单→选角→创角→读条→进图→暂停→回菜单）
│   │   ├── Map/      # 格子地图 + 随机生成 + 可走查询 + A* + 等距投影
│   │   ├── Player/   # 主角：点击移动、属性、装备生效
│   │   ├── Combat/   # 伤害/命中/抗性/死亡/复活
│   │   ├── Monster/  # 怪物 AI 与精英词缀
│   │   ├── Skill/    # 技能树/学习/施放/投射物
│   │   ├── Item/     # 物品生成/背包/装备/腰带/掉落
│   │   ├── Quest/    # 任务状态机（邪恶洞穴）
│   │   ├── Npc/      # NPC 定义/对话/商店/修理
│   │   ├── Input/    # 鼠标点击与悬停、快捷键
│   │   ├── Camera/    # 等距跟随相机
│   │   ├── View/     # 精灵视图（朝向/动画/血条/飘字）
│   │   ├── Audio/    # 音效触发点
│   │   └── Save/     # 存档
│   ├── UI/           # 面板（一个文件一个 MonoBehaviour，只发/收事件）
│   ├── Table/        # ★ 打表产物（禁止手改：直接生成到 ResPaths 对应目录）
│   └── App/          # Bootstrap（全项目唯一手动挂载的脚本，≤200 行）
├── Editor/           # ProjectBuilder（场景/预制体/BuildSettings）/ AssetImporter（导入设置）
└── Resources/
    ├── Clover/D2/{UI,Fonts,Tiles,Objects,Chars,Monsters,Items}/   # 原版素材（走 Game.Res）
    ├── Clover/Sound/{BGM,SFX}/                                    # 引擎约定路径
    └── UI/{类名}                                                  # ★ 面板预制体（引擎约定）
```

**硬性**：

- **`Assets/Scripts/` 之外不许放业务代码**；
- **跨模块判定只许写在 `App` 或 `Module/Gameplay`**（本项目用 `App` 注入接口 + `Core/Events.cs` 事件总线）：
  `Module/A` **不许** `using` `Module/B` 的具体类型；
- **UI 不许 `using Diablo2.Module.*`**（只发/收事件 + `Game.UI.Open<T>(param)`）；
- **`App/` 只有 `Bootstrap` 一个文件，且 ≤ 200 行**。

## 坐标与等距投影（**本项目最容易错的地方**）

逻辑坐标使用**格子坐标**（整数格，`Vector2Int`）；渲染时做等距投影。

| 对象 | 约定 |
| --- | --- |
| **格子 (gx, gy)** | 占逻辑方形 `[gx, gx+1] × [gy, gy+1]`，中心 `(gx+0.5, gy+0.5)` |
| **等距投影（格 → 屏幕/世界）** | `sx = (gx - gy) * IsoTilePxW/2 / PPU`，`sy = -(gx + gy) * IsoTilePxH/2 / PPU` |
| **等距逆投影（世界点 → 格）** | 反解上面的线性方程组，再 `FloorToInt`（**负数必须 Floor，C# 强转向零截断会出错**） |
| **深度排序** | `sortingOrder = (gx + gy)` 决定，**同一 `gx+gy` 按 gy 排序** |
| **角色锚点** | 精灵 pivot = **脚底中心**（站地上才不会浮空/陷地） |
| **点击移动** | `Camera.main.ScreenToWorldPoint` → 等距逆投影 → 格 → A* → 路径点列表（世界坐标） |

**引擎不提供 `ScreenToWorld`**（`Input.cs` 只有 `MousePosition`/`MouseDelta`），反投影由业务做（属 C 桶）。

## 素材规则

- **只用 A（暗黑2）自己的素材**：来源见 `策划/素材调研.md`；
  **禁止**引入通用兜底素材（KayKit / Quaternius / Kenney / 内置几何体）；
- 所有素材路径**收敛在 `Core/ResPaths.cs`**：换素材 = 换文件，**不动逻辑**；
- 某素材确实未到位时，用**纯色占位**并登记 `client/资源欠缺清单.md`，
  **交付说明首行高亮**「⚠️ 此处为占位，非成品」；
- 原版贴图导入设置：`FilterMode.Point`、`textureType = Sprite`、`spritePixelsPerUnit = 64`、
  无压缩、角色/怪物轴心 = 脚底中心（由 `Assets/Editor/AssetImporter.cs` 按目录统一设置，**新增目录必须同步改它**）。

## 数值规则

- **一切同质化数值走配表**（`策划/数值文档/*_c.txt` → 打表 → `Assets/Scripts/Table/**`）；
- 代码里**不许**出现成排的硬编码数值 / 大 `switch` / 大 `map` 字面量；
- 读表：`Tables.Default.<表名>.Get(id)`（打表产物自带强类型访问器，命名空间 `Table` / `Table.Base`）；
- 只属于"手感参数且永不增长的"常量（如 `PlayerWalkSpeed`）放 `Core/GameConst.cs`。

## 与全局 skill 的差异

| 项 | 全局 skill 写法 | 本项目写法 | 原因 |
| --- | --- | --- | --- |
| 视角 / 操作 | 有角色就做第三人称相机 + 方向键移动 + 鼠标转视角 | **固定等距 + 鼠标点击移动**（**没有**方向键备选移动） | **参考游戏保真优先**：原版 D2 就是等距点击移动；~~曾经的「D2R 风格方向键开关」已按「A 没有 ⇒ 不加」删除~~（验收表 U-1 / bug 表 B35） |
| 服务端 | 默认按联服处理 | **完全没有** | 形态判定为单机 |
| 地图 | 引擎 `MapBake` + `Game.Map`（静态烘焙地图） | **业务侧程序化格子地图 + A\*** | 原版野外/地牢**每局随机生成**，与静态烘焙语义不符；引擎 CloverMap 写入器为 `internal`。**未重造任何二进制格式** |
| 消息号 | 业务消息号 ≥ 10001 | **不用消息号** | 单机无网络 |
| 配表 | `_c/_s/_cs` 三后缀 | 全 `_c` | 单机只出客户端产物 |
| 面板内容 | 预制体里拖好 | **预制体是空壳，内容在代码里用 `UIFactory` 搭** | 复刻原版界面需要像素级对齐，代码里能用常量约束坐标 |
