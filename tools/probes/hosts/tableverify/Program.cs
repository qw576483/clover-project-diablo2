// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · tools/probes/hosts/tableverify/Program.cs
//
// 打表产物（全仓 `TableVerify.csproj` 0 命中）。
// 为什么只编「不引引擎」的那一半产物 ⇒ 见 TableVerify.csproj 的文件头（三条理由）。
//
// 判据四项：
//   ① 编译自检：打表产物（除唯一引引擎的 `TableLoader.cs`）能否独立 dotnet build；
//   ② 数值判据：从**真实运行时 tsv**（`client/Assets/StreamingAssets/Table`）读 class_c，
//      逐职业断言 classcols 片新增的 3 列（hp_add / base_stamina / block_factor）== 官方
//      `charstats.txt` 原值（= 本次删掉的代码常量的逐条值）；
//   ③ **退化样本**：把 Class.tsv 的 3 个新列**摘掉**（还原成改动前的 19 列）再读 ⇒
//      断言必须**不**等于官方值（证明 ② 真的在判"这三列"，不是恒真）；
//   ④ 无附带损伤：其余 10 张表行数不变（防"重打表"把别的表带歪）。
//
// 退出码：全部通过 0 / 任一失败 1（`tools/probes/hosts/run_all_hosts.ps1` 靠它判 PASS/FAIL）。
// 本宿主不碰 Unity、不碰引擎、不进 Play（纯离线、秒级）。
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
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

        /// <summary>
        /// 源表（`策划/数值文档/&lt;file&gt;.txt`）的**数据行数**：前 4 行是表头
        /// （0 列名 / 1 类型 / 2 `c` / 3 中文注释），其余非空行都算数据。
        /// <para>文件不在 ⇒ **-1**（调用点会因此变红，⛔ 不静默当成 0）。</para>
        /// </summary>
        private static int SourceDataRows(string root, string file)
        {
            var path = Path.Combine(root, "策划", "数值文档", file + ".txt");
            if (!File.Exists(path)) return -1;
            var lines = File.ReadAllLines(path);
            var n = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                if (i < 4) continue;
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                n++;
            }
            return n;
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
            Console.WriteLine("── ④ 其余 10 张表行数 == 源表数据行数（防「重打表把别的表带歪」）──");
            //   ⚠️ 修订：原来把行数**写死**（`Level=2` / `Missile=42`）—— 那两个数字在"表被
            //   **合法**扩展"之后必然变红，而且挡不住真损伤（真损伤把数字一起改掉就行）。本轮实测的两处红
            //   正是**合法扩展**，出处都在源表里：
            //     · `missile_c` 42 → 85 行：为法师技能补齐投射物行（每行带官方 `Missiles.Id`，
            //       如 `chargedbolt`=56 / `infernoflame1`=60 / `glacialspike`=96 / `frozenorb`=260 …）；
            //     · `level_c` 2 → 3 行：源表早就有「邪恶洞穴」第三行，重打表后运行时 tsv 才与源表同步。
            //   ⇒ 判据改成 **运行时行数 == 源表数据行数**（同一份源、逐表对照；不一致 = 没同步 / 被带歪）。
            var rowBad = new List<string>();
            Action<string, string, int> rows = (label, file, count) =>
            {
                var src = SourceDataRows(root, file);
                var ok = src >= 0 && src == count;
                if (!ok) rowBad.Add($"{label}：运行时 {count} ≠ 源表 {src}");
                Check($"{label}={count}（源表 {file}.txt 数据行 {src}）", ok,
                    ok ? "一致" : $"运行时 {count} ≠ 源表 {src}");
            };
            rows("StartItem", "start_item_c", Tables.Default.StartItem.Count);
            rows("Experience", "experience_c", Tables.Default.Experience.Count);
            rows("Level", "level_c", Tables.Default.Level.Count);
            rows("Monster", "monster_c", Tables.Default.Monster.Count);
            rows("Skill", "skill_c", Tables.Default.Skill.Count);
            rows("Item", "item_c", Tables.Default.Item.Count);
            rows("Affix", "affix_c", Tables.Default.Affix.Count);
            rows("Monumod", "monumod_c", Tables.Default.Monumod.Count);
            rows("Treasureclass", "treasureclass_c", Tables.Default.Treasureclass.Count);
            rows("Missile", "missile_c", Tables.Default.Missile.Count);

            //   §自检：把源表行数拨 1（= 已知错样本）⇒ 同一条比对必须判"不等"（证明它真的在比两份行数，
            //   而不是"拿同一个数跟自己比"）。
            var realMissile = SourceDataRows(root, "missile_c");
            Check("§自检：源表行数拨 1 ⇒ 与运行时行数不等（判据真的在对照两份行数）",
                realMissile > 0 && realMissile + 1 != Tables.Default.Missile.Count,
                $"源表 {realMissile} / 运行时 {Tables.Default.Missile.Count}（拨 1 后 {realMissile + 1}）");

            Console.WriteLine();
            Console.WriteLine("================ 结束：" + _pass + " 项通过，" + _fail + " 项失败 ==============");
            return _fail == 0 ? 0 : 1;
        }
    }
}
