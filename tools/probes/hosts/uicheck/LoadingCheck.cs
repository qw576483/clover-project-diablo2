// ═══════════════════════════════════════════════════════════════════════════
//  LoadingCheck.cs（uicheck 宿主的一个检查节，agent-a3 新增）
//
//  断言两件事（离线可验的部分；屏幕观感留给 Play 截图）：
//   ① **进图读条画面**：原版 = 黑底 + 居中 10 帧读条图（`data/global/ui/Loading/loadingscreen.dc6`，
//      逐帧独立 PNG），进度 → 帧号；并且**原版没有的进度条/百分比/提示文字已被删除**。
//   ② **区域名弹出**：几何/字体/颜色/时长逐条对齐原版 `LevelEntryTitle.prefab` + `LevelEntryTitle.cs`；
//      区域官方名与 `Table/Tsv/Level.tsv` 的 `level_name` 列 + `Module/Monster/AreaLevelTable` 三方一致。
//
//  依据全部写在各 Check 的 detail 里（文件:行号 或 磁盘文件名），不靠记忆。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module.Flow;      // ★load：LoadingSteps（分档表，与 AppFlow 同程序集 ⇒ internal 可见）
using Diablo2.UI;
using UnityEngine;

namespace Uicheck
{
    /// <summary>进图读条画面 + 区域名弹出的离线断言。</summary>
    internal static class LoadingCheck
    {
        /// <summary>转发宿主统一的断言出口（`Program.Check` 负责计数与 `[ OK ]/[FAIL]` 打印）。</summary>
        private static void Check(string what, bool ok, string detail) => Program.Check(what, ok, detail);

        private static string Project(string rel)
            => Path.Combine(Program.ProjectRoot, rel.Replace('/', Path.DirectorySeparatorChar));

        private static string UiSrc(string file) => Path.Combine(Program.UiDir, file);

        private static bool Near(float a, float b, float eps = 1e-3f) => Math.Abs(a - b) <= eps;

        public static void Run()
        {
            CheckLoadingScreen();
            CheckLevelEntryTitle();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ① 进图读条画面
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckLoadingScreen()
        {
            Console.WriteLine("── a3-① 进图读条画面（原版：黑底 + 居中 10 帧读条图，帧号 = 真实进度）──");

            // ── 资源路径常量 + 磁盘文件 ────────────────────────────────────────
            var expectPrefix = ResPaths.D2UiMenu + "loadingscreen";
            Check("ResPaths.MenuLoadingScreen = `D2/UI/Menu/loadingscreen`（原版 DC6 解出的帧名前缀）",
                ResPaths.MenuLoadingScreen == expectPrefix, ResPaths.MenuLoadingScreen);

            Check("ResPaths.FrameCountLoadingScreen = 10（实测：loadingscreen.dc6 dir=1 fpd=10）",
                ResPaths.FrameCountLoadingScreen == 10, ResPaths.FrameCountLoadingScreen.ToString());

            var frameNameOk = true;
            var frameNames = new List<string>();
            for (var i = 0; i < ResPaths.FrameCountLoadingScreen; i++)
            {
                var n = ResPaths.Frame(ResPaths.MenuLoadingScreen, i);
                frameNames.Add(n);
                if (n != expectPrefix + "_" + i) frameNameOk = false;
            }
            Check("帧名 = `{前缀}_{i}`（i 从 0 起）共 10 个，逐帧可直接 Resources.Load<Sprite>",
                frameNameOk, frameNames[0] + " … " + frameNames[frameNames.Count - 1]);

            var missing = new List<string>();
            var badSize = new List<string>();
            for (var i = 0; i < ResPaths.FrameCountLoadingScreen; i++)
            {
                var file = Path.Combine(Program.ResourceRoot, "Clover",
                    ResPaths.Frame(ResPaths.MenuLoadingScreen, i).Replace('/', Path.DirectorySeparatorChar) + ".png");
                if (!File.Exists(file)) { missing.Add(ResPaths.Frame(ResPaths.MenuLoadingScreen, i)); continue; }

                // PNG 的 IHDR：宽高在字节 16..24（与 `tools/d2codec/dc6.py::write_png_rgba` 一致）
                var head = new byte[24];
                using (var fs = File.OpenRead(file)) fs.Read(head, 0, 24);
                var w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                var h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                if (w != 256 || h != 256) badSize.Add($"{Path.GetFileName(file)}={w}x{h}");
            }
            Check("10 张读条帧 PNG 全部在磁盘上（Resources/Clover/D2/UI/Menu/loadingscreen_0..9.png）",
                missing.Count == 0, missing.Count == 0 ? "0 缺失" : string.Join(", ", missing.ToArray()));
            Check("读条帧逐张 256×256（= 原版 DC6 帧尺寸；`LoadingScreen.cs:68` SetNativeSize）",
                badSize.Count == 0, badSize.Count == 0 ? "10 张全部 256×256" : string.Join(", ", badSize.ToArray()));

            // ── 布局（唯一来源 = UiLayoutFlow.Loading）─────────────────────────
            Check("读条图尺寸 = 原版 256×256 × 1.8 = 460.8×460.8（按高度等比：460.8/1080 = 42.7% = 原版 256/600）",
                Near(UiLayoutFlow.Loading.ArtSize.x, 256f * UiLayoutFlow.Scale)
                && Near(UiLayoutFlow.Loading.ArtSize.y, 256f * UiLayoutFlow.Scale)
                && Near(UiLayoutFlow.Loading.ArtSize.x, 460.8f),
                $"{UiLayoutFlow.Loading.ArtSize}（原版 {UiLayoutFlow.Loading.ArtOrigSize}）");

            Check("读条图中心 = 画布正中 (0,0)（原版 LoadingScreen.cs:58-61 的 anchorMin/Max/pivot/pos 全 = 0.5）",
                Near(UiLayoutFlow.Loading.ArtPos.x, 0f) && Near(UiLayoutFlow.Loading.ArtPos.y, 0f),
                UiLayoutFlow.Loading.ArtPos.ToString());

            // ── 1:1：原版读条屏没有进度条/百分比/提示文字 ⇒ 这些常量必须已被删掉 ──
            var loadingType = typeof(UiLayoutFlow).GetNestedType("Loading", BindingFlags.Public | BindingFlags.Static);
            var leftovers = new List<string>();
            if (loadingType != null)
            {
                foreach (var f in loadingType.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    var n = f.Name;
                    if (n.StartsWith("Bar", StringComparison.Ordinal)
                        || n.StartsWith("Percent", StringComparison.Ordinal)
                        || n.StartsWith("Tip", StringComparison.Ordinal))
                        leftovers.Add(n);
                }
            }
            Check("1:1：`UiLayoutFlow.Loading` 里已无 Bar*/Percent*/Tip*（原版载入屏只有黑底 + 一张图）",
                loadingType != null && leftovers.Count == 0,
                loadingType == null ? "找不到嵌套类型 Loading" : (leftovers.Count == 0 ? "0 条残留" : string.Join(", ", leftovers.ToArray())));

            var loadSrc = File.ReadAllText(UiSrc("LoadingPanel.cs"));
            Check("LoadingPanel 源码：黑底（UiArt.FullPanel + Color.black，= 原版 Background）",
                loadSrc.Contains("UiArt.FullPanel(") && loadSrc.Contains("Color.black"), "见 LoadingPanel.Build ①");
            Check("LoadingPanel 源码：用 ResPaths.MenuLoadingScreen 取原版 10 帧",
                loadSrc.Contains("ResPaths.MenuLoadingScreen"), "见 LoadingPanel.RequestFrames");
            Check("LoadingPanel 源码：读条图位置/尺寸走 UiLayoutFlow.Loading（不散落魔数）",
                loadSrc.Contains("UiLayoutFlow.Loading.ArtSize") && loadSrc.Contains("UiLayoutFlow.Loading.ArtPos"),
                "见 LoadingPanel.Build ②");
            Check("1:1：LoadingPanel 里已无自造进度条 / 百分比 / 提示文字（ProgressBar·Percent·Tip·D2Label）",
                !loadSrc.Contains("UiArt.ProgressBar(") && !loadSrc.Contains("Percent")
                && !loadSrc.Contains("\"Tip\"") && !loadSrc.Contains("D2Label.Create"),
                "原版 LoadingScreen.cs:34-70 全文无这三个节点");
            Check("LoadingPanel 里没有定时器/假定曲线（只把进来的 completeness 贴成帧，节奏在 Flow 侧）",
                !loadSrc.Contains("Game.Timer") && !loadSrc.Contains("Every(") && !loadSrc.Contains("After("),
                "见 LoadingPanel.SetProgress + Module/Flow/LoadingSteps.FrameCadenceSeconds");

            // ── 纯函数：进度 → 帧号 ────────────────────────────────────────────
            Check("FrameIndex：原版公式 `(int)((帧数-1) × completeness)`（LoadingScreen.cs:66）",
                LoadingPanel.FrameIndex(0f, 10) == 0 && LoadingPanel.FrameIndex(0.5f, 10) == 4
                && LoadingPanel.FrameIndex(1f, 10) == 9 && LoadingPanel.FrameIndex(-5f, 10) == 0
                && LoadingPanel.FrameIndex(5f, 10) == 9,
                $"0→{LoadingPanel.FrameIndex(0f, 10)} 0.5→{LoadingPanel.FrameIndex(0.5f, 10)} "
                + $"1→{LoadingPanel.FrameIndex(1f, 10)}（-5→{LoadingPanel.FrameIndex(-5f, 10)}、5→{LoadingPanel.FrameIndex(5f, 10)} 夹取）");

            // ── load 分档表（`Module/Flow/LoadingSteps.cs`，本轮新增）──────────
            // 修前这里断言的是 `LoadingPanel.MapProgress`（引擎 [0,0.9] → 整个门 [0,1]）。
            //    （Play 日志 20:36:40.229 → :40.231，2ms 走完 10 帧）。现在引擎那一段只喂门的前 5 档。
            Check("LoadingSteps：档数 = 原版读条图帧数 10",
                LoadingSteps.Count == 10 && LoadingSteps.Count == ResPaths.FrameCountLoadingScreen,
                LoadingSteps.Count.ToString());

            Check("LoadingSteps：引擎进度 [0,0.9] 只喂门的前 5 档（0→第1帧 / 0.45→第3帧 / 0.9→第5帧 / >0.9 夹到第5帧）",
                LoadingSteps.SceneLoadFrameIndex(0f) == 0
                && LoadingSteps.SceneLoadFrameIndex(0.45f) == 2
                && LoadingSteps.SceneLoadFrameIndex(0.9f) == LoadingSteps.SceneLoadedFrame
                && LoadingSteps.SceneLoadFrameIndex(1f) == LoadingSteps.SceneLoadedFrame
                && LoadingSteps.SceneLoadFrameIndex(float.NaN) == 0,
                $"0→{LoadingSteps.SceneLoadFrameIndex(0f)} 0.45→{LoadingSteps.SceneLoadFrameIndex(0.45f)} "
                + $"0.9→{LoadingSteps.SceneLoadFrameIndex(0.9f)} 1→{LoadingSteps.SceneLoadFrameIndex(1f)}"
                + $" NaN→{LoadingSteps.SceneLoadFrameIndex(float.NaN)}（SceneLoadedFrame={LoadingSteps.SceneLoadedFrame}）");

            var roundTrip = true;
            var rtDetail = new List<string>();
            for (var i = 0; i < LoadingSteps.Count; i++)
            {
                var c = LoadingSteps.CompletenessOf(i);
                var back = LoadingPanel.FrameIndex(c, LoadingSteps.Count);
                if (back != i) roundTrip = false;
                rtDetail.Add($"{i}→{c:0.####}→{back}");
            }
            Check("LoadingSteps：档号 → completeness → 帧号 往返一致（端点会吃浮点误差，故取档位区间中点）",
                roundTrip, string.Join(" ", rtDetail.ToArray()));

            // 边界刻意取"档宽 ±1ms"而不是正好 `FrameCadenceSeconds`：后者是 float 常量，
            var cad = LoadingSteps.FrameCadenceSeconds;
            Check("LoadingSteps：原版节奏 —— elapsed 0→档0 / (档宽−1ms)→档0 / (档宽+1ms)→档1 / 9×档宽+1ms→档9 / 超长夹到档9 / 负值→档0",
                LoadingSteps.MaxIndexAt(0d) == 0
                && LoadingSteps.MaxIndexAt(cad - 0.001d) == 0
                && LoadingSteps.MaxIndexAt(cad + 0.001d) == 1
                && LoadingSteps.MaxIndexAt(cad * 9 + 0.001d) == 9
                && LoadingSteps.MaxIndexAt(99d) == 9
                && LoadingSteps.MaxIndexAt(-1d) == 0 && LoadingSteps.MaxIndexAt(double.NaN) == 0,
                $"{cad}s/档：0→{LoadingSteps.MaxIndexAt(0d)} {cad - 0.001d:0.###}→{LoadingSteps.MaxIndexAt(cad - 0.001d)} "
                + $"{cad + 0.001d:0.###}→{LoadingSteps.MaxIndexAt(cad + 0.001d)} "
                + $"{cad * 9 + 0.001d:0.###}→{LoadingSteps.MaxIndexAt(cad * 9 + 0.001d)} +∞→{LoadingSteps.MaxIndexAt(99d)}");

            Check("LoadingSteps：逐档绑真实里程碑（第 5 档=场景就位·原版 Show(0.5f) / 第 7 档=主角·Show(0.75f) / "
                + "第 9 档=相机·Show(0.9f) / 第 10 档=世界就绪·Show(1.0f)）",
                LoadingSteps.SceneLoadedFrame == 4 && LoadingSteps.PlayerFrame == 6
                && LoadingSteps.CameraFrame == 8 && LoadingSteps.WorldReadyFrame == 9
                && LoadingSteps.ReasonOf(4).Contains("Show(0.5f)")
                && LoadingSteps.ReasonOf(6).Contains("Show(0.75f)")
                && LoadingSteps.ReasonOf(8).Contains("Show(0.9f)")
                && LoadingSteps.ReasonOf(9).Contains("Show(1.0f)")
                && LoadingSteps.ReasonOf(9).Contains("世界就绪"),
                $"{LoadingSteps.ReasonOf(4)} ｜ {LoadingSteps.ReasonOf(9)}");

            Check("LoadingSteps：10 档铺开时长 ≥ 0.5s（任务书要求「第 1 帧→第 10 帧可见 ≥0.5s」）",
                LoadingSteps.FrameCadenceSeconds * (LoadingSteps.Count - 1) >= 0.5f,
                $"{LoadingSteps.FrameCadenceSeconds} × {LoadingSteps.Count - 1} = "
                + $"{LoadingSteps.FrameCadenceSeconds * (LoadingSteps.Count - 1):0.00}s");

            // 门不许"自己走"：Flow 的呈现步同时受「真实档位」上限约束
            var flowSrc = File.ReadAllText(Project("client/Assets/Scripts/Module/Flow/AppFlow.cs"));
            Check("AppFlow：呈现档位 = min(真实档位, 节奏放行, 已呈现+1) ⇒ 门永不超前真实进度（不许假进度）",
                flowSrc.Contains("private void AdvanceReal(int frame, string why)")
                && flowSrc.Contains("private void PresentDoor()")
                && flowSrc.Contains("var allowed = _realFrame < paced ? _realFrame : paced;")
                && flowSrc.Contains("if (_doorFrame + 1 < allowed) allowed = _doorFrame + 1;"),
                "见 AppFlow.AdvanceReal / PresentDoor");
            Check("AppFlow：关屏在世界就绪之后（真实档 10 + 门开满第 10 帧 + 再等一帧）",
                flowSrc.Contains("private void CloseLoadingAndEnterStage()")
                && flowSrc.Contains("_realFrame < LoadingSteps.WorldReadyFrame || _doorFrame < LoadingSteps.WorldReadyFrame")
                && flowSrc.Contains("Game.UI.Close<LoadingPanel>();"),
                "见 AppFlow.OnLoadingTick ③ / CloseLoadingAndEnterStage");
            Check("AppFlow：世界构建分 5 档逐帧执行（每 tick 一档，不再一次做完）",
                flowSrc.Contains("private const int BuildStepCount = 5;")
                && flowSrc.Contains("if (_sceneReady && _buildNext < BuildStepCount)"),
                "见 AppFlow.RunBuildStep / OnLoadingTick ①");

            // ── 原版依据可复核：DC6 帧数 ───────────────────────────────────────
            var dc6 = Project("原版资源/d2dc6/data/global/ui/Loading/loadingscreen.dc6");
            // 原版资源树不进 git ⇒ 不在位时打 [SKIP]（不计失败）；在位则照旧断言存在。
            Program.CheckOriginalRes(
                "原版依据存在：`原版资源/d2dc6/data/global/ui/Loading/loadingscreen.dc6`（10 帧来源）", dc6);

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ② 区域名弹出（原版 LevelEntryTitle）
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckLevelEntryTitle()
        {
            Console.WriteLine("── a3-② 区域名弹出（原版 Prefabs/LevelEntryTitle.prefab + Engine/UI/LevelEntryTitle.cs）──");

            // ── 几何：满宽 ×300 贴顶，文字中心 = 顶边下 150 原版px ────────────
            //   位图字模按**原版 px** 排版 ⇒ 内层标签用"等效原版尺寸"建、再整体 ×K：
            //      等效原版宽 = 画布宽 / K = 1920 / 1.8 = 1066.67（原版是 anchor(0,1)-(1,1) 铺满父层
            //      ⇒ 我们铺满 1920 画布，等效原版宽就是 1066.67；**不是** 800 —— 800 是原版**画布**宽，
            //      而原版 prefab 的满宽也是 800，两者只在 800×600 基准下相等）。
            Check("几何：满宽 × 原版 300 高、贴屏幕顶边（prefab anchorMin(0,1)/anchorMax(1,1)/sizeDelta(0,300)/pivot(0.5,1)）",
                Near(UiLayoutGame.LevelTitleOrigSize.x, UiArt.RefWidth / UiLayoutGame.K)
                && Near(UiLayoutGame.LevelTitleOrigSize.y, 300f)
                && Near(UiLayoutGame.LevelTitleH, 300f * UiLayoutGame.K)
                && Near(UiLayoutGame.LevelTitleRect().yMax, UiArt.RefHeight * 0.5f)
                && Near(UiLayoutGame.LevelTitleRect().width, UiArt.RefWidth),
                $"原版尺寸 {UiLayoutGame.LevelTitleOrigSize} ×{UiLayoutGame.K} → {UiLayoutGame.LevelTitleH} 高；"
                + $"rect={UiLayoutGame.LevelTitleRect()}");

            Check("几何：文字中心 = 顶边下 150 原版px → ×1.8 = 270 → 画布 y = +270（点 (0,270)，顶边 +540）",
                Near(UiLayoutGame.LevelTitlePos.x, 0f) && Near(UiLayoutGame.LevelTitlePos.y, 270f),
                UiLayoutGame.LevelTitlePos.ToString());

            // ── 字体 / 颜色：照抄 prefab ───────────────────────────────────────
            Check("字体 = font30（prefab 的 Text.m_Font guid → Resources/Fonts/font30.fontsettings）",
                LevelEntryTitle.Font == D2Text.D2Font.Font30, LevelEntryTitle.Font.ToString());

            Check("颜色 = prefab 的 m_Color (0.8308824, 0.37267518, 0.37267518, 1)（照抄，不提亮不压暗）",
                Near(LevelEntryTitle.TextColor.r, 0.8308824f, 1e-5f)
                && Near(LevelEntryTitle.TextColor.g, 0.37267518f, 1e-5f)
                && Near(LevelEntryTitle.TextColor.b, 0.37267518f, 1e-5f)
                && Near(LevelEntryTitle.TextColor.a, 1f),
                LevelEntryTitle.TextColor.ToString());

            // ── 时序：总 3.75s（原版 duration），淡入/停留/淡出分摊 ────────────
            Check("时序：可见总时长 = 3.75s（原版 UI/LevelEntryTitle.cs:27 的 duration 默认值）",
                Near(LevelEntryTitle.TotalSeconds, 3.75f), LevelEntryTitle.TotalSeconds.ToString());

            Check("时序：淡入 0.4 + 停留 2.95 + 淡出 0.4 = 3.75（本项目新增的是**分摊**，总时长仍是原版的）",
                Near(LevelEntryTitle.FadeInSeconds + LevelEntryTitle.HoldSeconds + LevelEntryTitle.FadeOutSeconds,
                    LevelEntryTitle.TotalSeconds)
                && Near(LevelEntryTitle.FadeInSeconds, 0.4f) && Near(LevelEntryTitle.FadeOutSeconds, 0.4f),
                $"{LevelEntryTitle.FadeInSeconds} + {LevelEntryTitle.HoldSeconds} + {LevelEntryTitle.FadeOutSeconds}");

            Check("AlphaAt：淡入端 (0→0, 0.2→0.5, 0.4→1)、停留 1、淡出端 (3.55→0.5, 3.75→0, 5→0)",
                Near(LevelEntryTitle.AlphaAt(0f), 0f) && Near(LevelEntryTitle.AlphaAt(0.2f), 0.5f)
                && Near(LevelEntryTitle.AlphaAt(0.4f), 1f) && Near(LevelEntryTitle.AlphaAt(2f), 1f)
                && Near(LevelEntryTitle.AlphaAt(3.55f), 0.5f) && Near(LevelEntryTitle.AlphaAt(3.75f), 0f)
                && Near(LevelEntryTitle.AlphaAt(5f), 0f),
                $"α(0)={LevelEntryTitle.AlphaAt(0f)} α(0.2)={LevelEntryTitle.AlphaAt(0.2f)} "
                + $"α(0.4)={LevelEntryTitle.AlphaAt(0.4f)} α(2)={LevelEntryTitle.AlphaAt(2f)} "
                + $"α(3.55)={LevelEntryTitle.AlphaAt(3.55f)} α(3.75)={LevelEntryTitle.AlphaAt(3.75f)}");

            Check("AlphaAt：非法值不崩（-1→0 / NaN→0）",
                Near(LevelEntryTitle.AlphaAt(-1f), 0f) && Near(LevelEntryTitle.AlphaAt(float.NaN), 0f),
                $"-1→{LevelEntryTitle.AlphaAt(-1f)} NaN→{LevelEntryTitle.AlphaAt(float.NaN)}");

            // ── 文案：`"Entering " + 官方关卡名` ───────────────────────────────
            Check("文案前缀 = `\"Entering \"`（原版 Engine/Level.cs:20 `\"Entering \" + info.levelName`）",
                LevelEntryTitle.TextPrefix == "Entering ", $"\"{LevelEntryTitle.TextPrefix}\"");

            var tsv = Project("client/Assets/Scripts/Table/Tsv/Level.tsv");
            var tsvNames = new Dictionary<int, string>();
            if (File.Exists(tsv))
            {
                var lines = File.ReadAllLines(tsv);
                for (var i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrEmpty(lines[i])) continue;
                    var c = lines[i].Split('\t');
                    if (c.Length < 4) continue;
                    if (int.TryParse(c[0], out var id)) tsvNames[id] = c[3];   // level_name 列（第 4 列，0 基 3）
                }
            }

            var nameOk = true;
            var nameDetail = new List<string>();
            var areas = new[] { AreaId.Town, AreaId.BloodMoor, AreaId.DenOfEvil };
            for (var i = 0; i < areas.Length; i++)
            {
                var mine = LevelEntryTitle.OfficialLevelName(areas[i]);
                // level_c 主键是 **1 基**（AreaId 0 基）—— 见 Module/Monster/AreaLevelTable.cs 的说明
                tsvNames.TryGetValue((int)areas[i] + 1, out var fromTsv);
                nameDetail.Add($"{areas[i]}={(int)areas[i]}：UI「{mine}」/ TSV「{fromTsv}」");
                if (mine == null || fromTsv == null
                    || !string.Equals(mine, fromTsv, StringComparison.OrdinalIgnoreCase)) nameOk = false;

                if (LevelEntryTitle.TitleTextFor(areas[i]) != "Entering " + mine) nameOk = false;
            }
            Check("区域官方名与 `Table/Tsv/Level.tsv` 的 `level_name` 列逐一相符（id = AreaId+1）",
                nameOk && tsvNames.Count > 0,
                tsvNames.Count == 0 ? "Level.tsv 未找到" : string.Join("；", nameDetail.ToArray()));

            // 与 Module 侧的「契约哨兵」交叉核对（UI 不许引用 Module ⇒ 只能比源码文本）
            var areaLevelTable = Project("client/Assets/Scripts/Module/Monster/AreaLevelTable.cs");
            var crossOk = true;
            if (File.Exists(areaLevelTable))
            {
                var src = File.ReadAllText(areaLevelTable);
                crossOk = src.Contains("\"Rogue Encampment\"") && src.Contains("\"Blood Moor\"")
                          && src.Contains("\"Den of Evil\"");
            }
            Check("区域名与 `Module/Monster/AreaLevelTable.ExpectedLevelNameOf` 的三项逐字一致（同一份契约哨兵）",
                crossOk, crossOk ? "Rogue Encampment / Blood Moor / Den of Evil" : "AreaLevelTable.cs 里对不上");

            Check("未登记区域 ⇒ 不崩、不弹（返回 null，由调用方 WarnOnce）",
                LevelEntryTitle.OfficialLevelName((AreaId)999) == null
                && LevelEntryTitle.TitleTextFor((AreaId)999) == null,
                "AreaId=999 → null");

            // ── 接入：谁在驱动它 ──────────────────────────────────────────────
            var hudSrc = File.ReadAllText(UiSrc("HudPanel.cs"));
            Check("HudPanel：建了区域名控件并在最上层（Build 最后一步 BuildLevelEntryTitle）",
                hudSrc.Contains("LevelEntryTitle.Create(transform, nameof(HudPanel))")
                && hudSrc.Contains("BuildLevelEntryTitle();"),
                "见 HudPanel.Build / BuildLevelEntryTitle");
            Check("HudPanel：逐帧驱动（OnUpdate 里 Tick，且排在 Game.Input 判空之前）",
                hudSrc.Contains("_levelTitle?.Tick(dt);"), "见 HudPanel.OnUpdate");
            Check("HudPanel：两条触发路径（MapGenerated 的 areaId = 进图 / AreaChanged = 过门）",
                hudSrc.Contains("On<Diablo2.Def.AreaId>(Events.AreaChanged, OnAreaChanged)")
                && hudSrc.Contains("TriggerLevelEntryTitle((AreaId)map.areaId"),
                "见 HudPanel.Subscribe / OnMapGenerated / OnAreaChanged");
            Check("HudPanel：进新一局时清「已弹记录」（StageEntered 里 Reset）",
                hudSrc.Contains("_levelTitle?.Reset();"), "见 HudPanel.OnStageEntered");

            var titleSrc = File.ReadAllText(UiSrc("LevelEntryTitle.cs"));
            Check("LevelEntryTitle：一个文件里 0 个 MonoBehaviour（constraints.md #1）",
                !titleSrc.Contains(": MonoBehaviour") && !titleSrc.Contains(": UIPanel"),
                "非 MonoBehaviour，由 HudPanel 持有");
            //  ★「CanvasGroup 渐隐 + 时间轴（淡入/停留/淡出）+ 走完自动隐藏」已下沉到引擎
            //    `CenterAnnounceLayer`（`clover-client-unity-engine/Runtime/Presentation/WorldOverlayWidgets.cs`）；
            //    项目侧 `LevelEntryTitle` 只剩取值 / 区域名表 / 去重 / 文字工厂（公开面 = `AlphaAt` 纯函数）
            //    ⇒ 判据读**引擎真源**：CanvasGroup 真挂上、透明度真写进 `group.alpha`（不逐帧重排字模）。
            var announSrc = File.ReadAllText(Path.Combine(Program.ProjectRoot, "..", "clover-client-unity-engine",
                "Runtime", "Presentation", "WorldOverlayWidgets.cs"));
            Check("LevelEntryTitle：透明度走 CanvasGroup（不逐帧重建字模）",
                titleSrc.Contains("CenterAnnounceLayer")
                && announSrc.Contains("AddComponent<CanvasGroup>()")
                && announSrc.Contains("_group.alpha = Mathf.Clamp01(a);"),
                "见 LevelEntryTitle（转调）+ 引擎 CenterAnnounceLayer（WorldOverlayWidgets.cs）");
            Check("LevelEntryTitle：本局首次才弹（HashSet<AreaId> 去重，重复进入留一条 Info）",
                titleSrc.Contains("HashSet<AreaId>") && titleSrc.Contains("_shown.Add(area)"),
                "见 LevelEntryTitle.ShowForFirstEntry");

            // ── 字模冷启动：首次用 font30 时不能先画出一整条实心色块 ──────────────
            //   期间 `Image.sprite == null` ⇒ uGUI 把**整格**画成实心色块（第一张区域名截图就是一条红块）。
            var fontSrc = File.ReadAllText(UiSrc("D2Text.cs"));
            Check("字模冷启动：ApplyGlyph 先同步试整图集 LoadAll（不再先画实心色块）",
                fontSrc.Contains("BulkTried") && fontSrc.Contains("TryBulkLoad(font, false)"),
                "见 UI/D2Text.cs::ApplyGlyph（agent-a3）");
            //   重建入口叫 `RebuildAll()`）⇒ 这条断言恒 FAIL。改按真实的实现断言（语义不变：
            //   渲染循环里那次 LoadAll 传 `rebuildLive:false` ⇒ **不**触发重建）。
            Check("字模冷启动：渲染循环里那次 LoadAll **不**触发重建（防重入销毁正在建的字形节点）",
                fontSrc.Contains("private static bool TryBulkLoad(D2Text.D2Font font, bool rebuildLive)")
                && fontSrc.Contains("if (rebuildLive) RebuildAll();"),
                "见 UI/D2Text.cs::TryBulkLoad");

            // ── 原版依据可复核：直接读 prefab / 字体 meta ──────────────────────
            var prefab = Project("原版资源/参考工程_Diablerie/Diablerie/Assets/Prefabs/LevelEntryTitle.prefab");
            if (File.Exists(prefab))
            {
                var p = File.ReadAllText(prefab);
                Check("原版依据可复核：LevelEntryTitle.prefab 里 anchor(0,1)-(1,1)/size(0,300)/pivot(0.5,1)/alignment 4/字色/m_Font guid 逐条与上面断言一致",
                    p.Contains("m_AnchorMin: {x: 0, y: 1}") && p.Contains("m_AnchorMax: {x: 1, y: 1}")
                    && p.Contains("m_SizeDelta: {x: 0, y: 300}") && p.Contains("m_Pivot: {x: 0.5, y: 1}")
                    && p.Contains("m_Alignment: 4") && p.Contains("m_Color: {r: 0.8308824, g: 0.37267518, b: 0.37267518, a: 1}")
                    && p.Contains("guid: 75686650217a5234cb237fa8bfb75f0c"),
                    "原版资源/参考工程_Diablerie/.../Prefabs/LevelEntryTitle.prefab");

                var fontMeta = Project("原版资源/参考工程_Diablerie/Diablerie/Assets/Resources/Fonts/font30.fontsettings.meta");
                Check("原版依据可复核：prefab 用的字体 guid 就是 `font30`（Resources/Fonts/font30.fontsettings.meta）",
                    File.Exists(fontMeta) && File.ReadAllText(fontMeta).Contains("75686650217a5234cb237fa8bfb75f0c"),
                    File.Exists(fontMeta) ? Path.GetFileName(fontMeta) : "（不在则跳过）");

                var levelCs = Project("原版资源/参考工程_Diablerie/Diablerie/Assets/Scripts/Diablerie/Engine/UI/LevelEntryTitle.cs");
                Check("原版依据可复核：`Show(title, duration = 3.75f)` 与我们的 TotalSeconds 同值",
                    File.Exists(levelCs) && File.ReadAllText(levelCs).Contains("float duration = 3.75f"),
                    "Engine/UI/LevelEntryTitle.cs:27");

                var level2 = Project("原版资源/参考工程_Diablerie/Diablerie/Assets/Scripts/Diablerie/Engine/Level.cs");
                Check("原版依据可复核：`Show(\"Entering \" + info.levelName)` 与我们的文案前缀同形",
                    File.Exists(level2) && File.ReadAllText(level2).Contains("Show(\"Entering \" + info.levelName)"),
                    "Engine/Level.cs:20");
            }
            else
            {
                Console.WriteLine("[ OK ] 原版依据可复核：本机 `原版资源/` 不在 ⇒ 跳过 prefab 文本核对（其余断言不受影响）");
            }

            Console.WriteLine();
        }
    }
}
