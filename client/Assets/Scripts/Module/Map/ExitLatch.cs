// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/ExitLatch.cs
// **出口/接缝"该不该发 ExitEntered"的唯一判据**（纯值类型，不碰地图数据、不碰渲染、不依赖 Unity 运行时）。
//
// 为什么单独一个文件（与 `MapSeam` / `DeckTiles` 同一口径）：同一条判据会被**两处**用到
//   —— 生产路径（`PlayerModule.CheckExit` 决定"发不发过门请求"）、离线断言
//   （`tools/probes/hosts/audiocheck` 的"同一出口格连续 60 帧只发 1 次"）——
//   ⛔ 不许各写一份（"改了口径没扫全路径"是本项目已踩过的坑）。
//
// 语义（= 状态跃迁闩锁，不是"记住上一格"）：
//   · 在出口区（`TileKind.Exit` 格，或 `MapSeam` 判定的东边接缝格）**进入的那一刻**发一次；
//   · 仍站在出口区（同一格**或**沿出口格逐格挪动）**不再发** —— 旧口径"记住上一格"会在
//     沿出口列/接缝逐格走时**每格各发一次**（同一族缺陷）；
//   · **离开**出口区 ⇒ 重新武装，下次再进入可再发（⛔ 出口必须仍能真的触发切换，不许把角色卡住）。
//
// ⛔ 本文件**只判"要不要发"**，不判"发去哪"（目标区域仍由 `PlayerModule.ExitTargetArea` 按 `_common.md` §3.5
//   的冻结规则算），也不改任何"什么算出口"的形状（那仍是 `TileKind.Exit` + `MapSeam.IsTownEastSeam`）。
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>出口/接缝触发闩锁（纯值类型：无 Unity 依赖，可离线逐帧断言）。</summary>
    internal struct ExitLatch
    {
        /// <summary>「没有格」哨兵（与 `PlayerModule.NoGrid` 同值口径）。</summary>
        public static readonly Vector2Int NoGrid = new Vector2Int(int.MinValue, int.MinValue);

        /// <summary>是否已在本轮"进入出口区"里发过（0 = 已重新武装，1 = 已发过）。</summary>
        private byte _fired;

        /// <summary>最近一次触发时所在的格（供日志/排障读；不参与判定）。</summary>
        private Vector2Int _lastTriggerGrid;

        /// <summary>最近一次触发时所在的格（`NoGrid` = 从未触发过）。</summary>
        public Vector2Int LastTriggerGrid => _lastTriggerGrid;

        /// <summary>是否处于"已发过、尚未重新武装"状态。</summary>
        public bool Fired => _fired != 0;

        /// <summary>
        /// 是否应当发出过门请求。
        /// <para><paramref name="onExit"/> = 本格是否属于出口区（调用方按唯一口径算好再传进来）。</para>
        /// <para>⛔ 纯函数式：同样的入参序列 ⇒ 同样的出参序列，没有隐藏状态（状态只有本值类型自己的两个字段）。</para>
        /// </summary>
        public bool ShouldEmit(bool onExit, Vector2Int grid)
        {
            if (!onExit)
            {
                // 离开出口区 ⇒ 重新武装（下一次再进入可再发一次）
                _fired = 0;
                return false;
            }

            if (_fired != 0) return false;      // 已在本次"进入出口区"里发过 ⇒ 同一出口/接缝只发一次

            _fired = 1;
            _lastTriggerGrid = grid;
            return true;
        }

        /// <summary>重新武装（进图落位 / 传送 / 复活 / 复位：把玩家搬到别处后，出口要能再触发一次）。</summary>
        public void Reset()
        {
            _fired = 0;
            _lastTriggerGrid = NoGrid;
        }
    }
}
