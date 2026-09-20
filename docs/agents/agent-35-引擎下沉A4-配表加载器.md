# agent-35：引擎下沉 A4 —— 配表加载器（读打表产物 tsv + 强类型访问）

## 0. 技能（开工必做）

- 有 `use_skill` → `use_skill("clover-engine")`、`use_skill("unity-cli")`；没有就按序读文件：项目级 `<项目根>/tools/ai-skill/SKILL.md`、全局 `~/.codebuddy/skills/ai-skill/SKILL.md`（规则层不可覆盖）、`~/.codebuddy/skills/unity-cli/SKILL.md`
- **必读**：`patterns/engine-fix.md`、`patterns/table.md`（打表三步闭环）、§3 四拍、§4 证据契约。

## 1. 目标（一句话）

引擎现在能"**声明**配表"（`CloverData.InitDataTable(dir)`，要求行类实现 `IDataRow`），但**读不了自家打表工具的产物**（产物是 tsv、行类由打表生成、**不实现** `IDataRow`）⇒ 每个业务项目只能自写加载器（本项目 = `client/Assets/Scripts/Table/TableLoader.cs` + `Tables.cs`）。

**本片**：在引擎里补上"**读打表产物 tsv + 强类型访问**"的能力（⛔ 不动 `CloverData` 既有签名、⛔ 不改打表工具），并把本项目切过去（删自写加载器 / 改成薄转发）。

## 2. 任务边界

**只做**：
- 引擎**新增**（建议 `Runtime/Data/CloverTable.cs`，命名以你取证后与既有 `Runtime/Data/*` 风格一致为准）：一个"配表加载器"公开类型，能力 = ① 读一个目录下的全部打表产物 tsv；② 按表名 + 主键取行（`int` 与 `string` 两种主键）；③ 失败返回**可定位的错误串**（⛔ 不抛异常、⛔ 不静默）
- 项目侧：`Table/TableLoader.cs` 改为**薄转发**（或直接删、调用点改到引擎）；`Tables.cs`（打表生成的强类型壳）保留（那是打表工具的产物，⛔ 不许手改生成物）
- 必要时同步 `.ai-tmp/hosts/**` 的链接项

**绝不做的**：
- ⛔ 不许改 `CloverData` 的既有公开签名；⛔ 不许改 `tools/table/**`（打表工具）；⛔ 不许改打表生成物（`Table/Base/*.cs` 等）
- ⛔ 不许改 `tools/ai-skill/**`、`策划/**`、`docs/**`、任何全局 skill（需要同步的写进回报）
- ⛔ 不许读其它 `clover-project-*`；⛔ 不许再派子 agent；⛔ 不许写交接/进度类文档；⛔ 不许自己写 `# adjudicated:` 行

## 3. 契约（形状由主 agent 定；**落地细节先取证再定**，与你取证到的现状对齐）

```csharp
namespace CloverEngine
{
    /// <summary>打表产物（tsv）加载器：与 clover 打表工具的输出格式配套。</summary>
    public static class CloverTable          // 名字/位置以你取证后与 Runtime/Data/* 风格一致为准
    {
        /// <summary>读一个目录下全部 tsv。成功返回 null，失败返回可定位的错误串（哪个文件/哪一行/为什么）。</summary>
        public static string LoadAll(string streamingAssetsDir, string dataDir);

        /// <summary>按表名 + int 主键取行；没有 ⇒ null（并限频告警一次）。</summary>
        public static T Get<T>(string tableName, int id) where T : class;

        /// <summary>按表名 + string 主键取行；没有 ⇒ null（并限频告警一次）。</summary>
        public static T Get<T>(string tableName, string key) where T : class;
    }
}
```

**必须与现状对齐的三件事（取证后写进回报）**：
1. 本项目 `TableLoader.LoadAll(...)` 的**返回约定**（成功 `null` / 失败错误串）与 tsv 读法（`File.ReadAllLines`、分隔符、表头行、`#` 注释行规则）；
2. 打表生成物里"行类 → 列字段"的映射方式（public 字段？列名 `hp_min` → `HpMin`？）⇒ 决定 `Get<T>` 怎么填；
3. **离线宿主**怎么编它（`.ai-tmp/hosts/*` 目前把 `Table/**` 源码链进去；切到引擎后要改链接项）。

## 4. 产出物

- 引擎新增文件（+ `.meta`）
- 项目：加载器薄转发/删除 + 调用点调整（清单由你取证后给出）
- 回报（消息）：产出物 + 自检原始输出 + 未决

## 5. 验收标准（逐条自查）

- [ ] 编译绿：`recompile_status` ⇒ `completed / failed=false / errors=[]`
- [ ] **既有签名零变化**：`CloverData` 的成员列表改前/改后**字符级对照**（只许多出新增内容）
- [ ] `.ai-tmp/hosts/run_all_hosts.ps1` ⇒ `TOTAL_HOSTS=10 FAILED=0`
- [ ] **行为等价（关键）**：本项目 10 张表的读取结果与改前**逐行 0 差异** —— 用现有宿主的表相关断言（`fullcheck` / `itemcheck` / `mapcheck` / `combatcheck` 里凡涉及 `Tables.Default.*.Get(...)` 的输出）做改前/改后逐行比对；**同时跑一次"同构建重跑"作抖动基线**
- [ ] 失败路径有据：`LoadAll(不存在的目录)` ⇒ 返回**可定位错误串**（不是 null、不是异常），且**失败后 `Get` 不崩**（返回 null + 限频告警）
- [ ] `.ai-tmp/test/` 清空到只剩 `dispatch-log.tsv` / `play-log.tsv`；**本片 0 次 Play**（全离线可判）
- [ ] `git diff --stat -- clover-client-unity-engine` 只多出本片新增/改动（逐条列出）
- [ ] 跑一次只读 `tools/verify.ps1`，把 summary 行贴进回报（如实贴红项）

## 6. 约束

- 批次流水线：① 只读取证（本项目 `Table/**` 全部 + 打表产物样例 + 10 个宿主的链接项 + `CloverData` 现状）→ ② 一次改完 → ③ 一次编译 + 全部宿主 + 逐行比对 → ④ 集中回报。
- ⛔ 表数据本身的语义/字段名/主键规则**一个字都不许改**（只换加载实现位置）。
- 回报格式：`产出物 / 自检（签名对照 + 编译 + 宿主 + 逐行比对 + diff）/ 未决`。
