# agent 通用前置（**每个 agent 开工第一件事**）

## 0. 拿到 skill（按顺序取第一个成功的方式；**不许跳过**）

1. 工具集里有 `use_skill` → 依次执行 `use_skill("clover-engine")`、`use_skill("unity-cli")`。
2. 宿主已按 frontmatter 自动加载 → 确认能看到这两份 skill 内容，**不要重复读盘**。
3. **兜底（任何环境都适用）**：用读文件工具按序查找，**命中即用**：
   - **项目级（首选）**：`<项目根>/tools/ai-skill/SKILL.md`（只记本项目特有约定：命名 / 目录细分 / 配表 / 消息号分段）
     → 再按需读 `conventions.md` / `registry.md` / `constraints.md`
     ⛔ **但项目级不得放宽全局规则层**：层级 = 用户本轮明说 > 全局 skill §0~§7 > 项目级 > 项目文档 > 既有代码；
     项目约定只在「全局没写」或「全局明说可自选」处优先，**冲突时照全局做**（全局 `SKILL.md` §1.10）
   - 全局：`~/.codebuddy/skills/ai-skill/SKILL.md`、`~/.claude/skills/ai-skill/SKILL.md`、`~/.cursor/skills/ai-skill/SKILL.md`
   - **unity-cli** 同上（客户端 agent 必需：编译 / 场景 / 日志 / 截图都靠它）
   - 仓库源副本（最后兜底）：`<仓库根>/clover-tools/ai-skill/SKILL.md`
4. 然后按 skill 的「混合模式找依据」查代码（**用户 > 引擎 > 联网/自创**；**不许编 API**）。

## 1. 必读（本项目契约，**改契约 = 违规**）

| 文件 | 看什么 |
| --- | --- |
| `<项目根>/docs/步骤文档.md` | **唯一总纲**：系统清单 / 配表列名 / **引擎 API 契约（§3，已逐条核对含出处行号）** / 常量与 `ResPaths` / 场景与面板名 / agent 拆分 |
| `<项目根>/策划/策划案/暗黑破坏神2参考规格.md` | 保真规格：42 项系统清单、行为→表现规格、数值来源、UI 布局 |
| `<项目根>/tools/ai-skill/conventions.md` | 命名 / 目录边界 / 等距坐标 / 素材规则 / 数值规则 |
| `<项目根>/tools/ai-skill/constraints.md` | **10 条静默失败风险，逐条遵守** |
| `<项目根>/tools/ai-skill/registry.md` | 已有模块 / 面板 / 场景 / 常量 / 配表（新增设施要补一行） |

## 2. 红线（违反即未完成）

- ⛔ **不许读工作区里其它 `clover-project-*`**（源码 / `tools/ai-skill/` / `策划/` / `docs/` / Editor 生成器 / 素材全算）。
  可参考的只有：**本项目**的文件、`clover-client-unity-engine/Runtime/**`、`clover-doc/**`、skill 的 `patterns`/`scaffold`/`experience`。
- ⛔ **不许改契约**：`docs/步骤文档.md` §2/§3 的配表列名、API 签名、常量、`ResPaths`、场景名、面板名。发现问题 → **先回报主 agent**。
- ⛔ **不许改 `<项目根>/tools/ai-skill/`**（由主 agent 统一维护，避免并发写冲突）。
- ⛔ **不许再派生任何子 agent**。需要更多人 → 回报主 agent。
- ⛔ **不许编造 API**：任何类名/方法名/字段名都要能指出出处（引擎源码 `文件:行号` / `clover-doc` / 本项目契约文档）。
- ⛔ **本项目是单机**：不建 `server/`、不写 Go、**不调** `CloverNet.Init`、**不查**服务器环境（不跑 `env.exe`）、
  **不调用** `Game.Net/Sync/Alert/CloverScene/FrameRoom/Schema/Http/LanBrowser`（全为 null，必崩）。
- ⛔ **只用暗黑2 自己的素材**（见 `策划/素材调研.md`）；**禁止**引入 KayKit / Quaternius / Kenney / 内置几何体等通用素材。
  缺图用**纯色占位**并在 `<项目根>/client/资源欠缺清单.md` 登记。

## 3. 日志要求（硬约束）

**凡是"没按预期走"的分支，必须留下一条日志。**
必须打：出错返回 / 兜底判空 / 参数校验失败 / 非法状态 / `switch` 的 `default` / 资源加载失败 / 高频回调里的异常（防刷屏：只报一次或降频）。

- 客户端一律 `Game.Logger.Info/Warn/Error(tag, msg)`，**禁止裸 `Debug.Log`**；
- 日志必须带可定位信息（谁 / 哪个模块 / 关键参数 / 期望 vs 实际）；tag 用模块名（`Map` / `Flow` / `Combat` / …）。

## 3.5 第一批（agent-01~05）已落盘的事实 —— **第二批必须按它写，不许另起一套**

| 事实 | 说明 |
| --- | --- |
| **接口 = `Diablo2.Module`；DTO = `Diablo2.Def`** | `Module/Contracts.cs` 里两个 namespace：12 个门面接口在 `Diablo2.Module`，20 个 DTO 在 `Diablo2.Def`。**DTO 里不许出现 Unity 类型**（用 `gridX/gridY`、`worldX/Y/Z` 而非 `Vector2Int/Vector3`）—— 这样 `UI/**` 只 `using Diablo2.Def;` 就能收发 DTO，不必碰 `Diablo2.Module` |
| **实现类命名 = 自动装配的契约** | `App/AppContext.AutoWire()` 用**反射按接口找程序集内唯一实现并 `Activator.CreateInstance`**。所以你的实现必须叫 `XxxModule`（`internal sealed class MapModule : IMapModule` 这种），且**构造函数无参**或可无参构造。找不到 → 留 `null` + Warn 降级（`Bootstrap` 不需要任何人改） |
| **`IAppFlow` 刻意排除在 AutoWire 之外** | 它构造即订阅事件，自动再 new 会重复处理。**不要**把它加进 AutoWire |
| **`Core` 已提供** | `Log.{Info,Warn,Error,Debug,WarnThrottled,ErrorThrottled,WarnOnce,ErrorOnce}` · `Rng`（注入式，**禁 UnityEngine.Random**）· `Iso`（正/逆投影、排序、方向）· `AStar.{Find,FindSmoothed,HasLineOfSight}` · `GameConst` · `Events`（72 条，`D2.` 前缀）· `ResPaths` · `Cfg` · `SceneNames` · `Def.{Enums,TileKindInfo}`。**直接复用，禁止重写** |
| **配表读法（与 `docs/步骤文档.md` §3.1 的旧写法不同，以本条为准）** | 打表产物是「`File.ReadAllLines(path)` 读 tsv」，**不是** `CloverData.InitDataTable`（那条链路要求行类实现 `IDataRow`，打表产物不实现）。唯一入口：`Table.TableLoader.LoadAll(Application.streamingAssetsPath, Application.dataPath)`，成功返回 `null`、失败返回可定位错误串。调用点在 `App/Bootstrap.cs`（**接线由后续集成 agent 负责，谁都不许私自改 App/**）。表访问：`Table.Tables.Default.<Table>.Get(int|string id)`，行字段是 **public 字段**（`hp_min`→`HpMin`） |
| **地图模块额外入口（`IMapModule` 之外）** | `MapModule.AttachRoot(Transform)` / `SetFogOfWar(bool)` / `MarkExplored` / `IsExplored` / `DumpStats()` / `DumpAscii()` / `Hash()`；`GridMap.FloodFillFrom` / `FillUnreachablePockets` 可复用 |
| **地图出的关键点** | `SpawnPoint` / `Exits`（`Town`→`BloodMoor`；`BloodMoor`→格 `== CaveEntrance` ? `DenOfEvil` : `Town`；`DenOfEvil`→`BloodMoor`）/ `CaveEntrance`（仅野外非 null）/ `NpcPoints`（下标 = `(int)NpcId`）/ `MonsterSpawns`（**仅洞穴非空；野外刷怪一律用 `RandomWalkableTile(rng)`**） |
| **瓦片/物件 sprite 名（素材到位后只改这两个函数）** | `MapView.GroundKeyOf`：`grass1/dirt1/road1/townfloor1/cavefloor1/cavewall1/exit1`；`ObjectKeyOf`：`rock1/tree1/fence1/tent1/cavewall1` |
| **UI 参考分辨率 = 1920×1080** | 引擎 `UIManager` 把 `CanvasScaler.referenceResolution` 固定为 **1920×1080**（`Runtime/Presentation/UI.cs:52-56`）。UI 布局一律按 **1920×1080** 写（`UI/UiArt.cs` 已提供 `RefWidth/RefHeight`） |
| **`UI/UiArt.cs` 已存在** | 7 个流程面板共用的「铺满根 / 原版贴图背景 / 按钮 / 文本 / 输入框 / 锚点宽度进度条」工具，**只封装 `CloverEngine.UIFactory`**。新面板**先看它有没有现成的**，不要重复写 |
| **站点迁移日志唯一出口** | `Game.Fsm.OnChange` → `FlowLog.Station(to)` → `[Flow] → <站点>`。**验收 grep `[Flow] →`**。别在 `onEnter` 里各打一遍 |
| **HUD 打开方式（约定）** | Flow **不引用** `HudPanel`。约定 **HUD 监听 `Events.StageEntered` 打开、`Events.StageLeft` 关闭** |
| **离线快校验环路（重要）** | `client/client.slnx` 里**没有 `Diablo2.csproj`**（要等用户打开编辑器才生成）⇒ `dotnet build client.slnx` **覆盖不到本项目的业务代码**。已有两套可用宿主，**照抄它们的做法**：`.ai-tmp/hosts/mapcheck/`（agent-04）、`.ai-tmp/hosts/flowcheck/`（agent-05）—— 把 `Assets/Scripts/**` 源文件 + 引擎托管 DLL 编到 .NET 上跑断言。**你的自检必须用这种方式，不许"看起来 0 错误"就当通过** |

## 4. 分层与自检（硬指标）

- `App/Bootstrap.cs` **≤ 200 行**；
- `Module/X` 不许 `using` `Module/Y` 的具体类型（协作走 `Core/Events.cs` 事件或 App 注入接口）；
- `UI/**` 不许 `using Diablo2.Module.*`；
- 事件名一律来自 `Core/Events.cs`（**禁止裸字符串**）；资源路径一律来自 `Core/ResPaths.cs`。

**完成后自己跑这几条并把输出贴进回报**：

```powershell
# ① App 行数
(Get-Content "<项目根>\client\Assets\Scripts\App\*.cs" | Measure-Object -Line).Lines
# ② 跨模块耦合（应 0 命中）
Select-String -Path "<项目根>\client\Assets\Scripts\Module\*\*.cs" -Pattern "using Diablo2\.Module\." 
# ③ UI 引用业务模块（应 0 命中）
Select-String -Path "<项目根>\client\Assets\Scripts\UI\*.cs" -Pattern "using Diablo2\.Module"
# ④ 业务直连网络（应 0 命中）
Select-String -Path "<项目根>\client\Assets\Scripts\**\*.cs" -Pattern "Game\.Net|Game\.Sync|Game\.Http|CloverNet\.Init"
# ⑤ 裸事件名（应 0 命中）
Select-String -Path "<项目根>\client\Assets\Scripts\**\*.cs" -Pattern "Game\.Event\.(On|Emit|Off)\("""
```

> ⚠️ **③ 的原始命令有假阳性**：`-Pattern "using Diablo2\.Module"` 会命中**注释里写的这句禁令**。
> 请改用锚定版：`-Pattern "^\s*using\s+Diablo2\.Module"`，并把命中逐条看是不是真 `using`。

> **用户尚未打开 Unity 编辑器**（skill 闸门 2）⇒ **禁止**跑 `unity run` / `unity test` / `Unity.exe -batchmode`。
> `dotnet build client.slnx` **覆盖不到本项目业务代码**（没有 `Diablo2.csproj`）⇒
> **必须用 `.ai-tmp/hosts/mapcheck/`、`.ai-tmp/hosts/flowcheck/` 那套离线宿主**（把 `Assets/Scripts/**` + 引擎 DLL 编到 .NET 并跑断言），
> 或者自己照抄建一个 `tools/<你的模块>check/`。**"看起来 0 错误"不算自检通过。**

## 5. 回报格式（做完**一次性**回报，中途不播报）

```
产出物：<文件绝对路径清单>
自检：<实际执行的命令 + 原始输出（截取关键行）>
分层自检：App 行数=… / ②=…命中 / ③=…命中 / ④=…命中 / ⑤=…命中
未决：无 / <具体条目>
```

**没跑过自检命令、没读过自己产出文件的证据，就等于没做完。**
**接近轮次上限时不许硬撑到被掐断** —— 主动收尾，如实回报「已完成什么 / 没完成什么 / 下一棒从哪个文件接着做」。
