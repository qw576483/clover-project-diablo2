# agent-04：Map 模块（格子地图 / 随机生成 / A* / 等距渲染）

## 0. 技能（开工必做）

先读 `<项目根>/docs/agents/_common.md` 并照它执行。项目根 = `c:\Work\Server\full-dev\clover-project-diablo2`。
另必读：`docs/步骤文档.md` §3.5/§3.6/§4、`tools/ai-skill/conventions.md`（**等距坐标**一节）、`constraints.md` #4 #5 #9 #10。

## 1. 目标

实现 `IMapModule`：三处区域（罗格营地 / 血腥荒野 / 邪恶洞穴）的**格子地图**、**随机生成**、可走查询、A* 寻路，
以及**等距渲染**（地面层 / 物件层 / 遮蔽层，深度排序）。

## 2. 任务边界

- **只做**：`client/Assets/Scripts/Module/Map/**`。
- **绝不做**：不碰 `Core/`（`Iso`/`AStar`/`Rng` 由 agent-01 提供，**直接复用，不许重写**）；
  不碰 `UI/`、`App/`、`Editor/`、其它 `Module/*`；不改契约；不改 `tools/ai-skill/`。
- **绝不自造地图二进制格式**：本项目地图 = **业务侧格子数据**（`TileKind[,]`），
  **不解析/不生成** CloverMap；`Game.Map`（引擎地图）**本项目不使用**（原因见 `conventions.md` §与全局 skill 的差异）。

## 3. 前置依赖（已就绪）

- `Core/Iso.cs`：`GridToWorld(Vector2Int)` / `WorldToGrid(Vector3)` / `SortOrder(Vector2Int)` / `ScreenToGrid(Camera, Vector3)`
- `Core/AStar.cs`：`Find(Func<Vector2Int,bool> walkable, Vector2Int from, Vector2Int to, int maxNodes)`
- `Core/Rng.cs`：注入式随机（seed 可复现）
- `Core/GameConst.cs`：`TileSize` / `IsoTilePxW=128` / `IsoTilePxH=64` / `PixelsPerUnit=64`
- `Core/ResPaths.cs`：`D2/Tiles/`、`D2/Objects/`
- `Module/Contracts.cs`：`IMapModule` / `TileKind` / `AreaId`
- 配表：`level_c`（区域：面积范围、怪物密度、是否随机）、`monster_c`

## 4. 产出物（`client/Assets/Scripts/Module/Map/`）

| 文件 | 内容 |
| --- | --- |
| `MapModule.cs` | `IMapModule` 实现（门面，`internal`），持有 `GridMap` + `MapView` |
| `GridMap.cs` | `TileKind[,] _tiles` + `Width/Height/Area/Seed/BlockedCount`；`Walkable` / `InBounds` / `TileAt` / `FindPath` / `RandomWalkableTile` |
| `MapGenTown.cs` | **罗格营地**：固定布局（栅栏环形边界、5 个 NPC 点、帐篷/篝火/木桩位置、出城口 `Exit` 格） |
| `MapGenWilderness.cs` | **血腥荒野**：程序化（泥土地面 + 随机岩石/树/尖刺栅栏 + 蜿蜒土路 + 通向洞穴的 `Exit` 格 + 洞穴入口坐标） |
| `MapGenCave.cs` | **邪恶洞穴**：房间-走廊算法（N 个矩形房 + L 型走廊连通 + 洞壁 `CaveWall`）；**返回怪物刷新点列表** |
| `MapView.cs` | 等距渲染：三层（`GroundLayer` / `ObjectLayer` / `OverlayLayer`）用 `SpriteRenderer`（`sortingOrder = Iso.SortOrder(gx,gy) + 层偏移`）；瓦片来自 `ResPaths.D2Tiles`，取不到时用**纯色占位 sprite**（并在 `资源欠缺清单.md` 登记）；支持 `ShowArea(AreaId)` / `SetFogOfWar(bool)`（已探索记录供小地图用） |
| `MapDebug.cs` | 自证：`DumpStats()` 打印区域名/尺寸/seed/障碍数/可走数/生成耗时；`DumpAscii()` 把可走性打成字符画（**用于日志取证**） |
| `MapLog` | 统一日志 tag = `Map` |

**关键实现要求**：

1. **随机生成必须可复现**：一切随机走注入的 `Core.Rng`；`Generate(area, seed)` 同 seed ⇒ 同地图（日志打印 seed）。
2. **连接性自检**：生成后**必须**做一次 BFS 校验「出生点 → Exit / 洞穴入口 / 所有房间」可达；
   不可达 ⇒ **重生成**（最多 N 次，超限用保底布局）并 `Log.Warn`。
3. **出生点净空**：出生格及其 8 邻必须可走（否则角色一出生就被卡死，见 skill `3d-mmo-basics` §0 的实测）。
4. **洞穴入口 / 出城口**是可走格 + 特殊 `TileKind.Exit`，并记录其格坐标供 Flow/Quest 查询。
5. **渲染不要每帧重建**：`ShowArea` 时一次性铺好；瓦片数超阈值时按可见区域分块（避免卡顿），至少保证 64×64 级地图流畅。
6. **占位渲染**：无素材时用 `SpriteRenderer` + 1×1 白 sprite + 颜色区分 `TileKind`（草地=绿、泥土=褐、岩石=灰、树=深绿、墙=深灰、地面=亮色……），**必须让"哪可走哪不可走"一眼可辨**（验收要用）。

## 5. 验收标准

- [ ] `dotnet build` 本模块零报错
- [ ] **三处区域各生成一次**，`DumpStats()` 输出（贴日志）：名称 / 尺寸 / seed / 障碍数 / 可走数
- [ ] **同 seed 两次生成结果一致**（贴两次 `DumpAscii()` 的哈希或前若干行比较）
- [ ] **连通性校验通过**（出生点可达 Exit / 洞穴入口 / 所有房间）；贴出校验日志
- [ ] `FindPath` 从出生点到 Exit 返回非空路径；贴出路径长度与首尾格
- [ ] 连续生成 5 次血腥荒野，**5 个 seed 互不相同且障碍数不同**（证明是真随机）
- [ ] 分层自检 ②③④⑤ 全 0 命中
- [ ] 回报里给出 **NPC 点 / Exit 点 / 洞穴入口点 / 怪物刷新点的确切 API**（后续 agent 要用）

## 6. 约束

- 所有非预期分支必须打日志（重生成、连通性失败、素材缺失、路径找不到…）。
- **禁止** `UnityEngine.Random`；**禁止** `GameObject.Find` / `FindObjectOfType`。
- 一个 `.cs` 一个类。
