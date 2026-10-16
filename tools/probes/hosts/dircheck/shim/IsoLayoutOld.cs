// ─────────────────────────────────────────────────────────────────────────────
//
// 本文件是 `Runtime/Core/IsoLayout.cs` **改动前**那四个成员的**逐字复制**
//   （唯一改动：类名 `IsoLayout` → `IsoLayoutOld`，因为 `IsoLayout` 是 `sealed` 不能继承）。
//   只能就地留一份对照（真实源码没有被它影响：`IsoLayout.cs` 仍是唯一被编进工程的实现）。
//
//   ( 0,+1) → S    ( 0,-1) → N    (+1, 0) → E    (-1, 0) → W
//   (+1,+1) → SE   (-1,+1) → SW   (+1,-1) → NE   (-1,-1) → NW
// 即：整体比"世界位移真正指向的屏幕方向"**少一档**（逆时针偏 45°）。
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace CloverEngine
{
    /// <summary>改动前的 `IsoLayout`（仅方向相关成员 + 两个投影成员，逐字复制）。</summary>
    public sealed class IsoLayoutOld
    {
        public float HalfW { get; }
        public float HalfH { get; }

        public IsoLayoutOld(float halfW, float halfH, int sortOrderStep, int sortOrderBase)
        {
            HalfW = halfW;
            HalfH = halfH;
        }

        public Vector3 GridToWorld(int gx, int gy)
        {
            var x = (gx - gy) * HalfW;
            var y = -(gx + gy + 1) * HalfH;
            return new Vector3(x, y, 0f);
        }

        public Vector2 WorldToGridContinuous(Vector3 world)
        {
            var fx = (world.x / HalfW - world.y / HalfH) * 0.5f;
            var fy = (-world.y / HalfH - world.x / HalfW) * 0.5f;
            return new Vector2(fx, fy);
        }

        public Dir8 DirectionTo(Vector2Int delta)
        {
            var sx = System.Math.Sign(delta.x);
            var sy = System.Math.Sign(delta.y);

            switch (sx)
            {
                case 0:
                    if (sy > 0) return Dir8.S;
                    if (sy < 0) return Dir8.N;
                    return Dir8.S;

                case 1:
                    if (sy > 0) return Dir8.SE;
                    if (sy < 0) return Dir8.NE;
                    return Dir8.E;

                default:
                    if (sy > 0) return Dir8.SW;
                    if (sy < 0) return Dir8.NW;
                    return Dir8.W;
            }
        }

        public Vector2Int DirectionDelta(Dir8 dir)
        {
            switch (dir)
            {
                case Dir8.S: return new Vector2Int(0, 1);
                case Dir8.SW: return new Vector2Int(-1, 1);
                case Dir8.W: return new Vector2Int(-1, 0);
                case Dir8.NW: return new Vector2Int(-1, -1);
                case Dir8.N: return new Vector2Int(0, -1);
                case Dir8.NE: return new Vector2Int(1, -1);
                case Dir8.E: return new Vector2Int(1, 0);
                case Dir8.SE: return new Vector2Int(1, 1);
                default: return Vector2Int.zero;
            }
        }
    }
}
