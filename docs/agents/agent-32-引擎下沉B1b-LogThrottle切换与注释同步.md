# agent-32：引擎下沉 B1b —— 项目 `Log` 的降频/时钟切到引擎 `LogThrottle` + 注释同步

## 0. 技能（开工必做）

- 有 `use_skill` → `use_skill("clover-engine")`、`use_skill("unity-cli")`；没有就按序读文件：
  - 项目级：`<项目根>/tools/ai-skill/SKILL.md` → `conventions.md` / `registry.md` / `constraints.md`
  - 全局：`~/.codebuddy/skills/ai-skill/SKILL.md`（规则层不可被项目级覆盖）、`~/.codebuddy/skills/unity-cli/SKILL.md`
- **必读**：`clover-engine` §2（成本闸门：能离线判的不进 Play）、§3（四拍；编译失败 ⇒ 中止一切验证）。

## 1. 目标（一句话）

把项目 `client/Assets/Scripts/Core/Log.cs` 里**自己实现的"限频 + 时钟三级降级"**改为**委托给引擎 `CloverEngine.LogThrottle`**（A1 已下沉，71/71 断言 PASS），**项目侧 `Log.*` 的公开签名一个都不变**（调用点零改动）；顺手把源码里指向已删除文件 `Core/Rng.cs` 的注释/打印文案同步到 `CloverEngine.Rng`。

## 2. 任务边界

**只做**：
- 改 `client/Assets/Scripts/Core/Log.cs`：删掉内部限频表与时钟实现，改为转发 `LogThrottle`（`ShouldLog` / `WarnThrottled` / `ErrorThrottled` / `WarnOnce` / `ErrorOnce` / `Reset` / `Clock` / `ClockSource`）
- 改 8 处**注释** + 1 处**打印文案**里的 `Core.Rng` → `CloverEngine.Rng`（清单见 §3）
- 必要时调整 `.ai-tmp/hosts/**` 的链接项（若因此需要）
- `corecheck` 那条脆弱断言的**口径收紧**（见 §3 第 3 条）

**绝不做的**：
- ⛔ **不许改 `Log.*` 的公开签名**（`Info/Warn/Error/Debug/WarnThrottled/ErrorThrottled/WarnOnce/ErrorOnce/ShouldLog/ResetThrottle/Clock/ClockSource/Suppress/IsKnownTag` 全部保持）—— 有 13 个模块 + 7 个宿主在用它
- ⛔ 不许改引擎任何一行（`LogThrottle` 若发现缺陷 ⇒ 停下回报）
- ⛔ 不许改 `tools/ai-skill/**`（项目 skill；需要同步的写进回报，主 agent 已登记 Rng 两处，Log 部分由主 agent 登记）、`策划/**`、`docs/**`、任何全局 skill
- ⛔ 不许读工作区里其它 `clover-project-*`；⛔ 不许再派子 agent；⛔ 不许写交接/进度类文档

## 3. 契约与清单

**① `Log` 门面保留、只委托降频与时钟**（关键：**tag 白名单/规范化留在项目侧**，⛔ 不许一起搬走）：

```csharp
// 项目 Log.cs 保留：KnownTags / IsKnownTag / Normalize / Suppress / Info / Warn / Error / Debug
// 改为委托（示例，签名一字不改）：
public static bool ShouldLog(string key, float intervalSeconds = 5f) => LogThrottle.ShouldLog(key, intervalSeconds);
public static bool WarnThrottled(string tag, string key, string msg, float intervalSeconds = 5f)
{ if (!LogThrottle.ShouldLog(key, intervalSeconds)) return false; Warn(tag, msg); return true; }   // Warn() 里做 Normalize
public static bool ErrorThrottled(string tag, string key, string msg, float intervalSeconds = 5f) { … }
public static bool WarnOnce(string tag, string key, string msg)  { … ShouldLog(key, +∞) … }
public static bool ErrorOnce(string tag, string key, string msg) { … }
public static void ResetThrottle() => LogThrottle.Reset();
public static Func<float> Clock { get => LogThrottle.Clock; set => LogThrottle.Clock = value; }   // 项目 API 不变
public static string ClockSource => LogThrottle.ClockSource;
```
- ⛔ **必须保住**：`WarnThrottled/ErrorThrottled/WarnOnce/ErrorOnce` 在真正输出前**先** `Normalize(tag)`（否则验收脚本按 `[tag]` 检索会失效）。
- ⛔ **必须保住**：`Suppress=true` 时 `ShouldLog` 返回 false 且不产生任何日志。
- 删掉的内部实现（限频表 / `Now()` / 三级时钟 / `_unityClockUnavailable` / `_processClock` / `CreateProcessClock`）**语义由 `LogThrottle` 承接**；若项目侧还有引用 ⇒ 改为转发。

**② 注释/文案同步清单**（`Core.Rng` → `CloverEngine.Rng`）：
`Module/Combat/CombatModule.cs:17`、`Module/Combat/DamageFormula.cs:75`、`Module/Combat/DamagePipeline.cs:25`、`Module/Map/MapGenWilderness.cs:29`、`Module/Monster/MonsterModule.cs:20`、`Module/Skill/SkillModule.cs:22`、`Module/Map/MapModule.cs:13`、`Module/Map/GridMap.cs:406`（**打印文案**）、`.ai-tmp/hosts/mapcheck/Program.cs:205`（打印行）。
> ⚠️ 其中 2 处是**打印文案** ⇒ 改完必须重跑相应宿主确认仍 PASS（地图转储那一行文案会变，属预期）。

**③ `corecheck` 脆弱断言**：`_log.Count("WARN ", "Unity 时钟不可用") == 1` —— 引擎 `LogThrottle` 的降级告警含同样字样 ⇒ 将来 corecheck 一旦真调用 `Rng`/`LogThrottle` 会误红。**收紧口径**（例如只认 `[D2]` tag 或只认项目 `Log` 自己那条），并在断言旁写一行注释说明为什么这么判。

## 4. 产出物

- `client/Assets/Scripts/Core/Log.cs`（改：委托 `LogThrottle`；**签名零变化**）
- 上述 9 处注释/文案 + `corecheck` 断言口径
- 回报（消息）：产出物 + 自检原始输出 + 未决

## 5. 验收标准（逐条自查）

- [ ] **公开签名零变化**（机检）：`git` 看不到项目（被 .gitignore 忽略）⇒ 用**字符级对照**：把改前 `Log.cs` 的所有 `public static` 行与 `IsKnownTag/Clock/ClockSource/Suppress` 提取出来，改后逐字比对**完全一致**，把两串贴进回报
- [ ] 编译绿：`unity command recompile_status` ⇒ `completed / failed=false / errors=[]`
- [ ] `.ai-tmp/hosts/run_all_hosts.ps1` ⇒ `TOTAL_HOSTS=10 FAILED=0`（贴原始末行）
- [ ] 行为等价证据（离线，秒级）：`corecheck` / `flowcheck` 等与日志/限频相关的宿主输出**与改前逐行比对 0 差异**（或差异项逐条解释清楚）
- [ ] 9 处注释/文案全部同步（贴 `grep -n "Core\.Rng"` 结果 = **0 命中**）
- [ ] `git diff --stat -- clover-client-unity-engine` 与本片开工前**逐字一致**（证明没碰引擎）
- [ ] `.ai-tmp/test/` 清空到只剩 `dispatch-log.tsv` / `play-log.tsv`；**本片 0 次 Play**（全部离线可判）
- [ ] 跑一次只读 `tools/verify.ps1`，把 summary 行贴进回报（如实贴红项）

## 6. 约束

- 批次流水线：① 只读取证（改前 `Log.cs` 的公开面 + 所有 `Log.*` 调用点 + 9 处注释位置）→ ② 一次改完 → ③ 一次编译 + 跑全部宿主 + 逐行比对 → ④ 集中回报。
- ⛔ 不许改 `Log.*` 的**行为语义**（限频窗口、只报一次、空 key 不输出、Suppress 语义）；只换实现位置。
- 回报格式：`产出物 / 自检（签名对照 + 编译 + 宿主 + 逐行比对 + grep 0 命中 + diff）/ 未决`。
