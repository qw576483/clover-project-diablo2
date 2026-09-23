// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/TileRenderState.cs  ★ T0FIX-A 新增
//
// **一格瓦片的全部渲染状态**（不可变值类型）。存在的唯一理由：
//   对象池复用节点时，"复用"与"新建"必须给出**逐项相同**的渲染结果 —— 把状态收成一个
//   5 字段的值，再由 `MapView.ApplyTileState` **无条件**写全 5 个字段，就使这件事成为
//   **结构性保证**（而不是"我记得每次都重设了"）。
//
// 字段与 `SpriteRenderer` 的对应（断言口径 = 这 5 项逐项相等）：
//   Sprite        → SpriteRenderer.sprite
//   Color         → SpriteRenderer.color
//   LocalScale    → transform.localScale
//   Position      → transform.position（世界坐标，含 x/y/z ⇒ **6 个字段值**）
//   SortingOrder  → SpriteRenderer.sortingOrder
//
// ⛔ 本类型**只放渲染状态**：不许塞业务字段（`TileKind`/块号/是否已探索…都进不来）——
//    一旦塞进来，池化路径就会开始"继承上一次的残留状态"。
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>一格瓦片的渲染状态（见文件头；5 个字段 / 6 个字段值）。</summary>
    internal readonly struct TileRenderState
    {
        /// <summary>贴图（null = 占位菱形）。</summary>
        public readonly Sprite Sprite;

        /// <summary>节点颜色（有贴图 = 白；占位 = 可辨的占位色）。</summary>
        public readonly Color Color;

        /// <summary>节点缩放（有贴图 = `契约PPU / 80`；占位 = 1）。</summary>
        public readonly Vector3 LocalScale;

        /// <summary>节点世界坐标（含对齐修正，见 `MapView.PlaceOfPx`）。</summary>
        public readonly Vector3 Position;

        /// <summary>深度排序值（`Iso.SortOrder(g, 层偏移)`）。</summary>
        public readonly int SortingOrder;

        /// <summary>构造（唯一入口；全部字段必须显式给）。</summary>
        public TileRenderState(Sprite sprite, Color color, Vector3 localScale, Vector3 position, int sortingOrder)
        {
            Sprite = sprite;
            Color = color;
            LocalScale = localScale;
            Position = position;
            SortingOrder = sortingOrder;
        }

        /// <summary>
        /// 5 个渲染字段**逐项相等**（离线断言用；不依赖 `Vector3.Equals` 的 epsilon 语义）。
        /// </summary>
        public bool SameAs(TileRenderState other)
        {
            return ReferenceEquals(Sprite, other.Sprite)
                   && Color.r == other.Color.r && Color.g == other.Color.g
                   && Color.b == other.Color.b && Color.a == other.Color.a
                   && LocalScale.x == other.LocalScale.x && LocalScale.y == other.LocalScale.y
                   && LocalScale.z == other.LocalScale.z
                   && Position.x == other.Position.x && Position.y == other.Position.y
                   && Position.z == other.Position.z
                   && SortingOrder == other.SortingOrder;
        }

        /// <summary>单行描述（日志/断言输出用）。</summary>
        public override string ToString()
        {
            return $"sprite={(Sprite != null ? Sprite.name : "(占位)")} color=({Color.r:0.###},{Color.g:0.###}," +
                   $"{Color.b:0.###},{Color.a:0.###}) scale=({LocalScale.x:0.####},{LocalScale.y:0.####}) " +
                   $"pos=({Position.x:0.####},{Position.y:0.####},{Position.z:0.####}) order={SortingOrder}";
        }
    }
}
