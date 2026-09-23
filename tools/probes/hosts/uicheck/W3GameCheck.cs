// ─────────────────────────────────────────────────────────────────────────────
// ★ w3 片：**游戏内 UI（HUD / 背包 / 人物属性 / 技能树 / 任务日志 / 小地图 / 图标 / 字模）逐控件审计**
// 的离线断言（节 ㉑）。
//
// 判据全部**可离线计算**（不需要 Unity 原生、不进 Play）：
//   ① **素材侧**：每个"带原版素材"的元件 —— 文件在位、**IHDR == 声明的原版尺寸**、
//      且**矩形 == 声明的原版尺寸 ×1.8**（对"由 prefab 容器定尺"的元件只判比例，见行内 `Exact`）。
//   ② **几何侧**：矩形 / 素材原生尺寸的**宽高比一致**（拦住"矩形本身就写错了比例 ⇒ 被拉伸"）。
//      例外必须**逐条写明理由**（同质填充条 / 原版 prefab 自身的不对称），⛔ 不许静默放行。
//   ③ **双套素材侧**：工程引用的必须是**本项目 DC6 直出**那一套（调色板索引 0 = 透明），
//      ⛔ 不是社区复刻工程的副本（同画面，但把原版透明像素写成了**不透明黑**）。
//   ④ **图标侧**：配表 code/official_id → 磁盘上的原版图标**命中率**（缺口不得变多）。
//   ⑤ **字模侧**：UI 源码里**字符串字面量**的每个 CJK 字都能落到原版 chi 字模
//      （字模表 + 简繁映射）—— 落不到 = 画面上静默缺字（原版行为是"不画"）。
//
// 为什么必须离线钉住：面板实例在离线进程造不出来（`new GameObject()` 需要原生运行时），
// 而"底图是不是原版那张 / 矩形 == 原版×1.8 / 素材比例 == 矩形比例 / 该有却没有"这四类判据
// **都是纯数据 + 磁盘事实**。进 Play 才能判的（字模观感、贴图色调、光标、填充真的在动）
// 在本文件末尾声明。
//
// ⛔ 只读断言，不改任何工程产物。
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Diablo2.Core;
using Diablo2.UI;
using UnityEngine;

namespace Uicheck
{
    internal static class W3GameCheck
    {
        /// <summary>一个"带原版素材"的游戏内元件。</summary>
        private struct Row
        {
            public string Panel;     // 面板名（回报/日志里的归属）
            public string Control;   // 控件名
            public string Path;      // 素材路径（`Resources/Clover/` 之下，不含 .png）
            public float OrigW;      // 声明的原版尺寸（原版 px）
            public float OrigH;
            public float RectW;      // 元件矩形（画布单位）
            public float RectH;
            public bool Exact;       // true ⇒ 断言 矩形 == 声明原版尺寸 × K
            public bool SkipIhdr;    // true ⇒ 素材是"多帧条带"（IHDR 是整幅，不等于声明帧尺寸）
            public float Tol;        // 宽高比容差（相对；0 = 用默认 1e-2）
            public string Note;      // 例外/说明（非空即在报告里点名）
        }

        private const float K = 1.8f;

        public static void Run()
        {
            Console.WriteLine("── ㉑ ★ w3：游戏内 UI 逐控件审计（原版素材 IHDR ↔ 工程常量 ↔ 面板源码）──");
            ArtSide();
            EquipArtSide();             // ★ w4：装备槽（逐像素核对"声明 == 素材实测"）
            SkillTreeSide();            // ★ w4：技能树「拼装后整页 vs 素材帧尺寸」
            DualSetSide();
            IconSide();
            FontSide();
            CursorSide();
            BoxFrameSide();              // ★ w5：选项/暂停底板 = 原版 boxpieces 拼装窗框（偏移 + 接缝自证）
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ① 素材侧 + ② 几何侧
        // ═════════════════════════════════════════════════════════════════════
        private static List<Row> BuildRows()
        {
            var rows = new List<Row>();
            void Add(string panel, string control, string path, float ow, float oh, Vector2 rect,
                bool exact = true, bool skipIhdr = false, float tol = 0f, string note = "")
            {
                rows.Add(new Row
                {
                    Panel = panel, Control = control, Path = path,
                    OrigW = ow, OrigH = oh, RectW = rect.x, RectH = rect.y,
                    Exact = exact, SkipIhdr = skipIhdr, Tol = tol, Note = note,
                });
            }

            // ── HUD（原版 `ControlPanel.prefab` 节点值 ×1.8；素材尺寸 = 磁盘 IHDR）──
            Add("HUD", "控制面板底图", ResPaths.D2UiPanel + "ControlPanel", 948f, 160f, UiLayoutGame.HudBgSize);
            // 球的填充块：rect = prefab 的 Lifebulb/Manabulb 容器（108×108），素材 80×80
            // ⇒ 等比放大 1.35（正方→正方，不变形）。容器的子节点 sizeDelta 无出处 ⇒ 只判比例。
            Add("HUD", "生命球填充", ResPaths.PanelHealthBar, 80f, 80f,
                new Vector2(UiLayoutGame.OrbSize, UiLayoutGame.OrbSize), exact: false);
            Add("HUD", "法力球填充", ResPaths.PanelManaBar, 80f, 80f,
                new Vector2(UiLayoutGame.OrbSize, UiLayoutGame.OrbSize), exact: false);
            // 经验条填充 = 同质纯色条（`AssetImporter.NineSlices` 给它的 border = 0 并注明
            // "整幅同质（纯填充条）⇒ 无边框，任意拉伸都不失真"）⇒ 允许非等比拉伸。
            Add("HUD", "经验条填充", ResPaths.D2UiPanel + "ExperienceBar", 50f, 5f,
                UiLayoutGame.ExpBarSize, exact: false, tol: 1f,
                note: "同质填充条（AssetImporter.NineSlices 的 border=0 + 实测整幅同质）⇒ 允许非等比拉伸");
            Add("HUD", "经验条覆盖层", ResPaths.D2UiPanel + "ExperienceBarOverlay", 948f, 160f,
                UiLayoutGame.HudBgSize);
            Add("HUD", "小面板底图", ResPaths.D2UiPanel + "minipanel", 173f, 26f, HudPanel.MiniPanelSize,
                note: "★ w3 修正：素材原生 173×26（prefab 节点写 152×26 ⇒ 照它贴会横向压 12%）");
            Add("HUD", "小面板按钮", UiArt.MiniPanelBtnFrame(0), 20f, 20f,
                new Vector2(HudPanel.MiniButtonSize, HudPanel.MiniButtonSize));
            Add("HUD", "小面板开关箭头", UiArt.ArrowFrame(0), 15f, 24f, HudPanel.MiniPanelArrowSize);
            Add("HUD", "走按钮", UiArt.RunButtonWalkFrame, 16f, 20f, HudPanel.RunButtonSize);
            Add("HUD", "跑按钮", UiArt.RunButtonRunFrame, 16f, 20f, HudPanel.RunButtonSize);
            // 球高光遮罩：`overlap` 是 256×128 的**条带**（2 帧各 82×88，见 AssetImporter.MultiFrameStrips）
            // ⇒ IHDR 不等于帧尺寸，跳过 A2；帧 82×88 铺进 108² 的球 = 非等比 7%。
            Add("HUD", "球高光遮罩", ResPaths.PanelOverlap, 82f, 88f,
                new Vector2(UiLayoutGame.OrbSize, UiLayoutGame.OrbSize), exact: false, skipIhdr: true,
                tol: 1f,
                note: "原版 `HealthBulbOverlay` 的 sizeDelta 无出处（原版 prefab 不在本机）⇒ 铺满球容器，"
                      + "非等比 7%（登记为待确认，见审计 TSV）");

            // ── 背包（原版 `InventoryPanel.prefab` 节点值 ×1.8）──
            Add("背包", "面板底图", ResPaths.PanelInventory, 320f, 432f, InventoryPanel.PanelSize);
            // ★ w4 修红：装备槽的绘制几何改成「**裁剪框 = 该槽在素材里的不透明内容外接框**（逐像素实测）×1.8」
            //   + 「整幅贴图按 IHDR ×1.8 1:1 摆」（`UiLayoutGame.InvEquipArt` / `InventoryPanel.BuildEquipFrame`）。
            //   旧口径（矩形 = prefab 节点值，整幅贴图塞进去）实测差 35.2%（`inv_armor` 97.2×149.4 vs 素材 64×128）。
            //   ⚠️ OrigW/OrigH 这里是**本槽图形外接框**、不是贴图 IHDR（双槽拼图的 IHDR 是两槽并排 +
            //      右侧留白）⇒ 走 `SkipIhdr`，由 `EquipArtSide()` **再解一次像素**逐槽核对「声明 == 实测」。
            foreach (var s in InventoryPanel.EquipSlots)
            {
                var stem = s.sprite.Substring(s.sprite.LastIndexOf('/') + 1);
                Add("背包", "装备槽·" + stem + (s.half == 0 ? "" : (s.half == 1 ? "(取左半=本槽图形)" : "(取右半=本槽图形)")),
                    s.sprite, s.artOrig.x, s.artOrig.y, s.artSize, exact: true, skipIhdr: true,
                    note: "裁剪框 = 素材不透明内容外接框 ×1.8；整幅贴图 " + $"{s.sheetSize.x:0.##}×{s.sheetSize.y:0.##}"
                          + " 按 IHDR ×1.8 **1:1 摆**（每轴缩放比都是 1.8，不得拉伸/不得带出隔壁槽图案）");
            }

            // ── 人物属性（原版 `CharstatPanel.prefab`）×4 行同尺寸，这里只判面板/箭头 ──
            Add("人物属性", "面板底图", ResPaths.PanelCharStat, 320f, 432f, CharacterPanel.PanelSize);
            Add("人物属性", "加点箭头", UiArt.ArrowFrame(0), 15f, 24f, UiLayoutGame.CharPlusSize);

            // ── 技能树（底图 = 原版**拼装后整页** `Panel/skltree_{cls}_back_{0..3}.png` 320×432；
            //    图标 = 原版位图 48×48）──
            // ★ w4 修红：旧行的路径写的是 `ResPaths.SkillTreeBack("a", 页)` = **逐帧落位**目录
            //   （`D2/UI/SkillTree/`，那一族的帧原生是 256×256 / 64×256 / …）⇒ 「声明 320×432 == 素材 IHDR」
            //   必然红。**红的是"审计行的路径/声明"，不是面板**：面板底图一直用 `D2Icon.SkillTreeBackPath`
            //   拼的 `D2/UI/Panel/skltree_{cls}_back_{页}.png`（= 拼好的整页 320×432）。
            //   现按运行时口径取路径，并把「拼装后整页矩形 320×432」与「素材帧尺寸 256×256/64×256/…」
            //   两个概念分别断言（见 `SkillTreeSide()` 与 `UiLayoutGame.SkillPanelSize` 的注释）。
            Add("技能树", "底图页 0（共用右列）", SkillTreePagePath("a", UiLayoutGame.SkillTabPage),
                320f, 432f, SkillTreePanel.PanelSize);
            Add("技能树", "底图页 1（系 1）", SkillTreePagePath("a", 1), 320f, 432f, SkillTreePanel.PanelSize);
            Add("技能树", "技能图标", ResPaths.SkillIcon("ama", 0), UiLayoutGame.SkillIconArtPx,
                UiLayoutGame.SkillIconArtPx, SkillTreePanel.IconCellSize,
                note: "★ w3 修正：原版位图原生 48×48（旧值取「框内径 41×46」⇒ 图标被缩到 85.4%）");

            // ── 任务日志（原版 `MENU/questbackground.dc6` 实测分区 ×1.8）──
            Add("任务日志", "面板底图", ResPaths.PanelQuestBack, 320f, 432f, QuestLogPanel.PanelSize);
            Add("任务日志", "章节页签", ResPaths.PanelQuestTabs + "_1", 78f, 30f, UiLayoutGame.QuestTabSize);
            Add("任务日志", "任务石龛", ResPaths.PanelQuestSocket + "_0", 80f, 95f, UiLayoutGame.QuestSlotSize);
            Add("任务日志", "任务图", ResPaths.QuestImage("a1q1", 0), 72f, 86f, UiLayoutGame.QuestArtSize);
            Add("任务日志", "标题条", ResPaths.Banner("quests_0"), 74f, 54f, UiLayoutGame.QuestBannerBox,
                note: "`preserveAspect` 等比落进外框（原版标题条宽度各不相同）");

            // ── 小地图（原版 `MINIMAP/mapicons.DC6` 帧 16×16；框尺寸无原版出处，登记 E23）──
            Add("小地图", "标记图标", ResPaths.MiniMapIcon(ResPaths.MiniMapMarkerFrame), 16f, 16f,
                new Vector2(UiLayoutGame.MiniMapIconPx, UiLayoutGame.MiniMapIconPx));

            return rows;
        }

        private static void ArtSide()
        {
            var rows = BuildRows();

            // A1 文件在位
            var missing = new List<string>();
            foreach (var r in rows)
                if (!PngFile(r.Path, out _)) missing.Add(r.Panel + "/" + r.Control + "=" + r.Path);
            Program.Check($"{rows.Count} 个游戏内元件的原版素材**全部在磁盘上**", missing.Count == 0,
                missing.Count == 0 ? "0 缺失" : string.Join(", ", missing.ToArray()));

            // A2 IHDR == 声明的原版尺寸
            var sizeBad = new List<string>();
            var checkedN = 0;
            foreach (var r in rows)
            {
                if (r.SkipIhdr) continue;
                if (!PngSize(r.Path, out var w, out var h)) continue;
                checkedN++;
                if (Math.Abs(w - r.OrigW) > 0.5f || Math.Abs(h - r.OrigH) > 0.5f)
                    sizeBad.Add($"{r.Panel}/{r.Control}: 声明 {r.OrigW:0}×{r.OrigH:0} vs 实测 {w}×{h}（{r.Path}）");
            }
            Program.Check($"{checkedN} 个元件的「声明原版尺寸」== 素材 IHDR 实测（0 例外）", sizeBad.Count == 0,
                sizeBad.Count == 0 ? "0 例外" : string.Join("；", sizeBad.ToArray()));

            // A3 矩形 == 声明原版尺寸 × K（对 Exact 行）
            var rectBad = new List<string>();
            var exactN = 0;
            foreach (var r in rows)
            {
                if (!r.Exact) continue;
                exactN++;
                if (Math.Abs(r.RectW - r.OrigW * K) > 0.05f || Math.Abs(r.RectH - r.OrigH * K) > 0.05f)
                    rectBad.Add($"{r.Panel}/{r.Control}: {r.RectW:0.##}×{r.RectH:0.##} vs 原版 "
                                + $"{r.OrigW:0.##}×{r.OrigH:0.##} ×{K} = {r.OrigW * K:0.##}×{r.OrigH * K:0.##}");
            }
            Program.Check($"{exactN} 个元件：**矩形 == 原版像素 ×1.8**（逐个逐轴相等；这拦的就是"
                          + "「拿 prefab 节点值冒充素材原生尺寸」那类错）", rectBad.Count == 0,
                rectBad.Count == 0 ? "0 例外" : string.Join("；", rectBad.ToArray()));

            // ② 宽高比（防非等比拉伸）
            var aspectBad = new List<string>();
            var aspectN = 0;
            var exceptions = new List<string>();
            foreach (var r in rows)
            {
                if (r.OrigW <= 0f || r.OrigH <= 0f) continue;
                var tol = r.Tol > 0f ? r.Tol : 1e-2f;
                if (tol >= 1f)
                {
                    exceptions.Add(r.Panel + "/" + r.Control + "：" + r.Note);
                    continue;                        // tol ≥ 1 ⇒ 明确登记为"允许非等比"
                }
                aspectN++;
                var diff = Math.Abs(r.RectW * r.OrigH - r.RectH * r.OrigW);
                if (diff > tol * r.OrigW * r.OrigH)
                    aspectBad.Add($"{r.Panel}/{r.Control}: 矩形 {r.RectW:0.##}×{r.RectH:0.##} vs 素材 "
                                  + $"{r.OrigW:0}×{r.OrigH:0}（差 {100f * diff / (r.OrigW * r.OrigH):0.#}%）");
            }
            Program.Check($"{aspectN} 个元件：**矩形宽高比 == 素材原生宽高比**（防非等比拉伸；0 例外）",
                aspectBad.Count == 0, aspectBad.Count == 0 ? "0 例外" : string.Join("；", aspectBad.ToArray()));

            Program.Check("「允许非等比」的元件**每条都写了理由**（⛔ 不许静默放行）",
                exceptions.Count <= 3 && AllHaveReason(rows),
                exceptions.Count == 0 ? "0 条" : string.Join(" ¦ ", exceptions.ToArray()));
        }

        private static bool AllHaveReason(List<Row> rows)
        {
            foreach (var r in rows)
                if ((r.Tol >= 1f || (!r.Exact && r.SkipIhdr)) && string.IsNullOrEmpty(r.Note))
                    return false;
            return true;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ③ 双套素材：引用的必须是 DC6 直出那一套（索引 0 = 透明）
        // ═════════════════════════════════════════════════════════════════════
        private static void DualSetSide()
        {
            // B-1 源码侧：`UI/**` 里**不许再出现**副本帧名（它们是同画面 + 不透明黑底）
            var uiDir = Program.UiDir;
            var copyMarks = new[] { "minipanelbtn__00__", "menubutton__0__", "runbutton_run_", "runbutton_walk_" };
            var offenders = new List<string>();
            foreach (var f in Directory.GetFiles(uiDir, "*.cs"))
            {
                var src = File.ReadAllText(f, Encoding.UTF8);
                var body = StripNonCode(src);      // 去注释：注释里会"说明"这些名字，那是解释不是引用
                foreach (var m in copyMarks)
                    if (body.Contains(m)) offenders.Add(Path.GetFileName(f) + ":" + m);
            }
            Program.Check($"UI/**.cs **0 命中**「Diablerie 副本帧名」（{string.Join("/", copyMarks)}）"
                          + " —— 副本同画面但把原版透明像素写成不透明黑",
                offenders.Count == 0, offenders.Count == 0 ? "0 命中（去注释后）" : string.Join(", ", offenders.ToArray()));

            // B-2 磁盘侧：被引用的 24 帧，每帧都必须**有全透明像素**（= 索引 0 透明那一套）
            var frames = new List<string>();
            for (var i = 0; i < 16; i++) frames.Add(UiArt.MiniPanelBtnFrame(i));
            for (var i = 0; i < 4; i++) frames.Add(UiArt.ArrowFrame(i));
            frames.Add(UiArt.RunButtonRunFrame);
            frames.Add(UiArt.RunButtonWalkFrame);

            var noTransparent = new List<string>();
            var decoded = 0;
            foreach (var p in frames)
            {
                if (!TryDecodeRgba(p, out var w, out var h, out var rgba))
                {
                    noTransparent.Add(p + "（PNG 解码跳过：色彩类型不支持）");
                    continue;
                }
                decoded++;
                var transparent = 0;
                for (var i = 3; i < rgba.Length; i += 4) if (rgba[i] == 0) transparent++;
                if (transparent == 0) noTransparent.Add($"{p}（{w}×{h} 全不透明 ⇒ 又被换成副本了？）");
            }
            Program.Check($"{frames.Count} 个被引用的原版帧：**每帧都有透明像素**（= DC6 直出、索引 0 = 透明）",
                noTransparent.Count == 0,
                noTransparent.Count == 0 ? $"0 例外（逐像素解码 {decoded}/{frames.Count} 张）"
                    : string.Join(", ", noTransparent.ToArray()));

            // B-3 副本若还在磁盘上，登记其"不透明黑填充"的事实（非失败：删文件是"素材落位"的动作，
            //     不是断言；本宿主只读盘）。w4 已把这 20 个副本**从磁盘删掉**并把 4 个 `ResPaths.PanelArrow*`
            //     改指 DC6 导出那一套 ⇒ 正常情况下这里是 0 个；万一将来又被拷回来，这行会立刻点名。
            var copyDir = Path.Combine(Program.ResourceRoot, "Clover", "D2", "UI", "Panel");
            var stillCopy = new List<string>();
            for (var i = 0; i < 16; i++)
            {
                var f = Path.Combine(copyDir, "minipanelbtn__00__" + i.ToString("00") + ".png");
                if (File.Exists(f)) stillCopy.Add(Path.GetFileName(f));
            }
            for (var i = 0; i < 4; i++)
            {
                var f = Path.Combine(copyDir, "menubutton__0__" + i + ".png");
                if (File.Exists(f)) stillCopy.Add(Path.GetFileName(f));
            }
            Console.WriteLine($"      │ [登记·非失败] 磁盘上仍保留 {stillCopy.Count} 个 Diablerie 副本文件"
                + "（已无代码引用）：" + (stillCopy.Count == 0 ? "0 个（w4 已删除 20 个副本）"
                    : string.Join(",", stillCopy.ToArray())));
            Console.WriteLine("      │   实测事实（`python tools/probes/measure/scan_uigame.py --pairs`）："
                + "20 对全部「同画面；alpha 差 N 像素（DC6=透明 / 副本=不透明黑）」，"
                + "minipanelbtn 每帧 20 px、menubutton 每帧 38 px；"
                + "w4 处置 = **删除 20 个副本 + `Core/ResPaths.cs` 的 4 个 `PanelArrow*` 改指 `menubutton_{0..3}`**"
                + "（与 `UiArt.ArrowFrame` / `UiArt.MiniPanelBtnFrame` 同口径）。");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ④ 图标命中率（物品 code / 技能 official_id → 磁盘上的原版图标）
        // ═════════════════════════════════════════════════════════════════════
        private static void IconSide()
        {
            // 已登记的物品图标缺口（`client/资源欠缺清单.md`：刺客/德鲁伊/野蛮人/圣骑士/死灵的专属装备，
            // 原版图标本身不在本批素材里）—— 断言「缺口不得变多」，素材补齐后自动变绿。
            var knownMissingItemCodes = new HashSet<string>
            {
                "ktr", "wrb", "axf", "ob1", "ob2", "ob3", "dr1", "dr2", "ba1", "ba2", "pa1", "pa2", "ne1", "ne2",
            };

            var itemTable = Path.Combine(Program.ProjectRoot, "client", "Assets", "StreamingAssets", "Table", "Item.tsv");
            var skillTable = Path.Combine(Program.ProjectRoot, "client", "Assets", "StreamingAssets", "Table", "Skill.tsv");
            var classTable = Path.Combine(Program.ProjectRoot, "client", "Assets", "StreamingAssets", "Table", "Class.tsv");
            if (!File.Exists(itemTable) || !File.Exists(skillTable) || !File.Exists(classTable))
            {
                Program._skip++;
                Console.WriteLine("[SKIP] 配表 TSV 不在本机 ⇒ 图标命中率无法判（读不到 code 列）");
                return;
            }

            // 物品：路径由工程自己的 `D2Icon.IconFileCode` + `ResPaths.ItemIcon` 拼（**不复制别名表**）
            var newMissing = new List<string>();
            var hit = 0;
            foreach (var row in ReadTsv(itemTable))
            {
                if (!row.TryGetValue("code", out var code) || string.IsNullOrEmpty(code) || code == "-") continue;
                var path = ResPaths.ItemIcon(D2Icon.IconFileCode(code));
                if (PngFile(path, out _)) hit++;
                else if (!knownMissingItemCodes.Contains(code)) newMissing.Add(code + "(id=" + row["id"] + ")");
            }
            Program.Check($"物品图标：命中 {hit} 个，**新增缺口 0 个**（已登记的 14 个 code 仍缺，见资源欠缺清单）",
                newMissing.Count == 0,
                newMissing.Count == 0 ? $"命中 {hit}（登记缺口 {knownMissingItemCodes.Count} 个）"
                    : string.Join(", ", newMissing.ToArray()));

            // 技能：帧号公式用工程自己的常量（`D2Icon.FirstOfficialId/SkillsPerClass`）+ 职业字母取配表 skill_class 列
            var skillClass = new Dictionary<string, string>();
            foreach (var row in ReadTsv(classTable))
                if (row.TryGetValue("id", out var id) && row.TryGetValue("skill_class", out var sc))
                    skillClass[id] = sc;

            var skillMiss = new List<string>();
            var skillHit = 0;
            foreach (var row in ReadTsv(skillTable))
            {
                if (!row.TryGetValue("class", out var cls) || !row.TryGetValue("official_id", out var oid)) continue;
                if (!int.TryParse(cls, out var c) || !int.TryParse(oid, out var o)) continue;
                if (!skillClass.TryGetValue(cls, out var sc) || string.IsNullOrEmpty(sc)) continue;
                var frame = (o - (D2Icon.FirstOfficialId + D2Icon.SkillsPerClass * (c - 1))) * 2;
                if (frame < 0) { skillMiss.Add($"id={row["id"]}(idx<0)"); continue; }
                if (PngFile(ResPaths.SkillIcon(sc, frame), out _)) skillHit++;
                else skillMiss.Add($"id={row["id"]}/{sc}@{frame}");
            }
            Program.Check($"技能图标：{skillHit} 个技能**全部命中**磁盘素材（帧号 = (official_id − 起点)×2）",
                skillMiss.Count == 0,
                skillMiss.Count == 0 ? "150/150" : string.Join(", ", skillMiss.ToArray()));
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑤ 字模侧：画面中文字面量 → 原版 chi 字模
        // ═════════════════════════════════════════════════════════════════════
        private static void FontSide()
        {
            var m = Program.LoadChiMetrics();
            if (m.Advance.Count == 0)
            {
                Program._skip++;
                Console.WriteLine("[SKIP] `font16_chi_map.txt` 不在本机 ⇒ 中文字形覆盖无法判");
                return;
            }

            var lit = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Singleline);
            var offenders = new List<string>();
            var files = 0;
            var chars = new HashSet<int>();
            foreach (var f in Directory.GetFiles(Program.UiDir, "*.cs"))
            {
                files++;
                var src = File.ReadAllText(f, Encoding.UTF8);
                foreach (Match mm in lit.Matches(src))
                {
                    var s = mm.Groups[1].Value;
                    foreach (var ch in s)
                    {
                        if (ch < 0x2E80) continue;           // 只判 CJK 区（含全角标点/假名）
                        chars.Add(ch);
                        if (Renderable(m, ch)) continue;
                        var where = Path.GetFileName(f) + "「" + ch + "」(U+" + ((int)ch).ToString("X4") + ")";
                        if (!offenders.Contains(where)) offenders.Add(where);
                    }
                }
            }

            Program.Check($"UI/**（{files} 个 .cs）字符串字面量里的 {chars.Count} 个 CJK 字**全部能落到原版字模**"
                          + "（字模表 或 简繁映射；落不到 = 画面上静默缺字）",
                offenders.Count == 0,
                offenders.Count == 0 ? "0 缺字形" : string.Join(", ", offenders.ToArray()));
        }

        /// <summary>该 CJK 字能不能画出来（口径与 `UI/D2Text.HasGlyph` 一致：直查 → 简繁回退）。</summary>
        private static bool Renderable(Program.ChiMetrics m, char c)
        {
            if (m.Advance.ContainsKey(c)) return true;
            return m.S2T.TryGetValue(c, out var alt) && m.Advance.ContainsKey(alt);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑨ ★ w5：选项 / 暂停底板 = **原版 `MENU/boxpieces.DC6` 拼装窗框**
        //   （判据：偏移**自证** = 声明的偏移 == 逐像素量出来的边带位置；接缝**自证** = 解整幅像素）
        // ═════════════════════════════════════════════════════════════════════
        //  为什么必须重新解像素：那 22 帧的 **DC6 帧 offset 表本机拿不到**
        //  （`原版资源/` 被 .gitignore 排除），拼装偏移是**从 PNG 反推**的
        //  （推导见 `tools/d2codec/assemble_boxpieces.py` 文件头）。
        //  ⇒ 常量与素材之间没有权威表可对，只能**把同一件事独立算第二遍**：
        //     ① 声明的各族偏移必须等于「量出来的边带位置」之差（改错一个数必红）；
        //     ② 拼出来的整幅必须**接缝连续 + 外沿是矩形 + 空腔全透明 + 四角 == 角块素材内容区**
        //        （这一条同时钉死"素材真的被用上、没被错位/拉伸"）。
        private static void BoxFrameSide()
        {
            const int pitch = UiLayoutFlow.BoxFrame.TilePitch;

            // ① 22 帧素材在位，且 IHDR == 14×15（逐帧）
            var missFrame = new List<string>();
            var badIhdr = new List<string>();
            var frames = 0;
            for (var i = 0; i < 22; i++)
            {
                var res = ResPaths.D2UiMenu + "boxpieces_" + i;
                if (!TryPngSize(res, out var w, out var h)) { missFrame.Add("boxpieces_" + i); continue; }
                frames++;
                if (w != UiLayoutFlow.BoxFrame.FramePixelW || h != UiLayoutFlow.BoxFrame.FramePixelH)
                    badIhdr.Add($"boxpieces_{i}: {w}×{h}");
            }
            Program.Check($"原版 `MENU/boxpieces.DC6` 的 22 帧素材都在磁盘上且 IHDR == "
                          + $"{UiLayoutFlow.BoxFrame.FramePixelW}×{UiLayoutFlow.BoxFrame.FramePixelH}",
                missFrame.Count == 0 && badIhdr.Count == 0 && frames == 22,
                missFrame.Count == 0 && badIhdr.Count == 0 ? "22/22" :
                    ("缺=" + string.Join(",", missFrame.ToArray()) + " 尺寸异=" + string.Join(",", badIhdr.ToArray())));

            // ② 偏移自证：逐像素量「边带压在帧内哪几列/行」，与声明的偏移核对
            var bandBad = new List<string>();
            var longCols = new Dictionary<int, int[]>();   // 帧号 → 通高不透明的列
            var longRows = new Dictionary<int, int[]>();
            for (var i = 0; i < 22; i++)
            {
                if (!TryDecodeRgba(ResPaths.D2UiMenu + "boxpieces_" + i, out var w, out var h, out var rgba))
                { bandBad.Add($"boxpieces_{i} 解码失败（色彩类型不支持）"); continue; }
                longCols[i] = FullyOpaqueLines(rgba, w, h, columns: true);
                longRows[i] = FullyOpaqueLines(rgba, w, h, columns: false);
            }

            void Expect(string what, int frame, int[] got, int[] want)
            {
                var same = got != null && got.Length == want.Length;
                if (same)
                    for (var i = 0; i < want.Length; i++)
                        if (got[i] != want[i]) same = false;
                if (same) return;
                bandBad.Add($"{what} boxpieces_{frame}: 量到 [{Show(got)}] vs 声明 [{Show(want)}]");
            }

            Expect("左上角块·竖边带列", UiLayoutFlow.BoxFrame.TileTopLeft, longCols[0], new[] { 1, 2, 3 });
            Expect("左上角块·横边带行", UiLayoutFlow.BoxFrame.TileTopLeft, longRows[0], new[] { 1, 2, 3 });
            Expect("右上角块·竖边带列", UiLayoutFlow.BoxFrame.TileTopRight, longCols[1], new[] { 10, 11, 12 });
            Expect("右上角块·横边带行", UiLayoutFlow.BoxFrame.TileTopRight, longRows[1], new[] { 1, 2, 3 });
            Expect("左下角块·竖边带列", UiLayoutFlow.BoxFrame.TileBottomLeft, longCols[8], new[] { 1, 2, 3 });
            Expect("左下角块·横边带行", UiLayoutFlow.BoxFrame.TileBottomLeft, longRows[8], new[] { 10, 11, 12 });
            Expect("右下角块·竖边带列", UiLayoutFlow.BoxFrame.TileBottomRight, longCols[9], new[] { 10, 11, 12 });
            Expect("右下角块·横边带行", UiLayoutFlow.BoxFrame.TileBottomRight, longRows[9], new[] { 10, 11, 12 });

            foreach (var i in new[] { 2, 3, 4, 5, 6, 7, 16, 17, 18, 19, 20, 21 })
                Expect("上/下边块·横边带行", i, longRows[i], new[] { 1, 2, 3 });
            foreach (var i in new[] { 10, 11, 12, 13, 14, 15 })
                Expect("左/右边块·竖边带列", i, longCols[i], new[] { 5, 6, 7 });

            // 声明的偏移 == 「角块的外沿位置 − 该族自己的边带起点」（同一条像素事实推出来的）
            var offTop = UiLayoutFlow.BoxFrame.OffsetTop;
            var offBottom = UiLayoutFlow.BoxFrame.OffsetBottom;
            var offLeft = UiLayoutFlow.BoxFrame.OffsetLeft;
            var offRight = UiLayoutFlow.BoxFrame.OffsetRight;
            // ⚠️ 逐族看**自己那一轴**：上/下边块管 y（dx 恒 0），左/右边块管 x（dy 恒 0）。
            var derived = new[]
            {
                ("上边", offTop.y,      1 - First(longRows, 2),   0,  "dy"),
                ("下边", offBottom.y,  10 - First(longRows, 16),  9,  "dy"),
                ("左边", offLeft.x,     1 - First(longCols, 10), -4,  "dx"),
                ("右边", offRight.x,   10 - First(longCols, 13),  5,  "dx"),
            };
            foreach (var (name, declared, got, want, axis) in derived)
                if (declared != got || declared != want)
                    bandBad.Add($"{name}块声明的 {axis}={declared} 与像素实测差 {got}（应为 {want}）不一致");

            Program.Check("拼装偏移**自证**：声明的各族偏移 == 逐像素量出来的「角块外沿 − 边带起点」"
                          + "（角块 4 个在帧内 1..3 / 10..12、竖边块在 5..7 ⇒ 左 −4 / 右 +5 / 下 +9）",
                bandBad.Count == 0, bandBad.Count == 0
                    ? "4 族全部相符（左 −4 / 右 +5 / 下 +9 / 上 0）" : string.Join("；", bandBad.ToArray()));

            // ③ 边带朝向（who is "left" / "inner"）：近黑内线在边带的哪一侧
            var sideBad = new List<string>();
            CheckInnerSide(sideBad, longCols, new[] { 10, 11, 12 }, rightBorder: false, label: "左边块");
            CheckInnerSide(sideBad, longCols, new[] { 13, 14, 15 }, rightBorder: true, label: "右边块");
            CheckInnerSideRow(sideBad, longRows, new[] { 2, 3, 4, 5, 6, 7 }, bottom: false, label: "上边块");
            CheckInnerSideRow(sideBad, longRows, new[] { 16, 17, 18, 19, 20, 21 }, bottom: true, label: "下边块");
            Program.Check("边带朝向自证：上边块的内线在边带**下沿**、下边块在上沿、"
                          + "左边块在**右边**、右边块在左边（即 4 族各就各位，没有把左右/上下摆反）",
                sideBad.Count == 0, sideBad.Count == 0 ? "0 例外" : string.Join("；", sideBad.ToArray()));

            // ④ 整幅窗框：在位、IHDR == 面板常量 ÷1.8、且是拼装网格的整数倍
            var sizeBad = new List<string>();
            var boxSpecs = new[]
            {
                ("选项面板", ResPaths.PanelBoxFrameSettings, UiLayoutFlow.Settings.BoxSize),
                ("暂停菜单", ResPaths.PanelBoxFramePause, UiLayoutFlow.Pause.BoxSize),
            };
            foreach (var (name, path, size) in boxSpecs)
            {
                if (!TryPngSize(path, out var w, out var h)) { sizeBad.Add(name + " 缺 " + path); continue; }
                var ow = UiLayoutFlow.Orig(size.x);
                var oh = UiLayoutFlow.Orig(size.y);
                if (Math.Abs(w - ow) > 0.01f || Math.Abs(h - oh) > 0.01f)
                    sizeBad.Add($"{name}: 整幅 {w}×{h} vs 面板常量 ÷1.8 = {ow:0.##}×{oh:0.##}");
                if (w % pitch != 0 || h % pitch != 0)
                    sizeBad.Add($"{name}: 整幅 {w}×{h} 不是拼装网格 {pitch} 的整数倍");
                if (w / pitch < 3 || h / pitch < 3)
                    sizeBad.Add($"{name}: 整幅只有 {w / pitch}×{h / pitch} 格（四角 + 至少一条边块）");
            }
            Program.Check("两个底板的整幅窗框 PNG 在位，且 IHDR == 面板常量 ÷1.8（**与素材 1:1，不拉伸**）、"
                          + "且是拼装网格 12 的整数倍", sizeBad.Count == 0,
                sizeBad.Count == 0 ? "选项 432×348 / 暂停 288×180" : string.Join("；", sizeBad.ToArray()));

            // ⑤ 接缝自证：解整幅像素 → 接缝连续 + 四条边带无洞 + 空腔全透明 + 四角 == 角块素材内容区
            var seamBad = new List<string>();
            foreach (var (name, path, size) in boxSpecs)
            {
                if (!TryDecodeRgba(path, out var W, out var H, out var px))
                { seamBad.Add(name + " 解码失败"); continue; }
                var cols = W / pitch;
                var rows = H / pitch;
                if (cols * pitch != W || rows * pitch != H) { seamBad.Add(name + " 尺寸非网格"); continue; }

                for (var k = 1; k < cols; k++)
                {
                    var n = 0;
                    for (var y = 0; y < H; y++)
                        if ((px[(y * W + k * pitch - 1) * 4 + 3] > 0) != (px[(y * W + k * pitch) * 4 + 3] > 0)) n++;
                    if (n > 0) seamBad.Add($"{name} 竖缝 x={k * pitch} 有 {n} 个不一致像素");
                }
                for (var j = 1; j < rows; j++)
                {
                    var n = 0;
                    for (var x = 0; x < W; x++)
                        if ((px[((j * pitch - 1) * W + x) * 4 + 3] > 0) != (px[(j * pitch * W + x) * 4 + 3] > 0)) n++;
                    if (n > 0) seamBad.Add($"{name} 横缝 y={j * pitch} 有 {n} 个不一致像素");
                }

                var holes = 0;
                for (var x = 0; x < W; x++)
                    for (var y = 0; y < 3; y++) holes += px[(y * W + x) * 4 + 3] == 0 ? 1 : 0;
                for (var x = 0; x < W; x++)
                    for (var y = H - 3; y < H; y++) holes += px[(y * W + x) * 4 + 3] == 0 ? 1 : 0;
                for (var y = 0; y < H; y++)
                    for (var x = 0; x < 3; x++) holes += px[(y * W + x) * 4 + 3] == 0 ? 1 : 0;
                for (var y = 0; y < H; y++)
                    for (var x = W - 3; x < W; x++) holes += px[(y * W + x) * 4 + 3] == 0 ? 1 : 0;
                if (holes > 0) seamBad.Add($"{name} 四条边带有 {holes} 个透明空洞（窗框断了）");

                var t = UiLayoutFlow.BoxFrame.BorderThickness;
                var dirty = 0;
                for (var y = t; y < H - t; y++)
                    for (var x = t; x < W - t; x++) dirty += px[(y * W + x) * 4 + 3] > 0 ? 1 : 0;
                if (dirty > 0) seamBad.Add($"{name} 空腔里有 {dirty} 个非透明像素");

                foreach (var (label, frame, ox, oy) in new[]
                {
                    ("左上", UiLayoutFlow.BoxFrame.TileTopLeft, 0, 0),
                    ("右上", UiLayoutFlow.BoxFrame.TileTopRight, W - pitch, 0),
                    ("左下", UiLayoutFlow.BoxFrame.TileBottomLeft, 0, H - pitch),
                    ("右下", UiLayoutFlow.BoxFrame.TileBottomRight, W - pitch, H - pitch),
                })
                {
                    if (!TryDecodeRgba(ResPaths.D2UiMenu + "boxpieces_" + frame, out var fw, out var fh, out var fr))
                    { seamBad.Add($"{name} {label} 角块素材解码失败"); continue; }
                    var same = true;
                    for (var y = 0; y < pitch && same; y++)
                        for (var x = 0; x < pitch && same; x++)
                            for (var c = 0; c < 4; c++)
                                if (px[((oy + y) * W + ox + x) * 4 + c] != fr[((1 + y) * fw + 1 + x) * 4 + c]) same = false;
                    if (!same) seamBad.Add($"{name} {label}角 12×12 与 boxpieces_{frame} 的内容区不一致");
                }
            }
            Program.Check("整幅窗框**接缝自证**（重新解像素）：内部接缝 0 不一致 / 四条边带 0 空洞 / "
                          + "空腔全透明 / 四角 12×12 == 角块素材内容区 —— 即「逐像素接缝连续」",
                seamBad.Count == 0, seamBad.Count == 0
                    ? "选项 432×348 + 暂停 288×180 两张全部通过" : string.Join("；", seamBad.ToArray()));

            // ⑥ 面板真的在用它（否则素材又变成"躺在磁盘上的零引用文件"）
            var settingsSrc = StripNonCode(SafeRead(Path.Combine(Program.UiDir, "SettingsPanel.cs")));
            var pauseSrc = StripNonCode(SafeRead(Path.Combine(Program.UiDir, "PausePanel.cs")));
            Program.Check("两个面板的**源码**真的在加载窗框（`ResPaths.PanelBoxFrame*` + 在底色之后建）"
                          + " ⇒ 底板不再是「纯色块占位」",
                settingsSrc.Contains("ResPaths.PanelBoxFrameSettings")
                && settingsSrc.Contains("Settings.BoxSize")
                && pauseSrc.Contains("ResPaths.PanelBoxFramePause")
                && pauseSrc.Contains("Pause.BoxSize")
                && settingsSrc.IndexOf("BoxFrame", StringComparison.Ordinal)
                   > settingsSrc.IndexOf("Box\"", StringComparison.Ordinal)
                && pauseSrc.IndexOf("BoxFrame", StringComparison.Ordinal)
                   > pauseSrc.IndexOf("Box\"", StringComparison.Ordinal),
                "Settings/Pause 各一处 UiArt.Art(..., ResPaths.PanelBoxFrame*)；层序 = 底色 → 窗框 → 内容");
        }

        /// <summary>
        /// 一帧里"**在内容区内**通长不透明"的列（<paramref name="columns"/> = true）或行。
        /// <para>⚠️ 只看**内容区**（x/y ∈ 1..12）：每帧 14×15 的外圈页边（x0/x13/y0/y13/y14）本来就透明，
        /// 拿"整帧"判通长会一个都量不出来（那正是本方法第一版的错）。</para>
        /// </summary>
        private static int[] FullyOpaqueLines(byte[] rgba, int w, int h, bool columns)
        {
            var list = new List<int>();
            // 内容区 = 1..12（宽 14 → 1..12、高 15 → 1..12），见 UiLayoutFlow.BoxFrame 的框厚注释。
            const int lo = 1;
            var maxIdx = columns ? w - 2 : h - 3;      // 候选列/行号的上界（含）
            var spanMax = columns ? h - 3 : w - 2;     // 判定"通长"时另一轴的扫描上界（含）
            var n = columns ? w : h;
            for (var i = lo; i <= maxIdx && i < n; i++)
            {
                var ok = true;
                for (var j = lo; j <= spanMax; j++)
                {
                    var idx = columns ? (j * w + i) * 4 + 3 : (i * w + j) * 4 + 3;
                    if (rgba[idx] == 0) { ok = false; break; }
                }
                if (ok) list.Add(i);
            }
            return list.ToArray();
        }

        /// <summary>该族第一个帧的通高不透明列/通宽不透明行的起点（量不出来 ⇒ −999）。</summary>
        private static int First(Dictionary<int, int[]> map, int frame)
            => map.TryGetValue(frame, out var a) && a != null && a.Length > 0 ? a[0] : -999;

        /// <summary>把量到的列/行号列表打成 `1,2,3`（报告用）。</summary>
        private static string Show(int[] a)
        {
            if (a == null || a.Length == 0) return "（量不到）";
            var sb = new StringBuilder();
            for (var i = 0; i < a.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(a[i]);
            }
            return sb.ToString();
        }

        /// <summary>竖边块的近黑内线在边带的哪一侧（左块应在右边、右块应在左边）。</summary>
        private static void CheckInnerSide(List<string> bad, Dictionary<int, int[]> longCols,
            int[] frames, bool rightBorder, string label)
        {
            foreach (var f in frames)
            {
                if (!TryDecodeRgba(ResPaths.D2UiMenu + "boxpieces_" + f, out var w, out var h, out var rgba)) continue;
                if (!longCols.TryGetValue(f, out var cols) || cols.Length == 0) { bad.Add(label + f + " 无边带"); continue; }
                var darkest = -1;
                var best = int.MaxValue;
                foreach (var x in cols)
                {
                    var v = 0;
                    for (var y = 1; y <= 12; y++)
                        v += rgba[(y * w + x) * 4] + rgba[(y * w + x) * 4 + 1] + rgba[(y * w + x) * 4 + 2];
                    if (v < best) { best = v; darkest = x; }
                }
                var want = rightBorder ? cols[0] : cols[cols.Length - 1];
                if (darkest != want) bad.Add($"{label} boxpieces_{f}: 内线在 x={darkest}，应为 x={want}");
            }
        }

        /// <summary>横边块的近黑内线在边带的哪一侧（上块应在下沿、下块应在上沿）。</summary>
        private static void CheckInnerSideRow(List<string> bad, Dictionary<int, int[]> longRows,
            int[] frames, bool bottom, string label)
        {
            foreach (var f in frames)
            {
                if (!TryDecodeRgba(ResPaths.D2UiMenu + "boxpieces_" + f, out var w, out var h, out var rgba)) continue;
                if (!longRows.TryGetValue(f, out var rows) || rows.Length == 0) { bad.Add(label + f + " 无边带"); continue; }
                var darkest = -1;
                var best = int.MaxValue;
                foreach (var y in rows)
                {
                    var v = 0;
                    for (var x = 1; x <= 12; x++)
                        v += rgba[(y * w + x) * 4] + rgba[(y * w + x) * 4 + 1] + rgba[(y * w + x) * 4 + 2];
                    if (v < best) { best = v; darkest = y; }
                }
                var want = bottom ? rows[0] : rows[rows.Length - 1];
                if (darkest != want) bad.Add($"{label} boxpieces_{f}: 内线在 y={darkest}，应为 y={want}");
            }
        }

        private static string SafeRead(string path)
            => File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;

        // ═════════════════════════════════════════════════════════════════════
        // ⑩ ★ w5：光标 —— 「零消费方」消掉（判据 = 消费方存在 + 真的改光标 + 素材被引用 + 缺口已登记）
        // ═════════════════════════════════════════════════════════════════════
        //  为什么以前只做"登记·非失败"：那时确实**没有消费方**、且 4 态素材缺失，
        //  做成 FAIL 会让宿主永远到不了 `FAILED=0`（而"红"表达的是一件待补素材的事）。
        //  ★ 本轮把机制接上了 ⇒ 这些判据**现在必须是绿的**，故升级为真断言：
        //    ① `UI/CursorView.cs` 是 `Events.CursorChanged` 的**真消费方**（订阅 + 有处理方法）；
        //    ② 它**真的改鼠标光标**（`Cursor.visible` 的隐藏/恢复 + 跟随鼠标的 Image）；
        //    ③ `ResPaths.Cursor` 被真的引用（不再是零引用常量）；
        //    ④ 素材在位（IHDR 32×26）；
        //    ⑤ 4 态缺口**逐态有 Warn + 有登记**（⛔ 仍不许自画 4 个光标）。
        private static void CursorSide()
        {
            var code = new StringBuilder();
            foreach (var f in Directory.GetFiles(Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts"),
                         "*.cs", SearchOption.AllDirectories))
                code.Append(File.ReadAllText(f, Encoding.UTF8));
            var body = StripNonCode(code.ToString());

            var refs = CountOccurrences(body, "ResPaths.Cursor");
            var consumers = CountOccurrences(body, "Events.CursorChanged");

            var cursorSrc = StripNonCode(SafeRead(Path.Combine(Program.UiDir, "CursorView.cs")));

            Program.Check("光标：`ResPaths.Cursor` 被真的引用（不再是零引用常量）—— 引用点必须在 UI/** 里",
                refs >= 1 && cursorSrc.Contains("ResPaths.Cursor"),
                $"Scripts/**（去注释）出现 {refs} 次；`UI/CursorView.cs` 里 "
                + CountOccurrences(cursorSrc, "ResPaths.Cursor") + " 次");

            Program.Check("光标：`Events.CursorChanged` **有真消费方**（订阅 + 具名处理方法），且消费方是 UI 层",
                consumers >= 2
                && cursorSrc.Contains("Game.Event.On<CursorKind>(Events.CursorChanged")
                && cursorSrc.Contains("private void OnCursorChanged(CursorKind kind)"),
                $"Scripts/**（去注释）出现 {consumers} 次（发送方 InputReader + 消费方 CursorView 的订阅/注销/处理方法）");

            Program.Check("光标：消费方**真的改鼠标光标** —— 藏/恢复系统光标（`Cursor.visible`）"
                          + "+ 跟随鼠标的 Image（`Game.Input.MousePosition` → 画布局部坐标）",
                cursorSrc.Contains("Cursor.visible")
                && cursorSrc.Contains("Game.Input.MousePosition")
                && cursorSrc.Contains("ScreenPointToLocalPointInRectangle")
                && cursorSrc.Contains("_image.enabled")
                && cursorSrc.Contains("raycastTarget = false"),
                "见 UI/CursorView.cs（承载方式 = 独立 Top 画布 + Image：像素素材 isReadable=0 ⇒ Cursor.SetCursor 用不了）");

            Program.Check("光标：**菜单里不接管** —— 只在 StageEntered→StageLeft 之间显示，且贴图没到位时"
                          + "不隐藏系统光标（否则会出现\u201c看不见指针\u201d这种更糟的状态）",
                cursorSrc.Contains("Events.StageEntered, OnStageEntered")
                && cursorSrc.Contains("Events.StageLeft, OnStageLeft")
                && cursorSrc.Contains("_stageActive && _spriteReady")
                && cursorSrc.Contains("Cursor.visible = !hidden"),
                "见 UI/CursorView.cs 的 ApplyVisibility / SetSystemCursorHidden");

            TryPngSize(ResPaths.Cursor, out var cw, out var ch);
            Program.Check($"光标素材在位且 IHDR == 声明的原版尺寸 {UiLayoutGame.CursorArtPx.x:0}×{UiLayoutGame.CursorArtPx.y:0}",
                cw == (int)UiLayoutGame.CursorArtPx.x && ch == (int)UiLayoutGame.CursorArtPx.y,
                $"{ResPaths.Cursor} = {cw}×{ch}；画布尺寸 {UiLayoutGame.CursorSize.x:0.#}×{UiLayoutGame.CursorSize.y:0.#}（×{UiLayoutGame.K}）"
                + $"，hot spot = 箭头尖（pivot {UiLayoutGame.CursorHotspotPivot}）");

            // 4 态缺口：仍**不自画**（skill §0「A 没有就不加」）—— 但必须有 Warn + 有登记
            var lack = SafeRead(Path.Combine(Program.ProjectRoot, "client", "资源欠缺清单.md"));
            Console.WriteLine("      │ [登记·素材缺口] 原版 5 态光标（" + UiLayoutGame.CursorKindCount
                + " 种：普通/攻击/交互/拾取/不可走）本批**只有普通箭头 1 帧** ⇒ 5 态统一显示这一帧箭头，"
                + "切到缺口形态时**逐态一条 Warn**（`UI/CursorView.cs::OnCursorChanged`）。");
            Program.Check("光标 4 态缺口**已登记**在 `client/资源欠缺清单.md`（⛔ 不自画 4 个光标）",
                lack.Contains("Cursor") && lack.Contains("4 态") && cursorSrc.Contains("UiLog.Warn"),
                "见 `client/资源欠缺清单.md` 的「光标 5 态」行 + `UI/CursorView.cs::OnCursorChanged` 的逐态 Warn");
        }

        // ── 工具 ─────────────────────────────────────────────────────────────

        // ⚠️ w4 删除：`OrigSizeOfEquip(stem)`（按文件名的硬编码尺寸表）——
        //   它把"贴图整幅 IHDR"当成"本槽图形尺寸"用（双槽拼图尤其错），且是同一批数字的第二份副本
        //   （必然漂移）。现由 `UiLayoutGame.InvEquipArt` 的**逐像素实测**唯一提供，并由
        //   `EquipArtSide()` 再解一次像素核对。

        // ═════════════════════════════════════════════════════════════════════
        // ⑦ ★ w4：装备槽的**素材侧**核对（逐像素）——「声明 == 实测」不是抄过来的，是解出来的
        // ═════════════════════════════════════════════════════════════════════
        //  判什么：把每张 `D2/UI/EquipSlot/*.png` **真的解成像素**，按列投影切成连通块
        //          （相邻空列 = 两槽的分界），再逐槽核对：
        //            ① 块数 == 该贴图被几个槽分成几块（单槽图 1 块 / 双槽图 2 块）；
        //            ② 该槽声明的外接框（由 `sheetOffset` 反推回图内坐标）== 实测块的外接框；
        //            ③ 整幅贴图的绘制尺寸 == IHDR ×1.8（**1:1**：横竖缩放比都是 1.8 ⇒ 不拉伸）。
        private static void EquipArtSide()
        {
            var paths = new List<string>();
            foreach (var s in InventoryPanel.EquipSlots)
                if (!paths.Contains(s.sprite)) paths.Add(s.sprite);

            var bad = new List<string>();
            var sheets = 0;
            var slots = 0;
            foreach (var path in paths)
            {
                if (!TryDecodeRgba(path, out var iw, out var ih, out var rgba))
                {
                    bad.Add(path + "（PNG 解码跳过：色彩类型不支持）");
                    continue;
                }
                sheets++;

                var groups = ColumnGroups(rgba, iw, ih);
                var mine = new List<InventoryPanel.EquipSlotDef>();
                foreach (var s in InventoryPanel.EquipSlots) if (s.sprite == path) mine.Add(s);

                var need = 1;
                foreach (var s in mine) if (s.half == 2) need = 2;
                if (groups.Count != need)
                {
                    bad.Add($"{path}: 实测连通块 {groups.Count} 块 vs 期望 {need} 块（IHDR {iw}×{ih}）");
                    continue;
                }

                foreach (var s in mine)
                {
                    slots++;
                    var gi = s.half == 2 ? 1 : 0;      // half==2 ⇒ 右块；其余（0/1）⇒ 左块（单块图即第 0 块）
                    if (gi >= groups.Count) gi = 0;
                    var g = groups[gi];

                    // 由工程值**反推**声明的外接框（图内坐标）：
                    //   sheetOffset.x = (sw/2 − cx)·K、sheetOffset.y = −(sh/2 − cy)·K
                    var sw = s.sheetSize.x / K;
                    var sh = s.sheetSize.y / K;
                    var cx = sw * 0.5f - s.sheetOffset.x / K;
                    var cy = sh * 0.5f + s.sheetOffset.y / K;
                    var x0 = cx - s.artOrig.x * 0.5f;
                    var y0 = cy - s.artOrig.y * 0.5f;

                    var stem = path.Substring(path.LastIndexOf('/') + 1);
                    if (Math.Abs(s.artOrig.x - (g.x1 - g.x0)) > 0.5f || Math.Abs(s.artOrig.y - (g.y1 - g.y0)) > 0.5f)
                        bad.Add($"{stem}: 声明内容框 {s.artOrig.x:0}×{s.artOrig.y:0} vs 实测 "
                                + $"{g.x1 - g.x0}×{g.y1 - g.y0}（块 {g.x0}..{g.x1} × {g.y0}..{g.y1}）");
                    else if (Math.Abs(x0 - g.x0) > 0.5f || Math.Abs(y0 - g.y0) > 0.5f)
                        bad.Add($"{stem}: 声明内容框原点 ({x0:0.#},{y0:0.#}) vs 实测 ({g.x0},{g.y0})");
                    else if (Math.Abs(s.sheetSize.x - iw * K) > 0.01f || Math.Abs(s.sheetSize.y - ih * K) > 0.01f)
                        bad.Add($"{stem}: 整幅绘制 {s.sheetSize.x:0.##}×{s.sheetSize.y:0.##} vs IHDR×1.8 "
                                + $"{iw * K:0.##}×{ih * K:0.##}（不是 1:1 就是被缩放了）");
                }
            }

            Program.Check($"{slots} 个装备槽：声明的「本槽图形外接框 == 素材逐像素实测」且整幅贴图 **1:1**（IHDR ×1.8）",
                bad.Count == 0, bad.Count == 0 ? $"0 例外（解码 {sheets} 张贴图 / {slots} 个槽逐槽核对）"
                    : string.Join("；", bad.ToArray()));

            // 汇总口径（人读）：每张贴图实测几块、每块多大
            var desc = new List<string>();
            foreach (var path in paths)
            {
                if (!TryDecodeRgba(path, out var iw, out var ih, out var rgba)) continue;
                var gs = ColumnGroups(rgba, iw, ih);
                var parts = new List<string>();
                foreach (var g in gs) parts.Add($"[{g.x0}..{g.x1}×{g.y0}..{g.y1}]");
                desc.Add(Path.GetFileName(path) + " " + iw + "×" + ih + " → " + parts.Count + " 块 " + string.Join("", parts.ToArray()));
            }
            Console.WriteLine("      │ 装备槽素材实测（`EquipSlot/*.png` 列投影连通块）：" + string.Join("；", desc.ToArray()));
        }

        /// <summary>按列投影把一张贴图切成连通块（相邻空列 = 分界），返回每块的 (x0,y0,x1,y1)（右下开区间）。</summary>
        private static List<(int x0, int y0, int x1, int y1)> ColumnGroups(byte[] rgba, int w, int h)
        {
            var list = new List<(int, int, int, int)>();
            var empty = new bool[w];
            for (var x = 0; x < w; x++)
            {
                var any = false;
                for (var y = 0; y < h; y++)
                    if (rgba[(y * w + x) * 4 + 3] != 0) { any = true; break; }
                empty[x] = !any;
            }

            var start = -1;
            for (var x = 0; x <= w; x++)
            {
                if (x < w && !empty[x])
                {
                    if (start < 0) start = x;
                    continue;
                }
                if (start < 0) continue;

                var minY = h;
                var maxY = -1;
                for (var xx = start; xx < x; xx++)
                    for (var y = 0; y < h; y++)
                        if (rgba[(y * w + xx) * 4 + 3] != 0)
                        {
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                        }
                list.Add((start, minY, x, maxY + 1));
                start = -1;
            }
            return list;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑧ ★ w4：技能树——「**拼装后整页矩形**」与「**素材帧尺寸**」两个概念分开断言
        // ═════════════════════════════════════════════════════════════════════
        //  背景（w3 的那条红就是这两个概念被混在一行里）：原版 `SPELLS/skltree_{cls}_back.DC6`
        //    · **素材帧尺寸**：16 帧 / 职业，尺寸循环 256×256 / 64×256 / 256×176 / 64×176
        //      （= 4 帧一页的 tile 打包）→ 逐帧文件在 `D2/UI/SkillTree/`（`ResPaths.SkillTreeBack`）；
        //    · **拼装后整页矩形**：320×432 原版px（256+64 宽、256+176 高）→ 拼好的整页在
        //      `D2/UI/Panel/skltree_{cls}_back_{0..3}.png`（= 面板真正用来做底图的那张，
        //      运行时路径由 `UI/D2Icon.SkillTreeBackPath` 拼）。
        private static void SkillTreeSide()
        {
            // ① 路径口径：本节的 `SkillTreePagePath` 必须与**运行时**的拼法一致（只差目录）
            var mine = SkillTreePagePath("a", 0);
            var runtime = ResPaths.SkillTreeBack("a", 0).Replace(ResPaths.D2UiSkillTree, ResPaths.D2UiPanel);
            Program.Check("技能树底图路径 == 运行时 `D2Icon.SkillTreeBackPath` 的拼法（同前缀/同后缀，只差目录）",
                mine == runtime, mine + " vs " + runtime);

            // ② 面板源码：底图必须走**拼装后整页**，⛔ 不能走逐帧目录
            var srcPath = Path.Combine(Program.UiDir, "SkillTreePanel.cs");
            var src = File.Exists(srcPath) ? StripNonCode(File.ReadAllText(srcPath, Encoding.UTF8)) : string.Empty;
            Program.Check("技能树面板源码：底图走 `D2Icon.SkillTreeBackPath`（拼装后整页），且 **0 命中** `ResPaths.SkillTreeBack`（逐帧目录）",
                src.Contains("SkillTreeBackPath") && !src.Contains("SkillTreeBack("),
                "SkillTreeBackPath=" + CountOccurrences(src, "SkillTreeBackPath") + " 次 / ResPaths.SkillTreeBack="
                + CountOccurrences(src, "ResPaths.SkillTreeBack") + " 次（去注释）");

            // ③ 素材帧尺寸：每职业 16 帧、尺寸循环 == 4 帧一页
            var cycle = new[] { new Vector2(256f, 256f), new Vector2(64f, 256f), new Vector2(256f, 176f), new Vector2(64f, 176f) };
            var frameBad = new List<string>();
            var frameN = 0;
            foreach (var letter in new[] { "a", "b", "n", "p", "s" })
                for (var i = 0; i < ResPaths.FrameCountSkillTreeBack; i++)
                {
                    if (!TryPngSize(ResPaths.SkillTreeBack(letter, i), out var w, out var h)) { frameBad.Add(letter + "#" + i + " 缺"); continue; }
                    frameN++;
                    var exp = cycle[i % cycle.Length];
                    if (w != exp.x || h != exp.y) frameBad.Add($"{letter}#{i}: {w}×{h} != {exp.x:0}×{exp.y:0}");
                }
            Program.Check($"{frameN} 个技能树**逐帧**文件：帧尺寸循环 == 256×256 / 64×256 / 256×176 / 64×176"
                          + "（4 帧一页：256+64=320 宽、256+176=432 高）", frameBad.Count == 0,
                frameBad.Count == 0 ? "0 例外（5 职业 × 16 帧）" : string.Join("；", frameBad.ToArray()));

            // ④ 拼装后整页：素材 IHDR（**不是某一帧**）== 面板矩形 ÷1.8
            var pageBad = new List<string>();
            var pageN = 0;
            foreach (var letter in new[] { "a", "b", "n", "p", "s" })
                for (var page = 0; page < 4; page++)
                {
                    if (!TryPngSize(SkillTreePagePath(letter, page), out var w, out var h))
                    { pageBad.Add(letter + "#" + page + " 缺"); continue; }
                    pageN++;
                    if (w != 320 || h != 432) pageBad.Add($"{letter}#{page}: 整页 IHDR {w}×{h} != 320×432");
                    if (Math.Abs(w * K - SkillTreePanel.PanelSize.x) > 0.01f
                        || Math.Abs(h * K - SkillTreePanel.PanelSize.y) > 0.01f)
                        pageBad.Add($"{letter}#{page}: 整页 {w}×{h} ×1.8 != 面板矩形 {SkillTreePanel.PanelSize.x:0.##}×{SkillTreePanel.PanelSize.y:0.##}");
                }
            Program.Check($"{pageN} 个技能树**拼装后整页**文件（`Panel/skltree_*_back_{0..3}.png`）："
                          + "IHDR == 320×432 且 == 面板矩形 ÷1.8", pageBad.Count == 0,
                pageBad.Count == 0 ? "0 例外（5 职业 × 4 页）" : string.Join("；", pageBad.ToArray()));
        }

        /// <summary>
        /// 技能树**拼装后整页**的路径 —— 逐字复刻运行时 `UI/D2Icon.SkillTreeBackPath` 的拼法
        /// （`ResPaths.D2UiPanel + "skltree_" + 职业字母 + "_back_" + 页号`）。
        /// <para>
        /// 为什么不在本节直接调 `D2Icon.SkillTreeBackPath`：它要先读配表 `class_c.skill_class`
        /// 求职业字母，而本宿主是**离线进程**（读不到表 ⇒ 返回 null）。所以这里复刻拼法，
        /// 并由 ① 号断言**机器核对**两者一致（防漂移）。
        /// </para>
        /// </summary>
        private static string SkillTreePagePath(string clsLetter, int page)
            => ResPaths.D2UiPanel + "skltree_" + clsLetter + "_back_" + page;

        /// <summary>`Resources/Clover/{resPath}.png` 的文件是否存在。</summary>
        private static bool PngFile(string resPath, out string file)
        {
            file = null;
            if (string.IsNullOrEmpty(resPath)) return false;      // 路径拼不出来（配表没加载等）⇒ 算缺失
            file = Path.Combine(Program.ResourceRoot, "Clover",
                resPath.Replace('/', Path.DirectorySeparatorChar) + ".png");
            return File.Exists(file);
        }

        /// <summary>PNG 的 IHDR 宽高（PNG 头固定：宽 16..19、高 20..23，大端）。</summary>
        private static bool PngSize(string resPath, out int w, out int h)
            => TryPngSize(resPath, out w, out h);

        private static bool TryPngSize(string resPath, out int w, out int h)
        {
            w = 0; h = 0;
            if (!PngFile(resPath, out var file)) return false;
            var b = File.ReadAllBytes(file);
            if (b.Length < 24) return false;
            w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            return w > 0 && h > 0;
        }

        /// <summary>
        /// 解出 PNG 的 RGBA8 像素（**只处理 8bit、非隔行、色彩类型 2/6**）。
        /// <para>为什么必须在宿主里真的解像素：B-2 的判据是"**这一帧有没有全透明像素**"
        /// （= 用的是"索引 0 透明"的 DC6 直出那一套），这是**像素级**事实，IHDR 看不到。
        /// 解不出来的（例如调色板图 `Cursor.png`）返回 false ⇒ 调用方如实记为"跳过"，不假装通过。</para>
        /// </summary>
        private static bool TryDecodeRgba(string resPath, out int w, out int h, out byte[] rgba)
        {
            w = 0; h = 0; rgba = null;
            if (!PngFile(resPath, out var file)) return false;
            var b = File.ReadAllBytes(file);
            if (b.Length < 8 || b[0] != 0x89 || b[1] != 0x50) return false;

            var idat = new MemoryStream();
            var pos = 8;
            int bitDepth = 0, colorType = 0;
            while (pos + 8 <= b.Length)
            {
                var len = (b[pos] << 24) | (b[pos + 1] << 16) | (b[pos + 2] << 8) | b[pos + 3];
                if (len < 0 || pos + 12 + len > b.Length) break;
                var tag = Encoding.ASCII.GetString(b, pos + 4, 4);
                var d = pos + 8;
                if (tag == "IHDR")
                {
                    w = (b[d] << 24) | (b[d + 1] << 16) | (b[d + 2] << 8) | b[d + 3];
                    h = (b[d + 4] << 24) | (b[d + 5] << 16) | (b[d + 6] << 8) | b[d + 7];
                    bitDepth = b[d + 8];
                    colorType = b[d + 9];
                    if (b[d + 12] != 0) return false;        // 隔行不支持
                }
                else if (tag == "IDAT") idat.Write(b, d, len);
                else if (tag == "IEND") break;
                pos = d + len + 4;
            }

            if (bitDepth != 8 || (colorType != 6 && colorType != 2)) return false;

            byte[] raw;
            try
            {
                using (var ms = new MemoryStream(idat.ToArray()))
                using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionMode.Decompress))
                using (var outMs = new MemoryStream())
                {
                    z.CopyTo(outMs);
                    raw = outMs.ToArray();
                }
            }
            catch (Exception)
            {
                return false;
            }

            var bpp = colorType == 6 ? 4 : 3;
            var stride = w * bpp;
            if (raw.Length < (stride + 1) * h) return false;
            rgba = new byte[w * h * 4];
            var prev = new byte[stride];
            var cur = new byte[stride];
            var o = 0;
            for (var y = 0; y < h; y++)
            {
                var filter = raw[o++];
                Array.Copy(raw, o, cur, 0, stride);
                o += stride;
                for (var x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? cur[x - bpp] : 0;
                    int bb = prev[x];
                    int c = x >= bpp ? prev[x - bpp] : 0;
                    switch (filter)
                    {
                        case 1: cur[x] = (byte)(cur[x] + a); break;
                        case 2: cur[x] = (byte)(cur[x] + bb); break;
                        case 3: cur[x] = (byte)(cur[x] + ((a + bb) >> 1)); break;
                        case 4:
                        {
                            var pp = a + bb - c;
                            var pa = Math.Abs(pp - a);
                            var pb = Math.Abs(pp - bb);
                            var pc = Math.Abs(pp - c);
                            cur[x] = (byte)(cur[x] + (pa <= pb && pa <= pc ? a : (pb <= pc ? bb : c)));
                            break;
                        }
                    }
                }
                for (var x = 0; x < w; x++)
                {
                    rgba[(y * w + x) * 4] = cur[x * bpp];
                    rgba[(y * w + x) * 4 + 1] = cur[x * bpp + 1];
                    rgba[(y * w + x) * 4 + 2] = cur[x * bpp + 2];
                    rgba[(y * w + x) * 4 + 3] = bpp == 4 ? cur[x * bpp + 3] : (byte)255;
                }
                var t = prev; prev = cur; cur = t;
            }
            return true;
        }

        /// <summary>极简 TSV 读取（首行表头 → 每行一个字典；列数不足的行跳过）。</summary>
        private static List<Dictionary<string, string>> ReadTsv(string file)
        {
            var list = new List<Dictionary<string, string>>();
            var lines = File.ReadAllLines(file, Encoding.UTF8);
            if (lines.Length == 0) return list;
            var head = lines[0].Split('\t');
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                var f = lines[i].Split('\t');
                if (f.Length < head.Length) continue;
                var d = new Dictionary<string, string>();
                for (var j = 0; j < head.Length; j++) d[head[j]] = f[j];
                list.Add(d);
            }
            return list;
        }

        /// <summary>去掉注释后数出现次数（注释里会"提到"某个名字，那是解释不是引用）。</summary>
        private static int CountOccurrences(string src, string needle)
        {
            var n = 0;
            var i = 0;
            while ((i = src.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        /// <summary>去掉 `//` 行注释与 `/* */` 块注释。</summary>
        private static string StripNonCode(string src)
        {
            var sb = new StringBuilder(src.Length);
            for (var i = 0; i < src.Length; i++)
            {
                if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    sb.Append('\n');
                    continue;
                }
                if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                    i++;
                    continue;
                }
                sb.Append(src[i]);
            }
            return sb.ToString();
        }
    }
}
