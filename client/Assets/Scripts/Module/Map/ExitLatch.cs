// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/ExitLatch.cs
// **出口/接缝"该不该发 ExitEntered"的唯一判据**（纯值类型，不碰地图数据、不碰渲染、不依赖 Unity 运行时）。
//
// 为什么单独一个文件（与 `MapSeam` / `DeckTiles` 同一口径）：同一条判据会被**两处**用到
//   —— 生产路径（`PlayerModule.CheckExit` 决定"发不发过门请求"）、离线断言
//   （`tools/probes/hosts/audiocheck` 的"同一出口格连续 60 帧只发 1 次"）——
//
// 语义（= 状态跃迁闩锁，不是"记住上一格"）：
//   · 在出口区（`TileKind.Exit` 格，或 `MapSeam` 判定的东边接缝格）**进入的那一刻**发一次；
//   · 仍站在出口区（同一格**或**沿出口格逐格挪动）**不再发**；
//   · **离开**出口区 ⇒ 重新武装，下次再进入可再发（出口必须仍能真的触发切换，不许把角色卡住）。
//
// 判据本体 = 引擎件 `EnterLatch<Vector2Int>`（这是**任何**"踩到某区域触发一次"——出口 / 接缝 /
//   陷阱 / 传送门 / 治疗泉 / 拾取区——都用的共享判据）。
//   唯一的表达差异：引擎用 `HasLastTrigger`（布尔位）表达"从未触发"，本项目对外的哨兵值是
//   `NoGrid` ⇒ 在 `LastTriggerGrid` 里做一次映射，`int.MinValue` 这个项目侧哨兵不交给引擎。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>出口/接缝触发闩锁（纯值类型：无 Unity 依赖，可离线逐帧断言）。</summary>
    internal struct ExitLatch
    {
        /// <summary>「没有格」哨兵（与 `PlayerModule.NoGrid` 同值口径）。</summary>
        public static readonly Vector2Int NoGrid = new Vector2Int(int.MinValue, int.MinValue);

        /// <summary>闩锁状态（**唯一**一份；本类型不自留 `_fired` / `_lastTriggerGrid` 副本）。</summary>
        private EnterLatch<Vector2Int> _latch;

        /// <summary>最近一次触发时所在的格（`NoGrid` = 从未触发过）。</summary>
        public Vector2Int LastTriggerGrid => _latch.HasLastTrigger ? _latch.LastTriggerAt : NoGrid;

        /// <summary>是否处于"已发过、尚未重新武装"状态。</summary>
        public bool Fired => _latch.Fired;

        /// <summary>
        /// 是否应当发出过门请求。
        /// <para><paramref name="onExit"/> = 本格是否属于出口区（调用方按唯一口径算好再传进来）。</para>
        /// <para>纯函数式：同样的入参序列 ⇒ 同样的出参序列，没有隐藏状态（状态只有引擎件闩锁自己的三个字段）。</para>
        /// </summary>
        public bool ShouldEmit(bool onExit, Vector2Int grid) => _latch.ShouldEmit(onExit, grid);

        /// <summary>重新武装（进图落位 / 传送 / 复活 / 复位：把玩家搬到别处后，出口要能再触发一次）。</summary>
        public void Reset() => _latch.Reset();
    }
}
