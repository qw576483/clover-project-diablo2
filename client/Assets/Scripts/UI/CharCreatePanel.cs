// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/CharCreatePanel.cs
// 站点：CharCreate。层：Normal。预制体：`Resources/UI/CharCreatePanel`。
//
// ★ agent-15 §A：**1:1 复刻**原版"选职业并建角"那一屏的骨架
//   （依据 `Prefabs/Menu/ClassSelectMenu.prefab` 的 `ClassSelectMenu/Canvas` + 子节点）：
//     · 背景：原版 `class_select_screen`（800×600）按高度 ×1.8 = 1440×1080 水平居中（不横向拉伸）
//     · 标题 = 原版 `SelectHeroClass` 文本框 (0,267) 493×30 → 本工程 (0,480.6) 887.4×54  `SELECT HERO CLASS`
//     · 名字框 = 贴在与原版 `ClassName` 文本框**同一行**（原版 (0,198) 493×30 → (0,356.4) 887.4×54）
//     · 职业说明 = 原版 `ClassDescription` 文本框 (1,155) 300×50 → 本工程 (1.8,279) 540×90
//     · 底部「返回」= 原版 `ExitButton`(-300,-250)、「确定」= 原版 `OkButton`(300,-250) 的精确矩形
//
// ★★ 片 4（「启动链路三屏 1:1」轮）**按原版重排本屏**（三件事）：
//   ① **职业改成原版半身像横排**（旧版用"5 个中等按钮 + 职业名"顶替，理由"没有半身像素材"——
//      **该理由已被证伪**：素材就在 `data/global/ui/FrontEnd/{cls}/{CLS}{NU1,NU2,NU3}.DC6`，
//      片 4 已逐帧解出 301 张 PNG）。三态与交互**照原版实现**：
//        · 默认 = `NU1`（背面待机）—— 原版 `ClassSelector.cs:258 ChangeState(BackIdle)`；
//        · 悬停 = `NU2` —— `ClassSelector.cs:116-122 ToggleHover()`；
//        · 选中 = `NU3`（转正面待机）—— `ClassSelector.cs:53-70` 转到 `FrontIdle`；
//        · 再点同一职业 = 取消选中；**未选中时「确定」置灰**（`ClassSelectMenu.cs:110-111`）。
//      几何 = 原版热点 `anchoredPosition` + 该态 DC6 帧 0 的 `offset`（推导与出处见
//      `UiLayoutFlow.ClassMenu.PortraitState` 的注释），点击区 = 原版热点矩形本身（透明）。
//      ⚠️ **注册差异**：原版点选后还有一段"转身过渡动画"（`{CLS}FW` 54 帧 / 背面 `{CLS}BW`）
//      —— 本轮**跳过过渡直接落到终态 NU3**（逐帧播放器 + 叠加层材质 + 选人音效属独立片，见回报）。
//   ② **删「属性点分配」**（4 组「标签 / − / 值 / +」+「剩余属性点」+「生命/法力/耐力预览行」）——
//      原版创角屏**没有**属性分配：属性点是**升级后**在 `C`（人物属性）面板里加的
//      ⇒ 本工程照此：`CharacterSave.statPoints = 0`，每级 +5 由 `Module/Player/PlayerStats.cs` 发放。
//   ③ 说明行改回原版语义：显示**当前高亮/选中职业的原版说明**（英文，出处见 `UiLayoutFlow.ClassText`）。
//
//   ⛔ **难度选择本轮不加**（原版三档 Normal/Nightmare/Hell）：本项目只做 Normal（Act I 起始两图），
//      且 `Def.CharacterSave` 无难度字段（Core/Def 契约冻结，不许改）—— 更关键的是**没有坐标出处**：
//      参考工程 `ClassSelectMenu.prefab` 上**没有难度控件**（UI 层 grep `difficulty|Nightmare|Hell`
//      = 0 命中），基准图也只是背景美术。⇒ 按 §0.5「写不出出处的量不许进工程」不加，登记为范围边界。
//   ⛔ 职业名文本行（原版 `ClassName` Text）**本屏没有**：那一行被本项目的**名字输入框**占用
//      （自 agent-15 起的既有决定，本次未动）⇒ 职业身份靠"半身像转正面 + 职业说明"表达，已登记差异。
//   · 职业初始四维与成长系数**只来自配表 `class_c`**：由 Flow 查表后经 `OnOpen(param)` 传入
//     （`ClassEntry`），面板不查表、不硬编码任何职业数值。
//   · 「确定」把结果打包成 `Def.CharacterSave` 发 `Events.CharCreateRequest`（Flow 负责落盘/入列表）。
// 入口：CharSelect 的【新建角色】；出口：确定 → CharSelect、返回 → CharSelect。
// ⛔ 不引用任何业务模块。
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
            /// <para>⚠️ 片 4：创角屏**不再用它**（原版创角没有属性分配）——
            /// 字段保留是因为 `Module/Flow/ClassTable.cs:56` 在填它（跨模块契约，本片不许改 Module）
            /// ⇒ 只作 Flow 侧的搬运字段存在，本面板读都不读（登记在回报的"遗留"一节）。</para>
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
        }

        /// <summary>`OnOpen(param)` 参数。</summary>
        public class Args
        {
            /// <summary>5 个职业（按 `class_c` 主键升序）。</summary>
            public List<ClassEntry> classes = new List<ClassEntry>();

            /// <summary>名字输入框的默认值（来自 `Cfg.DefaultPlayerName`）。</summary>
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
        /// ★★ **片 22（用户 2026-09-19 直接决策）**：**只做 Amazon + Barbarian**，
        /// 其余三个职业的**素材已删**（`Chars/{necromancer,paladin,sorceress}`、
        /// `UI/FrontEnd/{...}`）⇒ 它们那三个槽位填 <see cref="NoClass"/>（该槽不显示、不可点）。
        /// </para>
        /// <para>
        /// ⚠️ **为什么保留 5 个槽位、不做"2 个居中重排"**：这 5 个 x 坐标是**原版几何**
        /// （`ClassSelectMenu.prefab` 的热点表）。把它们挪成"两个人居中"= **写不出出处的量**
        /// （§0.5 禁止）⇒ 保留原版槽位、空着的就空着。
        /// </para>
        /// <para>
        /// ⛔ `Def.PlayerClass` 的枚举值一个都没删（存档 `cls` 字段与配表 `class` 列依赖它）——
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

        // ── ★ 片 22：转身过渡（原版 `FrontTransition` / `BackTransition`）的播放状态 ──────
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

        // ── ★ agent-22 §B2：名字输入的键盘接线状态（见 OnUpdate 的说明）─────────────
        /// <summary>名字框是否可输入（原版创角屏的名字框就是当前输入框，默认一直可输入）。</summary>
        private bool _nameEditing = true;

        /// <summary>名字编辑缓冲（与 <see cref="_nameInput"/>.text 同步；本工程配置下 uGUI 自己收不到字符）。</summary>
        private string _nameBuffer = string.Empty;

        /// <summary>光标位置（0..<see cref="_nameBuffer"/>.Length）。</summary>
        private int _nameCaret;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        private Keyboard _nameKeyboard;
#endif
        private bool _nameKbWarned;
        private bool _nameLimitWarned;
        /// <summary>「名字里出现不能当文件名的字符」是否已警告过（只报一次；判据见 <see cref="IsNameCharAllowed"/>）。</summary>
        private bool _nameCharWarned;
        private bool _namePushWarned;
        private bool _noSelectionWarned;

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

            _nameInput.text = args != null && !string.IsNullOrEmpty(args.defaultName)
                ? args.defaultName
                : string.Empty;

            // ★ B2：编辑缓冲与 uGUI 输入框同步；并接管键盘（本工程配置下 uGUI 收不到字符）
            _nameBuffer = _nameInput.text ?? string.Empty;
            _nameCaret = _nameBuffer.Length;
            _nameEditing = true;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            SubscribeNameInput();
#endif

            // ★ 原版进屏状态：**什么都没选中**（半身像全 `NU1`、说明行空、「确定」置灰）
            _classIndex = -1;
            _hoverSlot = -1;
            BindSlots();
            Refresh();

            UiLayoutFlow.LogTable(nameof(CharCreatePanel));
            // ★ 片 22：日志说清「配表几行 vs 屏上可选几个」—— 用户决策只做 2 个职业，
            //   但 `class_c` 配表**故意保留 5 行**（`Def.PlayerClass` 枚举值与存档 `cls` 字段依赖它，
            //   删行会破坏"旧存档仍可读"这条兼容性）。
            var slotCount = 0;
            for (var i = 0; i < SlotClassIds.Length; i++)
                if (SlotClassIds[i] >= 0) slotCount++;

            Log.Info("Ui", $"创角面板已打开：屏上可选 {slotCount} 个职业（`class_c` 配表共 {_classes.Count} 行；" +
                           "按用户 2026-09-19 决策只做 Amazon + Barbarian，其余三个职业的素材已删、枚举值保留）；" +
                           "半身像 = 原版 NU1/NU2/NU3 三态 + FW/BW 两段转身过渡；" +
                           $"默认名字「{_nameInput.text}」；初始状态 = 未选职业");
        }

        /// <inheritdoc/>
        public override void OnClose()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            UnsubscribeNameInput();
#endif
            Log.Info("Ui", $"创角面板已关闭（最终名字「{_nameBuffer}」）");
        }

        /// <summary>
        /// ★ agent-22 §B2：每帧收键（只有在本工程当前配置下才编进来）。
        /// <para>
        /// ⛔ **实测根因**（探针 `client/_dev/p_a22_b2b.cs` 的原始输出，不是推断）：
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
        /// 再把结果写回 `_nameInput.text`（外观/取值口径不变，`OnConfirm` 照旧读 `_nameInput.text`）。
        /// 若以后把 Player Settings 改成 Both（旧输入可用），这段会被 `#if` 编掉、交回 uGUI 原生行为，
        /// 不会出现「一个字符打两遍」。
        /// </para>
        /// </summary>
        public override void OnUpdate(float dt)
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            PollNameKeys();
#endif
            TickTransition(dt);          // ★ 片 22：推进转身过渡（`FW`/`BW`，25fps，播完落终态）
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

            // ⚠️ 名字会被当成存档槽位的**文件名**（见 `Module/Save/SaveModule.cs` 文件头「已知边界」）
            // ⇒ 这里只放行"能当文件名"的字符；其余**拒绝并只报一次**（⛔ 不静默吞掉）
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
        /// <para>⚠️ 判据来自**存档侧**：角色名就是存档槽位的**文件名**（`Module/Save/SaveModule.cs` →
        /// 引擎 `CloverEngine.FileSlotStore` 的 key 校验：不许 `/` `\` 与非法文件名字符）⇒ 这里挡掉
        /// 文件系统不接受的字符（`:` `*` `?` `"` `<` `>` `|` 等），中文等 Unicode 字母照常放行。</para>
        /// <para>校验刻意放在**键入这一处**（`OnNameChar`）而不是 `EditName` 纯函数里：`EditName`
        /// 保持"只钳长度、不看字符集"，免得动到 `tools/uicheck` 已有的纯函数断言。</para>
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
        /// **纯函数**：名字的一次编辑（离线自检宿主可直接断言，见 `tools/uicheck`；边界钳制只写在这一处）。
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

        private void InsertName(string s) => ApplyNameEdit("ins", s);
        private void BackspaceName() => ApplyNameEdit("back", null);
        private void DeleteName() => ApplyNameEdit("del", null);
        private void MoveCaret(string op) => ApplyNameEdit(op, null);

        private void ApplyNameEdit(string op, string arg)
        {
            var before = _nameBuffer;
            int caret;
            _nameBuffer = EditName(_nameBuffer, _nameCaret, op, arg, NameMaxLength, out caret);
            _nameCaret = caret;

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

        /// <summary>把编辑缓冲写回 uGUI 输入框（面板外观/取值口径与原来完全一致）。</summary>
        private void PushName(bool log)
        {
            if (_nameInput == null) return;

            if (_nameInput.text != _nameBuffer) _nameInput.text = _nameBuffer;
            try
            {
                _nameInput.caretPosition = _nameCaret;
            }
            catch (Exception e)
            {
                // 非预期分支：光标位置写不进去（uGUI 内部状态异常）⇒ 文本仍然正确，只报一次
                if (!_namePushWarned)
                {
                    _namePushWarned = true;
                    Log.Warn("Ui", $"创角：写名字光标位置失败（{e.GetType().Name}: {e.Message}）⇒ 只更新文本（只报一次）");
                }
            }

            if (log)
                Log.Info("Ui", $"创角：名字 =「{_nameBuffer}」（光标 {_nameCaret}/{_nameBuffer.Length}，上限 {NameMaxLength}）");
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
                // ★ 片 22：`NoClass`（用户决策停用的槽）**不能**当下标用 —— 原实现直接
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
                // ★ 片 22：原版取消选中 = `BackTransition`（`{CLS}BW`）—— `ClassSelector.cs:60-62 / 207-219`
                BeginTransition(slot, ResPaths.Portrait.TransitionBack, ResPaths.Portrait.Idle);
            }
            else
            {
                _classIndex = idx;
                var c = _classes[idx];
                Log.Info("Ui", $"创角：选中职业「{c.name}」(id={c.id}) 初始 力{c.str}/敏{c.dex}/体{c.vit}/精{c.eng}" +
                               $"（半身像 → 播原版 `{ResPaths.Portrait.TransitionFront}` 转身过渡 " +
                               $"{ResPaths.Portrait.TransitionFps:0} fps，播完落到正面待机 NU3）");
                // ★ 片 22：原版点选 = `FrontTransition`（`{CLS}FW`）—— `ClassSelector.cs:57-59 / 221-233`
                BeginTransition(slot, ResPaths.Portrait.TransitionFront, ResPaths.Portrait.Front);
            }

            Refresh();
        }

        /// <summary>该槽位有没有对应职业数据（没有 ⇒ 半身像与点击区都被隐藏，鼠标事件不会到达）。</summary>
        private bool SlotAvailable(int slot) => SlotIndex(slot) >= 0;

        /// <summary>
        /// 槽位 → 面板里的职业下标（-1 = 该槽没有职业：既包括**停用槽** <see cref="NoClass"/>，
        /// 也包括"配表里没有这个职业"）。
        /// ★ 片 22 加了 `NoClass` 守卫 —— 否则 `_slotOfId[-1]` **越界**。
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

            var name = (_nameInput != null ? _nameInput.text : string.Empty)?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                Log.Warn("Ui", "创角失败：角色名为空");
                Game.UI.Toast("请输入角色名");
                return;
            }

            // ⚠️ 名字会被当成存档槽位的**文件名**（见 `Module/Save/SaveModule.cs` 文件头「已知边界」）：
            //    这里对**最终提交的名字**再校验一次 —— 键入路径已被 `OnNameChar` 过滤，这条兜的是
            //    `Cfg.DefaultPlayerName` 被配成非法字符之类的路径（⛔ 不让它一路走到存档才失败）
            var bad = FirstDisallowedNameChar(name);
            if (bad != '\0')
            {
                Log.Warn("Ui", $"创角失败：角色名「{name}」含不能当文件名的字符「{bad}」"
                    + "（存档槽位键 = 角色名，只接受字母 / 数字 / 中文 / `_` / `-`）");
                Game.UI.Toast("角色名只能包含字母、数字、中文、_ 和 -");
                return;
            }

            var c = _classes[_classIndex];

            // ★ 片 4：**没有属性分配** ⇒ 四维就是 `class_c` 的初始值；`statPoints = 0`
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

        // 官方 charstats 口径（`class_c` 列注释）：四维系 + 等级成长系。
        private static int LifeOf(ClassEntry c, int vit, int level)
            => Mathf.Max(1, Mathf.RoundToInt(vit * c.lifePerVit + (level - 1) * c.lifePerLvl));

        private static int ManaOf(ClassEntry c, int eng, int level)
            => Mathf.Max(1, Mathf.RoundToInt(eng * c.manaPerMag + (level - 1) * c.manaPerLvl));

        private static int StaminaOf(ClassEntry c, int vit, int level)
            => Mathf.Max(1, Mathf.RoundToInt(vit * c.stamPerVit + (level - 1) * c.stamPerLvl));

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

                // ★ 片 22：正在播过渡的槽**这一帧不接管** —— 过渡由 `TickTransition` 逐帧贴图；
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

        // ── ★ 片 22：转身过渡播放器（原版 `FrontTransition` / `BackTransition`）──────────
        //   为什么不用协程 / 新组件：原版就是**同一个 ClassSelector 上的一个 SpriteAnimator**
        //   （`ClassSelector.cs:34 _mainAnimator`）⇒ 这里同构：面板自己在 `OnUpdate` 里推进。
        //   ⛔ 帧率、是否循环、播完切哪个态 —— 三条口径全部来自参考物源码（见 ResPaths.Portrait 的注释）。

        /// <summary>
        /// 每个职业两段过渡的**帧数**。
        /// <para>
        /// **出处** = 本工程导出器的实际输出（可复跑）：
        /// `python tools\d2codec\export_d2ui.py 原版资源\d2dc6 client --only frontend`
        /// 打印 → `amazon AMFW.DC6 54 帧 / AMBW.DC6 30 帧 / barbarian bafw.DC6 64 帧 / babw.DC6 19 帧`。
        /// 键 = `"{职业小写}/{过渡码}"`（过渡码见 <see cref="ResPaths.Portrait.TransitionFront"/>）。
        /// </para>
        /// <para>⛔ 不许把帧数散落到别处；改了导出范围（例如将来恢复某个职业）必须同步这张表。</para>
        /// </summary>
        private static readonly Dictionary<string, int> TransitionFrames = new Dictionary<string, int>
        {
            { "amazon/fw", 54 }, { "amazon/bw", 30 },
            { "barbarian/fw", 64 }, { "barbarian/bw", 19 },
        };

        /// <summary>帧数表里查不到过渡时只 Warn 一次（不刷屏）。</summary>
        private bool _transitionFramesWarned;

        /// <summary>
        /// 起播某槽位的转身过渡。
        /// <para>
        /// 口径出处：`ClassSelector.cs:207-233`（`Loop = false`、`HideOnFinish = true`、**`Fps = 25`**）
        /// + `:53-76 MainAnimatorOnFinish`（过渡播完 ⇒ `FrontIdle`(NU3) / `BackIdle`(NU1)）。
        /// </para>
        /// </summary>
        private void BeginTransition(int slot, string code, int endState)
        {
            if (slot < 0 || slot >= _portrait.Length) return;
            var cls = (PlayerClass)SlotClassIds[slot];
            var key = cls.ToString().ToLowerInvariant() + "/" + code;
            var frames = TransitionFrames.TryGetValue(key, out var n) ? n : 0;

            if (frames <= 0)
            {
                if (!_transitionFramesWarned)
                {
                    _transitionFramesWarned = true;
                    Log.Warn("Ui", $"创角：过渡「{key}」没有登记帧数（见 CharCreatePanel.TransitionFrames 的出处注释）" +
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
            _transitionFrameCount = frames;
            _transitionEndState = endState;
            ShowTransitionFrame(0);
            Log.Info("Ui", $"创角：[过渡] 槽 {slot}（{cls}）起播 `{code}` {frames} 帧 @" +
                           $"{ResPaths.Portrait.TransitionFps:0}fps（原版 " +
                           (code == ResPaths.Portrait.TransitionFront ? "FrontTransition" : "BackTransition") +
                           $"，出处 ClassSelector.cs:207-233）；播完落「{ResPaths.Portrait.Label(endState)}」");
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
        /// 给正在播过渡的槽贴第 <paramref name="frame"/> 帧。
        /// <para>
        /// ⚠️ **矩形沿用帧 0 那一套**：原版过渡是**逐帧换矩形**的（`FW` 从 118×198 长到 121×234，
        /// 这是"由远及近"透视的来源之一），而本项目 `UiLayoutFlow` 只登记了三态（`NU1/NU2/NU3`）的矩形，
        /// **没有过渡帧矩形的出处** ⇒ 按 §0.5「写不出出处的量不许进工程」**不自己推**，
        /// 过渡在固定画框里播完整序列。**已登记为「允许的差异」**。
        /// </para>
        /// </summary>
        private void ShowTransitionFrame(int frame)
        {
            var slot = _transitionSlot;
            if (slot < 0 || slot >= _portrait.Length) return;
            var img = _portrait[slot];
            if (img == null) return;

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
            // ⚠️ **必须先建**（它是同层第一个子节点 ⇒ 层级最低 ⇒ 不会盖住后面的 UI 元素）。
            UiLayoutFlow.BackdropArt(transform, ResPaths.MenuClassSelectScreen, new Color(0.04f, 0.04f, 0.05f, 1f));

            // 屏适配容器：改按高度 ×1.8 后原版整屏正好 1080 高（标题 480.6 / 底部按钮 -450 都在 ±540 内）
            var screen = UiLayoutFlow.FitRoot(transform, UiLayoutFlow.FitClass);

            // ── 原版文本框位置（逐条来自 ClassSelectMenu.prefab）──
            // ⚠️ `FlowLabel.Create` 的尺寸参数是**原版 px**（位图字模按原版像素排版）⇒ 常量表（Canvas 单位）
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
            }

            // ── ★ 原版半身像横排（5 个槽位）：先贴图（不可点），再把**原版热点矩形**当透明点击/悬停区 ──
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
                //   ★ 片 22：**停用槽（`NoClass`）传空路径** —— 节点照建（保住原版几何，后面由
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

            // ④ ★ 预热三态贴图（5 槽 × 3 态 = 15 张，最大 131×234）：让"悬停/选中"换图**同步**生效。
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
                // ★ 片 22：**停用槽（`NoClass`）不预热** —— 否则会去加载 `D2/UI/FrontEnd/-1/nu*_0`
                //   并刷 3 条 `[Resource] 加载失败` Error（实测过）。
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
            Log.Info("Ui", $"创角：已预热 {n} 张职业半身像（5 槽 × 3 态 = 原版 FrontEnd/{{职业}}/NU1..NU3 的帧 0）");
        }
    }
}
