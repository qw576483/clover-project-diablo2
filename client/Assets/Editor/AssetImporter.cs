// ============================================================================================
//  Assets/Editor/AssetImporter.cs —— D2 原版素材导入设置（AssetPostprocessor，按目录生效）
//
//  铁律：**新增任何素材目录，必须回到本文件加一条规则**。
//     漏加 = 新素材按 Unity 默认导入（双线性过滤 + 压缩 + mipmap）⇒ 8bit 调色板像素图糊掉，
//
//  生效目录（其余目录一律不碰）
//    Assets/Resources/Clover/D2/**          像素图通用参数：Point / Sprite / PPU=64 / 无压缩 / 无 mipmap / Clamp
//    Assets/Resources/Clover/D2/UI/**        SpriteImportMode = Single（单帧底图）
//                                            例外：MultiFrameStrips 登记的多帧条带 ⇒ Multiple + 实测帧矩形
//                                              **自动吃到同样参数**（Point 过滤 / Sprite / PPU=64 / 无压缩 /
//                                              无 mipmap / Clamp / maxTextureSize 4096）。
//                                                远小于 4096，且都在 `D2UiDir` 前缀下 ⇒ **本文件无需新增规则**；
//                                                判据 = 本文件 `OnPreprocessTexture` 的
//                                                `assetPath.StartsWith(D2UiDir)` 分支先命中，
//                                                再看文件名是否在 `MultiFrameStrips` 表里（两批都**不在**表里）
//                                                ⇒ 落到 `SpriteImportMode.Single`，正是我们要的。
//                                              铁律不变：**只有 `D2/` 下新建"顶层"目录才必须回来加规则**。
//    Assets/Resources/Clover/D2/Fonts/**     SpriteImportMode = Multiple + 按实测格子逐字形切分
//                                              （13806 帧/张，格子 13/19/24/37px，列数见生成器输出），
//                                              **暂按 Single 导入** —— 帧→字符映射虽已拿到
//                                              （`tools/d2codec/export_d2ui.py --only chifont` 同时导出
//                                              `<原版资源>/导出的字体映射/font{N}_chi.tsv`），
//                                              但"工程内怎么用它"未定 ⇒ 先不切 13806 个子 sprite（切了没人用还拖导入）。
//                                              另：font42_chi 图集 4366×4329 > 4096 ⇒ 该批 maxTextureSize=8192，
//                                              否则 Unity 会**降采样**（像素图会糊，且不报错）。
//    Assets/ThirdParty/Diablo2/**            原样副本，同像素图参数（将来从副本取图/替换时不会糊）
//
//  字体切分参数出处（**实测，不是估的**）：素材来自社区复刻工程 mofr/Diablerie，其 Resources/Fonts/
//    font{N}.png 是由 DC6 打包出的图集（打包器 Assets/Scripts/Editor/EditorTools.cs:124-195
//    "Create font from DC6"），逐字形矩形存在同目录的 Unity Font 资产 font{N}.fontsettings 的
//    m_CharacterRects（256 条 uv 矩形）里。本文件把这些 uv 反算成像素格子，结果见 FontGrids 表（含每个字形的原始矩形与行距）。
//
//    Panel/buysellbtn.DC6.0.png  1024x64  22 帧
//    Panel/goldcoinbtn.dc6.0.png   64x32   2 帧
//    Panel/overlap.png            256x128   2 帧
//    Menu/button_medium.png       384x35    3 帧（normal / pressed / disabled）
//    Menu/button_wide.png         816x35    3 帧（normal / pressed / disabled）
//
//  帧矩形的**实测依据**（不是估的、也不是"不透明外接矩形"直接冒充）：
//    · 脚本 `tools/probes/hosts/buildcheck/frame_probe.py`（读真实像素）：
//        - 帧界 = 「非全透明列」的内容块边界（整幅不透明时改用 **RGB 竖缝**定帧界）；
//        - 帧矩形 = 内容块外接矩形；同文件内尺寸不齐时统一取**最大内容宽高**（UI 现取现用要稳定尺寸）；
//        - 脚本自带自证：逐帧断言「窗口含本帧内容」且「窗口不含任何邻帧内容」，失败即非零退出。
//    · 输出留档：`.ai-tmp/test/frame_probe_out.txt`（含每帧的 x/y/w/h 与 C# 字面量）。
//    · 交叉核对：素材源工程（mofr/Diablerie）自带 `.meta` 的 spriteSheet 登记值
//      （帧数、x 起点、y 与实测**完全一致**；宽度差 ≤1px，来源是 alpha 阈值取舍）。
//  改这张表 = 改契约：重跑 frame_probe.py，把新的 C# 字面量抄进 MultiFrameStrips，并更新上面的注释。
//
//  程序集说明：本文件位于 Assets/Editor，项目当前**没有** Editor 的 asmdef ⇒ 归 Unity 预定义程序集
//    Assembly-CSharp-Editor（自动引用包程序集）。若将来给 Assets/Editor 加 asmdef，必须让它引用
//    Unity.2D.Sprite.Editor，否则本文件编译不过。
// ============================================================================================

using System;
using UnityEditor;
using UnityEditor.U2D.Sprites; // SpriteDataProviderFactories / ISpriteEditorDataProvider
                                // 出处：包 com.unity.2d.sprite →
                                //   Editor/Interface/ISpriteEditorDataProvider.cs:43（接口 + 用法示例）
                                //   Editor/SpriteEditor/SpriteEditorWindow.cs:51（SpriteDataProviderFactories）
                                //   Documentation~/DataProvider.md:18-30（AssetPostprocessor 里的用法）
using UnityEngine;

namespace Diablo2.Editor
{
    /// <summary>
    /// D2 原版素材（8bit 调色板像素图）的导入设置：按目录统一覆盖 Unity 默认导入参数。
    /// </summary>
    /// <remarks>
    /// 类名不叫 <c>AssetImporter</c>：避免与 <see cref="UnityEditor.AssetImporter"/> 同名冲突
    /// </remarks>
    public sealed class D2AssetImporter : AssetPostprocessor
    {
        // ── 路径常量（与 Core/ResPaths.cs 的 D2Ui / D2Fonts 同源；换目录要同步两处）──
        private const string D2Root = "Assets/Resources/Clover/D2/";
        private const string D2UiDir = D2Root + "UI/";
        private const string D2FontsDir = D2Root + "Fonts/";
        private const string ArchiveRoot = "Assets/ThirdParty/Diablo2/";

        // ── 导入参数 ──
        // 刻意不引用 Diablo2 程序集的 GameConst：Editor 侧不依赖业务程序集（conventions.md 目录边界）。
        // 两处同值，改一处必须同步另一处。
        private const int PixelsPerUnit = 64;
        private const int MaxTextureSize = 4096; // 最大的是 font42 = 1024²，留余量
        private const int MaxTextureSizeChiFont = 8192; // ★ 片 1：中文位图字体图集（font42_chi = 4366×4329）
        private const int FontGlyphCount = 256;  // D2 位图字体固定 256 个字形（字符码 0..255）
        private const string LogTag = "[D2.AssetImporter]";

        /// <summary>位图字体图集的切分参数（全部实测，单位 px）。</summary>
        private sealed class FontGridSpec
        {
            public readonly string FileName;   // 不带扩展名
            public readonly int Cols;          // 图集列数
            public readonly int Rows;          // 图集行数（Cols*Rows ≥ 256，尾部空格子不切）
            public readonly int CellW;         // 格子宽 = 该图集字形最大宽 + 对齐余量
            public readonly int CellH;         // 格子高 = 字形高 + 2px 打包间隙
            public readonly int TexSize;       // 图集边长（正方形）
            public readonly int LineHeight;    // 源 Font 资产的 m_LineSpacing（多行文本行距）

            public FontGridSpec(string fileName, int cols, int rows, int cellW, int cellH, int texSize, int lineHeight)
            {
                FileName = fileName;
                Cols = cols;
                Rows = rows;
                CellW = cellW;
                CellH = cellH;
                TexSize = texSize;
                LineHeight = lineHeight;
            }
        }

        /// <summary>字体切分表（每行都是实测结果；新增字体必须补一行，否则退回 Single 并告警）。</summary>
        private static readonly FontGridSpec[] FontGrids =
        {
            //                文件       列   行  格宽 格高  图集  行距
            new FontGridSpec("font16", 32, 8, 16, 18, 512, 16),
            new FontGridSpec("font24", 21, 13, 24, 28, 512, 24),
            new FontGridSpec("font30", 16, 16, 31, 32, 512, 30),
            new FontGridSpec("font42", 24, 11, 41, 43, 1024, 42),
        };

        /// <summary>九宫格边距（Vector4 = 左/下/右/上，单位 px）。</summary>
        private sealed class NineSliceSpec
        {
            public readonly string FileName;
            public readonly Vector4 Border;

            public NineSliceSpec(string fileName, Vector4 border)
            {
                FileName = fileName;
                Border = border;
            }
        }

        /// <summary>
        /// 只登记「实测存在同质中段（可无损拉伸）」的图。
        /// 其余面板/按钮按原版尺寸 1:1 用（D2 的石头纹理面板没有可平铺中段，拉伸会失真），故 border 保持 0。
        /// </summary>
        private static readonly NineSliceSpec[] NineSlices =
        {
            // 实测：173x26 中，中间 7px 竖条逐列完全相同 ⇒ 可横向拉伸；上下/左右边框保留装饰
            new NineSliceSpec("minipanel.png", new Vector4(83f, 11f, 83f, 11f)),
            // 实测：50x5 整幅同质（纯填充条）⇒ 无边框，任意拉伸都不失真
            new NineSliceSpec("ExperienceBar.png", Vector4.zero),
        };

        //  帧矩形全部**实测**（见文件头「实测依据」）；改表必须重跑 tools/probes/hosts/buildcheck/frame_probe.py。
        //  取帧方式（UI 侧）：`Resources.LoadAll<Sprite>("Clover/D2/UI/Panel/buysellbtn.DC6.0")`
        //  拿到全部帧；或按子资源名 `{文件名}_{帧号}` 逐帧取（与字体图集同口径，
        //  见 `UI/D2Text.cs` 的 `AtlasPath + "_" + i` 取法）。帧号从 0 起。
        private sealed class StripSpec
        {
            /// <summary>不带扩展名的文件名（与 <see cref="GetFileName"/> 同口径）。</summary>
            public readonly string FileName;

            /// <summary>实测图幅宽（与登记不符 ⇒ 切分会整体错位，必须告警）。</summary>
            public readonly int TexW;

            /// <summary>实测图幅高。</summary>
            public readonly int TexH;

            /// <summary>帧矩形（Unity 纹理坐标：**左下原点、y 轴向上**）。</summary>
            public readonly Rect[] Rects;

            /// <summary>实测口径备注（每帧矩形怎么来的，写清可复查）。</summary>
            public readonly string Note;

            public StripSpec(string fileName, int texW, int texH, Rect[] rects, string note)
            {
                FileName = fileName;
                TexW = texW;
                TexH = texH;
                Rects = rects;
                Note = note;
            }
        }

        /// <summary>多帧条带切分表（帧矩形逐条实测，见文件头「实测依据」）。</summary>
        private static readonly StripSpec[] MultiFrameStrips =
        {
            // ── 买卖按钮（商店）22 帧 ──
            // 实测：非全透明列分 23 块，前 22 块是帧（间距 35/33 交替 ⇒ 打包 padding=2）；
            //       第 23 块 x=748..779 只有 1 行内容（y=1）⇒ 判为打包残留，**不作为帧**。
            //       各帧内容 32x31 / 30x30（第 21 帧 32x32）⇒ 统一取 32x32、统一 y=31（底边对齐）。
            new StripSpec("buysellbtn.DC6.0", 1024, 64, new[]
            {
                new Rect(0, 31, 32, 32), new Rect(35, 31, 32, 32), new Rect(68, 31, 32, 32),
                new Rect(103, 31, 32, 32), new Rect(136, 31, 32, 32), new Rect(171, 31, 32, 32),
                new Rect(204, 31, 32, 32), new Rect(239, 31, 32, 32), new Rect(272, 31, 32, 32),
                new Rect(307, 31, 32, 32), new Rect(340, 31, 32, 32), new Rect(375, 31, 32, 32),
                new Rect(408, 31, 32, 32), new Rect(443, 31, 32, 32), new Rect(476, 31, 32, 32),
                new Rect(511, 31, 32, 32), new Rect(544, 31, 32, 32), new Rect(579, 31, 32, 32),
                new Rect(612, 31, 32, 32), new Rect(647, 31, 32, 32), new Rect(680, 31, 32, 32),
                new Rect(715, 31, 32, 32),
            },
                "实测内容块 23 块（前 22 为帧），内容 x 起点 0/35/68/.../715（间距 35/33 交替）；"
                + "统一窗口 32x32、y=31；自证：每帧窗口含本帧内容且不与邻帧内容重叠"),

            // ── 金币按钮 2 帧 ──
            new StripSpec("goldcoinbtn.dc6.0", 64, 32, new[]
            {
                new Rect(0, 13, 20, 17), new Rect(22, 13, 20, 17),
            },
                "实测 2 个内容块 x=[0..19] / [22..41]，均 20x17，自顶向下 y=[2..18] ⇒ Unity y=13"),

            // ── 背包/属性面板的叠加框 2 帧 ──
            new StripSpec("overlap", 256, 128, new[]
            {
                new Rect(0, 39, 82, 88), new Rect(84, 39, 82, 88),
            },
                "实测 2 个内容块 x=[0..81] / [84..163]（第 1 块高 81、第 2 块高 88）⇒ 统一 82x88、y=39"),

            // ── 主菜单按钮 3 态（整幅 384x35 全不透明 ⇒ 用 RGB 竖缝定帧界）──
            new StripSpec("button_medium", 384, 35, new[]
            {
                new Rect(0, 0, 128, 35), new Rect(128, 0, 128, 35), new Rect(256, 0, 128, 35),
            },
                "整幅不透明（alpha 只有 255），实测 RGB 变化最强的 2 条竖缝在 x=127/255 ⇒ 帧宽 128"),
            new StripSpec("button_wide", 816, 35, new[]
            {
                new Rect(0, 0, 272, 35), new Rect(272, 0, 272, 35), new Rect(544, 0, 272, 35),
            },
                "整幅不透明（alpha 只有 255），实测 RGB 变化最强的 2 条竖缝在 x=271/543 ⇒ 帧宽 272"),
        };

        // ─────────────────────────────────────────────────────────────────────────────────────
        //  导入前：按目录设置导入参数（原版像素图 → 必须覆盖 Unity 默认值）
        // ─────────────────────────────────────────────────────────────────────────────────────
        private void OnPreprocessTexture()
        {
            var importer = assetImporter as TextureImporter;
            if (importer == null)
            {
                // 非预期分支：.png 理论上一定是 TextureImporter，走到这里说明资源扩展名/导入器不对
                Debug.LogWarning($"{LogTag} 非纹理导入器，跳过：{assetPath} ({assetImporter?.GetType().Name})");
                return;
            }

            bool inRuntime = assetPath.StartsWith(D2Root, StringComparison.Ordinal);
            bool inArchive = assetPath.StartsWith(ArchiveRoot, StringComparison.Ordinal);
            if (!inRuntime && !inArchive)
                return; // 项目里其它贴图（引擎/包/图标）不归本处理器管

            ApplyPixelArt(importer);

            if (inRuntime && assetPath.StartsWith(D2FontsDir, StringComparison.Ordinal))
            {
                //   必须排在字体切分分支**之前**：它不在 FontGrids 表里，走下面会打一条"未登记切分参数"
                //   的 Warn（误导：那是**有意**不登记），且 4366×4329 的图会被 4096 上限**降采样**（糊，不报错）。
                if (IsChineseFontAtlas(assetPath))
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.maxTextureSize = MaxTextureSizeChiFont;
                    importer.spritePivot = new Vector2(0.5f, 0.5f);
                    return;
                }

                importer.spriteImportMode = SpriteImportMode.Multiple;
                ApplyFontGridSlicing(importer);
                return;
            }

            //   必须排在下面通用 Single 分支**之前**：按 Single 导入时整幅 1024x64 被当成一张图，
            //   UI 侧取不到单帧（且**不报任何错**）。
            StripSpec strip = FindStrip(assetPath);
            if (strip != null)
            {
                importer.spriteImportMode = SpriteImportMode.Multiple;
                ApplyStripSlicing(importer, strip);
                return;
            }

            importer.spriteImportMode = SpriteImportMode.Single;
            ApplyNineSliceBorder(importer);
        }

        /// <summary>像素图通用导入参数（原版素材是 8bit 调色板图，默认导入会糊）。</summary>
        private static void ApplyPixelArt(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.spritePivot = new Vector2(0.5f, 0.5f); // UI 用不上轴心；角色/怪物轴心（脚底中心）由各自目录规则另设
            importer.filterMode = FilterMode.Point;         // ★ 像素图的命门：默认双线性会把点阵磨圆
            importer.wrapMode = TextureWrapMode.Clamp;      // 血/蓝球做填充时不能采样到对边像素
            importer.alphaIsTransparency = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.mipmapEnabled = false;                 // 等距游戏基本 1:1 像素，mipmap 只会让远处糊
            importer.textureCompression = TextureImporterCompression.Uncompressed; // 有损压缩会污染调色板色
            importer.maxTextureSize = MaxTextureSize;
            importer.isReadable = false;
        }

        /// <summary>给登记过的图设置九宫格边距（未登记的保持 0 = 不切片）。</summary>
        private void ApplyNineSliceBorder(TextureImporter importer)
        {
            string fileName = GetFileName(assetPath);
            for (int i = 0; i < NineSlices.Length; i++)
            {
                if (!string.Equals(NineSlices[i].FileName, fileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                importer.spriteBorder = NineSlices[i].Border;
                return;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────────
        //  多帧条带：Multiple + 按实测帧矩形逐帧切分（与字体同一套官方 DataProvider 流程）
        // ─────────────────────────────────────────────────────────────────────────────────────
        private void ApplyStripSlicing(TextureImporter importer, StripSpec spec)
        {
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null)
            {
                // 非预期分支：拿不到 DataProvider ⇒ 条带会退回整幅单帧（UI 取不到单帧），必须点名
                Debug.LogWarning($"{LogTag} 取不到 ISpriteEditorDataProvider，多帧条带未切分：{assetPath}");
                return;
            }

            provider.InitSpriteEditorDataProvider();

            SpriteRect[] expected = BuildStripRects(spec);
            SpriteRect[] current = provider.GetSpriteRects();
            if (Matches(current, expected))
                return; // 已切好：不重写 meta，保证每个 Sprite 的 spriteID（= fileID）跨次导入稳定

            provider.SetSpriteRects(expected);
            provider.Apply();
            Debug.Log($"{LogTag} 多帧条带已切分：{assetPath} → {expected.Length} 帧；{spec.Note}");
        }

        /// <summary>按 StripSpec 生成逐帧 sprite 矩形（帧名 = `{文件名}_{帧号}`）。</summary>
        private static SpriteRect[] BuildStripRects(StripSpec spec)
        {
            var rects = new SpriteRect[spec.Rects.Length];
            for (int i = 0; i < spec.Rects.Length; i++)
            {
                rects[i] = new SpriteRect
                {
                    name = spec.FileName + "_" + i, // 帧号从 0 起；索引与实测表一一对应
                    rect = spec.Rects[i],
                    alignment = SpriteAlignment.Center,  // UI 按钮/图标用中心轴心（不是字体的左上角）
                    pivot = new Vector2(0.5f, 0.5f),
                    border = Vector4.zero,
                    spriteID = GUID.Generate(),
                };
            }
            return rects;
        }

        // ─────────────────────────────────────────────────────────────────────────────────────
        //  字体：Multiple + 按实测格子逐字形切分（官方 SpriteEditorDataProvider）
        // ─────────────────────────────────────────────────────────────────────────────────────
        private void ApplyFontGridSlicing(TextureImporter importer)
        {
            FontGridSpec spec = FindFontGrid(assetPath);
            if (spec == null)
            {
                // 非预期分支：新增字体没登记参数 ⇒ 退回单帧并点名，避免"切错还不报错"
                importer.spriteImportMode = SpriteImportMode.Single;
                Debug.LogWarning($"{LogTag} 字体图集未登记切分参数，已退回 Single：{assetPath}（请在 FontGrids 表补一行）");
                return;
            }

            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null)
            {
                Debug.LogWarning($"{LogTag} 取不到 ISpriteEditorDataProvider，字体未切分：{assetPath}");
                return;
            }

            provider.InitSpriteEditorDataProvider();

            SpriteRect[] expected = BuildFontRects(spec);
            SpriteRect[] current = provider.GetSpriteRects();
            if (Matches(current, expected))
                return; // 已切好：不重写 meta，保证每个 Sprite 的 spriteID（= fileID）跨次导入稳定

            provider.SetSpriteRects(expected);
            provider.Apply();
        }

        /// <summary>按 FontGridSpec 生成 256 个字形矩形。</summary>
        private static SpriteRect[] BuildFontRects(FontGridSpec spec)
        {
            var rects = new SpriteRect[FontGlyphCount];
            for (int i = 0; i < FontGlyphCount; i++)
            {
                int col = i % spec.Cols;
                int row = i / spec.Cols; // 图集是自顶向下、自左向右逐字形打包的

                // 行号 → Unity 矩形：Unity 的 sprite 矩形 y 轴自底向上，必须翻 y。
                //   忘了翻会整表上下错位，而且"看起来也像对的"，很容易蒙混过去。
                var rect = new Rect(
                    col * spec.CellW,
                    spec.TexSize - (row + 1) * spec.CellH,
                    spec.CellW,
                    spec.CellH);

                rects[i] = new SpriteRect
                {
                    name = spec.FileName + "_" + i, // 索引 = D2 字符码 0..255（可直接当 char 索引查）
                    rect = rect,
                    alignment = SpriteAlignment.Custom,
                    pivot = new Vector2(0f, 1f),    // 左上角：文本排版时"光标原点 = 字形左上角"
                    border = Vector4.zero,
                    spriteID = GUID.Generate(),
                };
            }
            return rects;
        }

        /// <summary>比对已切好的矩形，避免每次导入都重建（重建会换掉 spriteID，破坏外部引用）。</summary>
        private static bool Matches(SpriteRect[] current, SpriteRect[] expected)
        {
            if (current == null || current.Length != expected.Length)
                return false;

            for (int i = 0; i < expected.Length; i++)
            {
                if (current[i] == null || current[i].rect != expected[i].rect)
                    return false;
            }
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────────────────────
        //  导入后：尺寸校验（切分参数与图集不符时点名，防止"切错但不报错"）
        // ─────────────────────────────────────────────────────────────────────────────────────
        private void OnPostprocessTexture(Texture2D texture)
        {
            // 多帧条带：图幅与登记参数不符 ⇒ 帧矩形会整体错位（同样是"切错但不报错"），必须点名
            StripSpec strip = FindStrip(assetPath);
            if (strip != null && (texture.width != strip.TexW || texture.height != strip.TexH))
            {
                Debug.LogWarning(
                    $"{LogTag} 条带图幅与登记参数不符，切分结果会错位：{assetPath} " +
                    $"实测 {texture.width}x{texture.height}，登记 {strip.TexW}x{strip.TexH}（重跑 frame_probe.py 后改 MultiFrameStrips）");
            }

            if (!assetPath.StartsWith(D2FontsDir, StringComparison.Ordinal))
                return;

            // 中文位图字体整幅图集：**有意**不切分、尺寸上限另行放宽 ⇒ 不做"尺寸与切分参数不符"的校验
            if (IsChineseFontAtlas(assetPath))
                return;

            FontGridSpec spec = FindFontGrid(assetPath);
            if (spec == null)
                return; // 已在 OnPreprocessTexture 里告警过，不重复刷屏

            if (texture.width != spec.TexSize || texture.height != spec.TexSize)
            {
                Debug.LogWarning(
                    $"{LogTag} 字体图集尺寸与登记参数不符，切分结果会错位：{assetPath} " +
                    $"实测 {texture.width}x{texture.height}，登记 {spec.TexSize}x{spec.TexSize}（重算 FontGrids 后改这里）");
            }
        }

        // ── 小工具 ──
        /// <summary>按文件名在 MultiFrameStrips 表里找登记项；未登记返回 null（= 走单帧）。</summary>
        private static StripSpec FindStrip(string path)
        {
            string fileName = GetFileName(path);
            for (int i = 0; i < MultiFrameStrips.Length; i++)
            {
                if (string.Equals(MultiFrameStrips[i].FileName, fileName, StringComparison.OrdinalIgnoreCase))
                    return MultiFrameStrips[i];
            }
            return null;
        }

        private static FontGridSpec FindFontGrid(string path)
        {
            string fileName = GetFileName(path);
            for (int i = 0; i < FontGrids.Length; i++)
            {
                if (string.Equals(FontGrids[i].FileName, fileName, StringComparison.OrdinalIgnoreCase))
                    return FontGrids[i];
            }
            return null;
        }

        private static string GetFileName(string path)
        {
            int slash = path.LastIndexOf('/');
            string name = slash >= 0 ? path.Substring(slash + 1) : path;
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        /// <summary>
        /// 命名口径与生成员一致（`UI/../Fonts/font16_chi.png` 这类）；判据只用文件名，够稳。
        /// </summary>
        private static bool IsChineseFontAtlas(string assetPath)
        {
            return GetFileName(assetPath).EndsWith("_chi", StringComparison.OrdinalIgnoreCase);
        }
    }
}
