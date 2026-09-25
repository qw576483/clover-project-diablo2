# agent-33：引擎下沉 A2 —— `AStar`（运行时寻路）+ `Iso`（等距几何/排序）【引擎新增 + 项目切换，一片做完】

## 0. 技能（开工必做）

- 有 `use_skill` → `use_skill("clover-engine")`、`use_skill("unity-cli")`；没有就按序读文件：
  - 项目级：`<项目根>/tools/ai-skill/SKILL.md` → `conventions.md` / `registry.md`
  - 全局：`~/.codebuddy/skills/ai-skill/SKILL.md`（规则层不可被项目级覆盖）、`~/.codebuddy/skills/unity-cli/SKILL.md`
- **必读**：`clover-engine` 的 `patterns/engine-fix.md`、§2（成本闸门）、§3（四拍，编译失败 ⇒ 中止一切验证）。

## 1. 目标（一句话）

把项目 `Core/AStar.cs`（277 行）与 `Core/Iso.cs`（191 行）这两项**通用能力**下沉到引擎；**项目侧调用点零改动**（`AStar.*` / `Iso.*` 的公开静态签名一个都不变）。

## 2. 任务边界

**只做**：
- 引擎**新增**：`Runtime/Core/AStar.cs`、`Runtime/Core/IsoLayout.cs`、`Runtime/Core/Dir8.cs`
- 项目侧：**删** `client/Assets/Scripts/Core/AStar.cs`；**改** `client/Assets/Scripts/Core/Iso.cs` 为**薄门面**（静态 API 不变，内部委托引擎 `IsoLayout`）
- 必要时调整 `.ai-tmp/hosts/**` 的链接项与 using

**绝不做的**：
- ⛔ 不许改 `AStar.*` / `Iso.*` 的公开签名（调用点遍布 `Module/**`、`UI/**`、7 个宿主）
- ⛔ 不许改 `GameConst` 的语义（`IsoHalfW/IsoHalfH/SortOrderStep/SortOrderBase/LayerOffset*` 留在项目侧，作为门面的构造参数来源）
- ⛔ 不许改 `Def.Dir8`（项目枚举）—— 引擎自带自己的 `Dir8`，门面里做**枚举映射**
- ⛔ 不许改引擎任何**既有**文件（只允许新增 3 个文件）
- ⛔ 不许改 `tools/ai-skill/**`、`策划/**`、`docs/**`、任何全局 skill（需同步的写进回报）
- ⛔ 不许读其它 `clover-project-*`；⛔ 不许再派子 agent；⛔ 不许写交接/进度类文档

## 3. 契约（主 agent 已定，照这个形状做）

**① `CloverEngine.AStar`（照搬项目版 API 与语义；已确认它只依赖 `System/Collections.Generic/UnityEngine`）**
```csharp
namespace CloverEngine
{
    public static class AStar
    {
        public static List<Vector2Int> Find(Func<Vector2Int,bool> walkable, Vector2Int from, Vector2Int to, …);
        public static List<Vector2Int> FindSmoothed(Func<Vector2Int,bool> walkable, Vector2Int from, Vector2Int to, …);
        public static List<Vector2Int> Smooth(IReadOnlyList<Vector2Int> path, Func<Vector2Int,bool> walkable);
        public static bool HasLineOfSight(Func<Vector2Int,bool> walkable, Vector2Int a, Vector2Int b);
        public static string Describe(IReadOnlyList<Vector2Int> path);
    }
}
```
参数个数 / 默认值 / 返回值**逐条照搬**（唯二允许的差异：命名空间 + 内部日志出口从 `Log.*` 改为 `LogThrottle`/`Game.Logger`）。

**② `CloverEngine.Dir8`（引擎自带枚举）**：8 方向，取值语义与项目 `Def.Dir8` 一致（`N/NE/E/SE/S/SW/W/NW` 次序照抄）。

**③ `CloverEngine.IsoLayout`（实例类，承载几何/排序/方向算法）**
```csharp
namespace CloverEngine
{
    /// <summary>等距投影 + 排序参数（页面/项目各自给一组常量即可复用）。</summary>
    public sealed class IsoLayout
    {
        public IsoLayout(float halfW, float halfH, int sortOrderStep, int sortOrderBase);
        public float HalfW { get; }   public float HalfH { get; }
        public Vector3 GridToWorld(int gx, int gy);
        public Vector3 GridOriginToWorld(int gx, int gy);
        public Vector2Int WorldToGrid(Vector3 world);
        public Vector2 WorldToGridContinuous(Vector3 world);
        public Vector3 ScreenToWorldOnGround(Camera cam, Vector3 screenPos);
        public Vector2Int ScreenToGrid(Camera cam, Vector3 screenPos);
        public int SortOrder(int gx, int gy);
        public int SortOrder(int gx, int gy, int layerOffset);
        public int GridDistance(Vector2Int a, Vector2Int b);
        public float GridDistanceEuclidean(Vector2Int a, Vector2Int b);
        public float GridDistanceEuclidean(Vector2 a, Vector2 b);
        public bool IsAdjacent(Vector2Int a, Vector2Int b);
        public Dir8 DirectionTo(Vector2Int delta);
        public Dir8 DirectionTo(Vector2Int from, Vector2Int to);
    }
}
```
- 算法与边界处理**逐行照搬项目 `Iso`**（含"排序基准 = `(gx+gy)*step + base`"、`EqualityComparer`/取整口径、`ScreenToWorldOnGround` 的 z 平面口径）。
- ⚠️ **落地细节两处必须自查**：① 项目 `Iso` 里用到 `layerOffset` 的重载（`SortOrder(g, layerOffset)`）⇒ 门面侧把 `GameConst.LayerOffset*` 加在返回值上（引擎侧不引入层偏移常量）；② `Dir8` 映射：门面返回项目 `Def.Dir8`，引擎侧返回 `CloverEngine.Dir8`，**按同一顺序的枚举值对齐**（在门面里写明"两枚举必须同序"的断言式注释）。

**④ 项目侧**：
- `Core/AStar.cs` **删除**；调用点靠 `using CloverEngine;` 解析（B1 时多数文件已加；缺的补上）。
- `Core/Iso.cs` 改为**薄门面**（API 一字不改）：
```csharp
public static class Iso
{
    public const float HalfW = GameConst.IsoHalfW;      // 保留（对外可见）
    public const float HalfH = GameConst.IsoHalfH;
    private static readonly IsoLayout Layout = new IsoLayout(GameConst.IsoHalfW, GameConst.IsoHalfH,
                                                             GameConst.SortOrderStep, GameConst.SortOrderBase);
    public static Vector3 GridToWorld(Vector2Int g) => Layout.GridToWorld(g.x, g.y);
    // …其余方法逐个转发；带 layerOffset 的重载在此叠加 GameConst.LayerOffset*
}
```

## 4. 产出物

- 引擎新增 3 个文件（+ 各自的 `.meta`，Unity 自动生成）
- 项目：`Core/AStar.cs` 删除、`Core/Iso.cs` 改门面、必要的 using/宿主链接调整
- 回报（消息）：产出物 + 自检原始输出 + 未决

## 5. 验收标准（逐条自查）

- [ ] 编译绿：`unity command recompile_status` ⇒ `completed / failed=false / errors=[]`
- [ ] **公开签名零变化**：`AStar` 5 个成员、`Iso` 的全部公开成员，改前/改后**字符级对照 0 差异**（把两串贴进回报）
- [ ] `tools/probes/hosts/run_all_hosts.ps1` ⇒ `TOTAL_HOSTS=10 FAILED=0`
- [ ] **行为等价（本片最关键）**：`mapcheck` / `playercheck` / `combatcheck` / `flowcheck` 等涉及**寻路、等距投影、排序**的宿主输出，改前/改后**逐行比对 0 差异**（时钟/耗时类行按 B1b 的做法剔除，并**同时跑一次"同构建重跑"作抖动基线**）；地图转储 `_maps/*.txt` 逐行 0 差异
- [ ] `git diff --stat -- clover-client-unity-engine` 只多出**新增**（无既有文件修改行）
- [ ] `.ai-tmp/test/` 清空到只剩 `dispatch-log.tsv` / `play-log.tsv`；**本片 0 次 Play**（全部离线可判）
- [ ] 跑一次只读 `tools/verify.ps1`，把 summary 行贴进回报（如实贴红项；⛔ 不许自己写 `# adjudicated:` 裁决行 —— 那属主 agent 权限）

## 6. 约束

- 批次流水线：① 只读取证（`AStar`/`Iso` 全文 + 所有调用点 + 宿主链接项）→ ② 一次改完（引擎 3 新文件 + 项目删/改）→ ③ 一次编译 + 全部宿主 + 逐行比对 → ④ 集中回报。
- ⛔ **本片不做"注释同步"以外的任何额外改动**（注释里若提到 `Core/AStar.cs`/`Core/Iso.cs` 请一并更新为引擎路径）。
- 回报格式：`产出物 / 自检（签名对照 + 编译 + 宿主 + 逐行比对 + diff）/ 未决`。
