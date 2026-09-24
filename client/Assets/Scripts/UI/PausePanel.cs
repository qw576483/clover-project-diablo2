// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/PausePanel.cs
// 站点：Pause（游戏内 ESC 菜单）。层：**Top**。预制体：`Resources/UI/PausePanel`。
//
//   里 `GameMenu/Buttons` 的 4 个槽位（`WideButton` 272×35、x=0、行节奏 原版 35+10=45）。
//   为什么复用：原版 ESC 菜单在 Diablerie 里**没有独立 prefab**（`Prefabs/` 下只有 `Menu/` 四个
//   文件），本项目里唯一有原版依据的菜单按钮几何就是它 ⇒ 不另造一套尺寸/间距。
//     第 1 项 原版 (0,-17.5) → 本工程 (0,-31.5)  = 继续
//     第 2 项 原版 (0,-62.5) → 本工程 (0,-112.5) = 选项
//     第 3 项 原版 (0,-107.5)→ 本工程 (0,-193.5) = 保存并退出
//     第 4 项 原版 (0,-152.5)→ 本工程 (0,-274.5) = 回主菜单（行节奏 45 原版px ×1.8 = 81）
//   按钮底图 = 原版帧（常态/悬停/按下）；文字 = 原版位图字体（英文），色 #191919（原版 prefab 值）；
//   色调 = 原版亮度（白）；整屏半透明遮罩为本项目新增（否则底下的 HUD 会误导点击）。
//   · 真暂停由 Flow 在进入 Pause 站点时设 `Time.timeScale = 0`
//     ⇒ 本面板**不使用任何定时器**（若将来要用，必须 `AfterUnscaled/EveryUnscaled`，`constraints.md` #2）。
//   · ESC 再按一次 = 继续（Flow 在 Pause 站点的 onTick 处理，timeScale=0 下也能响应）。
//   · 「保存并退出」「回主菜单」都由 Flow 处理（Flow 负责存档与清场）。
//     与同一屏的原版石雕按钮**两种风格**（实机图 `.ai-tmp/screenshots/x_b3_delete_confirm.png`）。
// 不引用任何业务模块。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using UnityEngine;

namespace Diablo2.UI
{
    /// <summary>暂停菜单面板（原版菜单按钮几何 + 本项目新增的遮罩/提示）。</summary>
    public class PausePanel : UIPanel
    {
        /// <summary>按钮文案（英文 = 原版位图字体）。</summary>
        private static class Text
        {
            public const string Resume = "CONTINUE";
            public const string Options = "OPTIONS";
            public const string SaveExit = "SAVE & EXIT";
            public const string ToMain = "MAIN MENU";
            public const string Hint = "PRESS ESC TO CONTINUE";
        }

        private bool _built;

        /// <inheritdoc/>
        public override UILayer Layer => UILayer.Top;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();
            UiLayoutFlow.LogTable(nameof(PausePanel));
            Log.Info("Ui", $"暂停菜单已打开（Time.timeScale={Time.timeScale:0.##}）");
        }

        private void Build()
        {
            if (_built) return;
            _built = true;

            // 整屏遮罩：吃掉点击，避免暂停时误点到底下的 HUD（本项目新增；**不参与屏适配缩放**）
            UiArt.FullPanel(transform, "Shade", UiArt.Overlay, true);

            // 屏适配容器（暂停菜单复用主菜单按钮列 ⇒ 内容都在原版 |y| ≤ 225 内 ⇒ 系数 = 1）
            var screen = UiLayoutFlow.FitRoot(transform, UiLayoutFlow.FitMenu);

            // w5：底板 = **原版 `MENU/boxpieces.DC6` 拼装的窗框**（288×180 原版px，×1.8 定尺
            //   ⇒ 与素材 1:1，不拉伸）+ 一层框内深色底。
            //   口径/出处见 `tools/d2codec/assemble_boxpieces.py` 与 `UiLayoutFlow.BoxFrame`；
            //   **尺寸不是拍的**：= 4 个菜单按钮的外接框 + 2×框厚后吸附 12 拼装网格
            //   （推导逐条写在 `UiLayoutFlow.Pause.BoxSize` 的注释里）。
            UiArt.Panel(screen, "Box", UiLayoutFlow.Pause.BoxSize, UiLayoutFlow.Pause.BoxPos,
                UiArt.PanelBg, true);
            UiArt.Art(screen, "BoxFrame", ResPaths.PanelBoxFramePause,
                UiLayoutFlow.Pause.BoxSize, UiLayoutFlow.Pause.BoxPos);

            // 4 个按钮 = 原版主菜单那 4 个槽位的精确几何（272×35、x=0、节奏 45 原版px）
            UiLayoutFlow.FlowButton.Create(screen, "Resume", Text.Resume,
                UiLayoutFlow.WideButtonOrig, UiLayoutFlow.Pause.ResumePos,
                () => Game.Event.Emit(Events.ResumeRequest));

            UiLayoutFlow.FlowButton.Create(screen, "Options", Text.Options,
                UiLayoutFlow.WideButtonOrig, UiLayoutFlow.Pause.OptionsPos,
                () => Game.UI.Open<SettingsPanel>());

            UiLayoutFlow.FlowButton.Create(screen, "SaveExit", Text.SaveExit,
                UiLayoutFlow.WideButtonOrig, UiLayoutFlow.Pause.SaveExitPos,
                () => Game.Event.Emit(Events.SaveAndExitRequest));

            UiLayoutFlow.FlowButton.Create(screen, "ToMain", Text.ToMain,
                UiLayoutFlow.WideButtonOrig, UiLayoutFlow.Pause.ToMainPos,
                //   **原版窗框 + 原版中等按钮**的 `D2ConfirmPanel`（理由与口径见该文件头）。
                () => D2ConfirmPanel.Show("回主菜单", "回主菜单将放弃未保存的进度，确定继续？", () =>
                {
                    Log.Info("Ui", "暂停菜单：已确认回主菜单");
                    Game.Event.Emit(Events.ToMainMenuRequest);
                }, () => Log.Info("Ui", "暂停菜单：取消回主菜单")));

            // 底部提示（本项目新增；原版 ESC 菜单没有这一行）
            UiLayoutFlow.FlowLabel.Create(screen, "Hint", Text.Hint, D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, UiArt.TextColor, new Vector2(400f, 20f), UiLayoutFlow.Pause.HintPos);
        }
    }
}
