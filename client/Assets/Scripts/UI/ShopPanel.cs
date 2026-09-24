// ─────────────────────────────────────────────────────────────────────────────
// 商店面板 = 原版买卖屏：底图 buysell_back.png（320×432 → ×1.8 居中），
// 商品格 = 原版那 10×10 格，**格内贴原版物品图标**
//
// 数据（不变）：`Diablo2.Def.ShopOpenArgs`（npcId/npcName/canRepair/playerGold/
//   stock(List<ShopEntry>)/playerItems(List<InventorySlot>)/repairAllCost）。
// 请求（不变）：`Events.ShopBuyRequest` / `ShopSellRequest` / `ShopRepairRequest` / `ShopClose`。
// 零 `using Diablo2.Module`（分层自检 ③）。
//
//   商店上看不到"这是谁家的、现在哪一页"。现在两行由 `BuildTitleLines()` 建出并接线
//   （落位 = 页签带与 10×10 格区之间的底图空白带，依据与核算见 `UI/UiLayoutGame.cs` §商店 的 S5 注释）。
//   本面板的**层仍是 `Popup`**，这是 R1-E 的 S1 要的（对话条降到 Normal 让位给商店遮罩，
//      商店必须在遮罩**之上**才点得动；依据见 `UI/NpcDialogPanel.cs` 文件头的 S1）。
//
//   ① **1 件 = 1 格 → 按物品自身占格**：商品/可卖物品用 `ShopEntry.gridW/gridH`
//      （= 配表 `item_c.grid_w/grid_h`，与背包同源）铺成 w×h 的块，**行优先**摆进原版 10×10 格盘
//      （口径照 `Module/Item/Inventory.TryPlace` :94-107；尺寸/偏移照 `UI/InventoryPanel.ItemIconRect`
//      :260-276 —— 图标块左上角与锚点格左上角重合，`preserveAspect` 不压不拉）。
//   ② 摆放算法与"放不下"的处置 = **纯函数** `FindAnchorCell` / `ClaimBlock` / `ComputeLayout`
//      （离线宿主 `tools/probes/hosts/uicheck/ShopGridCheck.cs` 逐格断言：锚点格 / 占用集 / 边界）。
//   ③ 点击映射改读**占用表** `_owner[]`（大件跨多格 ⇒ 格号 ≠ 商品下标；旧的
//      `index → stock[index]` 等号映射会把隔壁那件买/卖出去）。
//   ④ 几何**未动**：10×10 格线是原版底图 `buysell_back.png` 自己画的（实测竖线 14+29k ×11、
//      横线 62+29k ×11，与 `UiLayoutGame` 的 `ShopGridOrigin/ShopCell/ShopCols/Rows` 逐值相等），
//      `BuildGrid` 仍只建"命中区 + 图标层 + 数量"。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>商店面板（原版买卖屏）。层：<see cref="UILayer.Popup"/>。</summary>
    public class ShopPanel : UIPanel
    {
        /// <summary>面板尺寸 = 原版 `buysell_back` 320×432 → ×1.8 = 576×777.6。</summary>
        public static readonly Vector2 PanelSize = UiLayoutGame.ShopSize;

        /// <summary>商品/可卖物品格边长（原版底图实测 pitch 29 → ×1.8）。</summary>
        public static readonly float CellSize = UiLayoutGame.ShopCell;

        /// <summary>商品格总数（原版底图实测 10 列 × 10 行）。</summary>
        public const int CellCount = UiLayoutGame.ShopCols * UiLayoutGame.ShopRows;

        /// <summary>是否处于「卖出」页（false = 买入页）。</summary>
        private bool _sellMode;

        /// <summary>一格 = 命中区 + 图标层 + 数量 + 价格。</summary>
        /// <para>
        /// "格号 == 商品下标"这个等号不再成立，占用关系改由面板的**占用表** `_owner[]` 承载
        /// （见 <see cref="ComputeLayout"/> 与 <see cref="OnCellClick"/>）。
        /// </para>
        private sealed class Cell
        {
            public Image Hit;
            public Image Icon;
            public D2Label Count;
            public D2Label Price;
        }

        /// <summary>
        /// 每格"最近一次发起加载的图标路径"（`null` = 空）—— **本面板自己的缓存**，
        /// 交给 `D2Icon.ApplyItemIcon`（它按这个数组决定是否重复发起异步加载）。
        ///   （没有"文件是否真在磁盘上"那一档）⇒ 原版图标缺文件时 `sprite=null` + `color=白`
        ///   ⇒ Image 画成**一块白方块**（用户报「商店没商品图标」）。现统一走 `D2Icon` 的唯一口径。
        /// </summary>
        private readonly string[] _iconPath = new string[CellCount];

        /// <summary>
        /// <para>
        /// 为什么必须有它：物品按自身占格摆放后，一件 2×3 的商品会跨 6 格 ⇒ "格号" 与 "商品下标"
        /// **不再是同一个数**。点击某一格 = 要买/卖"占着那一格的那件" ⇒ 只能查这张表
        /// </para>
        /// <para>线性下标 = `row * ShopCols + col`（行优先，与 `UiLayoutGame.ShopCellCenter` 同口径）。</para>
        /// </summary>
        private readonly int[] _owner = new int[CellCount];

        private bool _built;
        private bool _subscribed;
        private ShopOpenArgs _shop;

        /// <summary>`[R1-E] S5` 的「只报一次」：标题/提示行接线口径（含实际文案，供实机对账）。</summary>
        private static bool _loggedS5;

        private D2Label _title;
        private D2Label _gold;
        private D2Label _hint;
        private Image _repairButton;
        private Image[] _tabHit = new Image[4];
        private readonly List<Cell> _cells = new List<Cell>();

        /// <inheritdoc/>
        public override UILayer Layer => UILayer.Popup;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();
            Subscribe();

            var shop = UiLog.Require<ShopOpenArgs>(param, nameof(ShopPanel));
            if (shop != null) _shop = shop;
            Rebuild(_shop);

            UiLog.Info($"商店面板已打开（NPC={(_shop != null ? _shop.npcName : "无数据")}，"
                       + $"商品={(_shop?.stock?.Count ?? 0)}，可卖={(_shop?.playerItems?.Count ?? 0)}，"
                       + $"金币={(_shop != null ? _shop.playerGold : 0)}）");
        }

        /// <inheritdoc/>
        public override void OnClose()
        {
            Unsubscribe();
            UiLog.Info("商店面板已关闭");
        }

        private void Build()
        {
            if (_built) return;
            _built = true;

            // 底图 = 原版买卖屏（320×432 → ×1.8 = 576×777.6，居中；**不拉伸**）
            var bg = UiArt.Panel(transform, "BuySellBg", PanelSize, UiLayoutGame.ShopPos, Color.white, true);
            UiArt.SetSprite(bg, ResPaths.PanelBuySellBack);

            BuildTabs();
            BuildTitleLines();
            BuildGrid();
            BuildBottomBar();
        }

        /// <summary>
        /// <para>
        /// 落位 = 页签带与 10×10 格区之间那段**底图空白带**（原版 y ≈ 29..62）；
        /// 为什么只有这里能放、以及两行不相交的核算，见 `UI/UiLayoutGame.cs` §商店 的 S5 注释
        /// 与 `uicheck` 的 ④-2 断言。字模/字号口径与 `_gold` 完全一致（`D2Text.D2Font.Font16` +
        /// `UiLayoutGame.FontPx16`，与同面板的 `_gold` / 格内数量同一套，不在这里另立字号）。
        /// </para>
        /// <para>**原来两行都没给 fontSize**（默认 0 = 按原版 px 1:1 画 ⇒ 只有应有的
        /// ~55%，正是用户报「文字太小」的 6 处之一 —— V5 也注过"字号偏小"）。现补
        /// `(int)UiLayoutGame.FontPx16`（唯一出处）。框 `ShopInfoLineSize` 已是画布单位（288×1.8 × 25），
        /// 两行中心相距 30 ⇒ 字高 28 时两行之间仍余 2px，不与页签带/格区相交。</para>
        /// </summary>
        private void BuildTitleLines()
        {
            _title = D2Label.Create(transform, "ShopTitle", string.Empty, D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, UiArt.TitleColor,
                UiLayoutGame.ShopInfoLineSize, UiLayoutGame.ShopTitlePos,
                (int)UiLayoutGame.FontPx16);

            _hint = D2Label.Create(transform, "ShopHint", string.Empty, D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, UiArt.TextColor,
                UiLayoutGame.ShopInfoLineSize, UiLayoutGame.ShopHintPos,
                (int)UiLayoutGame.FontPx16);

            if (_title == null || _hint == null)
            {
                // 非预期分支：标签没建出来（面板结构问题）⇒ 点名，别静默
                UiLog.Warn($"商店标题/提示行没建出来（title={(_title != null)} hint={(_hint != null)}）"
                    + " ⇒ 标题与页提示不可见（见 ShopPanel.BuildTitleLines）");
            }
        }

        /// <summary>顶部原版页签（`buyselltabs` 8 帧 = 4 页签 × 常态/按下）。</summary>
        private void BuildTabs()
        {
            for (var i = 0; i < 4; i++)
            {
                var slot = i;
                var img = UiArt.Panel(transform, "Tab" + i, UiLayoutGame.ShopTabSize,
                    new Vector2(UiLayoutGame.ShopTabX[i], UiLayoutGame.ShopTabY), Color.white, true);
                UiArt.SetSprite(img, ResPaths.PanelBuySellTabs + "_" + (slot * 2));
                var btn = img.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => OnTab(slot));
                _tabHit[i] = img;
            }
        }

        /// <summary>原版 10×10 商品格（格线是底图画好的 ⇒ 只放命中区 + 图标 + 数量 + 价格）。</summary>
        private void BuildGrid()
        {
            var size = new Vector2(CellSize, CellSize);
            for (var i = 0; i < CellCount; i++)
            {
                var col = i % UiLayoutGame.ShopCols;
                var row = i / UiLayoutGame.ShopCols;
                var pos = UiLayoutGame.ShopCellCenter(col, row);
                var hit = UiArt.Panel(transform, "Cell" + i, size, pos, new Color(1f, 1f, 1f, 0f), true);
                var icon = UiArt.Panel(hit.transform, "Icon", size, Vector2.zero, Color.white, false);
                icon.preserveAspect = true;
                icon.gameObject.SetActive(false);
                var cell = new Cell { Hit = hit, Icon = icon };
                _owner[i] = -1;                 // 占用表初值（Build 早于第一次 Rebuild 时也可用）
                var index = i;
                var btn = hit.gameObject.AddComponent<Button>();
                btn.targetGraphic = hit;
                btn.onClick.AddListener(() => OnCellClick(index));
                cell.Count = D2Label.Create(hit.transform, "Count", string.Empty, D2Text.D2Font.Font16,
                    TextAnchor.LowerRight, new Color(1f, 0.92f, 0.70f, 1f), size, Vector2.zero,
                    (int)UiLayoutGame.FontPx16);
                _cells.Add(cell);
            }
        }

        /// <summary>
        /// 底部：原版信息条（金币，落在底图那个 188×16 名牌里）+ **底图雕出的方槽里的两个动作按钮**。
        /// <para>
        /// ① 底图 `buysell_back.png`（320×432）底部**只有两类雕框** —— 左下 188×16 宽扁名牌
        ///    （x14..201 / y354..370，＝我们的 `ShopInfoBar`，已对齐），右下 **4 个方槽 34×27**
        ///    （列 115-148 / 167-200 / 219-252 / 271-304，y386..411，pitch 52；槽内是暗凹色 R20G20B20）；
        ///    是**浮在空白大理石上**的，没有底图依据（旧 `ShopBottomSlotX/Y/Size` 三个常量算了却没人用）；
        /// ③ 原版同屏唯一的**方形按钮艺术** = `PANEL/buysellbtn.DC6`（18 帧 × 32×32 = 9 钮 × 常态/按下；
        ///    参考工程 Diablerie 的 `Assets/Images/Panels/` 里买卖屏贴图**只有** `buysellbtn.DC6.0.png`
        ///    与 `goldcoinbtn.dc6.0.png`，**没有 `tradebtn`**）；
        /// ④ `tradebtn.DC6`（77×17）与 `buyselltabs` 同为"同底图另一套控件"，而 `trade.DC6` 与 `buysell.DC6`
        ///    本来就是**同一份底图**（帧尺寸与字节数完全相同）⇒ 两者共用背景、控件分属两套。
        /// ⇒ 结论：**方钮的底图 = `buysellbtn`（大理石方钮）**，`tradebtn` 不是这个位置的控件。
        /// 本项目没有玩家间交易（单机），故只放 2 个动作按钮（修理 / 关闭）到**最右两个雕槽**，
        /// 位置直接用底图反推出来的 `UiLayoutGame.ShopBottomSlotX/Y/Size`。
        /// </para>
        /// </summary>
        private void BuildBottomBar()
        {
            //   框 `ShopInfoBarSize` = 原版 183×20 ×1.8 = 329.4×36 画布px ⇒ 字高 28 放得下。
            _gold = D2Label.Create(transform, "Gold", string.Empty, D2Text.D2Font.Font16,
                TextAnchor.MiddleLeft, new Color(0.95f, 0.87f, 0.60f, 1f),
                UiLayoutGame.ShopInfoBarSize, UiLayoutGame.ShopInfoBarPos,
                (int)UiLayoutGame.FontPx16);

            var btnSize = new Vector2(UiLayoutGame.ShopBottomSlotSize, UiLayoutGame.ShopBottomSlotSize);
            //   原版这两颗钮是"纯图形自明"（帧 2 = 锤+铁砧、帧 10 = ⊘），标签铺满时**正好压住图形**。
            _repairButton = UiArt.SquareButton(transform, "RepairAll", "修理", btnSize,
                SlotCenter(2), OnRepairAll, ButtonLabelRect(2));
            // 常态帧 2（= 锤子 + 铁砧），见 ApplyBuySellButtonArt 的逐帧读图表
            ApplyBuySellButtonArt(_repairButton, 2);
            var close = UiArt.SquareButton(transform, "Close", "关闭", btnSize, SlotCenter(3), OnCloseShop,
                ButtonLabelRect(3));
            // 常态帧 10（= 禁止符 ⊘）
            ApplyBuySellButtonArt(close, 10);
        }

        /// <summary>
        /// 相邻雕槽的中心距（= `ShopBottomSlotX[1] − ShopBottomSlotX[0]` = 原版实测 pitch **52** × K = 93.6 画布px）。
        /// <para>标签框宽取它 ⇒ **四个雕槽的标签两两不重叠**（`Rect.Overlaps` 为假），且"52"有底图实测出处
        /// （`策划/验收表.md` E4 / B5：列 115-148 / 167-200 / 219-252 / 271-304，pitch 52）。</para>
        /// </summary>
        public static float SlotPitch
            => UiLayoutGame.ShopBottomSlotX[1] - UiLayoutGame.ShopBottomSlotX[0];

        /// <summary>
        /// 第 <paramref name="slot"/> 个雕槽里那颗方钮的矩形 —— **按钮 local 空间**（以按钮中心为原点）。
        /// 判据用（`tools/probes/hosts/uicheck/ShopArtCheck.cs` ⑤）：标签矩形必须与它**不相交**。
        /// </summary>
        public static Rect ButtonRect(int slot)
        {
            var s = UiLayoutGame.ShopBottomSlotSize;
            return new Rect(-s * 0.5f, -s * 0.5f, s, s);
        }

        /// <summary>
        /// 方钮**标签**矩形 —— **按钮 local 空间**（同 <see cref="ButtonRect"/>，故两者可直接比）。
        /// <para>标签放**钮外正下方居中**，让原版图形（帧 2 的锤+铁砧 / 帧 10 的 ⊘）
        /// **零遮挡**；这是本项目新增的表现（原版该处只有图形、没有文字）⇒ 已把"标签位置 = 钮外正下方"
        /// 作为 **E4 增补**回报主 agent 落表。</para>
        /// <para>几何出处（片 u53-shopart 量法，两路互证；报告 §R2-a）：
        /// ① 钮**居中在内凹区**后，钮下沿 = 原版 y **413**（= <see cref="SlotInnerBottom"/>，钮边长 28 落在 385..413）；
        /// ② 钮下方可用带 = **414..429（16 原版px）**：越过雕槽下框线（414..417）与槽下阴影（418..420），
        ///    止于**面板下边框**（429..430）之上 ⇒ 这是"钮居中 + 标签在钮正下方"唯一还剩的带（恰好 = 标签高）；
        /// ③ 标签高 = `UiLayoutGame.FontPx16`（28.8 画布px = 16 原版px）、**gap = 0**（上沿紧贴钮下沿）；
        /// ④ 框宽 = <see cref="SlotPitch"/>（原版 52）⇒ 相邻标签恰好相接、不重叠。
        /// 已知代价（登记）：标签带 414..429 会**压过雕槽下框线**（414..417）。
        ///    要避开它只能把标签放到面板外或钮上方 —— 二者都偏离 E4 裁定"钮外正下方"，留主 agent 裁。</para>
        /// </summary>
        public static Rect ButtonLabelRect(int slot)
        {
            var s = UiLayoutGame.ShopBottomSlotSize;
            var h = UiLayoutGame.FontPx16;
            var w = SlotPitch;
            var cy = -(s + h) * 0.5f;              // gap = 0：标签上沿 == 钮下沿
            return new Rect(-w * 0.5f, cy - h * 0.5f, w, h);
        }

        // ═════════════════════════════════════════════════════════════════════
        //
        //    · 槽的亮框线：上 = y **382..384**、下 = y **414..417**（行 429..430 是**面板下边框**，不是槽）；
        //    · **内凹区 = y 385..413（29 行）**，x = 115..149 / 167..201 / 219..253 / 271..305（34 宽，pitch 52）；
        //    · ⇒ 槽中心 y = **399**（内凹区中点）、x 中心 = 132 / 184 / 236 / 288。
        //
        //    **381 是槽的「顶沿」，不是槽中心** —— 底图上 381..384 正是那条亮上框线（实测），
        //    而同一批材料在 `ShopPanel.cs` 的 B5 注释里记的是「槽内凹区 y386..411」（中心 398.5）。
        //    两条记录**并存但从未对账** ⇒ 代码采用了 381 当中心 ⇒ 钮被抬高约半高(14)+框(4) = **18px**。
        //    本文件现在**不再读** `UiLayoutGame.ShopBottomSlotY`（那条常量已陈旧，应由其归属者删除/改值；
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>雕槽**内凹区**上沿（原版 y px，实测 = 亮上框线 382..384 之下第一行）。</summary>
        public const float SlotInnerTop = 385f;

        /// <summary>雕槽**内凹区**下沿（原版 y px，实测 = 亮下框线 414..417 之上最后一行）。</summary>
        public const float SlotInnerBottom = 413f;

        /// <summary>雕槽**内凹区**宽（原版 px，实测 34；同 B5 记录）。</summary>
        public const float SlotInnerWidth = 34f;

        /// <summary>雕槽内凹区中心 y（原版 **399**，= 385 与 413 的中点）；画布 y = (216 − 399) × K。</summary>
        public static readonly float SlotInnerCenterY = (SlotInnerTop + SlotInnerBottom) * 0.5f;

        /// <summary>
        /// 第 <paramref name="slot"/> 个雕槽**内凹区**的矩形（**面板 local 空间**，原版 px 口径 ⇒ ×K）。
        /// 判据（`ShopArtCheck` ⑥）用：钮矩形必须 ⊆ 它、且钮中心与它的中心距 ≤ 1 原版px。
        /// </summary>
        public static Rect SlotInnerRect(int slot)
        {
            var cx = UiLayoutGame.ShopBottomSlotX[slot];
            var w = SlotInnerWidth * UiLayoutGame.K;
            var h = (SlotInnerBottom - SlotInnerTop) * UiLayoutGame.K;
            var cy = (216f - SlotInnerCenterY) * UiLayoutGame.K;
            return new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);
        }

        /// <summary>
        /// 第 i 个雕槽里方钮的**位点（面板 local 空间）** = 内凹区中心（越界 ⇒ 退回最后一个并告警，不静默）。
        /// </summary>
        public static Vector2 SlotCenter(int i)
        {
            var xs = UiLayoutGame.ShopBottomSlotX;
            if (xs == null || xs.Length == 0)
                return new Vector2(0f, (216f - SlotInnerCenterY) * UiLayoutGame.K);
            if (i < 0 || i >= xs.Length)
            {
                UiLog.Warn($"商店底部雕槽下标 {i} 越界（底图只有 {xs.Length} 个）⇒ 退回最后一个");
                i = xs.Length - 1;
            }
            return new Vector2(xs[i], (216f - SlotInnerCenterY) * UiLayoutGame.K);
        }

        /// <summary>
        /// 把原版方钮的**指定帧（常态）+ 其下一帧（按下）**当底图贴到按钮上（路径前缀见
        /// `UiArt.BuySellButtonFramePrefix`，那里写了"为什么不用整张条带按名取帧"的实测）。
        /// <para>
        /// 原版 `PANEL/buysellbtn.DC6`（18 帧 × 32×32）= **9 个按钮 × 「常态/按下」连续两帧**：
        /// `0/1` = 空白大理石方钮；**`2/3` = 锤子 + 铁砧（修理）**；`4/5` = 手取物；
        /// `6/7` = 盆/碗（买卖）；**`8/9` = 问号（赌博）**；**`10/11` = 禁止符 ⊘（取消/关闭）**；
        /// `12/13` = ← 左箭头（上一页）；`14/15` = → 右箭头（下一页）；`16/17` = ✓ 对勾（确定）。
        /// </para>
        /// <para>
        /// "参考物已有的东西必须搬运"。
        /// </para>
        /// </summary>
        /// <param name="normalFrame">常态帧号（**必须是偶数**：0/2/4/…/16）；按下帧 = 它 + 1。</param>
        private static void ApplyBuySellButtonArt(Image img, int normalFrame)
        {
            if (img == null) return;
            if (normalFrame % 2 != 0 || normalFrame < 0 || normalFrame + 1 >= 18)
            {
                // 非预期分支：帧号不合法 ⇒ 退回 0/1 并点名（不静默）
                UiLog.Warn($"商店方钮常态帧 {normalFrame} 非法（须为 0/2/…/16 的偶数）⇒ 退回帧 0");
                normalFrame = 0;
            }
            var normal = UiArt.BuySellButtonFramePrefix + normalFrame;
            var pressed = UiArt.BuySellButtonFramePrefix + (normalFrame + 1);
            UiArt.SetSprite(img, normal);
            var btn = img.GetComponent<Button>();
            if (btn == null || Game.Res == null) return;
            Game.Res.LoadAsset<Sprite>(pressed, sp =>
            {
                if (img == null || sp == null) return;
                btn.transition = Selectable.Transition.SpriteSwap;
                btn.spriteState = new SpriteState { pressedSprite = sp, highlightedSprite = sp };
            });
        }

        private void Rebuild(ShopOpenArgs shop)
        {
            if (!_built) return;
            ClearCells();

            if (shop == null)
            {
                UiLog.WarnOnce("shop.no.data",
                    "商店面板未收到 `ShopOpenArgs` ⇒ 空商店；请 Npc 模块在交互时 emit `Events.ShopOpen`");
                if (_gold != null) _gold.SetText("（无数据）");
                UiArt.SetInteractable(_repairButton, false);
                return;
            }

            if (_gold != null) _gold.SetText($"金币: {shop.playerGold}");
            UiArt.SetInteractable(_repairButton, shop.canRepair);
            ApplyTitle();
            if (_sellMode) ApplyPlayerItems(shop.playerItems); else ApplyStock(shop.stock);
        }

        private void ClearCells()
        {
            for (var i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                _owner[i] = -1;                                  // 占用表清零（大件跨的每一格都要放）
                if (i < _iconPath.Length) _iconPath[i] = null;   // 缓存一起清 ⇒ 重开面板会重新发起加载
                if (c.Icon != null)
                {
                    c.Icon.sprite = null;
                    // 图标块**复位成 1 格**：上一页的大件把锚点格的图标层撑成 w×h，
                    //   不复位的话清空后仍留着大 rect（换页/换 NPC 时会出现"残块"）。
                    c.Icon.rectTransform.sizeDelta = new Vector2(CellSize, CellSize);
                    c.Icon.rectTransform.anchoredPosition = Vector2.zero;
                    c.Icon.gameObject.SetActive(false);
                }
                if (c.Count != null)
                {
                    // 数量标签同理复位（大件的数量被移到整块右下角过）
                    c.Count.Root.anchoredPosition = Vector2.zero;
                    c.Count.SetText(string.Empty);
                }
            }
        }

        /// <summary>
        /// 刷新商店的**标题行（NPC 名）+ 提示行（当前页）**（R1-E 的 S5）。
        /// <para>文案口径：NPC 名 = 模块给的 `ShopOpenArgs.npcName`（= 原版串表的 NPC 名，**不是自写**，
        /// 串 id 见 `Module/Npc/NpcModule.Names`）；页名 = 「买入」/「卖出」两个既有的本项目面板用词
        /// （与同面板底部方钮的「修理 / 关闭」同一类：原版这两个词的**串表出处不在本批材料里**
        /// —— 原版 `buyselltabs` 8 帧实测是**纯大理石、没有烘字**，页名在原版也是运行时文字 ⇒
        /// 本项目沿用既有简体用词，登记在回报的末节）。</para>
        /// <para>不改 `_sellMode` 的判定、不新增页签、不改 `OnTab` 行为（S5 只补"从不创建"的那两行）。</para>
        /// </summary>
        private void ApplyTitle()
        {
            var name = _shop != null ? _shop.npcName : "?";
            var page = _sellMode ? "卖出" : "买入";
            if (_title != null) _title.SetText(name);
            if (_hint != null) _hint.SetText(page);

            if (!_loggedS5)
            {
                _loggedS5 = true;
                UiLog.Info("[R1-E] S5 生效：商店标题/提示行已建出并接线 —— 标题行 = 「" + name
                    + "」（模块给的 NPC 名）、提示行 = 「" + page + "」（当前页）；"
                    + "落位 " + UiLayoutGame.ShopTitlePos.y.ToString("0.#") + " / "
                    + UiLayoutGame.ShopHintPos.y.ToString("0.#") + " 画布 y（页签带与格区之间的空带）");
            }
        }

        /// <summary>
        /// 把原版物品图标贴到某一格 —— **走 `D2Icon.ApplyItemIcon` 这个唯一口径**（背包格/装备栏/腰带同款）。
        /// <para>
        /// 原版图标不在本批素材里时（例：`ob1`「鹰眼宝珠」→ `invob1`，实测磁盘上**没有**这张）
        /// 会落成 `sprite=null` + `color=品质色（普通品质=白）` ⇒ Image 画出**一块白方块**，
        /// 看起来就是「商店没商品图标」。`D2Icon.ApplyItemIcon` 有那一档：换 `MissingIconColor`
        /// （暗青灰、一眼看得出是占位）+ `WarnOnce` 点名，并已登记 `client/资源欠缺清单.md`。
        /// </para>
        /// </summary>
        private void SetCellIcon(int index, Cell c, ItemStack item)
        {
            if (c == null || c.Icon == null) return;
            D2Icon.ApplyItemIcon(c.Icon, _iconPath, index, item);
        }

        // ═════════════════════════════════════════════════════════════════════
        //   口径与出处：
        //     · 行优先 / 左上锚点 / 整块在界内且未被占 ⇒ 照 `Module/Item/Inventory.TryPlace`
        //       （:94-107）与 `CanPlaceBlock`（:231-244）；
        //     · 图标块尺寸与偏移 ⇒ 照 `UI/InventoryPanel.ItemIconRect`（:260-276）：
        //       size = (w×CellSize, h×CellSize)、offset = ((w−1)·CellSize/2, −(h−1)·CellSize/2)
        //       （等价于"图标块左上角与锚点格左上角重合"：2×3 的大盾就跨 2 列 3 行）。
        //   下面这些函数是**纯函数**（只碰传入的数组 + `UiLayoutGame` 常量，不碰 Unity 对象）
        //     ⇒ 秒级离线可判（`tools/probes/hosts/uicheck/ShopGridCheck.cs`）。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>一次「按占格摆放」的结果（纯数据；面板与离线宿主共用）。</summary>
        public sealed class ShopLayout
        {
            /// <summary>逐件的**锚点格线性下标**（−1 = 放不下 ⇒ 该件不显示）；下标 = 条目下标。</summary>
            public int[] Anchor;

            /// <summary>**占用表**：`Owner[格] = 条目下标`（−1 = 空）；长度恒 = <see cref="CellCount"/>。</summary>
            public int[] Owner;

            /// <summary>真的摆下的件数。</summary>
            public int Placed;

            /// <summary>放不下（已被点名 Warn）的件数。</summary>
            public int Overflow;
        }

        /// <summary>某件物品的图标块尺寸（= `w×CellSize, h×CellSize`）。`w/h ≤ 0` ⇒ 按 1 算。</summary>
        public static Vector2 IconSize(int w, int h)
            => new Vector2((w > 0 ? w : 1) * CellSize, (h > 0 ? h : 1) * CellSize);

        /// <summary>
        /// 图标块**相对锚点格中心**的偏移（= `((w−1)·CellSize/2, −(h−1)·CellSize/2)`）——
        /// 效果是图标块左上角与锚点格左上角重合（口径同 `UI/InventoryPanel.ItemIconRect`）。
        /// </summary>
        public static Vector2 IconOffset(int w, int h)
            => new Vector2((w > 0 ? w : 1) - 1f, -((h > 0 ? h : 1) - 1f)) * (CellSize * 0.5f);

        /// <summary>`col/row` 处能否放下 `w×h` 的块（越界 / 与已占格重叠 ⇒ false）。</summary>
        public static bool CanPlaceBlock(int[] occupant, int col, int row, int w, int h)
        {
            if (occupant == null || col < 0 || row < 0) return false;
            if (w < 1 || h < 1) return false;
            if (col + w > UiLayoutGame.ShopCols || row + h > UiLayoutGame.ShopRows) return false;

            for (var r = row; r < row + h; r++)
            {
                for (var c = col; c < col + w; c++)
                {
                    if (occupant[r * UiLayoutGame.ShopCols + c] >= 0) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// **行优先**找第一个能容下 `w×h` 的锚点格（线性下标）；放不下返回 **−1**（不改动任何格）。
        /// 比格区还大（`w &gt; ShopCols || h &gt; ShopRows`）⇒ 直接 −1（必然放不下）。
        /// </summary>
        public static int FindAnchorCell(int[] occupant, int w, int h)
        {
            if (occupant == null || w < 1 || h < 1) return -1;
            if (w > UiLayoutGame.ShopCols || h > UiLayoutGame.ShopRows) return -1;

            for (var row = 0; row < UiLayoutGame.ShopRows; row++)
            {
                for (var col = 0; col < UiLayoutGame.ShopCols; col++)
                {
                    if (!CanPlaceBlock(occupant, col, row, w, h)) continue;
                    return row * UiLayoutGame.ShopCols + col;
                }
            }
            return -1;
        }

        /// <summary>把 `anchorCell` 起 `w×h` 的块记到占用表（`owner` = 条目下标）。</summary>
        public static void ClaimBlock(int[] occupant, int anchorCell, int w, int h, int owner)
        {
            if (occupant == null || anchorCell < 0 || anchorCell >= occupant.Length) return;
            var col = anchorCell % UiLayoutGame.ShopCols;
            var row = anchorCell / UiLayoutGame.ShopCols;
            for (var r = row; r < row + h && r < UiLayoutGame.ShopRows; r++)
            {
                for (var c = col; c < col + w && c < UiLayoutGame.ShopCols; c++)
                {
                    occupant[r * UiLayoutGame.ShopCols + c] = owner;
                }
            }
        }

        /// <summary>已占格数（占用表里 ≥ 0 的格数）。</summary>
        public static int UsedCells(int[] occupant)
        {
            if (occupant == null) return 0;
            var n = 0;
            for (var i = 0; i < occupant.Length; i++)
            {
                if (occupant[i] >= 0) n++;
            }
            return n;
        }

        /// <summary>
        /// 买入页：把 NPC 的商品**按自身占格**行优先摆进 10×10。
        /// 放不下的**点名 Warn** 且**不覆盖**已摆好的货（不静默丢弃）。
        /// </summary>
        public static ShopLayout ComputeLayout(IReadOnlyList<ShopEntry> stock)
        {
            var res = NewLayout(stock?.Count ?? 0);
            for (var i = 0; i < res.Anchor.Length; i++)
            {
                var e = stock[i];
                if (e == null)
                {
                    UiLog.Warn($"商品列表第 {i} 条为 null ⇒ 跳过（不占格）");
                    continue;
                }
                ClaimOrWarn(res, i, e.gridW, e.gridH, $"商品第 {i} 件「{e.name}」");
            }
            return res;
        }

        /// <summary>卖出页：背包锚点格物品**按自身占格**行优先摆进 10×10（同买入页口径）。</summary>
        public static ShopLayout ComputeLayout(IReadOnlyList<InventorySlot> items)
        {
            var res = NewLayout(items?.Count ?? 0);
            for (var i = 0; i < res.Anchor.Length; i++)
            {
                var slot = items[i];
                if (slot?.item == null)
                {
                    UiLog.Warn($"可卖物品第 {i} 条为空 ⇒ 跳过（不占格）");
                    continue;
                }
                ClaimOrWarn(res, i, slot.item.gridW, slot.item.gridH, $"可卖物品第 {i} 件「{slot.item.name}」");
            }
            return res;
        }

        private static ShopLayout NewLayout(int count)
        {
            var res = new ShopLayout { Anchor = new int[count], Owner = new int[CellCount] };
            for (var i = 0; i < res.Anchor.Length; i++) res.Anchor[i] = -1;
            for (var i = 0; i < res.Owner.Length; i++) res.Owner[i] = -1;
            return res;
        }

        /// <summary>摆一件：放得下 ⇒ 记占用表并返回锚点格；放不下 ⇒ 点名 Warn + 计数（该件不显示）。</summary>
        private static void ClaimOrWarn(ShopLayout res, int index, int gridW, int gridH, string what)
        {
            var w = gridW > 0 ? gridW : 1;
            var h = gridH > 0 ? gridH : 1;
            var cell = FindAnchorCell(res.Owner, w, h);
            if (cell < 0)
            {
                res.Overflow++;
                UiLog.Warn($"{what}（{w}×{h}）放不下 ⇒ 该件不显示"
                    + $"（已占 {UsedCells(res.Owner)}/{CellCount} 格；分页未接线，⛔ 不覆盖已摆好的货）");
                return;
            }
            ClaimBlock(res.Owner, cell, w, h, index);
            res.Anchor[index] = cell;
            res.Placed++;
        }

        /// <summary>
        /// 把一件已定位的物品画到锚点格上：图标层 = `w×h` 格大小、左上角对齐锚点格；
        /// 数量标签挪到整块的右下角（`TextAnchor.LowerRight` 不动 ⇒ 只挪框，字号/字模口径不变）。
        /// </summary>
        private void PlaceItemVisual(int anchorCell, int w, int h, ItemStack item, string countText)
        {
            if (anchorCell < 0 || anchorCell >= _cells.Count) return;
            var c = _cells[anchorCell];

            if (c.Icon != null)
            {
                c.Icon.rectTransform.sizeDelta = IconSize(w, h);
                c.Icon.rectTransform.anchoredPosition = IconOffset(w, h);
            }
            if (c.Count != null)
            {
                c.Count.Root.anchoredPosition = new Vector2((w - 1) * CellSize, -(h - 1) * CellSize);
                c.Count.SetText(countText);
            }
            SetCellIcon(anchorCell, c, item);
        }

        /// <summary>把一次摆放写成一条运行时可抄的**数值日志**（锚点格 + 已占格数 + 放不下的件数）。</summary>
        private static void LogLayout(string page, int total, ShopLayout layout)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < layout.Anchor.Length; i++)
            {
                var cell = layout.Anchor[i];
                if (cell < 0) continue;
                sb.Append(" 第").Append(i).Append("件@格(")
                  .Append(cell % UiLayoutGame.ShopCols).Append(',')
                  .Append(cell / UiLayoutGame.ShopCols).Append(')');
            }
            UiLog.Info($"商店{page}页占格摆放：{layout.Placed}/{total} 件已摆（占 "
                + $"{UsedCells(layout.Owner)}/{CellCount} 格，{layout.Overflow} 件放不下）" + sb);
        }

        /// <summary>买入页：NPC 的商品**按自身占格**铺进 10×10（行优先、左上锚点、两两不重叠）。</summary>
        private void ApplyStock(List<ShopEntry> stock)
        {
            var layout = ComputeLayout(stock);
            for (var i = 0; i < _owner.Length; i++) _owner[i] = layout.Owner[i];

            for (var i = 0; i < layout.Anchor.Length; i++)
            {
                var cell = layout.Anchor[i];
                if (cell < 0) continue;
                var e = stock[i];
                PlaceItemVisual(cell, e.gridW > 0 ? e.gridW : 1, e.gridH > 0 ? e.gridH : 1,
                    new ItemStack { itemId = e.itemId, quality = e.quality },
                    e.count < 0 ? "∞" : (e.count > 1 ? e.count.ToString() : string.Empty));
            }
            LogLayout("买入", stock?.Count ?? 0, layout);
        }

        /// <summary>卖出页：背包锚点格物品**按自身占格**铺进 10×10（同买入页口径）。</summary>
        private void ApplyPlayerItems(List<InventorySlot> items)
        {
            var layout = ComputeLayout(items);
            for (var i = 0; i < _owner.Length; i++) _owner[i] = layout.Owner[i];

            for (var i = 0; i < layout.Anchor.Length; i++)
            {
                var cell = layout.Anchor[i];
                if (cell < 0) continue;
                var item = items[i].item;
                PlaceItemVisual(cell, item.gridW > 0 ? item.gridW : 1, item.gridH > 0 ? item.gridH : 1,
                    item, item.count > 1 ? item.count.ToString() : string.Empty);
            }
            LogLayout("卖出", items?.Count ?? 0, layout);
        }

        private void OnCellClick(int cellIndex)
        {
            if (_shop == null || cellIndex < 0 || cellIndex >= _owner.Length) return;

            //   不再用 `index → stock[index]` 的等号映射 —— 大件跨多格后，格号 ≠ 商品下标，
            //   旧映射点大件右侧/下方的格会把**隔壁那件**买/卖出去。
            var itemIndex = _owner[cellIndex];
            if (itemIndex < 0) return;

            if (!_sellMode)
            {
                var stock = _shop.stock;
                if (stock == null || itemIndex >= stock.Count) return;
                OnBuy(stock[itemIndex]);
                return;
            }

            var items = _shop.playerItems;
            if (items == null || itemIndex >= items.Count) return;
            var slot = items[itemIndex];
            OnSell(slot.isAnchor ? slot.index : slot.anchorIndex, slot.item);
        }

        private void OnTab(int tab)
        {
            if (tab >= 2)
            {
                UiLog.Warn($"原版页签 {tab}（修理/赌博）本项目未实装 ⇒ 只提示");
                Game.UI.Toast("该页签本项目未实装");
                return;
            }

            _sellMode = tab == 1;
            UiLog.Info($"商店切到「{(_sellMode ? "卖出" : "买入")}」页");
            ApplyTabArt();
            Rebuild(_shop);
        }

        private void ApplyTabArt()
        {
            for (var i = 0; i < _tabHit.Length; i++)
            {
                if (_tabHit[i] == null) continue;
                var on = (_sellMode && i == 1) || (!_sellMode && i == 0);
                UiArt.SetSprite(_tabHit[i], ResPaths.PanelBuySellTabs + "_" + (i * 2 + (on ? 1 : 0)));
            }
        }

        private void OnBuy(ShopEntry entry)
        {
            if (_shop == null || entry == null) return;
            if (!entry.affordable)
            {
                UiLog.Info($"买「{entry.name}」被拒：金币不足");
                Game.UI.Toast("金币不足");
                return;
            }

            var args = new ShopTradeArgs { npcId = _shop.npcId, index = entry.index, count = 1 };
            UiLog.Info($"请求买入「{entry.name}」（index={entry.index}，价 {entry.price}）");
            Game.Event.Emit(Events.ShopBuyRequest, args);
        }

        private void OnSell(int anchorIndex, ItemStack item)
        {
            if (_shop == null || anchorIndex < 0) return;
            if (item != null && item.isQuestItem)
            {
                UiLog.Warn($"「{item.name}」是任务物品，原版不可出售 ⇒ 不发请求");
                Game.UI.Toast("任务物品不能出售");
                return;
            }

            var a1 = new ShopTradeArgs { npcId = _shop.npcId, index = anchorIndex, count = 1 };
            UiLog.Info($"请求卖出背包锚点格 {anchorIndex}（{item?.name}）");
            Game.Event.Emit(Events.ShopSellRequest, a1);
        }

        private void OnRepairAll()
        {
            if (_shop == null) return;
            if (!_shop.canRepair)
            {
                UiLog.Warn("该 NPC 不提供修理 ⇒ 不发请求");
                return;
            }

            var a2 = new ShopTradeArgs { npcId = _shop.npcId, index = -1, count = 1 };
            UiLog.Info($"请求全部修理（费用 {_shop.repairAllCost}）");
            Game.Event.Emit(Events.ShopRepairRequest, a2);
        }

        private void OnCloseShop()
        {
            UiLog.Info("商店被玩家关闭 ⇒ 发 `Events.ShopClose`");
            Game.Event.Emit(Events.ShopClose);
            Game.UI.Close<ShopPanel>();
        }

        private void Subscribe()
        {
            if (_subscribed || Game.Event == null) return;
            _subscribed = true;
            Game.Event.On<ShopOpenArgs>(Events.ShopOpen, OnShopOpen);
            Game.Event.On<ShopOpenArgs>(Events.ShopChanged, OnShopChanged);
            Game.Event.On(Events.ShopClose, OnShopCloseEvent);
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Game.Event == null) return;
            _subscribed = false;
            Game.Event.Off<ShopOpenArgs>(Events.ShopOpen, OnShopOpen);
            Game.Event.Off<ShopOpenArgs>(Events.ShopChanged, OnShopChanged);
            Game.Event.Off(Events.ShopClose, OnShopCloseEvent);
        }

        private void OnShopOpen(ShopOpenArgs args)
        {
            if (args == null) { UiLog.Warn("收到商店打开事件但参数为 null ⇒ 忽略"); return; }
            _shop = args;
            Rebuild(_shop);
        }

        private void OnShopChanged(ShopOpenArgs args)
        {
            if (args == null) { UiLog.Warn("收到商店变化事件但参数为 null ⇒ 忽略"); return; }
            _shop = args;
            Rebuild(_shop);
        }

        private void OnShopCloseEvent()
        {
            if (!Game.UI.IsOpen<ShopPanel>()) return;
            UiLog.Info("收到 `Events.ShopClose` ⇒ 关闭商店面板");
            Game.UI.Close<ShopPanel>();
        }
    }
}

