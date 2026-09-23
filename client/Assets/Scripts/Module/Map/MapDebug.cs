// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapDebug.cs
// 地图模块**自证取证**工具（`docs/agents/agent-04-地图模块.md` §5 验收要用）：
//   · `DumpStats()`   —— 区域名 / 尺寸 / seed / 障碍数 / 可走数 / 生成耗时 / 出口/NPC/刷怪点
//   · `DumpAscii()`   —— 把可走性打成字符画（**日志取证**：同 seed 两次生成应逐字符相同）
//   · `Hash()`        —— 地形+F9 的 FNV-1a 哈希（两次生成比对用，日志里只贴哈希就够）
//
// 生成流程（`MapModule.Generate`）在成功后会**自动**打一份 stats + 字符画头部 + 哈希，
// 所以「三处区域各生成一次」的证据**天然落在日志里**，不需要额外调用。
// ─────────────────────────────────────────────────────────────────────────────

using System.Text;
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
        /// </summary>
        /// <param name="maxRows">只输出顶部若干行（&lt;= 0 = 全部）。</param>
        public static string DumpAscii(GridMap map, int maxRows = 0)
        {
            if (map == null) return "MapDebug.DumpAscii: map == null";

            var sb = new StringBuilder((map.Width + 8) * Mathf.Min(map.Height, maxRows > 0 ? maxRows : map.Height) + 256);
            sb.AppendLine($"ASCII y={map.Height - 1}→0  x=0→{map.Width - 1}   " +
                          "图例: ' '=图外/未生成 '.'=可走 '#'=障碍 '~'=水 'S'=出生点 'E'=出口 'C'=洞穴入口 'N'=NPC 'M'=刷怪点");

            var rows = 0;
            for (var y = map.Height - 1; y >= 0; y--)
            {
                if (maxRows > 0 && rows >= maxRows) break;
                sb.Append(y.ToString("D3")).Append('|');
                for (var x = 0; x < map.Width; x++)
                {
                    sb.Append(CharOf(map, x, y));
                }
                sb.AppendLine();
                rows++;
            }
            return sb.ToString();
        }

        /// <summary>地形 + 尺寸 + seed 的 FNV-1a 64 位哈希（同 seed 两次生成必须一致）。</summary>
        public static string Hash(GridMap map)
        {
            if (map == null) return "0000000000000000";

            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var h = offset;

            h = Mix(h, (ulong)map.Width, prime);
            h = Mix(h, (ulong)map.Height, prime);
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    h = Mix(h, (ulong)(byte)map.Get(x, y), prime);
                }
            }
            return h.ToString("X16");
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
        private static ulong Mix(ulong h, ulong value, ulong prime)
        {
            h ^= value;
            h *= prime;
            return h;
        }

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
            // ★ 片 L / R12：水在字符画里用 '~'（与障碍 '#' 分开）⇒ 日志/离线取证一眼能分出
            //   "这条是河"还是"这条是石头"。⛔ 只改显示字符，可走性仍走 `TileKindInfo`（水 = 阻挡）。
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
