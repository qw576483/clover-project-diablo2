// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/TileNodePool.cs  ★ T0FIX-A 新增
//
// 地图瓦片节点的**对象池**。动机（T0 量到的真缺陷）：
//   `MapView` 旧口径在每次整图重铺时 `Destroy` 全部节点（Town 56×40 ≈ 2000+ 个 GameObject，
//   洞穴最坏 ≈ 9000）再全部 `new GameObject` —— 单帧 46.3~73.9 ms（实测，≫ 一帧预算 16.67 ms）。
//   池化后：旧节点**归还**（不 Destroy）、新节点**复用**（不 new）⇒ 第二次及以后的整图重铺
//   新建数 = 0。
//
// 三条硬规矩：
//   ① **只有本类的 `Take` 会 `new GameObject`**（全工程唯一的瓦片节点创建点）⇒ "复用"与
//      "新建"走同一条路，渲染字段由 `MapView.ApplyTileState` 无条件重设 ⇒ 画面逐像素不变；
//   ② 归还 = `SetActive(false)` + 挂回池根（**不销毁**）⇒ 块根被销毁时不会连带销毁它们；
//   ③ 池里可能残留**已被场景卸载销毁**的空引用（Unity 的 `==` 重载判 null）⇒ `Take` 跳过它们
//      （`Clear()` 退场时也会真销毁）。
//
// 不是 MonoBehaviour（纯 C# 类）；一个 `.cs` 一个 MonoBehaviour 的规矩不受影响。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>瓦片节点池（`SpriteRenderer` + 其 `GameObject`）。</summary>
    internal sealed class TileNodePool
    {
        /// <summary>空闲节点栈（后进先出 ⇒ 复用的节点尽量是最近归还的，缓存友好）。</summary>
        private readonly Stack<SpriteRenderer> _free = new Stack<SpriteRenderer>();

        /// <summary>归还节点的挂载根（在 `MapRoot` 下 ⇒ 随场景销毁，不跨场景泄漏）。</summary>
        private readonly Transform _root;

        /// <summary>累计**新建**（冷分支）次数。</summary>
        public int CreatedCount { get; private set; }

        /// <summary>累计**复用**（热分支）次数。</summary>
        public int ReusedCount { get; private set; }

        /// <summary>当前空闲节点数。</summary>
        public int FreeCount { get { return _free.Count; } }

        /// <summary>池根（归还节点的父节点）。</summary>
        public TileNodePool(Transform root)
        {
            _root = root;
        }

        /// <summary>
        /// 取一个节点（复用到就复用；池空/池里全是已销毁引用才新建），并挂到 <paramref name="parent"/>
        /// 的**末尾**（追加 ⇒ 块内格序与"全新建"时逐项一致）。
        /// </summary>
        public SpriteRenderer Take(Transform parent)
        {
            SpriteRenderer sr = null;
            while (_free.Count > 0)
            {
                var pooled = _free.Pop();
                if (pooled == null) continue;               // 已被场景卸载销毁：跳过（不留脏引用）
                sr = pooled;
                ReusedCount++;
                break;
            }

            if (sr == null)
            {
                CreatedCount++;
                var go = new GameObject("T");
                sr = go.AddComponent<SpriteRenderer>();
            }

            sr.transform.SetParent(parent, false);

            // ★ T0FIX-I（**根因修复**）：**取出即复活** —— 与 `Return` 的 `SetActive(false)` **严格配对**。
            //   为什么必须在这里：`Return` 归还时把节点 `SetActive(false)`，而同一个节点会被**复用**到
            //   新块的（隐藏→可见的）缓冲层下。若不复位 `activeSelf`，它会永远停在 false；而
            //   Unity 的语义是**激活父节点不复活 activeSelf=false 的子节点** ⇒ 双缓冲建出的整张地图
            //   一次都没被渲染过（实机实测：进图就绪后 `wholeMapView_totalSR=5134` 而 `activeSR=0`、
            //   `chunk0Tile0_activeInHierarchy=0`、全屏 `mean_lum=5.42/255`）。
            //   ⛔ 口径：池里一律 inactive、取出的一律 active（逐次配对，离线断言见 mapcheck §20②）。
            sr.gameObject.SetActive(true);
            return sr;
        }

        /// <summary>归还一个节点：失活 + 脱离原父节点（挂回池根），**不销毁**。</summary>
        public void Return(SpriteRenderer sr)
        {
            if (sr == null) return;
            sr.gameObject.SetActive(false);
            if (_root != null) sr.transform.SetParent(_root, false);
            _free.Push(sr);
        }

        /// <summary>
        /// 真销毁池内全部空闲节点（退场用：`MapView.Clear()`）。**不影响已取出的节点**。
        /// 计数不清零（进程内累计自证量；语义见 `MapView.PoolCreatedCount` 的注释）。
        /// </summary>
        public void Clear()
        {
            while (_free.Count > 0)
            {
                var sr = _free.Pop();
                if (sr == null) continue;
                Object.Destroy(sr.gameObject);
            }
        }

        /// <summary>
        /// **纯函数**：给定空闲数与需求数，算出「从池里取几个 / 新建几个」。
        /// 离线自检宿主用它做池化收益的算术断言（不需要真的建 GameObject）——
        /// 见 `tools/probes/hosts/mapcheck` §17。
        /// </summary>
        public static void SplitDemand(int freeCount, int demand, out int fromFree, out int create)
        {
            if (demand <= 0)
            {
                fromFree = 0;
                create = 0;
                return;
            }
            var free = freeCount < 0 ? 0 : freeCount;
            fromFree = free < demand ? free : demand;
            create = demand - fromFree;
        }
    }
}
