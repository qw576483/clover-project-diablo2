// ─────────────────────────────────────────────────────────────────────────────
// uicheck · Fix4Check.cs
// 四条「实机画面可见缺陷」的**离线判据**（逐条都能失败，且各带退化样本）：
//   ① 启动屏 logo：占位色不得是白色（uGUI 的 `sprite == null` = 纯白四边形 ⇒ 启动头几帧闪白块）
//   ② 物品图标：取不到图标路径时不得用**品质色**（普通品质 = 纯白 ⇒ 白方块），必须用 `MissingIconColor`
//   ③ 任务日志正文：原版三种状态各自的行数 + 正文框 == 原版实测黑芯 316×129（防"内容被布局挤没"）
//   ④ 死亡屏标题条：原版 `youdiedsoftcore.dc6` 是**两块**（256×54 + 40×54 = 整幅 296×54，
//      正文「你損失金錢數量為」）⇒ 两块都要在盘、尺寸对得上、且第 1 块**有笔画**（不是空帧）
//
// 判据形态与既有断言同风格：源码文本断言 + 纯函数断言 + PNG（IHDR / 原始 IDAT 笔画代理）。
// ⛔ 没有任何"读一次实机截图"的依赖 —— 三条是源码/几何，一条是原始素材字节。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.UI;

namespace Uicheck
{
    internal static class Fix4Check
    {
        private static void Check(string what, bool ok, string detail) => Program.Check(what, ok, detail);

        public static void Run()
        {
            Console.WriteLine("── (u56-fix4) 实机四条画面缺陷的离线判据：① 启动屏白块 ② 商店白方块 ③ 任务日志正文 ④ 死亡屏横幅取帧 ──");
            BootLogo();
            ItemIcon();
            QuestText();
            DeathBanner();
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ① 启动屏 logo：占位色不是白色
        //    判据 = 源码里 `Logo` 节点经 `UiArt.Panel(..., 占位色, ...)` 建出（不是 `UiArt.Art` 的白兜底），
        //    且那条占位色**不是** `Color.white`。
        // ═════════════════════════════════════════════════════════════════════
        private static void BootLogo()
        {
            var src = File.ReadAllText(Path.Combine(Program.UiDir, "BootPanel.cs"));

            Check("① 启动屏 logo 走 `UiArt.Panel` + `SetSprite`（占位色可由调用方给），⛔ 不是 `UiArt.Art` 的白兜底",
                src.Contains("UiArt.Panel(screen, \"Logo\"") && src.Contains("UiArt.SetSprite(logo,"),
                src.Contains("UiArt.Panel(screen, \"Logo\"") ? "读到 UiArt.Panel(screen, \"Logo\") + SetSprite" : "未读到（仍是 UiArt.Art 白兜底？）");

            Check("① logo 占位色 = 启动屏底色 `BgColor`（0.02,0.02,0.03），⛔ 不是纯白 —— 白色占位就是那块白矩形",
                !Regex.IsMatch(src, @"UiArt\.Panel\(screen, ""Logo""[^;]*Color\.white") &&
                Regex.IsMatch(src, @"BgColor\s*=\s*new Color\(0\.02f, 0\.02f, 0\.03f"),
                "占位色 = BgColor");

            // 素材在位 + IHDR（启动屏 logo 的预期原版素材）
            var logo = ResPaths.Frame(ResPaths.MenuLogo, 0);
            var okSize = PngSize(logo, out var w, out var h) && w == 319 && h == 177;
            Check("① 启动屏 logo 的原版素材在盘且 IHDR == 319×177（`D2/UI/Logo/logo_0` = DIABLO II 火焰字标）",
                okSize, $"path={logo} size={w}×{h}");

            // 退化样本：把占位色换回 `Color.white` ⇒ 同一判据必须变红
            var deg = src.Replace("UiArt.Panel(screen, \"Logo\", LogoSize, new Vector2(0f, 180f), BgColor, false)",
                                  "UiArt.Panel(screen, \"Logo\", LogoSize, new Vector2(0f, 180f), Color.white, false)");
            Check("① 退化样本（占位色换回 `Color.white` = 修前形状）喂进**同一判据** ⇒ 必须变红",
                deg != src && Regex.IsMatch(deg, @"UiArt\.Panel\(screen, ""Logo""[^;]*Color\.white"),
                $"源码被替换={deg != src}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ② 物品图标：取不到路径 ⇒ 暗色占位（不是品质色；普通品质的品质色 = 纯白 = 白方块）
        // ═════════════════════════════════════════════════════════════════════
        private static void ItemIcon()
        {
            var src = File.ReadAllText(Path.Combine(Program.UiDir, "D2Icon.cs"));

            // 判据 = `path` 为空的那一段里出现的是 MissingIconColor，而不是 ItemQualityColor
            var idx = src.IndexOf("var path = ItemIconPath(item.itemId);", StringComparison.Ordinal);
            var seg = idx >= 0 ? src.Substring(idx, Math.Min(600, src.Length - idx)) : string.Empty;
            var cut = seg.IndexOf("return;", StringComparison.Ordinal);

            Check("② `D2Icon.ApplyItemIcon` 的「取不到图标路径」分支用 `MissingIconColor`（暗色占位）",
                idx >= 0 && seg.Contains("MissingIconColor") && seg.Contains("icon.sprite = null;"),
                idx >= 0 ? "读到 MissingIconColor" : "未定位到 `var path = ItemIconPath(item.itemId);`");

            Check("② ⛔ 该分支不再用品质色（普通品质 = 纯白 ⇒ 画出来就是那块白方块）",
                cut > 0 && !seg.Substring(0, cut).Contains("ItemQualityColor.Of("),
                cut > 0 ? "0 命中 ItemQualityColor.Of(" : "分支结构变了（找不到 return）");

            // 退化样本：把暗色占位换回品质色 ⇒ 必须变红
            var deg = new Regex(@"icon\.color = MissingIconColor;").Replace(src,
                "icon.color = ItemQualityColor.Of(item.quality);", 1);
            var degIdx = deg.IndexOf("var path = ItemIconPath(item.itemId);", StringComparison.Ordinal);
            var degSeg = degIdx >= 0 ? deg.Substring(degIdx, Math.Min(600, deg.Length - degIdx)) : string.Empty;
            var degCut = degSeg.IndexOf("return;", StringComparison.Ordinal);
            Check("② 退化样本（换回 `ItemQualityColor.Of(...)` = 修前形状）喂进**同一判据** ⇒ 必须变红",
                deg != src && degCut > 0 && degSeg.Substring(0, degCut).Contains("ItemQualityColor.Of("),
                $"源码被替换={deg != src}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ③ 任务日志正文：行数按状态（原版串口径）+ 正文框 == 原版实测黑芯 316×129
        // ═════════════════════════════════════════════════════════════════════
        private static int LineCount(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            var n = 1;
            for (var i = 0; i < text.Length; i++) if (text[i] == '\n') n++;
            return n;
        }

        private static QuestStateDto Quest(QuestState state, string objective, int progress, int required)
            => new QuestStateDto
            {
                questId = 1,
                name = "邪惡洞穴",
                state = state,
                objective = objective,
                progress = progress,
                required = required,
            };

        private static void QuestText()
        {
            var notStarted = QuestLogPanel.TextOf(Quest(QuestState.NotStarted, "去洞穴深處", 0, 4));
            var inProgress = QuestLogPanel.TextOf(Quest(QuestState.InProgress, "殺光洞穴裡的怪物", 1, 4));
            var ready = QuestLogPanel.TextOf(Quest(QuestState.ReadyToTurnIn, "回報阿卡拉", 4, 4));

            Check("③ 未接取 ⇒ 整屏**只有一行**原版串「沒有進行中的任務。」（不画任务名/目标）",
                notStarted == QuestLogPanel.TextNoActiveQuest && LineCount(notStarted) == 1,
                $"行数={LineCount(notStarted)} 文本=\"{notStarted}\"");

            Check("③ 进行中 ⇒ **三行**（任务名 | 目标 | 进度行），正文区不该只剩一行",
                LineCount(inProgress) == 3 && inProgress.Contains(QuestLogPanel.TextMonstersRemainingPrefix),
                $"行数={LineCount(inProgress)} 预览=\"{inProgress.Replace("\n", " | ")}\"");

            Check("③ 可交付 ⇒ **两行**（任务名 | 目标）",
                LineCount(ready) == 2, $"行数={LineCount(ready)}");

            var boxOk = Math.Abs(UiLayoutGame.QuestTextBoxSize.x - 316f * UiLayoutGame.K) < 0.01f
                        && Math.Abs(UiLayoutGame.QuestTextBoxSize.y - 129f * UiLayoutGame.K) < 0.01f
                        && Math.Abs(UiLayoutGame.QuestTextWidth
                                    - (316f - 2f * UiLayoutGame.QuestTextPadX) * UiLayoutGame.K) < 0.01f;
            Check("③ 正文框 == 原版 `questbackground.dc6` 实测黑芯 316×129（×1.8），文字框宽 = 框宽 − 左右各 8 原版px",
                boxOk, $"框={UiLayoutGame.QuestTextBoxSize.x}×{UiLayoutGame.QuestTextBoxSize.y} 文本宽={UiLayoutGame.QuestTextWidth}");

            // 退化样本：目标行被吞掉 ⇒ 行数变 2 ⇒ 同一"必须 3 行"的判据变红
            var deg = LineCount(QuestLogPanel.TextOf(Quest(QuestState.InProgress, string.Empty, 1, 4)));
            Check("③ 退化样本（进行中但 objective 为空 ⇒ 行数 2）喂进同一判据 ⇒ 必须变红",
                deg != 3, $"退化样本行数={deg}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ④ 死亡屏标题条：原版 DC6 的两块（296×54 拼接）
        // ═════════════════════════════════════════════════════════════════════
        private static void DeathBanner()
        {
            Check("④ 标题条 = 原版 `youdiedsoftcore.dc6` 的**两块**（块数 == 2，整幅 296×54）",
                ResPaths.FrameCountBannerYouDiedSoftCore == 2
                && Math.Abs(UiLayoutGame.DeathBannerFullSize.x - 296f * UiLayoutGame.K) < 0.01f
                && UiLayoutGame.DeathBannerTiles.Length == 2
                && UiLayoutGame.DeathBannerTiles[0].w == 256f && UiLayoutGame.DeathBannerTiles[1].w == 40f
                && UiLayoutGame.DeathBannerTiles[1].x == 256f,
                $"块数={ResPaths.FrameCountBannerYouDiedSoftCore} 整幅={UiLayoutGame.DeathBannerFullSize.x}");

            var bad = new List<string>();
            var okPix = true;
            for (var i = 0; i < ResPaths.FrameCountBannerYouDiedSoftCore; i++)
            {
                var p = ResPaths.BannerYouDiedSoftCoreTile(i);
                var t = UiLayoutGame.DeathBannerTiles[i];
                if (!PngSize(p, out var w, out var h))
                {
                    bad.Add(p + "=不在盘");
                    continue;
                }
                if (Math.Abs(w - t.w) > 0.5f || Math.Abs(h - t.h) > 0.5f) bad.Add($"{p}={w}×{h}≠{t.w}×{t.h}");
                if (i == 1 && !HasStroke(p)) { okPix = false; bad.Add(p + "=空帧（无笔画）"); }
            }
            Check("④ 两块素材都在盘、IHDR 与 `DeathBannerTiles` 逐值相等",
                bad.Count == 0,
                bad.Count == 0 ? "2 块全部在盘且尺寸相符" : string.Join("；", bad.ToArray()));

            Check("④ 第 2 块（「為」）**有笔画**（原始 IDAT 的去重字节数 ≥ 8 —— 空帧/全透明帧只有 1~2 个值）",
                okPix, okPix ? "有笔画" : "空帧");

            Check("④ 退化样本（全 0 / 全常量字节流 = 空帧）喂进同一判据 ⇒ 必须变红",
                StrokeOf(new byte[4096]) < 8 && StrokeOf(Fill(4096, 0xAB)) < 8,
                $"全 0 去重字节数={StrokeOf(new byte[4096])} 全常量去重字节数={StrokeOf(Fill(4096, 0xAB))}");
        }

        private static byte[] Fill(int n, byte v)
        {
            var a = new byte[n];
            for (var i = 0; i < n; i++) a[i] = v;
            return a;
        }

        /// <summary>原始 IDAT 解压后的去重字节数（≥8 视为"有笔画"）。</summary>
        private static int StrokeOf(byte[] raw)
        {
            var seen = new bool[256];
            var n = 0;
            for (var i = 0; i < raw.Length; i++)
            {
                if (seen[raw[i]]) continue;
                seen[raw[i]] = true;
                n++;
            }
            return n;
        }

        private static bool HasStroke(string resPath)
        {
            if (!PngFile(resPath, out var file)) return false;
            try
            {
                var b = File.ReadAllBytes(file);
                var idat = new MemoryStream();
                var i = 8;
                while (i + 8 <= b.Length)
                {
                    var len = (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
                    var type = System.Text.Encoding.ASCII.GetString(b, i + 4, 4);
                    if (len < 0 || i + 12 + len > b.Length) break;
                    if (type == "IDAT") idat.Write(b, i + 8, len);
                    if (type == "IEND") break;
                    i += 12 + len;
                }

                var bytes = idat.ToArray();
                if (bytes.Length == 0) return false;
                using var dz = new ZLibStream(new MemoryStream(bytes), CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                dz.CopyTo(outMs);
                return StrokeOf(outMs.ToArray()) >= 8;
            }
            catch (Exception e)
            {
                Console.WriteLine("   [note] 解 PNG 失败：" + resPath + ": " + e.Message);
                return false;
            }
        }

        private static bool PngFile(string resPath, out string file)
        {
            file = Path.Combine(Program.ResourceRoot, "Clover", resPath.Replace('/', Path.DirectorySeparatorChar) + ".png");
            return File.Exists(file);
        }

        private static bool PngSize(string resPath, out int w, out int h)
        {
            w = h = 0;
            if (!PngFile(resPath, out var file)) return false;
            var b = File.ReadAllBytes(file);
            if (b.Length < 24) return false;
            w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            return true;
        }
    }
}
