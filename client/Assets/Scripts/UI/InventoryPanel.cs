// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/InventoryPanel.cs（agent-09 · 1:1 轮）
// 背包面板：原版 `inventory.png`(320×432) 底图 ×1.8 + **10×4 背包格** + 装备栏（`inv_*`）
// + 底部「金币按钮 + 金币数字 + 关闭按钮」；支持拖放、点击装/卸、右键丢地上、悬停 tooltip。
//
// ★★ 本轮（1:1）改了什么：
//   ① 面板与全部子元素一律 **×1.8 居中**（原版 800×600 → 本工程 1920×1080；口径见 `UiLayoutGame`）；
//   ② 装备栏 10 个槽的矩形**改用 `InventoryPanel.prefab` 的节点实测值**
//      （head/neck/tors/rarm/larm/glov/belt/rrin/lrin/feet —— 见 `UiLayoutGame.InvEquipOrig`），
//      不再用上一轮"量底图"的近似值（差 2px 量级，但既然有精确值就用精确值）；
//   ③ 底部补齐原版三个节点：`GoldButton`(20×17 @ -65.5,-184.1)、
//      `CloseButton`(32×31 @ -125.8,-184.1)、`GoldText`(87.1×15.2 @ -7,-183.2)；
//   ④ **删掉面板里那 4 格「腰带行」**——`InventoryPanel.prefab` **没有**这一行
//      （原版腰带只在底部控制面板上，本项目的 HUD 已按原版位置画了 4 格）；
//      留着它就会压住原版底图右下角那一片大理石底纹 ⇒ 1:1 要求删。
//   ⑤ **★ agent-27：物品按自身尺寸占格（修用户报的「背包里道具占格子还是不对」）**：
//      图标层不再固定成"1 格大小"，而是按 `item_c.grid_w/grid_h` 铺成 `w×h` 格的块，
//      **左上角与锚点格左上角重合**（2×4 的盔甲就跨 2 列 4 行）。见 `ItemIconRect`。
//      格网本身 = 原版底图实测 pitch（29.2 / 29.25 原版px → ×1.8 = 52.56 / 52.65），
//      与原版 `InventoryPanel.prefab` 的 10×4 一致（`GameConst.InventoryCols/Rows`）。
//   ⑥ **★ w4：装备槽底图不再被拉变形**（用户报「装备格被拉变形」）。
//      旧实现 = 把整幅 `inv_*.png` 塞进 prefab 节点矩形里，而原版这些贴图**四周有透明边**
//      （`inv_armor.png` 64×128 里不透明内容只占 54×81）⇒ 横竖压缩比不同（84.4% vs 64.8%）；
//      双槽拼图（`inv_helm_glove` / `inv_ring_amulet`）按"半宽"裁又会带出隔壁槽的图案。
//      现口径 = **裁剪框（素材外接框 ×K）+ 整幅贴图（IHDR ×K，1:1 不缩放）**，
//      实测常量在 `UiLayoutGame.InvEquipArt`（逐像素列投影求出的连通块），见 `BuildEquipFrame`。
//
// ★ 格子对齐：**格子必须和底图画出来的框重合**（这是肉眼判据），故格宽/格高取
//   `inventory.png` 的**格线实测**（竖线 x=17,46,…,309 ⇒ 29.2；横线 y=252,281,310,339,369 ⇒ 29.25），
//   首格左上角 = 贴图 (17,252) → 面板中心坐标 (-143, -36)。→ ×1.8 见 `UiLayoutGame`。
//   （prefab 的 `Grid` 外框 287.8×114.9 与格线差 1.5%：外框是装饰边框，格线才是玩家看得见的格子。）
//
// ★ 分层：`UILayer.Popup`（引擎对 Popup 自动互斥 + 遮罩，见 `Runtime/Presentation/UI.cs:124-128`）。
// ★ 数据：只吃 `Diablo2.Def.InventoryChangedArgs`；**零 `using Diablo2.Module`**（分层自检 ③）。
// ★ 交互请求全部走 `Core/Events.cs` 的事件（**载荷打包以 `Events.cs` 的注释为准**）：
//     EquipToggleRequest(int 锚点格) / ItemDropRequest(int 锚点格) / UseBeltRequest(int 0..3)；
//     UnequipRequest(int = `((int)ItemSlot & 0xFF) | (slotIndex << 8)`)，
//     MoveInInventoryRequest(int = `fromAnchor | (toAnchor << 16)`)。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>
    /// 一次拖拽**落点判定**的结论（★ U4；由 <see cref="InventoryPanel.PlanDrop"/> 产出，纯函数、离线可断言）。
    /// </summary>
    internal enum DropKind
    {
        /// <summary>无效落点（原地 / 面板内空白）⇒ 不产生任何请求。</summary>
        Ignore = 0,

        /// <summary>落在装备槽上 ⇒ `Events.EquipToggleRequest`。</summary>
        Equip = 1,

        /// <summary>落在背包格上 ⇒ `Events.MoveInInventoryRequest`（<c>value</c> = 目标锚点格）。</summary>
        Move = 2,

        /// <summary>落在面板外 ⇒ `Events.ItemDropRequest`（丢到地面）。</summary>
        DropToGround = 3,
    }

    /// <summary>背包面板（10×4 格 + 装备栏 + 底部金币/关闭 + 拖放 + tooltip）。</summary>
    public class InventoryPanel : UIPanel, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        // ═════════════════════════════════════════════════════════════════════
        // 布局常量（**全部 = 原版 prefab 值 → ×1.8 居中**，见 `UI/UiLayoutGame.cs` 的逐条出处）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>面板尺寸（原版 320×432 → ×1.8 = 576×777.6）。</summary>
        public static readonly Vector2 PanelSize = UiLayoutGame.InvPanelSize;

        /// <summary>面板中心（原版 Panel pivot(0,0.5)@(0,0) ⇒ 原版矩形 x 0..320 ⇒ 我们的 (288,0)）。</summary>
        public static readonly Vector2 PanelPos = UiLayoutGame.InvPanelPos;

        /// <summary>背包列数（= `GameConst.InventoryCols`）。</summary>
        public const int Cols = GameConst.InventoryCols;

        /// <summary>背包行数（= `GameConst.InventoryRows`）。</summary>
        public const int Rows = GameConst.InventoryRows;

        /// <summary>背包格总数（40，= `GameConst.InventoryCellCount`）。</summary>
        public const int CellCount = GameConst.InventoryCellCount;

        /// <summary>单格宽（底图实测 29.2 → ×1.8 = 52.56）。</summary>
        public const float CellW = UiLayoutGame.InvCellW;

        /// <summary>单格高（底图实测 29.25 → ×1.8 = 52.65）。</summary>
        public const float CellH = UiLayoutGame.InvCellH;

        /// <summary>
        /// 拖拽时**目标格高亮**的颜色（半透明白）。
        /// <para>⛔ 这不是"自选外观"：原版拖拽时目标格是**亮起的框**（`InvMoveOverFx`/格高亮），
        /// 而本机没有那一帧素材（登记 `client/资源欠缺清单.md`），故用半透明覆盖块替代；
        /// 语义（"这里会落下"）与原版一致，**取色**为本项目选定并在此登记。</para>
        /// </summary>
        public static readonly Color DropHighlightColor = new Color(1f, 1f, 1f, 0.30f);

        /// <summary>格区左上角（面板中心坐标；底图 (17,252) ⇒ (-143, -36) → ×1.8）。</summary>
        public static readonly Vector2 GridOrigin = UiLayoutGame.InvGridOrigin;

        /// <summary>底部「金币按钮」矩形（原版 GoldButton 20×17 @ (-65.5,-184.1) → ×1.8）。</summary>
        public static readonly Vector2 GoldButtonPos = UiLayoutGame.InvGoldButtonPos;
        public static readonly Vector2 GoldButtonSize = UiLayoutGame.InvGoldButtonSize;

        /// <summary>底部「关闭按钮」矩形（原版 CloseButton 32×31 @ (-125.8,-184.1) → ×1.8）。</summary>
        public static readonly Vector2 CloseButtonPos = UiLayoutGame.InvCloseButtonPos;
        public static readonly Vector2 CloseButtonSize = UiLayoutGame.InvCloseButtonSize;

        /// <summary>底部「金币数字」矩形（原版 GoldText 87.1×15.2 @ (-7,-183.2) → ×1.8）。</summary>
        public static readonly Vector2 GoldTextPos = UiLayoutGame.InvGoldTextPos;
        public static readonly Vector2 GoldTextSize = UiLayoutGame.InvGoldTextSize;

        /// <summary>一个装备槽的静态定义（矩形 + 对应的 `ItemSlot` + 底图 + 是否只画贴图的一半）。</summary>
        public struct EquipSlotDef
        {
            /// <summary>`Def.ItemSlot`。</summary>
            public ItemSlot slot;

            /// <summary>同槽多件时的下标（戒指 0/1）。</summary>
            public int slotIndex;

            /// <summary>槽中心（**面板矩形中心**坐标，= 原版 prefab 的 `m_AnchoredPosition` → ×1.8）。</summary>
            public Vector2 center;

            /// <summary>槽尺寸（= 原版 prefab 的 `m_SizeDelta` → ×1.8）。**只是贴图/图标的排布框**，
            /// **不是**裁剪框 —— 裁剪框用 <see cref="artSize"/>（素材实测，见 `UiLayoutGame.InvEquipArt`）。</summary>
            public Vector2 size;

            /// <summary>底图完整路径（`ResPaths.D2UiEquipSlot + 文件名`）。</summary>
            public string sprite;

            /// <summary>底图是否只取一半：0 = 整幅、1 = 左半、2 = 右半（`inv_helm_glove` / `inv_ring_amulet` 是双槽拼图）。</summary>
            public int half;

            /// <summary>本槽图形在素材里的**不透明内容外接框尺寸**（**原版px**，逐像素实测；出处见 `UiLayoutGame.InvEquipArt`）。</summary>
            public Vector2 artOrig;

            /// <summary>裁剪框尺寸（画布）= <see cref="artOrig"/> × K。同时是**命中区**（tooltip / 拖放落点）。</summary>
            public Vector2 artSize;

            /// <summary>贴图**整幅**的原尺寸（画布）= 素材 IHDR × K（`inv_helm_glove` 等双槽图是两槽并排的整幅）。</summary>
            public Vector2 sheetSize;

            /// <summary>整幅贴图中心**相对裁剪框中心**的偏移（画布）：把"内容外接框"摆到裁剪框里（y 轴已翻到画布向上）。</summary>
            public Vector2 sheetOffset;
        }

        /// <summary>
        /// 10 个装备槽（**中心** = `InventoryPanel.prefab` 的节点实测值 → ×1.8 居中；节点名见注释）。
        /// 戒指两枚分别取 `rrin` / `lrin` 两个原版节点。
        /// <para>
        /// ★ w4：绘制几何（裁剪框 / 整幅贴图尺寸与偏移）**另有一张素材实测表**
        /// <see cref="UiLayoutGame.InvEquipArt"/>（逐像素列投影求出的本槽图形外接框）。
        /// 为什么必须分开：prefab 节点值 = **社区复刻工程**量的框（与素材实测相差 0～2 原版px），
        /// 而"贴图该怎么摆才不被拉变形/不带出隔壁槽的图案"只能由**素材自己**回答。
        /// </para>
        /// </summary>
        public static readonly EquipSlotDef[] EquipSlots =
        {
            FromOrig(ItemSlot.Weapon, 0, "rarm"),   // 右手武器 54.93×108.7
            FromOrig(ItemSlot.Helm,   0, "head"),   // 头盔     54×54
            FromOrig(ItemSlot.Amulet, 0, "neck"),   // 项链     24.15×24.3
            FromOrig(ItemSlot.Armor,  0, "tors"),   // 盔甲     54×83
            FromOrig(ItemSlot.Shield, 0, "larm"),   // 左手盾   54×109.6
            FromOrig(ItemSlot.Gloves, 0, "glov"),   // 手套     54.9×54.2
            FromOrig(ItemSlot.Ring,   0, "rrin"),   // 戒指 1   23.7×24.125
            FromOrig(ItemSlot.Belt,   0, "belt"),   // 腰带     52×24
            FromOrig(ItemSlot.Ring,   1, "lrin"),   // 戒指 2   24.1×25
            FromOrig(ItemSlot.Boots,  0, "feet"),   // 靴子     53.9×55.3
        };

        /// <summary>
        /// 按原版节点名取几何（查不到 ⇒ 打 Warn 并退回零尺寸，不静默）。
        /// <para>
        /// 位置/尺寸出自**两张表**，各管一段，别混：
        ///   · `UiLayoutGame.InvEquipOrig`（社区复刻工程的 prefab 节点值）⇒ 槽**中心**（唯一位置出处）+ `size`；
        ///   · `UiLayoutGame.InvEquipArt`（**素材逐像素实测**）⇒ 裁剪框 `artSize` 与整幅贴图 `sheetSize/sheetOffset`。
        /// </para>
        /// </summary>
        private static EquipSlotDef FromOrig(ItemSlot slot, int slotIndex, string node)
        {
            var src = UiLayoutGame.InvEquipOrig;
            for (var i = 0; i < src.Length; i++)
            {
                if (src[i].node != node) continue;

                var art = ArtOf(node);
                if (art.artOrig.x <= 0f || art.artOrig.y <= 0f)
                {
                    UiLog.Warn($"装备槽「{slot}#{slotIndex}」的素材实测表里找不到「{node}」"
                               + " ⇒ 该槽按零尺寸处理（请核对 UiLayoutGame.InvEquipArt）");
                }

                return new EquipSlotDef
                {
                    slot = slot,
                    slotIndex = slotIndex,
                    center = src[i].center * UiLayoutGame.K,
                    size = src[i].size * UiLayoutGame.K,
                    sprite = ResPaths.D2UiEquipSlot + src[i].file,
                    half = src[i].half,
                    artOrig = art.artOrig,
                    artSize = art.artOrig * UiLayoutGame.K,
                    sheetSize = art.sheetSize,
                    sheetOffset = art.sheetOffset,
                };
            }

            UiLog.Warn($"装备槽「{slot}#{slotIndex}」在原版 prefab 节点表里找不到「{node}」"
                       + " ⇒ 该槽按零尺寸处理（请核对 UiLayoutGame.InvEquipOrig）");
            return new EquipSlotDef
            {
                slot = slot, slotIndex = slotIndex,
                sprite = ResPaths.D2UiEquipSlot + "inv_weapons",
            };
        }

        /// <summary>
        /// 从 `UiLayoutGame.InvEquipArt`（素材逐像素实测）算出某槽的**绘制几何**：
        /// 裁剪框（= 该槽图形外接框 ×K）、整幅贴图尺寸（= IHDR ×K）、以及
        /// 「把外接框摆进裁剪框」所需的整幅偏移。全部由实测常量导出，本函数不含魔数。
        /// </summary>
        internal static (Vector2 artOrig, Vector2 sheetSize, Vector2 sheetOffset) ArtOf(string node)
        {
            var t = UiLayoutGame.InvEquipArt;
            for (var i = 0; i < t.Length; i++)
            {
                if (t[i].node != node) continue;

                var w = t[i].x1 - t[i].x0;
                var h = t[i].y1 - t[i].y0;
                // 外接框中心（图内坐标）
                var cx = (t[i].x0 + t[i].x1) * 0.5f;
                var cy = (t[i].y0 + t[i].y1) * 0.5f;
                // 整幅中心相对外接框中心的偏移（x 同向；**y 要翻号**：图内 y 向下、画布 y 向上）
                var dx = (t[i].sw * 0.5f - cx) * UiLayoutGame.K;
                var dy = (t[i].sh * 0.5f - cy) * UiLayoutGame.K;
                return (new Vector2(w, h),
                        new Vector2(t[i].sw * UiLayoutGame.K, t[i].sh * UiLayoutGame.K),
                        new Vector2(dx, -dy));
            }

            return (Vector2.zero, Vector2.zero, Vector2.zero);
        }

        /// <summary>
        /// 第 <paramref name="index"/> 个背包格的中心（面板中心坐标）；越界返回零并打 Warn。
        /// 纯函数 ⇒ `tools/uicheck` 会断言「40 格互不重叠且行优先」。
        /// </summary>
        public static Vector2 CellCenter(int index)
        {
            if (index < 0 || index >= CellCount)
            {
                UiLog.WarnThrottled("inv.cell.range", $"背包格下标越界：{index}（合法 0..{CellCount - 1}）⇒ 按 (0,0) 处理");
                return Vector2.zero;
            }

            var col = index % Cols;
            var row = index / Cols;
            return new Vector2(
                GridOrigin.x + (col + 0.5f) * CellW,
                GridOrigin.y - (row + 0.5f) * CellH);
        }

        /// <summary>
        /// 锚点格上**物品图标层的尺寸与偏移**（相对锚点格中心；**纯函数，离线可断言**）。
        /// <para>
        /// 口径（原版 D2 的做法）：物品**按自身尺寸占格** —— 图标块的宽 = `gridW × CellW`、
        /// 高 = `gridH × CellH`，其**左上角与锚点格的左上角重合**：
        /// 锚点格宽高 = 1 格，图标块中心相对格中心 = `((gridW−1)·CellW/2, −(gridH−1)·CellH/2)`。
        /// 2×4 的盔甲 ⇒ 108.12×210.6 的大图标跨 2 列 4 行（不是缩在 1 格里）。
        /// </para>
        /// <para>
        /// `gridW/gridH` 越出格区（>10 / >4）时夹到格区大小并 Warn（配表 `item_c.grid_w/grid_h`
        /// 来自官方 `invwidth/invheight`，越界说明配错了，必须能查）。
        /// </para>
        /// </summary>
        internal static (Vector2 size, Vector2 offset) ItemIconRect(ItemStack item)
        {
            var w = item != null && item.gridW > 0 ? item.gridW : 1;
            var h = item != null && item.gridH > 0 ? item.gridH : 1;

            if (w > Cols || h > Rows)
            {
                UiLog.WarnThrottled("inv.icon.oversize." + w + "x" + h,
                    $"物品「{item?.name}」占格 {w}×{h} 超出背包格区 {Cols}×{Rows}"
                    + " ⇒ 图标按格区大小夹取（请核对配表 item_c.grid_w/grid_h）");
                if (w > Cols) w = Cols;
                if (h > Rows) h = Rows;
            }

            return (new Vector2(w * CellW, h * CellH),
                    new Vector2((w - 1) * CellW * 0.5f, -(h - 1) * CellH * 0.5f));
        }

        // ═════════════════════════════════════════════════════════════════════
        // 运行时
        // ═════════════════════════════════════════════════════════════════════
        private bool _built;
        private bool _subscribed;
        private InventoryChangedArgs _data;

        private readonly Image[] _cells = new Image[CellCount];
        private readonly Image[] _cellIcons = new Image[CellCount];   // ★ agent-18 §B：原版物品图标层
        private readonly D2Label[] _cellCounts = new D2Label[CellCount];
        private readonly Image[] _equipIcons = new Image[EquipSlots.Length];
        private readonly RectTransform[] _equipRects = new RectTransform[EquipSlots.Length];

        /// <summary>
        /// 每个背包格/装备槽**最近一次贴上的图标路径**（`null` = 空）。
        /// 为什么要缓存：`Rebuild` 会被每次 `Events.InventoryChanged` 触发（拾取/移动都发），
        /// 而贴图是**异步**加载的 ⇒ 不比较就会每次都重新发起一次加载（白跑 + 覆盖拖影状态）。
        /// </summary>
        private readonly string[] _cellIconPath = new string[CellCount];
        private readonly string[] _equipIconPath = new string[EquipSlots.Length];

        private D2Label _goldText;
        private ItemTooltip _tooltip;
        /// <summary>`OnDrag` 里"目标格"状态变化才报一次日志（见 <see cref="OnDrag"/>；-1 = 指针下无格）。</summary>
        private int _loggedDropCell = int.MinValue;

        private Image _ghost;

        /// <summary>
        /// ★ U4：拖拽时**目标格高亮**（原版手感 = 拖影跟随鼠标 + 目标格高亮；见 `DragAndDrop` 注释）。
        /// <c>null</c> / 未拖拽时不显示。
        /// </summary>
        private Image _dropHighlight;
        private int _dragAnchor = -1;

        // ═════════════════════════════════════════════════════════════════════
        // 请求事件（**离线可断言**；载荷打包即契约，逐字对齐 `Core/Events.cs` 的注释）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// `Events.UnequipRequest` 的载荷：`((int)slot &amp; 0xFF) | (slotIndex &lt;&lt; 8)`。
        /// 与 `Core/Events.cs` 的常量注释、`App/AppEventRouting.OnUnequipRequest` 的解码**同口径**。
        /// </summary>
        public static int PackUnequip(ItemSlot slot, int slotIndex)
            => ((int)slot & 0xFF) | (slotIndex << 8);

        /// <summary>`Events.MoveInInventoryRequest` 的载荷：`fromAnchor | (toAnchor &lt;&lt; 16)`（口径同 `Core/Events.cs`）。</summary>
        public static int PackMoveInInventory(int fromAnchor, int toAnchor)
            => fromAnchor | (toAnchor << 16);

        /// <summary>请求卸下某装备槽（`Events.UnequipRequest`）——本面板 UI 的**唯一出口**，便于离线断言。</summary>
        public static void EmitUnequipRequest(ItemSlot slot, int slotIndex)
        {
            var packed = PackUnequip(slot, slotIndex);
            UiLog.Info($"点击装备槽 {slot}#{slotIndex} ⇒ 请求卸下 `{Events.UnequipRequest}`(packed={packed})");
            Game.Event.Emit(Events.UnequipRequest, packed);
        }

        /// <summary>请求在背包内移动/交换物品（`Events.MoveInInventoryRequest`）——**唯一出口**，便于离线断言。</summary>
        public static void EmitMoveInInventoryRequest(int fromAnchor, int toAnchor)
        {
            var packed = PackMoveInInventory(fromAnchor, toAnchor);
            UiLog.Info($"拖放：锚点格 {fromAnchor} → 锚点格 {toAnchor} ⇒ "
                       + $"请求背包内移动 `{Events.MoveInInventoryRequest}`(packed={packed})");
            Game.Event.Emit(Events.MoveInInventoryRequest, packed);
        }

        /// <summary>请求把背包某锚点格的物品丢到地面（`Events.ItemDropRequest`）——**唯一出口**，便于离线断言。</summary>
        public static void EmitItemDropRequest(int anchor)
        {
            UiLog.Info($"请求丢弃背包锚点格 {anchor} 的物品 ⇒ `{Events.ItemDropRequest}`");
            Game.Event.Emit(Events.ItemDropRequest, anchor);
        }

        /// <summary>
        /// 某背包格作为**拖放落点**时的锚点格索引（**纯函数**，离线可断言）：
        /// 被占用 ⇒ 该物品的锚点；空格 ⇒ 自身下标；越界 / 空数据 ⇒ -1。
        /// </summary>
        internal static int DropAnchorOf(InventoryChangedArgs data, int cellIndex)
        {
            if (data?.inventory == null || cellIndex < 0 || cellIndex >= data.inventory.Count) return -1;
            var slot = data.inventory[cellIndex];
            if (slot == null) return -1;
            return slot.occupied ? slot.anchorIndex : cellIndex;
        }

        /// <inheritdoc/>
        public override UILayer Layer => UILayer.Popup;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();
            Subscribe();

            var data = UiLog.Require<InventoryChangedArgs>(param, nameof(InventoryPanel));
            if (data != null) _data = data;
            Rebuild(_data);

            UiLog.Info($"背包面板已打开（数据={(data != null ? "有快照" : "无 ⇒ 空格显示")}，"
                       + $"格数={CellCount}（{Cols}×{Rows}），装备槽={EquipSlots.Length}，"
                       + $"布局=原版 InventoryPanel.prefab ×{UiLayoutGame.K}）");
        }

        /// <inheritdoc/>
        public override void OnClose()
        {
            Unsubscribe();
            _tooltip?.Destroy();
            _tooltip = null;
            _dragAnchor = -1;
            UiLog.Info("背包面板已关闭");
        }

        /// <inheritdoc/>
        public override void OnUpdate(float dt)
        {
            UpdateHover();
            _tooltip?.Tick();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 构件
        // ═════════════════════════════════════════════════════════════════════
        private void Build()
        {
            if (_built) return;
            _built = true;

            // ★ 片 K（R8）：底图**必须吃射线** —— 口径与 `ShopPanel.BuySellBg`（`raycastTarget=true`）一致。
            //   为什么：本图是**面板矩形**（576×777.6，非满屏）⇒ 它是"面板本体"。
            //   若不吃射线，点面板内部空白处时 `EventSystem.IsPointerOverGameObject()` 为 false
            //   ⇒ `Module/Input/InputReader.UiEatsIntent(pressed:true, pointerOverUi:false)` 返回 false
            //   ⇒ 这次点击被反投影成"点地面" ⇒ **角色会走向面板背后的地面**（原版语义：点面板空白不移动）。
            //   面板**外**点地面照旧可走（本 Image 只覆盖面板矩形，不是满屏）。
            var bg = UiArt.Panel(transform, "InventoryBg", PanelSize, PanelPos, Color.white, true);
            UiArt.SetSprite(bg, ResPaths.PanelInventory);

            BuildGrid();
            BuildEquipSlots();
            BuildBottomRow();

            // 拖影（拖放时跟着鼠标）+ 目标格高亮（原版手感：拖影跟随 + 目标格亮起）
            _ghost = UiArt.Panel(transform, "DragGhost", new Vector2(CellW, CellH), Vector2.zero,
                new Color(1f, 1f, 1f, 0.65f), false);
            _ghost.gameObject.SetActive(false);

            _dropHighlight = UiArt.Panel(transform, "DropHighlight", new Vector2(CellW, CellH), Vector2.zero,
                DropHighlightColor, false);
            _dropHighlight.gameObject.SetActive(false);

            _tooltip = ItemTooltip.Create(transform);
        }

        private void BuildGrid()
        {
            for (var i = 0; i < CellCount; i++)
            {
                // 格线已经画在 `inventory.png` 上 ⇒ 命中区保持**近乎透明**（只做点击/拖放/tooltip），
                // 有物品时才由品质色表达（原版也是"框 + 物品图"）。
                var cell = UiArt.Panel(transform, "Cell" + i, new Vector2(CellW, CellH), PanelPos + CellCenter(i),
                    new Color(1f, 1f, 1f, 0f), true);
                _cells[i] = cell;

                // ★ agent-18 §B：原版物品图标层（`D2/Items/inv{code}.png`，由 d2data.mpq 的
                //   `data/global/items/inv*.DC6` 解出）。空物品 ⇒ 隐藏（**不画色块**：原版空格就是空的，
                //   格线由 `inventory.png` 底图提供）。
                var icon = UiArt.Panel(cell.transform, "Icon", new Vector2(CellW, CellH), Vector2.zero,
                    new Color(1f, 1f, 1f, 1f), false);
                icon.preserveAspect = true;      // ★ 原版像素画：按比例内缩，绝不拉变形
                icon.gameObject.SetActive(false);
                _cellIcons[i] = icon;

                // ★ 片 font-scale：补显式字号（原来默认 0 = 按原版 px 1:1 画 ⇒ 格上数量只有应有的
                //   ~55%，用户报「文字太小」的 6 处之一）。字号唯一出处 = `UiLayoutGame.FontPx16`。
                _cellCounts[i] = D2Label.Create(cell.transform, "Count", string.Empty, D2Text.D2Font.Font16,
                    TextAnchor.LowerRight, UiArt.ButtonText, new Vector2(CellW, CellH), Vector2.zero,
                    (int)UiLayoutGame.FontPx16);
            }
        }

        private void BuildEquipSlots()
        {
            // ① 先建**全部裁剪框 + 底图**（贴图是异步回来的，与图标层无依赖）
            for (var i = 0; i < EquipSlots.Length; i++)
                _equipRects[i] = BuildEquipFrame(EquipSlots[i], i);

            // ② 再建**全部图标层**（原版物品图），且**不挂在裁剪框里** ——
            //    裁剪框会按素材外接框裁子节点，挂进去会让稍大的装备图标（如盔甲 2×3 格）
            //    被切掉边。分两轮建 ⇒ 图标层一律画在底图之上，且没有任何裁剪。
            for (var i = 0; i < EquipSlots.Length; i++)
            {
                var icon = UiArt.Panel(transform, "EquipIcon" + i, EquipSlots[i].size, PanelPos + EquipSlots[i].center,
                    new Color(1f, 1f, 1f, 0.92f), false);
                icon.gameObject.SetActive(false);       // 有装备时才有颜色（`D2Icon.ApplyItemIcon` 会置 preserveAspect）
                _equipIcons[i] = icon;
            }
        }

        /// <summary>
        /// 装备槽底图 = **裁剪框（素材外接框 ×K）+ 整幅贴图（IHDR ×K，1:1 不缩放）**。
        /// <para>
        /// 为什么必须这样（★ w4 修「装备格被拉变形」）：
        ///   ① 原版 `inv_*.png` **四周有透明边**（`inv_armor.png` 64×128 里内容只占 54×81）；
        ///      旧实现把整幅塞进内容大小的矩形 ⇒ 横竖压缩比不同（84.4% vs 64.8%）= 非等比拉伸；
        ///   ② 双槽拼图（`inv_helm_glove` 128×64 / `inv_ring_amulet` 64×32）的两个图形
        ///      **不在半宽处切开**（实测在 x 54/55 与 x 23/24 两列空列处分开）——
        ///      按半宽裁会把隔壁槽的图案带进来 / 把自己切掉一块；
        ///      现改成「裁剪框 = 本槽图形外接框」（实测常量见 `UiLayoutGame.InvEquipArt`）。
        /// </para>
        /// <para>位置：裁剪框中心 = prefab 节点中心（`def.center`）；整幅贴图按其外接框偏移摆进去。</para>
        /// <para>命中区 = 裁剪框（<c>_equipRects[i]</c>）⇒ tooltip / 拖放落点都只覆盖画出来的那块图形。</para>
        /// </summary>
        private RectTransform BuildEquipFrame(EquipSlotDef def, int index)
        {
            var holder = UIFactory.CreateCentered("Equip" + index, transform, def.artSize, PanelPos + def.center);
            holder.gameObject.AddComponent<RectMask2D>();

            var full = UiArt.Panel(holder, "Art", def.sheetSize, def.sheetOffset, Color.white, false);
            UiArt.SetSprite(full, def.sprite);
            return holder;
        }

        /// <summary>
        /// 底部一行（原版 `InventoryPanel.prefab` 的三个节点，位置/尺寸逐条 ×1.8）：
        /// `CloseButton`(关闭) + `GoldButton`(金币按钮，原版帧 `goldcoinbtn.dc6.0` 0/1 = 常态/按下)
        /// + `GoldText`(金币数字，位图字体)。
        /// <para>⚠️ 关闭按钮的「X」图形**不在本批素材里**（底图只有一个凹槽）⇒ 命中区按原版矩形摆放，
        /// 图形登记在 `client/资源欠缺清单.md`，**不自己画一个**（1:1 硬标准）。</para>
        /// </summary>
        private void BuildBottomRow()
        {
            // ── 关闭按钮：命中区 + 原版底图的凹槽（点一下 = 关面板，与原版 `CloseButton` 同效）──
            var close = UiArt.Panel(transform, "CloseButton", CloseButtonSize, PanelPos + CloseButtonPos,
                new Color(1f, 1f, 1f, 0f), true);
            var closeBtn = close.gameObject.AddComponent<Button>();
            closeBtn.targetGraphic = close;
            closeBtn.onClick.AddListener(() =>
            {
                UiLog.Info("点背包关闭按钮 ⇒ 走 `Events.PanelToggleRequest` 关闭（与按 I/Esc 同一条路径）");
                Game.Event.Emit(Events.PanelToggleRequest, nameof(InventoryPanel));
            });

            // ── 金币按钮：原版 `goldcoinbtn.dc6.0` 两帧（0=常态 1=按下），帧尺寸实测 20×17 ──
            // ⚠️ 逐帧按名加载在本工程取不到（见 `UiArt.RequestStrip` 的实测注释）⇒ 一律走条带取帧。
            var goldBtn = UiArt.Panel(transform, "GoldButton", GoldButtonSize, PanelPos + GoldButtonPos,
                Color.white, true);
            var gb = goldBtn.gameObject.AddComponent<Button>();
            gb.targetGraphic = goldBtn;
            UiArt.RequestStrip(ResPaths.PanelGoldCoinButton, ResPaths.FrameCountGoldCoinButton, frames =>
            {
                if (goldBtn == null || gb == null) return;
                var normal = frames != null && frames.Length > 0 ? frames[0] : null;
                if (normal == null)
                {
                    UiLog.Warn($"金币按钮原版底图缺失：{ResPaths.PanelGoldCoinButton}（保持纯色/空白）");
                    return;
                }
                goldBtn.sprite = normal;
                goldBtn.color = Color.white;
                UiArt.EnsureUnlit(goldBtn);

                var pressed = frames.Length > 1 ? frames[1] : null;
                if (pressed == null)
                {
                    UiLog.Warn($"金币按钮按下帧缺失：{ResPaths.PanelGoldCoinButton} 帧 1（只保留常态帧）");
                    return;
                }
                gb.transition = Selectable.Transition.SpriteSwap;
                gb.spriteState = new SpriteState { pressedSprite = pressed, highlightedSprite = pressed };
            });
            gb.onClick.AddListener(() => UiLog.Info("点金币按钮 ⇒ 原版是「把钱丢地上」；本项目未接线（回报「未接线」）"));

            // ── 金币数字（原版 GoldText）：位图字体、居中 ──
            // ★ 片 font-scale：补显式字号（默认 0 = 原版 px 1:1 ⇒ 金币数只有应有的 ~55%）。
            _goldText = D2Label.Create(transform, "Gold", "0", D2Text.D2Font.Font16, TextAnchor.MiddleCenter,
                UiArt.TitleColor, GoldTextSize, PanelPos + GoldTextPos, (int)UiLayoutGame.FontPx16);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 刷新（**状态值只在 OnOpen / 事件回调里刷**）
        // ═════════════════════════════════════════════════════════════════════
        private void Rebuild(InventoryChangedArgs data)
        {
            if (!_built)
            {
                UiLog.Warn("背包面板尚未构建就收到刷新 ⇒ 忽略");
                return;
            }

            if (data == null)
            {
                UiLog.WarnOnce("inv.no.data",
                    "背包面板没有收到 `InventoryChangedArgs`（OnOpen 参数为空且模块未 emit 过全量快照）⇒ 先按空背包显示；"
                    + "请 Item 模块在进图装配完成后 emit 一次 `Events.InventoryChanged`");
            }

            var inv = data?.inventory;
            if (inv != null && inv.Count != CellCount)
            {
                UiLog.Warn($"背包快照格数 {inv.Count} 与面板格数 {CellCount} 不一致（GameConst.InventoryCols/Rows 变了？）"
                           + " ⇒ 多余的格忽略、缺的格按空处理");
            }

            for (var i = 0; i < CellCount; i++)
            {
                var slot = inv != null && i < inv.Count ? inv[i] : null;
                var item = slot != null ? slot.item : null;

                // 格子本身**永远透明**（原版格线由 `inventory.png` 底图提供，不能被色块盖住）
                _cells[i].color = new Color(1f, 1f, 1f, 0f);

                // ★ agent-27：物品图标层按**物品自身的占格**（`item_c.grid_w × grid_h`）铺开，
                //   左上角与锚点格的左上角重合 —— 原版就是"图占满它那几格"，不许缩进单格里。
                //   其余被覆盖的格 `slot.item == null`（模块契约：只有锚点格挂 item）⇒ 天然不重复画。
                var icon = _cellIcons[i];
                var rect = ItemIconRect(item);
                icon.rectTransform.sizeDelta = rect.size;
                icon.rectTransform.anchoredPosition = rect.offset;
                ApplyItemIcon(icon, _cellIconPath, i, item);

                var count = item != null && item.count > 1 ? item.count.ToString() : string.Empty;
                _cellCounts[i].SetText(count);
            }

            ApplyEquip(data?.equip);

            // 金币用原版位图字体（纯数字/英文）——没有数据时显示 0 而不是空白（原版也不留空）
            _goldText.SetText((data != null ? data.gold : 0).ToString());
        }

        private void ApplyEquip(List<ItemStack> equip)
        {
            for (var i = 0; i < EquipSlots.Length; i++)
            {
                var def = EquipSlots[i];
                var item = FindEquipped(equip, def.slot, def.slotIndex);

                if (item == null)
                {
                    _equipIcons[i].gameObject.SetActive(false);
                    continue;
                }

                ApplyItemIcon(_equipIcons[i], _equipIconPath, i, item);
            }
        }

        /// <summary>
        /// ★ agent-18 §B：把**原版物品图标**贴到某个图标层上（背包格 / 装备槽）。
        /// 实现收在 `UI/D2Icon.ApplyItemIcon`（HUD 腰带的图标层也调它，避免两处口径分叉）。
        /// </summary>
        private static void ApplyItemIcon(Image icon, string[] cache, int index, ItemStack item)
            => D2Icon.ApplyItemIcon(icon, cache, index, item);

        /// <summary>按槽位找已装备物品（同槽多件用 <paramref name="slotIndex"/> 区分；戒指两枚）。</summary>
        internal static ItemStack FindEquipped(List<ItemStack> equip, ItemSlot slot, int slotIndex)
        {
            if (equip == null) return null;

            var seen = 0;
            for (var i = 0; i < equip.Count; i++)
            {
                var item = equip[i];
                if (item == null) continue;

                var itemSlot = SlotOf(item);
                if (itemSlot != slot) continue;

                if (seen == slotIndex) return item;
                seen++;
            }
            return null;
        }

        /// <summary>
        /// 物品 → 装备槽。`Def.ItemStack` 只有大类（武器/防具/杂项）与格数，没有细分类字段
        /// ⇒ 这里用**格数**按原版口径推定（1×1 杂项不占槽）。缺字段记在回报「未接线」。
        /// </summary>
        private static ItemSlot SlotOf(ItemStack item)
        {
            if (item.type == ItemType.Weapon) return ItemSlot.Weapon;
            if (item.type != ItemType.Armor) return ItemSlot.None;

            switch (item.gridW)
            {
                case 2 when item.gridH >= 4: return ItemSlot.Armor;
                case 2 when item.gridH == 3: return ItemSlot.Shield;
                case 2 when item.gridH == 2: return ItemSlot.Helm;
                case 2 when item.gridH == 1: return ItemSlot.Belt;
                case 1 when item.gridH == 1: return ItemSlot.Ring;
                default:
                    UiLog.WarnOnce("inv.slot.guess." + item.gridW + "x" + item.gridH,
                        $"防具 {item.name}（{item.gridW}×{item.gridH} 格）无法按格数推定槽位 ⇒ 按盔甲处理");
                    return ItemSlot.Armor;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 交互（点击 / 拖放 / 悬停）
        // ═════════════════════════════════════════════════════════════════════
        /// <inheritdoc/>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null) return;

            // ★ U4 根因修复（用户报「道具没法拖动」+「点装备格没反应」）：
            //   旧实现用 `eventData.pointerPress` 找被点的格子，但 uGUI 把 `pointerPress` 填成
            //   **收了 PointerDown 的那个 GameObject** —— 本面板把 `IPointerClickHandler` /
            //   `IDragHandler` 实现在 **面板根**（`InventoryPanel` 组件挂在根节点）上，格子只是
            //   子 `Image`（`Cell{N}`）⇒ `pointerPress` **恒 = 面板根**，而
            //   `IndexOfCell(面板根)` 恒为 -1 ⇒ 左键装备 / 拖拽**永远进不去**（`OnBeginDrag`
            //   打的是「从非背包格开始拖拽」那条 Info）。诊断见 `.ai-tmp/test/report-U4.md`。
            //   改为与 `UpdateHover` / `CellAt` 完全同一套**屏幕点矩形命中**（同一真源，
            //   离线宿主可逐格断言）⇒ 不再依赖 uGUI 的 `pointerPress` 语义。
            var screen = eventData.position;

            var cell = CellAt(screen);
            if (cell >= 0)
            {
                var anchor = AnchorOf(cell);
                if (anchor < 0) return;

                if (eventData.button == PointerEventData.InputButton.Right)
                {
                    EmitItemDropRequest(anchor);
                    return;
                }

                UiLog.Info($"背包格 {cell}（锚点 {anchor}）左键 ⇒ 请求装备/使用（`{Events.EquipToggleRequest}`）");
                Game.Event.Emit(Events.EquipToggleRequest, anchor);
                return;
            }

            var equip = EquipAt(screen);
            if (equip >= 0 && (eventData.button == PointerEventData.InputButton.Right
                               || eventData.button == PointerEventData.InputButton.Left))
            {
                // 左键/右键点装备槽 = 卸下：
                //   `Events.UnequipRequest`(打包槽位) → `App/AppEventRouting` → `IItemModule.Unequip(slot, slotIndex)`
                var def = EquipSlots[equip];
                EmitUnequipRequest(def.slot, def.slotIndex);
            }
        }

        /// <inheritdoc/>
        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragAnchor = -1;
            if (eventData == null) return;

            // ★ U4：与 `OnPointerClick` 同一处根因 —— 不再用 `eventData.pointerPress`
            //   （它恒 = 面板根，判别不出是哪个格），改用屏幕点矩形命中。
            var cell = CellAt(eventData.position);
            if (cell < 0)
            {
                // 只支持从背包格拖起（装备槽/腰带拖拽需要模块侧的移动接口，见回报「未接线」）
                UiLog.Info($"从非背包格开始拖拽（屏幕点 {eventData.position}）⇒ 本次拖拽不生效"
                           + "（装备槽拖拽需要 `IItemModule` 的移动接口，未接线）");
                return;
            }

            var anchor = AnchorOf(cell);
            if (anchor < 0)
            {
                UiLog.Info($"从空格 {cell} 拖拽 ⇒ 忽略");
                return;
            }

            _dragAnchor = anchor;
            var item = _data != null && anchor < _data.inventory.Count ? _data.inventory[anchor]?.item : null;
            // ★ agent-18 §B：拖影也用**原版物品图**（取不到才退回品质色块）
            _ghost.color = item != null ? ItemQualityColor.Of(item.quality) : Color.white;
            _ghost.sprite = null;
            if (item != null)
            {
                var iconPath = D2Icon.ItemIconPath(item.itemId);
                if (string.IsNullOrEmpty(iconPath))
                {
                    UiLog.Warn($"拖影取不到原版物品图（itemId={item.itemId}「{item.name}」）⇒ 退回品质色块");
                }
                else
                {
                    UiArt.SetArtTint(_ghost, D2Icon.QualityTint(item.quality));
                    UiArt.SetSprite(_ghost, iconPath);
                }
            }
            _ghost.gameObject.SetActive(true);
            _ghost.rectTransform.sizeDelta = item != null
                ? new Vector2(item.gridW * CellW, item.gridH * CellH)
                : new Vector2(CellW, CellH);
            UiLog.Info($"开始拖拽背包物品（锚点格 {anchor}，{item?.name ?? "?"}）");
        }

        /// <inheritdoc/>
        public void OnDrag(PointerEventData eventData)
        {
            if (_dragAnchor < 0 || _ghost == null) return;

            // 拖影跟随鼠标（原版：拖影贴在指针上）
            _ghost.rectTransform.position = Game.Input != null
                ? new Vector3(Game.Input.MousePosition.x, Game.Input.MousePosition.y, 0f)
                : _ghost.rectTransform.position;

            // 目标格高亮（原版：指针下的背包格亮起，作为"松手会落到这里"的反馈）
            if (_dropHighlight == null) return;
            var screen = Game.Input != null
                ? new Vector2(Game.Input.MousePosition.x, Game.Input.MousePosition.y)
                : eventData.position;
            var cell = CellAt(screen);
            if (cell < 0)
            {
                // ★ V6：这条分支原来**静默**（实机只看到"目标格高亮没出现"，查不出是"没命中格"
                //   还是"高亮没画出来"）⇒ 补一条**只在状态变化时**报点名的日志（非预期分支必须留痕，
                //   且不逐帧刷屏）。判据：V6 的 Play 日志里 `[Ui] 拖拽目标格` / `拖拽指针下无格`。
                if (_loggedDropCell != -1)
                {
                    _loggedDropCell = -1;
                    UiLog.Warn($"拖拽中指针下没有背包格（屏幕点 {screen}）⇒ 目标格高亮关闭；"
                        + "若玩家确实拖在格上，请查 `_cells` 矩形与画布换算（`RectTransformUtility`）");
                }
                _dropHighlight.gameObject.SetActive(false);
                return;
            }

            if (_loggedDropCell != cell)
            {
                _loggedDropCell = cell;
                UiLog.Info($"拖拽目标格 = {cell}（高亮已开，色 {DropHighlightColor}）");
            }
            _dropHighlight.rectTransform.sizeDelta = new Vector2(CellW, CellH);
            _dropHighlight.rectTransform.anchoredPosition = CellCenter(cell);
            _dropHighlight.gameObject.SetActive(true);
        }

        /// <inheritdoc/>
        public void OnEndDrag(PointerEventData eventData)
        {
            if (_ghost != null) _ghost.gameObject.SetActive(false);
            if (_dropHighlight != null) _dropHighlight.gameObject.SetActive(false);
            if (_dragAnchor < 0) return;

            var anchor = _dragAnchor;
            _dragAnchor = -1;

            var mouse = Game.Input != null ? Game.Input.MousePosition : Vector3.zero;
            var screen = new Vector2(mouse.x, mouse.y);

            // ★ U4：落点判定收进**纯函数** `PlanDrop`（离线宿主可逐行断言"按下→移动→松手"
            //   这一序列的判定结果；见 `tools/probes/hosts/uicheck`）。
            var plan = PlanDrop(_data, anchor, CellAt(screen), EquipAt(screen), InsidePanel(screen));

            switch (plan.kind)
            {
                case DropKind.Equip:
                    UiLog.Info($"拖放：锚点格 {anchor} → 装备槽 {EquipSlots[plan.value].slot} ⇒ "
                               + $"请求装备（`{Events.EquipToggleRequest}`）");
                    Game.Event.Emit(Events.EquipToggleRequest, anchor);
                    return;

                case DropKind.Move:
                    EmitMoveInInventoryRequest(anchor, plan.value);
                    return;

                case DropKind.DropToGround:
                    EmitItemDropRequest(anchor);
                    return;

                case DropKind.Ignore:
                    UiLog.Info(plan.value >= 0
                        ? $"拖放：锚点格 {anchor} → 格 {plan.value}（落点锚点同源）⇒ 原地/无效 ⇒ 不请求移动"
                        : $"拖放：锚点格 {anchor} → 面板内空白处 ⇒ 忽略");
                    return;

                default:
                    UiLog.Warn($"拖放：未处理的落点判定 {plan.kind}（锚点格 {anchor}）⇒ 不产生任何请求");
                    return;
            }
        }

        /// <summary>
        /// **落点判定**（★ U4）：纯函数，判"按下 → 移动 → 松手"里的**落点**该做什么。
        /// 判据口径（与原版一致）：装备槽 → 装备；背包格 → 背包内移动/交换（被占用则与该物品的锚点格互换）；
        /// 面板外 → 丢地面；面板内空白 → 忽略。
        /// <para>抽成静态纯函数的理由：拖拽的手感只能进 Play 看，但"落点判对没有"必须**离线可断言**
        /// （宿主喂 `(data, fromAnchor, cell, equip, insidePanel)` 逐行核对，见 `uicheck`）。</para>
        /// </summary>
        /// <param name="data">背包快照（`null` = 还没有数据 ⇒ 只可能判出 DropToGround / Ignore）。</param>
        /// <param name="fromAnchor">拖起的物品锚点格。</param>
        /// <param name="cell">松手处的背包格下标（不在任何格上 = -1）。</param>
        /// <param name="equip">松手处的装备槽下标（不在任何槽上 = -1）。</param>
        /// <param name="insidePanel">松手处是否还在面板矩形内。</param>
        internal static (DropKind kind, int value) PlanDrop(
            InventoryChangedArgs data, int fromAnchor, int cell, int equip, bool insidePanel)
        {
            if (equip >= 0 && equip < EquipSlots.Length) return (DropKind.Equip, equip);

            if (cell >= 0)
            {
                var to = DropAnchorOf(data, cell);
                if (to < 0 || to == fromAnchor) return (DropKind.Ignore, cell);
                return (DropKind.Move, to);
            }

            if (!insidePanel) return (DropKind.DropToGround, fromAnchor);

            return (DropKind.Ignore, -1);
        }

        /// <summary>松手处是否还在本面板矩形内（拖到面板外 = 原版的"丢到地面"）。</summary>
        private bool InsidePanel(Vector2 screen)
            => RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, screen, null);

        /// <summary>悬停显示 tooltip（用矩形命中测试，不依赖子节点事件冒泡）。</summary>
        private void UpdateHover()
        {
            if (_tooltip == null) return;
            if (Game.Input == null || !Game.Input.Available) return;

            var mouse = Game.Input.MousePosition;
            var screen = new Vector2(mouse.x, mouse.y);
            if (!RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, screen, null))
            {
                _tooltip.Hide();
                return;
            }

            for (var i = 0; i < CellCount; i++)
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(_cells[i].rectTransform, screen, null)) continue;

                var anchor = AnchorOf(i);
                var item = anchor >= 0 && _data != null && anchor < _data.inventory.Count
                    ? _data.inventory[anchor]?.item
                    : null;
                if (item != null) _tooltip.Show(item);
                else _tooltip.Hide();
                return;
            }

            for (var i = 0; i < EquipSlots.Length; i++)
            {
                if (_equipRects[i] == null) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(_equipRects[i], screen, null)) continue;

                var item = FindEquipped(_data?.equip, EquipSlots[i].slot, EquipSlots[i].slotIndex);
                if (item != null) _tooltip.Show(item);
                else _tooltip.Hide();
                return;
            }

            _tooltip.Hide();
        }

        /// <summary>
        /// 屏幕点命中的装备槽下标（未命中 = -1）。
        /// <para>★ U4：与 <see cref="CellAt"/> 同一套屏幕点矩形命中 —— 取代原先
        /// 「比 `eventData.pointerPress` 是不是某个格节点」的做法（那个做法恒不命中，见
        /// <see cref="OnPointerClick"/> 的根因注释）。</para>
        /// </summary>
        private int EquipAt(Vector2 screen)
        {
            for (var i = 0; i < _equipRects.Length; i++)
                if (_equipRects[i] != null
                    && RectTransformUtility.RectangleContainsScreenPoint(_equipRects[i], screen, null))
                    return i;
            return -1;
        }

        /// <summary>屏幕点命中的背包格下标（拖放落点判定用；未命中 = -1）。</summary>
        private int CellAt(Vector2 screen)
        {
            for (var i = 0; i < CellCount; i++)
                if (_cells[i] != null
                    && RectTransformUtility.RectangleContainsScreenPoint(_cells[i].rectTransform, screen, null))
                    return i;
            return -1;
        }

        /// <summary>取某格所属物品的锚点格（空/仅被占用 ⇒ -1）。</summary>
        private int AnchorOf(int cellIndex)
        {
            if (_data?.inventory == null || cellIndex >= _data.inventory.Count) return -1;
            var slot = _data.inventory[cellIndex];
            if (slot == null) return -1;
            if (slot.item != null) return slot.index;
            return slot.anchorIndex;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 事件
        // ═════════════════════════════════════════════════════════════════════
        private void Subscribe()
        {
            if (_subscribed || Game.Event == null) return;
            _subscribed = true;
            Game.Event.On<InventoryChangedArgs>(Events.InventoryChanged, OnInventoryChanged);
            Game.Event.On<InventoryChangedArgs>(Events.EquipChanged, OnInventoryChanged);
            Game.Event.On(Events.InventoryFull, OnInventoryFull);
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Game.Event == null) return;
            _subscribed = false;
            Game.Event.Off<InventoryChangedArgs>(Events.InventoryChanged, OnInventoryChanged);
            Game.Event.Off<InventoryChangedArgs>(Events.EquipChanged, OnInventoryChanged);
            Game.Event.Off(Events.InventoryFull, OnInventoryFull);
        }

        private void OnInventoryChanged(InventoryChangedArgs args)
        {
            if (args == null)
            {
                UiLog.Warn("收到背包快照事件但参数为 null ⇒ 忽略");
                return;
            }
            _data = args;
            Rebuild(_data);
        }

        private void OnInventoryFull()
        {
            UiLog.Info("收到「背包已满」事件 ⇒ 提示玩家（物品仍在原地）");
            Game.UI.Toast("背包已满");
        }
    }
}
