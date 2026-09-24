// ─────────────────────────────────────────────────────────────────────────────
// 物品 tooltip：**名称按品质配色**（白/蓝/金/绿/暗金）+ 属性行 + 需求行 + 词缀行，跟随鼠标。
//
// 事实依据：
//     颜色按品质（白/蓝/金/绿/暗金）」⇒ 颜色常量见 <see cref="ItemQualityColor"/>；
//   · 品质枚举 = `Def.ItemQuality`（Normal/Magic/Rare/Set/Unique，`Def/Enums.cs`）；
//   · 物品 DTO = `Def.ItemStack`（名称/词缀/伤害/防御/需求/耐久/售价**全在里面**，
//     所以 tooltip **不需要任何模块门面**，UI 层零耦合）；
//     禁止直连 `UnityEngine.Input`）；屏幕 → Canvas 局部换算 / 贴边翻转 / 显隐 / 复用**已下沉到引擎**
//     `CloverEngine.PointerFloatLayer`（`CursorOffsetX/Y` = 22 / -18 与定位契约见
//     `clover-client-unity-engine/Runtime/Presentation/PointerFloatLayer.cs`）——
//     本文件只留 "D2 的物品文本怎么排版 / 怎么配色"（品质色、字模、行数测量）。
//
// 非 MonoBehaviour：由 `InventoryPanel` / `ShopPanel` 持有，在 `OnUpdate(dt)` 里 `Tick()`
//（`UIPanel.OnUpdate` 由引擎 `UIManager.Tick` 驱动，见 `Runtime/Presentation/UI.cs:293-318`）。
//
// 本文件在 UI 层：只引用 `CloverEngine` / `Diablo2.Core` / `Diablo2.Def` / UnityEngine(.UI)。
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

        /// <summary>
        /// （`UI/GroundItemLabelView.cs`）**复用同一个常量**，保证"名牌字号 = tooltip 标题字号"
        /// </summary>
        internal const int TitleSize = 22;
        private const int BodySize = 18;
        private const float BodyLine = 22f;

        /// <summary>标题一行的高度（原版 tooltip 的名条行高）。标题**允许多行**（长魔法名会折行）。</summary>
        private const float TitleLine = 26f;

        private const float GapUnderTitle = 2f;


        /// <summary>浮层容器（**引擎件**）：定位 / 显隐 / 复用都由它管；本文件只往 <see cref="_root"/> 里塞内容。</summary>
        private readonly PointerFloatLayer _layer;

        /// <summary>内容宿主（= <see cref="_layer"/> 的 `Content`；pivot = 左上角 ⇒ 从指针右下展开）。</summary>
        private readonly RectTransform _root;

        private readonly Image _bg;
        private Text _title;
        private Text _body;

        /// <summary>
        /// 上一次已打过日志的「物品指纹」（`itemId|品质|名字|数量|词缀`）。
        /// <para>为什么要去重：`InventoryPanel.OnUpdate` **每帧**都会对悬停格调一次 `Show(item)`，
        /// 不去重就是每帧一条日志（把日志文件打爆）。指纹变了才打 —— 即"换了一件物品"才留一行。</para>
        /// </summary>
        private string _loggedKey;

        private ItemTooltip(PointerFloatLayer layer, Image bg)
        {
            _layer = layer;
            _root = layer.Content;      // 内容宿主（pivot 左上，引擎已设好）
            _bg = bg;
        }

        /// <summary>是否正在显示（真源在引擎容器的显隐态上，本项目不再自己存一份）。</summary>
        public bool IsVisible => _layer != null && _layer.IsVisible;

        /// <summary>
        /// 只有三个条件**同时**成立才允许停在可见态：
        ///   ① 拥有它的面板还开着（面板 `OnClose` 会 `Destroy`，见 `InventoryPanel.OnClose`）；
        ///   ② 指针在面板矩形**内**（`InventoryPanel.UpdateHover` 的 `RectangleContainsScreenPoint`）；
        ///   ③ 指针下的格/装备槽**有物品**（悬空格 ⇒ 必须 `Hide`）。
        /// 任一不成立 ⇒ 调用方必须 `Hide()`/`Destroy()` —— 这正是"离开背包后残留一块空框"
        /// </summary>
        internal static bool ShouldBeVisible(bool panelOpen, bool pointerInsidePanel, bool hasItem)
            => panelOpen && pointerInsidePanel && hasItem;

        /// <summary>
        /// 在 <paramref name="parent"/> 下造一个 tooltip（默认隐藏）。
        /// <paramref name="parent"/> 用面板根（铺满父层）即可 —— 位置按 Canvas 局部坐标算。
        /// </summary>
        /// <remarks>
        /// 容器 / 定位 / 显隐 / 复用**全是引擎件** <see cref="PointerFloatLayer"/> 的事：
        /// 它建出"轴心 = 左上角"的空宿主、自己找画布与换算相机（Overlay ⇒ null）、
        /// 每帧按 `Game.Input.MousePosition` 摆位并做贴边翻转。
        /// <para>节点名仍是 <c>ItemTooltip</c>：既有实机驱动按名字 Find 它（`.ai-tmp/drivers/uifix*_dump.cs`）。
        /// 画布取不到的降频留痕也由引擎件负责（tag = <c>PointerFloat</c>）。</para>
        /// </remarks>
        public static ItemTooltip Create(Transform parent)
        {
            // 初始尺寸交给引擎件：Create 建的是零尺寸宿主，真实尺寸每次 `Show` 由 `SetSize` 写入
            var layer = PointerFloatLayer.Create(parent, "ItemTooltip");
            var root = layer.Content;

            var bg = UiArt.Panel(root, "Bg", new Vector2(Width, 120f), Vector2.zero, UiArt.PanelBg, false);
            bg.rectTransform.anchorMin = Vector2.zero;
            bg.rectTransform.anchorMax = Vector2.one;
            bg.rectTransform.offsetMin = Vector2.zero;
            bg.rectTransform.offsetMax = Vector2.zero;

            var tooltip = new ItemTooltip(layer, bg);
            tooltip.BuildTexts();
            return tooltip;     // Create 内部已把宿主 SetActive(false)
        }

        private void BuildTexts()
        {
            //   原版 tooltip 的名条与正文**同一条左边界**，故标题改 `UpperLeft`。
            _title = UIFactory.CreateText("Title", _root, string.Empty, TitleSize, TextAnchor.UpperLeft,
                UiArt.TextColor);
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

            //   —— 行数一多（词缀/需求/售价齐上）超出框高就被截掉半行（看起来"文字被切"）。
            //   这里显式：横向 Wrap（属性行长时折行）、纵向 Overflow（框高由 Show 按行数算，
            //   但即使算少一格也不许把字裁掉）。标题同理已在上面设过
            //   （`horizontalOverflow = Wrap` / `verticalOverflow = Overflow`）。
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
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

            //   原版 tooltip 是"名条折行后整块往下长"，故这里按实际行数给高度、并把正文整体下移。
            var titleLines = TitleLineCount(title);
            var titleH = TitleLine * titleLines;
            _title.rectTransform.sizeDelta = new Vector2(_title.rectTransform.sizeDelta.x, titleH);
            _body.rectTransform.anchoredPosition = new Vector2(0f, -(Padding + titleH + GapUnderTitle));
            //   （一直是 `CreateText` 的默认高）⇒ 行数多时文字溢出/被裁。现在 = 行数 × 行高，
            //   与下面 `_root` 的高度用**同一个来源**（不再有两套算法）。
            _body.rectTransform.sizeDelta =
                new Vector2(_body.rectTransform.sizeDelta.x, lines.Count * BodyLine);

            // 高度 = 标题（可能多行）+ 正文行数 × 行高 + 上下留白（单行标题时与改动前完全相同）
            var height = Padding * 2f + titleH + GapUnderTitle + lines.Count * BodyLine;
            // 尺寸交给引擎容器（它负责把 `sizeDelta` 写进宿主，并据此做贴边翻转）
            _layer.SetSize(new Vector2(Width, height));
            //   底板 Bg 的锚点是**铺满**（`anchorMin=0 / anchorMax=1` + 四边 offset 0）⇒ 它的 `sizeDelta`
            //   应当恒为 0（= 与宿主严格等大）。改动前这里写的是 `(Width, height)` —— 在铺满锚点下那表示
            //   **比宿主再大一圈**（左右各多 `Width/2`、上下各多 `height/2`），实机表现为底板溢出到浮层外。
            if (_bg != null) _bg.rectTransform.sizeDelta = Vector2.zero;

            // 显示 + 立刻摆到鼠标处（避免第一帧从旧位置飞过来）：引擎容器内部就是"SetActive(true) + Tick"
            _layer.Show();
        }

        /// <summary>隐藏（节点保留，下次 <see cref="Show"/> 复用）。</summary>
        public void Hide() => _layer.Hide();

        /// <summary>
        /// 每帧跟随鼠标（由面板 `OnUpdate` 驱动）。
        /// <para>定位全在引擎容器里：`Game.Input.MousePosition` → 画布局部点（按画布模式取相机）→
        /// 贴边**关于指针镜像**翻转 → 夹进画布。本方法只做转发（项目侧不再自己算一遍）。</para>
        /// </summary>
        public void Tick() => _layer.Tick();

        /// <summary>销毁（面板关闭时调用）。</summary>
        public void Destroy() => _layer.Destroy();

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

            var avail = Width - Padding * 2f;                      // 画布单位（可用宽）
            var font = D2Text.FontFor(TitleSize);
            var chi = !D2Text.IsLatinOnly(text);
            var scale = D2Text.ScaleFor(TitleSize, chi, font);
            var availNative = Mathf.Max(1, Mathf.RoundToInt(avail / scale));
            return D2Text.CountLines(font, text, chi, availNative, true);
        }

        // w3 审计删除：`CharAdvance(char)` / `IsWide(char)` 两个私有静态方法**已无调用方**
        //   `D2Text.CountLines` / `MeasureNative`，口径 = 原版 `.tbl` 的 `width` + 原版 `breakLine`，
        //   见 `UI/D2Text.cs` 文件头）。留着估算式的字宽函数只会让人以为排版还在用它。

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
