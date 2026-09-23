// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/D2ConfirmPanel.cs   ★ 本项目新增（缺陷：二次确认弹窗是引擎默认 uGUI）
// 站点：无（弹窗，不在 FSM 站点表里）。层：**Top**（与引擎确认框同一层，见下）。预制体：
// `Resources/UI/D2ConfirmPanel`。
//
// **缺陷（实机图 `.ai-tmp/screenshots/x_b3_delete_confirm.png`）**：选角屏的「删除角色」二次确认、
// 暂停菜单的「回主菜单」二次确认，原先都调引擎通用件 `Game.UI.Confirm(...)`
// （`clover-client-unity-engine/Runtime/Presentation/UI.cs:318` → `ConfirmLayer.Build`，
// `Runtime/Presentation/UIWidgets.cs:617-650`）——它画的是**引擎默认 uGUI**：
// 深灰方块（`new Color(0.11f,0.13f,0.18f,0.99f)`）+ 两颗纯蓝按钮
// （`new Color(0.16f,0.44f,0.78f)`）。同一屏的角色行按钮却是**原版石雕按钮**
// ⇒ **一屏两种风格**，不是 1:1。
//
// **为什么不是"给 `Game.UI.Confirm` 换皮"**（⛔ 先把这条路证伪，不然就是在绕远）：
//   · `IUIManager.Confirm` 的签名（`Runtime/Core/PresentationContracts.cs:111`）**只有**
//     `title / message / onConfirm / onCancel / confirmText / cancelText` —— **没有任何外观参数**；
//   · 画这个框的 `ConfirmLayer` 是引擎包里的 `internal sealed` 类，几何（`760×420` 面板、
//     `260×84` 按钮、`(±150,-148)`）与配色**全部硬编码在 `Build` 里**，项目侧改不到。
//   ⇒ 只能**项目侧自建**一个面板（本文件），底板/按钮都换成原版素材。
//
// **外观口径（每个数都有出处，⛔ 无自创几何/颜色）**：
//   · 底板 = **原版 `MENU/boxpieces.DC6` 拼装窗框**，素材 = `ResPaths.PanelBoxFramePause`
//     （288×180 原版px × 1.8 = 518.4×324 画布单位，**1:1 不拉伸**）。
//     尺寸/位置全部走 `UiLayoutFlow.Confirm`（§6b），那里逐条写了"由暂停菜单那一套原版度量派生"；
//   · 两颗按钮 = **原版中等按钮帧**（`ResPaths.BtnMedNormal` / `BtnMedSel` / `BtnMedPressed`，
//     128×35，由 `UiArt.Button` 做 `SpriteSwap`）+ 原版位图字体标签（`FlowButton`）；
//   · 文字 = 原版位图字模（标题 `font24` / 正文 `font16`，中英同源，见 `UiLayoutFlow.FlowLabel`）；
//   · 颜色 = **复用既有项目常量** `UiArt.PanelBg`（框内深色底）/ `UiArt.Overlay`（整屏遮罩，
//     与 `PausePanel` 同一档）/ `UiArt.TitleColor` / `UiArt.TextColor` —— ⛔ 不新增任何颜色。
//
// **层级 = `Top`**：出处 = 引擎自己的确认框也是挂 `Top`（`UI.cs:93`，那里有整段注释解释为什么
// 从 `Popup` 改到 `Top`：暂停菜单/死亡屏在 `Top`，确认框挂 `Popup` 会被它们盖住、鼠标点不到）。
// 后开的面板在同一层里兄弟序在后 ⇒ 弹窗压在暂停菜单之上。
//
// **排队语义 = 与引擎 `ConfirmLayer` 一致**（同时只显示一个，后到的排队）：
//   引擎的实现见 `UIWidgets.cs:546-573`（`Queue<Request>` + `PopNext`）。本面板复刻同一语义：
//   `Show(...)` 入队后调 `Game.UI.Open<D2ConfirmPanel>()`；已打开时引擎会走
//   `UIManager.Open` 的"已存在分支"（`UI.cs:119-128`：置顶 + 再调一次 `OnOpen(param)`）
//   ⇒ `OnOpen` 里发现"正有一条在显示"就把新请求留在队列里，等这条结束再出。
//   关闭（`CloseAll` / 场景切换）时**当前这条与队列里每一条都按「取消」回调**——
//   与引擎 `ConfirmLayer.CloseAll`（`UIWidgets.cs:576-595`）逐条同口径：
//   业务在取消回调里解锁的状态（按钮置灰、Loading 引用计数…）不能被吞掉。
//
// ⚠️ 本文件只有一个类、且是 MonoBehaviour（`constraints.md` #1：一个 .cs 一个 MonoBehaviour）。
// ⛔ 不引用任何业务模块（分层自检 ③）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using UnityEngine;

namespace Diablo2.UI
{
    /// <summary>
    /// 原版风格的二次确认弹窗（原版窗框 + 原版中等按钮 + 原版位图字体）。
    /// <para>用法与引擎确认框**同形**（调用点只需换 `Game.UI.Confirm(` → `D2ConfirmPanel.Show(`）：</para>
    /// <code>
    /// D2ConfirmPanel.Show("删除角色", "确定删除角色「X」？该操作不可撤销。",
    ///     () => { /* 确认 */ }, () => { /* 取消 */ });
    /// </code>
    /// </summary>
    public class D2ConfirmPanel : UIPanel
    {
        /// <summary>`Show(...)` 的一条请求（与引擎 `ConfirmLayer.Request` 同字段口径）。</summary>
        public class Args
        {
            /// <summary>标题（空 ⇒ 用 <see cref="DefaultTitle"/>，同引擎 `ui.tip` 的兜底）。</summary>
            public string title;

            /// <summary>正文。</summary>
            public string message;

            /// <summary>确认按钮文案（空 ⇒ <see cref="DefaultConfirm"/>）。</summary>
            public string confirmText;

            /// <summary>取消按钮文案（空 ⇒ <see cref="DefaultCancel"/>）。</summary>
            public string cancelText;

            /// <summary>点确认后的回调（可为 null）。</summary>
            public Action onConfirm;

            /// <summary>点取消 / 被关闭时的回调（可为 null）。</summary>
            public Action onCancel;
        }

        /// <summary>标题缺省值（= 引擎确认框 `UIFactory.Localize("ui.tip", "提示")` 的兜底文案）。</summary>
        public const string DefaultTitle = "提示";

        /// <summary>确认按钮缺省文案（= 引擎确认框 `UIFactory.Localize("ui.confirm", "确认")` 的兜底文案）。</summary>
        public const string DefaultConfirm = "确认";

        /// <summary>取消按钮缺省文案（= 引擎确认框 `UIFactory.Localize("ui.cancel", "取消")` 的兜底文案）。</summary>
        public const string DefaultCancel = "取消";

        /// <summary>排队中的请求（静态 = 跨面板实例存活；同时只显示一个，见文件头）。</summary>
        private static readonly Queue<Args> Pending = new Queue<Args>();

        private bool _built;
        private bool _closed;

        /// <summary>正在显示的那条请求（null = 没有在显示）。</summary>
        private Args _current;

        private UiLayoutFlow.FlowLabel _titleLabel;
        private UiLayoutFlow.FlowLabel _messageLabel;
        private UiLayoutFlow.FlowButton _cancelButton;
        private UiLayoutFlow.FlowButton _confirmButton;

        /// <summary>
        /// 层：<see cref="UILayer.Top"/> —— 与引擎确认框同层（出处与理由见文件头）。
        /// </summary>
        public override UILayer Layer => UILayer.Top;

        /// <summary>
        /// 弹一个原版风格的二次确认框（排队语义见文件头）。
        /// <para>⛔ 调用方**不要**再调 `Game.UI.Confirm`：那会画出引擎默认 uGUI（一屏两种风格）。</para>
        /// </summary>
        public static void Show(string title, string message, Action onConfirm, Action onCancel = null,
            string confirmText = null, string cancelText = null)
        {
            Pending.Enqueue(new Args
            {
                title = title,
                message = message,
                confirmText = confirmText,
                cancelText = cancelText,
                onConfirm = onConfirm,
                onCancel = onCancel,
            });

            if (Game.UI == null)
            {
                // 非预期分支：UI 门面还没起来（Launch 之前）⇒ 点名并**如实回调取消**，
                // 不能让业务等在确认回调里的状态永久卡住。
                Fire(onCancel);
                Pending.Clear();
                UiLog.Error("D2ConfirmPanel.Show 时 Game.UI 未初始化 ⇒ 弹窗无法显示，已按「取消」回调（请在 Game.Launch 之后调用）");
                return;
            }

            // 已打开 ⇒ 引擎走"已存在分支"（置顶 + 再调一次 OnOpen），本面板据 _current 决定是否出下一条。
            Game.UI.Open<D2ConfirmPanel>();
        }

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            _closed = false;
            UiArt.PrepareRoot(Root);
            Build();

            if (_current != null) return;   // 正有一条在显示 ⇒ 新请求留在队列里（排队语义）
            ShowNext();
        }

        /// <inheritdoc/>
        public override void OnClose()
        {
            _closed = true;

            // 与引擎 `ConfirmLayer.CloseAll`（UIWidgets.cs:576-595）同口径：当前这条 + 队列里每一条
            // 都按「取消」回调，业务不会被吞掉回调而卡住。
            var current = _current;
            _current = null;
            Fire(current != null ? current.onCancel : null);

            while (Pending.Count > 0)
            {
                var dropped = Pending.Dequeue();
                Fire(dropped.onCancel);
            }
        }

        // ── 队列推进 ────────────────────────────────────────────────────────

        /// <summary>出队下一条；队列空则关闭本面板。</summary>
        private void ShowNext()
        {
            if (_closed) return;

            if (Pending.Count == 0)
            {
                Game.UI.Close<D2ConfirmPanel>();
                return;
            }

            _current = Pending.Dequeue();
            Apply(_current);
        }

        /// <summary>按钮点击（<paramref name="confirmed"/> = 确认 / 取消）。</summary>
        private void Finish(bool confirmed)
        {
            var req = _current;
            _current = null;

            // 先回调再推进：回调里可能又 `Show(...)`（`Show` → `Open` → `OnOpen` 会把下一条建出来），
            // 那时 `_current != null`，下面这一步就不会二次出队（否则会吞掉一条请求）。
            if (req != null) Fire(confirmed ? req.onConfirm : req.onCancel);

            if (_closed) return;        // 回调里把面板关了/场景切了
            if (_current == null) ShowNext();
        }

        /// <summary>把一条请求写到界面上。</summary>
        private void Apply(Args a)
        {
            var title = string.IsNullOrEmpty(a.title) ? DefaultTitle : a.title;
            var confirm = string.IsNullOrEmpty(a.confirmText) ? DefaultConfirm : a.confirmText;
            var cancel = string.IsNullOrEmpty(a.cancelText) ? DefaultCancel : a.cancelText;

            _titleLabel.SetText(title);
            _messageLabel.SetText(a.message ?? string.Empty);
            _confirmButton.SetText(confirm);
            _cancelButton.SetText(cancel);

            var length = a.message == null ? 0 : a.message.Length;
            UiLog.Info($"确认弹窗（原版窗框 boxframe_pause + 原版中等按钮）：标题「{title}」/"
                       + $"正文 {length} 字 / 按钮「{cancel}」「{confirm}」");
        }

        /// <summary>回调隔离（⛔ 不叫 `Invoke`：与 `MonoBehaviour.Invoke` 重名容易看错）。</summary>
        private static void Fire(Action action)
        {
            if (action == null) return;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // 非预期分支：回调抛异常不能让弹窗卡住（下一条请求还要出）⇒ 留痕后吞掉。
                UiLog.Error($"确认弹窗回调异常：{ex.Message}", ex);
            }
        }

        // ── 构建 ────────────────────────────────────────────────────────────

        private void Build()
        {
            if (_built) return;
            _built = true;

            // ① 整屏遮罩（吃掉点底层的点击；与 `PausePanel` 用同一档 `UiArt.Overlay`，
            //    **不参与屏适配缩放**）。引擎确认框的遮罩是 0.6 黑，本项目统一用既有常量。
            UiArt.FullPanel(transform, "Shade", UiArt.Overlay, true);

            // ② 屏适配容器（弹窗内容全在原版 |y| ≤ 108 内 ⇒ 系数 = 1，同 `Pause`/`Settings`）
            var screen = UiLayoutFlow.FitRoot(transform, UiLayoutFlow.FitMenu);

            // ③ 底板两层：框内深色底（`UiArt.PanelBg`，与暂停/选项同档）+ **原版窗框**（后建 ⇒ 压在底色上）
            //    ⛔ 层序不能反：`PausePanel` / `SettingsPanel` 也是"底色 → 窗框 → 内容"。
            UiArt.Panel(screen, "Box", UiLayoutFlow.Confirm.BoxSize, UiLayoutFlow.Confirm.BoxPos,
                UiArt.PanelBg, true);
            UiArt.Art(screen, "BoxFrame", ResPaths.PanelBoxFramePause,
                UiLayoutFlow.Confirm.BoxSize, UiLayoutFlow.Confirm.BoxPos);

            // ④ 标题 / 正文（原版位图字体；尺寸参数是**原版 px**，口径见 `FlowLabel` 的注释）
            _titleLabel = UiLayoutFlow.FlowLabel.Create(screen, "Title", DefaultTitle, D2Text.D2Font.Font24,
                TextAnchor.MiddleCenter, UiArt.TitleColor,
                UiLayoutFlow.Confirm.TitleSizeOrig, UiLayoutFlow.Confirm.TitlePos);

            _messageLabel = UiLayoutFlow.FlowLabel.Create(screen, "Message", string.Empty, D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, UiArt.TextColor,
                UiLayoutFlow.Confirm.MessageSizeOrig, UiLayoutFlow.Confirm.MessagePos);

            // ⑤ 两颗按钮 = 原版中等按钮（128×35 ⇒ 底图走 `ResPaths.BtnMed*` 那条分支）
            _cancelButton = UiLayoutFlow.FlowButton.Create(screen, "Cancel", DefaultCancel,
                UiLayoutFlow.MediumButtonOrig, UiLayoutFlow.Confirm.CancelPos, () => Finish(false));

            _confirmButton = UiLayoutFlow.FlowButton.Create(screen, "Confirm", DefaultConfirm,
                UiLayoutFlow.MediumButtonOrig, UiLayoutFlow.Confirm.ConfirmPos, () => Finish(true));

            UiLayoutFlow.LogTable(nameof(D2ConfirmPanel));
        }
    }
}
