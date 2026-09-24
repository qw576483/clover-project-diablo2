// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapDebug.cs
//   · `DumpStats()`   —— 区域名 / 尺寸 / seed / 障碍数 / 可走数 / 生成耗时 / 出口/NPC/刷怪点
//   · `DumpAscii()`   —— 把可走性打成字符画（**日志取证**：同 seed 两次生成应逐字符相同）
//   · `Hash()`        —— 地形+F9 的 FNV-1a 哈希（两次生成比对用，日志里只贴哈希就够）
//
// 生成流程（`MapModule.Generate`）在成功后会**自动**打一份 stats + 字符画头部 + 哈希，
// 所以「三处区域各生成一次」的证据**天然落在日志里**，不需要额外调用。
//
//   地形 + 尺寸的哈希本体在引擎件 `CloverEngine.StableHash`（"先混宽高、再按 y 外层升序 /
//   x 内层升序逐格"的遍历顺序在那里）；本文件只保留**本项目语义**
//   （`CharOf`：一格画什么字符 —— S/E/C/N/M 与 '~' 水）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Text;
using CloverEngine;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>地图自证输出（纯字符串，不依赖引擎 UI）。</summary>
    public static class MapDebug
    {
        /// <summary>是否在生成后把**完整**字符画打进日志（大图会刷屏，默认只打头部若干行）。</summary>
        public static bool LogFullAsciiOnGenerate;

        /// <summary>生成后日志里打多少行字符画（从地图「北」端开始）。</summary>
        public static int AsciiHeadLines = 12;

        /// <summary>单行统计（一行说完，便于 grep）。</summary>
        public static string StatsLine(GridMap map)
        {
            if (map == null) return "MapDebug.StatsLine: map == null";
            var cave = map.CaveEntrance.HasValue ? map.CaveEntrance.Value.ToString() : "-";
            return $"area={map.Area}({MapLog.AreaLabel(map.Area)}) size={map.Width}x{map.Height} seed={map.Seed} " +
                   $"blocked={map.BlockedCount} walkable={map.WalkableCount} timeMs={map.LastGenerateMs:F1} " +
                   $"attempts={map.GenerateAttempts} spawn={map.SpawnPoint} exits={map.Exits.Count} " +
                   $"caveEntrance={cave} npc={map.NpcPoints.Count} monsterSpawns={map.MonsterSpawns.Count}";
        }

        /// <summary>多行统计块（验收要贴的那份）。</summary>
        public static string DumpStats(GridMap map)
        {
            if (map == null) return "MapDebug.DumpStats: map == null";

            var total = Mathf.Max(1, map.Width * map.Height);
            var ratio = 100f * map.WalkableCount / total;
            var sb = new StringBuilder(512);

            sb.AppendLine("── MAP STATS ────────────────────────────────────────────────");
            sb.AppendLine($"  区域      : {map.Area}（{MapLog.AreaLabel(map.Area)}）");
            sb.AppendLine($"  尺寸      : {map.Width} x {map.Height} = {map.Width * map.Height} 格");
            sb.AppendLine($"  seed      : {map.Seed}（重试次数 {map.GenerateAttempts}）");
            sb.AppendLine($"  障碍数    : {map.BlockedCount}");
            sb.AppendLine($"  可走数    : {map.WalkableCount}（{ratio:F1}%）");
            sb.AppendLine($"  生成耗时  : {map.LastGenerateMs:F1} ms");
            sb.AppendLine($"  出生点    : {map.SpawnPoint}");
            sb.AppendLine($"  出口      : {Fmt(map.Exits)}");
            sb.AppendLine($"  洞穴入口  : {(map.CaveEntrance.HasValue ? map.CaveEntrance.Value.ToString() : "-（本区域无）")}");
            sb.AppendLine($"  NPC 站位  : {Fmt(map.NpcPoints)}");
            sb.AppendLine($"  刷怪点    : {map.MonsterSpawns.Count} 个{Head(map.MonsterSpawns, 8)}");
            sb.AppendLine($"  地形哈希  : {Hash(map)}");
            sb.Append("─────────────────────────────────────────────────────────────");
            return sb.ToString();
        }

        /// <summary>
        /// 可走性字符画：`y` 从大到小逐行输出（地图「北」在顶部），行首带 y 坐标。
        /// <para>排版口径（行首 `D3 + '|'` / 首行图例 / "只输出顶部 maxRows 行"）在
        /// 引擎件 <see cref="StableHash.ToAscii"/>；本方法只给"一格画什么字符"（<see cref="CharOf"/>，
        /// 那才是本项目语义：`S`/`E`/`C`/`N`/`M` 与地形字符）。</para>
        /// </summary>
        /// <param name="maxRows">只输出顶部若干行（&lt;= 0 = 全部）。</param>
        public static string DumpAscii(GridMap map, int maxRows = 0)
        {
            if (map == null) return "MapDebug.DumpAscii: map == null";

            return StableHash.ToAscii(map.Width, map.Height, (x, y) => CharOf(map, x, y),
                $"ASCII y={map.Height - 1}→0  x=0→{map.Width - 1}   " +
                "图例: ' '=图外/未生成 '.'=可走 '#'=障碍 '~'=水 'S'=出生点 'E'=出口 'C'=洞穴入口 'N'=NPC 'M'=刷怪点",
                maxRows);
        }

        /// <summary>
        /// 地形 + 尺寸的 FNV-1a 64 位哈希（同 seed 两次生成必须一致）。
        /// <para>哈希常量（`OffsetBasis` / `Prime`）、"先混宽高、再按 y 外层升序 / x 内层升序
        /// 逐格混入 `byte` 地形码"、"`X16` 大写十六进制"三条口径全在引擎件
        /// <see cref="StableHash.HashGridHex"/>。不自留第二份 —— 常量写错或顺序不一致会让两条日志
        /// "看起来都对、却永远对不上"，且不报错。</para>
        /// <para>注意：本哈希只混**地形码 + 尺寸**；
        /// seed / 出生点等元数据不进哈希（要哈希复合结构请用 <see cref="StableHash.Combine(ulong,ulong)"/>
        /// 自行串联，**顺序即契约**）。</para>
        /// </summary>
        public static string Hash(GridMap map)
        {
            if (map == null) return "0000000000000000";
            return StableHash.HashGridHex(map.Width, map.Height, (x, y) => (byte)map.Get(x, y));
        }

        /// <summary>生成完成后统一取证：统计块 + 哈希 + 字符画（头部或全量）。</summary>
        public static void ReportAfterGenerate(GridMap map)
        {
            if (map == null)
            {
                MapLog.Warn("ReportAfterGenerate: map 为 null，跳过取证输出");
                return;
            }

            MapLog.Info(StatsLine(map));
            MapLog.Info("DumpStats →\n" + DumpStats(map));

            var ascii = DumpAscii(map, LogFullAsciiOnGenerate ? 0 : AsciiHeadLines);
            MapLog.Info($"DumpAscii hash={Hash(map)}（同 seed 两次生成必须同哈希）\n" + ascii);
        }

        // ── 内部 ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 一格画什么字符（**本项目语义**：图例里的 `S`/`E`/`C`/`N`/`M` 与 `'~'` 水都在这里判；
        /// 引擎件 `StableHash.ToAscii` 只负责"怎么排版"，不认识这些符号）。
        /// </summary>
        private static char CharOf(GridMap map, int x, int y)
        {
            var g = new Vector2Int(x, y);
            if (g == map.SpawnPoint) return 'S';
            if (map.CaveEntrance.HasValue && map.CaveEntrance.Value == g) return 'C';
            for (var i = 0; i < map.Exits.Count; i++)
            {
                if (map.Exits[i] == g) return 'E';
            }
            for (var i = 0; i < map.NpcPoints.Count; i++)
            {
                if (map.NpcPoints[i] == g) return 'N';
            }
            for (var i = 0; i < map.MonsterSpawns.Count; i++)
            {
                if (map.MonsterSpawns[i] == g) return 'M';
            }

            var kind = map.Get(x, y);
            if (kind == TileKind.Void) return ' ';
            // 水与石头都不可走，但显示字符不同（'~' vs '#'）；可走性仍走 `TileKindInfo`（水 = 阻挡）。
            if (kind == TileKind.Water) return '~';
            return TileKindInfo.IsWalkable(kind) ? '.' : '#';
        }

        private static string Fmt(System.Collections.Generic.IReadOnlyList<Vector2Int> list)
        {
            if (list == null || list.Count == 0) return "0 个";
            var sb = new StringBuilder(list.Count * 10 + 8);
            sb.Append(list.Count).Append(" 个 [");
            for (var i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(list[i].x).Append(',').Append(list[i].y);
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string Head(System.Collections.Generic.IReadOnlyList<Vector2Int> list, int max)
        {
            if (list == null || list.Count == 0) return string.Empty;
            var sb = new StringBuilder(max * 12 + 8);
            sb.Append(" [");
            for (var i = 0; i < list.Count && i < max; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(list[i].x).Append(',').Append(list[i].y);
            }
            if (list.Count > max) sb.Append(", …");
            sb.Append(']');
            return sb.ToString();
        }
    }
}
