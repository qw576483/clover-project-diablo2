// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/PlayerLightMask.cs
//
// 以**角色为圆心**的区域光照遮罩的**薄转发**：渲染（一张运行时生成的径向渐变贴图 +
// 一个 `SpriteRenderer`、跟随圆心、按分级剖面压暗）已下沉到引擎 `CloverEngine.WorldLightMask`。
// 本件只留**本工程的口径**：
//   · 半径 = `AreaLighting.RadiusUnits`（出处 / 标定见 `Module/Map/AreaLighting.cs` 文件头）；
//   · 剖面 = `AreaLighting.ShadowAlphaAt` 在 `[0, 半径]` 上的等距采样（分级 alpha 剖面）；
//   · 压到的颜色 = 该区域的环境光（`AreaLighting.IsDark` 的 out 值）；
//   · 覆盖边长 / 层级排序值 / 父节点 = 本工程的视口与层级预算（下面三个常量）。
//
// 位置口径与所有 View 节点同源 —— 必须走 `ViewModule.EntityWorld`（`Module/View/**` 里
// `Root.transform.position` 赋值的唯一入口，带实体 z 次级排序键；源码级不变式由 `movecheck §⑤`
// 逐行扫描）。遮罩不是实体 ⇒ 借**角色自己的 id** 取同一档 z 键（排序由引擎件唯一的
// `sortingOrder` 决定，z 键只为不在 Unity 的"距离决胜"上产生歧义）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using Diablo2.Module.Map;
using UnityEngine;

namespace Diablo2.Module.View
{
    /// <summary>角色光照遮罩（本工程口径 → 引擎 `WorldLightMask` 的薄转发）。</summary>
    internal sealed class PlayerLightMask
    {
        /// <summary>
        /// 贴图分辨率（方形）。半径 2.6 世界单位在 64×64 的覆盖里 = 20.8 px ⇒ 渐变段有足够像素，
        /// 不出现色带。
        /// </summary>
        private const int TextureSize = 512;

        /// <summary>
        /// 四边形覆盖的世界边长：必须**同时**包住"渐变段（半径 2.6）"与"整个视口"——
        /// 视口最大半高 = `CameraRig.MaxOrthographicSize`(12) ⇒ 高 24、16:9 宽 42.7 ≤ 64 ⇒ 够。
        /// </summary>
        private const float SpanUnits = 64f;

        /// <summary>分级 alpha 剖面的采样点数（等距采样，末点 = 半径处）。</summary>
        private const int ProfileSamples = 64;

        /// <summary>遮罩节点名（既有的层级命名）。</summary>
        private const string NodeName = "AreaLightMask";

        /// <summary>
        /// 高于任何格（含物件层 / 实体层）的排序值：最大格坐标和 = 2 × `GameConst.WildernessMaxSize`
        /// ⇒ 取荒野上限那一格的物件层排序值再加一档（洞穴上限 75 &lt; 荒野上限 80，故它覆盖全部区域）。
        /// </summary>
        private static readonly int SortingOrder =
            Iso.SortOrder(GameConst.WildernessMaxSize, GameConst.WildernessMaxSize, GameConst.LayerOffsetOverlay)
            + GameConst.SortOrderStep;

        /// <summary>分级 alpha 剖面（**进程内共享**：只与半径口径有关，与区域 / 角色无关）。</summary>
        private static float[] _profile;

        private readonly WorldLightMask _mask;

        /// <summary>遮罩当前是否生效（false = 该区域不压暗，节点被禁用）。</summary>
        public bool IsOn { get; private set; }

        /// <summary>在 <paramref name="parent"/> 下建遮罩节点（同一父节点下只建一个）。</summary>
        public PlayerLightMask(Transform parent)
        {
            if (parent == null)
            {
                ViewLog.Warn("PlayerLightMask: 父节点为 null（实体根未建？）⇒ 本次不建光照遮罩");
                return;
            }

            _mask = new WorldLightMask(parent, SortingOrder, SpanUnits, TextureSize, NodeName);
            _mask.SetRadius(AreaLighting.RadiusUnits);
            _mask.SetProfile(Profile);
        }

        /// <summary>分级 alpha 剖面：`AreaLighting.ShadowAlphaAt` 在 `[0, 半径]` 上的等距采样（懒建一次）。</summary>
        private static float[] Profile
        {
            get
            {
                if (_profile != null) return _profile;

                var p = new float[ProfileSamples];
                for (var i = 0; i < ProfileSamples; i++)
                {
                    var t = i / (float)(ProfileSamples - 1);
                    p[i] = AreaLighting.ShadowAlphaAt(t * AreaLighting.RadiusUnits);
                }

                _profile = p;
                return _profile;
            }
        }

        /// <summary>遮罩中心跟到角色的**逻辑世界坐标**（每帧一次写入；口径见文件头）。</summary>
        public void Follow(Vector3 playerWorld)
        {
            if (_mask == null) return;
            _mask.Follow(ViewModule.EntityWorld(GameConst.PlayerEntityId, playerWorld));
        }

        /// <summary>
        /// 按区域设定：<paramref name="dark"/> = 该区域是否压暗（false ⇒ 整块禁用，画面一字不变）、
        /// <paramref name="ambient"/> = 半径外压到的颜色（= 官方区域环境光）。
        /// </summary>
        public void Set(bool dark, Color ambient)
        {
            if (_mask == null) return;
            IsOn = dark;
            _mask.SetShadow(new Color(ambient.r, ambient.g, ambient.b, 1f));
            _mask.SetVisible(dark);
        }

        /// <summary>拆掉遮罩节点（贴图 / `Sprite` 归引擎件自持，随节点一起销毁）。</summary>
        public void Dispose()
        {
            if (_mask != null) _mask.Dispose();
        }
    }
}
