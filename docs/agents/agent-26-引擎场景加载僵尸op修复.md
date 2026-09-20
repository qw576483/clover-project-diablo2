# agent-26：引擎 `SceneModule.Load` 僵尸 AsyncOperation 修复（收尾轮 B）

## 0. 技能（开工必做）

- 有 `use_skill` → `use_skill("clover-engine")`、`use_skill("unity-cli")`；没有就按序读文件（命中即用）：
  - 项目级（首选）：`<项目根>/tools/ai-skill/SKILL.md` → 按需 `constraints.md`（看 `E-build-01/02` 那条表的写法）
  - 全局：`~/.codebuddy/skills/ai-skill/SKILL.md`（**规则层 §0~§7 不可被项目级覆盖**、§1.10 层级）、`~/.codebuddy/skills/unity-cli/SKILL.md`
  - 兜底：`<仓库根>/clover-tools/ai-skill/SKILL.md`
- **必读**：`clover-engine` 的 `patterns/engine-fix.md`（改引擎 SOP：定位 → **最小修复** → 回归用例 → 记 E 编号）、§1.13（四拍）、§1.11（收尾自检）、§7（日志硬约束）。

## 1. 目标（一句话）

修掉引擎 `SceneModule.Load` 的 **AsyncOperation 泄漏**：被"后一次 Load"顶掉的旧 op 会永久停在 `allowSceneActivation = false` ⇒ 永不激活 ⇒ **僵尸场景**（`isLoaded=false` / `rootCount=0`）累积；实测后果是编辑器内后续 `LoadSceneAsync` 的 `progress` **恒 0.000 / isDone=False**，`AppFlow.EnsureMenuScene` 的 `onReady` 永不触发、主菜单/创角屏一个都不开。

## 2. 任务边界

**只做**：
- 改**一个文件**：`c:\Work\Server\full-dev\clover-client-unity-engine\Runtime\Presentation\Scene.cs`（`SceneModule.Load`，:22-79）
- 改完自己跑 `unity command recompile --project-path <项目根>/client` 验证编译通过

**绝不做的**：
- ⛔ 不许改 `ISceneManager` / `Game` 门面 / 任何契约签名
- ⛔ 不许改第二个引擎文件；⛔ 不许改 `<项目根>/client/Assets/Scripts/**`（项目业务代码）
- ⛔ 不许改任何 skill（项目级 `tools/ai-skill/**`、仓库源 `clover-tools/ai-skill/**`、宿主安装副本全算）—— E 编号登记由**主 agent** 写
- ⛔ 不许读工作区里其它 `clover-project-*`；不许再派子 agent
- ⛔ 不许写 `docs/交接-*.md` / 进度类文档

## 3. 现状与缺陷（主 agent 已核实的代码事实）

`clover-client-unity-engine/Runtime/Presentation/Scene.cs`：

- `:37` `op.allowSceneActivation = false;`
- `:42` `Game.Timer?.Stop(_progressTimerId);` ← **每次 Load 都停掉上一次的进度轮询定时器**
- `:43-78` 新 op 用 `EveryUnscaled(0.05f, …)` 轮询；只有 `op.progress >= 0.9f` 才 `allowSceneActivation = true`（:51）
- `:41` 的注释把泄漏归因于"同名 `EveryName` 会先停上一个同名定时器" —— 改法（唯一 id + 手动 `Stop`）**只换掉了"谁停"，没有解决"被停掉的那个 op 永远不会激活"**：旧 op 既没人再轮询、`allowSceneActivation` 又是 `false` ⇒ 永久僵尸。

现场证据（执行者 agent-25 实测，本机同一编辑器进程 pid 80760）：
`[Scene] Loading scene: Menu` 之后永不完成；连续 10 次采样 `LoadSceneAsync("Menu").progress = 0.000 / isDone=False`（`allowSceneActivation=true` 也推不动）；`SceneManager.sceneCount = 4`（Boot + 3 个 Menu 全 `isLoaded=False rootCount=0`）；同时 `Timer.EveryUnscaled` 正常、`dt≈0.017`、`isCompiling=False`。

## 4. 契约（最小修复的形状，按这个改，别自由发挥）

1. **新增一个字段**记住"这一次的 op"（例如 `private AsyncOperation _progressOp;`），与 `_progressTimerId` 同生命周期。
2. 在**停旧定时器之前**（即 `:42` 之前），若上一个 op 仍 `!isDone`：**先放行**（`allowSceneActivation = true`），再停它的定时器。
   - 语义：旧加载**不再被等待**，但不能让它烂在"永不激活"上；放行后 Unity 会把它激活完（`completed` 仍会触发，其中的 `_currentScene`/`CleanupSceneResources` 分支照旧）。**别**去 `Stop` 它的 `completed`、**别**去改它的 `onDone`（本次不承诺旧回调语义 —— 但必须在回报里写明你的选择与理由）。
   - 这是**非预期分支** ⇒ 必须按 §7 打 Warn，带可定位信息：旧场景名 / 新场景名 / 旧 op 的 progress / 为什么要放行。
3. 新 op 赋值：轮询回调与 `completed` 里对新 `_progressOp` 的读法要与 `_progressTimerId` 一致（自己与自己比，别让旧 op 的 `completed` 把新 op 的字段清掉 —— `completed` 里先判 `_progressOp == op` 再置 null）。
4. **正常路径一个字都不许变**：`progress>=0.9` → stop timer → `allowSceneActivation=true` → `completed` → `_currentScene` 赋值 → 同场景不清理的分支 → `Dispatcher.Post` → handlers → `onDone`。
5. `op == null`（场景名不存在）分支保持"`onDone` 必达"（:29-35）。
6. 顺手把那几条**已经不成立**的注释改对（`:39-41` 说"同名 EveryName 会先停上一个同名定时器"——现在不是这个原因了）：注释要写成**代码事实**，不许留会误导下一棒的旧说法。⛔ 不许把注释写成"以后可以怎样"。

## 5. 产出物

- `c:\Work\Server\full-dev\clover-client-unity-engine\Runtime\Presentation\Scene.cs`（唯一被改的文件）
- 回报（消息）：改动前后逐行对照 + 编译证据 + 未决

## 6. 验收标准（逐条自查）

- [ ] `Scene.cs` 是**唯一**被改动的文件（用 `Get-ChildItem … | Where-Object LastWriteTime -gt <开工时间>` 自查并列出来）
- [ ] `unity command recompile --project-path c:\Work\Server\full-dev\clover-project-diablo2\client` = `completed`/`up_to_date`，且 `unity command console_status` 里 `compilationFailed=false`、无新增 C# 错误
- [ ] `git -C c:\Work\Server\full-dev --no-pager diff -- clover-client-unity-engine/Runtime/Presentation/Scene.cs` 的 diff **净增删 ≤ 25 行**（超了就是改大了，回到最小修）
- [ ] 回报里给出「改动前 → 改动后」的行为对照表：正常路径 / 被顶掉的旧 op / 同场景重载 / 场景名不存在，四条各写一句
- [ ] 非预期分支有 Warn 日志（贴日志格式的实际字符串）
- [ ] ⛔ 不许把"实机闭环验证"写进本片：当前编辑器进程的场景加载已瘫痪，修复的**实机回归由下一棒（重启后）做** —— 本片只到"编译通过 + 代码事实自洽"

## 7. 约束

- 批次流水线（§1.13）：① 只读取证（读 `Scene.cs` + `patterns/engine-fix.md` + 相关调用方）→ ② 一次改完 → ③ 一次编译 + 预演 → ④ 集中出证据。⛔ 不许改一处编译一次。
- 日志用 `Game.Logger?.Warn("Scene", …)`（引擎侧写法，跟 `Scene.cs` 现有风格一致）；⛔ 不许用 `Debug.Log`。
- 一切一次性产物只许放 `<项目根>/.ai-tmp/test/`（用完删，回报前清空，只留 `dispatch-log.tsv`）。
- 每条 `unity` 命令带 `--project-path`。
- 回报格式：`产出物 / 自检（原始输出）/ 未决`。
