// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapGenTown.cs
// **罗格营地（Rogue Encampment）**：**照原版预设块逐格铺**（不做任何程序化撒点）。
//
// 布局数据**不是本项目自己设计的**，而是从原版地图文件解出来的：
//   来源 = `data/global/tiles/ACT1/TOWN/*.ds1`（Blizzard North, 2000，取自 d2data.mpq）
//          —— 块清单与原版关卡尺寸**都读原版规则表**：
//            · `Levels.txt`「Act 1 - Town」⇒ 关卡 **56×40**（LevelName = Rogue Encampment）
//            · `LvlPrest.txt`「Act 1 - Town 1」⇒ 4 块
//              `TownN1 / TownE1 / TownS1 / TownW1`
//   生成 = `tools/d2codec/export_town_layout.py` → `Module/Map/MapGenTownLayout.cs`
//          （生成物，禁止手改）。
//
// 尺寸：**生成物与契约常量同值 = 原版关卡尺寸 56×40**。
//   上一版只取 `TownW1` 的营地本体并裁成 32×32 ⇒ **营地外的河被裁掉了**（只剩 1 列）；
//   agent-41 改成"整关"，agent-42 按主 agent 裁决把 `GameConst.TownWidth/TownHeight`
//   一并同步为 56/40 ⇒ 这里**不再有尺寸漂移**，两者不一致时直接报错拒绝生成（不静默）。
//
// ★ 木桥：河上那一座桥只有原版 `TownE1.ds1` 有（`OUTDOORS/bridge.dt1`）。生成器给桥
//   单开了一条规则（桥面可走 / 栏杆阻挡），所以本生成器铺出来的图里河是**可以走过去的**
//   —— 见 `MapGenTownLayout.Rows` 第 20/22 行的 `dddddddddd`。
//
// ★ 片 4（窗口原点）：关卡窗口**不再**取参考块的本地 [0,0)，而是「营地本体 39 列 + 出城口
//   外面 17 列」= 56 列（行同理：营地南围栏那行就是关卡南边界）。依据见生成器
//   `tools/d2codec/export_town_layout.py` 的 `WIN_X0/WIN_Y0` 注释。**效果**：出城口
//   （现在是内陆格 `(17,26..28)`）西边 17 列是"出城口外面那片地"（原版瓦片）⇒ 不再出现
//   "出口外面一片黑"（旧口径下地图西边界正好压在营地西围栏上，出城口就在地图边界上）。
//
// ⛔ 布局是**固定**的 ⇒ 不使用 `rng`（保留参数只为与其它生成器**签名一致**，
//    这样 `MapModule` 可以用同一段重试逻辑驱动三个区域）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>罗格营地固定布局生成器（数据来自原版 DS1，见文件头）。</summary>
    public static class MapGenTown
    {
        /// <summary>生成罗格营地（**固定布局**，同 seed 与不同 seed 结果都一样）。</summary>
        public static void Generate(GridMap map, Rng rng)
        {
            if (map == null)
            {
                MapLog.Error("MapGenTown.Generate: map 为 null，放弃生成");
                return;
            }

            // rng 仅用于统一签名（固定布局不需要随机）；显式说明，避免读代码的人以为漏了随机
            var seed = rng != null ? rng.Seed : 0;

            var w = MapGenTownLayout.Width;
            var h = MapGenTownLayout.Height;
            // 契约常量与生成物**必须相等**（主 agent 裁决：两侧都按原版关卡尺寸 56×40）。
            // 不一致 = 生成物没跟上 / 有人改了常量 ⇒ 报错拒绝生成，别静默铺一张错尺寸的图。
            if (w != GameConst.TownWidth || h != GameConst.TownHeight)
            {
                MapLog.Error($"MapGenTown: 生成物尺寸 {w}x{h} ≠ GameConst.TownWidth/TownHeight = " +
                             $"{GameConst.TownWidth}x{GameConst.TownHeight} ⇒ 拒绝生成（两处都必须 = " +
                             "原版 `Levels.txt`「Act 1 - Town」的 SizeX/SizeY；改完重跑 " +
                             "`tools/d2codec/export_town_layout.py`）");
                return;
            }
            map.Reset(AreaId.Town, w, h, seed);

            if (MapGenTownLayout.Rows == null || MapGenTownLayout.Rows.Length != h)
            {
                // 非预期：生成物与网格尺寸不一致 ⇒ 直接报出来，别静默铺一张空图
                MapLog.Error($"MapGenTown: 布局表行数 {MapGenTownLayout.Rows?.Length ?? -1} ≠ 网格高 {h}" +
                             "（MapGenTownLayout.cs 是生成物，请重跑 tools/d2codec/export_town_layout.py）");
                map.Fill(TileKind.Grass);
                return;
            }

            // ① 营地外先铺草地（原版野外底色），再逐格盖原版布局
            map.Fill(TileKind.Grass);

            // ①.5 逐格登记**原版瓦片键**（floor 层 + wall 层各一张，取自原版块）：
            //     `MapView` 见到本图启用了覆盖就一律用它 ⇒ 营地画面 = 原版营地画面（含
            //     "原版这格没铺/没物件" 的格）。取不到键的格留空串 = 原版那格不画。
            map.BeginTileOverrides();
            var noGround = 0;

            var counts = new int[128];
            for (var y = 0; y < h; y++)
            {
                var row = MapGenTownLayout.Rows[y];
                if (row == null || row.Length != w)
                {
                    MapLog.Error($"MapGenTown: 布局表第 {y} 行长度 {(row == null ? -1 : row.Length)} ≠ 网格宽 {w}" +
                                 "（生成物与网格不一致，整行按草地处理）");
                    continue;
                }

                for (var x = 0; x < w; x++)
                {
                    var c = row[x];
                    counts[c < 128 ? c : 0]++;
                    var kind = KindOf(c);
                    map.Set(x, y, kind);
                    if (kind == TileKind.Exit) map.Exits.Add(new Vector2Int(x, y));

                    string gk = null, ok = null;
                    if (!MapGenTownLayout.TryGetTiles(x, y, out gk, out ok)) { noGround++; gk = ""; ok = ""; }
                    map.SetTiles(x, y, gk ?? "", ok ?? "");
                }
            }
            if (noGround > 0)
            {
                MapLog.Warn($"MapGenTown: 有 {noGround} 格不在原版布局表范围内（无原版瓦片键）⇒ 这" +
                            "几格在原版里没有瓦片，渲染层不会画任何东西");
            }

            // ② 出生点 / NPC 站位：由生成器在原版布局上算好（出生点 8 邻全可走；NPC 全在可达区）
            map.SpawnPoint = MapGenTownLayout.Spawn;
            for (var i = 0; i < MapGenTownLayout.Npcs.Length; i++) map.NpcPoints.Add(MapGenTownLayout.Npcs[i]);

            // 必须可达：出城口 + 全部 NPC
            map.RequiredReachable.AddRange(map.Exits);
            map.RequiredReachable.AddRange(map.NpcPoints);

            // 不变量：可达 == 可走（营地是围栏围起来的，正常不会填到任何格；填到了说明有死地）
            map.FillUnreachablePockets(map.SpawnPoint, TileKind.Wall);

            MapLog.Info($"MapGenTown: 原版罗格营地布局已铺（源 {Sources()} 关卡尺寸 {w}x{h}" +
                        $"（出处 {MapGenTownLayout.SizeSource}））：" +
                        $"栅栏 {Count(counts, 'f')} / 摊位·帐篷 {Count(counts, 'o')} / 树 {Count(counts, 't')} / " +
                        $"石矮墙 {Count(counts, 's')} / 水·河 {Count(counts, 'r')} / 泥土 {Count(counts, 'd')} / " +
                        $"草地 {Count(counts, '.')} / " +
                        $"出城口 {map.Exits.Count} / NPC {map.NpcPoints.Count} / 出生点 {map.SpawnPoint}");

            // 出生点净空是硬要求（生成器只在"8 邻全可走"的格里挑，这里再验一次，防生成物被改坏）
            if (!map.IsSpawnClear(map.SpawnPoint, 1))
            {
                MapLog.Warn($"MapGenTown: 出生点 {map.SpawnPoint} 的 3×3 邻域不是全可走" +
                            "（生成物被手改过？）—— MapModule 会换 seed 重试并最终落保底布局");
            }
        }

        /// <summary>块清单拼成一个字符串（日志用）。</summary>
        private static string Sources()
        {
            var names = MapGenTownLayout.SourceDs1;
            if (names == null || names.Length == 0) return "（无）";
            var sb = new StringBuilder();
            for (var i = 0; i < names.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var n = names[i];
                var slash = n.LastIndexOf('/');
                sb.Append(slash >= 0 ? n.Substring(slash + 1) : n);
            }
            return sb.ToString();
        }

        /// <summary>布局字符 → 本项目 `TileKind`（字符含义见 `MapGenTownLayout.Rows` 注释）。</summary>
        private static TileKind KindOf(char c)
        {
            switch (c)
            {
                case '.': return TileKind.Grass;
                case 'd': return TileKind.Dirt;
                case 'f': return TileKind.Fence;
                case 'o': return TileKind.Wall;    // 摊位 / 帐篷 / 货车（原版 wall 层的 objects.dt1）
                case 'w': return TileKind.Wall;    // 兼容别名（旧生成物用过）
                case 't': return TileKind.Tree;
                case 's': return TileKind.Rock;    // 营地内的石矮墙（原版 wall 层的 stonewall.dt1）
                case 'r': return TileKind.Rock;    // 水（河/水塘，原版 river.dt1）—— 不可涉水
                case 'x': return TileKind.Exit;
                // ★ 片 4：'v' = 原版**四块都没有瓦片**的格（实测只有西北角 3×10 一块）。
                //    原版那几格什么都不画、也没有 walk 标志 ⇒ 不画 + 不可走（= TileKind.Void）。
                //    ⛔ 不许当草地（当草地会变成"可走的隐形格"，玩家能走到纯黑背景上去）。
                case 'v': return TileKind.Void;
                default:
                    // 非预期：生成物里出现了没登记的分类字符 ⇒ 留痕，并按草地处理（不静默）
                    MapLog.WarnThrottled("towngen.ch." + (int)c,
                        $"MapGenTown: 布局表出现未登记字符 '{c}'（(int)={(int)c}）⇒ 按草地处理");
                    return TileKind.Grass;
            }
        }

        private static int Count(int[] counts, char c) { return counts[c]; }
    }
}
