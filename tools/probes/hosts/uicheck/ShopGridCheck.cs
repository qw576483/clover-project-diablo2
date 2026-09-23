// ═══════════════════════════════════════════════════════════════════════════
//  ShopGridCheck.cs（uicheck 宿主的一个检查节，片 impl-shop 新增）
//
//  判什么（用户症状：「商店商品占的格子不对」）：
//   ① **格区几何**：`buysell_back.png` 逐像素实测的格线 pitch / 原点 / 线数，与
//      `UiLayoutGame` 的 `ShopCell / ShopGridOrigin / ShopCols / ShopRows` 推出的值**逐值相等**，
//      并且 `ShopCellCenter(i,j)` 反算回底图像素 == 实测的格心（100 格全对）。
//   ② **按物品自身占格摆放**：喂 `1×1 / 2×2 / 2×3 / 1×4 / 2×4`（共 23 格）给
//      `ShopPanel.ComputeLayout` ⇒ 逐件**锚点格**与**占用集**和"行优先"手算结果逐格相等，
//      且「占用总数 == Σ gridW×gridH」「两两不重叠（每格只有一个占用者）」。
//   ③ **边界**：货物总占格 > 100 ⇒ 放不下的那几件被**点名 Warn**（抓 `Game.Logger`），
//      且**不覆盖**已摆好的货（逐格校对占用者）。
//   ④ 卖出页（`InventorySlot` 入口）同口径；`NpcShop.AddEntry` 真把尺寸带过来（源码锚点）。
//
//  为什么能离线判：②③ 的判据走的是**产品代码里的纯函数**（`ShopPanel.FindAnchorCell /
//  ClaimBlock / ComputeLayout` —— 只碰传入的数组 + `UiLayoutGame` 常量）；① 是解原版 PNG 的像素；
//  纯函数与像素解码都不需要 Unity 运行时（宿主已链 Unity 托管 DLL，`Vector2/Mathf` 是纯托管实现）。
//
//  实测数字出处（本片自己量的，⛔ 不是抄任务书）：`.ai-tmp/test/measure_shop_lines2.py`
//  对 `buysell_back.png` 的整列/整行平均亮度扫描 ⇒ 亮竖线 x = 14,43,72,…,275,304（11 条）、
//  亮横线 y = 62,91,120,…,323,352（11 条）；格心（+14）全部 ≤ 27.9、线像素全部 ≥ 118.5。
//  ⚠️ 该脚本是一次性量法（用后删）；断言值本身硬编码在上面这条结论里，判据不依赖脚本存在。
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Diablo2.Def;
using Diablo2.UI;
using UnityEngine;

namespace Uicheck
{
    /// <summary>商店买卖屏：10×10 格盘几何 + 按物品自身占格摆放（片 impl-shop）。</summary>
    internal static class ShopGridCheck
    {
        /// <summary>转发宿主统一的断言出口（`Program.Check` 负责计数与 `[ OK ]/[FAIL]` 打印）。</summary>
        private static void Check(string what, bool ok, string detail) => Program.Check(what, ok, detail);

        // ── 实测阈值（本片 2026-09-22 量的；见文件头「实测数字出处」）────────────────
        /// <summary>格线像素的平均亮度下限（实测线像素 ≥118.5、格心 ≤27.9 ⇒ 78 有很大的双向余量）。</summary>
        private const float LineBright = 78f;

        /// <summary>格心像素的平均亮度上限（同上，45 用来判"这里没有线"）。</summary>
        private const float CellDark = 45f;

        /// <summary>底图实测的格线 pitch（原版 px）与左上原点（原版 px）。</summary>
        private const int Pitch = 29;
        private const int OriginX = 14;
        private const int OriginY = 62;

        /// <summary>底图的资源相对路径（= `ResPaths.PanelBuySellBack`）。</summary>
        private const string BgRes = "D2/UI/Panel/buysell_back";

        public static void Run()
        {
            Console.WriteLine("── ⑳ 商店买卖屏：10×10 格盘几何 + 按物品自身占格摆放（片 impl-shop）──");
            CheckGridGeometry();
            CheckPlacement();
            CheckOverflow();
            CheckSellPage();
            CheckSourceWiring();
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ① 格区几何：原版底图像素 ↔ UiLayoutGame 常量
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckGridGeometry()
        {
            var file = Path.Combine(Program.ResourceRoot, "Clover",
                BgRes.Replace('/', Path.DirectorySeparatorChar) + ".png");
            if (!File.Exists(file))
            {
                Check("商店底图 `buysell_back.png` 在磁盘上", false, file);
                return;
            }
            if (!TryDecodeRgba(file, out var W, out var H, out var px))
            {
                Check("商店底图能解出 RGBA（8bit / 非隔行 / 色彩类型 2·6）", false, file);
                return;
            }

            Check("商店底图 IHDR == 320×432（= `UiLayoutGame.ShopSize` ÷ K 反算的原版 px）",
                W == 320 && H == 432
                && Near(UiLayoutGame.ShopSize.x / UiLayoutGame.K, 320f)
                && Near(UiLayoutGame.ShopSize.y / UiLayoutGame.K, 432f),
                $"PNG {W}×{H}；ShopSize÷K = {UiLayoutGame.ShopSize.x / UiLayoutGame.K:0.#}×"
                + $"{UiLayoutGame.ShopSize.y / UiLayoutGame.K:0.#}");

            // ── 11 条竖线 / 11 条横线：位置逐条实测为亮线 ─────────────────────
            var vDet = new StringBuilder();
            var vBad = string.Empty;
            for (var k = 0; k <= UiLayoutGame.ShopCols; k++)
            {
                var x = OriginX + Pitch * k;
                var m = ColumnMean(px, W, x, 80, 330);
                vDet.Append("x").Append(x).Append('=').Append(m.ToString("0.#")).Append(' ');
                if (m < LineBright) vBad += $"x={x}({m:0.#}) ";
            }
            Check($"竖格线 {UiLayoutGame.ShopCols + 1} 条 @ x = {OriginX}+{Pitch}k 逐条实测为亮线（阈值 >{LineBright}）",
                vBad.Length == 0, vBad.Length == 0 ? vDet.ToString().Trim() : "不够亮：" + vBad);

            var hDet = new StringBuilder();
            var hBad = string.Empty;
            for (var k = 0; k <= UiLayoutGame.ShopRows; k++)
            {
                var y = OriginY + Pitch * k;
                var m = RowMean(px, W, y, 150, 300);
                hDet.Append("y").Append(y).Append('=').Append(m.ToString("0.#")).Append(' ');
                if (m < LineBright) hBad += $"y={y}({m:0.#}) ";
            }
            Check($"横格线 {UiLayoutGame.ShopRows + 1} 条 @ y = {OriginY}+{Pitch}k 逐条实测为亮线（阈值 >{LineBright}）",
                hBad.Length == 0, hBad.Length == 0 ? hDet.ToString().Trim() : "不够亮：" + hBad);

            // ── 格心必须是暗的（证明"线之间是格子"，不是整片亮/整片暗）──────────
            var cBad = string.Empty;
            for (var j = 0; j < UiLayoutGame.ShopRows; j++)
            {
                for (var i = 0; i < UiLayoutGame.ShopCols; i++)
                {
                    var cx = OriginX + Pitch * i + Pitch / 2;
                    var cy = OriginY + Pitch * j + Pitch / 2;
                    if (ColumnMean(px, W, cx, 80, 330) >= CellDark) cBad += $"x{cx} ";
                    if (RowMean(px, W, cy, 150, 300) >= CellDark) cBad += $"y{cy} ";
                }
            }
            Check($"100 个格心（{(OriginX + Pitch / 2)} + {Pitch}i / {(OriginY + Pitch / 2)} + {Pitch}j）实测为暗格"
                + $"（阈值 <{CellDark}）", cBad.Length == 0, cBad.Length == 0 ? "0 例外（100 格心全暗）" : "偏亮：" + cBad);

            // ── "没有多余的线"：格区内所有亮线都必须落在 14+29k / 62+29k 附近 ────
            var vExtra = new List<int>();
            var vSeen = new HashSet<int>();
            for (var x = OriginX - 3; x <= OriginX + Pitch * UiLayoutGame.ShopCols + 3; x++)
            {
                if (ColumnMean(px, W, x, 80, 330) < LineBright) continue;
                var k = (int)Math.Round((x - OriginX) / (double)Pitch);
                if (Math.Abs(x - (OriginX + Pitch * k)) > 3) vExtra.Add(x);
                else vSeen.Add(k);
            }
            var hExtra = new List<int>();
            var hSeen = new HashSet<int>();
            for (var y = OriginY - 4; y <= OriginY + Pitch * UiLayoutGame.ShopRows + 3; y++)
            {
                if (RowMean(px, W, y, 150, 300) < LineBright) continue;
                var k = (int)Math.Round((y - OriginY) / (double)Pitch);
                if (Math.Abs(y - (OriginY + Pitch * k)) > 3) hExtra.Add(y);
                else hSeen.Add(k);
            }
            Check("格区内**没有多余的亮线**：所有亮竖线都在 14+29k ±3 内、亮横线都在 62+29k ±3 内"
                + "（⇒ pitch 与原点被钉死，不可能是 28 或 30）",
                vExtra.Count == 0 && hExtra.Count == 0,
                vExtra.Count == 0 && hExtra.Count == 0
                    ? $"竖 {vSeen.Count} 组 / 横 {hSeen.Count} 组，全部落在预期位置"
                    : "多余亮线：" + string.Join(",", vExtra) + " / " + string.Join(",", hExtra));

            Check($"格线组数 = {UiLayoutGame.ShopCols} 列 × {UiLayoutGame.ShopRows} 行（竖 11 条 / 横 11 条一条不缺）",
                vSeen.Count == UiLayoutGame.ShopCols + 1 && hSeen.Count == UiLayoutGame.ShopRows + 1,
                $"竖组 {vSeen.Count}/{UiLayoutGame.ShopCols + 1}、横组 {hSeen.Count}/{UiLayoutGame.ShopRows + 1}");

            // ── 常量 ↔ 实测值 ────────────────────────────────────────────────
            Check($"`UiLayoutGame.ShopCell` == 实测 pitch {Pitch} × K（K == 1.8）",
                Near(UiLayoutGame.K, 1.8f) && Near(UiLayoutGame.ShopCell, Pitch * UiLayoutGame.K),
                $"ShopCell={UiLayoutGame.ShopCell:0.###} / {Pitch}×K={Pitch * UiLayoutGame.K:0.###}");

            Check($"`UiLayoutGame.ShopGridOrigin` == 实测原点 ({OriginX},{OriginY}) 换算的中性坐标"
                + $"（(14−160)·K, (216−62)·K）",
                Near(UiLayoutGame.ShopGridOrigin.x, (OriginX - 160f) * UiLayoutGame.K)
                && Near(UiLayoutGame.ShopGridOrigin.y, (216f - OriginY) * UiLayoutGame.K),
                $"ShopGridOrigin=({UiLayoutGame.ShopGridOrigin.x:0.#},{UiLayoutGame.ShopGridOrigin.y:0.#}) / "
                + $"期望=({(OriginX - 160f) * UiLayoutGame.K:0.#},{(216f - OriginY) * UiLayoutGame.K:0.#})");

            Check($"`ShopCols/Rows` == 10/10（= 实测 11 条线 − 1）",
                UiLayoutGame.ShopCols == 10 && UiLayoutGame.ShopRows == 10
                && ShopPanel.CellCount == 100,
                $"{UiLayoutGame.ShopCols}×{UiLayoutGame.ShopRows}，CellCount={ShopPanel.CellCount}");

            // ── 运行时的格子中心反算回底图像素（100 格全对）──────────────────
            var mapBad = string.Empty;
            for (var j = 0; j < UiLayoutGame.ShopRows && mapBad.Length < 120; j++)
            {
                for (var i = 0; i < UiLayoutGame.ShopCols; i++)
                {
                    var c = UiLayoutGame.ShopCellCenter(i, j);
                    var px0 = c.x / UiLayoutGame.K + 160f;            // 面板中心坐标 → 原版 px
                    var py0 = 216f - c.y / UiLayoutGame.K;
                    if (!Near(px0, OriginX + Pitch * i + Pitch / 2f)
                        || !Near(py0, OriginY + Pitch * j + Pitch / 2f))
                        mapBad += $"({i},{j})→({px0:0.#},{py0:0.#}) ";
                }
            }
            Check("`ShopCellCenter(i,j)` 反算回底图像素 == 实测格心 (14+29i+14.5, 62+29j+14.5)（100 格全对）",
                mapBad.Length == 0,
                mapBad.Length == 0 ? "0 例外（100 格全对）" : mapBad);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ② 按占格摆放（纯函数：行优先锚点 + 占用集）
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckPlacement()
        {
            // 假货物：1×1 / 2×2 / 2×3 / 1×4 / 2×4（共 23 格）—— 尺寸取自原版口径的典型件
            var stock = new List<ShopEntry>
            {
                new ShopEntry { index = 0, itemId = 101, name = "小符", gridW = 1, gridH = 1 },
                new ShopEntry { index = 1, itemId = 102, name = "盾(2×2)", gridW = 2, gridH = 2 },
                new ShopEntry { index = 2, itemId = 103, name = "大盾(2×3)", gridW = 2, gridH = 3 },
                new ShopEntry { index = 3, itemId = 104, name = "法杖(1×4)", gridW = 1, gridH = 4 },
                new ShopEntry { index = 4, itemId = 105, name = "盔甲(2×4)", gridW = 2, gridH = 4 },
            };

            Program._logger.Clear();
            var layout = ShopPanel.ComputeLayout(stock);

            // **手算**（行优先，不引用被测算法）：见文件头 ② 的推导
            var wantAnchors = new[] { 0, 1, 3, 5, 6 };
            var anchorsOk = layout.Anchor.Length == 5;
            for (var i = 0; anchorsOk && i < wantAnchors.Length; i++) anchorsOk = layout.Anchor[i] == wantAnchors[i];
            Check("逐件锚点格 = 行优先手算 {0,1,3,5,6}（格号 @ (col,row) = (0,0)/(1,0)/(3,0)/(5,0)/(6,0)）",
                anchorsOk,
                "实测 [" + string.Join(",", Array.ConvertAll(layout.Anchor, v => v.ToString())) + "]"
                + " = " + Describe(layout.Anchor));

            Check("摆放件数 = 5、放不下 = 0（23 格 ≪ 100 格）",
                layout.Placed == 5 && layout.Overflow == 0,
                $"Placed={layout.Placed} Overflow={layout.Overflow}");

            Check("占用总数 == Σ gridW×gridH == 23（1+4+6+4+8）",
                ShopPanel.UsedCells(layout.Owner) == 23,
                $"UsedCells={ShopPanel.UsedCells(layout.Owner)} / 100");

            // 逐格占用集：每件覆盖的格（硬编码手算结果）都必须属于它，且**每个格只有一个占用者**
            var wantCells = new[]
            {
                // 1×1 @ (0,0)
                new[] { 0 },
                // 2×2 @ (1,0)
                new[] { 1, 2, 11, 12 },
                // 2×3 @ (3,0)
                new[] { 3, 4, 13, 14, 23, 24 },
                // 1×4 @ (5,0)
                new[] { 5, 15, 25, 35 },
                // 2×4 @ (6,0)
                new[] { 6, 7, 16, 17, 26, 27, 36, 37 },
            };
            var cellsBad = new List<string>();
            for (var i = 0; i < wantCells.Length; i++)
            {
                for (var c = 0; c < wantCells[i].Length; c++)
                {
                    var cell = wantCells[i][c];
                    if (layout.Owner[cell] != i)
                        cellsBad.Add($"第{i}件的格({cell % 10},{cell / 10})占用者=layout.Owner[{cell}]={layout.Owner[cell]}");
                }
            }
            Check("逐格占用集 == 手算的 23 格（每格恰好一个占用者 ⇒ 两两不重叠、没有越界多占）",
                cellsBad.Count == 0,
                cellsBad.Count == 0 ? "23/23 格核对通过" : string.Join("；", cellsBad.ToArray()));

            // 全表登记：把每件的 (col,row) 与占用集打出来（人读的证据，不是断言）
            Console.WriteLine("      │ 买入页摆放（行优先）：" + Describe(layout.Anchor)
                + $"；占用 {ShopPanel.UsedCells(layout.Owner)}/100 格");

            // 图标块几何：尺寸 = w×h 格、左上角与锚点格左上角重合（口径同 InventoryPanel.ItemIconRect）
            var geoBad = string.Empty;
            for (var w = 1; w <= 5; w++)
            {
                for (var h = 1; h <= 5; h++)
                {
                    var size = ShopPanel.IconSize(w, h);
                    var off = ShopPanel.IconOffset(w, h);
                    if (!Near(size.x, w * ShopPanel.CellSize) || !Near(size.y, h * ShopPanel.CellSize))
                        geoBad += $"size({w}×{h}) ";
                    // 左上角重合 ⇔ offset = (size − 1格)/2
                    if (!Near(off.x, (size.x - ShopPanel.CellSize) * 0.5f)
                        || !Near(off.y, -(size.y - ShopPanel.CellSize) * 0.5f))
                        geoBad += $"offset({w}×{h}) ";
                }
            }
            Check("图标块 = (w×ShopCell, h×ShopCell)，偏移使**左上角与锚点格左上角重合**"
                + "（口径同 `InventoryPanel.ItemIconRect`：offset = ((w−1)·Cell/2, −(h−1)·Cell/2)）",
                geoBad.Length == 0, geoBad.Length == 0 ? "w,h ∈ 1..5 全组合通过" : geoBad);

            var invR = InventoryPanel.ItemIconRect(new ItemStack { gridW = 2, gridH = 3 });
            Check("与背包侧同一条口径：`InventoryPanel.ItemIconRect(2×3)` 的偏移/尺寸也是"
                + " `((w−1)·Cell/2, −(h−1)·Cell/2)` / `(w·Cell, h·Cell)`（只是格边长不同）",
                Near(invR.offset.x, (invR.size.x - InventoryPanel.CellW) * 0.5f)
                && Near(invR.offset.y, -(invR.size.y - InventoryPanel.CellH) * 0.5f),
                $"背包 2×3 → size({invR.size.x:0.#},{invR.size.y:0.#}) offset({invR.offset.x:0.#},{invR.offset.y:0.#})；"
                + $"商店 Cell={ShopPanel.CellSize:0.#} vs 背包 CellW={InventoryPanel.CellW:0.#}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ③ 边界：总占格 > 100 ⇒ 点名 Warn 且不覆盖已占格
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckOverflow()
        {
            // 60 件 × (2×3 = 6 格) = 360 格 ≫ 100：10×10 里每 3 行只放得下 5 件（列 0/2/4/6/8）
            var many = new List<ShopEntry>();
            for (var i = 0; i < 60; i++)
                many.Add(new ShopEntry { index = i, itemId = 200 + i, name = "货" + i, gridW = 2, gridH = 3 });

            Program._logger.Clear();
            var layout = ShopPanel.ComputeLayout(many);

            Check("放得下的件数 = 15（10×10 里 2×3 的块：3 行一带 × 5 件，共 3 带 ⇒ 15 件 / 占 90 格）",
                layout.Placed == 15 && ShopPanel.UsedCells(layout.Owner) == 90,
                $"Placed={layout.Placed} Used={ShopPanel.UsedCells(layout.Owner)}/100");

            var wantFirst = new[] { 0, 2, 4, 6, 8, 30, 32, 34, 36, 38, 60, 62, 64, 66, 68 };
            var aBad = string.Empty;
            for (var i = 0; i < wantFirst.Length; i++)
            {
                if (layout.Anchor[i] != wantFirst[i])
                    aBad += $"第{i}件={layout.Anchor[i]}(期望{wantFirst[i]}) ";
            }
            Check("前 15 件的锚点格逐条 = 手算 {0,2,4,6,8} / {30,32,34,36,38} / {60,62,64,66,68}",
                aBad.Length == 0, aBad.Length == 0 ? Describe(layout.Anchor) : aBad);

            var overWarn = 0;
            foreach (var e in Program._logger.All)
            {
                if (e.Level == "WARN" && e.Msg != null && e.Msg.Contains("放不下")) overWarn++;
            }
            Check("放不下的 45 件**逐件点名 Warn**（45 条 `… 放不下 ⇒ 该件不显示 …`，⛔ 不静默丢弃）",
                overWarn == 45,
                $"WARN 放不下 条数 = {overWarn}（期望 45）");

            Check("Warn 文案里点了名（件号 + 尺寸 + 已占格数）：`商品第 15 件「货15」（2×3）放不下 … 已占 90/100 格`",
                Program._logger.Has("WARN", UiLog.Tag, "第 15 件")
                && Program._logger.Has("WARN", UiLog.Tag, "2×3")
                && Program._logger.Has("WARN", UiLog.Tag, "90/100"),
                FirstWarn());

            // 不覆盖：已摆下的 15 件，每一格都必须还是它自己的（第 16..59 件不许抢格）
            var coverBad = string.Empty;
            for (var i = 0; i < 15 && coverBad.Length < 100; i++)
            {
                var col = wantFirst[i] % 10;
                var row = wantFirst[i] / 10;
                for (var r = row; r < row + 3; r++)
                {
                    for (var c = col; c < col + 2; c++)
                    {
                        var cell = r * 10 + c;
                        if (layout.Owner[cell] != i) coverBad += $"格({c},{r})={layout.Owner[cell]}≠{i} ";
                    }
                }
                if (layout.Anchor[i] != wantFirst[i]) coverBad += $"锚点{i} ";
            }
            Check("**不覆盖已占格**：已摆 15 件的 90 格逐格仍是原占用者（放不下的 45 件一个格都没抢）",
                coverBad.Length == 0, coverBad.Length == 0 ? "90/90 格核对通过" : coverBad);

            var lastRowFree = true;
            for (var c = 0; c < 10; c++) if (layout.Owner[90 + c] != -1) lastRowFree = false;
            Check("最后一行（第 9 行，10 格）保持空闲 —— 2×3 的块在那里**放不下**，"
                + "算法没有越界硬塞（整块必须在界内）",
                lastRowFree, "row9 = " + Row9(layout.Owner));

            // 单个超大件（比格区还大）：必须 −1 + Warn，且不占任何格
            var huge = new List<ShopEntry>
            {
                new ShopEntry { index = 0, itemId = 999, name = "超大件", gridW = 11, gridH = 1 },
                new ShopEntry { index = 1, itemId = 998, name = "正常件", gridW = 1, gridH = 1 },
            };
            Program._logger.Clear();
            var hugeLayout = ShopPanel.ComputeLayout(huge);
            Check("比格区还宽的件（11×1）⇒ 放不下 + Warn，**且不影响后面的正常件**（正常件仍摆在 (0,0)）",
                hugeLayout.Anchor[0] == -1 && hugeLayout.Overflow == 1
                && hugeLayout.Anchor[1] == 0 && ShopPanel.UsedCells(hugeLayout.Owner) == 1
                && Program._logger.Has("WARN", UiLog.Tag, "超大件"),
                $"Anchor=[{hugeLayout.Anchor[0]},{hugeLayout.Anchor[1]}] Overflow={hugeLayout.Overflow} "
                + $"Used={ShopPanel.UsedCells(hugeLayout.Owner)}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ④ 卖出页（InventorySlot 入口）+ 源码接线锚点
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckSellPage()
        {
            var items = new List<InventorySlot>
            {
                new InventorySlot
                {
                    index = 0, x = 0, y = 0, occupied = true, isAnchor = true, anchorIndex = 0,
                    item = new ItemStack { itemId = 301, name = "盾(2×2)", gridW = 2, gridH = 2 },
                },
                // 背包里的"被覆盖格"：item == null ⇒ 卖出页必须跳过（不占格）
                new InventorySlot { index = 1, x = 1, y = 0, occupied = true, isAnchor = false, anchorIndex = 0 },
                new InventorySlot
                {
                    index = 2, x = 2, y = 0, occupied = true, isAnchor = true, anchorIndex = 2,
                    item = new ItemStack { itemId = 302, name = "杖(1×4)", gridW = 1, gridH = 4 },
                },
            };

            Program._logger.Clear();
            var layout = ShopPanel.ComputeLayout(items);

            Check("卖出页：锚点格物品按占格摆（2×2 → 格(0,0) 占 4 格；被覆盖格跳过；1×4 → 格(2,0) 占 4 格 ⇒ 共占 8 格）",
                layout.Anchor[0] == 0 && layout.Anchor[1] == -1 && layout.Anchor[2] == 2
                && layout.Placed == 2 && ShopPanel.UsedCells(layout.Owner) == 8,
                $"Anchor=" + Describe(layout.Anchor) + $" Used={ShopPanel.UsedCells(layout.Owner)}/100");

            Check("卖出页：被覆盖格（`item == null`）跳过时也点名 Warn（⛔ 不静默）",
                Program._logger.Has("WARN", UiLog.Tag, "可卖物品第 1 条为空"),
                FirstWarn());
        }

        private static void CheckSourceWiring()
        {
            var shopSrc = File.ReadAllText(Path.Combine(Program.UiDir, "ShopPanel.cs"), Encoding.UTF8);
            var npcSrc = File.ReadAllText(Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts",
                "Module", "Npc", "NpcShop.cs"), Encoding.UTF8);

            // ⚠️ 以下是**源码文本锚点（L1）**，只用来钉"接线没被改回去"；行为判据是上面 ①②③④ 的真跑断言。
            var addEntry = Body(npcSrc, "private void AddEntry(ItemStack st, int count, int playerGold)");
            Check("[L1] `NpcShop.AddEntry` 真把占格拷进 `ShopEntry`（gridW = gw / gridH = gh），"
                + "且缺值分支点名 Warn（含 `item_c.grid_w/grid_h`）",
                addEntry.Contains("gridW = gw,") && addEntry.Contains("gridH = gh,")
                && addEntry.Contains("item_c.grid_w/grid_h")
                && addEntry.Contains("Log.Warn(\"Npc\""),
                "见 NpcShop.AddEntry");

            Check("[L1] `NpcShop.Build` 每建一次货架复位「只报一次」标记 `_warnedBadGrid = false`",
                Body(npcSrc, "public void Build(NpcDef npc, int mapSeed, int playerLevel, IItemModule item, int playerGold)")
                    .Contains("_warnedBadGrid = false;"),
                "见 NpcShop.Build");

            var applyStock = Body(shopSrc, "private void ApplyStock(List<ShopEntry> stock)");
            var applyItems = Body(shopSrc, "private void ApplyPlayerItems(List<InventorySlot> items)");
            Check("[L1] 买入页/卖出页都走 `ComputeLayout(...)`（按占格），并把占用表拷进面板 `_owner`",
                applyStock.Contains("ComputeLayout(stock)") && applyItems.Contains("ComputeLayout(items)")
                && applyStock.Contains("_owner[i] = layout.Owner[i]")
                && applyItems.Contains("_owner[i] = layout.Owner[i]"),
                "见 ShopPanel.ApplyStock / ApplyPlayerItems");

            var click = Body(shopSrc, "private void OnCellClick(int cellIndex)");
            Check("[L1] `OnCellClick` 改读**占用表** `_owner[cellIndex]`（⛔ 不再 `index → stock[index]` 等号映射）",
                click.Contains("var itemIndex = _owner[cellIndex];")
                && !click.Contains("stock[cellIndex]")
                && !click.Contains("items[cellIndex]"),
                "见 ShopPanel.OnCellClick");

            var buildGrid = Body(shopSrc, "private void BuildGrid()");
            Check("[L1] `BuildGrid` 仍只建「命中区 + 图标层 + 数量」（格线**不重画**：底图自带），"
                + "且格子中心仍取 `UiLayoutGame.ShopCellCenter(col, row)`",
                buildGrid.Contains("UiLayoutGame.ShopCellCenter(col, row)")
                && !buildGrid.Contains("GridLine")
                && !buildGrid.Contains("DrawLine"),
                "见 ShopPanel.BuildGrid");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 工具
        // ═════════════════════════════════════════════════════════════════════

        private static bool Near(float a, float b, float eps = 1e-3f) => Math.Abs(a - b) <= eps;

        private static string Describe(int[] anchors)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < anchors.Length; i++)
            {
                if (anchors[i] < 0) continue;
                sb.Append('(').Append(anchors[i] % 10).Append(',').Append(anchors[i] / 10).Append(')');
            }
            return sb.ToString();
        }

        private static string Row9(int[] owner)
        {
            var sb = new StringBuilder();
            for (var c = 0; c < 10; c++) sb.Append(owner[90 + c]).Append(' ');
            return sb.ToString().Trim();
        }

        private static string FirstWarn()
        {
            foreach (var e in Program._logger.All)
            {
                if (e.Level == "WARN") return e.ToString();
            }
            return "（没有 WARN —— 断言失败原因）";
        }

        /// <summary>某列在原版 px 空间里的平均亮度（y 从 y0 到 y1，含两端）。</summary>
        private static float ColumnMean(byte[] rgba, int w, int x, int y0, int y1)
        {
            var s = 0f;
            var n = 0;
            for (var y = y0; y <= y1; y++) { s += Lum(rgba, (y * w + x) * 4); n++; }
            return n == 0 ? 0f : s / n;
        }

        /// <summary>某行在原版 px 空间里的平均亮度（x 从 x0 到 x1，不含 x1）。</summary>
        private static float RowMean(byte[] rgba, int w, int y, int x0, int x1)
        {
            var s = 0f;
            var n = 0;
            for (var x = x0; x < x1; x++) { s += Lum(rgba, (y * w + x) * 4); n++; }
            return n == 0 ? 0f : s / n;
        }

        private static float Lum(byte[] rgba, int i)
            => 0.299f * rgba[i] + 0.587f * rgba[i + 1] + 0.114f * rgba[i + 2];

        /// <summary>取某个方法声明之后配对大括号包起来的那一段（含声明行）。</summary>
        private static string Body(string src, string marker)
        {
            var i = src.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return string.Empty;
            var open = src.IndexOf('{', i);
            if (open < 0) return string.Empty;
            var depth = 0;
            for (var j = open; j < src.Length; j++)
            {
                if (src[j] == '{') depth++;
                else if (src[j] == '}')
                {
                    depth--;
                    if (depth == 0) return src.Substring(i, j - i + 1);
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// 解 PNG 的 RGBA8 像素（只处理 8bit / 非隔行 / 色彩类型 2·6）。
        /// 与 `W3GameCheck` 里那份同源实现（那边是 `private` 且按**资源路径**取图；这里按**文件路径**，
        /// 免得为了复用去改另一个大文件而与其并发写入冲突）。解不出来返回 false ⇒ 调用方如实报失败。
        /// </summary>
        private static bool TryDecodeRgba(string file, out int w, out int h, out byte[] rgba)
        {
            w = 0; h = 0; rgba = null;
            if (!File.Exists(file)) return false;
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
                using (var z = new ZLibStream(ms, CompressionMode.Decompress))
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
    }
}
