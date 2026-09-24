// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/TileRenderState.cs  ★ eng-tile 下沉后 = **薄转发**
//
// 实现已下沉到引擎：`CloverEngine.TileRenderState`
//   （clover-client-unity-engine · Runtime/Presentation/TileRenderState.cs）。
// 本文件**只保留项目侧类型名与公开成员名**（`Sprite` / `Color` / `LocalScale` / `Position` /
// `SortingOrder` / `SameAs` / `ToString` 一字未改）⇒ 全部调用点（`MapView.ApplyTileState` 的逐项读写、
// `GroundState`/`ObjectState`/`FogState` 的构造、离线判据的 `SameAs`）逐字不变。
//
// ⚠️ 与引擎那份的**一处形状差异**（本项目的 mapcheck §17⑥ 钉的是引擎类型）：引擎里这 5 项是
//   `public readonly` **字段**；这里是**只读属性**（C# 的 struct 不能继承，转发只能用属性）。
//   逐项语义与只读性完全一致。
//
// ⛔ 本文件不许再长出状态字段：⛔ 不许在这里复制一份 5 字段的存储（那就是"平行两套"，
//   会让"复用节点的渲染字段从哪来"重新变成两个真相）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>一格瓦片的渲染状态（见文件头）—— 薄转发到 <see cref="CloverEngine.TileRenderState"/>。</summary>
    internal readonly struct TileRenderState
    {
        private readonly CloverEngine.TileRenderState _v;

        /// <summary>构造（唯一入口；全部字段必须显式给）。</summary>
        public TileRenderState(Sprite sprite, Color color, Vector3 localScale, Vector3 position, int sortingOrder)
        {
            _v = new CloverEngine.TileRenderState(sprite, color, localScale, position, sortingOrder);
        }

        /// <summary>贴图（null = 占位菱形）。</summary>
        public Sprite Sprite { get { return _v.Sprite; } }

        /// <summary>节点颜色（有贴图 = 白；占位 = 可辨的占位色）。</summary>
        public Color Color { get { return _v.Color; } }

        /// <summary>节点缩放。</summary>
        public Vector3 LocalScale { get { return _v.LocalScale; } }

        /// <summary>节点世界坐标（含对齐修正）。</summary>
        public Vector3 Position { get { return _v.Position; } }

        /// <summary>深度排序值。</summary>
        public int SortingOrder { get { return _v.SortingOrder; } }

        /// <summary>5 个渲染字段**逐项相等**（离线断言用）。</summary>
        public bool SameAs(TileRenderState other)
        {
            return _v.SameAs(other._v);
        }

        /// <summary>单行描述（日志/断言输出用）。</summary>
        public override string ToString()
        {
            return _v.ToString();
        }
    }
}
