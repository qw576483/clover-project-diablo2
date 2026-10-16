// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Skill/SkillModule.cs
// `ISkillModule` 的**唯一实现**（门面，internal，无参构造 ⇒ 可被 `AppContext.AutoWire` 反射创建）。
//
// 职责：技能树（5 职业 × 3 系）/ 学习（前置 + 等级 + 技能点）/ 左右键绑定 / 施放（法力 + 冷却 + 目标）
//       / 投射物（火弹、冰弹…）。
//
// 数据来源（**零硬编码**）：
//   · `skill_c`  → `class/tree/name/req_level/max_level/req_skill/skill_points/mana_cost/mana_per_lvl/
//                   dmg_min/dmg_max/dmg_type/passive/missile`
//   · `missile_c`→ `speed/range/radius/cel_file`（按 `skill_c.missile` → `missile_c.code` 关联）
//   · `class_c` / `level_c` 不直接使用（等级/技能点来自 `IPlayerModule`）
//
// 与其他模块的边界：
//   · 伤害结算**不在这里**：走 `Module/Combat` 的 `DamagePipeline`（抗性只减一次 + 三件套只有一份实现）。
//   · 扣法力走 `IPlayerModule.TrySpendMana(cost)`（契约已有）：
//     `RestoreMana(-cost)` 扣蓝会被静默钳掉（实测「施法不扣法力」）。现在消耗走独立入口；
//     顺序固定为 **校验法力足够（Warn，不是 Error）→ 扣蓝 → 再施放**；见 `SpendMana`。
//   · 技能树面板布局（行列）由本模块算好放进 `SkillDef.slotRow/slotCol`（`skill_c` 没有该列）。
//
// 单机：不碰引擎的网络类门面（`Game` 的 Net / Sync / Http 等，全为 null）。
// 随机一律注入式 `CloverEngine.Rng`（地图 seed 派生，可复现）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using AppContext = Diablo2.App.AppContext;

namespace Diablo2.Module.Skill
{
    /// <summary>技能门面实现（技能树 / 学习 / 绑定 / 施放 / 投射物）。</summary>
    internal sealed class SkillModule : ISkillModule
    {
        /// <summary>技能随机流 salt（投射物伤害掷骰用；与战斗/掉落/刷怪流分开，保证可复现）。</summary>
        private const int SkillSeedSalt = 0x5C11;

        // ── 状态 ─────────────────────────────────────────────────────────────
        // 决策：单职业 = 法师 ⇒ 初值也随默认职业（真正生效值由
        //   `ResetForClass(save.cls)` 在创角/读档时覆盖；此初值只影响"还没选角"的空窗期）。
        private PlayerClass _cls = PlayerClass.Sorceress;

        /// <summary>
        /// 学习限制总开关（**指令**：「学习技能不限制」）。
        /// <para>true ⇒ 不检查 ① 角色等级要求（`skill_c.req_level`）、② 前置技能（`skill_c.req_skill`）、
        /// ③ 技能点余额（点数不足也允许学；消耗按「有多少扣多少」实扣，绝不产生负点）。
        /// **满级上限（`skill_c.max_level`）仍然保留** —— 那是"这个技能已经练满"，与"能不能学"无关。</para>
        /// <para>为什么做成开关（而不是删检查）：这是**口径 / 调试体验**，不是"原版规则改了"——
        /// 将来要对回原版只需把这里改成 false；离线判据（combatcheck 8b）**读这个开关算期望**，两边都成立。</para>
        /// </summary>
        public static bool UnrestrictedLearning { get { return true; } }

        /// <summary>本职业全部技能定义（按 tree → reqLevel → id 排序；面板按它渲染）。</summary>
        private readonly List<SkillDef> _available = new List<SkillDef>();

        /// <summary>技能 id → 配表行（法力/被动/投射物等配表细节不在 `SkillDef` 里）。</summary>
        private readonly Dictionary<int, Table.BaseSkillRow> _rows = new Dictionary<int, Table.BaseSkillRow>();

        /// <summary>技能 id → 已学等级（0/缺省 = 未学）。</summary>
        private readonly Dictionary<int, int> _levels = new Dictionary<int, int>();

        /// <summary>技能 id → 剩余冷却（秒）。</summary>
        private readonly Dictionary<int, float> _cd = new Dictionary<int, float>();

        /// <summary>技能 id → 本次冷却总时长（秒；HUD 画冷却圈用）。</summary>
        private readonly Dictionary<int, float> _cdTotal = new Dictionary<int, float>();

        private readonly List<Projectile> _projectiles = new List<Projectile>();
        private readonly List<int> _cdKeys = new List<int>();

        // ── 技能效果────────────
        //   投射物只管"会飞的一颗"；留在原地或挂在施法者身上的东西（火墙/暴风雪/雷云/光环/多头蛇/
        //   陨石落点/新星环/连锁折线）走 `SkillEffect`（见该文件头"为什么与 Projectile 分开"）。
        private readonly List<SkillEffect> _effects = new List<SkillEffect>();
        private int _nextEffectId = 1;

        /// <summary>火焰之径每簇地火的素材池：官方 Blaze 落地混合 Big/Medium/Small
        /// （missiles.txt：`blaze`/`firewall`→Big、`firemedium`→Medium、`firesmall`→Small），
        /// 每簇随机取一种（见 <see cref="ResolveEffectTick"/>）。</summary>
        private static readonly string[] TrailGroundArts = { "groundFireBig", "groundFireMedium", "groundFireSmall" };

        /// <summary>限时增益（护甲 / 能量护盾 / 附魔 / 雷云风暴）：技能 id → 状态。</summary>
        private readonly Dictionary<int, ActiveBuff> _buffs = new Dictionary<int, ActiveBuff>();

        /// <summary>`_buffs` 的 key 快照（遍历时不改字典；`TickBuffs` 里回收到期项）。</summary>
        private readonly List<int> _buffKeys = new List<int>();

        /// <summary>温暖（Warmth）的法力回复累计器（秒）——每满 1 秒回一次，避免每帧碎回。</summary>
        private float _warmthAccum;

        /// <summary>一条限时增益（技能 id / 等级 / 剩余秒 / 总秒 / 光环表现 id）。</summary>
        private struct ActiveBuff
        {
            public int skillId;
            public int level;
            public float remain;
            public float total;
            public int auraEffectId;
        }

        /// <summary>左右键绑定（下标 0 = 左键，1 = 右键；-1 = 普通攻击）。</summary>
        private readonly int[] _buttons = { -1, -1 };

        /// <summary>
        /// 技能热键（原版 `F1`~`F8`）各自记住的技能 id；<see cref="HotkeyUnset"/> = 还没设置。
        /// <para>原版口径（见 `Events.SkillHotkeyRequest`）：热键是**玩家自己设的**（在技能速度棒里
        /// 悬停技能图标 + 按 F 键），不是"第 N 个已学技能"。</para>
        /// </summary>
        private readonly int[] _hotkeys = new int[GameKeyAlias.SkillSlotCount];

        /// <summary>热键"还没设置"的哨兵值（与 `-1` = 普通攻击区分开）。</summary>
        private const int HotkeyUnset = -2;

        /// <summary>技能速度棒当前是否开着（原版 `S`；开着时 F 键 = **设置**热键）。</summary>
        private bool _speedBarOpen;

        /// <summary>速度棒里指针停在哪个技能上（`HotkeyUnset` = 没停在任何格上；`-1` = 普通攻击）。</summary>
        private int _speedBarHover = HotkeyUnset;

        /// <summary>速度棒本次打开时点的是哪只手（`-1` = `S` 打开的、没指定手；0 = 左键；1 = 右键）。</summary>
        private int _speedBarButton = -1;

        private int _selected = -1;
        private int _nextProjectileId = 1;
        private CharacterSave _save;
        private Rng _rng;

        /// <summary>
        /// 技能输入订阅（`Events.SkillHotkeyRequest` / `SkillWheelRequest` / `SkillSpeedBarChanged` /
        /// `SkillSlotBindRequest` / `PlayerDamaged`）是否已挂（**幂等**；一起挂、一起算已挂）。
        /// <para>为什么不在构造函数里订阅：本模块由 `AppContext.AutoWire` 在**很早**的时刻创建，
        /// 那一刻 `Game.Event` 可能还没挂上（构造时订阅会静默丢订阅）⇒ 改为首个
        /// `ResetForClass`（= 创角/读档进图，必然发生在任何游戏内按键之前）时补订阅。</para>
        /// </summary>
        private bool _slotSubscribed;

        // ═════════════════════════════════════════════════════════════════════
        // ISkillModule：只读状态
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public PlayerClass Class { get { return _cls; } }

        /// <inheritdoc />
        public int SelectedSkillId { get { return _selected; } }

        /// <inheritdoc />
        public IReadOnlyList<SkillDef> Available { get { return _available; } }

        /// <inheritdoc />
        public int GetLevel(int skillId)
        {
            if (skillId <= 0) return 0;
            return _levels.TryGetValue(skillId, out var lv) ? lv : 0;
        }

        /// <inheritdoc />
        public float GetCooldownRemain(int skillId)
        {
            if (skillId <= 0) return 0f;
            return _cd.TryGetValue(skillId, out var v) && v > 0f ? v : 0f;
        }

        /// <inheritdoc />
        public int GetButtonSkill(int button)
        {
            if (button < 0 || button >= _buttons.Length)
            {
                SkillLog.WarnThrottled("btn.oob", $"GetButtonSkill({button})：按钮下标越界（合法 0/1）⇒ 返回 -1");
                return -1;
            }
            return _buttons[button];
        }

        // ═════════════════════════════════════════════════════════════════════
        // 技能树：切换职业 / 学习 / 绑定
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void ResetForClass(PlayerClass cls, CharacterSave save)
        {
            EnsureSubscribed();     // 技能热键 / 滚轮 / 速度棒状态的订阅点（幂等）
            _cls = cls;
            _save = save;

            _levels.Clear();
            _cd.Clear();
            _cdTotal.Clear();
            ClearProjectiles();
            _buttons[0] = -1;
            _buttons[1] = -1;
            _selected = -1;
            _rng = null;
            for (var i = 0; i < _hotkeys.Length; i++) _hotkeys[i] = HotkeyUnset;   // 热键全部按"未设置"起步
            _speedBarOpen = false;
            _speedBarHover = HotkeyUnset;
            _speedBarButton = -1;

            BuildAvailable();

            if (save != null)
            {
                LoadLevelsFrom(save);
                LoadButtonsFrom(save);
                LoadHotkeysFrom(save);
            }
            else
            {
                SkillLog.Warn("ResetForClass: save 为 null（未选角色？）⇒ 技能等级/绑键按空状态初始化");
            }

            _selected = _buttons[1];

            SkillLog.Info($"ResetForClass：职业={_cls}（{(int)_cls}）技能 {_available.Count} 个（" +
                          $"系分布 1/2/3 = {CountTree(0)}/{CountTree(1)}/{CountTree(2)}），" +
                          $"已学 {_levels.Count} 个，左键={_buttons[0]} 右键={_buttons[1]}，" +
                          $"热键已设 {CountHotkeys()} 个");

            RaiseTreeChanged();
            RaiseButtonsChanged();      // 读档后把左右键绑定推给 HUD（重启后 HUD 也一致）
            RaiseHotkeysChanged();      // 热键标签（速度棒里 F1~F8）也推一次
        }

        /// <inheritdoc />
        public bool CanLearn(int skillId)
        {
            return CanLearnInternal(skillId, true);
        }

        /// <inheritdoc />
        public bool Learn(int skillId)
        {
            // 注意：`CanLearnInternal(id, true)` 已把**不满足的原因**打进日志（等级/前置/点数/满级）
            if (!CanLearnInternal(skillId, true)) return false;

            var player = Player;
            if (player == null) return false;      // CanLearnInternal 已告警

            var cost = CostPointsOf(skillId);
            var newLevel = GetLevel(skillId) + 1;
            _levels[skillId] = newLevel;
            // 放开口径下点数不足也允许学 ⇒ 消耗按「有多少扣多少」实扣（绝不为负，
            //   也不空调 AddSkillPoint(0)）；原版口径下 CanLearnInternal 已保证点数 ≥ cost ⇒ spend 恒 == cost。
            var spend = player.SkillPoints < cost ? player.SkillPoints : cost;
            if (spend > 0) player.AddSkillPoint(-spend);
            MirrorLevelsToSave();

            var def = FindDef(skillId);
            SkillLog.Info($"Learn：{def?.name}#{skillId} → 等级 {newLevel}/{def?.maxLevel}" +
                          $"（消耗技能点 {cost}{(spend != cost ? $"、实扣 {spend}（点数不足，放开口径）" : "")}，" +
                          $"剩余 {player.SkillPoints}）");

            if (Game.Event != null) Game.Event.Emit(Events.SkillLearned, skillId);
            RaiseTreeChanged();
            return true;
        }

        /// <inheritdoc />
        public void SelectSkill(int skillId)
        {
            if (skillId != -1 && !ValidateSelectable(skillId, "SelectSkill")) return;

            _selected = skillId;
            _buttons[1] = skillId;
            MirrorButtonsToSave();

            SkillLog.Info($"SelectSkill：当前右键技能 = {Describe(skillId)}");
            if (Game.Event != null) Game.Event.Emit(Events.SkillSelected, skillId);
            RaiseButtonsChanged();      // HUD 的左右技能格换图
        }

        /// <inheritdoc />
        public void AssignToButton(int button, int skillId)
        {
            if (button < 0 || button >= _buttons.Length)
            {
                SkillLog.Warn($"AssignToButton({button}, {skillId})：按钮下标非法（合法 0 = 左键 / 1 = 右键）⇒ 忽略");
                return;
            }
            if (skillId != -1 && !ValidateSelectable(skillId, "AssignToButton")) return;

            // 原版左键限制：**非攻击类**不能装左键（官方 `skills.txt` 的 `leftskill` 列；
            //   本工程取值见 `SkillCatalog.CanLeftCast`）。右键不拦（原版"右键可以设置所有的技能"）。
            if (button == 0 && !CanLeftCast(skillId))
            {
                SkillLog.Warn($"AssignToButton：{Describe(skillId)} 按原版**不能用鼠标左键**" +
                              $"（官方 skills.txt 的 leftskill 列为空）⇒ 本次忽略；右键可以装任何技能");
                Notify("leftcast", $"「{NameOf(skillId)}」不能用鼠标左键施放（原版规则）——请装到右键");
                return;
            }

            _buttons[button] = skillId;
            if (button == 1) _selected = skillId;
            MirrorButtonsToSave();

            SkillLog.Info($"AssignToButton：{(button == 0 ? "左键" : "右键")} = {Describe(skillId)}");
            if (button == 1 && Game.Event != null) Game.Event.Emit(Events.SkillSelected, skillId);
            RaiseButtonsChanged();      // HUD 的左右技能格换图
        }

        /// <summary>
        /// 订阅技能热键 / 滚轮 / 速度棒状态 / 拖拽落格 / 拖拽落格 / 受伤补偿这些意图事件。
        /// 幂等；`Game.Event` 还没挂上时**不置位**（下次 `ResetForClass` 再试）。
        /// </summary>
        private void EnsureSubscribed()
        {
            if (_slotSubscribed || Game.Event == null) return;
            Game.Event.On<int>(Events.SkillHotkeyRequest, OnSkillHotkeyRequest);
            Game.Event.On<int>(Events.SkillWheelRequest, OnSkillWheelRequest);
            Game.Event.On<SkillSpeedBarArgs>(Events.SkillSpeedBarChanged, OnSkillSpeedBarChanged);
            Game.Event.On<int>(Events.SkillSlotBindRequest, OnSkillSlotBindRequest);
            // ：三种护甲的"挨打反伤"与能量护盾的"扣蓝抵伤"都挂在**受伤事件**上
            //   （`DamagePipeline` 结算完玩家伤害后 Emit；本模块只做**事后补偿**，不改伤害管线）
            Game.Event.On<DamageArgs>(Events.PlayerDamaged, OnPlayerDamaged);
            _slotSubscribed = true;
            SkillLog.Info($"已订阅 {Events.SkillHotkeyRequest}（原版 F1~F8：速度棒悬停时=设热键，否则=切右键技能）、"
                + $"{Events.SkillWheelRequest}（原版滚轮：在已设热键的技能里切前/后一项）、"
                + $"{Events.SkillSpeedBarChanged}（技能速度棒开/关与悬停）、"
                + $"{Events.SkillSlotBindRequest}（速度棒点图标 / 技能树拖到左右技能格）与 "
                + $"{Events.PlayerDamaged}（护甲反伤 / 能量护盾按法力抵伤）");
        }

        /// <summary>
        /// 拖拽/点选落格绑定（<see cref="Events.SkillSlotBindRequest"/>）：把**具体技能 id** 绑到左/右键技能格。
        /// <para>载荷 = <c>button(0/1) | (skillId &lt;&lt; 1)</c>；发送方 = `UI/HudPanel`（技能速度棒里点图标）
        /// 与 `UI/SkillTreePanel.OnEndDrag`。</para>
        /// <para>校验走既有 <see cref="AssignToButton"/>（`ValidateSelectable`：须已学、非被动；
        /// **左键另加官方的 leftskill 限制**；被拒有日志与 Toast）。</para>
        /// </summary>
        private void OnSkillSlotBindRequest(int packed)
        {
            var button = packed & 1;
            var skillId = packed >> 1;
            SkillLog.Info($"收到 {Events.SkillSlotBindRequest}：把技能 #{skillId} 绑到"
                + $"{(button == 0 ? "左键" : "右键")}技能格（速度棒点选 / 技能树拖拽落格）");
            AssignToButton(button, skillId);
        }

        /// <summary>
        /// 技能速度棒（原版 `S`）的开/关与悬停（<see cref="Events.SkillSpeedBarChanged"/>）。
        /// <para>只存三个状态：开没开、指针停在哪个技能上、本次打开时点的是哪只手 ——
        /// 它们决定 `F1`~`F8` 是"设"还是"切"（见 <see cref="OnSkillHotkeyRequest"/>）。</para>
        /// </summary>
        private void OnSkillSpeedBarChanged(SkillSpeedBarArgs args)
        {
            if (args == null) return;
            _speedBarOpen = args.open;
            _speedBarHover = args.hoverSkillId;
            _speedBarButton = args.targetButton;
            SkillLog.Info($"技能速度棒：{(args.open ? "开" : "关")}"
                + $"，悬停 = {(_speedBarHover == HotkeyUnset ? "（没有停在任何技能上）" : Describe(_speedBarHover))}"
                + $"，目标手 = {(_speedBarButton < 0 ? "未指定（点技能图标默认装右键）" : _speedBarButton == 0 ? "左键" : "右键")}"
                + $" ⇒ 此时按 F1~F8 = {(_speedBarOpen && _speedBarHover != HotkeyUnset ? "**设置**该热键" : "**切换**右键技能")}");
        }

        /// <summary>
        /// 按下技能热键（原版 `F1`~`F8`）。**同一组键两种含义**（原版口径，出处见
        /// `Def/GameKeyAlias.cs` 的技能热键段 + `Events.SkillHotkeyRequest`）：
        /// <list type="number">
        /// <item>技能速度棒开着且指针停在某个技能上 ⇒ **设置**：把这个技能记到该热键上（落档 + 刷新标签）；</item>
        /// <item>否则 ⇒ **切换**：把这个热键记住的技能装到**右键**技能格（原版"readies the associated
        /// Right Mouse Button Skill"；施放仍要点鼠标）。</item>
        /// </list>
        /// <para>非预期分支（都留日志）：槽号越界、热键还没设过、热键记的技能已经洗掉/不属于本职业。
        /// 注意**被动技能可以设成热键**（官方 `skills.txt` 的 `leftskill=1` 覆盖法师 4 个被动 ⇒
        /// 被动与别的技能同权；绑上后点它不产生施放，见 `ValidateSelectable` 的注释）。</para>
        /// </summary>
        private void OnSkillHotkeyRequest(int slot)
        {
            var index = slot - 1;
            if (index < 0 || index >= _hotkeys.Length)
            {
                SkillLog.WarnThrottled("hotkey.oob",
                    $"技能热键：槽号 {slot} 越界（合法 1..{_hotkeys.Length}）⇒ 忽略本次按键");
                return;
            }

            // ── 分支 1：速度棒开着 + 指针停在某个技能上 ⇒ 设置热键 ──────────────────
            if (_speedBarOpen && _speedBarHover != HotkeyUnset)
            {
                var id = _speedBarHover;
                if (id != -1 && !ValidateSelectable(id, $"设热键F{slot}")) return;
                _hotkeys[index] = id;
                MirrorHotkeysToSave();
                SkillLog.Info($"技能热键：F{slot} ← {Describe(id)}（在技能速度棒里按 F{slot} 设置）");
                Notify("hotkey.set", $"F{slot} = {NameOf(id)}");
                RaiseHotkeysChanged();
                return;
            }

            // ── 分支 2：切换右键技能 ────────────────────────────────────────────
            var bound = _hotkeys[index];
            if (bound == HotkeyUnset)
            {
                SkillLog.WarnThrottled("hotkey.unset",
                    $"技能热键：F{slot} 还没设置过（原版口径：热键要先在技能速度棒里悬停技能图标 + 按 F 键设置）" +
                    $"⇒ 不改变右键技能；按 {GameKeyAlias.KeySkillSpeedBar} 打开技能速度棒去设一个");
                Notify("hotkey.unset", $"F{slot} 还没设置技能（按 {GameKeyAlias.KeySkillSpeedBar} 打开技能速度棒设置）");
                return;
            }

            if (bound != -1 && GetLevel(bound) <= 0)
            {
                SkillLog.WarnThrottled("hotkey.stale",
                    $"技能热键：F{slot} 记的是 {Describe(bound)}，但它已经不在已学技能里（洗点/坏档？）" +
                    $"⇒ 本次不切换；请重新在技能速度棒里设置");
                Notify("hotkey.stale", $"F{slot} 记的技能已不可用——请重新设置");
                return;
            }

            SkillLog.Info($"技能热键：F{slot} ⇒ 切换右键技能 = {Describe(bound)}（原版口径：热键改的是右键技能，"
                + "施放仍要点鼠标）");
            AssignToButton(1, bound);
        }

        /// <summary>
        /// 鼠标滚轮切换右键技能（<see cref="Events.SkillWheelRequest"/>；参数 `+1` = 前一项 / `-1` = 下一项）。
        /// <para>原版口径（手册 + 串表 3944/3945）：滚轮在**已设热键的技能**里循环，作用于**右键**技能格。
        /// 候选集 = 8 个热键里已设置且仍可用的技能，按热键号 1→8；当前右键技能不在候选集里 ⇒ 从
        /// "下一项"取第一个、"前一项"取最后一个（与 `S` 之后按方向键的手感一致）。</para>
        /// </summary>
        private void OnSkillWheelRequest(int dir)
        {
            if (dir == 0) return;

            var ring = new List<int>();
            var curPos = -1;
            for (var i = 0; i < _hotkeys.Length; i++)
            {
                var id = _hotkeys[i];
                if (id == HotkeyUnset) continue;
                if (id != -1 && GetLevel(id) <= 0) continue;      // 洗掉的技能不进候选
                if (id == _buttons[1]) curPos = ring.Count;
                ring.Add(id);
            }

            if (ring.Count == 0)
            {
                SkillLog.WarnThrottled("wheel.empty",
                    $"技能滚轮：一个热键都没设置过 ⇒ 不切换右键技能（先按 {GameKeyAlias.KeySkillSpeedBar} "
                    + "打开技能速度棒，悬停技能图标 + 按 F1~F8 设置）");
                return;
            }

            var pos = dir > 0
                ? (curPos < 0 ? 0 : (curPos - 1 + ring.Count) % ring.Count)     // 前一项
                : (curPos < 0 ? ring.Count - 1 : (curPos + 1) % ring.Count);    // 下一项

            SkillLog.Info($"技能滚轮：{(dir > 0 ? "向上（前一项）" : "向下（下一项）")} ⇒ 右键技能 = "
                + $"{Describe(ring[pos])}（候选 = 已设热键的技能 {ring.Count} 个）");
            AssignToButton(1, ring[pos]);
        }

        /// <summary>
        /// 这个技能能不能装**鼠标左键**（原版限制）。取值 = `SkillCatalog.CanLeftCast`（官方
        /// `skills.txt` 的 `leftskill` 列）；取不到配表行 ⇒ 不拦（宁可漏拦也不误拦）。
        /// </summary>
        private bool CanLeftCast(int skillId)
        {
            if (skillId == -1) return SkillCatalog.CanLeftCast(-1, null);
            var row = _rows.TryGetValue(skillId, out var r) ? r : null;
            return SkillCatalog.CanLeftCast(skillId, row);
        }

        /// <summary>已设置的热键个数（日志/判据用）。</summary>
        private int CountHotkeys()
        {
            var n = 0;
            for (var i = 0; i < _hotkeys.Length; i++)
            {
                if (_hotkeys[i] != HotkeyUnset) n++;
            }
            return n;
        }

        /// <summary>
        /// 广播左右键技能格绑定快照（`Events.SkillButtonsChanged`，载荷 `Def.SkillButtonsArgs`）。
        /// 收方 = `UI/HudPanel`（换 `LeftSkill` / `RightSkill` 两格的图标）。
        /// </summary>
        private void RaiseButtonsChanged()
        {
            if (Game.Event == null) return;

            var args = new SkillButtonsArgs
            {
                leftId = _buttons[0],
                rightId = _buttons[1],
                leftName = NameOf(_buttons[0]),
                rightName = NameOf(_buttons[1]),
            };
            Game.Event.Emit(Events.SkillButtonsChanged, args);
            SkillLog.Info($"左右键技能格快照：左={args.leftName}({args.leftId}) 右={args.rightName}({args.rightId})");
        }

        /// <summary>技能显示名（-1 = 普通攻击；未载入的 id 原样回显）。</summary>
        private string NameOf(int skillId)
        {
            if (skillId == -1) return "普通攻击";
            var def = FindDef(skillId);
            return def != null ? def.name : "#" + skillId;
        }

        /// <summary>
        /// 广播技能热键表（`Events.SkillHotkeysChanged`，载荷 `Def.SkillHotkeysArgs`）。
        /// 收方 = `UI/HudPanel`（技能速度棒里给已设热键的技能图标贴 `F1`~`F8` 标签）。
        /// </summary>
        private void RaiseHotkeysChanged()
        {
            if (Game.Event == null) return;

            var args = new SkillHotkeysArgs { count = _hotkeys.Length };
            for (var i = 0; i < _hotkeys.Length; i++) args.ids.Add(_hotkeys[i]);
            Game.Event.Emit(Events.SkillHotkeysChanged, args);

            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < _hotkeys.Length; i++)
            {
                if (_hotkeys[i] == HotkeyUnset) continue;
                if (sb.Length > 0) sb.Append("  ");
                sb.Append('F').Append(i + 1).Append('=').Append(NameOf(_hotkeys[i]));
            }
            SkillLog.Info($"技能热键快照：{(sb.Length == 0 ? "（一个都没设置）" : sb.ToString())}");
        }

        /// <inheritdoc />
        public SkillTreeArgs BuildTree()
        {
            var args = new SkillTreeArgs { cls = _cls };

            var player = Player;
            if (player != null) args.skillPoints = player.SkillPoints;
            else
            {
                SkillLog.WarnOnce("tree.noplayer",
                    "BuildTree: IPlayerModule 未接入（AppContext.Player == null）⇒ 技能点按 0 渲染，" +
                    "所有技能都会显示为不可学");
            }

            for (var i = 0; i < _available.Count; i++)
            {
                var def = _available[i];
                var level = GetLevel(def.id);
                def.descLines = DescLinesOf(def, level);   // 说明窗字段行：模块算好、UI 照画（见契约注释）
                args.skills.Add(def);
                args.learnedLevels.Add(level);
                args.learnable.Add(CanLearnInternal(def.id, false));   // 面板每帧都可能重绘 ⇒ 不打日志
            }
            return args;
        }

        /// <summary>
        /// 说明窗的字段行：**原版说明文本**（`SkillDef.desc`）+ 原有字段名/值行。
        /// <para>原版说明窗布局（Phrozen Keep《SkillDesc.txt & descline reference》Appendix A）=
        /// `str name` → **`str long`** → 数值行 ⇒ 本方法产 `def.desc` 的段（逐行）**在前**、
        /// 数值行在后；技能名由 UI 侧拼在最前（`SkillTreePanel.DescTextOf`）。</para>
        /// <para>标签**单独一行**、值另起一行 —— 可见区 78 art = 7 个 font8 汉字，「标签 + 值」放不下
        /// （算术见 `Def.SkillDef.descLines` 的注释）。</para>
        /// <para>值只取既有生产函数；等级 0（未学）时耗蓝/伤害在那些函数里就是 0 ⇒ 只剩等级行，
        /// **不写占位符**、也不拿 1 级的数字冒充（未学技能的预览值本项目没有出处）。</para>
        /// <para>说明段可能超 78 art 宽（原版长句 10+ 字）⇒ 折行交给 UI 侧 `Text` 的 `Wrap`
        /// （按框宽折，口径 = libd2 `font.zig` breakLine，见 `UI/SkillTreePanel` 区段注释）。</para>
        /// </summary>
        private List<string> DescLinesOf(SkillDef def, int level)
        {
            var lines = new List<string>(12);

            //   原版说明文本（`skill_c.desc` = 官方 `SkillDesc` 的 `str long` 串逐字；段序 = 显示序）。
            //   落表是**字面 `\n`**（反斜杠 + n，TSV 单元里不能有真实换行；先例 = 原版
            //   `chi_string.txt` 的格式，见 `原版资源/清单.md` §5.7）⇒ 这里按字面 `\n` 拆段。
            if (!string.IsNullOrEmpty(def.desc))
            {
                var segs = def.desc.Split(new[] { "\\n" }, StringSplitOptions.None);
                for (var i = 0; i < segs.Length; i++)
                {
                    var seg = segs[i].Trim();
                    if (seg.Length > 0) lines.Add(seg);
                }
            }

            lines.Add(LabelSkillLevel);
            lines.Add(level.ToString());

            if (level <= 0) return lines;                        // 0 级：耗蓝/伤害都算不出（生产函数返回 0）
            if (!_rows.TryGetValue(def.id, out var row) || row == null) return lines;

            lines.Add(LabelManaCost);
            lines.Add(Combat.DamageFormula.SkillManaCost(row, level).ToString());

            Combat.DamageFormula.SkillDamageRange(row, level, out var dmgMin, out var dmgMax);
            if (dmgMax > 0)                                     // 武器伤害类（`SrcDam` 非空）算不出区间 ⇒ 不出这一行
            {
                lines.Add(DamageLabelOf(def.dmgType));
                lines.Add(dmgMin + "-" + dmgMax);
            }
            return lines;
        }

        /// <summary>伤害字段名；<see cref="DamageType.Magic"/> 用中性的「傷害：」（原版 4256-4260 里没有魔法伤害一行）。</summary>
        private static string DamageLabelOf(DamageType t)
        {
            switch (t)
            {
                case DamageType.Fire: return LabelFireDamage;
                case DamageType.Cold: return LabelColdDamage;
                case DamageType.Lightning: return LabelLightningDamage;
                case DamageType.Poison: return LabelPoisonDamage;
                default: return LabelDamage;
            }
        }

        //  字段名出处 = 原版串表 `原版资源/d2text/chi_string.txt` 的 id（原版 UI 文案在本项目没有配表载体，
        //  与 `UI/CharacterPanel` 的四维/派生名同一写法：常量 + id 注释）。逐字都在原版 chi 字模里
        //  （`font8_chi_map.txt` 可查，`目前技能等級：` 7 字实测 advance 各 11 art）。
        private const string LabelSkillLevel = "目前技能等級：";   // id 4254
        private const string LabelManaCost = "法力耗費：";          // id 4255
        private const string LabelDamage = "傷害：";                // id 4256
        private const string LabelFireDamage = "火焰傷害：";        // id 4257
        private const string LabelColdDamage = "冰凍傷害：";        // id 4258
        private const string LabelLightningDamage = "閃電傷害：";   // id 4259
        private const string LabelPoisonDamage = "毒素傷害：";      // id 4260

        // ═════════════════════════════════════════════════════════════════════
        // 施放
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public bool TryCast(int skillId, Vector2Int targetGrid)
        {
            var def = FindDef(skillId);
            if (def == null)
            {
                SkillLog.WarnThrottled("cast.nodef",
                    $"TryCast({skillId})：该技能不属于当前职业 {_cls} 或不存在 ⇒ 施放失败");
                Notify("cast.nodef", $"技能 #{skillId} 不属于当前职业（放不出来）");
                return false;
            }

            if (IsPassive(skillId))
            {
                SkillLog.WarnThrottled("cast.passive",
                    $"TryCast：{def.name}#{skillId} 是被动技能（官方 passive=1）⇒ 不能主动施放");
                //   绑定**不拦**（官方 `leftskill` 列在被动上就是 1，见 `ValidateSelectable` 的文档），
                //   点了给个说法即可 —— 实测：装了被动、点了完全没反应最伤观感的就是这里没提示。
                Notify("cast.passive", $"「{def.name}」是被动技能 —— 一直都在生效、不需要点（要换掉就把别的技能拖到鼠标键上）");
                return false;
            }

            var level = GetLevel(skillId);
            if (level <= 0)
            {
                SkillLog.WarnThrottled("cast.nolearn",
                    $"TryCast：{def.name}#{skillId} 尚未学习（等级 0）⇒ 施放失败");
                Notify("cast.nolearn", $"「{def.name}」还没学 —— 先按 T 打开技能树加点");
                return false;
            }

            var cdRemain = GetCooldownRemain(skillId);
            if (cdRemain > 0f)
            {
                SkillLog.WarnThrottled("cast.cd",
                    $"TryCast：{def.name}#{skillId} 冷却中（剩 {cdRemain:0.00}s / 共 {CdTotalOf(skillId):0.00}s）⇒ 施放失败");
                Notify("cast.cd", $"「{def.name}」冷却中（还剩 {cdRemain:0.0} 秒）");
                return false;
            }

            var player = Player;
            if (player == null)
            {
                SkillLog.WarnOnce("cast.noplayer",
                    "TryCast: IPlayerModule 未接入（AppContext.Player == null）⇒ 无法扣蓝/取位置，施放失败");
                return false;
            }

            var cost = ManaCostOf(skillId, level);
            if (player.Mana < cost)
            {
                // 节流（不是裸 Warn）：左键/右键**按住连发**时这条会每帧命中
                // （`PlayerModule` 的持续交战/按住分支每 tick 都调一次 TryCast）
                SkillLog.WarnThrottled("cast.mana",
                    $"TryCast：{def.name}#{skillId} 法力不足（需要 {cost}，当前 {player.Mana}）⇒ 施放失败");
                Notify("cast.mana", $"法力不足：「{def.name}」需要 {cost}，当前 {player.Mana}");
                return false;
            }

            // ── 行为目录（提前取：目标闸也要用它 —— 见 `SkillSpec.target` 的注释）──
            //   行为目录 = **本项目新增**的唯一出处（`skill_c` 没有"这个技能怎么打"的列 ⇒ 见
            //   `SkillCatalog` 文件头）。同一个"有伤害 + 有 missile"的组合在此分派成：
            //   直线弹 / 落点炸开 / 自身新星 / 地面持续场 / 直线扫射 / 连锁 / 延迟落点 / 光环 / 召唤 ——
            //   每一种都**带画面**（`SkillEffect` + `SkillFxView`），这是 指令
            //   「每个技能都要有表现 / 要有命中效果、特效」的落点。
            var row = _rows[skillId];
            var spec = SkillCatalog.Of(skillId, row, def);

            // ── 目标判定（在**扣蓝之前**做：否则"点空"会白扣蓝）──
            //   ⚠️ 用 `spec.target` 而**不是** `def.target`：后者是"有伤害 ⇒ Enemy"的粗判，
            //   会把新星/雷云/护甲/火墙这类技能判成"必须点到怪"⇒ 点空地放不出来（回报的
            //   "放技能没效果"）。表外职业由 `Infer` 填回同一个粗判值 ⇒ 零回归。
            var targetMonsterId = -1;
            switch (spec.target)
            {
                case SkillTarget.Enemy:
                    targetMonsterId = FindMonsterNear(targetGrid, SkillTuning.EnemySearchRadius);
                    if (targetMonsterId < 0)
                    {
                        SkillLog.WarnThrottled("cast.notarget",
                            $"TryCast：{def.name}#{skillId} 需要敌方目标，但格 {targetGrid}（半径 " +
                            $"{SkillTuning.EnemySearchRadius}）附近没有存活怪物 ⇒ 施放失败（未扣蓝）");
                        Notify("cast.notarget", $"「{def.name}」要对着怪物放 —— 指针下没有目标");
                        return false;
                    }
                    break;

                case SkillTarget.Ground:
                case SkillTarget.Self:
                case SkillTarget.None:
                    break;

                default:
                    SkillLog.WarnThrottled("cast.unknowntarget",
                        $"TryCast：{def.name}#{skillId} 的目标类型 {(int)spec.target} 未登记 ⇒ 按无目标处理");
                    break;
            }

            // ── 扣蓝 ──
            if (!SpendMana(player, cost, def.name)) return false;

            // ── 转身（原版语义：**施放时角色面向施法方向**）──
            //   口径与近战完全同源：`Module/Combat/CombatModule.RequestAttack` 在出手前调
            //   `player.FaceTo(monsterGrid)`（注释原文"点怪 ⇒ 角色先**面向目标** ⇒ 出手"）。
            //   放在**通过全部闸之后**（已学 / 非被动 / 不在冷却 / 蓝够 / 目标合格）：
            //   被拒的施放不转身 —— 与近战"没挥出去就不消耗冷却"同一语义。
            //   `FaceTo` 只改朝向（内部走引擎 `Iso.DirectionTo`），不打断移动/不打断持续交战；
            //   自身/地面技能（新星 / 护甲 / 火墙）用的也是**指针那一格** ⇒ 原版同样是"朝鼠标转"。
            //   ⛔ 别挪到 `TryCast` 之外：本方法是左键技能、右键技能、按住连发三条入口的唯一交汇点，
            //   放在调用方（`PlayerModule`）会漏掉"按住连发每 tick 直调 TryCast"那条路。
            player.FaceTo(targetGrid);

            // ── 冷却 ──
            var cd = CooldownSeconds(def, spec, level);
            _cd[skillId] = cd;
            _cdTotal[skillId] = cd;

            // ── 施法表现（音效钩子可空；施法动画由 View 监听 `SkillCast` 自行播放）──
            var ctx = AppContext.I;
            var audio = ctx != null ? ctx.Audio : null;
            if (audio != null)
            {
                audio.SfxAt(Combat.SfxKeys.CastOf(def.dmgType), player.World.x, player.World.y, player.World.z);
            }

            SkillLog.Info($"TryCast：{def.name}#{skillId}（等级 {level}）→ 目标格 {targetGrid} 目标怪 " +
                          $"{Describe(targetMonsterId)}；法力 {player.Mana + cost} → {player.Mana}（-{cost}），" +
                          $"冷却 {cd:0.00}s，伤害类型 {def.dmgType}");

            if (Game.Event != null) Game.Event.Emit(Events.SkillCast, skillId);

            // ── 效果 ──（`spec` 已在上面取好：目标闸与分派用的是同一份行为目录）
            Combat.DamageFormula.SkillDamageRange(row, level, out var dmgMin, out var dmgMax);

            //   非空 = **武器伤害类**攻击技能（Bash/Smite/Zeal/Sacrifice/...）。它们的
            //   `MinDam/MaxDam/EMin/EMax` 官方**本来就是空**（伤害 = 武器伤害 × `calc1` 的 damage%）
            //   ⇒ `dmgMin/dmgMax` 会是 0，但**绝不是**"辅助/增益技能"。
            //      像「火焰箭」这类 `SrcDam` 也非空、但**另有元素伤害**的行维持原路径
            //      （原版 = 武器伤害 + 元素，本项目只结算元素 ⇒ 已登记，不在此处静默改口径）。
            var weaponOnly = row.SrcDam > 0 && dmgMax <= 0;
            if (row.SrcDam > 0 && dmgMax > 0)
            {
                SkillLog.WarnOnce("cast.weapon.tail." + skillId,
                    $"TryCast：{def.name}#{skillId} 官方 SrcDam={row.SrcDam}（武器伤害转移 " +
                    $"{row.SrcDam}/128）**另有**自身元素伤害 ⇒ 本次仍只结算元素部分，" +
                    "武器伤害部分未叠加（原版 = 两者相加；本项目未建模双通道，已登记）");
            }

            return Execute(skillId, def, row, spec, player, level, targetGrid, targetMonsterId,
                dmgMin, dmgMax, weaponOnly);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 行为执行（法师 30 技能 = `SkillCatalog` 的 16 种行为；表外技能走 `Infer` 的等价分支）
        //
        // 纪律（三条，与改动前的 `TryCast` 尾段一致）：
        //   ① 伤害结算**只**走 `DamagePipeline.ApplyToMonster`（抗性/致命/飘字/受击表现只有一份实现）；
        //   ② 每一个分支**都必须留下看得见的东西**（`SpawnEffect` / `ProjectileView` / 飘字 / 镜头）
        //      —— 这是要求：每个技能都要有表现的机械判据（`combatcheck` 逐技能断言
        //      "施放后 `_effects` + `_projectiles` 至少有一个"）；
        //   ③ 扣蓝/冷却已在 `TryCast` 里完成 ⇒ 这里**拒绝执行**的路径必须**退蓝**（见 `Refuse`），
        //      绝不允许"蓝扣了、什么都没发生"（那正是回报的"放技能没效果"）。
        // ═════════════════════════════════════════════════════════════════════
        private bool Execute(int skillId, SkillDef def, Table.BaseSkillRow row, SkillSpec spec,
            IPlayerModule player, int level, Vector2Int targetGrid, int targetMonsterId,
            int dmgMin, int dmgMax, bool weaponOnly)
        {
            switch (spec.behavior)
            {
                case SkillBehavior.Passive:
                    // 上游 `IsPassive` 已拒；表外行不可能落到这里（`Infer` 也把被动单独分出去）
                    SkillLog.WarnThrottled("cast.passive.exec",
                        $"Execute：{def.name}#{skillId} 是被动技能 ⇒ 不执行（被动在 `TickPassives` 里持续生效）");
                    return false;

                // ── 飞行体三兄弟：直线 / 落点炸开 / 多发散射 ──────────────────────
                case SkillBehavior.Projectile:
                case SkillBehavior.SplashProjectile:
                case SkillBehavior.MultiProjectile:
                    return FireProjectiles(skillId, def, row, spec, player, targetGrid,
                        dmgMin, dmgMax, weaponOnly);

                // ── 在目标格立即结算（表外技能的旧口径：单体 / 小范围）──────────────
                case SkillBehavior.InstantArea:
                    return ResolveInstant(skillId, def, row, spec, player, level, targetGrid,
                        targetMonsterId, dmgMin, dmgMax, weaponOnly);

                // ── 自身为圆心（新星 / 霜之新星）─────────────────────────────────
                case SkillBehavior.Nova:
                {
                    var center = player.Grid;
                    var hits = ResolveAreaDamage(center, spec.radius, skillId, def.name, def.dmgType,
                        dmgMin, dmgMax, out var dmg);
                    SpawnEffect(skillId, def.name, SkillFxKind.Nova, Center(center), def.dmgType,
                        spec.art, spec.impactArt, spec.radius, spec.duration <= 0f ? 0.45f : spec.duration,
                        dmgMin, dmgMax);
                    Shake(0.10f, 0.16f);
                    SkillLog.Info($"[Skill] {def.name}#{skillId}（新星）以施法者 {center} 为圆心、半径 " +
                                  $"{spec.radius} 格 ⇒ 命中 {hits} 个目标，合计伤害 {dmg}（{def.dmgType}）");
                    return true;
                }

                // ── 地面持续场（火墙 / 暴风雪）──────────────────────────────────
                case SkillBehavior.GroundField:
                    return SpawnField(skillId, def, spec, player, targetGrid, dmgMin, dmgMax);

                // ── 跟随施法者的火星带（火焰之径）───────────────────────────────
                case SkillBehavior.Trail:
                    return SpawnTrail(skillId, def, spec, player, dmgMin, dmgMax);

                // ── 直线贯穿（炼狱之火 / 闪电）──────────────────────────────────
                case SkillBehavior.Beam:
                    return FireBeam(skillId, def, spec, player, targetGrid, dmgMin, dmgMax);

                // ── 连锁（连锁闪电）─────────────────────────────────────────────
                case SkillBehavior.Chain:
                    return FireChain(skillId, def, spec, player, targetGrid, targetMonsterId, dmgMin, dmgMax);

                // ── 延迟落点（陨石）─────────────────────────────────────────────
                case SkillBehavior.DelayedStrike:
                {
                    var center = Center(targetGrid);
                    var e = SpawnEffect(skillId, def.name, SkillFxKind.Marker, center, def.dmgType,
                        spec.art, spec.impactArt, spec.radius, spec.duration, dmgMin, dmgMax);
                    e.resolveOnExpire = true;

                    // 原版陨石 = 落点标记（`meteorcenter`）+ 从天上掉下来的火球 `Meteor`
                    // + 尾迹 `MeteorTail`（missiles.txt：meteor 的 CltSubMissile1/2）⇒ 补一个纯表现坠落物。
                    // 它不结算伤害（到期由 Marker 的 `ResolveDelayedLanding` 统一炸），只是"看得见的掉落过程"。
                    var fall = SpawnEffect(skillId, def.name, SkillFxKind.Falling, center, def.dmgType,
                        "Meteor", "MeteorTail", 0, spec.duration, 0, 0);
                    fall.fallFrom = center + Vector2.up * SkillTuning.MeteorFallHeight;
                    fall.landAt = center;
                    // 出生点就在**高空**（不是落点）：否则第一帧火球会闪现在落点、下一 tick 才跳回天上
                    fall.pos = fall.fallFrom;

                    SkillLog.Info($"[Skill] {def.name}#{skillId} 落点标记已出：格 {targetGrid}（{spec.duration:0.0}s 后" +
                                  $"在半径 {spec.radius} 格内结算 {dmgMin}-{dmgMax} {def.dmgType}；" +
                                  $"火球自高空 {SkillTuning.MeteorFallHeight:0.#} 格处坠落）");
                    return true;
                }

                // ── 近身自动打击（雷云风暴）─────────────────────────────────────
                case SkillBehavior.Storm:
                {
                    var e = SpawnEffect(skillId, def.name, SkillFxKind.Field, Center(player.Grid), def.dmgType,
                        spec.art, spec.impactArt, spec.radius, spec.duration, dmgMin, dmgMax);
                    e.followCaster = true;
                    e.tick = spec.tick > 0f ? spec.tick : 1f;
                    e.tickLeft = e.tick;
                    // 起云：官方 Overlay.txt `thunderstormcast` → Filename `ThunderstormCast`（10 帧）
                    SpawnImpactFx(skillId, def.name, Center(player.Grid), def.dmgType, "ThunderstormCast");
                    AddBuff(skillId, level, spec, player, withAura: false);
                    SkillLog.Info($"[Skill] {def.name}#{skillId} 起云：{spec.duration:0.0}s 内每 {e.tick:0.0}s " +
                                  $"劈最近的敌人（半径 {spec.radius} 格，{dmgMin}-{dmgMax} {def.dmgType}）");
                    return true;
                }

                // ── 位移（传送）──────────────────────────────────────────────────
                case SkillBehavior.Teleport:
                    return DoTeleport(skillId, def, spec, player, targetGrid);

                // ── 自身增益（三种护甲 / 能量护盾 / 附魔）───────────────────────
                case SkillBehavior.Buff:
                    if (spec.duration <= 0f) return ResolveLegacyBuff(skillId, def, spec, player);
                    AddBuff(skillId, level, spec, player, withAura: true);
                    SkillLog.Info($"[Skill] {def.name}#{skillId} 生效：{BuffSummary(skillId, level)}；" +
                                  $"持续 {spec.duration:0.0}s");
                    return true;

                // ── 召唤（多头蛇）───────────────────────────────────────────────
                case SkillBehavior.Summon:
                    return SpawnSummon(skillId, def, spec, player, targetGrid, dmgMin, dmgMax);

                // ── 力场（静电力场）─────────────────────────────────────────────
                case SkillBehavior.ForceField:
                    return FireForceField(skillId, def, spec, player, dmgMin, dmgMax);

                // ── 念力（心灵传动）─────────────────────────────────────────────
                case SkillBehavior.Telekinesis:
                    return DoTelekinesis(skillId, def, spec, player, targetGrid, dmgMin, dmgMax);

                default:
                {
                    // 目录缺行 + 推断也没给出行为 ⇒ 绝不静默（判据里这是红项）
                    SkillLog.Error($"Execute：{def.name}#{skillId} 的行为未登记（`SkillCatalog` 缺行且推断失败）" +
                                   "⇒ 本次只退蓝，不做任何事（请补 `SkillCatalog`）");
                    RefundMana(player, ManaCostOf(skillId, level), def.name);
                    Notify("exec.unknown", $"「{def.name}」的行为未登记，施放已取消（法力已退还）");
                    return false;
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 各行为的落地
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>飞行体三兄弟：一次发 `count` 颗（多发按 15° 扇形铺开）。</summary>
        private bool FireProjectiles(int skillId, SkillDef def, Table.BaseSkillRow row, SkillSpec spec,
            IPlayerModule player, Vector2Int targetGrid, int dmgMin, int dmgMax, bool weaponOnly)
        {
            var missileCode = MissileCodeOf(row);
            if (string.IsNullOrEmpty(missileCode))
            {
                // 表内行不会走到（目录里选这三种行为的都带 missile）；表外行由 `Infer` 直接给了 InstantArea
                SkillLog.WarnThrottled("cast.nomissile." + skillId,
                    $"FireProjectiles：{def.name}#{skillId} 目录行为 = {spec.behavior} 但 `skill_c.missile` 为空 " +
                    "⇒ 退成「目标格立即结算」（配表/目录核对）");
                return ResolveInstant(skillId, def, row, new SkillSpec { behavior = SkillBehavior.InstantArea },
                    player, GetLevel(skillId), targetGrid, -1, dmgMin, dmgMax, weaponOnly);
            }

            var count = spec.behavior == SkillBehavior.MultiProjectile
                ? (spec.count > 0 ? spec.count : 1)
                : 1;
            var splash = spec.behavior == SkillBehavior.SplashProjectile ? spec.radius : 0;

            for (var i = 0; i < count; i++)
            {
                SpawnProjectile(skillId, def, row, player, targetGrid, dmgMin, dmgMax, missileCode,
                    i, count, splash);
            }

            if (count > 1)
            {
                SkillLog.Info($"[Skill] {def.name}#{skillId} 一次发出 {count} 颗（每颗 {dmgMin}-{dmgMax} " +
                              $"{def.dmgType}，扇形 {"±"}{SkillTuning.MultiProjectileSpreadDeg * (count - 1) / 2f:0.#}°）");
            }
            return true;
        }

        /// <summary>
        /// 本技能该用哪一颗投射物（`missile_c.code`）。
        /// <para>
        /// 顺序 = `skill_c.missile` → `missile_a` → `missile_b` → `missile_c`。
        /// </para>
        /// <para>
        /// **为什么必须回退**：`convert.py` 只把"单一主投射物"写进 `missile` 列（官方 `srvmissile`），
        /// 而多投射物技能（充能弹 / 火墙 / 暴风雪 / 陨石 / 连锁闪电 / 雷云风暴…）的官方值落在
        /// `srvmissilea/b/c` 三槽 ⇒ 只读 `missile` 会把「充能弹」判成**没有投射物**
        /// （实测：行为目录说要发 3 颗，却因 `row.Missile` 为空退成"目标格立即结算"⇒ 按了没反应）。
        /// 表头注释原文见 `Table/Base/BaseSkill.cs:52,60-62`。
        /// </para>
        /// </summary>
        private static string MissileCodeOf(Table.BaseSkillRow row)
        {
            if (row == null) return null;
            if (!string.IsNullOrEmpty(row.Missile)) return row.Missile;
            if (!string.IsNullOrEmpty(row.MissileA)) return row.MissileA;
            if (!string.IsNullOrEmpty(row.MissileB)) return row.MissileB;
            return row.MissileC;
        }

        /// <summary>在目标格**立即**结算：`radius = 0` ⇒ 单体；`radius &gt; 0` ⇒ 该格半径内全部。</summary>
        private bool ResolveInstant(int skillId, SkillDef def, Table.BaseSkillRow row, SkillSpec spec,
            IPlayerModule player, int level, Vector2Int targetGrid, int targetMonsterId,
            int dmgMin, int dmgMax, bool weaponOnly)
        {
            var hasDamage = dmgMax > 0 || weaponOnly;

            if (!hasDamage)
            {
                //   判据（三选一都不成立）= 官方该行①无伤害值（`dmgMax<=0`）、
                //   ②非武器伤害源（`SrcDam==0`）、③无投射物（`Missile` 为空）。
                //   表外技能里这一类就是"只扣蓝进冷却"的辅助/诅咒 ⇒ 保持旧口径（不假装有增益）。
                SkillLog.WarnOnce("cast.nobuff",
                    $"TryCast：{def.name}#{skillId} 没有伤害、非武器伤害源、无投射物（辅助/增益/召唤/诅咒类）" +
                    "⇒ 当前只消耗法力+进冷却，增益数值未建模（已登记未决：需要 `SkillCalc.txt` 的加成列）");
                return true;
            }

            //    （不新写公式；武器区间与加成系数复用普攻那一处 `CombatModule.GetWeaponDamage`）
            if (weaponOnly)
            {
                var targetW = targetMonsterId >= 0
                    ? targetMonsterId
                    : FindMonsterNear(targetGrid, SkillTuning.EnemySearchRadius);
                if (targetW < 0)
                {
                    SkillLog.WarnThrottled("cast.weapon.losttarget",
                        $"TryCast：{def.name}#{skillId} 武器伤害结算时目标已消失 ⇒ 只扣蓝进冷却");
                    return true;
                }

                var rawW = RollWeaponSkillDamage(row, def, level, player, out var detail);
                SkillLog.Info($"[Skill] 武器伤害结算：{def.name}#{skillId}（等级 {level}）{detail} ⇒ raw={rawW}");
                ResolveOnMonster(targetW, rawW, def.dmgType, skillId, def.name, "武器伤害");
                SpawnImpactFx(skillId, def.name, MonsterPos(targetW), def.dmgType, spec.impactArt);
                return true;
            }

            // 即时结算：单目标 / 小范围
            if (spec.radius > 0)
            {
                var n = ResolveAreaDamage(targetGrid, spec.radius, skillId, def.name, def.dmgType,
                    dmgMin, dmgMax, out var total);
                SpawnImpactFx(skillId, def.name, Center(targetGrid), def.dmgType, spec.impactArt);
                SkillLog.WarnThrottled("cast.ground.noaoe",
                    $"TryCast：{def.name}#{skillId} 是地面技能但 `skill_c.missile` 为空 ⇒ 按半径 " +
                    $"{spec.radius} 的即时范围结算（命中 {n} 个目标，合计 {total}）");
                return true;
            }

            var target = targetMonsterId >= 0 ? targetMonsterId : FindMonsterNear(targetGrid, SkillTuning.EnemySearchRadius);
            if (target < 0)
            {
                SkillLog.WarnThrottled("cast.losttarget",
                    $"TryCast：{def.name}#{skillId} 结算时目标已消失（召唤物/自身技能？）⇒ 只扣蓝进冷却");
                return true;
            }

            var raw = RollSkillDamage(dmgMin, dmgMax, def.dmgType);
            ResolveOnMonster(target, raw, def.dmgType, skillId, def.name, "即时");
            SpawnImpactFx(skillId, def.name, MonsterPos(target), def.dmgType, spec.impactArt);
            return true;
        }

        /// <summary>地面持续场：火墙 = **垂直于施法方向的 5 格**；暴风雪 = 目标格周围 9 格。
        /// <para>两者的分派判据 = 目录里的 `radius`（暴风雪 `4`、火墙 `0`）—— 不另立一个字段。</para></summary>
        private bool SpawnField(int skillId, SkillDef def, SkillSpec spec, IPlayerModule player,
            Vector2Int targetGrid, int dmgMin, int dmgMax)
        {
            var pts = new List<Vector2>();
            if (spec.radius > 0)
            {
                // 暴风雪：以落点为中心、半径内随机 `count` 格（同一格不重复下两次）
                //   落点那一格**恒定在内**（原版也是"圈里全下"）—— 否则把雪下在脚下正中的怪
                //   反而一点不掉血（随机挑格时中心可能一次都没被挑到）。
                pts.Add(Center(targetGrid));
                var pool = new List<Vector2Int>();
                for (var dx = -spec.radius; dx <= spec.radius; dx++)
                {
                    for (var dy = -spec.radius; dy <= spec.radius; dy++)
                    {
                        var g = new Vector2Int(targetGrid.x + dx, targetGrid.y + dy);
                        if (g == targetGrid) continue;                  // 中心已加过
                        if (Iso.GridDistance(g, targetGrid) > spec.radius) continue;
                        pool.Add(g);
                    }
                }
                var want = spec.count > 0 ? spec.count - 1 : pool.Count;
                for (var i = 0; i < want && pool.Count > 0; i++)
                {
                    var k = SkillRng().Next(0, pool.Count);
                    pts.Add(Center(pool[k]));
                    pool.RemoveAt(k);
                }
            }
            else
            {
                // 火墙：与"施法者 → 落点"**垂直**的一条线（原版火墙就是横在敌人面前的一道墙）
                var from = player.Grid;
                var dx = targetGrid.x - from.x;
                var dy = targetGrid.y - from.y;
                if (dx == 0 && dy == 0) dx = 1;
                var len = dx * dx + dy * dy;
                var perpX = -dy;
                var perpY = dx;
                var n = spec.count > 0 ? spec.count : 5;
                var half = n / 2;
                for (var i = 0; i < n; i++)
                {
                    var t = i - half;
                    var g = new Vector2Int(targetGrid.x + perpX * t / 1, targetGrid.y + perpY * t / 1);
                    // `perp` 未归一化（长度 = |d|）⇒ 按 |d| 缩放回"每格一格"
                    var scale = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(len)));
                    g = new Vector2Int(targetGrid.x + perpX * t / scale, targetGrid.y + perpY * t / scale);
                    pts.Add(Center(g));
                }
            }

            var e = SpawnEffect(skillId, def.name, SkillFxKind.Field, Center(targetGrid), def.dmgType,
                spec.art, spec.impactArt, spec.radius, spec.duration, dmgMin, dmgMax);
            e.points.AddRange(pts);
            e.tick = spec.tick > 0f ? spec.tick : 0.4f;
            e.tickLeft = e.tick;

            // 暴风雪：标记 `markBlizzard`，让每 tick 在随机场格补落原版 `icestormfallvar01` 落冰
            // （见 `ResolveEffectTick`）。本体冰柱由各场格的 `Field` 渲染 `Blizzard` 真图；不再自创冰环。
            if (spec.art == SkillCatalog.BlizzardArtFallback)
            {
                e.markBlizzard = true;
            }

            SkillLog.Info($"[Skill] {def.name}#{skillId} 铺场：{pts.Count} 格、持续 {spec.duration:0.0}s、" +
                          $"每 {e.tick:0.0}s 结算一次（{dmgMin}-{dmgMax} {def.dmgType}/次），" +
                          $"首格 {(pts.Count > 0 ? pts[0].ToString() : "(无)")}");
            return true;
        }

        /// <summary>火焰之径：跟人走，每 `tick` 秒在脚下留一簇会自己烧的小火（子场各自独立计时）。</summary>
        private bool SpawnTrail(int skillId, SkillDef def, SkillSpec spec, IPlayerModule player,
            int dmgMin, int dmgMax)
        {
            var e = SpawnEffect(skillId, def.name, SkillFxKind.Field, Center(player.Grid), def.dmgType,
                spec.art, spec.impactArt, 0, spec.duration, dmgMin, dmgMax);
            e.followCaster = true;
            e.tick = spec.tick > 0f ? spec.tick : 0.35f;
            e.tickLeft = e.tick;
            e.markTrail = true;
            SkillLog.Info($"[Skill] {def.name}#{skillId} 起火：{spec.duration:0.0}s 内跟人走，每 {e.tick:0.0}s " +
                          $"在脚下留一簇 {SkillTuning.TrailPuddleSeconds:0.0}s 的火（{dmgMin}-{dmgMax} {def.dmgType}）");
            return true;
        }

        /// <summary>直线贯穿：从施法者朝目标方向 `spec.radius` 格，线上全部命中（点—线段距离 &lt; 半宽）。</summary>
        private bool FireBeam(int skillId, SkillDef def, SkillSpec spec, IPlayerModule player,
            Vector2Int targetGrid, int dmgMin, int dmgMax)
        {
            var from = Iso.WorldToGridContinuous(player.World);
            var to = Center(targetGrid);
            var dir = to - from;
            if (dir.sqrMagnitude < 1e-6f) dir = new Vector2(1f, 0f);
            dir.Normalize();
            var end = from + dir * Mathf.Max(1, spec.radius);

            var monster = AppContext.I != null ? AppContext.I.Monster : null;
            var hits = 0;
            var total = 0;
            if (monster != null)
            {
                var all = monster.All;
                for (var i = 0; i < all.Count; i++)
                {
                    var s = all[i];
                    if (s == null || !s.alive) continue;
                    var p = new Vector2(s.gridX + 0.5f, s.gridY + 0.5f);
                    if (DistanceToSegment(p, from, end) > SkillTuning.BeamHalfWidth) continue;
                    var raw = RollSkillDamage(dmgMin, dmgMax, def.dmgType);
                    ResolveOnMonster(s.id, raw, def.dmgType, skillId, def.name, "直线");
                    hits++;
                    total += raw;
                }
            }

            var e = SpawnEffect(skillId, def.name, SkillFxKind.Beam, from, def.dmgType,
                spec.art, spec.impactArt, Mathf.Max(2, Mathf.RoundToInt(spec.radius)),
                spec.duration <= 0f ? 0.35f : spec.duration, dmgMin, dmgMax);
            e.from = from;
            e.to = end;
            Shake(0.06f, 0.10f);
            SkillLog.Info($"[Skill] {def.name}#{skillId} 直线贯穿 {from} → {end:0.##}（长 {spec.radius} 格、半宽 " +
                          $"{SkillTuning.BeamHalfWidth:0.##}）：命中 {hits} 个目标，合计 {total} {def.dmgType}");
            return true;
        }

        /// <summary>连锁：起手目标 → 之后每次跳向"最近的、还没被打过的"一只，最多 `count` 跳。</summary>
        private bool FireChain(int skillId, SkillDef def, SkillSpec spec, IPlayerModule player,
            Vector2Int targetGrid, int targetMonsterId, int dmgMin, int dmgMax)
        {
            var monster = AppContext.I != null ? AppContext.I.Monster : null;
            var first = targetMonsterId >= 0
                ? targetMonsterId
                : FindMonsterNear(targetGrid, SkillTuning.EnemySearchRadius);

            var pts = new List<Vector2>();
            pts.Add(Iso.WorldToGridContinuous(player.World));

            if (monster == null || first < 0)
            {
                // 没目标也**要留画面**（否则"按了没反应"）：画一条朝目标方向的短折线
                var from0 = pts[0];
                var dir0 = Center(targetGrid) - from0;
                if (dir0.sqrMagnitude < 1e-6f) dir0 = new Vector2(1f, 0f);
                dir0.Normalize();
                var e0 = SpawnEffect(skillId, def.name, SkillFxKind.Chain, from0, def.dmgType,
                    spec.art, spec.impactArt, 0, spec.duration <= 0f ? 0.3f : spec.duration, dmgMin, dmgMax);
                e0.points.Add(from0);
                e0.points.Add(from0 + dir0 * 2f);
                SkillLog.WarnThrottled("chain.notarget",
                    $"FireChain：{def.name}#{skillId} 起手目标不存在（格 {targetGrid}）⇒ 只放电不结算");
                return true;
            }

            var hit = new List<int>();
            var cur = first;
            var jumps = Mathf.Max(1, spec.count);
            var total = 0;
            for (var i = 0; i < jumps; i++)
            {
                var st = monster.Get(cur);
                if (st == null || !st.alive) break;

                var raw = RollSkillDamage(dmgMin, dmgMax, def.dmgType);
                ResolveOnMonster(cur, raw, def.dmgType, skillId, def.name, i == 0 ? "连锁首击" : $"连锁第 {i} 跳");
                total += raw;
                hit.Add(cur);
                pts.Add(new Vector2(st.gridX + 0.5f, st.gridY + 0.5f));

                // 下一跳：最近的、未打过的（跳跃半径 = 目录里的 radius）
                var next = -1;
                var best = float.MaxValue;
                var all = monster.All;
                for (var k = 0; k < all.Count; k++)
                {
                    var s = all[k];
                    if (s == null || !s.alive) continue;
                    if (hit.Contains(s.id)) continue;
                    var d = Iso.GridDistanceEuclidean(new Vector2Int(s.gridX, s.gridY),
                        new Vector2Int(st.gridX, st.gridY));
                    if (d > spec.radius || d >= best) continue;
                    best = d;
                    next = s.id;
                }
                if (next < 0) break;
                cur = next;
            }

            var e = SpawnEffect(skillId, def.name, SkillFxKind.Chain, pts[0], def.dmgType,
                spec.art, spec.impactArt, 0, spec.duration <= 0f ? 0.3f : spec.duration, dmgMin, dmgMax);
            e.points.AddRange(pts);
            SkillLog.Info($"[Skill] {def.name}#{skillId} 连锁：打过 {hit.Count} 个目标（跳数上限 {jumps}、" +
                          $"跳跃半径 {spec.radius} 格），合计伤害 {total} {def.dmgType}");
            return true;
        }

        /// <summary>传送：落点必须是**可走格**；不可走时退到"朝目标方向最近的可走格"，仍不可走则**退蓝撤销**。</summary>
        private bool DoTeleport(int skillId, SkillDef def, SkillSpec spec, IPlayerModule player,
            Vector2Int targetGrid)
        {
            var map = AppContext.I != null ? AppContext.I.Map : null;
            var dest = targetGrid;
            if (map != null && map.IsGenerated)
            {
                if (!map.InBounds(dest) || !map.Walkable(dest))
                {
                    var found = false;
                    for (var r = 1; r <= SkillTuning.TeleportSnapRadius && !found; r++)
                    {
                        for (var dx = -r; dx <= r && !found; dx++)
                        {
                            for (var dy = -r; dy <= r && !found; dy++)
                            {
                                var g = new Vector2Int(targetGrid.x + dx, targetGrid.y + dy);
                                if (!map.InBounds(g) || !map.Walkable(g)) continue;
                                if (Iso.GridDistance(g, player.Grid) > SkillTuning.TeleportMaxTiles) continue;
                                dest = g;
                                found = true;
                                SkillLog.Info($"Teleport：目标格 {targetGrid} 不可走 ⇒ 自动落到最近可走格 {g}" +
                                              $"（半径 {r} 内）");
                            }
                        }
                    }
                    if (!found)
                    {
                        RefundMana(player, ManaCostOf(skillId, GetLevel(skillId)), def.name);
                        Notify("cast.tp.blocked", $"「{def.name}」落点不可达（{targetGrid}），法力已退还");
                        return false;
                    }
                }

                if (Iso.GridDistance(dest, player.Grid) > SkillTuning.TeleportMaxTiles)
                {
                    RefundMana(player, ManaCostOf(skillId, GetLevel(skillId)), def.name);
                    Notify("cast.tp.far", $"「{def.name}」超出最大距离 {SkillTuning.TeleportMaxTiles} 格，法力已退还");
                    return false;
                }
            }

            var fromPos = Iso.WorldToGridContinuous(player.World);
            player.TeleportTo(dest);
            SpawnImpactFx(skillId, def.name, fromPos, def.dmgType, spec.art);
            SpawnImpactFx(skillId, def.name, Center(dest), def.dmgType, spec.art);
            var e = SpawnEffect(skillId, def.name, SkillFxKind.Aura, Center(dest), def.dmgType,
                spec.art, spec.impactArt, 1, 0.35f, 0, 0);
            e.followCaster = true;
            SkillLog.Info($"[Skill] {def.name}#{skillId} 传送：{player.Grid} ← {dest}（两点各留一次闪光）");
            return true;
        }

        /// <summary>多头蛇：落点放一个会自己开火的召唤物（每 `tick` 秒朝最近的敌人吐一颗火弹）。</summary>
        private bool SpawnSummon(int skillId, SkillDef def, SkillSpec spec, IPlayerModule player,
            Vector2Int targetGrid, int dmgMin, int dmgMax)
        {
            var e = SpawnEffect(skillId, def.name, SkillFxKind.Summon, Center(targetGrid), def.dmgType,
                spec.art, spec.impactArt, spec.radius, spec.duration, dmgMin, dmgMax);
            e.tick = spec.tick > 0f ? spec.tick : 0.8f;
            e.tickLeft = e.tick;
            // `count` 在 `Summon` 里 = **蛇口份数**（纯表现，`SkillFxView.SyncSummon` 摆三根火柱：
            // 主口 + 两侧小一号）。开火仍只由本效果驱动一次 ⇒ 份数**不**放大伤害。
            e.count = SkillTuning.HydraHeads;
            // 官方 missiles.txt：`hydra` 导弹 → CelFile `Firebolt` ⇒ 召唤落地那一下 = 火弹闪光
            // （蛇身本体在原版是召唤怪物，本项目无召唤怪物精灵管线，用原地燃烧的高火柱顶替）。
            SpawnImpactFx(skillId, def.name, Center(targetGrid), def.dmgType, SkillCatalog.HydraSpawnFlashArt);
            SkillLog.Info($"[Skill] {def.name}#{skillId} 召唤：{e.count} 个（格 {targetGrid}），存活 {spec.duration:0.0}s，" +
                          $"每 {e.tick:0.0}s 朝 {spec.radius} 格内最近的敌人吐一次火（{dmgMin}-{dmgMax} {def.dmgType}）");
            return true;
        }

        /// <summary>
        /// 静电力场：把半径内所有**存活**怪物的当前生命按比例削掉（官方普通难度保留 25%）。
        /// <para>用 `IMonsterModule.ApplyDamage` 结算（伤害类型 = 闪电）⇒ 飘字/受击/死亡链与其他技能完全一致，
        /// 不自己去改 `MonsterState.hp`（那会绕过抗性与表现）。</para>
        /// </summary>
        private bool FireForceField(int skillId, SkillDef def, SkillSpec spec, IPlayerModule player,
            int dmgMin, int dmgMax)
        {
            var monster = AppContext.I != null ? AppContext.I.Monster : null;
            var center = player.Grid;
            var hit = 0;
            var total = 0;
            if (monster != null)
            {
                var all = monster.All;
                for (var i = 0; i < all.Count; i++)
                {
                    var s = all[i];
                    if (s == null || !s.alive) continue;
                    if (Iso.GridDistance(new Vector2Int(s.gridX, s.gridY), center) > spec.radius) continue;

                    var keep = Mathf.FloorToInt(s.hp * SkillTuning.StaticFieldKeepFraction);
                    var dmg = s.hp - Mathf.Max(1, keep);
                    if (dmg <= 0) continue;
                    ResolveOnMonster(s.id, dmg, DamageType.Lightning, skillId, def.name, "力场");
                    hit++;
                    total += dmg;
                }
            }

            SpawnEffect(skillId, def.name, SkillFxKind.Nova, Center(center), DamageType.Lightning,
                spec.art, spec.impactArt, spec.radius, 0.5f, 0, 0);
            Shake(0.08f, 0.14f);
            SkillLog.Info($"[Skill] {def.name}#{skillId} 力场：半径 {spec.radius} 格内 {hit} 个目标被削到 " +
                          $"{SkillTuning.StaticFieldKeepFraction * 100f:0}% 生命（合计 -{total}）");
            return true;
        }

        /// <summary>心灵传动：隔空拾取范围内的地面物品（顺带对目标怪打一下）。</summary>
        private bool DoTelekinesis(int skillId, SkillDef def, SkillSpec spec, IPlayerModule player,
            Vector2Int targetGrid, int dmgMin, int dmgMax)
        {
            var item = AppContext.I != null ? AppContext.I.Item : null;
            var picked = item != null && item.PickupNearest(targetGrid, spec.radius);

            var target = FindMonsterNear(targetGrid, SkillTuning.EnemySearchRadius);
            if (target >= 0)
            {
                var raw = RollSkillDamage(dmgMin, dmgMax, def.dmgType);
                ResolveOnMonster(target, raw, def.dmgType, skillId, def.name, "念力");
                SpawnImpactFx(skillId, def.name, MonsterPos(target), def.dmgType, spec.art);
            }
            else
            {
                SpawnImpactFx(skillId, def.name, Center(targetGrid), def.dmgType, spec.art);
            }

            if (!picked)
            {
                Notify("cast.tk.nothing", $"「{def.name}」范围内没有可拾取的物品（半径 {spec.radius} 格）");
            }
            SkillLog.Info($"[Skill] {def.name}#{skillId} 念力：物品拾取={picked}，目标怪={(target >= 0 ? "#" + target : "无")}" +
                          $"（格 {targetGrid}）");
            return true;
        }

        /// <summary>表外技能的"辅助/增益"档（旧口径：只扣蓝进冷却，不假装有增益）。</summary>
        private bool ResolveLegacyBuff(int skillId, SkillDef def, SkillSpec spec, IPlayerModule player)
        {
            SkillLog.WarnOnce("cast.nobuff",
                $"TryCast：{def.name}#{skillId} 没有伤害、非武器伤害源、无投射物（辅助/增益/召唤/诅咒类）" +
                "⇒ 当前只消耗法力+进冷却，增益数值未建模（已登记未决：需要 `SkillCalc.txt` 的加成列）");
            return true;
        }

        /// <inheritdoc />
        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            TickCooldowns(dt);
            TickProjectiles(dt);
            TickEffects(dt);
            TickBuffs(dt);
            TickPassives(dt);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 技能树构建（配表 → SkillDef）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>从 `skill_c` 取本职业全部技能，排序并算好面板行列。</summary>
        // ═════════════════════════════════════════════════════════════════════
        // 效果推进（TickEffects）/ 增益（TickBuffs）/ 被动（TickPassives）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>造一个效果实例并挂表现（表现建不出来 ⇒ 只有逻辑，与投射物同一口径）。</summary>
        private SkillEffect SpawnEffect(int skillId, string skillName, SkillFxKind kind, Vector2 pos,
            DamageType type, string art, string impactArt, int radius, float duration,
            int dmgMin, int dmgMax)
        {
            var e = new SkillEffect
            {
                id = _nextEffectId++,
                skillId = skillId,
                skillName = skillName,
                kind = kind,
                pos = pos,
                lifeLeft = duration > 0f ? duration : 0.3f,
                lifeTotal = duration > 0f ? duration : 0.3f,
                radius = radius,
                dmgMin = dmgMin,
                dmgMax = dmgMax,
                type = type,
                art = art,
                impactArt = string.IsNullOrEmpty(impactArt) ? SkillFxView.DefaultImpactArt(type) : impactArt,
            };
            SkillFxView.TryCreate(e);
            _effects.Add(e);
            return e;
        }

        /// <summary>短促的命中/爆炸闪光（伤害类效果"打到了"的最小可见信号）。</summary>
        private void SpawnImpactFx(int skillId, string skillName, Vector2 pos, DamageType type, string art)
        {
            SpawnEffect(skillId, skillName, SkillFxKind.Impact, pos, type,
                art, string.IsNullOrEmpty(art) ? SkillFxView.DefaultImpactArt(type) : art,
                0, SkillTuning.ImpactSeconds, 0, 0);
        }

        /// <summary>
        /// 推进全部效果：跟随 → 寿命 → **周期结算** → 到期结算（陨石落地）→ 表现同步 → 回收。
        /// <para>结算口径（每一类都留了日志与计数，`combatcheck` 逐技能断言 `resolvedTicks`）：</para>
        /// <list type="bullet">
        /// <item>地面场：每个 `tick` 秒，把**落在任一场格半径内**的怪物集合去重后各打一次
        /// （去重 = 同一只站在两格交界处不会被算两次）；每跳伤害 = `掷点 × tick`（≥1）——
        /// 因为配表给的是"每秒伤害"，跳一次只该拿到那一小段。</item>
        /// <item>雷云风暴：每 `tick` 秒劈**半径内最近**的一只（原版就是自动锁敌）。</item>
        /// <item>火焰之径：驱动器自己不结算，每 `tick` 秒在脚下**生出一簇独立小火**（各自计时/结算）。</item>
        /// <item>多头蛇：每 `tick` 秒朝半径内最近的敌人**吐一颗火弹**（走投射物链 ⇒ 命中判定/表现全套复用）。</item>
        /// <item>陨石：只在**到期那一下**结算一次（`resolveOnExpire`），命中即闪光 + 镜头震。</item>
        /// </list>
        /// </summary>
        private void TickEffects(float dt)
        {
            if (_effects.Count == 0) return;

            var monster = AppContext.I != null ? AppContext.I.Monster : null;
            var player = Player;
            var playerGrid = player != null ? Iso.WorldToGridContinuous(player.World) : Vector2.zero;

            for (var i = _effects.Count - 1; i >= 0; i--)
            {
                var e = _effects[i];

                // 跟随施法者（光环 / 火焰之径 / 雷云风暴）：位置每帧取玩家当前点
                if (e.followCaster && player != null) e.pos = playerGrid;

                var wasAlive = e.alive;
                e.StepLife(dt);

                // 坠落物（陨石火球）：每 tick 按重力曲线（t² 加速下落）从高空插值到落点。
                // 位置推进与渲染解耦 ⇒ 离线宿主（无画面）里逻辑照样自洽可断言。
                if (e.kind == SkillFxKind.Falling)
                {
                    var t = 1f - Mathf.Clamp01(e.lifeLeft / e.lifeTotal);
                    e.pos = Vector2.Lerp(e.fallFrom, e.landAt, t * t);
                }

                if (e.alive)
                {
                    if (e.tick > 0f)
                    {
                        var guard = 0;
                        while (e.alive && e.tickLeft <= 0f && guard++ < 64)
                        {
                            e.tickLeft += e.tick;
                            ResolveEffectTick(e, monster, player);
                        }
                    }
                }
                else if (wasAlive && e.resolveOnExpire && e.resolvedTicks == 0)
                {
                    ResolveDelayedLanding(e, monster);
                }

                if (!e.alive)
                {
                    SkillFxView.Destroy(e);
                    RetireEffectAura(e);
                    _effects.RemoveAt(i);
                    continue;
                }

                SkillFxView.Sync(e);
            }
        }

        /// <summary>一次周期结算（按效果类型分派；见 <see cref="TickEffects"/> 的口径表）。</summary>
        private void ResolveEffectTick(SkillEffect e, IMonsterModule monster, IPlayerModule player)
        {
            if (e.kind == SkillFxKind.Summon)
            {
                //   `resolvedTicks` = **周期结算发生了几次**（在找目标之前就记）：
                //   否则"没触发"与"触发了但半径内没敌人"读数一样（都是 0），自证判据就废了。
                e.resolvedTicks++;
                var t = FindNearestMonster(monster, e.pos, e.radius);
                if (t < 0) return;
                var st = monster.Get(t);
                if (st == null) return;
                SpawnSummonShot(e, st.id, new Vector2(st.gridX + 0.5f, st.gridY + 0.5f));
                return;
            }

            if (e.kind == SkillFxKind.Field && e.markTrail)
            {
                // 火焰之径：在脚下留一簇**独立**的小火（自己计时、自己结算）。
                // 官方 Blaze 的落地混合 `groundFireBig` / `groundFireMedium` / `groundFireSmall`
                // （missiles.txt：`blaze`/`firewall`→Big、`firemedium`→Medium、`firesmall`→Small）
                // ⇒ 每簇从三池随机取一种，读作"大小不一的火路"（旧值恒 Small，一路都是同样的小火）。
                var art = TrailGroundArts[SkillRng().Next(0, TrailGroundArts.Length)];
                var p = SpawnEffect(e.skillId, e.skillName, SkillFxKind.Field, e.pos, e.type,
                    art, e.impactArt, 0, SkillTuning.TrailPuddleSeconds, e.dmgMin, e.dmgMax);
                p.points.Add(e.pos);
                p.tick = SkillTuning.TrailPuddleTick;
                p.tickLeft = p.tick;
                return;
            }

            if (e.kind == SkillFxKind.Field && e.radius > 0 && e.points.Count == 0)
            {
                // 雷云风暴（自带半径、无场格、跟人走）：劈最近的一只
                e.resolvedTicks++;                          // 同 Summon：先记"这次周期结算发生了"
                var t = FindNearestMonster(monster, e.pos, e.radius);
                if (t < 0) return;
                var st = monster.Get(t);
                var raw = RollSkillDamage(e.dmgMin, e.dmgMax, e.type);
                ResolveOnMonster(t, raw, e.type, e.skillId, e.skillName, "雷云");
                e.totalDamage += raw;
                if (st != null)
                {
                    var to = new Vector2(st.gridX + 0.5f, st.gridY + 0.5f);
                    SpawnImpactFx(e.skillId, e.skillName, to, e.type, e.impactArt);
                    // 可见落雷：从雷云（施法者当前位置）朝目标落一道 `LightningStrike` 折线
                    // （原版帧图；Beam 多段沿 from→to 铺成一道闪电，而非只在地上的命中闪光）。
                    var bolt = SpawnEffect(e.skillId, e.skillName, SkillFxKind.Beam, e.pos, e.type,
                        "LightningStrike", "LightningStrike",
                        Mathf.Max(2, (int)Vector2.Distance(e.pos, to) + 1), 0.25f, 0, 0);
                    bolt.from = e.pos;
                    bolt.to = to;
                }
                Shake(0.05f, 0.08f);
                return;
            }

            if (e.kind == SkillFxKind.Field)
            {
                // 地面持续场：逐格收集"被覆盖的怪物"，去重后各打一次
                if (monster == null || e.points.Count == 0) return;
                var per = Mathf.Max(1, Mathf.RoundToInt(RollSkillDamage(e.dmgMin, e.dmgMax, e.type) * e.tick));
                var hit = new List<int>();
                var all = monster.All;
                for (var pi = 0; pi < e.points.Count; pi++)
                {
                    var p = e.points[pi];
                    for (var mi = 0; mi < all.Count; mi++)
                    {
                        var s = all[mi];
                        if (s == null || !s.alive) continue;
                        if (hit.Contains(s.id)) continue;
                        var c = new Vector2(s.gridX + 0.5f, s.gridY + 0.5f);
                        if ((c - p).sqrMagnitude > SkillTuning.FieldPointRadius * SkillTuning.FieldPointRadius) continue;
                        hit.Add(s.id);
                    }
                }
                for (var k = 0; k < hit.Count; k++)
                {
                    ResolveOnMonster(hit[k], per, e.type, e.skillId, e.skillName, "场持续");
                }
                e.totalDamage += per * hit.Count;
                e.resolvedTicks++;
                if (hit.Count > 0)
                {
                    SkillLog.Info($"[Skill] {e.skillName}#{e.skillId} 持续场第 {e.resolvedTicks} 次结算：" +
                                  $"{hit.Count} 个目标各 {per}（{e.type}）");
                }

                // 暴风雪：每 tick 在随机场格补落 1~2 团原版落冰（`icestormfallvar01~04` 随机，
                // 与原版逐帧随机取变体一致）。纯表现增强，不重复结算伤害。
                if (e.markBlizzard && e.points.Count > 0)
                {
                    // 原版暴风雪的落冰在 `icestormfallvar01~04` 间随机取一帧序列。
                    var fallArts = new[] { "icestormfallvar01", "icestormfallvar02",
                                           "icestormfallvar03", "icestormfallvar04" };
                    const int hail = 2;
                    for (var h = 0; h < hail; h++)
                    {
                        var pt = e.points[SkillRng().Next(0, e.points.Count)];
                        var art = fallArts[SkillRng().Next(0, fallArts.Length)];
                        SpawnImpactFx(e.skillId, e.skillName, pt, DamageType.Cold, art);
                    }
                }
                return;
            }
        }

        /// <summary>陨石落地（延迟结算的那一下）：范围伤害 + 冲击闪光 + 镜头震。</summary>
        private void ResolveDelayedLanding(SkillEffect e, IMonsterModule monster)
        {
            var grid = e.Grid;
            var hits = ResolveAreaDamage(grid, e.radius, e.skillId, e.skillName, e.type,
                e.dmgMin, e.dmgMax, out var total);
            SpawnImpactFx(e.skillId, e.skillName, e.pos, e.type, e.impactArt);
            Shake(0.22f, 0.35f);
            e.resolvedTicks++;
            e.totalDamage += total;
            SkillLog.Info($"[Skill] {e.skillName}#{e.skillId} 落地：格 {grid}、半径 {e.radius} 格 ⇒ " +
                          $"命中 {hits} 个目标，合计 {total} {e.type}");
        }

        /// <summary>多头蛇吐一颗火弹（走投射物链：命中判定 / 表现 / 伤害管线全套复用）。</summary>
        private void SpawnSummonShot(SkillEffect e, int monsterId, Vector2 target)
        {
            var def = FindDef(e.skillId);
            var row = _rows.TryGetValue(e.skillId, out var r) ? r : null;
            var missile = FindMissileByCode(SkillTuning.SummonMissile);
            var speed = missile != null && missile.Speed > 0
                ? missile.Speed * SkillTuning.SpeedToTilesPerSecond
                : SkillTuning.FallbackSpeed;
            var range = missile != null && missile.Range > 0
                ? missile.Range * SkillTuning.RangeToTiles
                : SkillTuning.FallbackRange;
            var radius = missile != null && missile.Radius > 0
                ? missile.Radius * SkillTuning.RadiusToTiles
                : 0.6f;

            var baseDir = target - e.pos;
            if (baseDir.sqrMagnitude < 1e-6f) baseDir = new Vector2(1f, 0f);
            baseDir.Normalize();

            // 原版多头蛇三个头各喷一发火弹 ⇒ 三发呈小扇形（中间对准目标，两侧 ±spread 偏出，可能打空，与原版一致）。
            const float spread = 0.21f; // ≈ 12°
            var ang = Mathf.Atan2(baseDir.y, baseDir.x);
            for (var k = -1; k <= 1; k++)
            {
                var a = ang + k * spread;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var p = new Projectile
                {
                    id = _nextProjectileId++,
                    skillId = e.skillId,
                    skillName = e.skillName + "·召唤物",
                    ownerId = GameConst.PlayerEntityId,
                    pos = e.pos + dir * SkillTuning.SpawnOffset,
                    dir = dir,
                    speed = speed,
                    rangeLeft = range,
                    hitRadius = radius + SkillTuning.MonsterBodyRadius,
                    dmgMin = e.dmgMin,
                    dmgMax = e.dmgMax,
                    type = e.type,
                    celFile = missile != null ? missile.CelFile : string.Empty,
                };
                p.Trail.Add(p.pos);
                ProjectileView.TryCreate(p);
                _projectiles.Add(p);
            }
            SkillLog.Info($"[Skill] 多头蛇#{e.id} 三头齐喷（3 发扇形）→ 目标 m#{monsterId}" +
                          $"（{e.dmgMin}-{e.dmgMax} {e.type}；{(missile != null ? "配表 " + SkillTuning.SummonMissile : "兜底速度/射程")}）");
            _ = row;   // 召唤物的投射物**不带**目录行为（它就是一颗普通火弹），row 只用于将来扩展
            _ = def;
        }

        /// <summary>
        /// 推进限时增益：到期的摘掉（连光环表现一起）、未到期的把**附魔**的近战加成推给
        /// `SkillBuffs`（`Module/Combat/CombatModule` 只读它，见那个文件头"为什么不改契约"）。
        /// </summary>
        private void TickBuffs(float dt)
        {
            if (_buffs.Count == 0)
            {
                SkillBuffs.SetMeleeFire(0, 0);
                SkillBuffs.SetArmorDefensePercent(0);    // 同上：护甲防御加成也要归零
                return;
            }

            _buffKeys.Clear();
            foreach (var kv in _buffs) _buffKeys.Add(kv.Key);

            for (var i = 0; i < _buffKeys.Count; i++)
            {
                var id = _buffKeys[i];
                var b = _buffs[id];
                b.remain -= dt;
                if (b.remain <= 0f)
                {
                    _buffs.Remove(id);
                    if (b.auraEffectId > 0) KillEffect(b.auraEffectId);
                    SkillLog.Info($"增益到期：{NameOf(id)}#{id}（等级 {b.level}，持续 {b.total:0.0}s）");
                    continue;
                }
                _buffs[id] = b;
            }

            // 附魔：近战附加火焰伤害（数值取 `skill_c` 的 1 级值 × 等级缩放，走既有 `SkillDamageRange`）
            if (_buffs.TryGetValue(EnchantId, out var en) && en.remain > 0f
                && _rows.TryGetValue(EnchantId, out var enRow))
            {
                Combat.DamageFormula.SkillDamageRange(enRow, en.level, out var mn, out var mx);
                SkillBuffs.SetMeleeFire(Mathf.Max(1, mn), Mathf.Max(1, mx));
            }
            else
            {
                SkillBuffs.SetMeleeFire(0, 0);
            }

            // 护甲：防御加成（冻结 / 寒颤 / 冰冷）—— 每帧推一次 ⇒ 到期当帧即归零
            //   （口径见 `RefreshArmorDefense`；与附魔同一"写方恒为本模块"的纪律）。
            RefreshArmorDefense();
        }

        /// <summary>
        /// 被动技能（温暖 / 三种精通）的持续效果。
        /// <list type="bullet">
        /// <item><b>温暖</b>：唯一的法力回复来源（本项目 `PlayerModule` 只靠药水 ⇒ 见 `SkillTuning.Warmth…`
        /// 的"为什么是每秒若干点"）。每满 1 秒回一次，避免每帧碎回。</item>
        /// <item><b>三种精通</b>：不加在这里，直接参与伤害掷点（`RollSkillDamage` 的元素倍率）——
        /// 那是"精通"唯一该影响的地方，放这里会绕开抗性口径。</item>
        /// </list>
        /// </summary>
        private void TickPassives(float dt)
        {
            var player = Player;
            if (player == null) return;

            var warmth = GetLevel(WarmthId);
            if (warmth <= 0)
            {
                _warmthAccum = 0f;
                return;
            }
            _warmthAccum += dt;
            if (_warmthAccum < 1f) return;

            var seconds = Mathf.FloorToInt(_warmthAccum);
            _warmthAccum -= seconds;
            var per = WarmthManaPerSecond(warmth);
            var amount = per * seconds;
            if (amount <= 0) return;
            player.RestoreMana(amount);
            SkillLog.Info($"[Skill] 温暖#{WarmthId}（等级 {warmth}）法力回复 +{amount}" +
                          $"（{per}/秒，当前 {player.Mana}/{player.MaxMana}）");
        }

        /// <summary>温暖每秒回复量 = `maxMana × pct / 1000`（≥1）；`pct = 30 + 12×(等级-1)`。见 `SkillTuning`。</summary>
        private static int WarmthManaPerSecond(int level)
        {
            var pct = SkillTuning.WarmthBasePercent + SkillTuning.WarmthPercentPerLevel * (level - 1);
            var player = Player;
            var maxMana = player != null ? player.MaxMana : 0;
            var per = maxMana * pct / 1000;
            return per < 1 ? 1 : per;
        }

        /// <summary>挨打之后的两件事（订阅 `Events.PlayerDamaged`；只做**事后补偿**，不碰伤害管线）。</summary>
        private void OnPlayerDamaged(DamageArgs a)
        {
            if (a == null || !a.targetIsPlayer || a.amount <= 0) return;
            var player = Player;
            if (player == null || player.IsDead) return;

            // ① 能量护盾：按比例把这次伤害的一部分改用**法力**承担（先扣蓝、再把等量生命补回）
            if (_buffs.TryGetValue(EnergyShieldId, out var es) && es.remain > 0f)
            {
                var want = a.amount * EnergyShieldPercent(es.level) / 100;
                var afford = player.Mana < want ? player.Mana : want;
                if (afford > 0 && player.TrySpendMana(afford))
                {
                    player.Heal(afford);
                    SkillLog.Info($"[Skill] 能量护盾#{EnergyShieldId} 抵伤 {afford}" +
                                  $"（本次受伤 {a.amount}，法力剩 {player.Mana}）");
                }
            }

            // ② 三种护甲：谁打我，谁挨一记冰伤（原版护甲系列的核心手感）
            //   互斥口径：同时挂了多个时只按**等级最高**的那个反伤（平级取登记序靠前的），**只反一次**。
            //   ⚠️ 先选中、再判伤害：不能"第一个没伤害就继续找下一个"——那会让「冻结护甲」（官方就是
            //   纯防御、无伤害列）把反伤机会让给别的护甲，两个都没挂时更会静默什么都不发生。
            if (a.attackerId < 0) return;
            var armorId = -1;
            var armorLevel = 0;
            var ids = ArmorIds;
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                if (!_buffs.TryGetValue(id, out var b) || b.remain <= 0f) continue;
                if (b.level <= armorLevel) continue;
                armorLevel = b.level;
                armorId = id;
            }
            if (armorId <= 0) return;
            if (!_rows.TryGetValue(armorId, out var armorRow)) return;

            Combat.DamageFormula.SkillDamageRange(armorRow, armorLevel, out var amn, out var amx);
            var armorRaw = RollSkillDamage(amn, amx, DamageType.Cold);
            if (armorRaw <= 0) return;      // 该护甲官方无伤害列（冻结护甲）⇒ 只加防御、不反伤
            ResolveOnMonster(a.attackerId, armorRaw, DamageType.Cold, armorId, NameOf(armorId), "护甲反伤");
            var st = AppContext.I != null && AppContext.I.Monster != null ? AppContext.I.Monster.Get(a.attackerId) : null;
            if (st != null)
            {
                SpawnImpactFx(armorId, NameOf(armorId), new Vector2(st.gridX + 0.5f, st.gridY + 0.5f), DamageType.Cold, null);
            }
        }

        /// <summary>挂一条限时增益（同时按需挂一个脚下光环表现）。</summary>
        private void AddBuff(int skillId, int level, SkillSpec spec, IPlayerModule player, bool withAura)
        {
            var auraId = -1;
            if (withAura && player != null)
            {
                var aura = SpawnEffect(skillId, NameOf(skillId), SkillFxKind.Aura,
                    Iso.WorldToGridContinuous(player.World), DamageType.Physical,
                    spec.art, spec.impactArt, 1, spec.duration, 0, 0);
                aura.followCaster = true;
                auraId = aura.id;
                // 施法脉冲：buff 落地的"生效"闪光。素材 = 该系**官方 cast overlay**
                // （`impactArt`：火=FireCast_for_Sorceress / 冰=IceCastNew01 / 电=LightningCast；
                // 见 Overlay.txt 的 `*_cast_*` 行）；没配则回落本体图。用 `Impact` 是因为
                // `SkillFxView.ArtOf` 只有 Impact/Beam/Chain/Falling 取 impactArt（Nova 只取 art）。
                var castArt = string.IsNullOrEmpty(spec.impactArt) ? spec.art : spec.impactArt;
                SpawnEffect(skillId, NameOf(skillId), SkillFxKind.Impact,
                    Iso.WorldToGridContinuous(player.World), DamageType.Physical,
                    spec.art, castArt, 2, 0.45f, 0, 0);
            }

            _buffs[skillId] = new ActiveBuff
            {
                skillId = skillId,
                level = level,
                remain = spec.duration,
                total = spec.duration,
                auraEffectId = auraId,
            };
            if (IsArmorId(skillId)) RefreshArmorDefense();   // 护甲生效当帧即把防御加成推给 `SkillBuffs`
            Notify("buff." + skillId, $"「{NameOf(skillId)}」生效 {spec.duration:0}s");
        }

        /// <summary>能量护盾在给定等级下的吸收比例（%）。出处见 `SkillTuning.EnergyShieldBasePercent`。</summary>
        internal static int EnergyShieldPercent(int level)
        {
            if (level <= 0) return 0;
            return SkillTuning.EnergyShieldBasePercent + SkillTuning.EnergyShieldPercentPerLevel * (level - 1);
        }

        /// <summary>一条增益的中文摘要（日志/自证用；数值全部来自配表 + `SkillTuning`）。</summary>
        private string BuffSummary(int skillId, int level)
        {
            if (skillId == EnergyShieldId)
                return $"受伤时 {EnergyShieldPercent(level)}% 由法力承担";
            if (skillId == EnchantId && _rows.TryGetValue(skillId, out var row))
            {
                Combat.DamageFormula.SkillDamageRange(row, level, out var mn, out var mx);
                return $"近战附加火焰伤害 {mn}-{mx}";
            }
            if (IsArmorId(skillId) && _rows.TryGetValue(skillId, out var ar))
            {
                Combat.DamageFormula.SkillDamageRange(ar, level, out var mn, out var mx);
                var defTxt = $"防御 +{ArmorDefensePercentOf(skillId, level)}%";
                // 「冻结护甲」官方就是纯防御（本表 0-0）⇒ 摘要必须说实话，不能写"反伤 0-0"
                return mx > 0 ? $"挨打时反伤 {mn}-{mx} 冰冷伤害；{defTxt}" : $"{defTxt}（本系无伤害列 ⇒ 不反伤）";
            }
            if (skillId == StormId)
                return $"每 {SkillTuning.StormTickSeconds:0.0}s 自动劈最近的敌人";
            return "（无额外数值）";
        }

        // ═════════════════════════════════════════════════════════════════════
        // 小工具（效果 / 增益 / 提示 / 几何）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>格中心（连续格坐标）。</summary>
        internal static Vector2 Center(Vector2Int g) => new Vector2(g.x + 0.5f, g.y + 0.5f);

        /// <summary>怪物所在的世界（格）点；不存在 ⇒ 原点。</summary>
        private static Vector2 MonsterPos(int monsterId)
        {
            var st = AppContext.I != null && AppContext.I.Monster != null ? AppContext.I.Monster.Get(monsterId) : null;
            return st != null ? new Vector2(st.gridX + 0.5f, st.gridY + 0.5f) : Vector2.zero;
        }

        /// <summary>点 <paramref name="p"/> 到线段 `a→b` 的距离（直线技能"扫到没扫到"的判据）。</summary>
        internal static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector2.Distance(p, a);
            var t = Vector2.Dot(p - a, ab) / len2;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>半径内**最近**的存活怪物（-1 = 没有）。雷云 / 召唤物锁敌用。</summary>
        private static int FindNearestMonster(IMonsterModule monster, Vector2 center, int radius)
        {
            if (monster == null) return -1;
            var all = monster.All;
            var best = -1;
            var bestD = float.MaxValue;
            for (var i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.alive) continue;
                var d = Vector2.Distance(center, new Vector2(s.gridX + 0.5f, s.gridY + 0.5f));
                if (d > radius || d >= bestD) continue;
                bestD = d;
                best = s.id;
            }
            return best;
        }

        /// <summary>按 id 干掉一个效果（增益到期时摘光环用）。</summary>
        private void KillEffect(int effectId)
        {
            for (var i = 0; i < _effects.Count; i++)
            {
                if (_effects[i].id != effectId) continue;
                _effects[i].alive = false;
                SkillFxView.Destroy(_effects[i]);
                _effects.RemoveAt(i);
                return;
            }
        }

        /// <summary>效果被回收时，若它正是某条增益的光环 ⇒ 断开引用（避免下次到期再按 id 找）。</summary>
        private void RetireEffectAura(SkillEffect e)
        {
            if (_buffs.Count == 0) return;
            _buffKeys.Clear();
            foreach (var kv in _buffs) _buffKeys.Add(kv.Key);
            for (var i = 0; i < _buffKeys.Count; i++)
            {
                var id = _buffKeys[i];
                var b = _buffs[id];
                if (b.auraEffectId != e.id) continue;
                b.auraEffectId = -1;
                _buffs[id] = b;
            }
        }

        /// <summary>
        /// 拒绝执行时**退还**已扣的法力 —— 铁律：绝不允许"蓝扣了、什么都没发生"
        /// （实测的放技能没效果"里有一半就是这条）。
        /// </summary>
        private void RefundMana(IPlayerModule player, int cost, string skillName)
        {
            if (player == null || cost <= 0) return;
            player.RestoreMana(cost);
            SkillLog.Warn($"RefundMana：{skillName} 未执行 ⇒ 退还法力 {cost}（当前 {player.Mana}）");
        }

        /// <summary>
        /// 屏幕提示（节流）：`Game.UI.Toast`。**为什么模块层能调**：`Module/Flow/AppFlow.cs` 已有先例
        /// （"请先选择角色"），且 `Game.UI` 在离线宿主里为 null ⇒ 这里必须判空（不判空则离线直接崩）。
        /// </summary>
        private static void Notify(string key, string msg)
        {
            // 同一句话 1.5 秒内只弹一次（按住左键连发时"法力不足"会每帧命中）
            if (!CloverEngine.LogThrottle.ShouldLog("Skill/toast/" + key, 1.5f)) return;
            SkillLog.Info($"[提示] {msg}");
            try
            {
                if (Game.UI != null) Game.UI.Toast(msg);
            }
            catch (Exception ex)
            {
                SkillLog.WarnOnce("notify.fail", $"Notify: Game.UI.Toast 抛异常（已吞掉）：{ex.Message}");
            }
        }

        /// <summary>镜头震（大威力技能的手感）；相机未接入 ⇒ 静默跳过（离线宿主属正常）。</summary>
        private static void Shake(float amplitude, float duration)
        {
            var rig = AppContext.I != null ? AppContext.I.Camera : null;
            if (rig == null) return;
            try { rig.Shake(amplitude, duration); }
            catch (Exception ex) { SkillLog.WarnOnce("shake.fail", $"Shake 抛异常（已吞掉）：{ex.Message}"); }
        }

        /// <summary>半径内范围结算（去重、逐个走 `DamagePipeline`）。返回命中数，`total` 为本次掷点合计。</summary>
        private int ResolveAreaDamage(Vector2Int center, int radius, int skillId, string skillName,
            DamageType type, int dmgMin, int dmgMax, out int total)
        {
            total = 0;
            var monster = AppContext.I != null ? AppContext.I.Monster : null;
            if (monster == null) return 0;

            var all = monster.All;
            var hit = 0;
            for (var i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.alive) continue;
                if (Iso.GridDistance(new Vector2Int(s.gridX, s.gridY), center) > radius) continue;

                var raw = RollSkillDamage(dmgMin, dmgMax, type);
                ResolveOnMonster(s.id, raw, type, skillId, skillName, "范围");
                total += raw;
                hit++;
            }
            return hit;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 被动/增益的 id 与倍率（一律**按官方 code 查**，不写死数字）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>`skill_c.code` → 技能 id（`BuildAvailable` 时建一次；查不到 ⇒ -1）。</summary>
        private readonly Dictionary<string, int> _codeToId = new Dictionary<string, int>();

        /// <summary>按官方 code 取技能 id（-1 = 本职业没有这个技能）。</summary>
        internal int IdOfCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return -1;
            return _codeToId.TryGetValue(code, out var id) ? id : -1;
        }

        /// <summary>温暖（Warmth）id。</summary>
        private int WarmthId { get { return IdOfCode(SkillCatalog.CodeWarmth); } }

        /// <summary>能量护盾 id。</summary>
        private int EnergyShieldId { get { return IdOfCode(SkillCatalog.CodeEnergyShield); } }

        /// <summary>附魔 id。</summary>
        private int EnchantId { get { return IdOfCode(SkillCatalog.CodeEnchant); } }

        /// <summary>雷云风暴 id。</summary>
        private int StormId { get { return IdOfCode(SkillCatalog.CodeThunderStorm); } }

        /// <summary>三种护甲的 id 列表（冻结 / 寒颤 / 冰冷）。</summary>
        private int[] ArmorIds
        {
            get
            {
                if (_armorIds == null)
                {
                    _armorIds = new int[SkillCatalog.CodeArmors.Length];
                    for (var i = 0; i < _armorIds.Length; i++) _armorIds[i] = IdOfCode(SkillCatalog.CodeArmors[i]);
                }
                return _armorIds;
            }
        }

        private int[] _armorIds;

        private bool IsArmorId(int skillId)
        {
            var ids = ArmorIds;
            for (var i = 0; i < ids.Length; i++)
            {
                if (ids[i] == skillId) return true;
            }
            return false;
        }

        /// <summary>
        /// 某件护甲在给定等级下的**防御加成**（%）。
        /// <para>数值出处 = `SkillTuning` 的「三种护甲的防御加成」段（官方 `skills.txt` 的
        /// `aurastat1 = skill_armor_percent`）：冻结 30+5/级、寒颤 45+6/级、冰冷 45+5/级；
        /// 非护甲 id ⇒ 0。</para>
        /// <para>id 对应关系 = `SkillCatalog.CodeArmors` 的声明顺序
        /// （`ArmorIds` 就是按它逐个查出来的：0=冻结 / 1=寒颤 / 2=冰冷）。</para>
        /// </summary>
        private int ArmorDefensePercentOf(int skillId, int level)
        {
            if (level <= 0) return 0;
            var ids = ArmorIds;
            var k = level - 1;
            if (ids.Length > 0 && skillId == ids[0])
                return SkillTuning.FrozenArmorDefenseBasePercent + SkillTuning.FrozenArmorDefensePerLevel * k;
            if (ids.Length > 1 && skillId == ids[1])
                return SkillTuning.ShiverArmorDefenseBasePercent + SkillTuning.ShiverArmorDefensePerLevel * k;
            if (ids.Length > 2 && skillId == ids[2])
                return SkillTuning.ChillingArmorDefenseBasePercent + SkillTuning.ChillingArmorDefensePerLevel * k;
            return 0;
        }

        /// <summary>
        /// 把"当前生效护甲里**等级最高**那件"的防御加成%推给 `SkillBuffs`
        /// （读方 = `Module/Player/PlayerModule.Defense`，命中判定的 `player.Defense` 走它）。
        /// <para>互斥口径与护甲反伤**逐字同形**（见 `OnPlayerDamaged` 的 ②）：多件同时挂只算一份、
        /// 取**等级最高**者（平级取 `ArmorIds` 遍历序靠前的）；零护甲生效 ⇒ 推 0
        /// （绝不"漏一条挂到下一局"）。</para>
        /// <para>调用点 = `AddBuff`（护甲生效当帧）、`TickBuffs`（每帧，覆盖到期）与
        /// 清档（`ClearProjectiles` 尾的 `SkillBuffs` 归零段）。</para>
        /// </summary>
        private void RefreshArmorDefense()
        {
            var best = 0;
            var armorLevel = 0;
            var ids = ArmorIds;
            for (var i = 0; i < ids.Length; i++)
            {
                if (!_buffs.TryGetValue(ids[i], out var b) || b.remain <= 0f) continue;
                if (b.level <= armorLevel) continue;      // 与反伤同一行：只认"等级更高"的
                armorLevel = b.level;
                best = ArmorDefensePercentOf(ids[i], b.level);
            }
            SkillBuffs.SetArmorDefensePercent(best);
        }

        /// <summary>元素精通对**该元素技能伤害**的加成百分比（未学 / 无该系 ⇒ 0）。</summary>
        /// <para>分系口径（官方 `skills.txt` Id=61/63/65 三行 Param 各不相同，旧"三系共用 30/12"已废）：
        /// 火 `30 + 7×(lv-1)`、闪电 `50 + 12×(lv-1)`；**冰冷不进本方法**（它的机制是降抗性，
        /// 见 <see cref="ColdMasteryResistReduction"/>）⇒ Cold 恒返回 0。</para>
        internal int MasteryPercent(DamageType type)
        {
            switch (type)
            {
                case DamageType.Fire:
                {
                    var id = IdOfCode(SkillCatalog.CodeFireMastery);
                    if (id <= 0) return 0;
                    var lv = GetLevel(id);
                    if (lv <= 0) return 0;
                    return SkillTuning.FireMasteryBasePercent
                         + SkillTuning.FireMasteryPercentPerLevel * (lv - 1);
                }
                case DamageType.Lightning:
                {
                    var id = IdOfCode(SkillCatalog.CodeLightningMastery);
                    if (id <= 0) return 0;
                    var lv = GetLevel(id);
                    if (lv <= 0) return 0;
                    return SkillTuning.LightningMasteryBasePercent
                         + SkillTuning.LightningMasteryPercentPerLevel * (lv - 1);
                }
                default: return 0;   // Cold 走抗性通道；Physical/Magic 无精通
            }
        }

        /// <summary>冰冷精通**降低怪物冰冷抗性**的百分点（未学 / 无该系 ⇒ 0）。
        /// <para>官方机制（`skills.txt` Id=65 `Cold Mastery`，社区 1.10 文档一致：
        /// reduces the cold resistances of monsters，下限 −100%）——与另两系"加技能伤害"不同，
        /// 这里不进任何乘区，由 <see cref="Combat.DamagePipeline"/> 在结算怪物抗性前
        /// 传给 <see cref="IMonsterModule.ApplyDamage"/> 的 <c>resistReduction</c> 参数。</para>
        /// </summary>
        public int ColdMasteryResistReduction()
        {
            var id = IdOfCode(SkillCatalog.CodeColdMastery);
            if (id <= 0) return 0;
            var lv = GetLevel(id);
            if (lv <= 0) return 0;
            return SkillTuning.ColdMasteryResistBase
                 + SkillTuning.ColdMasteryResistPerLevel * (lv - 1);
        }

        /// <summary>增益是否在生效（HUD / 自证用）。</summary>
        internal bool IsBuffActive(int skillId)
        {
            return _buffs.TryGetValue(skillId, out var b) && b.remain > 0f;
        }

        /// <summary>增益剩余秒数（0 = 没有）。</summary>
        internal float BuffRemain(int skillId)
        {
            return _buffs.TryGetValue(skillId, out var b) && b.remain > 0f ? b.remain : 0f;
        }

        /// <summary>活跃效果数（自证用："施放后有没有东西留在场景里"）。</summary>
        internal int ActiveEffectCount { get { return _effects.Count; } }

        /// <summary>活跃效果列表（只读；自证用）。</summary>
        internal IReadOnlyList<SkillEffect> Effects { get { return _effects; } }

        /// <summary>`BuildAvailable` 之后再调一次把 code→id 建全（表外职业的被动靠它）。</summary>
        private void RebuildCodeIndex()
        {
            _codeToId.Clear();
            foreach (var kv in _rows)
            {
                var code = kv.Value != null ? kv.Value.Code : null;
                if (!string.IsNullOrEmpty(code)) _codeToId[code] = kv.Key;
            }
        }

        private void BuildAvailable()
        {
            _available.Clear();
            _rows.Clear();

            var all = Table.Tables.Default.Skill.All();
            if (all == null || all.Count == 0)
            {
                SkillLog.Error("BuildAvailable: skill_c 未加载（表为空）⇒ 技能树为空；先确认 Bootstrap 的配表加载是否成功");
                return;
            }

            var rows = new List<Table.BaseSkillRow>();
            for (var i = 0; i < all.Count; i++)
            {
                var row = all[i];
                if (row == null) continue;
                if (row.Class != (int)_cls) continue;

                if (row.Tree < 1 || row.Tree > 3)
                {
                    SkillLog.Warn($"BuildAvailable: 技能 {row.Name}#{row.Id} 的 tree={row.Tree} 越界（官方 SkillPage 应为 1..3）" +
                                  "⇒ 归入第 1 系");
                }
                rows.Add(row);
            }

            if (rows.Count == 0)
            {
                SkillLog.Error($"BuildAvailable: skill_c 里没有职业 {(int)_cls}({_cls}) 的技能（配表核对）⇒ 技能树为空");
                return;
            }

            // 排序：系 → 需求等级 → id（面板从上到下、从左到右就是这个顺序）
            rows.Sort((a, b) =>
            {
                var t = a.Tree.CompareTo(b.Tree);
                if (t != 0) return t;
                var l = a.ReqLevel.CompareTo(b.ReqLevel);
                if (l != 0) return l;
                return a.Id.CompareTo(b.Id);
            });

            // 面板行列：行 = 等级档位（1/6/12/18/24/30），列 = 同系同档内的第几个
            var colCounters = new Dictionary<string, int>();
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var def = ToDef(row);

                def.slotRow = TierIndex(row.ReqLevel);
                var key = (row.Tree < 1 ? 1 : row.Tree) + ":" + def.slotRow;
                colCounters.TryGetValue(key, out var col);
                def.slotCol = col;
                colCounters[key] = col + 1;

                _available.Add(def);
                _rows[def.id] = row;
            }

            SkillLog.Info($"BuildAvailable：职业 {_cls} 载入 {_available.Count} 个技能" +
                          $"（系 1/2/3 = {CountTree(0)}/{CountTree(1)}/{CountTree(2)}；" +
                          $"面板档次 {SkillTuning.TreeLevelTiers.Length} 档）");

            RebuildCodeIndex();     // 被动/增益按官方 code 找 id（温暖/精通/护甲/能量护盾/附魔/雷云）
        }

        /// <summary>`skill_c` 行 → 对外的 `Def.SkillDef`。</summary>
        private static SkillDef ToDef(Table.BaseSkillRow row)
        {
            var tree = row.Tree < 1 || row.Tree > 3 ? 0 : row.Tree - 1;      // DTO 是 0 基

            return new SkillDef
            {
                id = row.Id,
                cls = (PlayerClass)row.Class,
                tree = tree,
                name = row.Name,
                desc = row.Desc,
                reqLevel = row.ReqLevel,
                reqSkill = row.ReqSkill,
                reqSkillLevel = ReqSkillLevel,          // 官方前置技能需求等级 = 1（`skill_c` 无该列）
                maxLevel = row.MaxLevel > 0 ? row.MaxLevel : DefaultMaxLevel,
                manaCost = row.ManaCost,
                delayMs = 0,                            // `skill_c` 无 delay 列 ⇒ 实际冷却走 SkillTuning 默认值
                dmgMin = row.DmgMin,
                dmgMax = row.DmgMax,
                dmgType = Combat.DamageFormula.DamageTypeOf(row.DmgType),
                target = TargetOf(row),
            };
        }

        /// <summary>官方前置技能需求等级（`skill_c` 无该列，官方规则是前置技能至少 1 级）。</summary>
        private const int ReqSkillLevel = 1;

        /// <summary>技能等级上限的兜底值（配表 `max_level` 为空时）。</summary>
        private const int DefaultMaxLevel = 20;

        /// <summary>
        /// 目标类型推断（`skill_c` 没有 target 列 ⇒ **本项目新增**的映射，规则写在这里唯一一处）：
        /// 被动 → None；有投射物 → Ground（指向地面飞出）；**武器伤害类（官方 SrcDam≠0）或有伤害 → Enemy**；
        /// 其余 → Self（辅助/增益）。
        /// <para>新增 `SrcDam > 0` 一条 —— 官方 `SrcDam` 非空的技能（如「重击」Bash）自身伤害列为空，
        /// 只看 `DmgMin/DmgMax` 会把它误判成 `Self`（辅助/增益）⇒ 施放时零效果（审计 R1）。</para>
        /// </summary>
        private static SkillTarget TargetOf(Table.BaseSkillRow row)
        {
            if (row.Passive != 0) return SkillTarget.None;
            if (!string.IsNullOrEmpty(row.Missile)) return SkillTarget.Ground;
            if (row.SrcDam > 0) return SkillTarget.Enemy;
            if (row.DmgMin > 0 || row.DmgMax > 0) return SkillTarget.Enemy;
            return SkillTarget.Self;
        }

        /// <summary>需求等级 → 面板行下标（档位见 `SkillTuning.TreeLevelTiers`；超出档位归最后一档）。</summary>
        private static int TierIndex(int reqLevel)
        {
            var tiers = SkillTuning.TreeLevelTiers;
            for (var i = 0; i < tiers.Length; i++)
            {
                if (reqLevel <= tiers[i]) return i;
            }
            return tiers.Length - 1;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 投射物
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 飞出一个投射物（数值取 `skill_c` 伤害 + `missile_c` 速度/射程/半径）。
        /// <para><paramref name="spreadIndex"/> / <paramref name="spreadTotal"/>：多发技能（充能弹）的扇形铺开 ——
        /// 总张角 = <see cref="SkillTuning.MultiProjectileSpreadDeg"/> × (n-1)，以"施法者 → 目标"为中轴左右均分。</para>
        /// <para><paramref name="splashRadius"/> &gt; 0 ⇒ 命中（或到达）时按该半径**范围结算**（火球 / 冰爆 / 冰川尖刺 / 冰霜宝珠）。</para>
        /// </summary>
        private void SpawnProjectile(int skillId, SkillDef def, Table.BaseSkillRow row, IPlayerModule player,
            Vector2Int targetGrid, int dmgMin, int dmgMax, string missileCode,
            int spreadIndex = 0, int spreadTotal = 1, int splashRadius = 0)
        {
            var missile = FindMissileByCode(missileCode);

            var speed = missile != null && missile.Speed > 0
                ? missile.Speed * SkillTuning.SpeedToTilesPerSecond
                : SkillTuning.FallbackSpeed;
            var range = missile != null && missile.Range > 0
                ? missile.Range * SkillTuning.RangeToTiles
                : SkillTuning.FallbackRange;
            var radius = missile != null && missile.Radius > 0
                ? missile.Radius * SkillTuning.RadiusToTiles
                : 0.6f;

            if (missile == null)
            {
                SkillLog.WarnThrottled("proj.nomissile",
                    $"SpawnProjectile：`missile_c` 里找不到 code=\"{missileCode}\"（skill_c.missile 列）" +
                    "⇒ 用兜底速度/射程（配表核对）");
            }

            var start = Iso.WorldToGridContinuous(player.World);
            var targetCenter = new Vector2(targetGrid.x + 0.5f, targetGrid.y + 0.5f);
            var dir = targetCenter - start;
            if (dir.sqrMagnitude < 1e-6f)
            {
                dir = new Vector2(1f, 0f);
                SkillLog.Warn($"SpawnProjectile：目标格 {targetGrid} 与施法者同格 ⇒ 方向退回 (1,0)（避免零向量）");
            }
            dir.Normalize();

            // 多发扇形：第 i 颗在中轴左右偏移（i - (n-1)/2）× 步角
            if (spreadTotal > 1)
            {
                var step = SkillTuning.MultiProjectileSpreadDeg;
                var off = (spreadIndex - (spreadTotal - 1) * 0.5f) * step;
                var rad = off * Mathf.Deg2Rad;
                var cs = Mathf.Cos(rad);
                var sn = Mathf.Sin(rad);
                dir = new Vector2(dir.x * cs - dir.y * sn, dir.x * sn + dir.y * cs);
            }

            var p = new Projectile
            {
                id = _nextProjectileId++,
                skillId = skillId,
                skillName = def.name,
                ownerId = GameConst.PlayerEntityId,
                pos = start + dir * SkillTuning.SpawnOffset,
                dir = dir,
                speed = speed,
                rangeLeft = range,
                hitRadius = radius + SkillTuning.MonsterBodyRadius,
                dmgMin = dmgMin,
                dmgMax = dmgMax,
                type = def.dmgType,
                celFile = missile != null ? missile.CelFile : string.Empty,
                splashRadius = splashRadius,
            };
            p.Trail.Add(p.pos);

            // 表现层（离线自检宿主里会失败并返回 false ⇒ 只有逻辑没有画面，已在 ProjectileView 里留日志）
            ProjectileView.TryCreate(p);

            _projectiles.Add(p);
            SkillLog.Info($"[Skill] 投射物 #{p.id}「{def.name}#{skillId}」发出：{start} → {targetCenter} " +
                          (spreadTotal > 1 ? $"[{spreadIndex + 1}/{spreadTotal}] " : "") +
                          $"速度={speed:0.00} 格/s 射程={range:0.00} 格 命中半径={p.hitRadius:0.00} 格 " +
                          $"伤害={dmgMin}-{dmgMax} {def.dmgType}" +
                          (splashRadius > 0 ? $" 落点范围半径={splashRadius} 格" : "") +
                          $" 来源={(missile != null ? missile.Name + "(missile_c)" : "兜底")}");
        }

        /// <summary>
        /// 地形阻挡探针（注入给引擎件 `ProjectileRuntime`；**缓存一次**，避免每帧方法组转换分配委托）。
        /// </summary>
        private CloverEngine.ProjectileBlockProbe _blockProbe;

        /// <summary>目标探针（注入给引擎件 `ProjectileRuntime`；同上缓存）。</summary>
        private CloverEngine.ProjectileTargetProbe _targetProbe;

        /// <summary>
        /// 推进全部投射物：飞行 → **地形逐格步进** → 命中判定 → 消散。
        /// <para>
        /// `CloverEngine.ProjectileRuntime`（`clover-client-unity-engine/Runtime/Core/ProjectileRuntime.cs`
        /// 的 `Advance`），本方法只做**编排 + 项目侧副作用**（表现同步 / 伤害管线 / 音效 / 日志 / 回收）。
        /// </para>
        /// <para>
        /// 两处注入：`_blockProbe` = "这一格能不能走"（逐类裁决表仍是本文件 `BlocksProjectile`，唯一出处）、
        /// `_targetProbe` = "打到谁了"；"消散 / 命中要干什么"按引擎返回的 `ProjectileOutcome` **分类**分派。
        /// </para>
        /// </summary>
        private void TickProjectiles(float dt)
        {
            if (_projectiles.Count == 0) return;

            // 地形探针：拿不到地图 / 地图未生成 ⇒ 传 null（引擎件本帧不做地形阻挡），原因在这里留痕
            var map = AppContext.I != null ? AppContext.I.Map : null;
            CloverEngine.ProjectileBlockProbe blocked;
            if (map == null)
            {
                SkillLog.WarnOnce("proj.terrain.nomap",
                    "投射物地形碰撞：IMapModule 未接入（AppContext.Map == null）⇒ 本帧不做地形阻挡" +
                    "（撞墙会穿过去；离线自检宿主属正常）");
                blocked = null;
            }
            else if (!map.IsGenerated)
            {
                SkillLog.WarnOnce("proj.terrain.nogen",
                    "投射物地形碰撞：地图未生成（IsGenerated == false）⇒ 本帧不做地形阻挡");
                blocked = null;
            }
            else
            {
                if (_blockProbe == null) _blockProbe = ProbeTerrainBlocked;
                blocked = _blockProbe;
            }

            var monster = AppContext.I != null ? AppContext.I.Monster : null;
            var monsterCount = monster != null ? monster.All.Count : 0;
            if (monsterCount > 0 && _targetProbe == null) _targetProbe = ProbeMonster;

            for (var i = _projectiles.Count - 1; i >= 0; i--)
            {
                var p = _projectiles[i];

                if (!p.alive)
                {
                    RetireProjectile(p, i);
                    continue;
                }

                // ── 一帧推进（引擎件）─────────────────────────────────────────
                //    审计 B 红行 R3：地形扫掠**必须排在命中怪物之前** —— 一帧跨多格（高速 / 卡帧后的 dt）
                //    时端点会越过墙，若先判怪物就会判成"命中墙后那只怪" = 隔墙射杀。
                //    引擎件内部次序：飞行积分 → 地形逐格扫掠（截停 + 回退射程）→ 最近命中
                //    （截停后**还要再判一次**：贴墙站着的怪必须能被打到）→ 分类。
                var body = p.ToBody();
                // 引擎件签名 = `Advance(ref body, dt, blocked, target, targetCount)`（**5 参**）：
                //   地形扫掠的采样步长由引擎件自持（`ProjectileRuntime.DefaultSampleStep` = 0.25 格），
                //   与本文件的 `TerrainSampleStep` **同值** ⇒ 不再作为参数传出（行为不变）。
                var tick = CloverEngine.ProjectileRuntime.Advance(ref body, dt, blocked,
                    monsterCount > 0 ? _targetProbe : null, monsterCount);
                p.FromBody(body);

                if (tick.Stepped) p.Trail.Add(tick.SteppedPos);     // 轨迹点取**截停之前**的位置
                ProjectileView.Sync(p);
                if (tick.Clipped) ProjectileView.Sync(p);           // 截停后的表现点

                switch (tick.Outcome)
                {
                    case CloverEngine.ProjectileOutcome.HitMonster:
                        ResolveHit(p, tick.MonsterId);
                        RetireProjectile(p, i);
                        continue;

                    case CloverEngine.ProjectileOutcome.HitTerrain:
                        // 地形类别仍由本侧查（`TileKind` 是项目语义；引擎件只回"第一格阻挡格"）
                        ResolveTerrainHit(p, tick.BlockedCell, map.TileAt(tick.BlockedCell));
                        RetireProjectile(p, i);
                        continue;

                    case CloverEngine.ProjectileOutcome.Expired:
                        SkillLog.Info($"[Skill] 投射物 #{p.id}（{p.skillName}）射程耗尽自然消散：" +
                                      $"飞了 {p.traveled:0.00} 格，轨迹 {p.TrailText()}");
                        RetireProjectile(p, i);
                        continue;
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 投射物 × 地形（审计 B 红行 R3）
        //
        // 沿用项目**已有**的地形查询（`IMapModule.TileAt` + 契约方的 `TileKind`），**不新造一份地形**。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 投射物逐格采样的步长（格）。**必须 &lt; 1**：采样只在"格号变化"时判一次，步长越大越可能
        /// 整格跳过（高速投射物一帧跨多格）⇒ 取 1/4 格，保证任意线段都不会跳过一整格。
        /// </summary>
        private const float TerrainSampleStep = 0.25f;

        /// <summary>
        /// **该地形是否阻挡投射物** —— 逐类裁决的**唯一出处**（`internal` 供 `tools/probes/hosts/combatcheck` 逐类断言）。
        ///
        /// <para><b>裁决表</b>（判据 = 该地形在本项目的**几何/占格语义**，见 `MapGenTown` /
        /// `MapGenWilderness` 的铺图点；不是"随手把 Walkable 抄一遍"）：</para>
        /// <list type="table">
        /// <item><description><c>Grass / Dirt / Road / CaveFloor / TownFloor / Exit</c> = **可穿** —
        /// 全是**可穿的地面层**（= `TileKindInfo.IsGroundLayer` 的 6 个**非水**值；`Water` 虽然也是
        /// **阻挡**，故"可穿集合"与 `IsGroundLayer` **不再同集合**），投射物贴地飞过；桥面（deck）也是
        /// `Dirt` + deck 登记 ⇒ 桥上射出的投射物照常飞。</description></item>
        /// <item><description><c>Void</c> = **阻挡** — 图外 / 未生成，没有可飞的空间。</description></item>
        /// <item><description><c>Wall</c> = **阻挡** — 帐篷 / 摊位 / 货车 / 野外建筑墙
        /// （`MapGenTown.cs` <c>'o'</c>；`MapGenWilderness.cs` <c>'W'</c>）。</description></item>
        /// <item><description><c>CaveWall</c> = **阻挡** — 岩体占满整格且不可走。</description></item>
        /// <item><description><c>Tree</c> = **阻挡** — 树干占格（`MapGenTown.cs` <c>'t'</c>；
        /// `MapGenWilderness.cs` <c>'T'</c>）。原版树干挡投射物。</description></item>
        /// <item><description><c>Fence</c> = **阻挡** — 栅栏（`MapGenTown.cs` <c>'f'</c>；
        /// `MapGenWilderness.cs` <c>'F'</c>）。本项目栅栏**占满整格且不可走**（不是"半格矮物"）
        /// ⇒ 按实体障碍处理。</description></item>
        /// <item><description><c>Rock</c> = **阻挡** — 石头矮墙 / **桥栏杆**（<c>'s'</c>）/ 崖壁 / 碎石 /
        /// 杂物；它们都是**占据整格的实体障碍**（桥栏杆正是 R2 里能盖住桥上实体的那个遮挡物）
        /// ⇒ 一律阻挡。</description></item>
        /// <item><description><c>Water</c> = **阻挡**（见 `Def/Enums.cs:115-134`）——
        /// 裁决依据 = ① 水格在本项目模型里是**占满整格、不可走**的地形（`TileKindInfo.IsWalkable` = false），
        /// ② 项目对 `TileKind` **只有一个"可走性"轴**（`TileKindInfo` 没有"仅挡行走、不挡弹道"这种数据位），
        /// ③ 水与 `Rock` 共用同一判等口径 ⇒ 判"挡"**保持行为不变**（零回归）。
        /// **仍待参考物裁决**：原版 `ds1` 的碰撞位里 `BlockWalk` 与 `BlockMissile` 是**两个位**，
        /// 若原版水格只置 `BlockWalk`，则投射物应当**飞过水面** ⇒ 那时改**本表一行** +
        /// `combatcheck §15.1` 一行即可（两处都有断言/闸门守着），本条不自行拍板原版语义。</description></item>
        /// </list>
        /// <para>**不许在别处再写一份判等表**；本表与 `TileKindInfo.IsWalkable` 的对应关系由
        /// `combatcheck` 断言守着。</para>
        /// </summary>
        internal static bool BlocksProjectile(TileKind kind)
        {
            switch (kind)
            {
                // ── 地面层：可穿 ──
                case TileKind.Grass:
                case TileKind.Dirt:
                case TileKind.Road:
                case TileKind.CaveFloor:
                case TileKind.TownFloor:
                case TileKind.Exit:
                    return false;

                // ── 实体障碍 / 图外：阻挡 ──
                case TileKind.Void:
                case TileKind.Rock:
                case TileKind.Tree:
                case TileKind.Fence:
                case TileKind.Wall:
                case TileKind.CaveWall:
                case TileKind.Water:      // 拆值后回来重判（判据见上面 XML：占整格 + 不可走 + 零回归）
                    return true;

                default:
                    // 新增 TileKind 却忘了登记裁决：按"阻挡"处理（安全侧），并留痕（不静默）
                    SkillLog.WarnOnce("proj.terrain.unknownkind",
                        $"BlocksProjectile：TileKind {(int)kind} 未登记裁决 ⇒ 按'阻挡投射物'处理" +
                        "（新增地形时请回来更新本表 + combatcheck 的逐类断言）");
                    return true;
            }
        }

        /// <summary>该地形能否被投射物穿过（= <see cref="BlocksProjectile"/> 取反；自证入口）。</summary>
        internal static bool PassableForProjectile(TileKind kind) => !BlocksProjectile(kind);

        /// <summary>
        /// **"这一格能不能走"** —— 注入给引擎件 `ProjectileRuntime.Advance` 的地形探针
        /// （逐格扫掠的内核在引擎件 `TrySweepTerrain` 里，见 `Runtime/Core/ProjectileRuntime.cs`）。
        /// <para>
        /// 逐类**裁决表**仍在本文件的 <see cref="BlocksProjectile"/>（唯一出处）——
        /// 引擎件只问"这一格挡不挡"，不抄第二份地形语义（新增 `TileKind` 时只需改这一处）。
        /// </para>
        /// </summary>
        /// <summary>
        /// **第 <paramref name="index"/> 个候选取哪个怪** —— 注入给引擎件 `ProjectileRuntime` 的目标探针。
        /// <para>索引空间**必须**与调用点传的 <c>targetCount</c> 同一份：调用点用
        /// <c>AppContext.Monster.All.Count</c>，本方法也用 `All[index]` ⇒ 全程 1:1，不会错位。</para>
        /// <para>位置口径与发射时一致：`targetCenter = (gridX + 0.5, gridY + 0.5)`（连续格坐标的格心，
        /// 见本文件发射段的 `new Vector2(targetGrid.x + 0.5f, …)`）。</para>
        /// <para>尸体（`alive == false`）返回 false ⇒ 引擎件跳过它，但仍占着自己的下标。</para>
        /// </summary>
        private static bool ProbeMonster(int index, out int id, out Vector2 center)
        {
            id = -1;
            center = default;

            var monster = AppContext.I != null ? AppContext.I.Monster : null;
            if (monster == null) return false;

            var all = monster.All;
            if (index < 0 || index >= all.Count)
            {
                // 非预期分支：探针索引越界（调用方的 targetCount 与本帧的 All 快照不一致）
                SkillLog.WarnThrottled("proj.probe.oob",
                    $"投射物目标探针索引越界（index={index}，All.Count={all.Count}）⇒ 本帧漏判一次命中");
                return false;
            }

            var m = all[index];
            if (m == null || !m.alive) return false;

            id = m.id;
            center = new Vector2(m.gridX + 0.5f, m.gridY + 0.5f);
            return true;
        }

        private static bool ProbeTerrainBlocked(Vector2Int cell)
        {
            var map = AppContext.I != null ? AppContext.I.Map : null;
            if (map == null)
            {
                // 理论不可达（`TickProjectiles` 外层已判并传 null）；防御：不把"拿不到地图"变成"撞墙"
                return false;
            }
            return BlocksProjectile(map.TileAt(cell));
        }

        /// <summary>
        /// 投射物撞上不可穿越地形 ⇒ **消散**。
        /// 表现 = 复用在已有命中音效（`SfxKeys.Hit`）+ 日志留痕，**不新增任何特效资源**
        /// （`client/Resources/**` 不许动）。
        /// </summary>
        private static void ResolveTerrainHit(Projectile p, Vector2Int cell, TileKind kind)
        {
            p.hitTerrain = true;
            p.terrainCell = cell;
            p.terrainKind = kind;
            p.alive = false;

            var w = Projectile.WorldOf(p.pos);
            var ctx = AppContext.I;
            var audio = ctx != null ? ctx.Audio : null;
            if (audio != null) audio.SfxAt(Combat.SfxKeys.Hit, w.x, w.y, w.z);

            SkillLog.Info($"[Skill] 投射物 #{p.id}（{p.skillName}#{p.skillId}）撞上地形 {kind} " +
                          $"格 ({cell.x},{cell.y}) ⇒ 命中地形消散（不穿墙）：飞了 {p.traveled:0.00} 格；" +
                          $"轨迹 {p.TrailText()}");
        }

        /// <summary>把投射物从在飞列表里摘掉，并销毁它的表现节点。</summary>
        private void RetireProjectile(Projectile p, int index)
        {
            ProjectileView.Destroy(p);
            _projectiles.RemoveAt(index);
        }

        /// <summary>找被投射物擦到的怪物（取最近的一只）。</summary>
        private int FindHitMonster(Projectile p)
        {
            var monster = AppContext.I != null ? AppContext.I.Monster : null;
            if (monster == null) return -1;

            var all = monster.All;
            var bestId = -1;
            var bestDist = float.MaxValue;
            for (var i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.alive) continue;

                var center = new Vector2(s.gridX + 0.5f, s.gridY + 0.5f);
                if (!p.Overlaps(center)) continue;

                var d = Vector2.Distance(p.pos, center);
                if (d >= bestDist) continue;
                bestDist = d;
                bestId = s.id;
            }
            return bestId;
        }

        /// <summary>
        /// 投射物命中：掷伤害 → 交给 `DamagePipeline`（抗性 + 三件套只有一份实现）。
        /// <para>`splashRadius &gt; 0`（火球 / 冰爆 / 冰川尖刺 / 冰霜宝珠）⇒ 命中点**再按范围结算一圈**，
        /// 并在落点放一次冲击闪光 —— 这就是"命中效果"的可见落点。</para>
        /// </summary>
        private void ResolveHit(Projectile p, int monsterId)
        {
            var state = AppContext.I != null && AppContext.I.Monster != null
                ? AppContext.I.Monster.Get(monsterId)
                : null;
            if (state == null)
            {
                SkillLog.WarnThrottled("proj.hit.gone",
                    $"投射物 #{p.id} 命中判定时目标 m#{monsterId} 已不存在 ⇒ 本次伤害丢弃");
                return;
            }

            p.hitSomething = true;
            p.hitMonsterId = monsterId;
            p.alive = false;                 // 命中即结束飞行（`TickProjectiles` 随后把它从在飞列表摘掉）

            var raw = RollSkillDamage(p.dmgMin, p.dmgMax, p.type);
            SkillLog.Info($"[Skill] 投射物 #{p.id}（{p.skillName}#{p.skillId}）命中 m#{monsterId} {state.name}：" +
                          $"raw={raw} {p.type}；飞行 {p.traveled:0.00} 格；轨迹 {p.TrailText()}");

            Combat.DamagePipeline.ApplyToMonster(GameConst.PlayerEntityId, state, raw, p.type, false,
                "技能#" + p.skillId);

            if (p.splashRadius > 0)
            {
                var grid = new Vector2Int(state.gridX, state.gridY);
                var n = ResolveAreaDamage(grid, p.splashRadius, p.skillId, p.skillName, p.type,
                    p.dmgMin, p.dmgMax, out var total);
                SkillLog.Info($"[Skill] 投射物 #{p.id}「{p.skillName}」在 {grid} 炸开：半径 {p.splashRadius} 格 ⇒ " +
                              $"连主目标共波及 {n} 个目标，范围内掷点合计 {total} {p.type}");
            }

            SpawnImpactFx(p.skillId, p.skillName, new Vector2(state.gridX + 0.5f, state.gridY + 0.5f),
                p.type, null);
            if (p.splashRadius > 0) Shake(0.10f, 0.16f);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 即时伤害
        // ═════════════════════════════════════════════════════════════════════

        private static void ResolveOnMonster(int monsterId, int raw, DamageType type, int skillId,
            string skillName, string kind)
        {
            var state = AppContext.I != null && AppContext.I.Monster != null
                ? AppContext.I.Monster.Get(monsterId)
                : null;
            if (state == null)
            {
                SkillLog.WarnThrottled("cast.hit.gone",
                    $"TryCast({skillName}#{skillId})：结算时目标 m#{monsterId} 已不存在 ⇒ 本次伤害丢弃");
                return;
            }

            SkillLog.Info($"[Skill] {kind}命中 m#{monsterId} {state.name}：raw={raw} {type}（{skillName}#{skillId}）");
            Combat.DamagePipeline.ApplyToMonster(GameConst.PlayerEntityId, state, raw, type, false,
                "技能#" + skillId);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 冷却 / 法力 / 存档镜像
        // ═════════════════════════════════════════════════════════════════════

        private void TickCooldowns(float dt)
        {
            if (_cd.Count == 0) return;

            _cdKeys.Clear();
            foreach (var kv in _cd) _cdKeys.Add(kv.Key);

            List<int> ready = null;
            for (var i = 0; i < _cdKeys.Count; i++)
            {
                var id = _cdKeys[i];
                var v = _cd[id] - dt;
                if (v > 0f)
                {
                    _cd[id] = v;
                    continue;
                }

                _cd.Remove(id);
                if (ready == null) ready = new List<int>();
                ready.Add(id);
            }

            if (ready == null) return;

            for (var i = 0; i < ready.Count; i++)
            {
                var id = ready[i];
                SkillLog.Info($"冷却就绪：{Describe(id)}（{CdTotalOf(id):0.00}s 已过）");
                if (Game.Event != null) Game.Event.Emit(Events.SkillReady, id);
            }
        }

        private float CdTotalOf(int skillId)
        {
            return _cdTotal.TryGetValue(skillId, out var v) ? v : 0f;
        }

        /// <summary>
        /// 扣法力。走契约的**消耗**入口 `IPlayerModule.TrySpendMana(cost)`
        /// 调用方（<see cref="TryCast"/>）已先做过「法力足够」的 Warn 校验；
        /// 这里若仍失败，说明校验与真实扣减不一致（不应发生）⇒ 记一条 **Warn**（不是 Error）并放弃施放。
        /// </summary>
        private bool SpendMana(IPlayerModule player, int cost, string skillName)
        {
            if (cost <= 0) return true;      // 0 消耗技能：不调用契约入口

            var before = player.Mana;
            if (!player.TrySpendMana(cost))
            {
                SkillLog.WarnThrottled("cast.manaspend.failed",
                    $"TryCast({skillName}): 扣法力失败（当前 {before}，需要 -{cost}）⇒ 本次施放放弃；" +
                    "`IPlayerModule.TrySpendMana` 返回 false（法力不足或消耗 ≤ 0）");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 冷却时长（`skill_c` 无 delay 列 ⇒ 用 `SkillTuning` 的默认值，至少 `MinCooldownSeconds`）。
        /// <para>地面/自身类（往地上放、自己身上挂）用 0.40s 的"施法后摇"，必须点到怪的即时技用 0.25s
        /// —— 判据取 <see cref="SkillSpec.target"/>（与 `TryCast` 的目标闸**同一份**行为目录），
        /// 不取 `def.target` 的粗判（否则新星/火墙会被按"点怪"档给冷却）。</para>
        /// </summary>
        private static float CooldownSeconds(SkillDef def, SkillSpec spec, int level)
        {
            var cd = def.delayMs > 0
                ? def.delayMs / 1000f
                : (spec.target == SkillTarget.Ground || spec.target == SkillTarget.Self
                    ? SkillTuning.DefaultGroundCooldownSeconds
                    : SkillTuning.DefaultInstantCooldownSeconds);

            // 官方 skills.txt 的 delay 可按等级变化，但本项目表里没有该列 ⇒ 只保证不为 0
            if (level <= 0) cd = SkillTuning.MinCooldownSeconds;
            return cd < SkillTuning.MinCooldownSeconds ? SkillTuning.MinCooldownSeconds : cd;
        }

        private int ManaCostOf(int skillId, int level)
        {
            return _rows.TryGetValue(skillId, out var row)
                ? Combat.DamageFormula.SkillManaCost(row, level)
                : 0;
        }

        private int CostPointsOf(int skillId)
        {
            if (!_rows.TryGetValue(skillId, out var row)) return 1;
            return row.SkillPoints > 0 ? row.SkillPoints : 1;
        }

        /// <summary>掷技能伤害（走本模块注入的随机流 ⇒ 固定 seed 可复现）。</summary>
        private int RollSkillDamage(int min, int max)
        {
            if (min < 0) min = 0;
            if (max < min) max = min;
            if (max == 0) return 0;
            if (max == min) return min;
            return SkillRng().Next(min, max + 1);
        }

        /// <summary>
        /// 掷技能伤害 + **元素精通加成**（火焰/闪电；冰冷不走这里，见下）。
        /// <para>口径：先按精通百分比放大 `min`/`max` 两端、**再在放大后的区间里掷点**
        /// —— 与官方"精通是技能伤害加成，加成作用于伤害区间"一致（旧口径"掷点后再乘"
        /// 会让每一次掷点都多受一次整数截断，长尾技能累计偏差明显）。</para>
        /// <para>分系数值见 <see cref="SkillTuning"/> 的元素精通段（火 30+7×Δ、闪电 50+12×Δ）；
        /// **冰冷精通的机制是降怪物抗性**，不进本乘区（走 <see cref="ColdMasteryResistReduction"/>
        /// → `DamagePipeline` → `MonsterModule.ApplyDamage`）。</para>
        /// <para>只对**该元素**生效（`DamageType.Physical` / `Magic` 不受任何精通影响 ⇒ pct=0）。</para>
        /// </summary>
        private int RollSkillDamage(int min, int max, DamageType type)
        {
            var pct = MasteryPercent(type);
            if (pct <= 0) return RollSkillDamage(min, max);
            var bMin = min * (100 + pct) / 100;
            var bMax = max * (100 + pct) / 100;
            return RollSkillDamage(bMin, bMax);
        }

        /// <summary>
        /// `武器伤害 × SrcDam/128 × (1 + 官方 calc1 伤害倍率%)`，**走 `DamageFormula` 的既有生产入口**
        /// （不新写公式；武器区间与 `str/dex` 加成系数复用普攻那一处的 `CombatModule.GetWeaponDamage`）。
        /// <para>出处三处：① `SrcDam` —— D2 数据指南定义 *"percentage modifier for how much weapon damage
        /// is transferred to the skill's damage (Out of 128)"*（⇒ 分母 128，**不是位标志**）；
        /// ② 技能倍率 —— 官方 `calc1` + `*calc1 desc`，打表已解析成
        /// `skill_c.dmg_pct_base/per_lvl/parsed`（见 `tools/table-convert/convert.py::_damage_pct_of`）；
        /// ③ 武器数值 —— `item_c.dmg_min/dmg_max/str_bonus/dex_bonus`（官方 `Weapons.txt`）。</para>
        /// <para>非预期分支**都留日志**（不静默退化）：
        /// ① `DmgPctParsed=0`（官方 `calc1` 不是 `ln12/ln34` 前导，例如 `skill('Bash'.blvl)*par8`）
        /// ⇒ 技能倍率按 **0%** 结算并 WarnOnce；
        /// ② 结算为 0（武器区间为 0 / 倍率把伤害压到 0，例如 `Whirlwind` 1 级的 `-50%`）⇒ WarnThrottled；
        /// ③ 徒手 ⇒ `GetWeaponDamage` 内部已 Warn（官方空手 1~2 + 近战加成口径 100/0）。</para>
        /// </summary>
        private int RollWeaponSkillDamage(Table.BaseSkillRow row, SkillDef def, int level,
            IPlayerModule player, out string detail)
        {
            Combat.CombatModule.GetWeaponDamage(AppContext.I, out var wMin, out var wMax,
                out var strBonus, out var dexBonus);
            var roll = Combat.DamageFormula.RollWeaponDamage(wMin, wMax, SkillRng());

            // 官方 SrcDam：分母 128 的"武器伤害转移比"（128 = 100%）。官方实测取值域 = {32,48,96,128}。
            var srcDam = row.SrcDam;
            if (srcDam < 0) srcDam = 0;
            if (srcDam > 128)
            {
                SkillLog.WarnOnce("cast.weapon.srcdam." + row.Id,
                    $"RollWeaponSkillDamage：{def.name}#{row.Id} 的 src_dam={srcDam} 超出官方分母 128 ⇒ 按 128 计");
                srcDam = 128;
            }
            var transferred = srcDam >= 128 ? roll : roll * srcDam / 128;

            // 技能 ED%：打表已静态解析的 ln12/ln34（**可为负** —— Whirlwind 1 级 -50%）
            var edPct = 0;
            if (row.DmgPctParsed != 0)
            {
                edPct = row.DmgPctBase + row.DmgPctPerLvl * (level - 1);
            }
            else
            {
                SkillLog.WarnOnce("cast.weapon.pctraw." + row.Id,
                    $"RollWeaponSkillDamage：{def.name}#{row.Id} 官方 calc1=\"{row.DmgPctCalc}\"（desc=\"{row.DmgPctDesc}\"）" +
                    "无法静态解析（非 ln12/ln34 前导）⇒ 技能倍率按 0% 结算，只算武器伤害本身");
            }

            var raw = Combat.DamageFormula.PhysicalDamageEd(transferred, player.Str, player.Dex,
                strBonus, dexBonus, edPct);

            detail = $"武器 {wMin}-{wMax}（str/dex bonus {strBonus}/{dexBonus}）掷={roll} " +
                     $"×SrcDam {srcDam}/128 ⇒ {transferred}；力量={player.Str} 敏捷={player.Dex} " +
                     $"技能倍率={edPct}%";
            if (raw <= 0)
            {
                SkillLog.WarnThrottled("cast.weapon.zero",
                    $"RollWeaponSkillDamage：{def.name}#{row.Id} 结算为 0（{detail}）" +
                    " ⇒ 武器区间为 0 / 倍率把伤害压到 0");
            }
            return raw;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 查询小工具
        // ═════════════════════════════════════════════════════════════════════

        private static IPlayerModule Player
        {
            get { return AppContext.I != null ? AppContext.I.Player : null; }
        }

        private Rng SkillRng()
        {
            if (_rng != null) return _rng;

            var map = AppContext.I != null ? AppContext.I.Map : null;
            var seed = map != null && map.IsGenerated ? map.Seed : 0;
            if (seed == 0)
            {
                SkillLog.WarnOnce("skill.rng.fallback",
                    "SkillModule: 地图未生成（拿不到 seed）⇒ 技能随机改用时钟 seed（本局不可复现）");
                _rng = Rng.FromTime();
            }
            else
            {
                _rng = new Rng(seed ^ SkillSeedSalt);
            }
            return _rng;
        }

        private SkillDef FindDef(int skillId)
        {
            for (var i = 0; i < _available.Count; i++)
            {
                if (_available[i].id == skillId) return _available[i];
            }
            return null;
        }

        private bool IsPassive(int skillId)
        {
            return _rows.TryGetValue(skillId, out var row) && row.Passive != 0;
        }

        private bool CanLearnInternal(int skillId, bool logReason)
        {
            var def = FindDef(skillId);
            if (def == null)
            {
                if (logReason)
                {
                    SkillLog.WarnThrottled("learn.nodef",
                        $"CanLearn({skillId})：该技能不属于当前职业 {_cls} 或不存在 ⇒ false");
                }
                return false;
            }

            var player = Player;
            if (player == null)
            {
                if (logReason)
                {
                    SkillLog.WarnOnce("learn.noplayer",
                        "CanLearn: IPlayerModule 未接入（AppContext.Player == null）⇒ 无法判断等级/技能点，一律 false");
                }
                return false;
            }

            var learned = GetLevel(skillId);
            if (learned >= def.maxLevel)
            {
                if (logReason) SkillLog.Warn($"CanLearn({def.name}#{skillId})：已达上限 {def.maxLevel} 级 ⇒ false");
                return false;
            }

            if (!UnrestrictedLearning && player.Level < def.reqLevel)
            {
                if (logReason)
                {
                    SkillLog.Warn($"CanLearn({def.name}#{skillId})：角色等级 {player.Level} < 需求等级 {def.reqLevel} ⇒ false");
                }
                return false;
            }

            if (!UnrestrictedLearning && def.reqSkill != 0 && GetLevel(def.reqSkill) < def.reqSkillLevel)
            {
                var pre = FindDef(def.reqSkill);
                if (logReason)
                {
                    SkillLog.Warn($"CanLearn({def.name}#{skillId})：前置技能「{pre?.name ?? ("#" + def.reqSkill)}」" +
                                  $"等级 {GetLevel(def.reqSkill)} < {def.reqSkillLevel} ⇒ false");
                }
                return false;
            }

            var cost = CostPointsOf(skillId);
            if (!UnrestrictedLearning && player.SkillPoints < cost)
            {
                if (logReason)
                {
                    SkillLog.Warn($"CanLearn({def.name}#{skillId})：技能点不足（需要 {cost}，当前 {player.SkillPoints}）⇒ false");
                }
                return false;
            }

            return true;
        }

        /// <summary>选/绑技能前的合法性校验（存在 + 已学 + 非被动）。</summary>
        /// <summary>
        /// 绑定校验（`SelectSkill` / `AssignToButton` / 设热键共用）：技能必须属于本职业且**已学**。
        /// <para>
        /// ⚠️ 口径纠正：**不再拒绝被动技能**。旧版这里有一条"被动技能不能绑定到左右键"的闸，
        /// 但官方数据恰好相反 —— 官方 `skills.txt` 的 `leftskill` 列（= 该技能可否装到鼠标左键）在法师
        /// 4 个被动上**全是 1**：`Warmth`(#37) / `Fire Mastery`(#61) / `Lightning Mastery`(#63) /
        /// `Cold Mastery`(#65)；官方也没有"被动不能装鼠标"的规则（右键不限）⇒ 被动**可以**绑到左右键、
        /// 也可以设成热键。
        /// </para>
        /// <para>
        /// 「绑上之后点了不产生施放」由 <see cref="TryCast"/> 的 `cast.passive` 分支拒绝（原版用被动
        /// 点鼠标也正是"没反应"）—— 绑定闸与施放闸是两件事，别混。
        /// </para>
        /// </summary>
        private bool ValidateSelectable(int skillId, string who)
        {
            var def = FindDef(skillId);
            if (def == null)
            {
                SkillLog.WarnThrottled("sel.nodef", $"{who}({skillId})：该技能不属于当前职业 {_cls} 或不存在 ⇒ 忽略");
                return false;
            }
            if (GetLevel(skillId) <= 0)
            {
                SkillLog.Warn($"{who}({def.name}#{skillId})：尚未学习（等级 0）⇒ 忽略；先 Learn 再绑定");
                Notify("sel.nolearn", $"「{def.name}」还没学（先在技能树里加点）");
                return false;
            }

            return true;
        }

        private int FindMonsterNear(Vector2Int grid, int radius)
        {
            var monster = AppContext.I != null ? AppContext.I.Monster : null;
            if (monster == null)
            {
                SkillLog.WarnOnce("cast.nomonster", "FindMonsterNear: IMonsterModule 未接入 ⇒ 找不到目标");
                return -1;
            }

            var all = monster.All;
            var bestId = -1;
            var bestDist = radius + 1;      // 只要 ≤ radius 的
            for (var i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.alive) continue;
                var d = Iso.GridDistance(new Vector2Int(s.gridX, s.gridY), grid);
                if (d > radius || d >= bestDist) continue;
                bestDist = d;
                bestId = s.id;
            }
            return bestId;
        }

        private Table.BaseMissileRow FindMissileByCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            var all = Table.Tables.Default.Missile.All();
            if (all == null || all.Count == 0)
            {
                SkillLog.WarnOnce("proj.notable", "FindMissileByCode: missile_c 未加载（表为空）⇒ 用兜底投射物参数");
                return null;
            }
            for (var i = 0; i < all.Count; i++)
            {
                var row = all[i];
                if (row == null) continue;
                if (string.Equals(row.Code, code, StringComparison.OrdinalIgnoreCase)) return row;
            }
            return null;
        }

        private int CountTree(int treeIndex0Based)
        {
            var n = 0;
            for (var i = 0; i < _available.Count; i++)
            {
                if (_available[i].tree == treeIndex0Based) n++;
            }
            return n;
        }

        private string Describe(int skillId)
        {
            if (skillId == -1) return "普通攻击(-1)";
            var def = FindDef(skillId);
            return def != null ? $"{def.name}#{skillId}(等级 {GetLevel(skillId)})" : $"#{skillId}(未载入)";
        }

        private void RaiseTreeChanged()
        {
            if (Game.Event == null) return;
            Game.Event.Emit(Events.SkillTreeChanged, BuildTree());
        }

        /// <summary>
        /// 清场：在飞的投射物 + 留在场景里的技能效果（火墙/暴风雪/雷云/光环/多头蛇）+ 全部限时增益
        /// （换职业 / 离场 / 重开时调；不清增益会导致"换角色还带着上一局的护甲"）。
        /// </summary>
        private void ClearProjectiles()
        {
            for (var i = 0; i < _projectiles.Count; i++) ProjectileView.Destroy(_projectiles[i]);
            if (_projectiles.Count > 0)
            {
                SkillLog.Info($"ClearProjectiles：清掉 {_projectiles.Count} 个在飞的投射物（换职业/离场）");
            }
            _projectiles.Clear();

            for (var i = 0; i < _effects.Count; i++) SkillFxView.Destroy(_effects[i]);
            if (_effects.Count > 0)
            {
                SkillLog.Info($"ClearProjectiles：清掉 {_effects.Count} 个技能效果（地面场/光环/召唤物）");
            }
            _effects.Clear();

            if (_buffs.Count > 0)
            {
                SkillLog.Info($"ClearProjectiles：清掉 {_buffs.Count} 条限时增益（护甲/能量护盾/附魔/雷云）");
            }
            _buffs.Clear();
            SkillBuffs.SetMeleeFire(0, 0);      // 跨模块只读查询口也要归零（否则换角色后普攻还带火焰伤害）
            SkillBuffs.SetArmorDefensePercent(0);   // 同上：护甲的防御加成也必须归零（否则换角色后防御虚高）
            _warmthAccum = 0f;
        }

        // ── 存档镜像（`ISkillModule` 没有 WriteTo ⇒ 就近写回 `ResetForClass` 收到的 save 对象）──
        private void LoadLevelsFrom(CharacterSave save)
        {
            var ids = save.skillIds;
            var lvs = save.skillLevels;
            if (ids == null || lvs == null || ids.Count != lvs.Count)
            {
                if (ids != null && ids.Count > 0)
                {
                    SkillLog.Warn($"LoadLevelsFrom：存档技能 id/等级 列表长度不一致（{ids.Count} vs " +
                                  $"{(lvs == null ? 0 : lvs.Count)}）⇒ 只按较短的读，其余忽略");
                }
                return;
            }

            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                var lv = lvs[i];
                if (lv <= 0) continue;
                if (_rows.ContainsKey(id)) _levels[id] = lv;
                else SkillLog.Warn($"LoadLevelsFrom：存档里的技能 id={id} 不属于当前职业 {_cls} ⇒ 忽略该条（存档与职业不匹配？）");
            }
        }

        private void LoadButtonsFrom(CharacterSave save)
        {
            var btns = save.buttonSkills;
            if (btns == null || btns.Count < 2)
            {
                SkillLog.Warn("LoadButtonsFrom：存档的 buttonSkills 长度不足 2 ⇒ 左右键都用普通攻击(-1)");
                return;
            }
            _buttons[0] = btns[0];
            _buttons[1] = btns[1];
        }

        private void MirrorLevelsToSave()
        {
            if (_save == null) return;

            _save.skillIds.Clear();
            _save.skillLevels.Clear();

            var keys = new List<int>(_levels.Keys);
            keys.Sort();
            for (var i = 0; i < keys.Count; i++)
            {
                _save.skillIds.Add(keys[i]);
                _save.skillLevels.Add(_levels[keys[i]]);
            }
        }

        private void MirrorButtonsToSave()
        {
            if (_save == null) return;
            if (_save.buttonSkills == null) _save.buttonSkills = new List<int> { -1, -1 };
            while (_save.buttonSkills.Count < 2) _save.buttonSkills.Add(-1);
            _save.buttonSkills[0] = _buttons[0];
            _save.buttonSkills[1] = _buttons[1];
        }

        /// <summary>
        /// 读档还原技能热键（原版 `F1`~`F8`）。长度不足/旧档没这个字段 ⇒ 缺的按"未设置"。
        /// </summary>
        private void LoadHotkeysFrom(CharacterSave save)
        {
            var hs = save.skillHotkeys;
            if (hs == null || hs.Count == 0) return;      // 旧档：全部保持"未设置"（正常路径，不告警）

            var n = hs.Count < _hotkeys.Length ? hs.Count : _hotkeys.Length;
            for (var i = 0; i < n; i++)
            {
                var id = hs[i];
                if (id == HotkeyUnset || id == -1) { _hotkeys[i] = id; continue; }
                if (GetLevel(id) <= 0)
                {
                    SkillLog.Warn($"LoadHotkeysFrom：F{i + 1} 记的技能 id={id} 当前未学（洗过点？）⇒ 该热键按未设置处理");
                    continue;
                }
                _hotkeys[i] = id;
            }
            if (hs.Count != _hotkeys.Length)
            {
                SkillLog.Warn($"LoadHotkeysFrom：存档热键个数 {hs.Count} ≠ 本版 {_hotkeys.Length} ⇒ 只读前 " +
                              $"{n} 个，其余按未设置");
            }
        }

        /// <summary>把技能热键表写回存档对象（`ResetForClass` 收到的 `save`）。</summary>
        private void MirrorHotkeysToSave()
        {
            if (_save == null) return;
            if (_save.skillHotkeys == null) _save.skillHotkeys = new List<int>();
            _save.skillHotkeys.Clear();
            for (var i = 0; i < _hotkeys.Length; i++) _save.skillHotkeys.Add(_hotkeys[i]);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 自证用小工具（不在契约里；供 `tools/probes/hosts/combatcheck` 打印）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>在飞投射物数量。</summary>
        internal int ActiveProjectileCount { get { return _projectiles.Count; } }

        /// <summary>在飞投射物列表（只读；自证用）。</summary>
        internal IReadOnlyList<Projectile> Projectiles { get { return _projectiles; } }

        /// <summary>一行状态摘要。</summary>
        internal string DumpStats()
        {
            return $"技能统计：职业={_cls} 技能表 {_available.Count} 个，已学 {_levels.Count} 个，" +
                   $"冷却中 {_cd.Count} 个，在飞投射物 {_projectiles.Count} 个，" +
                   $"左键={_buttons[0]} 右键={_buttons[1]} 选中={_selected}，热键已设 {CountHotkeys()} 个";
        }
    }
}
