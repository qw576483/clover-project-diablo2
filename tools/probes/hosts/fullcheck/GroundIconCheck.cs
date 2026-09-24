// ─────────────────────────────────────────────────────────────────────────────
//
// 判据（只加断言，**不改**本宿主任何既有判据）：地面物品画的是**原版物品图**，
// 而不是一块按品质着色的占位四边形。离线可判的部分：
//   ① 口径统一：`GroundItemVisual.IconPathOf(item)` **逐行等于**
//      `D2Icon.ItemIconPath(itemId)`（= 背包/装备/腰带/商店用的那张）—— 两条链只能有一张图；
//   ② 图真的在磁盘上：`client/Assets/Resources/Clover/<path>.png` 存在，且是**真 PNG**
//      （签名 + IHDR 尺寸 ≥ 8×8，且不是 1×1 的纯色小片）；
//   ③ 缺图那 14 件（资料片职业专属装备）仍然是"路径拼得出、磁盘上确实没有"
//      ⇒ 它们走的是 `ViewModule` 里那条**品质色块**回退（可见的缺失信号，不是静默变透明）；
//   ④ 色调：`Normal` 品质 ⇒ **纯白**（普通物品逐像素等于原版）；`Magic` ⇒ 明显偏蓝；
//   ⑤ 过程断言（防"改错地方"）：`ViewModule.cs` 的地面物品链确实接了 `GroundItemVisual`，
//
// 强度如实说明（不许夸大）：本文件证明的是**素材与取法**（路径、文件、色调、接线）；
//    "运行时每件地面物品的 `SpriteRenderer.sprite` 真的不是占位块" 需要真跑，
//    由实机 Play 会话的节点转储承担（`tools/probes/drivers/groundicon_dump.cs`）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using Diablo2.Module.Item;
using Diablo2.Module.View;
using Diablo2.UI;
using Table;
using UnityEngine;

namespace FullCheck
{
    internal static class GroundIconCheck
    {
        /// <summary>返回失败条数（由 Program 累加进 `_fail`）。</summary>
        public static int Run()
        {
            Console.WriteLine();
            Console.WriteLine("──────────────────────────────────────────────────────────────────");
            Console.WriteLine("▸ 10. 地面物品图标：画的是原版物品图，不是品质色块（片 ground-item-icon）");
            Console.WriteLine("──────────────────────────────────────────────────────────────────");

            var fail = 0;
            var rows = Tables.Default.Item != null ? Tables.Default.Item.All() : null;

            // 先找到 Resources 根（宿主在 tools/probes/hosts/fullcheck/bin/… 下跑 ⇒ 向上找）——
            // 找不到就把"磁盘存在性"那几条报成 FAIL（不静默跳过：那会让判据变成"什么也没判"）。
            var resRoot = FindResourcesRoot();
            bool Say(string what, bool ok, string detail)
            {
                if (!ok) fail++;
                Console.WriteLine((ok ? "[ OK ] " : "[FAIL] ") + what
                    + (string.IsNullOrEmpty(detail) ? "" : "   （" + detail + "）"));
                return ok;
            }

            if (rows == null || rows.Count == 0)
            {
                Say("item_c 配表已加载（地面物品取图的前置）", false,
                    "Tables.Default.Item 为空 ⇒ 请先看上面步骤 1（本项无法判定）");
                return fail;
            }

            // ── ① 口径统一 + ② 磁盘存在 + ③ 缺图那批 ────────────────────────────
            int unified = 0, mismatch = 0, firstMismatch = -1;
            int withIcon = 0, onDisk = 0, missingFile = 0, firstMissingFile = -1;
            int noIconRows = 0, noIconOnDisk = 0;
            int badPng = 0;
            string firstBadPngWhy = null;

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null) continue;

                var item = new ItemStack { itemId = row.Id, name = row.Name };
                var groundPath = GroundItemVisual.IconPathOf(item);
                var uiPath = D2Icon.ItemIconPath(row.Id);

                if (string.Equals(groundPath, uiPath, StringComparison.Ordinal)) unified++;
                else { mismatch++; if (firstMismatch < 0) firstMismatch = row.Id; }

                var hasIcon = ItemIconAvailability.HasOriginalIcon(row);
                var file = resRoot == null || string.IsNullOrEmpty(groundPath)
                    ? null : Path.Combine(resRoot, groundPath.Replace('/', Path.DirectorySeparatorChar) + ".png");
                var exists = file != null && File.Exists(file);

                if (hasIcon)
                {
                    if (!string.IsNullOrEmpty(groundPath)) withIcon++;
                    if (exists) onDisk++;
                    else { missingFile++; if (firstMissingFile < 0) firstMissingFile = row.Id; }
                    if (exists && !IsRealPng(file, out var why)) { badPng++; if (firstBadPngWhy == null) firstBadPngWhy = row.Name + ":" + why; }
                }
                else
                {
                    noIconRows++;
                    if (exists) noIconOnDisk++;      // 与清单不符：清单说缺，磁盘却有
                }
            }

            Say("地面物品图 == 背包图（`GroundItemVisual.IconPathOf` 逐行 == `D2Icon.ItemIconPath`）",
                mismatch == 0,
                $"item_c {rows.Count} 行：一致 {unified}，不一致 {mismatch}"
                + (mismatch > 0 ? $"（首个不一致 id={firstMismatch}）" : "（两种画法只可能有一张图）"));

            Say("所有有原版图的物品都拼得出路径（`D2/Items/inv{code}`）", missingFile == 0,
                $"有图行 {withIcon}，其中磁盘在位 {onDisk}，缺文件 {missingFile}"
                + (missingFile > 0 ? $"（首个 id={firstMissingFile}）" : ""));

            Say("在位图标都是**真 PNG**（签名 + IHDR ≥ 8×8 且非 1×1 纯色小片）", badPng == 0,
                badPng == 0
                    ? $"{onDisk} 张逐张验过（原始 DC6 尺寸，不是运行时那块 40×79 占位白图）"
                    : $"{badPng} 张不合（首个 {firstBadPngWhy}）");

            Say("资料片职业专属装备（本批素材无原版图）确实**磁盘上没有**图 —— 走品质色块回退",
                noIconOnDisk == 0,
                $"`ItemIconAvailability` 判定无图 {noIconRows} 件；其中磁盘上真有图的 {noIconOnDisk} 件"
                + "（>0 说明'缺图'清单与磁盘不一致，需登记）");

            // ── ③b 金币（itemId = 0 的金币堆，不是 item_c 行）──────────────────
            // 出处：`Module/Item/LootRoller.cs:327` 造的 `ItemStack{ itemId = 0, name = "金币", isGold = true }`
            // ⇒ `D2Icon` 那条链覆盖不到它（对 itemId ≤ 0 直接 null），修前地上是白方块。
            // 原版地面金币图 = `invgld*.dc6`（三档都在盘），本轮**统一用 invgld**、分档 BLOCKED。
            var gold = new ItemStack { itemId = 0, name = "金币", isGold = true };
            var goldPath = GroundItemVisual.IconPathOf(gold);
            var goldExpect = ResPaths.ItemIcon("gld");           // 项目里唯一的物品图路径拼法
            var goldFile = resRoot == null || string.IsNullOrEmpty(goldExpect)
                ? null : Path.Combine(resRoot, goldExpect.Replace('/', Path.DirectorySeparatorChar) + ".png");
            Say("金币（itemId=0 的金币堆）走原版金堆图 `invgld`（不是色块），且路径来自唯一入口 ResPaths.ItemIcon",
                string.Equals(goldPath, goldExpect, StringComparison.Ordinal)
                && goldPath == "D2/Items/invgld" && goldFile != null && File.Exists(goldFile),
                $"IconPathOf(金币)={goldPath ?? "null"} 期望={goldExpect}（磁盘在位={goldFile != null && File.Exists(goldFile)}）");

            var notGoldZero = GroundItemVisual.IconPathOf(new ItemStack { itemId = 0, name = "?", isGold = false });
            Say("非金币的 itemId=0 **不许**乱指一张图（保持 null ⇒ 走可见品质色块回退）",
                notGoldZero == null, $"IconPathOf(itemId=0,isGold=false)={(notGoldZero ?? "null")}");

            Say("原版金堆三档图都在盘（⛔ 阈值无权威载体 ⇒ 分档登记 BLOCKED，本轮统一用 invgld）",
                resRoot != null
                && File.Exists(Path.Combine(resRoot, "D2", "Items", "invgld.png"))
                && File.Exists(Path.Combine(resRoot, "D2", "Items", "invgldm.png"))
                && File.Exists(Path.Combine(resRoot, "D2", "Items", "invgldh.png")),
                "`D2/Items/{invgld,invgldm,invgldh}.png` 逐个 Test-Path");

            // ── ④ 色调 ───────────────────────────────────────────────────────
            var tNormal = GroundItemVisual.TintOf(ItemQuality.Normal);
            var tMagic = GroundItemVisual.TintOf(ItemQuality.Magic);
            var tRare = GroundItemVisual.TintOf(ItemQuality.Rare);
            var tSet = GroundItemVisual.TintOf(ItemQuality.Set);
            var tUnique = GroundItemVisual.TintOf(ItemQuality.Unique);

            Say("普通物品的地面图色调 = 纯白（⇒ 逐像素等于原版，⛔ 不染色）",
                Mathf.Approximately(tNormal.r, 1f) && Mathf.Approximately(tNormal.g, 1f)
                && Mathf.Approximately(tNormal.b, 1f) && Mathf.Approximately(tNormal.a, 1f),
                $"Normal tint=({tNormal.r:0.###},{tNormal.g:0.###},{tNormal.b:0.###},{tNormal.a:0.###})");

            var distinct = new HashSet<string>
            {
                Key(tNormal), Key(tMagic), Key(tRare), Key(tSet), Key(tUnique),
            };
            Say("五种品质的地面图色调互不相同（品质在地面上可辨，口径同背包）",
                distinct.Count == 5,
                $"Normal={Key(tNormal)} Magic={Key(tMagic)} Rare={Key(tRare)} Set={Key(tSet)} Unique={Key(tUnique)}");

            // `Resources/Clover` 往上两级 = `Assets/` ⇒ `Assets/Scripts/Module/View/ViewModule.cs`
            var vmPath = resRoot == null ? null
                : Path.GetFullPath(Path.Combine(resRoot, "..", "..", "Scripts", "Module", "View", "ViewModule.cs"));
            var src = vmPath != null && File.Exists(vmPath) ? File.ReadAllText(vmPath) : null;
            if (src == null)
            {
                Say("能读到 `ViewModule.cs` 源码（过程断言的前置）", false, "找不到 " + (vmPath ?? "(resRoot 缺失)"));
            }
            else
            {
                Say("地面物品链真的接了 `GroundItemVisual.IconPathOf`（不是只加了个没人调的函数）",
                    src.Contains("GroundItemVisual.IconPathOf("),
                    "ViewModule.cs 出现 `GroundItemVisual.IconPathOf(`");

                Say("地面物品的贴图路径**不再**被 `ApplyFrame` 的 `IsGroundItem` 早退吞掉",
                    src.Contains("ApplyGroundItemIcon(")
                    && !src.Contains("if (v == null || v.Renderer == null || v.IsGroundItem) return;"),
                    "改动前那一行 `ApplyFrame` 早退（根因）已不存在，改为走 `ApplyGroundItemIcon`");

                // 缺图回退必须仍然**可见**（品质色块），不许静默变透明
                Say("取不到原版图时仍保留**可见**的品质色块（不许静默透明）",
                    src.Contains("grounditem.noicon") && src.Contains("SpriteFrames.Placeholder"),
                    "`grounditem.noicon` WarnOnce + `SpriteFrames.Placeholder` 回退都在源码里");
            }

            return fail;
        }

        private static string Key(Color c)
            => c.r.ToString("0.###") + "/" + c.g.ToString("0.###") + "/" + c.b.ToString("0.###");

        /// <summary>PNG 签名 + IHDR 尺寸（不解码像素，只看头 33 字节）。</summary>
        private static bool IsRealPng(string file, out string why)
        {
            why = null;
            try
            {
                var head = new byte[33];
                using (var fs = File.OpenRead(file))
                {
                    var n = fs.Read(head, 0, head.Length);
                    if (n < 33) { why = "文件太短(" + n + "B)"; return false; }
                }
                var sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
                for (var i = 0; i < sig.Length; i++) if (head[i] != sig[i]) { why = "PNG 签名不符"; return false; }
                if (Encoding.ASCII.GetString(head, 12, 4) != "IHDR") { why = "首个块不是 IHDR"; return false; }

                var w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                var h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                if (w < 8 || h < 8) { why = "尺寸过小 " + w + "x" + h; return false; }
                if (w * h == 1) { why = "1x1 纯色片"; return false; }
                return true;
            }
            catch (Exception e)
            {
                why = e.GetType().Name + ":" + e.Message;
                return false;
            }
        }

        /// <summary>向上找 `client/Assets/Resources/Clover`（宿主 cwd 在 bin/Debug/net10.0 下 ⇒ 必须向上找）。</summary>
        private static string FindResourcesRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 10 && dir != null; i++)
            {
                var cand = Path.Combine(dir.FullName, "client", "Assets", "Resources", "Clover");
                if (Directory.Exists(cand)) return cand;
                dir = dir.Parent;
            }
            return null;
        }
    }
}
