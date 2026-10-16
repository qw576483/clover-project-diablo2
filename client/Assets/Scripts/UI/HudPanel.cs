// ─────────────────────────────────────────────────────────────────────────────
// 游戏内 HUD：底部控制面板 = 左右两颗球（左红生命 / 右蓝法力）+ 经验条 + 左右技能格 +
// 腰带 4 格 + 小面板 8 按钮 + 跑/走按钮。
//
// 布局口径 = **按高度等比 ×1.8 + 水平居中**（见 `UI/UiLayoutGame.cs` 的口径说明）：
//   常量集中在 `UI/UiLayoutGame.cs`（每个值都注明「原版值 → ×1.8 居中」与来源 prefab 节点），
//   本文件只消费它、不自己写数字。
//   不按宽度比 ×2.4 等比：948×2.4 = 2275.2 > 1920、600×2.4 = 1440 > 1080 ⇒ 控制面板底边出屏 51px（E5）。
//   底带两个按钮（小面板开合箭头、跑/走）在原版里父容器 `m_IsActive=0` ⇒ 原版不显示；
//   按原值摆会压住第 1/第 5 个技能格 ⇒ 位置见 `UiLayoutGame.HudSubBarArtY` 与 E7。
//
// 依据（原版精确 RectTransform，脚本从 prefab YAML 逐节点解析，非肉眼估）：
//   `_assets_tmp/d2src/Diablerie/Assets/Prefabs/ControlPanel.prefab`
//     Background(948×160, pivot(0.5,0), pos(0,-21.3))
//     Lifebulb(108×108 @ -316.01,66.96)   Manabulb(108×108 @ 299.56,66.96)
//     LeftSkill(33.495×35.12 @ -229.9,35.2, 文本 "Attack")  RightSkill(同尺寸 @ -192.2,35.5)
//     ImageMinipanel(节点 152×26 @ 0,60 / **素材原生 173×26** —— 取后者，见 UiLayoutGame.MiniPanelSize)
//     + 7 子按钮(20×20, x=-63/-42/-21/0/21/42/63, y=0)
//     ExperienceBar(486.94×4.06 @ -8.9,7.77)    ExpBarOverlay(948×160 @ 0,59.1)
//     ImageExpBarLeft(128×104 @ -90,3) 的子 Button(16×20 @ 18,18)  → 跑/走按钮
//   腰带 4 格：原版 `ControlPanel.png` 底图上**已画出的格子**（实测 art x 中心 599.5/630.5/
//     661.5/692.5、pitch 31、格内 27×25、art y 中心 101），正好被 prefab 的
//     `ImageBeltRight`(128×104 @ pos(166,3)) 框住 ⇒ 这是原版腰带格（在画布中线**右侧**、紧邻法力球）。
//
//   · HUD 右上那行「快捷键提示」：原版没有这条，原版的做法是**技能格上的热键标签**
//     （`UiLayoutGame` 右上那格另有用途 —— `CharacterPanel` 已按原版右上空框补上等级/经验）。
//
//   `Events.StageLeft` 关闭；它同时是**游戏内面板的总入口**（小面板 8 按钮 = 各面板入口）。
// 零 `using Diablo2.Module`（分层自检 ③）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>游戏内 HUD（双球 + 经验条 + 技能格 + 腰带 + 小面板）。层：<see cref="UILayer.Normal"/>。</summary>
    public class HudPanel : UIPanel
    {
        // ═════════════════════════════════════════════════════════════════════
        // 布局（**全部转发 `UiLayoutGame`**；本类不再出现裸数字）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>控制面板底图尺寸（原版 948×160 → ×1.8 = 1706.4×288，水平居中含在 1920 内）。</summary>
        public static readonly Vector2 PanelBgSize = UiLayoutGame.HudBgSize;

        /// <summary>控制面板底图中心（原版 pivot(0.5,0)+pos(0,-21.3)+贴底抬升 21.3 ⇒ 底边贴画布底边，中心 y = −396）。</summary>
        public static readonly Vector2 PanelBgPos = UiLayoutGame.HudBgPos;

        /// <summary>经验条轨道尺寸（原版 486.94×4.06 → ×1.8）。</summary>
        public static readonly Vector2 ExpBarSize = UiLayoutGame.ExpBarSize;

        /// <summary>经验条轨道中心（原版 pos(-8.9, 7.77) → ×1.8 居中）。</summary>
        public static readonly Vector2 ExpBarPos = UiLayoutGame.ExpBarPos;

        /// <summary>经验条覆盖层中心（原版 ExpBarOverlay 948×160 @ pos(0,59.1) → ×1.8 居中）。</summary>
        public static readonly Vector2 ExpOverlayPos = UiLayoutGame.ExpOverlayPos;

        /// <summary>球直径（原版 108×108 → ×1.8 = 194.4）。</summary>
        public const float OrbSize = UiLayoutGame.OrbSize;

        /// <summary>生命球中心（原版 pos(-316.01, 66.96)）。</summary>
        public static readonly Vector2 LifeOrbPos = UiLayoutGame.LifeOrbPos;

        /// <summary>法力球中心（原版 pos(299.56, 66.96)）。</summary>
        public static readonly Vector2 ManaOrbPos = UiLayoutGame.ManaOrbPos;

        /// <summary>技能格尺寸（原版 33.495×35.12 → ×1.8 = 60.291×63.216）。</summary>
        public static readonly Vector2 SkillSlotSize = UiLayoutGame.SkillSlotSize;

        /// <summary>左键技能格中心（原版 pos(-229.9, 35.2)）。</summary>
        public static readonly Vector2 LeftSkillPos = UiLayoutGame.LeftSkillPos;

        /// <summary>右键技能格中心（原版 pos(-192.2, 35.5)）。</summary>
        public static readonly Vector2 RightSkillPos = UiLayoutGame.RightSkillPos;

        /// <summary>
        /// 原版底图上那条 6 格带**在本文件已撤出**（无功能证据，见 `UiLayoutGame` 同名注释段）。
        /// 旧版这里有一组 `SkillBarSlots / SkillBarSize / SkillBarCenter / SkillBarSlotX` 透传常量
        /// ⇒ 连同 <c>BuildSkillBar()</c> 一并删除；技能显示/设置改由**技能速度棒**承担
        /// （`BuildSpeedBar()`，键位 = 原版 `S`）。
        /// </summary>

        /// <summary>展开/收起小面板的箭头按钮尺寸（原版 `ImageExpBarRight/Button` 15×24 → ×1.8 = 27×43.2）。</summary>
        public static readonly Vector2 MiniPanelArrowSize = UiLayoutGame.MiniPanelArrowSize;

        /// <summary>
        /// 展开/收起小面板的箭头按钮中心：x = 原版 `ImageExpBarRight` 的子 Button art x 475 → ×1.8 居中；
        /// **y = 格带下方的空白条**（原版该按钮的父容器 `m_IsActive=0` ⇒ 原版不显示，
        /// 按原值摆会压住第 5 个技能格）——见 <see cref="UiLayoutGame.HudSubBarArtY"/> 与 E7。
        /// </summary>
        public static readonly Vector2 MiniPanelArrowPos = UiLayoutGame.MiniPanelArrowPos;

        /// <summary>
        /// 展开/收起小面板的箭头贴图（原版 `PANEL/menubutton.DC6` 帧 0..3，15×24，逐帧文件）：
        /// `[0]` 上箭头常态、`[1]` 上箭头按下、`[2]` 下箭头常态、`[3]` 下箭头按下。
        /// <para>
        /// （素材双份）——
        ///   · `menubutton_{0..3}.png` = 本项目 `tools/d2codec/export_d2ui.py` 从原版
        ///     `data/global/ui/PANEL/menubutton.DC6` 直接解出（**索引 0 = 透明**，即 D2 的口径）；
        ///   · `menubutton__0__{0..3}.png` = 社区复刻工程 `Diablerie/Assets/Images/ControlPanel/`
        ///     的同名副本 —— 实测**同画面**，但把原版的透明像素写成了**不透明黑 (0,0,0,255)**
        ///     （每帧 **38 个像素** —— 逐像素比对两套导出的 alpha 掩码即得）。
        /// 走副本的帧 ⇒ 箭头周围会带一圈**黑点**（原版那里是透出大理石底）⇒ 本工程用 DC6 导出那一套
        /// （同画面、alpha 正确），尺寸/帧序/位置不变。
        /// </para>
        /// <para>`ResPaths.PanelArrowUp/Down*` 四个常量指向 `menubutton_{0..3}`（`Core/ResPaths.cs`）；
        /// 本文件不直接经它们取图；
        /// 路径以 `ResPaths.D2UiPanel` 为唯一前缀来源，**帧名唯一来源 = `UiArt.ArrowFrame`**
        /// （本节与人物属性面板的加点箭头共用它）—— 两处同值，`uicheck` ㉑ 节断言磁盘存在。</para>
        /// </summary>
        public static readonly string[] MiniPanelArrowFrames =
        {
            UiArt.ArrowFrame(0), UiArt.ArrowFrame(1), UiArt.ArrowFrame(2), UiArt.ArrowFrame(3),
        };

        /// <summary>
        /// 小面板底图尺寸（**原版素材原生 173×26** → ×1.8 = 311.4×46.8；不是 prefab 的 152×26 ——
        /// 理由/出处逐条见 `UiLayoutGame.MiniPanelSize`）。
        /// </summary>
        public static readonly Vector2 MiniPanelSize = UiLayoutGame.MiniPanelSize;

        /// <summary>小面板底图中心 y（原版 pos(0,60) → 贴底抬升后 y = −393.66）。</summary>
        public const float MiniPanelY = UiLayoutGame.MiniPanelY;

        /// <summary>小面板 8 个按钮的 x（原版底条 173×26 上 8 钮按 pitch 21 居中 → ×1.8）。</summary>
        public static readonly float[] MiniButtonX = UiLayoutGame.MiniButtonX;

        /// <summary>小面板按钮边长（原版 20 → ×1.8 = 36）。</summary>
        public const float MiniButtonSize = UiLayoutGame.MiniButtonSize;

        // ── 8 个按钮的 hover 文案（**全部来自原版中文串表逐字**，`原版资源/d2text/chi_string.txt`，
        //    格式 `id<TAB>文案`；复跑：`Select-String '^4169\t' 原版资源\d2text\chi_string.txt`）
        //    串表 4169-4175 即「人物/物品/技能樹/隊伍畫面/自動地圖/訊息記錄/任務記錄」本身无后缀；
        //    仅 4176「遊戲選單（Esc）」原版就自带后缀（保留）。热键仍由键盘可达（C/I/T/Tab/Esc，见下方 Info 日志）──

        /// <summary>原版串 **4169**「人物」（逐字，无热键后缀；键位见 `GameKeyAlias.KeyCharSheet`）。</summary>
        public const string MiniTipChar = "人物";

        /// <summary>原版串 **4171**「技能樹」（逐字，无热键后缀；键位见 `GameKeyAlias.KeySkillTree`）。</summary>
        public const string MiniTipSkillTree = "技能樹";

        /// <summary>原版串 **4170**「物品」（逐字，无热键后缀；键位见 `GameKeyAlias.KeyInventory`）。</summary>
        public const string MiniTipInventory = "物品";

        /// <summary>原版串 **4172**「隊伍畫面」；本工程未登记该屏键位 ⇒ 无热键后缀。</summary>
        public const string MiniTipParty = "隊伍畫面";

        /// <summary>原版串 **4173**「自動地圖」（逐字，无热键后缀；键位见 `GameKeyAlias.KeyMinimap`）。</summary>
        public const string MiniTipAutomap = "自動地圖";

        /// <summary>原版串 **4174**「訊息記錄」；本工程未登记该屏键位 ⇒ 无热键后缀。</summary>
        public const string MiniTipMessageLog = "訊息記錄";

        /// <summary>原版串 **4176**「遊戲選單（Esc）」（原文自带热键后缀）；键位 = `GameKeyAlias.KeyPause`。</summary>
        public const string MiniTipMenu = "遊戲選單（Esc）";

        /// <summary>原版串 **4175**「任務記錄」（逐字，无后缀）；本项目未实装该界面 ⇒ 与隊伍畫面/訊息記錄同款 toast。</summary>
        public const string MiniTipQuestLog = "任務記錄";

        /// <summary>
        /// 经验条悬停提示的前缀 = 原版串 **4163**「經驗： %u / %u」的字面部分
        /// （出处 `原版资源/d2text/chi_string.txt`，两个 `%u` = 当前经验 / 下一级所需；
        /// 复跑：`Select-String '^4163\t' 原版资源\d2text\chi_string.txt`）。
        /// <para>原版**确有**这条提示：`ControlPanel.prefab` 的 `TooltipArea` 挂在 `ExperienceBar` 下，
        /// 吃射线、贴图 alpha 0，挂 `Tooltip`（文案字段是占位英文 `experience bar tooltip`）
        /// ⇒ 实装文案取同位置的串 4163。</para>
        /// </summary>
        public const string ExpTipPrefix = "經驗： ";

        public const float BeltCellSize = UiLayoutGame.BeltCellSize;

        public const float BeltCellH = UiLayoutGame.BeltCellH;

        /// <summary>腰带 4 格中心的 x（原版底图实测 125.5/156.5/187.5/218.5 → ×1.8 = 225.9..393.3）。</summary>
        public static readonly float[] BeltCellX = UiLayoutGame.BeltCellX;

        /// <summary>腰带格中心 y（原版底图腰带格 art y 中心 101 ⇒ 贴底抬升后 y = −433.8）。</summary>
        public const float BeltCellY = UiLayoutGame.BeltCellY;

        /// <summary>
        /// 跑/走按钮矩形：x/size = 原版 `ImageExpBarLeft` 的子 Button（16×20 @ art x 338 → ×1.8）；
        /// **y 用格带下方的空白条**（原版父容器 `m_IsActive=0` ⇒ 原版不显示，按原值摆会压住第 1 个技能格）。
        /// </summary>
        public static readonly Vector2 RunButtonPos = UiLayoutGame.RunButtonPos;
        public static readonly Vector2 RunButtonSize = UiLayoutGame.RunButtonSize;

        // ═════════════════════════════════════════════════════════════════════
        // 运行时
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>常驻 HUD 实例数（正常 0 或 1；用于「场景预置」与「UIManager 打开」两种装配都能工作）。</summary>
        private static int _live;

        /// <summary>本实例是否由场景预置（不经 `UIManager`）⇒ 需要自己管显隐。</summary>
        private bool _selfManaged;

        private bool _built;
        private bool _subscribed;

        private Image _lifeFill;
        private Image _manaFill;
        private Image _expFill;
        private D2Label _lifeText;
        private D2Label _manaText;
        private Image _runButton;

        private readonly Image[] _beltCells = new Image[GameConst.BeltSlots];
        private readonly Image[] _beltIcons = new Image[GameConst.BeltSlots];

        /// <summary>
        /// 技能速度棒（原版 `S`）的**技能格图标层**（下标 → 图标；条数按内容变，故用 List）。
        /// <para>⚠️ 口径纠正：原版底图上那条 6 格带（`ControlPanel.png` art x 316..543、
        /// pitch 38）**没有任何原版功能证据**（串表的 HUD 悬停标签里没有它、原版「选项 → 指令」里没有它、
        /// 官方手册的 Interface Bar 元素表里也没有它）⇒ 本工程**不再给它挂自造语义**
        /// （旧版把它当"6 格技能栏 = F1~F6，点格子=按 F 键"，那是参考工程 Diablerie 的自定口径、原版没有）。
        /// 技能显示改由**技能速度棒**承担（原版确有：串表 3935「技能速度棒」+ `S` 键）。</para>
        /// </summary>
        private readonly List<Image> _speedBarIcons = new List<Image>();

        /// <summary>速度棒里每个技能格的**热键标签**（`F1`~`F8`；该技能没被设过热键 ⇒ 空）。</summary>
        private readonly List<D2Label> _speedBarHotkeyLabels = new List<D2Label>();

        /// <summary>速度棒每个技能格对应的技能 id（与 <see cref="_speedBarIcons"/> 同序；`-1` = 普通攻击）。</summary>
        private readonly List<int> _speedBarSkillIds = new List<int>();

        /// <summary>速度棒每个技能格的命中矩形（屏幕点包含测试用；见 `SkillSlotHit` 的同款理由）。</summary>
        private readonly List<RectTransform> _speedBarCellRects = new List<RectTransform>();

        /// <summary>速度棒根节点（**首次打开才建**；见 <see cref="BuildSpeedBar"/>）。</summary>
        private RectTransform _speedBarRoot;

        /// <summary>速度棒里悬停技能的名字/热键行（原版用 tooltip；本项目用一行字，口径见 `BuildSpeedBar`）。</summary>
        private D2Label _speedBarInfo;

        /// <summary>速度棒当前是否打开（原版 `S` 开关；开着时 `F1`~`F8` = **设置**热键）。</summary>
        private bool _speedBarOpen;

        /// <summary>速度棒本次打开时点的是哪只手（`-1` = `S` 打开、没指定手；0 = 左键；1 = 右键）。</summary>
        private int _speedBarButton = -1;

        /// <summary>指针当前停在速度棒的第几格（-1 = 没停在任何格上）。</summary>
        private int _speedBarHoverCell = -1;

        /// <summary>最近一次收到的技能热键表（自证/断言用；也用于给速度棒贴 `F1`~`F8` 标签）。</summary>
        internal SkillHotkeysArgs SkillHotkeys { get; private set; }

        /// <summary>
        /// 左右键技能格的图标层：由 `OnSkillButtonsChanged`
        /// 换成当前绑定的技能图标；未绑（-1）时回退普通攻击图标（原版左右键默认都是 Attack）。
        /// </summary>
        private Image _leftSkillIcon;
        private Image _rightSkillIcon;

        /// <summary>地面物品名牌层（见 `UI/GroundItemLabelView.cs`）。</summary>
        private GroundItemLabelView _groundLabels;

        /// <summary>最近一次收到的左右键技能格绑定快照（自证/断言用）。</summary>
        internal SkillButtonsArgs SkillButtons { get; private set; }

        /// <summary>最近一次收到的地面物品名牌载荷（自证/断言用）。</summary>
        internal GroundItemLabelsArgs GroundItemLabels { get; private set; }

        /// <summary>小面板开关箭头（原版 `ImageExpBarRight` 的子 Button，本项目按其底图空档放置）。</summary>
        private Image _miniToggle;

        /// <summary>箭头贴图的"版本号"：连点两次时用来丢弃过期的异步贴图回调。</summary>
        private int _miniToggleRev;

        /// <summary>小面板底图（原版 `ImageMinipanel`）。</summary>
        private Image _miniPanelBg;

        /// <summary>小面板 8 个按钮（展开/收起时一起显隐）。</summary>
        private readonly Transform[] _miniButtons = new Transform[UiLayoutGame.MiniButtonCount];

        /// <summary>小面板 8 个按钮的悬停提示（文案 = <see cref="MiniTipChar"/> 那一族原版串）。</summary>
        private readonly ControlTip[] _miniTips = new ControlTip[UiLayoutGame.MiniButtonCount];

        /// <summary>经验条的悬停提示（文案 = 原版串 4163，值随 `Events.HudDirty` 刷新）。</summary>
        private ControlTip _expTip;

        /// <summary>小面板是否展开（原版靠箭头按钮 `ShowNavigationalBar` 切换；原版默认**收起**）。</summary>
        private bool _miniOpen;

        private readonly string[] _beltIconPath = new string[GameConst.BeltSlots];
        private readonly D2Label[] _beltTexts = new D2Label[GameConst.BeltSlots];

        // 最近一次收到的快照（用于「打开子面板时把已有数据带上」，避免刚打开是空的）
        private PlayerStatsDto _stats;
        private InventoryChangedArgs _inventory;
        private SkillTreeArgs _tree;

        /// <summary>
        /// 最近一次 `Events.MapGenerated` 的小地图快照。
        /// **必须缓存并传给 `MiniMapPanel`**：该面板在 `OnOpen` 里才订阅 `MapGenerated`，
        /// </summary>
        private MinimapArgs _minimap;

        /// <summary>
        /// **区域名弹出**（原版 `LevelEntryTitle`，见 `UI/LevelEntryTitle.cs`）。
        /// <para>触发口径 = **首次进入某区域**（本局内每个区域弹一次）。两条触发路径：</para>
        /// <list type="bullet">
        ///   <item>`Events.MapGenerated`（参数 `MinimapArgs.areaId`）——**进图**那次由
        ///     `AppSnapshots.Broadcast` 在 HUD 打开**之后**补发 ⇒ 覆盖"开局第一次进入某区域"。
        ///     —— `Module/Map/MapModule.cs:581`（**生成地图时**，即**进图那次也会发**）
        ///     与 `Module/Flow/AppFlow.cs:1569`（换区第二拍）⇒ 一次换区消费方会收到**两次**
        ///     （`waypoint` 的 L3 实机实测：`AREA-CHANGED area=0 frame=75` 就是进图那一次）。
        ///     ⇒ **消费方必须幂等**（本类这一路靠 `UI/LevelEntryTitle.cs` 的"本局首次去重"）；
        ///     `waypoint` 已把 4 个消费方逐个核过：`AppWaypoint` HashSet 幂等 / `LevelEntryTitle`
        ///     本局首次去重 / `AudioModule.Bgm` 同键 return / `MonsterModule` 0 只
        ///     —— **均无实害**。（原文"进图那次不发"与实机不符，故删。）</item>
        ///   <item>`Events.AreaChanged`（参数 `Def.AreaId`）——**过门换区**那次。</item>
        /// </list>
        /// <para>去重与淡入/停留/淡出都在 `UI/LevelEntryTitle.cs`（本类只触发 + 逐帧驱动）。</para>
        /// </summary>
        private LevelEntryTitle _levelTitle;

        /// <summary>跑/走开关（原版 R 键）——纯表现，开关只切贴图。</summary>
        private bool _running = true;

        /// <inheritdoc/>
        public override UILayer Layer => UILayer.Normal;

        private void Awake()
        {
            _live++;
        }

        private void OnDestroy()
        {
            _live = Mathf.Max(0, _live - 1);
            Unsubscribe();
        }

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            // UIManager.Open 会先把本实例登记进窗口栈再回调 OnOpen（Runtime/Presentation/UI.cs:130-138）
            // ⇒ 这里 IsOpen 为 true 表示「我是被 UIManager 打开的」；false = 场景预置实例。
            _selfManaged = !Game.UI.IsOpen<HudPanel>();

            UiArt.PrepareRoot(Root);
            Build();
            Subscribe();

            var stats = UiLog.Require<PlayerStatsDto>(param, nameof(HudPanel));
            if (stats != null) _stats = stats;
            Refresh(_stats);

            UiLog.Info($"HUD 已打开（装配方式={(_selfManaged ? "场景预置" : "UIManager")}，"
                       + $"参数={(stats != null ? "有 PlayerStatsDto" : "无 ⇒ 先按 0 显示")}，"
                       + $"布局=原版 ControlPanel.prefab ×{UiLayoutGame.K}）");
        }

        /// <inheritdoc/>
        public override void OnClose()
        {
            Unsubscribe();
            CloseSpeedBar();                // ★ 速度棒随 HUD 一起收（否则下次开 HUD 会带着"开着"的旧状态）
            UiBar.Forget(_lifeFill);
            UiBar.Forget(_manaFill);
            UiBar.Forget(_expFill);
            _groundLabels?.Destroy();       // ★ 名牌层随 HUD 一起拆（节点不跨局保留）
            _groundLabels = null;
            HideMiniTips();
            _expTip?.Hide();
            SkillSlotHit.Clear();           // 拖拽落格的登记随 HUD 一起失效（免留野 RectTransform）
            UiLog.Info("HUD 已关闭");
        }

        /// <inheritdoc/>
        public override void OnUpdate(float dt)
        {
            // 区域名的淡入/停留/淡出**先推**（它是纯表现，不该受输入后端的可用性影响；
            //   下面 `Game.Input` 为空时本方法会提前 return，放在后面就会让区域名卡住不淡出）。
            _levelTitle?.Tick(dt);

            if (Game.Input == null) return;
            if (!Game.Input.Available)
            {
                UiLog.WarnOnce("hud.input.unavailable",
                    "Game.Input 不可用（旧输入后端未初始化？）⇒ HUD 快捷键不响应（鼠标仍可点按钮）");
                return;
            }

            PollHotkey(GameKeyAlias.KeyInventory, nameof(InventoryPanel));
            PollHotkey(GameKeyAlias.KeyCharSheet, nameof(CharacterPanel));
            PollHotkey(GameKeyAlias.KeySkillTree, nameof(SkillTreePanel));
            PollHotkey(GameKeyAlias.KeyMinimap, nameof(MiniMapPanel));
            PollHotkey(GameKeyAlias.KeyRunToggle, null);

            // ★ 技能速度棒（原版 `S`，键位唯一来源 = `GameKeyAlias.KeySkillSpeedBar`；见 `BuildSpeedBar`）：
            //   开/关切；并每帧更新"指针停在哪一格"（技能模块靠它决定 F1~F8 是"设"还是"切"）。
            if (Game.Input.GetKeyDown(GameKeyAlias.KeySkillSpeedBar))
            {
                if (_speedBarOpen)
                {
                    UiLog.Info($"{GameKeyAlias.KeySkillSpeedBar} 按下 ⇒ 收起技能速度棒");
                    CloseSpeedBar();
                }
                else
                {
                    UiLog.Info($"{GameKeyAlias.KeySkillSpeedBar} 按下 ⇒ 打开技能速度棒（原版键位：串表 3935「技能速度棒」）");
                    OpenSpeedBar(-1);
                }
            }

            //   `Esc` 关速度棒。原版 ESC 是"返回上一级"（本工程 = `AppFlow` 的暂停/游戏菜单，键位同
            //   `GameKeyAlias.KeyPause`）⇒ 两条各自生效、互不冲突：速度棒先收，游戏菜单照原版弹
            //   （见 `Module/Flow/AppFlow.cs` 的 `EscPressed`）。
            if (_speedBarOpen && Game.Input.GetKeyDown(GameKeyAlias.KeyPause))
            {
                UiLog.Info("Esc 按下 ⇒ 收起技能速度棒（同帧 `AppFlow` 仍走原版的游戏菜单）");
                CloseSpeedBar();
            }

            PollSpeedBarHover();
        }

        /// <summary>快捷键 → `Events.PanelToggleRequest`（原版键位见 `Def/GameKeyAlias.cs`）。</summary>
        private void PollHotkey(GameKey key, string panelName)
        {
            if (!Game.Input.GetKeyDown(key)) return;

            if (panelName == null)
            {
                _running = !_running;      // 跑/走切换：原版只影响移速，本项目仅切贴图
                ApplyRunButton();
                UiLog.Info($"跑/走切换：{(_running ? "跑" : "走")}（移速切换需 Input/Player 模块接线，见回报「未接线」）");
                return;
            }

            Game.Event.Emit(Events.PanelToggleRequest, panelName);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 构件
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 构件（顺序 = **原版 `ControlPanel.prefab` 的兄弟顺序**；uGUI 里后者画在越上层）。
        /// <para>
        /// 原版顺序：Background → LeftSkill/RightSkill → Lifebulb/Manabulb → ImageExpBarLeft(跑/走)
        /// → ImageExpBarRight(小面板开关) → ImageBeltRight(腰带) → ImageMinipanel(8 键)
        /// → ExperienceBar → **ExpBarOverlay（最后一个 ⇒ 覆盖在上层）**。
        /// </para>
        /// <para>
        /// `SkillPanel`（6 格技能栏）**已删**（原版底图上那条 6 格带无功能证据，见 `UiLayoutGame`
        /// 同名注释段）⇒ 本工程不再有这一层；技能显示/设置走**技能速度棒**（原版 `S`），
        /// 它摆在控制面板**上沿之上**（飘在游戏画面上）⇒ 建在 `BuildExpBar` 之后、区域名之前，
        /// 保证它压在底栏之上而不被经验条覆盖。
        /// </para>
        /// </summary>
        private void Build()
        {
            if (_built) return;
            _built = true;

            BuildControlPanel();        // 1  Background
            BuildSkillSlots();          // 2  LeftSkill / RightSkill
            BuildOrbs();                // 3  Lifebulb / Manabulb
            BuildRunButton();           // 4  ImageExpBarLeft 的子 Button（跑/走）
            BuildMiniPanelToggle();     // 5  ImageExpBarRight 的子 Button（小面板开关）
            BuildBelt();                // 6  ImageBeltRight 框住的 4 格
            BuildMiniPanel();           // 7  ImageMinipanel + 8 键
            BuildExpBar();              // 8  ExperienceBar + ExpBarOverlay
            BuildSpeedBar();            // 9  ★ 技能速度棒（原版 `S`；飘在控制面板上沿之上 ⇒ 建在底栏之后）
            BuildLevelEntryTitle();     // 10 ★ 区域名（原版 LevelEntryTitle；建在最后 ⇒ 盖在 HUD 之上）

            // 11 常驻品牌署名 `by clover-engine`（居底居中）。**最后建** ⇒ 画在 HUD 之上、不被压住。
            //    y 用 `ByLineHudPos`：HUD 底栏内部无空带（球/技能格/腰带/经验条占满）⇒ 贴底栏上沿之上。
            UiLayoutFlow.Brand.Attach(transform, UiLayoutFlow.Brand.ByLineHudPos.y);
        }

        /// <summary>
        /// 区域名弹出控件（原版 `LevelEntryTitle.prefab`）。**必须最后建**：uGUI 里后建的兄弟画在上层，
        /// 区域名要盖住整个 HUD（原版它也是画在控制面板之上的）。
        /// </summary>
        private void BuildLevelEntryTitle()
        {
            _levelTitle = LevelEntryTitle.Create(transform, nameof(HudPanel));
        }

        private void BuildControlPanel()
        {
            // 占位色 = `UiArt.PanelBg`（深色）：贴图在途 / 缺失时面板不该是一整块刺眼白
            // （贴图到位后由 `SetSprite` 把色调覆写回白）。
            var bg = UiArt.Panel(transform, "ControlPanel", PanelBgSize, PanelBgPos, UiArt.PanelBg, false);
            UiArt.SetSprite(bg, ResPaths.D2UiPanel + "ControlPanel");
        }

        private void BuildOrbs()
        {
            // 原版 `Lifebulb` / `Manabulb` **只有三层**：
            //   `HealthBar`/`ManaAnimation`（Filled 填充）+ `*BulbOverlay`（玻璃高光）+ `*Label`（球内数字）。
            //   球体本身是**底图 `ControlPanel.png` 画好的** —— 原版**没有**深色底球节点。
            _lifeFill = BuildOrb("LifeOrb", LifeOrbPos, ResPaths.PanelHealthBar, 0, out var lifeHolder);
            _manaFill = BuildOrb("ManaOrb", ManaOrbPos, ResPaths.PanelManaBar, 1, out var manaHolder);

            // 原版 `HealthLabel`/`ManaLabel`：**球内**子节点、铺满球宽、高 8px、y 偏 +4、居中；
            // 文本格式照原版自己的写法「Life: 123/123」（见 ControlPanel.prefab 的 m_Text）。
            // 挂在球容器**之内**、且在填充/高光之后 ⇒ 数字画在球的最上层（与 prefab 子节点顺序一致）。
            //
            //   字号**唯一出处** = `UiLayoutGame.FontPx16`（原版行距 16 × K 1.8 = **28.8**，取整 28；
            //   见 `UiLayoutGame` 的「全 UI 字号唯一出处」一节）；本文件不另写字号字面量。
            //   判据：`uicheck` 的 FontScaleCheck（本处 9th 实参 = `(int)UiLayoutGame.FontPx16`）。
            //   是自创文案，1:1 硬标准下不允许）。字号/位置/外框见 `UiLayoutGame.OrbLabelSize`
            //   与 `OrbLabelOffsetY`（都按基线图逐像素重定过）。
            _lifeText = D2Label.Create(lifeHolder, "LifeText", "生命： 0 / 0", D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, new Color(0.98f, 0.93f, 0.90f, 1f),
                UiLayoutGame.OrbLabelSize, new Vector2(0f, UiLayoutGame.OrbLabelOffsetY),
                (int)UiLayoutGame.FontPx16);
            _manaText = D2Label.Create(manaHolder, "ManaText", "法力： 0 / 0", D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, new Color(0.86f, 0.91f, 0.99f, 1f),
                UiLayoutGame.OrbLabelSize, new Vector2(0f, UiLayoutGame.OrbLabelOffsetY),
                (int)UiLayoutGame.FontPx16);

            if (_lifeText == null || _manaText == null)
                UiLog.WarnOnce("hud.orblabel", "球内数字标签未建出来（D2Label.Create 返回 null）⇒ 球上没有数字");
        }

        /// <summary>
        /// 造一颗球：**填充球**（原版 `HealthBar`/`ManaAnimation` = Filled + Vertical + fillOrigin=Bottom）
        /// + 玻璃高光。两层都在**同尺寸容器**（`{name}Holder`）里：`UiBar` 的锚点降级模式以父节点为基准。
        /// <para>不铺"深色底球"——原版没有这个节点，底图自带球体（见 <see cref="BuildOrbs"/> 的说明）。</para>
        /// </summary>
        /// <param name="holder">球容器（球内数字标签挂在它下面）。</param>
        private Image BuildOrb(string name, Vector2 pos, string spritePath, int overlayFrame,
            out RectTransform holder)
        {
            holder = UIFactory.CreateCentered(name + "Holder", transform, new Vector2(OrbSize, OrbSize), pos);

            var fill = UiArt.Panel(holder, name, new Vector2(OrbSize, OrbSize), Vector2.zero, Color.white, false);
            // 贴图交给 UiBar：它保证「有 sprite 才 Filled」（constraints.md #3）
            UiBar.Set(fill, spritePath, 1f, horizontal: false);

            BuildOrbOverlay(holder, name + "BulbOverlay", overlayFrame);
            return fill;
        }

        /// <summary>
        /// 原版 `HealthBulbOverlay` / `ManaBulbOverlay`：同一张 `overlap.png` 的两帧（玻璃反光），
        /// 节点铺满球（prefab 里 anchors 0..1、sizeDelta 0）⇒ 画在填充球之上、尺寸 = 球尺寸 ×1.8。
        /// </summary>
        private void BuildOrbOverlay(RectTransform holder, string name, int frame)
        {
            var ov = UiArt.Panel(holder, name, new Vector2(OrbSize, OrbSize), Vector2.zero, Color.white, false);
            UiArt.RequestStrip(ResPaths.PanelOverlap, ResPaths.FrameCountOverlap, frames =>
            {
                if (ov == null) return;
                var sp = frames != null && frame < frames.Length ? frames[frame] : null;
                if (sp == null)
                {
                    UiLog.Warn($"原版球高光遮罩缺失：{ResPaths.PanelOverlap} 帧 {frame}"
                               + "（球只剩液体填充，登记在 素材欠缺登记）");
                    return;
                }
                ov.sprite = sp;
                ov.color = Color.white;
                UiArt.EnsureUnlit(ov);
            });
        }

        private void BuildExpBar()
        {
            // 原版 `ExperienceBar` 节点**没有 Image**：未填充段透出底图自己的经验条凹槽。
            // 故 track 用**全透明**（alpha 0），只作填充块的父容器；铺不透明底色会盖掉原版凹槽。
            var track = UiArt.Panel(transform, "ExpBarTrack", ExpBarSize, ExpBarPos,
                new Color(0.10f, 0.09f, 0.08f, 0f), false);

            var fill = UiArt.Panel(track.transform, "ExpBarFill", UiLayoutGame.ExpFillerSize, Vector2.zero,
                new Color(0.95f, 0.85f, 0.35f, 1f), false);
            _expFill = fill;
            UiBar.Set(fill, ResPaths.D2UiPanel + "ExperienceBar", 0f, horizontal: true);

            // 原版 ExpBarOverlay（948×160 → ×1.8 = 1706.4×288）画在填充条之上，只留中间一条透明通道
            var overlay = UiArt.Panel(transform, "ExpBarOverlay", PanelBgSize, ExpOverlayPos, Color.white, false);
            UiArt.SetSprite(overlay, ResPaths.D2UiPanel + "ExperienceBarOverlay");

            // 经验条悬停区（原版 `ControlPanel.prefab` 的 `TooltipArea`：挂在 `ExperienceBar` 下、
            // 吃射线、贴图 alpha 0、anchors 0..1 + sizeDelta (4,11) ⇒ 比经验条本体四周各放出一点）。
            // 本项目建成"居中定尺"节点（`ControlTip` 的锚要求），矩形等价 = 本体尺寸 + (4,11) 原版px。
            // 文案 = 原版串 4163（见 `ExpTipPrefix`），值在 `Refresh` 里随 `PlayerStatsDto` 更新。
            var tipArea = UiArt.Panel(transform, "ExpBarTipArea",
                ExpBarSize + UiLayoutGame.ExpBarTipPad * UiLayoutGame.K, ExpBarPos,
                new Color(1f, 1f, 1f, 0f), true);
            _expTip = ControlTip.Create(transform, tipArea.rectTransform, ExpTipText(null));
            var expHover = tipArea.gameObject.AddComponent<HoverTarget>();
            if (_expTip != null)
            {
                expHover.OnEnter = _expTip.Show;
                expHover.OnExit = _expTip.Hide;
            }
        }

        /// <summary>
        /// 经验条悬停文案 = 原版串 4163「經驗： %u / %u」的两个占位（当前经验 / 下一级所需）。
        /// 纯函数（离线宿主可逐行断言）；快照缺失 ⇒ 两值按 0。
        /// </summary>
        public static string ExpTipText(PlayerStatsDto stats)
            => ExpTipPrefix + (stats != null ? stats.exp : 0) + " / " + (stats != null ? stats.expNext : 0);

        private void BuildSkillSlots()
        {
            // 原版 `ControlPanel.prefab` 的 `LeftSkill`/`RightSkill` 各有一个子标签，**文本就是 "L" / "R"**
            // 两格的 Image 要留着 ⇒ 收到 `Events.SkillButtonsChanged` 时换图标。
            //   **点击**这两格 = 原版「点 Action Icon 弹出技能图标列表」（手册 Interface Bar 段）⇒ 本项目
            //   打开**技能速度棒**并把目标手设成这一格；参数 0 = 左键 / 1 = 右键。
            _leftSkillIcon = BuildSkillSlot("LeftSkill", LeftSkillPos, "L", ResPaths.SkillIconAttack, 0);
            _rightSkillIcon = BuildSkillSlot("RightSkill", RightSkillPos, "R", ResPaths.SkillIconAttack, 1);

            // 登记两格的矩形给「技能拖拽落格」用（`SkillTreePanel.OnEndDrag` 问 `SkillSlotHit.SlotAt`；
            //   为什么不用 uGUI 的 IDropHandler 见 `SkillSlotHit` 的文件头）。
            SkillSlotHit.Register(_leftSkillIcon != null ? _leftSkillIcon.rectTransform : null,
                _rightSkillIcon != null ? _rightSkillIcon.rectTransform : null);
        }

        /// <summary>
        /// 左右键技能格换图标（原版：两个技能格显示**当前绑定的技能**的图标；未绑 = 普通攻击图标）。
        /// 收方 = `Events.SkillButtonsChanged`。
        /// </summary>
        private void ApplySkillButtons(SkillButtonsArgs args)
        {
            SkillButtons = args;
            if (args == null) return;

            ApplySkillSlotIcon(_leftSkillIcon, args.leftId);
            ApplySkillSlotIcon(_rightSkillIcon, args.rightId);

            UiLog.Info($"[HUD] 左右键技能格已更新：左={args.leftName}({args.leftId}) 右={args.rightName}({args.rightId})"
                + "（图标来自原版 skill 图标表；两格无文字位置，故原版亦只用图标表达）");
        }

        /// <summary>把某技能格的图标换成该技能的图标；id &lt; 0（或取不到图标）⇒ 回退普通攻击图标。</summary>
        private static void ApplySkillSlotIcon(Image slot, int skillId)
        {
            if (slot == null) return;

            var path = skillId >= 0 ? D2Icon.SkillIconPath(skillId, false) : null;
            if (string.IsNullOrEmpty(path))
            {
                // 非预期但可解释：未绑定（-1）或该技能取不到原版图标 ⇒ 回退普通攻击图标（原版默认值）
                if (skillId >= 0)
                {
                    UiLog.WarnOnce("hud.skillicon." + skillId,
                        $"技能 #{skillId} 取不到原版图标（配表缺行 / 资源缺）⇒ 技能格回退普通攻击图标");
                }
                path = ResPaths.SkillIconAttack;
            }

            UiArt.SetSprite(slot, path);
        }

        /// <summary>
        /// 技能速度棒（原版 `S`）—— **设置技能热键的唯一入口**，也是原版点左右键技能格时"弹出的技能列表"。
        /// <para>
        /// 原版口径（三条出处）：① 官方中文字串表 `原版资源/d2text/chi_string.txt:3935`「技能速度棒」
        /// （原版「选项 → 指令」里的键位命令，与「技能樹」= `T` 同表）；② GameFAQs 原版 FAQ
        /// 「Basic Commands: S - Opens the Skills Speed Bar」；③ 凯恩之角
        /// `wiki.d.163.com/In_Game_control_(Diablo_II)`「打开技能选项，把鼠标移动到你要设置的技能的图标上，
        /// 然后按 "F1" 到 "F8" 键中的任何一个」+ 官方手册「Left-clicking on an Action Icon displays a
        /// pop-up menu of skill icons. Click on a skill icon to assign it to that mouse button.」。
        /// </para>
        /// <para>
        /// 三个交互（对应上面三条出处）：
        /// <list type="bullet">
        /// <item>**悬停技能图标 + 按 `F1`~`F8`** ⇒ 设热键（判定在技能模块：它收到本面板发的
        /// `Events.SkillSpeedBarChanged` 才知道"速度棒开着 + 指针停在这个技能上"）；</item>
        /// <item>**点技能图标** ⇒ 装到**目标那只手**（`S` 打开时没有目标手 ⇒ 默认右键，与原版
        /// "readied for use（右键施放）"一致）；</item>
        /// <item>**`S` / `Esc` / 再点同一只手的技能格 / 点完技能图标** ⇒ 关。</item>
        /// </list>
        /// </para>
        /// <para>
        /// 位置/尺寸：原版没有留下这条面板的 prefab（`ControlPanel.prefab` 里没有技能栏节点，
        /// `d2dc6` 的 `PANEL/` 下也没有它的素材）⇒ **本项目取"界面上方一行图标"**（Diablo I 手册对
        /// Speedbook 的同一件东西的原话：「A row of icons indicating the spells and skills that your
        /// character is capable of using will appear in the Play Area, just above the Interface Bar」）
        /// ⇒ 摆在控制面板上沿之上、水平居中；技能多时按屏宽**换行**（原版是一行，本项目 31 个技能一行放不下
        /// ⇒ 这一条）。格子尺寸复用原版技能格 33.495×35.12 → ×1.8。
        /// </para>
        /// <para>内容 = 「普通攻击」+ 本职业已学且能装鼠标键的技能（来自 `Events.SkillTreeChanged` 的
        /// `SkickTreeArgs`，即 `OnSkillTreeChanged` 缓存的那份）。</para>
        /// </summary>
        internal void BuildSpeedBar()
        {
            if (_speedBarRoot != null) return;

            // 先建一个 1×1 的空底板（尺寸在 `RebuildSpeedBarCells` 里按内容定）
            var root = UiArt.Panel(transform, "SkillSpeedBar", Vector2.one, Vector2.zero,
                UiLayoutGame.SpeedBarBackColor, true);
            _speedBarRoot = root.rectTransform;
            _speedBarRoot.anchorMin = new Vector2(0.5f, 0f);
            _speedBarRoot.anchorMax = new Vector2(0.5f, 0f);
            _speedBarRoot.pivot = new Vector2(0.5f, 0f);
            _speedBarRoot.anchoredPosition = new Vector2(0f, UiLayoutGame.SpeedBarBottomY);

            _speedBarInfo = D2Label.Create(_speedBarRoot, "SpeedBarInfo", string.Empty, D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, Color.white,
                new Vector2(400f, UiLayoutGame.SpeedBarInfoLineH), Vector2.zero, (int)UiLayoutGame.FontPx16);

            RebuildSpeedBarCells();
            _speedBarRoot.gameObject.SetActive(false);
            UiLog.Info("[HUD] 技能速度棒已建（原版 S）：悬停技能图标 + 按 F1~F8 = 设热键；点图标 = 装到该手技能格");
        }

        /// <summary>
        /// 按当前内容重建速度棒的格子（技能列表变化 / 首次打开时调）。
        /// <para>格子 = 图标 + 热键标签；顺序 = 「普通攻击」+ 已学且非被动的技能（技能树 tree→reqLevel→id）。</para>
        /// </summary>
        private void RebuildSpeedBarCells()
        {
            if (_speedBarRoot == null) return;

            for (var i = 0; i < _speedBarIcons.Count; i++)
            {
                if (_speedBarIcons[i] != null) UnityEngine.Object.Destroy(_speedBarIcons[i].gameObject);
            }
            _speedBarIcons.Clear();
            _speedBarHotkeyLabels.Clear();
            _speedBarSkillIds.Clear();
            _speedBarCellRects.Clear();
            _speedBarHoverCell = -1;

            // 技能 id 列表：第 0 项 = 普通攻击（原版列表第一项就是 Attack），其后 = 已学可绑技能。
            //   ⚠️ 口径纠正：**被动技能也列**（旧版这里 `def.passive ⇒ continue` 是自定口径）。
            //   据 = 官方 `skills.txt` 的 `leftskill` 列：法师 4 个被动（Warmth #37 / Fire Mastery #61 /
            //   Lightning Mastery #63 / Cold Mastery #65）**全是 1**（可装左键），官方也没有"被动不能装鼠标"
            //   的规则 ⇒ 原版速度棒里它们与别的技能同列。绑上后点它不产生施放（`SkillModule.TryCast` 的
            //   `cast.passive` 分支拒绝）—— 原版点击也正是"没反应"。
            var ids = new List<int> { -1 };
            var tree = _tree;
            if (tree != null && tree.skills != null)
            {
                for (var i = 0; i < tree.skills.Count; i++)
                {
                    var def = tree.skills[i];
                    if (def == null) continue;
                    var lv = i < tree.learnedLevels.Count ? tree.learnedLevels[i] : 0;
                    if (lv <= 0) continue;
                    ids.Add(def.id);
                }
            }

            var perRow = UiLayoutGame.SpeedBarPerRow;
            var rows = (ids.Count + perRow - 1) / perRow;
            if (rows < 1) rows = 1;

            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                var col = i % perRow;
                var row = rows - 1 - i / perRow;      // 第 0 行在**最下**（往上数）
                // 占位色 = `UiArt.PanelBg`（深色）：图标取不到路径被 SetActive(false) 时
                // 不该露出刺眼白格（贴图到位后由 `SetSprite` 把色调覆写回白）。
                var cell = UiArt.Panel(_speedBarRoot, "SpeedCell" + i, SkillSlotSize,
                    UiLayoutGame.SpeedBarCellPos(col, row, perRow, rows), UiArt.PanelBg, true);

                var icon = UiArt.Panel(cell.transform, "Icon", SkillSlotSize, Vector2.zero, Color.white, false);
                // 原版图标 48×48 贴进非正方框（60.29×63.216）⇒ 保比例避免被拉变形约 5%。
                icon.preserveAspect = true;
                var path = D2Icon.SkillIconPath(id, false);
                if (!string.IsNullOrEmpty(path)) UiArt.SetSprite(icon, path);
                else if (id >= 0)
                {
                    // 非预期分支：已学但取不到原版图标（配表缺行 / 资源缺）⇒ 该格留空并告警
                    UiLog.WarnOnce("hud.speedbaricon." + id,
                        $"技能速度棒：技能 #{id} 取不到原版图标（配表缺行 / 资源缺）⇒ 该格留空");
                    icon.gameObject.SetActive(false);
                }
                else UiArt.SetSprite(icon, ResPaths.SkillIconAttack);

                // 点击 = 装到目标那只手（`S` 打开 ⇒ 没有目标手 ⇒ 默认**右键**，
                //   与原版「点技能图标 → 该技能被 ready 给右键施放」同口径：Diablo I 手册 Speedbook 段
                //   + 凯恩之角「左键点击该图标选中它，你就可以通过鼠标右键来施放这种技能了」）
                var button = _speedBarButton < 0 ? 1 : _speedBarButton;
                var skillId = id;
                var btn = cell.gameObject.AddComponent<Button>();
                btn.targetGraphic = cell;
                btn.onClick.AddListener(() =>
                {
                    UiLog.Info($"技能速度棒：点技能 #{skillId} ⇒ 发 {Events.SkillSlotBindRequest}"
                        + $"（装到{(button == 0 ? "左键" : "右键")}技能格）");
                    Game.Event.Emit(Events.UiClick);
                    Game.Event.Emit(Events.SkillSlotBindRequest, button | (skillId << 1));
                    CloseSpeedBar();
                });

                var label = AddHotkeyLabel(cell.transform, string.Empty, "SpeedCell" + i);
                _speedBarIcons.Add(icon);
                _speedBarHotkeyLabels.Add(label);
                _speedBarCellRects.Add(cell.rectTransform);
                _speedBarSkillIds.Add(id);
            }

            // 底板尺寸 + 上方那行字（技能名 / 热键）的位置
            var size = UiLayoutGame.SpeedBarSize(perRow, rows);
            _speedBarRoot.sizeDelta = size;
            if (_speedBarInfo != null)
            {
                var rt = _speedBarInfo.rectTransform;
                rt.sizeDelta = new Vector2(size.x, UiLayoutGame.SpeedBarInfoLineH);
                rt.anchoredPosition = new Vector2(0f, size.y * 0.5f + UiLayoutGame.SpeedBarInfoLineH * 0.5f);
                _speedBarInfo.size = rt.sizeDelta;      // 排版框也要跟着改（否则字按旧框宽折行）
                _speedBarInfo.text = string.Empty;
            }

            ApplyHotkeyLabelsToSpeedBar();
            UiLog.Info($"[HUD] 技能速度棒内容：{ids.Count} 格（普通攻击 + 已学技能 {ids.Count - 1} 个；"
                + $"每行 {perRow} 格，共 {rows} 行）");
        }

        /// <summary>
        /// 速度棒里某个技能格的上方那行字（悬停时显示技能名 + 已设的热键）。
        /// 原版把这类信息放 tooltip 里；本项目速度棒是常驻层、没有 tooltip 通道 ⇒ 用一行字
        /// </summary>
        private void UpdateSpeedBarInfo()
        {
            if (_speedBarInfo == null) return;

            if (_speedBarHoverCell < 0 || _speedBarHoverCell >= _speedBarSkillIds.Count)
            {
                _speedBarInfo.text = _speedBarButton < 0
                    ? "技能速度棒：点技能图标 = 装到右键；悬停 + 按 F1~F8 = 设热键"
                    : "技能速度棒：点技能图标 = 装到" + (_speedBarButton == 0 ? "左键" : "右键")
                      + "；悬停 + 按 F1~F8 = 设热键";
                return;
            }

            var id = _speedBarSkillIds[_speedBarHoverCell];
            var name = id == -1 ? "普通攻击" : NameOfSkill(id);
            var hotkey = string.Empty;
            var hs = SkillHotkeys;
            if (hs != null && hs.ids != null)
            {
                for (var s = 0; s < hs.ids.Count; s++)
                {
                    if (hs.ids[s] != id) continue;
                    hotkey = "（已设热键 F" + (s + 1) + "）";
                    break;
                }
            }
            _speedBarInfo.text = name + hotkey;
        }

        /// <summary>速度棒里用的技能名（来自最近一次 `SkillTreeChanged` 的快照；取不到就回显 id）。</summary>
        private string NameOfSkill(int skillId)
        {
            var tree = _tree;
            if (tree != null && tree.skills != null)
            {
                for (var i = 0; i < tree.skills.Count; i++)
                {
                    var def = tree.skills[i];
                    if (def != null && def.id == skillId) return def.name;
                }
            }
            return "#" + skillId;
        }

        /// <summary>
        /// `F1`~`F8` 标签刷新（`Events.SkillHotkeysChanged` 的收方；重建格子后也补一次）。
        /// 某个技能被设到第 N 个热键 ⇒ 它的格子左上角显示 `FN`（没设 ⇒ 空）。标签样式 = 原版
        /// `SkillSlot.prefab` 的 `HotkeyLabel`（见 <see cref="AddHotkeyLabel"/>）。
        /// </summary>
        private void ApplyHotkeyLabelsToSpeedBar()
        {
            var hs = SkillHotkeys;
            for (var i = 0; i < _speedBarHotkeyLabels.Count; i++)
            {
                var label = _speedBarHotkeyLabels[i];
                if (label == null) continue;

                var id = _speedBarSkillIds[i];
                var text = string.Empty;
                if (hs != null && hs.ids != null)
                {
                    for (var s = 0; s < hs.ids.Count; s++)
                    {
                        if (hs.ids[s] != id) continue;
                        text = "F" + (s + 1);
                        break;
                    }
                }
                label.text = text;
            }
            UpdateSpeedBarInfo();
        }

        /// <summary>
        /// 打开速度棒（原版 `S` 或点某只手的技能格）。**已经开着且点的是同一只手 ⇒ 关掉**（开关）。
        /// </summary>
        /// <param name="button">`-1` = `S` 打开（点技能图标 ⇒ 默认装右键）；0 = 左键；1 = 右键。</param>
        internal void OpenSpeedBar(int button)
        {
            BuildSpeedBar();

            if (_speedBarOpen && _speedBarButton == button)
            {
                UiLog.Info("技能速度棒：已开着且点的是同一只手 ⇒ 收起（开关语义）");
                CloseSpeedBar();
                return;
            }

            _speedBarButton = button;
            _speedBarOpen = true;
            _speedBarRoot.gameObject.SetActive(true);
            RebuildSpeedBarCells();      // 技能列表可能变了（学了新技能 / 换角色）
            UiLog.Info($"技能速度棒：打开（目标手 = {(button < 0 ? "未指定 ⇒ 点图标装右键" : button == 0 ? "左键" : "右键")}）"
                + "；悬停技能图标 + 按 F1~F8 = 设热键");
            EmitSpeedBarState();
        }

        /// <summary>关掉速度棒（`S` / `Esc` / 点完技能 / HUD 关闭时调）。</summary>
        internal void CloseSpeedBar()
        {
            if (!_speedBarOpen) return;
            _speedBarOpen = false;
            _speedBarHoverCell = -1;
            if (_speedBarRoot != null) _speedBarRoot.gameObject.SetActive(false);
            UiLog.Info("技能速度棒：关闭 ⇒ 此后按 F1~F8 = 切换右键技能（不再是设热键）");
            EmitSpeedBarState();
        }

        /// <summary>把速度棒状态发给技能模块（它靠这个决定 `F1`~`F8` 是"设"还是"切"）。</summary>
        private void EmitSpeedBarState()
        {
            var args = new SkillSpeedBarArgs
            {
                open = _speedBarOpen,
                targetButton = _speedBarButton,
                hoverSkillId = _speedBarOpen && _speedBarHoverCell >= 0
                    && _speedBarHoverCell < _speedBarSkillIds.Count
                    ? _speedBarSkillIds[_speedBarHoverCell]
                    : -2,
            };
            Game.Event.Emit(Events.SkillSpeedBarChanged, args);
        }

        /// <summary>
        /// 每帧测指针停在速度棒的哪一格上（口径与 `SkillSlotHit` 同源：**屏幕点 → 矩形包含**，
        /// 不走 uGUI 射线；理由见 `SkillSlotHit` 的文件头）。
        /// </summary>
        private void PollSpeedBarHover()
        {
            if (!_speedBarOpen || _speedBarRoot == null) return;

            var input = Game.Input;
            var pos = input != null ? (Vector2)input.MousePosition : Vector2.zero;
            var cell = -1;
            for (var i = 0; i < _speedBarCellRects.Count; i++)
            {
                var rt = _speedBarCellRects[i];
                if (rt == null) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, pos, null)) { cell = i; break; }
            }

            if (cell == _speedBarHoverCell) return;

            _speedBarHoverCell = cell;
            UpdateSpeedBarInfo();
            UiLog.Info(cell < 0
                ? "技能速度棒：指针离开技能格"
                : $"技能速度棒：指针停在技能 #{_speedBarSkillIds[cell]}（{NameOfSkill(_speedBarSkillIds[cell])}）"
                  + " ⇒ 此刻按 F1~F8 = 把该技能设到那个热键");
            EmitSpeedBarState();
        }

        /// <summary>
        /// 技能格 = 图标层（原版节点自己的 Image）+ 热键标签（原版 `SkillSlot.prefab` 的 `HotkeyLabel`：
        /// 相对格子铺满、sizeDelta(−2,3)、偏移 (2,3)、pivot 左上）。
        /// <para><paramref name="openSpeedBarButton"/> ≥ 0 时额外挂一个点击：**点技能格 = 弹出技能列表**
        /// （原版手册 Interface Bar 段：「Left-clicking on an Action Icon displays a pop-up menu of skill
        /// icons. Click on a skill icon to assign it to that mouse button.」）⇒ 本项目 = 打开**技能速度棒**
        /// 且目标手 = 这一格（0 = 左键 / 1 = 右键）。<b>旧版这里是"循环切换该手技能"，那也是无出处的自定行为，已删。</b></para>
        /// </summary>
        private Image BuildSkillSlot(string name, Vector2 pos, string hotkey, string iconPath,
            int openSpeedBarButton = -1)
        {
            var slot = UiArt.Panel(transform, name, SkillSlotSize, pos, Color.white, true);
            UiArt.SetSprite(slot, iconPath);
            AddHotkeyLabel(slot.transform, hotkey, name);

            if (openSpeedBarButton >= 0)
            {
                var btn = slot.gameObject.AddComponent<Button>();
                btn.targetGraphic = slot;
                var b = openSpeedBarButton;
                btn.onClick.AddListener(() =>
                {
                    UiLog.Info($"[HUD] {(b == 0 ? "左键" : "右键")}技能格被点击 ⇒ 打开技能速度棒"
                        + "（原版：点 Action Icon 弹出技能图标列表 ⇒ 点图标即装到这只手）");
                    Game.Event.Emit(Events.UiClick);
                    OpenSpeedBar(b);
                });
            }

            return slot;
        }

        /// <summary>给技能格加原版样式的热键标签（左上角、暖黄色）；返回它，方便调用方后续改文案。</summary>
        private static D2Label AddHotkeyLabel(Transform slot, string hotkey, string owner)
        {
            //   字号唯一出处 = `UiLayoutGame.FontPx16`。
            var label = D2Label.Create(slot, "HotkeyLabel", hotkey, D2Text.D2Font.Font16,
                TextAnchor.UpperLeft, new Color(1f, 0.92f, 0.70f, 1f),
                SkillSlotSize + UiLayoutGame.SkillLabelSizeDelta,
                UiLayoutGame.SkillLabelPos - UiLayoutGame.SkillLabelSizeDelta * 0.5f,
                (int)UiLayoutGame.FontPx16);
            ApplyPrefabLabelRect(label);
            if (label == null) UiLog.WarnOnce("hud.slot.hotkey." + owner, $"{owner} 的热键标签未建出来");
            return label;
        }

        private static void ApplyPrefabLabelRect(D2Label label)
        {
            if (label == null || label.Root == null) return;
            var rt = label.Root;
            rt.anchorMin = UiLayoutGame.SkillLabelAnchorMin;
            rt.anchorMax = UiLayoutGame.SkillLabelAnchorMax;
            rt.pivot = UiLayoutGame.SkillLabelPivot;
            rt.sizeDelta = UiLayoutGame.SkillLabelSizeDelta;
            rt.anchoredPosition = UiLayoutGame.SkillLabelPos;
        }

        /// <summary>
        /// 腰带 4 格。
        /// <para>
        /// 原版**格线已经画在 `ControlPanel.png` 底图里**（art x 中心 599.5/630.5/661.5/692.5）⇒
        /// 这里只需要「可点的透明命中区 + 物品图标层 + 数量」三层，**不铺底色**（铺了就会盖住原版格线，
        /// </para>
        /// </summary>
        private void BuildBelt()
        {
            for (var i = 0; i < GameConst.BeltSlots; i++)
            {
                var pos = new Vector2(BeltCellX[i], BeltCellY);
                var size = new Vector2(BeltCellSize, BeltCellH);
                var cell = UiArt.Panel(transform, "Belt" + i, size, pos, new Color(1f, 1f, 1f, 0f), true);

                var icon = UiArt.Panel(cell.transform, "Icon", size, Vector2.zero, Color.white, false);
                icon.gameObject.SetActive(false);

                var index = i;   // 闭包捕获
                var button = cell.gameObject.AddComponent<Button>();
                button.targetGraphic = cell;
                button.onClick.AddListener(() =>
                {
                    UiLog.Info($"腰带格 {index} 被点击 ⇒ 请求使用（数字键 {index + 1} 同效）");
                    Game.Event.Emit(Events.UseBeltRequest, index);
                });

                _beltCells[i] = cell;
                _beltIcons[i] = icon;
                // 数量小字色统一走 `UiArt.ButtonText`（与背包格 `InventoryPanel` 同款金，避免两处不同色值）。
                _beltTexts[i] = D2Label.Create(cell.transform, "Count", string.Empty, D2Text.D2Font.Font16,
                    TextAnchor.LowerRight, UiArt.ButtonText, size, Vector2.zero,
                    (int)UiLayoutGame.FontPx16);
            }
        }

        private void BuildMiniPanel()
        {
            // 原版 ImageMinipanel：152×26 @ (0,60)（→ ×1.8 居中 + 贴底抬升）
            _miniPanelBg = UiArt.Panel(transform, "MiniPanel", MiniPanelSize, new Vector2(0f, MiniPanelY),
                Color.white, false);
            UiArt.SetSprite(_miniPanelBg, ResPaths.D2UiPanel + "minipanel");

            //   ① **8 个按钮**（依据见 `UiLayoutGame.MiniButtonCount`：`minipanelbtn.DC6` 16 帧 = 8 对
            //      + `string.tbl` 的 8 条 `minipanel*` tooltip）。默认**展开**（原版控制面板上这一排
            //      就在画面里，`string.tbl` 的 `StrHelp17迷你面板（開啟人物的物品欄，以及其他畫面）`
            //      是它的 tooltip）；唯一的开合箭头按钮按原版 `ImageExpBarRight` 的 `m_IsActive=0`
            //      `SetActive(false)` ⇒ 打开状态由这一排本身承担。
            //   ② **帧对 = 常态 `2i` / 按下 `2i+1`**（`ApplyMiniButtonPressFrame` 贴 `frame+1`），
            //      即每钮占连续两帧；帧号唯一来源 = `D2/UI/Panel/minipanelbtn_{0..15}.png`。
            //      逐帧图形：0/1 人像 · 2/3 剑 · 4/5 背袋人形 · 6/7 三人 · 8/9 星盘 ·
            //                 10/11 对话气泡 · 12/13 书 · 14/15 齿轮。
            //   ③ 每钮的归属（按钮语义按**图标形态**定，与 ② 的帧序一一对应）：
            //      `MiniBtn0` 人像 = 人物 · `MiniBtn1` 剑 = 技能樹 · `MiniBtn2` 背袋人形 = 物品 ·
            //      `MiniBtn3` 三人 = 隊伍畫面（本项目未实装）· `MiniBtn4` 星盘 = 自動地圖 ·
            //      `MiniBtn5` 对话气泡 = 訊息記錄（未实装）· `MiniBtn6` 书 = 任務記錄（未实装）
            //      · `MiniBtn7` 齿轮 = 遊戲選單。
            //   ④ hover 文案 = 原版串 4169..4176（见 `MiniTipChar` 那一族常量），8 条与 8 个按钮一一对应。
            AddMiniButton(0, 0, nameof(CharacterPanel), MiniTipChar);
            AddMiniButton(1, 2, nameof(SkillTreePanel), MiniTipSkillTree);
            AddMiniButton(2, 4, nameof(InventoryPanel), MiniTipInventory);
            AddMiniButton(3, 6, null, MiniTipParty);
            AddMiniButton(4, 8, nameof(MiniMapPanel), MiniTipAutomap);
            AddMiniButton(5, 10, null, MiniTipMessageLog);
            // slot 6（帧 12/13 = 书本图标）= 任務記錄（原版 4175）；本项目未实装该界面 ⇒ 与隊伍畫面/訊息記錄同款 toast。
            AddMiniButton(6, 12, null, MiniTipQuestLog);
            AddMiniButton(7, 14, PanelNamePause, MiniTipMenu);

            SetMiniPanelOpen(true);
            UiLog.Info($"迷你面板：展开（8 键，原版 `minipanelbtn.DC6` 的 8 对帧按 2i/2i+1 对齐）；"
                       + "键盘 C/I/T/Tab/Esc 同样可达（键位见 `Def/GameKeyAlias.cs`）");
        }

        /// <summary>小面板「菜单」按钮的目标：暂停请求（不是面板名，走 `Events.PauseRequest`）。</summary>
        public const string PanelNamePause = "__Pause";

        /// <summary>
        /// 展开 / 收起小面板（原版 `ControlPanelNavBarOpeningHandler.ShowNavigationalBar` 的同一件事：
        /// 切 `ImageMinipanel` 的 activeSelf + 换箭头按钮的贴图）。
        /// </summary>
        public void SetMiniPanelOpen(bool open)
        {
            _miniOpen = open;
            if (_miniPanelBg != null) _miniPanelBg.gameObject.SetActive(open);
            for (var i = 0; i < _miniButtons.Length; i++)
                if (_miniButtons[i] != null) _miniButtons[i].gameObject.SetActive(open);
            if (!open) HideMiniTips();      // 按钮一起收 ⇒ 悬停提示不许留在画面上
            ApplyMiniToggleSprite();
        }

        /// <summary>收起 8 个按钮的悬停提示（面板收起 / HUD 关闭时调用）。</summary>
        private void HideMiniTips()
        {
            for (var i = 0; i < _miniTips.Length; i++)
                if (_miniTips[i] != null) _miniTips[i].Hide();
        }

        /// <summary>
        /// 原版 `ImageExpBarRight` 的子 Button（15×24）：展开态 = 上箭头（`menubutton__0__0/1`）、
        /// 收起态 = 下箭头（`menubutton__0__2/3`）—— 与 `ControlPanelNavBarOpeningHandler` 里
        /// 「按下后换一张箭头贴图」的做法一致（贴图来源见 `UiLayoutGame.MiniPanelArrowFrames`）。
        /// </summary>
        private void BuildMiniPanelToggle()
        {
            _miniToggle = UiArt.Panel(transform, "MiniPanelToggle", MiniPanelArrowSize, MiniPanelArrowPos,
                Color.white, true);
            var button = _miniToggle.gameObject.AddComponent<Button>();
            button.targetGraphic = _miniToggle;
            button.onClick.AddListener(() =>
            {
                SetMiniPanelOpen(!_miniOpen);
                UiLog.Info($"小面板开关被点击 ⇒ {(_miniOpen ? "展开" : "收起")}（原版：箭头按钮切换导航条）");
            });
            ApplyMiniToggleSprite();
            //   原版小面板默认收起（见 `_miniOpen` 注释）⇒ 按原版默认态隐藏本按钮。
            _miniToggle.gameObject.SetActive(false);
        }

        /// <summary>按展开/收起态贴箭头贴图（常态/按下两态走 `SpriteSwap`）。</summary>
        private void ApplyMiniToggleSprite()
        {
            if (_miniToggle == null) return;

            var f = MiniPanelArrowFrames;
            var rev = ++_miniToggleRev;                       // 连点两次时丢弃过期回调
            var normalPath = _miniOpen ? f[0] : f[2];
            var pressedPath = _miniOpen ? f[1] : f[3];

            UiArt.SetSprite(_miniToggle, normalPath);

            var button = _miniToggle.GetComponent<Button>();
            if (button == null) return;
            if (Game.Res == null)
            {
                UiLog.WarnOnce("hud.minitoggle.res", "Game.Res 未初始化 ⇒ 小面板箭头按下帧取不到（只显示常态帧）");
                return;
            }

            Game.Res.LoadAsset<Sprite>(pressedPath, sp =>
            {
                if (_miniToggle == null || rev != _miniToggleRev) return;   // 已切换新一态 / 面板已关
                if (sp == null)
                {
                    UiLog.Warn($"小面板箭头按下帧缺失：{pressedPath}（保持常态帧）");
                    return;
                }
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = new SpriteState { pressedSprite = sp, highlightedSprite = sp };
            });
        }

        private void AddMiniButton(int slot, int frame, string panelName, string tip)
        {
            var size = new Vector2(MiniButtonSize, MiniButtonSize);
            var pos = new Vector2(MiniButtonX[slot], MiniPanelY);
            var img = UiArt.Panel(transform, "MiniBtn" + slot, size, pos, Color.white, true);
            // 原版两张帧（常态 NN / 按下 NN+1，见 ControlPanel.prefab 的 m_SpriteState）
            UiArt.SetSprite(img, UiArt.MiniPanelBtnFrame(frame));
            _miniButtons[slot] = img.transform;

            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            ApplyMiniButtonPressFrame(img, frame);

            // 悬停提示（原版这一排按钮各挂一条 `minipanel*` tooltip 串，出处见 `UI/ControlTip.cs`）：
            //   指针进 / 出各一次显隐；面板收起与 HUD 关闭各再收一次。
            _miniTips[slot] = ControlTip.Create(transform, img.rectTransform, tip);
            var hover = img.gameObject.AddComponent<HoverTarget>();
            if (_miniTips[slot] != null)
            {
                hover.OnEnter = _miniTips[slot].Show;
                hover.OnExit = _miniTips[slot].Hide;
            }

            var target = panelName;
            button.onClick.AddListener(() =>
            {
                if (target == PanelNamePause)
                {
                    // 本支不发 `PanelToggleRequest`（走的是 PauseRequest）⇒ 点击音要在这里自己发，
                    // 否则这一颗钮点下去没声（原版 8 颗钮都是同一声 `cursor\button.wav`）。
                    Game.Event.Emit(Events.UiClick);
                    Game.Event.Emit(Events.PauseRequest);
                    return;
                }
                if (target == null)
                {
                    // 同上：未实装的两颗钮也要有原版点击音（键盘/鼠标点了同一个钮，反馈应一致）。
                    Game.Event.Emit(Events.UiClick);
                    UiLog.Warn($"小面板按钮「{tip}」未接线（本项目未实装该界面）⇒ 只打日志");
                    Game.UI.Toast("该界面本项目未实装");
                    return;
                }
                Game.Event.Emit(Events.PanelToggleRequest, target);
            });
        }

        /// <summary>
        /// 按下帧：原版 8 个按钮的 `m_SpriteState.m_PressedSprite` 就是同一张图的**下一帧**
        /// （0→按下 1、2→3、…、14→15，见 `ControlPanel.prefab`）。
        /// </summary>
        private static void ApplyMiniButtonPressFrame(Image img, int frame)
        {
            var button = img.GetComponent<Button>();
            if (button == null) return;

            var pressedPath = UiArt.MiniPanelBtnFrame(frame + 1);
            Game.Res.LoadAsset<Sprite>(pressedPath, sp =>
            {
                if (img == null || button == null) return;
                if (sp == null)
                {
                    UiLog.Warn($"小面板按钮按下帧缺失：{pressedPath}（保持常态帧）");
                    return;
                }
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = new SpriteState { pressedSprite = sp, highlightedSprite = sp };
            });
        }

        private void BuildRunButton()
        {
            _runButton = UiArt.Panel(transform, "RunButton", RunButtonSize, RunButtonPos, Color.white, true);
            ApplyRunButton();
            // **保持隐藏**（理由）：
            //   原版实机基准图（`策划/基线图/原版_实机_UI基准_20260923.png`）**确实画着这个"跑/走小人"**，
            //   但**在我们这套素材上它无处可放**：
            //     · 实测 `ControlPanel.png`：格带凹槽占 art y 87..115、经验条轨道占 art y 129..133
            //       ⇒ 两者之间只有 **14 行**，而按钮高 **20 行** ⇒ 任何 y 都必然压住格带或压住经验条；
            //     · 按 prefab 的 x（art 330..346）+ "空白条" y（art 行 124.7 ⇒ 占 114.7..134.7）
            //       ⇒ 会盖住经验条最左 **16×4 原版px**（= 28.8×7.2 画布px）；
            //     · 实机图里那个小人**在经验条左边、与经验条同一行**——而那颗经验条在我们素材上
            //       左端在 art x 221（早于左键技能格 230），实机图那套面板的版面与 948×160 素材
            //       **不是同一套**（逐像素比对可证）⇒ **抄不到一个"有出处"的坐标**。
            _runButton.gameObject.SetActive(false);
            var button = _runButton.gameObject.AddComponent<Button>();
            button.targetGraphic = _runButton;
            button.onClick.AddListener(() =>
            {
                _running = !_running;
                ApplyRunButton();
                UiLog.Info($"跑/走切换：{(_running ? "跑" : "走")}");
            });
        }

        /// <summary>
        /// 跑/走按钮的常态帧（帧名 = `UiArt.RunButtonRunFrame` / `UiArt.RunButtonWalkFrame`；
        /// 为什么取第 2 帧 / 第 0 帧、以及为什么不用 `Diablerie` 副本，见 `UI/UiArt.cs` 的
        /// 「原版小图标帧」一节）。
        /// </summary>
        private void ApplyRunButton()
        {
            UiArt.SetSprite(_runButton, _running ? UiArt.RunButtonRunFrame : UiArt.RunButtonWalkFrame);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 数据刷新（**状态值只在这里刷**；`Awake` 不读上下文，constraints.md #7）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>按属性快照刷新（`Events.HudDirty` 的参数，也是 `OnOpen` 的参数）。</summary>
        private void Refresh(PlayerStatsDto stats)
        {
            if (!_built)
            {
                UiLog.Warn("HUD 尚未构建就收到刷新请求 ⇒ 忽略（正常路径不会发生：OnOpen 先 Build）");
                return;
            }

            var life = stats != null ? stats.life : 0;
            var maxLife = stats != null ? stats.maxLife : 0;
            var mana = stats != null ? stats.mana : 0;
            var maxMana = stats != null ? stats.maxMana : 0;

            if (stats == null)
            {
                UiLog.WarnOnce("hud.no.stats",
                    "HUD 还没有收到 `Events.HudDirty`（PlayerStatsDto）⇒ 球/经验条先按 0 显示；"
                    + "请 Player 模块在进图装配完成后 emit 一次全量快照");
            }

            UiBar.SetRatio(_lifeFill, maxLife > 0 ? (float)life / maxLife : 0f);
            UiBar.SetRatio(_manaFill, maxMana > 0 ? (float)mana / maxMana : 0f);
            UiBar.SetRatio(_expFill, ExpRatio(stats));

            // 经验条悬停文案跟着快照走（原版串 4163 的两个占位 = 当前经验 / 下一级所需）
            _expTip?.SetText(ExpTipText(stats));

            // 文本格式对齐原版串表 4165「生命： %d / %d」/ 4166「法力： %d / %d」（全角冒号 + 空格，
            // 审计 #3：旧版半角冒号无空格是偏离；基线图若为半角则此处需回退，但本项目口径取串表逐字）。
            _lifeText?.SetText($"生命： {life} / {maxLife}");
            _manaText?.SetText($"法力： {mana} / {maxMana}");

            ApplyBeltAndGold(stats);
        }

        /// <summary>经验条比例（满级或数据缺失 ⇒ 0；分母为 0 时不许除）。</summary>
        private static float ExpRatio(PlayerStatsDto stats)
        {
            if (stats == null || stats.expNext <= 0) return 0f;
            var span = (float)stats.expNext;
            if (span <= 0f) return 0f;
            return Mathf.Clamp01(stats.exp / span);
        }

        /// <summary>腰带格显示（数量来自快照；**药水图标未到位 ⇒ 品质色占位**，登记为素材缺口 #5）。</summary>
        private void ApplyBeltAndGold(PlayerStatsDto stats)
        {
            var belt = _inventory != null ? _inventory.belt : null;

            for (var i = 0; i < _beltCells.Length; i++)
            {
                var item = belt != null && i < belt.Count ? belt[i] : null;
                var count = stats != null && stats.beltCounts != null && i < stats.beltCounts.Count
                    ? stats.beltCounts[i]
                    : (item != null ? item.count : 0);

                // 图标层：有物品才显示（原版格线由底图提供，格子本身保持透明）
                //   取不到时才退回品质色块（`D2Icon.ApplyItemIcon` 内已点名 Warn）。
                D2Icon.ApplyItemIcon(_beltIcons[i], _beltIconPath, i, item);
                _beltTexts[i]?.SetText(count > 0 ? count.ToString() : string.Empty);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 事件（全部具名方法：`Game.Event` 无句柄，匿名 lambda 注销不掉）
        // ═════════════════════════════════════════════════════════════════════
        private void Subscribe()
        {
            if (_subscribed) return;
            if (Game.Event == null)
            {
                UiLog.Error("HUD 订阅失败：Game.Event 为 null（Game.Launch 未调用）");
                return;
            }

            _subscribed = true;
            Game.Event.On(Events.StageEntered, OnStageEntered);
            Game.Event.On(Events.StageLeft, OnStageLeft);
            Game.Event.On<Diablo2.Def.AreaId>(Events.AreaChanged, OnAreaChanged);   // ★ 区域名（过门换区）
            Game.Event.On<PlayerStatsDto>(Events.HudDirty, OnHudDirty);
            Game.Event.On<PlayerStatsDto>(Events.PlayerStatsChanged, OnHudDirty);
            Game.Event.On<int>(Events.LevelUp, OnLevelUp);
            Game.Event.On<InventoryChangedArgs>(Events.InventoryChanged, OnInventoryChanged);
            Game.Event.On<InventoryChangedArgs>(Events.EquipChanged, OnInventoryChanged);
            Game.Event.On<SkillTreeArgs>(Events.SkillTreeChanged, OnSkillTreeChanged);
            Game.Event.On<MinimapArgs>(Events.MapGenerated, OnMapGenerated);
            Game.Event.On<string>(Events.PanelToggleRequest, OnPanelToggle);
            Game.Event.On<NpcDialogArgs>(Events.DialogOpen, OnDialogOpen);
            Game.Event.On<ShopOpenArgs>(Events.ShopOpen, OnShopOpen);
            Game.Event.On(Events.PlayerDied, OnPlayerDied);
            // 左右键技能格绑定快照 + 地面物品名牌（都是 Def 载荷）
            Game.Event.On<SkillButtonsArgs>(Events.SkillButtonsChanged, OnSkillButtonsChanged);
            Game.Event.On<GroundItemLabelsArgs>(Events.GroundItemLabelsChanged, OnGroundItemLabels);
            // ★ 技能热键表（F1~F8 里各设了什么）⇒ 刷新技能速度棒格子上的 `FN` 标签
            Game.Event.On<SkillHotkeysArgs>(Events.SkillHotkeysChanged, OnSkillHotkeysChanged);
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Game.Event == null) return;
            _subscribed = false;

            Game.Event.Off(Events.StageEntered, OnStageEntered);
            Game.Event.Off(Events.StageLeft, OnStageLeft);
            Game.Event.Off<Diablo2.Def.AreaId>(Events.AreaChanged, OnAreaChanged);
            Game.Event.Off<PlayerStatsDto>(Events.HudDirty, OnHudDirty);
            Game.Event.Off<PlayerStatsDto>(Events.PlayerStatsChanged, OnHudDirty);
            Game.Event.Off<int>(Events.LevelUp, OnLevelUp);
            Game.Event.Off<InventoryChangedArgs>(Events.InventoryChanged, OnInventoryChanged);
            Game.Event.Off<InventoryChangedArgs>(Events.EquipChanged, OnInventoryChanged);
            Game.Event.Off<SkillTreeArgs>(Events.SkillTreeChanged, OnSkillTreeChanged);
            Game.Event.Off<MinimapArgs>(Events.MapGenerated, OnMapGenerated);
            Game.Event.Off<string>(Events.PanelToggleRequest, OnPanelToggle);
            Game.Event.Off<NpcDialogArgs>(Events.DialogOpen, OnDialogOpen);
            Game.Event.Off<ShopOpenArgs>(Events.ShopOpen, OnShopOpen);
            Game.Event.Off(Events.PlayerDied, OnPlayerDied);
            Game.Event.Off<SkillButtonsArgs>(Events.SkillButtonsChanged, OnSkillButtonsChanged);
            Game.Event.Off<GroundItemLabelsArgs>(Events.GroundItemLabelsChanged, OnGroundItemLabels);
            Game.Event.Off<SkillHotkeysArgs>(Events.SkillHotkeysChanged, OnSkillHotkeysChanged);
        }

        private void OnStageEntered()
        {
            // 进（新一局的）舞台 ⇒ 清掉"区域名已弹过"记录。
            //   `Events.StageEntered` 只在本局真正装配舞台时发（Pause→Resume 走 `OnEnterStage` 的
            //   `_stageActive` 提前返回，**不会**发本事件，见 `Module/Flow/AppFlow.cs:582-595`）
            //   ⇒ 这里清空不会出现"一暂停一恢复就重弹一遍区域名"。
            _levelTitle?.Reset();

            if (_selfManaged)
            {
                PrepareRootIfNeeded();
                gameObject.SetActive(true);
                Refresh(_stats);
                UiLog.Info("HUD 随 StageEntered 显示（场景预置实例）");
                return;
            }

            if (_live <= 1 && !Game.UI.IsOpen<HudPanel>())
            {
                Game.UI.Open<HudPanel>(_stats);
                return;
            }

            UiLog.Info("StageEntered：HUD 已在窗口栈里（或已由场景预置）⇒ 不重复打开");
        }

        private void OnStageLeft()
        {
            if (_selfManaged)
            {
                gameObject.SetActive(false);
                UiLog.Info("HUD 随 StageLeft 隐藏（场景预置实例）");
                return;
            }

            if (Game.UI.IsOpen<HudPanel>()) Game.UI.Close<HudPanel>();
        }

        private void PrepareRootIfNeeded()
        {
            if (_built) return;
            UiArt.PrepareRoot(Root);
            Build();
        }

        private void OnHudDirty(PlayerStatsDto stats)
        {
            if (stats == null)
            {
                UiLog.Warn("收到 `Events.HudDirty` 但参数为 null ⇒ 忽略（发送方请检查 Player.Snapshot()）");
                return;
            }
            _stats = stats;
            Refresh(_stats);
        }

        private void OnLevelUp(int level)
        {
            UiLog.Info($"升级事件：Lv {level}（HUD 等级/经验条随下一次 HudDirty 刷新）");
            if (_stats != null) _stats.level = level;
            Refresh(_stats);
        }

        private void OnInventoryChanged(InventoryChangedArgs args)
        {
            if (args == null)
            {
                UiLog.Warn("收到 `Events.InventoryChanged/EquipChanged` 但参数为 null ⇒ 忽略");
                return;
            }
            _inventory = args;
            ApplyBeltAndGold(_stats);
        }

        private void OnSkillTreeChanged(SkillTreeArgs args)
        {
            if (args == null)
            {
                UiLog.Warn("收到 `Events.SkillTreeChanged` 但参数为 null ⇒ 忽略");
                return;
            }
            _tree = args;

            // ★ 技能速度棒的内容 = 「普通攻击」+ 已学且非被动的技能 ⇒ 树一变就重建格子
            //   （新学一个技能后打开速度棒就能看到它）。速度棒没建出来（还没打开过）时不用管：
            //   `OpenSpeedBar` 里还会再 `RebuildSpeedBarCells()` 一次。
            if (_speedBarRoot != null) RebuildSpeedBarCells();
            else UiLog.Info($"[HUD] 已缓存技能树快照（已学 {args.learnedLevels?.Count ?? 0} 项）"
                + "⇒ 打开技能速度棒（S）时按它填充格子");
        }

        /// <summary>
        /// 左右键技能格图标刷新（`Events.SkillButtonsChanged` 的收方）。
        /// </summary>
        private void OnSkillButtonsChanged(SkillButtonsArgs args)
        {
            ApplySkillButtons(args);
        }

        /// <summary>
        /// 技能热键表刷新（`Events.SkillHotkeysChanged` 的收方；发方 = 技能模块，见 `SkillModule.RaiseHotkeysChanged`）。
        /// <para>用途：把 `F1`~`F8` 标签贴到技能速度棒的对应格子上（某个技能被设到第 N 个热键 ⇒ 它的格子显示 `FN`）。</para>
        /// </summary>
        private void OnSkillHotkeysChanged(SkillHotkeysArgs args)
        {
            if (args == null)
            {
                UiLog.Warn("收到 `Events.SkillHotkeysChanged` 但参数为 null ⇒ 技能速度棒标签不更新");
                return;
            }

            SkillHotkeys = args;
            ApplyHotkeyLabelsToSpeedBar();

            var set = 0;
            if (args.ids != null)
                for (var i = 0; i < args.ids.Count; i++)
                    if (args.ids[i] != -2) set++;

            UiLog.Info($"[HUD] 技能热键表已更新：已设 {set}/{args.count} 个（-1 = 普通攻击；未设 = -2）");
        }

        /// <summary>
        /// 地面物品名牌（`Events.GroundItemLabelsChanged` 的收方）。
        /// 名牌层**按需创建**（第一次收到非空载荷时建），避免"没人开道具时白建一层节点"。
        /// </summary>
        private void OnGroundItemLabels(GroundItemLabelsArgs args)
        {
            GroundItemLabels = args;

            if (args == null || args.labels == null || args.labels.Count == 0)
            {
                _groundLabels?.Clear();
                return;
            }

            if (_groundLabels == null)
            {
                // `Root`（`UIPanel` 的根 GameObject）优先；拿不到就退回本组件的 transform。
                var parent = Root != null ? Root.GetComponent<RectTransform>() : transform as RectTransform;
                if (parent == null)
                {
                    UiLog.WarnOnce("groundlabel.noroot", "HUD 根节点不是 RectTransform ⇒ 地面物品名牌层无法创建");
                    return;
                }
                _groundLabels = new GroundItemLabelView(parent);
                UiLog.Info("[HUD] 地面物品名牌层已创建（挂在 HUD 下，随 HUD 生命周期）");
            }

            _groundLabels.Apply(args);
        }

        private void OnMapGenerated(MinimapArgs map)
        {
            _minimap = map;
            if (map == null)
            {
                UiLog.Warn($"收到 `{Events.MapGenerated}` 但参数为 null ⇒ 小地图缓存清空（Map 模块请检查 BuildMinimap）");
                return;
            }
            UiLog.Info($"HUD 已缓存小地图快照（{map.width}×{map.height} seed={map.seed}）⇒ 打开自动地图时直接传入");

            // **进图那一次**的区域名触发点（`MinimapArgs.areaId`）。
            //   为什么用这里：进图时 `Events.AreaChanged` **不发**（它只在过门时发，见 `AppFlow.EnterArea`），
            //   而 `AppSnapshots.Broadcast("StageEntered")` 在 HUD 打开之后会补发一次 `MapGenerated`
            //   （`App/AppSnapshots.cs:123-128`）⇒ 这是 UI 侧能拿到的"进图后当前区域"的唯一权威来源。
            //   重复触发（同一区域）由 `LevelEntryTitle` 的"本局首次"去重挡掉。
            TriggerLevelEntryTitle((AreaId)map.areaId, "Events.MapGenerated（进图快照）");
        }

        /// <summary>
        /// 过门换区 ⇒ 可能弹区域名（`AppFlow.EnterArea` 在换区完成后发本事件）。
        /// </summary>
        private void OnAreaChanged(Diablo2.Def.AreaId area)
        {
            TriggerLevelEntryTitle(area, "Events.AreaChanged（过门换区）");
        }

        /// <summary>
        /// 区域名的统一触发口（两条路径都走它 ⇒ 日志格式一致、口径只有一处）。
        /// <para>**不在这里判重**：判重（本局首次）在 `UI/LevelEntryTitle.cs` 的 `ShowForFirstEntry`，
        /// 因为"首次"是那条控件自己的状态。</para>
        /// </summary>
        private void TriggerLevelEntryTitle(Diablo2.Def.AreaId area, string via)
        {
            if (_levelTitle == null)
            {
                // 非预期分支：控件没建出来（Build 失败）⇒ 必须留日志，否则"区域名不弹"会无从查起
                UiLog.Warn($"[区域名] {via} 触发区域={area}，但控件未建出来（HudPanel.Build 未跑？）⇒ 本次不弹");
                return;
            }

            UiLog.Info($"[区域名] 触发：区域={(int)area}({area}) 来源={via}");
            _levelTitle.ShowForFirstEntry(area);
        }

        /// <summary>
        /// 「面板类名 → 打开参数」的映射（**纯函数，离线可断言**；`OnPanelToggle` 就是调它）。
        /// </summary>
        public static object PanelParam(string panelName, PlayerStatsDto stats, InventoryChangedArgs inventory,
            SkillTreeArgs tree, MinimapArgs minimap)
        {
            switch (panelName)
            {
                case nameof(InventoryPanel): return inventory;
                case nameof(CharacterPanel): return stats;
                case nameof(SkillTreePanel): return tree;
                case nameof(MiniMapPanel): return minimap;
                default: return null;
            }
        }

        /// <summary>
        /// 「角色屏家族」= 共用 HUD 小面板入口（`I`/`C`/`T`）的三个面板。
        /// <para>只在**本入口**补「同族互斥」，不改别的进入点的语义：NPC 点「交易」开商店、传送点开面板
        /// 走的仍是引擎的 `Popup` 互斥（`InventoryPanel`/`CharacterPanel` 仍留 `Popup`）。</para>
        /// </summary>
        private static readonly string[] ScreenFamily =
        {
            nameof(InventoryPanel), nameof(CharacterPanel), nameof(SkillTreePanel),
        };

        /// <summary>关掉「角色屏家族」里除了 <paramref name="except"/> 之外的屏（`Close&lt;T&gt;` 对未开面板是空操作）。</summary>
        private static void CloseScreenFamily(string except)
        {
            for (var i = 0; i < ScreenFamily.Length; i++)
            {
                var other = ScreenFamily[i];
                if (other == except) continue;

                var open = other == nameof(InventoryPanel) ? Game.UI.IsOpen<InventoryPanel>()
                    : other == nameof(CharacterPanel) ? Game.UI.IsOpen<CharacterPanel>()
                    : Game.UI.IsOpen<SkillTreePanel>();
                if (!open) continue;

                UiLog.Info($"[R8-close] 同族互斥（由本入口显式补，引擎 `CloseMutexPanels` 已不覆盖 "
                           + $"`Normal` 层的屏）：开「{except}」⇒ 关掉仍开着的「{other}」");
                if (other == nameof(InventoryPanel)) Game.UI.Close<InventoryPanel>();
                else if (other == nameof(CharacterPanel)) Game.UI.Close<CharacterPanel>();
                else Game.UI.Close<SkillTreePanel>();
            }
        }

        /// <summary>面板开关统一入口（参数 = 面板类名；`HudPanel` 自己也支持）。</summary>
        private void OnPanelToggle(string panelName)
        {
            var param = PanelParam(panelName, _stats, _inventory, _tree, _minimap);

            // 同族互斥：见 `ScreenFamily` 的注释（`T` 降层后引擎管不到它了）。
            CloseScreenFamily(panelName);

            switch (panelName)
            {
                case nameof(InventoryPanel):
                    Toggle<InventoryPanel>(param);
                    break;
                case nameof(CharacterPanel):
                    Toggle<CharacterPanel>(param);
                    break;
                case nameof(SkillTreePanel):
                    Toggle<SkillTreePanel>(param);
                    break;
                case nameof(MiniMapPanel):
                    // param = 缓存的 `MinimapArgs`（见 `PanelParam`）；为 null 表示还没收到 MapGenerated
                    Toggle<MiniMapPanel>(param);
                    if (param == null)
                    {
                        UiLog.Warn($"打开小地图时还没有缓存到 `{Events.MapGenerated}` 的 `MinimapArgs`"
                                   + " ⇒ 面板先按空数据打开（地图尚未生成？见 Map 模块日志）");
                    }
                    break;
                case nameof(HudPanel):
                    OnStageLeft();
                    break;
                default:
                    UiLog.Warn($"`Events.PanelToggleRequest` 收到未知面板名「{panelName}」⇒ 忽略"
                               + "（请用 nameof(面板类) 作为参数）");
                    break;
            }
        }

        private void Toggle<T>(object param) where T : class, IUIPanel
        {
            if (Game.UI.IsOpen<T>())
            {
                Game.UI.Close<T>();
                return;
            }
            Game.UI.Open<T>(param);
        }

        private void OnDialogOpen(NpcDialogArgs args)
        {
            if (args == null)
            {
                UiLog.Warn("`Events.DialogOpen` 参数为 null ⇒ 不打开对话面板");
                return;
            }
            Game.UI.Open<NpcDialogPanel>(args);
        }

        private void OnShopOpen(ShopOpenArgs args)
        {
            if (args == null)
            {
                UiLog.Warn("`Events.ShopOpen` 参数为 null ⇒ 不打开商店面板");
                return;
            }
            Game.UI.Open<ShopPanel>(args);
        }

        private void OnPlayerDied()
        {
            UiLog.Info("玩家死亡事件 ⇒ 打开死亡面板");
            Game.UI.Open<DeathPanel>();
        }
    }
}
