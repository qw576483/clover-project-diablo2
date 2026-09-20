# agent-31：引擎下沉 B1 —— 项目侧切换到引擎 `CloverEngine.Rng`（删掉自写件 + 恢复绿编译）

## 0. 技能（开工必做）

- 有 `use_skill` → `use_skill("clover-engine")`、`use_skill("unity-cli")`；没有就按序读文件（命中即用）：
  - 项目级：`<项目根>/tools/ai-skill/SKILL.md` → `conventions.md` / `registry.md` / `constraints.md`
  - 全局：`~/.codebuddy/skills/ai-skill/SKILL.md`（**规则层不可被项目级覆盖**）、`~/.codebuddy/skills/unity-cli/SKILL.md`
- **必读**：`clover-engine` §2（成本闸门：**能离线判的不许进 Play**）、§3（四拍，**编译失败 ⇒ 中止一切验证**）、§4（证据契约）。

## 1. 背景与目标（一句话）

A1 已把通用能力下沉到引擎：`clover-client-unity-engine/Runtime/Core/Rng.cs`（**纯新增**，与项目 `Core/Rng.cs` 语义逐条一致，71/71 离线断言 PASS）。
本项目侧仍留着自写的 `Diablo2.Core.Rng` ⇒ 两个类型**同名**，6 个项目文件同时 `using Diablo2.Core; using CloverEngine;` ⇒ **15 条编译错（CS0104/CS0535）**，项目当前处于**红编译**。

**本片目标**：删掉项目自写的 `Core/Rng.cs`、把用法收敛到引擎类型，**恢复绿编译**，并用离线断言证明「**地图/掉落的随机序列一字未变**」。

## 2. 任务边界

**只做**：
- 删 `<项目根>/client/Assets/Scripts/Core/Rng.cs`（含 `.meta`）
- 修由此产生的编译错（预期主要靠"删掉一边 ⇒ 歧义消失"，但要逐个确认）
- 调整 `.ai-tmp/hosts/**` 的链接/using（它们若链接了项目 `Core/Rng.cs`，改链引擎 `Runtime/Core/Rng.cs`）

**绝不做的**：
- ⛔ 不许改引擎（除 A1 已新增的两个文件；本片**一行引擎代码都不许改**）
- ⛔ 不许改 `tools/ai-skill/**`（项目 skill）、`策划/**`、`docs/**`、`tools/**`、任何全局 skill —— 需要同步登记的，写进回报由主 agent 改
- ⛔ 不许删 `Core/Log.cs`（`Log` 的降频切换是**另一片 B1b**，本片只切 `Rng`）
- ⛔ 不许读工作区里其它 `clover-project-*`；⛔ 不许再派子 agent；⛔ 不许写交接/进度类文档

## 3. 前置依赖（已就绪）

| 事实 | 出处 |
|---|---|
| 引擎新增 `CloverEngine.Rng`（18 个成员，语义与项目版逐条一致，内部仍是 `System.Random`） | `clover-client-unity-engine/Runtime/Core/Rng.cs`（A1 新增，未提交） |
| A1 的离线断言 | `clover-client-unity-engine/Tools~/core-assert/rng-throttle/` ⇒ `dotnet run` = **71/71 ALL PASS** |
| 当前红编译（15 条，全在项目侧） | `unity command recompile_status` ⇒ `failed=true / compilationFailed=true / errorCount=15`；点名文件：`Module/Combat/CombatModule.cs`、`Module/Combat/DeathFlow.cs`、`Module/Item/ItemModule.cs`、`Module/Skill/SkillModule.cs`、`Module/Map/MapModule.cs`、`Module/Monster/MonsterModule.cs` |
| 项目自写件的源 | `client/Assets/Scripts/Core/Rng.cs`（178 行，本片要删的就是它） |
| 离线宿主 | `.ai-tmp/hosts/run_all_hosts.ps1`（10 个宿主，**秒级**）；其中 `mapcheck` 有"同 seed ⇒ 同图"类断言 |

## 4. 产出物

- `<项目根>/client/Assets/Scripts/Core/Rng.cs` **已删除**（含 `.meta`）
- 项目与宿主**编译绿**
- 回报（消息）：产出物 + 自检原始输出 + 未决

## 5. 验收标准（逐条自查）

- [ ] **编译绿**：`unity command recompile --project-path client` ⇒ `recompile_status` = `completed / failed=false / errors=[]`
- [ ] 全项目 grep 确认**再无** `Diablo2.Core.Rng` 的自写定义（`Core/Rng.cs` 不存在），且**没有任何 `Rng` 相关的编译错**（贴 `recompile_status` 原文）
- [ ] **序列未变（本片最关键的判据）**：`.ai-tmp/hosts/run_all_hosts.ps1` = `TOTAL_HOSTS=10 FAILED=0`；并**单独**贴出 `mapcheck`（及其它涉及随机/掉落的宿主）里"同 seed ⇒ 同结果"那几行原始断言输出 —— 它们必须仍然 PASS（= 引擎版 `Rng` 与项目版产生**完全相同的序列**）
- [ ] `git -C c:\Work\Server\full-dev --no-pager diff --stat -- clover-client-unity-engine` 与 A1 结束时**逐字一致**（证明本片**没碰引擎**）
- [ ] 项目侧 diff 只包含：删除 `Core/Rng.cs`(+meta) + 必要的宿主链接/using 调整（逐条列在回报里，并说明每处为什么必要）
- [ ] `.ai-tmp/test/` 清空到只剩 `dispatch-log.tsv` / `play-log.tsv`；⛔ **本片不需要进 Play**（全部判据离线可判 ⇒ 按 §2 第 5 条记账：0 次）
- [ ] 跑一次只读的 `tools/verify.ps1`，把 summary 行贴进回报（注意：`freeze-before-capture` / `evidence-economy` 若报红，如实贴出并说明是否与本片有关）

## 6. 约束

- 批次流水线：① 只读取证（列出所有 `Rng` 引用点：项目源码 + 10 个宿主的链接项）→ ② 一次改完 → ③ **一次**编译 + 跑全部离线宿主 → ④ 集中回报。
- 语义约束：**只换实现位置，不许改行为**。⛔ 不许"顺手"把 `Rng` 的调用点改成别的东西；⛔ 不许改 seed 来源；⛔ 不许改 `System.Random` 之外的内核行为。
- `Log` 相关一律**不动**（B1b 再切）。
- 发现"引擎版 Rng 与项目版有语义差异"⇒ **立刻停下回报**（不许自己在项目侧打补丁绕开）。
- 回报格式：`产出物 / 自检（编译 + 宿主 + 序列未变证据 + diff）/ 未决`。
