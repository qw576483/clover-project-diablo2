# agent-37：引擎下沉 A6 —— 原子槽位存储（存档槽的通用层）

## 0. 技能（开工必做）

- 有 `use_skill` → `use_skill("clover-engine")`、`use_skill("unity-cli")`；没有就按序读文件：项目级 `<项目根>/tools/ai-skill/SKILL.md` → `constraints.md`；全局 `~/.codebuddy/skills/ai-skill/SKILL.md`（规则层不可覆盖）、`~/.codebuddy/skills/unity-cli/SKILL.md`
- **必读**：`patterns/engine-fix.md`、§3 四拍、§4 证据契约。

## 1. 目标（一句话）

本项目 `Module/Save/SaveModule.cs`（517 行）里有一层**与业务无关**的能力 —— **多槽存档**：原子写（`*.tmp` → `File.Replace`）、损坏留档、槽位枚举 / 存在性 / 删除。引擎只有 `Setting`（单文件设置、已含原子写），**没有槽位抽象** ⇒ 每个新项目都会重写一遍。

**本片**：把这一层下沉成引擎的 `FileSlotStore`（纯新增），本项目 `SaveModule` 改为**薄转发**（公开 API 一字不改）；`SaveJson.cs`（772 行，绑着项目 DTO `CharacterSave`）**留在项目侧，不动**。

## 2. 任务边界

**只做**：
- 引擎**新增** `Runtime/Data/FileSlotStore.cs`（位置以你取证后与 `Runtime/Data/*` 风格一致为准；`Setting.cs` 在同目录可参考其原子写与注释风格）
- 项目：`Module/Save/SaveModule.cs` 的**文件层**改为转发 `FileSlotStore`（⛔ 公开签名一字不改；`SaveJson` 的调用方式不变）
- 必要时调整 `.ai-tmp/hosts/**` 的链接项

**绝不做的**：
- ⛔ 不许改 `SaveModule` 的公开签名（`Save()/Save(data)/Load(name)/TryLoad(name,out)/Delete(name)/List()/ListAll()/Exists(name)/ApplyToModules(data)`）
- ⛔ 不许动 `SaveJson.cs` 的序列化语义 / `CharacterSave` 的字段
- ⛔ 不许改 `ISetting` / `Setting` 的既有签名与行为
- ⛔ 不许改 `tools/ai-skill/**`、`策划/**`、`docs/**`、任何全局 skill；⛔ 不许读其它 `clover-project-*`；⛔ 不许再派子 agent；⛔ 不许自己写 `# adjudicated:` 行

## 3. 契约（主 agent 已定；细节先取证再落地）

```csharp
namespace CloverEngine
{
    /// <summary>「键 → 文本」的槽位存储：原子写 + 损坏留档 + 枚举。存档 / 配置 / 回放 / 关卡草稿都可用。</summary>
    public sealed class FileSlotStore
    {
        public FileSlotStore(string dir, string extension = ".json");

        public string Dir { get; }

        /// <summary>该槽是否存在（只看文件在不在，不解析内容）。</summary>
        bool Exists(string key);

        /// <summary>
        /// 原子写：先写 &lt;key&gt;.tmp → 校验 → File.Replace 落到 &lt;key&gt;&lt;ext&gt;；失败返回 false + error（⛔ 不抛）。
        /// 已是坏文件时：先把旧文件改名留档（见 CorruptPath 约定）再写。
        /// </summary>
        bool Write(string key, string content, out string error);

        /// <summary>读回文本；文件不存在 ⇒ null；内容坏了 ⇒ null + 留档 + **限频告警一次**（⛔ 不抛）。</summary>
        string Read(string key);

        bool Delete(string key);

        /// <summary>全部 key（**排序稳定**，便于比对与 UI 列表）。</summary>
        List<string> List();

        /// <summary>最近一次被留档的坏文件路径（没有 ⇒ null；供"损坏留档"判据用）。</summary>
        string LastCorruptPath { get; }
    }
}
```

**必须取证并写进回报的三件事**：
1. 本项目 `SaveModule` 现在的**原子写/损坏处理**到底怎么做（`*.tmp` → `File.Replace`？失败怎么办？坏文件怎么留档？）——实现必须**至少等价**，并把"改前行为 → 引擎实现"逐条对照列出；
2. 槽位文件名/扩展名/目录约定（`List()` 要能稳定复现改前的排序口径）；
3. `Setting.cs` 的原子写写法（复用它的成熟做法，别另创一套）。

## 4. 产出物

- 引擎：`Runtime/Data/FileSlotStore.cs`（+ `.meta`）
- 项目：`SaveModule.cs` 文件层转发
- 回报（消息）：产出物 + 自检原始输出 + 未决

## 5. 验收标准（逐条自查）

- [ ] 编译绿：`recompile_status` ⇒ `completed / failed=false / errors=[]`
- [ ] **公开签名零变化**：`SaveModule` / `SaveJson` 的公开成员列表改前/改后**字符级对照 0 差异**（贴两串）
- [ ] `.ai-tmp/hosts/run_all_hosts.ps1` ⇒ `TOTAL_HOSTS=10 FAILED=0`
- [ ] **存档往返等价（本片最关键）**：改前/改后各跑一次"写一个存档 → 读回 → 打印全部字段"，逐字段 0 差异；并做**跨进程**对照（两个不同的 .NET 进程读写同一个槽目录，`List()` 顺序一致）。
      ⚠️ 已知抖动：`fullcheck` 里存档字节数会在 `4022/4021` 之间跳（`SaveModule.ElapsedSinceBase()` 的浮点时长）⇒ 比对时**剔除该字段**，并**同时跑一次"同构建重跑"作抖动基线**（证明剔除是必要的、不是掩盖）
- [ ] **损坏留档有据**：往槽文件写一段坏 JSON ⇒ `Read` 返回 `null`、`LastCorruptPath` 非空、**原文件不丢**（留档可见）、且**不抛异常**、后续 `Write` 仍能正常工作
- [ ] **失败路径有据**：目录不可写 / key 非法（空、含路径分隔符）⇒ 返回 `false/null` + error（⛔ 不抛、⛔ 不静默）
- [ ] `git diff --stat -- clover-client-unity-engine` 只多出本片新增/改动（逐条列出）
- [ ] `.ai-tmp/test/` 清空到只剩 `dispatch-log.tsv` / `play-log.tsv`；**本片 0 次 Play**（全离线可判）
- [ ] 跑一次只读 `tools/verify.ps1`，把 summary 行贴进回报（如实贴红项）

## 6. 约束

- 批次流水线：① 只读取证（`SaveModule.cs` / `SaveJson.cs` / `Setting.cs` + 宿主的存档断言）→ ② 一次改完 → ③ 一次编译 + 全部宿主 + 往返/损坏/失败三条路径 → ④ 集中回报。
- ⛔ **存档数据格式一个字都不许改**（改了就作废玩家存档）；只换"写盘/读盘的实现位置与手法"。
- 回报格式：`产出物 / 自检（签名对照 + 编译 + 宿主 + 往返等价 + 损坏留档 + 失败路径 + diff）/ 未决`。
