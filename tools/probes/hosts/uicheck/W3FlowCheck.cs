// ─────────────────────────────────────────────────────────────────────────────
// w3 片：**流程屏（启动/菜单/选角/创角/读条/设置/暂停/死亡）逐控件审计**的离线断言（节 ⑲）。
//
// 本节的判据全部**可离线计算**（不需要 Unity 原生）：
//   ① **素材侧**（磁盘 = 唯一权威）：8 个屏引用到的原版 PNG **真在磁盘上**，
//      且它的 IHDR 实测尺寸 == 常量表里声明的「原版尺寸」（不是"同类就行"）；
//   ② **几何侧**：元件矩形 / 素材原生尺寸的**宽高比一致**（这一步才拦住"矩形与素材比例不符 ⇒ 被拉伸"，
//      `preserveAspect` 只管 UI 侧内缩，管不了"矩形本身就写错了比例"）；
//   ③ **常量侧**：行栈公式（`Select.RowY`）、4 行选项节奏、新登记进对照表的条目**真的在表里**；
//   ④ **面板源码侧**：7 个流程屏都**显式声明 Layer**、底部两钮的**槽位语义**照原版 Exit/Ok、
//      8 个屏都调 `UiLayoutFlow.LogTable`、读条屏**零 `Events.*`**（它是纯呈现）。
//
// 为什么这些必须离线钉住：面板实例在离线进程造不出来（`new GameObject()` 需要原生运行时），
// 而"底图是不是原版那张 / 矩形 == 原版×1.8 / 素材比例 == 矩形比例 / 该有却没有"这四类判据
// **都是纯数据 + 磁盘事实** ⇒ 不需要进 Play。进 Play 才能判的（字模观感、贴图色调、按钮手感）
// 留给主 agent 采联络图，本文件末尾声明。
//
// 只读断言，不改任何工程产物。
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.IO;
using Diablo2.Core;
using Diablo2.UI;
using UnityEngine;

namespace Uicheck
{
    internal static class W3FlowCheck
    {
        /// <summary>一个"带原版素材"的元件：素材路径 + 声明的原版尺寸 + 本体矩形（画布单位）。</summary>
        private struct Art
        {
            public string Screen;    // 屏名（回报/日志里的归属）
            public string Control;   // 控件名
            public string Path;      // 素材路径（`Resources/Clover/` 之下，不含 .png）
            public float OrigW;      // 声明的原版尺寸（原版 px，= DC6 帧/整幅实测）
            public float OrigH;
            public float RectW;      // 元件矩形（画布单位）
            public float RectH;
        }

        public static void Run()
        {
            Console.WriteLine("── ⑲ ★ w3：流程屏逐控件审计（原版素材 ↔ 常量表 ↔ 面板源码）──");
            AssetSide();
            GeometrySide();
            ConstantSide();
            SourceSide();
            RegisteredUnused();
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ① 素材侧：文件在位 + IHDR == 声明的原版尺寸
        // ═════════════════════════════════════════════════════════════════════
        private static void AssetSide()
        {
            // 每一行的 4 个数值都有出处（常量表注释里逐条写了 DC6 实测/整幅尺寸）：
            //   启动屏 logo / 主菜单 main_screen / 选角·创角 class_select_screen / 宽・中按钮四帧 /
            //   职业 6 张半身像帧 0 / 读条 10 帧 / 死亡屏 4 块底图 + 标题条 + 按钮两帧。
            var arts = new List<Art>();
            void Add(string screen, string control, string path, float ow, float oh, Vector2 rect)
            {
                arts.Add(new Art
                {
                    Screen = screen, Control = control, Path = path,
                    OrigW = ow, OrigH = oh, RectW = rect.x, RectH = rect.y,
                });
            }

            // ── 启动屏：原版 `ui/Logo/logo.DC6` 帧 0（实测 319×177 = DIABLO II 火焰字标）──
            Add("Boot", "原版 logo", ResPaths.Frame(ResPaths.MenuLogo, 0),
                319f, 177f, UiLayoutFlow.Px(new Vector2(319f, 177f)));

            // ── 主菜单：原版整屏贴图 `main_screen`（800×600）+ 宽按钮两态 ──
            Add("MainMenu", "整屏底图", ResPaths.MenuMainScreen, 800f, 600f,
                new Vector2(UiLayoutFlow.OrigWidth * UiLayoutFlow.Scale,
                            UiLayoutFlow.OrigHeight * UiLayoutFlow.Scale));
            Add("MainMenu", "按钮常态底图", ResPaths.BtnWideNormal,
                UiLayoutFlow.WideButtonOrig.x, UiLayoutFlow.WideButtonOrig.y, UiLayoutFlow.WideButton);
            Add("MainMenu", "按钮按下底图", ResPaths.BtnWidePressed,
                UiLayoutFlow.WideButtonOrig.x, UiLayoutFlow.WideButtonOrig.y, UiLayoutFlow.WideButton);

            // ── 选角 / 创角：原版整屏贴图 `class_select_screen`（800×600）+ 中等按钮两态 ──
            Add("CharSelect", "整屏底图", ResPaths.MenuClassSelectScreen, 800f, 600f,
                new Vector2(UiLayoutFlow.OrigWidth * UiLayoutFlow.Scale,
                            UiLayoutFlow.OrigHeight * UiLayoutFlow.Scale));
            Add("CharCreate", "整屏底图", ResPaths.MenuClassSelectScreen, 800f, 600f,
                new Vector2(UiLayoutFlow.OrigWidth * UiLayoutFlow.Scale,
                            UiLayoutFlow.OrigHeight * UiLayoutFlow.Scale));
            Add("CharSelect", "底部两钮常态底图", ResPaths.BtnMedNormal,
                UiLayoutFlow.MediumButtonOrig.x, UiLayoutFlow.MediumButtonOrig.y, UiLayoutFlow.MediumButton);
            Add("CharSelect", "底部两钮按下底图", ResPaths.BtnMedPressed,
                UiLayoutFlow.MediumButtonOrig.x, UiLayoutFlow.MediumButtonOrig.y, UiLayoutFlow.MediumButton);
            Add("CharCreate", "底部两钮常态底图", ResPaths.BtnMedNormal,
                UiLayoutFlow.MediumButtonOrig.x, UiLayoutFlow.MediumButtonOrig.y, UiLayoutFlow.MediumButton);
            Add("CharCreate", "底部两钮按下底图", ResPaths.BtnMedPressed,
                UiLayoutFlow.MediumButtonOrig.x, UiLayoutFlow.MediumButtonOrig.y, UiLayoutFlow.MediumButton);
            Add("CharSelect", "行内小按钮悬停底图", ResPaths.BtnMedSel,
                UiLayoutFlow.MediumButtonOrig.x, UiLayoutFlow.MediumButtonOrig.y, UiLayoutFlow.MediumButton);

            // ── 创角屏：2 个职业 × 3 态的半身像帧 0（素材与 `Spot` 的声明尺寸必须逐张相等）──
            // 槽位号 = `CharCreatePanel.SlotClassIds` 的行号：0 = Amazon、1 = Necromancer、2 = Barbarian
            //   （本项目只启用 0 与 2；槽 1 是用户决策停用的 Necromancer 位）。
            var portraitSlots = new[] { 0, 2 };
            var portraitCls = new[] { "amazon", "barbarian" };
            for (var c = 0; c < portraitCls.Length; c++)
            {
                for (var st = ResPaths.Portrait.Idle; st <= ResPaths.Portrait.Front; st++)
                {
                    var ps = UiLayoutFlow.ClassMenu.Spot.Of(portraitSlots[c], st);
                    Add("CharCreate", portraitCls[c] + "·" + ResPaths.Portrait.Label(st),
                        ResPaths.D2UiFrontEnd + portraitCls[c] + "/" + ResPaths.Portrait.Code(st) + "_0",
                        ps.OrigSize.x, ps.OrigSize.y, ps.Size);
                }
            }

            // ── 读条屏：原版 `ui/Loading/loadingscreen.dc6` 10 帧，全部 256×256 ──
            for (var i = 0; i < ResPaths.FrameCountLoadingScreen; i++)
            {
                Add("Loading", $"读条图第 {i + 1} 帧", ResPaths.Frame(ResPaths.MenuLoadingScreen, i),
                    UiLayoutFlow.Loading.ArtOrigSize.x, UiLayoutFlow.Loading.ArtOrigSize.y,
                    UiLayoutFlow.Loading.ArtSize);
            }

            // ── 死亡屏：底图 4 块 + 标题条 + 按钮两帧（几何来自 `UiLayoutGame.Death*`）──
            for (var i = 0; i < UiLayoutGame.DeathBackTiles.Length; i++)
            {
                var t = UiLayoutGame.DeathBackTiles[i];
                Add("Death", $"底图第 {i + 1} 块", ResPaths.EndGameTile(0, i), t.w, t.h,
                    UiLayoutGame.DeathTileSize(i));
            }
            for (var i = 0; i < ResPaths.FrameCountBannerYouDiedSoftCore; i++)
                Add("Death", $"标题条第 {i + 1} 块", ResPaths.BannerYouDiedSoftCoreTile(i),
                    UiLayoutGame.DeathBannerTiles[i].w, UiLayoutGame.DeathBannerTiles[i].h,
                    UiLayoutGame.DeathBannerTileSize(i));
            Add("Death", "按钮常态底图", ResPaths.Frame(ResPaths.MenuEndGameOK, 0), 96f, 32f,
                UiLayoutGame.DeathButtonSize);
            Add("Death", "按钮按下底图", ResPaths.Frame(ResPaths.MenuEndGameOK, 1), 96f, 32f,
                UiLayoutGame.DeathButtonSize);

            // A1：文件在位
            var missing = new List<string>();
            foreach (var a in arts)
                if (!PngSize(a.Path, out _, out _)) missing.Add(a.Screen + "/" + a.Control + "=" + a.Path);
            Program.Check($"{arts.Count} 个元件引用的原版素材**全部在磁盘上**", missing.Count == 0,
                missing.Count == 0 ? "0 缺失" : string.Join(", ", missing.ToArray()));

            // A2：IHDR == 声明的原版尺寸（「看着像同一类」不算，要逐张尺寸相等）
            var sizeBad = new List<string>();
            foreach (var a in arts)
            {
                if (!PngSize(a.Path, out var w, out var h)) continue;
                if (Math.Abs(w - a.OrigW) > 0.5f || Math.Abs(h - a.OrigH) > 0.5f)
                    sizeBad.Add($"{a.Screen}/{a.Control}: 声明 {a.OrigW:0}×{a.OrigH:0} vs 实测 {w}×{h}（{a.Path}）");
            }
            Program.Check($"{arts.Count} 个元件的「声明原版尺寸」== 素材 IHDR 实测（0 例外）", sizeBad.Count == 0,
                sizeBad.Count == 0 ? "0 例外" : string.Join("；", sizeBad.ToArray()));

            // A3：按钮三态用的是**原版对应的那几帧**（宽：常态/按下；中：常态/按下/高亮）
            var threeState = new List<string> { ResPaths.BtnWideNormal, ResPaths.BtnWidePressed, ResPaths.BtnMedSel };
            var stateMissing = new List<string>();
            foreach (var p in threeState)
                if (!PngSize(p, out _, out _)) stateMissing.Add(p);
            Program.Check("按钮三态底图（常态/悬停/按下）都取自原版帧且文件在位",
                stateMissing.Count == 0,
                stateMissing.Count == 0 ? string.Join(" / ", threeState.ToArray()) : string.Join(", ", stateMissing.ToArray()));
        }

        // ═════════════════════════════════════════════════════════════════════
        // ② 几何侧：矩形 / 素材 的宽高比一致（"被拉伸"的判据）
        // ═════════════════════════════════════════════════════════════════════
        private static void GeometrySide()
        {
            // 交叉相乘比比例（避免除零 + 浮点误差）：|rw*oh − rh*ow| ≤ 1e-2 * (ow*oh)
            var bad = new List<string>();
            var n = 0;
            foreach (var (screen, control, path, ow, oh, rw, rh) in RectRows())
            {
                if (ow <= 0f || oh <= 0f) continue;
                n++;
                var diff = Math.Abs(rw * oh - rh * ow);
                if (diff > 1e-2f * ow * oh)
                    bad.Add($"{screen}/{control}: 矩形 {rw:0.#}×{rh:0.#} vs 素材 {ow:0}×{oh:0}（{path}）");
            }
            Program.Check($"{n} 个带素材的元件：**矩形宽高比 == 素材原生宽高比**（防非等比拉伸；0 例外）",
                bad.Count == 0, bad.Count == 0 ? "0 例外" : string.Join("；", bad.ToArray()));
        }

        /// <summary>把 <see cref="AssetSide"/> 的同一批行再走一遍（单源：同一个构造函数）。</summary>
        private static IEnumerable<(string screen, string control, string path, float ow, float oh, float rw, float rh)>
            RectRows()
        {
            yield return ("Boot", "原版 logo", ResPaths.Frame(ResPaths.MenuLogo, 0), 319f, 177f,
                UiLayoutFlow.Px(new Vector2(319f, 177f)).x, UiLayoutFlow.Px(new Vector2(319f, 177f)).y);
            var bg = new Vector2(UiLayoutFlow.OrigWidth * UiLayoutFlow.Scale,
                UiLayoutFlow.OrigHeight * UiLayoutFlow.Scale);
            yield return ("MainMenu", "整屏底图", ResPaths.MenuMainScreen, 800f, 600f, bg.x, bg.y);
            yield return ("CharSelect", "整屏底图", ResPaths.MenuClassSelectScreen, 800f, 600f, bg.x, bg.y);
            yield return ("CharCreate", "整屏底图", ResPaths.MenuClassSelectScreen, 800f, 600f, bg.x, bg.y);
            yield return ("MainMenu", "宽按钮", ResPaths.BtnWideNormal,
                UiLayoutFlow.WideButtonOrig.x, UiLayoutFlow.WideButtonOrig.y,
                UiLayoutFlow.WideButton.x, UiLayoutFlow.WideButton.y);
            yield return ("CharSelect", "中按钮", ResPaths.BtnMedNormal,
                UiLayoutFlow.MediumButtonOrig.x, UiLayoutFlow.MediumButtonOrig.y,
                UiLayoutFlow.MediumButton.x, UiLayoutFlow.MediumButton.y);
            yield return ("Loading", "读条图", ResPaths.Frame(ResPaths.MenuLoadingScreen, 0),
                UiLayoutFlow.Loading.ArtOrigSize.x, UiLayoutFlow.Loading.ArtOrigSize.y,
                UiLayoutFlow.Loading.ArtSize.x, UiLayoutFlow.Loading.ArtSize.y);
            for (var i = 0; i < ResPaths.FrameCountBannerYouDiedSoftCore; i++)
                yield return ("Death", $"标题条第 {i + 1} 块", ResPaths.BannerYouDiedSoftCoreTile(i),
                    UiLayoutGame.DeathBannerTiles[i].w, UiLayoutGame.DeathBannerTiles[i].h,
                    UiLayoutGame.DeathBannerTileSize(i).x, UiLayoutGame.DeathBannerTileSize(i).y);
            yield return ("Death", "按钮", ResPaths.Frame(ResPaths.MenuEndGameOK, 0), 96f, 32f,
                UiLayoutGame.DeathButtonSize.x, UiLayoutGame.DeathButtonSize.y);
            for (var i = 0; i < UiLayoutGame.DeathBackTiles.Length; i++)
            {
                var t = UiLayoutGame.DeathBackTiles[i];
                var sz = UiLayoutGame.DeathTileSize(i);
                yield return ("Death", $"底图第 {i + 1} 块", ResPaths.EndGameTile(0, i), t.w, t.h, sz.x, sz.y);
            }
            var portraitSlots = new[] { 0, 2 };         // 同上：0 = Amazon、2 = Barbarian
            var portraitCls = new[] { "amazon", "barbarian" };
            for (var c = 0; c < portraitCls.Length; c++)
            {
                for (var st = ResPaths.Portrait.Idle; st <= ResPaths.Portrait.Front; st++)
                {
                    var ps = UiLayoutFlow.ClassMenu.Spot.Of(portraitSlots[c], st);
                    yield return ("CharCreate", portraitCls[c] + "·" + ResPaths.Portrait.Label(st),
                        ResPaths.D2UiFrontEnd + portraitCls[c] + "/" + ResPaths.Portrait.Code(st) + "_0",
                        ps.OrigSize.x, ps.OrigSize.y, ps.Size.x, ps.Size.y);
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // ③ 常量侧：行栈公式 / 行节奏 / 新登记条目**真的在表里** / 单源
        // ═════════════════════════════════════════════════════════════════════
        private static void ConstantSide()
        {
            var listSize = UiLayoutFlow.Select.ListSize;
            var rowH = UiLayoutFlow.Select.RowH;
            var step = UiLayoutFlow.RowStep;

            // C-1 行栈：首行顶边 == 容器顶边内侧（行高 = 原版中等按钮高 35×1.8）
            var firstTop = UiLayoutFlow.Select.RowY(0) + rowH * 0.5f;
            var lastBottom = UiLayoutFlow.Select.RowY(UiLayoutFlow.Select.MaxRows - 1) - rowH * 0.5f;
            Program.Check("角色行栈：首行顶边 == 容器顶边内侧、末行底边 ≥ 容器底边（7 行都装得下）",
                Math.Abs(firstTop - listSize.y * 0.5f) < 1e-2f
                && lastBottom >= -listSize.y * 0.5f - 1e-2f,
                $"首行顶 {firstTop:0.#} vs 容器顶 {listSize.y * 0.5f:0.#}；末行底 {lastBottom:0.#} vs 容器底 {-listSize.y * 0.5f:0.#}");

            // C-2 行步进 81 > 行内元件最大高 63 ⇒ 相邻行不叠（这就是"行栈只有 1 行被算过"的那条担保）
            Program.Check($"行步进 {step:0.#} > 行内元件最大高 {rowH:0.#}（相邻行不叠行）",
                step > rowH, $"{step:0.#} > {rowH:0.#}");

            // C-3 行矩形是**单源**（`Select.RowSize` = 容器宽 × 行高）
            Program.Check("角色行矩形单源：Select.RowSize == (容器宽, 行高)（面板与对照表同用这一个值）",
                Math.Abs(UiLayoutFlow.Select.RowSize.x - listSize.x) < 1e-3f
                && Math.Abs(UiLayoutFlow.Select.RowSize.y - rowH) < 1e-3f,
                $"{UiLayoutFlow.Select.RowSize}");

            // C-4 选项面板 4 行节奏 == 原版 45 原版px ×1.8 = 81，且首行 == 原版 +60
            var ys = new[] { UiLayoutFlow.Settings.Row1Y, UiLayoutFlow.Settings.Row2Y,
                UiLayoutFlow.Settings.Row3Y, UiLayoutFlow.Settings.Row4Y };
            var rhythmOk = true;
            for (var i = 1; i < ys.Length; i++)
                if (Math.Abs((ys[i - 1] - ys[i]) - step) > 1e-3f) rhythmOk = false;
            Program.Check("选项面板 4 行的行节奏 == 原版 (35+10)×1.8 = 81（首行 = 原版 +60）",
                rhythmOk && Math.Abs(ys[0] - UiLayoutFlow.Px(60f)) < 1e-3f,
                $"{ys[0]:0.#}/{ys[1]:0.#}/{ys[2]:0.#}/{ys[3]:0.#}（步进 {ys[0] - ys[1]:0.#}）");

            var settingsRows = CountTableRows(UiLayoutFlow.Panel.Settings, true);
            var charSelectRows = CountTableRows(UiLayoutFlow.Panel.CharSelect, true);
            Program.Check($"对照表登记：Settings 屏实元件 {settingsRows} 条（≥15）/ CharSelect 屏实元件 {charSelectRows} 条（≥13）",
                settingsRows >= 15 && charSelectRows >= 13,
                $"Settings={settingsRows} CharSelect={charSelectRows}（修前分别是 1 / 0）");

            // C-6 两组**不混装**：CharSelect 组里不许出现创角屏专属节点（半身像/名字框），
            //     Class 组里不许出现选角屏专属节点（角色行）
            var charSelectBad = new List<string>();
            var classBad = new List<string>();
            foreach (var e in UiLayoutFlow.Table)
            {
                var tag = e.Node + "|" + e.Use;
                // 创角屏专属：半身像（素材节点名带 `FrontEnd/`）/ 名字输入框 / 职业热点
                // （行热点的 Use 里也带「(热点)」⇒ 必须按节点名排除，见下一行条件）
                if (e.Panel == UiLayoutFlow.Panel.CharSelect
                    && (tag.Contains("FrontEnd/") || tag.Contains("半身像") || tag.Contains("名字输入框")
                        || (tag.Contains("(热点)") && !tag.Contains("角色行"))))
                    charSelectBad.Add(e.Node);
                // 选角屏专属：角色行（含行热点 —— 它的 Use 里写的是「行热点」而不是「(热点)」）
                if (e.Panel == UiLayoutFlow.Panel.Class && tag.Contains("角色行"))
                    classBad.Add(e.Node);
            }
            Program.Check("选角屏专属组（CharSelect）与创角屏专属元件**不混装**（跨屏的\"两两重叠\"比较无意义）",
                charSelectBad.Count == 0 && classBad.Count == 0,
                charSelectBad.Count == 0 && classBad.Count == 0
                    ? "0 混装"
                    : "CharSelect 组里混入 " + string.Join(",", charSelectBad.ToArray())
                      + " / Class 组里混入选角行 " + string.Join(",", classBad.ToArray()));

            // C-7 `Panel.CharSelect` 组整体在画布内（与既有 7 屏逐屏断言同一口径，这里点名本组）
            var b = UiLayoutFlow.BoundsOf(UiLayoutFlow.Panel.CharSelect);
            var fit = UiLayoutFlow.FitOf(UiLayoutFlow.Panel.CharSelect);
            Program.Check("选角屏专属组乘适配系数后仍落在 1920×1080 画布内",
                b.xMin * fit >= -UiLayoutFlow.RefHalf.x && b.xMax * fit <= UiLayoutFlow.RefHalf.x
                && b.yMin * fit >= -UiLayoutFlow.RefHalf.y && b.yMax * fit <= UiLayoutFlow.RefHalf.y,
                $"x[{b.xMin * fit:0.#},{b.xMax * fit:0.#}] y[{b.yMin * fit:0.#},{b.yMax * fit:0.#}]");

            // C-8 死亡屏换算口径与流程屏一致（`UiLayoutGame.K` 必须 == `UiLayoutFlow.Scale`）
            Program.Check("死亡屏换算 K == 流程屏 Scale == 1.8（同一套「按高等比 + 水平居中」口径）",
                Math.Abs(UiLayoutGame.K - UiLayoutFlow.Scale) < 1e-6f
                && Math.Abs(UiLayoutGame.K - 1.8f) < 1e-6f,
                $"K={UiLayoutGame.K} Scale={UiLayoutFlow.Scale}");
        }

        private static int CountTableRows(string panel, bool requireNonZeroSize)
        {
            var n = 0;
            foreach (var e in UiLayoutFlow.Table)
            {
                if (e.Panel != panel) continue;
                if (requireNonZeroSize && (e.Size.x <= 0f || e.Size.y <= 0f)) continue;
                n++;
            }
            return n;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ④ 面板源码侧
        // ═════════════════════════════════════════════════════════════════════
        private static void SourceSide()
        {
            // D-1 7 个流程屏都**显式声明 Layer**（修前 4 个屏靠基类默认值走通，只是文件头的一行散文）
            var layerExpect = new (Type type, string name, string layer)[]
            {
                (typeof(BootPanel), "BootPanel", "Normal"),
                (typeof(MainMenuPanel), "MainMenuPanel", "Normal"),
                (typeof(CharSelectPanel), "CharSelectPanel", "Normal"),
                (typeof(CharCreatePanel), "CharCreatePanel", "Normal"),
                (typeof(LoadingPanel), "LoadingPanel", "System"),
                (typeof(SettingsPanel), "SettingsPanel", "Top"),
                (typeof(PausePanel), "PausePanel", "Top"),
            };
            var layerBad = new List<string>();
            foreach (var (type, name, layer) in layerExpect)
            {
                var p = type.GetProperty("Layer");
                if (p == null || p.DeclaringType != type) { layerBad.Add(name + "(没声明)"); continue; }
                var src = File.ReadAllText(Path.Combine(Program.UiDir, name + ".cs"), System.Text.Encoding.UTF8);
                if (!src.Contains("override UILayer Layer => UILayer." + layer))
                    layerBad.Add(name + "(声明值≠" + layer + ")");
            }
            Program.Check("7 个流程屏**都显式声明 Layer**（值 = 反射读到的声明类型 + 源码字面量，双向核对）",
                layerBad.Count == 0, layerBad.Count == 0 ? "7/7" : string.Join(", ", layerBad.ToArray()));

            // D-2 底部两钮的**槽位语义照原版**：Exit 槽(−540) = 离开本屏；Ok 槽(+540) = 确认/推进
            var selSrc = File.ReadAllText(Path.Combine(Program.UiDir, "CharSelectPanel.cs"), System.Text.Encoding.UTF8);
            var createSrc = File.ReadAllText(Path.Combine(Program.UiDir, "CharCreatePanel.cs"), System.Text.Encoding.UTF8);
            var selBack = CallAt(selSrc, "\"Back\", Text.Back");
            var selCreate = CallAt(selSrc, "\"Create\", Text.NewHero");
            var createBack = CallAt(createSrc, "\"Back\", Text.Back");
            var createOk = CallAt(createSrc, "\"Confirm\", Text.Ok");
            Program.Check("选角屏：Exit 槽(−540) 放「离开本屏」、Ok 槽(+540) 放「确认/推进」（原版 ExitButton.m_Text=EXIT / OkButton.m_Text=OK）",
                selBack.Contains("ClassMenu.ExitPos") && selBack.Contains("Events.ToMainMenuRequest")
                && selCreate.Contains("ClassMenu.OkPos") && selCreate.Contains("Events.Fsm.TriggerNeedCreate"),
                "Back@ExitPos→ToMainMenuRequest / Create@OkPos→TriggerNeedCreate");
            Program.Check("创角屏与选角屏的底部槽位语义**一致**（左下永远\"离开/返回\"、右下永远\"确认\"）",
                createBack.Contains("ClassMenu.ExitPos") && createBack.Contains("Events.CharSelectRequest")
                && createOk.Contains("ClassMenu.OkPos") && createOk.Contains("OnConfirm"),
                "Back@ExitPos / Confirm@OkPos");

            // D-3 7 个屏都调 `LogTable`（验收要的「面板 → 原版坐标 → 我们的坐标 → 依据节点名」出口）
            var noLog = new List<string>();
            foreach (var (_, name, _) in layerExpect)
            {
                var src = File.ReadAllText(Path.Combine(Program.UiDir, name + ".cs"), System.Text.Encoding.UTF8);
                if (!src.Contains("UiLayoutFlow.LogTable(")) noLog.Add(name);
            }
            Program.Check("7 个流程屏都调用 `UiLayoutFlow.LogTable`（对照表有一次性日志出口）",
                noLog.Count == 0, noLog.Count == 0 ? "7/7" : string.Join(", ", noLog.ToArray()));

            // D-4 读条屏是**纯呈现** ⇒ 源码里 0 处 `Events.`（进度由 `AppFlow` 直接调 `SetProgress`）
            var loadSrc = File.ReadAllText(Path.Combine(Program.UiDir, "LoadingPanel.cs"), System.Text.Encoding.UTF8);
            // 去掉注释再数（文件头大段注释里会提到事件名，那是说明不是引用）
            var loadCode = StripComments(loadSrc);
            Program.Check("读条屏**不发也不收任何 Events.**（纯呈现：进度由 AppFlow 直接调 SetProgress）",
                !loadCode.Contains("Events."), "0 命中（去注释后）");

            // D-5 4 个屏新加的 Layer 覆盖是**行为零变化**（值 == 基类 `UIPanel.Layer` 的默认值 Normal）。
            //   读的是**引擎源码**（面板实例在离线宿主里造不出来 ⇒ 实例属性读不到；
            //   `MonoBehaviour` 也不能 `new`/`GetUninitializedObject`）—— 与 §⑳ 读引擎 UI.cs 同一做法。
            var contracts = Path.Combine(Program.ProjectRoot, "..", "clover-client-unity-engine",
                "Runtime", "Core", "PresentationContracts.cs");
            if (!File.Exists(contracts))
            {
                Program._skip++;
                Console.WriteLine($"[SKIP] 基类默认层的出处（{contracts} 不在本机 ⇒ 跳过；"
                    + "读不到引擎源码时这条断言无判据）");
            }
            else
            {
                var cs = File.ReadAllText(contracts, System.Text.Encoding.UTF8);
                Program.Check("新增的 4 处 Layer 覆盖值 == 基类 `UIPanel.Layer` 的默认值（Normal）⇒ **行为零变化**",
                    cs.Contains("public virtual UILayer Layer => UILayer.Normal;"),
                    "出处 = clover-client-unity-engine/Runtime/Core/PresentationContracts.cs 的 UIPanel 默认层");
            }
        }

        /// <summary>
        /// 取源码里**含 <paramref name="marker"/> 的那一整次调用**（从它前面最近的 `(` 起，按括号配平截到对应 `)`）。
        /// 用途：断言"某个按钮的构造调用把哪个位置常量与哪个动作配在一起"——
        /// 这是**配对关系**，不是"文件里出现过就算"。
        /// <para>为什么不能截到下一个 `;`：回调是**多语句 lambda**（`"Back"` 那颗里面先 `Log.Info(…);`
        /// 再 `Emit(…)`），截到第一个 `;` 会把真正的动作丢掉 ⇒ 断言会**假红**（本片实测踩到）。</para>
        /// </summary>
        private static string CallAt(string src, string marker)
        {
            var i = src.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return string.Empty;
            var open = src.LastIndexOf('(', i);
            if (open < 0) return string.Empty;
            var depth = 0;
            for (var j = open; j < src.Length; j++)
            {
                if (src[j] == '(') depth++;
                else if (src[j] == ')')
                {
                    depth--;
                    if (depth == 0) return src.Substring(open, j - open + 1);
                }
            }
            return src.Substring(open);
        }

        /// <summary>去掉 `//` 行注释与 `/* */` 块注释（用于"源码里有没有真的引用某 API"这类计数）。</summary>
        private static string StripComments(string src)
        {
            var sb = new System.Text.StringBuilder(src.Length);
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

        // ═════════════════════════════════════════════════════════════════════
        // ⑤ 登记（**不计失败**）：原版导出素材里"零引用"的那一批
        // ═════════════════════════════════════════════════════════════════════
        //  为什么**不**做成失败断言：这批素材（`MENU/boxpieces` 九宫格黑框 22 帧、`helpborder`、
        //  `upgrade`、`okcancelbtn`、`buttontempok/cancel`、`btn_short` …）**没有任何一屏的
        //  权威拼装口径**（`export_d2ui.py` 的注释自己写着"怎么摆缺权威依据"），
        //  **接进来才是错**（那就是自创布局）。所以这里只**打印清单**，把决定权交回主 agent；
        //  做成 FAIL 会让本宿主永远到不了 `FAILED=0`，而"红"表达的是一件**待裁决**的事。
        private static void RegisteredUnused()
        {
            var menuDir = Path.Combine(Program.ResourceRoot, "Clover", "D2", "UI", "Menu");
            if (!Directory.Exists(menuDir))
            {
                Program.Check("原版 MENU/ 素材目录存在", false, menuDir);
                return;
            }

            var allCode = new System.Text.StringBuilder();
            foreach (var f in Directory.GetFiles(Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts"),
                         "*.cs", SearchOption.AllDirectories))
                allCode.Append(File.ReadAllText(f, System.Text.Encoding.UTF8));

            var code = allCode.ToString();
            var unused = new List<string>();
            var total = 0;
            foreach (var f in Directory.GetFiles(menuDir, "*.png"))
            {
                var stem = Path.GetFileNameWithoutExtension(f);
                total++;
                // 取帧名前缀（`endgameok_0` → `endgameok`）：本工程用 `ResPaths.Frame(前缀, i)` 拼帧名，
                // 所以**前缀**在源码里出现即算被引用（只有 `stem` 出现才算会把 Frame 写法误判成未引用）。
                var cut = stem.LastIndexOf('_');
                var prefix = cut > 0 && IsAllDigits(stem.Substring(cut + 1)) ? stem.Substring(0, cut) : stem;
                if (!code.Contains(prefix)) unused.Add(stem);
            }
            Console.WriteLine($"      │ [登记·非失败] MENU/ 下 {total} 张原版导出 PNG，其中 **{unused.Count} 张在"
                + " Scripts/** 里零引用**（无 ResPaths 常量、无调用方）");
            Console.WriteLine("      │   未引用清单：" + string.Join(", ", unused.ToArray()));
            //   `boxpieces_0..21` 现在**有**消费者了，只是消费者不在 `Scripts/**` 里，而在
            //   **构建期脚本** `tools/d2codec/assemble_boxpieces.py`（把 22 帧拼成整幅窗框 PNG，
            //   偏移由像素自证反推；口径与判据见该脚本文件头与 ㉑ 节 `BoxFrameSide()`）。
            //   运行期面板加载的是拼好的 `D2/UI/Panel/boxframe_{settings,pause}.png` ⇒
            //   这 22 帧在 `Scripts/**` 里"零引用"是**设计内**的（本行的口径只看 Scripts/**，
            //   故它仍列在这里，但**必须**能指出真正的消费者，否则就是遗漏）。
            var assembler = Path.Combine(Program.ProjectRoot, "tools", "d2codec", "assemble_boxpieces.py");
            var asmOk = File.Exists(assembler)
                        && File.ReadAllText(assembler).Contains("boxpieces");
            Program.Check("`boxpieces_0..21` 的**构建期消费者**在位（`tools/d2codec/assemble_boxpieces.py`）"
                          + " —— 它们不在 Scripts/** 里被引用是**设计内**（拼好之后运行期只加载整幅）",
                asmOk, asmOk ? "脚本在位且引用 boxpieces" : "缺 " + assembler);

            Console.WriteLine("      │   其中 `boxpieces_0..21` = 原版 `data/global/ui/MENU/boxpieces.DC6`"
                + " 的**窗框拼装块**（22 帧 14×15）—— ★ w5 已接：拼成整幅窗框当**选项/暂停的底板**"
                + "（`D2/UI/Panel/boxframe_settings.png` 432×348 / `boxframe_pause.png` 288×180；"
                + "偏移自证 + 接缝自证见 ㉑ 节 `BoxFrameSide()`）；"
                + "其中**装饰变体 14 帧**（上/下边块各另 5 个、左/右边块各另 2 个，只差石纹与金色饰点）"
                + "**有意不用**：「哪个变体放哪一格」无出处 ⇒ 每族一律取第一个（登记在 `client/资源欠缺清单.md`）。");
        }

        private static bool IsAllDigits(string s)
        {
            if (s.Length == 0) return false;
            for (var i = 0; i < s.Length; i++)
                if (s[i] < '0' || s[i] > '9') return false;
            return true;
        }

        // ── 工具 ─────────────────────────────────────────────────────────────

        /// <summary>`Resources/Clover/{resPath}.png` 的 IHDR 宽高（PNG 头固定：宽在 16..19、高在 20..23，大端）。</summary>
        private static bool PngSize(string resPath, out int w, out int h)
        {
            w = 0; h = 0;
            var file = Path.Combine(Program.ResourceRoot, "Clover",
                resPath.Replace('/', Path.DirectorySeparatorChar) + ".png");
            if (!File.Exists(file)) return false;
            var b = File.ReadAllBytes(file);
            if (b.Length < 24) return false;
            w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            return w > 0 && h > 0;
        }
    }
}
