// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/D2EngineTextHook.cs（引擎文字渲染后端挂钩；问题与判据 = E19）
//
// **问题（E19）**：引擎通用件（`ToastLayer` / `LoadingLayer` / `ConfirmLayer` / `FloatTextLayer` /
// `GuideLayer`）自己建的 `Text` 一律 `UIFactory.DefaultFont()` ⇒ 引擎自己的提示
// （「角色名已存在」「背包已满」「加载中...」「正在进入 Stage…」「确认 / 取消」）在画面上
// **显示不出来**（Unity 内置字体不含中文字模）。
//
// **本文件**：引擎新增挂钩 `CloverEngine.ITextHook` 的本项目实现。引擎在
// `UIFactory.CreateText` 把 Text 建好（文案 / 字号 / 对齐 / 颜色 / 溢出策略就位）后通知一次，
// 这里把它交给 `D2TextMirror` —— Text 留作**数据持有者**（`font = null` + `enabled = false`，
// 不产生任何绘制），画面由 `D2Label` 用原版字模画（与 `UiArt.Label` / `UiArt.Input` 的占位符 /
// `ItemTooltip` 同一套约定，见 `UI/D2TextMirror.cs` 文件头）。
//
// **装配点（两行，都在 `App/Bootstrap.cs`，顺序不能换）**：
//   ① `Install()` —— **在 `Game.Launch` 之前**：引擎 LoadingLayer 的「加载中...」是 Launch
//      期间建的（`new UIManager()` 的构造里），晚于它注册就永久漏掉那一条。
//   ② `FlushPending()` —— **紧跟 `CloverRes.Init`**：见下面第 ③ 条（资源闸门）。
//
// ── 三道闸门（都是"引擎那条同步通知"的必要防抖，不是可选优化）──────────────────
//   ① **重入闸门**：通知是在 `UIFactory.CreateText` 里**同步**发出的，而 `D2Text` 在
//      "字模在途 / 字模不可用"时会**自己再调一次 `UIFactory.CreateText`**（`D2Label.BuildFallback`
//      —— 那是"系统字体兜底"这条既有设计）。不拦住就会「建 Text → 通知挂钩 → 挂镜像 →
//      渲染 → 又建 Text → 又通知挂钩 → …」**无限递归 ⇒ StackOverflow**（比 E19 严重得多）。
//   ② **祖先闸门**：字模在途时 `D2Label` 造的那条兜底 Text 会挂在**被镜像的节点下面**
//      （祖先带 `D2TextMirror`）⇒ 跳过它，兜底那条才能真的用系统字体画出来
//      （它本来就是"字模还没到"的过渡；字模一到 `D2Text.RebuildAll` 就把它换成字模方块）。
//   ③ **资源闸门（最危险的一条）**：挂镜像会走到
//      `D2TextMirror.Attach → D2Label.Attach → D2Text.EnsureChi`，而 `EnsureChi` 在
//      **`Game.Res == null`** 时走 `OnAtlasFailure → D2Label.MarkBitmapUnavailable`
//      = 把**整个项目的字模**永久降级成系统字体（`_bitmapUnavailable` 是静态开关，一局之内不再恢复）。
//      而引擎 LoadingLayer 的标签恰恰是在 `Game.Launch` 期间建的，那时 `CloverRes.Init` **还没调用**
//      ⇒ 直接挂 = 一进 Play 就把全项目文字降级（画面立刻看得见，且只在日志里留一条 Error）。
//      ⇒ 判据与 `EnsureChi` 的失败条件**同一个**（`Game.Res == null`）：先**寄存**，等
//      `FlushPending()`（Bootstrap 里紧跟 `CloverRes.Init`，那时的唯一一条就是 LoadingLayer 的标签）。
//
// 本文件只有一个类、且**不是 MonoBehaviour**（`constraints.md` #1：一个 .cs 一个 MonoBehaviour）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>
    /// 引擎通用件建的 <see cref="Text"/> → 交给 <see cref="D2TextMirror"/> 用**原版字模**画。
    /// <para>判据（为什么"引擎建的 Text 一律挂镜像"而不是按文案挑）：引擎通用件的文案全部是本项目
    /// 的中文提示（见文件头），挂镜像后才满足「画面上没有一个像素由引擎默认字体产生」；
    /// 而**业务面板自己的 Text** 根本不经过这个挂钩（`UiArt.Label` / `UiArt.Input` 是
    /// 直接 `AddComponent<Text>()` 的），故不会有重复挂载。</para>
    /// </summary>
    internal sealed class D2EngineTextHook : ITextHook
    {
        /// <summary>重入闸门（见文件头 ①）：真值 = 正在挂镜像，本条直接放过（保持系统字体）。</summary>
        private static bool _attaching;

        /// <summary>资源闸门（见文件头 ③）：`Game.Res` 未就绪时收到的引擎 Text 先寄存。</summary>
        private static readonly List<Text> Pending = new List<Text>();

        /// <summary>寄存分支只报一次（启动期预期分支，不该刷屏也不该静默）。</summary>
        private static bool _parkedLogged;

        /// <summary>
        /// 装配入口（`Bootstrap` 在 `Game.Launch` **之前**调用）：复位跨局静态残留
        /// （工程若开了「Enter Play Mode Options（不重载域）」，静态会跨局保留 —— 装配见 `App/Bootstrap.cs` 的 P-3）
        /// 并注册挂钩。
        /// </summary>
        public static void Install()
        {
            _attaching = false;
            _parkedLogged = false;
            Pending.Clear();
            TextHooks.Current = new D2EngineTextHook();
        }

        /// <inheritdoc/>
        public void OnTextCreated(Text text)
        {
            if (text == null) return;

            // ① 重入：这一条是 `D2Label` 为"字模在途 / 字模不可用"造的兜底 Text（或别的嵌套创建）
            if (_attaching) return;

            // ② 祖先已带镜像：同上，兜底 Text 挂在被镜像的节点下 ⇒ 不该镜像它
            if (text.GetComponentInParent<D2TextMirror>(true) != null) return;

            // ③ 资源未就绪：现在挂会把字模整体永久降级（见文件头 ③）⇒ 寄存，等 `FlushPending`
            if (Game.Res == null)
            {
                Pending.Add(text);
                if (!_parkedLogged)
                {
                    _parkedLogged = true;
                    Game.Logger?.Info("UI",
                        "文字渲染挂钩：资源模块尚未就绪（Game.Res == null）⇒ 本次引擎 Text 寄存待补挂"
                        + "（正常就是 LoadingLayer 的「加载中...」；挂早了会把字模整体降级成系统字体）");
                }
                return;
            }

            Attach(text);
        }

        /// <summary>
        /// 资源模块就绪后补挂寄存的引擎 Text。`Bootstrap` 在 `CloverRes.Init(ResPaths.Root)` **之后**
        /// 调用一次（那时 `Game.Res` 才有值 —— 与 `D2Text.EnsureChi` 的失败条件是同一个判据）。
        /// </summary>
        public static void FlushPending()
        {
            if (Pending.Count == 0) return;

            if (Game.Res == null)
            {
                // 非预期分支：说明装配顺序被改坏了（`FlushPending` 早于 `CloverRes.Init`）⇒ 不静默
                Game.Logger?.Warn("UI",
                    $"文字渲染挂钩：FlushPending 时 Game.Res 仍为 null ⇒ {Pending.Count} 条引擎 Text"
                    + "继续寄存（维持引擎内置字体，不挂字模镜像；请检查 Bootstrap 里 FlushPending 的位置）");
                return;
            }

            var attached = 0;
            for (var i = 0; i < Pending.Count; i++)
            {
                var t = Pending[i];
                if (t == null) continue;          // Launch 期建的节点若已销毁（`== null` 即 Unity 的"已销毁"）
                Attach(t);
                attached++;
            }
            Pending.Clear();
            Game.Logger?.Info("UI", $"文字渲染挂钩：资源模块就绪后补挂 {attached} 条引擎 Text");
        }

        /// <summary>挂镜像（档位按**引擎给的字号**选，与 `UiArt.Label` 同口径）。</summary>
        private static void Attach(Text text)
        {
            _attaching = true;
            try
            {
                // `PlaceholderFor = null`：引擎通用件没有输入框占位符那种形态
                D2TextMirror.Attach(text, D2Text.FontFor(text.fontSize), null);
            }
            finally
            {
                _attaching = false;
            }
        }
    }
}
