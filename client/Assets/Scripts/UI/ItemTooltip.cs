// ─────────────────────────────────────────────────────────────────────────────
// 物品 tooltip：**名称按品质配色**（白/蓝/金/绿/暗金）+ 属性行 + 需求行 + 词缀行，跟随鼠标。
//
// 事实依据：
//   · 名称颜色 = <see cref="ItemQualityColor"/>（白/蓝/金/绿/暗金）；
//   · 品质枚举 = `Def.ItemQuality`（Normal/Magic/Rare/Set/Unique，`Def/Enums.cs`）；
//   · 物品 DTO = `Def.ItemStack`（名称/词缀/伤害/防御/需求/耐久/售价**全在里面**，
//     所以 tooltip **不需要任何模块门面**，UI 层零耦合）；
//   · UI 层**禁止直连 `UnityEngine.Input`**；屏幕 → Canvas 局部换算 / 贴边翻转 / 显隐 / 复用由引擎件
//     `CloverEngine.PointerFloatLayer` 提供（`CursorOffsetX/Y` = 22 / -18 与定位契约见
//     `clover-client-unity-engine/Runtime/Presentation/PointerFloatLayer.cs`）——
//     本文件只留 "D2 的物品文本怎么排版 / 怎么配色"（品质色、字模、行数测量）。
//
// 非 MonoBehaviour：由 `InventoryPanel` / `ShopPanel` 持有，在 `OnUpdate(dt)` 里 `Tick()`
//（`UIPanel.OnUpdate` 由引擎 `UIManager.Tick` 驱动，见 `Runtime/Presentation/UI.cs:293-318`）。
//
// 两个显示入口：`Show(ItemStack[, priceLine])`（真 `Def.ItemStack`：背包 / 装备 / 商店卖出页）、
//   `ShowStock(ShopEntry)`（商店买入页 —— 取载荷透传的 `ShopEntry.item` 走**同一套正文**，
//   见 `Def.ShopEntry.item` 的字段注释；载荷没带 item 时才退回"名称 + 价格"最小形态）。
// 价格行：**默认不出**（原版物品 tooltip 没有价格行，价格只在商店页 —— 审计 #7）；商店按该格的
//   语义传 `priceLine`（商人的货 = 3329「花費：」+ 标价 / 玩家的货 = 3331「出售價格： 」+
//   `GameConst.SellPriceOf`），见 `UI/ShopPanel.UpdateHover`。
// 正文文案 = **原版中文串表逐字**（id 见各 `Label*` 常量；数字用块内 token 3463「之」/ 3464「到」
//   拼装 —— 英文表实证 = of / to），⛔ 不许自造简体句式（审计 #8）；行集也照原版
//   （没有"类型 / 任务物品 / （无附加属性）"这三种自造行）。
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
    /// <summary>物品品质 → 名称颜色（原版五色 + 未鉴定降级）。</summary>
    internal static class ItemQualityColor
    {
        // 原版《暗黑破坏神 II》物品名色（社区文档与复刻工程通用的惯例值）：
        //   普通 #FFFFFF / 魔法 #6969FF / 稀有 #FFFF64 / 套装 #00FF00 / 暗金 #C7B377
        private static readonly Color Normal = new Color(1.000f, 1.000f, 1.000f, 1f);
        private static readonly Color Magic = new Color(0.412f, 0.412f, 1.000f, 1f);
        private static readonly Color Rare = new Color(1.000f, 1.000f, 0.392f, 1f);
        private static readonly Color Set = new Color(0.000f, 1.000f, 0.000f, 1f);
        private static readonly Color Unique = new Color(0.780f, 0.702f, 0.467f, 1f);

        /// <summary>
        /// **未鉴定物品的品质色按原版降级为普通（白）**（纯函数，离线宿主可直接断言）。
        /// <para>原版：未鉴定的魔法/稀有/暗金一律显示成**白字**，名字只有基础类型名 ——
        /// 这正是"看不出好坏、必须去鉴定"的手感来源。</para>
        /// </summary>
        internal static ItemQuality EffectiveOf(ItemQuality quality, bool unidentified)
            => unidentified ? ItemQuality.Normal : quality;

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
        /// 品质的中文名 + 原版惯用色称（原版五种品质（白/蓝/黄/绿/暗金）逐张悬停对照的日志口径）。
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

        // ── 正文文案 = 原版中文串表**逐字**（`原版资源/d2text/chi_string.txt`；写法与
        //    `Module/Skill/SkillModule` 的 `Label*` / `UI/CharacterPanel` 一致：传统字面 + id 注释）。
        //    ⛔ 逐字就是逐字 —— 不许换成简体句式、不许改标点（审计 #8）。
        //    ⚠️ 两个"看着像笔误、其实是串表原文"的点：3457 是**半角冒号**、3331 自带**尾随空格**。
        //    id 3450-3475 = 原版「物品描述」块（3455 未辨視 / 3457 耐久度 / 3458 力量 / 3461 防禦 /
        //    3463 之 / 3464 到 / 3465 單手傷害 / 3466 雙手傷害 / 3469 須要等級）——英文表对照实证
        //    （`D2data.mpq` 的 `data/local/LNG/ENG/string.tbl`：3463 = `of`、3464 = `to`、
        //    3469 = `Required Level:`）；⚠️ 4187「需要等級：」是 41xx 选单块的同义串，**不是**本块。
        internal const string LabelUnidentified = "未辨視";        // id 3455（Unidentified）
        internal const string LabelDurability = "耐久度:";         // id 3457（半角冒号 = 串表原文）
        internal const string LabelStrReq = "須要的力量點數：";     // id 3458
        internal const string LabelDefense = "防禦：";             // id 3461
        internal const string LabelOneHandDamage = "單手傷害：";    // id 3465
        internal const string LabelTwoHandDamage = "雙手傷害：";    // id 3466
        internal const string LabelLevelReq = "須要等級：";         // id 3469
        internal const string RangeOf = "之";                      // id 3463（"of"：耐久 a 之 b）
        internal const string RangeTo = "到";                      // id 3464（"to"：伤害/防御 a 到 b）

        /// <summary>商店两页价格行的前缀（逐字；<see cref="LabelSellPrice"/> 的尾随空格 = 串表原文，直接接数值）。</summary>
        internal const string LabelCost = "花費：";                // id 3329（"Cost: "）
        internal const string LabelSellPrice = "出售價格： ";      // id 3331（"Sell value: "，自带尾随空格）


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
        /// 任一不成立 ⇒ 调用方必须 `Hide()`/`Destroy()`（否则会在面板上留一块空气框）。
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
        /// <para>节点名仍是 <c>ItemTooltip</c>：既有实机驱动按名字 Find 它。
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

        /// <summary>显示某件物品（名称配色见 <see cref="ItemQualityColor"/>；**不带价格行** —— 见双参重载）。</summary>
        public void Show(ItemStack item) => Show(item, null);

        /// <summary>
        /// 显示某件物品；<paramref name="priceLine"/> 非空时**追加**一行价格（**唯一的价格来源** ——
        /// 本方法默认不出价格行：原版物品 tooltip 没有价格、价格只在商店页，见审计 #7）。
        /// 商店按该格的语义传这一行（卖出页 = <see cref="LabelSellPrice"/> + `GameConst.SellPriceOf`，
        /// 见 `UI/ShopPanel.UpdateHover`）。
        /// </summary>
        public void Show(ItemStack item, string priceLine)
        {
            if (item == null)
            {
                UiLog.Warn("ItemTooltip.Show 收到 null 物品 ⇒ 不显示（调用方应先判空）");
                Hide();
                return;
            }

            if (_root == null) return;

            // 未鉴定：名字只留基础类型名（原版口径），品质色降级为白（ItemQualityColor.EffectiveOf）。
            // 基础名由**物品模块在生成/鉴定时写进 `baseName`**（`Def.ItemStack.baseName`），
            // UI 不查表（分层自检 ③：`UI/**` 不许依赖 `Table`）。
            var title = TitleTextOf(item, item.name, item.itemId);
            // 指纹带上价格行 ⇒ 同一件物品在买卖两页之间切换时会各留一行日志。
            var key = item.itemId + "|" + (int)item.quality + "|" + item.name + "|" + item.count
                      + "|" + AffixSummary(item) + "|" + priceLine
                      + "|" + (item.unidentified ? "uid" : "id");
            Apply(title, ItemQualityColor.EffectiveOf(item.quality, item.unidentified),
                BuildLines(item, priceLine), key,
                $"物品「{title}」 类型={item.type} 占格={item.gridW}x{item.gridH} 词缀={AffixSummary(item)}"
                + (item.unidentified ? "（未鉴定）" : ""));
        }

        /// <summary>
        /// 显示商店「商人的货」（载荷 = `Def.ShopEntry`）：**原版买入页 tooltip 显示完整物品属性**
        /// （审计 #9）⇒ 优先取载荷透传的 <see cref="ShopEntry.item"/>（`Module/Npc/NpcShop.AddEntry`
        /// 写入）走与背包**同一套** `BuildLines`；`item == null`（旧载荷 / 造载荷方没填）才退回
        /// "名称 + 价格"的最小形态 —— ⛔ 不按 itemId 回查配表（UI 层不许依赖 `Table`）。
        /// <para>价格行 = <see cref="LabelCost"/> + 标价（原版串逐字；审计 #10）。</para>
        /// </summary>
        public void ShowStock(ShopEntry entry)
        {
            if (entry == null)
            {
                UiLog.Warn("ItemTooltip.ShowStock 收到 null 商品 ⇒ 不显示（调用方应先判空）");
                Hide();
                return;
            }

            if (_root == null) return;

            var item = entry.item;
            var title = TitleTextOf(item, entry.name, entry.itemId);
            var priceLine = LabelCost + entry.price;
            var lines = item != null ? BuildLines(item, priceLine) : new List<string> { priceLine };

            //   指纹带上"完整/最小"两态：同一格在两种数据形态之间切换时各留一行日志。
            var key = "stock|" + entry.index + "|" + entry.itemId + "|" + (int)entry.quality + "|"
                      + entry.name + "|" + entry.price + "|" + entry.count
                      + "|" + (item != null ? "full" : "min");
            Apply(title, entry.quality, lines, key,
                $"商店商品「{title}」 占格={entry.gridW}x{entry.gridH} 花费={entry.price}"
                + $" 数量={(entry.count < 0 ? "无限" : entry.count.ToString())}"
                + (item != null ? "（tooltip = 完整属性）" : "（tooltip = 最小形态：载荷未带 item）"));
        }

        /// <summary>标题文案：未鉴定 ⇒ 只显基础类型名（原版口径）；名字空 ⇒ 兜底 `物品 #id`。</summary>
        private static string TitleTextOf(ItemStack item, string name, int itemId)
        {
            if (item != null && item.unidentified && !string.IsNullOrEmpty(item.baseName)) return item.baseName;
            return string.IsNullOrEmpty(name) ? $"物品 #{itemId}" : name;
        }

        /// <summary>把标题 / 品质色 / 正文行写进浮层并显示（两个悬停入口共用同一套排版与日志口径）。</summary>
        private void Apply(string title, ItemQuality quality, List<string> lines, string logKey, string logDetail)
        {
            _title.text = title;
            _title.color = ItemQualityColor.Of(quality);

            _body.text = string.Join("\n", lines.ToArray());

            // 原版悬停截图逐张对照的日志口径：**每件物品被悬停时一行**，含品质名 / 颜色。
            LogHover(logKey, quality, logDetail);

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

        // ── 日志（悬停截图逐张对照的证据行）──────────────────────────────────────────
        /// <summary>
        /// 悬停一行日志：`[tooltip] 悬停品质=&lt;名&gt;(&lt;色&gt;) 色=&lt;#RRGGBB&gt; &lt;detail&gt;`。
        /// <para>同一件物品只在**第一次**悬停时打一行（见 <see cref="_loggedKey"/>），
        /// 因为悬停判定被面板每帧调用。</para>
        /// </summary>
        private void LogHover(string key, ItemQuality quality, string detail)
        {
            if (string.Equals(key, _loggedKey, System.StringComparison.Ordinal)) return;
            _loggedKey = key;

            UiLog.Info($"[tooltip] 悬停品质={ItemQualityColor.NameOf(quality)}"
                + $" 色={ItemQualityColor.HexOf(quality)} {detail}");
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

        // 字宽 / 断行一律走 `D2Text.CountLines` / `MeasureNative`（口径 = 原版 `.tbl` 的 `width` +
        //   原版 `breakLine`，见 `UI/D2Text.cs` 文件头）；本文件不另留估算式的字宽函数。

        // ── 文本行（数值全部来自 `Def.ItemStack`；文案全部取自原版串表 —— 见 `Label*` 常量）──
        /// <summary>
        /// 正文行（`internal` = 供离线宿主 `uicheck` 逐行断言；纯函数、不碰 Unity 运行时状态）。
        /// <para>行集 / 顺序照原版：单手 / 双手伤害 或 防御 → 耐久 → 需求（力量 / 等级）→ 词缀；
        /// **未鉴定 ⇒ 只有一行「未辨視」**（不列任何属性 / 词缀 —— 原版"看不出好坏"的手感来源）；
        /// 原版没有的"类型行 / 任务物品 / （无附加属性）"三种自造句**已删**（审计 #8）。</para>
        /// <para><paramref name="priceLine"/> 非空 ⇒ 追加为**最后一行**（商店两页显式传；
        /// 背包 / 装备不传 ⇒ 没有价格行 —— 审计 #7）。</para>
        /// </summary>
        internal static List<string> BuildLines(ItemStack item, string priceLine)
        {
            var lines = new List<string>();

            if (item.unidentified)
            {
                lines.Add(LabelUnidentified);
            }
            else
            {
                AddDamageLines(lines, item);
                if (item.defMin > 0 || item.defMax > 0)
                    lines.Add(LabelDefense + RangeText(item.defMin, item.defMax));
                if (item.maxDurability > 0)
                    lines.Add(LabelDurability + item.durability + " " + RangeOf + " " + item.maxDurability);
                if (item.strReq > 0) lines.Add(LabelStrReq + item.strReq);
                if (item.lvlReq > 0) lines.Add(LabelLevelReq + item.lvlReq);
                AddAffixLines(lines, item);
            }

            if (!string.IsNullOrEmpty(priceLine)) lines.Add(priceLine);

            return lines;
        }

        /// <summary>
        /// （内部）伤害行：标签按武器形态取 3465「單手傷害：」/ 3466「雙手傷害：」 —— 官方列语义：
        /// `2handed` ⇒ 只出双手行；`1or2handed`（如双手剑）⇒ **两行都出**；其余（单手）⇒ 只出单手行。
        /// 双手数值缺失（`dmg2_*` 为 0：旧档 / 配表缺口）⇒ 回退用单手数值
        /// （同 `Def.ItemStack.dmg2Min` 注释里的"向后兼容"口径）。
        /// </summary>
        private static void AddDamageLines(List<string> lines, ItemStack item)
        {
            if (item.dmgMin <= 0 && item.dmgMax <= 0) return;

            var hasTwo = item.dmg2Min > 0 || item.dmg2Max > 0;
            if (!item.twoHanded || item.oneOrTwo)
                lines.Add(LabelOneHandDamage + RangeText(item.dmgMin, item.dmgMax));
            if (item.twoHanded || item.oneOrTwo)
                lines.Add(LabelTwoHandDamage
                    + (hasTwo ? RangeText(item.dmg2Min, item.dmg2Max) : RangeText(item.dmgMin, item.dmgMax)));
        }

        /// <summary>（内部）范围文本：`a 到 b`（token = 3464「到」，英文表 = `to`）；`a == b` ⇒ 单值。</summary>
        internal static string RangeText(int min, int max)
            => min == max ? min.ToString() : min + " " + RangeTo + " " + max;

        /// <summary>（内部）词缀行：`{名} +{值}`（名 / 值来自 `affix_c` 表数据 —— 不是 UI 自造文案）。</summary>
        private static void AddAffixLines(List<string> lines, ItemStack item)
        {
            if (item.affixes == null) return;

            for (var i = 0; i < item.affixes.Count; i++)
            {
                var affix = item.affixes[i];
                if (affix == null) continue;

                var name = string.IsNullOrEmpty(affix.name) ? affix.mod : affix.name;
                var value = affix.value != 0 ? affix.value : affix.min;
                lines.Add(value != 0 ? $"{name} +{value}" : name);
            }
        }
    }
}
