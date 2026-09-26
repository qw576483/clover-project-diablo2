// ─────────────────────────────────────────────────────────────────────────────
// 人物属性面板（原版 `charstat.png` 320×432 → ×1.8 居中）：四维属性（带加点箭头 + 剩余点数）+
// 派生属性（生命/法力/耐力/防禦/攻擊準確率/格擋）+ 四系抗性 + 等級與經驗。
//   行名逐字取原版串表（`原版资源/d2text/chi_string.txt` id 4057-4074），见各常量注释；
//   两条**本项目扩展字段**（原版无 1:1 串）在 `ExtraName` / `Refresh` 处逐条登记。
//
//   ① 面板与全部行一律 **×1.8 居中**（原版 800×600 → 本工程 1920×1080）；
//   ② 行位置**改用 `CharstatPanel.prefab` 的节点实测值**（`UiLayoutGame` 里的
//      CharName / CharStatRowOrig / CharDerivedRowOrig / CharClosePos），
//   ③ 面板中心 = 原版矩形 x −320..0 的中心 (−160,0) → ×1.8 = **(−288,0)**：
//      **属性面板在原版里贴屏幕中线左侧**，背包贴右侧 —— 这是原版行为，不是错位。
//
// 原版 prefab 只有 9 个标签节点（CharName / Strength / Dex / Vitality / Energy /
//   Defense / Stamina / Life / Mana）+ 1 个 CloseButton。`PlayerStatsDto` 还有 等級 / 經驗 /
//   技能點（扩展字段）/ 攻擊準確率 / 格擋 / 四系抗性 ⇒ 按**底图 `Panel/charstat.png`（320×432，原版像素）**
//   逐像素量出来的凹槽落位：
//     · 上带右槽（art 193..309 × 10..26）→ 等級（见 `CharTopRightPos`）
//     · 第二排中/右槽（art 64..181 / 192..309 × 32..66）→ 技能點 / 經驗（见 `CharBand2*`）
//     · 右列上起第 3、4 行（art 161..269 + 271..309 × 142..162 / 166..186）→ 攻擊準確率 / 格擋
//       （见 `CharBottomRightOrig`）
//   右列「槽 ↔ 名」的对照现状（出处 = 原版 prefab + 底图逐像素，**不是缺图**）：
//     · `原版资源/参考工程_Diablerie/CharstatPanel.prefab` 的**具名**节点锚点换算 art_y = 216 − y_anchored，
//       与 `UiLayoutGame.CharDerivedRowOrig` / `CharResistRowOrig` 逐行**相等**：
//       DefenseLabel 206.5 / Stamina 244.6 / Life 268.8 / Mana 306.8 ↔ 本工程「防禦 / 耐力 / 生命 / 法力」；
//       左列 Strength 96.9 / Dex 159.0 / Vitality 244.4 / Energy 306.5 ↔ 「力量 / 敏捷 / 體力 / 精力」
//       ⇒ 这 8 行**逐槽对上**。
//     · 底图右列共 **12** 个标签隔间（实测 art_y ≈ 90 / 114 / 152 / 176 / 201 / 238 / 257 / 300 /
//       340 / 364 / 388 / 412；隔间内宽 97 / 97 / 108 / 108 / 108 / 68 / 68 / 68 / 95 / 95 / 95 / 95）
//       本工程只填到 10 个（152 / 176 / 201 / 238 / 257 / 300 + 下起 4 行）⇒ **art_y 90 与 114 两格空着**。
//     · **未定案（⛔ 不重排、不猜）**：具名节点只覆盖「防禦 / 耐力 / 生命 / 法力」4 条；上组 4 格
//       （90 / 114 / 152 / 176）里 152、176 由本工程占用，而可用的原版字段名只有
//       `傷害(4061)` / `攻擊準確率(4063)` / `比率(4065)` —— **3 条名对 4 格**；且串表 id 顺序会把
//       「防禦」摆在四行组的第 3 行（≈art 152），与 prefab 具名的 `DefenseLabel @ 206.5` **互相矛盾**。
//       另：原版字段 `傷害(4061)` 本工程**没有渲染**（已登记，待原版人物面板实机图定案）。
//     · 右列下起 4 行（art 174..269 + 271..309 × 332..421）→ 四系抗性（见 `CharResistRowOrig`）
//   逐行在 `UiLayoutGame` 里注明几何出处。
//
// 数据：只吃 `Diablo2.Def.PlayerStatsDto`（`OnOpen` 参数 + `Events.HudDirty`/`PlayerStatsChanged`）。
// 加点请求：`Events.StatAllocateRequest` + `Def.StatAllocArgs`（`delta` 可负，用于撤回）。
// 文字分两列（原版一个矩形里就是"名字在左、数字在右"）：数字走**原版位图字体**（`D2Label`），
// 零 `using Diablo2.Module`（分层自检 ③）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>人物属性面板。层：<see cref="UILayer.Popup"/>。</summary>
    public class CharacterPanel : UIPanel
    {
        /// <summary>面板尺寸（原版 `charstat.png` 320×432 → ×1.8 = 576×777.6）。</summary>
        public static readonly Vector2 PanelSize = UiLayoutGame.CharPanelSize;

        /// <summary>
        /// 面板中心（原版 Panel pivot(1.0,0.5)@(0,0) ⇒ 原版矩形 x −320..0 ⇒ 中心 (−160,0)
        /// ⇒ ×1.8 = **(−288,0)**；值取自 `UiLayoutGame.CharPanelPos = (−160×K, 0)`）。
        /// </summary>
        public static readonly Vector2 PanelPos = UiLayoutGame.CharPanelPos;

        /// <summary>四维行对应的枚举（顺序与 <see cref="UiLayoutGame.CharStatRowOrig"/> 一致）。</summary>
        public static readonly StatKind[] StatRowKind =
        {
            StatKind.Strength, StatKind.Dexterity, StatKind.Vitality, StatKind.Energy,
        };

        /// <summary>
        /// 四维行名，**逐字取原版串表**（`原版资源/d2text/chi_string.txt` 的人员面板字段名块
        /// id 4060/4062/4066/4069 = `力量` / `敏捷` / `體力` / `精力`）。
        /// <para>顺序与 <see cref="UiLayoutGame.CharStatRowOrig"/> 一致；原版串是**繁体**，照抄不改写
        /// （本工程字模没有简体码位、只有 `font_chi_s2t` 回退 ⇒ 写繁体 = 直查字模，写简体 = 走回退；
        /// 既有先例见 `WaypointPanel` 的「傳送點」等原样繁体串）。</para>
        /// </summary>
        public static readonly string[] StatRowName = { "力量", "敏捷", "體力", "精力" };

        /// <summary>
        /// 右侧派生行名，**逐字取原版串表**（id 4064/4067/4068/4070 = `防禦` / `耐力` / `生命` / `法力`）。
        /// <para>顺序与 <see cref="UiLayoutGame.CharDerivedRowOrig"/> 一致
        /// （prefab 节点名 Defense / Stamina / Life / Mana 是同一批行的英文名）。</para>
        /// </summary>
        public static readonly string[] DerivedName = { "防禦", "耐力", "生命", "法力" };

        /// <summary>
        /// 占底图右列上起第 3、4 行的两行名。
        /// <para>① `攻擊準確率` 逐字取原版串表 id 4063（该串原文是**两段式** `%s\n攻擊準確率`：
        /// 值在上、名在下 —— 本行取其中的**名**这一半，版面仍按底图隔间「名在左、值在右」；
        /// 整条串逐字采用会改本行版面含义 ⇒ 留待原版人物面板实机图定案，见
        /// `tools/probes/hosts/uicheck` 的既定口径）。</para>
        /// <para>② `格擋` = **本项目扩展字段**（登记在此）：原版串表里 `格擋` 这个词是有出处的
        /// （id 1853 `格擋的` / id 4363 `成功格擋： `），但**没有**一条把「格擋」当作独立字段名的串；
        /// 候选串 id 4065 是两段式 `%s\n比率`（原版这一行的名字就是「比率」，语义单看含糊）——
        /// 换不换待实机图定案。值取自 `PlayerStatsDto.blockChance`。</para>
        /// <para>③ **右列逐槽实测**（量法 `.ai-tmp/test/charstat_slots.py`，读数 `.ai-tmp/test/charstat_slots.tsv`；
        /// 底图 = `UI/Panel/charstat.png` 320×432 的暗色凹槽行带）：右列共 **8 个槽**，
        /// 行心 art y = `90 / 114 / 152 / 176 / 201.5 / 239.5 / 263.5 / 301.5`
        /// （标签框 art x 161..269 或 161..258，数值框 art x 271..309 或 260..309）。
        /// 其中 4 个已由 `原版资源/参考工程_Diablerie/CharstatPanel.prefab` 的节点坐标钉死
        /// （换算到 art 系后与左列四行同偏移 +5.3，**4/4 一致**）：
        /// `201.5 = 防禦`（= `DefenseLabel`，它用的正是那个 **109 宽的框**）、`239.5 = 耐力`、
        /// `263.5 = 生命`、`301.5 = 法力`。</para>
        /// <para>⇒ 上方 **4 个槽**（`90 / 114 / 152 / 176`）装的是原版串表右列剩下的 3 个字段
        /// `傷害(4061) / 攻擊準確率(4063) / 比率(4065)`，**4 槽对 3 字段 ⇒ 逐槽对应仍未定**：
        /// 底图那些框是**空凹槽**（字是运行时画的）、串表不带位置，本机 4 张基线图 + 2 个参考 prefab
        /// 都不含人物面板实机图 ⇒ 逐槽定案**必须有一张原版人物面板实机图**。
        /// 本项目现用：`攻擊準確率` 占 `152`、`格擋` 占 `176`，`90 / 114` 两槽留空；
        /// 行对齐线索 = `90` 与左列「力量」同行、`152` 与「敏捷」同行（图到手后按行对齐一次定案）。</para>
        /// </summary>
        public static readonly string[] ExtraName = { "攻擊準確率", "格擋" };

        /// <summary>右上凹槽（等級）的行名前缀：原版串 id 4057 `等級` + 一个空格（分隔名与值 = 本面板排版）。</summary>
        public const string TopRightPrefix = "等級 ";

        /// <summary>第二排右框（當前經驗）的行名前缀：原版串 id 4058 `經驗` + 一个空格。</summary>
        public const string Band2RightPrefix = "經驗 ";

        /// <summary>
        /// 第二排中框（技能點）的行名前缀：**本项目扩展字段**（登记口径见 <see cref="ExtraName"/> 与 `Refresh`）。
        /// <para>三个前缀都公开，是为了让判据（`tools/probes/hosts/uicheck` 的单行宽度判据）**量面板真正画的串**，
        /// 而不是在判据里另抄一份文案 —— 抄一份就会在文案改动后静默失配。</para>
        /// </summary>
        public const string Band2MidPrefix = "技能點 ";

        /// <summary>
        /// 四系抗性行名（顺序 火/冰/电/毒，与 <see cref="UiLayoutGame.CharResistRowOrig"/> 同序）。
        /// <para>出处 = **原版人物面板的串表条目** `原版资源/d2text/chi_string.txt` id 4071-4074
        /// （`火焰抵抗力 / 冰冷抵抗力 / 閃電抵抗力 / 毒素抵抗力`），**逐字照抄繁体**（不改简写、
        /// 也不缩成 `火/冰/電/毒`：原文就是这 5 个字的全名，5 字 = 55 art ≤ 标签隔间 96 art）；
        /// 物品 tooltip 一侧仍用配表
        /// `client/Assets/StreamingAssets/Table/Affix.tsv:19-26` 的 4 字词缀名（那一屏各自的出处）。</para>
        /// </summary>
        public static readonly string[] ResistName =
        {
            "火焰抵抗力", "冰冷抵抗力", "閃電抵抗力", "毒素抵抗力",
        };

        private bool _built;
        private bool _subscribed;
        private PlayerStatsDto _stats;

        private Text _nameText;
        private Text _topRightText;
        private Text _band2MidText;
        private Text _band2RightText;
        private ControlTip _closeTip;
        private readonly D2Label[] _statValues = new D2Label[4];
        private readonly Text[] _derivedNames = new Text[4];
        private readonly D2Label[] _derivedValues = new D2Label[4];
        private readonly Text[] _extraNames = new Text[2];
        private readonly D2Label[] _extraValues = new D2Label[2];
        private readonly Text[] _resistNames = new Text[4];
        private readonly D2Label[] _resistValues = new D2Label[4];
        private readonly Image[] _plusButtons = new Image[4];

        /// <inheritdoc/>
        public override UILayer Layer => UILayer.Popup;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();
            Subscribe();

            var stats = UiLog.Require<PlayerStatsDto>(param, nameof(CharacterPanel));
            if (stats != null) _stats = stats;
            Refresh(_stats);

            UiLog.Info($"人物属性面板已打开（数据={(stats != null ? "有快照" : "无 ⇒ 全 0 显示")}，"
                       + $"布局=原版 CharstatPanel.prefab ×{UiLayoutGame.K}）");
        }

        /// <inheritdoc/>
        public override void OnClose()
        {
            Unsubscribe();
            _closeTip?.Hide();          // 鼠标仍停在关闭钮上时关面板 ⇒ 提示要跟着收（参考实现的 `OnDisable`）
            UiLog.Info("人物属性面板已关闭");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 构件
        // ═════════════════════════════════════════════════════════════════════
        private void Build()
        {
            if (_built) return;
            _built = true;

            //   面板外仍可点地面走。理由与逐字判据见 `InventoryPanel.Build` 的同款注释。
            var bg = UiArt.Panel(transform, "CharstatBg", PanelSize, PanelPos, Color.white, true);
            UiArt.SetSprite(bg, ResPaths.PanelCharStat);

            // ── 角色名（原版 CharName 矩形）──
            _nameText = UiArt.Label(transform, "CharName", string.Empty, (int)UiLayoutGame.FontPx16, TextAnchor.MiddleCenter,
                UiArt.TitleColor, UiLayoutGame.CharNameSize, PanelPos + UiLayoutGame.CharNamePos);
            _nameText.raycastTarget = false;

            // ── 右上凹槽：等級（几何 = **底图实测凹槽** art 193..309 × 10..26；行名 = 原版串 id 4057）──
            _topRightText = UiArt.Label(transform, "TopRight", string.Empty, (int)UiLayoutGame.FontPx16, TextAnchor.MiddleCenter,
                UiArt.TextColor, UiLayoutGame.CharTopRightSize, PanelPos + UiLayoutGame.CharTopRightPos);
            _topRightText.raycastTarget = false;

            // ── 底图第二排两个空框：中框 = 技能點（扩展字段）/ 右框 = 經驗（原版串 id 4058）──
            //   串长 ≈ 400 画布px，而右上凹槽净宽只有 210 画布px ⇒ 左起越过凹槽左沿、右端越出面板右边缘。
            _band2MidText = UiArt.Label(transform, "Band2Mid", string.Empty, (int)UiLayoutGame.FontPx16,
                TextAnchor.MiddleCenter, UiArt.TextColor,
                UiLayoutGame.CharBand2MidSize, PanelPos + UiLayoutGame.CharBand2MidPos);
            _band2MidText.raycastTarget = false;

            _band2RightText = UiArt.Label(transform, "Band2Right", string.Empty, (int)UiLayoutGame.FontPx16,
                TextAnchor.MiddleCenter, UiArt.TextColor,
                UiLayoutGame.CharBand2RightSize, PanelPos + UiLayoutGame.CharBand2RightPos);
            _band2RightText.raycastTarget = false;

            BuildStatRows();
            BuildDerivedRows();
            BuildExtraRows();
            BuildResistRows();
            BuildCloseButton();
        }

        /// <summary>四维行：原版每个节点只有一个矩形（名字在左、数字在右），故同一矩形内拆成两半。</summary>
        private void BuildStatRows()
        {
            var rect = UiLayoutGame.CharStatRowSize;
            var nameSize = new Vector2(rect.x * 0.58f, rect.y);
            var nameOffset = new Vector2(-rect.x * 0.21f, 0f);

            //   数字画在底图的数值隔间里（原版 art 80..112 ⇒ node −64 ± 16，
            //   见 `UiLayoutGame.CharStatValueX/W` 的实测注释）。
            //   行高用**凹槽净高 18 art**（不是标签矩形的 27.9）⇒ 字底不压行框下沿金线。
            var valueSize = new Vector2(UiLayoutGame.CharStatValueW * UiLayoutGame.K,
                UiLayoutGame.CharRowSlotH * UiLayoutGame.K);

            for (var i = 0; i < 4; i++)
            {
                // 标签：仍用 prefab 的标签矩形中心（值不动），只把**文字**按凹槽中心下移修正 `CharRowTextDy`
                var center = PanelPos + UiLayoutGame.CharStatRowOrig[i] * UiLayoutGame.K;
                var textCenter = center + new Vector2(0f, UiLayoutGame.CharRowTextDy * UiLayoutGame.K);

                var nm = UiArt.Label(transform, "StatName" + i, StatRowName[i], (int)UiLayoutGame.FontPx16,
                    TextAnchor.MiddleLeft, UiArt.TextColor, nameSize, textCenter + nameOffset);
                nm.raycastTarget = false;

                _statValues[i] = D2Label.Create(transform, "StatValue" + i, "0", D2Text.D2Font.Font16,
                    TextAnchor.MiddleRight, UiArt.TitleColor, valueSize,
                    PanelPos + new Vector2(UiLayoutGame.CharStatValueX,
                        UiLayoutGame.CharStatRowOrig[i].y + UiLayoutGame.CharRowTextDy) * UiLayoutGame.K,
                    (int)UiLayoutGame.FontPx16);

                // 加点箭头（原版底图行尾的三角槽）：贴图用原版 `PANEL/menubutton.DC6` 帧 0（15×24）。
                // 帧名来源：`UiArt.ArrowFrame(0)` = DC6 直出那一套（**DC6 调色板索引 0 = 透明色**；
                //   不是"第 0 帧是空白帧" —— 帧 0 有内容：实测 opaque=322/360，
                //   量法：逐像素统计该帧的 alpha；两套导出的差别就是那 38 个透明像素），
                //   不用 Diablerie 副本（同画面但透明像素被写成不透明黑）
                //   —— 理由逐条见 `UI/UiArt.cs` 的「原版小图标帧」一节。
                var kind = StatRowKind[i];
                var index = i;
                var plus = UiArt.Panel(transform, "Plus" + i, UiLayoutGame.CharPlusSize,
                    center + new Vector2(UiLayoutGame.CharPlusX, 0f), Color.white, true);
                UiArt.SetSprite(plus, UiArt.ArrowFrame(0));
                var button = plus.gameObject.AddComponent<Button>();
                button.targetGraphic = plus;
                button.onClick.AddListener(() =>
                {
                    Game.Event.Emit(Events.UiClick);
                    Allocate(kind, index);
                });
                UiArt.ApplyArrowPressFrame(plus, button, 0);
                _plusButtons[i] = plus;
            }
        }

        /// <summary>右侧派生行（原版 Defense / Stamina / Life / Mana 的节点矩形）。</summary>
        private void BuildDerivedRows()
        {
            for (var i = 0; i < 4; i++)
            {
                var size = i == 0 ? UiLayoutGame.CharDefenseSize : UiLayoutGame.CharDerivedSize;
                var center = PanelPos + UiLayoutGame.CharDerivedRowOrig[i] * UiLayoutGame.K;
                var textCenter = center + new Vector2(0f, UiLayoutGame.CharRowTextDy * UiLayoutGame.K);

                _derivedNames[i] = UiArt.Label(transform, "DerivedName" + i, DerivedName[i],
                    (int)UiLayoutGame.FontPx16, TextAnchor.MiddleLeft, UiArt.TextColor,
                    new Vector2(size.x * 0.55f, size.y), textCenter + new Vector2(-size.x * 0.22f, 0f));
                _derivedNames[i].raycastTarget = false;

                // 数值列 = 底图**紧邻标签隔间右侧**的数值隔间 ——
                //   · i = 0（防禦）：art 271..309 ⇒ node 130 ± 19（`CharDefValueX/W`）
                //   · i ≥ 1（耐力/生命/法力）：底图这几行有**两格**数值隔间（art 231..270 + 272..309）
                //     ⇒ `cur/max` 一串写在**跨这两格**的框里（node 110 ± 39 = `CharCurMaxX/W`）
                var valueX = i == 0 ? UiLayoutGame.CharDefValueX : UiLayoutGame.CharCurMaxX;
                var valueW = i == 0 ? UiLayoutGame.CharDefValueW : UiLayoutGame.CharCurMaxW;

                _derivedValues[i] = D2Label.Create(transform, "DerivedValue" + i, "0", D2Text.D2Font.Font16,
                    TextAnchor.MiddleRight, UiArt.TitleColor,
                    new Vector2(valueW * UiLayoutGame.K, UiLayoutGame.CharRowSlotH * UiLayoutGame.K),
                    PanelPos + new Vector2(valueX,
                        UiLayoutGame.CharDerivedRowOrig[i].y + UiLayoutGame.CharRowTextDy) * UiLayoutGame.K,
                    (int)UiLayoutGame.FontPx16);
            }
        }

        /// <summary>攻擊準確率 / 格擋：占底图右列上起第 3、第 4 行（标签隔间 art 161..269 的实测矩形）。</summary>
        private void BuildExtraRows()
        {
            var size = UiLayoutGame.CharBottomRightSize;
            for (var i = 0; i < 2; i++)
            {
                var center = PanelPos + UiLayoutGame.CharBottomRightOrig[i] * UiLayoutGame.K;

                //   标签矩形 = **原版标签隔间本身**（`CharBottomRightSize` = art 161..269，109×18）——
                //   不按行宽缩到 55%：`攻擊準確率` 5 字 = 65 art > 60 art（55%），缩了就会被 `Wrap` 折成两行；
                //   矩形 = 隔间 ⇒ 左对齐文字起点仍是 art 161，且与数值隔间（art 271..309）不相交。
                _extraNames[i] = UiArt.Label(transform, "ExtraName" + i, ExtraName[i],
                    (int)UiLayoutGame.FontPx16, TextAnchor.MiddleLeft, UiArt.TextColor,
                    new Vector2(size.x, size.y), center);
                _extraNames[i].raycastTarget = false;

                // 数值列与「防禦」同列（底图 art 271..309 ⇒ node 130 ± 19）。
                //   行中心直接用 `CharBottomRightOrig`（它已是底图行框中心）⇒ 不加 `CharRowTextDy`
                //   （那个修正只属于 `CharDerivedRowOrig` 的 prefab 标签矩形，见其注释）。
                _extraValues[i] = D2Label.Create(transform, "ExtraValue" + i, "0", D2Text.D2Font.Font16,
                    TextAnchor.MiddleRight, UiArt.TitleColor,
                    new Vector2(UiLayoutGame.CharDefValueW * UiLayoutGame.K,
                        UiLayoutGame.CharRowSlotH * UiLayoutGame.K),
                    PanelPos + new Vector2(UiLayoutGame.CharDefValueX, UiLayoutGame.CharBottomRightOrig[i].y)
                        * UiLayoutGame.K,
                    (int)UiLayoutGame.FontPx16);
            }
        }

        /// <summary>四系抗性：占底图右列**下起 4 行**的实测矩形（标签隔间 art 174..269 + 数值隔间 art 271..309）。</summary>
        private void BuildResistRows()
        {
            var size = UiLayoutGame.CharResistRowSize;
            for (var i = 0; i < 4; i++)
            {
                var center = PanelPos + UiLayoutGame.CharResistRowOrig[i] * UiLayoutGame.K;

                //   标签框 = 底图标签隔间（`CharResistNameX/W` = art 174..269）。
                //   画法 = **单行 + 允许溢出**（原版画字不折行）⇒ 关掉 uGUI 的 `Wrap`：
                //   `D2TextMirror` 每帧把 `Text.horizontalOverflow` 同步给 `D2Label`，
                //   而 `D2Label` 只在 `Wrap` 时算 `availPx` ⇒ `Overflow` 下**永不折行**。
                _resistNames[i] = UiArt.Label(transform, "ResistName" + i, ResistName[i],
                    (int)UiLayoutGame.FontPx16, TextAnchor.MiddleLeft, UiArt.TextColor,
                    new Vector2(UiLayoutGame.CharResistNameW * UiLayoutGame.K, size.y),
                    PanelPos + new Vector2(UiLayoutGame.CharResistNameX, UiLayoutGame.CharResistRowOrig[i].y)
                        * UiLayoutGame.K);
                _resistNames[i].horizontalOverflow = HorizontalWrapMode.Overflow;
                _resistNames[i].raycastTarget = false;

                //   值列 = 底图那条数值隔间（`CharResistValueX/W` = art 271..309 = 39 art，
                //   与派生行 `CharDefValueX/W` 同列）。最坏值 `-100%` 的 advance = 47 art > 39 art
                //   ⇒ 这一格**有意溢出**（仍单行、向右压出框沿），不缩字、也不砍 `%`；
                //   算术见 `UiLayoutGame.CharResistValueX`。
                //   `D2Label.Create` 的默认就是 `Overflow`；这里显式写一次，避免将来被改成 `Wrap`
                //   （改成 `Wrap` 会把 `-100%` 折成两行）。
                _resistValues[i] = D2Label.Create(transform, "ResistValue" + i, "0%", D2Text.D2Font.Font16,
                    TextAnchor.MiddleRight, UiArt.TitleColor,
                    new Vector2(UiLayoutGame.CharResistValueW * UiLayoutGame.K, size.y),
                    PanelPos + new Vector2(UiLayoutGame.CharResistValueX, UiLayoutGame.CharResistRowOrig[i].y)
                        * UiLayoutGame.K,
                    (int)UiLayoutGame.FontPx16);
                _resistValues[i].horizontalOverflow = HorizontalWrapMode.Overflow;
            }
        }

        /// <summary>
        /// 关闭钮三件事：
        /// ① 命中区**几何不动**（原版 prefab `CloseButton` (−15.4,−188.3) 32×31，与底图
        ///    art 128..159 × 389..420 的方形凹槽实测一致）；
        /// ② 贴**原版方钮的「关闭 / 取消」图形帧**（`PANEL/buysellbtn.DC6` 帧 10 常态 / 帧 11 按下）——
        ///    语义与帧号的绑定出处 = 参考工程 `CharstatPanel.prefab` 的同名 `CloseButton.m_Sprite`
        ///    （见 `Core/ResPaths.cs` 的 `BuySellButtonFrameClose`）；
        /// ③ 兜底色 = 原版按钮底板色（**alpha 1**）⇒ 即使贴图加载失败，也**可见 + 可点**
        ///    （判据 `FindCloseNode != null &amp;&amp; alpha &gt; 0.9` 因此不依赖素材是否到位）。
        /// <para>钮面**只有图形、没有常显文字**。依据 = 原版 prefab 那个 `text: Close` 字段属于挂在该节点上的
        /// `Tooltip` 组件（guid `371dd4dd5595a3b46bc35996b3c6be92` =
        /// `Assets/Scripts/Diablerie/Engine/UI/Tooltip.cs`）：它是 **hover 文案**
        /// （`OnPointerEnter` → `Ui.ShowScreenLabel(控件矩形顶边中点, text)`，
        /// `OnPointerExit` / `OnDisable` → `Ui.HideScreenLabel()`）⇒ ⛔ `text` **不是标签**，
        /// 不要照它给钮加常显文字（`Ui.ShowScreenLabel` 那套屏幕标签本项目没有载体）。</para>
        /// </summary>
        private void BuildCloseButton()
        {
            var close = UiArt.Panel(transform, "CloseButton", UiLayoutGame.CharCloseSize,
                PanelPos + UiLayoutGame.CharClosePos, UiArt.ButtonBg, true);
            //   图形帧的路径走 `ResPaths` 的**具名常量**（见 `UiArt.ApplyCloseButtonArt`）——
            //   不写字面量：写错路径的失败模式是**静默返回 null**（真值只在常量里）。
            close.preserveAspect = true;

            var button = close.gameObject.AddComponent<Button>();
            button.targetGraphic = close;
            UiArt.ApplyCloseButtonArt(close, button);

            //   悬停提示（原版该节点挂着 `Tooltip`、文案字段 = `Close`；出处与外观口径见 `UI/ControlTip.cs`）：
            //   进 / 出各一次显隐；面板 `OnClose` 再收一次（对应参考实现的 `OnDisable`）。
            //   文案 = 原版串表逐字（`WaypointPanel.CloseText` = `關閉`，出处见该常量自身的注释）。
            _closeTip = ControlTip.Create(transform, close.rectTransform, WaypointPanel.CloseText);
            var hover = close.gameObject.AddComponent<HoverTarget>();
            if (_closeTip != null)
            {
                hover.OnEnter = _closeTip.Show;
                hover.OnExit = _closeTip.Hide;
            }

            button.onClick.AddListener(() =>
            {
                UiLog.Info("点属性面板关闭按钮 ⇒ 走 `Events.PanelToggleRequest` 关闭（与按 C/Esc 同一条路径）");
                Game.Event.Emit(Events.PanelToggleRequest, nameof(CharacterPanel));
            });
        }

        // ═════════════════════════════════════════════════════════════════════
        // 刷新 / 加点
        // ═════════════════════════════════════════════════════════════════════
        private void Refresh(PlayerStatsDto stats)
        {
            if (!_built)
            {
                UiLog.Warn("人物属性面板尚未构建就收到刷新 ⇒ 忽略");
                return;
            }

            if (stats == null)
            {
                UiLog.WarnOnce("char.no.stats",
                    "人物属性面板未收到 `PlayerStatsDto` ⇒ 全 0 显示；"
                    + "请 Player 模块在进图装配完成后 emit 一次 `Events.HudDirty`");
            }

            _nameText.text = stats != null && !string.IsNullOrEmpty(stats.name) ? stats.name : "(无角色)";
            var points = stats != null ? stats.statPoints : 0;
            var skillPoints = stats != null ? stats.skillPoints : 0;

            // 三段分框显示（右上 = 等級 / 第二排右框 = 經驗 / 第二排中框 = 技能點）。
            //   出处：底图三处凹槽的逐像素实测（`UiLayoutGame.CharTopRightPos` / `CharBand2RightPos` /
            //   `CharBand2MidPos`）。
            //   本框只显示当前经验：原版 `CharstatPanel.prefab`（`原版资源/参考工程_Diablerie/`）无等级/经验节点，
            //   原版把「当前经验 / 下一等级」并列的载体是底部经验条的悬停提示串
            //   （`原版资源/d2text/chi_string.txt` 串 4163 `經驗： %u / %u`），不在人物面板。
            //   容量（框 118 art ⇒ availPx 99；font16 汉字 13 / 数字 6 / 斜杠 3 art）：「經驗 {10 位}」need 89 ⇒ 单行；
            //   「經驗 {10 位}/{10 位}」need 152、「下一等級 {10 位}」need 115 ⇒ 都排不下。
            //   行名前缀逐字取原版串表：`等級` id 4057 / `經驗` id 4058。
            //   第二排中框的 `技能點` 是**本项目扩展字段**（登记在此）：原版人物面板不显示技能点，
            //   串表里 4059 `下一等級` / 4075 `狀況點數` / 4076 `剩餘` 都指别的字段 ⇒ 无 1:1 原版串可用。
            _topRightText.text = TopRightPrefix + (stats != null ? stats.level : 0);
            _band2RightText.text = Band2RightPrefix + (stats != null ? stats.exp : 0);
            _band2MidText.text = Band2MidPrefix + skillPoints;

            SetStat(0, stats?.str ?? 0);
            SetStat(1, stats?.dex ?? 0);
            SetStat(2, stats?.vit ?? 0);
            SetStat(3, stats?.eng ?? 0);

            // 加点按钮：剩余点数决定能不能点（`Button.interactable`）；箭头贴图与颜色不在这里改
            for (var i = 0; i < _plusButtons.Length; i++)
                SetPlusEnabled(_plusButtons[i], points > 0);

            SetDerived(0, stats?.defense ?? 0, string.Empty);
            SetDerived(1, stats?.stamina ?? 0, "/" + (stats?.maxStamina ?? 0));
            SetDerived(2, stats?.life ?? 0, "/" + (stats?.maxLife ?? 0));
            SetDerived(3, stats?.mana ?? 0, "/" + (stats?.maxMana ?? 0));

            SetExtra(0, stats?.attackRating ?? 0, string.Empty);
            SetExtra(1, stats?.blockChance ?? 0, "%");

            _resistValues[0].SetText((stats?.fireResist ?? 0) + "%");
            _resistValues[1].SetText((stats?.coldResist ?? 0) + "%");
            _resistValues[2].SetText((stats?.lightResist ?? 0) + "%");
            _resistValues[3].SetText((stats?.poisonResist ?? 0) + "%");
        }

        /// <summary>
        /// 加点箭头的可交互态：<paramref name="on"/> 写进 `Button.interactable`（`points == 0` ⇒ 不可点）。
        /// <para>`UiArt` 只封装了「带 Label 的按钮」，这里是贴图按钮，故本面板自己拿 `Button`。
        /// 箭头贴图与颜色由 `UiArt.SetSprite` 按原版帧决定（`UiArt.ArrowFrame(0)`），本方法不改色。</para>
        /// </summary>
        private static void SetPlusEnabled(Image img, bool on)
        {
            if (img == null) return;

            var button = img.GetComponent<Button>();
            if (button != null) button.interactable = on;
        }

        private void SetStat(int index, int value) => _statValues[index].SetText(value.ToString());

        private void SetDerived(int index, long value, string suffix)
        {
            _derivedNames[index].text = DerivedName[index];
            _derivedValues[index].SetText(value + suffix);
        }

        private void SetExtra(int index, long value, string suffix)
            => _extraValues[index].SetText(value + suffix);

        private void Allocate(StatKind kind, int rowIndex)
        {
            var points = _stats != null ? _stats.statPoints : 0;
            if (points <= 0)
            {
                UiLog.Info($"加「{StatRowName[rowIndex]}」被拒：剩余属性点为 0（按钮本应置灰）");
                Game.UI.Toast("没有可分配的属性点");
                return;
            }

            UiLog.Info($"请求加「{StatRowName[rowIndex]}」1 点（`{Events.StatAllocateRequest}`，"
                       + $"剩余 {points} ⇒ 由 Player 模块结算）");
            Game.Event.Emit(Events.StatAllocateRequest, new StatAllocArgs { kind = kind, delta = 1 });
        }

        // ═════════════════════════════════════════════════════════════════════
        // 事件
        // ═════════════════════════════════════════════════════════════════════
        private void Subscribe()
        {
            if (_subscribed || Game.Event == null) return;
            _subscribed = true;
            Game.Event.On<PlayerStatsDto>(Events.HudDirty, OnStats);
            Game.Event.On<PlayerStatsDto>(Events.PlayerStatsChanged, OnStats);
            Game.Event.On<int>(Events.LevelUp, OnLevelUp);
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Game.Event == null) return;
            _subscribed = false;
            Game.Event.Off<PlayerStatsDto>(Events.HudDirty, OnStats);
            Game.Event.Off<PlayerStatsDto>(Events.PlayerStatsChanged, OnStats);
            Game.Event.Off<int>(Events.LevelUp, OnLevelUp);
        }

        private void OnStats(PlayerStatsDto stats)
        {
            if (stats == null)
            {
                UiLog.Warn("收到属性快照但参数为 null ⇒ 忽略");
                return;
            }
            _stats = stats;
            Refresh(_stats);
        }

        private void OnLevelUp(int level)
        {
            if (_stats != null) _stats.level = level;
            Refresh(_stats);
        }
    }
}
