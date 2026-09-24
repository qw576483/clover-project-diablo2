// ─────────────────────────────────────────────────────────────────────────────
//
// 把**已存在的一个 uGUI `Text` 节点**改造成"用原版字模画"的节点。
//
// 为什么要这么绕（而不是把全项目 `Text` 全换成别的类）：
//   · 工程里的面板都按 uGUI `Text` 的成员写（`_x.text = ...` / `.color` / `rectTransform` /
//     `horizontalOverflow` / `resizeTextForBestFit`），共 14 个文件、上百处；
//   · uGUI 的 `Text` 只要 `enabled = false` + `font = null` 就**不产生任何绘制**，但它仍是
//     "文案 / 颜色 / 对齐 / 溢出策略"的**数据持有者**。
//   ⇒ 于是：数据留在 `Text` 上，**画面由 `D2Label` 用原版字模画**。
//     实机判据：画面上没有一个像素由引擎默认字体产生（截图 + 日志逐帧对照）。
//
// 逐帧只做"变了才重画"（字符串/颜色/字号/对齐/换行策略的**变化检测**），不是每帧重建。
// 本文件只有一个 MonoBehaviour（`constraints.md` #1：一个 `.cs` 不许放多个 MonoBehaviour）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>把 uGUI `Text` 的数据镜像成**原版位图字模**的渲染器。</summary>
    internal sealed class D2TextMirror : MonoBehaviour
    {
        /// <summary>数据持有者（**必须** `font == null` 且 `enabled == false`：它只存数据、不绘制）。</summary>
        public Text Source;

        /// <summary>用哪一档原版字模。</summary>
        public D2Text.D2Font Font = D2Text.D2Font.Font16;

        /// <summary>非 null ⇒ 本节点是输入框的占位符（按 `InputField` 的聚焦/内容状态显隐）。</summary>
        public InputField PlaceholderFor;

        /// <summary>
        /// 强制走 chi（原版中文）字模，即使文案全是 ASCII。
        /// <para>为什么存在：**原版拉丁字模 `font{16,24,30,42}` 不分大小写** —— 码位 97..122 的格子是
        /// 缩小号的同形大写、且没有降部 ⇒ `by clover-engine` 会被画成 `BY CLOVER-ENGINE`（小型大写），
        /// （实测 `font24_chi`：97＝真 a、103＝带降部 g、121＝带降部 y）⇒ 品牌署名行走它。
        /// 细节与实测见 `D2Text.D2Label._forceChi` 的注释。</para>
        /// </summary>
        public bool ForceChi;

        private D2Label _label;
        private string _lastText;
        private Color _lastColor;
        private int _lastSize = int.MinValue;
        private TextAnchor _lastAnchor;
        private HorizontalWrapMode _lastWrap;
        private bool _lastBestFit;
        private int _lastMin;
        private int _lastMax;
        private bool _lastPlaceholderVisible = true;

        /// <summary>挂上并立刻同步一次（幂等；重复调用只换引用）。</summary>
        public static D2TextMirror Attach(Text source, D2Text.D2Font font, InputField placeholderFor,
            bool forceChi = false)
        {
            if (source == null) return null;

            // 数据持有者：不画（`font == null` + `enabled == false`）—— 画面交给 D2Label
            source.font = null;
            source.enabled = false;

            var mirror = source.gameObject.GetComponent<D2TextMirror>();
            if (mirror == null) mirror = source.gameObject.AddComponent<D2TextMirror>();
            mirror.Source = source;
            mirror.Font = font;
            mirror.PlaceholderFor = placeholderFor;
            mirror.ForceChi = forceChi;
            mirror.Sync(true);
            return mirror;
        }

        private void Update()
        {
            // V6：启动期（`Game.Res` 未就绪）被**延迟**的字模在这里自愈 ——
            //   引擎那条 Text 挂钩有 `FlushPending`（Bootstrap 里紧跟 CloverRes.Init 调一次），
            //   但业务面板自己建的标签（`UiArt.Label` / `D2Label.Create`）没有那个人，
            //   只能靠每帧一次的这里把"延后的加载"补上（见 `D2Text.EnsureChi` 的注释）。
            D2Text.RetryDeferred();
            Sync(false);
        }

        private void Sync(bool force)
        {
            if (Source == null) return;

            if (_label == null)
            {
                _label = D2Label.Attach(Source.rectTransform, Source.text, Font, Source.alignment,
                    Source.color, Vector2.zero, Source.fontSize);
                _label.forceChi = ForceChi;      // 见 ForceChi 的注释（品牌署名行 = 逐字小写）
                _lastText = Source.text;
                _lastColor = Source.color;
                _lastSize = Source.fontSize;
                _lastAnchor = Source.alignment;
                _lastWrap = Source.horizontalOverflow;
                _lastBestFit = Source.resizeTextForBestFit;
                _lastMin = Source.resizeTextMinSize;
                _lastMax = Source.resizeTextMaxSize;
                _label.horizontalOverflow = _lastWrap;
                _label.verticalOverflow = Source.verticalOverflow;
                _label.resizeTextForBestFit = _lastBestFit;
                _label.resizeTextMinSize = _lastMin;
                _label.resizeTextMaxSize = _lastMax;
                _label.linePitch = LinePitch();
            }
            else
            {
                if (force || !string.Equals(Source.text, _lastText, System.StringComparison.Ordinal))
                {
                    _lastText = Source.text;
                    _label.SetText(_lastText);
                }
                if (force || Source.color != _lastColor)
                {
                    _lastColor = Source.color;
                    _label.SetColor(_lastColor);
                }
                if (force || Source.fontSize != _lastSize)
                {
                    _lastSize = Source.fontSize;
                    _label.fontSize = _lastSize;
                    _label.linePitch = LinePitch();
                }
                if (force || Source.alignment != _lastAnchor)
                {
                    _lastAnchor = Source.alignment;
                    _label.anchor = _lastAnchor;
                }
                if (force || Source.horizontalOverflow != _lastWrap)
                {
                    _lastWrap = Source.horizontalOverflow;
                    _label.horizontalOverflow = _lastWrap;
                }
                if (force || Source.resizeTextForBestFit != _lastBestFit
                    || Source.resizeTextMinSize != _lastMin || Source.resizeTextMaxSize != _lastMax)
                {
                    _lastBestFit = Source.resizeTextForBestFit;
                    _lastMin = Source.resizeTextMinSize;
                    _lastMax = Source.resizeTextMaxSize;
                    _label.resizeTextForBestFit = _lastBestFit;
                    _label.resizeTextMinSize = _lastMin;
                    _label.resizeTextMaxSize = _lastMax;
                }
            }

            if (PlaceholderFor != null)
            {
                // 与 uGUI `InputField.UpdatePlaceholder` 同一判据：没聚焦 + 内容为空 ⇒ 显示占位
                var visible = !PlaceholderFor.isFocused && string.IsNullOrEmpty(PlaceholderFor.text);
                if (force || visible != _lastPlaceholderVisible)
                {
                    _lastPlaceholderVisible = visible;
                    _label.SetActive(visible);
                }
            }
        }

        /// <summary>
        /// 行距（画布单位）。
        /// <para>口径：原版行高 = 字模格高（libd2 `font.zig` L141-146 `lineHeight()` = 最高那帧），
        /// 再按画布单位换算（字号 / 格高）。</para>
        /// </summary>
        private float LinePitch()
        {
            // 与 D2Label 的字模选择**同一口径**（含 ForceChi；两边不一致会让行距与字形错档）
            var chi = ForceChi || !D2Text.IsLatinOnly(Source != null ? Source.text : null);
            var cell = (chi ? D2Text.ChiCellH(Font) : D2Text.CellHeight(Font)) * 1.8f;
            var pt = Source != null && Source.fontSize > 0 ? Source.fontSize : cell;
            return pt;
        }
    }
}
