// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Flow/ClassTable.cs
// 职业数据（配表 `class_c`）→ UI 用的 `CharCreatePanel.ClassEntry` 的唯一转换点。
//
// 为什么要这一层：
//   ① UI 面板**不许引用业务模块**，也不该自己拼配表路径 ⇒ Flow 查表、经
//      `OnOpen(param)` 把纯数据交给面板（`constraints.md` #7）；
//   ② 「查表」只在**一处**发生，将来换表/加列只改这里。
//
// 读表方式（**照 agent-02 的结论，不自己拼路径**）：
//   `Table.TableLoader.Class(id)` → `Table.BaseClassRow`；tsv 由 `App/Bootstrap` 启动时
//   经 `Table.TableLoader.LoadAll(Application.streamingAssetsPath, Application.dataPath)`
//   一次性灌进 `Table.Tables.Default`（见 `Table/TableLoader.cs` 顶部说明）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.UI;
using Table;

namespace Diablo2.Module.Flow
{
    /// <summary>配表 `class_c` → 创角面板数据的转换。</summary>
    internal static class ClassTable
    {
        /// <summary>`class_c` 的职业 id 范围（1-5，与 `Def.PlayerClass` 取值一致）。</summary>
        public const int MinClassId = 1;

        /// <summary>职业 id 上限。</summary>
        public const int MaxClassId = 5;

        /// <summary>取 5 个职业（按主键升序）。表未加载时返回空列表并留下可定位的日志。</summary>
        public static List<CharCreatePanel.ClassEntry> Build()
        {
            var list = new List<CharCreatePanel.ClassEntry>();

            for (var id = MinClassId; id <= MaxClassId; id++)
            {
                var row = TableLoader.Class(id);
                if (row == null)
                {
                    Log.WarnOnce("Flow", "class.missing." + id,
                        $"配表 class_c 缺职业 id={id} ⇒ 该职业在创角屏不可选");
                    continue;
                }

                list.Add(new CharCreatePanel.ClassEntry
                {
                    id = row.Id,
                    name = row.Name,
                    str = row.Str,
                    dex = row.Dex,
                    vit = row.Vit,
                    eng = row.Eng,
                    statPerLvl = row.StatPerLvl,
                    lifePerLvl = row.LifePerLvl,
                    manaPerLvl = row.ManaPerLvl,
                    stamPerLvl = row.StamPerLvl,
                    lifePerVit = row.LifePerVit,
                    manaPerMag = row.ManaPerMag,
                    stamPerVit = row.StamPerVit,
                    // ★ classcols 片（2026-09-24）：起始值两列也搬过去 —— 创角预览算 1 级
                    //   生命/耐力要的就是它们（官方 `charstats.hpadd` / `stamina`），
                    //   ⛔ 面板里不再有这两个数字的常量（真值只在表里）。
                    hpAdd = row.HpAdd,
                    baseStamina = row.BaseStamina,
                });
            }

            if (list.Count == 0)
            {
                Log.Error("Flow",
                    "配表 class_c 一行都没读到（Tables.Default.Class.Count == 0）⇒ 创角屏无职业可选。" +
                    "检查 Bootstrap 里的 Table.TableLoader.LoadAll 是否成功（见其 Error 日志）");
            }
            else
            {
                Log.Info("Flow", $"class_c 已读取 {list.Count} 个职业（{list[0].name} … {list[list.Count - 1].name}）");
            }

            return list;
        }

        /// <summary>职业中文名（来自 `class_c.name`）；表不可用时退回枚举名并限频告警。</summary>
        public static string NameOf(PlayerClass cls)
        {
            var row = TableLoader.Class((int)cls);
            if (row != null && !string.IsNullOrEmpty(row.Name)) return row.Name;

            Log.WarnThrottled("Flow", "class.name." + (int)cls,
                $"class_c 取不到职业 {(int)cls}（{cls}）的名字，退回枚举名");
            return cls.ToString();
        }
    }
}
