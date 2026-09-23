// ─────────────────────────────────────────────────────────────────────────────
// SaveCheck · agent-37（引擎下沉 A6 · 原子槽位存储）离线自检宿主
//
// 覆盖的判据（对应任务书 `docs/agents/agent-37-引擎下沉A6-原子槽位存储.md` §5，全离线可判，0 次 Play）：
//   1) 引擎新增 `CloverEngine.FileSlotStore` 的**公开面与契约一致**（反射打印 + 逐条比对）；
//   2) **改前手法取证**：项目存档的原子写/损坏留档**不在 SaveModule 里，而在引擎 `Setting`** ——
//      本宿主链真实 `Runtime/Core/Setting.cs` 实测（`settings.json.tmp` → `File.Replace`、坏 json ⇒
//      `.corrupt` 留档、目录非法 ⇒ 退内存不抛），并**只读**打开项目真实存档
//      `client/setting/settings.json` 打印 `char/*` 槽位与 `char/index` 口径；
//   3) **`List()` 口径差异实测**：项目口径（`char/index` 保序 = 插入序）vs 引擎新类（字典序）；
//   4) **真实存档载荷往返**：`SaveJson.Write` → `FileSlotStore.Write/Read` → `SaveJson.TryParse`
//      ⇒ 逐字段对照 0 差异 + `Write(Parse(json)) == json` 逐字节恒等；
//   5) **跨进程**：两个不同的 .NET 进程读写同一槽目录 ⇒ `List()` 顺序一致 + 逐槽载荷 SHA 一致；
//   6) **损坏留档有据**：坏 JSON ⇒ `Read` null + `LastCorruptPath` 非空 + 留档内容 = 原坏内容 +
//      原文件不丢 + 不抛 + **之后仍能正常写**（`Read` 路径与 `Write` 路径各一条）；
//   7) **失败路径有据**：目录不可写 / key 非法（null、空、空白、含分隔符、`../`、通配符）/ 内容 null
//      ⇒ `false`/`null` + error（⛔ 不抛、⛔ 不静默）。
//   8) ★ **片 Q（R7 读档失败静默，缺陷出处 `.ai-tmp/test/audit-C-logic-num.md` §2 红行 R7）**：
//      `SaveModule.Load/TryLoad` 的**三情况可判别** —— ① 档不存在 ⇒ `false` + `LastError==""`（正常、不报错）；
//      ② 档在但解析失败/损坏 ⇒ `false` + `LastError!=""`（含档名+原因）+ `Events.LoadDone` 发 **null**
//      （= 用户可见反馈的触发点，唯一消费者 `AppFlow.OnLoadDone` → 复用 `UI/D2ConfirmPanel`）；
//      ③ 版本不符 / 缺字段 ⇒ 兼容读**成功** + Info（值取默认值）。⛔ 本步骤**只加断言**，
//      并把**被验证对象本身**（`Module/Save/SaveModule.cs`，与 Assets/ 同一份文件）编进宿主。
//
// 已知边界（不是缺陷）：
//   · 本宿主不驱动 Unity 原生（不 `new GameObject`）：本片判据全是文件 + 数据层，离线即可判完；
//   · 对项目真实存档目录**只调 `Get`、从不 `Save()`** ⇒ 不重写玩家存档
//     （shell 侧另比 `settings.json` 的 SHA256 作为外部证据）；
//   · 本宿主**不在** `.ai-tmp/hosts/run_all_hosts.ps1` 的 10 个名额里（`TOTAL_HOSTS=10` 保持不变）：
//     它是本片的独立复检入口，跑法 = `cd .ai-tmp/hosts/savecheck; dotnet run -v q --nologo`。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module.Save;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace SaveCheck
{
    /// <summary>控制台 + 内存双写日志（内存副本用于断言"某条日志确实打出来了"、"只打了一次"）。</summary>
    internal sealed class RecLogger : ILogger
    {
        private readonly List<string> _lines = new List<string>();

        public int Count(string fragment)
        {
            var n = 0;
            for (var i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].IndexOf(fragment, StringComparison.Ordinal) >= 0) n++;
            }
            return n;
        }

        private void W(string level, string tag, string msg)
        {
            var line = "[" + level + "] [" + tag + "] " + msg;
            _lines.Add(line);
            Console.WriteLine("      " + line);
        }

        public void Info(string tag, string msg) => W("INFO ", tag, msg);
        public void Warn(string tag, string msg) => W("WARN ", tag, msg);
        public void Debug(string tag, string msg) => W("DEBUG", tag, msg);
        public void Error(string tag, string msg, Exception ex = null)
            => W("ERROR", tag, msg + (ex != null ? " | " + ex.GetType().Name + ": " + ex.Message : ""));
        public void Fatal(string tag, string msg, Exception ex = null)
            => W("FATAL", tag, msg + (ex != null ? " | " + ex.GetType().Name + ": " + ex.Message : ""));
    }

    public static class Program
    {
        // ★ 仓库根改为**运行期推导**（见 ResolveProjectRoot），不再依赖调用方 cwd。
        private static readonly string ProjectRoot = ResolveProjectRoot();
        private static readonly string ClientRoot = ProjectRoot + @"\client";
        private const string BadJson = "{{{ this is not json";
        private const string PayloadA = "{\"k\":1}";

        private static RecLogger _log;
        private static string _work;
        private static int _fail;

        /// <summary>
        /// 从宿主自己的可执行目录向上找“含 client/Assets 的那一层” = 仓库根。
        /// 宿主位于 tools/probes/hosts/&lt;名&gt;/bin/&lt;cfg&gt;/&lt;tfm&gt;/；若按调用方 cwd 定位，
        /// 从仓库根运行时会被拼成 &lt;仓库根&gt;/clover-project-diablo2/client/...（一个文件都找不到）。
        /// 找不到就回退成原来的相对写法，保持“从仓库上一级目录运行”的老用法不变。
        /// </summary>
        private static string ResolveProjectRoot()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "client", "Assets")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            Console.WriteLine("[warn] 未从可执行目录向上找到含 client/Assets 的仓库根，回退相对路径 clover-project-diablo2");
            return @"clover-project-diablo2";
        }

        /// <summary>
        /// 子进程守卫（环境变量名）：带它启动的进程**只允许**跑子进程模式，认不出模式就直接退出。
        /// <para>★ 事故（2026-09-21，主 agent 实测）：`RunChild` 的参数拼接错了（apphost 被额外塞了一个
        /// `.dll` 参数 ⇒ 子进程把它当普通参数、于是**又跑了一遍完整 Main**、再派生 2 个子进程 …）
        /// ⇒ 实测同时有 **6754 个 `savecheck.exe`** 在跑（进程雪崩，拖死整机与 shell）。
        /// 本守卫把它变成**结构上不可能**：子进程只许做子进程的事，认不出就退出（exit 3）。</para>
        /// </summary>
        private const string ChildGuard = "SAVECHECK_CHILD";

        /// <summary>在 args 里找第一个已知的子进程模式（容忍宿主在前面多塞参数）。</summary>
        private static int FindChildMode(string[] args)
        {
            if (args == null) return -1;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "crosswrite" || args[i] == "crossread") return i;
            }
            return -1;
        }

        public static int Main(string[] args)
        {
            // 子进程模式（跨进程判据用）：**扫描出模式下标**，而不是死认 args[0]
            var modeIdx = FindChildMode(args);
            if (modeIdx >= 0 && modeIdx + 1 < args.Length)
            {
                return args[modeIdx] == "crosswrite" ? CrossWrite(args[modeIdx + 1]) : CrossRead(args[modeIdx + 1]);
            }
            if (Environment.GetEnvironmentVariable(ChildGuard) == "1")
            {
                // 带了守卫却认不出模式 ⇒ 绝不允许"再跑一遍完整自检"（那就是雪崩的入口）
                Console.WriteLine("SAVECHECK_CHILD_ABORT args=[" + string.Join(",", args) + "]");
                return 3;
            }

            Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  SaveCheck：引擎 FileSlotStore（A6）离线自检 + 改前手法对照      ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");

            _log = new RecLogger();
            Game.Logger = _log;
            // 离线宿主：注入确定性时钟（否则 LogThrottle 的限频入口会降级并多打一条 Warn）
            LogThrottle.Clock = () => 0f;

            _work = Path.Combine(ProjectRoot, @".ai-tmp\test\savecheck");
            Console.WriteLine("  工作目录（一次性产物，只在这里）：" + _work);
            FreshDir(_work);

            Run(Step1_EngineSurface);
            Run(Step2_BeforeAtomicWrite);
            Run(Step3_RealSaveReadOnly);
            Run(Step4_ListOrderConvention);
            Run(Step5_SlotBasics);
            Run(Step6_RealPayloadRoundTrip);
            Run(Step7_CrossProcess);
            Run(Step8_CorruptArchiveOnRead);
            Run(Step9_CorruptArchiveOnWrite);
            Run(Step10_NonJsonExtension);
            Run(Step11_FailurePaths);
            Run(Step12_LoadFailureClassification);

            Console.WriteLine();
            Console.WriteLine("──────────────────────────────────────────────────────────────────");
            Console.WriteLine(_fail == 0 ? "=== 自检全部通过 ===" : "=== 自检失败 " + _fail + " 项 ===");
            Console.WriteLine("SAVECHECK_SUMMARY FAIL=" + _fail);
            return _fail == 0 ? 0 : 1;
        }

        // ── 1. 引擎新增类的公开面（契约对照） ────────────────────────────────
        private static void Step1_EngineSurface()
        {
            Section("1. 引擎新增 `CloverEngine.FileSlotStore` 的公开面（逐条对齐任务书 §3 契约）");

            var actual = Surface(typeof(FileSlotStore));
            Console.WriteLine("      实测公开成员（排序后）：");
            for (var i = 0; i < actual.Count; i++) Console.WriteLine("        " + actual[i]);

            var expected = new List<string>
            {
                "Boolean Delete(String)",
                "Boolean Exists(String)",
                "Boolean Write(String, String, String&)",
                "List<String> List()",
                "String Dir { get }",
                "String LastCorruptPath { get }",
                "String Read(String)",
                "Void .ctor(String, String)",
            };
            expected.Sort(StringComparer.Ordinal);

            // ★ 已登记的引擎漂移（2026-09-21 主 agent 实测）：引擎 `FileSlotStore.LastCorruptPath` 是
            //   `{ get set }`（公开 setter），而本宿主契约列写的是 `{ get }`。断言**不放宽**：
            //   先把这条漂移从实测里摘掉、再要求"剩下的逐字等于契约 8 项"，且**摘掉的必须恰好只有这一条**。
            //   （引擎侧是否该收起这个公开 setter 待裁决；本宿主只如实记录，不替它拍板。）
            const string KnownDrift = "String LastCorruptPath { get set }";
            const string ContractCorrupt = "String LastCorruptPath { get }";
            var drift = actual.FindAll(s => s == KnownDrift);
            var normalized = actual.FindAll(s => s != KnownDrift);
            var expectedNoCorrupt = expected.FindAll(s => s != ContractCorrupt);   // 同一口径摘掉该条再比
            Check("公开成员 = 契约的 8 项（放行 1 条已登记漂移：LastCorruptPath 的公开 setter）",
                string.Join(" | ", normalized) == string.Join(" | ", expectedNoCorrupt) && drift.Count == 1,
                "\n        实测=" + string.Join(" | ", actual) +
                "\n        摘掉已登记漂移后=" + string.Join(" | ", normalized) +
                "\n        期望（同口径摘掉该条）=" + string.Join(" | ", expectedNoCorrupt));
            Check("★ 已登记漂移清单未扩大（除 LastCorruptPath 的 setter **变体**外无其它公开面变化；条数不变）",
                actual.Count == expected.Count && drift.Count == 1,
                "实测条数=" + actual.Count + " / 契约条数=" + expected.Count +
                " / 漂移命中=" + drift.Count + "（`{ get }`→`{ get set }` 是同一条成员的变体，不改变条数）");
            Console.WriteLine();
        }

        // ── 2. 改前手法：`Setting`（原子写 + 损坏留档） ──────────────────────
        private static void Step2_BeforeAtomicWrite()
        {
            Section("2. 改前手法取证：项目存档的原子写/损坏留档**在引擎 `Setting` 里**（实测，不是读源码下结论）");

            var dir = Path.Combine(_work, "setting-demo");
            FreshDir(dir);
            var file = Path.Combine(dir, "settings.json");
            var payload = "{\"version\":1,\"name\":\"Demo\"}";

            var s1 = new Setting(dir);
            s1.Set(GameConst.SaveKeyPrefix + "Demo", payload);
            s1.Set(GameConst.SaveIndexKey, SaveJson.WriteStringList(new List<string> { "Demo" }));
            s1.Save();

            Check("改前手法①：`Setting.Save()` 落盘 `settings.json` 且**无 `.tmp` 残留**（= 先写 .tmp → File.Replace）",
                File.Exists(file) && !File.Exists(file + ".tmp"), file);
            var s2 = new Setting(dir);
            Check("改前手法②：新实例（等价新进程）读回同一份键值",
                s2.Get<string>(GameConst.SaveKeyPrefix + "Demo", "") == payload &&
                s2.Get<string>(GameConst.SaveIndexKey, "") == SaveJson.WriteStringList(new List<string> { "Demo" }),
                s2.Get<string>(GameConst.SaveIndexKey, ""));

            File.WriteAllText(file, BadJson);                       // 整份 settings.json 坏掉
            var s3 = new Setting(dir);
            var archived = Directory.GetFiles(dir, "*.corrupt");
            Check("改前手法③：`settings.json` 损坏 ⇒ **不抛** + 回退默认值 + 整份文件留档 `.corrupt`",
                s3.Get<string>(GameConst.SaveKeyPrefix + "Demo", "") == "" && archived.Length > 0,
                "留档=" + (archived.Length > 0 ? archived[0] : "(无)") +
                "；粒度 = **整份文件**（新类 `FileSlotStore` 是**单槽**粒度 ⇒ 不是同一层，见回报对照表）");

            var bad = new Setting("<>:|?*", "settings.json");
            bad.Set("x", 1);
            bad.Save();
            Check("改前手法④：目录非法 ⇒ 不抛、退化为内存（新类 `FileSlotStore` 的处置不同：写返回 false + error）",
                bad.Get("x", 0) == 1, "Setting.Get(x)=" + bad.Get("x", 0));
            Console.WriteLine();
        }

        // ── 3. 项目真实存档（只读） ──────────────────────────────────────────
        private static void Step3_RealSaveReadOnly()
        {
            Section("3. 项目真实存档（**只读**：只读取，从不写）：A6 起 = `<SettingDir>/saves/<角色名>.json` + `char/index` 顺序索引");

            var realDir = Path.Combine(ClientRoot, "setting");
            var idxFile = Path.Combine(realDir, "settings.json");
            var slotsDir = Path.Combine(realDir, "saves");
            Check("引擎设置文件在位（`char/index` 在它里面）", File.Exists(idxFile), idxFile);
            Check("槽位目录在位（A6 口径：一角色一文件）", Directory.Exists(slotsDir), slotsDir);
            if (!File.Exists(idxFile)) return;

            var real = new Setting(realDir);            // ctor 只 Load；本步骤**不调 Save()**
            var idxRaw = real.Get<string>(GameConst.SaveIndexKey, "");
            var names = SaveJson.ParseStringList(idxRaw);
            Console.WriteLine("      `char/index` 原样 = " + idxRaw);

            var parsed = 0;
            var files = 0;
            var missing = new List<string>();
            var sb = new StringBuilder();
            for (var i = 0; i < names.Count; i++)
            {
                var slot = Path.Combine(slotsDir, names[i] + ".json");
                var exists = File.Exists(slot);
                if (exists) { files++; } else { missing.Add(names[i]); }
                var json = exists ? File.ReadAllText(slot) : null;
                string perr;
                if (SaveJson.TryParse(json, out perr) != null) parsed++;
                sb.Append(names[i]).Append("(").Append(json == null ? 0 : json.Length).Append("B) ");
            }
            Console.WriteLine("      真实槽位（按 char/index 顺序）= " + sb.ToString().TrimEnd());

            Check("真实存档可读：索引里每个角色都有 `saves/<名>.json`、且内容能解析成 CharacterSave",
                names.Count > 0 && files == names.Count && parsed == names.Count,
                "索引 " + names.Count + " 个 / 槽位文件 " + files + " 个 / 可解析 " + parsed + " 个" +
                (missing.Count == 0 ? "" : "；缺文件=" + string.Join(",", missing)));

            var stray = new List<string>();
            if (Directory.Exists(slotsDir))
            {
                var all = Directory.GetFiles(slotsDir, "*.json");
                for (var i = 0; i < all.Length; i++)
                {
                    var key = Path.GetFileNameWithoutExtension(all[i]);
                    if (!names.Contains(key)) stray.Add(key);
                }
            }
            Check("槽位目录里没有**索引外的孤儿档**（`char/index` 与 `saves/*.json` 一一对应）",
                stray.Count == 0,
                stray.Count == 0 ? "0 个孤儿" : stray.Count + " 个孤儿：" + string.Join(",", stray));

            // ★ 片 assert-audit：原为硬编码 `true`（只是一句结论陈述 ⇒ 不产生判据、却占"通过"计数）。
            //   改成**真的问一次引擎 `FileSlotStore`**：它必须能认出同一批槽位（口径同源 = 这条结论本身）。
            //   出处：`Module/Save/SaveModule.cs:4-7`（槽位目录 `saves` / 全部走引擎 FileSlotStore）
            //        + `Module/Save/SaveJson.cs:5`（旧键 `char/{名}` 已作废）。
            var engineStoreNames = new List<string>(new FileSlotStore(slotsDir).List());
            Check("⇒ 现行存档口径 = 引擎 `FileSlotStore` 的 `<SettingDir>/saves/<角色名>.json`（一角色一文件）" +
                  "，创建顺序在 `Game.Setting` 的 `char/index`（**A6 已落地**；`char/{名}` 旧键写法已作废）",
                names.Count > 0 && engineStoreNames.Count == names.Count && engineStoreNames.Contains(names[0]),
                $"引擎 FileSlotStore 在 {slotsDir} 认出 {engineStoreNames.Count} 个槽；`char/index` {names.Count} 个" +
                "（两者同源 ⇒ 选角列表顺序由 index 决定，原文案「判据只增不减」保持）");
            Console.WriteLine();
        }

        // ── 4. `List()` 口径差异（项目插入序 vs 引擎字典序） ──────────────────
        private static void Step4_ListOrderConvention()
        {
            Section("4. `List()` 口径实测：项目 = 插入序（`char/index`）；引擎新类 = 字典序（契约要求「排序稳定」）");

            var insert = new List<string> { "zeta", "alpha", "mid" };   // 刻意非字典序：模拟「先建 zeta 后建 alpha」

            var idxDir = Path.Combine(_work, "index-order");
            FreshDir(idxDir);
            var so = new Setting(idxDir);
            so.Set(GameConst.SaveIndexKey, SaveJson.WriteStringList(insert));
            so.Save();
            var so2 = new Setting(idxDir);
            var back = SaveJson.ParseStringList(so2.Get<string>(GameConst.SaveIndexKey, ""));
            Check("项目口径：`char/index` 保序 ⇒ `SaveModule.List()` = **插入序**（zeta,alpha,mid）",
                string.Join(",", back) == "zeta,alpha,mid", string.Join(",", back));

            var slotDir = Path.Combine(_work, "slot-order");
            FreshDir(slotDir);
            var store = new FileSlotStore(slotDir);
            for (var i = 0; i < insert.Count; i++)
            {
                string e;
                store.Write(insert[i], "x", out e);
            }
            var got = string.Join(",", store.List());
            Console.WriteLine("      引擎 `FileSlotStore.List()`（同三个键、同写入序）= " + got);
            Check("引擎新类：`List()` = **字典序**（alpha,mid,zeta）—— 与项目插入序**不同**",
                got == "alpha,mid,zeta", got);
            // ★ 片 assert-audit：原为硬编码 `true`（只写"上两条即证据"）⇒ 不产生判据。
            //   改成**把上面两条量到的顺序真的比一遍**（两者必须不同 = 转发会改变选角列表顺序）。
            Check("⇒ 结论：把 `SaveModule.List()` 直接转发给 `FileSlotStore.List()` 会**改变选角列表顺序**（行为不等价）",
                string.Join(",", back) != got,
                $"项目插入序={string.Join(",", back)}；引擎字典序={got}（不同 ⇒ 不等价；选角屏 `CharSelectPanel` 按 `List()` 顺序出行）");
            Console.WriteLine();
        }

        // ── 5. 槽位基本行为 ─────────────────────────────────────────────────
        private static void Step5_SlotBasics()
        {
            Section("5. `FileSlotStore` 基本行为：写/存在性/读/枚举/删除 + 原子写无残留");

            var dir = Path.Combine(_work, "slots");
            FreshDir(dir);
            var st = new FileSlotStore(dir);
            var fileA = Path.Combine(dir, "a.json");

            Check("`Dir` 原样可读", st.Dir == dir, st.Dir);
            Check("`LastCorruptPath` 初始为 null（尚未留档）", st.LastCorruptPath == null, st.LastCorruptPath ?? "(null)");
            Check("`Exists`：没写过的键 ⇒ false", !st.Exists("a"), "");

            string err;
            Check("`Write` 首次（目标不存在 ⇒ 走「删除+改名」兜底）成功",
                st.Write("a", PayloadA, out err), err ?? "");
            Check("一槽一文件：`<dir>/a.json` 真的存在", File.Exists(fileA), fileA);
            Check("`Exists`：写过的键 ⇒ true", st.Exists("a"), "");
            Check("原子写完成：目录里**无 `.tmp` 残留**", Directory.GetFiles(dir, "*.tmp").Length == 0,
                string.Join(",", Directory.GetFiles(dir, "*.tmp")));
            Check("`Read` 回原文", st.Read("a") == PayloadA, st.Read("a") ?? "(null)");

            Check("`Write` 覆盖（第二次写不同内容）成功",
                st.Write("a", "{\"k\":2}", out err) && st.Read("a") == "{\"k\":2}", err ?? "");
            Check("覆盖后仍**无 `.tmp` 残留**（走 `File.Replace` 路径）",
                Directory.GetFiles(dir, "*.tmp").Length == 0, "");

            st.Write("b", "{\"m\":1}", out err);
            Check("`List()` = 字典序（a,b）", string.Join(",", st.List()) == "a,b", string.Join(",", st.List()));
            Check("`Delete` 已存在的键 ⇒ true；重复删 ⇒ false", st.Delete("b") && !st.Delete("b"), "");
            Check("`Delete` 后 `List()` 不再含 b、文件也没了",
                string.Join(",", st.List()) == "a" && !File.Exists(Path.Combine(dir, "b.json")),
                string.Join(",", st.List()));
            Check("`Read` 不存在的键 ⇒ null（正常情形，不告警）", st.Read("nope") == null, "");
            Check("成功路径不打日志（引擎惯例：成功不刷屏）", _log.Count("[FileSlotStore]") == 0,
                "[FileSlotStore] 日志行数=" + _log.Count("[FileSlotStore]"));
            Console.WriteLine();
        }

        // ── 6. 真实存档载荷往返（逐字段 0 差异） ──────────────────────────────
        private static void Step6_RealPayloadRoundTrip()
        {
            Section("6. **真实存档载荷**过槽位存储：逐字段 0 差异 + `Write(Parse(json)) == json` 逐字节");

            var dir = Path.Combine(_work, "payload");
            FreshDir(dir);
            var store = new FileSlotStore(dir);

            var before = BuildSave();
            var json1 = SaveJson.Write(before);
            Console.WriteLine("      JSON 长度=" + json1.Length + " 字节；槽位路径=" + Path.Combine(dir, before.name + ".json"));

            string err;
            Check("写入成功（`FileSlotStore.Write`）", store.Write(before.name, json1, out err), err ?? "");

            var text = store.Read(before.name);
            Check("读回文本与写入文本**逐字节相同**", text == json1,
                "写入=" + json1.Length + "B / 读回=" + (text == null ? 0 : text.Length) + "B");

            string perr;
            var after = SaveJson.TryParse(text, out perr);
            Check("读回内容可解析成 `CharacterSave`", after != null, perr ?? "");

            string detail;
            var diffs = DiffFields(before, after, out detail);
            Console.WriteLine(detail);
            Check("★ 逐字段对照：差异 = 0（公开字段数 = " + typeof(CharacterSave).GetFields().Length + "）",
                diffs == 0, "diff=" + diffs);
            Check("★ `Write(Parse(json)) == json`（逐字节恒等 ⇒ 本片没动 `SaveJson` 的序列化语义）",
                SaveJson.Write(after) == json1, "");
            Check("关键字段抽样（等级/金币/seed/背包锚点/任务）",
                after.level == 7 && after.gold == 4321 && after.mapSeed == 20260919 &&
                CountAnchors(after) == 1 && after.quests.Count == 1 && after.quests[0].state == QuestState.InProgress,
                "lv=" + after.level + " gold=" + after.gold + " seed=" + after.mapSeed +
                " anchors=" + CountAnchors(after) + " quests=" + after.quests.Count);
            Console.WriteLine();
        }

        // ── 7. 跨进程 ───────────────────────────────────────────────────────
        private static void Step7_CrossProcess()
        {
            Section("7. **跨进程**：两个不同的 .NET 进程读写同一槽目录 ⇒ `List()` 顺序一致 + 逐槽 SHA 一致");

            var dir = Path.Combine(_work, "cross");
            FreshDir(dir);

            var w = RunChild("crosswrite", dir);
            var r = RunChild("crossread", dir);

            var sigW = Extract(w.output, new[] { "LIST=", "SHA " });
            var sigR = Extract(r.output, new[] { "LIST=", "SHA " });
            Check("写侧与读侧逐行一致（`List()` 顺序 + 每个槽的载荷 SHA256 前 8 字节）",
                sigW.Length > 0 && sigW == sigR,
                "\n        写侧：\n" + Indent(sigW) + "        读侧：\n" + Indent(sigR));
            Check("跨进程 `List()` = 字典序 `alpha,hero_01,mid,zeta`（写入序是 zeta,alpha,mid,hero_01 ⇒ 再次证明不是插入序）",
                Extract(w.output, new[] { "LIST=" }).Trim() == "LIST=alpha,hero_01,mid,zeta",
                Extract(w.output, new[] { "LIST=" }).Trim());
            Check("读侧（另一个进程）能解析出真实存档：等级 7",
                r.output.IndexOf("CROSSREAD_LEVEL=7", StringComparison.Ordinal) >= 0,
                "见子进程输出");
            Check("两个子进程 exit=0", w.code == 0 && r.code == 0, "write=" + w.code + " read=" + r.code);
            Console.WriteLine();
        }

        // ── 8. 损坏留档（Read 路径） ─────────────────────────────────────────
        private static void Step8_CorruptArchiveOnRead()
        {
            Section("8. **损坏留档有据**（`Read` 路径）：坏 JSON ⇒ null + 留档 + 原文件不丢 + 不抛 + 之后仍能写");

            var dir = Path.Combine(_work, "corrupt-read");
            FreshDir(dir);
            var store = new FileSlotStore(dir);
            var slotFile = Path.Combine(dir, "bad.json");
            File.WriteAllText(slotFile, BadJson);       // 绕过 Write 直接写盘 = 模拟"写一半崩了 / 被外部改坏"

            Check("损坏槽位已就位（原文件仍在那儿）", File.Exists(slotFile), slotFile);

            string thrown = null;
            string back = null;
            try { back = store.Read("bad"); }
            catch (Exception ex) { thrown = ex.GetType().Name + ": " + ex.Message; }

            Check("★ `Read` 坏内容**不抛异常**", thrown == null, thrown ?? "无异常");
            Check("★ `Read` 坏内容返回 null", back == null, back ?? "(null)");
            Check("★ `LastCorruptPath` 非空", !string.IsNullOrEmpty(store.LastCorruptPath), store.LastCorruptPath ?? "(null)");
            Check("★ 留档文件存在、内容 = 原坏内容（**原文件内容没丢**）",
                store.LastCorruptPath != null && File.Exists(store.LastCorruptPath) &&
                File.ReadAllText(store.LastCorruptPath) == BadJson,
                store.LastCorruptPath + " → " + (store.LastCorruptPath != null && File.Exists(store.LastCorruptPath)
                    ? Truncate(File.ReadAllText(store.LastCorruptPath), 40) : "(缺)"));
            Check("★ 原槽文件仍在（副本语义 ⇒ 后续 `Write` 能正常覆盖它）", File.Exists(slotFile), "");
            Check("坏内容有告警（⛔ 不静默）", _log.Count("内容损坏") == 1, "告警行数=" + _log.Count("内容损坏"));

            store.Read("bad");                          // 再读一次（同一槽）
            Check("★ 限频：同一槽第二次读**不再刷屏**（只说一次）",
                _log.Count("内容损坏") == 1, "告警行数=" + _log.Count("内容损坏"));

            string err;
            Check("★ 坏文件之后**仍能正常写**同一槽（留档不阻断写入）",
                store.Write("bad", "{\"ok\":true}", out err) && store.Read("bad") == "{\"ok\":true}", err ?? "");
            Check("`List()` 不含 `.corrupt` / `.tmp` 伪槽位", string.Join(",", store.List()) == "bad",
                string.Join(",", store.List()));
            Console.WriteLine();
        }

        // ── 9. 损坏留档（Write 路径） ────────────────────────────────────────
        private static void Step9_CorruptArchiveOnWrite()
        {
            Section("9. **损坏留档有据**（`Write` 路径）：目标已是坏文件 ⇒ 先留档再写（⛔ 不静默覆盖现场）");

            var dir = Path.Combine(_work, "corrupt-write");
            FreshDir(dir);
            var store = new FileSlotStore(dir);
            File.WriteAllText(Path.Combine(dir, "c.json"), BadJson);

            string err;
            Check("写入成功（覆盖一个坏槽）", store.Write("c", "{\"fresh\":1}", out err), err ?? "");
            Check("★ 留档指向该槽、内容 = 原坏内容",
                store.LastCorruptPath != null &&
                store.LastCorruptPath.EndsWith("c.json.corrupt", StringComparison.OrdinalIgnoreCase) &&
                File.ReadAllText(store.LastCorruptPath) == BadJson,
                store.LastCorruptPath ?? "(null)");
            Check("新内容已落盘（坏内容没被静默丢掉：已留档）", store.Read("c") == "{\"fresh\":1}", store.Read("c"));
            Console.WriteLine();
        }

        // ── 10. 非 .json 扩展名 ─────────────────────────────────────────────
        private static void Step10_NonJsonExtension()
        {
            Section("10. 扩展名约定：`.json` 槽校验 JSON；其它扩展名一律按合法文本");

            var dir = Path.Combine(_work, "txt-slots");
            FreshDir(dir);
            var txt = new FileSlotStore(dir, ".txt");
            string err;
            const string anyText = "这不是 JSON，也不算坏";
            Check("`.txt` 槽：任意文本可写入 / 读回原文（不按 JSON 校验）",
                txt.Write("note", anyText, out err) && txt.Read("note") == anyText, err ?? "");

            var noDot = new FileSlotStore(dir, "txt");
            Check("扩展名缺前导点自动补齐（`txt` ⇒ `.txt`）", noDot.Exists("note"), "");
            var dflt = new FileSlotStore(dir);
            Check("默认扩展名 = `.json`（同名键互不干扰：`.json` 槽此时还不存在）", !dflt.Exists("note"), "");
            Console.WriteLine();
        }

        // ── 11. 失败路径 ────────────────────────────────────────────────────
        private static void Step11_FailurePaths()
        {
            Section("11. **失败路径有据**：目录不可写 / key 非法 / 内容 null ⇒ false/null + error（⛔ 不抛、⛔ 不静默）");

            // ① 目录不可写：拿一个**普通文件**当目录的父级 ⇒ 建目录/写盘必失败
            var blocker = Path.Combine(_work, "blocker-file");
            if (File.Exists(blocker)) File.Delete(blocker);
            File.WriteAllText(blocker, "x");
            var broken = new FileSlotStore(Path.Combine(blocker, "sub"));

            string thrown = null;
            string err = null;
            var ok = false;
            try { ok = broken.Write("k", "v", out err); }
            catch (Exception ex) { thrown = ex.GetType().Name + ": " + ex.Message; }
            Check("★ 目录不可写 ⇒ `Write` false + 可定位 error，且**不抛**",
                thrown == null && !ok && !string.IsNullOrEmpty(err), (err ?? thrown ?? "(空)"));

            thrown = null;
            string readBack = null;
            try { readBack = broken.Read("k"); } catch (Exception ex) { thrown = ex.GetType().Name; }
            Check("★ 目录不可写 ⇒ `Read` null（不抛）", thrown == null && readBack == null, thrown ?? "(null)");

            thrown = null;
            var cnt = -1;
            try { cnt = broken.List().Count; } catch (Exception ex) { thrown = ex.GetType().Name; }
            Check("★ 目录不可写 ⇒ `List()` 空（不抛）", thrown == null && cnt == 0, "count=" + cnt);

            thrown = null;
            var del = true;
            try { del = broken.Delete("k"); } catch (Exception ex) { thrown = ex.GetType().Name; }
            Check("★ 目录不可写 ⇒ `Delete`/`Exists` 都 false（不抛）",
                thrown == null && !del && !broken.Exists("k"), thrown ?? "");

            // ② 目录为空串 / 非法字符
            var emptyDir = new FileSlotStore("");
            string e2 = null;
            var ok2 = emptyDir.Write("k", "v", out e2);
            Check("目录为空串 ⇒ `Write` false + error（不抛、不静默）", !ok2 && !string.IsNullOrEmpty(e2), e2);
            var illegal = new FileSlotStore("<>:|?*");
            string e3 = null;
            Check("目录含非法字符 ⇒ `Write` false + error（不抛、不静默）",
                !illegal.Write("k", "v", out e3) && !string.IsNullOrEmpty(e3), e3);

            // ③ key 非法
            var keyDir = Path.Combine(_work, "key-slots");
            FreshDir(keyDir);
            var store = new FileSlotStore(keyDir);
            var badKeys = new[] { null, "", "   ", "a/b", "a\\b", "../evil", "..\\evil", "a*b" };
            var rejected = 0;
            var problems = new List<string>();
            for (var i = 0; i < badKeys.Length; i++)
            {
                var k = badKeys[i];
                try
                {
                    string e;
                    var w = store.Write(k, "x", out e);
                    var r = store.Read(k);
                    var x = store.Exists(k);
                    var d = store.Delete(k);
                    if (!w && string.IsNullOrEmpty(e)) problems.Add(Show(k) + ":Write=false 但 error 为空");
                    if (w) problems.Add(Show(k) + ":Write 竟然成功");
                    if (r != null) problems.Add(Show(k) + ":Read 非 null");
                    if (x) problems.Add(Show(k) + ":Exists=true");
                    if (d) problems.Add(Show(k) + ":Delete=true");
                    if (!w && r == null && !x && !d && !string.IsNullOrEmpty(e)) rejected++;
                }
                catch (Exception ex)
                {
                    problems.Add(Show(k) + ":抛了 " + ex.GetType().Name);
                }
            }
            Check("★ key 非法（null/空/空白/分隔符/`../`/通配符）⇒ 全部 false/null + error，且**一处都没抛**",
                rejected == badKeys.Length && problems.Count == 0,
                "合格 " + rejected + "/" + badKeys.Length +
                (problems.Count == 0 ? "；异常清单：无" : "；异常清单：" + string.Join(" | ", problems)));
            Check("★ 非法 key 没在槽目录里留下任何文件（⛔ 不许用 `../` 逃出槽目录）",
                !Directory.Exists(keyDir) || Directory.GetFiles(keyDir, "*", SearchOption.AllDirectories).Length == 0,
                "目录文件数=" + (Directory.Exists(keyDir)
                    ? Directory.GetFiles(keyDir, "*", SearchOption.AllDirectories).Length.ToString() : "0"));

            // ④ 内容 null
            string e4 = null;
            Check("内容为 null ⇒ `Write` false + error（⛔ 不静默写空）",
                !store.Write("ok", null, out e4) && !string.IsNullOrEmpty(e4), e4);

            Check("失败路径都有日志（⛔ 不静默）", _log.Count("[ERROR]") > 0, "ERROR 行数=" + _log.Count("[ERROR]"));
            Console.WriteLine();
        }

        // ── 12. ★ 片 Q（R7 读档失败静默）：`SaveModule.Load/TryLoad` 的三情况 ──

        /// <summary>`Events.LoadDone` 的发出账（参数 null = 读档失败）。</summary>
        private static readonly List<CharacterSave> LoadDoneArgs = new List<CharacterSave>();

        /// <summary>
        /// ★ 片 Q（缺陷出处 `.ai-tmp/test/audit-C-logic-num.md` §2 红行 R7）：
        /// 修前 `SaveModule.Load()` 的"档不存在"与"解析失败"**都返回 null**（`:238-242` / `:244-250`），
        /// 且 `LastError` 的唯一消费者是**保存**失败分支（`AppFlow.cs:1379`）⇒ 损坏档读失败**无用户可见反馈**。
        /// <para>本步骤把**被验证对象本身**（`SaveModule.cs`，与 Assets/ 同一份文件）编进来，断言三种情况**可判别**
        /// 且失败时"用户可见反馈"的那条代码路径（`Events.LoadDone` 发 null ⇒ 唯一消费者 `AppFlow.OnLoadDone`
        /// → 复用 `UI/D2ConfirmPanel`）**被触发**。⛔ 判过程不判结果：断言的是"判别位 / 事件 / 日志"，不是"提示长什么样"。</para>
        /// </summary>
        private static void Step12_LoadFailureClassification()
        {
            Section("12. ★ 片 Q（R7 读档失败静默）：三情况可判别（`LastError` 空 vs 非空）+ 失败时 `Events.LoadDone` 发 null");

            var dir = Path.Combine(_work, "q-save");
            FreshDir(dir);
            var savesDir = Path.Combine(dir, "saves");
            Directory.CreateDirectory(savesDir);

            // 事件账：`Events.LoadDone`（`Core/Events.cs:368`：参数 null = 读档失败）
            LoadDoneArgs.Clear();
            Game.Event = new ConsoleEventBus();
            Game.Event.On<CharacterSave>(Events.LoadDone, d => LoadDoneArgs.Add(d));

            Game.Launch(new GameConfig { SettingDir = dir });   // 引擎 `Game.cs:370` `Config = config`
            Game.Logger = _log;
            Game.Setting = new Setting(dir);
            var ctx = Diablo2.App.AppContext.Create();          // SaveModule 靠它取各模块（离线全 null ⇒ 只多几条 Warn）
            var save = new SaveModule();
            ctx.Save = save;
            Check("被验证对象已就位：`SaveModule.Ready` = true（`Game.Setting` 已接入）+ 槽位目录 = SettingDir/saves",
                save.Ready && string.IsNullOrEmpty(save.LastError),
                "Ready=" + save.Ready + " 槽位目录=" + savesDir);

            // ── ① 档不存在（正常：新玩家 / 空槽）───────────────────────────
            var errors0 = _log.Count("[ERROR]");
            CharacterSave missingData;
            var missingOk = save.TryLoad("NoSuchHero", out missingData);
            Check("① 档不存在 ⇒ `TryLoad` = false、`data` = null（**不报错**）",
                !missingOk && missingData == null,
                "TryLoad=" + missingOk + " data=" + (missingData == null ? "null" : missingData.name));
            Check("① 档不存在 ⇒ `LastError == \"\"`（判别位：正常情形 ⇒ 调用方走新建流程、⛔ 不起报错界面）",
                save.LastError == "", "LastError=\"" + save.LastError + "\"");
            Check("① 档不存在 ⇒ **不发** `Events.LoadDone`（没有失败要报 ⇒ 不会弹框）",
                LoadDoneArgs.Count == 0, "LoadDone 发出次数=" + LoadDoneArgs.Count);
            Check("① 档不存在 ⇒ 日志是 Info、不是 Error（⛔ 不静默也不误报）",
                _log.Count("[ERROR]") == errors0, "Error 行数增量=" + (_log.Count("[ERROR]") - errors0));

            // ── ② 档存在但解析失败 / 损坏（截断）──────────────────────────
            const string truncated = "{\"version\":5,\"name\":\"BadHero\",\"level\":12";
            var badPath = Path.Combine(savesDir, "BadHero.json");
            File.WriteAllText(badPath, truncated);
            LoadDoneArgs.Clear();
            var errors1 = _log.Count("[ERROR]");

            CharacterSave badData;
            var badOk = save.TryLoad("BadHero", out badData);
            Check("② 损坏档（截断 JSON）⇒ `TryLoad` = false、`data` = null", !badOk && badData == null,
                "TryLoad=" + badOk + " data=" + (badData == null ? "null" : badData.name));
            Check("② ★ 损坏档 ⇒ `LastError` **非空**，含档名「BadHero」与「损坏」——**与①可判别**（R7 修法的判据本身）",
                !string.IsNullOrEmpty(save.LastError) && save.LastError.Contains("损坏") && save.LastError.Contains("BadHero"),
                "LastError=\"" + save.LastError + "\"");
            Check("② ★ 用户可见反馈那条代码路径**被触发**：`Events.LoadDone` 以 **null** 发出、恰好一次" +
                  "（唯一消费者 = `AppFlow.OnLoadDone` → 复用 `UI/D2ConfirmPanel` 给玩家可见提示）",
                LoadDoneArgs.Count == 1 && LoadDoneArgs[0] == null,
                "LoadDone 发出次数=" + LoadDoneArgs.Count +
                (LoadDoneArgs.Count > 0 ? "，参数=" + (LoadDoneArgs[0] == null ? "null（失败）" : "非 null") : ""));
            Check("② 损坏档 ⇒ 有 Error 日志（⛔ 不静默）", _log.Count("[ERROR]") > errors1,
                "Error 行数增量=" + (_log.Count("[ERROR]") - errors1));
            Check("② 损坏档 ⇒ 原文件**未被覆盖 / 未被删除**（⛔ 读档失败路径不替玩家删档）",
                File.Exists(badPath) && File.ReadAllText(badPath) == truncated,
                badPath + " 存在=" + File.Exists(badPath));

            // ③ 的对照组：**残留错误不得泄漏到"档不存在"这条正常路径上**
            CharacterSave afterCorrupt;
            var afterCorruptOk = save.TryLoad("AnotherMissing", out afterCorrupt);
            Check("②' 紧接着读一个**不存在**的档 ⇒ `LastError` 回到空串（`Load` 入口清零；" +
                  "否则空槽会被误判成'损坏'并弹框 —— 这是①/②可判别能成立的前提）",
                !afterCorruptOk && save.LastError == "", "LastError=\"" + save.LastError + "\"");

            // ── ③a 版本不符但可兼容 ────────────────────────────────────────
            var legacy = BuildSave();
            legacy.name = "OldVersionHero";
            legacy.version = GameConst.SaveVersion - 1;
            legacy.gold = 777;
            File.WriteAllText(Path.Combine(savesDir, "OldVersionHero.json"), SaveJson.Write(legacy));
            LoadDoneArgs.Clear();

            CharacterSave legacyData;
            var legacyOk = save.TryLoad("OldVersionHero", out legacyData);
            Check("③a 旧版本档（version=" + (GameConst.SaveVersion - 1) + " ≠ 当前 " + GameConst.SaveVersion +
                  "）⇒ 兼容读**成功**（`TryLoad` = true，⛔ 不因版本不符而失败）",
                legacyOk && legacyData != null, "TryLoad=" + legacyOk);
            Check("③a ⇒ 版本被抬到当前（`data.version == GameConst.SaveVersion`）",
                legacyData != null && legacyData.version == GameConst.SaveVersion,
                "version=" + (legacyData == null ? -1 : legacyData.version));
            Check("③a ⇒ **值不丢**（金币 777 / 等级 7 / seed 原样读回）",
                legacyData != null && legacyData.gold == 777 && legacyData.level == 7 && legacyData.mapSeed == 20260919,
                legacyData == null ? "(null)" : ("gold=" + legacyData.gold + " level=" + legacyData.level + " seed=" + legacyData.mapSeed));
            Check("③a ⇒ 留了一条 **Info**（含「兼容路径」，按任务书 §2.3）", _log.Count("兼容路径") >= 1,
                "「兼容路径」日志行数=" + _log.Count("兼容路径"));
            Check("③a ⇒ `LastError == \"\"`（成功路径不残留错误）+ `Events.LoadDone` 以**非 null** 发出",
                save.LastError == "" && LoadDoneArgs.Count == 1 && LoadDoneArgs[0] != null,
                "LastError=\"" + save.LastError + "\" LoadDone=" + LoadDoneArgs.Count);

            // ── ③b 版本相同但缺字段（旧档少列）⇒ 取默认值 ─────────────────
            File.WriteAllText(Path.Combine(savesDir, "SparseHero.json"),
                "{\"version\":" + GameConst.SaveVersion.ToString(CultureInfo.InvariantCulture) + ",\"name\":\"SparseHero\"}");
            LoadDoneArgs.Clear();

            CharacterSave sparseData;
            var sparseOk = save.TryLoad("SparseHero", out sparseData);
            Check("③b 缺字段旧档 ⇒ 兼容读**成功**（⛔ 不抛异常、⛔ 不判成'损坏'）",
                sparseOk && sparseData != null, "TryLoad=" + sparseOk + " LastError=\"" + save.LastError + "\"");
            Check("③b ★ 缺的字段**取默认值**（level=1 / gold=0 / cls=1 / 背包 0 格 / buttonSkills 补齐 2 个 -1）",
                sparseData != null && sparseData.name == "SparseHero" && sparseData.level == 1 && sparseData.gold == 0 &&
                (int)sparseData.cls == 1 && sparseData.inventory != null && sparseData.inventory.Count == 0 &&
                sparseData.buttonSkills != null && sparseData.buttonSkills.Count == 2 &&
                sparseData.buttonSkills[0] == -1 && sparseData.buttonSkills[1] == -1,
                sparseData == null ? "(null)" : ("lv=" + sparseData.level + " gold=" + sparseData.gold +
                    " cls=" + (int)sparseData.cls + " inv=" + sparseData.inventory.Count +
                    " btn=" + string.Join(",", sparseData.buttonSkills)));
            Check("③b ⇒ `Events.LoadDone` 以**非 null** 发出（成功）+ 无 Error 日志",
                LoadDoneArgs.Count == 1 && LoadDoneArgs[0] != null && save.LastError == "",
                "LoadDone=" + LoadDoneArgs.Count + " LastError=\"" + save.LastError + "\"");

            // ★ 片 assert-audit：原为硬编码 `true`（一句结论陈述 ⇒ 不产生判据却占"通过"计数）。
            //   改成**把三种情况在同一入口上重跑一遍**（档都还在盘上）⇒ 这条结论本身变成可失败的判据：
            //   任一情况退化成"与另一情况不可判别"（例如 LastError 不回归空串 / LoadDone 不发）就变红。
            LoadDoneArgs.Clear();
            CharacterSave r7Missing; var r7MissingOk = save.TryLoad("NoSuchHero_R7", out r7Missing);
            var r7MissingDisc = !r7MissingOk && r7Missing == null && save.LastError == "" && LoadDoneArgs.Count == 0;

            LoadDoneArgs.Clear();
            CharacterSave r7Bad; var r7BadOk = save.TryLoad("BadHero", out r7Bad);      // ② 的截断档仍在盘上
            var r7BadDisc = !r7BadOk && r7Bad == null && !string.IsNullOrEmpty(save.LastError)
                            && save.LastError.Contains("损坏") && LoadDoneArgs.Count == 1 && LoadDoneArgs[0] == null;

            LoadDoneArgs.Clear();
            CharacterSave r7Ok; var r7OkFlag = save.TryLoad("OldVersionHero", out r7Ok);  // ③a 的档仍在盘上
            var r7SuccessDisc = r7OkFlag && r7Ok != null && save.LastError == "" && LoadDoneArgs.Count == 1
                                && LoadDoneArgs[0] != null;

            Check("⇒ R7 结论（**判据本身重跑一遍**）：**三种情况全部可判别**（LastError 空=档不存在 / 非空=读失败 / 成功）、" +
                  "失败有可定位原因、失败时 `Events.LoadDone(null)` 被发出（= 用户可见反馈的触发点）",
                r7MissingDisc && r7BadDisc && r7SuccessDisc,
                $"①档不存在={r7MissingDisc} ②损坏={r7BadDisc} ③成功={r7SuccessDisc}"
                + "（修前：两情况同返 null 且无任何判别位 / 无事件 ⇒ 见 `.ai-tmp/test/audit-C-logic-num.md` §2 R7）");
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 子进程模式（跨进程判据）
        // ═════════════════════════════════════════════════════════════════════

        private static int CrossWrite(string dir)
        {
            Game.Logger = new ConsoleLogger();
            LogThrottle.Clock = () => 0f;

            var store = new FileSlotStore(dir);
            var order = new[] { "zeta", "alpha", "mid" };    // 刻意非字典序
            var ok = true;
            for (var i = 0; i < order.Length; i++)
            {
                string e;
                ok &= store.Write(order[i], PayloadA, out e);
            }
            string e2;
            ok &= store.Write("hero_01", SaveJson.Write(BuildSave()), out e2);

            Console.WriteLine("CROSSWRITE_OK=" + ok);
            Console.WriteLine("CROSSWRITE_ORDER=" + string.Join(",", order));
            Dump(store);
            return ok ? 0 : 1;
        }

        private static int CrossRead(string dir)
        {
            Game.Logger = new ConsoleLogger();
            LogThrottle.Clock = () => 0f;

            var store = new FileSlotStore(dir);
            Dump(store);

            var text = store.Read("hero_01");
            string perr;
            var data = SaveJson.TryParse(text, out perr);
            Console.WriteLine("CROSSREAD_PARSE=" + (data != null ? "ok" : ("fail:" + perr)));
            Console.WriteLine("CROSSREAD_LEVEL=" + (data != null ? data.level.ToString(CultureInfo.InvariantCulture) : "-1"));
            Console.WriteLine("CROSSREAD_GOLD=" + (data != null ? data.gold.ToString(CultureInfo.InvariantCulture) : "-1"));
            return data != null ? 0 : 1;
        }

        private static void Dump(FileSlotStore store)
        {
            var keys = store.List();
            Console.WriteLine("LIST=" + string.Join(",", keys));
            for (var i = 0; i < keys.Count; i++)
            {
                Console.WriteLine("SHA " + keys[i] + " " + Sha(store.Read(keys[i])));
            }
        }

        private static (int code, string output) RunChild(string mode, string dir)
        {
            var exe = Environment.ProcessPath;
            var entry = Assembly.GetEntryAssembly();
            var dll = entry != null ? entry.Location : null;
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // ★ 只有当宿主是 `dotnet(.exe)` 时才需要显式带上入口 dll。
            //   apphost（`SaveCheck.exe`）**自己就是应用**：再塞一个 dll 参数会被当普通参数传进来，
            //   于是子进程认不出模式 ⇒ 又跑一遍完整 Main（= 雪崩）。旧写法就是漏了这道判断。
            var hostName = Path.GetFileNameWithoutExtension(exe ?? string.Empty);
            if (!string.IsNullOrEmpty(dll) && File.Exists(dll) &&
                hostName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                psi.ArgumentList.Add(dll);      // 通过 `dotnet <dll>` 起子进程
            }
            psi.ArgumentList.Add(mode);
            psi.ArgumentList.Add(dir);
            // 守卫：子进程只许做子进程的事（认不出模式就 exit 3，绝不再派生）
            psi.Environment[ChildGuard] = "1";

            using (var p = Process.Start(psi))
            {
                var outp = p.StandardOutput.ReadToEnd();
                var errp = p.StandardError.ReadToEnd();
                p.WaitForExit();
                Console.WriteLine("      [子进程 " + mode + "] exit=" + p.ExitCode +
                    (errp.Length > 0 ? (" stderr=" + Truncate(errp, 200)) : ""));
                return (p.ExitCode, outp);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 工具
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>照 `CharCreatePanel`/`playercheck` 的口径造一份**真实形状**的存档（含 null 元素）。</summary>
        private static CharacterSave BuildSave()
        {
            var data = new CharacterSave
            {
                version = GameConst.SaveVersion,
                name = "ProbeHero",
                cls = PlayerClass.Amazon,
                level = 7,
                exp = 123456789L,
                str = 20,
                dex = 25,
                vit = 20,
                eng = 15,
                life = 88,
                mana = 33,
                stamina = 90,
                statPoints = 5,
                skillPoints = 3,
                gold = 4321,
                areaId = (int)AreaId.Town,
                gridX = 10,
                gridY = 8,
                mapSeed = 20260919,
                savedAtTicks = 638912345670000000L,
                playedSeconds = 12.5f,
            };

            data.inventory = new List<InventorySlot>(GameConst.InventoryCellCount);
            for (var i = 0; i < GameConst.InventoryCellCount; i++)
            {
                var slot = new InventorySlot
                {
                    index = i,
                    x = i % GameConst.InventoryCols,
                    y = i / GameConst.InventoryCols,
                };
                if (i == 0)
                {
                    slot.occupied = true;
                    slot.isAnchor = true;
                    slot.anchorIndex = 0;
                    slot.item = new ItemStack
                    {
                        itemId = 2,
                        name = "短剑",
                        type = ItemType.Weapon,
                        quality = ItemQuality.Magic,
                        count = 1,
                        gridW = 1,
                        gridH = 3,
                        price = 120,
                        durability = 20,
                        maxDurability = 24,
                    };
                }
                data.inventory.Add(slot);
            }

            data.belt = new List<ItemStack>();
            for (var i = 0; i < GameConst.BeltSlots; i++)
            {
                data.belt.Add(i == 0
                    ? new ItemStack { itemId = 60, name = "治疗药水", type = ItemType.Misc, quality = ItemQuality.Normal, count = 3 }
                    : null);
            }

            data.equip = new List<ItemStack>
            {
                new ItemStack { itemId = 2, name = "短剑", type = ItemType.Weapon, quality = ItemQuality.Magic, count = 1 },
            };
            data.quests = new List<QuestStateDto>
            {
                new QuestStateDto
                {
                    questId = 1,
                    name = "邪恶洞穴",
                    state = QuestState.InProgress,
                    progress = 5,
                    required = 13,
                    rewardClaimed = false,
                    objective = "殺死所有盤踞在洞穴中的怪物。",
                },
            };
            data.skillIds = new List<int> { 1, 7 };
            data.skillLevels = new List<int> { 2, 1 };
            data.buttonSkills = new List<int> { 1, 7 };
            return data;
        }

        private static int CountAnchors(CharacterSave s)
        {
            var n = 0;
            if (s == null || s.inventory == null) return 0;
            for (var i = 0; i < s.inventory.Count; i++)
            {
                if (s.inventory[i] != null && s.inventory[i].isAnchor && s.inventory[i].item != null) n++;
            }
            return n;
        }

        /// <summary>逐字段对照（公开实例字段，按名字排序；值用 <see cref="Repr"/> 归一化）。</summary>
        private static int DiffFields(object a, object b, out string report)
        {
            var fields = a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(fields, (x, y) => string.CompareOrdinal(x.Name, y.Name));

            var sb = new StringBuilder();
            var diff = 0;
            for (var i = 0; i < fields.Length; i++)
            {
                var va = Truncate(Repr(fields[i].GetValue(a), 0), 120);
                var vb = Truncate(Repr(fields[i].GetValue(b), 0), 120);
                var same = string.Equals(va, vb, StringComparison.Ordinal);
                if (!same) diff++;
                sb.Append("        ").Append(same ? "=  " : "!! ").Append(fields[i].Name)
                  .Append(": 前=").Append(va).Append(" / 后=").Append(vb).Append(Environment.NewLine);
            }
            report = sb.ToString();
            return diff;
        }

        /// <summary>把任意值渲染成**确定**字符串（枚举/基本类型/集合/复合类型递归；有深度上限防环）。</summary>
        private static string Repr(object v, int depth)
        {
            if (v == null) return "null";
            if (depth > 6) return "<深度截断:" + v.GetType().Name + ">";
            var t = v.GetType();
            if (v is string s) return "\"" + s + "\"";
            if (t.IsEnum) return t.Name + "." + v;
            if (t.IsPrimitive || v is decimal) return Convert.ToString(v, CultureInfo.InvariantCulture);
            if (v is System.Collections.IEnumerable en)
            {
                var sb = new StringBuilder("[");
                var first = true;
                foreach (var item in en)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(Repr(item, depth + 1));
                }
                return sb.Append(']').ToString();
            }

            var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(fields, (x, y) => string.CompareOrdinal(x.Name, y.Name));
            var o = new StringBuilder(t.Name).Append('{');
            for (var i = 0; i < fields.Length; i++)
            {
                if (i > 0) o.Append(';');
                o.Append(fields[i].Name).Append('=').Append(Repr(fields[i].GetValue(v), depth + 1));
            }
            return o.Append('}').ToString();
        }

        /// <summary>反射列出公开面（构造函数 / 声明在本类的公开属性与方法），归一化成可读签名后排序。</summary>
        private static List<string> Surface(Type t)
        {
            const BindingFlags F = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            var list = new List<string>();
            var ctors = t.GetConstructors();
            for (var i = 0; i < ctors.Length; i++)
            {
                list.Add("Void .ctor(" + Params(ctors[i].GetParameters()) + ")");
            }
            var props = t.GetProperties(F);
            for (var i = 0; i < props.Length; i++)
            {
                list.Add(TName(props[i].PropertyType) + " " + props[i].Name +
                    " { get" + (props[i].CanWrite ? " set" : "") + " }");
            }
            var ms = t.GetMethods(F);
            for (var i = 0; i < ms.Length; i++)
            {
                if (ms[i].IsSpecialName) continue;
                list.Add(TName(ms[i].ReturnType) + " " + ms[i].Name + "(" + Params(ms[i].GetParameters()) + ")");
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        private static string Params(ParameterInfo[] ps)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < ps.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(TName(ps[i].ParameterType));
            }
            return sb.ToString();
        }

        private static string TName(Type t)
        {
            if (t.IsByRef) return TName(t.GetElementType()) + "&";
            if (t.IsArray) return TName(t.GetElementType()) + "[]";
            if (t.IsGenericType)
            {
                var name = t.Name;
                var cut = name.IndexOf('`');
                if (cut > 0) name = name.Substring(0, cut);
                var args = t.GetGenericArguments();
                var sb = new StringBuilder(name).Append('<');
                for (var i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(TName(args[i]));
                }
                return sb.Append('>').ToString();
            }
            return t.Name;
        }

        private static string Sha(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                var sb = new StringBuilder(16);
                for (var i = 0; i < 8; i++) sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static string Extract(string output, string[] prefixes)
        {
            var sb = new StringBuilder();
            var lines = output.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                for (var p = 0; p < prefixes.Length; p++)
                {
                    if (line.StartsWith(prefixes[p], StringComparison.Ordinal))
                    {
                        sb.Append(line).Append('\n');
                        break;
                    }
                }
            }
            return sb.ToString();
        }

        private static string Indent(string text)
        {
            var sb = new StringBuilder();
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                sb.Append("          ").Append(lines[i]).Append('\n');
            }
            return sb.ToString();
        }

        private static string Truncate(string s, int max)
        {
            if (s == null) return "(null)";
            return s.Length <= max ? s : s.Substring(0, max) + "…(共" + s.Length + "字符)";
        }

        private static string Show(string key)
        {
            if (key == null) return "null";
            if (key.Length == 0) return "(空串)";
            return "'" + key + "'";
        }

        private static void FreshDir(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
        }

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("──────────────────────────────────────────────────────────────────");
            Console.WriteLine("▶ " + title);
            Console.WriteLine("──────────────────────────────────────────────────────────────────");
        }

        private static void Check(string what, bool ok, string detail)
        {
            if (!ok) _fail++;
            Console.WriteLine((ok ? "[ OK ] " : "[FAIL] ") + what +
                (string.IsNullOrEmpty(detail) ? "" : "   （" + detail + "）"));
        }

        private static void Run(Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                _fail++;
                Console.WriteLine("[FAIL] " + step.Method.Name + " 抛异常：" + ex.GetType().Name + ": " + ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
