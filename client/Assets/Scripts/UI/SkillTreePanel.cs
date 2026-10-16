// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/SkillTreePanel.cs（「技能树面板 1:1 重做」轮）
//
// 技能树面板：**用原版底图**（`SPELLS/skltree_{a,b,n,s,p}_back.DC6` tile 拼出的 320×432 页），
// 技能图标按**原版底图画出的节点框内径**贴上去，点右侧原版页签切系（一屏一系）。
// 左键学技能 / 右键设为按钮技能，数据仍吃 `Def.SkillTreeArgs`。
//
//   ① `SkillFrame` 自绘外框（原版有页边框） ② `SkillBg` 纯色底（原版有石纹底）
//   ③ `Link*` 自绘连线（原版画好了管线 + 箭头） ④ 自绘标题条（原版右列顶部就是说明窗）
//   ⑤ 格下技能名（原版格下**没有**文字，名字在悬停提示/说明窗里）⑥ 节点自绘边框与色调
//   ⑦ 由 `AvailableSkillsPanel.prefab` 反推的 `SkillPanelW/H` / `SkillNodeX/Y` / `SkillCols` …
//
//   · 底图：`D2/UI/Panel/skltree_{cls}_back_{0..3}.png` —— **原版 PNG 未改一个字节**；
//     页 0 = 共用右列（顶部木框说明窗 + 3 个系页签，5 职业逐像素相同）；
//     页 k（k=1,2,3）= 系 k 的完整屏。
//   · 版面：`Def/SkillTreeLayout.cs`（**生成物**）—— 节点框 / 页签槽 / 说明窗 / 木框，
//     全部由 `tools/d2codec/export_skilltree_layout.py` 从上面那批 PNG 逐像素解析。
//   · 页↔系：底图第 k 页 ↔ 原版 `skilldesc.txt` 的 `SkillPage = k`（15/15 页两条独立特征互证）。
//
// 层序（**实测定的，不是猜的**）：先铺**页 0**、再铺**页 k**。
//   依据：① 页 k 在 x 230..319 上只不透明于「被选中页签」那一槽（其余透明）；
//        ② 页 k 的树区右边框（原版 x 225..229）在**被选中页签**处断开；
//        ③ 页 0 叠在页 k 之上时选中页签是"暗的"，页 k 叠在页 0 之上时是"亮（高亮）的"。
//   ⇒ 只铺这两张**未改动的原版 PNG**，高亮页签 / 断口 / 右列三件事**全部自动成立**。
//
// 技能图标尺寸 = 原版**位图原生 48×48**（`UiLayoutGame.SkillIconCell` = 48×48 原版px → 86.4 画布px），
//   中心 = 节点框（`SkillTreeCell.box`，L 形管线的外接矩形）的中心；`preserveAspect` 在正方形框里
//   是恒等变换（不拉变形、不放大）。
//   尺寸口径 = 「控件矩形 == 原版像素 ×1.8」（出处见 `UiLayoutGame.SkillIconCell`）。
//   未学 / 已学的状态 = **原版灰化帧 + 项目既有压暗色调**（判据单点在 `NodeDullFrameOf` / `NodeTintOf`），
//   不画任何自绘标记。
//
// 零 `using Diablo2.Module`（分层自检 ③；`conventions.md` 硬性）。
// 本文件**不画**任何原版底图里已有的东西（边框 / 连线 / 底 / 标题条）。
// ─────────────────────────────────────────────────────────────────────────────

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
    /// 技能树面板。层：<see cref="UILayer.Normal"/>。
    /// <para>本屏层 = <see cref="UILayer.Normal"/>：`Popup` 层会让引擎插一块**全屏模态遮罩**
    /// （`clover-client-unity-engine/Runtime/Presentation/UI.cs:155-159` 的
    /// `Open&lt;T&gt;` ⇒ `:443-461` `ShowMask()`，`img.raycastTarget = true`，插在 `_layers[Popup]` 首位）
    /// —— 遮罩画在 `Normal`（= HUD）之上、且吃射线 ⇒ **纯鼠标玩家打开技能树后点不到 HUD 的任何入口**；
    /// 本屏出口 = **右下角关闭钮**（见 `BuildCloseButton`，与 `CharacterPanel` / `InventoryPanel` 同一套图形帧）
    /// + T 键 / HUD「技能樹」按钮。原版**没有**全屏模态遮罩（面板是叠在世界上的半透明页，
    /// 鼠标仍能点地面与 HUD）⇒ 本屏取 `Normal`；同层先例 = `UI/NpcDialogPanel.cs`（对话条同为 `Normal`，
    /// 理由同为"引擎的 Popup 语义与原版不符"）。
    /// `Normal` 层不由引擎的 `CloseMutexPanels()`（`UI.cs:431-441`，只关 `Layer == Popup` 的面板）管理
    /// ⇒ 「开另一个面板时已在开的面板关掉」由 HUD 入口处显式补上并留痕
    /// （见 `UI/HudPanel.cs` 的 `CloseScreenFamily`）。</para>
    /// </summary>
    public class SkillTreePanel : UIPanel, IPointerClickHandler, IPointerMoveHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>面板尺寸（= 原版底图一页 320×432 ×1.8 = 576×777.6）。</summary>
        public static readonly Vector2 PanelSize = UiLayoutGame.SkillPanelSize;

        /// <summary>面板位置（画布居中）。</summary>
        public static readonly Vector2 PanelPos = UiLayoutGame.SkillPanelPos;

        /// <summary>
        /// 技能图标层尺寸（= 原版**位图原生 48×48** 原版px → **86.4×86.4** 画布px）。
        /// <para>依据与出处见 `UiLayoutGame.SkillIconCell`；图标中心 = 节点框中心，不随尺寸变。</para>
        /// </summary>
        public static readonly Vector2 IconCellSize = UiLayoutGame.SkillIconCell;

        // ═════════════════════════════════════════════════════════════════════
        // 说明窗字段行的**容量规则**（原版口径，三个量都可复算）
        //
        //   ① 汉字排版步进 = **11 原版px**（原版 font8 `font8.DC6` 13806 帧全 11×11；
        //      `Fonts/font8_chi_map.txt` 的 `CELL 11 11` 与每行 `advance` 列实测 CJK 恒 11）。
        //   ② 每行字数 = 可见宽 78 ÷ 11 = **7 汉字**（可见宽出处 = `UiLayoutGame.SkillInfoBox`：
        //      页 0 木框外沿内缩金饰条 4px）。数字 5 / 斜杠 3 / 减号 3 / 百分号 9（同表 advance 列）。
        //   ③ 行数 = `SkillDesc.txt` 的 `descline1..6` ⇒ 字段行**最多 6 行**；可见高 97 ÷ 行距 11
        //      = **8 行**可见（行距出处 = `D2Text.LineSpacing`：font8 无 `.fontsettings`，
        //      取原版帧高 11，与 libd2 `packages/formats/src/font.zig` L141-146 `lineHeight()` 同口径）。
        //
        //   溢出处理 = 原版口径：**先按串里的 `\n` 硬断，再按框宽贪心折行**
        //   （libd2 `font.zig` L188-216 `breakLine`，工程内实现 = `D2Text.WrapLines`）。
        //   ⚠️ 数据面变化：字段行开头多了**原版说明文本**（`skill_c.desc` =
        //   官方 `SkillDesc` 的 `str long` 串逐字，长句 7~17 字）⇒ **折行成为常态**；
        //   法师 30 技能里 24 个的"说明折行 + 数值行"总行数 > 可见 8 行（最长 14 行）。
        //   ⇒ 取舍：**说明在前、数值在后**（与原版布局同序），超容量时由 `Text.verticalOverflow
        //   = Truncate` **裁尾**（不画到木框外）；`VerifyDescFits` 照旧留痕（不静默）。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>原版 font8 汉字排版步进（原版 px），见本区段 ①。</summary>
        private const int DescChiStep = 11;

        /// <summary>
        /// 字段行数期望上限 = 原版 `SkillDesc.txt` 的 `descline1..6`（见本区段 ③）。
        /// <para>起字段行**开头含原版说明文本** ⇒ 部分技能合法超出（法师最长 14 行）；
        /// 超出的行由 `Text.Truncate` 裁掉（见本区段"溢出处理"）⇒ 本常量只用于 `VerifyDescFits` 留痕。</para>
        /// </summary>
        private const int DescMaxLines = 6;

        // 原版串 **4332**「剩餘技能點：」（说明区首行；出处 `原版资源/d2text/chi_string.txt`）。
        private const string LabelRemainingSkillPoints = "剩餘技能點：";

        /// <summary>说明窗可见区能容下的行数（97 ÷ 11 = 8），见本区段 ③。</summary>
        private static readonly int DescMaxVisibleLines = Mathf.FloorToInt(
            UiLayoutGame.SkillInfoBox.h / D2Text.LineSpacing(D2Text.D2Font.Font8));

        /// <summary>已做过容量核对的技能 id（同一技能只留一次痕）。</summary>
        private readonly HashSet<int> _descChecked = new HashSet<int>();

        private sealed class Node
        {
            /// <summary>透明点击/命中区（尺寸 = 原版节点框 45×50 原版px）。</summary>
            public Image Hit;

            /// <summary>原版技能图标（框内径，`preserveAspect`）。</summary>
            public Image Icon;

            /// <summary>在 `_tree.skills` 里的下标。</summary>
            public int SkillIndex;

            /// <summary>本节点属于哪个系（1..3）。</summary>
            public int Tree;

            /// <summary>最近一次贴上的原版图标路径（避免每帧重复异步加载）。</summary>
            public string IconPath;
        }

        private bool _built;
        private bool _subscribed;
        private SkillTreeArgs _tree;

        /// <summary>当前显示的**系**（1..3 = 原版 `SkillPage`；面板一屏一系）。</summary>
        private int _treeNo = 1;

        private Image _bgTabs;          // 页 0（共用右列：顶部木框说明窗 + 3 个系页签）
        private Image _bgTree;          // 页 k（系 k 的完整屏）
        private string _bgTabsPath;
        private string _bgTreePath;
        private PlayerClass _bgCls;

        /// <summary>关闭钮的悬停提示（`OnClose` 收起，对应参考实现的 `OnDisable`）。</summary>
        private ControlTip _closeTip;

        private Text _points;           // 剩余技能点（说明区可见区内）
        private Text _desc;             // 所点/所悬停技能的 名+等级+说明（同区）

        private readonly List<Node> _nodes = new List<Node>();
        private readonly Dictionary<GameObject, int> _tabByObject = new Dictionary<GameObject, int>();

        /// <summary>说明区当前展示的技能下标（-1 = 还没悬停/点过任何技能）。</summary>
        private int _shownSkill = -1;

        // ── 拖拽──
        /// <summary>拖拽中显示的幽灵图标（跟鼠标；`OnEndDrag` 销毁）。null = 不在拖拽。</summary>
        private Image _dragGhost;
        /// <summary>本次拖拽要绑的技能 id（-1 = 没在拖技能；`OnBeginDrag` 命中节点时才有值）。</summary>
        private int _dragSkillId = -1;

        /// <inheritdoc/>
        /// <remarks>层 = `Normal`（**无遮罩 ⇒ HUD 的「技能樹 T」入口可点 = 同一入口开合**；
        /// 理由/出处见类头注释与 `UI/NpcDialogPanel.cs`）。</remarks>
        public override UILayer Layer => UILayer.Normal;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);

            var tree = UiLog.Require<SkillTreeArgs>(param, nameof(SkillTreePanel));
            if (tree != null) _tree = tree;

            Build();
            Subscribe();
            Rebuild();
        }

        /// <inheritdoc/>
        public override void OnClose()
        {
            Unsubscribe();
            _closeTip?.Hide();
            UiLog.Info("技能树面板已关闭");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 构件（面板一开就建齐；内容是每次 `Rebuild` 刷的）
        // ═════════════════════════════════════════════════════════════════════
        private void Build()
        {
            if (_built) return;
            _built = true;

            // ── 底图**两张原版页**：先页 0（右列：木框说明窗 + 3 个系页签）、再页 k（树）──
            //    层序依据见文件头「层序」。此处**只建两个 Image**，贴图在 `ApplyBackdrop` 里按系换。
            //   面板外仍可点地面走（原版语义）。若不吃射线，点面板内部空白会被反投影成"点地面"⇒ 角色走动。
            //   两张图**互斥显示**（`ApplyBackdrop` 按系切帧），故两张都要吃（哪张在台上就由哪张吃）。
            _bgTabs = UiArt.Panel(transform, "TreeBackPage0", PanelSize, PanelPos, UiArt.PanelBg, true);
            _bgTree = UiArt.Panel(transform, "TreeBackPageK", PanelSize, PanelPos, UiArt.PanelBg, true);

            BuildSkillInfoText();
            BuildTabHit();
            BuildCloseButton();

            // 常驻品牌署名 `by clover-engine`（居底居中，几何/字号/颜色见 `UiLayoutFlow.Brand`）。
            //   画在面板下方空白带（面板底边 −388.8，署名行 −462）；**最后建** ⇒ 不被压住。
            UiLayoutFlow.Brand.Attach(transform);
        }

        /// <summary>
        /// 关闭钮（面板右下角）：命中区 = <see cref="UiLayoutGame.SkillClosePos"/>（口径与依据写在
        /// 那个常量上），图形 = 原版方钮的「关闭 / 取消」帧（`PANEL/buysellbtn.DC6` 帧 10 常态 / 11 按下，
        /// 见 `Core/ResPaths.cs` 的 `BuySellButtonFrameClose`），底板**透明**（露出底图的大理石；
        /// 与背包关闭钮同一处置）。
        /// <para>点它走 `Events.PanelToggleRequest`（与按 T / HUD 小面板的「技能樹」同一入口）。</para>
        /// </summary>
        private void BuildCloseButton()
        {
            var close = UiArt.Panel(transform, "CloseButton", UiLayoutGame.CharCloseSize,
                PanelPos + UiLayoutGame.SkillClosePos, new Color(1f, 1f, 1f, 0f), true);
            close.preserveAspect = true;

            var button = close.gameObject.AddComponent<Button>();
            button.targetGraphic = close;
            UiArt.ApplyCloseButtonArt(close, button);

            _closeTip = ControlTip.Create(transform, close.rectTransform, WaypointPanel.CloseText);
            var hover = close.gameObject.AddComponent<HoverTarget>();
            if (_closeTip != null)
            {
                hover.OnEnter = _closeTip.Show;
                hover.OnExit = _closeTip.Hide;
            }

            button.onClick.AddListener(() =>
            {
                UiLog.Info("点技能树关闭按钮 ⇒ 走 `Events.PanelToggleRequest` 关闭（与按 T 同一条路径）");
                Game.Event.Emit(Events.PanelToggleRequest, nameof(SkillTreePanel));
            });
        }

        /// <summary>
        /// 说明区文字（原版 px → 画布，落在**页 0 顶部木框的可见区**内）。
        /// <para>可见区 = `UiLayoutGame.SkillInfoBox`（木框外沿内缩金饰条 4px ⇒ 78×97 原版px）。
        /// 第一行 = 剩余技能点；其余 = 所点/所悬停技能的「名 / 等级 / 说明」。</para>
        /// <para>字号 = <see cref="UiLayoutGame.FontPx8"/>（原版 **font8** = 11×1.8 = 19.8 画布px）——
        /// 全 UI 字号的唯一出处。为什么是这一档：原版说明窗自己的字段标签
        /// `chi_string.txt:4254`「目前技能等級：」是 **7 个字**（font8 下 7×11 = **77 art**，
        /// 正好放进 78 art 宽的可见区；font16 下 7×13 = 91 art 放不下）⇒ 原版这个框走 font8。</para>
        /// <para>换行策略：第一行（剩余技能点）`Overflow`（单行、居中，不折行）；
        /// 字段行列表 `Wrap` —— 与原版口径一致（数据里的 `\n` 硬断 + 按框宽贪心折行，
        /// 即 libd2 `font.zig` L188-216 `breakLine`）。字段行由 Skill 模块按 78 原版px 切好
        /// （标签一行、值一行，见 `SkillDef.descLines`）⇒ 合规数据下**不会触发折行**；
        /// 真超宽时折到框内，而不是压出框沿（超容量另由 `VerifyDescFits` 留痕）。</para>
        /// <para>两行都不带任何自绘框 —— 框是原版木框自己画的。</para>
        /// </summary>
        private void BuildSkillInfoText()
        {
            var box = UiLayoutGame.SkillInfoBox;
            var center = UiLayoutGame.SkillArtToPanel(box.x + box.w * 0.5f, box.y + box.h * 0.5f);
            var size = UiLayoutGame.SkillArtSize(box.w, box.h);
            var lineH = UiLayoutGame.SkillPointsLineH;
            var fontPx = (int)UiLayoutGame.FontPx8;

            // 剩余技能点：说明区顶行（居中）+ 单行不折行
            //   （`Text.horizontalOverflow` 由 `D2TextMirror` 同步给 `D2Label`；
            //    `Overflow` 下 `D2Label` 不算 `availPx` ⇒ 永不折行）。
            //   字模家族显式给 font8（不是只把 font16 的字形缩小）：见 `UiArt.Label` 的 `font` 参数。
            _points = UiArt.Label(transform, "SkillPoints", string.Empty, fontPx, TextAnchor.MiddleCenter,
                UiArt.TitleColor, new Vector2(size.x, lineH),
                new Vector2(center.x, center.y + size.y * 0.5f - lineH * 0.5f),
                false, D2Text.D2Font.Font8);
            _points.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 字段行列表：说明区其余部分（左上）。按框宽 `Wrap`（原版口径，见本方法注释）：
            //   行的内容由 Skill 模块按 78 art 的可用宽切好（字段名一行、值一行）
            //   ⇒ 合规数据下 `availPx`(=78 art) 内放得下、不发生折行。
            var restH = size.y - lineH;
            _desc = UiArt.Label(transform, "SkillDesc", string.Empty, fontPx, TextAnchor.UpperLeft,
                UiArt.TextColor, new Vector2(size.x, restH),
                new Vector2(center.x, center.y + size.y * 0.5f - lineH - restH * 0.5f),
                false, D2Text.D2Font.Font8);
            _desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            // 竖向溢出 = 裁尾：字段行现在含**原版说明文本**（长句折行后最多 14 行 > 可见 8 行，
            //   见本类「说明窗字段行的容量规则」区段）——`UiArt.Label` 默认 `Overflow` 会把超出
            //   的行画到**木框外**（盖到页签区）⇒ 这里显式裁切，超容量由 `VerifyDescFits` 留痕。
            _desc.verticalOverflow = VerticalWrapMode.Truncate;
        }

        /// <summary>
        /// 3 个**系页签**的点击区（位置 = `Def.SkillTreeLayout.TabSlots[系-1]`，原版 px，实测）。
        /// <para>点击区是**透明**的（原版页签边框是底图画好的，不许重画）⇒ 只盖一层隐形按钮。</para>
        /// <para>**系名 = 运行时文字**：底图页 0 的 3 个
        /// 页签槽里**没有烘字** —— 判据 = `skltree_{a,b,n,p,s}_back_0.png` 五职业 **md5 完全相同**，
        /// 而三个系名逐职业不同 ⇒ 页 0 不可能烘入职业相关文字，系名只能运行时画（与商店
        /// `buyselltabs` 同一形态，见 `ShopPanel.BuildTabs` 的 remarks）。</para>
        /// <para>**系 ↔ 页签槽 ↔ 串 id 的映射（本轮查实，三条独立证据互锁）**：
        /// ① **原版像素**：`skltree_{s}_back_{1,2,3}.png` 树区右边框（x 225..229）的暗缺口
        ///   （= 该页被选中页签的占位，宽 96..100px 与页签槽 100px 对齐）逐页落在：
        ///   页1 → y 329..424（下槽）、页2 → y 221..317（中槽）、页3 → y 113..208（上槽）；
        /// ② **串表排列**：`chi_string.txt` 法师三系名 4249 冰凍 / 4250 閃電 / 4251 火焰
        ///   **正按自上而下**排（与 ① 同向；亚玛逊 4232/4234/4236、圣骑士 4238/4240/4241 亦同向）；
        /// ③ **页 ↔ 系**：底图第 k 页节点框 (列,行) 集合 == `skilldesc.txt` 该 SkillPage 的集合
        ///   （15/15 一致，生成器 abort 级校验），且 `skill_c.tree == SkillPage` 逐技能断言过。
        /// ⇒ **自上而下 = 系3 冰凍 / 系2 閃電 / 系1 火焰**（`TabSlots[系-1]` 即按此排）。
        /// 本项目**单职业 = 法师**⇒
        /// 只需要法师这三条；法师三系名都是**单 id 成串**（无「標槍+和長矛」式两段拼接）⇒
        /// 不存在分行口径问题。其余四职业的两段式名字（含死灵那组与页序反向）不在本项目范围内，
        /// 要开放时再按同一方法（像素缺口 + 串表同向性）逐职业定，**不许照抄方向**。</para>
        /// </summary>
        private void BuildTabHit()
        {
            for (var t = 1; t <= SkillTreeLayout.TreeCount; t++)
            {
                var slot = SkillTreeLayout.TabSlots[t - 1];
                var center = UiLayoutGame.SkillArtToPanel(slot.x + slot.w * 0.5f, slot.y + slot.h * 0.5f);
                var size = UiLayoutGame.SkillArtSize(slot.w, slot.h);

                var img = UiArt.Panel(transform, "TabHit" + t, size, center, new Color(0f, 0f, 0f, 0f), true);
                var no = t;
                var btn = img.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => OnTabClicked(no));
                _tabByObject[img.gameObject] = no;

                // 系名（运行时文字，见本方法 <remarks>）—— 画在**页签自己的矩形**里居中
                //   （rect 与命中区同源 ⇒ 文字不会跑出页签；层序在两张底图之上：Build 里
                //   `_bgTabs`/`_bgTree` 先建，本方法后建）。
                var label = D2Label.Create(transform, "TabName" + no, TreeTabNames[no - 1],
                    D2Text.D2Font.Font24, TextAnchor.MiddleCenter, UiArt.TitleColor, size, center,
                    (int)UiLayoutGame.FontPx24);
                if (label == null)
                {
                    UiLog.Warn($"技能树系页签 [{no}] 的系名没建出来（`D2Label.Create` 回 null）"
                        + " ⇒ 该页签是空页签（见 SkillTreePanel.BuildTabHit）");
                }
            }
        }

        /// <summary>
        /// 3 个系页签的系名（下标 = 系 − 1；**原版串表 id 逐字**，简→繁由位图字体层映射）：
        /// 系1 = 火焰（chi_string 4251「火焰」）、系2 = 闪电（4250「閃電」）、系3 = 冰冻（4249「冰凍」）。
        /// <para>映射依据（三条证据互锁）见 <see cref="BuildTabHit"/> 的 remarks：自上而下的页签槽
        /// 依次是 系3/系2/系1，而串表 4249→4251 正按自上而下排。</para>
        /// </summary>
        private static readonly string[] TreeTabNames = { "火焰", "闪电", "冰冻" };

        /// <summary>节点：透明命中区（尺寸 = 原版节点框）+ 原版技能图标（框内径）。</summary>
        private Node CreateNode(int skillIndex)
        {
            var hit = UiArt.Panel(transform, "Node" + skillIndex,
                UiLayoutGame.SkillArtSize(SkillTreeLayout.CellW, SkillTreeLayout.CellH),
                Vector2.zero, new Color(0f, 0f, 0f, 0f), true);

            // 图标层：**底色先铺暗色占位**（⛔ 不是 `Color.white`：贴图在途/失败时露出的就是一块
            //   白方块，与"素材缺失"长得一样）；到位后由 `ApplyNodeIcon` 写回正常色调
            //   （未学 = `UiArt.ArtDim`、已学 = `UiArt.ArtFullBright`）。
            //   原生尺寸 48×48 ×1.8 = 86.4（preserveAspect 在正方形框里是恒等变换 ⇒ 原版像素 1:1
            //   落位，既不放大也不拉变形）。
            var icon = UiArt.Panel(hit.transform, "Icon", IconCellSize, Vector2.zero,
                D2Icon.MissingIconColor, false);
            icon.preserveAspect = true;

            var node = new Node { Hit = hit, Icon = icon, SkillIndex = skillIndex };
            _nodes.Add(node);
            return node;
        }

        private Node FindNode(int skillIndex)
        {
            for (var i = 0; i < _nodes.Count; i++)
                if (_nodes[i].SkillIndex == skillIndex)
                    return _nodes[i];
            return null;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 刷新
        // ═════════════════════════════════════════════════════════════════════
        private void Rebuild()
        {
            if (!_built)
            {
                UiLog.Warn("技能树面板尚未构建就收到刷新 ⇒ 忽略");
                return;
            }

            if (_tree == null)
            {
                UiLog.WarnOnce("skill.no.tree",
                    "技能树面板未收到 `SkillTreeArgs` ⇒ 空树显示；"
                    + "请 Skill 模块在进关（ResetForClass）后 emit 一次 `Events.SkillTreeChanged`");
                _points.text = LabelRemainingSkillPoints + "—";
                _desc.text = string.Empty;
                for (var i = 0; i < _nodes.Count; i++) _nodes[i].Hit.gameObject.SetActive(false);
                return;
            }

            // 系号越界（配表被改过）⇒ 夹回 1..3 并点名，不静默
            if (_treeNo < 1 || _treeNo > SkillTreeLayout.TreeCount)
            {
                UiLog.Warn($"技能树当前系号 {_treeNo} 越界（合法 1..{SkillTreeLayout.TreeCount}）⇒ 归到第 1 系");
                _treeNo = 1;
            }

            ApplyBackdrop(_tree.cls);
            _points.text = LabelRemainingSkillPoints + _tree.skillPoints;

            var skills = _tree.skills ?? new List<SkillDef>();
            if (_tree.learnedLevels != null && _tree.learnedLevels.Count != skills.Count)
            {
                UiLog.Warn($"技能树数据不一致：skills={skills.Count}，learnedLevels={_tree.learnedLevels.Count}"
                           + " ⇒ 越界项按 0 级处理");
            }
            if (_tree.learnable != null && _tree.learnable.Count != skills.Count)
            {
                UiLog.Warn($"技能树数据不一致：skills={skills.Count}，learnable={_tree.learnable.Count}"
                           + " ⇒ 越界项按不可学处理");
            }

            var placed = 0;
            var hidden = 0;
            for (var i = 0; i < skills.Count; i++)
            {
                var def = skills[i];
                if (def == null)
                {
                    UiLog.Warn($"技能树第 {i} 项为 null ⇒ 跳过");
                    continue;
                }

                var node = FindNode(i) ?? CreateNode(i);

                if (!SkillTreeLayout.TryGet(def.id, out var cell))
                {
                    // 非预期分支：配表里新增/改了技能 id，而版面表是按原版布局生成的 ⇒ 没有它的位置
                    node.Hit.gameObject.SetActive(false);
                    hidden++;
                    UiLog.WarnThrottled("skill.cell.miss." + def.id,
                        $"技能 {def.id}（{def.name}）不在版面表 `Def/SkillTreeLayout` 里 ⇒ 不显示"
                        + "（请重跑 `python tools/d2codec/export_skilltree_layout.py --write`）");
                    continue;
                }

                var learned = _tree.learnedLevels != null && i < _tree.learnedLevels.Count
                    ? _tree.learnedLevels[i] : 0;
                //   `learnable` 缺数据（旧档 / 表没给）时按**可学**处理：宁可比现在亮一档，
                //   也不要把所有节点都涂成"不可学"（那会更暗、更误导）。
                var learnable = _tree.learnable == null || i >= _tree.learnable.Count
                                || _tree.learnable[i];

                //   图标按**该技能自己的已学等级**贴，与"当前显示哪一系"无关：换系只切 `SetActive`
                //   ⇒ 本职业**所有**节点都有图标（"数节点 / 数图标"的读数不会把隐藏节点算成缺图）。
                //   色调三档：已学=亮 / 未学可学=暗 / 未学不可学=更暗（见 `NodeTintOf`）。
                ApplyNodeIcon(node, def, learned, learnable);

                if (cell.tree != _treeNo)
                {
                    node.Hit.gameObject.SetActive(false);
                    continue;
                }

                node.Hit.gameObject.SetActive(true);
                node.Tree = cell.tree;
                node.Hit.rectTransform.anchoredPosition = UiLayoutGame.SkillArtToPanel(
                    cell.box.x + cell.box.w * 0.5f, cell.box.y + cell.box.h * 0.5f);
                placed++;
            }

            // 数据比已建节点少时把多余节点藏起来（否则画面会留着上一次刷新的图标）
            for (var i = skills.Count; i < _nodes.Count; i++)
                _nodes[i].Hit.gameObject.SetActive(false);

            UiLog.Info($"技能树已刷新：系={_treeNo}（原版页 {_treeNo}）本系节点={placed}，"
                       + $"其余系隐藏，版面表未覆盖而隐藏={hidden}，"
                       + $"面板={PanelSize.x:0.#}×{PanelSize.y:0.#}（原版 {SkillTreeLayout.PageW}×"
                       + $"{SkillTreeLayout.PageH} ×1.8），图标={IconCellSize.x:0.#}×{IconCellSize.y:0.#}（框内径）");
        }

        /// <summary>
        /// 铺两张**未改动的原版页**：先页 0（右列），再页 k（当前系）。
        /// <para>职业或系变了才重新取图（`UiArt.SetSprite` 是异步的，同路径重复请求没必要）。</para>
        /// </summary>
        private void ApplyBackdrop(PlayerClass cls)
        {
            var id = (int)cls;                                  // PlayerClass.Amazon=1 …… Barbarian=5
            var treePath = D2Icon.SkillTreeBackPath(id, _treeNo);
            var tabsPath = D2Icon.SkillTreeBackPath(id, UiLayoutGame.SkillTabPage);

            if (_bgCls != cls)
            {
                _bgCls = cls;
                UiLog.Info($"技能树底图：职业={(int)cls}（{cls}）⇒ 页 0（右列）+ 页 {_treeNo}（系 {_treeNo}）");
            }

            if (!string.IsNullOrEmpty(tabsPath) && _bgTabsPath != tabsPath)
            {
                _bgTabsPath = tabsPath;
                UiArt.SetSprite(_bgTabs, tabsPath);
            }
            if (!string.IsNullOrEmpty(treePath) && _bgTreePath != treePath)
            {
                _bgTreePath = treePath;
                UiArt.SetSprite(_bgTree, treePath);
            }
            if (string.IsNullOrEmpty(treePath) || string.IsNullOrEmpty(tabsPath))
            {
                UiLog.WarnOnce("skill.skltree.back.miss." + id,
                    $"技能树底图取不到（职业 {(int)cls}）⇒ 保留占位纯色；"
                    + "请确认 `Table.TableLoader.LoadAll` 已执行（职业字母取自 class_c.skill_class）");
            }
        }

        /// <summary>
        /// **未学（等级 0）⇒ 用原版灰化帧**；已学 ⇒ 用常态帧。纯函数（离线宿主直接断言）。
        /// </summary>
        internal static bool NodeDullFrameOf(int learnedLevel) => learnedLevel <= 0;

        /// <summary>
        /// **未学节点的图标色调 = <see cref="UiArt.ArtDim"/>（项目已有的"压暗一档"）；已学 = <see cref="UiArt.ArtFullBright"/>**。
        /// 纯函数（离线宿主直接断言两个取值）。
        /// <para>灰化帧本身只比常态帧暗约 10%（实测 `amaSkillicon_0` 平均亮度 86.4 / `_1` 77.8）
        /// ⇒ 单靠换帧在人眼下几乎看不出"置灰"，故在帧之上再套这一层既有色调。</para>
        /// <para>**未学且不可学 ⇒ <see cref="UiArt.ArtLocked"/>（再暗一档）**：原版技能树里
        /// "可学未学"与"不可学"是两档可见暗度，审计前本项目只有一档
        /// ⇒ 学不了的技能和能学的长得一样。口径 = 三档可辨（1.0 / 0.62 / 0.38）。</para>
        /// </summary>
        internal static Color NodeTintOf(int learnedLevel, bool learnable)
            => learnedLevel > 0 ? UiArt.ArtFullBright
             : learnable ? UiArt.ArtDim
             : UiArt.ArtLocked;

        /// <summary>
        /// 技能图标 = **原版位图**（`D2/UI/SkillIcon/{cls}Skillicon_{帧}`）+ 未学/已学/不可学的状态色调。
        /// <para>两个判据都由 <see cref="NodeDullFrameOf"/> / <see cref="NodeTintOf"/> 单点给出：
        /// 未学可学 ⇒ 灰化帧 + <see cref="UiArt.ArtDim"/>（暗），已学 ⇒ 常态帧 + 原版亮度（亮），
        /// 未学不可学 ⇒ 灰化帧 + <see cref="UiArt.ArtLocked"/>（更暗）。</para>
        /// </summary>
        private static void ApplyNodeIcon(Node node, SkillDef def, int learned, bool learnable)
        {
            var path = D2Icon.SkillIconPath(def.id, NodeDullFrameOf(learned));

            if (string.IsNullOrEmpty(path))
            {
                // 非预期分支：配表里查不到该技能 / 职业码 ⇒ 无图可贴（`D2Icon` 已点名 Warn）。
                //   ⛔ 不要只把 sprite 置空：底色留白 = 一块白方块（与"素材缺失"无法区分）⇒ 铺暗色占位。
                node.Icon.sprite = null;
                node.Icon.color = D2Icon.MissingIconColor;
                node.IconPath = null;
                return;
            }

            if (node.IconPath == path) return;          // 同一张图，已在（或正在）加载

            var tint = NodeTintOf(learned, learnable);
            node.IconPath = path;
            node.Icon.color = tint;
            UiArt.SetArtTint(node.Icon, tint);
            UiArt.SetSprite(node.Icon, path);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 交互
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 拖拽开始（**只在按住的起点命中技能格时**才算拖拽；页签/空地拖动不进入拖拽态）。
        /// <para>uGUI 的 `IBeginDragHandler` 也实现在**面板根**（与 `IPointerClickHandler` 同节点）——
        /// 这与 `NodeAt` 注释里"事件冒泡"是同一件事：拖拽事件从命中对象冒泡到根，所以这里**必须**
        /// 自己用屏幕点命中测试判断"起点是不是技能格"（不能信 `pointerPress`）。</para>
        /// </summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_tree?.skills == null || _nodes.Count == 0 || eventData == null) return;

            //   命中口径 = **按下起点**（`pressPosition`），⛔ 不是 `eventData.position`（当前点）：
            //   `IBeginDragHandler` 在"移动超过拖拽阈值（`EventSystem.pixelDragThreshold`，默认 10px）"
            //   **之后**才触发（`StandaloneInputModule.ProcessDrag` ⇒ `ShouldStartDrag`）⇒ 快速拖动时
            //   当前点已离开节点（甩一下就是一整帧位移）⇒ `NodeAt(position)` miss 就 `return`、
            //   连日志都不打 —— 实测：技能拖不到技能栏 + 零拖拽日志的根因。
            //   （与 10-12「点击无效」是同族问题：事件到了面板根、但内部命中测试取错了点。）
            var nodeIndex = NodeAt(eventData.pressPosition, eventData.pressEventCamera);
            if (nodeIndex < 0) return;                       // 拖的不是技能格 ⇒ 不开始（页面空白/页签）
            if (_nodes[nodeIndex].SkillIndex >= _tree.skills.Count) return;

            var def = _tree.skills[_nodes[nodeIndex].SkillIndex];
            if (def == null) return;

            _dragSkillId = def.id;
            CreateDragGhost(def);
            UpdateDragGhost(eventData.position);
            UiLog.Info($"开始拖拽技能 {def.id}（{def.name}）⇒ 拖到 HUD 左/右键技能格上松开即可绑定");
        }

        /// <summary>拖拽中：幽灵图标跟鼠标（位置换算口径 = 面板本地坐标，与 `NodeAt` 反向）。</summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (_dragGhost == null || eventData == null) return;
            UpdateDragGhost(eventData.position);
        }

        /// <summary>
        /// 拖拽结束：命中 HUD 左/右键技能格 ⇒ 发 <see cref="Events.SkillSlotBindRequest"/>
        /// （载荷 = `button | (skillId &lt;&lt; 1)`）；落在别处 = 取消（不绑、不报错）。
        /// </summary>
        public void OnEndDrag(PointerEventData eventData)
        {
            var skillId = _dragSkillId;
            DestroyDragGhost();
            _dragSkillId = -1;
            if (skillId < 0 || eventData == null) return;

            var button = SkillSlotHit.SlotAt(eventData.position, eventData.pressEventCamera);
            if (button < 0)
            {
                UiLog.Info($"拖拽技能 #{skillId} 落在非技能格 ⇒ 取消（拖到 HUD 左下角 L / R 两格上即可绑定）");
                return;
            }

            UiLog.Info($"拖拽技能 #{skillId} 落到{(button == 0 ? "左键" : "右键")}技能格 ⇒ 发 "
                + $"{Events.SkillSlotBindRequest}（button={button}）");
            Game.Event.Emit(Events.SkillSlotBindRequest, button | (skillId << 1));
        }

        /// <summary>创建拖拽幽灵（图标 = 该技能的**常态帧**原版图标；不吃射线，免得挡 HUD 的悬停）。</summary>
        private void CreateDragGhost(SkillDef def)
        {
            DestroyDragGhost();

            var path = D2Icon.SkillIconPath(def.id, false);
            _dragGhost = UiArt.Panel(transform, "DragGhost", IconCellSize, Vector2.zero,
                new Color(1f, 1f, 1f, 0.8f), false);
            _dragGhost.preserveAspect = true;
            if (!string.IsNullOrEmpty(path)) UiArt.SetSprite(_dragGhost, path);
            _dragGhost.transform.SetAsLastSibling();         // 画在面板最上层（拖到哪都看得见）
        }

        private void DestroyDragGhost()
        {
            if (_dragGhost == null) return;
            Object.Destroy(_dragGhost.gameObject);
            _dragGhost = null;
        }

        /// <summary>幽灵跟鼠标：屏幕点 → 面板根矩形内本地坐标（anchor/pivot 居中 ⇒ 直接赋 anchoredPosition）。</summary>
        private void UpdateDragGhost(Vector2 screen)
        {
            if (_dragGhost == null) return;
            var rt = transform as RectTransform;
            if (rt == null) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, null, out var local))
            {
                _dragGhost.rectTransform.anchoredPosition = local;
            }
        }

        /// <summary>点系页签 ⇒ 切换当前系（一屏一系；原版底图第 k 页就是系 k 的完整屏）。</summary>
        private void OnTabClicked(int treeNo)
        {
            // 点击音（原版 UI 点击音 `cursor\button.wav`，键 = `SfxRegistry.UiClick`）——
            // 本屏页签**不发任何业务事件**（切系是本面板自己的状态）⇒ 不在这里发就没有声。
            // 同一条写法见 `UI/ShopPanel.BuildTabs`（商店页签）与 `UI/CharacterPanel` 的加点箭头。
            Game.Event.Emit(Events.UiClick);

            if (treeNo == _treeNo)
            {
                UiLog.Info($"点技能树「系 {treeNo}」页签 ⇒ 已是当前系，无操作（原版也不重画）");
                return;
            }

            UiLog.Info($"点技能树「系 {treeNo}」页签 ⇒ 切到系 {treeNo}（原版底图页 {treeNo}）");
            _treeNo = treeNo;
            _shownSkill = -1;
            _desc.text = string.Empty;
            Rebuild();
        }

        /// <inheritdoc/>
        public void OnPointerMove(PointerEventData eventData)
        {
            if (_tree?.skills == null || _nodes.Count == 0) return;
            if (eventData == null) return;

            var nodeIndex = NodeAt(eventData.position, eventData.pressEventCamera);
            if (nodeIndex < 0) return;
            ShowSkill(_nodes[nodeIndex].SkillIndex);             // 原版：鼠标扫过技能 ⇒ 右列说明窗换内容
        }

        /// <summary>
        /// **屏幕点 → 节点下标**（命中测试的唯一真源；<see cref="OnPointerMove"/> 与
        /// <see cref="OnPointerClick"/> 共用，离线宿主可逐节点断言）。
        /// <para>
        /// **为什么不用 <c>eventData.pointerPress</c>**：
        /// 本面板把 <see cref="IPointerClickHandler"/> / <see cref="IPointerMoveHandler"/> 实现在
        /// **面板根**（`SkillTreePanel` 组件挂在根节点），而技能格只是**子 Image**（`Node{N}`）⇒
        /// uGUI 的指针事件从命中对象**冒泡**到第一个实现了该接口的祖先 ⇒ <c>pointerPress</c>
        /// **恒 = 面板根**，拿面板根去查节点字典必然查不到 ⇒ 点击永远静默返回。
        /// 实测日志也吻合：鼠标划过有反应（`OnPointerMove` 的说明窗逐个技能刷），
        /// 而点击**一条日志都不打**。
        /// 同款修法见 `UI/InventoryPanel.OnPointerClick`（那里有同一段故障的完整注释）。
        /// </para>
        /// </summary>
        /// <param name="screen">屏幕点（`PointerEventData.position`）。</param>
        /// <param name="cam">事件相机（Screen Space - Overlay 传 null）。</param>
        /// <returns>命中的节点下标；没命中任何可见节点返回 -1。</returns>
        private int NodeAt(Vector2 screen, Camera cam)
        {
            if (_nodes.Count == 0) return -1;

            // 逆序遍历：后建的节点画在上层 ⇒ 命中优先（与渲染层序一致）
            for (var i = _nodes.Count - 1; i >= 0; i--)
            {
                var node = _nodes[i];
                if (node.Hit == null || !node.Hit.gameObject.activeInHierarchy) continue;
                if (node.SkillIndex < 0 || node.SkillIndex >= _tree.skills.Count) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(node.Hit.rectTransform, screen, cam))
                    return i;
            }
            return -1;
        }

        /// <inheritdoc/>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null) return;
            if (_tree?.skills == null) return;

            // 与 OnPointerMove 同一套屏幕点命中（**不能**用 pointerPress —— 见 NodeAt 的注释）
            var nodeIndex = NodeAt(eventData.position, eventData.pressEventCamera);
            if (nodeIndex < 0)
                return;                                  // 点的是页签/空地 ⇒ 交给其它通道

            var node = _nodes[nodeIndex];
            if (node.SkillIndex >= _tree.skills.Count) return;

            var def = _tree.skills[node.SkillIndex];
            if (def == null) return;

            ShowSkill(node.SkillIndex);

            // 点击音（键 = `SfxRegistry.UiClick`，原版 `cursor\button.wav`）：本屏技能格**发的是业务事件**
            // （学技能 / 设按钮技能），音频模块只订阅 `Events.UiClick` 这一条"按钮点击"通道 ⇒ 在这里发。
            Game.Event.Emit(Events.UiClick);

            if (eventData.button == PointerEventData.InputButton.Right)
            {
                UiLog.Info($"右键技能 {def.id}（{def.name}）⇒ 设为按钮技能（`{Events.SkillSelected}`）");
                Game.Event.Emit(Events.SkillSelected, def.id);
                return;
            }

            var learned = _tree.learnedLevels != null && node.SkillIndex < _tree.learnedLevels.Count
                ? _tree.learnedLevels[node.SkillIndex]
                : 0;
            var learnable = _tree.learnable != null && node.SkillIndex < _tree.learnable.Count
                            && _tree.learnable[node.SkillIndex];

            if (!learnable)
            {
                UiLog.Info($"学习技能 {def.id}（{def.name}）被拒：不满足条件（需等级 {def.reqLevel}，"
                           + $"前置 {def.reqSkill}）或技能点不足（当前 {_tree.skillPoints}）");
                Game.UI.Toast("条件不满足：等级/前置技能/技能点不足");
                return;
            }

            if (learned >= def.maxLevel)
            {
                UiLog.Info($"技能 {def.id}（{def.name}）已达上限 {def.maxLevel} 级");
                Game.UI.Toast("该技能已满级");
                return;
            }

            UiLog.Info($"请求学习技能 {def.id}（{def.name}）第 {learned + 1} 级（`{Events.SkillLearnRequest}`）");
            Game.Event.Emit(Events.SkillLearnRequest, def.id);
        }

        /// <summary>把某技能的名 + 字段行写进说明窗（同一技能不重复刷、也不刷屏）。</summary>
        private void ShowSkill(int skillIndex)
        {
            if (!_built || _tree?.skills == null) return;
            if (skillIndex < 0 || skillIndex >= _tree.skills.Count) return;

            var def = _tree.skills[skillIndex];
            if (def == null) return;
            if (_shownSkill == skillIndex) return;      // 同一个技能：不重复写
            _shownSkill = skillIndex;

            _desc.text = DescTextOf(def);
            VerifyDescFits(def);
        }

        /// <summary>
        /// 一行的原生宽度（原版 px）：汉字按 <see cref="DescChiStep"/>（font8 帧宽 11）、
        /// 拉丁按 <see cref="D2Text.Advance"/>（同 `font8_chi_map.txt` 的 `advance` 列）。
        /// <para>算法与 `D2Text.MeasureNative` 同表同口径，但**不依赖 chi 图集是否已加载**（可离线复算）。</para>
        /// </summary>
        private static int LineWidthArt(string line)
        {
            if (string.IsNullOrEmpty(line)) return 0;

            var w = 0;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                w += D2Text.IsLatinOnly(c.ToString()) ? D2Text.Advance(D2Text.D2Font.Font8, c) : DescChiStep;
            }
            return w;
        }

        /// <summary>
        /// 说明窗字段行的容量核对（每技能一次）：行数 &gt; <see cref="DescMaxLines"/>
        /// 或某行宽 &gt; 可见宽 ⇒ 一条 Warn，写明超了多少原版px。
        /// <para>超容量**不改渲染**（原版不缩字、不砍字；折行口径见本类「说明窗字段行的容量规则」区段）——
        /// 这里只保证它**不静默**发生。</para>
        /// </summary>
        private void VerifyDescFits(SkillDef def)
        {
            var lines = def?.descLines;
            if (lines == null || lines.Count == 0) return;
            if (!_descChecked.Add(def.id)) return;

            var avail = UiLayoutGame.SkillInfoBox.w;
            var widest = 0;
            var widestLine = string.Empty;
            for (var i = 0; i < lines.Count; i++)
            {
                var w = LineWidthArt(lines[i]);
                if (w > widest)
                {
                    widest = w;
                    widestLine = lines[i];
                }
            }

            var overLines = lines.Count > DescMaxLines;
            var overWidth = widest > avail;
            if (overLines || overWidth)
            {
                UiLog.Warn($"技能 {def.id}（{def.name}）说明窗字段行超容量："
                           + $"行数 {lines.Count}/{DescMaxLines}，最宽行「{widestLine}」{widest}/{avail:0.#} 原版px"
                           + $"（可见 {DescMaxVisibleLines} 行；超出的行由 Text.Truncate 裁掉、不画到框外）");
                return;
            }

            UiLog.Info($"技能 {def.id}（{def.name}）说明窗字段行：{lines.Count} 行、最宽 {widest}/{avail:0.#} 原版px"
                       + $"（上限 {DescMaxLines} 行 / 每行 {avail / DescChiStep:0} 汉字，可见 {DescMaxVisibleLines} 行）");
        }

        /// <summary>
        /// 说明窗文字 = 技能名（`skill_c.name`）一行 + `SkillDef.descLines` 的原版字段行。
        /// <para>字段行**由 Skill 模块算好**（值走既有生产函数）；本面板只拼行、画字，
        /// 不解析、不补默认值（分层：UI 不 `using Diablo2.Module`）。</para>
        /// </summary>
        private static string DescTextOf(SkillDef def)
        {
            if (def == null) return string.Empty;

            var lines = def.descLines;
            var name = def.name ?? string.Empty;
            if (lines == null || lines.Count == 0) return name;

            var all = new string[lines.Count + 1];
            all[0] = name;
            for (var i = 0; i < lines.Count; i++) all[i + 1] = lines[i];
            return string.Join("\n", all);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 事件
        // ═════════════════════════════════════════════════════════════════════
        private void Subscribe()
        {
            if (_subscribed || Game.Event == null) return;
            _subscribed = true;
            Game.Event.On<SkillTreeArgs>(Events.SkillTreeChanged, OnTreeChanged);
            Game.Event.On<int>(Events.SkillLearned, OnSkillLearned);
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Game.Event == null) return;
            _subscribed = false;
            Game.Event.Off<SkillTreeArgs>(Events.SkillTreeChanged, OnTreeChanged);
            Game.Event.Off<int>(Events.SkillLearned, OnSkillLearned);
        }

        private void OnTreeChanged(SkillTreeArgs args)
        {
            if (args == null)
            {
                UiLog.Warn("收到技能树事件但参数为 null ⇒ 忽略");
                return;
            }
            _tree = args;
            Rebuild();
        }

        private void OnSkillLearned(int skillId)
        {
            UiLog.Info($"技能已学会：{skillId}（面板刷新依赖 Skill 模块随后的 `{Events.SkillTreeChanged}`）");
        }
    }
}
