// ─────────────────────────────────────────────────────────────────────────────
//  Diablo2 · Table/TableLoader.cs
//  配表运行时加载入口（`App/Bootstrap` 是唯一加载调用点）。
//
//  读取口径 = **打表工具产物自带的强类型壳 `Tables.Default.*`**（单一口径）：
//     `TableLoader.X(id)` 这组便捷访问器就是 `Tables.Default.X.Get(id)` 的转发，
//     不再有第二条读取实现。生成壳的 `Get(id)` / `All()` 出处 =
//     `clover-tools/table/core/internal/gen/cs.go`（产物在 `Table/Base/Base*.cs`）。
//
//  加载 = 引擎目录约定 + 本项目文件清单 + 生成壳加载，三步各一份出处：
//     ① 目录解析 `CloverTable.ResolveDir`（引擎 `Runtime/Data/CloverTable.cs`）——
//        优先 `<streamingAssets>/Table`，回退 `<dataPath>/Scripts/Table/Tsv`（仅编辑器）；
//     ② 缺文件检查用本文件的 `TsvFiles`：少一张时报出**是哪个文件**
//        （生成壳的 `Load` 是 `File.ReadAllLines`，文件缺失只会抛异常，指不到"本工程要哪几张表"）；
//     ③ `Tables.Default.LoadAll(dir)` 灌数据（`Table/Registry.cs`）。
//
//  为什么路径是“真实目录”而不是 Resources：
//     生成的 `Load(string path)` 内部是 `File.ReadAllLines(path)` —— 它需要一个**文件系统真实路径**。
//     `Resources.Load` 拿到的是内存里的 Object、没有真实路径，且 `.tsv` 不是 Unity 的
//     TextAsset 扩展名 ⇒ 放在 `Assets/Resources/` 下**读不到**。
//     故：tsv 同步到 `Assets/StreamingAssets/Table/`（编辑器与 Windows 独立版都是真实目录），
//     由调用方把 `Application.streamingAssetsPath` 传进来。
//
//  调用方式（放进 Bootstrap，一次即可；**这一行由工程骨架/流程模块负责**）：
//      string err = Table.TableLoader.LoadAll(UnityEngine.Application.streamingAssetsPath,
//                                             UnityEngine.Application.dataPath);
//      if (err != null) Game.Logger.Error("Table", err);
//      else Game.Logger.Info("Table", "配表已加载：" + Table.TableLoader.LastDir);
//
//  本文件**只依赖 System.IO + CloverEngine**（不引用 UnityEngine），因此可被
//  `dotnet build` 独立编译校验（见 `tools/probes/hosts/*`）。
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.IO;
using CloverEngine;

namespace Table
{
    /// <summary>配表运行时加载入口（读取走打表产物生成壳 <see cref="Tables"/>）。</summary>
    public static class TableLoader
    {
        /// <summary>tsv 子目录名：`&lt;streamingAssetsPath&gt;/Table/*.tsv`（= 引擎约定，单一出处）。</summary>
        public const string StreamingSubDir = CloverTable.StreamingSubDir;

        /// <summary>编辑器回退目录（相对 `Application.dataPath`）：打表工具直接产出的位置（= 引擎约定）。</summary>
        public const string EditorFallbackSubDir = CloverTable.EditorFallbackSubDir;

        /// <summary>打表产物里的 tsv 文件名（与 `Registry.cs` 的 LoadAll 一一对应）。</summary>
        public static readonly string[] TsvFiles =
        {
            "Affix.tsv", "Class.tsv", "Experience.tsv", "Item.tsv", "Level.tsv",
            "Missile.tsv", "Monster.tsv", "Monumod.tsv", "Skill.tsv", "StartItem.tsv",
            "Treasureclass.tsv",
        };

        /// <summary>最近一次成功加载所用目录；未加载过为 null。</summary>
        public static string LastDir { get; private set; }

        /// <summary>
        /// 解析运行时 tsv 目录：优先 StreamingAssets/Table（发布后可用），
        /// 回退 &lt;dataPath&gt;/Scripts/Table/Tsv（仅编辑器）。都找不到返回 null。
        /// </summary>
        public static string ResolveDir(string streamingAssetsRoot, string dataPath)
            => CloverTable.ResolveDir(streamingAssetsRoot, dataPath);

        /// <summary>
        /// 加载全部配表到打表产物强类型壳 <see cref="Tables.Default"/>（业务与离线宿主的唯一读取源）。
        /// </summary>
        /// <returns>成功返回 null；失败返回**可定位的错误描述**（由调用方用 Game.Logger.Error 打出）。</returns>
        public static string LoadAll(string streamingAssetsRoot, string dataPath = null)
        {
            var dir = CloverTable.ResolveDir(streamingAssetsRoot, dataPath);
            if (dir == null)
                return "[TableLoader] 未找到配表目录（候选："
                       + Path.Combine(streamingAssetsRoot ?? "(参数为空)", StreamingSubDir) + " / "
                       + Path.Combine(dataPath ?? "(参数为空)", EditorFallbackSubDir) + "）";

            for (var i = 0; i < TsvFiles.Length; i++)
            {
                var path = Path.Combine(dir, TsvFiles[i]);
                if (!File.Exists(path)) return "[TableLoader] 配表文件缺失：" + path;
            }

            try
            {
                Tables.Default.LoadAll(dir);
            }
            catch (Exception ex)
            {
                // 非预期分支（读文件被占 / 目录权限 / 磁盘错误）：不穿到 Bootstrap，返回可定位错误。
                return "[TableLoader] 配表加载失败（dir=" + dir + "）："
                       + ex.GetType().Name + ": " + ex.Message;
            }

            LastDir = dir;
            return null;
        }

        /// <summary>取一行（失败返回 null）；业务侧的便捷封装，省掉每次判空。</summary>
        public static BaseClassRow Class(int id) { return Tables.Default.Class.Get(id); }
        public static BaseExperienceRow Experience(int level) { return Tables.Default.Experience.Get(level); }
        public static BaseLevelRow Level(int id) { return Tables.Default.Level.Get(id); }
        public static BaseMonsterRow Monster(int id) { return Tables.Default.Monster.Get(id); }
        public static BaseSkillRow Skill(int id) { return Tables.Default.Skill.Get(id); }
        public static BaseItemRow Item(int id) { return Tables.Default.Item.Get(id); }
        public static BaseAffixRow Affix(int id) { return Tables.Default.Affix.Get(id); }
        public static BaseMonumodRow Monumod(int id) { return Tables.Default.Monumod.Get(id); }
        public static BaseStartItemRow StartItem(int id) { return Tables.Default.StartItem.Get(id); }
        public static BaseTreasureclassRow Treasureclass(string name) { return Tables.Default.Treasureclass.Get(name); }
        public static BaseMissileRow Missile(int id) { return Tables.Default.Missile.Get(id); }
    }
}
