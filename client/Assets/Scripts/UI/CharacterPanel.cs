// ─────────────────────────────────────────────────────────────────────────────
// 人物属性面板（原版 `charstat.png` 320×432 → ×1.8 居中）：四维属性（带加点箭头 + 剩余点数）+
// 派生属性（生命/法力/耐力/防御/命中/格挡）+ 四系抗性 + 等级与经验。
//
//   ① 面板与全部行一律 **×1.8 居中**（原版 800×600 → 本工程 1920×1080）；
//   ② 行位置**改用 `CharstatPanel.prefab` 的节点实测值**（`UiLayoutGame` 里的
//      CharName / CharStatRowOrig / CharDerivedRowOrig / CharClosePos），
//   ③ 面板中心 = 原版矩形 x −320..0 的中心 (−160,0) → ×1.8 = **(−288,0)**：
//      **属性面板在原版里贴屏幕中线左侧**，背包贴右侧 —— 这是原版行为，不是错位。
//
// 原版 prefab 只有 5 个标签节点（CharName / Strength / Dex / Vitality / Energy /
//   Defense / Stamina / Life / Mana）+ 1 个 CloseButton，**没有**「等级/经验/命中/格挡/四系抗性」
//   的节点（原版把这些画在别的屏）。本项目 DTO 有这些字段 ⇒ 按**原版底图上剩下的空框**补齐：
//     · 右上空框（art x 165..315）→ 等级 / 经验
//     · 右下两个细长空框（art x 180..310, y 403..430）→ 命中 / 格挡
//     · 左下空白区（art y 331..421）→ 四系抗性
//   逐行在 `UiLayoutGame` 里注明「本项目新增」。
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

        /// <summary>四维中文名（本项目新增；配表 `class_c` 无四维列名，故写成常量）。</summary>
        public static readonly string[] StatRowName = { "力量", "敏捷", "体力", "精力" };

        /// <summary>右侧派生行名（顺序与 <see cref="UiLayoutGame.CharDerivedRowOrig"/> 一致，原版节点名）。</summary>
        public static readonly string[] DerivedName = { "防御", "耐力", "生命", "法力" };

        /// <summary>补齐行名（本项目新增；原版 prefab 无对应节点，见文件头说明）。</summary>
        public static readonly string[] ExtraName = { "命中", "格挡" };

        /// <summary>
        /// 四系抗性行名（顺序 火/冰/电/毒，与 <see cref="UiLayoutGame.CharResistRowOrig"/> 同序）。
        /// <para>出处：本项目**配表**里这四个名字就是它们 ——
        /// `client/Assets/StreamingAssets/Table/Affix.tsv:19-26` 的
        /// `res-cold 冰冷抗性 / res-fire 火焰抗性 / res-ltng 闪电抗性 / res-pois 毒素抗性`
        /// （由 `tools/table-convert/cn_names.py` 从原版串表映射；原版长形 `4071..4074`
        /// 「火焰抵抗力/冰冷抵抗力/閃電抵抗力/毒素抵抗力」是 5 字，advance 65 art ⇒ 需 76 art 框，
        /// 与「值列不折行」在 art 0..112.5 的预算内互斥 ⇒ 只能取 4 字这一档）。
        /// ⇒ **本面板与物品 tooltip 用同一套词**，改文案会让两处不一致。</para>
        /// </summary>
        public static readonly string[] ResistName = { "火焰抗性", "冰冷抗性", "闪电抗性", "毒素抗性" };

        private bool _built;
        private bool _subscribed;
        private PlayerStatsDto _stats;

        private Text _nameText;
        private Text _topRightText;
        private Text _band2MidText;
        private Text _band2RightText;
        private Text _closeText;
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

            // ── 右上凹槽：等级（本项目新增；几何 = **底图实测凹槽** art 193..309 × 10..26）──
            _topRightText = UiArt.Label(transform, "TopRight", string.Empty, (int)UiLayoutGame.FontPx16, TextAnchor.MiddleCenter,
                UiArt.TextColor, UiLayoutGame.CharTopRightSize, PanelPos + UiLayoutGame.CharTopRightPos);
            _topRightText.raycastTarget = false;

            // ── 底图第二排两个空框（本项目新增）：中框 = 技能点 / 右框 = 经验 ──
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
                button.onClick.AddListener(() => Allocate(kind, index));
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
                //   · i = 0（防御）：art 271..309 ⇒ node 130 ± 19（`CharDefValueX/W`）
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

        /// <summary>补齐行：命中 / 格挡（本项目新增，占原版右下两个细长空框）。</summary>
        private void BuildExtraRows()
        {
            var size = UiLayoutGame.CharBottomRightSize;
            for (var i = 0; i < 2; i++)
            {
                var center = PanelPos + UiLayoutGame.CharBottomRightOrig[i] * UiLayoutGame.K;

                _extraNames[i] = UiArt.Label(transform, "ExtraName" + i, ExtraName[i],
                    (int)UiLayoutGame.FontPx16, TextAnchor.MiddleLeft, UiArt.TextColor,
                    new Vector2(size.x * 0.55f, size.y), center + new Vector2(-size.x * 0.22f, 0f));
                _extraNames[i].raycastTarget = false;

                // 数值列与「防御」同口径（底图右下这几个薄框与右侧派生行同宽：
                //   art 271..309 ⇒ node 130 ± 19）。
                _extraValues[i] = D2Label.Create(transform, "ExtraValue" + i, "0", D2Text.D2Font.Font16,
                    TextAnchor.MiddleRight, UiArt.TitleColor,
                    new Vector2(UiLayoutGame.CharDefValueW * UiLayoutGame.K,
                        UiLayoutGame.CharRowSlotH * UiLayoutGame.K),
                    PanelPos + new Vector2(UiLayoutGame.CharDefValueX, UiLayoutGame.CharBottomRightOrig[i].y)
                        * UiLayoutGame.K,
                    (int)UiLayoutGame.FontPx16);
            }
        }

        /// <summary>四系抗性（本项目新增，占原版左下空白区）。</summary>
        private void BuildResistRows()
        {
            var size = UiLayoutGame.CharResistRowSize;
            for (var i = 0; i < 4; i++)
            {
                var center = PanelPos + UiLayoutGame.CharResistRowOrig[i] * UiLayoutGame.K;

                //   标签框改走 `CharResistNameX/W`（中心 node −127、宽 66 art = art 0..66），
                //   值列同步改走收窄后的 `CharResistValueX/W`（art 67.5..112.5，**右沿不动**）；
                //   两个框不相交（间隔 1.5 art px）、都在面板内。
                //   为什么不能用「四维标签隔间的左沿 art 10」起框、为什么不能改文案
                //   （配表出处 + 5 字原版长形放不下）⇒ 逐条写在 `UiLayoutGame.CharResistNameX` 的注释里。
                //   折行判据（生产口径：`needNative < availPx`）见
                //   `tools/probes/hosts/uicheck/U52ResistCheck.cs`。
                _resistNames[i] = UiArt.Label(transform, "ResistName" + i, ResistName[i],
                    (int)UiLayoutGame.FontPx16, TextAnchor.MiddleLeft, UiArt.TextColor,
                    new Vector2(UiLayoutGame.CharResistNameW * UiLayoutGame.K, size.y),
                    PanelPos + new Vector2(UiLayoutGame.CharResistNameX, UiLayoutGame.CharResistRowOrig[i].y)
                        * UiLayoutGame.K);
                _resistNames[i].raycastTarget = false;

                // 值列**与四维行数值列对齐**（同一列 x = `CharStatValueX`，宽 `CharStatValueW`）。
                //   本组行是"本项目新增"（底图左下是空白大理石，没有隔间）⇒ 列位只在面板内部求一致：
                //   与**最近的一族有框行**（四维行）同列 ⇒ **右沿与四维数值列右沿对齐** = art 112.5。
                //   值列宽 = **45 art**（左沿 art 67.5，右沿 art 112.5）——
                //   给上面那个 66 art 的标签框让位；`-100%`（advance 47 art）在 45 art 下
                //   availPx = 52 > 47 ⇒ **不折行**（余量 5 art；最小可放宽度 = 42 art）。
                //   出处与算术逐条写在 `UiLayoutGame.CharResistValueX` 的注释里，
                //   离线判据（含退化样本）见 `tools/probes/hosts/uicheck/U52ResistCheck.cs`。
                _resistValues[i] = D2Label.Create(transform, "ResistValue" + i, "0%", D2Text.D2Font.Font16,
                    TextAnchor.MiddleRight, UiArt.TitleColor,
                    new Vector2(UiLayoutGame.CharResistValueW * UiLayoutGame.K, size.y),
                    PanelPos + new Vector2(UiLayoutGame.CharResistValueX, UiLayoutGame.CharResistRowOrig[i].y)
                        * UiLayoutGame.K,
                    (int)UiLayoutGame.FontPx16);
            }
        }

        /// <summary>关闭钮文案（与 `UI/WaypointPanel.cs` 的关闭钮同口径：原版按钮帧 + 原版字模文案）。</summary>
        private const string CloseText = "关闭";

        /// <summary>
        /// 关闭钮三件事：
        /// ① 命中区**几何不动**（原版 prefab `CloseButton` (−15.4,−188.3) 32×31，与底图
        ///    art 128..159 × 389..420 的方形凹槽实测一致）；
        /// ② 贴**原版按钮帧** `Resources/Clover/D2/UI/Menu/btn_cancel_0.png`（DC6 直出，
        ///    与 `WaypointPanel` 的中按钮同源）+ 原版字模文案「关闭」；
        /// ③ 兜底色 = 原版按钮底板色（**alpha 1**）⇒ 即使贴图加载失败，也**可见 + 可点**
        ///    （判据 `FindCloseNode != null &amp;&amp; alpha &gt; 0.9` 因此不依赖素材是否到位）。
        /// <para>原版依据：原版 prefab 的 `CloseButton` 自带一个 `text: Close` 的标签组件
        /// （原版 prefab 的 MonoBehaviour 114272374721566020）。
        /// 原版那个 X 的独立图形不在本机素材里 ⇒ **只追加**登记到 `client/资源欠缺清单.md`，不自己画一个冒充原版。</para>
        /// </summary>
        private void BuildCloseButton()
        {
            var close = UiArt.Panel(transform, "CloseButton", UiLayoutGame.CharCloseSize,
                PanelPos + UiLayoutGame.CharClosePos, UiArt.ButtonBg, true);
            //   路径走 `ResPaths` 的**具名常量**（`Core/ResPaths.cs:255`，= 原版
            //   `MENU/MediumButtonBlank.dc6` 帧 0，与 `WaypointPanel` 的中按钮**同一张图**）——
            //   不写字面量：`ResPaths.cs:21` 记着"未确认文件名的素材不要臆造常量"，
            //   而写错路径的失败模式是**静默返回 null**（真值只在常量里）。
            UiArt.SetSprite(close, ResPaths.BtnMedNormal);
            close.preserveAspect = true;

            // 原版字模文案（`D2Label`，与面板其它文字同字号 = `UiLayoutGame.FontPx16`）
            var text = UiArt.Label(transform, "CloseLabel", CloseText, (int)UiLayoutGame.FontPx16,
                TextAnchor.MiddleCenter, UiArt.ButtonText, UiLayoutGame.CharCloseSize,
                PanelPos + UiLayoutGame.CharClosePos);
            text.raycastTarget = false;
            _closeText = text;

            var button = close.gameObject.AddComponent<Button>();
            button.targetGraphic = close;
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

            // 三段分框显示（右上 = 等级 / 第二排右框 = 经验 / 第二排中框 = 技能点）。
            //   出处：底图三处凹槽的逐像素实测（`UiLayoutGame.CharTopRightPos` / `CharBand2RightPos` /
            //   `CharBand2MidPos`）。
            _topRightText.text = $"等级 {(stats != null ? stats.level : 0)}";
            _band2RightText.text = $"经验 {(stats != null ? stats.exp : 0)}/{(stats != null ? stats.expNext : 0)}";
            _band2MidText.text = $"技能点 {skillPoints}";

            SetStat(0, stats?.str ?? 0);
            SetStat(1, stats?.dex ?? 0);
            SetStat(2, stats?.vit ?? 0);
            SetStat(3, stats?.eng ?? 0);

            // 加点按钮：没有剩余点数就置灰（原版也是灰的）
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

        /// <summary>置灰 / 点亮加点箭头（`UiArt` 只封装了「带 Label 的按钮」，这里是贴图按钮，故本面板自理）。</summary>
        private static void SetPlusEnabled(Image img, bool on)
        {
            if (img == null) return;

            var button = img.GetComponent<Button>();
            if (button != null) button.interactable = on;

            img.color = on ? Color.white : new Color(1f, 1f, 1f, 0.40f);
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
