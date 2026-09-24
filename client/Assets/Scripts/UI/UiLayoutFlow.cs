// ─────────────────────────────────────────────────────────────────────────────
// **2026 主 agent 裁决：换算从 ×2.4 改为 ×1.8 + 水平居中**
//
// 唯一职责：**流程面板（启动 / 主菜单 / 选角 / 创角 / 设置 / 读条 / 暂停）的布局常量表**。
// 全部数值来自原版暗黑2 的 Unity prefab（社区复刻工程 `mofr/Diablerie`）里**真实的
// RectTransform**，不是"设计一版顺眼的"。
//
//   · 主菜单      `Prefabs/Menu/MainMenu.prefab`        → 节点 `MainMenu/GameMenu/Buttons` + 子按钮
//   · 选角 / 创角 `Prefabs/Menu/ClassSelectMenu.prefab` → 节点 `ClassSelectMenu/Canvas` + 子节点
//   · 宽按钮      `Prefabs/Menu/WideButton.prefab`      → 272×35（含 Text 子节点：fontSize 18 / Bold /
//                                                         MiddleCenter / color #191919）
//   · 中等按钮    `Prefabs/Menu/MediumButton.prefab`    → 128×35
//   · 场景        `Scenes/MainMenu.unity`               → 仅用于确认基准 = 800×600
//
//   ① **主菜单项回归原版 4 项**（删 `CONTINUE` / `SETTINGS`；设置 → ESC 暂停菜单的 `OPTIONS`）；
//   ② **创角屏的 5 个职业改成原版半身像**（源 `data/global/ui/FrontEnd/{cls}/{CLS}{NU1,NU2,NU3}.DC6`，
//      三态 = 默认/悬停/选中；几何推导见 `ClassMenu.PortraitState` 的注释），
//      并**删掉**「属性点分配 / 剩余点数 / 生命法力耐力预览」三项（原版创角没有）；
//   ③ **选角屏删掉**自加的「存档角色 N 个（一屏最多 7 个）」说明行 —— 那一行改回原版
//      `ClassDescription` 的语义（显示高亮角色的**职业说明**，文案出处见 `ClassText`）。
//
// 换算口径（**逐元素，不"看着差不多"**）：
//   原版基准 = **4:3（800×600）**；本工程画布 = **16:9（1920×1080）**（引擎 `UIFactory`/`UIManager`
//   固定 `referenceResolution`，见 `Runtime/Presentation/UI.cs:52-56`）。
//   旧口径用**宽度比** 1920/800 = **2.4** 等比缩放 —— 600×2.4 = **1440 > 1080**，纵向必然溢出。
//   正确口径（**按高度等比 + 水平居中**）：
//      `Scale = 1080/600 = 1.8`
//      · 水平：原版 `x ∈ [0,800]` → 屏幕 `960 + (x − 400) × 1.8`。本表所有 x 都写成
//        「相对屏幕中线的原版偏移 × 1.8」⇒ 乘出来天然水平居中（原版 ±400 → ±720，画布 ±960 之内，
//        左右各留 240 交给相机/背景，**不把 UI 横向拉伸**）。
//      · 垂直：原版 y（屏幕中线为原点、y 向上）× 1.8。
//   ⇒ 原版整屏 800×600 → **1440×1080**（高正好铺满，宽居中）。
//
//   坐标语义与原版 prefab 一致：anchor = 屏幕中心 (0.5,0.5)、**y 向上**、pivot = 中心 (0.5,0.5)
//   —— 原版少数节点（7 个职业热点）用**非中心 pivot**，本表已折算成**矩形中心**：
//        中心 = anchoredPosition + (0.5 - pivot) × size
//      每条的注释都写出了原版的 anchoredPosition / size / pivot 与折算结果，便于逐条复核。
//
// 「原版节点名 → 本工程用途」的全量清单见 `Table`（同时是**离线断言表**：
//   `uicheck` 逐条核对 `常量 == 原版值 × 1.8`，改错一个数就会红）。
//
// 本项目新增的元素（原版 prefab 里没有的：名字输入框 / 暂停菜单框 / 选项面板框 / 启动屏 / 选角角色行 …）
// 一律**按原版节奏推导**（原版按钮高 35 + 布局间距 10 ⇒ 行节奏 45）并在注释里写明推导过程；
// 这些条目 `FromOriginal = false`，不算"原版坐标"，但也同样走 ×1.8 口径、同样可断言。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>
    /// 参考表的一行：原版节点名 + 原版矩形 + 本工程常量（= 原版值 ×<see cref="UiLayoutFlow.Scale"/>）。
    /// </summary>
    internal sealed class FlowLayoutEntry
    {
        /// <summary>原版节点路径（prefab 名 / 节点名），回报与日志里的"依据节点名"就是它。</summary>
        public readonly string Node;

        /// <summary>本工程的用途（哪一块 UI 元素）。</summary>
        public readonly string Use;

        /// <summary>原版坐标（800×600 基准，屏幕中心为原点，y 向上）。</summary>
        public readonly Vector2 OrigPos;

        /// <summary>原版尺寸（800×600 基准像素）。</summary>
        public readonly Vector2 OrigSize;

        /// <summary>本工程坐标（1920×1080 基准 = <see cref="OrigPos"/> × 1.8）。</summary>
        public readonly Vector2 Pos;

        /// <summary>本工程尺寸（1920×1080 基准 = <see cref="OrigSize"/> × 1.8）。</summary>
        public readonly Vector2 Size;

        /// <summary>true = 原版 prefab 里有这个节点；false = 本项目新增（按原版节奏推导）。</summary>
        public readonly bool FromOriginal;

        /// <summary>归属屏（决定屏适配系数：<see cref="UiLayoutFlow.FitMenu"/> / <see cref="UiLayoutFlow.FitClass"/>）。</summary>
        public readonly string Panel;

        public FlowLayoutEntry(string node, string use, Vector2 origPos, Vector2 origSize,
            Vector2 pos, Vector2 size, bool fromOriginal, string panel)
        {
            Node = node;
            Use = use;
            OrigPos = origPos;
            OrigSize = origSize;
            Pos = pos;
            Size = size;
            FromOriginal = fromOriginal;
            Panel = panel;
        }

        /// <summary>坐标是否满足「本工程值 == 原版值 × 1.8」（容差 1e-3）。</summary>
        public bool PosOk
        {
            get { return (Pos - UiLayoutFlow.Px(OrigPos)).sqrMagnitude < 1e-6f; }
        }

        /// <summary>尺寸是否满足「本工程值 == 原版值 × 1.8」（容差 1e-3）。</summary>
        public bool SizeOk
        {
            get { return (Size - UiLayoutFlow.Px(OrigSize)).sqrMagnitude < 1e-6f; }
        }

        /// <summary>`[布局] 节点 原版(…) → 本工程(…)`（回报要贴的那张对照表的原始行）。</summary>
        public string Line
        {
            get
            {
                return string.Format(
                    "{0,-46} 原版 ({1,7:0.##},{2,7:0.##}) {3,6:0.##}×{4,5:0.##} → 本工程 ({5,8:0.##},{6,8:0.##}) {7,7:0.##}×{8,6:0.##}   [{9}]",
                    Node, OrigPos.x, OrigPos.y, OrigSize.x, OrigSize.y,
                    Pos.x, Pos.y, Size.x, Size.y, Use);
            }
        }
    }

    /// <summary>
    /// 流程面板的布局常量表（原版 800×600 → 本工程 1920×1080，逐元素 ×1.8 水平居中）。
    /// </summary>
    internal static class UiLayoutFlow
    {
        // ═════════════════════════════════════════════════════════════════════
        // 0. 基准与换算
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>原版界面基准宽（prefab 的 `m_ReferenceResolution.x` = 800）。</summary>
        public const float OrigWidth = 800f;

        /// <summary>原版界面基准高（prefab 的 `m_ReferenceResolution.y` = 600）。</summary>
        public const float OrigHeight = 600f;

        /// <summary>本工程 Canvas 参考分辨率宽（引擎固定 1920，`Runtime/Presentation/UI.cs:52-56`）。</summary>
        public const float RefWidth = 1920f;

        /// <summary>本工程 Canvas 参考分辨率高（引擎固定 1080）。</summary>
        public const float RefHeight = 1080f;

        /// <summary>
        /// 缩放系数 = 1080/600 = **1.8**（**按高度等比**；主 agent 2026 裁决）。
        /// <para>**不再用宽度比 2.4**：800×2.4 = 1920 ✓ 但 600×2.4 = **1440 ≠ 1080**
        /// （原版是 4:3，本工程参考画布是 16:9）⇒ HUD 控制面板底边会被放大到出屏 51px。
        /// 按高度 1.8 后：原版 600 ×1.8 = **1080**（纵向正好铺满），原版 800 ×1.8 = 1440（横向居中、
        /// 左右各留 240 交给相机/背景）。见 <see cref="UiLayoutGame.K"/> 的同一口径。</para>
        /// </summary>
        public const float Scale = 1.8f;

        /// <summary>
        /// 主菜单 / 暂停 / 设置 / 读条 / 启动 的**屏适配系数** = **1.0**（= 纯 ×1.8）。
        /// 依据：按高度 1.8 后原版整屏高 600×1.8 = 1080 = 画布高 ⇒ 原版 |y| ≤ 300 的元素全部落在 ±540 内。
        /// </summary>
        public const float FitMenu = 1f;

        /// <summary>
        /// 选角 / 创角 的**屏适配系数** = **1.0**（同样纯 ×1.8）。
        /// <para>旧口径这里是 0.75 = 1080/1440 —— 那是为了把「×2.4 后 1440 高的原版整屏」压进 1080。
        /// 改按高度 ×1.8 后原版整屏正好 1080 高，**不需要再压**：选角屏标题（原版 +267 → 480.6）、
        /// 底部按钮（原版 −250 → −450）都在 ±540 内（见 <see cref="BoundsOf"/> 的离线断言）。</para>
        /// </summary>
        public const float FitClass = 1f;

        /// <summary>参考画布的半宽/半高（屏适配后的"必须在画布内"判据）。</summary>
        public static readonly Vector2 RefHalf = new Vector2(RefWidth * 0.5f, RefHeight * 0.5f);

        /// <summary>原版像素 → 本工程 Canvas 单位（1 维）。</summary>
        public static float Px(float origPx) { return origPx * Scale; }

        /// <summary>原版像素 → 本工程 Canvas 单位（2 维）。</summary>
        public static Vector2 Px(Vector2 origPx) { return origPx * Scale; }

        /// <summary>本工程 Canvas 单位 → 原版像素（2 维）；给"要用原版 px 重新算比例"的地方。</summary>
        public static Vector2 Orig(Vector2 canvasUnits) { return canvasUnits / Scale; }

        /// <summary>本工程 Canvas 单位 → 原版像素（1 维）。</summary>
        public static float Orig(float canvasUnits) { return canvasUnits / Scale; }

        /// <summary>中文回退字号 = 位图字体档位 × 1.8（中文没有原版位图字形，见 `欠缺清单` #11）。</summary>
        public static int ChineseFontSize(D2Text.D2Font font)
        {
            switch (font)
            {
                case D2Text.D2Font.Font16: return 29;    // 16 原版px ×1.8 = 28.8 → 29
                case D2Text.D2Font.Font24: return 43;    // 24 原版px ×1.8 = 43.2 → 43
                case D2Text.D2Font.Font30: return 54;    // 30 原版px ×1.8 = 54
                case D2Text.D2Font.Font42: return 76;    // 42 原版px ×1.8 = 75.6 → 76
                default:
                    UiLog.WarnOnce("flow.font.unknown." + (int)font,
                        $"UiLayoutFlow.ChineseFontSize 未登记档位 {(int)font} ⇒ 退回 font16 口径（请补一行）");
                    return 29;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 1. 原版按钮（WideButton.prefab / MediumButton.prefab）
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>原版宽按钮 `WideButton.prefab` 的 RectTransform：272×35 → ×1.8 = **489.6×63**。</summary>
        public static readonly Vector2 WideButtonOrig = new Vector2(272f, 35f);

        /// <inheritdoc cref="WideButtonOrig"/>
        public static readonly Vector2 WideButton = Px(WideButtonOrig);

        /// <summary>原版中等按钮 `MediumButton.prefab` 的 RectTransform：128×35 → ×1.8 = **230.4×63**。</summary>
        public static readonly Vector2 MediumButtonOrig = new Vector2(128f, 35f);

        /// <inheritdoc cref="MediumButtonOrig"/>
        public static readonly Vector2 MediumButton = Px(MediumButtonOrig);

        /// <summary>
        /// 主菜单按钮列的行节奏：原版 `MainMenu/GameMenu/Buttons` 的
        /// `VerticalLayoutGroup.m_Spacing = 10` + 子按钮高 35 ⇒ 45 原版px → ×1.8 = **81**。
        /// </summary>
        public const float RowStep = 81f;

        /// <summary>行节奏的**原版值**（35 高 + 10 间距），逐条断言的另一半。</summary>
        public const float RowStepOrig = 45f;

        /// <summary>
        /// <para>
        /// **出处（两个操作数都是原版/素材自己声明的）**：
        /// ① 原版字号 = **18 原版 px** —— `Prefabs/Menu/WideButton.prefab:68` 与
        ///    `Prefabs/Menu/MediumButton.prefab:68` 的 `m_FontData.m_FontSize: 18`（`m_FontStyle: 1` = Bold）；
        ///    （`Assets/Editor/AssetImporter.cs:129` 的 `FontGridSpec("font16", 32, 8, 16, 18, 512, 16)`
        ///    —— 档位名 16 = 名义字号，格子 18 = 字形高 + 2px 打包间隙）。
        /// ⇒ 要表达「原版字号 18px」，就要把 font16 的字模按 **18/16** 放大：
        ///   字模格子 18 → **20.25 原版px**（画布 36.45），字面推进 115 → **129.4 原版px**。
        /// </para>
        /// <para>
        /// **实测核对（复跑口径）**：原版 prefab 引用的字体资产是
        /// `Assets/Resources/Fonts/Diablo_light.TTF`（guid `eca957d2…`），
        /// 用 `PIL.ImageFont.truetype(该 TTF, 18)` 渲染 `SINGLE PLAYER` 实测**字面 143×13 原版px**（推进 145）；
        /// 本工程 font16 同串实测**字面 114×11**（推进 115）⇒ 按 1.125 放大后 **128×12.4**（字高差 0.6px）。
        /// **残留（登记，不修）**：推进宽度 129.4 vs 145（−11%）—— 那是**字面差异**
        /// （原版位图字模 vs Diablerie 用的 TTF 替代字体），**不许改字距**：推进宽度来自原版 `.tbl`
        /// </para>
        /// <para>
        /// **只用在按钮上**：`WideButton.prefab` / `MediumButton.prefab` 是**唯一**声明了 `m_FontSize`
        /// 的两个原版菜单 prefab；`ClassSelectMenu.prefab` 的三个文本框（`SelectHeroClass` / `ClassName` /
        /// `ClassDescription`）都是 `m_FontSize: 0`（由运行时脚本给）⇒ 那些行仍按既有档位口径渲染，不改。
        /// </para>
        /// </summary>
        public const float ButtonFontScale = 18f / 16f;

        /// <summary>
        /// 按钮文字色 = <see cref="UiArt.ButtonText"/>（**全项目按钮 label 的唯一字色常量**）。
        /// <para>**废掉**了这里原先照抄的 `#191919`
        /// （原值出处 = 参考工程 `Prefabs/Menu/WideButton.prefab` 的 `Text.m_Color = 0.098,0.098,0.098`）。
        /// 那个前提是"**浅灰**石牌"，而本工程实际用的原版按钮底图是**深板岩灰**：
        /// `Menu/btn_med_normal.png` 内区实测 mean sRGB **0.376**（量法 `tools/probes/measure/btn_plate_luma.py`）
        /// ⇒ `#191919` 只有 **2.79:1**（WCAG 2.1 AA 正文门槛 4.5:1），13px 中文密笔画直接糊成一块黑
        /// （实机 before 图 `.ai-tmp/screenshots/uifix4_z_before_btn1.png`）；现值 **4.70:1** ✓。</para>
        /// <para>不再两套字色：本常量直接派生自 <see cref="UiArt.ButtonText"/>
        /// （"面板按钮"那条渲染路径 `UiArt.Button/SquareButton/OrigButton` 用的就是它）——
        /// 之前两条路径各写一个数、只差一个量级，正是本次缺陷的温床。</para>
        /// </summary>
        public static readonly Color ButtonText = UiArt.ButtonText;

        /// <summary>
        /// 禁用态文字色 = 同色降透明度（原版 prefab 没给禁用态字色，本项目按"可读但明显变灰"取）。
        /// <para>禁用态低于 4.5:1 是**有意**的，登记依据 = WCAG 2.1 §1.4.3
        /// （inactive user interface component 不设对比度要求），见 <see cref="UiArt.ButtonTextDisabled"/>。</para>
        /// </summary>
        public static readonly Color ButtonTextDisabled
            = new Color(UiArt.ButtonText.r, UiArt.ButtonText.g, UiArt.ButtonText.b, 0.45f);

        //   · `ArtDim`（"未选中压暗一档"）的唯一用处是旧的 `FlowButton.SetSelected`（职业按钮选中态的近似）
        //     ⇒ 已被原版三态 `NU1/NU2/NU3` 取代；
        //   · `ArtFullBright` 的用处同样只剩上面那一条。
        //   两者在 `UiArt` 里的**本体保留不动**：`UiArt.ArtFullBright` 是**在用**的

        // ═════════════════════════════════════════════════════════════════════
        // 2. 主菜单（依据 `Prefabs/Menu/MainMenu.prefab`）
        // ═════════════════════════════════════════════════════════════════════
        // 原版结构：`MainMenu/GameMenu`(Canvas, 基准 800×600)
        //            └ `Buttons`  anchor(0.5,0.5) anchoredPosition **(0,-100)** size (272,200)
        //                 VerticalLayoutGroup: childAlignment = UpperCenter(1), spacing = 10,
        //                 childForceExpandHeight = 0, childControlHeight = 0
        //               ├ SinglePlayerButton (272×35, 第 0 行)
        //               ├ MultiPlayerButton  (272×35, 第 1 行)
        //               ├ CinematicsButton   (272×35, 第 2 行)
        //               └ ExitButton         (272×35, 第 3 行)
        // 推导（列顶边 = 容器顶边 = -100 + 200/2 = 0 ⇒ **首行顶边正好在屏幕中线**）：
        //   第 i 行中心 y = -(35/2) - i × 45 = -17.5 - 45i；x = 0（UpperCenter ⇒ 水平居中）
        public static class Menu
        {
            /// <summary>`SinglePlayerButton` 中心 原版 (0,-17.5) → ×1.8 = **(0,-31.5)**。</summary>
            public static readonly Vector2 SinglePos = new Vector2(0f, -31.5f);

            /// <summary>
            /// `MultiPlayerButton` 中心 原版 (0,-62.5) → ×1.8 = **(0,-112.5)**。
            /// <para>**槽位坐标保留，按钮不再建** —— 用户原话「菜单中，中间那两个既然
            /// 没开发，就不要那个按钮了」⇒ `MainMenuPanel` 只建 `SINGLE PLAYER` / `EXIT`。
            /// 本常量**不许删**：① `uicheck` 的「主菜单 4 个原版槽位」断言（Program.cs:1523-1528）
            /// 就引用 `Menu.MultiPos`/`Menu.CinematicsPos`，删了宿主编译不过；
            /// ② 它是这条登记的**依据本身**（对照表里那一行还在）。</para>
            /// </summary>
            public static readonly Vector2 MultiPos = new Vector2(0f, -112.5f);

            /// <summary>
            /// `CinematicsButton` 中心 原版 (0,-107.5) → ×1.8 = **(0,-193.5)**。
            /// <para>本槽位上一版被占用来放「设置」（当时主菜单多一个 SETTINGS 项）——
            /// **原版这一槽是 `CINEMATICS`**，已改回。字段名从 `SettingsPos` 改成 `CinematicsPos`
            /// （旧名会让后来的人以为这槽是"设置"）。</para>
            /// <para>**槽位坐标保留，按钮不再建**（同 <see cref="MultiPos"/> 的理由）——
            /// 原版 4 段 CG 是 Bink `.bik`，本项目播不了，点它只能弹 Toast，用户点名删掉入口。</para>
            /// </summary>
            public static readonly Vector2 CinematicsPos = new Vector2(0f, -193.5f);

            /// <summary>`ExitButton` 中心 原版 (0,-152.5) → ×1.8 = **(0,-274.5)**。</summary>
            public static readonly Vector2 ExitPos = new Vector2(0f, -274.5f);

            //   （原版 4 项 = SINGLE PLAYER / MULTIPLAYER / CINEMATICS / EXIT）。
            //   「继续游戏」的功能没丢：原版就是 `SINGLE PLAYER` → 选角屏 → ENTER
            //   （本工程 `Events.Fsm.TriggerNewGame` → CharSelect；`Module/Flow/AppFlow.cs:1001-1011`
            //   还在"无存档 ⇒ 直接进创角"这一档上兜底）。同理「设置」挪到 ESC 暂停菜单的 `OPTIONS`
            //   （`UiLayoutFlow.Pause.OptionsPos` + `PausePanel` 的 OPTIONS 按钮 → 设置面板，与原版层级一致）。
        }

        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        //    `by clover-engine`，判据 = **实机截图里可读**（不是 grep 源码）。
        //  · 启动屏其实**早就有**这一行（`BootPanel.cs` 的 `ByLine`），但 1080p 实机图
        //    `Screenshots/p29_r2_01_boot.png` 里几乎看不见 —— 旧口径是 font16 档（29 画布px）+ 色
        //    字号 → font24 档（43 画布px）、色 → (0.78,0.76,0.72)、框高 20→30 原版px（×1.8 = 54）。
        //    并且**与启动屏共用下面这套常量**（同一张 1920×1080 画布：x=0 居中、y=-462）
        //  · 位置口径（自 2026 起就是"底部最下一行"）：中心 y=-462 ⇒ 底边 -462-27 = **-489**
        //    （画布底 -540 ⇒ 留白 51 ≥ 18），顶边 -435 低于版权行底边 -336 / 版本行底边 -385.5
        //    ⇒ 与它们**都不重叠**（由 `uicheck` 的两两不重叠断言逐条核）。
        public static class Brand
        {
            public const string ByLineText = "by clover-engine";

            /// <summary>署名行中心 = 画布 (0,-462)（= 原版推导值 <see cref="ByLineOrigPos"/> ×1.8）。</summary>
            public static readonly Vector2 ByLinePos = new Vector2(0f, -462f);

            /// <summary>署名行尺寸 = 画布 900×54（= 原版口径 <see cref="ByLineOrigSize"/> ×1.8）。</summary>
            public static readonly Vector2 ByLineSize = new Vector2(900f, 54f);

            /// <summary>
            /// 署名行的原版口径坐标（1920×1080 画布 / 1.8 = 800×600 基准）。
            /// <para>本项目新增元素（原版 prefab 里**没有**这东西）⇒ 这个"原版值"是**推导值**，
            /// 不是 prefab 实测值；它的唯一作用 = 与画布值互为 1.8 倍的断言锚点
            /// （见 <see cref="FlowLayoutEntry.PosOk"/>）。</para>
            /// </summary>
            public static readonly Vector2 ByLineOrigPos = new Vector2(0f, -256.6667f);

            /// <summary>
            /// 署名行的原版口径尺寸 500×30：行框高 **30 原版px** = font24 档字面 + 上下留白
            /// （旧口径 20 是配 font16 档的；字号提到 24 档后行框同步放大，避免文字顶到框外）。
            /// </summary>
            public static readonly Vector2 ByLineOrigSize = new Vector2(500f, 30f);

            /// <summary>
            /// 署名行颜色 = 低调灰但**比版权行亮一档**（版权/版本行仍是 0.62,0.60,0.56）
            /// </summary>
            public static readonly Color ByLineColor = new Color(0.78f, 0.76f, 0.72f, 1f);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 3. 选角 / 创角（依据 `Prefabs/Menu/ClassSelectMenu.prefab`）
        // ═════════════════════════════════════════════════════════════════════
        // 原版结构：`ClassSelectMenu/Canvas`(ScreenSpaceOverlay, 基准 800×600)，子节点（anchor 全是 0.5,0.5）：
        //   SelectHeroClass (Text)   (0,267)    493×30   alignment=UpperLeft  text="Select Hero Class"
        //   ClassName       (Text)   (0,198)    493×30   alignment=UpperLeft
        //   ClassDescription(Text)   (1,155)    300×50   alignment=MiddleCenter
        //   7 个职业热点（RawImage，原版**无贴图**，运行时由脚本喂图）：
        //     Amazon      (-299,-36) 90×200  pivot(0.5,0.4)   → 矩形中心 (-299,-16)
        //     Assassin    (-176,-63) 90×200  pivot(0.6,0.3)   → 矩形中心 (-185,-23)
        //     Necromancer (-99,-31)  90×200  pivot(0.5,0.4)   → 矩形中心 (-99,-11)
        //     Barbarian   (1,-30)    90×200  pivot(0.5,0.35)  → 矩形中心 (1,0)
        //     Paladin     (122,-38)  110×200 pivot(0.55,0.35) → 矩形中心 (116.5,-8)
        //     Sorceress   (226,-52)  85×200  pivot(0.55,0.35) → 矩形中心 (221.75,-22)
        //     Druid       (320,-69)  100×210 pivot(0.53,0.25) → 矩形中心 (317,-16.5)
        //   ExitButton (MediumButton) (-300,-250) 128×35 text="EXIT"
        //   OkButton   (MediumButton) (300,-250) 128×35 text="OK"
        public static class ClassMenu
        {
            /// <summary>`SelectHeroClass` 文本框 (0,267) 493×30 → ×1.8 = **(0,480.6) 887.4×54**。</summary>
            public static readonly Vector2 TitlePos = new Vector2(0f, 480.6f);

            /// <inheritdoc cref="TitlePos"/>
            public static readonly Vector2 TitleSize = new Vector2(887.4f, 54f);

            /// <summary>`ClassName` 文本框 (0,198) 493×30 → ×1.8 = **(0,356.4) 887.4×54**。</summary>
            public static readonly Vector2 NameRowPos = new Vector2(0f, 356.4f);

            /// <inheritdoc cref="NameRowPos"/>
            public static readonly Vector2 NameRowSize = new Vector2(887.4f, 54f);

            /// <summary>`ClassDescription` 文本框 (1,155) 300×50 → ×1.8 = **(1.8,279) 540×90**。</summary>
            public static readonly Vector2 DescPos = new Vector2(1.8f, 279f);

            /// <inheritdoc cref="DescPos"/>
            public static readonly Vector2 DescSize = new Vector2(540f, 90f);

            /// <summary>`ExitButton` (-300,-250) 128×35 → ×1.8 = **(-540,-450) 230.4×63**（本工程=「返回」）。</summary>
            public static readonly Vector2 ExitPos = new Vector2(-540f, -450f);

            /// <summary>`OkButton` (300,-250) 128×35 → ×1.8 = **(540,-450) 230.4×63**（本工程=「确定 / 进入」）。</summary>
            public static readonly Vector2 OkPos = new Vector2(540f, -450f);

            // ── 7 个职业热点（本工程用到其中 5 个：class_c 的 5 职业）─────────
            /// <summary>`Amazon` 热点 原版中心 (-299,-16) 90×200 → ×1.8 = **(-538.2,-28.8) 162×360**。</summary>
            public static readonly Vector2 AmazonPos = new Vector2(-538.2f, -28.8f);

            /// <summary>`Assassin` 热点 原版中心 (-185,-23) 90×200 → ×1.8 = (-333,-41.4) 162×360（本项目无该职业）。</summary>
            public static readonly Vector2 AssassinPos = new Vector2(-333f, -41.4f);

            /// <summary>`Necromancer` 热点 原版中心 (-99,-11) 90×200 → ×1.8 = **(-178.2,-19.8) 162×360**。</summary>
            public static readonly Vector2 NecromancerPos = new Vector2(-178.2f, -19.8f);

            /// <summary>`Barbarian` 热点 原版中心 (1,0) 90×200 → ×1.8 = **(1.8,0) 162×360**。</summary>
            public static readonly Vector2 BarbarianPos = new Vector2(1.8f, 0f);

            /// <summary>`Paladin` 热点 原版中心 (116.5,-8) 110×200 → ×1.8 = **(209.7,-14.4) 198×360**。</summary>
            public static readonly Vector2 PaladinPos = new Vector2(209.7f, -14.4f);

            /// <summary>`Sorceress` 热点 原版中心 (221.75,-22) 85×200 → ×1.8 = **(399.15,-39.6) 153×360**。</summary>
            public static readonly Vector2 SorceressPos = new Vector2(399.15f, -39.6f);

            /// <summary>`Druid` 热点 原版中心 (317,-16.5) 100×210 → ×1.8 = (570.6,-29.7) 180×378（本项目无该职业）。</summary>
            public static readonly Vector2 DruidPos = new Vector2(570.6f, -29.7f);

            /// <summary>热点矩形 90×200 → ×1.8 = **162×360**（Amazon/Assassin/Necromancer/Barbarian）。</summary>
            public static readonly Vector2 SpotSize = new Vector2(162f, 360f);

            /// <summary>热点矩形 110×200 → ×1.8 = **198×360**（Paladin）。</summary>
            public static readonly Vector2 SpotSizeWide = new Vector2(198f, 360f);

            /// <summary>热点矩形 85×200 → ×1.8 = **153×360**（Sorceress）。</summary>
            public static readonly Vector2 SpotSizeNarrow = new Vector2(153f, 360f);

            /// <summary>热点矩形 100×210 → ×1.8 = **180×378**（Druid）。</summary>
            public static readonly Vector2 SpotSizeTall = new Vector2(180f, 378f);

            //   素材就在 `data/global/ui/FrontEnd/{cls}/{CLS}{NU1,NU2,NU3}.DC6`（主 agent 亲手 dir 到），
            /// <summary>
            /// 一个职业半身像**某一态**的几何：原版 px 的矩形中心/帧尺寸 + ×1.8 的画布值。
            /// <para>
            /// **中心怎么来的（推导，不是估的）**：原版把半身像贴图放在 `RectTransform.position`
            /// （= prefab 的 `anchoredPosition`）上，而贴图自身的 pivot 由帧头偏移算出 ——
            /// 参考物 `Diablerie/Engine/IO/D2Formats/DC6.cs:197`：
            /// `pivot = (-offsetX/width, offsetY/height)`；`:198` 用该 pivot `Sprite.Create(...)`；
            /// `Game/UI/Menu/ClassSelect/ClassSelector.cs:237-245` 把对象位置设成 `RectTransform.position`。
            /// ⇒ 贴图左上角 = `anchoredPosition + (offsetX, -offsetY)`，
            ///    **矩形中心 = anchoredPosition + (offsetX + w/2, -offsetY + h/2)**。
            /// 本工程用**等价写法**：Image 走 `pivot(0.5,0.5)` + 中心 = 上式（逐像素等价）。
            /// 每条注释里的 `off=` 与 `帧=` 都是 `dc6.py` 实测值，可复核。
            /// </para>
            /// </summary>
            public sealed class PortraitState
            {
                /// <summary>原版坐标（800×600 基准，矩形中心）。</summary>
                public readonly Vector2 OrigPos;

                /// <summary>原版尺寸（= 该态 DC6 帧 0 的像素尺寸）。</summary>
                public readonly Vector2 OrigSize;

                /// <summary>本工程坐标（= <see cref="OrigPos"/> × 1.8）。</summary>
                public readonly Vector2 Pos;

                /// <summary>本工程尺寸（= <see cref="OrigSize"/> × 1.8）。</summary>
                public readonly Vector2 Size;

                /// <summary>依据（原版 DC6 相对路径 + 帧号）。</summary>
                public readonly string Node;

                /// <summary>用途（中文，进对照表与日志）。</summary>
                public readonly string Use;

                public PortraitState(string node, string use, Vector2 origPos, Vector2 origSize)
                {
                    Node = node;
                    Use = use;
                    OrigPos = origPos;
                    OrigSize = origSize;
                    Pos = Px(origPos);
                    Size = Px(origSize);
                }
            }

            /// <summary>职业半身像：按**原版屏上从左到右**的槽位序（与 <see cref="HotspotSlotName"/> 同序）。</summary>
            public static class Spot
            {
                // ① 亚马逊：热点 anchoredPosition (-299,-36)
                public static readonly PortraitState AmazonNu1 = new PortraitState(
                    "FrontEnd/amazon/AMNU1.DC6 帧 0", "亚马逊·默认(背面待机)",
                    new Vector2(-315f, -17f), new Vector2(118f, 198f));      // off=(-75,80)
                public static readonly PortraitState AmazonNu2 = new PortraitState(
                    "FrontEnd/amazon/AMNU2.DC6 帧 0", "亚马逊·悬停",
                    new Vector2(-315f, -17f), new Vector2(118f, 198f));      // off=(-75,80)
                public static readonly PortraitState AmazonNu3 = new PortraitState(
                    "FrontEnd/amazon/AMNU3.DC6 帧 0", "亚马逊·选中(转正面)",
                    new Vector2(-306.5f, -26f), new Vector2(121f, 234f));    // off=(-68,107)

                // ② 死灵法师：热点 anchoredPosition (-99,-31)
                public static readonly PortraitState NecromancerNu1 = new PortraitState(
                    "FrontEnd/necromancer/NENU1.DC6 帧 0", "死灵法师·默认(背面待机)",
                    new Vector2(-101f, -2.5f), new Vector2(58f, 183f));      // off=(-31,63)
                public static readonly PortraitState NecromancerNu2 = new PortraitState(
                    "FrontEnd/necromancer/NENU2.DC6 帧 0", "死灵法师·悬停",
                    new Vector2(-101f, -2.5f), new Vector2(58f, 183f));      // off=(-31,63)
                public static readonly PortraitState NecromancerNu3 = new PortraitState(
                    "FrontEnd/necromancer/NENU3.DC6 帧 0", "死灵法师·选中(转正面)",
                    new Vector2(-85.5f, -16.5f), new Vector2(129f, 233f));   // off=(-51,102)

                // ③ 野蛮人：热点 anchoredPosition (1,-30)
                public static readonly PortraitState BarbarianNu1 = new PortraitState(
                    "FrontEnd/barbarian/banu1.DC6 帧 0", "野蛮人·默认(背面待机)",
                    new Vector2(5f, 10.5f), new Vector2(86f, 183f));         // off=(-39,51)
                public static readonly PortraitState BarbarianNu2 = new PortraitState(
                    "FrontEnd/barbarian/banu2.DC6 帧 0", "野蛮人·悬停",
                    new Vector2(5f, 10.5f), new Vector2(86f, 183f));         // off=(-39,51)
                public static readonly PortraitState BarbarianNu3 = new PortraitState(
                    "FrontEnd/barbarian/banu3.DC6 帧 0", "野蛮人·选中(转正面)",
                    new Vector2(-1.5f, 3.5f), new Vector2(95f, 201f));       // off=(-50,67)

                // ④ 圣骑士：热点 anchoredPosition (122,-38)
                public static readonly PortraitState PaladinNu1 = new PortraitState(
                    "FrontEnd/paladin/PANU1.DC6 帧 0", "圣骑士·默认(背面待机)",
                    new Vector2(128f, -0.5f), new Vector2(82f, 179f));       // off=(-35,52)
                public static readonly PortraitState PaladinNu2 = new PortraitState(
                    "FrontEnd/paladin/PANU2.DC6 帧 0", "圣骑士·悬停",
                    new Vector2(128f, -0.5f), new Vector2(82f, 179f));       // off=(-35,52)
                public static readonly PortraitState PaladinNu3 = new PortraitState(
                    "FrontEnd/paladin/PANU3.DC6 帧 0", "圣骑士·选中(转正面)",
                    new Vector2(70.5f, -49.5f), new Vector2(131f, 173f));    // off=(-117,98)

                // ⑤ 法师：热点 anchoredPosition (226,-52)
                public static readonly PortraitState SorceressNu1 = new PortraitState(
                    "FrontEnd/sorceress/SONU1.DC6 帧 0", "法师·默认(背面待机)",
                    new Vector2(209.5f, -22f), new Vector2(89f, 166f));      // off=(-61,53)
                public static readonly PortraitState SorceressNu2 = new PortraitState(
                    "FrontEnd/sorceress/SONU2.DC6 帧 0", "法师·悬停",
                    new Vector2(209.5f, -22f), new Vector2(89f, 166f));      // off=(-61,53)
                public static readonly PortraitState SorceressNu3 = new PortraitState(
                    "FrontEnd/sorceress/SONU3.DC6 帧 0", "法师·选中(转正面)",
                    new Vector2(227.5f, -21.5f), new Vector2(107f, 209f));   // off=(-52,74)

                /// <summary>槽位 × 状态的二维表（**行 = 槽位**，列 = <see cref="ResPaths.Portrait"/> 的 0/1/2）。</summary>
                private static readonly PortraitState[][] Slots =
                {
                    new[] { AmazonNu1, AmazonNu2, AmazonNu3 },
                    new[] { NecromancerNu1, NecromancerNu2, NecromancerNu3 },
                    new[] { BarbarianNu1, BarbarianNu2, BarbarianNu3 },
                    new[] { PaladinNu1, PaladinNu2, PaladinNu3 },
                    new[] { SorceressNu1, SorceressNu2, SorceressNu3 },
                };

                /// <summary>槽位数 = 本项目有的 5 个职业（原版屏上那 7 个里的 5 个）。</summary>
                public static int SlotCount { get { return Slots.Length; } }

                /// <summary>槽位 → 状态几何；越界 ⇒ Warn + 返回 null（不静默拿错格子）。</summary>
                public static PortraitState Of(int slot, int state)
                {
                    if (slot < 0 || slot >= Slots.Length)
                    {
                        UiLog.WarnOnce("flow.spot.bad_slot." + slot,
                            $"UiLayoutFlow.ClassMenu.Spot.Of 槽位越界 {slot}（应 0..{Slots.Length - 1}）⇒ 返回 null");
                        return null;
                    }
                    if (state < 0 || state > 2)
                    {
                        UiLog.WarnOnce("flow.spot.bad_state." + state,
                            $"UiLayoutFlow.ClassMenu.Spot.Of 状态越界 {state}（应 0/1/2）⇒ 返回 null");
                        return null;
                    }
                    return Slots[slot][state];
                }

                /// <summary>槽位 → 原版热点名（日志/对照表用；顺序同 <see cref="Of"/> 的行序）。</summary>
                public static string NameOf(int slot)
                {
                    switch (slot)
                    {
                        case 0: return "Amazon";
                        case 1: return "Necromancer";
                        case 2: return "Barbarian";
                        case 3: return "Paladin";
                        case 4: return "Sorceress";
                        default:
                            UiLog.WarnOnce("flow.spot.bad_name." + slot, $"Spot.NameOf 槽位越界 {slot}");
                            return "?";
                    }
                }

                /// <summary>槽位 → 原版热点矩形中心（本工程坐标）= 透明点击区的中心。</summary>
                public static Vector2 HotspotPosOf(int slot)
                {
                    switch (slot)
                    {
                        case 0: return AmazonPos;
                        case 1: return NecromancerPos;
                        case 2: return BarbarianPos;
                        case 3: return PaladinPos;
                        case 4: return SorceressPos;
                        default: return Vector2.zero;
                    }
                }

                /// <summary>槽位 → 原版热点矩形尺寸（本工程坐标）。</summary>
                public static Vector2 HotspotSizeOf(int slot)
                {
                    switch (slot)
                    {
                        case 3: return SpotSizeWide;    // Paladin 110×200
                        case 4: return SpotSizeNarrow;  // Sorceress 85×200
                        default: return SpotSize;       // 其余 90×200
                    }
                }
            }

            // ═════════════════════════════════════════════════════════════════════
            // R1-C · 转身过渡（原版 `{CLS}FW` / `{CLS}BW`）的**逐帧矩形**
            // ═════════════════════════════════════════════════════════════════════
            //
            //   **全塞进"帧 0 那张画框"**（`CharCreatePanel.ShowTransitionFrame` 当时只换 sprite、
            //   不动矩形）。而导出 PNG 的 IHDR 实测（可复跑，见生成器）：
            //     Amazon  FW：`fw_0` = **118×198** → `fw_21` = **215×228** → `fw_53` = **121×234**
            //     Barbarian FW：`fw_0` = **86×183** → `fw_20` = **147×204** → `fw_63` = **95×201**
            //   ⇒ 215×228 的帧被压进 118×198 的画框 = **横向压掉 45%**、纵向再拉 13%
            //   —— 画面上就是"人物被拍扁 + 忽胖忽瘦"的"变形/诡异"。
            //   ⇒ 本表给出**逐帧尺寸**，面板每帧同步 `sizeDelta` / `anchoredPosition`
            //
            // **尺寸的出处**（唯一）：工程内实际落位的 `Resources/Clover/D2/UI/FrontEnd/{cls}/{code}_{i}.png`
            //   的 IHDR 宽高 —— 这批 PNG 由 `tools/d2codec/export_d2ui.py --only frontend` 逐帧 1:1 解自
            //   原版 DC6（与 `dc6.py png` 的产出逐字节同名同内容）。
            //   生成 = `python tools/probes/gen_portrait_frame_table.py`（幂等，只改下面两个标记之间）。
            //   复核 = `uicheck`「每一过渡帧的原生宽高比 == 该帧矩形宽高比」逐帧回读 PNG 比对（0 例外才算过）。
            //
            // **位置的出处**（本工程唯一能拿到的口径，缺口如实登记）：
            //   原版每一帧的 `DC6 offset` 决定该帧贴在锚点上的哪一处，但**原版 DC6 不在本机**
            //   （`原版资源/` 未下载 ⇒ `dc6.py info` 跑不了），拿不到逐帧 offset。
            //   ⇒ 用**两端真实几何 + 中间线性过渡**：
            //     · 起点锚点 = 起始状态的矩形**底边中点**（fw: `NU1` 背面待机；bw: `NU3` 正面待机）；
            //     · 终点锚点 = 结束状态的矩形**底边中点**（fw: `NU3`；bw: `NU1`）——
            //       这两个矩形是**原版 prefab 热点 + 该态 DC6 offset 推出来的真值**（见 `PortraitState` 注释）；
            //     · 帧 `f` 的锚点 = 两端锚点在 `t = f/(帧数-1)` 上的线性插值，帧高按该帧原生尺寸给。
            //   为什么按**底边中点**而不是矩形中心：帧是**紧贴内容的包围盒**（实测 `bbox == 整幅`），
            //     过渡中角色挥臂会让包围盒忽宽忽窄 ⇒ 按中心对齐会让**身体左右乱窜**；
            //     按底边（脚 / 地面接触点）对齐则肢体伸展不动身体，只有身高/体型在变（与原版观感一致）。
            //   为什么两端要插值而不是用固定锚点：`NU1` 与 `NU3` 的真实底边相差 27 原版px（Amazon），
            //     固定锚点会让过渡**播完的瞬间跳一下**（用户能看见的另一种"诡异"）；
            //     插值后 **fw 首帧 = `NU1` 矩形、fw 末帧 = `NU3` 矩形**（逐像素一致，可断言）⇒ 两头无缝。
            //   **允许的差异（登记，等原版 DC6 到位后消除）**：中途帧的绝对位置是**插值**，
            //     不是原版逐帧 offset；消除条件 = `原版资源/` 到位后跑 `dc6.py info` 取到逐帧 offset。
            public static class Transition
            {
                /// <summary>本项目**有过渡素材**的槽位（0 = Amazon / 2 = Barbarian；与 `Spot` 同序）。</summary>
                /// <remarks>其余三个槽（Necromancer / Paladin / Sorceress）的素材按用户 2026-09-19 决策已删，
                /// 这三个槽**没有**过渡序列 ⇒ <see cref="Of"/> 返回 null（面板据此退回"不播过渡、直接落终态"）。</remarks>
                public static readonly int[] SlotIds = { 0, 2 };

                /// <summary>两段过渡的码（`fw` = 转到正面 / `bw` = 转回背面），与 <see cref="ResPaths.Portrait"/> 同源。</summary>
                public static readonly string[] Codes =
                {
                    ResPaths.Portrait.TransitionFront,
                    ResPaths.Portrait.TransitionBack,
                };

                // 标记区（由 tools/probes/gen_portrait_frame_table.py 重写；别手改下面的数字）
                // >>> R1-C portrait transition frame table (generated) >>>
                // **本节由 `tools/probes/gen_portrait_frame_table.py` 生成，不许手改**：
                //   数值 = 工程内导出 PNG（`D2/UI/FrontEnd/{cls}/{code}_{i}.png`）的 IHDR 实测宽高。
                //   复算 = `python tools/probes/gen_portrait_frame_table.py`；
                //   复核 = `uicheck`「每一过渡帧的原生宽高比 == 该帧矩形宽高比」那条断言（逐帧回读 PNG）。
                //   帧序 = 导出器的 DC6 帧号（0 起）。每行 6 帧（= 6×2 个数）。

                /// <summary>`amazon/fw` 54 帧的（宽,高）原版像素（每帧两个数）。</summary>
                private static readonly int[] AmazonFw =
                {
                    118,198, 117,198, 115,198, 112,199, 107,199, 103,200,
                    98,199, 93,199, 94,199, 104,202, 118,204, 132,206,
                    154,208, 169,211, 175,212, 178,214, 180,215, 184,216,
                    191,216, 203,220, 214,225, 215,228, 204,230, 177,231,
                    138,231, 130,230, 130,228, 130,227, 128,225, 128,224,
                    130,230, 135,245, 143,255, 158,255, 176,242, 181,227,
                    172,225, 171,225, 212,225, 207,226, 199,227, 188,228,
                    168,230, 143,234, 122,238, 126,240, 127,239, 128,236,
                    127,235, 125,236, 122,235, 122,234, 122,234, 121,234,
                };

                /// <summary>`amazon/bw` 30 帧的（宽,高）原版像素（每帧两个数）。</summary>
                private static readonly int[] AmazonBw =
                {
                    121,233, 120,232, 118,229, 113,226, 107,224, 101,224,
                    94,223, 88,221, 83,219, 78,217, 78,213, 79,207,
                    80,207, 80,205, 82,204, 85,203, 88,202, 92,202,
                    95,200, 99,201, 103,200, 106,199, 109,199, 112,198,
                    114,197, 116,197, 116,197, 115,196, 115,197, 115,198,
                };

                /// <summary>`barbarian/fw` 64 帧的（宽,高）原版像素（每帧两个数）。</summary>
                private static readonly int[] BarbarianFw =
                {
                    86,183, 87,183, 88,183, 90,183, 92,184, 95,185,
                    100,185, 106,188, 114,190, 120,189, 124,189, 126,189,
                    125,190, 124,193, 121,194, 119,195, 119,196, 118,198,
                    120,198, 137,201, 147,204, 139,215, 138,220, 140,216,
                    141,214, 140,214, 140,213, 140,213, 140,213, 141,213,
                    140,213, 139,213, 140,212, 141,212, 144,212, 141,211,
                    137,210, 138,210, 149,210, 130,211, 135,210, 128,209,
                    122,208, 122,208, 122,207, 123,207, 123,207, 122,207,
                    121,207, 120,207, 118,207, 116,207, 110,206, 106,203,
                    111,199, 113,197, 112,197, 92,197, 91,197, 91,199,
                    92,199, 93,201, 93,201, 95,201,
                };

                /// <summary>`barbarian/bw` 19 帧的（宽,高）原版像素（每帧两个数）。</summary>
                private static readonly int[] BarbarianBw =
                {
                    95,201, 93,200, 93,198, 92,197, 93,195, 95,195,
                    92,192, 102,188, 110,186, 111,185, 108,182, 99,178,
                    90,178, 88,179, 90,179, 89,180, 85,181, 84,182,
                    85,183,
                };
                // <<< R1-C portrait transition frame table <<<

                /// <summary>该槽位有没有过渡素材（= 尺寸表非空）。</summary>
                public static bool Has(int slot) { return SizesOf(slot, ResPaths.Portrait.TransitionFront) != null; }

                /// <summary>该槽位该段过渡的帧数（= 导出 PNG 的张数）；没有该序列 ⇒ 0。</summary>
                public static int FrameCount(int slot, string code)
                {
                    var sizes = SizesOf(slot, code);
                    return sizes == null ? 0 : sizes.Length / 2;
                }

                /// <summary>
                /// 取「槽位 <paramref name="slot"/> 的 <paramref name="code"/> 序列第 <paramref name="frame"/> 帧」的
                /// 矩形（原版 px + 画布值；尺寸 = 该帧原生尺寸，位置 = 底边中点落在两端锚点的线性插值上）。
                /// <para>槽位/序列没有登记 ⇒ **WarnOnce + 返回 null**（调用方退回"只换图不改矩形"，不静默拿错格子）。</para>
                /// <para>帧号越界 ⇒ 钳到 `[0, 帧数-1]` 并 WarnOnce（多播一帧不该让画面跳到别的序列）。</para>
                /// </summary>
                public static PortraitState Of(int slot, string code, int frame)
                {
                    var sizes = SizesOf(slot, code);
                    if (sizes == null) return null;

                    var count = sizes.Length / 2;
                    var f = Mathf.Clamp(frame, 0, count - 1);
                    if (f != frame)
                    {
                        UiLog.WarnOnce("flow.transition.bad_frame." + slot + "." + code,
                            $"UiLayoutFlow.ClassMenu.Transition.Of 帧号越界 {frame}（{Spot.NameOf(slot)}/{code} 共 {count} 帧）" +
                            $"⇒ 钳到 {f}（只报一次）");
                    }

                    var w = (float)sizes[f * 2];
                    var h = (float)sizes[f * 2 + 1];

                    // t = 0 ⇒ 起始态矩形；t = 1 ⇒ 结束态矩形（两端逐像素一致，见类注释）
                    var t = count > 1 ? (float)f / (count - 1) : 0f;
                    var start = BottomCenterOf(StateAt(slot, code, true));
                    var end = BottomCenterOf(StateAt(slot, code, false));
                    var anchor = Vector2.Lerp(start, end, t);
                    var origPos = new Vector2(anchor.x, anchor.y + h * 0.5f);

                    return new PortraitState(
                        SourceOf(slot, code, f),
                        $"{Spot.NameOf(slot)}·转身过渡 {code} 帧 {f}/{count}",
                        origPos, new Vector2(w, h));
                }

                /// <summary>矩形**底边中点**（原版 px）= 该序列用来对齐的"脚 / 地面接触点"。</summary>
                private static Vector2 BottomCenterOf(PortraitState ps)
                    => ps == null ? Vector2.zero : new Vector2(ps.OrigPos.x, ps.OrigPos.y - ps.OrigSize.y * 0.5f);

                /// <summary>
                /// 该过渡的**起始 / 结束状态**矩形（原版三态之一）：
                /// `fw`(转到正面) = `NU1` 背面待机 → `NU3` 正面待机；`bw`(转回背面) = `NU3` → `NU1`。
                /// 口径出处：参考物 `ClassSelector.cs:53-76 / 207-233`（见 <see cref="ResPaths.Portrait.TransitionFront"/>）。
                /// </summary>
                private static PortraitState StateAt(int slot, string code, bool start)
                {
                    var toFront = code == ResPaths.Portrait.TransitionFront;
                    var idle = toFront == start;                     // true ⇒ 这一端是 `NU1`（背面待机）
                    return Spot.Of(slot, idle ? ResPaths.Portrait.Idle : ResPaths.Portrait.Front);
                }

                /// <summary>槽位 + 序列 → 尺寸表（没有登记 ⇒ null + WarnOnce 点名）。</summary>
                private static int[] SizesOf(int slot, string code)
                {
                    var forward = code == ResPaths.Portrait.TransitionFront;
                    var backward = code == ResPaths.Portrait.TransitionBack;

                    if (slot == 0)                                        // Amazon（`Spot.NameOf(0)`）
                    {
                        if (forward) return AmazonFw;
                        if (backward) return AmazonBw;
                    }
                    else if (slot == 2)                                   // Barbarian（`Spot.NameOf(2)`）
                    {
                        if (forward) return BarbarianFw;
                        if (backward) return BarbarianBw;
                    }

                    UiLog.WarnOnce("flow.transition.none." + slot + "." + code,
                        $"UiLayoutFlow.ClassMenu.Transition 没有「槽位 {slot}（{Spot.NameOf(slot)}）/ 过渡 {code}」的尺寸表" +
                        "（本项目只做 Amazon + Barbarian 两职业的过渡素材；其余职业按用户决策已删）" +
                        "⇒ 本次不播过渡、直接落终态（只报一次）");
                    return null;
                }

                /// <summary>依据（原版 DC6 相对路径 + 帧号），进对照表/日志用。</summary>
                private static string SourceOf(int slot, string code, int frame)
                    => string.Format("FrontEnd/{0}/{1}_{2}.png（原版 {0} 的 {1} 序列第 {2} 帧，逐帧落位见 export_d2ui.py）",
                        slot == 0 ? "amazon" : "barbarian", code, frame);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 4. 本项目新增的行内元素（名字 / 属性 / 预览）
        // ═════════════════════════════════════════════════════════════════════
        // 推导口径：**只用原版的节奏与既有元素边界**，不凭空造数：
        //   · 行节奏 = 原版 35（按钮高）+ 10（Buttons 的 spacing）= 45 原版px → 81
        //   · 底部按钮行 = 原版 `ExitButton`/`OkButton` 的 y = -250（精确值）
        //   · 于是往上一行 = -250+45 = -205、再上一行 = -250+90 = -160
        //   · 名字行 = 复用原版 `ClassName` 行（原版 (0,198)，宽 493 的文本框）
        //   · 「属性点分配」四行（`StatRowY` + 4 组「标签 / − / 值 / +」，
        //     旧的一整族常量 `StatGroupW/Step/FirstX`、`StatLabelDx`、`StatMinusDx`、`StatValueDx`、
        //     `StatPlusDx`、`StatLabelSize`、`StatValueSize`、`StatButtonSize` 一并删除）；
        //   · 「剩余属性点」(`PointsPos/PointsSize`) —— 原版创角没有可分配点数；
        //   · 「生命/法力/耐力预览行」(`PreviewRowY`) —— 它整行都是属性的派生量，属性分配没了 ⇒ 它也没了。
        //   依据：原版创角屏 = **职业（半身像横排）+ 名字 + 难度**；属性点原版是**升级后**在
        //   `C`（人物属性）面板里加的（本工程照此：`CharacterSave.statPoints = 0`，
        //   每级 +5 由 `Module/Player/PlayerStats.cs` 在升级时发放）。
        public static class New
        {
            /// <summary>名字输入框：贴在原版 `ClassName` 行上 (0,198)，本工程 462×35 原版px → ×1.8 = **(-27,356.4) 831.6×63**。</summary>
            public static readonly Vector2 NameInputPos = new Vector2(-27f, 356.4f);

            /// <inheritdoc cref="NameInputPos"/>
            public static readonly Vector2 NameInputSize = new Vector2(831.6f, 63f);

            /// <summary>「NAME:」标签：原版 ClassName 框左侧 (-330,198) 140×35 → ×1.8 = **(-594,356.4) 252×63**。</summary>
            public static readonly Vector2 NameLabelPos = new Vector2(-594f, 356.4f);

            /// <inheritdoc cref="NameLabelPos"/>
            public static readonly Vector2 NameLabelSize = new Vector2(252f, 63f);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 4b. 职业说明文案（原版创角屏 `ClassDescription` 行显示的那句话）
        // ═════════════════════════════════════════════════════════════════════
        //  出处（**参考物自带的常量表**，不是本项目自写）：社区复刻工程 mofr/Diablerie 的
        //  `Assets/Scripts/Diablerie/Game/UI/Menu/ClassSelect/ClassSelectInfo.cs:36-136`
        //  逐职业 `Description` 字段（原版英文），由 `ClassSelector.OnEnter/OnClick` 喂给
        //  `ClassSelectMenu.classDescriptionText`（见 `ClassSelectMenu.cs:65-99`）。
        //  本项目两处都用它：创角屏（悬停/选中某职业时显示）+ 选角屏（高亮某角色时显示其职业说明）。
        //  不许改成中文自写 —— 原版这一行就是这句英文（走原版位图字体）。
        /// <summary>职业说明文案（英文，出处 = 参考工程 `ClassSelectInfo.cs`）。</summary>
        public static class ClassText
        {
            /// <summary>取某职业的说明；未登记 ⇒ 空串 + Warn（不静默给别的职业的话）。</summary>
            /// <remarks>
            /// 而两个调用点给的写法不同 —— `CharCreatePanel` 传 `PlayerClass.ToString()` = `Amazon`，
            /// `CharSelectPanel` 传的是**展示用**的大写 `AMAZON` ⇒ **选角屏每次都查不到**、说明行恒为空
            /// （实测：`[Warn] [Ui] UiLayoutFlow.ClassText 未登记职业「AMAZON」的说明 ⇒ 该行留空`，
            /// </remarks>
            public static string Description(string classLatinName)
            {
                var key = classLatinName == null ? string.Empty : classLatinName.Trim().ToLowerInvariant();
                switch (key)
                {
                    case "amazon":
                        return "Skilled with the spear and the bow, she is a very versatile fighter.";
                    case "sorceress":
                        return "She has mastered the elemental magicks - fire, lightning, and ice.";
                    case "necromancer":
                        return "Summoning undead minions and cursing his enemies are his specialities.";
                    case "paladin":
                        return "He is a natural party leader, holy man, and blessed warrior.";
                    case "barbarian":
                        return "He is unequaled in close-quarters combat and mastery of weapons.";
                    default:
                        UiLog.WarnOnce("flow.classtext.unknown." + key,
                            $"UiLayoutFlow.ClassText 未登记职业「{classLatinName}」的说明 ⇒ 该行留空" +
                            "（原版这 5 句在参考工程 ClassSelectInfo.cs 里，缺的要么补表要么是拼错的职业名）");
                        return string.Empty;
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 5. 选角屏的角色行（本项目新增，落在原版元素的边界之间）
        // ═════════════════════════════════════════════════════════════════════
        // 角色列表容器 = 原版元素围出来的"自由带"：
        //   左/右 = 原版 7 个热点的并集（Amazon 左 -344 / Druid 右 370）
        //   上    = 原版 `ClassDescription` 底边 130 下留 20 → 110
        //   下    = 原版 `OkButton` 顶边 -232.5 上留 20 → -212.5
        //   ⇒ 原版 (-344..370, -212.5..110)：size 714×322.5，中心 (13,-51.25)
        //   行节奏 45 原版px ⇒ 一屏 7 行（7×45 = 315 ≤ 322.5）
        public static class Select
        {
            /// <summary>角色列表容器 原版中心 (13,-51.25) → ×1.8 = **(23.4,-92.25)**。</summary>
            public static readonly Vector2 ListPos = new Vector2(23.4f, -92.25f);

            /// <summary>角色列表容器 原版 714×322.5 → ×1.8 = **1285.2×580.5**。</summary>
            public static readonly Vector2 ListSize = new Vector2(1285.2f, 580.5f);

            /// <summary>一屏最多行数 = 322.5 / 45 = **7**（原版节奏）。</summary>
            public const int MaxRows = 7;

            /// <summary>
            /// 角色行高（画布单位）= 原版中等按钮高 **35** ×1.8 = **63**。
            /// <para> 本片从 `CharSelectPanel.Rebuild` 里**搬进来**的唯一来源：
            /// （数值相同，但"行高"这只在 UI 侧算、进不了对照表 ⇒ 拿不到离线断言的覆盖）。</para>
            /// </summary>
            public static readonly float RowH = Px(MediumButtonOrig.y);

            /// <summary>
            /// 角色**行矩形**尺寸（画布）= 容器宽 × 行高（<see cref="RowH"/>）。
            /// <para>行节点、行热点、行内标签/按钮都以它为"行画框"（`CharSelectPanel.BuildRow`），
            /// 对照表登记角色行时也用它 ⇒ 三处同源。</para>
            /// </summary>
            public static readonly Vector2 RowSize = new Vector2(ListSize.x, RowH);

            /// <summary>
            /// 第 <paramref name="row"/> 行的**行中心 y（相对容器中心）**，画布单位。
            /// <para>推导（与原版节奏一致，逐项给出处）：首行**顶边 = 容器顶边内侧**
            /// ⇒ `首行中心 = (容器高 − 行高)/2`；行步进 = <see cref="UiLayoutFlow.RowStep"/>
            /// （= 原版 35 + spacing 10 = 45 原版px ×1.8 = 81）。</para>
            /// <para> 本片把这条公式**收进常量表**：`CharSelectPanel` 与对照表（<see cref="UiLayoutFlow.Table"/>）
            /// 的**角色行登记**都调它 ⇒ 面板画在哪、表里断言的就是哪，不可能各算一套（两张表漂移）。</para>
            /// </summary>
            public static float RowY(int row) => (ListSize.y - RowH) * 0.5f - row * RowStep;

            /// <summary>行内「角色名」中心 原版 -250 → ×1.8 = **-450**（190×35 → 342×63）。</summary>
            public static readonly Vector2 RowNamePos = new Vector2(-450f, 0f);

            /// <inheritdoc cref="RowNamePos"/>
            public static readonly Vector2 RowNameSize = new Vector2(342f, 63f);

            /// <summary>行内「职业」中心 原版 -95 → ×1.8 = **-171**（110×35 → 198×63）。</summary>
            public static readonly Vector2 RowClassPos = new Vector2(-171f, 0f);

            /// <inheritdoc cref="RowClassPos"/>
            public static readonly Vector2 RowClassSize = new Vector2(198f, 63f);

            /// <summary>行内「等级」中心 原版 +25 → ×1.8 = **45**（110×35 → 198×63）。</summary>
            public static readonly Vector2 RowLevelPos = new Vector2(45f, 0f);

            /// <inheritdoc cref="RowLevelPos"/>
            public static readonly Vector2 RowLevelSize = new Vector2(198f, 63f);

            /// <summary>行内「进入」按钮中心 原版 +145 → ×1.8 = **261**（用原版中等按钮 128×35）。</summary>
            public static readonly Vector2 RowEnterPos = new Vector2(261f, 0f);

            /// <summary>行内「删除」按钮中心 原版 +290 → ×1.8 = **522**（用原版中等按钮 128×35）。</summary>
            public static readonly Vector2 RowDeletePos = new Vector2(522f, 0f);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 5b. w5 新增：**原版 `MENU/boxpieces.DC6` 拼装窗框**的量法（选项 / 暂停底板）
        // ═════════════════════════════════════════════════════════════════════
        //    原版那套窗框素材（22 帧 14×15）一直躺在磁盘上却零引用，缺的只是「22 帧怎么摆」。
        //    DC6 本机不在（`原版资源/` 被 .gitignore 排除 ⇒ 帧 offset 表拿不到），
        //    故按**像素自证**把偏移反推出来 —— 推导、逐像素判据、复跑命令全部落在
        //    `tools/d2codec/assemble_boxpieces.py` 的文件头里（整幅窗框 PNG 由它生成）。
        //  本组常量**一个都不是拍出来的**：全部是那 22 张 PNG 上量出来的（见下逐条出处），
        //    并由 `uicheck` ㉑ 节 `BoxFrameSide()` **重新解像素**核对（改错必红）。
        //  变体怎么取也有交代：上/下/左/右各有 6/6/3/3 个变体，**只差石纹与金色饰点**，
        //    「哪个变体放哪一格」在原版里没有出处 ⇒ 一律取该族**第一个**变体
        //    （上 2 / 下 16 / 左 10 / 右 13），其余 14 帧登记为未使用 —— 这不是"挑了一版好看的"，
        //    是"不挑"（不引入任何无出处的排布）。
        /// <summary>原版 `boxpieces` 拼装窗框：网格 pitch / 框厚 / 8 个用到的帧号 / 各族偏移。</summary>
        public static class BoxFrame
        {
            /// <summary>单元格 pitch = **12 原版px**（横竖同值）。出处：每帧实测 14×15、内容恰为
            /// 内部 12×12，四周页边 = 左 1 / 上 1 / 右 1 / 下 2（x0/x13/y0/y13/y14 全透明）。</summary>
            public const int TilePitch = 12;

            /// <summary>每帧像素尺寸（22 帧的 IHDR 逐张实测都是它）。</summary>
            public const int FramePixelW = 14;

            /// <inheritdoc cref="FramePixelW"/>
            public const int FramePixelH = 15;

            /// <summary>窗框**外沿 → 空腔**的厚度 = **4 原版px**（边带 3px：2px 石色 + 1px 近黑内线，
            /// 再加 1px 页边）。拼好的整幅上实测：第 0..2 行/列不透明、第 4 行/列起是空腔。</summary>
            public const int BorderThickness = 4;

            /// <summary>窗框外沿两侧都要让出的总量（= 2×<see cref="BorderThickness"/>），
            /// 用于「内容外接框 → 窗框外沿」的推导。</summary>
            public static readonly Vector2 ThicknessEdges =
                new Vector2(BorderThickness * 2f, BorderThickness * 2f);

            // ── 用到的 8 个帧（`D2/UI/Menu/boxpieces_{帧号}.png`）────────────────
            /// <summary>左上角块（内容 12×12，边带压在 x1..3 / y1..3）。</summary>
            public const int TileTopLeft = 0;

            /// <summary>右上角块（边带压在 x10..12 / y1..3）。</summary>
            public const int TileTopRight = 1;

            /// <summary>左下角块（边带压在 x1..3 / y10..12）。</summary>
            public const int TileBottomLeft = 8;

            /// <summary>右下角块（边带压在 x10..12 / y10..12）。</summary>
            public const int TileBottomRight = 9;

            /// <summary>上边块（内容 12×3，边带 y1..3）。</summary>
            public const int TileTop = 2;

            /// <summary>下边块（内容 12×3，边带 y1..3 ⇒ 相对下外沿要 dy=+9）。</summary>
            public const int TileBottom = 16;

            /// <summary>左边块（内容 3×12，边带 x5..7 ⇒ 相对左外沿要 dx=−4）。</summary>
            public const int TileLeft = 10;

            /// <summary>右边块（内容 3×12，边带 x5..7、近黑内线在 x5 ⇒ 相对右外沿要 dx=+5）。</summary>
            public const int TileRight = 13;

            // ── 偏移（相对"该单元格左上角"，原版px；**像素自证反推**，见脚本文件头）──
            /// <summary>上边块：与角块同页边 ⇒ 无偏移。</summary>
            public static readonly Vector2 OffsetTop = new Vector2(0f, 0f);

            /// <summary>下边块：内容第 1 行 = 近黑内线，要落到外沿的第 10 行 ⇒ dy = 10−1 = **+9**。</summary>
            public static readonly Vector2 OffsetBottom = new Vector2(0f, 9f);

            /// <summary>左边块：边带在 x5..7，要落到左外沿 x1..3 ⇒ dx = 1−5 = **−4**。</summary>
            public static readonly Vector2 OffsetLeft = new Vector2(-4f, 0f);

            /// <summary>右边块：边带在 x5..7，要落到右外沿 x10..12（角块口径）⇒ dx = 10−5 = **+5**。</summary>
            public static readonly Vector2 OffsetRight = new Vector2(5f, 0f);

            /// <summary>把窗框尺寸**吸附到拼装网格**（12 的整数倍；向上取，宁可留白不留缺口）。</summary>
            public static float Snap(float origPx)
            {
                if (origPx <= 0f) return TilePitch;
                return Mathf.CeilToInt(origPx / TilePitch) * (float)TilePitch;
            }

            /// <inheritdoc cref="Snap(float)"/>
            public static Vector2 Snap(Vector2 origSize)
                => new Vector2(Snap(origSize.x), Snap(origSize.y));
        }

        // ═════════════════════════════════════════════════════════════════════
        // 6. 暂停菜单 / 选项 / 读条 / 启动（原版无对应 prefab ⇒ 本项目新增）
        // ═════════════════════════════════════════════════════════════════════
        // 暂停菜单：**直接复用主菜单按钮列的原版几何**（272×35、x=0、原版 4 个槽位）
        //   —— 原版 ESC 菜单在 Diablerie 里没有独立 prefab（`Prefabs/` 下只有 Menu/ 四个），
        //      故取本项目里唯一有依据的菜单按钮几何，不另造一套。
        public static class Pause
        {
            /// <summary>
            /// w5：暂停菜单底板（原版 `boxpieces` 拼装窗框，**288×180 原版px**）。
            /// <para>推导（无裸魔数）：底板外沿 = **4 个菜单按钮**的外接框 + 窗框厚度，再吸附 12 网格：
            ///   按钮 = 原版 `WideButton` 272×35、行节奏 45（原版 35+10）⇒
            ///   外接框 x ±136、y **−170..0**（170 高）⇒ + 2×4 = 280×178
            ///   ⇒ <see cref="BoxFrame.Snap(Vector2)"/> = **288×180** = 24×15 个 12px 格。</para>
            /// <para>底部那行提示（`PRESS ESC TO CONTINUE`）**不进框**：它是本项目新增的一行注
            ///   （原版 ESC 菜单没有这一行），故不参与外接框。</para>
            /// </summary>
            public static readonly Vector2 BoxSize =
                BoxFrame.Snap(new Vector2(272f, 170f) + BoxFrame.ThicknessEdges) * Scale;

            /// <summary>底板中心 = 上述按钮外接框的中心 (0,−85) → ×1.8 = **(0,−153)**。</summary>
            public static readonly Vector2 BoxPos = new Vector2(0f, -85f) * Scale;

            /// <summary>第 1 项中心 = 原版主菜单首行 (0,-17.5) → ×1.8 = **(0,-31.5)**。</summary>
            public static readonly Vector2 ResumePos = new Vector2(0f, -31.5f);

            /// <summary>第 2 项中心 = 原版 (0,-62.5) → ×1.8 = **(0,-112.5)**。</summary>
            public static readonly Vector2 OptionsPos = new Vector2(0f, -112.5f);

            /// <summary>第 3 项中心 = 原版 (0,-107.5) → ×1.8 = **(0,-193.5)**。</summary>
            public static readonly Vector2 SaveExitPos = new Vector2(0f, -193.5f);

            /// <summary>第 4 项中心 = 原版 (0,-152.5) → ×1.8 = **(0,-274.5)**。</summary>
            public static readonly Vector2 ToMainPos = new Vector2(0f, -274.5f);

            /// <summary>底部提示行 = 原版节奏再下一行 (0,-197.5) → ×1.8 = **(0,-355.5)**。</summary>
            public static readonly Vector2 HintPos = new Vector2(0f, -355.5f);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        //  （`UIWidgets.cs:628-649` 硬编码 `new Vector2(760f,420f)` 与 `new Color(0.16f,0.44f,0.78f)`）
        //  —— 同一屏的角色行按钮却是原版石雕按钮 ⇒ **一屏两种风格**。
        //
        //  为什么不"只靠引擎 API 换皮"（任务里的做法 ②）：`IUIManager.Confirm` 的签名
        //  （`Runtime/Core/PresentationContracts.cs:111`）**只有文案与回调**，没有任何外观参数；
        //  `ConfirmLayer` 是引擎包内的 `internal sealed` 类、几何/配色全部写死在 `Build` 里
        //  ⇒ 项目侧无法换皮。故走做法 ①：本项目自建 `UI/D2ConfirmPanel.cs`（`UIPanel`），
        //  底板与按钮都用原版素材（`ResPaths.PanelBoxFramePause` / `ResPaths.BtnMed*`）。
        //
        //  下面每个数都是**派生值**（不新拍几何；出处逐条见各行注释）：
        //    · 底板 = **与暂停菜单同一块原版窗框**（`boxframe_pause`，288×180 原版px）——
        //      确认弹窗要装「标题 + 正文 + 两颗按钮」，与暂停菜单那 4 行窗框装的是同一套东西
        //      ⇒ 直接复用同一尺寸与同一素材，不生成第三张窗框（少一个"哪个变体放哪格"的无出处决策）。
        //      第 1 行 = `Pause.ResumePos − Pause.BoxPos` = 画布 (0,+121.5)；第 2/3 行中点 = (0,0)；
        //      第 4 行 = `Pause.ToMainPos − Pause.BoxPos` = 画布 (0,−121.5)。
        //    · 两颗按钮的**横向**偏移 = ±(原版宽按钮宽 272 − 原版中等按钮宽 128)/2 = ±72 原版px
        //      （两钮外沿贴齐 272 的外接框；两个操作数都是原版素材尺寸）。
        //    · 弹窗**居中** = 画布 (0,0)：模态确认框，原版没有可对照的 prefab（登记为本项目新增），
        //      取画布中心不引入新坐标（原版 prefab 的锚点本来就以屏幕中线为原点）。
        public static class Confirm
        {
            /// <summary>
            /// 底板外沿 = 暂停菜单那一块（**288×180 原版px** = 24×15 个 12px 格）。
            /// </summary>
            public static readonly Vector2 BoxSizeOrig = new Vector2(288f, 180f);

            /// <inheritdoc cref="BoxSizeOrig"/>
            public static readonly Vector2 BoxSize = Px(BoxSizeOrig);

            /// <summary>底板中心 = 画布正中 (0,0)（模态确认框；见本节注释）。</summary>
            public static readonly Vector2 BoxPos = Vector2.zero;

            /// <summary>标题行中心：原版 (0,67.5) → 画布 **(0,121.5)**（= `Pause.ResumePos − Pause.BoxPos`）。</summary>
            public static readonly Vector2 TitlePos = Px(new Vector2(0f, 67.5f));

            /// <summary>
            /// 标题框尺寸 = 原版宽按钮宽 **272** × 原版文本框高 **30**。
            /// <para>出处：宽 272 = `WideButton.prefab` 的 `m_SizeDelta.x`（= 本底板外接框的来源宽度）；
            /// 高 30 = `ClassSelectMenu.prefab` 的 `SelectHeroClass` / `ClassName` 文本框高。</para>
            /// </summary>
            public static readonly Vector2 TitleSizeOrig = new Vector2(272f, 30f);

            /// <inheritdoc cref="TitleSizeOrig"/>
            public static readonly Vector2 TitleSize = Px(TitleSizeOrig);

            /// <summary>正文行中心 = 暂停菜单第 2/3 行的中点（相对底板 0）→ 画布 **(0,0)**。</summary>
            public static readonly Vector2 MessagePos = Vector2.zero;

            /// <summary>
            /// 正文框尺寸 = 原版 **272 × 90**（宽同 <see cref="TitleSizeOrig"/>；
            /// 高 90 = 2 行节奏 45 原版px ⇒ 占窗框中间两行，够长文案折两行）。
            /// </summary>
            public static readonly Vector2 MessageSizeOrig = new Vector2(272f, 90f);

            /// <inheritdoc cref="MessageSizeOrig"/>
            public static readonly Vector2 MessageSize = Px(MessageSizeOrig);

            /// <summary>
            /// 取消钮中心：原版 (−72,−67.5) → 画布 **(−129.6,−121.5)**。
            /// <para>「左 = 取消」照原版槽位语义（`ClassSelectMenu.prefab` 的 `ExitButton` 在左、
            /// `OkButton` 在右 —— 同本项目 `CharSelectPanel` 底部两钮的归位口径）。</para>
            /// </summary>
            public static readonly Vector2 CancelPos = Px(new Vector2(-72f, -67.5f));

            /// <summary>确认钮中心：原版 (+72,−67.5) → 画布 **(129.6,−121.5)**（「右 = 确认」，同 <see cref="CancelPos"/>）。</summary>
            public static readonly Vector2 ConfirmPos = Px(new Vector2(72f, -67.5f));
        }

        // 选项面板：原版无 prefab ⇒ 用一个本项目新增的框 + 原版行节奏 45。
        //   w5：框**不再是纯色块** —— 底图换成原版 `boxpieces` 拼装的窗框
        //     （`ResPaths.PanelBoxFrameSettings`），框的尺寸改为**从本屏元素的外接框派生**。
        public static class Settings
        {
            /// <summary>
            /// 本屏**内容外接框**（原版px；逐条取自本类下面已有的元素常量，没有新拍的数）：
            /// <list type="bullet">
            /// <item>x 左 = <c>LabelPos.x − LabelSize.x/2</c> = −140 − 80 = **−220**（音量行标签，最左）；</item>
            /// <item>x 右 = <c>FootPos.x + FootSize.x/2</c> = 0 + 200 = **+200**（脚注 400 宽，最右）；</item>
            /// <item>y 下 = <c>FootPos.y − FootSize.y/2</c> = −200 − 10 = **−210**；</item>
            /// <item>y 上 = <c>TitlePos.y + TitleSize.y/2</c> = 110 + 15 = **+125**。</item>
            /// </list>
            /// ⇒ 420×335（中心 (−10,−42.5)）。
            /// <para> w5 修掉一处**既有缺陷**：旧框是 420×335 中心 (0,−42.5) ⇒ 左沿 −210 而
            /// 标签左沿 −220 ⇒ 标签**一直探出框外 10px**（离线断言没覆盖"框要包住子元素"这一条）。
            /// 现在框由内容外接框派生 ⇒ 必然包住全部子元素。</para>
            /// </summary>
            public static readonly Vector2 ContentSizeOrig = new Vector2(420f, 335f);

            /// <inheritdoc cref="ContentSizeOrig"/>
            public static readonly Vector2 ContentCenterOrig = new Vector2(-10f, -42.5f);

            /// <summary>
            /// 面板框外沿（原版 432×348）= 内容外接框 + 2×框厚（4）后**吸附 12 拼装网格**
            /// ⇒ ×1.8 = **777.6×626.4 画布单位**（原版 432×348 ×1.8，**1:1 不拉伸**）。
            /// </summary>
            public static readonly Vector2 BoxSize =
                BoxFrame.Snap(ContentSizeOrig + BoxFrame.ThicknessEdges) * Scale;

            /// <summary>面板框中心 = 内容外接框中心 (−10,−42.5) → ×1.8 = **(−18,−76.5)**。</summary>
            public static readonly Vector2 BoxPos = ContentCenterOrig * Scale;

            /// <summary>标题 原版 (0,110) 300×30 → ×1.8 = **(0,198) 540×54**。</summary>
            public static readonly Vector2 TitlePos = new Vector2(0f, 198f);

            /// <inheritdoc cref="TitlePos"/>
            public static readonly Vector2 TitleSize = new Vector2(540f, 54f);

            /// <summary>第 1 行（BGM）y：原版 60 → ×1.8 = **108**（行距 45 原版px）。</summary>
            public const float Row1Y = 108f;

            /// <summary>第 2 行（音效）y：原版 15 → ×1.8 = **27**。</summary>
            public const float Row2Y = 27f;

            /// <summary>第 3 行（全屏）y：原版 -30 → ×1.8 = **-54**。</summary>
            public const float Row3Y = -54f;

            /// <summary>
            /// 第 4 行（画质 / 质量等级）y：原版 -75 → ×1.8 = **-135**。
            /// <para>推导：行节奏 = 原版 35（按钮高）+ 10（间距）= 45 ⇒ 第 4 行 = 第 3 行 -30 - 45 = -75。</para>
            /// <para> 本行**原先是第 5 行**（原版 -120 = -216 画布px）——它上面那一行是「方向键移动开关」，
            /// 原版没有该开关 ⇒ 那一行已删除（验收表 U-1），故画质行上移接在第 3 行之后，面板不留空行。</para>
            /// </summary>
            public const float Row4Y = -135f;

            /// <summary>行内「标签」中心 原版 -140 → ×1.8 = **-252**（160×35 → 288×63）。</summary>
            public static readonly Vector2 LabelPos = new Vector2(-252f, 0f);

            /// <inheritdoc cref="LabelPos"/>
            public static readonly Vector2 LabelSize = new Vector2(288f, 63f);

            /// <summary>行内「−」中心 原版 0 → ×1.8 = **0**（35×35 → 63×63）。</summary>
            public static readonly Vector2 MinusPos = new Vector2(0f, 0f);

            /// <summary>行内「值」中心 原版 45 → ×1.8 = **81**（50×35 → 90×63）。</summary>
            public static readonly Vector2 ValuePos = new Vector2(81f, 0f);

            /// <inheritdoc cref="ValuePos"/>
            public static readonly Vector2 ValueSize = new Vector2(90f, 63f);

            /// <summary>行内「+」中心 原版 90 → ×1.8 = **162**（35×35 → 63×63）。</summary>
            public static readonly Vector2 PlusPos = new Vector2(162f, 0f);

            /// <summary>行内音量条：原版 240×6 @ (-40, 行 y-24) → ×1.8 = **432×10.8 @ (-72, 行 y-43.2)**。</summary>
            public static readonly Vector2 BarPos = new Vector2(-72f, -43.2f);

            /// <inheritdoc cref="BarPos"/>
            public static readonly Vector2 BarSize = new Vector2(432f, 10.8f);

            /// <summary>开关按钮（全屏 / 画质」等开关行）中心 原版 60 → ×1.8 = **108**（用原版中等按钮）。
            /// 原版 D2 只有**鼠标点地面移动** ⇒ 本工程**没有**方向键移动开关（该行连同它读的设置项
            /// 已按验收表 U-1 删除）；此处只服务「全屏 / 画质」两行。</summary>
            public static readonly Vector2 TogglePos = new Vector2(108f, 0f);

            /// <summary>「关闭」按钮 原版 (0,-170) → ×1.8 = **(0,-306)**（用原版中等按钮）。</summary>
            public static readonly Vector2 ClosePos = new Vector2(0f, -306f);

            /// <summary>脚注 原版 (0,-200) 400×20 → ×1.8 = **(0,-360) 720×36**。</summary>
            public static readonly Vector2 FootPos = new Vector2(0f, -360f);

            /// <inheritdoc cref="FootPos"/>
            public static readonly Vector2 FootSize = new Vector2(720f, 36f);
        }

        // 读条屏（agent-a3 重写：1:1 复刻原版进图画面）
        //
        // 依据（**原版行为**，出处 Diablerie `Assets/Scripts/Diablerie/Game/UI/LoadingScreen.cs`）：
        //   :36-49  `_gameObject` 下第一个子节点 = `Background`（RawImage，**纯黑**，铺满整屏）
        //   :54-61  `Image` 节点：anchorMin/Max = (0.5,0.5)、pivot = (0.5,0.5)、
        //           anchoredPosition = (0.5,0.5) ⇒ **屏幕正中**（差 0.5px，忽略）
        //   :51-52  贴图 = `data/global/ui/Loading/loadingscreen`（10 帧 256×256，本工程已解出）
        //   :66-68  帧号 = `(int)((帧数-1) × completeness)`；`SetNativeSize()` ⇒ **按原始像素尺寸显示**
        //   :27-35  `Show/Create/Update` 里**没有任何**进度条 / 百分比 / 提示文字节点
        // ⇒ 换算（本项目 800×600 → 1920×1080 ×1.8 口径）：
        //     图尺寸 = 256 原版px × 1.8 = **460.8×460.8**（占画布宽 460.8/1920 = 24%，
        //       与原版 256/800 = 32% 同为"居中一张图"的语义，按高度等比后横向占比自然变小）
        //     图中心 = 原版屏幕正中 (0,0) → 画布 **(0,0)**
        public static class Loading
        {
            /// <summary>
            /// 原版读条图尺寸 256×256 → ×1.8 = **(460.8×460.8)**，中心 = 画布正中 **(0,0)**。
            /// <para>依据：`LoadingScreen.cs:58-61`（居中）+ `:68`（`SetNativeSize()` = 按原始像素显示）
            /// + `:10`（`loadingscreen.dc6` 实测 10 帧**全部 256×256**）。</para>
            /// </summary>
            public static readonly Vector2 ArtSize = new Vector2(256f, 256f) * Scale;

            /// <inheritdoc cref="ArtSize"/>
            public static readonly Vector2 ArtPos = Vector2.zero;

            /// <summary>原版读条图尺寸（800×600 基准像素，256×256）—— 逐条断言的另一半。</summary>
            public static readonly Vector2 ArtOrigSize = new Vector2(256f, 256f);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 7. 对照表（也是离线断言表）
        // ═════════════════════════════════════════════════════════════════════
        private static readonly FlowLayoutEntry[] Rows = BuildTable();

        /// <summary>全量对照表（`uicheck` 逐条核对 `常量 == 原版值 × 1.8`）。</summary>
        public static FlowLayoutEntry[] Table { get { return Rows; } }

        /// <summary>归属屏名（<see cref="FlowLayoutEntry.Panel"/> 的取值）。</summary>
        public static class Panel
        {
            /// <summary>主菜单屏（适配系数 <see cref="FitMenu"/>）。</summary>
            public const string Menu = "Menu";

            /// <summary>选角 / 创角屏（适配系数 <see cref="FitClass"/>）。</summary>
            public const string Class = "Class";

            /// <summary>
            /// **选角屏专属**元素（适配系数 <see cref="FitClass"/>）。
            /// <para> 本片新开这个组的原因：`Panel.Class` 是**选角 + 创角两屏共用**的一组，
            /// 而两屏的专属元素**从不同时出现在画面上**（例：创角屏的 5 个半身像 vs 选角屏的角色行）
            /// ⇒ 把它们放在同一组里做"两两不重叠"，**跨屏的那些元件对是毫无意义的比较**
            /// （既可能误报重叠、也会让真正同屏的重叠被淹没）。</para>
            /// <para>本组只收**选角屏有、创角屏没有**的元件（角色列表容器 + 角色行）。
            /// 两屏**共用**的骨架（标题 / 说明行 / 底部两钮 / 整屏贴图）与**创角屏专属**元件
            /// （半身像 / 热点 / 名字行）仍在 <see cref="Class"/> 组 —— 一次性重切两屏归属会动到
            /// 既有断言的字面量与计数，超出本片范围（已在回报的「未决」里点名）。</para>
            /// </summary>
            public const string CharSelect = "CharSelect";

            /// <summary>暂停菜单（复用主菜单按钮几何）。</summary>
            public const string Pause = "Pause";

            /// <summary>选项面板。</summary>
            public const string Settings = "Settings";

            /// <summary>
            /// <para>单开一组的原因与 <see cref="CharSelect"/> 同一口径：弹窗**只在自己出现时有内容**，
            /// 与任何屏的元素做"两两不重叠"都是无意义的跨屏比较。</para>
            /// </summary>
            public const string Confirm = "Confirm";

            /// <summary>读条屏。</summary>
            public const string Loading = "Loading";

            /// <summary>启动画面（原版无对应 prefab ⇒ 坐标全为本项目新增）。</summary>
            public const string Boot = "Boot";
        }

        /// <summary>某屏的适配系数（<see cref="FitMenu"/> 或 <see cref="FitClass"/>）。</summary>
        public static float FitOf(string panel)
        {
            switch (panel)
            {
                case Panel.Class:
                case Panel.CharSelect: return FitClass;
                case Panel.Menu:
                case Panel.Pause:
                case Panel.Settings:
                case Panel.Confirm:
                case Panel.Loading:
                case Panel.Boot: return FitMenu;
                default:
                    UiLog.WarnOnce("flow.fit.unknown." + panel,
                        $"UiLayoutFlow.FitOf 收到未登记的屏名「{panel}」⇒ 按 {FitMenu} 处理（请补一行）");
                    return FitMenu;
            }
        }

        /// <summary>
        /// 某屏所有元素的**包围盒**（Canvas 单位，**未乘**适配系数）——离线断言
        /// 「乘适配系数后全部落在 1920×1080 参考画布内」用，也便于回报里贴范围。
        /// </summary>
        public static Rect BoundsOf(string panel)
        {
            var has = false;
            float minX = 0f, maxX = 0f, minY = 0f, maxY = 0f;
            for (var i = 0; i < Rows.Length; i++)
            {
                var e = Rows[i];
                if (e.Panel != panel) continue;

                var hx = e.Size.x * 0.5f;
                var hy = e.Size.y * 0.5f;
                if (!has)
                {
                    minX = e.Pos.x - hx; maxX = e.Pos.x + hx;
                    minY = e.Pos.y - hy; maxY = e.Pos.y + hy;
                    has = true;
                    continue;
                }
                minX = Mathf.Min(minX, e.Pos.x - hx); maxX = Mathf.Max(maxX, e.Pos.x + hx);
                minY = Mathf.Min(minY, e.Pos.y - hy); maxY = Mathf.Max(maxY, e.Pos.y + hy);
            }
            return has ? Rect.MinMaxRect(minX, minY, maxX, maxY) : new Rect();
        }

        private static FlowLayoutEntry[] BuildTable()
        {
            var list = new List<FlowLayoutEntry>();

            void Add(string panel, string node, string use, Vector2 oPos, Vector2 oSize, Vector2 pos,
                Vector2 size, bool fromOriginal)
            {
                list.Add(new FlowLayoutEntry(node, use, oPos, oSize, pos, size, fromOriginal, panel));
            }

            Add(Panel.Menu, "MainMenu/GameMenu/Buttons/VerticalLayoutGroup(35+10)",
                "按钮行节奏 45 原版px", Vector2.zero, new Vector2(0f, RowStepOrig),
                Vector2.zero, new Vector2(0f, RowStep), true);
            Add(Panel.Menu, "MainMenu/GameMenu/Buttons/WideButton(272×35)",
                "主菜单按钮尺寸", Vector2.zero, WideButtonOrig, Vector2.zero, WideButton, true);
            Add(Panel.Menu, "MainMenu/GameMenu/Buttons/SinglePlayerButton",
                "单人游戏", new Vector2(0f, -17.5f), WideButtonOrig, Menu.SinglePos, WideButton, true);
            //   `CinematicsButton 槽被占用来放设置`。原版 4 项 = SINGLE PLAYER / MULTIPLAYER /
            //   CINEMATICS / EXIT；「继续游戏」由 `SINGLE PLAYER → 选角屏 → ENTER` 承担，
            //   「设置」挪到 ESC 暂停菜单的 OPTIONS（`Panel.Pause` 的 OptionsPos，见下）。
            //   真实槽位**（坐标照原版）×1.8，但 `MainMenuPanel.Build()` **不再建这两个按钮**
            //   —— 用户原话「菜单中，中间那两个既然没开发，就不要那个按钮了」。
            //   两行照旧留在表里 = 这条"偏离原版 4 项"是**可被离线断言看见**的（删行反而隐藏了偏离）。
            Add(Panel.Menu, "MainMenu/GameMenu/Buttons/MultiPlayerButton",
                "多人游戏（原版第 2 槽；本轮用户点名删按钮，槽位坐标保留）",
                new Vector2(0f, -62.5f), WideButtonOrig, Menu.MultiPos, WideButton, true);
            Add(Panel.Menu, "MainMenu/GameMenu/Buttons/CinematicsButton",
                "过场动画（原版第 3 槽；本轮用户点名删按钮，槽位坐标保留）",
                new Vector2(0f, -107.5f), WideButtonOrig, Menu.CinematicsPos, WideButton, true);
            Add(Panel.Menu, "MainMenu/GameMenu/Buttons/ExitButton",
                "退出游戏", new Vector2(0f, -152.5f), WideButtonOrig, Menu.ExitPos, WideButton, true);
            //   登记进本表 = 画布断言/重叠断言**真的会检查它**（规范：定义了但没人检查 = 必然漏）——
            //   2026 启动屏那次漏登记的教训（署名行压在版权行上 48px 且没人发现）就在 `Brand` 的注释里。
            Add(Panel.Menu, "(新增署名行)", "by clover-engine 署名（主菜单居底居中）",
                Brand.ByLineOrigPos, Brand.ByLineOrigSize,
                Brand.ByLinePos, Brand.ByLineSize, false);

            // ── 选角 / 创角 ──
            Add(Panel.Class, "ClassSelectMenu/Canvas/SelectHeroClass",
                "标题", new Vector2(0f, 267f), new Vector2(493f, 30f),
                ClassMenu.TitlePos, ClassMenu.TitleSize, true);
            Add(Panel.Class, "ClassSelectMenu/Canvas/ClassName",
                "角色名行 / 名字输入框行", new Vector2(0f, 198f), new Vector2(493f, 30f),
                ClassMenu.NameRowPos, ClassMenu.NameRowSize, true);
            Add(Panel.Class, "ClassSelectMenu/Canvas/ClassDescription",
                "职业说明", new Vector2(1f, 155f), new Vector2(300f, 50f),
                ClassMenu.DescPos, ClassMenu.DescSize, true);
            Add(Panel.Class, "ClassSelectMenu/Canvas/ExitButton",
                "返回", new Vector2(-300f, -250f), MediumButtonOrig, ClassMenu.ExitPos, MediumButton, true);
            Add(Panel.Class, "ClassSelectMenu/Canvas/OkButton",
                "确定 / 进入", new Vector2(300f, -250f), MediumButtonOrig, ClassMenu.OkPos, MediumButton, true);
            Add(Panel.Class, "ClassSelectMenu/Canvas/Amazon(热点)",
                "亚马逊热点", new Vector2(-299f, -16f), new Vector2(90f, 200f),
                ClassMenu.AmazonPos, ClassMenu.SpotSize, true);
            Add(Panel.Class, "ClassSelectMenu/Canvas/Assassin(热点)",
                "刺客热点（本项目无该职业）", new Vector2(-185f, -23f), new Vector2(90f, 200f),
                ClassMenu.AssassinPos, ClassMenu.SpotSize, true);
            Add(Panel.Class, "ClassSelectMenu/Canvas/Necromancer(热点)",
                "死灵法师热点", new Vector2(-99f, -11f), new Vector2(90f, 200f),
                ClassMenu.NecromancerPos, ClassMenu.SpotSize, true);
            Add(Panel.Class, "ClassSelectMenu/Canvas/Barbarian(热点)",
                "野蛮人热点", new Vector2(1f, 0f), new Vector2(90f, 200f),
                ClassMenu.BarbarianPos, ClassMenu.SpotSize, true);
            Add(Panel.Class, "ClassSelectMenu/Canvas/Paladin(热点)",
                "圣骑士热点", new Vector2(116.5f, -8f), new Vector2(110f, 200f),
                ClassMenu.PaladinPos, ClassMenu.SpotSizeWide, true);
            Add(Panel.Class, "ClassSelectMenu/Canvas/Sorceress(热点)",
                "法师热点", new Vector2(221.75f, -22f), new Vector2(85f, 200f),
                ClassMenu.SorceressPos, ClassMenu.SpotSizeNarrow, true);
            Add(Panel.Class, "ClassSelectMenu/Canvas/Druid(热点)",
                "德鲁伊热点（本项目无该职业）", new Vector2(317f, -16.5f), new Vector2(100f, 210f),
                ClassMenu.DruidPos, ClassMenu.SpotSizeTall, true);
            //   原版 7 个热点里本项目有的 5 个，行序 = 原版屏上从左到右。
            for (var slot = 0; slot < ClassMenu.Spot.SlotCount; slot++)
            {
                for (var st = 0; st < 3; st++)
                {
                    var p = ClassMenu.Spot.Of(slot, st);
                    Add(Panel.Class, p.Node, p.Use + "（半身像）", p.OrigPos, p.OrigSize, p.Pos, p.Size, true);
                }
            }

            Add(Panel.Class, "ClassSelectMenu/Canvas/(新增名字输入框)",
                "名字输入框", new Vector2(-15f, 198f), new Vector2(462f, 35f),
                New.NameInputPos, New.NameInputSize, false);
            Add(Panel.Class, "ClassSelectMenu/Canvas/(新增 NAME 标签)",
                "NAME: 标签", new Vector2(-330f, 198f), new Vector2(140f, 35f),
                New.NameLabelPos, New.NameLabelSize, false);
            //   `(新增属性行) 四维加减行`、`(新增预览行) 生命/法力/耐力预览`。
            //   难度选择也**不在**本表里：原版控件坐标拿不出出处（见回报的 BLOCKED）。

            //   这些元件**只有选角屏有**（创角屏不建角色列表）⇒ 放进 `Class` 组会和创角屏的半身像/
            //   名字框做**毫无意义的跨屏重叠比较**。见 `Panel.CharSelect` 的注释。
            Add(Panel.CharSelect, "ClassSelectMenu/Canvas/(新增角色列表容器)",
                "角色列表（原版热点带左/右 + 说明行底/按钮行顶之间）",
                new Vector2(13f, -51.25f), new Vector2(714f, 322.5f),
                Select.ListPos, Select.ListSize, false);

            //   登记口径：**只登记第 1 行与最后一行**（行栈的上下极端）——
            //   中间 5 行是同模板的平移，由「行步进 81 > 行内元件最大高 63」这条断言罩住
            //   （`uicheck` 第 ⑲ 节），两行极端在界内且互不重叠 ⇒ 整栈都被包住；
            //   且不会让对照表日志一次多出 35 行。
            //   行内元件的坐标 = `Select.ListPos + (行内偏移 x, Select.RowY(row))`，
            //   与 `CharSelectPanel.BuildRow` **同一个来源**（`Select.RowY`）。
            void AddRowItem(int row, string node, string use, Vector2 local, Vector2 size)
            {
                var pos = new Vector2(Select.ListPos.x + local.x, Select.ListPos.y + Select.RowY(row));
                Add(Panel.CharSelect, node, use,
                    new Vector2(Orig(pos.x), Orig(pos.y)), Orig(size), pos, size, false);
            }

            for (var row = 0; row < Select.MaxRows; row += Select.MaxRows - 1)     // row = 0 与 6
            {
                var nth = row == 0 ? "第 1 行" : $"最后一行（第 {row + 1} 行）";
                var prefix = $"ClassSelectMenu/Canvas/(新增角色行·{nth}";
                AddRowItem(row, prefix + "·名字)", $"{nth}：角色名", Select.RowNamePos, Select.RowNameSize);
                AddRowItem(row, prefix + "·职业)", $"{nth}：职业", Select.RowClassPos, Select.RowClassSize);
                AddRowItem(row, prefix + "·等级)", $"{nth}：等级", Select.RowLevelPos, Select.RowLevelSize);
                AddRowItem(row, prefix + "·进入)", $"{nth}：进入（原版中等按钮）",
                    Select.RowEnterPos, MediumButton);
                AddRowItem(row, prefix + "·删除)", $"{nth}：删除（原版中等按钮）",
                    Select.RowDeletePos, MediumButton);
                // 行热点 = 整行宽的**透明点击区**（`UiLayoutFlow.Hotspot`）⇒ Use 里带「(热点)」，
                // 既有重叠断言按"透明热点"豁免（它的子元素本来就压在它上面）。
                AddRowItem(row, prefix + "·热点)", $"{nth}：行热点（透明点击区）(热点)",
                    Vector2.zero, Select.RowSize);
            }

            // ── 暂停 / 选项 / 读条 ──
            Add(Panel.Pause, "MainMenu/GameMenu/Buttons/SinglePlayerButton(复用)",
                "暂停：继续", new Vector2(0f, -17.5f), WideButtonOrig, Pause.ResumePos, WideButton, false);
            Add(Panel.Pause, "MainMenu/GameMenu/Buttons/MultiPlayerButton(复用)",
                "暂停：选项", new Vector2(0f, -62.5f), WideButtonOrig, Pause.OptionsPos, WideButton, false);
            Add(Panel.Pause, "MainMenu/GameMenu/Buttons/CinematicsButton(复用)",
                "暂停：保存并退出", new Vector2(0f, -107.5f), WideButtonOrig, Pause.SaveExitPos, WideButton, false);
            Add(Panel.Pause, "MainMenu/GameMenu/Buttons/ExitButton(复用)",
                "暂停：回主菜单", new Vector2(0f, -152.5f), WideButtonOrig, Pause.ToMainPos, WideButton, false);
            Add(Panel.Pause, "MainMenu/GameMenu/Buttons/(新增下一行)",
                "暂停：底部提示", new Vector2(0f, -197.5f), WideButtonOrig, Pause.HintPos, WideButton, false);
            Add(Panel.Pause, "(原版 MENU/boxpieces.DC6 拼装窗框)", "暂停菜单底板（窗框）",
                new Vector2(0f, -85f), new Vector2(288f, 180f),
                Pause.BoxPos, Pause.BoxSize, false);

            //     ① 「乘适配系数后落在 1920×1080 画布内」；② 「两两不重叠」。
            Add(Panel.Confirm, "(原版 MENU/boxpieces.DC6 拼装窗框)", "确认弹窗底板（窗框）",
                new Vector2(0f, 0f), new Vector2(288f, 180f),
                Confirm.BoxPos, Confirm.BoxSize, false);
            Add(Panel.Confirm, "(新增确认框·标题)", "确认框标题（原版宽按钮宽 272 × 原版文本框高 30）",
                new Vector2(0f, 67.5f), new Vector2(272f, 30f),
                Confirm.TitlePos, Confirm.TitleSize, false);
            Add(Panel.Confirm, "(新增确认框·正文)", "确认框正文（272 × 90，占窗框中间两行）",
                new Vector2(0f, 0f), new Vector2(272f, 90f),
                Confirm.MessagePos, Confirm.MessageSize, false);
            Add(Panel.Confirm, "(新增确认框·取消)", "确认框·取消（原版中等按钮 128×35，屏左）",
                new Vector2(-72f, -67.5f), MediumButtonOrig, Confirm.CancelPos, MediumButton, false);
            Add(Panel.Confirm, "(新增确认框·确认)", "确认框·确认（原版中等按钮 128×35，屏右）",
                new Vector2(72f, -67.5f), MediumButtonOrig, Confirm.ConfirmPos, MediumButton, false);

            // w5：选项底板由"420×335 纯色块"改为**原版 boxpieces 拼装窗框**。
            Add(Panel.Settings, "(原版 MENU/boxpieces.DC6 拼装窗框)", "选项面板底板（窗框）",
                new Vector2(-10f, -42.5f), new Vector2(432f, 348f),
                Settings.BoxPos, Settings.BoxSize, false);
            Add(Panel.Settings, "(新增画质行)", "画质 / 质量等级行（第 4 行，行节奏 45）",
                new Vector2(0f, -75f), new Vector2(0f, 35f),
                new Vector2(0f, Settings.Row4Y), new Vector2(0f, Px(35f)), false);

            //     ① `BoundsOf(Panel.Settings)` 只包住底板 ⇒「乘适配系数后落在 1920×1080 画布内」
            //        这条断言对选项屏的**标题 / 4 行控件 / 关闭钮 / 脚注**是**空集**（查了等于没查）；
            //     ②「两两不重叠」同样漏掉它们 ⇒ 把某一行 y 改错 30px、或把 Close 压到第 4 行上，
            //        **没有任何断言会红**。
            //   行节奏推出来的整数（出处写在该组的注释里），**不是拍出来的**。
            Add(Panel.Settings, "(新增选项标题)", "选项标题", new Vector2(0f, 110f), new Vector2(300f, 30f),
                Settings.TitlePos, Settings.TitleSize, false);
            Add(Panel.Settings, "(新增选项关闭钮)", "关闭（原版中等按钮）", new Vector2(0f, -170f),
                MediumButtonOrig, Settings.ClosePos, MediumButton, false);
            Add(Panel.Settings, "(新增选项脚注)", "脚注", new Vector2(0f, -200f), new Vector2(400f, 20f),
                Settings.FootPos, Settings.FootSize, false);

            // 4 行控件（第 1/2 行 = 音量：标签 + − + 值 + + + 音量条；第 3/4 行 = 开关：标签 + 原版中等按钮）。
            //   `orig y` 与「原版行节奏 45」一一对应：60 / 15 / -30 / -75。
            var minusOrig = new Vector2(35f, 35f);
            var minusSize = Px(minusOrig);
            var barOrig = new Vector2(240f, 6f);          // 音量条：原版 240×6
            var barSize = Px(barOrig);
            var rowYs = new[] { Settings.Row1Y, Settings.Row2Y, Settings.Row3Y, Settings.Row4Y };
            for (var r = 0; r < rowYs.Length; r++)
            {
                var y = rowYs[r];
                var oy = Orig(y);                          // 108→60 / 27→15 / -54→-30 / -135→-75
                var row = (r + 1).ToString();
                Add(Panel.Settings, $"(新增选项第 {row} 行·标签)", $"第 {row} 行标签",
                    new Vector2(-140f, oy), new Vector2(160f, 35f),
                    new Vector2(Settings.LabelPos.x, y), Settings.LabelSize, false);
                if (r > 1)
                {
                    // 第 3/4 行 = 开关行（全屏 / 画质）：只有「标签 + 原版中等按钮」，**没有** ± ／ 数值 ／ 音量条
                    Add(Panel.Settings, $"(新增选项第 {row} 行·开关)", $"第 {row} 行开关（原版中等按钮）",
                        new Vector2(60f, oy), MediumButtonOrig,
                        new Vector2(Settings.TogglePos.x, y), MediumButton, false);
                    continue;
                }
                // 第 1/2 行 = 音量行：标签 + − + 数值 + + + 音量条（**没有开关**）
                Add(Panel.Settings, $"(新增选项第 {row} 行·减号)", $"第 {row} 行 −（原版按钮行高做边长）",
                    new Vector2(0f, oy), minusOrig, new Vector2(Settings.MinusPos.x, y), minusSize, false);
                Add(Panel.Settings, $"(新增选项第 {row} 行·数值)", $"第 {row} 行数值（位图字体）",
                    new Vector2(45f, oy), new Vector2(50f, 35f),
                    new Vector2(Settings.ValuePos.x, y), Settings.ValueSize, false);
                Add(Panel.Settings, $"(新增选项第 {row} 行·加号)", $"第 {row} 行 +（原版按钮行高做边长）",
                    new Vector2(90f, oy), minusOrig, new Vector2(Settings.PlusPos.x, y), minusSize, false);
                Add(Panel.Settings, $"(新增选项第 {row} 行·音量条)", $"第 {row} 行音量条（轨道）",
                    new Vector2(-40f, oy - 24f), barOrig,
                    new Vector2(Settings.BarPos.x, y + Settings.BarPos.y), barSize, false);
            }
            Add(Panel.Loading, "(原版进图读条图 Loading/loadingscreen，居中)",
                "进图读条图（原版帧 256×256，中心 = 屏幕正中）",
                Vector2.zero, Loading.ArtOrigSize, Loading.ArtPos, Loading.ArtSize, true);

            // ── 启动屏（原版**没有**启动屏 prefab/贴图 ⇒ 全部本项目新增，坐标无原版依据，
            //    但要满足同一条硬约束：×1.8 口径下必须落在 1920×1080 画布内）──
            //   （`ui/Logo/logo.DC6` 帧 0，实测 319×177 = DIABLO II 火焰字标）。
            //   尺寸 = 原版像素 1:1（`OrigSize` 就是实测的 319×177，这里 **不是**"假的原版值"）。
            Add(Panel.Boot, "(原版 logo: ui/Logo/logo.DC6 帧 0)", "原版 DIABLO II 火焰字标（319×177 1:1）",
                new Vector2(0f, 100f), new Vector2(319f, 177f),
                new Vector2(0f, 180f), Px(new Vector2(319f, 177f)), true);
            Add(Panel.Boot, "(新增标题行)", "暗黑破坏神 II · 复刻", new Vector2(0f, -16.6667f), new Vector2(700f, 20f),
                new Vector2(0f, -30f), new Vector2(1260f, 36f), false);
            Add(Panel.Boot, "(新增提示行)", "按任意键继续", new Vector2(0f, -66.6667f), new Vector2(500f, 20f),
                new Vector2(0f, -120f), new Vector2(900f, 36f), false);
            Add(Panel.Boot, "(新增版权行)", "版权说明（两行）", new Vector2(0f, -166.6667f), new Vector2(700f, 40f),
                new Vector2(0f, -300f), new Vector2(1260f, 72f), false);
            //   同步放大 ⇒ 画布 900×54、底边 -489（画布底 -540 ⇒ 留白 51 ≥ 18），位置 y=-462 **没动**。
            Add(Panel.Boot, "(新增署名行)", "by clover-engine 署名（居底居中）",
                Brand.ByLineOrigPos, Brand.ByLineOrigSize,
                Brand.ByLinePos, Brand.ByLineSize, false);
            Add(Panel.Boot, "(新增版本行)", "单机版 · 存档版本", new Vector2(-230f, -204.1667f), new Vector2(300f, 20f),
                new Vector2(-414f, -367.5f), new Vector2(540f, 36f), false);

            return list.ToArray();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 8. 一次性打印对照表（验收要"面板 → 原版坐标 → 我们的坐标 → 依据节点名"）
        // ═════════════════════════════════════════════════════════════════════
        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 把对照表打进日志（**每个 <paramref name="panel"/> 一次**，避免每次开面板刷屏）。
        /// 日志行格式 = 验收要贴的那张表的原始行。
        /// </summary>
        public static void LogTable(string panel)
        {
            if (!Logged.Add(panel)) return;

            var original = 0;
            var added = 0;
            UiLog.Info($"[布局·1:1] {panel}：原版 800×600 → 本工程 1920×1080（×{Scale} 水平居中）逐元素对照表");
            for (var i = 0; i < Rows.Length; i++)
            {
                var e = Rows[i];
                if (e.FromOriginal) original++; else added++;
                UiLog.Info("[布局] " + e.Line);
            }
            UiLog.Info($"[布局·1:1] {panel}：原版节点 {original} 条 + 本项目新增 {added} 条；" +
                       "依据 = Prefabs/Menu/{MainMenu,ClassSelectMenu,WideButton,MediumButton}.prefab");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 一条 1:1 文字的定位/反馈句柄。
        /// <para>
        /// 载体 = <see cref="D2Label"/>（原版字模）：拉丁串（英文/数字/半角符号）走拉丁图集
        /// <c>D2/Fonts/font{N}</c>；含中文（或全角标点）的串整条走中文图集
        /// **中文不再回退 `UIFactory.DefaultFont()`。**
        /// </para>
        /// <para>
        /// **×1.8 的处理**：位图字模是按**原版像素**切的（font16 格子 16×18、中文格子 13×13），
        /// 所以内容按**原版 px** 排版，再给根节点 `localScale = 1.8` ⇒ 视觉尺寸/位置与整屏贴图一致
        /// （整屏原版贴图按高度放大到 1080 也是 ×1.8）。
        /// </para>
        /// </summary>
        internal sealed class FlowLabel
        {
            private readonly RectTransform _root;
            private readonly D2Text.D2Font _font;
            private readonly TextAnchor _anchor;
            private readonly Vector2 _origSize;

            /// <summary>额外字模缩放（1 = 不缩放；按钮用 <see cref="ButtonFontScale"/>，见该常量的出处）。</summary>
            private readonly float _fontScale;
            private string _text = string.Empty;
            private Color _color = Color.white;
            private D2Label _bitmap;

            /// <summary>位图字体接管是否已打 Info（证据用，一次就够）。</summary>
            private static bool _bitmapLogged;

            private FlowLabel(RectTransform root, D2Text.D2Font font, TextAnchor anchor, Vector2 origSize,
                float fontScale)
            {
                _root = root;
                _font = font;
                _anchor = anchor;
                _origSize = origSize;
                _fontScale = fontScale > 0f ? fontScale : 1f;
            }

            /// <summary>标签根节点（Canvas 单位定尺；用于显隐 / 改位置）。</summary>
            public RectTransform Root { get { return _root; } }

            /// <summary>当前是不是走原版位图字体（拉丁串）。</summary>
            public bool UsingBitmap { get { return _bitmap != null; } }

            /// <summary>
            /// 造一条 1:1 文字。
            /// </summary>
            /// <param name="origSize">**原版 px** 的文本框尺寸（位图排版与居中都用它）。</param>
            /// <param name="pos">本工程 Canvas 单位的位置（= 原版坐标 ×1.8）。</param>
            /// <param name="fontScale">
            /// 额外字模缩放（默认 1 = 按档位的原版 px 1:1）。按钮传
            /// <see cref="ButtonFontScale"/>（= 原版字号 18 ÷ font16 名义字号 16）。
            /// </param>
            public static FlowLabel Create(Transform parent, string name, string text, D2Text.D2Font font,
                TextAnchor anchor, Color color, Vector2 origSize, Vector2 pos, float fontScale = 1f)
            {
                var root = UIFactory.CreateCentered(name, parent, Px(origSize), pos);
                var label = new FlowLabel(root, font, anchor, origSize, fontScale);
                label._color = color;
                label.SetText(text);
                return label;
            }

            /// <summary>改文案（一律走原版位图字模，见 <see cref="Render"/>）。</summary>
            public void SetText(string text)
            {
                var next = text ?? string.Empty;
                if (_bitmap != null && string.Equals(next, _text, StringComparison.Ordinal)) return;

                _text = next;
                Render();
            }

            /// <summary>改颜色（原版亮度白 / 未选中压暗一档由调用方给）。</summary>
            public void SetColor(Color color)
            {
                if (_color == color) return;
                _color = color;
                if (_bitmap != null) _bitmap.SetColor(color);
            }

            /// <summary>显隐。</summary>
            public void SetActive(bool on)
            {
                if (_root != null) _root.gameObject.SetActive(on);
            }

            private void Render()
            {
                if (_root == null)
                {
                    UiLog.Warn("FlowLabel.Render：根节点已销毁（面板已关？）⇒ 丢弃本次文案");
                    return;
                }

                ClearInner();

                if (_text.Length == 0) return;

                //   （两者都是原版同一套机制：`.tbl` 表 + `.dc6` 图像，见 `UI/D2Text.cs` 文件头）。
                //   内容按**原版 px** 排版（`_origSize` 就是原版 px），再给根节点整体 ×1.8
                //   ⇒ 视觉尺寸/位置与整屏原版贴图一致（贴图也是按高度 ×1.8；见本文件头的换算口径）。
                _bitmap = D2Label.Create(_root, "Bitmap", _text, _font, _anchor, _color, _origSize, Vector2.zero);
                if (_bitmap == null)
                {
                    UiLog.Error($"FlowLabel 位图标签没建出来（{_root.name}）⇒ 该文案不可见");
                    return;
                }
                //   为什么乘在这里而不是改 `_origSize`：`_origSize` 是**排版用的框**（内容按它居中），
                //   给它乘系数会连带改变换行/居中取值；只缩放渲染节点 ⇒ 排版口径一个字都没动。
                var s = Scale * _fontScale;
                _bitmap.Root.localScale = new Vector3(s, s, 1f);
                _bitmap.Root.anchoredPosition = Vector2.zero;

                if (!_bitmapLogged)
                {
                    _bitmapLogged = true;
                    UiLog.Info($"[原版位图字体] 已接管流程面板文字（font{(int)_font}，中英同源：拉丁 "
                               + $"{D2Text.AtlasPath(_font)} / 中文 {ResPaths.FontChi(D2Text.SizeOf(_font))}）；"
                               + $"内容按原版 px 排版后整体 ×{Scale}；"
                               + $"按钮文字另按原版字号换算 ×{ButtonFontScale}（{18}/{16}，出处见该常量）");
                }
            }

            private void ClearInner()
            {
                if (_bitmap != null)
                {
                    _bitmap.Destroy();      // 连带销毁它自己的根节点
                    _bitmap = null;
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 10. 1:1 按钮：原版帧底图（0/1/2 = 常态/悬停/按下）+ 原版位图字体标签
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 一个"原版几何"按钮：底图 = 原版帧（`UiArt.Button` 负责取帧与 SpriteSwap），
        /// 文字 = <see cref="FlowLabel"/>（拉丁走位图字体、中文回退默认字体）。
        /// <para>
        /// 传尺寸给 `UiArt.Button` 时给的是**原版 px**（272×35 / 128×35 —— 正好等于原版帧
        /// 自身的像素尺寸），之后再按 ×1.8 放大 RectTransform。原因：`UiArt.ButtonStripFor`
        /// 的阈值（`WideButtonMinWidth = 200`）是**按原版帧宽**定的，若直接传放大后的 230.4
        /// 会被误判成宽按钮 ⇒ 中等按钮的底图被拉变形。这里不改 `UiArt.cs`（别的 agent 持有），
        /// 用"先按原版尺寸建、再放大矩形"的方式绕开。
        /// </para>
        /// </summary>
        internal sealed class FlowButton
        {
            /// <summary>底图 Image（原版帧 + SpriteSwap）。</summary>
            public Image Image;

            /// <summary>文字（位图字体 / 默认字体）。</summary>
            public FlowLabel Label;

            /// <summary>点击回调（用于个别按钮的自定义行为，如二次确认）。</summary>
            public Button Button;

            /// <summary>本颗按钮的**常态**文字色（默认 = <see cref="UiLayoutFlow.ButtonText"/>）。</summary>
            public Color NormalText = ButtonText;

            /// <summary>本颗按钮的**禁用态**文字色（默认 = <see cref="ButtonTextDisabled"/>）。</summary>
            public Color DisabledText = ButtonTextDisabled;

            /// <summary>造一个原版几何按钮。</summary>
            /// <param name="origSize">**原版 px** 尺寸（宽 ≥ 272 = 宽按钮底图；128 = 中等按钮底图）。</param>
            /// <param name="pos">本工程 Canvas 单位的位置（= 原版坐标 ×1.8）。</param>
            /// <param name="labelColor">
            /// 覆盖**常态文字色**（null = 用 <see cref="ButtonText"/>，即原版 `WideButton.prefab` 的 #191919）。
            /// <para> 加这个口子的原因（**量化过，不是口味**）：
            /// 原版按钮**底图本身是深板岩灰**（`Menu/btn_med_normal.png` 内区实测平均 sRGB 亮度 **0.376**，
            /// 量法 = `tools/probes/measure/btn_plate_luma.py`）⇒ #191919 压在它上面的对比度只有
            /// **2.79:1**（WCAG 2.1 AA 正文要求 ≥ 4.5:1）——拉丁细笔画还能认，**13px 的中文密笔画就糊成一块黑**。
            /// 所以：中文按钮（如传送点目的地）由调用方传一个**既有可读色常量**；拉丁按钮保持原版 #191919 不变
            /// （不动 `ButtonText` 本身 —— 它被 `uicheck` 的「按钮文字色 = #191919」那条断言钉着）。</para>
            /// <para>禁用态 = 传入色的同色降 alpha（与既有 `ButtonTextDisabled` 的 0.45 同口径）。</para>
            /// </param>
            public static FlowButton Create(Transform parent, string name, string text, Vector2 origSize,
                Vector2 pos, Action onClick, D2Text.D2Font font = D2Text.D2Font.Font16,
                Color? labelColor = null)
            {
                var normalText = labelColor ?? ButtonText;
                var disabledText = labelColor.HasValue
                    ? new Color(normalText.r, normalText.g, normalText.b, ButtonTextDisabled.a)
                    : ButtonTextDisabled;
                var img = UiArt.Button(parent, name, string.Empty, origSize, pos, onClick);
                if (img == null)
                {
                    UiLog.Error($"创建按钮 {name} 失败（UiArt.Button 返回 null）⇒ 该按钮缺失，交互不可用");
                    return null;
                }

                // 关掉 UiArt 挂的 uGUI 默认文字（内置字体 + fontSize 20）：1:1 要求文字走原版位图字体
                var builtin = UiArt.ButtonLabel(img);
                if (builtin != null)
                {
                    builtin.text = string.Empty;
                    builtin.gameObject.SetActive(false);
                }
                else
                {
                    UiLog.Warn($"按钮 {name} 没有 UiArt 的 Label 子节点（文字层已由 FlowLabel 接管）");
                }

                img.rectTransform.sizeDelta = Px(origSize);   // 原版 px → ×1.8

                var button = new FlowButton
                {
                    Image = img,
                    Button = img.GetComponent<Button>(),
                    NormalText = normalText,
                    DisabledText = disabledText,
                    //   出处与实测核对写在该常量的注释里）—— 原版 `WideButton.prefab` /
                    //   `MediumButton.prefab` 的 `Text.m_FontSize = 18` 就是这条口径的来源。
                    Label = FlowLabel.Create(img.transform, "FlowLabel", text, font, TextAnchor.MiddleCenter,
                        normalText, origSize, Vector2.zero, ButtonFontScale),
                };

                if (button.Button == null)
                {
                    UiLog.Warn($"按钮 {name} 上没有 Button 组件 ⇒ 只能点底图（UiArt.Button 应已挂上）");
                }

                return button;
            }

            /// <summary>
            /// 可用性（禁用 = 不可点 + 文字变灰；底图帧由 uGUI 的 `disabledSprite` 决定）。
            /// <para>用**本颗按钮自己的**常态/禁用色（`Create` 传入的 `labelColor`），
            /// 不许回落成全局 <see cref="ButtonText"/> —— 否则"先 SetEnabled(false) 再 SetEnabled(true)"
            /// 会把调用方特意指定的可读色悄悄改回近黑。</para>
            /// </summary>
            public void SetEnabled(bool on)
            {
                if (Button != null) Button.interactable = on;
                if (Label != null) Label.SetColor(on ? NormalText : DisabledText);
            }

            /// <summary>改文案。</summary>
            public void SetText(string text)
            {
                if (Label != null) Label.SetText(text);
            }

        }

        /// <summary>
        /// 一块**透明的原版热点**（点击区 = 原版矩形本身）。用于「按原版 RectTransform 1:1 复刻点击
        /// 区域」，同时不引入任何占位贴图（不可见 ⇒ 不会与背景原版贴图打架）。
        /// </summary>
        internal static Image Hotspot(Transform parent, string name, Vector2 origSize, Vector2 pos, Action onClick)
        {
            var img = UiArt.Panel(parent, name, Px(origSize), pos, new Color(0f, 0f, 0f, 0f), true);
            if (img == null)
            {
                UiLog.Error($"创建热点 {name} 失败（UiArt.Panel 返回 null）");
                return null;
            }

            if (onClick != null)
            {
                var btn = img.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => onClick());
            }
            return img;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 11. 屏适配容器 + 原版整屏贴图（×1.8 按高、水平居中、不拉伸）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 建「屏适配容器」：铺满面板根节点，整体 `localScale = <paramref name="fit"/>`（等比）。
        /// <para>
        /// 面板的**所有**内容（含原版整屏贴图）都挂在它下面 ⇒ 子节点坐标仍写"原版值 ×1.8"
        /// （离线断言逐条核对的就是这个），只有整块被统一缩放。改按高度 ×1.8 后，原版整屏 600×1.8 = 1080
        /// = 画布高 ⇒ 所有屏的 <see cref="FitOf"/> 都是 1（<see cref="FitClass"/> 不再是 0.75）。
        /// </para>
        /// </summary>
        public static RectTransform FitRoot(Transform panelRoot, float fit)
        {
            var rt = UIFactory.CreateNode("ScreenFit", panelRoot);
            rt.pivot = new Vector2(0.5f, 0.5f);          // 缩放围绕画布中心（否则整块会跑偏）
            rt.localScale = new Vector3(fit, fit, 1f);

            if (Mathf.Abs(fit - 1f) > 1e-3f)
                UiLog.Info($"[屏适配] 本屏内容整体 ×{fit}：原版 800×600 的整屏落到 " +
                           $"{OrigWidth * Scale * fit:0}×{OrigHeight * Scale * fit:0} 画布单位");
            return rt;
        }

        /// <summary>
        /// 原版整屏贴图（800×600）**按高度等比 ×1.8 = 1440×1080、水平居中**，后面垫一层铺满画布的纯色底。
        /// <para>
        /// 主 agent 2026 裁决：「缩放系数 = 1080/600 = 1.8（按高度等比）；水平居中，左右多出的空间交给相机
        /// （视野更宽，**不是把 UI 拉伸**）」⇒ 4:3 的原版整屏贴图**不许横向拉伸到 16:9**
        /// 现按 1.8 定尺居中：宽 800×1.8 = **1440**（左右各留 240 画布单位），高 600×1.8 = **1080**（正好铺满）。
        /// 左右两条留白由 <paramref name="fallback"/> 纯色底承担（不是黑边噪声，是原版暗色底）。
        /// </para>
        /// <para>贴图到位后由 `UiArt.SetSprite` 套**原版亮度（白）**，绝不提亮/压暗。</para>
        /// </summary>
        public static Image BackdropArt(Transform parent, string spritePath, Color fallback)
        {
            if (string.IsNullOrEmpty(spritePath))
            {
                UiLog.Error("BackdropArt 收到空贴图路径 ⇒ 该屏只有纯色底（调用方漏传 ResPaths？）");
                return UiArt.FullPanel(parent, "Backdrop", fallback, false);
            }

            // ① 铺满画布的纯色底（贴图缺失 / 左右留白时可见；不参与点击）
            UiArt.FullPanel(parent, "BackdropFill", fallback, false);

            // ② 原版整屏贴图：按高度 ×1.8 = 1440×1080、水平居中（**不拉伸**）
            var art = UiArt.Panel(parent, "Backdrop", new Vector2(OrigWidth * Scale, OrigHeight * Scale),
                Vector2.zero, Color.white, false);
            if (art == null)
            {
                UiLog.Error("BackdropArt 的贴图节点未建出来（UiArt.Panel 返回 null）⇒ 该屏只剩纯色底");
                return null;
            }

            UiArt.SetSprite(art, spritePath);   // 到位后套原版亮度（白）
            return art;
        }
    }
}
