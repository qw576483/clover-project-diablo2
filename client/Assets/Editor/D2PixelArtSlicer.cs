// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Editor/D2PixelArtSlicer.cs
//
// 做的事：把引擎的**切图规则**（`CloverEngine.Editor.PixelArtSlicingRule`）落地成真正的子精灵切分。
//
//   引擎侧只算「要切成哪几块」（名字 + 矩形），**不自己写子精灵登记** —— 因为真正落地要用官方
//   `UnityEditor.U2D.Sprites.ISpriteEditorDataProvider`，该命名空间属 **U2D 包程序集**，
//   引擎 Editor 程序集（asmdef）只引用引擎自己的程序集，加那条引用会让"没装 2D Sprite 包的工程"
//     会**明确告警**（不会静默不切）。
//
// 与既有 `AssetImporter.cs`（D2AssetImporter）的关系：
//   `AssetImporter.cs` 仍持有**项目实测数据**（多帧条带 / 字体格子的逐帧矩形），那属数据；
//   本文件是**通用落地通道**：数据若配进引擎的切图规则表，走的也是这里。
//   两者不冲突：`AssetImporter.cs` 的目录规则与引擎件是不同入口，先按现状并存。
//
// **已切好就不重写**：重建 `SpriteRect` 会换掉 `spriteID`（= fileID），已引用这些子精灵的
//   prefab / 场景会**断引用**；故与既有实现同口径，逐项比较（名字 + 矩形）相同就直接返回。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine.Editor;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Diablo2.Editor
{
    /// <summary>把引擎切图规则落地为真实子精灵（官方 `ISpriteEditorDataProvider` 流程）。</summary>
    public sealed class D2PixelArtSlicer : IPixelArtSlicer
    {
        /// <summary>开编辑器时自动注册（幂等：重复注册只是覆盖同一个静态槽）。</summary>
        [InitializeOnLoadMethod]
        private static void Wire()
        {
            try
            {
                PixelArtImportPostprocessor.RegisterSlicer(new D2PixelArtSlicer());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Diablo2][PixelArt] 注册切图器失败 ⇒ 命中切图规则时不会切图："
                                 + $"{e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>把请求里的每一块写进 importer；已切好（逐项相同）⇒ 一个字都不改。</summary>
        public bool TryApply(TextureImporter importer, PixelArtSliceRequest request)
        {
            if (importer == null || request == null) return false;

            var items = request.Items;
            if (items == null || items.Count == 0) return false;

            var factories = new SpriteDataProviderFactories();
            factories.Init();

            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null)
            {
                Debug.LogWarning($"[Diablo2][PixelArt] 取不到 ISpriteEditorDataProvider ⇒ 未切分：{request.AssetPath}");
                return false;
            }

            provider.InitSpriteEditorDataProvider();

            var expected = new SpriteRect[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                var it = items[i];
                expected[i] = new SpriteRect
                {
                    name = it.Name,
                    rect = it.Rect,
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    spriteID = GUID.Generate()
                };
            }

            var current = provider.GetSpriteRects();
            if (Same(current, expected)) return true;   // 已切好：不重写 meta（保 spriteID 稳定）

            importer.spriteImportMode = SpriteImportMode.Multiple;
            provider.SetSpriteRects(expected);
            return true;
        }

        /// <summary>比对已切好的矩形（名字 + 矩形逐项；重建会换掉 spriteID ⇒ 必须比）。</summary>
        private static bool Same(SpriteRect[] a, SpriteRect[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;

            for (var i = 0; i < a.Length; i++)
            {
                if (a[i].name != b[i].name || a[i].rect != b[i].rect) return false;
            }

            return true;
        }
    }
}
