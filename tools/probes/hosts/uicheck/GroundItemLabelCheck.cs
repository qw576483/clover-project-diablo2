// ─────────────────────────────────────────────────────────────────────────────
//
// 三节，全部离线可跑（秒级）：
//   ⑳ R8「面板底图吃不吃射线」**全量表**（逐行期望值 + **表完整性**机械判据）
//   ㉑ R5 地面物品名牌：口径逐条钉死（**数值级**：格→世界→"名牌在该格顶边"用生产代码算；
//      **源码级**：名字退化/字号/尺寸/颜色都必须是既有口径的复用，不许自创）
//   ㉒ R5 链路存在性：`Events.GroundItemLabelsChanged` 的**发送方**（`Module/Input/InputReader`）
//      与**收方**（`UI/HudPanel` → `UI/GroundItemLabelView`）两端都在位
//
// ── 为什么 R5 这一节只有"数值级 + 源码级"，没有"实例级" ────────────────────────
//   名牌层是 `internal sealed class GroundItemLabelView`（非 MonoBehaviour，构造要 `RectTransform`），
//   离线这一节能做的、也正是**最容易被改坏而不报错**的部分：
//     · "名牌画在物品上方"这条几何（换个常量就会整体偏移，且画面上只是"看着有点低"）；
//     · "颜色/字号/名字退化"三条样式口径（自创色值/字号不会报错）。
//
// ── 为什么"全量表"要带**完整性**判据（不是只挑四个报错的行断言）────────────
//   判据：把 `client/Assets/Scripts/UI/*.cs` 里**每一个** `UiArt.Panel/FullPanel/Art/Banner/Backdrop(`
//   调用点都枚举出来逐个对表；表里少一行（新加调用点没登记）或多一行（节点名写错/调用点被删）都红。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.UI;
using UnityEngine;

namespace Uicheck
{
    internal static class GroundItemLabelCheck
    {
        public static void Run()
        {
            CheckRaycastTable();
            CheckNameplateRules();
            CheckNameplateWiring();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑳ R8：`raycastTarget` 全量表（逐行 + 完整性）
        //    表项编码 = "节点名#t|f|n"（t=该吃射线, f=不吃, n=该 API 没有这个形参）
        //      · **面板矩形底图**（非满屏、代表一个面板本体）⇒ **该吃（t）** —— 面板内空白吃掉点击、
        //        面板外仍可点地面走（原版语义）；
        //      · **满屏**底图/遮罩 ⇒ 分两种：**模态**（暂停/设置/确认/死亡 = 必须吃，挡住下面一切）
        //        与**非模态叠加/屏体**（小地图 automap / 读条屏 / 流程屏 / 启动屏 = **不能吃**，
        //        否则整个屏幕都算"指针在 UI 上" ⇒ 游戏内一步都走不了）；
        //      · 跟随鼠标的浮层（光标 / tooltip / 拖影）⇒ **不能吃**（否则鼠标下方恒命中 UI）；
        //      · 纯装饰图（占位层 / 图标层 / 标题条 / 框美术）⇒ 不吃（命中由底图或手工矩形判定负责）。
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckRaycastTable()
        {
            Console.WriteLine("── ⑳ R8：面板底图 `raycastTarget` 全量表（逐行 + 表完整性）──");

            // ── 期望表（= 回报里的那张全量表，逐字对应）──────────────────────────
            var expected = new Dictionary<string, string[]>
            {
                // 启动屏：满屏屏体（无游戏内移动）⇒ 吃；Logo 是屏内装饰图 ⇒ 不吃
                ["BootPanel.cs"] = new[] { "Bg#t", "Logo#f" },

                // 创角屏：头像图是装饰（真正的热点 = `UiLayoutFlow.Hotspot` 的 Panel(true)）⇒ 不吃
                ["CharCreatePanel.cs"] = new[] { "Portrait#f" },

                // 人物属性面板：底图 = 面板矩形 ⇒ **改吃**（R8）
                ["CharacterPanel.cs"] = new[] { "CharstatBg#t", "Plus#t", "CloseButton#t" },

                // 游戏内光标：跟随鼠标 ⇒ 绝不能吃（吃了鼠标下方恒在 UI 上 ⇒ 点哪都不走）
                ["CursorView.cs"] = new[] { "Cursor#f" },

                // 确认框：满屏**模态**遮罩 ⇒ 吃；框体吃、框美术不吃（框体已覆盖）
                ["D2ConfirmPanel.cs"] = new[] { "Shade#t", "Box#t", "BoxFrame#f" },

                // 死亡屏：满屏模态 ⇒ 吃；底图拼接块/标题条由 Shade 覆盖 ⇒ 不吃
                ["DeathPanel.cs"] = new[] { "Shade#t", "EndGame#f", "Banner#n" },

                ["HudPanel.cs"] = new[]
                {
                    "ControlPanel#f", "?#f", "?#f", "ExpBarTrack#f", "ExpBarFill#f", "ExpBarOverlay#f",
                    "SkillBar#t", "Icon#f", "?#t", "Belt#t", "Icon#f", "MiniPanel#f",
                    "MiniPanelToggle#t", "MiniBtn#t", "RunButton#t",
                },

                // 背包：底图 = 面板矩形 ⇒ **改吃**（R8）；格/按钮吃；拖影/图标/装备图形不吃
                ["InventoryPanel.cs"] = new[]
                {
                    "InventoryBg#t", "DragGhost#f", "Cell#t", "Icon#f", "EquipIcon#f", "Art#f",
                    "CloseButton#t", "GoldButton#t",
                    //   （吃了会把"指针在 UI 上"恒判真 ⇒ 拖拽落点/点击移动被吃掉）。
                    "DropHighlight#f",
                },

                // 物品 tooltip：浮层跟随鼠标 ⇒ 绝不能吃
                ["ItemTooltip.cs"] = new[] { "Bg#f" },

                // 读条屏：满屏**非模态过渡屏**（无游戏内移动）⇒ 不吃
                ["LoadingPanel.cs"] = new[] { "Backdrop#f", "LoadingScreen#f" },

                ["MiniMapPanel.cs"] = new[] { "Backdrop#f", "PlayerDot#f", "Marker#f" },

                // 对话：底图不吃（等尺寸 `DialogHit` 吃）；标题条装饰 ⇒ 不吃
                ["NpcDialogPanel.cs"] = new[] { "DialogArt#f", "DialogHit#t", "SpeechBanner#n" },

                // 暂停：满屏模态 ⇒ 吃
                ["PausePanel.cs"] = new[] { "Shade#t", "Box#t", "BoxFrame#f" },

                // 任务日志：底图 = 面板矩形 ⇒ **改吃**（R8）；页签吃；石龛/任务图不吃（底图已覆盖）
                ["QuestLogPanel.cs"] = new[] { "QuestBg#t", "QuestBanner#n", "ActTab#t", "Slot#f", "Art#f" },

                // 设置：满屏模态 ⇒ 吃
                ["SettingsPanel.cs"] = new[] { "Shade#t", "Box#t", "BoxFrame#f" },

                ["ShopPanel.cs"] = new[] { "BuySellBg#t", "Tab#t", "Cell#t", "Icon#f" },

                // 技能树：两张底图 = 面板矩形（互斥显示）⇒ **都改吃**（R8）
                ["SkillTreePanel.cs"] = new[] { "TreeBackPage0#t", "TreeBackPageK#t", "TabHit#t", "Node#t", "Icon#f" },

                // 流程屏（菜单/选角/…）：满屏屏体与满屏底 ⇒ 不吃（无游戏内移动；按钮各自吃）
                ["UiLayoutFlow.cs"] = new[] { "?#t", "Backdrop#f", "BackdropFill#f", "Backdrop#f" },

                //   同型 = 打开即**模态**弹窗 ⇒ 满屏遮罩 `Shade` 必须吃（挡住底下一切，防"面板开着还能点地面走"）；
                //   `Box`（框内深色底）吃；`BoxFrame`（原版拼装窗框，后建压在底上）不吃，命中由底图负责。
                //   实参逐条对回 `UI/WaypointPanel.cs:203/211/212`（`UiArt.FullPanel(..., true)` /
                //   `UiArt.Panel(..., true)` / `UiArt.Art(...)`）。
                ["WaypointPanel.cs"] = new[] { "Shade#t", "Box#t", "BoxFrame#f" },
            };

            var actual = ScanRaycastCallSites(out var notes);

            // ① 完整性（正向）：磁盘上枚举出的每个调用点都必须在表里
            var missing = new List<string>();
            foreach (var kv in actual)
            {
                var exp = expected.ContainsKey(kv.Key) ? new List<string>(expected[kv.Key]) : new List<string>();
                foreach (var a in kv.Value)
                {
                    if (exp.Remove(a)) continue;
                    missing.Add(kv.Key + " → " + a);
                }
            }

            // ② 完整性（反向）：表里登记的行也必须都在磁盘上（节点名写错 / 调用点被删）
            var stale = new List<string>();
            foreach (var kv in expected)
            {
                var act = actual.ContainsKey(kv.Key) ? new List<string>(actual[kv.Key]) : new List<string>();
                foreach (var e in kv.Value)
                {
                    if (act.Remove(e)) continue;
                    stale.Add(kv.Key + " → " + e);
                }
            }

            Console.WriteLine($"   枚举到调用点 {CountOf(actual)} 个 / 表登记 {CountOf(expected)} 个（含 Banner·Backdrop 这类无该形参的）");
            for (var i = 0; i < notes.Count; i++) Console.WriteLine("   [note] " + notes[i]);

            Program.Check("⑳-1 磁盘上的每个调用点都在全量表里（新加调用点必须登记）",
                missing.Count == 0,
                missing.Count == 0 ? $"{CountOf(actual)} 个全部在表内" : string.Join("；", missing.ToArray()));
            Program.Check("⑳-2 表里没有多余/写错的行（节点名与实参逐行对得上）",
                stale.Count == 0, stale.Count == 0 ? "0 个失配" : string.Join("；", stale.ToArray()));

            var mustEat = new[]
            {
                "InventoryPanel.cs|InventoryBg", "CharacterPanel.cs|CharstatBg",
                "SkillTreePanel.cs|TreeBackPage0", "SkillTreePanel.cs|TreeBackPageK",
                "QuestLogPanel.cs|QuestBg",
            };
            for (var i = 0; i < mustEat.Length; i++)
            {
                var parts = mustEat[i].Split('|');
                var key = parts[1] + "#t";
                var ok = actual.ContainsKey(parts[0]) && actual[parts[0]].Contains(key);
                Program.Check($"⑳-{3 + i} {parts[0]} 的 `{parts[1]}` 吃射线（面板矩形底图 ⇒ 面板内空白不穿透成点地面）",
                    ok, ok ? "raycastTarget=true" : "未找到（应为 true）");
            }

            var mini = actual.ContainsKey("MiniMapPanel.cs") ? actual["MiniMapPanel.cs"] : new List<string>();
            Program.Check("⑳-8 `MiniMapPanel` 的**满屏** Backdrop 仍不吃射线（Tab 看地图时必须还能点地面走）",
                mini.Contains("Backdrop#f"),
                mini.Contains("Backdrop#f") ? "raycastTarget=false" : "被改成 true ⇒ 回退缺陷！");

            Console.WriteLine("   [归口片 I] HudPanel 同族口径：`ControlPanel#f`（HUD 底条 = 面板本体，应吃）、"
                + "`MiniPanel#f`（右上小面板底，应吃）—— 本片**不许碰** `UI/HudPanel.cs`，已记入回报");
            Program.Check("⑳-9 HudPanel 现状已按表登记（真值仍 false，未改；改动归口 = 片 I）",
                actual.ContainsKey("HudPanel.cs") && actual["HudPanel.cs"].Contains("ControlPanel#f")
                && actual["HudPanel.cs"].Contains("MiniPanel#f"),
                "ControlPanel#f + MiniPanel#f（未改，见回报）");
            Console.WriteLine();
        }

        /// <summary>
        /// 枚举 `client/Assets/Scripts/UI/*.cs` 里的 `UiArt.Panel/FullPanel/Art/Banner/Backdrop(` 调用点。
        /// 返回 文件名 → "节点名#t|f|n" 的多重集。
        /// </summary>
        private static Dictionary<string, List<string>> ScanRaycastCallSites(out List<string> notes)
        {
            notes = new List<string>();
            var map = new Dictionary<string, List<string>>();
            var re = new Regex(@"UiArt\.(Panel|FullPanel|Art|Banner|Backdrop)\s*\(");

            var files = Directory.GetFiles(Program.UiDir, "*.cs", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);

            for (var f = 0; f < files.Length; f++)
            {
                var file = Path.GetFileName(files[f]);
                var src = StripComments(File.ReadAllText(files[f]));
                var list = new List<string>();

                foreach (Match m in re.Matches(src))
                {
                    var open = m.Index + m.Length - 1;             // '(' 的下标
                    var close = MatchParen(src, open);
                    if (close < 0)
                    {
                        notes.Add(file + "：括号不配平（源码被截断？）");
                        continue;
                    }

                    var method = m.Groups[1].Value;
                    var body = src.Substring(open + 1, close - open - 1);
                    var args = SplitTopLevel(body);
                    var name = NodeNameOf(method, args);

                    var flag = "f";
                    var hasParam = (method == "Panel" || method == "Art") ? args.Count >= 6
                        : method == "FullPanel" ? args.Count >= 4 : false;
                    if (!hasParam && (method == "Banner" || method == "Backdrop")) flag = "n";
                    else if (hasParam)
                    {
                        var last = args[args.Count - 1].Trim();
                        if (last == "true") flag = "t";
                        else if (last == "false") flag = "f";
                        else
                        {
                            flag = "?";
                            notes.Add(file + $"：`{name}` 的 raycastTarget 实参不是 true/false 字面量（\"{last}\"）");
                        }
                    }

                    list.Add(name + "#" + flag);
                }

                if (list.Count > 0) map[file] = list;
            }

            return map;
        }

        /// <summary>调用点里的**节点名**：第 2 个实参若是字符串字面量就用它，否则 "?"（变量名）。</summary>
        private static string NodeNameOf(string method, List<string> args)
        {
            if (method == "Backdrop") return "(Backdrop)";       // 形参里第 2 个是 spritePath，不是节点名
            if (args.Count < 2) return "?";

            var m = Regex.Match(args[1].Trim(), "^\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "?";
        }

        /// <summary>去掉 `//` 行注释与 `/* */` 块注释（注释里提到调用语法时不许算成真调用点）。</summary>
        private static string StripComments(string src)
        {
            var sb = new System.Text.StringBuilder(src.Length);
            var i = 0;
            while (i < src.Length)
            {
                if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    continue;
                }
                if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                    i = Math.Min(src.Length, i + 2);
                    continue;
                }
                sb.Append(src[i]);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>从 <paramref name="open"/> 处的 '(' 找配对的 ')'（忽略字符串字面量里的括号）。</summary>
        private static int MatchParen(string src, int open)
        {
            var depth = 0;
            var inStr = false;
            for (var i = open; i < src.Length; i++)
            {
                var c = src[i];
                if (inStr)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; continue; }
                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        /// <summary>按**顶层**逗号拆实参。</summary>
        private static List<string> SplitTopLevel(string body)
        {
            var args = new List<string>();
            var depth = 0;
            var inStr = false;
            var cur = new System.Text.StringBuilder();

            for (var i = 0; i < body.Length; i++)
            {
                var c = body[i];
                if (inStr)
                {
                    cur.Append(c);
                    if (c == '\\' && i + 1 < body.Length) { cur.Append(body[++i]); continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; cur.Append(c); continue; }
                if (c == '(' || c == '[' || c == '{') { depth++; cur.Append(c); continue; }
                if (c == ')' || c == ']' || c == '}') { depth--; cur.Append(c); continue; }
                if (c == ',' && depth == 0) { args.Add(cur.ToString()); cur.Length = 0; continue; }
                cur.Append(c);
            }
            if (cur.Length > 0) args.Add(cur.ToString());
            return args;
        }

        private static int CountOf(Dictionary<string, string[]> d)
        {
            var n = 0;
            foreach (var kv in d) n += kv.Value.Length;
            return n;
        }

        private static int CountOf(Dictionary<string, List<string>> d)
        {
            var n = 0;
            foreach (var kv in d) n += kv.Value.Count;
            return n;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ㉑ R5：名牌口径（数值级：格→世界；源码级：名字/字号/尺寸/颜色）
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckNameplateRules()
        {
            Console.WriteLine("── ㉑ R5：地面物品名牌口径（几何用生产代码算；样式钉死为既有口径）──");

            var viewPath = Path.Combine(Program.UiDir, "GroundItemLabelView.cs");
            if (!File.Exists(viewPath))
            {
                Program.Check("㉑-0 名牌层文件在位（`UI/GroundItemLabelView.cs`）", false, "缺文件");
                Console.WriteLine();
                return;
            }
            var src = File.ReadAllText(viewPath);

            // ① 几何：名牌世界点 = 格中心 + 半格高 ⇒ 落在**该格顶边**（用生产代码 `Iso` + `GameConst` 算）
            var halfH = GameConst.IsoHalfH;
            var okGeom = true;
            var detail = new List<string>();
            foreach (var g in new[] { new Vector2Int(0, 0), new Vector2Int(3, 7), new Vector2Int(17, 26) })
            {
                var center = Iso.GridToWorld(g.x, g.y);
                var top = new Vector3(center.x, center.y + halfH, center.z);
                // 期望：顶边 = 格中心上抬半格高；且顶边 y 恰为「菱形顶点」= 中心 + 半格
                var expectTopY = center.y + halfH;
                var ok = Mathf.Abs(top.y - expectTopY) < 1e-5f
                         && Mathf.Abs(halfH - 0.5f) < 1e-5f
                         && Mathf.Abs(top.x - center.x) < 1e-5f;
                if (!ok) okGeom = false;
                detail.Add($"格({g.x},{g.y}) 中心=({center.x:0.##},{center.y:0.##}) → 名牌点=({top.x:0.##},{top.y:0.##})");
            }
            Program.Check("㉑-1 名牌世界点 = 格中心 + `GameConst.IsoHalfH`(0.5) ⇒ 落在该格**顶边**（`Iso.GridToWorld` 实算）",
                okGeom, string.Join(" ｜ ", detail.ToArray()));

            Program.Check("㉑-2 名牌层用的是 `GameConst.IsoHalfH` 抬升（⛔ 不是自己写 0.5 / 魔数）",
                src.Contains("GameConst.IsoHalfH"),
                src.Contains("GameConst.IsoHalfH") ? "源码命中 GameConst.IsoHalfH" : "0 命中");

            // ② 尺寸：一格的像素盒 = IsoTilePxW × HalfTilePxH（契约常量派生，不写字面量）
            Program.Check($"㉑-3 名牌尺寸 = 一格像素盒 `GameConst.IsoTilePxW`({GameConst.IsoTilePxW}) × "
                + $"`GameConst.HalfTilePxH`({GameConst.HalfTilePxH:0.#})",
                src.Contains("GameConst.IsoTilePxW") && src.Contains("GameConst.HalfTilePxH"),
                $"{GameConst.IsoTilePxW}×{GameConst.HalfTilePxH:0.#}");

            // ③ 字体：原版最小号 Font16（项目 UI 口径），不是系统字体
            Program.Check("㉑-4 名牌字体 = `D2Text.D2Font.Font16`（原版位图字模，项目 UI 口径；⛔ 不自创字体）",
                src.Contains("D2Text.D2Font.Font16"),
                src.Contains("D2Text.D2Font.Font16") ? "Font16" : "未用 Font16");

            // ④ 颜色：品质色表（与 item tooltip 同一张表）—— 逐品质贴 hex
            var sameSource = true;
            var hexes = new List<string>();
            foreach (ItemQuality q in Enum.GetValues(typeof(ItemQuality)))
            {
                var c = ItemQualityColor.Of(q);
                Color32 c32 = c;
                hexes.Add($"{q}=#{c32.r:X2}{c32.g:X2}{c32.b:X2}");
                var a = ItemQualityColor.Of(q);
                var b = ItemQualityColor.Of(q);
                if (a != b) sameSource = false;
            }
            Program.Check("㉑-5 名牌颜色 = `ItemQualityColor.Of(品质)`（与 `UI/ItemTooltip` 的名称配色**同一张表**）",
                sameSource && src.Contains("ItemQualityColor.Of") && hexes.Count == 5,
                string.Join(" / ", hexes.ToArray()));

            // ⑤ 名字退化：名字空 ⇒ 「物品 #id」（不是空串、不是静默不画）
            Program.Check("㉑-6 名字为空 ⇒ 退化为「物品 #id」（不静默、不画空串）",
                Regex.IsMatch(src, @"string\.IsNullOrEmpty\(l\.name\)") && src.Contains("\"物品 #\""),
                "含 string.IsNullOrEmpty(l.name) ? \"物品 #\" + l.id : l.name");

            // ⑥ 隐藏/过滤语义：null 载荷 ⇒ Clear；非法条目（null / id<0）⇒ 跳过
            Program.Check("㉑-7 载荷空 ⇒ 全部隐藏；条目 null / id<0 ⇒ 跳过（不崩、不画错位置）",
                src.Contains("Clear()") && Regex.IsMatch(src, @"l == null \|\| l\.id < 0\) continue"),
                "Apply 内 Clear() + `l == null || l.id < 0) continue`");

            // ⑦ DTO 契约字段在位（UI 与发送方靠它对齐；改字段名会两边一起错）
            var t = typeof(GroundItemLabelsArgs);
            var fields = new[] { "altHeld" };
            var dtoOk = t.GetField("labels") != null;
            for (var i = 0; i < fields.Length; i++) dtoOk &= t.GetField(fields[i]) != null;
            var lt = typeof(GroundItemLabel);
            var itemOk = lt.GetField("id") != null && lt.GetField("name") != null
                         && lt.GetField("quality") != null && lt.GetField("gridX") != null
                         && lt.GetField("gridY") != null;
            Program.Check("㉑-8 `Def.GroundItemLabelsArgs{altHeld,labels}` + `Def.GroundItemLabel{id,name,quality,gridX,gridY}` 字段在位",
                dtoOk && itemOk, "载荷 = (id, name, quality, gridX, gridY) + altHeld");
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ㉒ R5：链路存在性（发送方 + 收方）—— 反射 + 源码
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckNameplateWiring()
        {
            Console.WriteLine("── ㉒ R5：名牌链路两端（发送方 Emit / 收方 Apply）──");

            // ① 收方类型 + 方法（反射）
            var viewType = typeof(GroundItemLabelView);
            Program.Check("㉒-1 名牌层类型在位（`UI/GroundItemLabelView.cs`）", viewType != null, viewType.FullName);

            var apply = viewType.GetMethod("Apply", BindingFlags.Public | BindingFlags.Instance);
            Program.Check("㉒-2 名牌层有 `Apply(Def.GroundItemLabelsArgs)`（事件的落点）",
                apply != null && apply.GetParameters().Length == 1
                && apply.GetParameters()[0].ParameterType == typeof(GroundItemLabelsArgs),
                apply == null ? "缺失" : apply.GetParameters()[0].ParameterType.Name);

            var prefix = viewType.GetField("NodePrefix", BindingFlags.Public | BindingFlags.Static);
            var prefixVal = prefix != null ? prefix.GetRawConstantValue() as string : null;
            Program.Check("㉒-3 名牌节点名前缀常量在位（离线/实机都能按它查节点）",
                !string.IsNullOrEmpty(prefixVal), "NodePrefix=" + (prefixVal ?? "(缺失)"));

            // ② 收方接线：HudPanel 订阅 + 创建 + Apply（源码级）
            var hud = File.ReadAllText(Path.Combine(Program.UiDir, "HudPanel.cs"));
            Program.Check("㉒-4 收方接线：`UI/HudPanel.cs` 订阅 `Events.GroundItemLabelsChanged`（`Def` 载荷）",
                hud.Contains("Game.Event.On<GroundItemLabelsArgs>(Events.GroundItemLabelsChanged"),
                "HudPanel.cs 命中 On<GroundItemLabelsArgs>(Events.GroundItemLabelsChanged, …)");
            Program.Check("㉒-5 收方接线：HudPanel 建名牌层并 `Apply(args)`",
                hud.Contains("new GroundItemLabelView(") && hud.Contains(".Apply("),
                "new GroundItemLabelView(transform) + _groundLabels.Apply(args)");

            var inputPath = Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts",
                "Module", "Input", "InputReader.cs");
            var inputSrc = File.Exists(inputPath) ? File.ReadAllText(inputPath) : string.Empty;
            Program.Check("㉒-6 发送方：`Module/Input/InputReader.cs` 里 Emit `Events.GroundItemLabelsChanged`",
                inputSrc.Contains("Events.GroundItemLabelsChanged"),
                inputSrc.Contains("Events.GroundItemLabelsChanged") ? "已 Emit" : "未找到 ⇒ R5 只剩半条链");
            Program.Check("㉒-7 `InputReader.ShowGroundItems`（原版 Alt）不再是死属性（有消费点）",
                inputSrc.Contains("PublishGroundItemLabels") && inputSrc.Contains("ShowGroundItems"),
                "`PublishGroundItemLabels` 消费 `ShowGroundItems`");

            // ④ Alt 常显的数据源（全部地面物品）在位
            var pickerPath = Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts",
                "Module", "Input", "HoverPicker.cs");
            var picker = File.Exists(pickerPath) ? File.ReadAllText(pickerPath) : string.Empty;
            Program.Check("㉒-8 Alt 常显的数据源在位（`HoverPicker.AllLabels` = 当前区域全部地面物品）",
                picker.Contains("AllLabels") && picker.Contains("ItemQualityOf"),
                "AllLabels + ItemQualityOf（片 I：Module/Input/HoverPicker.cs）");

            // ⑤ 名牌不吃鼠标事件（否则"点地面走"会被名牌挡掉）
            var viewSrc = File.ReadAllText(Path.Combine(Program.UiDir, "GroundItemLabelView.cs"));
            Program.Check("㉒-9 名牌节点不吃鼠标事件（源码 0 命中 raycastTarget；节点 = 无 Graphic 的 `D2Label`）",
                !viewSrc.Contains("raycastTarget"),
                "0 命中 raycastTarget");

            // ⑥ 分层：名牌层只收 Def 载荷，不许 using Diablo2.Module
            Program.Check("㉒-10 名牌层零 `using Diablo2.Module`（分层自检 ③）",
                !Regex.IsMatch(viewSrc, @"^\s*using\s+Diablo2\.Module", RegexOptions.Multiline), "0 命中");
            Console.WriteLine();
        }
    }
}
