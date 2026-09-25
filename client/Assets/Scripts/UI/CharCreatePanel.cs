// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/CharCreatePanel.cs
// 站点：CharCreate。层：Normal。预制体：`Resources/UI/CharCreatePanel`。
//
//   （依据 `Prefabs/Menu/ClassSelectMenu.prefab` 的 `ClassSelectMenu/Canvas` + 子节点）：
//     · 背景：原版 `class_select_screen`（800×600）按高度 ×1.8 = 1440×1080 水平居中（不横向拉伸）
//     · 标题 = 原版 `SelectHeroClass` 文本框 (0,267) 493×30 → 本工程 (0,480.6) 887.4×54  `SELECT HERO CLASS`
//     · 名字框 = 贴在与原版 `ClassName` 文本框**同一行**（原版 (0,198) 493×30 → (0,356.4) 887.4×54）
//     · 职业说明 = 原版 `ClassDescription` 文本框 (1,155) 300×50 → 本工程 (1.8,279) 540×90
//     · 底部「返回」= 原版 `ExitButton`(-300,-250)、「确定」= 原版 `OkButton`(300,-250) 的精确矩形
//
//   半身像三态素材取自 `data/global/ui/FrontEnd/{cls}/{CLS}{NU1,NU2,NU3}.DC6`：
//        · 默认 = `NU1`（背面待机）—— 原版 `ClassSelector.cs:258 ChangeState(BackIdle)`；
//        · 悬停 = `NU2` —— `ClassSelector.cs:116-122 ToggleHover()`；
//        · 选中 = `NU3`（转正面待机）—— `ClassSelector.cs:53-70` 转到 `FrontIdle`；
//        · 再点同一职业 = 取消选中；**未选中时「确定」置灰**（`ClassSelectMenu.cs:110-111`）。
//      几何 = 原版热点 `anchoredPosition` + 该态 DC6 帧 0 的 `offset`（推导与出处见
//      `UiLayoutFlow.ClassMenu.PortraitState` 的注释），点击区 = 原版热点矩形本身（透明）。
//      原版点选后还有一段"转身过渡动画"（`{CLS}FW` 54 帧 / 背面 `{CLS}BW`），逐帧尺寸不同
//      ⇒ 本屏照播（尺寸表见下 ①）。
//
//   本屏**没有**的属性点分配与难度控件：
//      · 属性点（4 组「标签 / − / 值 / +」+「剩余属性点」+「生命/法力/耐力预览行」）：
//        原版创角屏**没有**属性分配，属性点是**升级后**在 `C`（人物属性）面板里加的
//        ⇒ 本工程照此：`CharacterSave.statPoints = 0`，每级 +5 由 `Module/Player/PlayerStats.cs` 发放。
//      · 难度：`Def.CharacterSave` 无难度字段（Core/Def 契约冻结，不许改），且**没有坐标出处**
//        （参考工程 `ClassSelectMenu.prefab` 上**没有难度控件**，UI 层 grep `difficulty|Nightmare|Hell` 0 命中）。
//   说明行 = 显示**当前高亮/选中职业的原版说明**（英文，出处见 `UiLayoutFlow.ClassText`）。
//   职业名文本行（原版 `ClassName` Text）**本屏没有**：那一行被本项目的**名字输入框**占用。
//   · 职业初始四维与成长系数**只来自配表 `class_c`**：由 Flow 查表后经 `OnOpen(param)` 传入
//     （`ClassEntry`），面板不查表、不硬编码任何职业数值。
//   · 「确定」把结果打包成 `Def.CharacterSave` 发 `Events.CharCreateRequest`（Flow 负责落盘/入列表）。
// 入口：CharSelect 的【新建角色】；出口：确定 → CharSelect、返回 → CharSelect。
// 不引用任何业务模块。
//
//   ① 「**创建人物时候，点击人物动画变形，很诡异**」
//      原版过渡帧的尺寸**逐帧不同**（Amazon `fw_0` 118×198 → `fw_21` 215×228 → `fw_53` 121×234），
//      每一帧按**该帧原生尺寸**摆放（不是塞进帧 0 的 118×198 画框，那会把 215×228 横向压掉 45%）。
//      尺寸表 = `UiLayoutFlow.ClassMenu.Transition`（值 = 工程内导出 PNG 实测，与本屏三态同源；
//      面板里不许再算/写任何尺寸魔数）。
//
//   ② 「**输入框输入…数字的时候会有奇怪的粘连**」
//      `caretPosition` setter **必然抛**（`InputField.cs:1082-1113` 的 `selectionAnchorPosition` /
//      `selectionFocusPosition` setter 要读 `compositionString`，而 `:343-346` 在无人设
//      `inputOverride` 时回落到 `UnityEngine.Input.compositionString` ⇒ `InvalidOperationException`），
//      而 `text` setter → `SetText` → `UpdateLabel` 在**该框是 EventSystem 选中项**时也会读同一处
//      （`InputField.cs:2690`）⇒ 抛异常沿 `onTextInput → OnNameChar → PushName` 逃出，
//      **这一次键入的字符/光标更新整段丢掉** ⇒ 表现为"字符粘连、丢字、光标不动"。
//      本屏据此：① **输入框退出交互**（不可射线命中 + 关键盘导航 ⇒ 永不成为选中项 ⇒ 上面两条抛点不可达）；
//      ② **可见文本 + 插入点光标完全由本面板驱动**（`_nameBuffer` + `_nameCaret` + 插入点上的光标标记，
//      见 `DisplayName`）；③ 面板关闭时置 `_nameEditing = false`（不再吃全局按键）。
//
//   ③ 默认名与用户输入**共用一个字符串**：
//   名字框开屏即预填 `Cfg.DefaultPlayerName`（= `Hero`，出处 `client/Assets/Configs/config.json:3`
//   → `Core/ClientConfig.cs:110-122` → `Module/Flow/AppFlow.cs:564` → `Args.defaultName`）。
//   既有口径的观察值（日志与截图）：
//     · `:325/:326` `CC-OPEN/CC-READY … nameBuffer="Hero" nameCaret=4 nameEditing=True`
//     · `:752` `NAME where=after-typing typed="Ama65x" buffer="HeroAma65x" digitsInBuffer=1 visible_input="HeroAma65x|" caret=10`
//     · 实机截图：`NAME: HeroAma65x|`
//
//      本屏的编辑语义（`EditNameDefault`）：
//     ① 键入任一有效字符 ⇒ **整个缓冲被该字符替换**（`Hero` → `A`），之后才是追加；
//     ② 未首次键入时退格/Delete **不生效**（语义 = 预填默认名当"空字段"：原版名字框初始为空、
//        空字段上退格/Delete 无效果）⇒ 默认名永不会被删成残缺（不存在 `Hero`→`Her`）；
//     ③ ←/→/Home/End 只移动插入点、**不触发**替换（它们不改文本）；
//     ④ 一旦首次键入发生，之后全部走 `EditName` 的常规语义。
//      不引入 `UnityEngine.Input` / `Keyboard.current` 直连（只走 `Keyboard.onTextInput`）。
//   离线断言在 `tools/probes/hosts/uicheck`（节 ⑱：默认态 / 首次键入 / 连续键入 / 删除键与方向键不触发替换）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;          // ★ 只在本工程当前配置（只用新 Input System）下编进来，见 OnUpdate 注释
#endif

namespace Diablo2.UI
{
    /// <summary>创建角色面板（原版半身像横排选职业 + 名字；1:1 复刻原版创角屏骨架）。</summary>
    public class CharCreatePanel : UIPanel
    {
        /// <summary>一个职业的初始四维与成长系数（值全部来自配表 `class_c`）。</summary>
        public class ClassEntry
        {
            /// <summary>`class_c` 主键（1-5，= `PlayerClass` 取值）。</summary>
            public int id;

            /// <summary>职业中文名。</summary>
            public string name;

            /// <summary>初始力量。</summary>
            public int str;

            /// <summary>初始敏捷。</summary>
            public int dex;

            /// <summary>初始体力。</summary>
            public int vit;

            /// <summary>初始精力。</summary>
            public int eng;

            /// <summary>
            /// 每级可分配属性点（`class_c.stat_per_lvl`）。
            /// <para>创角屏**不再用它**（原版创角没有属性分配）——
            /// ⇒ 只作 Flow 侧的搬运字段存在，本面板读都不读。</para>
            /// </summary>
            public int statPerLvl;

            /// <summary>每级生命成长（`class_c.life_per_lvl`）。</summary>
            public float lifePerLvl;

            /// <summary>每级法力成长（`class_c.mana_per_lvl`）。</summary>
            public float manaPerLvl;

            /// <summary>每级耐力成长（`class_c.stam_per_lvl`）。</summary>
            public float stamPerLvl;

            /// <summary>每点体力 = 生命（`class_c.life_per_vit`）。</summary>
            public float lifePerVit;

            /// <summary>每点精力 = 法力（`class_c.mana_per_mag`）。</summary>
            public float manaPerMag;

            /// <summary>每点体力 = 耐力（`class_c.stam_per_vit`）。</summary>
            public float stamPerVit;

            /// <summary>
            /// 起始生命加成（`class_c.hp_add`，官方 `charstats.hpadd`）。
            /// <para>从配表读入（真值只在表里，代码里不写常量）。</para>
            /// </summary>
            public int hpAdd;

            /// <summary>
            /// 起始耐力上限（`class_c.base_stamina`，官方 `charstats.stamina`）。
            /// </summary>
            public int baseStamina;
        }

        /// <summary>`OnOpen(param)` 参数。</summary>
        public class Args
        {
            /// <summary>5 个职业（按 `class_c` 主键升序）。</summary>
            public List<ClassEntry> classes = new List<ClassEntry>();

            /// <summary>
            /// 名字输入框的默认值（来自 `Cfg.DefaultPlayerName`，本工程 = `Hero`）。
            /// <para>它是**整体单元** —— 首次键入任一有效字符时整个被替换，不是"前缀"
            /// （见 `EditNameDefault` 与文件头 ③ 一节）。</para>
            /// </summary>
            public string defaultName;
        }

        /// <summary>官方（`charstats.txt`）角色名长度上限。</summary>
        private const int NameMaxLength = 15;

        /// <summary>文案（英文走原版位图字体）。</summary>
        private static class Text
        {
            public const string Title = "SELECT HERO CLASS";
            public const string Name = "NAME:";
            public const string Ok = "OK";
            public const string Back = "BACK";
        }

        /// <summary>该槽**没有对应职业**（配表里查不到 ⇒ 半身像与点击区都不显示；见 `SlotAvailable`）。</summary>
        private const int NoClass = -1;

        /// <summary>
        /// 5 个槽位 → 职业 id。顺序 = **原版屏上从左到右的热点顺序**
        /// （Amazon / Necromancer / Barbarian / Paladin / Sorceress；
        /// 依据 `ClassSelectMenu.prefab` 的 7 个热点 x 坐标 −299 / −99 / +1 / +122 / +226，
        /// 刺客/德鲁伊两槽本项目没有对应职业 ⇒ 空着）。
        /// <para>
        /// 其余三个职业的**素材已删**（`Chars/{necromancer,paladin,sorceress}`、
        /// `UI/FrontEnd/{...}`）⇒ 它们那三个槽位填 <see cref="NoClass"/>（该槽不显示、不可点）。
        /// </para>
        /// <para>
        /// **为什么保留 5 个槽位、不做"2 个居中重排"**：这 5 个 x 坐标是**原版几何**
        /// （`ClassSelectMenu.prefab` 的热点表）。把它们挪成"两个人居中"= **写不出出处的量**
        /// </para>
        /// <para>
        /// `Def.PlayerClass` 的枚举值一个都没删（存档 `cls` 字段与配表 `class` 列依赖它）——
        /// 这里是**素材/UI 层面的收窄**，不是"这个职业不存在"。
        /// </para>
        /// </summary>
        private static readonly int[] SlotClassIds =
        {
            (int)PlayerClass.Amazon,       // 原版热点 #0（x 原版 −299）
            NoClass,                       // 原版热点 #1 = Necromancer 位（用户决策：素材已删）
            (int)PlayerClass.Barbarian,    // 原版热点 #2（x 原版 +1）
            NoClass,                       // 原版热点 #3 = Paladin 位（用户决策：素材已删）
            NoClass,                       // 原版热点 #4 = Sorceress 位（用户决策：素材已删）
        };

        private readonly List<ClassEntry> _classes = new List<ClassEntry>();

        /// <summary>槽位 → 面板里的职业下标（-1 = 该槽没有对应职业数据）。</summary>
        private readonly int[] _slotOfId = new int[6];

        /// <summary>槽位的半身像 Image（三态共用同一个节点：换 sprite + 换矩形）。</summary>
        private readonly Image[] _portrait = new Image[SlotClassIds.Length];

        /// <summary>槽位当前的显示状态（`ResPaths.Portrait` 的 0/1/2），用于避免重复贴图。</summary>
        private readonly int[] _shownState = new int[SlotClassIds.Length];

        /// <summary>选中的职业下标（-1 = 还没选，此时「确定」置灰 —— 原版 `okButton.Disabled = true`）。</summary>
        private int _classIndex = -1;

        /// <summary>当前悬停的槽位（-1 = 没有）。</summary>
        private int _hoverSlot = -1;

        //   出处：`ClassSelector.cs:207-233`（`{CLS}FW` / `{CLS}BW`，`Loop = false`、
        //         `HideOnFinish = true`、**`Fps = 25`**）+ `:53-76 MainAnimatorOnFinish`（播完切终态）。
        //   为什么放在面板里而不是新开一个播放器：原版就是**同一个 ClassSelector 上的一个 SpriteAnimator**
        //   ⇒ 这里同构实现，不引入新系统。

        /// <summary>正在播过渡的槽位（-1 = 没有）。同一时刻只允许一个槽在播（原版每屏只有一个选中项）。</summary>
        private int _transitionSlot = -1;

        /// <summary>正在播的过渡码（`ResPaths.Portrait.TransitionFront` = `fw` / `TransitionBack` = `bw`）。</summary>
        private string _transitionCode = string.Empty;

        /// <summary>过渡已播秒数（帧号 = `⌊已播秒数 × 25⌋`，见 `ResPaths.Portrait.TransitionFps`）。</summary>
        private float _transitionElapsed;

        /// <summary>过渡的帧数（= 该职业该序列的 PNG 张数；≤0 表示未知 ⇒ 直接落终态）。</summary>
        private int _transitionFrameCount;

        /// <summary>过渡播完后要落到哪个待机态（`NU3` 正面 / `NU1` 背面）。</summary>
        private int _transitionEndState = ResPaths.Portrait.Idle;

        private InputField _nameInput;
        private UiLayoutFlow.FlowLabel _descLabel;
        private UiLayoutFlow.FlowButton _okButton;
        private bool _built;

        /// <summary>
        /// 名字框是否可输入。
        /// <para>**关闭面板时必须置 false**（`OnClose`）—— 否则面板关了还继续吃全局 `onTextInput`。</para>
        /// </summary>
        private bool _nameEditing = true;

        /// <summary>名字**真值**缓冲（本工程配置下 uGUI 自己收不到字符 ⇒ 面板自己管；`OnConfirm` 读它不读输入框）。</summary>
        private string _nameBuffer = string.Empty;

        /// <summary>插入点位置（0..<see cref="_nameBuffer"/>.Length）—— 也是**可见光标**的位置。</summary>
        private int _nameCaret;

        /// <summary>
        /// 「缓冲仍是**未被取代的默认名**」标记。
        /// <para>为 true ⇒ 下一次**键入有效字符**会把整个缓冲替换成那个字符（`Hero` → `A`），
        /// 而不是追加（追加会得到 `HeroA` ⇒ `HeroAma65x`，即用户报的"粘连"）。</para>
        /// <para>只在 <see cref="OnOpen"/> 由"默认名非空"置位，之后由 <see cref="EditNameDefault"/> 维护：
        /// **只有"真的键入了一个字符"才置 false** ⇒ 退格/Delete/方向键/鼠标都不会消掉它。</para>
        /// </summary>
        private bool _nameDefaultPending;

        /// <summary>「未首次键入时按了删除键 ⇒ 不生效」这条语义只说一次的日志已报否。</summary>
        private bool _nameDefaultWarned;

        /// <summary>
        /// 可见光标标记（插在插入点上的字符）。
        /// <para>为什么用字符而不是 uGUI 自己的光标：那个光标要 `InputField.caretPosition` 才能移动，
        /// 而该 setter 在本工程配置下**必然抛**（见文件头 ②），且它的光标 mesh 只在
        /// `m_AllowInput`（= 聚焦）时才画。把光标做成**显示串里插一个字符**⇒ 位置恒等于插入点、
        /// 离线就能断言（`DisplayName` 是纯函数），不依赖任何字体度量。</para>
        /// </summary>
        public const string CaretMark = "|";

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        private Keyboard _nameKeyboard;
#endif
        private bool _nameKbWarned;
        private bool _nameLimitWarned;
        /// <summary>「名字里出现不能当文件名的字符」是否已警告过（只报一次；判据见 <see cref="IsNameCharAllowed"/>）。</summary>
        private bool _nameCharWarned;
        private bool _namePushWarned;
        private bool _noSelectionWarned;

        //   为什么用 static：面板会被反复打开 ⇒ 实例字段每次开屏都会再报一遍，那就不是"只报一次"了。
        private static bool _r1cMorphLogged;
        private static bool _r1cInputLogged;

        /// <summary>默认名语义（首次键入整体替换 / 未首次键入时删除键不生效）只报一次。</summary>
        private static bool _r1fNameLogged;

        /// <summary>
        /// 层：<see cref="UILayer.Normal"/>（= 文件头「层：Normal」）。
        /// </summary>
        public override UILayer Layer => UILayer.Normal;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();

            var args = param as Args;
            if (args == null)
            {
                Log.Error("Ui", "CharCreatePanel.OnOpen 未收到 Args ⇒ 无职业数据，创角不可用（见 Flow 日志）");
                _classes.Clear();
            }
            else
            {
                _classes.Clear();
                if (args.classes != null) _classes.AddRange(args.classes);
            }

            if (_classes.Count == 0)
            {
                Log.Error("Ui", "创角：职业数据为空（配表 class_c 未加载？）——「确定」保持禁用并置灰半身像");
            }

            // **缓冲是真值**（不从输入框回读 —— 输入框只是显示层，见 PushName）
            _nameBuffer = args != null && !string.IsNullOrEmpty(args.defaultName)
                ? args.defaultName
                : string.Empty;
            _nameCaret = _nameBuffer.Length;
            _nameEditing = true;
            // 缓冲 == 默认名 ⇒ 标记"默认名尚未被键入取代"（首个有效字符会**整体**替换它，
            //   见 `EditNameDefault`）；默认名为空 ⇒ 无需标记（typed 与 append 结果相同）。
            _nameDefaultPending = _nameBuffer.Length > 0;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            SubscribeNameInput();
#endif
            PushName(false);        // 把缓冲写到显示层（含插入点上的光标标记）

            // 原版进屏状态：**什么都没选中**（半身像全 `NU1`、说明行空、「确定」置灰）
            _classIndex = -1;
            _hoverSlot = -1;
            BindSlots();
            Refresh();

            UiLayoutFlow.LogTable(nameof(CharCreatePanel));
            //   但 `class_c` 配表**故意保留 5 行**（`Def.PlayerClass` 枚举值与存档 `cls` 字段依赖它，
            //   删行会破坏"旧存档仍可读"这条兼容性）。
            var slotCount = 0;
            for (var i = 0; i < SlotClassIds.Length; i++)
                if (SlotClassIds[i] >= 0) slotCount++;

            Log.Info("Ui", $"创角面板已打开：屏上可选 {slotCount} 个职业（`class_c` 配表共 {_classes.Count} 行；" +
                           "按用户 2026-09-19 决策只做 Amazon + Barbarian，其余三个职业的素材已删、枚举值保留）；" +
                           "半身像 = 原版 NU1/NU2/NU3 三态 + FW/BW 两段转身过渡；" +
                           $"默认名字「{_nameBuffer}」；初始状态 = 未选职业");

            //   为什么写在 OnOpen：这个时刻"接线已就绪、素材表已可用"，且 static 标志保证整进程只报一次。
            if (!_r1cMorphLogged)
            {
                _r1cMorphLogged = true;
                var frames = 0;
                for (var s = 0; s < UiLayoutFlow.ClassMenu.Transition.SlotIds.Length; s++)
                {
                    var slot = UiLayoutFlow.ClassMenu.Transition.SlotIds[s];
                    for (var c = 0; c < UiLayoutFlow.ClassMenu.Transition.Codes.Length; c++)
                        frames += UiLayoutFlow.ClassMenu.Transition.FrameCount(
                            slot, UiLayoutFlow.ClassMenu.Transition.Codes[c]);
                }
                Log.Info("R1-C", "创角半身像转身过渡：**逐帧矩形** = 该帧原生尺寸 ×" + UiLayoutFlow.Scale +
                    "（尺寸表 = UiLayoutFlow.ClassMenu.Transition，值 = 导出 PNG 实测，与三态同源；共 " +
                    frames + " 帧）；每帧同步 sizeDelta + anchoredPosition" +
                    "（旧实现把每一帧塞进帧 0 的画框 ⇒ 215×228 的帧被压进 118×198，即用户报的\"变形\"）；" +
                    "UiArt.SetSprite 已加请求守卫（同一 Image 只有最新一次请求的回调落地）；" +
                    "过渡帧已预热 ⇒ 首帧同步生效");
            }
            if (!_r1cInputLogged)
            {
                _r1cInputLogged = true;
                Log.Info("R1-C", "创角名字输入：可见文本 + 插入点光标**由面板自己驱动**" +
                    "（缓冲 = 真值；显示串 = 缓冲在插入点插一个「" + CaretMark + "」）⇒ 光标位置恒等于插入点；" +
                    "输入框已退出交互（不可射线命中 + 关键盘导航 ⇒ 永不成为 EventSystem 选中项）" +
                    "⇒ uGUI InputField 的 `caretPosition` / 聚焦时 `text` 两条抛点不可达；" +
                    "OnClose 置 _nameEditing=false ⇒ 面板关闭后不再吃全局键入");
            }
            if (!_r1fNameLogged)
            {
                _r1fNameLogged = true;
                Log.Info("Ui", "R1-F 创角名字输入：默认名「" + _nameBuffer + "」是**整体单元** —— " +
                    "首次键入任一有效字符 ⇒ 默认名**整体被该字符替换**（之后才是追加）；" +
                    "未首次键入时退格/Delete **不生效**（语义 = 把预填默认名当\"空字段\"：原版名字框初始为空，" +
                    "空字段上退格/Delete 无效果 ⇒ ⛔ 默认名永不会被删成残缺）；" +
                    "←/→/Home/End 只移动插入点、不触发替换；" +
                    "⛔ 光标仍由显示串承载（源码 0 处 `caretPosition` setter）");
            }
        }

        /// <inheritdoc/>
        public override void OnClose()
        {
            //   （外加 `Keyboard.onTextInput` 的退订），面板关了还会往缓冲里插字符。
            _nameEditing = false;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            UnsubscribeNameInput();
#endif
            Log.Info("Ui", $"创角面板已关闭（最终名字「{_nameBuffer}」；已置 _nameEditing=false 并退订全局键入）");
        }

        /// <summary>
        /// <para>
        /// 本工程 `ProjectSettings.asset` 是 `activeInputHandler: 1`（**只用新 Input System**），
        /// 而 uGUI `InputField` 取字符那条路走
        /// `EventSystem.currentInputModule.input`（= `BaseInput`，`BaseInput.cs:13`
        /// `compositionString => Input.compositionString`）→ `UnityEngine.Input`，
        /// 在本配置下**直接抛**：
        /// `InvalidOperationException: You are trying to read Input using the UnityEngine.Input class,
        ///  but you have switched active Input handling to Input System package in Player Settings.`
        /// ⇒ 无论是否聚焦、是否 `interactable`，输入框永远停在初始值
        /// （Play 实测：点中输入框后 `InputField.isFocused == true`，注入 A/C/C 后文本仍 'Hero'）。
        /// </para>
        /// <para>
        /// 因此由面板自己从 `Keyboard.onTextInput` 收字符（真实键入走的就是同一条路），
        /// 自己维护 `_nameBuffer` / `_nameCaret`，并把**显示串**刷到输入框（<see cref="PushName"/>）；
        /// `OnConfirm` 读的是**缓冲**（不再读输入框 —— 那儿装着含光标标记的显示串）。
        /// 若以后把 Player Settings 改成 Both（旧输入可用），这段会被 `#if` 编掉、交回 uGUI 原生行为，
        /// 不会出现「一个字符打两遍」。
        /// </para>
        /// </summary>
        public override void OnUpdate(float dt)
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            PollNameKeys();
#endif
            TickTransition(dt);          // 推进转身过渡（`FW`/`BW`，25fps，播完落终态）
        }

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        /// <summary>每帧轮询功能键（字符走 <see cref="OnNameChar"/> 的文本事件）。</summary>
        private void PollNameKeys()
        {
            SubscribeNameInput();                 // 键盘设备换了（重连）⇒ 重订阅

            var kb = Keyboard.current;
            if (kb == null || !_nameEditing) return;

            if (kb.backspaceKey.wasPressedThisFrame) BackspaceName();
            if (kb.deleteKey.wasPressedThisFrame) DeleteName();
            if (kb.leftArrowKey.wasPressedThisFrame) MoveCaret("left");
            if (kb.rightArrowKey.wasPressedThisFrame) MoveCaret("right");
            if (kb.homeKey.wasPressedThisFrame) MoveCaret("home");
            if (kb.endKey.wasPressedThisFrame) MoveCaret("end");
        }

        private void SubscribeNameInput()
        {
            var kb = Keyboard.current;
            if (kb == null)
            {
                if (!_nameKbWarned)
                {
                    _nameKbWarned = true;
                    Log.Warn("Ui", "创角：Keyboard.current 为 null ⇒ 名字暂时无法键入（只报一次）");
                }
                return;
            }
            if (_nameKeyboard == kb) return;

            if (_nameKeyboard != null) _nameKeyboard.onTextInput -= OnNameChar;
            _nameKeyboard = kb;
            _nameKeyboard.onTextInput += OnNameChar;
            Log.Info("Ui", $"创角名字输入已接管（新 Input System 设备「{kb.name}」；"
                           + "uGUI InputField 在 activeInputHandler=1 下收不到字符，见 CharCreatePanel.OnUpdate 注释）");
        }

        private void UnsubscribeNameInput()
        {
            if (_nameKeyboard == null) return;
            _nameKeyboard.onTextInput -= OnNameChar;
            _nameKeyboard = null;
        }

        /// <summary>文本事件（真实键入的字符就是从这里来的）。控制字符交给按键轮询。</summary>
        private void OnNameChar(char c)
        {
            if (!_nameEditing || !isActiveAndEnabled) return;
            if (c < ' ' || c == (char)127) return;

            // 名字会被当成存档槽位的**文件名**（见 `Module/Save/SaveModule.cs` 文件头「已知边界」）
            // ⇒ 这里只放行"能当文件名"的字符；其余**拒绝并只报一次**（不静默吞掉）
            if (!IsNameCharAllowed(c))
            {
                if (!_nameCharWarned)
                {
                    _nameCharWarned = true;
                    Log.Warn("Ui", "创角：名字不接受字符「" + c + "」（只收字母 / 数字 / 中文 / `_` / `-`）"
                        + "—— 存档槽位键 = 角色名，不能含文件系统不接受的字符（只报一次）");
                }
                return;
            }

            InsertName(c.ToString());
        }
#endif

        // ── 名字编辑（纯函数算文本 + 单一出口写回 uGUI 输入框）────────────────

        /// <summary>
        /// 名字允许的字符：**字母 / 数字（含中文等 Unicode 字母）+ `_` / `-`**。
        /// <para>判据来自**存档侧**：角色名就是存档槽位的**文件名**（`Module/Save/SaveModule.cs` →
        /// 引擎 `CloverEngine.FileSlotStore` 的 key 校验：不许 `/` `\` 与非法文件名字符）⇒ 这里挡掉
        /// 文件系统不接受的字符（`:` `*` `?` `"` `<` `>` `|` 等），中文等 Unicode 字母照常放行。</para>
        /// <para>校验刻意放在**键入这一处**（`OnNameChar`）而不是 `EditName` 纯函数里：`EditName`
        /// 保持"只钳长度、不看字符集"，免得动到 `tools/probes/hosts/uicheck` 已有的纯函数断言。</para>
        /// </summary>
        public static bool IsNameCharAllowed(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '-';
        }

        /// <summary>
        /// 名字里**第一个**"不能当文件名"的字符；全合法（或空串）⇒ `'\0'`。
        /// 判据同 <see cref="IsNameCharAllowed"/>；供「确定」在提交前兜一道（见 `OnConfirm`）。
        /// </summary>
        public static char FirstDisallowedNameChar(string name)
        {
            if (string.IsNullOrEmpty(name)) return '\0';
            for (var i = 0; i < name.Length; i++)
            {
                if (!IsNameCharAllowed(name[i])) return name[i];
            }
            return '\0';
        }

        /// <summary>
        /// **纯函数**：名字的一次编辑（离线自检宿主可直接断言，见 `tools/probes/hosts/uicheck`；边界钳制只写在这一处）。
        /// </summary>
        /// <param name="text">当前文本。</param>
        /// <param name="caret">当前光标（0..text.Length）。</param>
        /// <param name="op">`ins` 插入 / `back` 退格 / `del` 前删 / `left`·`right`·`home`·`end` 移动光标。</param>
        /// <param name="arg">`ins` 的插入内容（其余操作忽略）。</param>
        /// <param name="maxLength">长度上限（`NameMaxLength` = 15，官方 charstats 口径）。</param>
        /// <param name="newCaret">输出：编辑后的光标位置。</param>
        public static string EditName(string text, int caret, string op, string arg, int maxLength, out int newCaret)
        {
            text = text ?? string.Empty;
            caret = Mathf.Clamp(caret, 0, text.Length);

            switch (op)
            {
                case "ins":
                    if (!string.IsNullOrEmpty(arg) && text.Length + arg.Length <= maxLength)
                    {
                        text = text.Insert(caret, arg);
                        caret += arg.Length;
                    }
                    break;
                case "back":
                    if (caret > 0) { text = text.Remove(caret - 1, 1); caret--; }
                    break;
                case "del":
                    if (caret < text.Length) text = text.Remove(caret, 1);
                    break;
                case "left": caret = Mathf.Max(0, caret - 1); break;
                case "right": caret = Mathf.Min(text.Length, caret + 1); break;
                case "home": caret = 0; break;
                case "end": caret = text.Length; break;
                default:
                    // 非预期分支：未登记的操作 ⇒ 文本不变 + 点一次名（不静默吞掉）
                    UiLog.WarnOnce("name.op." + op, $"创角名字编辑收到未知操作「{op}」⇒ 文本不变（只报一次）");
                    break;
            }

            newCaret = Mathf.Clamp(caret, 0, text.Length);
            return text;
        }

        /// <summary>
        /// **纯函数**：带「默认名整体替换」语义的一次名字编辑
        /// （<see cref="EditName"/> 保持"只钳长度、只看操作的通用编辑"不变，本函数才是面板实际走的那条路）。
        /// <para>
        /// **为什么需要它**：`Args.defaultName`（本工程 = `Cfg.DefaultPlayerName` = `Hero`，
        /// 出处 `client/Assets/Configs/config.json:3`）会被预填进名字框（既有口径：开屏可见文本 == `Hero|`，
        /// 日志也这么写）。默认名预填后，第一个键入的字符若按"追加在插入点上"处理 ⇒ 用户看到 `HeroAma65x`
        /// ⇒ 本函数把默认名当成一个整体单元（见下）。
        /// </para>
        /// <para>
        /// **本函数把默认名当成一个整体单元**（<paramref name="defaultPending"/> = true 表示它还没被取代）：
        /// <list type="number">
        /// <item>键入有效字符（`op == "ins"` 且 `arg` 非空）⇒ **整个缓冲被该内容替换**（`Hero` → `A`），
        ///       <paramref name="newPending"/> 随之置 false，之后才是常规追加；</item>
        /// <item>退格 / Delete ⇒ **不生效**（文本与插入点都不动）—— 语义 = 把预填默认名当作**"空字段"**：
        ///       原版名字框初始为空，空字段上退格/Delete 本来就没有效果。这样**默认名永远不会被删成残缺**
        ///       （不存在 `Hero` → `Her` 这种半截状态），也就**不需要**第三套"半删的默认名"状态；</item>
        /// <item>←/→/Home/End ⇒ 照常移动插入点，**不触发**首次替换（它们不改文本 ⇒ 不可能产生粘连）；</item>
        /// <item>一旦 <paramref name="defaultPending"/> 变 false，之后**全部**走 <see cref="EditName"/>
        ///       的常规语义（追加 / 删除 / 移动）。</item>
        /// </list>
        /// </para>
        /// <para>不许把第 2 条改成"退格删掉默认名最后一个字符"（会产生 `Hero` → `Her` 这种"半截默认名"）。
        /// 也不许把首次替换做成"清空后插入"以外的任何形态（例如只删 `Hero` 再追加）—— 那会多出中间态。</para>
        /// </summary>
        /// <param name="text">当前缓冲。</param>
        /// <param name="caret">当前插入点（0..text.Length）。</param>
        /// <param name="defaultPending">缓冲是否仍是**未被取代的默认名**（字段 <see cref="_nameDefaultPending"/>）。</param>
        /// <param name="op">同 <see cref="EditName"/>（`ins` / `back` / `del` / `left` / `right` / `home` / `end`）。</param>
        /// <param name="arg">同 <see cref="EditName"/>（`ins` 的插入内容）。</param>
        /// <param name="maxLength">长度上限（`NameMaxLength` = 15，官方 charstats 口径）。</param>
        /// <param name="newCaret">输出：编辑后的插入点位置。</param>
        /// <param name="newPending">输出：编辑后 `defaultPending` 的取值（**只有"真的键入了一个字符"才会置 false**）。</param>
        public static string EditNameDefault(string text, int caret, bool defaultPending, string op, string arg,
            int maxLength, out int newCaret, out bool newPending)
        {
            text = text ?? string.Empty;
            caret = Mathf.Clamp(caret, 0, text.Length);

            // "真的键入了一个字符"才算首次键入（`inserting`）；其余操作一律不改这一步的语义。
            var inserting = op == "ins" && !string.IsNullOrEmpty(arg);
            newPending = defaultPending && !inserting;

            if (defaultPending && inserting)
            {
                text = arg.Length <= maxLength ? arg : arg.Substring(0, maxLength);
                newCaret = text.Length;
                return text;
            }

            if (defaultPending && (op == "back" || op == "del"))
            {
                // ② 未首次键入时删除键不生效（预填默认名按"空字段"语义；见 summary 第 2 条）
                newCaret = caret;
                return text;
            }

            // ③/④ 其余（方向键 / Home / End / 已非 pending 的任意操作）走通用编辑
            text = EditName(text, caret, op, arg, maxLength, out caret);
            newCaret = Mathf.Clamp(caret, 0, text.Length);
            return text;
        }

        private void InsertName(string s) => ApplyNameEdit("ins", s);
        private void BackspaceName() => ApplyNameEdit("back", null);
        private void DeleteName() => ApplyNameEdit("del", null);
        private void MoveCaret(string op) => ApplyNameEdit(op, null);

        private void ApplyNameEdit(string op, string arg)
        {
            var before = _nameBuffer;
            var wasPending = _nameDefaultPending;
            int caret;
            // 走带"默认名整体替换"语义的那条路（`EditName` 仍是它内部的通用编辑）
            _nameBuffer = EditNameDefault(_nameBuffer, _nameCaret, _nameDefaultPending, op, arg, NameMaxLength,
                out caret, out _nameDefaultPending);
            _nameCaret = caret;

            // ── 未首次键入时的删除键**不生效**（文本与插入点都没动）──
            //   这不是"达长度上限"，报一次把语义说清（不静默吞掉，也不刷屏）。
            if (wasPending && (op == "back" || op == "del"))
            {
                if (!_nameDefaultWarned)
                {
                    _nameDefaultWarned = true;
                    Log.Info("Ui", $"R1-F 创角：默认名「{before}」尚未被键入取代 ⇒「{op}」不生效" +
                        "（预填默认名按\"空字段\"语义处理 —— 原版名字框初始为空、空字段上退格/Delete 无效果）；" +
                        "键入任一有效字符即**整体**替换它（只报一次）");
                }
                return;
            }

            if (wasPending && !_nameDefaultPending)
            {
                Log.Info("Ui", $"R1-F 创角：首次键入 ⇒ 默认名「{before}」**整体**被「{_nameBuffer}」替换" +
                    $"（旧行为是追加 ⇒ 会得到「{before}{_nameBuffer}」）；插入点 {_nameCaret}/{_nameBuffer.Length}，" +
                    $"显示「{DisplayName(_nameBuffer, _nameCaret)}」");
                PushName(true);
                return;
            }

            var textChanged = _nameBuffer != before;
            if (!textChanged && op == "ins")
            {
                // 非预期分支：想插入但文本没变（达长度上限）⇒ 报一次
                if (!_nameLimitWarned)
                {
                    _nameLimitWarned = true;
                    Log.Info("Ui", $"创角：名字已达上限 {NameMaxLength} 字 ⇒ 后续字符被忽略（只报一次）");
                }
                return;
            }

            PushName(textChanged);
        }

        /// <summary>
        /// **纯函数**：名字框的**显示串** = 缓冲 + 插入点上的光标标记（<see cref="CaretMark"/>）。
        /// <para>
        /// 为什么"光标"做成显示串里的一个字符（而不是 uGUI 那个光标）：
        /// ① 那个光标只能靠 `InputField.caretPosition` 移动，而该 setter 在本工程配置下**必抛**
        ///    （见文件头 ②）⇒ 它永远停在 0 位（= 用户看到的"光标不动 / 粘连"）；
        /// ② 它只在 `m_AllowInput`（聚焦）时才画，而本工程不让输入框获得焦点（无文本转发）；
        /// ③ 由面板把它当字符插进串里 ⇒ **位置恒等于插入点**、且是纯函数（离线宿主可逐例断言），
        ///    不需要任何字体度量/运行时 TextGenerator。
        /// </para>
        /// <para>空缓冲 ⇒ 返回空串：此时既有的**占位提示**（`输入角色名（最多 15 字）`）按原口径显示
        /// （它的显隐判据是 `!isFocused &amp;&amp; 输入框 text 为空`，见 `D2TextMirror`），
        /// 一有内容就显示光标标记。</para>
        /// </summary>
        public static string DisplayName(string text, int caret)
        {
            var t = text ?? string.Empty;
            if (t.Length == 0) return string.Empty;
            return t.Insert(Mathf.Clamp(caret, 0, t.Length), CaretMark);
        }

        /// <summary>
        /// 把缓冲（<see cref="_nameBuffer"/>，**真值**）刷到显示层（两段写，顺序有讲究）。
        /// <para>
        /// ① **同步给 `InputField.text`**（= 显示串）：
        ///    它是这个框的**字符串持有者**（`m_Text`），也是**占位提示的判据来源**
        ///    （`D2TextMirror` 按 `!isFocused &amp;&amp; text 为空` 显隐）；而且 uGUI 把 `UpdateLabel`
        ///    注册成 `textComponent` 的**重绘回调** ⇒ 只要 `m_Text` 就是显示串，
        ///    之后任何一次重建（字体回调 / 布局）都会把同一串写回可见层，**光标标记不会被抹掉**。
        ///    这一写仍走 `InputField.SetText → UpdateLabel`，而 `UpdateLabel` 里读 `Input.compositionString`
        ///    那一句**只在该框是 EventSystem 选中项时才执行**（`InputField.cs:2690` 的
        ///    `gameObject == currentSelectedGameObject` 短路）—— 本面板在 `UiArt.Input` 里关掉了射线命中与
        ///    键盘导航 ⇒ 它永远不会成为选中项 ⇒ 不抛。万一将来有人把它改回可聚焦：抛点被下面的 catch 收住。
        /// </para>
        /// <para>
        /// ② **最后再写一次 `textComponent.text`**（我们自己的显示层）：这一步与 `InputField` 的逻辑无关，
        ///    放在最后是为了让"可见文本 == 缓冲 + 插入点光标标记"**在①抛没抛的情况下都成立**
        ///    （①抛了的话，它内部的 `UpdateLabel` 可能停在旧串上）。
        /// </para>
        /// </summary>
        private void PushName(bool log)
        {
            var display = DisplayName(_nameBuffer, _nameCaret);

            if (_nameInput != null)
            {
                // ① 字符串持有者 + 占位提示判据（走到 uGUI 的 SetText→UpdateLabel；非聚焦态不会读到 compositionString）
                try
                {
                    if (_nameInput.text != display) _nameInput.text = display;
                }
                catch (Exception e)
                {
                    // 非预期分支：输入框不接受写入（例如被谁改回可聚焦 ⇒ UpdateLabel 读 compositionString）
                    // ⇒ ② 仍会把显示刷对（不会出现"字符丢/粘"），只报一次点名。
                    if (!_namePushWarned)
                    {
                        _namePushWarned = true;
                        Log.Warn("Ui", $"创角：写输入框失败（{e.GetType().Name}: {e.Message}）" +
                                      "⇒ 可见文本/光标仍由面板自己驱动（不受影响）；请检查输入框是否又变成了可聚焦控件（只报一次）");
                    }
                }

                // ② 显示层（最后兜一次，见 summary）
                if (_nameInput.textComponent != null && _nameInput.textComponent.text != display)
                    _nameInput.textComponent.text = display;
            }

            if (log)
                Log.Info("Ui", $"创角：名字 =「{_nameBuffer}」（插入点 {_nameCaret}/{_nameBuffer.Length}，" +
                               $"上限 {NameMaxLength}；显示「{display}」）");
        }

        // ── 槽位绑定 ────────────────────────────────────────────────────────

        /// <summary>把 5 个槽位绑到配表给的职业下标上（缺哪个职业就空哪个槽 + Warn 点名）。</summary>
        private void BindSlots()
        {
            for (var i = 0; i < _slotOfId.Length; i++) _slotOfId[i] = -1;
            for (var i = 0; i < _classes.Count; i++)
            {
                var id = _classes[i].id;
                if (id <= 0 || id >= _slotOfId.Length)
                {
                    Log.Warn("Ui", $"创角：职业「{_classes[i].name}」的 id={id} 超出 PlayerClass 范围（1-5）⇒ 不参与槽位绑定");
                    continue;
                }
                _slotOfId[id] = i;
            }

            for (var slot = 0; slot < SlotClassIds.Length; slot++)
            {
                //   `_slotOfId[SlotClassIds[slot]]`，槽里是 -1 时会 `_slotOfId[-1]` **越界**。先守卫。
                var id = SlotClassIds[slot];
                var idx = id >= 0 && id < _slotOfId.Length ? _slotOfId[id] : -1;
                var on = idx >= 0;
                if (!on)
                    Log.Warn("Ui", $"创角：槽位 #{slot}（原版热点 {UiLayoutFlow.ClassMenu.Spot.NameOf(slot)}，" +
                                   $"x 原版 {UiLayoutFlow.Orig(UiLayoutFlow.ClassMenu.Spot.HotspotPosOf(slot).x):0.#}）" +
                                   (id < 0
                                       ? "按用户决策停用（本项目只做 Amazon + Barbarian）⇒ 该槽半身像与点击区都不显示"
                                       : $"在本项目 class_c 里没有职业 id={id}（{(PlayerClass)id}）⇒ 该槽半身像与点击区都不显示"));

                if (_portrait[slot] != null) _portrait[slot].gameObject.SetActive(on);
                if (_hotspotImages[slot] != null) _hotspotImages[slot].gameObject.SetActive(on);
            }
        }

        /// <summary>透明点击/悬停区（原版热点矩形）。与半身像同生共死。</summary>
        private readonly Image[] _hotspotImages = new Image[SlotClassIds.Length];

        // ── 交互（与原版 `ClassSelector` + `ClassSelectMenu` 同语义）────────────

        /// <summary>指针进入某槽位：换 `NU2`（悬停态）+ 顶上一行显示该职业说明（原版 `OnEnter`）。</summary>
        private void OnSpotEnter(int slot)
        {
            if (!SlotAvailable(slot)) return;
            _hoverSlot = slot;
            Refresh();
        }

        /// <summary>指针离开：没有选中项时说明行清空（原版 `OnExit` 的 `if (_selected == null)` 判断）。</summary>
        private void OnSpotExit(int slot)
        {
            if (_hoverSlot != slot) return;
            _hoverSlot = -1;
            Refresh();
        }

        /// <summary>
        /// 点击某槽位：选中它（转正面 `NU3`）；再点同一个 ⇒ 取消选中。
        /// 语义出处：`ClassSelectMenu.cs:81-99`（`OnClick` 里的"同项再点 = 取消"分支）。
        /// </summary>
        private void OnSpotClick(int slot)
        {
            var idx = SlotIndex(slot);
            if (idx < 0)
            {
                Log.Warn("Ui", $"创角：槽位 #{slot} 没有对应职业数据，点击被忽略");
                return;
            }

            if (_classIndex == idx)
            {
                _classIndex = -1;
                Log.Info("Ui", $"创角：取消选中职业「{_classes[idx].name}」（原版同项再点 = 取消）⇒「确定」置灰");
                BeginTransition(slot, ResPaths.Portrait.TransitionBack, ResPaths.Portrait.Idle);
            }
            else
            {
                _classIndex = idx;
                var c = _classes[idx];
                Log.Info("Ui", $"创角：选中职业「{c.name}」(id={c.id}) 初始 力{c.str}/敏{c.dex}/体{c.vit}/精{c.eng}" +
                               $"（半身像 → 播原版 `{ResPaths.Portrait.TransitionFront}` 转身过渡 " +
                               $"{ResPaths.Portrait.TransitionFps:0} fps，播完落到正面待机 NU3）");
                BeginTransition(slot, ResPaths.Portrait.TransitionFront, ResPaths.Portrait.Front);
            }

            Refresh();
        }

        /// <summary>该槽位有没有对应职业数据（没有 ⇒ 半身像与点击区都被隐藏，鼠标事件不会到达）。</summary>
        private bool SlotAvailable(int slot) => SlotIndex(slot) >= 0;

        /// <summary>
        /// 槽位 → 面板里的职业下标（-1 = 该槽没有职业：既包括**停用槽** <see cref="NoClass"/>，
        /// 也包括"配表里没有这个职业"）。
        /// </summary>
        private int SlotIndex(int slot)
        {
            if (slot < 0 || slot >= SlotClassIds.Length) return -1;
            var id = SlotClassIds[slot];
            return id >= 0 && id < _slotOfId.Length ? _slotOfId[id] : -1;
        }

        // ── 确定 ────────────────────────────────────────────────────────────

        private void OnConfirm()
        {
            if (_classes.Count == 0 || _classIndex < 0)
            {
                // 非预期分支：没选职业就点确定（按钮本应置灰；这里是兜底 + 点名）
                if (!_noSelectionWarned)
                {
                    _noSelectionWarned = true;
                    Log.Warn("Ui", "创角：还没选职业就点了「确定」（按钮应处于置灰态）⇒ 拒绝并提示（只报一次）");
                }
                Game.UI.Toast("请先点一个职业半身像");
                return;
            }

            // **读缓冲（真值）**，不读输入框 —— 输入框装的是显示串「缓冲 + 插入点光标标记」
            //   （见 `DisplayName`），读它会把光标标记当成角色名的一部分。
            var name = _nameBuffer.Trim();
            if (string.IsNullOrEmpty(name))
            {
                Log.Warn("Ui", "创角失败：角色名为空");
                Game.UI.Toast("请输入角色名");
                return;
            }

            // 名字会被当成存档槽位的**文件名**（见 `Module/Save/SaveModule.cs` 文件头「已知边界」）：
            //    这里对**最终提交的名字**再校验一次 —— 键入路径已被 `OnNameChar` 过滤，这条兜的是
            //    `Cfg.DefaultPlayerName` 被配成非法字符之类的路径（不让它一路走到存档才失败）
            var bad = FirstDisallowedNameChar(name);
            if (bad != '\0')
            {
                Log.Warn("Ui", $"创角失败：角色名「{name}」含不能当文件名的字符「{bad}」"
                    + "（存档槽位键 = 角色名，只接受字母 / 数字 / 中文 / `_` / `-`）");
                Game.UI.Toast("角色名只能包含字母、数字、中文、_ 和 -");
                return;
            }

            var c = _classes[_classIndex];

            //   （官方：1 级角色没有可花点数；每级 +5 由 `PlayerStats` 在升级时发放）。
            const int level = 1;
            var life = LifeOf(c, c.vit, level);
            var mana = ManaOf(c, c.eng, level);
            var stamina = StaminaOf(c, c.vit, level);

            var save = new CharacterSave
            {
                version = GameConst.SaveVersion,
                name = name,
                cls = (PlayerClass)c.id,
                level = level,
                exp = 0,
                str = c.str,
                dex = c.dex,
                vit = c.vit,
                eng = c.eng,
                life = life,
                mana = mana,
                stamina = stamina,
                statPoints = 0,
                skillPoints = 0,
                gold = 0,
                areaId = (int)AreaId.Town,   // 新角色从罗格营地开始
                gridX = 0,
                gridY = 0,
                mapSeed = 0,                 // 0 = 待 Flow 在进图时用 Rng.FromTime() 决定（见 Flow 日志）
                savedAtTicks = 0,
            };

            Log.Info("Ui",
                $"[创角] {name} 职业={c.name} 力{c.str}/敏{c.dex}/体{c.vit}/精{c.eng} " +
                $"生命={life} 法力={mana} 耐力={stamina} 剩余点数={save.statPoints}（原版创角无属性分配）" +
                $" → 发 {Events.CharCreateRequest}");
            Game.Event.Emit(Events.CharCreateRequest, save);
        }

        //   不是"起始四维 × 成长系数"。官方 1 级值（出处：`原版资源/参考工程_Diablerie/
        //   d2lod1.10txt/data/global/excel/charstats.txt:2..6` 的 `hpadd`(30)/`stamina` 列
        //   —— 与 `原版资源/d2raw/…/charstats.txt` 逐字节相同 —— 加 Arreat Summit 各职业页
        //   Starting Attributes）：
        //     生命 = hpadd(30) + 起始体力（亚马逊 50 / 女法师 40 / 亡灵法师 45 / 圣骑士 55 / 野蛮人 55）
        //     法力 = 起始精力（15 / 35 / 25 / 15 / 10）
        //     耐力 = `stamina` 列（84 / 74 / 79 / 89 / 92）
        //   旧式 `vit × lifePerVit` 给出 60/20/30/75/100、`eng × manaPerMag` 给出 22/70/50/22/10、
        //   `vit × stamPerVit` 给出 20/10/15/25/25 ⇒ 1 级数值全错（用户第三批投诉 ①）。
        //   与 `Module/Player/PlayerStats.MaxLifeOf/MaxManaOf/MaxStaminaOf` **逐字同式** ——
        //   分层自检 ③ 禁止 UI 引用 `Diablo2.Module`，故这里复制一份；两处一致性由
        //
        //   `ClassEntry.hpAdd` / `ClassEntry.baseStamina` 读 —— 值由 `Module/Flow/ClassTable.Build()`
        //   取自配表 `class_c` 的 `hp_add` / `base_stamina` 列 ⇒ **真值只在表里**。

        private static int LifeOf(ClassEntry c, int vit, int level)
            => Mathf.Max(1, Mathf.RoundToInt(
                (c.hpAdd + c.vit) + (vit - c.vit) * c.lifePerVit + (level - 1) * c.lifePerLvl));

        private static int ManaOf(ClassEntry c, int eng, int level)
            => Mathf.Max(1, Mathf.RoundToInt(
                c.eng + (eng - c.eng) * c.manaPerMag + (level - 1) * c.manaPerLvl));

        private static int StaminaOf(ClassEntry c, int vit, int level)
            => Mathf.Max(1, Mathf.RoundToInt(
                c.baseStamina + (vit - c.vit) * c.stamPerVit + (level - 1) * c.stamPerLvl));

        // ── 刷新 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 按"选中 / 悬停 / 其它"三档给每个槽位贴原版三态，并刷新说明行与「确定」可用性。
        /// 状态优先级与出处：选中（`NU3`）> 悬停（`NU2`）> 默认（`NU1`）。
        /// </summary>
        private void Refresh()
        {
            for (var slot = 0; slot < _portrait.Length; slot++)
            {
                var idx = SlotIndex(slot);
                if (idx < 0) continue;

                //   若这里照常贴待机态，会把过渡的首帧覆盖掉（表现上就是"过渡根本没播"）。
                if (slot == _transitionSlot) continue;

                var state = ResPaths.Portrait.Idle;
                if (_classIndex >= 0 && idx == _classIndex) state = ResPaths.Portrait.Front;
                else if (_hoverSlot == slot) state = ResPaths.Portrait.Hover;

                ShowPortrait(slot, state);
            }

            // 说明行 = 当前"选中优先、其次悬停"那个职业的原版说明（原版 ClassDescription 行的语义）
            var showcase = ShowcaseSlot();
            var desc = string.Empty;
            if (showcase >= 0)
            {
                var idx = SlotIndex(showcase);
                if (idx >= 0) desc = UiLayoutFlow.ClassText.Description(ClassLatinName(idx));
            }
            if (_descLabel != null) _descLabel.SetText(desc);

            // 「确定」：没选职业就置灰（原版 `ClassSelectMenu.cs:110-111 okButton.Disabled = true`）
            // 判据注记：
            //   未选职业时本按钮 `interactable = false` ⇒ uGUI 走 `Selectable.DoStateTransition` 的
            //   **禁用态**，套 `spriteState.disabledSprite`（`UiArt.Apply` 里 = `btn_med_normal` 常态帧）
            //   ⇒ 此时**悬停/按下都不换底图**（`OnPointerDown` 在 `!IsInteractable()` 时直接 return）
            //   ⇒ 实测"悬停/按下像素差 0.0%"是**符合原版语义的正确行为**，不是"SpriteSwap 没接上"。
            //   依据链：① 本行（原版 `ClassSelectMenu.cs:110-111` 的 `okButton.Disabled = true`）；
            //   ② `策划/状态矩阵.tsv:2457` 的判据列本身就是「边界: 禁用态 = 无反馈」；
            //   ③ 同屏对照 = `CharSelectPanel` 的 Enter/Delete（可交互）实测差 37%（`t0e_hover_contact.index.tsv` g3/g4）。
            //   仍未实机确认的是"**选职业之后**悬停 Confirm 是否换成 `btn_med_sel`"（那一档 uGUI 走
            if (_okButton != null) _okButton.SetEnabled(_classes.Count > 0 && _classIndex >= 0);
        }

        /// <summary>当前该在说明行里展示哪个槽位（选中优先，其次悬停；都没有 = -1）。</summary>
        private int ShowcaseSlot()
        {
            if (_classIndex >= 0)
            {
                for (var slot = 0; slot < SlotClassIds.Length; slot++)
                    if (SlotIndex(slot) == _classIndex) return slot;
            }
            if (_hoverSlot >= 0) return _hoverSlot;
            return -1;
        }

        /// <summary>面板下标 → 职业的拉丁名（原版屏上就是英文职业名）。</summary>
        private string ClassLatinName(int index)
            => index >= 0 && index < _classes.Count ? ((PlayerClass)_classes[index].id).ToString() : string.Empty;

        /// <summary>
        /// 给某槽位贴某状态的原版半身像（**连同矩形一起换** —— 三态的画框不同：
        /// 各态的 `DC6 帧尺寸 + offset` 不一样，见 `UiLayoutFlow.ClassMenu.PortraitState`）。
        /// 状态没变则直接返回（避免每次鼠标移动都重新发起贴图加载）。
        /// </summary>
        private void ShowPortrait(int slot, int state)
        {
            var img = _portrait[slot];
            if (img == null) return;

            var ps = UiLayoutFlow.ClassMenu.Spot.Of(slot, state);
            if (ps == null) return;

            if (_shownState[slot] == state) return;
            _shownState[slot] = state;

            img.rectTransform.sizeDelta = ps.Size;
            img.rectTransform.anchoredPosition = ps.Pos;

            var cls = (PlayerClass)SlotClassIds[slot];
            UiArt.SetSprite(img, ResPaths.ClassPortrait(cls, state, 0));
            Log.Info("Ui", $"[半身像] 槽 {slot}（{UiLayoutFlow.ClassMenu.Spot.NameOf(slot)}）→ " +
                           $"{ResPaths.Portrait.Label(state)}：{ResPaths.ClassPortrait(cls, state, 0)} " +
                           $"（画布 {ps.Size.x:0.#}×{ps.Size.y:0.#} @ {ps.Pos.x:0.#},{ps.Pos.y:0.#}）");
        }

        //   为什么不用协程 / 新组件：原版就是**同一个 ClassSelector 上的一个 SpriteAnimator**
        //   （`ClassSelector.cs:34 _mainAnimator`）⇒ 这里同构：面板自己在 `OnUpdate` 里推进。
        //   帧率、是否循环、播完切哪个态 —— 三条口径全部来自参考物源码（见 ResPaths.Portrait 的注释）。

        /// <summary>
        /// 已贴过的过渡帧号（-1 = 还没有）——`TickTransition` 每个 Update 都会被调，
        /// 而动画只有 25fps（60fps 下同一帧会被连叫 2~3 次）⇒ 用帧号去重，别重复发起同一次贴图。
        /// </summary>
        private int _transitionFrame = -1;

        /// <summary>过渡没有素材（该职业没导出）时只 Warn 一次（不刷屏）。</summary>
        private bool _transitionFramesWarned;

        /// <summary>
        /// 起播某槽位的转身过渡。
        /// <para>
        /// 口径出处：`ClassSelector.cs:207-233`（`Loop = false`、`HideOnFinish = true`、**`Fps = 25`**）
        /// + `:53-76 MainAnimatorOnFinish`（过渡播完 ⇒ `FrontIdle`(NU3) / `BackIdle`(NU1)）。
        /// </para>
        /// <para>帧数取自 `UiLayoutFlow.ClassMenu.Transition`（= 逐帧尺寸表的长度，
        /// 出处 = 导出 PNG 张数）—— **面板里不再另存一张帧数表**（两张表必然漂移）。</para>
        /// </summary>
        private void BeginTransition(int slot, string code, int endState)
        {
            if (slot < 0 || slot >= _portrait.Length) return;
            var cls = (PlayerClass)SlotClassIds[slot];
            var frames = UiLayoutFlow.ClassMenu.Transition.FrameCount(slot, code);

            if (frames <= 0)
            {
                if (!_transitionFramesWarned)
                {
                    _transitionFramesWarned = true;
                    Log.Warn("Ui", $"创角：槽 {slot}（{cls}）的过渡「{code}」没有尺寸表" +
                                   "（见 UiLayoutFlow.ClassMenu.Transition；本项目只导出 Amazon + Barbarian）" +
                                   "⇒ 本次直接落终态、不播过渡（只报一次）");
                }
                _transitionSlot = -1;
                _shownState[slot] = -1;
                ShowPortrait(slot, endState);
                return;
            }

            _transitionSlot = slot;
            _transitionCode = code;
            _transitionElapsed = 0f;
            _transitionFrame = -1;                       // ★ R1-C：新一段过渡 ⇒ 帧号去重复位
            _transitionFrameCount = frames;
            _transitionEndState = endState;
            ShowTransitionFrame(0);
            Log.Info("Ui", $"创角：[过渡] 槽 {slot}（{cls}）起播 `{code}` {frames} 帧 @" +
                           $"{ResPaths.Portrait.TransitionFps:0}fps（原版 " +
                           (code == ResPaths.Portrait.TransitionFront ? "FrontTransition" : "BackTransition") +
                           $"，出处 ClassSelector.cs:207-233）；每帧按该帧原生尺寸 ×" +
                           $"{UiLayoutFlow.Scale} 同步矩形（表 UiLayoutFlow.ClassMenu.Transition）；" +
                           $"播完落「{ResPaths.Portrait.Label(endState)}」");
        }

        /// <summary>
        /// 每帧推进过渡（`OnUpdate` 调用）。
        /// 帧号 = `⌊已播秒数 × 25⌋`（25 = 原版 `Fps`，见 <see cref="ResPaths.Portrait.TransitionFps"/>）；
        /// 超出帧数 ⇒ **立刻落终态**（等价原版的 `HideOnFinish = true` + `MainAnimatorOnFinish`）。
        /// <para>用 `dt` 而不是 `Time.deltaTime` ⇒ 与引擎的 Tick 口径一致（`UIPanel.OnUpdate(float dt)`）。</para>
        /// </summary>
        private void TickTransition(float dt)
        {
            if (_transitionSlot < 0) return;

            _transitionElapsed += dt;
            var frame = Mathf.FloorToInt(_transitionElapsed * ResPaths.Portrait.TransitionFps);

            if (frame >= _transitionFrameCount)
            {
                var slot = _transitionSlot;
                var played = _transitionFrameCount;
                var end = _transitionEndState;
                _transitionSlot = -1;
                _shownState[slot] = -1;                  // 过渡期间 `_shownState` 被改过 ⇒ 强制重贴终态
                ShowPortrait(slot, end);
                Log.Info("Ui", $"创角：[过渡] 槽 {slot} 播完 {played} 帧 ⇒ 落「{ResPaths.Portrait.Label(end)}」");
                return;
            }

            ShowTransitionFrame(frame);
        }

        /// <summary>
        /// 给正在播过渡的槽贴第 <paramref name="frame"/> 帧 —— **连同矩形一起换**。
        /// <para>
        /// （注释当时自认"矩形沿用帧 0 那一套"）。而原版过渡**逐帧换矩形**（导出 PNG 实测：
        /// Amazon `fw_0` = 118×198 → `fw_21` = 215×228 → `fw_53` = 121×234）⇒ 215 宽的帧被压进
        /// 118 宽的画框（横向压掉 45%），画面上就是"人物被拍扁 + 忽胖忽瘦"。
        /// </para>
        /// <para>
        /// **现在**：几何由 `UiLayoutFlow.ClassMenu.Transition.Of(slot, code, frame)` 给
        /// （尺寸 = 该帧原生尺寸 ×1.8 ⇒ **每帧的矩形宽高比 == 该帧原生宽高比**，`uicheck` 逐帧断言；
        /// 位置 = 底边中点落在两端锚点的线性插值上，两端与 `NU1` / `NU3` 矩形逐像素一致）⇒
        /// **不需要 `preserveAspect`**（矩形本身就是按该帧原生比例给的，再让 uGUI 等比内缩只会缩小画面）。
        /// </para>
        /// <para>同一帧不重复贴（`TickTransition` 每帧都调；动画 25fps < 屏幕刷新率）。</para>
        /// </summary>
        private void ShowTransitionFrame(int frame)
        {
            var slot = _transitionSlot;
            if (slot < 0 || slot >= _portrait.Length) return;
            if (frame == _transitionFrame) return;               // ★ R1-C：同一帧不重复贴（去重）
            _transitionFrame = frame;

            var img = _portrait[slot];
            if (img == null) return;

            var ps = UiLayoutFlow.ClassMenu.Transition.Of(slot, _transitionCode, frame);
            if (ps != null)
            {
                // 逐帧矩形（与三态同一套做法；这里不许出现任何尺寸/位置魔数）
                img.rectTransform.sizeDelta = ps.Size;
                img.rectTransform.anchoredPosition = ps.Pos;
            }

            var cls = (PlayerClass)SlotClassIds[slot];
            UiArt.SetSprite(img, ResPaths.ClassTransition(cls, _transitionCode, frame));
        }

        // ── 构建（原版骨架一次搭好，内容在 Refresh 里填）────────────────────

        private void Build()
        {
            if (_built) return;
            _built = true;

            // 原版职业选择屏贴图（800×600）**按高度 ×1.8 = 1440×1080 水平居中**（挂在**面板根**上；
            // 左右各留 240 由纯色底补，**不把 4:3 素材横向拉伸**）；
            // **必须先建**（它是同层第一个子节点 ⇒ 层级最低 ⇒ 不会盖住后面的 UI 元素）。
            UiLayoutFlow.BackdropArt(transform, ResPaths.MenuClassSelectScreen, new Color(0.04f, 0.04f, 0.05f, 1f));

            // 屏适配容器：改按高度 ×1.8 后原版整屏正好 1080 高（标题 480.6 / 底部按钮 -450 都在 ±540 内）
            var screen = UiLayoutFlow.FitRoot(transform, UiLayoutFlow.FitClass);

            // ── 原版文本框位置（逐条来自 ClassSelectMenu.prefab）──
            // `FlowLabel.Create` 的尺寸参数是**原版 px**（位图字模按原版像素排版）⇒ 常量表（Canvas 单位）
            //    统一经 `Orig()` 折回原版 px。
            UiLayoutFlow.FlowLabel.Create(screen, "Title", Text.Title, D2Text.D2Font.Font24,
                TextAnchor.UpperLeft, Color.white, UiLayoutFlow.Orig(UiLayoutFlow.ClassMenu.TitleSize),
                UiLayoutFlow.ClassMenu.TitlePos);

            _descLabel = UiLayoutFlow.FlowLabel.Create(screen, "ClassDescription", string.Empty,
                D2Text.D2Font.Font16, TextAnchor.MiddleCenter, UiArt.TextColor,
                UiLayoutFlow.Orig(UiLayoutFlow.ClassMenu.DescSize), UiLayoutFlow.ClassMenu.DescPos);

            // 名字行（本项目新增；贴在原版 ClassName 框那一行）
            UiLayoutFlow.FlowLabel.Create(screen, "NameLabel", Text.Name, D2Text.D2Font.Font16,
                TextAnchor.MiddleRight, Color.white, UiLayoutFlow.Orig(UiLayoutFlow.New.NameLabelSize),
                UiLayoutFlow.New.NameLabelPos);

            _nameInput = UiArt.Input(screen, "NameInput", UiLayoutFlow.New.NameInputSize,
                UiLayoutFlow.New.NameInputPos, "输入角色名（最多 15 字）", string.Empty, NameMaxLength);

            // 输入框内文字/占位按 ×1.8 放大（UiArt.Input 里的固有 22/20 是按 1:1 面板定的）
            if (_nameInput != null)
            {
                if (_nameInput.textComponent != null)
                    _nameInput.textComponent.fontSize = UiLayoutFlow.ChineseFontSize(D2Text.D2Font.Font16);
                if (_nameInput.placeholder is UnityEngine.UI.Text ph)
                    ph.fontSize = UiLayoutFlow.ChineseFontSize(D2Text.D2Font.Font16);

                // **限长由面板的缓冲管**（`EditName` 按 `NameMaxLength` 钳）⇒ 关掉输入框自带的截断：
                //   显示串 = 缓冲 + 插入点光标标记（最多 16 个字符），开着 `characterLimit = 15`
                //   会把光标标记裁掉（表现上就是"满 15 字后光标消失"）。
                _nameInput.characterLimit = 0;
            }

            // ── 原版半身像横排（5 个槽位）：先贴图（不可点），再把**原版热点矩形**当透明点击/悬停区 ──
            BuildSpots(screen);

            // ── 底部两钮 = 原版 `ExitButton` / `OkButton` 的精确矩形（原版中等按钮）──
            UiLayoutFlow.FlowButton.Create(screen, "Back", Text.Back, UiLayoutFlow.MediumButtonOrig,
                UiLayoutFlow.ClassMenu.ExitPos, () =>
                {
                    // 空名 = 仅返回选角屏（不选中具体角色）；语义见 AppFlow.OnCharSelectRequest。
                    Log.Info("Ui", "创角面板：返回选角屏");
                    Game.Event.Emit(Events.CharSelectRequest, string.Empty);
                });

            _okButton = UiLayoutFlow.FlowButton.Create(screen, "Confirm", Text.Ok, UiLayoutFlow.MediumButtonOrig,
                UiLayoutFlow.ClassMenu.OkPos, OnConfirm);
            if (_okButton != null) _okButton.SetEnabled(false);   // 原版：进屏时「确定」置灰
        }

        /// <summary>
        /// 5 个职业槽位：半身像 Image（默认态）+ 原版热点矩形上的透明点击/悬停区。
        /// 半身像的矩形与位置来自 `UiLayoutFlow.ClassMenu.Spot`（原版 hotspot anchoredPosition + 帧 offset）。
        /// </summary>
        private void BuildSpots(Transform screen)
        {
            for (var slot = 0; slot < _portrait.Length; slot++)
            {
                var s = slot;
                var clsId = SlotClassIds[slot];
                var hasClass = clsId >= 0;
                var id0 = UiLayoutFlow.ClassMenu.Spot.Of(slot, ResPaths.Portrait.Idle);
                if (id0 == null) continue;

                // ① 半身像（不可点：raycastTarget=false ⇒ 点击由下面的热点接）
                //   `BindSlots` 统一 `SetActive(false)`），但**不请求贴图**：否则会去加载
                //   `D2/UI/FrontEnd/-1/nu1_0` 并刷 `[Resource] 加载失败` Error（实测过）。
                //   `UiArt.SetSprite` 对空路径有守卫（`string.IsNullOrEmpty(spritePath) ⇒ return`）。
                var img = UiArt.Art(screen, "Portrait" + UiLayoutFlow.ClassMenu.Spot.NameOf(slot),
                    hasClass ? ResPaths.ClassPortrait((PlayerClass)clsId, ResPaths.Portrait.Idle, 0) : null,
                    id0.Size, id0.Pos);
                _portrait[slot] = img;
                _shownState[slot] = ResPaths.Portrait.Idle;

                // ② 原版热点矩形上的透明点击/悬停区（**后建 ⇒ 压在贴图上**，坐标 = 原版热点本身）
                var spot = UiLayoutFlow.Hotspot(screen, "Spot" + UiLayoutFlow.ClassMenu.Spot.NameOf(slot),
                    UiLayoutFlow.Orig(UiLayoutFlow.ClassMenu.Spot.HotspotSizeOf(slot)),
                    UiLayoutFlow.ClassMenu.Spot.HotspotPosOf(slot), () => OnSpotClick(s));

                if (spot == null)
                {
                    Log.Error("Ui", $"创角：槽 {slot} 的热点没建出来 ⇒ 该职业点不了（见 UiLayoutFlow.Hotspot 的 Error）");
                    continue;
                }
                _hotspotImages[slot] = spot;

                // ③ 悬停接线（原版 `ClassSelector.OnPointerEnter/Exit`；引擎没有悬停事件 ⇒ 用 HoverTarget）
                var hover = spot.gameObject.AddComponent<HoverTarget>();
                hover.OnEnter = () => OnSpotEnter(s);
                hover.OnExit = () => OnSpotExit(s);
            }

            // ④ 预热三态贴图（5 槽 × 3 态 = 15 张，最大 131×234）：让"悬停/选中"换图**同步**生效。
            //    为什么必须预热：`UiArt.SetSprite` 走 `Game.Res.LoadAsset`（**异步**，只有命中引擎缓存才同步回调）。
            //    不预热时鼠标快速进出会连续发起多次异步加载、**回调顺序不保证** ⇒ 半身像可能停在错的那一态
            //    （正是本项目最忌讳的"静默错态"）。预热后全部命中缓存 ⇒ 换图在调用点同步完成。
            PreloadPortraits();
        }

        /// <summary>预热 5 槽 × 3 态的半身像贴图（原因见 <see cref="BuildSpots"/> ④）。</summary>
        private void PreloadPortraits()
        {
            if (Game.Res == null)
            {
                Log.Warn("Ui", "创角：Game.Res 未初始化 ⇒ 半身像三态无法预热，换图可能是异步的（有停在错态的风险）");
                return;
            }

            var n = 0;
            for (var slot = 0; slot < _portrait.Length; slot++)
            {
                if (SlotClassIds[slot] < 0) continue;
                var cls = (PlayerClass)SlotClassIds[slot];
                for (var st = ResPaths.Portrait.Idle; st <= ResPaths.Portrait.Front; st++)
                {
                    var path = ResPaths.ClassPortrait(cls, st, 0);
                    n++;
                    Game.Res.LoadAsset<Sprite>(path, sp =>
                    {
                        if (sp == null)
                            Log.Warn("Ui", $"创角：半身像贴图缺失 {path}（该状态会显示为纯色块；" +
                                           "检查 tools/d2codec/export_d2ui.py --only frontend 是否跑过）");
                    });
                }
            }

            // 措辞里的 {cls} 是**字面占位**（不是插值变量）：原版路径形如 FrontEnd/necromancer/NENU1.DC6
            Log.Info("Ui", $"创角：已预热 {n} 张职业半身像（有素材的槽 × 3 态 = 原版 FrontEnd/{{职业}}/NU1..NU3 的帧 0）");

            // ④b **过渡帧也预热**（与三态同口径）—— 用户点下去的第一帧必须**同步**贴出来，
            //    否则第一次点击会先闪一下上一张图（异步回调晚到），而"闪一下"正是"点人物时画面跳"的来源之一。
            //    数量：Amazon 54+30 / Barbarian 64+19 = 167 张（都是 ≤215×255 的小 PNG）。
            var t = 0;
            for (var s = 0; s < UiLayoutFlow.ClassMenu.Transition.SlotIds.Length; s++)
            {
                var slot = UiLayoutFlow.ClassMenu.Transition.SlotIds[s];
                if (SlotClassIds[slot] < 0) continue;                 // 停用槽（本项目不会走到）
                var cls = (PlayerClass)SlotClassIds[slot];
                for (var c = 0; c < UiLayoutFlow.ClassMenu.Transition.Codes.Length; c++)
                {
                    var code = UiLayoutFlow.ClassMenu.Transition.Codes[c];
                    var count = UiLayoutFlow.ClassMenu.Transition.FrameCount(slot, code);
                    for (var f = 0; f < count; f++)
                    {
                        var path = ResPaths.ClassTransition(cls, code, f);
                        t++;
                        Game.Res.LoadAsset<Sprite>(path, sp =>
                        {
                            if (sp == null)
                                Log.WarnOnce("Ui", "charcreate.transition.missing",
                                    $"创角：转身过渡帧缺失 {path}（该帧会显示为纯色块；" +
                                    "检查 tools/d2codec/export_d2ui.py --only frontend 是否跑过）—— 同类只报一次");
                        });
                    }
                }
            }

            Log.Info("Ui", $"创角：已预热 {t} 张转身过渡帧（{string.Join("/", UiLayoutFlow.ClassMenu.Transition.Codes)}" +
                           " 全部帧；逐帧矩形见 UiLayoutFlow.ClassMenu.Transition）");
        }
    }
}
