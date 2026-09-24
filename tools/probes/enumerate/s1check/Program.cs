// ─────────────────────────────────────────────────────────────────────────────
//
// 断言对象 = `策划/状态矩阵.tsv` 的 S1 20 条边界行（10 张表 × `id=空` / `id=max+1`）：
//   判据（与本项目别的维度同一口径）= **越界回落默认值（null）+ 打一次 Warn**。
//   出处：引擎 `clover-client-unity-engine/Runtime/Data/CloverTable.cs:241`（`Get<T>(table,int)`）
//         的 `!table.ByInt.TryGetValue(...)` 分支 ⇒ `:249 WarnThrottled` + `:251 return null`；
//         同名分支在 `:258`（string 主键）⇒ `:266 WarnThrottled` + `:269 return null`。
//
// 本断言**不判**"值与官方 txt 相等"（那要 T1 官方 txt 载体，本机没有 ⇒ 那 821 行仍留空）。
// 日志用 shim 自带的 `ConsoleLogger`（内存副本 + 控制台），计数走 `ConsoleLogger.CountOf`。
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CloverEngine;

namespace S1Check
{
    /// <summary>占位行类型：越界分支在 `Materialize&lt;T&gt;` **之前**返回 ⇒ 类型是什么都行。</summary>
    internal sealed class ProbeRow
    {
        public int Id;
    }

    internal static class Program
    {
        private static int _fail;

        private static void Check(string name, bool ok, string detail)
        {
            Console.WriteLine((ok ? "    [ OK ] " : "    [FAIL] ") + name + "   (" + detail + ")");
            if (!ok) _fail++;
        }

        private static string FindRoot()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null)
            {
                if (Directory.Exists(Path.Combine(d.FullName, "client/Assets/StreamingAssets/Table")))
                    return d.FullName;
                d = d.Parent;
            }
            throw new InvalidOperationException("找不到项目根（含 client/Assets/StreamingAssets/Table 的目录）");
        }

        private static string Tail(string line)
        {
            var i = line.IndexOf("] [", StringComparison.Ordinal);
            var j = line.IndexOf("] ", StringComparison.Ordinal);
            return j >= 0 ? line.Substring(j + 2) : line;
        }

        private static int Main(string[] argv)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { /* 非交互控制台 */ }
            var root = argv.Length > 0 && Directory.Exists(argv[0]) ? argv[0] : FindRoot();
            var tableDir = Path.Combine(root, "client/Assets/StreamingAssets/Table");

            Game.Logger = new ConsoleLogger();
            // 注入时钟 ⇒ 限频行为确定可复现（`LogThrottle.cs:87` 的 Clock / :278 的 Now）。
            LogThrottle.Clock = () => 0f;
            LogThrottle.Reset();
            ConsoleLogger.Clear();

            var err = CloverTable.LoadAll(
                Path.Combine(root, "client/Assets/StreamingAssets"),
                Path.Combine(root, "client/Assets"));

            Console.WriteLine("=== S1Check：配表越界边界（回落默认值 + 一次 Warn）===");
            Check("配表目录可解析 + 10 张表加载成功", err == null && CloverTable.Dir != null,
                err == null ? ("dir=" + CloverTable.Dir) : ("err=" + err));
            if (err != null) { Console.WriteLine("S1CHECK_SUMMARY FAIL=" + _fail); return 1; }

            var files = new List<string>(Directory.GetFiles(tableDir, "*.tsv"));
            files.Sort(StringComparer.Ordinal);
            Check("表数 = 10（每表 2 条边界 ⇒ 20 条）", files.Count == 10, "表数=" + files.Count);

            var rows = 0;
            foreach (var f in files)
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var lines = File.ReadAllLines(f);
                var keys = new List<string>();
                for (var i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrEmpty(lines[i])) continue;
                    keys.Add(lines[i].Split('\t')[0]);
                }

                var intKey = keys.Count > 0;
                var maxInt = 0;
                for (var i = 0; i < keys.Count; i++)
                {
                    int v;
                    if (int.TryParse(keys[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                    {
                        if (v > maxInt) maxInt = v;
                    }
                    else intKey = false;
                }

                if (intKey)
                {
                    // ── 边界 A：id=空 / 首行之前（`TableParsers.ToInt("")` = 0 ⇒ 落到 0）──────
                    var tag0 = "[WARN ] [CloverTable] 配表 " + name + " 里没有 int 主键 0";
                    var before = ConsoleLogger.Lines.Count;
                    var r1 = CloverTable.Get<ProbeRow>(name, 0);
                    var r2 = CloverTable.Get<ProbeRow>(name, 0);
                    var r3 = CloverTable.Get<ProbeRow>(name, 0);
                    var w = ConsoleLogger.CountOf(tag0);
                    var extra = ConsoleLogger.Lines.Count - before - w;
                    Check(name + " · id=空(ToInt(\"\")→0) ⇒ 回落默认值 null + 恰一次 Warn",
                        r1 == null && r2 == null && r3 == null && w == 1 && extra == 0,
                        "3 次查询全 null=" + (r1 == null && r2 == null && r3 == null)
                        + "；Warn 条数=" + w + "（同主键限频 ⇒ 重复查不刷屏）；非 Warn 行=" + extra
                        + " ｜ 原文=" + (w > 0 ? Tail(ConsoleLogger.Lines[ConsoleLogger.Lines.Count - 1]) : "—"));

                    // ── 边界 B：id=max+1（超界）─────────────────────────────────────
                    var over = maxInt + 1;
                    var tag1 = "[WARN ] [CloverTable] 配表 " + name + " 里没有 int 主键 " + over;
                    before = ConsoleLogger.Lines.Count;
                    var q1 = CloverTable.Get<ProbeRow>(name, over);
                    var q2 = CloverTable.Get<ProbeRow>(name, over);
                    var w2 = ConsoleLogger.CountOf(tag1);
                    extra = ConsoleLogger.Lines.Count - before - w2;
                    Check(name + " · id=max+1(" + over + ") ⇒ 回落默认值 null + 恰一次 Warn",
                        q1 == null && q2 == null && w2 == 1 && extra == 0,
                        "id=" + over + "（表内最大 id=" + maxInt + "）；2 次查询全 null=" + (q1 == null && q2 == null)
                        + "；Warn 条数=" + w2 + "；非 Warn 行=" + extra
                        + " ｜ 原文=" + (w2 > 0 ? Tail(ConsoleLogger.Lines[ConsoleLogger.Lines.Count - 1]) : "—"));
                    rows += 2;
                }
                else
                {
                    // string 主键表（Treasureclass）：空键 + 「末键之后」的不存在键各一次
                    var lastKey = keys.Count > 0 ? keys[keys.Count - 1] : "";
                    var tag0 = "[WARN ] [CloverTable] 配表 " + name + " 里没有 string 主键 \"\"";
                    var before = ConsoleLogger.Lines.Count;
                    var r1 = CloverTable.Get<ProbeRow>(name, "");
                    var r2 = CloverTable.Get<ProbeRow>(name, "");
                    var w = ConsoleLogger.CountOf(tag0);
                    var extra = ConsoleLogger.Lines.Count - before - w;
                    Check(name + " · id=空(string 主键 \"\") ⇒ 回落默认值 null + 恰一次 Warn",
                        r1 == null && r2 == null && w == 1 && extra == 0,
                        "string 主键表（" + keys.Count + " 行，末键=\"" + lastKey + "\"）；2 次查询全 null="
                        + (r1 == null && r2 == null) + "；Warn 条数=" + w + "；非 Warn 行=" + extra
                        + " ｜ 原文=" + (w > 0 ? Tail(ConsoleLogger.Lines[ConsoleLogger.Lines.Count - 1]) : "—"));

                    var beyond = lastKey + "~";
                    var tag1 = "[WARN ] [CloverTable] 配表 " + name + " 里没有 string 主键 \"" + beyond + "\"";
                    before = ConsoleLogger.Lines.Count;
                    var q1 = CloverTable.Get<ProbeRow>(name, beyond);
                    var q2 = CloverTable.Get<ProbeRow>(name, beyond);
                    var w2 = ConsoleLogger.CountOf(tag1);
                    extra = ConsoleLogger.Lines.Count - before - w2;
                    Check(name + " · id=max+1(末键之后 \"" + beyond + "\") ⇒ 回落默认值 null + 恰一次 Warn",
                        q1 == null && q2 == null && w2 == 1 && extra == 0,
                        "2 次查询全 null=" + (q1 == null && q2 == null) + "；Warn 条数=" + w2 + "；非 Warn 行=" + extra
                        + " ｜ 原文=" + (w2 > 0 ? Tail(ConsoleLogger.Lines[ConsoleLogger.Lines.Count - 1]) : "—"));
                    rows += 2;
                }
            }

            Check("边界行总数 = 20（10 表 × 2）", rows == 20, "rows=" + rows);
            Console.WriteLine("S1CHECK_SUMMARY FAIL=" + _fail);
            return _fail == 0 ? 0 : 1;
        }
    }
}
