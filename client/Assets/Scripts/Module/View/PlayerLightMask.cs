// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/PlayerLightMask.cs
//
// 以**角色为圆心**的区域光照遮罩：一张按半径做好的径向渐变贴图，挂在角色所在世界坐标上，
// 半径内不压暗、半径外压到该区域的环境光色（取值口径见 `Module/Map/AreaLighting`）。
//
// 为什么是"一张贴图"而不是"逐格改色"：
//   · 逐格改色要把每格的 `SpriteRenderer.color` 按距离重算 ⇒ 角色每换一格就得重算一片格
//     （本工程 `MapView` 的整图重铺有逐帧节点预算，不能塞进"每走一格乘一遍"）；
//   · 一次绘制、随角色平移 ⇒ 每帧只有一次 `transform.position` 写入；
//   · 渐变是逐像素的 ⇒ 边缘是柔和圆边，不是按格切出来的方块边。
//
// 坐标口径：本工程"世界坐标 → 屏幕"是**等比**的（`Iso` 的等距形变已经烘进瓦片美术里），
//   所以世界坐标里的圆 = 屏幕上的圆（原版的角色光照也是屏幕上的圆）。
//
// 半径/衰减的出处、以及"罗格营地 / 血腥荒野不压暗"的判据 ⇒ 见 `Module/Map/AreaLighting.cs` 文件头。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Core;
using Diablo2.Module.Map;
using UnityEngine;

namespace Diablo2.Module.View
{
    /// <summary>角色光照遮罩（一个节点 + 一张运行时生成的渐变贴图；不逐帧重算）。</summary>
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

        /// <summary>
        /// 高于任何格（含物件层 / 实体层）的排序值：最大格坐标和 = 2 × `GameConst.WildernessMaxSize`
        /// ⇒ 取荒野上限那一格的物件层排序值再加一档（洞穴上限 75 &lt; 荒野上限 80，故它覆盖全部区域）。
        /// </summary>
        private static readonly int SortingOrder =
            Iso.SortOrder(GameConst.WildernessMaxSize, GameConst.WildernessMaxSize, GameConst.LayerOffsetOverlay)
            + GameConst.SortOrderStep;

        /// <summary>
        /// 渐变贴图（**进程内共享**：只与半径有关，与区域/角色无关 ⇒ 每次进图不重新生成）。
        /// </summary>
        private static Sprite _sprite;

        private GameObject _node;
        private SpriteRenderer _renderer;

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

            _node = new GameObject("AreaLightMask");
            _node.transform.SetParent(parent, false);
            _node.transform.localPosition = Vector3.zero;

            _renderer = _node.AddComponent<SpriteRenderer>();
            _renderer.sprite = Sprite;
            _renderer.sortingOrder = SortingOrder;
            _renderer.enabled = false;
        }

        /// <summary>渐变贴图（懒建一次；`Sprite.Create` 的 PPU 让节点**不需要缩放**就覆盖 `SpanUnits`）。</summary>
        private static Sprite Sprite
        {
            get
            {
                if (_sprite != null) return _sprite;

                var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
                tex.name = "AreaLightMask";
                var px = new Color32[TextureSize * TextureSize];
                var half = TextureSize * 0.5f;
                for (var y = 0; y < TextureSize; y++)
                {
                    for (var x = 0; x < TextureSize; x++)
                    {
                        // 贴图中心 = 角色所在点；一格像素的边长为 SpanUnits / TextureSize 世界单位
                        var du = (x + 0.5f - half) * (SpanUnits / TextureSize);
                        var dv = (y + 0.5f - half) * (SpanUnits / TextureSize);
                        var d = Mathf.Sqrt(du * du + dv * dv);
                        var a = Mathf.RoundToInt(AreaLighting.ShadowAlphaAt(d) * 255f);
                        px[y * TextureSize + x] = new Color32(255, 255, 255, (byte)a);
                    }
                }

                tex.SetPixels32(px);
                tex.Apply(false, false);
                tex.wrapMode = TextureWrapMode.Clamp;

                // PPU = 分辨率 ÷ 覆盖边长 ⇒ 节点不缩放即正好覆盖 SpanUnits × SpanUnits 世界单位。
                //   ⛔ 不要改成缩放节点：半径是以世界单位算的，缩放会连带改半径。
                _sprite = UnityEngine.Sprite.Create(tex, new Rect(0f, 0f, TextureSize, TextureSize),
                    new Vector2(0.5f, 0.5f), TextureSize / SpanUnits);
                _sprite.name = "AreaLightMask";
                return _sprite;
            }
        }

        /// <summary>
        /// 遮罩中心跟到角色的**逻辑世界坐标**（每帧一次写入）。
        /// <para>位置口径与所有 View 节点一致 —— 必须走 <see cref="ViewModule.EntityWorld"/>（`Module/View/**`
        /// 里 `Root.transform.position` 赋值的唯一入口，带实体 z 次级排序键；源码级不变式由 `movecheck §⑤`
        /// 逐行扫描）。遮罩不是实体 ⇒ 借**角色自己的 id** 取同一档 z 键（排序由本件的唯一 `sortingOrder` 决定，
        /// z 键只为不在 Unity 的"距离决胜"上产生歧义）。</para>
        /// </summary>
        public void Follow(Vector3 playerWorld)
        {
            if (_node == null) return;
            _node.transform.position = ViewModule.EntityWorld(GameConst.PlayerEntityId, playerWorld);
        }

        /// <summary>
        /// 按区域设定：<paramref name="dark"/> = 该区域是否压暗（false ⇒ 整块禁用，画面一字不变）、
        /// <paramref name="ambient"/> = 半径外压到的颜色（= 官方区域环境光）。
        /// </summary>
        public void Set(bool dark, Color ambient)
        {
            if (_renderer == null) return;
            IsOn = dark;
            _renderer.enabled = dark;
            _renderer.color = new Color(ambient.r, ambient.g, ambient.b, 1f);
        }

        /// <summary>拆掉节点（贴图是进程内共享件，不随节点销毁）。</summary>
        public void Dispose()
        {
            if (_node != null) Object.Destroy(_node);
            _node = null;
            _renderer = null;
            IsOn = false;
        }
    }
}
