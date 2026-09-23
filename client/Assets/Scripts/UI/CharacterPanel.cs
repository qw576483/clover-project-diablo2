// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/CharacterPanel.cs（agent-09 · 1:1 轮）
// 人物属性面板（原版 `charstat.png` 320×432 → ×1.8 居中）：四维属性（带加点箭头 + 剩余点数）+
// 派生属性（生命/法力/耐力/防御/命中/格挡）+ 四系抗性 + 等级与经验。
//
// ★★ 本轮（1:1）改了什么：
//   ① 面板与全部行一律 **×1.8 居中**（原版 800×600 → 本工程 1920×1080）；
//   ② 行位置**改用 `CharstatPanel.prefab` 的节点实测值**（`UiLayoutGame` 里的
//      CharName / CharStatRowOrig / CharDerivedRowOrig / CharClosePos），
//      不再用上一轮"扫描贴图亮度峰"的近似值；
//   ③ 面板中心 = 原版矩形 x −320..0 的中心 (−160,0) → ×1.8 = **(−288,0)**：
//      **属性面板在原版里贴屏幕中线左侧**，背包贴右侧 —— 这是原版行为，不是错位。
//
// ⚠️ 原版 prefab 只有 5 个标签节点（CharName / Strength / Dex / Vitality / Energy /
//   Defense / Stamina / Life / Mana）+ 1 个 CloseButton，**没有**「等级/经验/命中/格挡/四系抗性」
//   的节点（原版把这些画在别的屏）。本项目 DTO 有这些字段 ⇒ 按**原版底图上剩下的空框**补齐：
//     · 右上空框（art x 165..315）→ 等级 / 经验
//     · 右下两个细长空框（art x 180..310, y 403..430）→ 命中 / 格挡
//     · 左下空白区（art y 331..421）→ 四系抗性
//   逐行在 `UiLayoutGame` 里注明「本项目新增」，并在回报的对照表里单列。
//
// ★ 数据：只吃 `Diablo2.Def.PlayerStatsDto`（`OnOpen` 参数 + `Events.HudDirty`/`PlayerStatsChanged`）。
// ★ 加点请求：`Events.StatAllocateRequest` + `Def.StatAllocArgs`（`delta` 可负，用于撤回）。
// ★ 文字分两列（原版一个矩形里就是"名字在左、数字在右"）：数字走**原版位图字体**（`D2Label`），
//   中文名也走原版位图字模（片 3：`D2/Fonts/font16_chi`；原版 chi 字模是繁体字集，
//   简体字由 `D2/Fonts/font_chi_s2t` 换成原版字形 —— 见 `UI/D2Text.cs` 文件头）。
// ⛔ 零 `using Diablo2.Module`（分层自检 ③）。
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
        /// ⇒ ×1.8 = **(−288,0)**）。
        /// <para>⚠️ w3 审计修正注释：旧注释写「(−384,0)」与值不符（−384 = 原版 −213.33×1.8，
        /// 既不是 −320×1.8(=−576) 也不是 −160×1.8(=−288)）。**值一直是对的**（见
        /// `UiLayoutGame.CharPanelPos = (−160×K, 0)`），只是注释里的数字抄错了。</para>
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

        /// <summary>四系抗性（本项目新增）。</summary>
        public static readonly string[] ResistName = { "火焰抗性", "冰冷抗性", "闪电抗性", "毒素抗性" };

        private bool _built;
        private bool _subscribed;
        private PlayerStatsDto _stats;

        private Text _nameText;
        private Text _topRightText;
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

            // ★ 片 K（R8）：底图吃射线 —— 面板矩形（576×777.6，非满屏）⇒ 面板内空白吃掉点击、
            //   面板外仍可点地面走。理由与逐字判据见 `InventoryPanel.Build` 的同款注释。
            var bg = UiArt.Panel(transform, "CharstatBg", PanelSize, PanelPos, Color.white, true);
            UiArt.SetSprite(bg, ResPaths.PanelCharStat);

            // ── 角色名（原版 CharName 矩形）──
            _nameText = UiArt.Label(transform, "CharName", string.Empty, 22, TextAnchor.MiddleCenter,
                UiArt.TitleColor, UiLayoutGame.CharNameSize, PanelPos + UiLayoutGame.CharNamePos);
            _nameText.raycastTarget = false;

            // ── 右上框：等级 / 经验（本项目新增，占原版右上空框）──
            _topRightText = UiArt.Label(transform, "TopRight", string.Empty, 18, TextAnchor.MiddleCenter,
                UiArt.TextColor, UiLayoutGame.CharTopRightSize, PanelPos + UiLayoutGame.CharTopRightPos);
            _topRightText.raycastTarget = false;

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
            var valueSize = new Vector2(rect.x * 0.42f, rect.y);
            var nameOffset = new Vector2(-rect.x * 0.21f, 0f);
            var valueOffset = new Vector2(rect.x * 0.29f, 0f);

            for (var i = 0; i < 4; i++)
            {
                var center = PanelPos + UiLayoutGame.CharStatRowOrig[i] * UiLayoutGame.K;

                var nm = UiArt.Label(transform, "StatName" + i, StatRowName[i], 17, TextAnchor.MiddleLeft,
                    UiArt.TextColor, nameSize, center + nameOffset);
                nm.raycastTarget = false;

                // ★ 片 font-scale：补显式字号（默认 0 = 按原版 px 1:1 画 ⇒ 只有应有的 ~55%；
                //   用户报「属性面板文字太小」的 4 组之一）。字号唯一出处 = `UiLayoutGame.FontPx16`。
                _statValues[i] = D2Label.Create(transform, "StatValue" + i, "0", D2Text.D2Font.Font16,
                    TextAnchor.MiddleRight, UiArt.TitleColor, valueSize, center + valueOffset,
                    (int)UiLayoutGame.FontPx16);

                // 加点箭头（原版底图行尾的三角槽）：贴图用原版 `PANEL/menubutton.DC6` 帧 0（15×24）。
                // ★ w3 审计换帧名来源：`UiArt.ArrowFrame(0)` = DC6 直出那一套（索引 0 = 透明），
                //   不用 Diablerie 副本（同画面但透明像素被写成不透明黑；该副本文件已由 w4 删除）
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

                _derivedNames[i] = UiArt.Label(transform, "DerivedName" + i, DerivedName[i], 17,
                    TextAnchor.MiddleLeft, UiArt.TextColor,
                    new Vector2(size.x * 0.55f, size.y), center + new Vector2(-size.x * 0.22f, 0f));
                _derivedNames[i].raycastTarget = false;

                // ★ 片 font-scale：补显式字号（唯一出处 `UiLayoutGame.FontPx16`）。
                _derivedValues[i] = D2Label.Create(transform, "DerivedValue" + i, "0", D2Text.D2Font.Font16,
                    TextAnchor.MiddleRight, UiArt.TitleColor,
                    new Vector2(size.x * 0.45f, size.y), center + new Vector2(size.x * 0.27f, 0f),
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

                _extraNames[i] = UiArt.Label(transform, "ExtraName" + i, ExtraName[i], 16,
                    TextAnchor.MiddleLeft, UiArt.TextColor,
                    new Vector2(size.x * 0.55f, size.y), center + new Vector2(-size.x * 0.22f, 0f));
                _extraNames[i].raycastTarget = false;

                // ★ 片 font-scale：补显式字号（唯一出处 `UiLayoutGame.FontPx16`）。
                _extraValues[i] = D2Label.Create(transform, "ExtraValue" + i, "0", D2Text.D2Font.Font16,
                    TextAnchor.MiddleRight, UiArt.TitleColor,
                    new Vector2(size.x * 0.45f, size.y), center + new Vector2(size.x * 0.27f, 0f),
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

                _resistNames[i] = UiArt.Label(transform, "ResistName" + i, ResistName[i], 17,
                    TextAnchor.MiddleLeft, UiArt.TextColor,
                    new Vector2(size.x * 0.66f, size.y), center + new Vector2(-size.x * 0.17f, 0f));
                _resistNames[i].raycastTarget = false;

                // ★ 片 font-scale：补显式字号（唯一出处 `UiLayoutGame.FontPx16`）。
                //   ⚠️ 值列框 = 0.34 × 74.3 ×1.8 = **45.5 画布px**（最窄的一列）⇒ 字高 28 时
                //   换行阈值 = 45.5/scale ≈ 29 原版px（"75%" ≈ 24px，放得下；"100%" 会折行）。
                //   实测（Probe/Play）逐条核 `LineCount`；若出现折行就按"值列不折行"处理（Overflow）。
                _resistValues[i] = D2Label.Create(transform, "ResistValue" + i, "0%", D2Text.D2Font.Font16,
                    TextAnchor.MiddleRight, UiArt.TitleColor,
                    new Vector2(size.x * 0.34f, size.y), center + new Vector2(size.x * 0.33f, 0f),
                    (int)UiLayoutGame.FontPx16);
            }
        }

        /// <summary>
        /// 关闭按钮：命中区按原版矩形；「X」图形**不在本批素材里**（底图只有一个凹槽）
        /// ⇒ 不自己画一个，登记在 `client/资源欠缺清单.md`。
        /// </summary>
        private void BuildCloseButton()
        {
            var close = UiArt.Panel(transform, "CloseButton", UiLayoutGame.CharCloseSize,
                PanelPos + UiLayoutGame.CharClosePos, new Color(1f, 1f, 1f, 0f), true);
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
            _topRightText.text = $"等级 {(stats != null ? stats.level : 0)}    经验 "
                                 + $"{(stats != null ? stats.exp : 0)}/{(stats != null ? stats.expNext : 0)}"
                                 + $"    技能点 {skillPoints}";

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
