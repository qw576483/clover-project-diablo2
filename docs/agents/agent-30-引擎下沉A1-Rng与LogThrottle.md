# agent-30：引擎下沉 A1 —— `Rng`（可复现随机）+ `LogThrottle`（日志降频）

## 0. 技能（开工必做）

- 有 `use_skill` → `use_skill("clover-engine")`、`use_skill("unity-cli")`；没有就按序读文件（命中即用）：
  - 项目级（首选）：`<项目根>/tools/ai-skill/SKILL.md` → 按需 `constraints.md`
  - 全局：`~/.codebuddy/skills/ai-skill/SKILL.md`（**规则层不可被项目级覆盖**）、`~/.codebuddy/skills/unity-cli/SKILL.md`
  - 兜底：`<仓库根>/clover-ai-skill/SKILL.md`
- **必读**：`clover-engine` 的 `patterns/engine-fix.md`（改引擎 SOP：**最小复现 → 最小改动（⛔ 不动公开签名）→ 前失败后通过 → E 编号由主 agent 登记**）、§2/§3/§4（成本闸门 / 四拍 / 证据契约）。

## 1. 目标（一句话）

把本项目自写的两个**通用横切能力**下沉到引擎（**纯新增**，不改任何现有公开签名）：

1. **`Rng`**：注入式、**seed 可复现**的随机器（源：`<项目根>/client/Assets/Scripts/Core/Rng.cs`，178 行）
2. **`LogThrottle`**：日志防刷屏（限频 / 只报一次 / 可注入时钟 / Unity 时钟不可用时自动降级）（源：`<项目根>/client/Assets/Scripts/Core/Log.cs`，247 行 —— **只搬降频与时钟部分**，tag 白名单是项目专属，**不搬**）

## 2. 任务边界

**只做**：在引擎 `clover-client-unity-engine/Runtime/Core/` 下**新增文件**（例如 `Rng.cs` / `LogThrottle.cs`）+ 必要时补 `Tools~/core-assert/` 的断言与最小替身。

**绝不做的**：
- ⛔ **不许改任何现有公开签名**（`ILogger` / `ITimer` / `IResourceManager` / `Game` 门面一律不动）—— 本片是**纯新增**；`LogThrottle` 是**独立静态类**，不是 `ILogger` 的成员
- ⛔ 不许改 `<项目根>/client/**`（**项目侧切换是后续片 B1**，本片一律不碰项目代码）
- ⛔ 不许改 `策划/**`、`docs/**`、`tools/**`、任何 skill（E 编号与文档由主 agent 登记）
- ⛔ 不许读工作区里其它 `clover-project-*`；⛔ 不许再派子 agent；⛔ 不许写交接/进度类文档

## 3. 契约（主 agent 已定；照抄，别自由发挥）

**源文件为准**：语义、边界处理（非法参数**不抛异常**、返回安全值 + **限频告警**）、注释里的出处都要保留。

```csharp
namespace CloverEngine
{
    /// <summary>注入式可复现随机（非线程安全，主线程使用）。⛔ 不许用全局 UnityEngine.Random。</summary>
    public sealed class Rng
    {
        public Rng(int seed);
        public int Seed { get; }
        public static Rng FromTime();                    // 仅"决定本局 seed"这一处入口
        public int DeriveSeed(int salt);
        public Rng Derive(int salt);
        public int Next();
        public int Next(int maxExclusive);
        public int Next(int minInclusive, int maxExclusive);
        public float NextFloat();                        // [0,1)
        public float Range(float min, float max);
        public int Range(int minInclusive, int maxExclusive);
        public bool Chance(float probability);           // <=0 恒 false，>=1 恒 true
        public T Pick<T>(IList<T> items);
        public int PickWeighted(IList<int> weights);
        public void Shuffle<T>(IList<T> list);           // Fisher-Yates 原地
        public UnityEngine.Vector2Int NextGrid(int width, int height);
        public UnityEngine.Vector2Int NextGrid(int width, int height, UnityEngine.Vector2Int from);
        public int Index(int size);
    }

    /// <summary>日志防刷屏（对 <c>Game.Logger</c> 的包装；纯新增，不改 ILogger）。</summary>
    public static class LogThrottle
    {
        public static Func<float> Clock { get; set; }    // 注入单调秒（离线宿主必须注入才有确定性）
        public static string ClockSource { get; }        // "Injected" / "Unity" / "Process"
        public static bool Suppress { get; set; }
        public static bool ShouldLog(string key, float intervalSeconds = 5f);
        public static bool WarnThrottled(string tag, string key, string message, float intervalSeconds = 5f);
        public static bool ErrorThrottled(string tag, string key, string message, float intervalSeconds = 5f);
        public static bool WarnOnce(string tag, string key, string message);
        public static bool ErrorOnce(string tag, string key, string message);
        public static void Reset();                      // 换场景/重进游戏时清限频表
    }
}
```

**必须逐条保持的语义**（都来自源码，改一条就是语义漂移）：
- 时钟三级：① 已注入 `Clock` ⇒ 用它；② 否则 `UnityEngine.Time.realtimeSinceStartup`，**非 Unity 进程会抛** ⇒ `try/catch` 接住（只探测一次）；③ 降级到 `Stopwatch` 并**只报一次** Warn（⚠️ 这条 Warn 必须**直发 `Game.Logger`**，⛔ 不许走 `ShouldLog`，否则递归）。
- `ShouldLog(key, +∞)` = 只报一次；空/`null` key ⇒ **不输出** + 只报一次 Warn（⛔ 不许刷屏）。
- `Rng`：`Next(max<=0)` ⇒ 返回 0 + 限频告警；`Next(min>=max)` ⇒ 返回 min + 告警；`Range(float min>=max)` ⇒ 返回 min + 告警；`Pick(null/空)` ⇒ `default` + 告警；`PickWeighted(权重全 0)` ⇒ `-1` + 告警；`Shuffle` 用同一 seed 必须得到同一排列（**确定性**）。
- `Rng` 内部限定用 `System.Random`（与项目现状一致，保证**现有 seed 的地图/掉落序列完全不变**）。

## 4. 产出物

- `clover-client-unity-engine/Runtime/Core/Rng.cs`（新增）
- `clover-client-unity-engine/Runtime/Core/LogThrottle.cs`（新增）
- `clover-client-unity-engine/Tools~/core-assert/`：**新增断言**（见 §5）+ 必要时补最小替身（`Vector2Int` / `Time`）
- 回报（消息）：产出物 + 自检原始输出 + 未决

## 5. 验收标准（逐条自查）

- [ ] 引擎编译通过：`unity command recompile --project-path client` ⇒ `recompile_status` = `completed / failed=false / errors=[]`
- [ ] **纯新增**（可机检）：`git -C c:\Work\Server\f-v2 --no-pager diff --stat -- clover-client-unity-engine` 里**只有新增文件**（`Runtime/Core/Rng.cs`、`Runtime/Core/LogThrottle.cs` 及 `Tools~/core-assert/**`），⛔ **没有**任何既有文件的修改行
- [ ] **离线断言**（`Tools~/core-assert`，照它的 `README.md` 方式跑）至少覆盖：
      · `Rng`：同 seed ⇒ 同序列（连取 100 个整数逐一相等）；不同 seed ⇒ 不同；`Chance(0)`/`Chance(1)` 边界；`Shuffle` 同 seed 同排列；`PickWeighted` 权重全 0 ⇒ -1；`Next(0)` ⇒ 0；`Derive` 确定性
      · `LogThrottle`：注入时钟后 `WarnThrottled` 在间隔内只出 1 条、跨过间隔再出；`WarnOnce`/`ErrorOnce` 第 2 次返回 false；空 key ⇒ false；`Reset()` 后重新可出
      · 若 `Tools~/core-assert` 缺 `Vector2Int` / `Time` 替身 ⇒ 按它的 README **补最小替身**（只用于编译，⛔ 不许改语义）；若补替身代价过高 ⇒ 断言只覆盖不依赖 Unity 类型的子集，并在回报里写明
- [ ] 断言输出贴进回报（原始文本）
- [ ] 两个新文件头部注释写清：**出处**（项目 `Core/Rng.cs` / `Core/Log.cs` 的哪一段）、**为什么下沉**（通用横切能力）、**语义约束**（非法参数不抛 / 时钟三级 / 确定性）；⛔ 不许写"以后可以…"
- [ ] 未改任何现有文件（`Runtime/**` 里除两个新文件外，`git diff` 无内容）

## 6. 约束

- 批次流水线（四拍）：① 只读取证（读源文件 + `Tools~/core-assert/README.md`）→ ② 一次写完两个文件 + 断言 → ③ 一次编译 + 跑断言 → ④ 集中回报。⛔ 不许改一处编译一次。
- 探针/一次性产物只放 `<项目根>/.ai-tmp/test/`（用完删）；⛔ 不许散落。
- 发现**源文件**里存在缺陷 ⇒ 本片**照搬语义**，缺陷写进回报（⛔ 不许顺手"改进"）。
- 回报格式：`产出物 / 自检（编译 + 断言原始输出 + diff 证明纯新增）/ 未决`。
