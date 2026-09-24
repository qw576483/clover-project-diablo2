// ═══════════════════════════════════════════════════════════════════════════
//  V6Check.cs（uicheck 宿主的一个检查节，片 V6 新增）
//
//  判什么（V5 实机裁定暴露的 6 条「代码改了但表现仍不对」，逐条留一条**离线**判据）：
//   ① **对话框几何**：`NpcDialogPanel` 的常量推出的「石框矩形 / 名字行矩形 / 正文矩形」
//      必须**二维包含**（这是 V5「标题与正文画在石框外」的判据；V6 的实机 dump 同口径）。
//   ①b **降级路径几何**：`D2Label.BuildFallback` 不许再对已 `Stretch` 的框再设 `sizeDelta`
//      （那会把兜底文本撑到框外半屏 —— 实机证据见 V6 报告）。
//   ①c **字模在途不画**：`D2Label.Render` 在 chi 字模**在途**时必须直接返回，
//      **不许**用系统 TTF 顶上（那是 V5 图 03 里"跑出框的字"的另一半根因）。
//   ①d **全项目字模降级**：`D2Text.EnsureChi` 在 `Game.Res == null` 时**不许**调
//      `OnAtlasFailure`（那是"一进 Play 就把全项目文字切成系统字体"的静态开关；
//      实测 11:57/12:04/12:54/13:05/13:12 五局五次都命中）。
//   ③ **怪物悬停字号**：`EntityTooltip` 的两条 `D2Label.Create` 必须**显式给字号**
//      （`(int)UiLayoutGame.FontPx16`），否则按原版 px 1:1 画 ⇒ 只有其它 UI 文字的 ~45%
//      （实机 `v5_14` 的"散落小黄字"）。
//   ④ **商店关闭**：`ShopPanel.OnClose` 里**不许**再 `Game.UI.Open`（"关了又被重开"）；
//      `HudPanel` 里打开商店的唯一入口是 `OnShopOpen`（订阅 `Events.ShopOpen`）。
//   ⑤b **automap 注入集合渲染**（★ U46，本轮 S1）：集合由**判据自己造**（不经过 `Reveal`），
//      判 空集合⇒0 图元 / 只注入一格⇒图元数==单格 blit / 确定性 / 相邻格 bbox 平移 (+8,−4)。
//   ⑤ **automap 绘制口径**：不压暗（`BackdropAlpha == 0`）+ 贴图尺寸非退化 + 绘制源非空
//      （逐格 Cel 像素表）+ 已探索 = **记忆式**（片 g2-resume 把 E23 ④ 从"本格 + 8 邻域"改成
//      半径 `MiniMapPanel.RevealRadius` 的可通行 BFS + 相邻墙轮廓；本条断言**行为**：
//      不穿墙 / 同屏覆盖 / 墙轮廓 / 记忆单调 / 每个有 cel 的已探索格 ≥1 图元）。
//   ⑥ **拖拽目标格高亮**：`DropHighlightColor.a > 0` + `OnDrag` 里命中格时**确实**置 active
//      （且"没命中"这一支要点名留痕，不再静默 —— V5 就是查不出"没命中"还是"没画"）。
//
//  为什么能离线判：① 是常量算术；①b/①c/①d/③/④/⑥ 是对**产品源码**的字符级断言
//  （本宿主既有的同类做法：P5Check / W3GameCheck 都用"声明形状命中数"判源码口径）；
//  ③ 还带一条纯函数判据（`D2Text.ScaleFor`）。
//  ⚠️ 源码断言的口径：只判"**必须存在的写法**"与"**必须消失的写法**"，不判风格。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Diablo2.Core;
using Diablo2.UI;
using UnityEngine;

namespace Uicheck
{
    /// <summary>片 V6：6 条实机缺陷的离线判据（见文件头）。</summary>
    internal static class V6Check
    {
        private static void Check(string what, bool ok, string detail) => Program.Check(what, ok, detail);

        private static string Ui(string file)
        {
            var p = Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts", "UI", file);
            return File.Exists(p) ? File.ReadAllText(p) : string.Empty;
        }

        /// <summary>
        /// 去掉注释（源码断言不该被注释里的示例写法骗到）。
        /// <para>⚠️ 实现口径（V6 实测踩过）：**逐行**丢注释行 + 去行尾注释，⛔ 不用
        /// `/*…*/` 正则整段匹配 —— 文件里只要有一处"字符串或注释里出现的 `/*`"，
        /// 那个正则就会把**后面一大片代码**当成注释吃掉（实测：`D2Text.cs` 的注释被吞掉 82%，
        /// 连 `RetryDeferred` / `OnAtlasFailure` 都被吞 ⇒ 断言假 FAIL）。</para>
        /// </summary>
        private static string Strip(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var sb = new System.Text.StringBuilder(src.Length);
            foreach (var rawLine in src.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var t = rawLine.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("/*", StringComparison.Ordinal)
                    || t.StartsWith("*", StringComparison.Ordinal) || t.StartsWith("*/", StringComparison.Ordinal))
                    continue;                                  // 整行注释
                var i = rawLine.IndexOf("//", StringComparison.Ordinal);
                sb.Append(i >= 0 ? rawLine.Substring(0, i) : rawLine).Append('\n');
            }
            return sb.ToString();
        }

        public static void Run()
        {
            Console.WriteLine("── (V6) 6 条实机缺陷的离线判据（对话框几何 / 字模降级 / 悬停字号 / 商店关闭 / automap / 拖拽高亮）──");
            CheckDialogContainment();
            CheckFallbackRect();
            CheckBitmapInTransit();
            CheckNoGlobalDowngrade();
            CheckEntityTooltipFont();
            CheckShopClosePath();
            CheckAutomapDraw();
            CheckDropHighlight();
            Console.WriteLine();

        }

        // ── ① 对话框：名字/正文矩形必须落在石框矩形内（二维包含）────────────────
        private static void CheckDialogContainment()
        {
            var artW = NpcDialogPanel.DialogArtSize.x;
            var artH = NpcDialogPanel.DialogArtSize.y;
            var fL = -artW * 0.5f; var fR = artW * 0.5f;
            var fB = NpcDialogPanel.FrameY - artH * 0.5f; var fT = NpcDialogPanel.FrameY + artH * 0.5f;

            var cL = NpcDialogPanel.ContentCx - NpcDialogPanel.ContentW * 0.5f;
            var cR = NpcDialogPanel.ContentCx + NpcDialogPanel.ContentW * 0.5f;

            var spkL = cL; var spkR = cR;
            var spkTop = NpcDialogPanel.NameY + NpcDialogPanel.NameH * 0.5f;
            var spkBot = NpcDialogPanel.NameY - NpcDialogPanel.NameH * 0.5f;

            var bodyTop = NpcDialogPanel.BodyY + NpcDialogPanel.BodyH * 0.5f;
            var bodyBot = NpcDialogPanel.BodyY - NpcDialogPanel.BodyH * 0.5f;

            var inside = spkL >= fL && spkR <= fR && spkTop <= fT && spkBot >= fB
                         && cL >= fL && cR <= fR && bodyTop <= fT && bodyBot >= fB;
            Check("① 对话框：名字行与正文行的矩形都**包含于**石框矩形内（常量算术；V6 实机 dump 同口径）",
                inside,
                $"frame=({fL:0.#},{fB:0.#})..({fR:0.#},{fT:0.#}) speaker=({spkL:0.#},{spkBot:0.#})..({spkR:0.#},{spkTop:0.#})"
                + $" body=({cL:0.#},{bodyBot:0.#})..({cR:0.#},{bodyTop:0.#})");

            // 正文框至少装得下 2 行（最长一条台词折行后 2 行；V6 实机图里正是 2 行）
            Check("① 对话框：正文框高 ≥ 2 × 正文字号（长台词能在框内折行，不溢出石框）",
                NpcDialogPanel.BodyH >= 2f * NpcDialogPanel.BodyFont,
                $"bodyH={NpcDialogPanel.BodyH:0.#} bodyFont={NpcDialogPanel.BodyFont}");

            // 选项列在两雕槽之间（S6 不变式；V6 实机图里 3 个选项就落在这条中央列）
            var optL = NpcDialogPanel.OptionX - NpcDialogPanel.OptionSize.x * 0.5f;
            var optR = NpcDialogPanel.OptionX + NpcDialogPanel.OptionSize.x * 0.5f;
            var midL = NpcDialogPanel.Cx(NpcDialogPanel.SlotCellLeftX1);
            var midR = NpcDialogPanel.Cx(NpcDialogPanel.SlotCellRightX0);
            Check("① 对话框：选项列仍落在两个 34×34 雕花方槽之间的中央列（S6 不变式）",
                optL >= midL - 0.5f && optR <= midR + 0.5f,
                $"option=({optL:0.#}..{optR:0.#}) midColumn=({midL:0.#}..{midR:0.#})");
        }

        // ── ①b 兜底文本不许再撑大自己的框（那就是"字跑到石框外"的根因）──────────
        private static void CheckFallbackRect()
        {
            var raw = Ui("D2Text.cs");
            var src = Strip(raw);
            var bad = src.Contains("_fallback.rectTransform.sizeDelta = size");
            Check("①b D2Text.BuildFallback **不再**对已 Stretch 的兜底文本再设 sizeDelta（否则文本被撑到框外半屏）",
                !bad,
                "path=" + Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts", "UI", "D2Text.cs")
                + " rawLen=" + raw.Length + " strippedLen=" + src.Length
                + (bad ? " 命中 `_fallback.rectTransform.sizeDelta = size`" : " 0 命中"));
        }

        // ── ①c 字模在途：不画（不许用系统 TTF 顶）──────────────────────────────
        private static void CheckBitmapInTransit()
        {
            var src = Strip(Ui("D2Text.cs"));
            var early = src.Contains("if (!D2Text.ChiReady(_font))") && src.Contains("_fallback = null;");
            Check("①c D2Label.Render 在 chi 字模**在途**时直接返回（不拿系统 TTF 顶上；配合 ①d 后无 TTF 帧）",
                early,
                early ? "命中 ChiReady 早退 + 清空 _fallback" : "未命中 `if (!D2Text.ChiReady(_font))` 早退块");
        }

        // ── ①d 全项目字模降级：Game.Res 未就绪时只延迟、不降级 ──────────────────
        private static void CheckNoGlobalDowngrade()
        {
            var src = Strip(Ui("D2Text.cs"));
            var deferred = src.Contains("RetryDeferred") && src.Contains("_deferred");
            Check("①d D2Text：`Game.Res == null` 时字模加载**延后**（`RetryDeferred`）而不是永久降级",
                deferred,
                deferred ? "命中 _deferred / RetryDeferred" : "0 命中（会退化成 MarkBitmapUnavailable 永久降级）");

            // EnsureChi 的 `Game.Res == null` 分支里不许出现 OnAtlasFailure
            var m = Regex.Match(src, @"public static void EnsureChi\(D2Font font\)(.*?)slot\.Loading = true;",
                RegexOptions.Singleline);
            var body = m.Success ? m.Groups[1].Value : string.Empty;
            var bad = body.Contains("OnAtlasFailure");
            Check("①d D2Text.EnsureChi 的 `Game.Res == null` 分支**不再**调 OnAtlasFailure（该静态开关一局之内不恢复）",
                !bad && body.Length > 0,
                bad ? "分支里仍有 OnAtlasFailure" : ("分支长度 " + body.Length + " 字符，0 命中 OnAtlasFailure"));

            // 自愈入口必须被每帧调用（否则延后的标签永远空着）
            var mirror = Strip(Ui("D2TextMirror.cs"));
            Check("①d D2TextMirror.Update 每帧调 D2Text.RetryDeferred()（延后的字模自愈入口）",
                mirror.Contains("D2Text.RetryDeferred()"),
                mirror.Contains("D2Text.RetryDeferred()") ? "命中" : "0 命中");
        }

        // ── ③ 怪物悬停：字号必须显式给（否则按原版 px 1:1 = 其它 UI 的 ~45%）────
        //   ★ 片 u44（契约 C4）：承载它的文件从 `UI/EntityTooltip.cs`（头顶 tooltip，已删）
        //     换成 `UI/EnemyBarView.cs`（顶部条 + NPC 名字牌）。③ 这条缺陷的**回归保护**随载体迁移，
        //     口径不变：两条 `D2Label.Create` 必须显式给字号；只是第二条"框单位"改成新载体的形状
        //     （名字牌尺寸 = 文本实测 × 画布字号 + padding×K ⇒ 同样不许混进世界格 px）。
        private static void CheckEntityTooltipFont()
        {
            var src = Strip(Ui("EnemyBarView.cs"));
            var hasFontPx = src.Contains("(int)UiLayoutGame.FontPx16");
            Check("③ EnemyBarView 的两条 D2Label.Create 传了显式字号 `(int)UiLayoutGame.FontPx16`",
                hasFontPx, hasFontPx ? "命中 (int)UiLayoutGame.FontPx16" : "0 命中（会按原版 px 1:1 画成小字块）");

            var boxCanvas = src.Contains("* UiLayoutGame.K") && !src.Contains("GameConst.IsoTilePxW");
            Check("③ EnemyBarView 的排版/牌子尺寸用**画布单位**（原版 px / 实测字宽 × K），不混用世界格 px",
                boxCanvas, boxCanvas ? "命中 ×UiLayoutGame.K 且 0 处 GameConst.IsoTilePxW"
                                     : "未命中（排版框与世界单位混用 ⇒ 换行/黑底尺寸错）");

            // 纯函数判据：fontSize = 0 就是"原版 px 1:1"（这正是缺陷的量级来源）
            var scale0 = D2Text.ScaleFor(0, true, D2Text.D2Font.Font16);
            var scaleUi = D2Text.ScaleFor((int)UiLayoutGame.FontPx16, true, D2Text.D2Font.Font16);
            Check("③ `D2Text.ScaleFor(0,…) == 1`（0 = 原版 px 1:1）而 `(int)FontPx16` 的缩放 > 2（差 2 倍以上）",
                Mathf.Approximately(scale0, 1f) && scaleUi > 2f,
                $"scale(0)={scale0:0.###} scale((int)FontPx16)={scaleUi:0.###}");
        }

        // ── ④ 商店：关掉之后不许被重开 ────────────────────────────────────────
        private static void CheckShopClosePath()
        {
            var shop = Strip(Ui("ShopPanel.cs"));
            var hud = Strip(Ui("HudPanel.cs"));

            var m = Regex.Match(shop, @"public override void OnClose\(\)(.*?)\n        \}", RegexOptions.Singleline);
            var onClose = m.Success ? m.Groups[1].Value : string.Empty;
            Check("④ ShopPanel.OnClose 里没有 Game.UI.Open（「关了又被重开」的路径不存在）",
                onClose.Length > 0 && !onClose.Contains("Game.UI.Open"),
                onClose.Length == 0 ? "OnClose 未取到（正则失配 ⇒ 需人工核）"
                    : (onClose.Contains("Game.UI.Open") ? "命中 Game.UI.Open" : "0 命中"));

            var count = Regex.Matches(hud, @"Game\.UI\.Open<ShopPanel>").Count;
            Check("④ HudPanel 里打开商店的入口只有 1 处（订阅 Events.ShopOpen 的 OnShopOpen）",
                count == 1, $"Game.UI.Open<ShopPanel> 命中数={count}");

            var closePath = shop.Contains("Game.Event.Emit(Events.ShopClose)") && shop.Contains("Game.UI.Close<ShopPanel>()");
            Check("④ 关闭按钮路径 = 发 Events.ShopClose + Close<ShopPanel>()（两条都在）",
                closePath, closePath ? "命中" : "0 命中");
        }

        // ── ⑤ automap：不压暗 + 贴图非退化 + 绘制源非空 + 揭示口径 = E23 ④ ──────
        private static void CheckAutomapDraw()
        {
            Check("⑤ automap 不压暗（BackdropAlpha == 0；U4 起建都不建那层）",
                Mathf.Approximately(MiniMapPanel.BackdropAlpha, 0f),
                $"BackdropAlpha={MiniMapPanel.BackdropAlpha}");

            // 56×40（Town）贴图尺寸 = (w+h-2)*stepX + W 与 (w+h-2)*stepY + H
            var stepX = AutoMapCel.W / 2;
            var stepY = AutoMapCel.W / 4;
            var texW = (56 + 40 - 2) * stepX + AutoMapCel.W;
            var texH = (56 + 40 - 2) * stepY + AutoMapCel.H;
            Check("⑤ automap 贴图尺寸对 56×40 的图非退化（> 0）",
                texW > 0 && texH > 0, $"tex={texW}x{texH}（V6 实机 dump：768×408 原版px ⇒ 1382.4×734.4 画布px）");

            Check("⑤ automap 绘制源非空（逐格 Cel 像素表已生成）",
                AutoMapCel.CelPixels != null && AutoMapCel.CelPixels.Count > 0,
                $"CelPixels={AutoMapCel.CelPixels?.Count ?? -1} 格 = {AutoMapCel.W}x{AutoMapCel.H}");

            // ★ 片 g2-resume：口径由「本格 + 8 邻域」改为**记忆式已探索**（E23 ④ 内容本轮已更新）
            //   ⇒ 旧断言（grep 源码里的 ±1 双循环）判的是**已经删掉的旧写法**（判结果不判过程），
            //     换成**行为断言**：直接调产品侧纯函数 `MiniMapPanel.Reveal` / `CountDrawn`
            //     判 不穿墙 / 同屏覆盖 / 墙轮廓 / 记忆单调 / 图元不丢。测试文件头「口径」一节。
            CheckRevealRubric();
        }

        /// <summary>
        /// ⑤ 已探索口径的**行为**断言（纯函数，不依赖 Unity 运行时；见文件头 ⑤）。
        /// 合成地图 32×24：`x == wallX` 一整列阻挡格把左右隔开 ⇒ 可以真的判"不穿墙"。
        /// </summary>
        private static void CheckRevealRubric()
        {
            const int w = 32;
            const int h = 24;
            const int wallX = 16;
            var map = new Diablo2.Def.MinimapArgs { width = w, height = h, seed = 424242 };
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    map.tiles.Add((byte)(x == wallX
                        ? Diablo2.Def.MinimapArgs.TileBlocking
                        : Diablo2.Def.MinimapArgs.TileWalkable));
                }
            }

            var px = wallX - 2;                        // 玩家在墙左侧 2 格
            var py = h / 2;
            var r = MiniMapPanel.RevealRadius;
            var explored = new bool[w * h];
            MiniMapPanel.Reveal(map, explored, px, py);
            var n1 = MiniMapPanel.CountExplored(explored);

            // ① 同屏覆盖：曼哈顿 ≤ R、且在墙**左侧**（BFS 真能走到的）地面格必须全部已探索
            var want = 0;
            var missing = 0;
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) + Math.Abs(dy) > r) continue;
                    var x = px + dx;
                    var y = py + dy;
                    if (x < 0 || y < 0 || x >= w || y >= h) continue;
                    if (x >= wallX) continue;           // 墙及其右侧由 ②/③ 判
                    want++;
                    if (!explored[y * w + x]) missing++;
                }
            }
            Check($"⑤ 揭示覆盖同屏（曼哈顿 ≤ RevealRadius={r} 的可达地面格全部已探索）",
                want > 0 && missing == 0, $"应有 {want} 格，缺 {missing} 格（首次揭示共 {n1} 格）");

            // ② 不穿墙：墙右侧（x > wallX）一格都不许被揭示
            var leak = 0;
            for (var y = 0; y < h; y++)
            {
                for (var x = wallX + 1; x < w; x++) if (explored[y * w + x]) leak++;
            }
            Check("⑤ 揭示不穿墙（墙右侧 0 格被揭示 —— 旧 ±1 邻域口径在墙角会漏）",
                leak == 0, $"墙右侧被揭示 {leak} 格；墙本身（x={wallX}）已揭示格数="
                           + CountColumn(explored, w, h, wallX));

            // ③ 墙轮廓：与已揭示地面相邻的阻挡格必须一并揭示（原版"地板先、墙后"的轮廓感）
            Check("⑤ 墙轮廓：玩家正对的墙格已揭示（地板先、墙后）",
                explored[py * w + wallX], $"({wallX},{py}) = {explored[py * w + wallX]}");

            // ④ 记忆单调：再走一段，先前已探索的格**只增不减**
            var snapshot = (bool[])explored.Clone();
            MiniMapPanel.Reveal(map, explored, px - 3, py - 2);
            var n2 = MiniMapPanel.CountExplored(explored);
            var lost = 0;
            for (var i = 0; i < snapshot.Length; i++) if (snapshot[i] && !explored[i]) lost++;
            Check("⑤ 记忆式已探索：走过即记忆（第二轮揭示后 0 格丢失，总数不减）",
                lost == 0 && n2 >= n1, $"n1={n1} → n2={n2}，丢失 {lost} 格");

            // ⑤ 图元：给合成图铺**真实存在**的 cel（取 `AutoMapCel.CelPixels` 的两帧）
            //    ⇒ 「有 cel 的已探索格」必须每格写出 ≥1 图元（静默丢格 = 红）
            var celFloor = -1;
            var celWall = -1;
            foreach (var kv in AutoMapCel.CelPixels)
            {
                if (kv.Value == null || kv.Value.Length == 0) continue;
                if (celFloor < 0) { celFloor = kv.Key; continue; }
                if (celWall < 0) { celWall = kv.Key; break; }
            }
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var blocking = x == wallX;
                    map.cels.Add((short)(blocking ? -1 : celFloor));
                    map.celsOver.Add((short)(blocking ? celWall : -1));
                }
            }

            var stepX = AutoMapCel.W / 2;
            var stepY = AutoMapCel.W / 4;
            var texW = (w + h - 2) * stepX + AutoMapCel.W;
            var texH = (w + h - 2) * stepY + AutoMapCel.H;
            int withCel, drawn, opaque;
            MiniMapPanel.CountDrawn(map, explored, texW, texH, MiniMapPanel.CreatePalette(),
                out withCel, out drawn, out opaque);
            Check("⑤ 已探索 ⇒ 图元：每个「有 cel」的已探索格都写出 ≥1 图元（不静默丢格）",
                withCel > 0 && drawn == withCel,
                $"有 cel 的已探索格={withCel}，真正画出={drawn}，不透明像素={opaque}"
                + $"（tex={texW}x{texH}，cel 底={celFloor}/墙={celWall}）");

            // ★ U46（本轮 S1）：把"已探索集合"做成**注入集合**后，渲染侧必须满足的三条
            //   （判据自己造集合 ⇒ 与"揭示口径"解耦；入口 = `MiniMapPanel.ApplyExplored` / 核心 = `RenderExplored`）
            CheckInjectedRender();
        }

        /// <summary>
        /// ★ U46：**注入集合 ⇒ 图元**（纯函数渲染侧断言；⛔ 不经过 `Reveal`，集合由判据自己造）。
        /// <para>① 空集合 ⇒ 0 格 / 0 图元；只注入 (0,0) ⇒ 图元数 == 该格 cel 单独 blit 的图元数（没有多画别的格）。</para>
        /// <para>② 确定性：同一集合渲染两次逐像素一致；超集 ⇒ drawn 格数 = 注入格数（图元只增不减）。</para>
        /// <para>③ cel 几何：相邻格 (0,0)→(1,0) 的图元 bbox 正好平移 **(+8, −4)** 纹理（列,行）
        /// —— 原版 1/10 等距步进（世界向 (x+1) ⇒ (+8,+4)；纹理行号自底向上 ⇒ 行 −4）；
        /// 出处 `.ai-tmp/test/automap-plan.md` §1.4「相邻格间距」+「锚点 = 帧左上角在格心 −(8,28)」。</para>
        /// </summary>
        private static void CheckInjectedRender()
        {
            const int w = 8;
            const int h = 8;

            var cel = -1;
            foreach (var kv in AutoMapCel.CelPixels)
            {
                if (kv.Value != null && kv.Value.Length > 0) { cel = kv.Key; break; }
            }
            if (cel < 0)
            {
                Check("U46 ① 注入集合 ⇒ 图元（需要 `AutoMapCel.CelPixels` 非空）", false,
                    "`AutoMapCel.CelPixels` 为空 ⇒ 无法判 cel 画法（生成器请重跑）");
                return;
            }

            var map = new Diablo2.Def.MinimapArgs { width = w, height = h, seed = 90210 };
            for (var i = 0; i < w * h; i++)
            {
                map.tiles.Add(Diablo2.Def.MinimapArgs.TileWalkable);
                map.cels.Add((short)cel);
                map.celsOver.Add((short)-1);
            }

            var stepX = AutoMapCel.W / 2;
            var stepY = AutoMapCel.W / 4;
            var texW = (w + h - 2) * stepX + AutoMapCel.W;
            var texH = (w + h - 2) * stepY + AutoMapCel.H;
            var palette = MiniMapPanel.CreatePalette();

            // ① 空集合 ⇒ 什么都不画（"渲染源只有这个集合"的最强口径）
            var none = new bool[w * h];
            int wc, dr, op;
            MiniMapPanel.CountDrawn(map, none, texW, texH, palette, out wc, out dr, out op);
            Check("U46 ① 空注入集合 ⇒ 0 格 / 0 图元（画法不自己造格）",
                wc == 0 && dr == 0 && op == 0, $"withCel={wc} drawn={dr} opaque={op}");

            // ① 只注入 (0,0) ⇒ 图元数必须正好等于该格 cel 单独 blit 的图元数
            var onlyA = new bool[w * h];
            onlyA[0] = true;
            var bufA = new Color32[texW * texH];
            var drawnA = MiniMapPanel.RenderExplored(bufA, texW, texH, map, onlyA, palette, out wc, out op);
            var scratch = new Color32[texW * texH];
            var single = MiniMapPanel.BlitInto(scratch, texW, texH, h, cel, 0, 0, palette);
            Check("U46 ① 只注入 (0,0) ⇒ 图元数 == 该格 cel 单独 blit 的图元数（没有多画任何别的格）",
                drawnA == 1 && op == single && single > 0,
                $"drawn={drawnA} opaque={op} singleBlit={single}（withCel={wc}）");

            // ② 确定性 + 单调
            var both = new bool[w * h];
            both[0] = true; both[1] = true;
            var bufB = new Color32[texW * texH];
            var drawnB = MiniMapPanel.RenderExplored(bufB, texW, texH, map, both, palette, out wc, out op);
            var bufA2 = new Color32[texW * texH];
            var drawnA2 = MiniMapPanel.RenderExplored(bufA2, texW, texH, map, onlyA, palette, out wc, out op);
            var same = true;
            for (var i = 0; i < bufA.Length; i++)
            {
                if (bufA[i].r == bufA2[i].r && bufA[i].g == bufA2[i].g
                    && bufA[i].b == bufA2[i].b && bufA[i].a == bufA2[i].a) continue;
                same = false; break;
            }
            Check("U46 ② 确定性：同一注入集合渲染两次逐像素一致；两格注入 ⇒ 恰好 2 格有图元",
                same && drawnA2 == drawnA && drawnB == 2,
                $"两次单格 drawn={drawnA}/{drawnA2}（逐像素一致={same}）；两格 drawn={drawnB}");

            // ③ cel 几何：bbox(1,0) − bbox(0,0) 必须正好 (+stepX, −stepY)
            //   ⚠️ bbox 必须取自**只含 (1,0) 的集合**（拿两格的并集去比，比出来是并集范围、不是平移量）
            var onlyB = new bool[w * h];
            onlyB[1] = true;
            var bufC = new Color32[texW * texH];
            var drawnC = MiniMapPanel.RenderExplored(bufC, texW, texH, map, onlyB, palette, out wc, out op);
            int ax0, ax1, ay0, ay1, bx0, bx1, by0, by1;
            Bbox(bufA, texW, texH, out ax0, out ax1, out ay0, out ay1);
            Bbox(bufC, texW, texH, out bx0, out bx1, out by0, out by1);
            Check("U46 ③ cel 几何：相邻格 (0,0)→(1,0) 的图元 bbox 正好平移 (+8, −4) 纹理（列,行）",
                drawnC == 1 && bx0 - ax0 == stepX && bx1 - ax1 == stepX
                && ay0 - by0 == stepY && ay1 - by1 == stepY,
                $"cell(0,0) bbox=({ax0},{ay0})..({ax1},{ay1})；cell(1,0) bbox=({bx0},{by0})..({bx1},{by1})；"
                + $"（只含(1,0)的集合 drawn={drawnC}）期望 Δ=(+{stepX}, −{stepY})");

            // ④ 跨片契约（判**接口形状**，不判源码字面）：`Core/Events.cs` 的 `Events.MapExplored`
            //   注释点名收方 = `UI/MiniMapPanel.ApplyExplored(IReadOnlyCollection<Vector2Int>)`
            //   ⇒ 这个接缝必须真的存在且签名一致（S2 发 / 本面板收，两边一改名就红）。
            var apply = typeof(MiniMapPanel).GetMethod("ApplyExplored",
                new[] { typeof(IReadOnlyCollection<Vector2Int>) });
            var flag = typeof(MiniMapPanel).GetProperty("ExploredInjected");
            Check("U46 ④ 跨片契约：`MiniMapPanel.ApplyExplored(IReadOnlyCollection<Vector2Int>)` + `ExploredInjected` 在位",
                apply != null && apply.ReturnType == typeof(int) && flag != null && flag.PropertyType == typeof(bool),
                $"ApplyExplored={(apply != null ? apply.ReturnType.Name : "(缺失)")}；"
                + $"ExploredInjected={(flag != null ? flag.PropertyType.Name : "(缺失)")}"
                + "（`Events.MapExplored` 的注释点名这个收方）");

            // ★ 片 automap-panel2（2026-09-24）：把「面板开着、数据非空却一笔不画」变成**可离线判的数**
            //   （前片只能靠截图目视 ⇒ 那次判定不成立：实机读数 DrawnCells=86 CellsWithCel=86
            //    OpaquePixels=1887 texOpaqueTexels=1772/313344，画了、只是稀）。两条：
            //     ⑤-新1 **不静默丢像素**：写出图元数 == 已探索格数 × cel 稀疏像素数（判「过程」：
            //           越界裁剪 / 漏帧都会让这条变红；既有 ⑤ 只判「不丢**格**」，这条判「不丢**像素**」）；
            //     ⑤-新2 **可见度比值**：不透明 texel 数 > 0（= 「真的画出了东西」）+ 比值 ≤ 数据上限
            //           （「画了数据里没有的图元」/重复绘制会变红）。
            //   ⚠️ **覆盖率下限（阈值）缺出处**：原版「一屏该被 automap 覆盖多少」在本机没有载体
            //      （`策划/基线图/原版_automap_实机截图_20260923.png` 是别的场景、别的探索量，不可当阈值）
            //      ⇒ 本条**只判 >0 与 ≤数据上限，不设下限**；要设下限必须先有出处。
            CheckNoSilentPixelLoss();
        }

        /// <summary>
        /// ★ 片 automap-panel2：**不静默丢像素** + **可见度比值**（纯函数层，与实例 `Redraw` 同一条
        /// `RenderExplored` ⇒ 判据与产品同源，不是第二份画法）。
        /// <para>为什么需要：`MiniMapPanel` 的「面板开着、数据非空」到「屏上有图」之间隔着
        /// 裁剪 / 帧缺像素 / 载体不可见三层，既有断言只覆盖「格数」这一层。</para>
        /// </summary>
        private static void CheckNoSilentPixelLoss()
        {
            const int w = 8;
            const int h = 8;

            // 取**像素最多**的 cel（用满数据集 ⇒ 避免「这一帧本来就稀」把断言变成空转）
            var cel = -1;
            var npix = 0;
            foreach (var kv in AutoMapCel.CelPixels)
            {
                if (kv.Value == null || kv.Value.Length == 0) continue;
                var n = kv.Value.Length / 3;         // 稀疏编码：每 3 字节 = 一个像素 (x, y, 调色板索引)
                if (n > npix) { npix = n; cel = kv.Key; }
            }
            if (cel < 0 || npix <= 0)
            {
                Check("⑤-新1 不静默丢像素（需要 `AutoMapCel.CelPixels` 非空）", false,
                    "`AutoMapCel.CelPixels` 为空 ⇒ 无法判像素级画法（生成器请重跑）");
                return;
            }

            var map = new Diablo2.Def.MinimapArgs { width = w, height = h, seed = 5150 };
            for (var i = 0; i < w * h; i++)
            {
                map.tiles.Add(Diablo2.Def.MinimapArgs.TileWalkable);
                map.cels.Add((short)cel);
                map.celsOver.Add((short)-1);         // 只判地面层 ⇒ 排除第二层叠加对图元数的干扰
            }

            var stepX = AutoMapCel.W / 2;
            var stepY = AutoMapCel.W / 4;
            var texW = (w + h - 2) * stepX + AutoMapCel.W;
            var texH = (w + h - 2) * stepY + AutoMapCel.H;
            var palette = MiniMapPanel.CreatePalette();

            var all = new bool[w * h];
            for (var i = 0; i < all.Length; i++) all[i] = true;

            var buf = new Color32[texW * texH];
            int wc, op;
            var drawn = MiniMapPanel.RenderExplored(buf, texW, texH, map, all, palette, out wc, out op);

            // 纹理按 (w+h-2) 步进给足 ⇒ 一格的像素都不该被裁掉 ⇒ 写出数 == Σ cel 稀疏像素数
            var expect = w * h * npix;
            Check("⑤-新1 不静默丢像素：写出图元数 == 已探索格数(" + w + "×" + h + ") × cel 稀疏像素数",
                drawn == w * h && op == expect,
                $"drawn={drawn} opaque={op} 期望={w}*{h}*{npix}={expect}"
                + $"（cel={cel}，tex={texW}x{texH}；越界裁剪会让这条变红）");

            var opaque = 0;
            for (var i = 0; i < buf.Length; i++) if (buf[i].a > 0) opaque++;
            var ratio = (double)opaque / (texW * texH);
            var upper = (double)expect / (texW * texH);
            Check("⑤-新2 可见度比值：不透明 texel > 0 且 ≤ 数据上限（下限缺出处 ⇒ 不设下限）",
                opaque > 0 && opaque <= expect,
                $"不透明 texel={opaque}/{texW * texH}（比值 {ratio:0.00000}，数据上限 {upper:0.00000}）；"
                + "⚠️ 覆盖率**下限**无出处（原版一屏覆盖多少本机无载体）⇒ 本条不设下限");

            // 退化用例：把纹理缩到装不下 ⇒ 同一条「不丢像素」判据必须变红（「能红」本身也要被判）
            var smallW = AutoMapCel.W;
            var smallBuf = new Color32[smallW * texH];
            int wc2, op2;
            var drawn2 = MiniMapPanel.RenderExplored(smallBuf, smallW, texH, map, all, palette, out wc2, out op2);
            Check("⑤-新1-退化 纹理装不下 ⇒ 不丢像素判据必须变红（op < 期望）",
                op2 < expect,
                $"缩纹理 tex={smallW}x{texH}：opaque={op2} < 期望 {expect}（drawn={drawn2}）"
                + " ⇒ 该判据确实在判「过程」（有裁剪就会红）");

            // ★ **与「覆盖率」分开的一条**：automap 有**两个独立根因**，⛔ 不许合成一个数：
            //   ① 覆盖率（画了多少格 / 多少墨）= 上面 ⑤-新1/⑤-新2 与 `DrawnCells/OpaquePixels`；
            //   ② **中心**（叠加层以谁为中心平移）= 本条。两条互不代偿：覆盖率上去 ≠ 居中对了。
            //   口径出处 = `Module/Contracts.cs` 的 `MinimapArgs.playerX/playerY` 注释「玩家所在格」
            //   ⇒ 渲染层的平移**只许**读 `_map.playerX/_map.playerY`；面板里**不许出现**出生点字段
            //   （片 automap-panel 实测：`MapModule.BuildMinimap` 曾把这两个字段填成 `SpawnPoint`，
            //    表现 = 走到别处开图，`anchoredPosition` 与"站在出生点旁"逐字节相同）。
            var src = Ui("MiniMapPanel.cs");      // ⚠️ 不脱注释：该文件**连注释**里也零 `spawn`（实测）
            var hasPx = src.Contains("_map.playerX");
            var hasPy = src.Contains("_map.playerY");
            var spawnHits = 0;
            for (var i = src.IndexOf("spawn", StringComparison.OrdinalIgnoreCase); i >= 0;
                 i = src.IndexOf("spawn", i + 1, StringComparison.OrdinalIgnoreCase)) spawnHits++;
            Check("⑤-新3 居中契约（与覆盖率**分开**判）：平移只读玩家格 `_map.playerX/Y`，面板内零「出生点」字段",
                hasPx && hasPy && spawnHits == 0,
                $"`_map.playerX` 命中={hasPx}、`_map.playerY` 命中={hasPy}；源码里 spawn 命中={spawnHits}"
                + "（居中口径 = 契约 `MinimapArgs.playerX/Y` = 玩家所在格；"
                + "出生点口径在 Map 侧 `MapModule.BuildMinimap`，⛔ 本条不判覆盖率，也不被覆盖率代偿）");
        }

        /// <summary>不透明像素的包围盒（纹理坐标：minTx / maxTx / minRow / maxRow）。</summary>
        private static void Bbox(Color32[] px, int texW, int texH,
            out int minX, out int maxX, out int minY, out int maxY)
        {
            minX = int.MaxValue; maxX = int.MinValue; minY = int.MaxValue; maxY = int.MinValue;
            for (var y = 0; y < texH; y++)
            {
                for (var x = 0; x < texW; x++)
                {
                    if (px[y * texW + x].a == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        /// <summary>某一列上已探索的格数（②的细节数字）。</summary>
        private static int CountColumn(bool[] explored, int w, int h, int x)
        {
            var n = 0;
            for (var y = 0; y < h; y++) if (explored[y * w + x]) n++;
            return n;
        }

        // ── ⑥ 拖拽目标格高亮：色非透明 + 命中即置 active + 未命中留痕 ──────────
        private static void CheckDropHighlight()
        {
            Check("⑥ 目标格高亮色 alpha > 0（可见）",
                InventoryPanel.DropHighlightColor.a > 0f,
                $"color={InventoryPanel.DropHighlightColor}");

            var src = Strip(Ui("InventoryPanel.cs"));
            var on = Regex.Match(src, @"public void OnDrag\(PointerEventData eventData\)(.*?)\n        \}",
                RegexOptions.Singleline);
            var body = on.Success ? on.Groups[1].Value : string.Empty;
            var setsActive = body.Contains("_dropHighlight.gameObject.SetActive(true)");
            Check("⑥ OnDrag 命中格时置 `_dropHighlight` active",
                setsActive, setsActive ? "命中 SetActive(true)" : "0 命中");

            var logsMiss = body.Contains("拖拽中指针下没有背包格");
            Check("⑥ OnDrag 的「没命中格」分支**留痕**（不再静默：V5 当年查不出「没命中」还是「没画」）",
                logsMiss, logsMiss ? "命中点名日志" : "0 命中（静默分支）");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  FontScaleCheck（片 font-scale 新增；**放在本文件内**的理由见下）
    //
    //  判什么 —— 用户连报「文字太小」的**第 2 层根因**：`D2Label.Create` 调用**没给字号**
    //  （`fontSize` 默认 0 = 按原版 px 1:1 画 ⇒ chi 格只有 16 画布px，是本画布应有字号的
    //   16/28.8 ≈ 55%）。V6 报告 §4-② 机械列出的 6 处就在其中。
    //
    //   ① **全仓零处漏字号**：`client/Assets/Scripts/UI/**` 下**每一处** `D2Label.Create(...)`
    //      调用都必须显式传第 9 个实参 `fontSize`。⛔ 不许靠"我以为只有 6 处"—— 本检查**机械扫
    //      全部调用点**（含将来新增的）。唯一允许的例外 = 放大由**根节点** `localScale = K`
    //      承担的两处（`UiLayoutFlow.cs` / `LevelEntryTitle.cs`）；例外必须**同域内**出现
    //      `localScale` 的 ×`K`，否则照样 FAIL（例外不可滥用）。
    //   ② **字号只能来自唯一出处**：非例外处的 fontSize 必须是 `(int)UiLayoutGame.FontPx16/24/30/42`，
    //      或本文件内的局部变量（如 `var fontPx = (int)UiLayoutGame.FontPx16;`，`EntityTooltip` 的写法）。
    //   ③ **出处本身成立**（**跑真值**，不是看字符串）：`UiLayoutGame.FontPx16 == LineSpacing(Font16) * K`，
    //      且 `LineSpacing` = 原版字模行距 16/24/30/42 ⇒ 28.8/43.2/54/75.6。
    //      这一条防"两边抄成同一个错数"（只比对字面量文本会让假绿通过）。
    //   ④ **不许双重放大**：`D2Label.Create` 的 `fontSize` 默认值必须是 `0`（= 1:1）—— ① 的前提；
    //      且上面的两处例外**必须仍然没传** fontSize（传了 = 54 → 97.2 的双重放大，
    //      见 V6 报告对 `UiLayoutFlow.cs:1806` 的警告）。
    //   ⑤ **框单位**：`GroundItemLabelView.LabelSize` 必须换算到**画布 px**（×`K`）——
    //      与 V6 给 `EntityTooltip` 的修法同一口径；世界格 px 混进画布单位会让文字**提前折行**
    //      （"字变大了但框没变大"这一类缺陷）。
    //
    //  为什么能离线判：①②④⑤ 是对**产品源码**的字符级断言（本宿主既有同类做法：V6Check /
    //  W3GameCheck / P5Check）；③ 是对**已编译常量与纯函数**的真值断言。
    //  ⚠️ 口径：只判"**必须存在的写法**"与"**必须消失的写法**"，不判风格、不判字号审美。
    //
    //  ⚠️ 为什么这个类写在 `V6Check.cs` 里（而不是自己的 .cs）：片 font-scale 实测
    //  **对 `Uicheck.csproj` 的写入不落盘**（两次 `replace_in_file` 都报成功，但磁盘上的
    //  `Uicheck.csproj` mtime 一直是 13:49:06、内容里没有新增的 `<Compile Include>`，
    //  `dotnet run` 因此报 `Program.cs(161,13): error CS0103: FontScaleCheck`）。
    //  本宿主是**白名单**工程（`EnableDefaultCompileItems=false`）⇒ 新 .cs 必须登记进 csproj。
    //  为了不依赖那个会被复位的文件，本片把检查类放进**已在清单里**的 V6Check.cs（同一命名空间）。
    //  ⇒ 下一步若有人能稳定改 csproj，可把本类原样挪回 `FontScaleCheck.cs` 并删掉这一节。
    // ═══════════════════════════════════════════════════════════════════════
    /// <summary>片 font-scale：`D2Label.Create` 字号唯一出处的离线判据（见上）。</summary>
    internal static class FontScaleCheck
    {
        private static void Check(string what, bool ok, string detail) => Program.Check(what, ok, detail);

        private const string CallName = "D2Label.Create";

        /// <summary>允许"不传 fontSize"的两处：放大由根节点 `localScale = K` 承担（见 ①）。</summary>
        private static readonly string[] ScaleByRootFiles = { "UiLayoutFlow.cs", "LevelEntryTitle.cs" };

        /// <summary>一个调用点（行号 = **原文件**行号：注释被等长掩码 ⇒ 行号可直接写进报告）。</summary>
        private sealed class Site
        {
            public string File;                                  // 文件名（不含目录）
            public string Path;                                  // 绝对路径
            public int Line;                                     // 1 起
            public System.Collections.Generic.List<string> Args;  // 顶层实参
            public string Text;                                  // 掩码后的整段调用文本
        }

        private static string UiDir
            => Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts", "UI");

        public static void Run()
        {
            Console.WriteLine("── (font-scale) `D2Label.Create` 字号唯一出处的离线判据（全仓零处漏字号）──");

            SelfTest();                 // ★ 先自检判据本身（已知正确 PASS / 已知错误 FAIL）
            var sites = Scan();
            var offenders = new System.Collections.Generic.List<string>();
            var exceptions = new System.Collections.Generic.List<Site>();
            var noSourceList = new System.Collections.Generic.List<string>();

            foreach (var s in sites)
            {
                var hasFontSize = s.Args.Count >= 9;
                var isScaleByRoot = Array.IndexOf(ScaleByRootFiles, s.File) >= 0 && HasRootScaleK(s);

                if (isScaleByRoot)
                {
                    exceptions.Add(s);
                    if (hasFontSize) offenders.Add($"{s.File}:{s.Line} 例外处不该传 fontSize（会双重放大）");
                    continue;
                }

                if (!hasFontSize)
                {
                    offenders.Add($"{s.File}:{s.Line} 没给 fontSize（默认 0 = 原版 px 1:1 ⇒ 只有应有的 ~55%）");
                    continue;
                }

                if (!FontSizeFromSoleSource(s, out var why))
                    noSourceList.Add($"{s.File}:{s.Line} 字号不是唯一出处：{why}");
            }

            // ① 全仓零处漏字号（机械扫全部调用点）
            Check($"全仓 `{CallName}` 调用点零处漏字号（共扫到 {sites.Count} 处）",
                offenders.Count == 0,
                offenders.Count == 0
                    ? $"全部 {sites.Count} 处都显式给了字号，或已登记为「根节点 ×K」例外"
                    : string.Join(" | ", offenders.ToArray()));

            // ①b 例外只有登记在案的两个文件、且都能查到 ×K 放大
            var badException = new System.Collections.Generic.List<string>();
            foreach (var f in ScaleByRootFiles)
            {
                var found = false;
                foreach (var e in exceptions) if (e.File == f) found = true;
                if (!found) badException.Add(f + " 未扫到调用点（例外表过时？）");
            }
            Check("「根节点 ×K」例外恰好是登记的两处（且同域确有 localScale ×K）",
                badException.Count == 0,
                badException.Count == 0
                    ? string.Join(" / ", ScaleByRootExceptionSummary(exceptions))
                    : string.Join(" | ", badException.ToArray()));

            // ② 字号只能来自唯一出处
            Check("非例外处字号一律取自 `UiLayoutGame.FontPx*`（零处字面量）",
                noSourceList.Count == 0,
                noSourceList.Count == 0
                    ? "全部取自 `(int)UiLayoutGame.FontPx16/24/30/42` 或其局部变量"
                    : string.Join(" | ", noSourceList.ToArray()));

            // ③ 出处本身成立（真值：编译后的常量 + 纯函数）
            var k = UiLayoutGame.K;
            var lp16 = Diablo2.UI.D2Text.LineSpacing(Diablo2.UI.D2Text.D2Font.Font16);
            var lp24 = Diablo2.UI.D2Text.LineSpacing(Diablo2.UI.D2Text.D2Font.Font24);
            var lp30 = Diablo2.UI.D2Text.LineSpacing(Diablo2.UI.D2Text.D2Font.Font30);
            var lp42 = Diablo2.UI.D2Text.LineSpacing(Diablo2.UI.D2Text.D2Font.Font42);
            var fpOk = lp16 == 16 && lp24 == 24 && lp30 == 30 && lp42 == 42
                       && Near(UiLayoutGame.FontPx16, lp16 * k) && Near(UiLayoutGame.FontPx24, lp24 * k)
                       && Near(UiLayoutGame.FontPx30, lp30 * k) && Near(UiLayoutGame.FontPx42, lp42 * k);
            Check("字号唯一出处成立：FontPx(font) == 原版行距(16/24/30/42) × K",
                fpOk,
                $"K={k}；LineSpacing={lp16}/{lp24}/{lp30}/{lp42}；"
                + $"FontPx16={UiLayoutGame.FontPx16} / 24={UiLayoutGame.FontPx24} /"
                + $" 30={UiLayoutGame.FontPx30} / 42={UiLayoutGame.FontPx42}");

            // ③b 1:1 口径 = fontSize 0（① 与 ⑤ 共同依赖这条默认值）
            var d2text = Mask(ReadFile(Path.Combine(UiDir, "D2Text.cs")));
            var defOk = d2text.IndexOf("int fontSize = 0", StringComparison.Ordinal) >= 0;
            Check("`D2Label.Create` 的 fontSize 默认值仍是 0（= 原版 px 1:1，①的前提）",
                defOk, defOk ? "签名含 `int fontSize = 0`" : "签名里找不到 `int fontSize = 0` —— ①的前提变了，必须重判");

            // ⑤ 框单位：名牌矩形必须换算到画布 px（否则"字大了框没大"⇒ 提前折行）
            var gil = Mask(ReadFile(Path.Combine(UiDir, "GroundItemLabelView.cs")));
            var boxOk = Regex.IsMatch(gil,
                @"LabelSize\s*=\s*[\s\S]{0,160}?GameConst\.IsoTilePxW\s*\*\s*UiLayoutGame\.K");
            Check("`GroundItemLabelView.LabelSize` 已换算到画布 px（× UiLayoutGame.K）",
                boxOk, boxOk ? "LabelSize = (IsoTilePxW × K, HalfTilePxH × K)" : "LabelSize 仍是世界格 px（会提前折行）");

            // ④ 逐面板：用户点名的 6 处，逐文件核"调用点数 == 已给字号数"
            PerPanel("HudPanel.cs", "球内 Life/Mana 数字 + 技能格热键 + 腰带数量", sites);
            PerPanel("InventoryPanel.cs", "格内数量 + 金币", sites);
            PerPanel("ShopPanel.cs", "标题行 / 提示行 / 格内数量 / 金币", sites);
            PerPanel("CharacterPanel.cs", "四维 / 派生 / 命中格挡 / 抗性 数值", sites);
            PerPanel("GroundItemLabelView.cs", "地面物品名牌", sites);
            PerPanel("LevelEntryTitle.cs", "区域名标题（例外：放大由根节点 ×K 承担）", sites);
        }

        /// <summary>逐面板：该文件的每一处调用点都过了（有字号 或 已登记例外）。</summary>
        private static void PerPanel(string file, string what, System.Collections.Generic.List<Site> sites)
        {
            var mine = new System.Collections.Generic.List<Site>();
            foreach (var s in sites) if (s.File == file) mine.Add(s);
            var bad = new System.Collections.Generic.List<string>();
            var sizes = new System.Collections.Generic.List<string>();
            foreach (var s in mine)
            {
                if (s.Args.Count >= 9) sizes.Add(s.Args[8].Trim());
                else if (Array.IndexOf(ScaleByRootFiles, s.File) >= 0 && HasRootScaleK(s))
                    sizes.Add($"根节点×K(line {s.Line})");
                else bad.Add($"{s.File}:{s.Line} 漏字号");
            }
            Check($"{file}：{what}（{mine.Count} 处调用点全部达标）",
                bad.Count == 0 && mine.Count > 0,
                mine.Count == 0 ? "没扫到调用点"
                    : (bad.Count > 0 ? string.Join(" | ", bad.ToArray())
                                     : "fontSize = " + string.Join(", ", sizes.ToArray())));
        }

        private static System.Collections.Generic.List<string> ScaleByRootExceptionSummary(
            System.Collections.Generic.List<Site> exceptions)
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var e in exceptions) list.Add($"{e.File}:{e.Line}（放大由 localScale ×K 承担）");
            return list;
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;

        /// <summary>
        /// 该调用点前后 20 行内是否有**根节点放大**（例外必须自证），且**本文件**确有 ×1.8 的出处。
        /// <para>口径（实测修正）：⛔ 不能要求那一行里出现 `K` ——
        /// `UiLayoutFlow.cs:1816` 写的是 `localScale = new Vector3(s, s, 1f)`（`s = Scale * _fontScale`，
        /// 而 `Scale` 是该文件自己的 `public const float Scale = 1.8f`，与 `UiLayoutGame.K` 同一口径）
        /// ⇒ 原来那条"行里必须有 K"会让它假 FAIL（实测：run3 报 2 项红）。</para>
        /// </summary>
        private static bool HasRootScaleK(Site s)
        {
            var src = Mask(ReadFile(s.Path));
            var lines = src.Replace("\r\n", "\n").Split('\n');
            var from = Math.Max(0, s.Line - 1 - 20);
            var to = Math.Min(lines.Length - 1, s.Line + 20);
            var hasRootScale = false;
            for (var i = from; i <= to; i++)
            {
                var t = lines[i];
                if (t.IndexOf("localScale", StringComparison.Ordinal) < 0) continue;
                if (t.IndexOf("new Vector3(", StringComparison.Ordinal) < 0) continue;   // ⛔ 只认真的设了缩放
                if (t.IndexOf("Vector3.one", StringComparison.Ordinal) >= 0) continue;   // ⛔ 复位不算放大
                hasRootScale = true;
                break;
            }
            if (!hasRootScale) return false;
            // 本文件必须确有 ×1.8 的出处（`UiLayoutGame.K` 或字面 1.8）—— 防"随便找处 localScale 就放过"
            return src.IndexOf("UiLayoutGame.K", StringComparison.Ordinal) >= 0
                   || src.IndexOf("1.8", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 闸门自检（SKILL §8 第 3 条：新检查项必须各验一次"已知正确样本 PASS / 已知错误样本 FAIL"）。
        /// 这里验的是**决定每个调用点判红的那个计数器**（`SplitTop` 的顶层切参 + 实参个数口径）。
        /// </summary>
        private static void SelfTest()
        {
            var good = SplitTop("a, \"b\", c, d, e, f, new Vector2(1f, 2f), g, (int)UiLayoutGame.FontPx16");
            var bad = SplitTop("a, \"b\", c, d, e, f, new Vector2(1f, 2f), g");      // 已知错误样本
            var empty = SplitTop("");                                                // 边界：无实参
            var nested = SplitTop("a, Foo(new Vector2(1f, 2f), \"x,y\"), b, c, d, e, f, g, h");  // 逗号在字符串/嵌套里
            Check("(font-scale 自检) 实参计数：9 参=有字号 / 8 参=漏字号 / 空=0 / 嵌套与字符串里的逗号不算分隔",
                good.Count == 9 && bad.Count == 8 && empty.Count == 0 && nested.Count == 9,
                $"good={good.Count} bad={bad.Count} empty={empty.Count} nested={nested.Count}");
        }

        /// <summary>字号实参是否来自唯一出处：`(int)UiLayoutGame.FontPx*`，或本文件里那样定义的局部变量。</summary>
        private static bool FontSizeFromSoleSource(Site s, out string why)
        {
            var arg = s.Args[8].Trim();
            if (Regex.IsMatch(arg, @"^\(int\)\s*UiLayoutGame\.FontPx(16|24|30|42)$"))
            {
                why = string.Empty;
                return true;
            }

            // 局部变量写法（`EntityTooltip`：`var fontPx = (int)UiLayoutGame.FontPx16;`）
            var src = Mask(ReadFile(s.Path));
            if (Regex.IsMatch(arg, @"^[A-Za-z_][A-Za-z0-9_]*$")
                && Regex.IsMatch(src, @"\b" + Regex.Escape(arg) + @"\s*=\s*\(int\)\s*UiLayoutGame\.FontPx(16|24|30|42)"))
            {
                why = string.Empty;
                return true;
            }

            why = $"实参是 `{arg}`（不是 `(int)UiLayoutGame.FontPx*`，也不是它的局部变量）";
            return false;
        }

        private static string ReadFile(string p) => File.Exists(p) ? File.ReadAllText(p) : string.Empty;

        // ── 扫描：掩码注释 → 找调用 → 括号配对 → 顶层切参 ──────────────────────
        private static System.Collections.Generic.List<Site> Scan()
        {
            var list = new System.Collections.Generic.List<Site>();
            var files = Directory.GetFiles(UiDir, "*.cs", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.Ordinal);
            foreach (var f in files)
            {
                var src = Mask(File.ReadAllText(f));
                var i = 0;
                while (true)
                {
                    i = src.IndexOf(CallName, i, StringComparison.Ordinal);
                    if (i < 0) break;
                    var open = i + CallName.Length;
                    if (open >= src.Length || src[open] != '(') { i = open; continue; }   // 只是提到名字
                    var close = MatchParen(src, open);
                    if (close < 0) break;
                    list.Add(new Site
                    {
                        File = Path.GetFileName(f),
                        Path = f,
                        Line = LineOf(src, i),
                        Args = SplitTop(src.Substring(open + 1, close - open - 1)),
                        Text = src.Substring(i, close - i + 1),
                    });
                    i = close + 1;
                }
            }
            return list;
        }

        private static int LineOf(string s, int index)
        {
            var line = 1;
            for (var i = 0; i < index && i < s.Length; i++) if (s[i] == '\n') line++;
            return line;
        }

        private static int MatchParen(string s, int open)
        {
            var depth = 0;
            var inStr = false;
            var inChar = false;
            var verbatim = false;
            for (var i = open; i < s.Length; i++)
            {
                var c = s[i];
                if (inStr)
                {
                    if (verbatim) { if (c == '"') inStr = false; }
                    else if (c == '\\') i++;
                    else if (c == '"') inStr = false;
                    continue;
                }
                if (inChar) { if (c == '\\') i++; else if (c == '\'') inChar = false; continue; }
                if (c == '"') { inStr = true; verbatim = i > 0 && s[i - 1] == '@'; continue; }
                if (c == '\'') { inChar = true; continue; }
                if (c == '(') depth++;
                else if (c == ')') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static System.Collections.Generic.List<string> SplitTop(string s)
        {
            var parts = new System.Collections.Generic.List<string>();
            var sb = new System.Text.StringBuilder();
            var depth = 0;
            var inStr = false;
            var inChar = false;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < s.Length) { sb.Append(s[++i]); continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (inChar)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < s.Length) { sb.Append(s[++i]); continue; }
                    if (c == '\'') inChar = false;
                    continue;
                }
                if (c == '"') { inStr = true; sb.Append(c); continue; }
                if (c == '\'') { inChar = true; sb.Append(c); continue; }
                if (c == '(' || c == '[' || c == '{') depth++;
                if (c == ')' || c == ']' || c == '}') depth--;
                if (c == ',' && depth == 0) { parts.Add(sb.ToString()); sb.Length = 0; continue; }
                sb.Append(c);
            }
            parts.Add(sb.ToString());
            if (parts.Count == 1 && parts[0].Trim().Length == 0) parts.Clear();   // `Create()` ⇒ 0 个实参
            return parts;
        }

        /// <summary>
        /// 把注释掩成**等长空格**（⛔ 不删行、不缩短 —— 行号/列号必须与真实文件一致，
        /// 报告里的 `文件:行` 才能直接打开复核）。字符串/字符字面量原样保留（注释符在里面不是注释）。
        /// </summary>
        private static string Mask(string src)
        {
            if (string.IsNullOrEmpty(src)) return string.Empty;
            var a = src.ToCharArray();
            var n = a.Length;
            for (var i = 0; i < n; i++)
            {
                var c = a[i];
                if (c == '/' && i + 1 < n && a[i + 1] == '/')
                {
                    while (i < n && a[i] != '\n') { a[i] = ' '; i++; }
                    continue;
                }
                if (c == '/' && i + 1 < n && a[i + 1] == '*')
                {
                    a[i] = ' '; a[i + 1] = ' '; i += 2;
                    while (i < n)
                    {
                        if (a[i] == '*' && i + 1 < n && a[i + 1] == '/')
                        {
                            a[i] = ' '; a[i + 1] = ' '; i += 2; break;
                        }
                        if (a[i] != '\n' && a[i] != '\r') a[i] = ' ';
                        i++;
                    }
                    i--;
                    continue;
                }
                if (c == '"')
                {
                    var verbatim = i > 0 && a[i - 1] == '@';
                    i++;
                    while (i < n)
                    {
                        if (verbatim) { if (a[i] == '"') break; }
                        else if (a[i] == '\\') { i++; }
                        else if (a[i] == '"' || a[i] == '\n') break;
                        i++;
                    }
                    continue;
                }
                if (c == '\'')
                {
                    i++;
                    while (i < n)
                    {
                        if (a[i] == '\\') { i++; }
                        else if (a[i] == '\'' || a[i] == '\n') break;
                        i++;
                    }
                    continue;
                }
            }
            return new string(a);
        }
    }

}
