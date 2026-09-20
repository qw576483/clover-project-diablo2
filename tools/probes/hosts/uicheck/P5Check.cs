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
using System.IO;
using System.Text;
using Diablo2.Core;
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

            Program.Check("小地图框位置 = 画布右上角留 20（940, 520）、图标边长 = 16 × K = 28.8",
                Math.Abs(UiLayoutGame.MiniMapBoxPos.x - 940f) < 0.01f
                && Math.Abs(UiLayoutGame.MiniMapBoxPos.y - 520f) < 0.01f
                && Math.Abs(UiLayoutGame.MiniMapIconPx - 28.8f) < 0.01f,
                $"boxPos={UiLayoutGame.MiniMapBoxPos} iconPx={UiLayoutGame.MiniMapIconPx}");
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
