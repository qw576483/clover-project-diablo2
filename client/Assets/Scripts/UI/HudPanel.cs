// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/HudPanel.cs（agent-09 · 1:1 轮）
// 游戏内 HUD：底部控制面板 = 左右两颗球（左红生命 / 右蓝法力）+ 经验条 + 左右技能格 +
// 腰带 4 格 + 小面板 7 按钮 + 跑/走按钮。
//
// ★★ 本文件改了什么、为什么（原版值 → **×1.8 居中**，见 `UI/UiLayoutGame.cs` 的口径说明）：
//   上一轮把**原版像素值**直接画在 1920×1080 的 Canvas 上（ControlPanel 948×160、球 108、
//   技能格 33.5、小按钮 20 …）⇒ 整个 HUD 只有应有的 1/1.8 大小；中间一版又用了**宽度比 ×2.4**
//   （948×2.4 = 2275.2 > 1920、600×2.4 = 1440 > 1080）⇒ 控制面板底边出屏 51px（旧 E5）。
//   现口径 = **按高度等比 ×1.8 + 水平居中**：常量集中在 `UI/UiLayoutGame.cs`
//   （每个值都注明「原版值 → ×1.8 居中」与来源 prefab 节点），本文件只消费它、不再自己写数字。
//   ★ agent-27：跑/走按钮与小面板开关的 **y** 已从 prefab 原值挪到「技能格带下方的空白条」
//     （原版这两个按钮的父容器 `m_IsActive=0` ⇒ 原版不显示；按原值摆会压住第 1/第 5 个技能格），
//     详见 `UiLayoutGame.HudSubBarArtY` 与 `策划/验收表.md` 的 E7。
//
// ★ 依据（原版精确 RectTransform，脚本从 prefab YAML 逐节点解析，非肉眼估）：
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
// ★ 本轮**移除**的两个非原版元素（1:1 硬标准：原版没有的就不画）：
//   · HUD 右上那行长「快捷键提示」——原版没有这条；原版的做法是**技能格上的热键标签**
//     （`SkillSlot.prefab` 的 `HotkeyLabel`），本轮改为给左/右技能格加同款标签。
//   · HUD 左上「Lv N」标签——原版 HUD 不显示等级（等级/经验在人物属性面板里，本轮的
//     `CharacterPanel` 已按原版右上空框补上）。
//
// ★ 打开方式（`docs/agents/_common.md` §3.5）：HUD 监听 `Events.StageEntered` 打开、
//   `Events.StageLeft` 关闭；它同时是**游戏内面板的总入口**（小面板 7 按钮 = 各面板入口）。
// ⛔ 零 `using Diablo2.Module`（分层自检 ③）。
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

        /// <summary>技能栏槽位数 = 6（原版 `SkillPanel.prefab` 根下的 6 个子槽位）。</summary>
        public const int SkillBarSlots = UiLayoutGame.SkillBarSlots;

        /// <summary>技能栏根矩形尺寸（原版 `SkillPanel.prefab` 224.97×35.12 → ×1.8 = 404.946×63.216）。</summary>
        public static readonly Vector2 SkillBarSize = UiLayoutGame.SkillBarSize;

        /// <summary>技能栏根矩形中心（原版 `SkillPanel.prefab` pos(-44.58, 35.76)）。</summary>
        public static readonly Vector2 SkillBarCenter = UiLayoutGame.SkillBarCenter;

        /// <summary>技能栏第 <paramref name="i"/> 个槽位的中心 x（原版 6 等分 ⇒ 步进 37.495 → ×1.8 = 67.491）。</summary>
        public static float SkillBarSlotX(int i) => UiLayoutGame.SkillBarSlotX(i);

        /// <summary>展开/收起小面板的箭头按钮尺寸（原版 `ImageExpBarRight/Button` 15×24 → ×1.8 = 27×43.2）。</summary>
        public static readonly Vector2 MiniPanelArrowSize = UiLayoutGame.MiniPanelArrowSize;

        /// <summary>
        /// 展开/收起小面板的箭头按钮中心：x = 原版 `ImageExpBarRight` 的子 Button art x 475 → ×1.8 居中；
        /// **y = 格带下方的空白条**（原版该按钮的父容器 `m_IsActive=0` ⇒ 原版不显示，
        /// 按原值摆会压住第 5 个技能格）——见 <see cref="UiLayoutGame.HudSubBarArtY"/> 与 `验收表` E7。
        /// </summary>
        public static readonly Vector2 MiniPanelArrowPos = UiLayoutGame.MiniPanelArrowPos;

        /// <summary>
        /// 展开/收起小面板的箭头贴图（原版 `PANEL/menubutton.DC6` 帧 0..3，15×24，逐帧文件）：
        /// `[0]` 上箭头常态、`[1]` 上箭头按下、`[2]` 下箭头常态、`[3]` 下箭头按下。
        /// <para>
        /// ★ 本轮（w3 游戏内 UI 审计）**换数据源**：原版这张图在工程里有**两套导出**
        /// （素材双份，见审计 TSV 的「双套素材」节）——
        ///   · `menubutton_{0..3}.png` = 本项目 `tools/d2codec/export_d2ui.py` 从原版
        ///     `data/global/ui/PANEL/menubutton.DC6` 直接解出（**索引 0 = 透明**，即 D2 的口径）；
        ///   · `menubutton__0__{0..3}.png` = 社区复刻工程 `Diablerie/Assets/Images/ControlPanel/`
        ///     的同名副本 —— 实测**同画面**，但把原版的透明像素写成了**不透明黑 (0,0,0,255)**
        ///     （每帧 **38 个像素**，逐像素比对见
        ///      `python tools/probes/measure/scan_uigame.py --pairs`）。
        /// 旧代码走的是副本 ⇒ 箭头周围会带一圈**黑点**（原版那里是透出大理石底）。
        /// 现走 DC6 导出那一套（同画面、alpha 正确），尺寸/帧序/位置**一个都没动**。
        /// </para>
        /// <para>★ w4 已收口：`ResPaths.PanelArrowUp/Down*` 四个常量已改指 `menubutton_{0..3}`
        /// 且 4 个副本文件已从磁盘删除（`Core/ResPaths.cs`）。本文件仍不直接经它们取图；
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

        /// <summary>小面板 7 个按钮的 x（原版 -63..63 步进 21 → ×1.8 = 37.8）。</summary>
        public static readonly float[] MiniButtonX = UiLayoutGame.MiniButtonX;

        /// <summary>小面板按钮边长（原版 20 → ×1.8 = 36）。</summary>
        public const float MiniButtonSize = UiLayoutGame.MiniButtonSize;

        /// <summary>腰带格边长（原版底图**格内凹槽宽 27** → ×1.8 = 48.6；旧值 31 = pitch，偏大 15%）。</summary>
        public const float BeltCellSize = UiLayoutGame.BeltCellSize;

        /// <summary>腰带格高度（原版底图**格内凹槽高 25** → ×1.8 = 45；旧值 29 是可见格高，偏高）。</summary>
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
        /// 6 格技能栏的图标层（原版 `SkillPanel`）。
        /// <para>★ impl-I-input（审计 R4）：本层现在由 `OnSkillTreeChanged` 按**已学技能顺序**
        /// 贴上真实技能图标（改动前恒空 + 点击只打「未接线」日志）。</para>
        /// </summary>
        private readonly Image[] _skillBarIcons = new Image[SkillBarSlots];

        /// <summary>
        /// 左右键技能格的图标层（★ impl-I-input，审计 R1/R4）：由 `OnSkillButtonsChanged`
        /// 换成当前绑定的技能图标；未绑（-1）时回退普通攻击图标（原版左右键默认都是 Attack）。
        /// </summary>
        private Image _leftSkillIcon;
        private Image _rightSkillIcon;

        /// <summary>地面物品名牌层（★ impl-I-input，审计 R5；见 `UI/GroundItemLabelView.cs`）。</summary>
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

        /// <summary>小面板 7 个按钮（展开/收起时一起显隐）。</summary>
        private readonly Transform[] _miniButtons = new Transform[UiLayoutGame.MiniButtonCount];

        /// <summary>小面板是否展开（原版靠箭头按钮 `ShowNavigationalBar` 切换；原版默认**收起**）。</summary>
        private bool _miniOpen;

        /// <summary>★ agent-18 §B：每格"最近一次贴上的原版物品图标路径"（`null` = 空）⇒ 不重复发起异步加载。</summary>
        private readonly string[] _beltIconPath = new string[GameConst.BeltSlots];
        private readonly D2Label[] _beltTexts = new D2Label[GameConst.BeltSlots];

        // 最近一次收到的快照（用于「打开子面板时把已有数据带上」，避免刚打开是空的）
        private PlayerStatsDto _stats;
        private InventoryChangedArgs _inventory;
        private SkillTreeArgs _tree;
        private QuestStateDto _quest;

        /// <summary>
        /// 最近一次 `Events.MapGenerated` 的小地图快照。
        /// **必须缓存并传给 `MiniMapPanel`**：该面板在 `OnOpen` 里才订阅 `MapGenerated`，
        /// 所以「打开时才派发」的快照它收不到（agent-13 §B-3）。
        /// </summary>
        private MinimapArgs _minimap;

        /// <summary>
        /// ★ agent-a3：**区域名弹出**（原版 `LevelEntryTitle`，见 `UI/LevelEntryTitle.cs`）。
        /// <para>触发口径 = **首次进入某区域**（本局内每个区域弹一次）。两条触发路径：</para>
        /// <list type="bullet">
        ///   <item>`Events.MapGenerated`（参数 `MinimapArgs.areaId`）——**进图**那次由
        ///     `AppSnapshots.Broadcast` 在 HUD 打开**之后**补发 ⇒ 覆盖"开局第一次进入某区域"。
        ///     ⚠️ `Events.AreaChanged` 只在**过门**时发（`AppFlow.EnterArea`），进图那次不发 ——
        ///     所以单靠 `AreaChanged` 会漏掉开局第一次（这是本项目必须两条都订的原因）。</item>
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
            UiBar.Forget(_lifeFill);
            UiBar.Forget(_manaFill);
            UiBar.Forget(_expFill);
            _groundLabels?.Destroy();       // ★ impl-I-input：名牌层随 HUD 一起拆（节点不跨局残留）
            _groundLabels = null;
            UiLog.Info("HUD 已关闭");
        }

        /// <inheritdoc/>
        public override void OnUpdate(float dt)
        {
            // ★ agent-a3：区域名的淡入/停留/淡出**先推**（它是纯表现，不该受输入后端的可用性影响；
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
            PollHotkey(GameKeyAlias.KeyQuestLog, nameof(QuestLogPanel));
            PollHotkey(GameKeyAlias.KeyMinimap, nameof(MiniMapPanel));
            PollHotkey(GameKeyAlias.KeyRunToggle, null);
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
        /// 构件（★ 顺序 = **原版 `ControlPanel.prefab` 的兄弟顺序**；uGUI 里后者画在越上层）。
        /// <para>
        /// 原版顺序：Background → LeftSkill/RightSkill → Lifebulb/Manabulb → ImageExpBarLeft(跑/走)
        /// → ImageExpBarRight(小面板开关) → ImageBeltRight(腰带) → ImageMinipanel(7 键)
        /// → ExperienceBar → **ExpBarOverlay（最后一个 ⇒ 覆盖在上层）**。
        /// `SkillPanel`（6 格技能栏）在原版是独立 prefab、prefab 里没挂到 ControlPanel 下，
        /// 本项目把它插在球之后、跑/走按钮之前（与底图格子同层，且被小面板底图盖住 —— 见 `BuildMiniPanel`）。
        /// </para>
        /// </summary>
        private void Build()
        {
            if (_built) return;
            _built = true;

            BuildControlPanel();        // 1  Background
            BuildSkillSlots();          // 2  LeftSkill / RightSkill
            BuildOrbs();                // 3  Lifebulb / Manabulb
            BuildSkillBar();            // 4  SkillPanel（6 格技能栏）
            BuildRunButton();           // 5  ImageExpBarLeft 的子 Button（跑/走）
            BuildMiniPanelToggle();     // 6  ImageExpBarRight 的子 Button（小面板开关）
            BuildBelt();                // 7  ImageBeltRight 框住的 4 格
            BuildMiniPanel();           // 8  ImageMinipanel + 7 键
            BuildExpBar();              // 9  ExperienceBar + ExpBarOverlay（最后 = 最上层）
            BuildLevelEntryTitle();     // 10 ★ agent-a3 区域名（原版 LevelEntryTitle；建在最后 ⇒ 盖在 HUD 之上）
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
            var bg = UiArt.Panel(transform, "ControlPanel", PanelBgSize, PanelBgPos, Color.white, false);
            UiArt.SetSprite(bg, ResPaths.D2UiPanel + "ControlPanel");
        }

        private void BuildOrbs()
        {
            // ★ 原版 `Lifebulb` / `Manabulb` **只有三层**：
            //   `HealthBar`/`ManaAnimation`（Filled 填充）+ `*BulbOverlay`（玻璃高光）+ `*Label`（球内数字）。
            //   球体本身是**底图 `ControlPanel.png` 画好的** —— 原版**没有**深色底球节点。
            //   ⛔ 上一轮自加的 `*OrbBack`（同贴图 ×0.30 压暗的 108×108 方块）会把底图球体整块盖掉，
            //      画面变成"一块平色圆盘"，正是"球不对"的成因 ⇒ **本轮删除**（1:1：原版没有的就不画）。
            _lifeFill = BuildOrb("LifeOrb", LifeOrbPos, ResPaths.PanelHealthBar, 0, out var lifeHolder);
            _manaFill = BuildOrb("ManaOrb", ManaOrbPos, ResPaths.PanelManaBar, 1, out var manaHolder);

            // 原版 `HealthLabel`/`ManaLabel`：**球内**子节点、铺满球宽、高 8px、y 偏 +4、居中；
            // 文本格式照原版自己的写法「Life: 123/123」（见 ControlPanel.prefab 的 m_Text）。
            // 挂在球容器**之内**、且在填充/高光之后 ⇒ 数字画在球的最上层（与 prefab 子节点顺序一致）。
            //
            // ★ 片 font-scale（用户报「文字太小」的第 2 层根因；V6 报告 §4-② 的 6 处之一）：
            //   原来这两条**没给 fontSize**（默认 0 = 按原版 px 1:1 画 ⇒ chi 格只有 ~16 画布px，
            //   是本画布应有字号的 16/28.8 ≈ 55%）⇒ 球内数字明显偏小。
            //   字号**唯一出处** = `UiLayoutGame.FontPx16`（原版行距 16 × K 1.8 = **28.8**，取整 28；
            //   见 `UiLayoutGame` 的「全 UI 字号唯一出处」一节）；⛔ 本文件不另写字号字面量。
            //   判据：`uicheck` 的 FontScaleCheck（本处 9th 实参 = `(int)UiLayoutGame.FontPx16`）。
            // ★ 2026-09-23「用户基准图」轮：文案改**中文**（原版实机基线图里就是
            //   「生命: 776/879」「法力: 335/335」——中文版口径；旧值是英文 "Life:"/"Mana:"，
            //   是自创文案，1:1 硬标准下不允许）。字号/位置/外框见 `UiLayoutGame.OrbLabelSize`
            //   与 `OrbLabelOffsetY`（都按基线图逐像素重定过）。
            _lifeText = D2Label.Create(lifeHolder, "LifeText", "生命: 0/0", D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, new Color(0.98f, 0.93f, 0.90f, 1f),
                UiLayoutGame.OrbLabelSize, new Vector2(0f, UiLayoutGame.OrbLabelOffsetY),
                (int)UiLayoutGame.FontPx16);
            _manaText = D2Label.Create(manaHolder, "ManaText", "法力: 0/0", D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, new Color(0.86f, 0.91f, 0.99f, 1f),
                UiLayoutGame.OrbLabelSize, new Vector2(0f, UiLayoutGame.OrbLabelOffsetY),
                (int)UiLayoutGame.FontPx16);

            if (_lifeText == null || _manaText == null)
                UiLog.WarnOnce("hud.orblabel", "球内数字标签未建出来（D2Label.Create 返回 null）⇒ 球上没有数字");
        }

        /// <summary>
        /// 造一颗球：**填充球**（原版 `HealthBar`/`ManaAnimation` = Filled + Vertical + fillOrigin=Bottom）
        /// + 玻璃高光。两层都在**同尺寸容器**（`{name}Holder`）里：`UiBar` 的锚点降级模式以父节点为基准。
        /// <para>⛔ 不铺"深色底球"——原版没有这个节点，底图自带球体（见 <see cref="BuildOrbs"/> 的说明）。</para>
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
                               + "（球只剩液体填充，登记在 `client/资源欠缺清单.md`）");
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
        }

        private void BuildSkillSlots()
        {
            // 原版 `ControlPanel.prefab` 的 `LeftSkill`/`RightSkill` 各有一个子标签，**文本就是 "L" / "R"**
            // （实测 prefab 的 m_Text: 'L' 与 m_Text: R）——不是"Attack"，上一轮写成 L/R 是对的。
            // ★ impl-I-input：两格的 Image 要留着 ⇒ 收到 `Events.SkillButtonsChanged` 时换图标。
            _leftSkillIcon = BuildSkillSlot("LeftSkill", LeftSkillPos, "L", ResPaths.SkillIconAttack);
            _rightSkillIcon = BuildSkillSlot("RightSkill", RightSkillPos, "R", ResPaths.SkillIconAttack);
        }

        /// <summary>
        /// 左右键技能格换图标（原版：两个技能格显示**当前绑定的技能**的图标；未绑 = 普通攻击图标）。
        /// 收方 = `Events.SkillButtonsChanged`（★ impl-I-input，审计 R4）。
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
        /// 6 格技能栏（原版 `SkillPanel.prefab` 根：224.97×35.12 @ pos(-44.58,35.76)，6 个子槽位等分）。
        /// <para>
        /// ★ 底图 `ControlPanel.png` 已经把 6 个格子画出来了（实测 art x 320..542、pitch 38），
        /// 所以这里**不铺底色、不抢先贴图标**，只放「透明命中区 + 热键标签」：
        /// 原版 `SkillSlot.prefab` 的 `HotkeyLabel` 相对格子铺满、偏移 (2,3)、左上对齐；
        /// 原版 6 格绑的是 **F1~F6**（`Diablerie/Engine/PlayerController.cs` 的
        /// `hotSkillsBindings = {F1,F2,F3,F4,F5,F6}` + `SkillPanel.SetHotKey(i, key.ToString())`，
        /// 与本项目 `Def/GameKeyAlias.cs` 的 `KeySkillSlot1..6` 同口径）。
        /// </para>
        /// <para>
        /// ★ impl-I-input（审计 R4）：「哪一格装哪个技能」= **已学技能顺序**的第 i 个（`OnSkillTreeChanged`
        /// 按 `Def.SkillTreeArgs.skills` + `learnedLevels` 贴图标）；点击第 i 格 = 按 `F(i+1)`，
        /// 发 `Events.SkillSlotAssignRequest`（发给技能模块去绑左右键技能格）。
        /// </para>
        /// </summary>
        private void BuildSkillBar()
        {
            for (var i = 0; i < SkillBarSlots; i++)
            {
                var pos = new Vector2(SkillBarSlotX(i), SkillBarCenter.y);
                var slot = UiArt.Panel(transform, "SkillBar" + i, SkillSlotSize, pos,
                    new Color(1f, 1f, 1f, 0f), true);

                var icon = UiArt.Panel(slot.transform, "Icon", SkillSlotSize, Vector2.zero, Color.white, false);
                icon.gameObject.SetActive(false);        // 有技能时由技能系统贴上（见上方说明）
                _skillBarIcons[i] = icon;

                var index = i;
                var button = slot.gameObject.AddComponent<Button>();
                button.targetGraphic = slot;
                button.onClick.AddListener(() =>
                {
                    // ★ impl-I-input：点第 i 格 = 按 F(i+1)（原版口径：技能栏格与 F 键同源）
                    UiLog.Info($"技能栏第 {index + 1} 格（F{index + 1}）被点击 ⇒ 发 {Events.SkillSlotAssignRequest}"
                        + $"（槽号 {index + 1}）");
                    Game.Event.Emit(Events.SkillSlotAssignRequest, index + 1);
                });

                AddHotkeyLabel(slot.transform, "F" + (i + 1), "SkillBar" + i);
            }
        }

        /// <summary>
        /// 技能格 = 图标层（原版节点自己的 Image）+ 热键标签（原版 `SkillSlot.prefab` 的 `HotkeyLabel`：
        /// 相对格子铺满、sizeDelta(−2,3)、偏移 (2,3)、pivot 左上）。
        /// </summary>
        private Image BuildSkillSlot(string name, Vector2 pos, string hotkey, string iconPath)
        {
            var slot = UiArt.Panel(transform, name, SkillSlotSize, pos, Color.white, true);
            UiArt.SetSprite(slot, iconPath);
            AddHotkeyLabel(slot.transform, hotkey, name);
            return slot;
        }

        /// <summary>给技能格加原版样式的热键标签（左上角、暖黄色）。</summary>
        private static void AddHotkeyLabel(Transform slot, string hotkey, string owner)
        {
            // ★ 片 font-scale：补显式字号（默认 0 = 原版 px 1:1 ⇒ 热键字只有应有的 ~55%）。
            //   字号唯一出处 = `UiLayoutGame.FontPx16`。
            var label = D2Label.Create(slot, "HotkeyLabel", hotkey, D2Text.D2Font.Font16,
                TextAnchor.UpperLeft, new Color(1f, 0.92f, 0.70f, 1f),
                SkillSlotSize + UiLayoutGame.SkillLabelSizeDelta,
                UiLayoutGame.SkillLabelPos - UiLayoutGame.SkillLabelSizeDelta * 0.5f,
                (int)UiLayoutGame.FontPx16);
            ApplyPrefabLabelRect(label);
            if (label == null) UiLog.WarnOnce("hud.slot.hotkey." + owner, $"{owner} 的热键标签未建出来");
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
        /// ★ 原版**格线已经画在 `ControlPanel.png` 底图里**（art x 中心 599.5/630.5/661.5/692.5）⇒
        /// 这里只需要「可点的透明命中区 + 物品图标层 + 数量」三层，**不铺底色**（铺了就会盖住原版格线，
        /// 上一轮就是拿深色方块顶掉了原版格子）。
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
                // ★ 片 font-scale：补显式字号（默认 0 = 原版 px 1:1 ⇒ 腰带数量只有应有的 ~55%）。
                _beltTexts[i] = D2Label.Create(cell.transform, "Count", string.Empty, D2Text.D2Font.Font16,
                    TextAnchor.LowerRight, new Color(1f, 0.92f, 0.70f, 1f), size, Vector2.zero,
                    (int)UiLayoutGame.FontPx16);
            }
        }

        private void BuildMiniPanel()
        {
            // 原版 ImageMinipanel：152×26 @ (0,60)（→ ×1.8 居中 + 贴底抬升）
            _miniPanelBg = UiArt.Panel(transform, "MiniPanel", MiniPanelSize, new Vector2(0f, MiniPanelY),
                Color.white, false);
            UiArt.SetSprite(_miniPanelBg, ResPaths.D2UiPanel + "minipanel");

            // ★★ U3 更正（用户 2026-09-23 报「下面的栏 100% 不是原版」「找不到入口只能按 c」）：
            //   ① **按钮数 7 → 8**（依据见 `UiLayoutGame.MiniButtonCount`：`minipanelbtn.DC6` 16 帧
            //      = 8 对 + `string.tbl` 的 8 条 `minipanel*` tooltip）。原版那一排（按原版 tooltip 名）
            //      = 人物 / 物品 / 技能樹 / 隊伍畫面 / 自動地圖 / 訊息記錄 / 任務記錄 / 遊戲選單。
            //   ② **默认展开**。上一轮按 Diablerie `ControlPanel.prefab` 的 `ImageMinipanel m_IsActive=0`
            //      默认收起，并且把唯一的开合箭头也 `SetActive(false)` ⇒ **鼠标入口 0 个**（只剩键盘），
            //      用户报的「找不到入口」即此。原版控制面板上这一排**就在画面里**（`string.tbl` 的
            //      `StrHelp17迷你面板（開啟人物的物品欄，以及其他畫面）` 是它的 tooltip）⇒ 默认展开。
            //   帧对（每钮 = 常态 `2i` / 按下 `2i+1`，见 `AddMiniButton` 下的 `ApplyMiniButtonPressFrame`）：
            //      i=0 人物 2/3 · i=1 物品 4/5 · i=2 技能樹 6/7 · i=3 隊伍畫面 8/9 ·
            //      i=4 自動地圖 10/11 · i=5 訊息記錄 12/13 · i=6 任務記錄 14/15 · i=7 遊戲選單 16/17
            //      ⚠️ `ControlPanel.prefab` 记的是 0/2/4/8/10/12/14（跳过 6）—— 那是"漏了第 4 个按钮"
            //      之后**把后面的整体前移一档**得到的错序；本表按 DC6 帧对的自然顺序重排。
            AddMiniButton(0, 0, nameof(CharacterPanel), "人物（C）");
            AddMiniButton(1, 2, nameof(InventoryPanel), "物品（I）");
            AddMiniButton(2, 4, nameof(SkillTreePanel), "技能樹（T）");
            AddMiniButton(3, 6, null, "隊伍畫面（原版有此屏，本项目未实装）");
            AddMiniButton(4, 8, nameof(MiniMapPanel), "自動地圖（Tab）");
            AddMiniButton(5, 10, null, "訊息記錄（原版有此屏，本项目未实装）");
            AddMiniButton(6, 12, nameof(QuestLogPanel), "任務記錄（Q）");
            AddMiniButton(7, 14, PanelNamePause, "遊戲選單（Esc）");

            SetMiniPanelOpen(true);
            UiLog.Info($"迷你面板：默认**展开**（8 键，原版 `minipanelbtn.DC6` 的 8 对帧对齐）；"
                       + $"键盘 I/C/T/Q/Tab/Esc 同样可达（本次修复：此前 7 键且默认隐藏 ⇒ 鼠标无入口）");
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
            ApplyMiniToggleSprite();
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
            // ★ 本轮（E7 收口）：原版该箭头的父容器 `ImageExpBarRight` 同样是 `m_IsActive: 0`
            //   ⇒ **原版 HUD 上看不到它**；而小面板本身在原版也是**默认收起**（见 `_miniOpen` 注释）
            //   ⇒ 按原版默认态隐藏本按钮。原版「导航条展开」的完整实现未拿到 ⇒ 保持 E7 登记。
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
            var target = panelName;
            button.onClick.AddListener(() =>
            {
                if (target == PanelNamePause)
                {
                    Game.Event.Emit(Events.PauseRequest);
                    return;
                }
                if (target == null)
                {
                    UiLog.Warn($"小面板按钮「{tip}」未接线（本项目未实装该界面）⇒ 只打日志");
                    Game.UI.Toast("该界面本项目未实装");
                    return;
                }
                Game.Event.Emit(Events.PanelToggleRequest, target);
            });
        }

        /// <summary>
        /// 按下帧：原版 7 个按钮的 `m_SpriteState.m_PressedSprite` 就是同一张图的**下一帧**
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
            // ⛔ **保持隐藏**（hud-redo2 试过打开，**量过之后又关回去**——理由必须留痕）：
            //   用户基线实机图（`策划/基线图/原版_实机_UI基准_20260923.png`）**确实画着这个"跑/走小人"**，
            //   但**在我们这套素材上它无处可放**：
            //     · 实测 `ControlPanel.png`：格带凹槽占 art y 87..115、经验条轨道占 art y 129..133
            //       ⇒ 两者之间只有 **14 行**，而按钮高 **20 行** ⇒ 任何 y 都必然压住格带或压住经验条；
            //     · 按 prefab 的 x（art 330..346）+ "空白条" y（art 行 124.7 ⇒ 占 114.7..134.7）
            //       ⇒ 会盖住经验条最左 **16×4 原版px**（= 28.8×7.2 画布px）；
            //     · 实机图里那个小人**在经验条左边、与经验条同一行**——而那颗经验条在我们素材上
            //       左端在 art x 221（早于左键技能格 230），实机图那套面板的版面与 948×160 素材
            //       **不是同一套**（见回报 B-3 的量化证明）⇒ **抄不到一个"有出处"的坐标**。
            //   ⇒ 按 §0「写不出出处的量不许进工程」**保持隐藏**（节点与 `R` 键入口都在，
            //     ⛔ 不发明替代坐标），并把「是否要为它改素材/改版面」交主 agent 裁决。
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

            // 文本格式照**原版实机基线图**（中文版）：`生命: 123/123` / `法力: 123/123`
            // —— 出处 `策划/基线图/原版_实机_UI基准_20260923.png`（左下/右下球上文字逐像素可读）。
            _lifeText?.SetText($"生命: {life}/{maxLife}");
            _manaText?.SetText($"法力: {mana}/{maxMana}");

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

        /// <summary>腰带格显示（数量来自快照；**药水图标未到位 ⇒ 品质色占位**，登记在资源欠缺清单 #5）。</summary>
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
                // ★ agent-18 §B：图标换成**原版物品图**（`D2/Items/inv{code}.png`，配表 item_c.code），
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
            Game.Event.On<Diablo2.Def.AreaId>(Events.AreaChanged, OnAreaChanged);   // ★ agent-a3 区域名（过门换区）
            Game.Event.On<PlayerStatsDto>(Events.HudDirty, OnHudDirty);
            Game.Event.On<PlayerStatsDto>(Events.PlayerStatsChanged, OnHudDirty);
            Game.Event.On<int>(Events.LevelUp, OnLevelUp);
            Game.Event.On<InventoryChangedArgs>(Events.InventoryChanged, OnInventoryChanged);
            Game.Event.On<InventoryChangedArgs>(Events.EquipChanged, OnInventoryChanged);
            Game.Event.On<SkillTreeArgs>(Events.SkillTreeChanged, OnSkillTreeChanged);
            Game.Event.On<QuestStateDto>(Events.QuestChanged, OnQuestChanged);
            Game.Event.On<MinimapArgs>(Events.MapGenerated, OnMapGenerated);
            Game.Event.On<string>(Events.PanelToggleRequest, OnPanelToggle);
            Game.Event.On<NpcDialogArgs>(Events.DialogOpen, OnDialogOpen);
            Game.Event.On<ShopOpenArgs>(Events.ShopOpen, OnShopOpen);
            Game.Event.On(Events.PlayerDied, OnPlayerDied);
            // ★ impl-I-input（审计 R1/R4/R5）：左右键技能格绑定快照 + 地面物品名牌（都是 Def 载荷）
            Game.Event.On<SkillButtonsArgs>(Events.SkillButtonsChanged, OnSkillButtonsChanged);
            Game.Event.On<GroundItemLabelsArgs>(Events.GroundItemLabelsChanged, OnGroundItemLabels);
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
            Game.Event.Off<QuestStateDto>(Events.QuestChanged, OnQuestChanged);
            Game.Event.Off<MinimapArgs>(Events.MapGenerated, OnMapGenerated);
            Game.Event.Off<string>(Events.PanelToggleRequest, OnPanelToggle);
            Game.Event.Off<NpcDialogArgs>(Events.DialogOpen, OnDialogOpen);
            Game.Event.Off<ShopOpenArgs>(Events.ShopOpen, OnShopOpen);
            Game.Event.Off(Events.PlayerDied, OnPlayerDied);
            Game.Event.Off<SkillButtonsArgs>(Events.SkillButtonsChanged, OnSkillButtonsChanged);
            Game.Event.Off<GroundItemLabelsArgs>(Events.GroundItemLabelsChanged, OnGroundItemLabels);
        }

        private void OnStageEntered()
        {
            // ★ agent-a3：进（新一局的）舞台 ⇒ 清掉"区域名已弹过"记录。
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
            ApplySkillBarIcons(args);      // ★ impl-I-input（审计 R4）：技能栏 6 格贴"已学技能"的图标
        }

        /// <summary>
        /// 技能栏 6 格贴图（★ impl-I-input，审计 R4）：第 i 格 = 本职业**已学技能顺序**的第 i 个
        /// （顺序 = `Def.SkillTreeArgs.skills` 的顺序 = 技能树 tree→reqLevel→id）。
        /// 已学数不足 6 ⇒ 多出来的格保持空（原版 1 级时技能栏也是空的）。
        /// </summary>
        private void ApplySkillBarIcons(SkillTreeArgs tree)
        {
            if (tree == null || tree.skills == null) return;

            var learned = new List<int>();
            for (var i = 0; i < tree.skills.Count; i++)
            {
                var def = tree.skills[i];
                if (def == null) continue;
                var lv = i < tree.learnedLevels.Count ? tree.learnedLevels[i] : 0;
                if (lv <= 0) continue;
                learned.Add(def.id);
            }

            for (var i = 0; i < _skillBarIcons.Length; i++)
            {
                var icon = _skillBarIcons[i];
                if (icon == null) continue;

                if (i >= learned.Count)
                {
                    icon.gameObject.SetActive(false);
                    continue;
                }

                var path = D2Icon.SkillIconPath(learned[i], false);
                if (string.IsNullOrEmpty(path))
                {
                    // 非预期分支：已学但取不到原版图标（配表缺行 / 资源缺）⇒ 该格留空
                    UiLog.WarnOnce("hud.baricon." + learned[i],
                        $"技能栏 {i + 1} 格：技能 #{learned[i]} 已学但取不到原版图标 ⇒ 该格留空");
                    icon.gameObject.SetActive(false);
                    continue;
                }

                UiArt.SetSprite(icon, path);
                icon.gameObject.SetActive(true);
            }

            UiLog.Info($"[HUD] 技能栏按已学技能刷新：已学 {learned.Count} 个 ⇒ " +
                       $"{Mathf.Min(learned.Count, _skillBarIcons.Length)} 格有图标（F1~F6 = 技能栏 1~6 格）");
        }

        /// <summary>
        /// 左右键技能格图标刷新（`Events.SkillButtonsChanged` 的收方；★ impl-I-input，审计 R4）。
        /// </summary>
        private void OnSkillButtonsChanged(SkillButtonsArgs args)
        {
            ApplySkillButtons(args);
        }

        /// <summary>
        /// 地面物品名牌（`Events.GroundItemLabelsChanged` 的收方；★ impl-I-input，审计 R5）。
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

        /// <summary>缓存任务快照：打开任务日志时带上它，避免刚打开是空面板。</summary>
        private void OnQuestChanged(QuestStateDto quest)
        {
            if (quest == null)
            {
                UiLog.Warn("收到 `Events.QuestChanged` 但参数为 null ⇒ 忽略");
                return;
            }
            _quest = quest;
        }

        /// <summary>缓存小地图快照：打开 `MiniMapPanel` 时作为 `OnOpen` 参数传入（agent-13 §B-3）。</summary>
        private void OnMapGenerated(MinimapArgs map)
        {
            _minimap = map;
            if (map == null)
            {
                UiLog.Warn($"收到 `{Events.MapGenerated}` 但参数为 null ⇒ 小地图缓存清空（Map 模块请检查 BuildMinimap）");
                return;
            }
            UiLog.Info($"HUD 已缓存小地图快照（{map.width}×{map.height} seed={map.seed}）⇒ 打开自动地图时直接传入");

            // ★ agent-a3：**进图那一次**的区域名触发点（`MinimapArgs.areaId`）。
            //   为什么用这里：进图时 `Events.AreaChanged` **不发**（它只在过门时发，见 `AppFlow.EnterArea`），
            //   而 `AppSnapshots.Broadcast("StageEntered")` 在 HUD 打开之后会补发一次 `MapGenerated`
            //   （`App/AppSnapshots.cs:123-128`）⇒ 这是 UI 侧能拿到的"进图后当前区域"的唯一权威来源。
            //   重复触发（同一区域）由 `LevelEntryTitle` 的"本局首次"去重挡掉。
            TriggerLevelEntryTitle((AreaId)map.areaId, "Events.MapGenerated（进图快照）");
        }

        /// <summary>
        /// ★ agent-a3：过门换区 ⇒ 可能弹区域名（`AppFlow.EnterArea` 在换区完成后发本事件）。
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
        /// 关键点：`MiniMapPanel` 必须拿到缓存的 `MinimapArgs`（**不再传 null**，agent-13 §B-3）。
        /// </summary>
        public static object PanelParam(string panelName, PlayerStatsDto stats, InventoryChangedArgs inventory,
            SkillTreeArgs tree, QuestStateDto quest, MinimapArgs minimap)
        {
            switch (panelName)
            {
                case nameof(InventoryPanel): return inventory;
                case nameof(CharacterPanel): return stats;
                case nameof(SkillTreePanel): return tree;
                case nameof(QuestLogPanel): return quest;
                case nameof(MiniMapPanel): return minimap;
                default: return null;
            }
        }

        /// <summary>面板开关统一入口（参数 = 面板类名；`HudPanel` 自己也支持）。</summary>
        private void OnPanelToggle(string panelName)
        {
            var param = PanelParam(panelName, _stats, _inventory, _tree, _quest, _minimap);

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
                case nameof(QuestLogPanel):
                    Toggle<QuestLogPanel>(param);
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
