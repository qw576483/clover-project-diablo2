// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/TileNodePool.cs  eng-tile 下沉后 = **薄转发**
//
// 实现已下沉到引擎：`CloverEngine.TileNodePool`
//   （clover-client-unity-engine · Runtime/Presentation/TileNodePool.cs）。
// 本文件**只保留项目侧类型名与公开签名**（一字未改）⇒ 全部调用点（`MapView.EnsurePool` /
// `NewTile` / `ReturnTiles`）与全部离线判据（`TileNodePool.SplitDemand(...)`）逐字不变。
//
// 本文件不许再长出实现：① 池化逻辑（借/还/清、`SetActive` 配对、跳过已销毁引用）只在引擎那份；
//   ② 不许在这里新造节点（不写建 `GameObject` / 挂 `SpriteRenderer` 的代码）—— 否则"瓦片节点的
// 出处与三条硬规矩见引擎文件头注释。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>瓦片节点池（`SpriteRenderer` + 其 `GameObject`）—— 薄转发到 <see cref="CloverEngine.TileNodePool"/>。</summary>
    internal sealed class TileNodePool
    {
        private readonly CloverEngine.TileNodePool _inner;

        /// <summary>池根（归还节点的父节点）。</summary>
        public TileNodePool(Transform root)
        {
            _inner = new CloverEngine.TileNodePool(root);
        }

        /// <summary>累计新建（冷分支）次数。</summary>
        public int CreatedCount { get { return _inner.CreatedCount; } }

        /// <summary>累计复用（热分支）次数。</summary>
        public int ReusedCount { get { return _inner.ReusedCount; } }

        /// <summary>当前空闲节点数。</summary>
        public int FreeCount { get { return _inner.FreeCount; } }

        /// <summary>取一个节点（取出即 `SetActive(true)`），挂到 <paramref name="parent"/> 末尾。</summary>
        public SpriteRenderer Take(Transform parent)
        {
            return _inner.Take(parent);
        }

        /// <summary>归还一个节点（失活 + 挂回池根，不销毁）。</summary>
        public void Return(SpriteRenderer sr)
        {
            _inner.Return(sr);
        }

        /// <summary>真销毁池内全部空闲节点（退场用）。</summary>
        public void Clear()
        {
            _inner.Clear();
        }

        /// <summary>**纯函数**：给定空闲数与需求数，算出「从池里取几个 / 新建几个」。</summary>
        public static void SplitDemand(int freeCount, int demand, out int fromFree, out int create)
        {
            CloverEngine.TileNodePool.SplitDemand(freeCount, demand, out fromFree, out create);
        }
    }
}
