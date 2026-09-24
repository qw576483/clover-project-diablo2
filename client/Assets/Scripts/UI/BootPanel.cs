// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/BootPanel.cs
//
//   **没有对应 prefab / 贴图**（`Prefabs/` 下无 logo 屏，素材清单里也没有该图）
//   ⇒ 本面板整体是**本项目新增**：坐标为本工程 1920×1080 口径（无原版值可换算），
//     所以这些坐标**不进 `UiLayoutFlow` 的原版对照表**（避免拿"假的原版值"冒充依据）。
//   `data/global/ui/Logo/logo.DC6`（实测单帧 **319×177** = DIABLO II 火焰字标，读图确认），
//   唯一按 1:1 口径处理的是**字体**：英文出品字走**原版位图字体**（font42 档），
//   与整屏贴图的放大倍率一致；色调 = 原版亮度（白），不做提亮/压暗。
//
//   色 (0.78,0.76,0.72)。几何统一走 `UiLayoutFlow.Brand`（与主菜单底部那行**共用同一套值**）。
//
// 职责：原版启动屏的等价物 —— ① 出品字 + 版本 + 版权提示；② 「按任意键继续」；
//   ③ 任意键/点击 → 发 `Events.BootDone`。
// 只发/收事件：本面板**不引用任何业务模块**（分层自检 ③）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using UnityEngine;

namespace Diablo2.UI
{
    /// <summary>启动画面面板：任意键（或鼠标点击）继续 → `Events.BootDone`。</summary>
    public class BootPanel : UIPanel
    {
        /// <summary>最短停留时间（秒）——避免启动瞬间的按键把启动屏直接跳过去。</summary>
        private const float MinShowSeconds = 0.6f;

        /// <summary>
        /// 层：<see cref="UILayer.Normal"/>（= 文件头「层：Normal」）。
        /// <para>基类 `UIPanel.Layer` 的默认值**就是** Normal
        /// （`clover-client-unity-engine/Runtime/Core/PresentationContracts.cs:174`
        /// `public virtual UILayer Layer => UILayer.Normal;`）⇒ 本行**行为不变**。
        /// 显式写出来的理由：`uicheck` 的 `PanelSpec` 只看**面板自己声明的层**，
        /// 不写就查不到「本屏在 Normal 层」（见 `uicheck` §⑲）。</para>
        /// </summary>
        public override UILayer Layer => UILayer.Normal;

        // ── 布局（本项目新增：本工程 1920×1080 口径，无原版值；见文件头说明）────────
        //   这些数**同时登记在 `UiLayoutFlow` 的对照表**（`Panel.Boot`）里，
        //   由 `uicheck` 断言"×1.8 口径下全部落在 1920×1080 画布内"（|y| ≤ 540、|x| ≤ 960）。
        //   实测 319×177 = DIABLO II 火焰字标）—— 资源欠缺清单 #23（"启动屏 logo 不在手上"）已作废：
        //   尺寸**按原版像素 1:1**（319×177）再 ×1.8 放大 ⇒ 不拉伸、不变形。
        private static readonly Vector2 LogoOrigSize = new Vector2(319f, 177f);
        private static readonly Vector2 LogoSize = UiLayoutFlow.Px(LogoOrigSize);  // = 574.2×318.6（换算走 UiLayoutFlow）
        private static readonly Vector2 GameSize = new Vector2(1260f, 36f);
        private static readonly Vector2 HintSize = new Vector2(900f, 36f);
        private static readonly Vector2 CopySize = new Vector2(1260f, 72f);
        private static readonly Vector2 VersionSize = new Vector2(540f, 36f);

        //      结构性保证；主菜单的登记行在 `UiLayoutFlow.BuildTable()` 的 `Panel.Menu` 段）；
        //   ② 它的值同时也被 `UiLayoutFlow.Table` 里 `Panel.Boot` 那条引用 ⇒ 面板与对照表**不可能
        //      各说一套**（2026 那次的漏法正是"面板里有、表里没有"）。
        //      (同尺寸 @ -300) **重叠 48px**（y 区间 -360..-288 撞 -336..-264），且**不在**对照表里
        //      ⇒ 离线画布断言抓不到。现挪到底部最下一行 y=-462（底边 -489，画布底 -540，留白 51）
        //      ⇒ 与版权行(底边 -336)/版本行(底边 -385.5)都不重叠。

        private float _shownAt;
        private bool _built;
        private bool _done;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();

            _shownAt = Time.unscaledTime;
            _done = false;
            Log.Info("Ui", $"[Flow] → {Events.Fsm.StateBoot}：启动画面已显示，等待任意键/点击");
        }

        /// <inheritdoc/>
        public override void OnUpdate(float dt)
        {
            if (_done) return;
            if (Time.unscaledTime - _shownAt < MinShowSeconds) return;

            if (Game.Input == null)
            {
                Log.WarnOnce("Ui", "boot.no_input", "Game.Input 未挂载（CloverInput.Init 未调用），启动画面无法按键继续");
                return;
            }

            if (!AnyKeyDown()) return;

            _done = true;
            Log.Info("Ui", "启动画面：检测到任意键/鼠标点击 → 继续");
            Game.Event.Emit(Events.BootDone);
        }

        /// <summary>遍历引擎按键全集判「任意键」（后端无关，不写死具体按键）。</summary>
        private static bool AnyKeyDown()
        {
            for (var k = GameKey.None + 1; k <= GameKey.MouseMiddle; k++)
            {
                if (Game.Input.GetKeyDown(k)) return true;
            }
            return false;
        }

        private void Build()
        {
            if (_built) return;
            _built = true;

            // 启动屏不切场景：Boot 场景只放 Bootstrap + Canvas，这里用纯色底 + 出品字
            // （原版 logo 图不在手上 ⇒ 登记在 `client/资源欠缺清单.md`，不拿通用素材顶替）。
            UiArt.FullPanel(transform, "Bg", new Color(0.02f, 0.02f, 0.03f, 1f), true);

            // 屏适配容器（启动屏内容都在画布 ±540/±960 内 ⇒ 系数 = 1，即纯 ×1.8）
            var screen = UiLayoutFlow.FitRoot(transform, UiLayoutFlow.FitMenu);

            // 原版 logo（DIABLO II 火焰字标 319×177，1:1 原版像素 ×1.8）—— 替掉旧的纯文字"出品字"。
            //   位置 (0,180)：占屏上半、居中，与下面的标题/提示/版权/署名各行两两不重叠（见 uicheck 断言）。
            //   贴图走 `UiArt.Art` ⇒ 加载成功即套**原版亮度（白）**，不提亮/压暗；缺失时保留白底 + Warn。
            //
            //     `ResPaths.MenuLogo` 是**帧名前缀**（= `D2/UI/Logo/logo`），它的注释（`ResPaths.cs`
            //     （实测 `Assets/Resources/Clover/D2/UI/Logo/` 下只有 `logo_0.png`）。
            //     直接请求 `D2/UI/Logo/logo` ⇒ 引擎资源模块打 Error
            //     `[Error] [Resource] 加载失败：D2/UI/Logo/logo`（实测 16:21:23.122），启动屏 logo 不显示。
            //   `QuestLogPanel` 取 `questsocket_{0,1}` 同一口径）：
            //   `FrameCountMenuLogo = 1` 就是这条路径的帧数上界。**素材与常量值都没改**。
            UiArt.Art(screen, "Logo", ResPaths.Frame(ResPaths.MenuLogo, 0), LogoSize, new Vector2(0f, 180f));

            UiArt.Label(screen, "Game", "暗黑破坏神 II · 复刻（Act I 起始两图 + 主线任务 1）",
                UiLayoutFlow.ChineseFontSize(D2Text.D2Font.Font16), TextAnchor.MiddleCenter, UiArt.TextColor,
                GameSize, new Vector2(0f, -30f));

            //   提示行再放 -45 就会与标题行**重叠 21px**（uicheck 的"两两不重叠"断言会红，实测）。
            //   整屏元素：logo 180 / 标题 -30 / 提示 -120 / 版权 -300 / 版本 (-414,-367.5) / 署名 -462。
            UiArt.Label(screen, "Hint", "按任意键 或 点击鼠标继续",
                UiLayoutFlow.ChineseFontSize(D2Text.D2Font.Font16), TextAnchor.MiddleCenter, UiArt.ButtonText,
                HintSize, new Vector2(0f, -120f));

            //   色 (0.62,0.60,0.56) 在 1080p 实机图 `Screenshots/p29_r2_01_boot.png` 里几乎看不见，
            //   用户据此判定"首界面没有 by clover-engine"（原话见 `UiLayoutFlow.Brand` 的注释）。
            //   位置 (0,-462) 与框宽 900 不变（框高 36→54，见 `Brand.ByLineOrigSize` 的推导）。
            //   2026 品牌署名轮：`forceChi: true` —— **原版拉丁字模不分大小写**，`by clover-engine`
            //   经 `font24` 会画成 `BY CLOVER-ENGINE`（实测：`font24_98`＝小号 B、`font24_121`＝小号 Y，
            //   且 g/p/q/y 无降部）。逐字小写只有原版 `font24_chi` 能画（实测 97＝真 a、121＝带降部 y）
            //   ⇒ 这一行走 chi 档。字号/颜色/位置/节点名**一个数没动**（仍是 font24 档 43 画布px）。
            UiArt.Label(screen, "ByLine",
                UiLayoutFlow.Brand.ByLineText,
                UiLayoutFlow.ChineseFontSize(D2Text.D2Font.Font24), TextAnchor.MiddleCenter,
                UiLayoutFlow.Brand.ByLineColor, UiLayoutFlow.Brand.ByLineSize,
                UiLayoutFlow.Brand.ByLinePos, forceChi: true);

            UiArt.Label(screen, "Copyright",
                "原版素材版权归 Blizzard North / Blizzard Entertainment（非商用）",
                UiLayoutFlow.ChineseFontSize(D2Text.D2Font.Font16), TextAnchor.MiddleCenter,
                new Color(0.62f, 0.60f, 0.56f, 1f), CopySize, new Vector2(0f, -300f));

            UiArt.Label(screen, "Version", $"单机版 · 存档版本 v{GameConst.SaveVersion}",
                UiLayoutFlow.ChineseFontSize(D2Text.D2Font.Font16), TextAnchor.MiddleLeft,
                new Color(0.62f, 0.60f, 0.56f, 1f), VersionSize, new Vector2(-414f, -367.5f));

            UiLayoutFlow.LogTable(nameof(BootPanel));
        }
    }
}
