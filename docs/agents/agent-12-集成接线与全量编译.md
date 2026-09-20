# agent-12：集成接线（App 层）+ 全量编译自检宿主

## 0. 技能（开工必做）

先读并照做：`<项目根>/docs/agents/_common.md`（**尤其 §3.5**）。
项目根 = `clover-project-diablo2`。

## 1. 目标

把第二/三批模块**真正接起来**，并给全工程建立**能编到全部业务代码**的离线宿主（现有 4 个宿主各自只编子集）。
这是**交付前最后一次接线**：之后只剩进 Play 实测。

## 2. 任务边界

- **允许改**：`client/Assets/Scripts/App/**`（`Bootstrap.cs` / `AppContext.cs`，**本 agent 接管**）、
  `client/Assets/Scripts/Core/Events.cs`（**仅允许"新增事件常量"**，不许删改已有名）、
  `client/Assets/Scripts/Module/Contracts.cs`（**仅允许"补注释/补缺失的极小型成员"**，任何签名变更必须先回报主 agent）。
- **绝不做**：不改 `Module/*` 的实现逻辑、不改 `UI/**` 的面板、不改 `Editor/**`、不改契约文档、不改 `tools/ai-skill/`。

## 3. 必须完成的接线清单（**逐条做完**）

| # | 接线项 | 依据 |
| --- | --- | --- |
| 1 | **`App/Bootstrap.cs` 调一次配表加载**：`string err = Table.TableLoader.LoadAll(Application.streamingAssetsPath, Application.dataPath);` 失败 `Game.Logger.Error("Table", err)`、成功 `Info` 打 `TableLoader.LastDir` | agent-02 报告（`docs/配表说明.md` §3）；**不许用 `CloverData.InitDataTable`** |
| 2 | **HUD 首次打开**：选**一种**（不要两种都做）——(a) 由 `App` 在 `Events.StageEntered` 时 `Game.UI.Open<HudPanel>(stats)`、`Events.StageLeft` 时 `Close<HudPanel>()`；或 (b) 在 `Stage` 场景预制里放。**选 (a)**（更可控，且 agent-10 不依赖此） | agent-09 未决 1 |
| 3 | **进图时各模块 emit 一次全量快照**（否则面板打开是空的）：`Player.Snapshot()` 等 → 对应事件（`HudDirty` / `InventoryChanged` / `SkillTreeChanged` / `QuestChanged` / `MapGenerated` / `PlayerGridChanged`） | agent-09 未决 3 |
| 4 | **地图/视图根节点接上**：`IMapModule` 与 `IViewModule` 的非契约入口 `AttachRoot(Transform)`，接 `Stage` 场景的 `MapRoot` / `EntityRoot`（**用 `GameObject.Find` 也不许**；改为在 `AppContext` 里暴露两个 `Transform` 字段由 `Bootstrap` 从序列化字段注入） | agent-04/07 报告 |
| 5 | **区域切换去重**：`Events.ExitEntered` 目前由 Flow 处理，`MapModule` 也可能发同类事件 ⇒ 保证**每次过门只重生成一次地图**（加断言日志） | agent-05 未决 8 |
| 6 | **进入 Stage 时刷怪**：`IMonsterModule.SpawnArea(area)` 的调用时机（城镇不刷怪、野外用 `RandomWalkableTile`、洞穴用 `MonsterSpawns`）；刷完 emit `MonsterSpawned` 全量 | agent-07/08 报告 |
| 7 | **新增缺失事件常量（只增不改）**：`D2.Item.UnequipRequest`（参数：`ItemSlot`+slotIndex 打包成 int 或新 DTO）、`D2.Player.Revived`、`D2.Item.MoveInInventoryRequest`、`D2.Npc.ShopOpenRequest` —— **UI 侧已在等它们**（见 agent-09 未决 4/5/8）。DTO 需求已在 `Diablo2.Def` 里，**不许改已有 DTO 字段**，只能新增 |
| 8 | **`level_c` 的 0 基/1 基口径**：统一走 `AreaLevelTable.Resolve(AreaId)`（agent-07 已提供），并在 `docs/步骤文档.md` 的口径里写清（**这是主 agent 的文件，你只回报结论，不要改**） | agent-07 未决 1 |
| 9 | **`DropLoot` 的 `int treasureClassId` 口径**：= `Tables.Default.Treasureclass.All()` 的 **1 基行序**；把这条写进代码注释并在回报里确认 | agent-07/08 未决 |
| 10 | **`App/*.cs` 单文件行数 ≤ 200**：`Bootstrap.cs` 当前 144 行（OK），但接线后会变长 —— 超了就**拆文件**（如 `App/AppWiring.cs`），**不许超** | `_common.md` §4 |

## 4. 产出物

### 4.1 全量编译宿主 `.ai-tmp/hosts/fullcheck/`

- `FullCheck.csproj`：把 **`client/Assets/Scripts/**` 全部 .cs** + 引擎托管 DLL 一起编译（照 `.ai-tmp/hosts/flowcheck/` 的 `EngineShim.cs` 做法；
  若 shim 覆盖不全，**优先补全 shim**，不许把文件排除出去——排除就等于没校验）；
- `Program.cs`：装配 `AppContext`（走真实 `AutoWire` 路径）→ 生成三处地图 → 刷怪 → 模拟一段战斗 →
  跑一次任务链 → 存档往返 → 打印全链路日志；
- **断言**：每个模块**都真的装配成功**（不为 null）、无异常、关键计数正确。

> 目标：**一条命令**能证明"全部业务代码能编译 + 能跑通核心链路"，供主 agent 与后续 agent 复用。

### 4.2 修改后的 `App/Bootstrap.cs` / `App/AppContext.cs`（+ 必要的拆分文件）

### 4.3 `<项目根>/docs/接线报告.md`

逐条对照 §3 的 10 项：做了什么 / 证据（日志或断言）/ 未做及原因。

## 5. 验收标准

- [ ] `dotnet build .ai-tmp/hosts/fullcheck/FullCheck.csproj` **0 错 0 警告**（这是全工程业务代码的编译证据）
- [ ] `dotnet run` 断言全过，且**所有模块非 null**（贴 `AppContext.Describe()` 输出）
- [ ] 三条链路在日志里可见：**配表已加载** / **地图已生成并刷怪** / **任务链可走通**
- [ ] `App/*.cs` **每个文件 ≤ 200 行**（逐文件贴行数）
- [ ] 回归：`tools/{mapcheck,flowcheck,playercheck,combatcheck,itemcheck,uicheck}` **六个宿主全部仍通过**（逐个跑并贴结果）
- [ ] 分层自检 ②③④⑤ 全 0 命中（③ 用锚定版；④ 的 `App/Bootstrap.cs:6` 注释假阳性要说明）
- [ ] 回报里给出：全量编译命令、接线 10 项的逐条状态、仍需主 agent 裁决的契约问题清单

## 6. 约束

- **不许改任何模块的实现逻辑**（发现 bug → 写进回报的"需返工"清单，由主 agent 决定是否激活原 agent）。
- 所有非预期分支必须打日志。用户未打开编辑器 ⇒ **禁止** `unity run/test/batchmode`。
