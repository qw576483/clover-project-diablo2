// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/ShopPanel.cs（agent-09 · 1:1 轮）
// 商店面板 = 原版买卖屏：底图 buysell_back.png（320×432 → ×1.8 居中），
// 商品格 = 原版那 10×10 格，**格内贴原版物品图标**
//
// ★ 数据（不变）：`Diablo2.Def.ShopOpenArgs`（npcId/npcName/canRepair/playerGold/
//   stock(List<ShopEntry>)/playerItems(List<InventorySlot>)/repairAllCost）。
// ★ 请求（不变）：`Events.ShopBuyRequest` / `ShopSellRequest` / `ShopRepairRequest` / `ShopClose`。
// ⛔ 零 `using Diablo2.Module`（分层自检 ③）。
//
// ★ R1-E 的 S5（本片）：`_title` / `_hint` 原先**声明了却从不创建** ⇒ `ApplyTitle()` 恒空转、
//   商店上看不到"这是谁家的、现在哪一页"。现在两行由 `BuildTitleLines()` 建出并接线
//   （落位 = 页签带与 10×10 格区之间的底图空白带，依据与核算见 `UI/UiLayoutGame.cs` §商店 的 S5 注释）。
//   ⚠️ 本面板的**层仍是 `Popup`**，这是 R1-E 的 S1 要的（对话条降到 Normal 让位给商店遮罩，
//      商店必须在遮罩**之上**才点得动；依据见 `UI/NpcDialogPanel.cs` 文件头的 S1）。
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
        private sealed class Cell
        {
            public Image Hit;
            public Image Icon;
            public D2Label Count;
            public D2Label Price;
            public int Slot = -1;            // 当前占用的下标（-1 = 空）
        }

        /// <summary>
        /// 每格"最近一次发起加载的图标路径"（`null` = 空）—— **本面板自己的缓存**，
        /// 交给 `D2Icon.ApplyItemIcon`（它按这个数组决定是否重复发起异步加载）。
        /// ★ 本轮新增：原先缓存在 `Cell.IconPath` 上、图标逻辑也在本文件里**又写了一遍**
        ///   （没有"文件是否真在磁盘上"那一档）⇒ 原版图标缺文件时 `sprite=null` + `color=白`
        ///   ⇒ Image 画成**一块白方块**（用户报「商店没商品图标」）。现统一走 `D2Icon` 的唯一口径。
        /// </summary>
        private readonly string[] _iconPath = new string[CellCount];

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
        /// ★ R1-E 的 **S5**：把原先**声明了却从不创建**的 `_title` / `_hint` 真建出来
        /// （改前 `ApplyTitle()` 恒空转 ⇒ 商店上看不到"这是谁家的、现在哪一页"）。
        /// <para>
        /// 落位 = 页签带与 10×10 格区之间那段**底图空白带**（原版 y ≈ 29..62）；
        /// 为什么只有这里能放、以及两行不相交的核算，见 `UI/UiLayoutGame.cs` §商店 的 S5 注释
        /// 与 `uicheck` 的 ④-2 断言。字模/字号口径与 `_gold` 完全一致（`D2Text.D2Font.Font16`，
        /// 不传 fontSize ⇒ 原生档，与同面板的 `_gold` / 格内数量同一套，⛔ 不在这里另立字号）。
        /// </para>
        /// </summary>
        private void BuildTitleLines()
        {
            _title = D2Label.Create(transform, "ShopTitle", string.Empty, D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, UiArt.TitleColor,
                UiLayoutGame.ShopInfoLineSize, UiLayoutGame.ShopTitlePos);

            _hint = D2Label.Create(transform, "ShopHint", string.Empty, D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, UiArt.TextColor,
                UiLayoutGame.ShopInfoLineSize, UiLayoutGame.ShopHintPos);

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
                var index = i;
                var btn = hit.gameObject.AddComponent<Button>();
                btn.targetGraphic = hit;
                btn.onClick.AddListener(() => OnCellClick(index));
                cell.Count = D2Label.Create(hit.transform, "Count", string.Empty, D2Text.D2Font.Font16,
                    TextAnchor.LowerRight, new Color(1f, 0.92f, 0.70f, 1f), size, Vector2.zero);
                _cells.Add(cell);
            }
        }

        /// <summary>
        /// 底部：原版信息条（金币，落在底图那个 188×16 名牌里）+ **底图雕出的方槽里的两个动作按钮**。
        /// <para>
        /// ★ agent-23 收口「底部按钮底图到底是 tradebtn 还是大理石按钮」这条悬案（**实测依据**）：
        /// ① 底图 `buysell_back.png`（320×432）底部**只有两类雕框** —— 左下 188×16 宽扁名牌
        ///    （x14..201 / y354..370，＝我们的 `ShopInfoBar`，已对齐），右下 **4 个方槽 34×27**
        ///    （列 115-148 / 167-200 / 219-252 / 271-304，y386..411，pitch 52；槽内是暗凹色 R20G20B20）；
        /// ② **全图没有任何 77×17 的槽位** ⇒ 旧实现把 `tradebtn`（77×17 → ×1.8 = 138.6×30.6）摆在那里，
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
            _gold = D2Label.Create(transform, "Gold", string.Empty, D2Text.D2Font.Font16,
                TextAnchor.MiddleLeft, new Color(0.95f, 0.87f, 0.60f, 1f),
                UiLayoutGame.ShopInfoBarSize, UiLayoutGame.ShopInfoBarPos);

            var btnSize = new Vector2(UiLayoutGame.ShopBottomSlotSize, UiLayoutGame.ShopBottomSlotSize);
            _repairButton = UiArt.SquareButton(transform, "RepairAll", "修理", btnSize,
                SlotPos(2), OnRepairAll);
            // 常态帧 2（= 锤子 + 铁砧），见 ApplyBuySellButtonArt 的逐帧读图表
            ApplyBuySellButtonArt(_repairButton, 2);
            var close = UiArt.SquareButton(transform, "Close", "关闭", btnSize, SlotPos(3), OnCloseShop);
            // 常态帧 10（= 禁止符 ⊘）
            ApplyBuySellButtonArt(close, 10);
        }

        /// <summary>`ShopBottomSlotX` 第 i 个雕槽的中心（越界 ⇒ 退回最后一个并告警，不静默）。</summary>
        private static Vector2 SlotPos(int i)
        {
            var xs = UiLayoutGame.ShopBottomSlotX;
            if (xs == null || xs.Length == 0) return new Vector2(0f, UiLayoutGame.ShopBottomSlotY);
            if (i < 0 || i >= xs.Length)
            {
                UiLog.Warn($"商店底部雕槽下标 {i} 越界（底图只有 {xs.Length} 个）⇒ 退回最后一个");
                i = xs.Length - 1;
            }
            return new Vector2(xs[i], UiLayoutGame.ShopBottomSlotY);
        }

        /// <summary>
        /// 把原版方钮的**指定帧（常态）+ 其下一帧（按下）**当底图贴到按钮上（路径前缀见
        /// `UiArt.BuySellButtonFramePrefix`，那里写了"为什么不用整张条带按名取帧"的实测）。
        /// <para>
        /// ★ 片 10 修正（**读图实测**，联络图 `.ai-tmp/test/p10_buysellbtn.png`，8× 最近邻放大）：
        /// 原版 `PANEL/buysellbtn.DC6`（18 帧 × 32×32）= **9 个按钮 × 「常态/按下」连续两帧**：
        /// `0/1` = 空白大理石方钮；**`2/3` = 锤子 + 铁砧（修理）**；`4/5` = 手取物；
        /// `6/7` = 盆/碗（买卖）；**`8/9` = 问号（赌博）**；**`10/11` = 禁止符 ⊘（取消/关闭）**；
        /// `12/13` = ← 左箭头（上一页）；`14/15` = → 右箭头（下一页）；`16/17` = ✓ 对勾（确定）。
        /// </para>
        /// <para>
        /// 旧实现**一律贴帧 0/1**（空白方钮）⇒ 实机上是"两个没有图形的空方块"，
        /// 而原版这两个位置的图形是**自明**的（修理 = 锤砧、关闭 = 禁止符）—— 属于§0.5 的
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
                c.Slot = -1;
                if (i < _iconPath.Length) _iconPath[i] = null;   // 缓存一起清 ⇒ 重开面板会重新发起加载
                if (c.Icon != null)
                {
                    c.Icon.sprite = null;
                    c.Icon.gameObject.SetActive(false);
                }
                c.Count?.SetText(string.Empty);
            }
        }

        /// <summary>
        /// 刷新商店的**标题行（NPC 名）+ 提示行（当前页）**（R1-E 的 S5）。
        /// <para>文案口径：NPC 名 = 模块给的 `ShopOpenArgs.npcName`（= 原版串表的 NPC 名，**不是自写**，
        /// 串 id 见 `Module/Npc/NpcModule.Names`）；页名 = 「买入」/「卖出」两个既有的本项目面板用词
        /// （与同面板底部方钮的「修理 / 关闭」同一类：原版这两个词的**串表出处不在本批材料里**
        /// —— 原版 `buyselltabs` 8 帧实测是**纯大理石、没有烘字**，页名在原版也是运行时文字 ⇒
        /// 本项目沿用既有简体用词，登记在回报的末节）。</para>
        /// <para>⛔ 不改 `_sellMode` 的判定、不新增页签、不改 `OnTab` 行为（S5 只补"从不创建"的那两行）。</para>
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
        /// ★ 本轮修正：本文件原先**自己又写了一遍**取图标逻辑，且**少了"文件是否真在磁盘上"那一档** ⇒
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

        /// <summary>买入页：NPC 的商品按行优先铺满 10×10 格。</summary>
        private void ApplyStock(List<ShopEntry> stock)
        {
            var n = stock?.Count ?? 0;
            if (n > CellCount)
                UiLog.Warn($"商品共 {n} 条 > 面板 {CellCount} 格 ⇒ 只显示前 {CellCount} 条（分页未接线）");

            for (var i = 0; i < n && i < CellCount; i++)
            {
                var e = stock[i];
                if (e == null) { UiLog.Warn($"商品列表第 {i} 条为 null ⇒ 跳过"); continue; }
                var c = _cells[i];
                c.Slot = i;
                SetCellIcon(i, c, new ItemStack { itemId = e.itemId, quality = e.quality });
                c.Count?.SetText(e.count < 0 ? "∞" : (e.count > 1 ? e.count.ToString() : string.Empty));
            }
        }

        /// <summary>卖出页：背包锚点格物品按行优先铺满 10×10 格。</summary>
        private void ApplyPlayerItems(List<InventorySlot> items)
        {
            var n = items?.Count ?? 0;
            for (var i = 0; i < n && i < CellCount; i++)
            {
                var slot = items[i];
                if (slot?.item == null) { UiLog.Warn($"可卖物品第 {i} 条为空 ⇒ 跳过"); continue; }
                var c = _cells[i];
                c.Slot = i;
                SetCellIcon(i, c, slot.item);
                c.Count?.SetText(slot.item.count > 1 ? slot.item.count.ToString() : string.Empty);
            }
        }

        private void OnCellClick(int index)
        {
            if (_shop == null || index < 0 || index >= _cells.Count) return;
            var c = _cells[index];
            if (c.Slot < 0) return;

            if (!_sellMode)
            {
                var stock = _shop.stock;
                if (stock == null || index >= stock.Count) return;
                OnBuy(stock[index]);
                return;
            }

            var items = _shop.playerItems;
            if (items == null || index >= items.Count) return;
            var slot = items[index];
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

