// ─────────────────────────────────────────────────────────────────────────────
//  Diablo2 · Table/TableLoader.cs
//  配表运行时加载入口（实现 = 引擎 `CloverEngine.CloverTable`；`App/Bootstrap` 是唯一加载调用点）。
//
//    按主键强类型取行的实现在引擎 `CloverEngine.CloverTable`
//    （`clover-client-unity-engine/Runtime/Data/CloverTable.cs`；与打表产物**逐条对齐**：
//     表头 / 主键 = 第 1 列 / 列名→字段名（`hp_min` → `HpMin`）/ 单元格解析语义，
//     每条约定在引擎那个文件的文件头都写着 `cs.go` / `ident.go` 的出处）。
//    本文件只留三件事：
//      ① `LoadAll` = 转发引擎加载 **+ 把打表产物自带的强类型壳 `Tables.Default` 灌上**；
//      ② 10 个便捷访问器 = 转发 `CloverTable.Get<T>`；
//      ③ 公开签名与公开常量由本文件对外提供（调用点遍布 `App/Bootstrap`、`Module/**`、`UI/**`
//         与 6 个链接了 `Table/**` 的离线宿主）。
//
//  为什么成功加载后还要 `Tables.Default.LoadAll(LastDir)`：
//     生成物 `Registry.cs` 的 `Tables.Default.<表>.Get(id)` / `All()` 是**打表工具自带**的访问器
//     （`clover-tools/table/core/internal/gen/cs.go:361-377`），业务（`Module/**` / `UI/**`）与离线宿主
//     都在用，其中 `All()` 的枚举能力**引擎契约里没有** ⇒ 必须照旧灌上。
//     ⇒ 本项目有**两条读取路径**：引擎 `CloverTable.Get<T>`（本文件转发）与生成壳 `Tables.Default.*`
//
//  为什么不用引擎的 `CloverData.InitDataTable(dir)`：
//     那是引擎自带的**通用** TSV 加载器（`Runtime/Data/DataTable.cs:33`），
//     它要求数据行类型实现 `IDataRow`（`Runtime/Core/Contracts.cs:911`，`int Id { get; }`），
//     而打表工具生成的行类（`Table.Base*Row`，见 `clover-tools/table/core/internal/gen/cs.go:259`）
//     只是普通字段容器、**不实现 IDataRow** ⇒ 两者不是同一条链路。
//     引擎已按「读打表产物 tsv + 强类型访问」补了配套能力 `CloverTable` —— 本文件转发的就是它。
//
//  为什么路径是“真实目录”而不是 Resources：
//     生成的 `Load(string path)` 内部是 `File.ReadAllLines(path)`
//     （`clover-tools/table/core/internal/gen/cs.go:279`）——它需要一个**文件系统真实路径**。
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
    /// <summary>配表运行时加载入口（实现在 <see cref="CloverTable"/>）。</summary>
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

        /// <summary>最近一次成功加载所用目录；未加载过为 null（由引擎 <see cref="CloverTable.Dir"/> 转发）。</summary>
        public static string LastDir { get; private set; }

        static TableLoader()
        {
            // 引擎不知道"本项目需要哪几张表" ⇒ 在这里声明一次：少一张时它会报出**是哪个文件**
            // （否则只会表现为"那张表空着"）。表名 = tsv 文件名去扩展名。
            for (var i = 0; i < TsvFiles.Length; i++)
                CloverTable.RequiredTables.Add(Path.GetFileNameWithoutExtension(TsvFiles[i]));
        }

        /// <summary>
        /// 解析运行时 tsv 目录：优先 StreamingAssets/Table（发布后可用），
        /// 回退 &lt;dataPath&gt;/Scripts/Table/Tsv（仅编辑器）。都找不到返回 null。
        /// </summary>
        public static string ResolveDir(string streamingAssetsRoot, string dataPath)
            => CloverTable.ResolveDir(streamingAssetsRoot, dataPath);

        /// <summary>
        /// 加载全部配表：引擎读 tsv（<see cref="CloverTable.LoadAll"/>）**并**灌满打表自带的强类型壳
        /// <see cref="Tables.Default"/>（本项目的业务与离线宿主直调 `Tables.Default.*` / `All()`）。
        /// </summary>
        /// <returns>成功返回 null；失败返回**可定位的错误描述**（由调用方用 Game.Logger.Error 打出）。</returns>
        public static string LoadAll(string streamingAssetsRoot, string dataPath = null)
        {
            var err = CloverTable.LoadAll(streamingAssetsRoot, dataPath);
            if (err != null) return err;              // 失败：不破坏上一次成功加载的数据（引擎侧语义）

            LastDir = CloverTable.Dir;

            try
            {
                Tables.Default.LoadAll(LastDir);
            }
            catch (Exception ex)
            {
                // 引擎已确保目录与文件就绪；这里只兜住"校验通过到生成壳读取之间"的竞态（文件被删/被占），
                // 不让异常穿到 Bootstrap。
                return "[TableLoader] 打表强类型壳加载失败（dir=" + LastDir + "）："
                       + ex.GetType().Name + ": " + ex.Message;
            }

            return null;
        }

        /// <summary>取一行（失败返回 null + 引擎限频告警）；业务侧的便捷封装，省掉每次判空。</summary>
        public static BaseClassRow Class(int id) { return CloverTable.Get<BaseClassRow>("Class", id); }
        public static BaseExperienceRow Experience(int level) { return CloverTable.Get<BaseExperienceRow>("Experience", level); }
        public static BaseLevelRow Level(int id) { return CloverTable.Get<BaseLevelRow>("Level", id); }
        public static BaseMonsterRow Monster(int id) { return CloverTable.Get<BaseMonsterRow>("Monster", id); }
        public static BaseSkillRow Skill(int id) { return CloverTable.Get<BaseSkillRow>("Skill", id); }
        public static BaseItemRow Item(int id) { return CloverTable.Get<BaseItemRow>("Item", id); }
        public static BaseAffixRow Affix(int id) { return CloverTable.Get<BaseAffixRow>("Affix", id); }
        public static BaseMonumodRow Monumod(int id) { return CloverTable.Get<BaseMonumodRow>("Monumod", id); }
        public static BaseStartItemRow StartItem(int id) { return CloverTable.Get<BaseStartItemRow>("StartItem", id); }
        public static BaseTreasureclassRow Treasureclass(string name) { return CloverTable.Get<BaseTreasureclassRow>("Treasureclass", name); }
        public static BaseMissileRow Missile(int id) { return CloverTable.Get<BaseMissileRow>("Missile", id); }
    }
}
