// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/UiArt.cs
// 流程面板（Boot/MainMenu/Settings/CharSelect/CharCreate/Loading/Pause）共用的
// **代码搭 UI 小工具**：铺满根节点 / 原版贴图背景 / 原版底图按钮 / 文本 / 输入框。
//
// 为什么需要它（而不是每个面板各写一份）：
//   7 个面板都要「铺满父层 + 加载原版贴图 + 造按钮」，各写一份必然出现口径分叉；
//   本文件只封装**引擎 `UIFactory` 之上的项目约定**（配色 / 字号 / 贴图加载兜底 / 原版亮度），
//   锚点/铺满一律转发 `UIFactory`，**不自己再写一套锚点工具**。
//   本文件在 UI 层，只允许引用 `CloverEngine` / `Diablo2.Core` / `Diablo2.Def`，
//      **不得引用 `Diablo2.Module` 下的任何类型**（分层自检 ③）。
//
//     实测证据（`client/Assets/Screenshots/p02_mainmenu.png`）：同一帧、同一 Canvas 里
//     **按钮文字是亮的**，只有背景贴图是暗的 ⇒ 与「2D 光照」无关，就是逐 Image 的 color 乘数。
//     （纯色占位仍然可见，不静默变黑）。
//
//     引擎 `UIManager` 建的是 `RenderMode.ScreenSpaceOverlay` 的 Canvas
//     （`Runtime/Presentation/UI.cs:49`），UI Image 走 Canvas 默认材质 `UI/Default`（unlit）⇒
//     **本来就不受光照影响**。但只要有谁给 Image 挂了受光材质，就会静默变暗 ⇒
//     `EnsureUnlit()` 在**每一个**本工具造出来的 Image 上做一次显式校验，
//     发现受光材质就换回 UI 默认材质并告警（`IsLitShader` 是纯函数，离线宿主可断言）。
//
//     取帧前的**帧数/帧名/条带导入模式**已由 `tools/probes/hosts/uicheck` 逐条对磁盘核对（`.png.meta`）。
//       逐帧按名 `LoadAsset<Sprite>("D2/UI/Menu/button_wide_0")` = **null**（本工程这套导入设置下失效），
//       而整条 `Game.Res.LoadAll<Sprite>("D2/UI/Menu/button_wide")` = 3 个名字正确的子 sprite。
//       ⇒ 逐帧按名为主、**整条 LoadAll 兜底**（`SpriteStripLoader` 的主路），与 `UI/D2Text.cs` 的字模加载同一套做法。
//
//   ① 异步贴图 + 请求序号守卫 + 占位保留 + 同路径去重 + unlit 材质校验 → 引擎件 `UiImageLoader`
//      （`SetSprite` / `SetArtTint` / `EnsureUnlit` / `IsLitShader` 四个方法体转调）；
//   ② sprite-swap 四态落地（`transition= SpriteSwap` + `SpriteState` 填充 + 缺态回落常态 + 色调 +
//      `preserveAspect` + unlit）→ 引擎件 `SpriteSwapButton.Apply`（`Apply` / `ApplyButtonFrames` / `LoadOrig` 三条落点）；
//   ③ 多帧条带取帧（整条 `LoadAll` 主路 + 逐帧按名兜底 + 就绪回调 + 同路径去重 + 缺帧留痕）→ 引擎件 `SpriteStripLoader`。
//   留在项目侧的三处是**刻意如此**，不是遗漏：
//     · `ButtonSpritesFor` / `ButtonStripFor` / 全部尺寸·配色·字号常量 = **项目素材数据与项目约定**，
//       引擎件按设计不含任何素材路径 / 配色（见 `SpriteSwapButton` 文件头「零项目取值」）；
//     · 按钮骨架仍走 `UIFactory.CreateButton`（也是引擎件），**不**走 `SpriteSwapButton.Create`：后者的
//       "占位皮肤"分支会把「贴图仍在途」按「常态贴图缺失」记一条 WarnOnce（假告警，污染日志）；
//     · `SquareButton` 的底图由调用方后贴（无四态、无 sprite-swap）⇒ 与 `SpriteSwapButton` 契约不同。
// ─────────────────────────────────────────────────────────────────────────────

using System;
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
        // 引擎 `UIManager` 构造时把 CanvasScaler 固定为 ScaleWithScreenSize +
        //    referenceResolution=(1920,1080) + match=0.5（`Runtime/Presentation/UI.cs:52-56`）。
        //    因此**面板坐标单位 = 1920×1080 参考像素**（`GameConst.UiReferenceWidth/Height`
        //    另有一套 1280×720 的策划口径，与引擎实现不同）。
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
        /// <summary>
        /// 按钮底板色 —— **只是异步在途 / 素材缺失时的兜底**（<see cref="SetSprite"/> 成功后
        /// `color` 会被改成 <see cref="ArtFullBright"/>，故它不是"按钮的底图"）。
        /// <para>读代码时最容易误判的一格：只读到这里会判「商店修理/关闭按钮底板是纯色占位」，
        /// 而 `<c>ShopPanel.BuildBottomBar</c>` 在其后三行就调 `ApplyBuySellButtonArt` 贴了原版
        /// `buysellbtn_2/_10` ⇒ 判据 = 每个"自带占位色的按钮工厂"都必须有 art 绑定：
        /// `UiArt.Button` ⇒ <see cref="ApplyButtonFrames"/>；`OrigButton` ⇒ <c>LoadOrig</c>；
        /// `SquareButton`（契约就是"底图由调用方贴"）⇒ 调用点必须紧跟 art 应用，
        /// 门禁 = `tools/probes/hosts/uicheck/ShopArtCheck.cs`（含退化样本必须变红）。</para>
        /// </summary>
        public static readonly Color ButtonBg = new Color(0.15f, 0.13f, 0.11f, 0.94f);

        /// <summary>
        /// **原版亮度**：贴图加载成功后的颜色乘数（白色 = 不做任何压暗 ⇒ 按素材本来的亮度显示）。
        /// </summary>
        public static readonly Color ArtFullBright = Color.white;

        /// <summary>未选中项的原版贴图色调（压暗一档，用于「5 职业」里非当前职业的按钮）。</summary>
        public static readonly Color ArtDim = new Color(0.62f, 0.62f, 0.62f, 1f);

        /// <summary>
        /// 按钮文字色（暗金的暖黄）= **全项目按钮 label 的唯一字色常量**。
        /// <para>**0.91/0.82/0.52 → 0.95/0.87/0.60**
        /// （= 与 <see cref="TitleColor"/> 同值）。为什么改（**量化，不是口味**）：
        /// 本工程用的原版按钮底图是**深板岩灰** —— `Resources/Clover/D2/UI/Menu/btn_med_normal.png`
        /// 内区（x 22..78% / y 25..75%，alpha>200，n=1278）实测平均 sRGB 亮度 **0.376**
        /// （量法：对该内区逐像素取均值），
        /// 按钮字按原版 18px **Bold** 渲染（`m_FontSize:18` / `m_FontStyle:1`），18px < WCAG「大号文本」
        /// 阈值 18.66px ⇒ 按**更严的正文**门槛 4.5:1 取。</para>
        /// <para>被谁用：`UiArt.Button` / `SquareButton` / `OrigButton` 三条按钮工厂（覆盖 NPC 对话 / 商店 /
        /// 死亡屏）与若干正文 label；`UiLayoutFlow.ButtonText` 现在**派生自本常量**（FlowButton 默认字色）
        /// ⇒ 全项目按钮字色只有这一个真源。不许在调用点另造字色
        /// （门禁 = `uicheck` 的「按钮 label 对比度 ≥ 4.5:1（逐屏列数）+ 判据自检」）。</para>
        /// </summary>
        public static readonly Color ButtonText = new Color(0.95f, 0.87f, 0.60f, 1f);

        /// <summary>
        /// 按钮**不可用**时的文字色。
        /// <para>登记为**允许的差异**：它的对比度**故意**低于 4.5:1（0.45/0.43/0.40 压在原版石牌上约 1.2:1）。
        /// （"Text or images of text that are part of an inactive user interface component … have no contrast
        /// requirement"）。禁用态靠"变暗"表达不可点，这正是原版语义，不为了过门槛把它提亮
        /// （提亮会让"禁用"与"可用"分不出来）。</para>
        /// </summary>
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

        // ── 原版贴图的「色调」状态（解决异步竞态 / d2-uiart 后只留项目侧副本）──────
        // 贴图是 `LoadAsset` **异步**回来的：面板在贴图回来**之后**才改色（如创角屏的
        // 「当前选中职业」）会被这次回调覆盖成白色 ⇒ 把"这幅图想要的颜色乘数"记在
        // `ConditionalWeakTable` 上（键是 Image，Image 被销毁后条目随 GC 消失，不泄漏），
        // 由**加载完成的那一刻**统一套用。这样两种顺序都对。
        //
        //   `UiImageLoader`**（它自己按 Image 记序号 + 路径 + 在途/已落地标记），本文件不再记 `Request`。
        //   这里保留的只是**项目侧色调副本**，原因：`UiImageLoader` 把色调存在它自己的弱表里且
        //   **不暴露读接口**，而本文件另有两条"不经 `UiImageLoader` 的落图路径" —— 条带整条取帧
        //   （`ApplyButtonFrames`）/ 原版单帧按钮底图（`Apply`）/ 专用底图（`LoadOrig`）——
        //   它们在贴图到位那一刻要读回"这幅图想要的色调"。
        //   ⇒ 色调**双写**（引擎一份 + 项目一份），两边口径一致（默认 `ArtFullBright` = 原版亮度白）。
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
        /// 贴图**已在**则立即生效；**未到**则记下，等贴图到位的那一刻套用（不丢状态）。
        /// <para>设置逻辑由引擎件 `UiImageLoader.SetTint` 负责（贴图已在 ⇒ 立即写 <c>img.color</c>；
        /// 未到 ⇒ 记下、由回调套用）；项目侧另存一份副本（见 <see cref="ArtState"/> 的注释），
        /// 供条带 / 单帧落图路径读回。</para>
        /// </summary>
        public static void SetArtTint(Image img, Color tint)
        {
            if (img == null) return;
            StateOf(img).Tint = tint;               // 项目侧副本（落图路径读它）
            UiImageLoader.SetTint(img, tint);       // ★ 引擎件：贴图已在 ⇒ 立即生效；未到 ⇒ 记下等回调套用
        }

        // ── 根节点 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 把面板根节点铺满父层（引擎 `UIManager` 把面板挂到固定层节点下，
        /// 但**预制体根节点的锚点不可信** ⇒ 每次打开都显式铺满一次，幂等）。
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
        /// <para>面板底图 / 图标 / 标题条用原版 DC6 解出的 PNG，
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
        /// 加载**成功** ⇒ `color = 原版亮度`（或调用方经 <see cref="SetArtTint"/> 记下的色调）；
        ///   加载**失败** ⇒ 保留调用方设的占位底色并打 Warn（纯色占位可见，不静默变黑/变透明）。
        /// </para>
        /// <para>
        /// **请求守卫：同一 Image 只有"最新一次请求"的回调会落地**。
        /// 连续发起 A、B 两次请求时，**A 的回调可能晚于 B 到达**，于是画面停在 A（旧图）。
        ///   过期的直接丢弃（连 Warn 都不打：那是设计内行为，不是异常）。
        /// </para>
        /// <para>**同一个 Image 上"后发起的请求"胜出**。所有调用点都是"贴当前该显示的那张图"；
        /// 回调按序到达时结果与发起顺序一致，乱序时把"错态"修成"最新态"。</para>
        /// </summary>
        /// <param name="onLoadedTint">可选的显式色调；不传 = 用记下的色调（默认原版亮度白）。</param>
        /// <remarks>
        /// 接口一对一同口径；引擎版**另加**"同一 Image 上同路径在途/已成功则不重复发起"的去重（失败过的
        /// 路径不拦 ⇒ 再调一次就是重试）。贴图加载一律经引擎件，本文件不直接调 `Game.Res.LoadAsset`。
        /// </remarks>
        public static void SetSprite(Image img, string spritePath, Color? onLoadedTint = null)
        {
            if (img == null || string.IsNullOrEmpty(spritePath)) return;

            if (onLoadedTint.HasValue) StateOf(img).Tint = onLoadedTint.Value;  // 与项目侧副本保持一致

            if (!_r1cGuardLogged)
            {
                _r1cGuardLogged = true;
                Log.Info("R1-C", "UiArt.SetSprite 转调引擎 `UiImageLoader.SetSprite`（请求序号守卫 + 占位保留 + 同路径去重"
                    + " + unlit 校验）：同一 Image 上只有最新一次请求的回调会落地"
                    + "（`LoadAsset` 异步 ⇒ 连续换图时旧回调可能晚到并盖掉新图；逐帧动画/快速悬停会踩到）"
                    + "；过期回调丢弃、不告警（设计内行为）");
            }

            UiImageLoader.SetSprite(img, spritePath, onLoadedTint);   // ★ 引擎件（唯一实现）
        }

        /// <summary>"只报一次"标志（见 <see cref="SetSprite"/> 的请求守卫，由引擎件落地）。</summary>
        private static bool _r1cGuardLogged;

        /// <summary>
        /// 造一个**原版底图**按钮：`UIFactory.CreateButton` + 项目配色/字号 +
        /// 原版帧底图（常态/悬停/按下，见 <see cref="ApplyButtonFrames"/>）。
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
            text.fontSize = (int)UiLayoutGame.FontPx16;

            //   引擎 `UIFactory.CreateButton` 造出来的那个 Text 保留为**数据持有者**（font=null、enabled=false）。
            D2TextMirror.Attach(text, D2Text.FontFor(20), null);

            //   见 `ApplyOriginalButtonArt` 的注释（为什么换数据源）。
            // 兜底路径 = 老的「多帧条带 + SpriteSwap」：**只在单帧原版图缺失时**才用它
            //   （例如素材目录还没同步到新文件），这样按钮不会退化成纯色块。
            //
            //   `[Error] [Resource] 加载失败：D2/UI/Menu/button_wide_{0,1,2}`
            //   （进 Play 的 4 条 Error 里这 3 条就是它们；实测时间戳 16:21:48.400/401 同一帧）。
            //   现在**只有**「单帧原版图整组缺失」才会走到它（`onMissing`）⇒ 正常工程一次都不发起。
            //   这不是"把 Error 降级 / 加静默兜底"：兜底路径**原地保留**（真缺失时照样
            //      取条带帧 + 逐帧 Warn + 条带兜底），只是不再**预先**跑一条已知取不到图的路径。
            //   d2-uiart：兜底路径的取帧改走引擎 `SpriteStripLoader`（见 `RequestStrip` 的转调），
            //     套帧仍由本文件的 `ApplyButtonFrames` 收敛（它只做"帧表 → 项目的按钮语义"这一段）。
            ApplyOriginalButtonArt(img, size, () =>
            {
                var strip = ButtonStripFor(size);
                RequestStrip(strip, ButtonStripFrames(strip), frames => ApplyButtonFrames(img, frames));
            });
            return img;
        }

        //  与 `RequestStrip`/`MenuButtonWide` 的区别（**不是重复造轮子，是换了数据源**）：
        //      4 帧 = 256×35 + 16×35 + 256×35 + 16×35 ⇒ 按钮 = 帧 0+1（常态）/ 帧 2+3（按下），
        //    于是直接落成两张整幅 PNG（272×35），**帧矩形不需要猜、路径唯一、单帧加载稳定**
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
                //   原版**高亮帧** `ResPaths.BtnMedSel`（= `FrontEnd/MediumSelButtonBlank.dc6` 帧 0）。
                //   该 DC6 属于 **FrontEnd 组 ⇒ 必须用 `fechar` 调色板解**（**不是** ACT1，**也不是**
                //   曾被猜过的 `menu1`）。工程里那份曾是**按 ACT1 解的麻点图**：
                //     孤立高饱和像素占比 **0.424**（同族干净图 0.017~0.075）⇒ 实机一悬停底板就花斑、
                //     把按钮文案糊掉（实机图中 NPC 对话第 2 项「交易」被糊住、第 1 项「離開」清晰）。
                //   定案依据（**可复算**）：把 MPQ 里能解的 **15 套** `data/global/palette/*/Pal.PL2`
                //   全解出来逐套量**同一麻点判据**：
                //     · `fechar` sel 帧0 = **0.054**（帧1 = 0.055）← 唯一落进干净带；
                //     · `menu1` 0.271（**前片的假设被证伪**）/ menu4 0.103 / sky 0.106 /
                //       ACT1 **0.424**（旧图来源）/ 其余 0.24~0.55；
                //     · **横向佐证（决定性）**：同目录的**共享**按钮只有 **ACT1** 干净 ——
                //       `WideButtonBlank` 0.017、`MediumButtonBlank` 0.040、`CancelButtonBlank` 0.045，
                //       用 `fechar` 反而 0.104~0.177 起麻点；**只有 FrontEnd 专属的 Sel 变体反过来要
                //       `fechar`** ⇒ 即「同一个 DC6 里不同按钮按所属组用不同 palette」。
                //     · 与本项目既有惯例一致：`export_d2ui.py` 的 `frontend` 组本来就整组用 `PL2_FECHAR`。
                //   资产已按 `fechar` 重出（`btn_med_sel` 0.054 / `btn_med_sel_pressed` 0.055），
                //   导出器 `tools/d2codec/export_d2ui.py::group_buttons` 已同步改用 `PL2_FECHAR`
                //   （防全量重导把麻点图覆盖回来）。
                //   判据 = uicheck 的 Ⓐ-4/Ⓐ-4b（`ButtonSpritesFor` 返回的**每一条**路径都无花斑）
                //   ⇒ 本方法保持纯函数、离线可断言。
                highlight = ResPaths.BtnMedSel;
            }
        }

        /// <summary>
        /// 原版按钮底图的三态就位后套到按钮上（`SpriteSwap`）。
        /// </summary>
        /// <param name="onMissing">
        /// </param>
        private static void ApplyOriginalButtonArt(Image img, Vector2 origSize, Action onMissing)
        {
            if (img == null) return;

            string normalPath, pressedPath, highlightPath;
            ButtonSpritesFor(origSize, out normalPath, out pressedPath, out highlightPath);

            var state = new ButtonArtState { Target = img, OnMissing = onMissing };
            var btn = img.GetComponent<Button>();
            state.Button = btn;

            //   已消失：`btn_med_sel.png` 已按 `fechar` 调色板重出（麻点 0.424 → 0.054）。

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
                // 两条路径都在 Load1 / 条带加载里 Warn 过，这里只点名"走了哪条兜底"。
                Log.Warn(Tag, $"原版按钮单帧底图整组缺失 ⇒ 退回多帧条带兜底（{ResPaths.MenuButtonWide} / "
                              + $"{ResPaths.MenuButtonMedium}）；条带也缺则按钮保持纯色块 {ButtonBg}");
                st.OnMissing?.Invoke();
                return;
            }

            var btn = st.Button != null ? st.Button : img.GetComponent<Button>();
            if (btn == null)
            {
                // 非预期分支：底板没有 Button 组件 ⇒ 只能贴常态帧（悬停/按下不会变）。不静默
                Log.Warn(Tag, $"按钮 {img.name} 没有 Button 组件 ⇒ 原版底图只套了常态帧（悬停/按下无变化）");
                img.sprite = normal;
                // 原版帧的宽高比是固定的（宽 272×35 / 中 128×35）：`preserveAspect` 让**矩形尺寸
                //   与帧比例不一致时按比例内缩**，而不是把原版按钮拉长/压扁（不改原版像素）。
                img.preserveAspect = true;
                img.color = StateOf(img).Tint;          // 原版亮度（默认白）
                EnsureUnlit(img);
                return;
            }

            // d2-uiart：四态落地（`transition` / `SpriteState` / 缺态回落常态 / 色调 / `preserveAspect`
            //   / unlit）转调引擎 `SpriteSwapButton.Apply` —— 本文件不再手写 `SpriteState`
            //   （手写时"某态为 null"会被 Unity 的 `DoSpriteSwap` **静默早退**，这正是引擎件要治的坑）。
            SpriteSwapButton.Apply(btn, new SpriteSwapButton.Skin(
                normal,
                st.Sprites[2],                      // 悬停 = 高亮帧（宽按钮没有高亮帧 ⇒ 由引擎回落常态）
                st.Sprites[1],                      // 按下（缺 ⇒ 引擎回落常态）
                normal,                             // 禁用 = 常态（只灰化文字，见 SetInteractable）
                tint: StateOf(img).Tint,            // 项目侧色调（默认 = 原版亮度白）
                placeholder: ButtonBg,
                preserveAspect: true));
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

        // ── 原版**小图标帧**的唯一取法（DC6 直出那一套）──────────────────────────
        //  为什么需要这一节：原版这三张图在工程里各有**两套导出**（同一画面、alpha 不同）——
        //    · `{name}_{i}.png`            = 本项目 `tools/d2codec/export_d2ui.py` 从原版
        //      `data/global/ui/PANEL/{name}.DC6` 直出 ⇒ **调色板索引 0 = 透明**（= D2 的口径，
        //      `tools/d2codec/dc6.py:frame_rgba` 的 `if idx == 0: continue`）；
        //    · `menubutton__0__{i}.png` / `minipanelbtn__00__{ii}.png` /
    //      `runbutton_{run,walk}_{NotPressed,Pressed}.png`
    //                                  = 社区复刻工程 `Diablerie/Assets/Images/ControlPanel/`
    //      的同名副本 ⇒ 实测**逐像素 RGB 完全相同**，但把原版那些透明像素写成了
    //      **不透明黑 (0,0,0,255)**（menubutton 每帧 38 px、minipanelbtn 每帧 20 px）。
    //  ⇒ 用副本 = 画面上多出黑点/黑线（原版那里是透出底图大理石）。
    //  上列 20 个副本文件（`menubutton__0__*` / `minipanelbtn__00__*`）**不参与取帧**，
    //    `Core/ResPaths.cs` 的 4 个 `PanelArrow*` 也已改指本节的 `menubutton_{0..3}` ⇒
    //    本节的两个 helper 就是这些帧名的**唯一来源**。
    //  判据（逐像素比对同族两套导出的 alpha 掩码）：
    //    20 对全部「同画面；alpha 差 N 像素（DC6=透明 / 副本=不透明黑）」；
    //    `runbutton` 的描述名 ↔ 帧号由**逐像素同画面**自动配对得到（不许靠猜名字）：
    //      walk_NotPressed→0、walk_Pressed→1、run_NotPressed→**2**、run_Pressed→3。
    //  本节的常量只是**帧名**；贴图仍走 <see cref="SetSprite"/>（异步 + 缺图 Warn）。

        /// <summary>
        /// 原版 `PANEL/menubutton.DC6` 第 <paramref name="i"/> 帧（15×24；0/1 = 上箭头常态/按下、2/3 = 下箭头常态/按下）。
        /// <para>用途：HUD 小面板开关箭头、人物属性面板的四维加点箭头（原版同一张图，两处共用这一个定义）。</para>
        /// </summary>
        public static string ArrowFrame(int i) => ResPaths.D2UiPanel + "menubutton_" + i;

        /// <summary>原版 `PANEL/minipanelbtn.DC6` 第 <paramref name="i"/> 帧（20×20；偶数 = 常态、奇数 = 按下）。</summary>
        public static string MiniPanelBtnFrame(int i) => ResPaths.D2UiPanel + "minipanelbtn_" + i;

        /// <summary>原版 `PANEL/runbutton.DC6` 第 2 帧 = **跑**·常态（配对依据见本节注释）。</summary>
        public const string RunButtonRunFrame = ResPaths.D2UiPanel + "runbutton_2";

        /// <summary>原版 `PANEL/runbutton.DC6` 第 0 帧 = **走**·常态（配对依据见本节注释）。</summary>
        public const string RunButtonWalkFrame = ResPaths.D2UiPanel + "runbutton_0";

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
        /// 原版「买卖屏方钮」的**单帧文件**前缀：`D2/UI/Panel/buysellbtn_{帧号}`（帧号语义见
        /// <see cref="ResPaths.BuySellButtonFrameClose"/>；常态 / 按下 = 连续两帧）。
        /// <para>为什么不用 `ResPaths.PanelBuySellButton`（那是整张 1024×64 条带的路径）：
        /// `D2/UI/Panel/buysellbtn.DC6.0_0` 直接
        /// `[Error] [Resource] 加载失败：D2/UI/Panel/buysellbtn.DC6.0_0`（本工程这套导入设置下
        /// **子 sprite 按名加载失效**，同引擎 <c>SpriteStripLoader</c> 注释里那条 `button_wide_0` 的实测）。
        /// 而这批素材**同时**存在单帧文件 `buysellbtn_0.png`（Sprite/Single/Point，与能正常加载的
        /// `buyselltabs_0.png` 导入参数逐项相同）⇒ 方钮走单帧文件，不做条带按名取帧。</para>
        /// </summary>
        public const string BuySellButtonFramePrefix = ResPaths.PanelBuySellButtonFramePrefix;

        /// <summary>
        /// 把原版「关闭 / 取消」方钮图形贴到按钮上（常态帧 = <see cref="ResPaths.BuySellButtonFrameClose"/>，
        /// 按下 / 悬停帧 = 它 + 1；路径 = <see cref="BuySellButtonFramePrefix"/>）。
        /// <para>与 `UI/ShopPanel.cs` 的方钮同一条取帧口径（单帧文件；条带子 sprite 按名加载在本工程失效）。
        /// 常态帧走 <see cref="SetSprite"/>（它会把 Image 的 color 置成原版亮度白），按下帧后到即补
        /// `SpriteSwap`。</para>
        /// </summary>
        public static void ApplyCloseButtonArt(Image img, Button btn)
        {
            if (img == null) return;

            SetSprite(img, BuySellButtonFramePrefix + ResPaths.BuySellButtonFrameClose);

            if (btn == null || Game.Res == null)
            {
                // 非预期分支：没有 Button 组件（悬停/按下不会变）或资源门面未起 ⇒ 点名原因，只留常态帧。
                Log.Warn(Tag, $"关闭钮 {img.name} 只贴了常态帧（"
                    + (btn == null ? "没有 Button 组件" : "Game.Res 未初始化") + "）");
                return;
            }

            Game.Res.LoadAsset<Sprite>(BuySellButtonFramePrefix + (ResPaths.BuySellButtonFrameClose + 1), sp =>
            {
                if (img == null || sp == null) return;
                btn.transition = Selectable.Transition.SpriteSwap;
                btn.spriteState = new SpriteState { pressedSprite = sp, highlightedSprite = sp };
            });
        }

        //   底图由调用方贴（见 `BuySellButtonFramePrefix`），这里只保证"定尺 + 命中区 + 文字"。
        //   为什么单独一个（不直接用 `Button`）：`Button` 会按**原版宽/中按钮**（272×35 / 128×35）
        //   贴底图，而那两套是**前端菜单**的按钮，跟买卖屏右下那 4 个雕槽（底图实测 34×27）不是一回事。
        /// <param name="labelRect">
        /// **可选**：标签矩形，**相对按钮自身中心**（按钮 local 空间，按钮 rect = 以 (0,0) 为中心、
        /// 边长 <paramref name="sz"/> 的正方形）。给 `null` ⇒ 保持历史行为（标签铺满整钮）。
        /// <para> 为什么加这个口子：原版这两颗钮是**纯图形自明**（：修理 = 锤+铁砧、
        /// 关闭 = ⊘），标签铺满整钮时**正好压住图形**（实机 8× 放大图里只剩两个汉字）⇒
        /// 商店把标签放到**钮外正下方**（`ShopPanel.ButtonLabelRect`，纯函数 + 离线判据）。
        /// 默认值 `null` 保证既有的"铺满"语义不被悄悄改掉（改行为必须由调用方**显式**给矩形）。</para>
        /// </param>
        public static Image SquareButton(Transform p, string n, string lb, Vector2 sz, Vector2 ps, Action cb,
            Rect? labelRect = null)
        {
            var img = UIFactory.CreateButton(n, p, lb, sz, ps, ButtonBg, cb);
            EnsureUnlit(img);

            var text = img.transform.Find("Label")?.GetComponent<Text>();
            if (text == null)
            {
                Log.Warn(Tag, $"方钮 {n} 找不到 Label 子节点 ⇒ 文字仍由引擎默认字体绘制（应排查 UIFactory）");
            }
            else
            {
                text.color = ButtonText;
                text.fontSize = (int)UiLayoutGame.FontPx16;      // 字号唯一真源（⛔ 不写死）
                D2TextMirror.Attach(text, D2Text.FontFor((int)UiLayoutGame.FontPx16), null);
                ApplyLabelRect(text, labelRect, sz);
            }

            UiLog.Info($"SquareButton 已建（底图由调用方贴；标签={Describe(labelRect, sz)}）");
            return img;
        }

        /// <summary>
        /// 把方钮标签摆到指定矩形（不给 ⇒ 保持 `UIFactory.CreateButton` 的**铺满**形状）。
        /// <para>纯几何：只碰 RectTransform 的 anchor/pivot/sizeDelta/anchoredPosition，
        /// 不动文字内容 / 字号 / 字色 / 字模。</para>
        /// </summary>
        private static void ApplyLabelRect(Text text, Rect? labelRect, Vector2 buttonSize)
        {
            if (text == null || !labelRect.HasValue) return;
            var rt = text.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = labelRect.Value.size;
            rt.anchoredPosition = labelRect.Value.center;
            if (rt.rect.width < 1f || rt.rect.height < 1f)
            {
                // 非预期分支：退化矩形（宽/高 ≤ 0）⇒ 文字会画不出来，点名而不是静默
                Log.Warn(Tag, $"方钮标签矩形退化 {labelRect.Value}（按钮 {buttonSize}）⇒ 标签只剩 0 尺寸");
            }
        }

        private static string Describe(Rect? labelRect, Vector2 buttonSize)
            => labelRect.HasValue
                ? $"钮外 {labelRect.Value.size.x:0.#}×{labelRect.Value.size.y:0.#} @({labelRect.Value.center.x:0.#},{labelRect.Value.center.y:0.#})"
                : $"铺满按钮 {buttonSize.x:0.#}×{buttonSize.y:0.#}";

        /// <summary>
        /// <para>
        /// 与 <see cref="Button"/> 的唯一区别：`Button` 的底图按**原版宽/中按钮**（272×35 / 128×35，
        /// = 前端菜单按钮）自动挑，而原版**有些屏上的按钮是另一套专用帧** ——
        /// 例：死亡屏的 `MENU/endgameok.dc6`（**96×32 ×2 帧**：常态 / 按下，实测）。
        /// 强行套 272×35 会把原版按钮拉变形 ⇒ 这里让调用方给**路径前缀**（**不带结尾下划线**，
        /// 例 `ResPaths.MenuEndGameOK` ⇒ `D2/UI/Menu/endgameok`），本方法内部走
        /// <see cref="ResPaths.Frame"/>（它会自己拼 `_i`）取 `{prefix}_0` / `{prefix}_1` 做 SpriteSwap。
        /// 传 `"…endgameok_"` 会拼出 `…endgameok__0`（双下划线）⇒ 取不到图，只剩纯色块。
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
                text.fontSize = (int)UiLayoutGame.FontPx16;
                D2TextMirror.Attach(text, D2Text.FontFor((int)UiLayoutGame.FontPx16), null);
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

                var btn = st.Button != null ? st.Button : img.GetComponent<Button>();
                if (btn == null)
                {
                    // 非预期分支：底板没有 Button 组件 ⇒ 只能贴常态帧。不静默
                    Log.Warn(Tag, $"按钮 {img.name} 没有 Button 组件 ⇒ 原版专用底图只套了常态帧（按下无变化）");
                    img.sprite = normal;
                    // 原版帧宽高比固定（96×32）⇒ 矩形与帧比例不一致时按比例内缩，不拉变形
                    img.preserveAspect = true;
                    img.color = StateOf(img).Tint;
                    EnsureUnlit(img);
                    return;
                }

                // 四态落地转调引擎 `SpriteSwapButton.Apply`。原版**专用钮只有常态/按下两态**
                //   ⇒ 悬停与选中都指常态帧。
                SpriteSwapButton.Apply(btn, new SpriteSwapButton.Skin(
                    normal,                             // 常态
                    normal,                             // 悬停 = 常态（原版这两套素材没有高亮帧）
                    st.Sprites[1],                      // 按下（缺 ⇒ 引擎回落常态）
                    normal,                             // 禁用 = 常态
                    tint: StateOf(img).Tint,
                    placeholder: ButtonBg,
                    preserveAspect: true));
            });
        }

        /// <summary>
        /// 造一个定尺文本（居中定位于父层中心偏移 pos）。
        /// <para>**画面由原版字模画**（`D2TextMirror` + `D2Label`）。
        /// 返回的 `Text` 是**数据持有者**（`font = null` + `enabled = false`，不产生任何绘制），
        /// 面板照旧用 `_x.text / _x.color / _x.horizontalOverflow / _x.resizeTextForBestFit` 写它，
        /// 由镜像逐帧同步到字模（改动面最小、且**布局坐标一个都不动**）。</para>
        /// </summary>
        /// <param name="forceChi">
        /// true ⇒ 即使文案全是 ASCII 也走 chi（原版中文）字模。**只有品牌署名行用得到** ——
        /// 原版拉丁字模 `font{16,24,30,42}` 不分大小写（97..122 是缩小号的同形大写、无降部），
        /// `by clover-engine` 会被画成 `BY CLOVER-ENGINE`；`font{N}_chi` 的 ASCII 才是真小写。
        /// 实测与出处见 `D2Text.D2Label._forceChi` 的注释。
        /// </param>
        public static Text Label(Transform parent, string name, string content, int fontSize, TextAnchor anchor,
            Color color, Vector2 size, Vector2 pos, bool forceChi = false)
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
            D2TextMirror.Attach(text, D2Text.FontFor(fontSize), null, forceChi);
            return text;
        }

        /// <summary>
        /// 造一个输入框（引擎 `UIFactory` 无输入框构件，按 uGUI 的标准层级代码搭：
        /// 底图 + Text + Placeholder）。返回挂好引用与监听无关的 `InputField`。
        /// </summary>
        public static InputField Input(Transform parent, string name, Vector2 size, Vector2 pos,
            string placeholder, string initial, int charLimit)
        {
            // **这个框在本工程里是"显示层"，不是交互控件** —— 因此：
            //   ① `raycastTarget = false`（鼠标点不到它 ⇒ uGUI `InputField.OnPointerDown` 收不到事件，
            //      不会 `SetSelectedGameObject`）；
            //   ② `targetGraphic = null` + `navigation = None`（键盘/手柄导航也不会选中它）。
            //   是 `activeInputHandler: 1`（只用新 Input System），而 uGUI `InputField`：
            //     · `caretPosition` setter（`PackageCache/com.unity.ugui@…/Runtime/UGUI/UI/Core/InputField.cs:1068-1072`）
            //       → `selectionAnchorPosition`/`selectionFocusPosition` setter（`:1082-1113`）
            //       → 读 `compositionString.Length`（`:1087` / `:1110`）→ `:343-346`
            //       `input != null ? input.compositionString : Input.compositionString`
            //       （本工程无人设 `inputOverride` ⇒ `input` 是 uGUI 自造的那个 `BaseInput` ⇒ 落到
            //        `UnityEngine.Input.compositionString`）⇒ **抛 InvalidOperationException**（该 setter
            //        无任何条件保护 ⇒ 一旦调用必抛）；
            //     · `text` setter → `SetText`（`:486`）→ `UpdateLabel`（`:533`）→ `:2690` 读同一处
            //         —— 这一句**有短路**：`EventSystem.current.currentSelectedGameObject == gameObject`
            //         为假时不会读 ⇒ **它只在"输入框是当前选中项"时才抛**。
            //   ⇒ 只要让它**永远成不了选中项**（上面 ①②），这两条抛点就都不可达；
            //     字符与光标由面板自己驱动（见 `CharCreatePanel.DisplayName` / `PushName`）。
            var bg = Panel(parent, name, size, pos, InputBg, false);

            //   InputField 的 `textComponent` 必须是**真实、启用**的 uGUI `Text`
            //   （uGUI 要求它有 font，否则 InputField 直接不工作；`InputField.cs:3309` 的
            //    `m_TextComponent.font == null` 会让它拒绝激活），没法换成字模方块
            //   ⇒ 这里保留 `UIFactory.DefaultFont()`。
            //   它渲染的是**玩家键入的角色名（ASCII 字母 + 下面的 `|` 光标标记）**，不是中文
            //   ⇒ 「中文 0 处走默认字体」仍成立。
            //   **光标不由它画**（它的 caret 要 `caretPosition`，本配置下必抛）；
            //     本节点现在由面板直接驱动（`CharCreatePanel.PushName` 写 `textComponent.text`），
            //     所以"可见文本/光标"这条判据不依赖 uGUI 的 TextGenerator 光标逻辑。
            var textRt = UIFactory.CreateNode("Text", bg.transform);
            var text = textRt.gameObject.AddComponent<Text>();
            text.font = UIFactory.DefaultFont();
            text.fontSize = 22;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = TextColor;
            text.supportRichText = false;
            // **子 Text 也不吃射线** —— uGUI 的点击是"命中 Graphic 后沿父链冒泡派发"，
            //   子 Text 若是 raycastTarget，点在字上仍会冒泡到 InputField（父上的 IPointerDownHandler）
            //   ⇒ 光把底图关掉不够（这正是"看起来改对了、其实还能点进焦点"的那类漏改）。
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            textRt.offsetMin = new Vector2(10f, 2f);
            textRt.offsetMax = new Vector2(-10f, -2f);

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
            ph.raycastTarget = false;                   // ★ R1-C：同 text（占位符也是子 Graphic，不吃射线）
            phRt.offsetMin = new Vector2(10f, 2f);
            phRt.offsetMax = new Vector2(-10f, -2f);

            var input = bg.gameObject.AddComponent<InputField>();
            // **不设 `targetGraphic`**（= 不做任何状态着色，也不让它参与 Selectable 的选中/导航）
            input.targetGraphic = null;
            input.navigation = new Navigation { mode = Navigation.Mode.None };
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
        /// 轨道是父底图，填充块的比例由**引擎** <see cref="UIFactory.SetBarWidth"/> 表达。
        /// <para>本方法原来自带一个 <c>SetBarRatio</c> 与自己的一套锚点数学，
        /// 与引擎 <c>UIWidgetControls.cs:239</c> 是**同一件事的第二份实现** ⇒ 已删；
        /// 刷新一律调 <c>UIFactory.SetBarWidth(fill.rectTransform, ratio)</c>（见 <see cref="ProgressBar"/>
        /// 与 `SettingsPanel.Refresh`）。</para>
        /// </summary>
        /// <returns>填充块 Image；调用方用 `UIFactory.SetBarWidth(fill.rectTransform, ratio)` 刷新。</returns>
        public static Image ProgressBar(Transform parent, string name, Vector2 size, Vector2 pos,
            Color trackColor, Color fillColor)
        {
            var track = Panel(parent, name, size, pos, trackColor, false);
            var fill = FullPanel(track.transform, "Fill", fillColor, false);
            UIFactory.SetBarWidth(fill.rectTransform, 0f);      // 唯一实现 = 引擎件
            return fill;
        }


        /// <summary>
        /// 判断某 shader 名是否**会让 UI 图受 2D 光照影响**（⇒ 必须换掉）。
        /// <para>
        /// 纯函数（离线宿主可断言）：含 `unlit`（`Sprite-Unlit-Default` / `Unlit/Color`）⇒ **不受光**；
        /// 否则出现 `lit`（`Sprite-Lit-Default` / `Universal Render Pipeline/2D/Sprite-Lit-Default`）⇒ 受光。
        /// `UI/Default` 与 `Sprites/Default` 都是 unlit ⇒ 返回 false。
        /// </para>
        /// </summary>
        public static bool IsLitShader(string shaderName) => UiImageLoader.IsLitShader(shaderName);

        /// <summary>
        /// 确保这个 Image 走**不受 2D 光照影响**的材质（引擎 Canvas 是
        /// `RenderMode.ScreenSpaceOverlay`，`Runtime/Presentation/UI.cs:49` ⇒ 默认材质 =
        /// Canvas 的 `UI/Default`，unlit）。本函数是**防御性校验**：只有发现 Image 被挂了
        /// 受光材质才动手（换回 Canvas 默认材质），并留下 Warn —— 不静默。
        /// <para>实现体转调引擎 `UiImageLoader.EnsureUnlit`（限频 Warn 由引擎 `LogThrottle` 负责）。</para>
        /// </summary>
        public static void EnsureUnlit(Image img) => UiImageLoader.EnsureUnlit(img);

        //   取帧实现见引擎 `Runtime/Presentation/SpriteStripLoader.cs`（含"整条优先"的次序、
        //   与"回调可能**同帧同步**"的约定）；本文件持有一个引擎加载器实例并转调，
        //   项目侧只留"帧表 → 本项目的按钮语义"这一段（见 `ApplyButtonFrames` / `Apply`）。

        /// <summary>项目侧共享的条带加载器（**进程内同路径只取一次图**）。</summary>
        private static readonly SpriteStripLoader StripLoader = new SpriteStripLoader();

        /// <summary>
        /// 取一条**多帧条带**的全部帧（逐帧按名 + 「整条 LoadAll」兜底，与按钮底图同一套）；
        /// 就绪后回调（帧序 = 条带内顺序，缺帧为 null）。
        /// <para>
        /// 为什么不直接 `Game.Res.LoadAsset&lt;Sprite&gt;(ResPaths.Frame(...))`：
        /// 逐帧按名（`Game.Res.LoadAsset&lt;Sprite&gt;("…/goldcoinbtn.dc6.0_0")`）在本工程的导入设置下返回 **null**，
        /// 只有整条 `LoadAll` 拿得到帧 ⇒ 取帧一律走本条带加载器。
        /// </para>
        /// <remarks>
        /// 全部由引擎件负责；项目侧只留一个**进程内共享**的加载器实例（<see cref="StripLoader"/>），
        /// 保证"同一条带只取一次图"的既有行为不变。
        /// </remarks>
        public static void RequestStrip(string stripPath, int frameCount, Action<Sprite[]> onReady)
            => StripLoader.RequestStrip(stripPath, frameCount, onReady);

        /// <summary>
        /// 把一条**多帧条带**的帧表套到按钮上 —— 帧序 **0 = 常态 / 1 = 悬停 / 2 = 按下**
        /// （= `AssetImporter.MultiFrameStrips` 的实测帧矩形；禁用态沿用常态帧，只灰化文字，见 <see cref="SetInteractable"/>）。
        /// <para>取帧 / 同路径去重 / 整条兜底由引擎 `SpriteStripLoader` 负责（见 <see cref="RequestStrip"/>）；
        /// 这里只做"帧表 → 本项目的按钮语义"，四态落地转调引擎 `SpriteSwapButton.Apply`。</para>
        /// <para>缺帧**不静默**：整条缺失（`frames[0] == null`）⇒ 保留纯色占位 <see cref="ButtonBg"/> 并 Warn
        /// （条带加载器已点名路径与缺口数）；非法参数（空表）⇒ 加载器已 Warn，这里直接返回（不重复告警）。</para>
        /// </summary>
        private static void ApplyButtonFrames(Image img, Sprite[] frames)
        {
            if (img == null) return;                            // 面板已关闭销毁
            if (frames == null || frames.Length == 0) return;   // 参数非法：加载器已 Warn

            var normal = frames[0];
            if (normal == null)
            {
                // 非预期分支：整条缺失 ⇒ 保持纯色块（加载器已点名条带路径 + 缺口数）
                Log.Warn(Tag, $"条带取帧失败 ⇒ 按钮 {img.name} 保持纯色块 {ButtonBg}（见本文件取帧注释）");
                return;
            }

            var btn = img.GetComponent<Button>();
            if (btn == null)
            {
                // 非预期分支：底板没有 Button 组件 ⇒ 只套常态帧（悬停/按下无变化）。不静默
                Log.Warn(Tag, $"按钮 {img.name} 没有 Button 组件，原版底图只套了常态帧（悬停/按下无变化）");
                img.sprite = normal;
                img.color = StateOf(img).Tint;           // 原版亮度（或调用方记下的色调）
                EnsureUnlit(img);
                return;
            }

            // 引擎件：四态落地（缺态回落常态、不把 null 填进 SpriteState —— 那会让 Unity 静默早退）
            SpriteSwapButton.Apply(btn, new SpriteSwapButton.Skin(
                normal,
                frames.Length > 1 ? frames[1] : null,    // 悬停（缺 ⇒ 引擎回落常态）
                frames.Length > 2 ? frames[2] : null,    // 按下（缺 ⇒ 引擎回落常态）
                normal,                                 // 禁用 = 常态
                tint: StateOf(img).Tint,
                placeholder: ButtonBg));
        }
    }
}
