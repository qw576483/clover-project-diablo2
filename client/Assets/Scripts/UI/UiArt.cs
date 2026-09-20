// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/UiArt.cs
// 流程面板（Boot/MainMenu/Settings/CharSelect/CharCreate/Loading/Pause）共用的
// **代码搭 UI 小工具**：铺满根节点 / 原版贴图背景 / 原版底图按钮 / 文本 / 输入框。
//
// 为什么需要它（而不是每个面板各写一份）：
//   7 个面板都要「铺满父层 + 加载原版贴图 + 造按钮」；各写一份必然出现
//   「有的面板根节点没铺满」这类静默失效（`docs/步骤文档.md` §3.4）。
//   本文件只封装**引擎 `UIFactory` 之上的项目约定**（配色 / 字号 / 贴图加载兜底 / 原版亮度），
//   锚点/铺满一律转发 `UIFactory`，**不自己再写一套锚点工具**。
//   ⛔ 本文件在 UI 层，只允许引用 `CloverEngine` / `Diablo2.Core` / `Diablo2.Def`，
//      **不得引用 `Diablo2.Module` 下的任何类型**（分层自检 ③）。
//
// ★ agent-14 §C 修复（界面保真：背景太暗 / 未用原版贴图）——**两条根因，逐条钉死**：
//
//   根因 ①（★ 真正把原版屏压成黑的那条）：`Image.color` 是**贴图的颜色乘数**，
//     而本工具原来是「拿**深色占位色**当底色 + 贴图到位后**只换 sprite、不改 color**」
//     ⇒ 原版屏被乘上 0.04~0.05 的亮度，看起来就是"极暗 / 纯黑底"，**且不报任何错**。
//     实测证据（`client/Assets/Screenshots/p02_mainmenu.png`）：同一帧、同一 Canvas 里
//     **按钮文字是亮的**，只有背景贴图是暗的 ⇒ 与「2D 光照」无关，就是逐 Image 的 color 乘数。
//     现改为：**贴图加载成功 ⇒ color = 原版亮度（白）**；贴图缺失时**保留**占位底色
//     （纯色占位仍然可见，不静默变黑）。
//
//   根因 ②（防复发）：本工程是 **URP 2D**，精灵默认材质 `Sprite-Lit-Default` 受 2D 光照影响；
//     引擎 `UIManager` 建的是 `RenderMode.ScreenSpaceOverlay` 的 Canvas
//     （`Runtime/Presentation/UI.cs:49`），UI Image 走 Canvas 默认材质 `UI/Default`（unlit）⇒
//     **本来就不受光照影响**。但只要有谁给 Image 挂了受光材质，就会静默变暗 ⇒
//     `EnsureUnlit()` 在**每一个**本工具造出来的 Image 上做一次显式校验，
//     发现受光材质就换回 UI 默认材质并告警（`IsLitShader` 是纯函数，离线宿主可断言）。
//
//   §C-④：按钮底图接**原版帧**（`ResPaths.Frame(ResPaths.MenuButtonWide, 0/1/2)`
//     = 常态/悬停/按下；窄按钮用 `MenuButtonMedium`），走 uGUI `SpriteSwap`。
//     取帧前的**帧数/帧名/条带导入模式**已由 `tools/uicheck` 逐条对磁盘核对（`.png.meta`）。
//     ⚠️ **实测**（Play 2026-09-17，探针 `client/_dev/p_frames.cs`）：
//       逐帧按名 `LoadAsset<Sprite>("D2/UI/Menu/button_wide_0")` = **null**（本工程这套导入设置下失效），
//       而整条 `Game.Res.LoadAll<Sprite>("D2/UI/Menu/button_wide")` = 3 个名字正确的子 sprite。
//       ⇒ 逐帧按名为主、**整条 LoadAll 兜底**（`TryBulkLoad`），与 `UI/D2Text.cs` 的字模加载同一套做法。
//       （片 34 起这两条都走引擎资源模块 `Game.Res.*`，⛔ 不再直接调 Unity 的 `Resources`；
//         路径是**相对 `CloverRes` 根前缀**的写法，根前缀由后端拼 —— 出处 `Contracts.cs` 的 `LoadAll` 注释。）
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CloverEngine;
using Diablo2.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>流程面板共用的 UI 构件工具。</summary>
    internal static class UiArt
    {
        // ── 布局基准 ─────────────────────────────────────────────────────────
        // ⚠️ 引擎 `UIManager` 构造时把 CanvasScaler 固定为 ScaleWithScreenSize +
        //    referenceResolution=(1920,1080) + match=0.5（`Runtime/Presentation/UI.cs:52-56`）。
        //    因此**面板坐标单位 = 1920×1080 参考像素**（`GameConst.UiReferenceWidth/Height`
        //    = 1280×720 是策划口径，与引擎实现不同，见回报「未决」）。
        public const float RefWidth = 1920f;
        public const float RefHeight = 1080f;

        /// <summary>
        /// 原版宽按钮（`Resources/Clover/D2/UI/Menu/button_wide`）单帧尺寸 = **272×35**。
        /// 出处：`Assets/Editor/AssetImporter.cs` 的 `MultiFrameStrips` 里
        /// `button_wide` 三条实测帧矩形（整幅 816×35 按 RGB 竖缝切成 3 帧 ⇒ 每帧 272×35）。
        /// </summary>
        public static readonly Vector2 MenuButtonSize = new Vector2(272f, 35f);

        /// <summary>
        /// 原版中等按钮（`Menu/button_medium`）单帧尺寸 = **128×35**（整幅 384×35 ⇒ 3 帧）。
        /// 窄按钮（职业按钮 / ± / 进入 / 删除 …）用它做底图，避免把 272 宽的装饰框压扁。
        /// </summary>
        public static readonly Vector2 MenuButtonMediumSize = new Vector2(128f, 35f);

        /// <summary>按钮宽度 ≥ 此值 ⇒ 用宽按钮底图，否则用中等按钮底图（纯函数，离线可断言）。</summary>
        public const float WideButtonMinWidth = 200f;

        // ── 配色（照原版暗黑、低饱和的暖褐 + 冷蓝；纯色块不引入通用素材）────
        /// <summary>按钮底板色（**仅贴图未到位/缺失时的纯色占位**；贴图到位后走原版亮度）。</summary>
        public static readonly Color ButtonBg = new Color(0.15f, 0.13f, 0.11f, 0.94f);

        /// <summary>
        /// **原版亮度**：贴图加载成功后的颜色乘数（白色 = 不做任何压暗 ⇒ 按素材本来的亮度显示）。
        /// 这是 §C 的核心修复点：以前贴图到位后仍乘着 0.04~0.05 的深色占位底色。
        /// </summary>
        public static readonly Color ArtFullBright = Color.white;

        /// <summary>未选中项的原版贴图色调（压暗一档，用于「5 职业」里非当前职业的按钮）。</summary>
        public static readonly Color ArtDim = new Color(0.62f, 0.62f, 0.62f, 1f);

        /// <summary>按钮文字色（暗金的暖黄）。</summary>
        public static readonly Color ButtonText = new Color(0.91f, 0.82f, 0.52f, 1f);

        /// <summary>按钮不可用时的文字色。</summary>
        public static readonly Color ButtonTextDisabled = new Color(0.45f, 0.43f, 0.40f, 1f);

        /// <summary>面板底板色（半透明黑，用于暂停/设置）。</summary>
        public static readonly Color PanelBg = new Color(0.06f, 0.05f, 0.05f, 0.94f);

        /// <summary>整屏遮罩色。</summary>
        public static readonly Color Overlay = new Color(0f, 0f, 0f, 0.72f);

        /// <summary>正文色。</summary>
        public static readonly Color TextColor = new Color(0.88f, 0.86f, 0.80f, 1f);

        /// <summary>标题色。</summary>
        public static readonly Color TitleColor = new Color(0.95f, 0.87f, 0.60f, 1f);

        /// <summary>数值/进度色。</summary>
        public static readonly Color AccentColor = new Color(0.85f, 0.32f, 0.24f, 1f);

        /// <summary>输入框底色。</summary>
        public static readonly Color InputBg = new Color(0.09f, 0.08f, 0.07f, 0.96f);

        private const string Tag = "Ui";

        // ── 原版贴图的「色调」状态（★ 解决异步竞态）──────────────────────────
        // 贴图是 `LoadAsset` **异步**回来的：面板在贴图回来**之后**才改色（如创角屏的
        // 「当前选中职业」）会被这次回调覆盖成白色 ⇒ 把"这幅图想要的颜色乘数"记在
        // `ConditionalWeakTable` 上（键是 Image，Image 被销毁后条目随 GC 消失，不泄漏），
        // 由**加载完成的那一刻**统一套用。这样两种顺序都对。
        private sealed class ArtState
        {
            /// <summary>贴图到位后要套的色调（默认 = 原版亮度）。</summary>
            public Color Tint = ArtFullBright;
        }

        private static readonly ConditionalWeakTable<Image, ArtState> ArtStates
            = new ConditionalWeakTable<Image, ArtState>();

        private static ArtState StateOf(Image img) => ArtStates.GetOrCreateValue(img);

        /// <summary>
        /// 设置某幅原版贴图的色调（选中/未选中、空球底 …）。
        /// 贴图**已在**则立即生效；**未到**则记下，等 `SetSprite` 的回调套用（不丢状态）。
        /// </summary>
        public static void SetArtTint(Image img, Color tint)
        {
            if (img == null) return;
            StateOf(img).Tint = tint;
            if (img.sprite != null) img.color = tint;   // 贴图已在 ⇒ 立即生效
        }

        // ── 根节点 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 把面板根节点铺满父层（引擎 `UIManager` 把面板挂到固定层节点下，
        /// 但**预制体根节点的锚点不可信** ⇒ 每次打开都显式铺满一次，幂等）。
        /// 缺 RectTransform 时补一个（agent-10 生成的空壳预制体可能只有 Transform）。
        /// </summary>
        /// <returns>面板内容根（铺满父层）。</returns>
        public static RectTransform PrepareRoot(GameObject root)
        {
            if (root == null)
            {
                Log.Error(Tag, "UiArt.PrepareRoot 收到 null 根节点");
                return null;
            }

            var rt = root.GetComponent<RectTransform>();
            if (rt == null)
            {
                Log.Warn(Tag, $"面板根节点 {root.name} 缺 RectTransform，已补一个（预制体应为 UI 节点）");
                rt = root.AddComponent<RectTransform>();
            }

            UIFactory.Stretch(rt);
            return rt;
        }

        // ── 构件 ────────────────────────────────────────────────────────────

        /// <summary>造一块纯色矩形（居中定尺）。</summary>
        public static Image Panel(Transform parent, string name, Vector2 size, Vector2 pos, Color color,
            bool raycastTarget)
        {
            var rt = UIFactory.CreateCentered(name, parent, size, pos);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycastTarget;
            // Image.sprite 为 null 也能渲染（纯色四边形）——所以进度条/底板不依赖任何美术。
            EnsureUnlit(img);
            return img;
        }

        /// <summary>造一块铺满父层的纯色矩形（整屏遮罩 / 背景底色）。</summary>
        public static Image FullPanel(Transform parent, string name, Color color, bool raycastTarget)
        {
            var rt = UIFactory.CreateNode(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycastTarget;
            EnsureUnlit(img);
            return img;
        }

        /// <summary>
        /// 造铺满父层的**原版贴图**背景（异步加载；素材缺失时保留纯色底 + Warn，不阻塞）。
        /// <para>★ 贴图到位后按**原版亮度**（白）显示 —— 见文件头"根因 ①"。</para>
        /// </summary>
        /// <param name="fallback">贴图缺失时可见的纯色底（**只在缺失时**可见，不再是贴图的颜色乘数）。</param>
        public static Image Backdrop(Transform parent, string spritePath, Color fallback)
        {
            var img = FullPanel(parent, "Backdrop", fallback, false);
            SetSprite(img, spritePath);
            return img;
        }

        /// <summary>
        /// 放一张**原版贴图**（定尺、居中定位于父层中心偏移 <paramref name="pos"/>）。
        /// <para>★ agent-18 §B 新增：本轮把面板底图 / 图标 / 标题条从"纯色占位"换成原版 DC6 解出的 PNG，
        /// 每个地方都要"建 Image + 异步贴图"，收在这里避免各面板各写一遍。</para>
        /// <para>贴图缺失时**保留白底**（会看到一块白色）并 Warn 点名路径 —— 不静默、也不假装成功。</para>
        /// </summary>
        public static Image Art(Transform parent, string name, string spritePath, Vector2 size, Vector2 pos,
            bool raycastTarget = false)
        {
            var img = Panel(parent, name, size, pos, Color.white, raycastTarget);
            SetSprite(img, spritePath);
            return img;
        }

        /// <summary>
        /// 放一条**原版中文标题条**（`data/local/ui/chi/**` 解出的红金标题，见 <see cref="ResPaths.D2UiBanner"/>）。
        /// <para>
        /// ★ agent-18 §B 新增。用 `Image.preserveAspect = true` + **外框**（<paramref name="box"/>）的方式：
        /// 原版这些标题条的宽度各不相同（实测 74/111/148/256 …），写死尺寸必然有的被压扁 ⇒ 给个
        /// 上界框、让 uGUI 等比缩放居中，**原版像素不会被拉变形**。
        /// </para>
        /// </summary>
        public static Image Banner(Transform parent, string name, string spritePath, Vector2 box, Vector2 pos)
        {
            var img = Panel(parent, name, box, pos, Color.white, false);
            img.preserveAspect = true;
            SetSprite(img, spritePath);
            return img;
        }

        /// <summary>
        /// 异步把 `Resources/Clover/{path}`（或 `ResPaths` 给的帧名 `xxx_0`）的 Sprite 贴到 Image 上。
        /// <para>
        /// ★ 加载**成功** ⇒ `color = 原版亮度`（或调用方经 <see cref="SetArtTint"/> 记下的色调）；
        ///   加载**失败** ⇒ 保留调用方设的占位底色并打 Warn（纯色占位可见，不静默变黑/变透明）。
        /// </para>
        /// </summary>
        /// <param name="onLoadedTint">可选的显式色调；不传 = 用记下的色调（默认原版亮度白）。</param>
        public static void SetSprite(Image img, string spritePath, Color? onLoadedTint = null)
        {
            if (img == null || string.IsNullOrEmpty(spritePath)) return;

            if (Game.Res == null)
            {
                Log.WarnOnce(Tag, "res.null", $"Game.Res 未初始化（CloverRes.Init 未调用），贴图 {spritePath} 无法加载");
                return;
            }

            var state = StateOf(img);
            if (onLoadedTint.HasValue) state.Tint = onLoadedTint.Value;

            Game.Res.LoadAsset<Sprite>(spritePath, sp =>
            {
                if (img == null) return;            // 面板可能已关闭销毁
                if (sp == null)
                {
                    // 非预期分支：素材缺失/路径写错 ⇒ 保留纯色占位 + 点名路径（不静默）
                    Log.Warn(Tag, $"原版贴图缺失：{spritePath}（保留纯色底 {img.color}，按占位显示）");
                    return;
                }

                img.sprite = sp;
                img.color = state.Tint;             // ★ 原版亮度（默认白）—— 贴图不再被深色占位底乘暗
                EnsureUnlit(img);
                Log.Info(Tag, $"[原版贴图] {spritePath} → {img.name}，色调={state.Tint}（白=原版亮度）");
            });
        }

        /// <summary>
        /// 造一个**原版底图**按钮：`UIFactory.CreateButton` + 项目配色/字号 +
        /// 原版帧底图（常态/悬停/按下，见 <see cref="ApplyButtonFrame"/>）。
        /// </summary>
        public static Image Button(Transform parent, string name, string label, Vector2 size, Vector2 pos,
            Action onClick)
        {
            var img = UIFactory.CreateButton(name, parent, label, size, pos, ButtonBg, onClick);
            EnsureUnlit(img);

            var text = img.transform.Find("Label")?.GetComponent<Text>();
            if (text == null)
            {
                Log.Warn(Tag, $"按钮 {name} 找不到 Label 子节点，文字样式未应用");
                return img;
            }

            text.color = ButtonText;
            text.fontSize = 20;

            // ★ 片 3：按钮文字（面板里是中文：复活 / 交易·修理 / 结束对话 …）也走原版字模。
            //   引擎 `UIFactory.CreateButton` 造出来的那个 Text 保留为**数据持有者**（font=null、enabled=false）。
            D2TextMirror.Attach(text, D2Text.FontFor(20), null);

            // ★ agent-18 §B：主路径 = 底图接**原版 DC6 直接解出的单帧 PNG**（常态/按下/高亮），
            //   见 `ApplyOriginalButtonArt` 的注释（为什么换数据源）。
            // 兜底路径 = 老的「多帧条带 + SpriteSwap」：**只在单帧原版图缺失时**才用它
            //   （例如素材目录还没同步到新文件），这样按钮不会退化成纯色块。
            //
            // ★ 片 4b 修（本轮 4 条 Error 里的 3 条的根因）：多帧条带那条老路径改成**按需触发**。
            //   原来这里**无条件**先 `RequestFrames(strip, …)`，而该路径在本工程**必然取不到图**
            //   （本文件头 §C-④ 的实测：逐帧按名 `LoadAsset<Sprite>("D2/UI/Menu/button_wide_0")`
            //   = null —— 本套导入设置下**子 sprite 按名加载失效**），于是每建一个按钮就产生 3 条
            //   `[Error] [Resource] 加载失败：D2/UI/Menu/button_wide_{0,1,2}`
            //   （进 Play 的 4 条 Error 里这 3 条就是它们；实测时间戳 16:21:48.400/401 同一帧）。
            //   现在**只有**「单帧原版图整组缺失」才会走到它（`onMissing`）⇒ 正常工程一次都不发起。
            //   ⛔ 这不是"把 Error 降级 / 加静默兜底"：兜底路径**原地保留**（真缺失时照样
            //      RequestFrames + 逐帧 Warn + 条带兜底），只是不再**预先**跑一条已知取不到图的路径。
            ApplyOriginalButtonArt(img, size, () =>
            {
                var strip = ButtonStripFor(size);
                var frames = RequestFrames(strip, ButtonStripFrames(strip));
                ApplyButtonFrame(img, frames);
            });
            return img;
        }

        // ── ★ agent-18 §B：原版按钮底图（DC6 单帧 PNG）────────────────────────
        //  ⚠️ 与 `RequestFrames`/`MenuButtonWide` 的区别（**不是重复造轮子，是换了数据源**）：
        //    旧路径把整条 `button_wide.png`（816×35）切成 3 帧用，帧矩形是"按 RGB 竖缝反推"的；
        //    本轮从 `d2data.mpq` 解 `FrontEnd/WideButtonBlank.dc6` 得到**权威帧**：
        //      4 帧 = 256×35 + 16×35 + 256×35 + 16×35 ⇒ 按钮 = 帧 0+1（常态）/ 帧 2+3（按下），
        //    于是直接落成两张整幅 PNG（272×35），**帧矩形不需要猜、路径唯一、单帧加载稳定**
        //    （多帧按名取帧在本工程会静默取不到，见本文件头 §C-④ 的实测）。
        //  中等按钮同理：`MediumButtonBlank.dc6` 128×35 ×2（常态/按下）+
        //    `MediumSelButtonBlank.dc6` 128×35 ×2（高亮态常态/按下）⇒ 悬停用高亮帧。
        //  路径全部来自 `Core/ResPaths.cs`（唯一来源），实测尺寸见各常量注释。

        /// <summary>按按钮**原版 px 尺寸**挑底图（≥200 = 宽按钮，否则中等）；纯函数 ⇒ 离线可断言。</summary>
        public static void ButtonSpritesFor(Vector2 origSize, out string normal, out string pressed, out string highlight)
        {
            if (origSize.x >= WideButtonMinWidth)
            {
                normal = ResPaths.BtnWideNormal;
                pressed = ResPaths.BtnWidePressed;
                highlight = ResPaths.BtnWideNormal;     // 原版宽按钮只有常态/按下两态
            }
            else
            {
                normal = ResPaths.BtnMedNormal;
                pressed = ResPaths.BtnMedPressed;
                highlight = ResPaths.BtnMedSel;         // 原版中等按钮另有"高亮"版 ⇒ 悬停用它
            }
        }

        /// <summary>
        /// 原版按钮底图的三态就位后套到按钮上（`SpriteSwap`）。
        /// </summary>
        /// <param name="onMissing">
        /// **单帧原版图缺失时的兜底**（调用方传"多帧条带"那条老路径）。缺图不静默：会先 Warn 再调它。
        /// </param>
        private static void ApplyOriginalButtonArt(Image img, Vector2 origSize, Action onMissing)
        {
            if (img == null) return;

            string normalPath, pressedPath, highlightPath;
            ButtonSpritesFor(origSize, out normalPath, out pressedPath, out highlightPath);

            var state = new ButtonArtState { Target = img, OnMissing = onMissing };
            var btn = img.GetComponent<Button>();
            state.Button = btn;

            Load1(state, normalPath, 0);
            Load1(state, pressedPath, 1);
            Load1(state, highlightPath, 2);
        }

        /// <summary>一张按钮底图的加载状态（三态都回来后才套 `SpriteSwap`，避免半套）。</summary>
        private sealed class ButtonArtState
        {
            public Image Target;
            public Button Button;
            public readonly Sprite[] Sprites = new Sprite[3];
            public int Pending = 3;

            /// <summary>单帧原版图整体缺失时的兜底（= 老的"多帧条带"路径）。</summary>
            public Action OnMissing;
        }

        private static void Load1(ButtonArtState st, string path, int slot)
        {
            if (Game.Res == null)
            {
                Log.WarnOnce(Tag, "btnart.res.null",
                    $"Game.Res 未初始化 ⇒ 原版按钮底图 {path} 取不到，按钮退回纯色块（见 UiArt.Button）");
                if (--st.Pending <= 0) Apply(st);
                return;
            }

            Game.Res.LoadAsset<Sprite>(path, sp =>
            {
                if (sp != null) st.Sprites[slot] = sp;
                else
                {
                    // 非预期分支：素材缺失 ⇒ 点名路径（不静默）
                    Log.Warn(Tag, $"原版按钮底图缺失：{path}（按钮会退回纯色块 {ButtonBg}）");
                }
                if (--st.Pending <= 0) Apply(st);
            });
        }

        private static void Apply(ButtonArtState st)
        {
            var img = st.Target;
            if (img == null) return;                    // 面板已关闭销毁

            var normal = st.Sprites[0];
            if (normal == null)
            {
                // 整组缺失（单帧原版图还没同步到工程）⇒ 退回"多帧条带"兜底；条带也缺才保持纯色块。
                // 两条路径都在 Load1/RequestFrames 里 Warn 过，这里只点名"走了哪条兜底"。
                Log.Warn(Tag, $"原版按钮单帧底图整组缺失 ⇒ 退回多帧条带兜底（{ResPaths.MenuButtonWide} / "
                              + $"{ResPaths.MenuButtonMedium}）；条带也缺则按钮保持纯色块 {ButtonBg}");
                st.OnMissing?.Invoke();
                return;
            }

            img.sprite = normal;
            // ★ 原版帧的宽高比是固定的（宽 272×35 / 中 128×35）：`preserveAspect` 让**矩形尺寸
            //   与帧比例不一致时按比例内缩**，而不是把原版按钮拉长/压扁（不改原版像素）。
            img.preserveAspect = true;
            img.color = StateOf(img).Tint;              // 原版亮度（默认白）
            EnsureUnlit(img);

            var btn = st.Button != null ? st.Button : img.GetComponent<Button>();
            if (btn == null) return;

            btn.transition = Selectable.Transition.SpriteSwap;
            btn.spriteState = new SpriteState
            {
                highlightedSprite = st.Sprites[2] != null ? st.Sprites[2] : normal,
                pressedSprite = st.Sprites[1] != null ? st.Sprites[1] : normal,
                selectedSprite = st.Sprites[2] != null ? st.Sprites[2] : normal,
                disabledSprite = normal,
            };
            Log.Info(Tag, $"[原版按钮底图] {ResPaths.Root}/{img.name} ← 三态就位（常态/按下/高亮）");
        }

        /// <summary>按按钮尺寸挑原版底图条带（纯函数 ⇒ 离线宿主可直接断言）。</summary>
        public static string ButtonStripFor(Vector2 size)
            => size.x >= WideButtonMinWidth ? ResPaths.MenuButtonWide : ResPaths.MenuButtonMedium;

        /// <summary>条带的帧数（与 `ResPaths.FrameCount*`、`AssetImporter.MultiFrameStrips` 同口径）。</summary>
        public static int ButtonStripFrames(string stripPath)
            => stripPath == ResPaths.MenuButtonWide
                ? ResPaths.FrameCountMenuButtonWide
                : ResPaths.FrameCountMenuButtonMedium;

        /// <summary>取按钮上的 Text（用于置灰/改文案）。</summary>
        public static Text ButtonLabel(Image button)
        {
            if (button == null) return null;
            return button.transform.Find("Label")?.GetComponent<Text>();
        }

        /// <summary>设置按钮可用性（uGUI 的 interactable + 文字灰化）。</summary>
        public static void SetInteractable(Image button, bool on)
        {
            if (button == null) return;
            var btn = button.GetComponent<Button>();
            if (btn != null) btn.interactable = on;

            var text = ButtonLabel(button);
            if (text != null) text.color = on ? ButtonText : ButtonTextDisabled;
        }

        /// <summary>
        /// 原版「买卖屏方钮」的**单帧文件**前缀：`D2/UI/Panel/buysellbtn_{帧号}`（帧 0 = 常态 / 帧 1 = 按下）。
        /// <para>为什么不用 `ResPaths.PanelBuySellButton`（那是整张 1024×64 条带的路径）：
        /// **实测**（Play 2026-09-17，本轮读图 + 日志）`Game.Res.LoadAsset&lt;Sprite&gt;` 取
        /// `D2/UI/Panel/buysellbtn.DC6.0_0` 直接
        /// `[Error] [Resource] 加载失败：D2/UI/Panel/buysellbtn.DC6.0_0`（本工程这套导入设置下
        /// **子 sprite 按名加载失效**，同 <see cref="TryBulkLoad"/> 注释里那条 `button_wide_0` 的实测）。
        /// 而这批素材**同时**存在单帧文件 `buysellbtn_0.png`（Sprite/Single/Point，与能正常加载的
        /// `buyselltabs_0.png` 导入参数逐项相同）⇒ 方钮走单帧文件，不做条带按名取帧。</para>
        /// </summary>
        public const string BuySellButtonFramePrefix = ResPaths.D2UiPanel + "buysellbtn_";

        // ★ agent-23：原版**方形按钮**（`PANEL/buysellbtn.DC6`，32×32 × 9 钮 × 常态/按下）
        //   底图由调用方贴（见 `BuySellButtonFramePrefix`），这里只保证"定尺 + 命中区 + 文字"。
        //   为什么单独一个（不直接用 `Button`）：`Button` 会按**原版宽/中按钮**（272×35 / 128×35）
        //   贴底图，而那两套是**前端菜单**的按钮，跟买卖屏右下那 4 个雕槽（底图实测 34×27）不是一回事。
        public static Image SquareButton(Transform p, string n, string lb, Vector2 sz, Vector2 ps, Action cb)
        {
            var img = UIFactory.CreateButton(n, p, lb, sz, ps, ButtonBg, cb);
            EnsureUnlit(img);

            // ★ 片 3：方钮文字（买卖屏的「修理 / 关闭」是中文）同样走原版字模
            var text = img.transform.Find("Label")?.GetComponent<Text>();
            if (text == null)
            {
                Log.Warn(Tag, $"方钮 {n} 找不到 Label 子节点 ⇒ 文字仍由引擎默认字体绘制（应排查 UIFactory）");
            }
            else
            {
                text.color = ButtonText;
                text.fontSize = 20;
                D2TextMirror.Attach(text, D2Text.FontFor(20), null);
            }

            UiLog.Info("SquareButton 已建（底图由调用方贴）");
            return img;
        }

        /// <summary>
        /// 造一个**底图由调用方指定前缀**的原版按钮（片 5 新增）。
        /// <para>
        /// 与 <see cref="Button"/> 的唯一区别：`Button` 的底图按**原版宽/中按钮**（272×35 / 128×35，
        /// = 前端菜单按钮）自动挑，而原版**有些屏上的按钮是另一套专用帧** ——
        /// 例：死亡屏的 `MENU/endgameok.dc6`（**96×32 ×2 帧**：常态 / 按下，实测）。
        /// 强行套 272×35 会把原版按钮拉变形 ⇒ 这里让调用方给**路径前缀**（**不带结尾下划线**，
        /// 例 `ResPaths.MenuEndGameOK` ⇒ `D2/UI/Menu/endgameok`），本方法内部走
        /// <see cref="ResPaths.Frame"/>（它会自己拼 `_i`）取 `{prefix}_0` / `{prefix}_1` 做 SpriteSwap。
        /// ⚠️ 传 `"…endgameok_"` 会拼出 `…endgameok__0`（双下划线）⇒ 取不到图，只剩纯色块。
        /// </para>
        /// <para>缺图**不静默**：逐帧 Warn 点名路径，按钮保留纯色底 <see cref="ButtonBg"/>。</para>
        /// </summary>
        public static Image OrigButton(Transform parent, string name, string label, string framePrefix,
            Vector2 size, Vector2 pos, Action onClick, int frameCount)
        {
            var img = UIFactory.CreateButton(name, parent, label, size, pos, ButtonBg, onClick);
            EnsureUnlit(img);

            var text = img.transform.Find("Label")?.GetComponent<Text>();
            if (text == null)
            {
                Log.Warn(Tag, $"按钮 {name} 找不到 Label 子节点，文字样式未应用");
            }
            else
            {
                text.color = ButtonText;
                text.fontSize = 20;
                D2TextMirror.Attach(text, D2Text.FontFor(20), null);
            }

            if (frameCount <= 0)
            {
                Log.Warn(Tag, $"OrigButton「{name}」的 frameCount={frameCount} 非法 ⇒ 只用纯色底（前缀 {framePrefix}）");
                return img;
            }

            var normal = ResPaths.Frame(framePrefix, 0);
            var pressed = frameCount > 1 ? ResPaths.Frame(framePrefix, 1) : normal;
            var st = new OrigButtonState { Target = img, Button = img.GetComponent<Button>() };
            LoadOrig(st, normal, 0);
            LoadOrig(st, pressed, 1);
            Log.Info(Tag, $"[原版按钮底图] {img.name} ← 前缀 {framePrefix}（{frameCount} 帧：常态/按下）");
            return img;
        }

        /// <summary>专用按钮底图的加载状态（两帧都回来后套 SpriteSwap，避免半套）。</summary>
        private sealed class OrigButtonState
        {
            public Image Target;
            public Button Button;
            public readonly Sprite[] Sprites = new Sprite[2];
            public int Pending = 2;
        }

        private static void LoadOrig(OrigButtonState st, string path, int slot)
        {
            if (Game.Res == null)
            {
                Log.WarnOnce(Tag, "origbtn.res.null",
                    $"Game.Res 未初始化 ⇒ 原版专用按钮底图 {path} 取不到（按钮退回纯色块 {ButtonBg}）");
                st.Pending--;
                return;
            }

            Game.Res.LoadAsset<Sprite>(path, sp =>
            {
                if (sp != null) st.Sprites[slot] = sp;
                else Log.Warn(Tag, $"原版专用按钮底图缺失：{path}（按钮保留纯色底 {ButtonBg}）");

                if (--st.Pending > 0) return;

                var img = st.Target;
                if (img == null) return;                 // 面板已关闭销毁
                var normal = st.Sprites[0];
                if (normal == null)
                {
                    Log.Warn(Tag, $"原版专用按钮底图整组缺失 ⇒ {img.name} 停在纯色块 {ButtonBg}");
                    return;
                }

                img.sprite = normal;
                // 原版帧宽高比固定（96×32）⇒ 矩形与帧比例不一致时按比例内缩，不拉变形
                img.preserveAspect = true;
                img.color = StateOf(img).Tint;
                EnsureUnlit(img);

                var btn = st.Button != null ? st.Button : img.GetComponent<Button>();
                if (btn == null) return;
                btn.transition = Selectable.Transition.SpriteSwap;
                btn.spriteState = new SpriteState
                {
                    highlightedSprite = normal,
                    pressedSprite = st.Sprites[1] != null ? st.Sprites[1] : normal,
                    selectedSprite = normal,
                    disabledSprite = normal,
                };
            });
        }

        /// <summary>
        /// 造一个定尺文本（居中定位于父层中心偏移 pos）。
        /// <para>★ 片 3：**画面由原版字模画**（`D2TextMirror` + `D2Label`）。
        /// 返回的 `Text` 是**数据持有者**（`font = null` + `enabled = false`，不产生任何绘制），
        /// 面板照旧用 `_x.text / _x.color / _x.horizontalOverflow / _x.resizeTextForBestFit` 写它，
        /// 由镜像逐帧同步到字模（改动面最小、且**布局坐标一个都不动**）。</para>
        /// </summary>
        public static Text Label(Transform parent, string name, string content, int fontSize, TextAnchor anchor,
            Color color, Vector2 size, Vector2 pos)
        {
            var rt = UIFactory.CreateCentered(name, parent, size, pos);
            var text = rt.gameObject.AddComponent<Text>();
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.text = content ?? string.Empty;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            D2TextMirror.Attach(text, D2Text.FontFor(fontSize), null);
            return text;
        }

        /// <summary>
        /// 造一个输入框（引擎 `UIFactory` 无输入框构件，按 uGUI 的标准层级代码搭：
        /// 底图 + Text + Placeholder）。返回挂好引用与监听无关的 `InputField`。
        /// </summary>
        public static InputField Input(Transform parent, string name, Vector2 size, Vector2 pos,
            string placeholder, string initial, int charLimit)
        {
            var bg = Panel(parent, name, size, pos, InputBg, true);

            // ⚠️ 片 3 的**唯一例外**（已在 `策划/验收表.md` 的「允许的差异」登记）：
            //   InputField 的 `textComponent` 必须是**真实、启用**的 uGUI `Text`（光标/选区/换行
            //   全按它的 TextGenerator 算），没法换成字模方块 ⇒ 这里保留 `UIFactory.DefaultFont()`。
            //   它渲染的是**玩家键入的角色名（ASCII 字母）**，不是中文 ⇒ 「中文 0 处走默认字体」仍成立。
            var textRt = UIFactory.CreateNode("Text", bg.transform);
            var text = textRt.gameObject.AddComponent<Text>();
            text.font = UIFactory.DefaultFont();
            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = TextColor;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            textRt.offsetMin = new Vector2(10f, 2f);
            textRt.offsetMax = new Vector2(-10f, -2f);

            // ★ 片 3：占位符是**中文**（例「输入角色名（最多 15 字）」）⇒ 必须走原版字模。
            //   这个 Text 只作数据持有者（镜像会把 font 清成 null、enabled 置 false），
            //   显隐由镜像按 `InputField` 的聚焦/内容状态驱动（与 uGUI `UpdatePlaceholder` 同判据）。
            var phRt = UIFactory.CreateNode("Placeholder", bg.transform);
            var ph = phRt.gameObject.AddComponent<Text>();
            ph.fontSize = 20;
            ph.alignment = TextAnchor.MiddleLeft;
            ph.color = new Color(0.55f, 0.53f, 0.50f, 1f);
            ph.text = placeholder ?? string.Empty;
            ph.supportRichText = false;
            ph.horizontalOverflow = HorizontalWrapMode.Overflow;
            phRt.offsetMin = new Vector2(10f, 2f);
            phRt.offsetMax = new Vector2(-10f, -2f);

            var input = bg.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = text;
            input.placeholder = ph;
            D2TextMirror.Attach(ph, D2Text.FontFor(ph.fontSize), input);
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = charLimit > 0 ? charLimit : 0;
            input.text = initial ?? string.Empty;
            return input;
        }

        /// <summary>
        /// 造一条**锚点宽度驱动**的进度条（`constraints.md` #3：不用 `fillAmount` + 空 sprite）。
        /// 轨道是父底图，填充块用 `anchorMax.x = ratio` 表达进度。
        /// </summary>
        /// <returns>填充块 Image；调用方用 <see cref="SetBarRatio"/> 刷新。</returns>
        public static Image ProgressBar(Transform parent, string name, Vector2 size, Vector2 pos,
            Color trackColor, Color fillColor)
        {
            var track = Panel(parent, name, size, pos, trackColor, false);
            var fill = FullPanel(track.transform, "Fill", fillColor, false);
            SetBarRatio(fill, 0f);
            return fill;
        }

        /// <summary>按比例刷新锚点宽度进度条（0~1）。</summary>
        public static void SetBarRatio(Image fill, float ratio)
        {
            if (fill == null) return;
            var r = Mathf.Clamp01(ratio);
            var rt = fill.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(r, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // ── 不受 2D 光照影响的材质（§C-①）────────────────────────────────────

        /// <summary>
        /// 判断某 shader 名是否**会让 UI 图受 2D 光照影响**（⇒ 必须换掉）。
        /// <para>
        /// 纯函数（离线宿主可断言）：含 `unlit`（`Sprite-Unlit-Default` / `Unlit/Color`）⇒ **不受光**；
        /// 否则出现 `lit`（`Sprite-Lit-Default` / `Universal Render Pipeline/2D/Sprite-Lit-Default`）⇒ 受光。
        /// `UI/Default` 与 `Sprites/Default` 都是 unlit ⇒ 返回 false。
        /// </para>
        /// </summary>
        public static bool IsLitShader(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) return false;
            var n = shaderName.ToLowerInvariant();
            if (n.Contains("unlit")) return false;   // 必须先判 unlit：`unlit` 里也含 `lit`
            return n.Contains("lit");
        }

        /// <summary>
        /// 确保这个 Image 走**不受 2D 光照影响**的材质（引擎 Canvas 是
        /// `RenderMode.ScreenSpaceOverlay`，`Runtime/Presentation/UI.cs:49` ⇒ 默认材质 =
        /// Canvas 的 `UI/Default`，unlit）。本函数是**防御性校验**：只有发现 Image 被挂了
        /// 受光材质才动手（换回 Canvas 默认材质），并留下 Warn —— 不静默。
        /// </summary>
        public static void EnsureUnlit(Image img)
        {
            if (img == null) return;

            var mat = img.material;
            if (mat == null) return;                 // 最常见路径：走 Canvas 默认材质（unlit），无需处理

            if (!IsLitShader(mat.shader != null ? mat.shader.name : null)) return;

            // 非预期分支：受光材质会随 2D 光照把 UI 压暗（且不报错）⇒ 换回 UI 默认材质 + 点名
            Log.Warn(Tag, $"Image {img.name} 的材质 {mat.shader.name} 受 2D 光照影响（会压暗 UI）" +
                          " ⇒ 已改回 Canvas 默认 UI 材质（unlit）");
            img.material = null;
        }

        // ── 原版按钮底图：多帧条带的取帧与补图（§C-④）────────────────────────

        /// <summary>一条多帧底图条带的加载状态（帧 sprite + 等待补图的按钮 + 未回帧数）。</summary>
        private sealed class FrameSet
        {
            /// <summary>条带路径（`ResPaths.MenuButtonWide` 这类，不带帧号）。</summary>
            public string Strip;

            /// <summary>条带**文件名**（`button_wide`）——子 sprite 名 = `{StripName}_{帧号}`。</summary>
            public string StripName;

            public Sprite[] Frames;
            public int Pending;
            public bool Ready;
            public bool BulkTried;
            public readonly List<Image> Waiters = new List<Image>();

            /// <summary>「条带就绪后回调」的等待者（`RequestStrip` 登记；与 <see cref="Waiters"/> 一样在结算时清空）。</summary>
            public readonly List<Action<Sprite[]>> Callbacks = new List<Action<Sprite[]>>();
        }

        /// <summary>条带路径 → 加载状态（进程内共享，5 个按钮只取一次图）。</summary>
        private static readonly Dictionary<string, FrameSet> FrameSets = new Dictionary<string, FrameSet>();

        /// <summary>登记/取一条多帧条带（首次调用触发异步取帧；重复调用直接复用）。</summary>
        private static FrameSet RequestFrames(string stripPath, int frameCount)
        {
            if (FrameSets.TryGetValue(stripPath, out var cached)) return cached;

            var set = new FrameSet
            {
                Strip = stripPath,
                StripName = stripPath.Substring(stripPath.LastIndexOf('/') + 1),
                Frames = new Sprite[frameCount],
                Pending = frameCount,
            };
            FrameSets[stripPath] = set;

            if (Game.Res == null)
            {
                Log.WarnOnce(Tag, "frames.res.null",
                    $"Game.Res 未初始化 ⇒ 原版按钮底图 {stripPath} 取不到，按钮退回纯色块（见 UiArt.Button）");
                set.Pending = 0;
                set.Ready = true;
                return set;
            }

            // ★ 片 8 B33（修掉"每次进 Stage 必现的 2 条 Error"）：
            //   原先**无条件先跑**「逐帧按名向引擎资源模块要每一帧」（见下面兜底段那三行），
            //   而这条路在本工程**必然取不到图**（本文件头 §C-④ 的实测：这套导入设置下子 sprite 按名加载失效），
            //   引擎资源模块对**每一次**失败都 `Log.Error("[Resource] 加载失败：…")`
            //   ⇒ 每建一次 HUD（= 每次进 Stage）白刷 2 条 Error（实测 `D2/UI/Panel/overlap_{0,1}`）。
            //   ⇒ 改成**先走真正能取到图的那条路**（整条 `LoadAll`），它失败才回退逐帧按名。
            //   ⛔ 这不是"把 Error 降级 / 加静默兜底"：逐帧路径**原地保留**（真取不到时照样走它、
            //      那时的 Error 是真失败、有意义），只是不再**预先**跑一条已知取不到图的路径。
            if (TryBulkLoad(set))
            {
                set.BulkTried = true;
                set.Pending = 0;
                CompleteFrames(set);
                return set;
            }

            // 兜底：逐帧按名（整条 LoadAll 也失败时才走）。
            // ⚠️ `Game.Res.LoadAsset` 命中缓存时回调可能**同步**触发 ⇒ 先把 Pending 设成帧总数，
            //    再逐帧发起；最后一个回调（无论同步还是下一帧）才是"全部就绪"，不会提前结算。
            for (var i = 0; i < frameCount; i++)
            {
                var index = i;
                var path = ResPaths.Frame(stripPath, index);
                Game.Res.LoadAsset<Sprite>(path, sp => OnFrameLoaded(set, index, sp));
            }
            return set;
        }

        private static void OnFrameLoaded(FrameSet set, int index, Sprite sp)
        {
            if (sp != null) set.Frames[index] = sp;      // 兜底已填过的槽不被后来的 null 覆盖

            set.Pending--;
            if (set.Pending > 0) return;                 // 三帧全部有结果后才结算（只结算一次）

            CompleteFrames(set);
        }

        /// <summary>
        /// 条带**结算**（只结算一次）：逐帧按名回齐 / 整条 `LoadAll` 直接成功 / 两者都失败 —— 三条路都收敛到这里。
        /// 缺帧时再兜一次整条 `LoadAll`（失败则 Warn，不静默）。
        /// </summary>
        private static void CompleteFrames(FrameSet set)
        {
            var missing = false;
            for (var i = 0; i < set.Frames.Length; i++)
                if (set.Frames[i] == null) missing = true;

            if (missing && !set.BulkTried)
            {
                set.BulkTried = true;
                if (TryBulkLoad(set))
                {
                    // 实测（Play 2026-09-17）：本工程 `Resources.Load<Sprite>("…/button_wide_0")`
                    // **取不到**（逐帧按名加载失效），整条 `LoadAll` 才能取到 —— 与
                    // `UI/D2Text.cs` 的字模加载是同一个坑、同一套兜底。
                    Log.Info(Tag, $"[原版按钮底图] {set.Strip}：逐帧按名加载取不到 ⇒ 已用整条 LoadAll 兜底");
                }
                else
                {
                    Log.Warn(Tag, $"原版按钮底图取不到：{set.Strip}（逐帧按名 + 整条 LoadAll 都失败）" +
                                  " ⇒ 按钮保持纯色块（见 UI/UiArt.cs 的取帧注释）");
                }
            }

            set.Ready = true;
            var ok = 0;
            for (var i = 0; i < set.Frames.Length; i++)
                if (set.Frames[i] != null) ok++;

            Log.Info(Tag, $"[原版按钮底图] {set.Strip} {ok}/{set.Frames.Length} 帧就位，补图 {set.Waiters.Count} 个按钮");

            // 快照后再应用：补图过程中可能有按钮被销毁（面板关闭）
            var waiters = set.Waiters.ToArray();
            set.Waiters.Clear();
            for (var i = 0; i < waiters.Length; i++) ApplyButtonFrame(waiters[i], set);

            var callbacks = set.Callbacks.ToArray();
            set.Callbacks.Clear();
            for (var i = 0; i < callbacks.Length; i++) callbacks[i]?.Invoke(set.Frames);
        }

        /// <summary>
        /// 取一条**多帧条带**的全部帧（逐帧按名 + 「整条 LoadAll」兜底，与按钮底图同一套）；
        /// 就绪后回调（帧序 = 条带内顺序，缺帧为 null）。
        /// <para>
        /// 为什么必须走这里而不是 `Game.Res.LoadAsset&lt;Sprite&gt;(ResPaths.Frame(...))`：
        /// **实测**（Play 2026-09-17，见本文件头 §C-④ 注释）本工程这套导入设置下
        /// 逐帧按名（`Game.Res.LoadAsset&lt;Sprite&gt;("…/goldcoinbtn.dc6.0_0")`）返回 **null**，
        /// 只有整条取（`Game.Res.LoadAll&lt;Sprite&gt;(条带)`，片 34 起走引擎资源模块）才取得到
        /// ⇒ 所有条带取帧都收敛到本函数。
        /// </para>
        /// </summary>
        public static void RequestStrip(string stripPath, int frameCount, Action<Sprite[]> onReady)
        {
            if (string.IsNullOrEmpty(stripPath) || frameCount <= 0)
            {
                onReady?.Invoke(Array.Empty<Sprite>());
                return;
            }

            var set = RequestFrames(stripPath, frameCount);
            if (set.Ready)
            {
                onReady?.Invoke(set.Frames);       // 已在缓存里 ⇒ 同步回调（调用方要能接受同步）
                return;
            }
            set.Callbacks.Add(onReady);
        }

        /// <summary>
        /// 兜底：把整条多帧贴图一次读出来，按「子 sprite 名 = `{条带名}_{帧号}`」装进 <see cref="FrameSet"/>。
        /// <para>
        /// **为什么需要它（实测，不是保险起见）**：Play 里逐帧按名
        /// （`Game.Res.LoadAsset&lt;Sprite&gt;("D2/UI/Menu/button_wide_0")`）返回 **null**，
        /// 而整条取（`Game.Res.LoadAll&lt;Sprite&gt;("D2/UI/Menu/button_wide")`）拿到
        /// 3 个名字正确（`button_wide_0/1/2`）的子 sprite ⇒ 本工程这套导入设置下**逐帧按名加载失效**。
        /// 同一个坑 `UI/D2Text.cs` 的字模加载已经踩过并同样用 `LoadAll` 兜底（该文件 §兜底 注释有出处）。
        /// </para>
        /// <para>取图走**引擎资源模块**（`Game.Res.LoadAll&lt;Sprite&gt;`，片 34 起）：整条取的能力已下沉到
        /// `IResourceManager`（`Contracts.cs` 的 `Exists` / `LoadAll`），⛔ 不再直接调 Unity 的 `Resources`
        /// —— 那会让资源根前缀 / 缓存 / 卸载策略 / 热更后端全部失效（收尾前这里是验收表 **E1** 登记的唯一例外）。</para>
        /// <para>⚠️ 路径口径：引擎会自己拼 `CloverRes.Init("Clover")` 的根前缀 ⇒ 传**相对路径**
        /// （`D2/UI/Menu/button_wide`），⛔ 不要再自己加 `ResPaths.Root + "/"`（那是 Unity `Resources` 的拼法）。</para>
        /// </summary>
        private static bool TryBulkLoad(FrameSet set)
        {
            var atlas = set.Strip;                       // 引擎相对路径（根前缀由后端拼）
            Sprite[] all;
            try
            {
                all = Game.Res.LoadAll<Sprite>(atlas);
            }
            catch (Exception ex)
            {
                Log.Warn(Tag, $"原版按钮底图 LoadAll 兜底异常（{atlas}）：{ex.GetType().Name}: {ex.Message}");
                return false;
            }

            if (all == null || all.Length == 0) return false;

            var prefix = set.StripName + "_";
            var taken = 0;
            for (var i = 0; i < all.Length; i++)
            {
                var sp = all[i];
                if (sp == null || string.IsNullOrEmpty(sp.name)) continue;
                if (!sp.name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (!int.TryParse(sp.name.Substring(prefix.Length), out var frame)) continue;
                if (frame < 0 || frame >= set.Frames.Length) continue;

                set.Frames[frame] = sp;
                taken++;
            }
            return taken > 0;
        }

        /// <summary>
        /// 把原版帧底图套到按钮上（帧未就绪 ⇒ 登记为等待者，就绪后自动补上）。
        /// 帧序（`AssetImporter.MultiFrameStrips` 实测帧矩形 + 本任务书 §C-④ 口径）：
        /// **0 = 常态 / 1 = 悬停 / 2 = 按下**；禁用态沿用常态帧（只灰化文字，见 <see cref="SetInteractable"/>）。
        /// </summary>
        private static void ApplyButtonFrame(Image img, FrameSet set)
        {
            if (img == null) return;                 // 面板已关闭销毁

            if (!set.Ready)
            {
                set.Waiters.Add(img);
                return;
            }

            var normal = set.Frames[0];
            if (normal == null) return;              // 整条缺失：保持纯色块（OnFrameLoaded 已逐帧 Warn）

            var btn = img.GetComponent<Button>();
            if (btn == null)
            {
                Log.Warn(Tag, $"按钮 {img.name} 没有 Button 组件，原版底图只套了常态帧（悬停/按下无变化）");
                img.sprite = normal;
                img.color = StateOf(img).Tint;
                EnsureUnlit(img);
                return;
            }

            img.sprite = normal;
            img.color = StateOf(img).Tint;           // 原版亮度（或调用方记下的色调）
            btn.transition = Selectable.Transition.SpriteSwap;
            btn.spriteState = new SpriteState
            {
                highlightedSprite = set.Frames[1],
                pressedSprite = set.Frames[2],
                selectedSprite = set.Frames[1],
                disabledSprite = normal,
            };
            EnsureUnlit(img);
        }
    }
}
