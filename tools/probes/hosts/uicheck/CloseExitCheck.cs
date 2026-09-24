// ─────────────────────────────────────────────────────────────────────────────
// uicheck · 弹框「关闭出口」机械判据（片 R8-close / u53-closefix，2026-09-24）
//
// 挡的是什么缺陷（用户第三批投诉：「你检查所有弹框…甚至连他妈的关闭都没有」）：
//   面板开在 `UILayer.Popup` 时，引擎会插一块**全屏模态遮罩**
//   （`clover-client-unity-engine/Runtime/Presentation/UI.cs:155-159` → `:443-461` `ShowMask()`，
//   `img.raycastTarget = true`，挂在 `_layers[Popup]` 首位）⇒ 遮罩**之下**的一切（含 `Normal` 的 HUD）
//   既被压暗、也吃不到射线；面板自己若又没有可见关闭控件 ⇒ **纯鼠标玩家关不掉它**。
//
// 判据（**判过程不判结果**：读的是"源码里有没有这条出口"，不是"某个数字改绿了"）：
//   `Ok = CloseVisible || (Layer != "Popup" && (HudEntry || Hotkey))`
//     · `CloseVisible` —— 屏上有**看得见**的关闭标记（不是 alpha=0 的命中区）；
//     · `Layer != Popup` —— 引擎不会插遮罩 ⇒ HUD/世界仍可点 ⇒ 鼠标能再点一次入口把屏关掉；
//     · `HudEntry` —— 该屏出现在 `UI/HudPanel.cs` 的**小面板入口**（`AddMiniButton(... nameof(屏))`）；
//     · `Hotkey`   —— 该屏出现在 `HudPanel.cs` 的 `PollHotkey(..., nameof(屏))`（同一入口键 = 开合同一颗键）。
//   即「**要么屏自己有可见关闭控件，要么同一个入口（HUD 按钮/热键）还能再点一次关掉它**」。
//
// 覆盖口径 = 影响域（R8-close 只动了这几屏的层与关闭控件）：
//   `InventoryPanel`（关闭控件可见化）· `SkillTreePanel` / `QuestLogPanel`（Popup → Normal）
//   · `MiniMapPanel`（本来就 Normal，登记"无控件"为允许差异）。
//   ⚠️ `CharacterPanel` 归片 `charstat`、`ShopPanel` 归片 `shopart` ⇒ **只打印读数，不参与判定**
//   （避免跨片把别人的进行中改动算成红）。
//
// 退化样本（**同一 `Judge` 判，同进程对立读数，不是文字声明**）：
//   ① 修前 `InventoryPanel` 的关闭按钮形状（`new Color(1f,1f,1f,0f)` 命中区 + 无任何可见标记）
//      ⇒ `CloseVisible=false` 且 `Layer=Popup` ⇒ 必须**不合格**；
//   ② 修前 `SkillTreePanel` 形状（`Layer => UILayer.Popup` + 全文无关闭控件）⇒ 必须**不合格**；
//   ③ 修前 `QuestLogPanel` 形状（同上）⇒ 必须**不合格**。
//   三条退化样本若有一条"合格"，说明本判据没有在判该判的东西 ⇒ 直接报错。
//
// 判据的**自认边界**（不夸大）：`CloseVisible` 是**源码结构**判据（节点底色 alpha / 后续贴原版贴图 /
//   其下有没有一条 alpha=1 的位图字模标记），**不是像素判据**；"点下去到底关不关"属实机表现类，
//   由 Play 驱动 `tools/probes/drivers/d2u3_popups_drive.cs` 的 `CLOSE-RESULT before/after` 采。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Uicheck
{
    /// <summary>「关闭出口」自检（离线源码判据）。</summary>
    public static class CloseExitCheck
    {
        private static void Check(string what, bool ok, string detail) => Program.Check(what, ok, detail);

        /// <summary>影响域：本片负责的屏（逐条给出为什么在这一格）。</summary>
        private static readonly (string Panel, string Why)[] Scope =
        {
            ("InventoryPanel", "关闭按钮从 alpha=0 命中区改成可见的原版字模「X」"),
            ("SkillTreePanel", "Popup → Normal（遮罩消失 ⇒ HUD「技能樹 T」入口可点 = 同一入口开合）"),
            ("QuestLogPanel", "Popup → Normal（同上；本屏无关闭控件，出口 = HUD「任務記錄 Q」/Q 键）"),
            ("MiniMapPanel", "本来就 Normal ⇒ 只需登记'无控件'为允许差异；出口 = Tab / HUD「自動地圖」"),
        };

        /// <summary>只打印读数、不参与判定的屏（归别的片）。</summary>
        private static readonly string[] InfoOnly = { "CharacterPanel", "ShopPanel" };

        /// <summary>一个屏的读数。</summary>
        public sealed class Reading
        {
            public string Panel;
            public string Layer = "(未声明)";
            public bool CloseNode;          // 源码里有名为 CloseButton/Close 的控件节点
            public bool CloseVisible;       // 该节点在屏上**看得见**（非 alpha=0 且无可见标记）
            public bool HudEntry;           // HudPanel.cs 的小面板按钮里有它
            public bool Hotkey;             // HudPanel.cs 的 PollHotkey 里有它

            /// <summary>判据本体（纯函数 ⇒ 退化样本可喂进同一判据）。</summary>
            public bool Ok
            {
                get { return CloseVisible || (Layer != "Popup" && (HudEntry || Hotkey)); }
            }

            public string Row()
            {
                return string.Format("│ {0,-16} 层={1,-7} 关闭节点={2,-4} 可见={3,-4} HUD入口={4,-4} 热键={5,-4} ⇒ {6}",
                    Panel, Layer, CloseNode ? "有" : "无", CloseVisible ? "是" : "否",
                    HudEntry ? "有" : "无", Hotkey ? "有" : "无", Ok ? "合格" : "不合格");
            }
        }

        /// <summary>源码 → 读数（纯函数；`hudSrc` = `UI/HudPanel.cs` 全文）。</summary>
        public static Reading Judge(string panel, string src, string hudSrc)
        {
            var r = new Reading { Panel = panel };

            // ⛔ 先剥注释再判：本片自己的文档注释里写了 `UiArt.SetSprite(close, …)`（作为"拿到原版 X 后
            //    怎么换"的说明）—— 不剥就会被 rule ② 当成"真的贴了贴图"⇒ 退化样本会假绿。实测踩到过。
            src = StripComments(src);

            var m = Regex.Match(src, @"Layer\s*=>\s*UILayer\.(\w+)");
            if (m.Success) r.Layer = m.Groups[1].Value;

            r.HudEntry = Regex.IsMatch(hudSrc, @"AddMiniButton\([^;]*nameof\(" + panel + @"\)");
            r.Hotkey = Regex.IsMatch(hudSrc, @"PollHotkey\([^;]*nameof\(" + panel + @"\)");

            // 关闭节点：`var x = UiArt.Panel(… "CloseButton" …)` 这类「建节点」调用（本项目面板都是代码建控件）
            var cm = Regex.Match(src,
                @"var\s+(\w+)\s*=\s*(?:UiArt\.Panel|UiArt\.Button|UiArt\.SquareButton|UiArt\.OrigButton|UIFactory\.CreateButton)\([^;]*?""(CloseButton|Close)""");
            if (cm.Success)
            {
                r.CloseNode = true;
                r.CloseVisible = IsVisible(src, cm.Groups[1].Value);
            }
            return r;
        }

        /// <summary>剥掉 `/* */` 与 `//` 注释（判据只看代码，不看注释里的"以后要怎么改"）。</summary>
        private static string StripComments(string src)
        {
            src = Regex.Replace(src, @"/\*[\s\S]*?\*/", " ");
            return Regex.Replace(src, @"//[^\n]*", " ");
        }

        /// <summary>关闭节点是否"看得见"（源码结构口径，见文件头「自认边界」）。</summary>
        private static bool IsVisible(string src, string v)
        {
            // ① 建节点时的底色 alpha 不是 0
            var dm = Regex.Match(src, @"var\s+" + v + @"\s*=\s*UiArt\.Panel\([^;]*?\);");
            if (dm.Success && !AlphaZero(dm.Value)) return true;

            // ② 之后贴过原版贴图 / 显式置成原版亮度白
            if (Regex.IsMatch(src, @"UiArt\.SetSprite\(\s*" + v + @"\s*,")) return true;
            if (Regex.IsMatch(src, v + @"\.color\s*=\s*Color\.white")) return true;

            // ③ 在它下面画了一条**不透明**的标记（位图字模标签 / uGUI Text）
            var lm = Regex.Match(src, @"(?:D2Label\.Create|UiArt\.Label)\(\s*" + v + @"\.transform\s*,[^;]*?\);");
            if (lm.Success && HasOpaqueColor(lm.Value)) return true;

            return false;
        }

        /// <summary>`new Color(r,g,b,a)` 的 a 是否为 0（三参形态 = 不透明度 1）。</summary>
        private static bool AlphaZero(string text)
        {
            var cm = Regex.Match(text, @"new\s+Color\(([^)]*)\)");
            if (!cm.Success) return false;
            var parts = cm.Groups[1].Value.Split(',');
            if (parts.Length < 4) return false;
            var a = parts[parts.Length - 1].Trim().TrimEnd('f', 'F');
            return a == "0" || a == "0.0" || a == ".0";
        }

        /// <summary>调用参数里有没有一个不透明色（既有配色常量或显式 alpha=1）。</summary>
        private static bool HasOpaqueColor(string call)
        {
            return call.Contains("UiArt.TitleColor") || call.Contains("UiArt.TextColor")
                   || call.Contains("Color.white") || Regex.IsMatch(call, @",\s*1f\s*\)");
        }

        public static void Run()
        {
            Console.WriteLine("── (R8-close) 弹框关闭出口：层/遮罩 + 关闭控件可见 + 同一入口（离线源码判据）──");

            var hudPath = Path.Combine(Program.UiDir, "HudPanel.cs");
            if (!File.Exists(hudPath))
            {
                Check("R8-close HudPanel.cs 在位", false, "找不到 " + hudPath);
                return;
            }
            var hudSrc = File.ReadAllText(hudPath);

            // ── ① 影响域：逐屏读数 + 判定 ───────────────────────────────────────
            var readings = new System.Collections.Generic.List<Reading>();
            foreach (var (panel, why) in Scope)
            {
                var path = Path.Combine(Program.UiDir, panel + ".cs");
                var exists = File.Exists(path);
                Check($"R8-close 影响域屏 {panel} 源文件在位", exists, path);
                if (!exists) continue;

                var r = Judge(panel, File.ReadAllText(path), hudSrc);
                readings.Add(r);
                Console.WriteLine(r.Row());
                Check($"R8-close {panel}：有可见关闭控件 或 （非 Popup 层 + HUD/热键同一入口）",
                    r.Ok, $"层={r.Layer} 关闭节点={r.CloseNode} 可见={r.CloseVisible} "
                          + $"HUD入口={r.HudEntry} 热键={r.Hotkey} —— {why}");

                // 「修前形状」正是本判据挡的那种：Popup 层 + 无可见关闭 ⇒ 必须不合格
                if (r.Layer == "Popup" && !r.CloseVisible)
                    Check($"R8-close {panel} 不是「遮罩锁死鼠标」的形状", false,
                        "层=Popup（引擎插全屏遮罩）且屏上没有可见关闭控件 ⇒ 纯鼠标玩家无出口");
            }

            // 影响域里至少要有一屏真的被读到了（防空转绿灯）
            Check("R8-close 影响域读数非空", readings.Count == Scope.Length,
                $"读到 {readings.Count} / 期望 {Scope.Length}");

            // ── ② 只报读数（归别的片，不参与判定）──────────────────────────────
            foreach (var panel in InfoOnly)
            {
                var path = Path.Combine(Program.UiDir, panel + ".cs");
                if (!File.Exists(path)) continue;
                var r = Judge(panel, File.ReadAllText(path), hudSrc);
                Console.WriteLine("│ [INFO 不参与判定 · 归别的片] " + r.Row().Substring(2));
            }

            // ── ③ 退化样本：把「修前形状」喂进**同一个 Judge**，必须变红 ──────────
            var afterInv = Path.Combine(Program.UiDir, "InventoryPanel.cs");
            var afterSkill = Path.Combine(Program.UiDir, "SkillTreePanel.cs");
            var afterQuest = Path.Combine(Program.UiDir, "QuestLogPanel.cs");

            // 退化 A：修前背包关闭按钮（透明命中区 + 无可见标记）
            var degA = File.Exists(afterInv)
                ? Regex.Replace(File.ReadAllText(afterInv),
                    @"\s*D2Label\.Create\(close\.transform,\s*""CloseMark""[\s\S]*?\);", "",
                    RegexOptions.Singleline)
                : "";
            var ra = Judge("InventoryPanel", degA, hudSrc);
            Check("R8-close 退化 A（背包关闭钮 alpha=0 且无可见标记）必须不合格",
                degA.Length > 0 && !ra.Ok && ra.Layer == "Popup",
                $"层={ra.Layer} 可见={ra.CloseVisible} ⇒ Ok={ra.Ok}");

            // 退化 B：修前技能树（Popup + 无关闭控件）
            var degB = File.Exists(afterSkill)
                ? Regex.Replace(File.ReadAllText(afterSkill), @"UILayer\.Normal", "UILayer.Popup")
                : "";
            var rb = Judge("SkillTreePanel", degB, hudSrc);
            Check("R8-close 退化 B（技能树 Popup 层 + 无关闭控件）必须不合格",
                degB.Length > 0 && !rb.Ok,
                $"层={rb.Layer} 关闭节点={rb.CloseNode} HUD入口={rb.HudEntry} ⇒ Ok={rb.Ok}");

            // 退化 C：修前任务日志（Popup + 无关闭控件）
            var degC = File.Exists(afterQuest)
                ? Regex.Replace(File.ReadAllText(afterQuest), @"UILayer\.Normal", "UILayer.Popup")
                : "";
            var rc = Judge("QuestLogPanel", degC, hudSrc);
            Check("R8-close 退化 C（任务日志 Popup 层 + 无关闭控件）必须不合格",
                degC.Length > 0 && !rc.Ok,
                $"层={rc.Layer} 关闭节点={rc.CloseNode} HUD入口={rc.HudEntry} ⇒ Ok={rc.Ok}");

            // 退化样本必须**真的改到了源码**（否则"变红"可能来自别的原因）
            Check("R8-close 退化样本确实替换了源码",
                degA.Length > 0 && degB.Length > 0 && degC.Length > 0
                && degA != File.ReadAllText(afterInv) && degB != File.ReadAllText(afterSkill)
                && degC != File.ReadAllText(afterQuest),
                "三条退化文本均非空且与当前源码不同");
        }
    }
}
