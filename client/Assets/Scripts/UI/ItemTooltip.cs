// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/ItemTooltip.cs  ★ 本项目新增（agent-09）
// 物品 tooltip：**名称按品质配色**（白/蓝/金/绿/暗金）+ 属性行 + 需求行 + 词缀行，跟随鼠标。
//
// 事实依据：
//   · `策划/策划案/暗黑破坏神2参考规格.md` §3.6 第 34 项：「悬停显示名称 + 属性，
//     颜色按品质（白/蓝/金/绿/暗金）」⇒ 颜色常量见 <see cref="ItemQualityColor"/>；
//   · 品质枚举 = `Def.ItemQuality`（Normal/Magic/Rare/Set/Unique，`Def/Enums.cs`）；
//   · 物品 DTO = `Def.ItemStack`（名称/词缀/伤害/防御/需求/耐久/售价**全在里面**，
//     所以 tooltip **不需要任何模块门面**，UI 层零耦合）；
//   · 鼠标坐标 = `Game.Input.MousePosition`（屏幕坐标，`docs/步骤文档.md` §3.3，
//     ⛔ 禁止直连 `UnityEngine.Input`）；屏幕 → Canvas 局部换算沿用引擎 `FloatTextLayer`
//     的同一口径（`RectTransformUtility.ScreenPointToLocalPointInRectangle`，相机传 null
//     因为引擎的 Canvas 是 ScreenSpaceOverlay，见 `Runtime/Presentation/UI.cs:49`）。
//
// 非 MonoBehaviour：由 `InventoryPanel` / `ShopPanel` 持有，在 `OnUpdate(dt)` 里 `Tick()`
//（`UIPanel.OnUpdate` 由引擎 `UIManager.Tick` 驱动，见 `Runtime/Presentation/UI.cs:293-318`）。
//
// ⛔ 本文件在 UI 层：只引用 `CloverEngine` / `Diablo2.Core` / `Diablo2.Def` / UnityEngine(.UI)。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>物品品质 → 名称颜色（原版五色）。</summary>
    internal static class ItemQualityColor
    {
        // 原版《暗黑破坏神 II》物品名色（社区文档与复刻工程通用的惯例值）：
        //   普通 #FFFFFF / 魔法 #6969FF / 稀有 #FFFF64 / 套装 #00FF00 / 暗金 #C7B377
        // 与 `策划案` §3.6 第 34 项「白/蓝/金/绿/暗金」一一对应。
        private static readonly Color Normal = new Color(1.000f, 1.000f, 1.000f, 1f);
        private static readonly Color Magic = new Color(0.412f, 0.412f, 1.000f, 1f);
        private static readonly Color Rare = new Color(1.000f, 1.000f, 0.392f, 1f);
        private static readonly Color Set = new Color(0.000f, 1.000f, 0.000f, 1f);
        private static readonly Color Unique = new Color(0.780f, 0.702f, 0.467f, 1f);

        /// <summary>取品质颜色；未知品质 ⇒ 白 + 一条 Warn。</summary>
        public static Color Of(ItemQuality quality)
        {
            switch (quality)
            {
                case ItemQuality.Normal: return Normal;
                case ItemQuality.Magic: return Magic;
                case ItemQuality.Rare: return Rare;
                case ItemQuality.Set: return Set;
                case ItemQuality.Unique: return Unique;
                default:
                    UiLog.WarnOnce("quality.unknown." + (int)quality,
                        $"未知物品品质 {(int)quality} ⇒ 名称按普通（白）显示");
                    return Normal;
            }
        }

        /// <summary>
        /// 品质的中文名 + 原版惯用色称（验收表 #34「五种品质（白/蓝/黄/绿/暗金）各一张悬停截图」的日志口径）。
        /// <para>只进日志、不进画面（画面上的品质表达**只有名字颜色**，与原版一致）。</para>
        /// </summary>
        public static string NameOf(ItemQuality quality)
        {
            switch (quality)
            {
                case ItemQuality.Normal: return "普通(白)";
                case ItemQuality.Magic: return "魔法(蓝)";
                case ItemQuality.Rare: return "稀有(黄)";
                case ItemQuality.Set: return "套装(绿)";
                case ItemQuality.Unique: return "暗金(暗金)";
                default:
                    UiLog.WarnOnce("quality.unknown." + (int)quality,
                        $"未知物品品质 {(int)quality} ⇒ 名称按普通（白）显示");
                    return "未知(降级为白)";
            }
        }

        /// <summary>颜色 → `#RRGGBB`（日志里跟画面上的字色逐位对照用）。</summary>
        public static string HexOf(ItemQuality quality)
        {
            Color32 c = Of(quality);
            return $"#{c.r:X2}{c.g:X2}{c.b:X2}";
        }
    }

    /// <summary>物品 tooltip（跟随鼠标的浮层；非 MonoBehaviour）。</summary>
    internal sealed class ItemTooltip
    {
        private const float Width = 320f;
        private const float Padding = 10f;
        private const int TitleSize = 22;
        private const int BodySize = 18;
        private const float BodyLine = 22f;

        /// <summary>标题一行的高度（原版 tooltip 的名条行高）。标题**允许多行**（长魔法名会折行）。</summary>
        private const float TitleLine = 26f;

        /// <summary>标题与正文之间的留白（单行标题时 `Padding+TitleLine+Gap` = 38 = 改动前的旧值，布局不变）。</summary>
        private const float GapUnderTitle = 2f;

        private const float CursorOffsetX = 22f;
        private const float CursorOffsetY = -18f;

        private readonly RectTransform _root;
        private readonly RectTransform _canvas;
        private readonly Image _bg;
        private Text _title;
        private Text _body;
        private bool _visible;
        private bool _warnedNoInput;

        /// <summary>
        /// 上一次已打过日志的「物品指纹」（`itemId|品质|名字|数量|词缀`）。
        /// <para>为什么要去重：`InventoryPanel.OnUpdate` **每帧**都会对悬停格调一次 `Show(item)`，
        /// 不去重就是每帧一条日志（把日志文件打爆）。指纹变了才打 —— 即"换了一件物品"才留一行。</para>
        /// </summary>
        private string _loggedKey;

        private ItemTooltip(RectTransform root, RectTransform canvas, Image bg)
        {
            _root = root;
            _canvas = canvas;
            _bg = bg;
        }

        /// <summary>是否正在显示。</summary>
        public bool IsVisible => _visible;

        /// <summary>
        /// 在 <paramref name="parent"/> 下造一个 tooltip（默认隐藏）。
        /// <paramref name="parent"/> 用面板根（铺满父层）即可 —— 位置按 Canvas 局部坐标算。
        /// </summary>
        public static ItemTooltip Create(Transform parent)
        {
            var root = UIFactory.CreateCentered("ItemTooltip", parent, new Vector2(Width, 120f), Vector2.zero);
            root.pivot = new Vector2(0f, 1f);            // 左上角为锚：从鼠标右下方向展开（原版手感）

            var bg = UiArt.Panel(root, "Bg", new Vector2(Width, 120f), Vector2.zero, UiArt.PanelBg, false);
            bg.rectTransform.anchorMin = Vector2.zero;
            bg.rectTransform.anchorMax = Vector2.one;
            bg.rectTransform.offsetMin = Vector2.zero;
            bg.rectTransform.offsetMax = Vector2.zero;

            var canvas = root.GetComponentInParent<Canvas>();
            if (canvas == null)
                UiLog.WarnOnce("tooltip.canvas.missing",
                    "ItemTooltip 找不到所属 Canvas ⇒ 位置换算退化为固定点（tooltip 仍可用，但不跟随鼠标）");

            var tooltip = new ItemTooltip(root, canvas != null ? canvas.transform as RectTransform : null, bg);
            tooltip.BuildTexts();
            root.gameObject.SetActive(false);
            return tooltip;
        }

        private void BuildTexts()
        {
            _title = UIFactory.CreateText("Title", _root, string.Empty, TitleSize, TextAnchor.UpperCenter,
                UiArt.TextColor);
            // ★ 片 3：物品名（中文）走**原版字模**（`Text` 只作数据持有者，见 UiArt.Label 的注释）
            D2TextMirror.Attach(_title, D2Text.FontFor(TitleSize), null);
            var titleRt = _title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.offsetMin = new Vector2(Padding, 0f);
            titleRt.offsetMax = new Vector2(-Padding, 0f);
            titleRt.sizeDelta = new Vector2(titleRt.sizeDelta.x, TitleLine);
            titleRt.anchoredPosition = new Vector2(0f, -Padding);
            _title.raycastTarget = false;
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;   // 长名折行（原版也折），高度见 Show
            _title.verticalOverflow = VerticalWrapMode.Overflow;

            _body = UIFactory.CreateText("Body", _root, string.Empty, BodySize, TextAnchor.UpperLeft,
                UiArt.TextColor);
            D2TextMirror.Attach(_body, D2Text.FontFor(BodySize), null);
            var bodyRt = _body.rectTransform;
            bodyRt.anchorMin = new Vector2(0f, 1f);
            bodyRt.anchorMax = new Vector2(1f, 1f);
            bodyRt.pivot = new Vector2(0.5f, 1f);
            bodyRt.offsetMin = new Vector2(Padding, 0f);
            bodyRt.offsetMax = new Vector2(-Padding, 0f);
            bodyRt.anchoredPosition = new Vector2(0f, -(Padding + TitleLine + GapUnderTitle));
            _body.raycastTarget = false;
        }

        /// <summary>显示某件物品（名称配色见 <see cref="ItemQualityColor"/>）。</summary>
        public void Show(ItemStack item)
        {
            if (item == null)
            {
                UiLog.Warn("ItemTooltip.Show 收到 null 物品 ⇒ 不显示（调用方应先判空）");
                Hide();
                return;
            }

            if (_root == null) return;

            var title = string.IsNullOrEmpty(item.name) ? $"物品 #{item.itemId}" : item.name;
            _title.text = title;
            _title.color = ItemQualityColor.Of(item.quality);

            var lines = BuildLines(item);
            _body.text = string.Join("\n", lines.ToArray());

            // 验收表 #34 的日志口径：**每件物品被悬停时一行**，含品质名 / 颜色 / 词缀。
            LogHover(item);

            // ★ 本轮修：长魔法名（例「伤害强化 21~30 阔斧 之 最小伤害 1~2 最大伤害 3~4」）会折成 2 行，
            //   而标题框原先固定 26 高 ⇒ **第 2 行压在正文第一行上**（实机截图 a34_A_q2 可见「武器（双手）」被盖住）。
            //   原版 tooltip 是"名条折行后整块往下长"，故这里按实际行数给高度、并把正文整体下移。
            var titleLines = TitleLineCount(title);
            var titleH = TitleLine * titleLines;
            _title.rectTransform.sizeDelta = new Vector2(_title.rectTransform.sizeDelta.x, titleH);
            _body.rectTransform.anchoredPosition = new Vector2(0f, -(Padding + titleH + GapUnderTitle));

            // 高度 = 标题（可能多行）+ 正文行数 × 行高 + 上下留白（单行标题时与改动前完全相同）
            var height = Padding * 2f + titleH + GapUnderTitle + lines.Count * BodyLine;
            _root.sizeDelta = new Vector2(Width, height);
            if (_bg != null) _bg.rectTransform.sizeDelta = new Vector2(Width, height);

            _root.gameObject.SetActive(true);
            _visible = true;
            Tick();      // 立刻摆到鼠标处，避免第一帧从旧位置飞过来
        }

        /// <summary>隐藏。</summary>
        public void Hide()
        {
            _visible = false;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        /// <summary>每帧跟随鼠标（由面板 `OnUpdate` 驱动）。</summary>
        public void Tick()
        {
            if (!_visible || _root == null || _canvas == null) return;

            if (Game.Input == null)
            {
                if (!_warnedNoInput)
                {
                    _warnedNoInput = true;
                    UiLog.Warn("Game.Input 未挂载（CloverInput.Init 未调用）⇒ tooltip 不跟随鼠标（停在最后位置）");
                }
                return;
            }

            var screen = Game.Input.MousePosition;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvas, new Vector2(screen.x, screen.y), null, out var local))
            {
                UiLog.WarnOnce("tooltip.convert.fail", "屏幕点换算到 Canvas 局部坐标失败 ⇒ tooltip 位置不更新");
                return;
            }

            var rect = _canvas.rect;
            var halfW = _root.sizeDelta.x;
            var halfH = _root.sizeDelta.y;
            var x = local.x + CursorOffsetX;
            var y = local.y + CursorOffsetY;

            // 贴边回收：超出画布就翻到另一侧（原版鼠标贴近右下角时 tooltip 会翻到左上）
            if (x + halfW > rect.xMax - 4f) x = local.x - halfW - 8f;
            if (y - halfH < rect.yMin + 4f) y = local.y + halfH + 8f;

            _root.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>销毁（面板关闭时调用）。</summary>
        public void Destroy()
        {
            _visible = false;
            if (_root != null)
                UnityEngine.Object.Destroy(_root.gameObject);
        }

        // ── 日志（验收表 #34 的证据行）──────────────────────────────────────────
        /// <summary>
        /// 悬停一行日志：`[tooltip] 品质=<名>(<色>) 色=<#RRGGBB> 物品「…」 词缀=…`。
        /// <para>同一件物品只在**第一次**悬停时打一行（见 <see cref="_loggedKey"/>），
        /// 因为 `Show` 被面板每帧调用。</para>
        /// </summary>
        private void LogHover(ItemStack item)
        {
            var key = item.itemId + "|" + (int)item.quality + "|" + item.name + "|" + item.count
                      + "|" + AffixSummary(item);
            if (string.Equals(key, _loggedKey, System.StringComparison.Ordinal)) return;
            _loggedKey = key;

            UiLog.Info($"[tooltip] 悬停品质={ItemQualityColor.NameOf(item.quality)}"
                + $" 色={ItemQualityColor.HexOf(item.quality)}"
                + $" 物品「{(string.IsNullOrEmpty(item.name) ? "物品 #" + item.itemId : item.name)}」"
                + $" 类型={item.type} 占格={item.gridW}x{item.gridH}"
                + $" 词缀={AffixSummary(item)}");
        }

        /// <summary>词缀摘要（前缀名+值 / 后缀名+值；无词缀 ⇒ 「无」——原版普通物品就是无词缀）。</summary>
        private static string AffixSummary(ItemStack item)
        {
            if (item.affixes == null || item.affixes.Count == 0) return "无";

            var sb = new StringBuilder();
            for (var i = 0; i < item.affixes.Count; i++)
            {
                var a = item.affixes[i];
                if (a == null) continue;
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(a.kind == AffixKind.Prefix ? "前缀" : "后缀");
                sb.Append(':').Append(string.IsNullOrEmpty(a.name) ? a.mod : a.name);
                var v = a.value != 0 ? a.value : a.min;
                if (v != 0) sb.Append(" +").Append(v);
            }
            return sb.Length == 0 ? "无" : sb.ToString();
        }

        // ── 标题折行测量（纯函数：不碰 Unity 运行时状态，离线可断言）──────────────
        /// <summary>
        /// 标题会折成几行：按**贪心换行**逐字累加字宽，超过可用宽度就换行。
        /// <para>字宽按字号估：全角（CJK/全角标点，`>= U+1100`）≈ 1 em，其余 ≈ 0.5 em。
        /// 目的只是"给标题留够高度、让正文让位"（原版行为），不是精确排版。</para>
        /// </summary>
        internal static int TitleLineCount(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1;

            // ★ 片 3：不再自己"按字号估字宽"，改成**问字模**（口径与真正画出来的完全一致：
            //   字步进 = 原版 `.tbl` 的 `width`，换行 = 原版 `breakLine`；见 `UI/D2Text.cs` 文件头）。
            var avail = Width - Padding * 2f;                      // 画布单位（可用宽）
            var font = D2Text.FontFor(TitleSize);
            var chi = !D2Text.IsLatinOnly(text);
            var scale = D2Text.ScaleFor(TitleSize, chi, font);
            var availNative = Mathf.Max(1, Mathf.RoundToInt(avail / scale));
            return D2Text.CountLines(font, text, chi, availNative, true);
        }

        /// <summary>单字占宽（字号单位）：全角 1 em、半角 0.5 em。</summary>
        private static float CharAdvance(char c) => IsWide(c) ? TitleSize : TitleSize * 0.5f;

        /// <summary>是否全角字（CJK / 全角标点 / 假名 / 韩文；与 `UIFactory.DefaultFont()` 的中文回退同一口径）。</summary>
        private static bool IsWide(char c)
        {
            if (c < 0x1100) return false;
            if (c >= 0x2E80 && c <= 0xA4CF) return true;      // CJK 部首 / 汉字 / 注音
            if (c >= 0xAC00 && c <= 0xD7A3) return true;      // 韩文
            if (c >= 0xF900 && c <= 0xFAFF) return true;      // CJK 兼容汉字
            if (c >= 0xFE30 && c <= 0xFE6F) return true;      // CJK 兼容标点
            if (c >= 0xFF00 && c <= 0xFF60) return true;      // 全角 ASCII
            if (c >= 0xFFE0 && c <= 0xFFE6) return true;      // 全角符号
            return false;
        }

        // ── 文本行（数值全部来自 `Def.ItemStack`，不查表、不硬编码）────────────
        private static List<string> BuildLines(ItemStack item)
        {
            var lines = new List<string>();

            var kind = KindText(item);
            if (!string.IsNullOrEmpty(kind)) lines.Add(kind);

            if (item.dmgMin > 0 || item.dmgMax > 0)
                lines.Add($"伤害: {item.dmgMin}-{item.dmgMax}");

            if (item.defMin > 0 || item.defMax > 0)
                lines.Add($"防御: {item.defMin}-{item.defMax}");

            if (item.maxDurability > 0)
                lines.Add($"耐久: {item.durability}/{item.maxDurability}");

            if (item.strReq > 0) lines.Add($"需要力量: {item.strReq}");
            if (item.lvlReq > 0) lines.Add($"需要等级: {item.lvlReq}");

            if (item.affixes != null)
            {
                for (var i = 0; i < item.affixes.Count; i++)
                {
                    var affix = item.affixes[i];
                    if (affix == null) continue;

                    var name = string.IsNullOrEmpty(affix.name) ? affix.mod : affix.name;
                    var value = affix.value != 0 ? affix.value : affix.min;
                    lines.Add(value != 0 ? $"{name} +{value}" : name);
                }
            }

            if (item.isQuestItem) lines.Add("任务物品（不可丢弃 / 不可出售）");
            if (item.price > 0) lines.Add($"售价: {item.price}");
            if (lines.Count == 0) lines.Add("（无附加属性）");

            return lines;
        }

        private static string KindText(ItemStack item)
        {
            switch (item.type)
            {
                case ItemType.Weapon: return item.gridW >= 2 ? "武器（双手）" : "武器（单手）";
                case ItemType.Armor: return item.gridH >= 3 ? "防具（身体）" : "防具";
                case ItemType.Misc: return item.isGold ? "金币" : "杂项";
                default:
                    UiLog.WarnOnce("tooltip.type." + (int)item.type, $"未知物品大类 {(int)item.type}");
                    return string.Empty;
            }
        }
    }
}
