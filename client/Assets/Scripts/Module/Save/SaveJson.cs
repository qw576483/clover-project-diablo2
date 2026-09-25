// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Save/SaveJson.cs   **本项目新增**（不是引擎能力，也不是打表产物）
//
// 作用：把 `Def.CharacterSave` 与 JSON 字符串**双向**转换 —— 产出的文本落进引擎文件槽位
//   （`<SettingDir>/saves/<角色名>.json`，写盘见 `SaveModule.Store`；更早版本的旧键 `char/{角色名}`
//   见 `GameConst.SaveKeyPrefix`、现仅用于懒迁移；创建先后索引见 `GameConst.SaveIndexKey`）。
//
// 为什么不用 `UnityEngine.JsonUtility`（契约注释里提到过它）：
//   ① **离线不可验证**：`JsonUtility` 是 Unity 原生内部调用（`UnityEngine.JSONSerializeModule`），
//      在 `tools/probes/hosts/itemcheck` 这种 .NET 宿主里没有 Unity 运行时 ⇒ 存档这条链路根本跑不起来；
//      而"存档往返一致"是存档链路的硬验收项，必须能离线断言。
//   ② **null 语义不确定**：`CharacterSave.inventory`（40 格，多数 `item == null`）与 `belt`（4 格带 null）
//      依赖"null 元素能否原样往返"。JsonUtility 对列表里的 null 对象引用行为不明确（可能写成 `{}`），
//      一旦丢 null 就会变成"空背包里冒出 40 个空物品"这类静默错。
//   ③ 本实现是**确定性**的：字段顺序固定、浮点用 R 格式 ⇒ `Write(Parse(json)) == json`（逐字节），
//      这条恒等式直接当自检断言用（见 `tools/probes/hosts/itemcheck/Program.cs`）。
//
// 无 Unity 依赖（只用 System / System.Collections.Generic / System.Text）⇒ 可离线编译运行。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CloverEngine;
using Diablo2.Def;

namespace Diablo2.Module.Save
{
    /// <summary>存档 JSON 编解码（确定性、null 安全、无 Unity 依赖）。</summary>
    internal static class SaveJson
    {
        // ═════════════════════════════════════════════════════════════════════
        // 写
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>序列化角色存档（字段顺序固定）。</summary>
        public static string Write(CharacterSave s)
        {
            if (s == null) return "null";

            var sb = new StringBuilder(2048);
            sb.Append('{');
            Field(sb, "version", s.version, false);
            Field(sb, "name", s.name, true);
            Field(sb, "cls", (int)s.cls, true);
            Field(sb, "level", s.level, true);
            Field(sb, "exp", s.exp, true);
            Field(sb, "str", s.str, true);
            Field(sb, "dex", s.dex, true);
            Field(sb, "vit", s.vit, true);
            Field(sb, "eng", s.eng, true);
            Field(sb, "life", s.life, true);
            Field(sb, "mana", s.mana, true);
            Field(sb, "stamina", s.stamina, true);
            Field(sb, "statPoints", s.statPoints, true);
            Field(sb, "skillPoints", s.skillPoints, true);
            Field(sb, "gold", s.gold, true);
            // 双武器组（新字段，写在 gold 之后与 CharacterSave 的字段顺序一致）：
            //   旧档没有这一行 ⇒ 读侧 GetInt(..., 0) 走默认值 0 = Ⅰ组（向后兼容，见 TryParse）。
            Field(sb, "activeWeaponIndex", s.activeWeaponIndex, true);
            Field(sb, "areaId", s.areaId, true);
            Field(sb, "gridX", s.gridX, true);
            Field(sb, "gridY", s.gridY, true);
            Field(sb, "mapSeed", s.mapSeed, true);
            //   旧档没有这两行 ⇒ 读侧取**空集合**（向后兼容，见 TryParse 与 `Def/ExploredCodec.cs` 头注）。
            Key(sb, "visitedWaypoints", true);
            WriteIntList(sb, s.visitedWaypoints);
            Key(sb, "exploredByArea", true);
            WriteExploredList(sb, s.exploredByArea);

            Key(sb, "skillIds", true);
            WriteIntList(sb, s.skillIds);
            Key(sb, "skillLevels", true);
            WriteIntList(sb, s.skillLevels);
            Key(sb, "buttonSkills", true);
            WriteIntList(sb, s.buttonSkills);

            Key(sb, "inventory", true);
            WriteSlotList(sb, s.inventory);
            Key(sb, "equip", true);
            WriteItemList(sb, s.equip);
            Key(sb, "belt", true);
            WriteItemList(sb, s.belt);
            Key(sb, "quests", true);
            WriteQuestList(sb, s.quests);

            Field(sb, "savedAtTicks", s.savedAtTicks, true);
            Key(sb, "playedSeconds", true);
            JsonWriter.WriteFloat(sb, s.playedSeconds);

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>序列化字符串清单（存档索引 `char/index` 用）。</summary>
        public static string WriteStringList(List<string> names)
        {
            var sb = new StringBuilder(128);
            sb.Append('[');
            if (names != null)
            {
                for (var i = 0; i < names.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteString(sb, names[i]);
                }
            }
            sb.Append(']');
            return sb.ToString();
        }

        // ── 写原语一律转发到引擎 JsonWriter（确定性：固定顺序 + R 浮点 + null 安全）──
        //    字段顺序仍由本文件的 Write(...) 决定（DTO 布局留业务侧，见 JsonWriter 类型注释）。
        private static void Field(StringBuilder sb, string key, int v, bool comma)
        {
            Key(sb, key, comma);
            JsonWriter.WriteInt(sb, v);
        }

        private static void Field(StringBuilder sb, string key, long v, bool comma)
        {
            Key(sb, key, comma);
            JsonWriter.WriteLong(sb, v);
        }

        private static void Field(StringBuilder sb, string key, string v, bool comma)
        {
            Key(sb, key, comma);
            JsonWriter.WriteString(sb, v);
        }

        private static void Key(StringBuilder sb, string key, bool comma)
        {
            JsonWriter.WriteKey(sb, key, comma);
        }

        private static void WriteString(StringBuilder sb, string v)
        {
            JsonWriter.WriteString(sb, v);
        }

        private static void WriteIntList(StringBuilder sb, List<int> list)
        {
            sb.Append('[');
            if (list != null)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(list[i].ToString(CultureInfo.InvariantCulture));
                }
            }
            sb.Append(']');
        }

        /// <summary>
        /// <para>`cells` = base64 位图（见 `Def/ExploredCodec`），不是逐格坐标数组
        /// （80×80 满图 6400 格 ⇒ 逐格写法会让存档膨胀到十几 KB；位图 ≈1 KB）。</para>
        /// </summary>
        private static void WriteExploredList(StringBuilder sb, List<ExploredAreaDto> list)
        {
            sb.Append('[');
            if (list != null)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var e = list[i];
                    if (i > 0) sb.Append(',');
                    if (e == null)
                    {
                        sb.Append("null");
                        continue;
                    }
                    sb.Append('{');
                    Field(sb, "area", e.area, false);
                    Field(sb, "w", e.w, true);
                    Field(sb, "h", e.h, true);
                    Field(sb, "cells", e.cells, true);
                    sb.Append('}');
                }
            }
            sb.Append(']');
        }

        private static void WriteSlotList(StringBuilder sb, List<InventorySlot> list)
        {
            sb.Append('[');
            if (list != null)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteSlot(sb, list[i]);
                }
            }
            sb.Append(']');
        }

        private static void WriteSlot(StringBuilder sb, InventorySlot s)
        {
            if (s == null)
            {
                sb.Append("null");
                return;
            }
            sb.Append('{');
            Field(sb, "index", s.index, false);
            Field(sb, "x", s.x, true);
            Field(sb, "y", s.y, true);
            Field(sb, "occupied", s.occupied, true);
            Field(sb, "isAnchor", s.isAnchor, true);
            Key(sb, "item", true);
            WriteItem(sb, s.item);
            Field(sb, "anchorIndex", s.anchorIndex, true);
            sb.Append('}');
        }

        private static void Field(StringBuilder sb, string key, bool v, bool comma)
        {
            Key(sb, key, comma);
            JsonWriter.WriteBool(sb, v);
        }

        private static void WriteItemList(StringBuilder sb, List<ItemStack> list)
        {
            sb.Append('[');
            if (list != null)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteItem(sb, list[i]);
                }
            }
            sb.Append(']');
        }

        private static void WriteItem(StringBuilder sb, ItemStack it)
        {
            if (it == null)
            {
                sb.Append("null");
                return;
            }
            sb.Append('{');
            Field(sb, "itemId", it.itemId, false);
            Field(sb, "name", it.name, true);
            Field(sb, "type", (int)it.type, true);
            Field(sb, "quality", (int)it.quality, true);
            Field(sb, "count", it.count, true);
            Field(sb, "gridW", it.gridW, true);
            Field(sb, "gridH", it.gridH, true);
            Field(sb, "lvlReq", it.lvlReq, true);
            Field(sb, "strReq", it.strReq, true);
            Field(sb, "dmgMin", it.dmgMin, true);
            Field(sb, "dmgMax", it.dmgMax, true);
            Field(sb, "defMin", it.defMin, true);
            Field(sb, "defMax", it.defMax, true);
            Field(sb, "price", it.price, true);
            Key(sb, "affixes", true);
            WriteAffixList(sb, it.affixes);
            Field(sb, "durability", it.durability, true);
            Field(sb, "maxDurability", it.maxDurability, true);
            Field(sb, "isGold", it.isGold, true);
            Field(sb, "isQuestItem", it.isQuestItem, true);
            sb.Append('}');
        }

        private static void WriteAffixList(StringBuilder sb, List<ItemAffix> list)
        {
            sb.Append('[');
            if (list != null)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var a = list[i];
                    if (i > 0) sb.Append(',');
                    if (a == null)
                    {
                        sb.Append("null");
                        continue;
                    }
                    sb.Append('{');
                    Field(sb, "affixId", a.affixId, false);
                    Field(sb, "kind", (int)a.kind, true);
                    Field(sb, "name", a.name, true);
                    Field(sb, "mod", a.mod, true);
                    Field(sb, "min", a.min, true);
                    Field(sb, "max", a.max, true);
                    Field(sb, "value", a.value, true);
                    sb.Append('}');
                }
            }
            sb.Append(']');
        }

        private static void WriteQuestList(StringBuilder sb, List<QuestStateDto> list)
        {
            sb.Append('[');
            if (list != null)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var q = list[i];
                    if (i > 0) sb.Append(',');
                    if (q == null)
                    {
                        sb.Append("null");
                        continue;
                    }
                    sb.Append('{');
                    Field(sb, "questId", q.questId, false);
                    Field(sb, "name", q.name, true);
                    Field(sb, "state", (int)q.state, true);
                    Field(sb, "progress", q.progress, true);
                    Field(sb, "required", q.required, true);
                    Field(sb, "rewardClaimed", q.rewardClaimed, true);
                    Field(sb, "objective", q.objective, true);
                    sb.Append('}');
                }
            }
            sb.Append(']');
        }

        // ═════════════════════════════════════════════════════════════════════
        // 读
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 反序列化角色存档。<paramref name="error"/> != null 表示失败（此时返回 null）。
        /// **缺字段按默认值处理**（不抛异常）—— 这是"版本不符降级"能成立的前提。
        /// </summary>
        public static CharacterSave TryParse(string json, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(json))
            {
                error = "[SaveJson] 存档字符串为空";
                return null;
            }

            object root;
            try
            {
                var pos = 0;
                root = ParseValue(json, ref pos);
                SkipWs(json, ref pos);
                if (pos != json.Length) error = $"[SaveJson] 解析后仍有多余字符（pos={pos}/{json.Length}）";
            }
            catch (System.Exception ex)
            {
                error = $"[SaveJson] JSON 解析失败：{ex.GetType().Name}: {ex.Message}";
                return null;
            }

            var dict = root as Dictionary<string, object>;
            if (dict == null)
            {
                error = "[SaveJson] 存档根节点不是 JSON 对象";
                return null;
            }

            var s = new CharacterSave();
            s.version = GetInt(dict, "version", 0);
            s.name = GetString(dict, "name", null);
            s.cls = (PlayerClass)GetInt(dict, "cls", 1);
            s.level = GetInt(dict, "level", 1);
            s.exp = GetLong(dict, "exp", 0);
            s.str = GetInt(dict, "str", 0);
            s.dex = GetInt(dict, "dex", 0);
            s.vit = GetInt(dict, "vit", 0);
            s.eng = GetInt(dict, "eng", 0);
            s.life = GetInt(dict, "life", 0);
            s.mana = GetInt(dict, "mana", 0);
            s.stamina = GetInt(dict, "stamina", 0);
            s.statPoints = GetInt(dict, "statPoints", 0);
            s.skillPoints = GetInt(dict, "skillPoints", 0);
            s.gold = GetInt(dict, "gold", 0);
            // 双武器组：**缺字段按 0（= Ⅰ组）** —— 这是"旧档无该字段仍可读且不崩"的落点
            //   （本文件的既有约定：缺字段一律取默认值、绝不抛异常，见文件头与 TryParse 注释）。
            s.activeWeaponIndex = GetInt(dict, "activeWeaponIndex", 0);
            s.areaId = GetInt(dict, "areaId", 0);
            s.gridX = GetInt(dict, "gridX", 0);
            s.gridY = GetInt(dict, "gridY", 0);
            s.mapSeed = GetInt(dict, "mapSeed", 0);
            s.visitedWaypoints = GetIntList(dict, "visitedWaypoints");
            s.exploredByArea = GetExploredList(dict, "exploredByArea");
            s.skillIds = GetIntList(dict, "skillIds");
            s.skillLevels = GetIntList(dict, "skillLevels");
            s.buttonSkills = GetIntList(dict, "buttonSkills");
            if (s.buttonSkills.Count == 0)
            {
                s.buttonSkills.Add(-1);
                s.buttonSkills.Add(-1);
            }
            s.inventory = GetSlotList(dict, "inventory");
            s.equip = GetItemList(dict, "equip");
            s.belt = GetItemList(dict, "belt");
            s.quests = GetQuestList(dict, "quests");
            s.savedAtTicks = GetLong(dict, "savedAtTicks", 0);
            s.playedSeconds = GetFloat(dict, "playedSeconds", 0f);

            return s;
        }

        /// <summary>解析字符串清单（存档索引）。失败返回空列表（不抛异常）。</summary>
        public static List<string> ParseStringList(string json)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(json)) return res;

            try
            {
                var pos = 0;
                var root = ParseValue(json, ref pos);
                var list = root as List<object>;
                if (list == null) return res;
                for (var i = 0; i < list.Count; i++)
                {
                    var v = list[i] as string;
                    if (!string.IsNullOrEmpty(v)) res.Add(v);
                }
            }
            catch (System.Exception ex)
            {
                // 索引坏了不该崩游戏：按空清单处理（角色文件仍在，只是暂时列不出来）
                Log_IndexBroken(ex.Message);
            }
            return res;
        }

        private static void Log_IndexBroken(string msg)
        {
            // 这里刻意**不引用** Diablo2.Core.Log：本文件要保持"纯 JSON 工具"的零依赖，
            // 索引损坏的日志由 SaveModule 用它自己的 tag 打（见 SaveModule.List）。
            LastIndexError = msg;
        }

        /// <summary>最近一次索引解析失败信息（供 SaveModule 打日志）。</summary>
        public static string LastIndexError { get; private set; }

        // ── 取值助手（缺字段/类型不符一律回默认值）──────────────────────────────

        private static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        private static int GetInt(Dictionary<string, object> d, string key, int def)
        {
            var v = Get(d, key);
            if (v == null) return def;
            if (v is long) return (int)(long)v;
            if (v is double) return (int)(double)v;
            return def;
        }

        private static long GetLong(Dictionary<string, object> d, string key, long def)
        {
            var v = Get(d, key);
            if (v == null) return def;
            if (v is long) return (long)v;
            if (v is double) return (long)(double)v;
            return def;
        }

        private static float GetFloat(Dictionary<string, object> d, string key, float def)
        {
            var v = Get(d, key);
            if (v == null) return def;
            if (v is long) return (long)v;
            if (v is double) return (float)(double)v;
            return def;
        }

        private static bool GetBool(Dictionary<string, object> d, string key, bool def)
        {
            var v = Get(d, key);
            if (v is bool) return (bool)v;
            return def;
        }

        private static string GetString(Dictionary<string, object> d, string key, string def)
        {
            var v = Get(d, key) as string;
            return v ?? def;
        }

        private static List<object> GetList(Dictionary<string, object> d, string key)
        {
            var v = Get(d, key) as List<object>;
            return v ?? new List<object>();
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
        {
            return Get(d, key) as Dictionary<string, object>;
        }

        private static List<int> GetIntList(Dictionary<string, object> d, string key)
        {
            var res = new List<int>();
            var list = GetList(d, key);
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] is long) res.Add((int)(long)list[i]);
                else if (list[i] is double) res.Add((int)(double)list[i]);
            }
            return res;
        }

        /// <summary>
        /// 旧档没有该键 ⇒ 空列表；某项缺 `w/h` 或 `cells` 非法 ⇒ 该项按"没有已探索记录"处理
        /// （`ExploredCodec.Decode` 是容错入口，坏 base64 只回 0）。
        /// </summary>
        private static List<ExploredAreaDto> GetExploredList(Dictionary<string, object> d, string key)
        {
            var res = new List<ExploredAreaDto>();
            var list = GetList(d, key);
            for (var i = 0; i < list.Count; i++)
            {
                var o = list[i] as Dictionary<string, object>;
                if (o == null) continue;
                res.Add(new ExploredAreaDto
                {
                    area = GetInt(o, "area", -1),
                    w = GetInt(o, "w", 0),
                    h = GetInt(o, "h", 0),
                    cells = GetString(o, "cells", null),
                });
            }
            return res;
        }

        private static List<InventorySlot> GetSlotList(Dictionary<string, object> d, string key)
        {            var res = new List<InventorySlot>();
            var list = GetList(d, key);
            for (var i = 0; i < list.Count; i++)
            {
                var o = list[i] as Dictionary<string, object>;
                if (o == null)
                {
                    res.Add(null);
                    continue;
                }
                res.Add(new InventorySlot
                {
                    index = GetInt(o, "index", 0),
                    x = GetInt(o, "x", 0),
                    y = GetInt(o, "y", 0),
                    occupied = GetBool(o, "occupied", false),
                    isAnchor = GetBool(o, "isAnchor", false),
                    item = GetItem(o, "item"),
                    anchorIndex = GetInt(o, "anchorIndex", -1),
                });
            }
            return res;
        }

        private static List<ItemStack> GetItemList(Dictionary<string, object> d, string key)
        {
            var res = new List<ItemStack>();
            var list = GetList(d, key);
            for (var i = 0; i < list.Count; i++)
            {
                var o = list[i] as Dictionary<string, object>;
                res.Add(o == null ? null : ItemOf(o));
            }
            return res;
        }

        private static ItemStack GetItem(Dictionary<string, object> d, string key)
        {
            var o = GetDict(d, key);
            return o == null ? null : ItemOf(o);
        }

        private static ItemStack ItemOf(Dictionary<string, object> o)
        {
            var it = new ItemStack
            {
                itemId = GetInt(o, "itemId", 0),
                name = GetString(o, "name", ""),
                type = (ItemType)GetInt(o, "type", (int)ItemType.Misc),
                quality = (ItemQuality)GetInt(o, "quality", (int)ItemQuality.Normal),
                count = GetInt(o, "count", 1),
                gridW = GetInt(o, "gridW", 1),
                gridH = GetInt(o, "gridH", 1),
                lvlReq = GetInt(o, "lvlReq", 0),
                strReq = GetInt(o, "strReq", 0),
                dmgMin = GetInt(o, "dmgMin", 0),
                dmgMax = GetInt(o, "dmgMax", 0),
                defMin = GetInt(o, "defMin", 0),
                defMax = GetInt(o, "defMax", 0),
                price = GetInt(o, "price", 0),
                durability = GetInt(o, "durability", 0),
                maxDurability = GetInt(o, "maxDurability", 0),
                isGold = GetBool(o, "isGold", false),
                isQuestItem = GetBool(o, "isQuestItem", false),
            };
            if (it.count <= 0) it.count = 1;

            var affixes = GetList(o, "affixes");
            for (var i = 0; i < affixes.Count; i++)
            {
                var a = affixes[i] as Dictionary<string, object>;
                if (a == null) continue;
                it.affixes.Add(new ItemAffix
                {
                    affixId = GetInt(a, "affixId", 0),
                    kind = (AffixKind)GetInt(a, "kind", (int)AffixKind.Suffix),
                    name = GetString(a, "name", ""),
                    mod = GetString(a, "mod", ""),
                    min = GetInt(a, "min", 0),
                    max = GetInt(a, "max", 0),
                    value = GetInt(a, "value", 0),
                });
            }
            return it;
        }

        private static List<QuestStateDto> GetQuestList(Dictionary<string, object> d, string key)
        {
            var res = new List<QuestStateDto>();
            var list = GetList(d, key);
            for (var i = 0; i < list.Count; i++)
            {
                var o = list[i] as Dictionary<string, object>;
                if (o == null)
                {
                    res.Add(null);
                    continue;
                }
                res.Add(new QuestStateDto
                {
                    questId = GetInt(o, "questId", 0),
                    name = GetString(o, "name", ""),
                    state = (QuestState)GetInt(o, "state", (int)QuestState.NotStarted),
                    progress = GetInt(o, "progress", 0),
                    required = GetInt(o, "required", 0),
                    rewardClaimed = GetBool(o, "rewardClaimed", false),
                    objective = GetString(o, "objective", ""),
                });
            }
            return res;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 极简 JSON 解析原语 —— 已收敛到引擎 `CloverEngine.JsonWriter`（本文件只保留转发，
        // 公开 API 与调用点零改动）。产物映射：对象 → Dictionary<string,object>，
        // 数组 → List<object>，整数 → long、浮点 → double，字符串 → string，null/bool 原样。
        // 与 `MiniJson.Parse` 的区别：这里整数只认 long（超出退 double），业务取值侧
        //    GetInt/GetLong/GetFloat 依赖这个口径；大整数（uint64 对象号）请用 MiniJson。
        // ═════════════════════════════════════════════════════════════════════

        private static void SkipWs(string s, ref int pos)
        {
            JsonWriter.SkipWhitespace(s, ref pos);
        }

        private static object ParseValue(string s, ref int pos)
        {
            return JsonWriter.ParseValue(s, ref pos);
        }

        private static Dictionary<string, object> ParseObject(string s, ref int pos)
        {
            return JsonWriter.ParseObject(s, ref pos);
        }

        private static List<object> ParseArray(string s, ref int pos)
        {
            return JsonWriter.ParseArray(s, ref pos);
        }

        private static string ParseString(string s, ref int pos)
        {
            return JsonWriter.ParseString(s, ref pos);
        }

        private static object ParseNumber(string s, ref int pos)
        {
            return JsonWriter.ParseNumber(s, ref pos);
        }
    }
}
