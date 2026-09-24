// ═══════════════════════════════════════════════════════════════════════════
//
//  判什么 —— 把「automap 面板开着却什么都没画」这句**表现类**抱怨，变成可离线判的数：
//   ① **载体尺寸有效**：`ApplyMap` 的贴图尺寸公式对**真实地图尺寸**（56×40 = 罗格营地）必须
//      非退化（>0），换算到画布（× `UiLayoutGame.K`）也必须 > 0。判的是"尺寸会不会等于 0"，
//      不是"够不够大"（观感不在这里判）。
//   ② **数据非空 ⇒ 一定有墨（ink）**：对 `AutoMapCel.CelPixels` 里**每一个真实导出的帧**，
//      各铺一格已探索 ⇒ `RenderExplored` 必须 **每个有 cel 的格都写出 ≥1 图元**
//      （`drawn == cellsWithCel`）且 `opaquePixels > 0`。这一条就是"不画"的红灯：
//      只要有任何一帧像素表为空 / 几何把像素全裁掉，这里立刻红。
//   ③ **alpha 有效**：`CreatePalette()` 里**帧数据真正用到的调色板索引**必须 a > 0
//      （索引 0 必须透明 —— 那是"不画"的口径，见 `CreatePalette`）。若某帧用到的索引落在
//      透明带上，贴图写进去也是全透明 ⇒ 屏幕上看不见（本条判的就是这种"写了等于没写"）。
//   ④ **载体接线**（源码级，口径 = 本宿主既有的 P5Check/V6Check 同款"必须存在的写法"）：
//      `RawImage` 必须被赋 `texture`、必须 `Apply()` 上传、`raycastTarget` 必须 false、
//      尺寸必须由贴图尺寸现算（`sizeDelta = … _texW … _texH …`）。
//      已知正确样本命中 + 已知错误样本不命中（否则恒真 = 假判据）。
//
//  为什么能离线判：①②③ 全部走**产品自己的纯函数**（`RenderExplored` / `BlitInto` /
//  `CreatePalette` / `CelPixels`），判据与产品同源、不留第二份画法；④⑤ 是字符级断言。
//  ④ 只判"必须存在的写法"，**不判风格**（同行既有做法）。
//
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.IO;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.UI;
using UnityEngine;

namespace Uicheck
{
    internal static class AutomapCarrierCheck
    {
        private static void Check(string what, bool ok, string detail) => Program.Check(what, ok, detail);

        public static void Run()
        {
            Console.WriteLine("── (automap-panel) automap 绘制载体有效性：尺寸 / 墨量 / alpha / 接线 ──");

            // ── ① 载体尺寸：真实地图尺寸（罗格营地 56×40）下贴图与画布尺寸均非退化 ──────────
            const int w = 56, h = 40;
            var stepX = AutoMapCel.W / 2;
            var stepY = AutoMapCel.W / 4;
            var texW = (w + h - 2) * stepX + AutoMapCel.W;
            var texH = (w + h - 2) * stepY + AutoMapCel.H;
            var k = UiLayoutGame.K;
            Check("automap ① 载体尺寸非退化：56×40 图 ⇒ 贴图 >0 且画布尺寸（×UiLayoutGame.K）>0",
                texW > 0 && texH > 0 && k > 0f && texW * k > 0f && texH * k > 0f,
                $"贴图={texW}×{texH} 原版px；K={k:0.###} ⇒ 画布={texW * k:0.#}×{texH * k:0.#}px"
                + "（公式与 MiniMapPanel.ApplyMap 同一份常量）");

            // ── ② 数据非空 ⇒ 一定有墨：真实导出帧逐帧铺一格，必须都写出 ≥1 图元 ────────────
            var frames = new List<int>(AutoMapCel.CelPixels != null ? AutoMapCel.CelPixels.Keys : new List<int>());
            frames.Sort();
            Check("automap ② 绘制源非空（`AutoMapCel.CelPixels` 的真实帧数 > 0）",
                frames.Count > 0, $"帧数={frames.Count}（生成物 tools/probes/gen_automap.py）");

            if (frames.Count > 0)
            {
                // 一帧一格：width = 帧数、height = 1（几何与产品同源：整张图一起 blit）
                var mw = frames.Count;
                var map = new MinimapArgs { areaId = (int)AreaId.Town, width = mw, height = 1, seed = 20260924 };
                var explored = new bool[mw];
                for (var i = 0; i < mw; i++)
                {
                    map.tiles.Add(MinimapArgs.TileWalkable);
                    map.cels.Add((short)frames[i]);
                    map.celsOver.Add((short)-1);
                    explored[i] = true;
                }
                var mx = (mw + 1 - 2) * stepX + AutoMapCel.W;
                var my = (mw + 1 - 2) * stepY + AutoMapCel.H;
                var palette = MiniMapPanel.CreatePalette();
                int withCel, drawn, opaque;
                MiniMapPanel.CountDrawn(map, explored, mx, my, palette, out withCel, out drawn, out opaque);
                Check("automap ② 已探索且数据非空 ⇒ 一定有墨：每帧都写出 ≥1 图元（drawn == cellsWithCel > 0）",
                    withCel == frames.Count && drawn == withCel && opaque > 0,
                    $"真实帧 {frames.Count} 个逐帧各一格：cellsWithCel={withCel} drawn={drawn} opaquePixels={opaque}"
                    + $"（贴图={mx}×{my}）");

                // 逐帧单独 blit：任何一帧像素表为空 / 几何把它全裁掉 ⇒ 这一条红（点名是哪一帧）
                var bad = new List<int>();
                for (var i = 0; i < frames.Count; i++)
                {
                    var scratch = new Color32[mx * my];
                    var one = MiniMapPanel.BlitInto(scratch, mx, my, 1, frames[i], i, 0, palette);
                    if (one <= 0) bad.Add(frames[i]);
                }
                Check("automap ② 任何一帧都不许静默空转（逐帧单独 blit > 0）",
                    bad.Count == 0, bad.Count == 0 ? $"全部 {frames.Count} 帧 > 0" : ("空帧=" + string.Join(",", bad)));

                // ── ③ alpha 有效：帧真正用到的调色板索引不许落在透明带上 ─────────────────
                var usedTransparent = new List<string>();
                var used = new HashSet<int>();
                foreach (var f in frames)
                {
                    //    （`uicheck exit=1` / `error CS0136: 无法在此范围中声明名为"px"的局部变量`），
                    //    导致 `offline-hosts` 由绿转红。**只改局部变量名（4 处），断言口径/阈值/文案一字未动。**
                    byte[] celPx;
                    if (!AutoMapCel.CelPixels.TryGetValue(f, out celPx) || celPx == null) continue;
                    for (var i = 0; i + 2 < celPx.Length; i += 3)
                    {
                        var idx = celPx[i + 2];
                        if (used.Add(idx) && palette[idx].a == 0) usedTransparent.Add(f + ":idx" + idx);
                    }
                }
                Check("automap ③ alpha 有效：帧用到的调色板索引在 `CreatePalette()` 里 a > 0（索引 0 除外）",
                    usedTransparent.Count == 0,
                    usedTransparent.Count == 0
                        ? $"用到 {used.Count} 个索引，全部 a>0（索引 0 = 透明 = 原版\"不画\"口径）"
                        : ("命中透明带：" + string.Join(",", usedTransparent)));
            }

            // ── ④ 载体接线（源码级；口径同 P5Check/V6Check） ──────────────────────────
            var srcPath = Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts", "UI", "MiniMapPanel.cs");
            var src = File.Exists(srcPath) ? File.ReadAllText(srcPath) : string.Empty;
            var code = Strip(src);

            var hasTextureAssign = code.Contains("_raw.texture = _tex;");
            var hasUpload = code.Contains("_tex.Apply(");
            var hasNoRaycast = code.Contains("_raw.raycastTarget = false;");
            // needle 必须**锚定接收者**：只写 `sizeDelta = new Vector2(_texW *` 会同时命中
            //    `UpdateView` 里 `_overlay.sizeDelta = …` 那一行 ⇒ 把 ApplyMap 那行退化成 (0,0) 也照样绿
            var sizeApplyMap = System.Text.RegularExpressions.Regex.IsMatch(
                code, @"_raw\.rectTransform\.sizeDelta\s*=\s*new\s+Vector2\(\s*_texW\s*\*");
            var sizeUpdateView = System.Text.RegularExpressions.Regex.IsMatch(
                code, @"_overlay\.sizeDelta\s*=\s*new\s+Vector2\(\s*_texW\s*\*");
            Check("automap ④ 载体接线：`_raw.texture = _tex;` + `_tex.Apply(` 上传 + `raycastTarget=false`"
                  + " + 载体尺寸由贴图尺寸现算（ApplyMap 与 UpdateView 两处各自点名）",
                hasTextureAssign && hasUpload && hasNoRaycast && sizeApplyMap && sizeUpdateView,
                $"texture 赋值={hasTextureAssign} Apply上传={hasUpload} 不吃射线={hasNoRaycast}"
                + $" ApplyMap.sizeDelta∝tex={sizeApplyMap} UpdateView.sizeDelta∝tex={sizeUpdateView}");

            // ── ⑤ needle 自检：正样本命中 + 负样本不命中（换 needle 的两次自检） ────────
            var needle = @"_raw\.rectTransform\.sizeDelta\s*=\s*new\s+Vector2\(\s*_texW\s*\*";
            var pos = System.Text.RegularExpressions.Regex.IsMatch(
                "                _raw.rectTransform.sizeDelta = new Vector2(_texW * UiLayoutGame.K, _texH * UiLayoutGame.K);",
                needle);
            var neg = System.Text.RegularExpressions.Regex.IsMatch(
                "                _raw.rectTransform.sizeDelta = new Vector2(0f, 0f);   // 退化样本",
                needle);
            // 负样本 2 = 只有 UpdateView 那一处还在（旧 needle 会被它骗绿 —— 这就是换 needle 的原因）
            var neg2 = System.Text.RegularExpressions.Regex.IsMatch(
                "                _overlay.sizeDelta = new Vector2(_texW * k, _texH * k);",
                needle);
            Check("automap ④ needle 自检：已知正确样本命中 + 两个已知错误样本都不命中（含「只命中 UpdateView」那一支）",
                pos && !neg && !neg2,
                $"正样本命中={pos}；（尺寸=0）负样本={neg}；（只有 UpdateView 那行）负样本2={neg2} —— 两个都必须 False");

            //   契约（`Module/Contracts.cs` 的 `MinimapArgs.playerX/Y` 注释）=「玩家所在格」。
            //   **完全以出生点为中心**（L3：玩家走到 (22,26) 开图，`mapPlayer==livePlayer=0`，
            //   叠加层位移与"玩家在出生点旁"那局逐字节相同）⇒ 用户说"地图没画出来"。
            //   ⇒ 本条把"源头必须填玩家当前格"钉成可离线判的数；退化（填回 SpawnPoint）必须变红。
            var mapSrcPath = Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts", "Module", "Map", "MapModule.cs");
            var mapSrc = File.Exists(mapSrcPath) ? Strip(File.ReadAllText(mapSrcPath)) : string.Empty;
            var contractPath = Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts", "Module", "Contracts.cs");
            // 契约锚**必须读原文**（不许 `Strip`）：它判的就是 `/// <summary>` 注释本身
            var contractSrc = File.Exists(contractPath) ? File.ReadAllText(contractPath) : string.Empty;

            var pxToPlayer = mapSrc.Contains("playerX = _hasLastPlayerGrid ? _lastPlayerGrid.x : _grid.SpawnPoint.x");
            var pyToPlayer = mapSrc.Contains("playerY = _hasLastPlayerGrid ? _lastPlayerGrid.y : _grid.SpawnPoint.y");
            Check("automap ⑥ `MapModule.BuildMinimap` 的 `playerX/Y` 填**玩家当前格**（`_lastPlayerGrid`），"
                  + "⛔ 不是出生点 —— 与 `Contracts.cs` 的「玩家所在格」契约同口径",
                pxToPlayer && pyToPlayer,
                $"playerX 填玩家格={pxToPlayer} playerY 填玩家格={pyToPlayer}"
                + "（回落到 `_grid.SpawnPoint` 只在玩家还没换过格时发生，含 `_view == null` 的离线宿主）");

            // 契约锚：注释改了 ⇒ 本条要重判（否则"实现符合契约"这句话就失去了依据）
            Check("automap ⑥ 契约锚仍在：`Contracts.cs` 仍把 `playerX/playerY` 注释为「玩家所在格」",
                contractSrc.Contains("玩家所在格 X") && contractSrc.Contains("玩家所在格 Y"),
                "命中「玩家所在格 X」/「玩家所在格 Y」"
                + "（⛔ 若这里红：说明契约改了，⑥ 的判据要跟着重判，不许直接删断言）");

            // needle 自检：正样本命中 + 退化样本（= 改回出生点）不命中
            var needlePlayer = "_hasLastPlayerGrid ? _lastPlayerGrid.x : _grid.SpawnPoint.x";
            var posPlayer = ("                playerX = _hasLastPlayerGrid ? _lastPlayerGrid.x : _grid.SpawnPoint.x,").Contains(needlePlayer);
            var negPlayer = ("                playerX = _grid.SpawnPoint.x,").Contains(needlePlayer);
            Check("automap ⑥ needle 自检：正样本命中 + 退化样本（填回 `_grid.SpawnPoint.x`）不命中",
                posPlayer && !negPlayer, $"正样本命中={posPlayer}；退化样本命中={negPlayer}（必须为 False）");

            Console.WriteLine();
        }

        /// <summary>去掉注释（同 V6Check.Strip 口径：逐行丢注释行 + 去行尾注释）。</summary>
        private static string Strip(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var rawLine in s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var t = rawLine.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("/*", StringComparison.Ordinal)
                    || t.StartsWith("*", StringComparison.Ordinal) || t.StartsWith("*/", StringComparison.Ordinal))
                    continue;
                var i = rawLine.IndexOf("//", StringComparison.Ordinal);
                sb.Append(i >= 0 ? rawLine.Substring(0, i) : rawLine).Append('\n');
            }
            return sb.ToString();
        }
    }
}
