// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · tools/probes/hosts/tableverify/Program.cs
//
// `_table_discard/verify/TableVerify.csproj`（全仓 `TableVerify.csproj` 0 命中）。
// 为什么只编「不引引擎」的那一半产物 ⇒ 见 TableVerify.csproj 的文件头（三条理由）。
//
// 判据四项：
//   ① 编译自检：打表产物（除唯一引引擎的 `TableLoader.cs`）能否独立 dotnet build；
//   ② 数值判据：从**真实运行时 tsv**（`client/Assets/StreamingAssets/Table`）读 class_c，
//      逐职业断言 classcols 片新增的 3 列（hp_add / base_stamina / block_factor）== 官方
//      `charstats.txt` 原值（= 该片删掉的代码常量的逐条值）；
//   ③ **退化样本**：把 Class.tsv 的 3 个新列**摘掉**（还原成改动前的 19 列）再读 ⇒
//      断言必须**不**等于官方值（证明 ② 真的在判"这三列"，不是恒真）；
//   ④ 无附带损伤：其余 10 张表行数不变（防"重打表"把别的表带歪）。
//
// 退出码：全部通过 0 / 任一失败 1（`tools/probes/hosts/run_all_hosts.ps1` 靠它判 PASS/FAIL）。
// 本宿主不碰 Unity、不碰引擎、不进 Play（纯离线、秒级）。
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.IO;
using System.Text;
using Table;

namespace TableVerify
{
    internal static class Program
    {
        private static int _pass;
        private static int _fail;

        private static void Check(string what, bool ok, string detail)
        {
            if (ok) { _pass++; Console.WriteLine("[ OK ] " + what + "   (" + detail + ")"); }
            else { _fail++; Console.WriteLine("[FAIL] " + what + "   (" + detail + ")"); }
        }

        /// <summary>
        /// 从宿主自己的可执行目录向上找「含 client/Assets 的那一层」= 仓库根。
        /// 与 playercheck / corecheck / fullcheck / savecheck / uicheck **同一套写法**
        /// （不写死相对层数：`run_all_hosts.ps1` 用 Push-Location 驱动，写死会随目录深度失效 ——
        /// </summary>
        private static string ResolveProjectRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "client", "Assets"))) return dir.FullName;
                dir = dir.Parent;
            }
            Console.WriteLine("[warn] 未从可执行目录向上找到含 client/Assets 的仓库根 ⇒ 本判据无法判定");
            return null;
        }

        private static int Main()
        {
            var root = ResolveProjectRoot();
            if (root == null) { Console.WriteLine("[FAIL] 仓库根未解析出来 ⇒ 判据不成立（⛔ 不假装通过）"); return 1; }

            var tsvDir = Path.Combine(root, "client", "Assets", "StreamingAssets", "Table");
            Console.WriteLine("tsv 目录: " + tsvDir);
            if (!Directory.Exists(tsvDir)) { Console.WriteLine("[FAIL] tsv 目录不存在"); return 1; }
            Console.WriteLine();

            Tables.Default.LoadAll(tsvDir);

            Console.WriteLine("── ② class_c 新增 3 列 ↔ 官方 charstats.txt 原值 ──");
            // 官方出处：`原版资源/参考工程_Diablerie/d2lod1.10txt/data/global/excel/charstats.txt` 第 2..6 行
            //   （= convert.py 的默认输入；与 `原版资源/d2raw/…/charstats.txt` 逐字节相同，SHA256 BFD28714…BFF51F）
            //   第 8 列 hpadd（5 职业同为 30）/ 第 7 列 stamina / 第 32 列 BlockFactor
            //   ⇒ 下面三个数组 = **官方原值**，也是 classcols 片从代码里删掉的那批常量的逐条值。
            var expHpAdd = new[] { 30, 30, 30, 30, 30 };
            var expStam = new[] { 84, 74, 79, 89, 92 };
            var expBlock = new[] { 25, 20, 20, 30, 25 };
            var names = new[] { "Amazon", "Sorceress", "Necromancer", "Paladin", "Barbarian" };

            Check("class_c 读到 5 行", Tables.Default.Class.Count == 5, "Count=" + Tables.Default.Class.Count);

            var allOk = true;
            var detail = new StringBuilder();
            for (var id = 1; id <= 5; id++)
            {
                var r = Tables.Default.Class.Get(id);
                if (r == null) { allOk = false; detail.Append(id + ":null "); continue; }
                var ok = r.HpAdd == expHpAdd[id - 1] && r.BaseStamina == expStam[id - 1] && r.BlockFactor == expBlock[id - 1];
                allOk &= ok;
                detail.Append(id + " " + names[id - 1] + " hp_add=" + r.HpAdd + "/" + expHpAdd[id - 1]
                              + " base_stamina=" + r.BaseStamina + "/" + expStam[id - 1]
                              + " block_factor=" + r.BlockFactor + "/" + expBlock[id - 1] + (ok ? " ✓; " : " ✗; "));
            }
            Check("5 职业 hp_add/base_stamina/block_factor = 官方 charstats 原值", allOk, detail.ToString().Trim());

            // 顺带证明"没把别的列挤歪"：老列仍逐列等于原值（Amazon 抽样）
            var a = Tables.Default.Class.Get(1);
            Check("老列未被挤位（Amazon: str/dex/vit/eng/to_hit_factor/start_skill）",
                a != null && a.Str == 20 && a.Dex == 25 && a.Vit == 20 && a.Eng == 15
                && a.ToHitFactor == 5 && a.LifePerVit == 3f && a.StamPerVit == 1f && a.StartSkill == "",
                a == null ? "row null" : string.Format("str={0} dex={1} vit={2} eng={3} thf={4} lpvit={5} spvit={6} skill='{7}'",
                    a.Str, a.Dex, a.Vit, a.Eng, a.ToHitFactor, a.LifePerVit, a.StamPerVit, a.StartSkill));

            Console.WriteLine();
            Console.WriteLine("── ③ 退化样本：把 3 个新列去掉（还原 19 列）⇒ 断言必须变红 ──");
            var raw = File.ReadAllText(Path.Combine(tsvDir, "Class.tsv"), Encoding.UTF8);
            var lines = raw.Replace("\r\n", "\n").Split('\n');
            var legacy = new StringBuilder();
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) { legacy.Append('\n'); continue; }
                var c = lines[i].Split('\t');
                var keep = new System.Collections.Generic.List<string>();
                for (var k = 0; k < c.Length; k++) if (k < 15 || k > 17) keep.Add(c[k]);
                legacy.Append(string.Join("\t", keep)).Append('\n');
            }
            var legacyPath = Path.Combine(Path.GetTempPath(), "tableverify_legacy_Class.tsv");
            File.WriteAllText(legacyPath, legacy.ToString(), new UTF8Encoding(false));
            var legacyTable = new BaseClassTable();
            legacyTable.Load(legacyPath);
            var lr = legacyTable.Get(1);
            Check("退化样本：19 列表读到的 HpAdd ≠ 30（说明断言对列敏感）",
                lr != null && lr.HpAdd != expHpAdd[0],
                lr == null ? "row null" : "HpAdd=" + lr.HpAdd + "（旧表第 16 列 = to_hit_factor=5 ⇒ 期望 5）"
                    + " ; BaseStamina=" + lr.BaseStamina + " BlockFactor=" + lr.BlockFactor);
            File.Delete(legacyPath);

            Console.WriteLine();
            Console.WriteLine("── ④ 其余 10 张表行数不变（无附带损伤）──");
            Check("StartItem=23", Tables.Default.StartItem.Count == 23, "Count=" + Tables.Default.StartItem.Count);
            Check("Experience=99", Tables.Default.Experience.Count == 99, "Count=" + Tables.Default.Experience.Count);
            Check("Level=3", Tables.Default.Level.Count == 3, "Count=" + Tables.Default.Level.Count);
            Check("Monster=8", Tables.Default.Monster.Count == 8, "Count=" + Tables.Default.Monster.Count);
            Check("Skill=150", Tables.Default.Skill.Count == 150, "Count=" + Tables.Default.Skill.Count);
            Check("Item=137", Tables.Default.Item.Count == 137, "Count=" + Tables.Default.Item.Count);
            Check("Affix=301", Tables.Default.Affix.Count == 301, "Count=" + Tables.Default.Affix.Count);
            Check("Monumod=18", Tables.Default.Monumod.Count == 18, "Count=" + Tables.Default.Monumod.Count);
            Check("Treasureclass=59", Tables.Default.Treasureclass.Count == 59, "Count=" + Tables.Default.Treasureclass.Count);
            Check("Missile=42", Tables.Default.Missile.Count == 42, "Count=" + Tables.Default.Missile.Count);

            Console.WriteLine();
            Console.WriteLine("================ 结束：" + _pass + " 项通过，" + _fail + " 项失败 ==============");
            return _fail == 0 ? 0 : 1;
        }
    }
}
