// ─────────────────────────────────────────────────────────────────────────────
// 片 5（「小地图 + 死亡屏 1:1」轮）离线断言。
//
// 覆盖两件事，全部**可离线计算**（不需要 Unity 原生）：
//   ① 死亡屏（EndGame）的**拼装口径与布局**：4 块底图 = 原版 DC6 实测帧尺寸、拼成 320×480、
//      ×1.8 居中、三行内容栈两两不重叠且都在面板内、旧版的裸魔数坐标已清 0。
//   ② 小地图（MiniMapPanel）：**自加的标题/图例已删干净**、标记改成原版 `mapicon_*` 帧。
//
// ⛔ 只读断言，不改任何工程产物。
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.UI;
using UnityEngine;

namespace Uicheck
{
    internal static class P5Check
    {
        public static void Run()
        {
            Console.WriteLine("── ⑯ 片 5：死亡屏（EndGame）拼装 + 布局 / 小地图（原版 mapicon） ──");
            DeathBackdrop();
            DeathLayout();
            DeathSource();
            MiniMapSource();
            MiniMapAutomapSource();   // ★ w6：automap 素材「在不在」+ 现行画法口径 + 与原版的差异项
            Assets();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ① 死亡屏底图：4 块 = 原版 `MENU/EndGame.dc6` 页 0 的实测帧尺寸
        //    实测出处：`dc6.py info` ⇒ 256x256 64x256 256x224 64x224（每 4 帧一页，2 页）
        // ═════════════════════════════════════════════════════════════════════
        private static void DeathBackdrop()
        {
            var expect = new[]
            {
                (w: 256f, h: 256f, x: 0f, y: 0f),
                (w: 64f, h: 256f, x: 256f, y: 0f),
                (w: 256f, h: 224f, x: 0f, y: 256f),
                (w: 64f, h: 224f, x: 256f, y: 256f),
            };

            var tiles = UiLayoutGame.DeathBackTiles;
            var same = tiles.Length == expect.Length;
            var area = 0f;
            for (var i = 0; i < tiles.Length && i < expect.Length; i++)
            {
                same &= Math.Abs(tiles[i].w - expect[i].w) < 0.01f
                        && Math.Abs(tiles[i].h - expect[i].h) < 0.01f
                        && Math.Abs(tiles[i].x - expect[i].x) < 0.01f
                        && Math.Abs(tiles[i].y - expect[i].y) < 0.01f;
                area += tiles[i].w * tiles[i].h;
            }
            Program.Check("死亡屏底图 4 块 = 原版 EndGame.dc6 页 0 实测帧尺寸",
                same, "256x256@0,0 / 64x256@256,0 / 256x224@0,256 / 64x224@256,256");

            Program.Check("死亡屏底图 4 块面积和 = 320×480（拼满、不重叠不缺口）",
                Math.Abs(area - 320f * 480f) < 0.01f, $"area={area}");

            Program.Check("死亡屏面板尺寸 = 320×480 × K(=1.8) = 576×864",
                Math.Abs(UiLayoutGame.DeathPanelSize.x - 576f) < 0.01f
                && Math.Abs(UiLayoutGame.DeathPanelSize.y - 864f) < 0.01f,
                $"{UiLayoutGame.DeathPanelSize.x}×{UiLayoutGame.DeathPanelSize.y}");

            // 块中心（画布）：由块表算出 —— 逐值对账（没有魔数）
            var wantPos = new[]
            {
                new Vector2(-57.6f, 201.6f), new Vector2(230.4f, 201.6f),
                new Vector2(-57.6f, -230.4f), new Vector2(230.4f, -230.4f),
            };
            var posOk = true;
            var detail = new StringBuilder();
            for (var i = 0; i < tiles.Length && i < wantPos.Length; i++)
            {
                var p = UiLayoutGame.DeathTilePos(i);
                posOk &= Math.Abs(p.x - wantPos[i].x) < 0.01f && Math.Abs(p.y - wantPos[i].y) < 0.01f;
                detail.Append($"[{i}]{p.x},{p.y} ");
            }
            Program.Check("死亡屏 4 块的中心坐标 = 由块表算出的期望值（无魔数）", posOk, detail.ToString());

            // 4 块拼起来的包围盒 == 面板矩形
            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minY = float.MaxValue;
            var maxY = float.MinValue;
            for (var i = 0; i < tiles.Length; i++)
            {
                var c = UiLayoutGame.DeathTilePos(i);
                var s = UiLayoutGame.DeathTileSize(i);
                minX = Math.Min(minX, c.x - s.x * 0.5f);
                maxX = Math.Max(maxX, c.x + s.x * 0.5f);
                minY = Math.Min(minY, c.y - s.y * 0.5f);
                maxY = Math.Max(maxY, c.y + s.y * 0.5f);
            }
            Program.Check("死亡屏 4 块的包围盒 == 面板矩形（±576×864 居中于画布）",
                Math.Abs(minX + 288f) < 0.01f && Math.Abs(maxX - 288f) < 0.01f
                && Math.Abs(minY + 432f) < 0.01f && Math.Abs(maxY - 432f) < 0.01f,
                $"x[{minX},{maxX}] y[{minY},{maxY}]");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ② 死亡屏布局：三行内容栈（本项目新增排布 E22）+ 都在面板内 + 两两不重叠
        // ═════════════════════════════════════════════════════════════════════
        private static void DeathLayout()
        {
            var K = UiLayoutGame.K;
            Program.Check("死亡屏标题条外框 = 原版 youdiedsoftcore 帧 0 的 256×54 × K",
                Math.Abs(UiLayoutGame.DeathBannerSize.x - 256f * K) < 0.01f
                && Math.Abs(UiLayoutGame.DeathBannerSize.y - 54f * K) < 0.01f,
                $"{UiLayoutGame.DeathBannerSize.x}×{UiLayoutGame.DeathBannerSize.y}");

            Program.Check("死亡屏按钮外框 = 原版 MENU/endgameok 的 96×32 × K",
                Math.Abs(UiLayoutGame.DeathButtonSize.x - 96f * K) < 0.01f
                && Math.Abs(UiLayoutGame.DeathButtonSize.y - 32f * K) < 0.01f,
                $"{UiLayoutGame.DeathButtonSize.x}×{UiLayoutGame.DeathButtonSize.y}");

            // 整栈垂直居中：栈顶（到面板顶的距离）== 栈底（到面板底的距离）
            var topEdge = UiLayoutGame.DeathRowCenterY(0) + UiLayoutGame.DeathBannerH * K * 0.5f;
            var bottomEdge = UiLayoutGame.DeathRowCenterY(2) - UiLayoutGame.DeathButtonH * K * 0.5f;
            Program.Check("死亡屏三行内容栈垂直居中（栈顶到面板顶 == 栈底到面板底）",
                Math.Abs(topEdge - (-bottomEdge)) < 0.01f, $"top={topEdge} bottom={bottomEdge}");

            // 都在面板矩形内；且**同层**两两不相交：
            //   · 4 块底图之间不许相交（它们要拼成整幅画）；
            //   · 3 行内容之间不许相交（标题条 / 提示行 / 按钮）。
            //   ⚠️ 内容行**本来就要画在底图之上**（底图是整幅画、文字/按钮叠在上面）
            //      ⇒ 不把"底图 vs 内容行"算作冲突。
            var rects = UiLayoutGame.DeathRects();
            var bad = string.Empty;
            for (var i = 0; i < rects.Length; i++)
            {
                var r = rects[i].rect;
                if (r.xMin < -288f - 0.01f || r.xMax > 288f + 0.01f
                    || r.yMin < -432f - 0.01f || r.yMax > 432f + 0.01f)
                    bad += rects[i].name + "(溢出) ";
            }

            for (var i = 0; i < rects.Length; i++)
            {
                for (var j = i + 1; j < rects.Length; j++)
                {
                    var layerI = rects[i].name.StartsWith("BackTile") ? 0 : 1;
                    var layerJ = rects[j].name.StartsWith("BackTile") ? 0 : 1;
                    if (layerI != layerJ) continue;         // 不同层可以叠（底图 vs 内容行）

                    var a = rects[i].rect;
                    var b = rects[j].rect;
                    var overlap = Math.Min(a.xMax, b.xMax) - Math.Max(a.xMin, b.xMin) > 0.01f
                                  && Math.Min(a.yMax, b.yMax) - Math.Max(a.yMin, b.yMin) > 0.01f;
                    if (overlap) bad += $"{rects[i].name}∩{rects[j].name} ";
                }
            }
            Program.Check("死亡屏图元矩形：全部在面板内、同层两两不相交（底图 4 块 / 内容 3 行）",
                bad.Length == 0, bad.Length == 0 ? $"{rects.Length} 个矩形全 OK" : bad);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ③ 死亡屏源码：旧版裸魔数坐标已清 0；用的是原版素材
        // ═════════════════════════════════════════════════════════════════════
        private static void DeathSource()
        {
            var src = File.ReadAllText(Path.Combine(Program.UiDir, "DeathPanel.cs"));

            // 旧版四个裸魔数（片 5 前的写法）：900×80@(0,90) / 900×30@(0,20) / 260×46@(0,-60) / 76x150
            var oldMagic = new[]
            {
                "900f, 80f", "900f, 30f", "260f, 46f", "new Vector2(0f, 90f)",
                "new Vector2(0f, 20f)", "new Vector2(0f, -60f)", "760f, 150f",
            };
            var hit = string.Empty;
            foreach (var m in oldMagic) if (src.Contains(m)) hit += "\"" + m + "\" ";
            Program.Check("死亡屏源码：旧版裸魔数坐标 0 命中（4 个坐标全部改成有出处的常量）",
                hit.Length == 0, hit.Length == 0 ? "0 命中" : hit);

            Program.Check("死亡屏源码：底图走原版 EndGame 页 0 的 4 块（ResPaths.EndGameTile(0, i)）",
                src.Contains("ResPaths.EndGameTile(0, i)"), "见 Build()");

            Program.Check("死亡屏源码：按钮走原版 MENU/endgameok 帧（ResPaths.MenuEndGameOK）",
                src.Contains("ResPaths.MenuEndGameOK"), "见 Build()");

            Program.Check("死亡屏源码：标题条走原版 chi/youdiedsoftcore 帧 0（ResPaths.Banner）",
                src.Contains("ResPaths.Banner(\"youdiedsoftcore_0\")"), "见 Build()");

            Program.Check("死亡屏源码：按钮文字 = 原版串表 id 3403「继续」（ContinueText）",
                src.Contains("ContinueText") && src.Contains("3403"), "见 ContinueText 注释");

            // ★ 实测踩过的坑（B31）：把「带下划线的前缀」交给 OrigButton ⇒ 拼成 `endgameok__0`
            //   （双下划线）⇒ 取不到图、按钮只是纯色块。这里把形状钉死。
            Program.Check("死亡屏源码：OrigButton 的前缀**不带结尾下划线**（防 `endgameok__0` 双下划线）",
                src.Contains("ResPaths.MenuEndGameOK,")
                && !src.Contains("MenuEndGameOK + \"_\""),
                "见 Build() 的 UiArt.OrigButton 调用");

            // ★ 实测踩过的坑（B35）：提示行超过 ~18 个中文字会**折成 2 行**，而该行框只有 1 行高
            //   ⇒ 两行互相压字、第 2 行还压住按钮。判据 = 必须显式 `Overflow` 且文案是短句。
            Program.Check("死亡屏源码：提示行显式 `HorizontalWrapMode.Overflow`（B35：绝不换行）",
                src.Contains("_hint.horizontalOverflow = HorizontalWrapMode.Overflow;"),
                "见 Build()");

            Program.Check("死亡屏源码：提示行文案是**短句**（不留修前那条会折行的长句）",
                !src.Contains("秒后可按「继续」（回罗格营地复活）")
                && !src.Contains("等待 `{Events.Revived}`）"),
                "WaitingText() / OnRevive / OnReviveTimeout");

            Program.Check("UiArt.OrigButton：内部用 ResPaths.Frame 拼帧名（前缀不带下划线也能取到图）",
                File.ReadAllText(Path.Combine(Program.UiDir, "UiArt.cs"))
                    .Contains("var normal = ResPaths.Frame(framePrefix, 0);"),
                "见 UiArt.LoadOrig 调用点");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ④ 小地图源码：自加的标题/图例已删干净；标记改成原版 mapicon
        // ═════════════════════════════════════════════════════════════════════
        private static void MiniMapSource()
        {
            var src = File.ReadAllText(Path.Combine(Program.UiDir, "MiniMapPanel.cs"));

            // 判据 = **声明形状**（不是裸名字）：文件头那段"删掉了哪些常量"的说明里
            // 本来就会提到这些名字 ⇒ 用裸名会**假阳性**。下面这些串只可能出现在**代码**里。
            var ghosts = new[]
            {
                "const float HeaderH", "const float TitleH", "const float LegendH",
                "const float HeaderGap", "const float HeaderW",
                "Vector2 TitlePos", "Vector2 LegendPos",
                "Text _title", "Text _legend", "_title.text", "_legend.text",
                "自动地图 · ", "Tab 关闭", "黄点=出入口",
            };
            var hit = string.Empty;
            foreach (var g in ghosts) if (src.Contains(g)) hit += "\"" + g + "\" ";
            Program.Check("小地图源码：自加的标题/图例（含 HeaderH 一族常量）**0 命中**",
                hit.Length == 0, hit.Length == 0 ? "0 命中" : hit);

            Program.Check("小地图源码：源码里已无标题/图例文本节点（无 UiArt.Label）",
                !src.Contains("UiArt.Label"), "标题行 / 图例行已整段删除");

            Program.Check("小地图源码：标记底图 = 原版 mapicons 帧（ResPaths.MiniMapIcon）",
                src.Contains("ResPaths.MiniMapIcon(ResPaths.MiniMapMarkerFrame)"),
                "见 BuildMarkers()");

            Program.Check("小地图源码：标记不再用纯色方块（无 UiArt.Panel(_box, \"Marker\")）",
                !src.Contains("UiArt.Panel(_box, \"Marker"), "已换成 UiArt.Art(...)");

            Program.Check("小地图源码：标记按原版图标是白模板 ⇒ 必须上色（SetArtTint）",
                src.Contains("UiArt.SetArtTint(dot,"), "见 BuildMarkers()");

            // ★ 片 `automap-original-verdict`（2026-09-22）：右上角**定尺框**口径已消除
            //   （原版 = 满屏叠加层）⇒ 断言它连同三个常量一起**不会回来**。
            var layoutSrc = File.ReadAllText(Path.Combine(Program.ProjectRoot, "client", "Assets",
                "Scripts", "UI", "UiLayoutGame.cs"));
            var layoutCode = CodeOnly(layoutSrc);
            var srcCode = CodeOnly(src);
            var boxLeft = string.Empty;
            foreach (var g in new[] { "MiniMapBoxMax", "MiniMapMargin", "MiniMapBoxPos" })
            {
                if (layoutCode.Contains(g)) boxLeft += g + "(UiLayoutGame) ";
                if (srcCode.Contains(g)) boxLeft += g + "(MiniMapPanel) ";
            }
            Program.Check("小地图：右上角**定尺框**口径 0 命中（MiniMapBoxMax / MiniMapMargin / MiniMapBoxPos）",
                boxLeft.Length == 0, boxLeft.Length == 0 ? "0 命中（原版 = 满屏叠加层）" : boxLeft);

            Program.Check("小地图：标记图标边长 = 原版 16 × K = 28.8（原版按固定像素 blit，不随地图缩放）",
                Math.Abs(UiLayoutGame.MiniMapIconPx - 28.8f) < 0.01f,
                $"iconPx={UiLayoutGame.MiniMapIconPx}");

            // ── ★ w6：画法口径（从"大色块棋盘"换成"暗底 + 细线 / 小点"）──
            //    色值**全部来自盘上原版 automap 家族素材的实测像素**（见 MiniMapPanel 文件头出处 A/B/C）。
            var oldPaint = string.Empty;
            foreach (var g in new[] { "ColWalkable", "ColBlocking", "ColExit", "ColInteractable",
                                      "ColUnexplored", "ColVoid", "private static Color32 ColorOf(" })
                if (src.Contains(g)) oldPaint += "\"" + g + "\" ";
            Program.Check("小地图源码：旧「大色块」配色与 ColorOf() **0 命中**（画法已换代）",
                oldPaint.Length == 0, oldPaint.Length == 0 ? "0 命中" : oldPaint);

            // ── ★ 片 `automap-original-verdict`：画法 = **原版 cel**（`AutoMap.txt` 的 CelN → `MaxiMap.dc6`
            //    帧，ACT1 调色板）blit 到 1/10 等距位置 —— 旧「程序化点阵 + 自选配色」口径已消除 ──
            var ghostPaint = string.Empty;
            foreach (var g in new[] { "ColBackdrop", "ColFloor", "ColBlock", "SuperSample",
                                      "FloorDotPx", "BlockDotPx", "ExitDotPx", "PaintSquare",
                                      "PaintCell(", "PixelBase(" })
                if (srcCode.Contains(g)) ghostPaint += "\"" + g + "\" ";
            Program.Check("小地图源码：旧「程序化点阵 + 本项目自选配色」的实现常量**全部删除**（0 命中）",
                ghostPaint.Length == 0, ghostPaint.Length == 0 ? "0 命中" : ghostPaint);

            Program.Check("小地图源码：逐格图形 = 原版 cel（`MinimapArgs.CelAt` + `AutoMapCel.CelPixels`）"
                          + "，⛔ 本文件里没有任何自选色常量",
                src.Contains("_map.CelAt(x, y, false)") && src.Contains("_map.CelAt(x, y, true)")
                && src.Contains("AutoMapCel.CelPixels") && src.Contains("AutoMapCel.PaletteRgb")
                && !src.Contains("new Color32(0x"),
                "见 Redraw() / Blit()（颜色只来自 ACT1 调色板表）");

            Program.Check("小地图源码：叠加层**铺满画布**（Backdrop 锚点 0..1）+ 以玩家格为中心平移（UpdateView）",
                src.Contains("anchorMin = Vector2.zero") && src.Contains("anchorMax = Vector2.one")
                && src.Contains("private void UpdateView()"),
                "原版 = 满屏叠加层（⛔ 不是右上角定尺框）");

            // 生成物（`Core/AutoMapCel.generated.cs`）：帧数 / 帧尺寸 / 调色板 / 像素数据自洽
            Program.Check("automap 生成物：`MaxiMap` 帧数 = 1260、cel 帧尺寸 = 16×32（ACT1 调色板解，实测）",
                AutoMapCel.FrameCount == 1260 && AutoMapCel.W == 16 && AutoMapCel.H == 32
                && AutoMapCel.ScaleDen == 10,
                $"FrameCount={AutoMapCel.FrameCount} {AutoMapCel.W}×{AutoMapCel.H} 1/{AutoMapCel.ScaleDen}");

            Program.Check("automap 生成物：ACT1 调色板 = 256×RGB = 768 字节（原版 `ACT1/Pal.PL2`）",
                AutoMapCel.PaletteRgb != null && AutoMapCel.PaletteRgb.Length == 768,
                $"len={AutoMapCel.PaletteRgb?.Length ?? -1}");

            var badCel = -1;
            foreach (var kv in AutoMapCel.CelPixels)
                if (kv.Key < 0 || kv.Key >= AutoMapCel.FrameCount || kv.Value == null
                    || kv.Value.Length == 0 || kv.Value.Length % 3 != 0)
                { badCel = kv.Key; break; }
            Program.Check($"automap 生成物：{AutoMapCel.CelPixels.Count} 个 cel 的像素数据自洽"
                          + "（0 ≤ cel < 1260 / 3 字节一组 / 非空）",
                badCel < 0, badCel < 0 ? "全部自洽" : $"cel={badCel} 不合规");

            // 逐格 Cel 字段（契约增补，见 `Module/Contracts.cs` 的 `# contract:` 注释）
            var celArgs = new MinimapArgs();
            Program.Check("`MinimapArgs` 增补了逐格 Cel 字段（`cels` 地面层 / `celsOver` 物件层）",
                celArgs.cels != null && celArgs.celsOver != null
                && celArgs.CelAt(-1, 0, false) == -1 && celArgs.CelAt(0, 0, true) == -1,
                "越界/未填 ⇒ -1（原版这一格不画）");

            Program.Check("小地图源码：文件头把「与原版差在哪」写实（AUTOMAP 图块表 / AutoMap.txt / 满屏叠加层）",
                src.Contains("MaxiMap.dc6") && src.Contains("AutoMap.txt") && src.Contains("满屏叠加层"),
                "见文件头 §素材结论 + §与原版还差在哪（① 口径 ② 图块 vs 点阵 ③ 视野）");
        }

        /// <summary>
        /// 只留**代码行**（去掉 `//` / `*` / `/*` 起头的行）。
        /// 为什么要它：源码文件头**必须**写清"旧口径已删除"，而那几句说明会**引用**旧常量名
        /// （如 `ColBackdrop`）⇒ 直接 `src.Contains` 会被自己的注释判红。
        /// 与 `tools/verify.ps1` 的 `-skipComment` 同口径。
        /// </summary>
        private static string CodeOnly(string text)
        {
            var sb = new StringBuilder();
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                var t = line.TrimStart();
                if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*")) continue;
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ④-b ★ 片 `automap-original-verdict`（2026-09-22）：automap 素材「在不在」+ 原版口径的判据
        //
        // 结论（本机实测，两路互相印证）：
        //   · **已到位**：原版图块表 `ui/AUTOMAP/MaxiMap.dc6`（1260 帧 × 16×32）与逐格 Cel 表
        //     `AutoMap.txt`（2603 行）—— 它们落在 `<仓库根>/原版资源/`（skill §1.9 的唯一落点；
        //     `.gitignore` 排除）⇒ 下面的 `CheckOriginalRes` 从「[SKIP]」**自动变成真判**。
        //   · **在**（工程内）：automap 家族的横幅 5 张（`data/local/ui/chi/*.dc6` 解出）
        //     + 标记图标（`MINIMAP/mapicons.DC6` 的 8 帧 16×16 白模板）。
        //   · **仍不在工程内**（是**刻意**的）：`MaxiMap` 的帧**不整包复制**进 `Assets/`
        //     —— 只把被引用的那几帧的索引数据 + ACT1 调色板编进生成物
        //     `client/Assets/Scripts/Core/AutoMapCel.generated.cs`（skill §3.6）。
        //   ⇒ 现行画法 = 原版的「按 `AutoMap.txt` 的 Cel 号从 `MaxiMap.dc6` blit」口径（见 ⑯）。
        // ═════════════════════════════════════════════════════════════════════
        private static void MiniMapAutomapSource()
        {
            // ① 原版图块 / Cel 表：`CheckOriginalRes`（不在位 ⇒ [SKIP] 且给期望路径）。
            Program.CheckOriginalRes("原版 automap 图块表在磁盘上（原版 blit 口径的素材，片 1「未解」的那批）",
                Path.Combine(Program.OriginalResDir, "d2dc6", "data", "global", "ui", "AUTOMAP", "MaxiMap.dc6"));
            Program.CheckOriginalRes("原版逐格 Cel 表 AutoMap.txt 在磁盘上（决定「哪格画哪个 Cel 号」）",
                Path.Combine(Program.OriginalResDir, "d2raw", "data", "global", "excel", "AutoMap.txt"));

            // ② 工程侧：`Resources/Clover/D2/**` 里**没有任何 AUTOMAP 图块**（只有横幅 + 8 帧图标）
            var d2 = Path.Combine(Program.ResourceRoot, "Clover", "D2");
            var tileHits = string.Empty;
            if (Directory.Exists(d2))
            {
                foreach (var f in Directory.GetFiles(d2, "*.png", SearchOption.AllDirectories))
                {
                    var n = Path.GetFileName(f);
                    if (n.StartsWith("MaxiMap") || n.StartsWith("Act2Map") || n.StartsWith("Act4Map"))
                        tileHits += n + " ";
                }
            }
            Program.Check("工程内 AUTOMAP 图块（MaxiMap* / Act2Map* / Act4Map*）**0 张** ⇒ 图块素材本机不存在",
                tileHits.Length == 0, tileHits.Length == 0 ? "0 命中（Resources/Clover/D2 全树）" : tileHits);

            Program.Check("工程内没有 D2/UI/AutoMap 目录（automap 图块目录只有原版才有）",
                !Directory.Exists(Path.Combine(d2, "UI", "AutoMap")),
                Path.Combine("Clover", "D2", "UI", "AutoMap"));

            // ③ 「在的」那两组：横幅 5 张 + 标记图标 8 帧（尺寸 = 原版 DC6 实测帧尺寸）
            var want = new[]
            {
                (name: "automap_0", w: 256, h: 36), (name: "automap_1", w: 122, h: 36),
                (name: "AutoMapCenter_0", w: 120, h: 34), (name: "AutoMapParty_0", w: 96, h: 34),
                (name: "AutoMapOptions_0", w: 222, h: 54),
            };
            var ok = true;
            var detail = new StringBuilder();
            foreach (var b in want)
            {
                var size = PngSize(ResPath("D2/UI/Banner/" + b.name + ".png"));
                var m = size.x == b.w && size.y == b.h;
                ok &= m;
                detail.Append($"{b.name}={size.x}x{size.y}{(m ? "" : $"(≠{b.w}x{b.h})")} ");
            }
            Program.Check("automap 家族**横幅** 5 张在磁盘上、尺寸 = 原版帧尺寸（`data/local/ui/chi/*.dc6`）",
                ok, detail.ToString());

            ok = true;
            detail.Clear();
            for (var i = 0; i < ResPaths.FrameCountMiniMapIcon; i++)
            {
                var size = PngSize(ResPath(ResPaths.MiniMapIcon(i) + ".png"));
                var m = size.x == 16 && size.y == 16;
                ok &= m;
                if (!m) detail.Append($"mapicon_{i}={size.x}x{size.y} ");
            }
            Program.Check("automap 家族**标记图标** 8 帧（16×16）× 全在磁盘上、尺寸 = 原版（`MINIMAP/mapicons.DC6`）",
                ok, ok ? $"{ResPaths.FrameCountMiniMapIcon} 帧全 16×16" : detail.ToString());

            // ④ ★ 逐格 Cel：**独立重解析**原版 `AutoMap.txt`（⛔ 不信生成物），再对生成表做金标抽样。
            var txt = Path.Combine(Program.OriginalResDir, "d2raw", "data", "global", "excel",
                "AutoMap.txt");
            if (!File.Exists(txt))
            {
                Console.WriteLine("   [SKIP] 原版 AutoMap.txt 不在位（环境依赖）：" + txt);
            }
            else
            {
                var raw = File.ReadAllLines(txt);
                var rows = new List<string>();
                foreach (var l in raw) if (l.Trim().Length > 0) rows.Add(l);
                var hdr = rows[0].Split('\t');
                Program.Check("原版 AutoMap.txt：1 表头 + **2603 数据行**、13 列（列名 / 语义见 plan §1.1）",
                    rows.Count - 1 == 2603 && hdr.Length == 13,
                    $"表头 {hdr.Length} 列 = {string.Join("|", hdr)}");

                var levels = new List<string>();
                var byLevel = new Dictionary<string, int>();
                var styles = new List<int>();
                var minCel = int.MaxValue;
                var maxCel = -1;
                for (var i = 1; i < rows.Count; i++)
                {
                    var f = rows[i].Split('\t');
                    if (!byLevel.ContainsKey(f[0])) { byLevel[f[0]] = 0; levels.Add(f[0]); }
                    byLevel[f[0]]++;
                    var st = int.Parse(f[2]);
                    if (!styles.Contains(st)) styles.Add(st);
                    for (var k = 6; k <= 12; k += 2)
                    {
                        var c = int.Parse(f[k]);
                        if (c > maxCel) maxCel = c;
                        if (c >= 0 && c < minCel) minCel = c;
                    }
                }
                Program.Check("原版 AutoMap.txt：LevelName = `<act> <LevelType>` 共 28 个（含 1 Town / 1 Wilderness / 1 Cave）",
                    levels.Count == 28 && byLevel.ContainsKey("1 Town")
                    && byLevel.ContainsKey("1 Wilderness") && byLevel.ContainsKey("1 Cave"),
                    $"1 Town={byLevel["1 Town"]} 行 / 1 Wilderness={byLevel["1 Wilderness"]} 行"
                    + $" / 1 Cave={byLevel["1 Cave"]} 行；Style {styles.Count} 种");

                Program.Check("原版 AutoMap.txt：CelN 值域 = **0..1254** ⊂ MaxiMap 的 0..1259 ⇒ CelN 就是帧序号",
                    maxCel == 1254 && minCel == 0,
                    $"Cel ∈ [{minCel},{maxCel}]，MaxiMap 帧数 {AutoMapCel.FrameCount}（−1 = 空槽位，本断言只看 ≥0）");
            }

            // 金标抽样：值由 `python tools/probes/gen_automap.py` 复算（口径见生成物文件头）；
            // 生成链路一漂移，这几条就变红（⛔ 不是"从生成物里抄一遍"）。
            var golden = new (int Area, bool Obj, string Key, int Cel)[]
            {
                (0, false, "moor_bridge/020", 0), (0, true, "moor_bridge/001", 60),
                (2, false, "cave/004", 130), (2, false, "cave/005", 131),
            };
            var gbad = string.Empty;
            foreach (var g in golden)
            {
                var got = AutoMapCel.Cel(g.Area, g.Obj, g.Key);
                if (got != g.Cel) gbad += $"{g.Key} 期望{g.Cel} 实得{got}；";
            }
            Program.Check("automap 生成物：逐格 Cel **金标抽样** 4 条（area/层/瓦片键 → Cel 号）",
                gbad.Length == 0, gbad.Length == 0 ? "4/4 一致" : gbad);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑤ 素材真在磁盘上（防"路径写错却编译通过"）+ 尺寸 = 原版帧尺寸
        // ═════════════════════════════════════════════════════════════════════
        private static void Assets()
        {
            // endgame 页 0 的 4 块
            var want = new[] { (256, 256), (64, 256), (256, 224), (64, 224) };
            var ok = true;
            var detail = new StringBuilder();
            for (var i = 0; i < 4; i++)
            {
                var path = ResPath(ResPaths.EndGameTile(0, i) + ".png");
                var size = PngSize(path);
                var m = size.x == want[i].Item1 && size.y == want[i].Item2;
                ok &= m;
                detail.Append($"{Path.GetFileName(path)}={size.x}x{size.y}{(m ? "" : "(≠原版)")} ");
            }
            Program.Check("ENDGAME 页 0 的 4 块 PNG 在磁盘上、且尺寸 = 原版帧尺寸", ok, detail.ToString());

            // endgameok 两帧
            ok = true;
            detail.Clear();
            for (var i = 0; i < ResPaths.FrameCountEndGameOK; i++)
            {
                var path = ResPath(ResPaths.Frame(ResPaths.MenuEndGameOK, i) + ".png");
                var size = PngSize(path);
                var m = size.x == 96 && size.y == 32;
                ok &= m;
                detail.Append($"{Path.GetFileName(path)}={size.x}x{size.y} ");
            }
            Program.Check("ENDGAMEOK 2 帧 PNG 在磁盘上、尺寸 = 原版 96×32", ok, detail.ToString());

            // 标题条（原版 youdiedsoftcore 帧 0）
            var banner = PngSize(ResPath("D2/UI/Banner/youdiedsoftcore_0.png"));
            Program.Check("死亡屏标题条 PNG 在磁盘上、尺寸 = 原版 256×54",
                banner.x == 256 && banner.y == 54, $"{banner.x}x{banner.y}");

            // 小地图标记（原版 mapicons 帧）
            var icon = PngSize(ResPath(ResPaths.MiniMapIcon(ResPaths.MiniMapMarkerFrame) + ".png"));
            Program.Check("小地图标记 PNG 在磁盘上、尺寸 = 原版 16×16",
                icon.x == 16 && icon.y == 16, $"mapicon_{ResPaths.MiniMapMarkerFrame}={icon.x}x{icon.y}");
        }

        /// <summary>
        /// `ResPaths` 给的是**相对 `Resources/Clover`** 的路径（`ResPaths.Root` = `"Clover"`）
        /// ⇒ 拼盘上路径时必须补 `Clover/`（漏了就成了所有文件都"不存在"的假失败）。
        /// </summary>
        private static string ResPath(string resPath)
            => Path.Combine(Program.ResourceRoot,
                (ResPaths.Root + "/" + resPath).Replace('/', Path.DirectorySeparatorChar));

        /// <summary>读 PNG 的宽高（只解析签名 + IHDR，不依赖任何图形库）。</summary>
        private static Vector2 PngSize(string path)
        {
            if (!File.Exists(path)) return new Vector2(-1f, -1f);
            var b = File.ReadAllBytes(path);
            if (b.Length < 24) return new Vector2(-1f, -1f);
            var w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            var h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            return new Vector2(w, h);
        }
    }
}
