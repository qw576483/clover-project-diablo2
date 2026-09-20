# agent-28：引擎 `Timer` 的 id 0 墓碑缺陷修复 + 无前置 timer 的新会话回归（收尾轮 D）

## 0. 技能（开工必做）

- 有 `use_skill` → `use_skill("clover-engine")`、`use_skill("unity-cli")`；没有就按序读文件（命中即用）：
  - 项目级（首选）：`<项目根>/tools/ai-skill/SKILL.md` → 按需 `constraints.md` / `conventions.md` / `registry.md`
  - 全局：`~/.codebuddy/skills/ai-skill/SKILL.md`（**规则层 §0~§7 不可被项目级覆盖**）、`~/.codebuddy/skills/unity-cli/SKILL.md`
  - 兜底：`<仓库根>/clover-tools/ai-skill/SKILL.md`
- **必读**：`clover-engine` 的 `patterns/engine-fix.md`（**最小复现 → 最小修复（不动公开签名）→ 修复前失败/修复后通过 → 记 E 编号**）、§1.13（四拍 / 编译必须先成功 / 采样器先自检）、§2（证据按类别）、`reference/visual-loop.md` 第八节（联络图）。
- **项目内必读**：`docs/agents/_common.md`；上一棒的任务书 `docs/agents/agent-27-新会话端到端联调与E-build-03实机验证.md`（本片是它的续，**它的探针已删**，你要重建）。

## 1. 目标（一句话）

修掉引擎 `Timer` 的 **id 0 墓碑缺陷**（发布级：正式包启动后**第一次** `Game.Scene.Load` 必死 ⇒ 主菜单永远打不开），并在**不带任何前置 timer** 的全新 Play 会话里回归"Boot → 主菜单 → 单人 → 创角 → 读条 → 进图 → 暂停 → 回主菜单"，重采本项目的流程联络图。

## 2. 缺陷（上一棒实测 + 主 agent 已核对的代码事实）

**代码事实**（`clover-client-unity-engine/Runtime/Core/Timer.cs`）：

| 位置 | 事实 |
|---|---|
| `:117` | `private long _nextId;` ⇒ **会话里第一个 timer 的 id = 0** |
| `:397` | `var id = _nextId++;` |
| `:225-228` | `public void Stop(long id) { _toRemove.Add(id); }` ⇒ **无条件**把 id 加进墓碑集合 |
| `:291` | Tick 遍历时 **先查墓碑**：`if (_toRemove.Contains(e.Id)) { RemoveEntryAt(i); continue; }`（在累加时间**之前**） |

**死亡链**：`SceneModule.Load`（`Runtime/Presentation/Scene.cs:55`）无条件 `Game.Timer?.Stop(_progressTimerId)`，而该字段初值 / 重置值就是 **0**（`Scene.cs:16`、`:65`）
⇒ `Stop(0)` 把 **0** 塞进墓碑 ⇒ 紧接着新建的进度轮询 timer 正好拿到 **id 0** ⇒ 它**在第一次 Tick 就被墓碑删掉、一次都没轮询**
⇒ `progress >= 0.9 → allowSceneActivation = true` 那句永不执行 ⇒ 场景永不激活（`progress` 停在 **0.9**、`allowSceneActivation` 恒 false、`sceneCount` 里留下 `isLoaded=false rootCount=0` 的僵尸场景）。

**实测（上一棒，新 Play 会话，24 次采样）**：
```
STATE n=1..24 wall=0.02→5.78 dFrames=347 dt≈0.0165 dtUnscaled≈0.0165
  fsm=MainMenu scene=(null) sceneCount=2 scenes=[Boot[L=1,roots=1] Menu[L=0,roots=0]] isOpenMainMenu=False
DIAG_SCENE progressScene=Menu progress=0.9 ge09=1 isDone=0 allowActivate=0
DIAG_TIMER nextId=6 entries=0[]        ← 会话 6 个 timer 全不在，其中 1 个是 Scene 的进度轮询
```
已排除失焦节流（`inBg=1 / vSync=0 / targetFps=60`，采样期 347 帧 / 5.78 s ≈ 60 fps）。

**A/B 对照（同一份代码）**：驱动前若跑过白名单配方 `client/_dev/p_runbg.cs`（它注册 1 s 心跳 timer ⇒ **吃掉 id 0**、自己成为墓碑牺牲品），Scene 的进度 timer 拿到 id ≥ 1 而存活 ⇒ 两次 Menu 加载 **102 ms / 98 ms** 完成、Stage 加载"真进度 0.9 → 世界就绪"正常推进。
⇒ **每个 Play 会话的第一条 `Game.Scene.Load` 是否成功，取决于此前有没有人建过 timer。正式包（无任何前置 timer）里就是"启动 → 主菜单永远打不开"。**

## 3. 任务边界

**只做**：
- 改**一个文件**：`clover-client-unity-engine\Runtime\Core\Timer.cs`
- 在 `<项目根>/.ai-tmp/test/` 写一次性探针 / 驱动脚本做回归与采证（**回报前删掉**）
- 覆盖重采：`client/Assets/Screenshots/p27_contact_flow.png`、`p27_contact_flow.index.tsv`、`p27_<状态>.png`（10 张）

**绝不做的**：
- ⛔ 不许改任何公开签名（`ITimer` 的成员与语义不动）；⛔ 不许改第二个引擎文件
- ⛔ 不许改 `client/Assets/Scripts/**`（本项目业务代码）
- ⛔ 不许改 `策划/验收表.md` / `docs/**` / `tools/**` / 任何 skill（E 编号登记由主 agent 写）
- ⛔ 不许删改 `client/_dev/p_runbg.cs` / `p_key3.cs`（白名单）
- ⛔ 不许读工作区里其它 `clover-project-*`；不许再派子 agent；不许写交接/进度类文档

## 4. 契约（最小修复的形状）

1. **`_nextId` 从 1 起发号**：`private long _nextId;` → `private long _nextId = 1;`
   —— 让 **0 成为"无效 / 未持有"哨兵**（引擎自己就是这么用的：`SceneModule._progressTimerId` 初值与重置值都是 0；`Game.Timer?.Every…() ?? 0` 也用 0 表示"没拿到"）。这是**系统性**修法：所有"拿 0 当没有"的调用方一起被治好。
2. **`Stop` 加防御**：`public void Stop(long id)` 首行 `if (id <= 0) return;`
   —— 把 0 哨兵挡在墓碑集合之外（`StopNamed` / `StopScope` / `StopAll` 传的是 entry 自己的 `Id`，必 > 0，不需要改）。
3. **注释写代码事实**（不许写"以后可以…"）：在 `_nextId` 与 `Stop` 处写清"id 从 1 开始发放、0 = 无效"以及"为什么 0 不能进墓碑"（贴本任务书 §2 的死亡链，一到两行讲清）。
4. **自查同类**：全仓 grep 一下"把 timer id 的初值 / 重置值写成 0、再无条件 `Stop(0)`"的调用方（本项目 `Scene.cs` 是其一；引擎里再自查一遍），把结论写进回报（**不要顺手改别的文件** —— 若发现别的文件也有同类问题，报给主 agent）。
5. **自查副作用**：确认没有代码拿 timer id 当数组下标 / 依赖"id 从 0 连续"（`_entries` 是 List、`_namedTimers` / `_scopedTimers` 是 `List<long>`、`_toRemove` 是 `HashSet<long>` ⇒ 预期无依赖，但要给出 grep 结论）。

## 5. 产出物

- `clover-client-unity-engine/Runtime/Core/Timer.cs`（**唯一被改的引擎文件**）
- 覆盖重采的证据：`client/Assets/Screenshots/p27_contact_flow.png` + `p27_contact_flow.index.tsv` + `p27_<状态>.png`（10 张）
- 回报（消息）：改动前后逐行对照 + 修复前/后对照证据 + 未决

## 6. 验收标准（逐条自查）

**A. 修复本身**
- [ ] `Timer.cs` 是唯一被改动的文件；`git -C c:\Work\Server\full-dev --no-pager diff --numstat -- clover-client-unity-engine/Runtime/Core/Timer.cs` 净增删 ≤ 15 行
- [ ] `unity command recompile --project-path client` → `recompile_status = completed / failed=false / errors=[]`
- [ ] 回报给出「修复前 → 修复后」的行为对照：会话第一个 timer（id=1，不再被 0 墓碑误删）/ `Stop(0)` 变成 no-op / `StopNamed`·`StopScope`·`StopAll` 行为不变 / 正常 timer 的停止语义不变

**B. 无前置 timer 的回归（**本片的重点：这条路径才是正式包路径**）**
- [ ] **全新会话 + 不跑 `p_runbg.cs`**：`editor_stop → clear_console → editor_play`；`runInBackground` 与 `vSyncCount` **在探针里自己设**（⛔ 不要用 `p_runbg.cs` —— 它的心跳 timer 会吃掉 id 0、正好掩盖本缺陷）
- [ ] 采样证明：会话里 Scene 的进度轮询 timer 的 **id = 1 且存活**（给出 `DIAG_TIMER nextId=… entries=…` 一类原始行）；`Loading scene: Menu` → `菜单场景 Menu 已就绪` 在 **< 1 s** 内完成；`sceneCount` 不出现 `isLoaded=false rootCount=0` 的僵尸场景
- [ ] 整链走通（生产入口，**零绕行**）：`Boot → MainMenu → SINGLE PLAYER → CharSelect → CharCreate（点 Amazon，`fw` 54 帧过渡播完）→ 输名字 → OK → Loading（读条真进度）→ Stage（HUD 开）→ ESC 暂停（timeScale=0）→ MAIN MENU → MainMenu`
- [ ] **连续两次**全新会话同结论
- [ ] `consoleErrors = 0`（读之前 `editor_stop → clear_console → editor_play → 跑链`）
- [ ] 联络图重采：`p27_contact_flow.png` mtime **晚于** `Timer.cs` 的 mtime；脚本按格判定全 PASS；**你只读这一张汇总图**（读不到内容 ⇒ `BLOCKED：图像通道不可用`）
- [ ] 上一棒的 `p27_*` 单张图（10 张）一并覆盖重采（否则它们早于本次代码改动 = 证据作废）

**C. 收尾**
- [ ] `.ai-tmp/test/` 只留 `dispatch-log.tsv`（探针 / 采样 / marker / 截图中间产物全删）
- [ ] 跑一次只读的 `tools/verify.ps1`，把 summary 行与逐行输出贴进回报

## 7. 约束

- 批次流水线（§1.13）：① 只读取证 → ② 一次改完（Timer.cs 2 处 + 注释）→ ③ 一次编译 + 预演 → ④ 一轮链条跑完、集中采一次联络图。⛔ 不许"改一处编译一次 / 改一处跑一次 Play"。
- 采样器先自检（§1.13 第 5 条）：`.ps1` 语法 0 错、**纯 ASCII**；完成判据 = **驱动自己写的 marker**（跑前删除）；读日志 `FileShare.ReadWrite`；探针日志统一 `[P28]` 前缀。
- 每条 `unity` 命令带 `--project-path client`；驱动前设 `runInBackground = true` / `vSyncCount = 0`（在探针里设，**不要跑 `p_runbg.cs`**）。
- 发现**其它**文件也有缺陷 ⇒ 写进回报，⛔ 不许顺手改。
- 回报格式：`产出物 / 自检（原始输出）/ 未决`。
