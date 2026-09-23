// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/SkillTreePanel.cs（「技能树面板 1:1 重做」轮）
//
// 技能树面板：**用原版底图**（`SPELLS/skltree_{a,b,n,s,p}_back.DC6` tile 拼出的 320×432 页），
// 技能图标按**原版底图画出的节点框内径**贴上去，点右侧原版页签切系（一屏一系）。
// 左键学技能 / 右键设为按钮技能，数据仍吃 `Def.SkillTreeArgs`。
//
// ★★ 本轮（"删掉全部自绘"）改了什么 —— 上一版的以下东西**全部删除**（原版底图里都有）：
//   ① `SkillFrame` 自绘外框（原版有页边框） ② `SkillBg` 纯色底（原版有石纹底）
//   ③ `Link*` 自绘连线（原版画好了管线 + 箭头） ④ 自绘标题条（原版右列顶部就是说明窗）
//   ⑤ 格下技能名（原版格下**没有**文字，名字在悬停提示/说明窗里）⑥ 节点自绘边框与色调
//   ⑦ 由 `AvailableSkillsPanel.prefab` 反推的 `SkillPanelW/H` / `SkillNodeX/Y` / `SkillCols` …
//
// ★ 本轮的**唯一依据**（三份权威载体，逐项可查）：
//   · 底图：`D2/UI/Panel/skltree_{cls}_back_{0..3}.png` —— **原版 PNG 未改一个字节**；
//     页 0 = 共用右列（顶部木框说明窗 + 3 个系页签，5 职业逐像素相同）；
//     页 k（k=1,2,3）= 系 k 的完整屏。
//   · 版面：`Def/SkillTreeLayout.cs`（**生成物**）—— 节点框 / 页签槽 / 说明窗 / 木框，
//     全部由 `tools/d2codec/export_skilltree_layout.py` 从上面那批 PNG 逐像素解析。
//   · 页↔系：底图第 k 页 ↔ 原版 `skilldesc.txt` 的 `SkillPage = k`（15/15 页两条独立特征互证）。
//
// ★ 层序（**实测定的，不是猜的**）：先铺**页 0**、再铺**页 k**。
//   依据：① 页 k 在 x 230..319 上只不透明于「被选中页签」那一槽（其余透明）；
//        ② 页 k 的树区右边框（原版 x 225..229）在**被选中页签**处断开；
//        ③ 页 0 叠在页 k 之上时选中页签是"暗的"，页 k 叠在页 0 之上时是"亮（高亮）的"。
//   ⇒ 只铺这两张**未改动的原版 PNG**，高亮页签 / 断口 / 右列三件事**全部自动成立**。
//
// ★ 技能图标尺寸 = 原版**位图原生 48×48**（`UiLayoutGame.SkillIconCell` = 48×48 原版px → 86.4 画布px），
//   中心 = 节点框（`SkillTreeCell.box`，L 形管线的外接矩形）的中心；`preserveAspect` 在正方形框里
//   是恒等变换（不拉变形、不放大、不加色调 —— 原版没有色调）。
//   ⚠️ 旧口径「框内径 41×46」把图标缩到 85.4% 并让底图管线露在图标外一圈，w3 审计已按
//      「控件矩形 == 原版像素 ×1.8」改成原生尺寸（出处见 `UiLayoutGame.SkillIconCell`）。
//   「能不能学」用**原版灰化帧**表达（`D2Icon.SkillIconPath(def.id, dull:true)`），不画任何自绘标记。
//
// ⛔ 零 `using Diablo2.Module`（分层自检 ③；`conventions.md` 硬性）。
// ⛔ 本文件**不画**任何原版底图里已有的东西（边框 / 连线 / 底 / 标题条）。
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
    /// <summary>技能树面板。层：<see cref="UILayer.Popup"/>。</summary>
    public class SkillTreePanel : UIPanel, IPointerClickHandler, IPointerMoveHandler
    {
        /// <summary>面板尺寸（= 原版底图一页 320×432 ×1.8 = 576×777.6）。</summary>
        public static readonly Vector2 PanelSize = UiLayoutGame.SkillPanelSize;

        /// <summary>面板位置（画布居中）。</summary>
        public static readonly Vector2 PanelPos = UiLayoutGame.SkillPanelPos;

        /// <summary>
        /// 技能图标层尺寸（= 原版**位图原生 48×48** 原版px → **86.4×86.4** 画布px）。
        /// <para>★ w3 审计修正：旧值是「节点框内径 41×46」，会把 48×48 的位图缩到 41（= 原版
        /// 像素的 85.4%）—— 依据与出处见 `UiLayoutGame.SkillIconCell`。图标中心不变。</para>
        /// </summary>
        public static readonly Vector2 IconCellSize = UiLayoutGame.SkillIconCell;

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

        private Text _points;           // 剩余技能点（说明区可见区内）
        private Text _desc;             // 所点/所悬停技能的 名+等级+说明（同区）

        private readonly List<Node> _nodes = new List<Node>();
        private readonly Dictionary<GameObject, int> _nodeByObject = new Dictionary<GameObject, int>();
        private readonly Dictionary<GameObject, int> _tabByObject = new Dictionary<GameObject, int>();

        /// <summary>说明区当前展示的技能下标（-1 = 还没悬停/点过任何技能）。</summary>
        private int _shownSkill = -1;

        /// <inheritdoc/>
        public override UILayer Layer => UILayer.Popup;

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
            //    层序依据见文件头「★ 层序」。此处**只建两个 Image**，贴图在 `ApplyBackdrop` 里按系换。
            // ★ 片 K（R8）：两张底图都**必须吃射线** —— 面板矩形（576×777.6，非满屏）⇒ 面板内空白吃点击、
            //   面板外仍可点地面走（原版语义）。若不吃射线，点面板内部空白会被反投影成"点地面"⇒ 角色走动。
            //   两张图**互斥显示**（`ApplyBackdrop` 按系切帧），故两张都要吃（哪张在台上就由哪张吃）。
            _bgTabs = UiArt.Panel(transform, "TreeBackPage0", PanelSize, PanelPos, UiArt.PanelBg, true);
            _bgTree = UiArt.Panel(transform, "TreeBackPageK", PanelSize, PanelPos, UiArt.PanelBg, true);

            BuildSkillInfoText();
            BuildTabHit();
        }

        /// <summary>
        /// 说明区文字（原版 px → 画布，落在**页 0 顶部木框的可见区**内）。
        /// <para>可见区 = `UiLayoutGame.SkillInfoBox`（木框外沿内缩金饰条 4px ⇒ 78×97 原版px）。
        /// 第一行 = 剩余技能点；其余 = 所点/所悬停技能的「名 / 等级 / 说明」。</para>
        /// <para>两行都不带任何自绘框 —— 框是原版木框自己画的。</para>
        /// </summary>
        private void BuildSkillInfoText()
        {
            var box = UiLayoutGame.SkillInfoBox;
            var center = UiLayoutGame.SkillArtToPanel(box.x + box.w * 0.5f, box.y + box.h * 0.5f);
            var size = UiLayoutGame.SkillArtSize(box.w, box.h);
            var lineH = UiLayoutGame.SkillPointsLineH;

            // 剩余技能点：说明区顶行（居中）
            _points = UiArt.Label(transform, "SkillPoints", string.Empty, 16, TextAnchor.MiddleCenter,
                UiArt.TitleColor, new Vector2(size.x, lineH),
                new Vector2(center.x, center.y + size.y * 0.5f - lineH * 0.5f));

            // 技能说明：说明区其余部分（左上，自动缩字号保证不出框）
            var restH = size.y - lineH;
            _desc = UiArt.Label(transform, "SkillDesc", string.Empty, 13, TextAnchor.UpperLeft,
                UiArt.TextColor, new Vector2(size.x, restH),
                new Vector2(center.x, center.y + size.y * 0.5f - lineH - restH * 0.5f));
            _desc.resizeTextForBestFit = true;
            _desc.resizeTextMinSize = 8;
            _desc.resizeTextMaxSize = 13;
        }

        /// <summary>
        /// 3 个**系页签**的点击区（位置 = `Def.SkillTreeLayout.TabSlots[系-1]`，原版 px，实测）。
        /// <para>点击区是**透明**的（原版页签是底图画好的，不许重画）⇒ 只盖一层隐形按钮。</para>
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
            }
        }

        /// <summary>节点：透明命中区（尺寸 = 原版节点框）+ 原版技能图标（框内径）。</summary>
        private Node CreateNode(int skillIndex)
        {
            var hit = UiArt.Panel(transform, "Node" + skillIndex,
                UiLayoutGame.SkillArtSize(SkillTreeLayout.CellW, SkillTreeLayout.CellH),
                Vector2.zero, new Color(0f, 0f, 0f, 0f), true);

            // 图标层：全白（原版亮度）+ 原生尺寸 48×48 ×1.8 = 86.4（preserveAspect 在正方形框里是
            // 恒等变换 ⇒ 原版像素 1:1 落位，既不放大也不拉变形）
            var icon = UiArt.Panel(hit.transform, "Icon", IconCellSize, Vector2.zero, Color.white, false);
            icon.preserveAspect = true;

            var node = new Node { Hit = hit, Icon = icon, SkillIndex = skillIndex };
            _nodes.Add(node);
            _nodeByObject[hit.gameObject] = _nodes.Count - 1;
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
                _points.text = "剩余技能点：—";
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
            _points.text = $"剩余技能点：{_tree.skillPoints}";

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

                if (cell.tree != _treeNo)
                {
                    node.Hit.gameObject.SetActive(false);
                    continue;
                }

                node.Hit.gameObject.SetActive(true);
                node.Tree = cell.tree;
                node.Hit.rectTransform.anchoredPosition = UiLayoutGame.SkillArtToPanel(
                    cell.box.x + cell.box.w * 0.5f, cell.box.y + cell.box.h * 0.5f);

                var learned = _tree.learnedLevels != null && i < _tree.learnedLevels.Count
                    ? _tree.learnedLevels[i] : 0;
                var learnable = _tree.learnable != null && i < _tree.learnable.Count && _tree.learnable[i];
                ApplyNodeIcon(node, def, learned, learnable);
                placed++;
            }

            // 数据比已建节点少时把多余节点藏起来（避免上一帧残留）
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
        /// 技能图标 = **原版位图**（`D2/UI/SkillIcon/{cls}Skillicon_{帧}`）。
        /// <para>"还学不了"用原版的**灰化帧**（同技能第 2 帧）表达 —— 原版就是这么区分状态的，
        /// 因此这里**不加任何色调**（上一版给已学加暖色、锁定加灰是自绘，本片删掉）。</para>
        /// </summary>
        private static void ApplyNodeIcon(Node node, SkillDef def, int learned, bool learnable)
        {
            var dull = !learnable && learned == 0;
            var path = D2Icon.SkillIconPath(def.id, dull);

            if (string.IsNullOrEmpty(path))
            {
                // 非预期分支：配表里查不到该技能 / 职业码 ⇒ 无图可贴（`D2Icon` 已点名 Warn）
                node.Icon.sprite = null;
                node.IconPath = null;
                return;
            }

            if (node.IconPath == path) return;          // 同一张图，已在（或正在）加载
            node.IconPath = path;
            node.Icon.color = Color.white;              // 原版亮度：不做任何色调
            UiArt.SetArtTint(node.Icon, Color.white);
            UiArt.SetSprite(node.Icon, path);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 交互
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>点系页签 ⇒ 切换当前系（一屏一系；原版底图第 k 页就是系 k 的完整屏）。</summary>
        private void OnTabClicked(int treeNo)
        {
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

            var cam = eventData.pressEventCamera;       // Screen Space - Overlay ⇒ null
            for (var i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node.Hit == null || !node.Hit.gameObject.activeInHierarchy) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(
                        node.Hit.rectTransform, eventData.position, cam))
                    continue;
                ShowSkill(node.SkillIndex);             // 原版：鼠标扫过技能 ⇒ 右列说明窗换内容
                return;
            }
        }

        /// <inheritdoc/>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || eventData.pointerPress == null) return;
            if (_tree?.skills == null) return;

            if (!_nodeByObject.TryGetValue(eventData.pointerPress, out var nodeIndex))
                return;                                  // 点的是页签/空地 ⇒ 交给其它通道

            var node = _nodes[nodeIndex];
            if (node.SkillIndex >= _tree.skills.Count) return;

            var def = _tree.skills[node.SkillIndex];
            if (def == null) return;

            ShowSkill(node.SkillIndex);

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

        /// <summary>把某技能的「名 / 等级 / 说明」写进说明窗（同一技能不重复刷、也不刷屏）。</summary>
        private void ShowSkill(int skillIndex)
        {
            if (!_built || _tree?.skills == null) return;
            if (skillIndex < 0 || skillIndex >= _tree.skills.Count) return;

            var def = _tree.skills[skillIndex];
            if (def == null) return;

            var learned = _tree.learnedLevels != null && skillIndex < _tree.learnedLevels.Count
                ? _tree.learnedLevels[skillIndex] : 0;

            if (_shownSkill == skillIndex) return;      // 同一个技能：不重复写
            _shownSkill = skillIndex;

            _desc.text = string.IsNullOrEmpty(def.desc)
                ? $"{def.name}　{learned}/{def.maxLevel}"
                : $"{def.name}　{learned}/{def.maxLevel}\n{def.desc}";
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
