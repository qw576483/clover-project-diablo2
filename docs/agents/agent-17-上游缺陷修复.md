# agent-17：两条**上游缺陷**（两个任务包，按 section 分派）

> 来源：agent-16 修 `MissingReferenceException` 时，Play 取证**顺带抓到的真实触发路径与观察项**。
> 通用前置按 `docs/agents/_common.md`（**尤其 §3.5**）；改完必须 `tools/probes/hosts/run_all_hosts.ps1` 10 宿主全绿。

---

## §A 交给 `agent-05`：**进图可重入 ⇒ Stage 场景被重载、视图悬空**（★）

**证据（agent-16 的 Play 复现）**：**已经在 Stage 时再次发进图请求**
⇒ `Game.Scene.Load("Stage")` **重载了 Stage 场景**、销毁全部视图节点；
而 `AppFlow.OnEnterStage` 因 `_stageActive == true` **提前 return**（既不重建也不清场）
⇒ 视图引用永久悬空（agent-16 已在视图侧兜住不崩，但**根因还在 Flow**）。

**要修**（`client/Assets/Scripts/Module/Flow/**`、`client/Assets/Scripts/App/**`）：

1. **`GoStage(AreaId)` 加重入守卫**：已在 Stage 且请求的是同一区域 ⇒ **直接忽略**（打一条可检索 Info，不重载场景）；
   若请求的是**不同区域** ⇒ 走"先清场再进图"的正规路径（清场清单 7 项照 `app-flow.md` §5），**绝不允许**在不清场的情况下重载场景。
2. **`OnEnterStage` 的 `_stageActive` 提前返回**：返回前必须确认"当前 Stage 与实际状态一致"；
   若发现 `Game.Scene` 已被重载/视图已失效 ⇒ **补一次清场 + 重建**（而不是静默返回）。
3. 加一条可检索日志：`[Flow] 进图请求被忽略（已在 Stage 同一区域）` / `[Flow] 检测到 Stage 场景已被重载 ⇒ 补清场重建`。
4. **回归断言**（`tools/probes/hosts/flowcheck`）：① 已在 Stage 时再发同区进图 ⇒ `Scene.Load` **调用次数不增加**；
   ② 不同区进图 ⇒ 清场 7 项各执行一次再加载。

**验收**：
- [ ] 离线断言（两条）+ 10 宿主全绿
- [ ] Play 实测：进 Stage → **再发一次同区进图** → 无场景重载、无悬空、`console_status` 的 error = 0
      （本工程的输入注入路径里已有现成的"注入重载"路径，可改造复用）

---

## §B 交给 `agent-04`：**NPC 站位时序 ⇒ 5 个 NPC 全落在 (0,0)**

**证据（两局 Play 都出现）**：
```
[Warn] [Npc] NPC「阿卡拉」的站位缺失（`IMapModule.NpcPoints` 长度 0 < 1）⇒ 暂用 (0,0)   ×5
```
后来又有一条 `[Npc] NPC 站位已按地图装配（seed=… 区域=Town）：5 个` ——说明是**时序问题**：
`NpcModule` 在自己的 `Tick` 里 `EnsureBuilt()` 时地图**还没生成**，于是 5 个 NPC 都退到 (0,0)。

**要修**（`client/Assets/Scripts/Module/Map/**` 与/或 `Module/Npc/**`；**NPC 站位缺失的根因在 Map 侧要先查清**）：

1. 先确认 `IMapModule.NpcPoints` 在**城镇地图生成后**是否真的有 5 个点（agent-04 上一轮报过有：
   `[0]Akara=(13,8) [1]Kashya=(8,11) [2]Charsi=(22,8) [3]Gheed=(24,17) [4]Warriv=(17,4)`）。
2. **修时序**：`NpcPoints` 为空时**不要退回 (0,0)**（那是"看起来正常但完全错"的静默降级）；
   改为：**延迟构建**（等地图就绪后重建），或**订阅地图生成事件**后重建。**必须留一条 Warn 说明为什么没就绪**。
3. **加 `IMapModule` 的地图就绪查询**（若非契约入口已存在就直接用；新增只加不改，并在回报里说明）。
4. **回归断言**（`tools/probes/hosts/mapcheck` 或 `tools/probes/hosts/fullcheck`）：城镇生成后 `NpcPoints.Count == 5`、
   且**每两个点不在同一格、都在可走格上**；`NpcModule` 在"地图未就绪 → 就绪"后 NPC 坐标 = `NpcPoints`（不再是 (0,0)）。

**验收**：
- [ ] 离线断言 + 10 宿主全绿
- [ ] Play 实测：进城镇，**Console 里不再出现「站位缺失 ⇒ 暂用 (0,0)」**，
      且探针打印 5 个 NPC 的世界坐标 ≠ (0,0)（贴出来）

---

## 共用

- ⛔ 不许读工作区里其它 `clover-project-*`；不许改 `UI/**`（那是 agent-15 的地盘）。
- 驱动编辑器前先跑 `tools/probes/interact/p_runbg.cs`（失焦不 tick）；截图前等 ≥6 秒。
- 回报里贴**原始日志/断言输出**，不要只说"修好了"。
