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
        private PlayerClass _cls = PlayerClass.Amazon;

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

        /// <summary>左右键绑定（下标 0 = 左键，1 = 右键；-1 = 普通攻击）。</summary>
        private readonly int[] _buttons = { -1, -1 };

        private int _selected = -1;
        private int _nextProjectileId = 1;
        private CharacterSave _save;
        private Rng _rng;

        /// <summary>
        /// `Events.SkillSlotAssignRequest` 是否已订阅（**幂等**）。
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
            EnsureSubscribed();     // F1~F8 技能槽绑定意图的订阅点（幂等）
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

            BuildAvailable();

            if (save != null)
            {
                LoadLevelsFrom(save);
                LoadButtonsFrom(save);
            }
            else
            {
                SkillLog.Warn("ResetForClass: save 为 null（未选角色？）⇒ 技能等级/绑键按空状态初始化");
            }

            _selected = _buttons[1];

            SkillLog.Info($"ResetForClass：职业={_cls}（{(int)_cls}）技能 {_available.Count} 个（" +
                          $"系分布 1/2/3 = {CountTree(0)}/{CountTree(1)}/{CountTree(2)}），" +
                          $"已学 {_levels.Count} 个，左键={_buttons[0]} 右键={_buttons[1]}");

            RaiseTreeChanged();
            RaiseButtonsChanged();      // 读档后把左右键绑定推给 HUD（重启后 HUD 也一致）
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
            player.AddSkillPoint(-cost);
            MirrorLevelsToSave();

            var def = FindDef(skillId);
            SkillLog.Info($"Learn：{def?.name}#{skillId} → 等级 {newLevel}/{def?.maxLevel}" +
                          $"（消耗技能点 {cost}，剩余 {player.SkillPoints}）");

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

            _buttons[button] = skillId;
            if (button == 1) _selected = skillId;
            MirrorButtonsToSave();

            SkillLog.Info($"AssignToButton：{(button == 0 ? "左键" : "右键")} = {Describe(skillId)}");
            if (button == 1 && Game.Event != null) Game.Event.Emit(Events.SkillSelected, skillId);
            RaiseButtonsChanged();      // HUD 的左右技能格换图
        }

        /// <summary>
        /// 订阅 `Events.SkillSlotAssignRequest`（原版 `F1`~`F8` 技能槽绑定意图）。
        /// 幂等；`Game.Event` 还没挂上时**不置位**（下次 `ResetForClass` 再试）。
        /// </summary>
        private void EnsureSubscribed()
        {
            if (_slotSubscribed || Game.Event == null) return;
            Game.Event.On<int>(Events.SkillSlotAssignRequest, OnSkillSlotAssignRequest);
            _slotSubscribed = true;
            SkillLog.Info($"已订阅 {Events.SkillSlotAssignRequest}（F1~F4 绑左键 / F5~F8 绑右键，按已学技能顺序）");
        }

        /// <summary>
        /// `F1`~`F8` → 把「当前职业**已学**且可主动施放」的第 N 个技能绑到左/右键技能格。
        /// <para>槽号口径（单一来源 = `Def/GameKeyAlias.cs`）：`SkillSlotIsLeftHand(slot)` 决定左右、
        /// `SkillSlotIndex(slot)` 决定"已学技能表"0 基下标。落档走既有 `CharacterSave.buttonSkills`
        /// （`AssignToButton` → `MirrorButtonsToSave`，格式不变）。</para>
        /// <para>非预期分支（都留日志）：槽号越界、本职业已学（可主动施放）技能数 ≤ 下标、
        /// 技能模块还没 `ResetForClass`（未选角色 ⇒ 没有已学技能表）。</para>
        /// </summary>
        private void OnSkillSlotAssignRequest(int slot)
        {
            var button = GameKeyAlias.SkillSlotIsLeftHand(slot) ? 0 : 1;
            var index = GameKeyAlias.SkillSlotIndex(slot);
            var hand = button == 0 ? "左键" : "右键";

            if (index < 0)
            {
                SkillLog.WarnThrottled("slot.oob",
                    $"技能槽绑定：槽号 {slot} 越界（合法 1..{GameKeyAlias.SkillSlotCount}）⇒ 忽略本次按键");
                return;
            }

            var learned = LearnedCastable();
            if (learned.Count == 0)
            {
                SkillLog.WarnThrottled("slot.nolearned",
                    $"技能槽绑定：F{slot}（{hand}，第 {index + 1} 个）按下，但本职业还没有已学且可主动施放的技能" +
                    $"（职业={_cls}，已学 {_levels.Count} 个）⇒ 不改变绑定；先去技能树学一个技能");
                return;
            }

            if (index >= learned.Count)
            {
                SkillLog.WarnThrottled("slot.shortlist",
                    $"技能槽绑定：F{slot}（{hand}，第 {index + 1} 个）按下，但本职业已学且可主动施放的技能只有 " +
                    $"{learned.Count} 个（< {index + 1}）⇒ 不改变绑定（可用槽位 F1~F{learned.Count}）");
                return;
            }

            var def = learned[index];
            SkillLog.Info($"技能槽绑定：F{slot} ⇒ {hand}技能格 = {Describe(def.id)}" +
                          $"（已学可施放列表第 {index + 1}/{learned.Count} 个，顺序 = 技能树 tree→reqLevel→id）");
            AssignToButton(button, def.id);
        }

        /// <summary>
        /// 当前职业**已学（等级 &gt; 0）且可主动施放（非被动）**的技能，顺序 = `_available` 的顺序
        /// （即技能树 tree → reqLevel → id）。
        /// <para>为什么排除被动：契约的 `AssignToButton` 自己会拒（`ValidateSelectable` 判 `passive`）⇒
        /// 这里先滤掉，免得"第 N 个"里混进绑不上的技能、让 F 键看起来时灵时不灵。</para>
        /// </summary>
        private List<SkillDef> LearnedCastable()
        {
            var list = new List<SkillDef>();
            for (var i = 0; i < _available.Count; i++)
            {
                var def = _available[i];
                if (def == null) continue;
                if (GetLevel(def.id) <= 0) continue;
                if (IsPassive(def.id)) continue;
                list.Add(def);
            }
            return list;
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
                args.skills.Add(def);
                args.learnedLevels.Add(GetLevel(def.id));
                args.learnable.Add(CanLearnInternal(def.id, false));   // 面板每帧都可能重绘 ⇒ 不打日志
            }
            return args;
        }

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
                return false;
            }

            if (IsPassive(skillId))
            {
                SkillLog.Warn($"TryCast：{def.name}#{skillId} 是被动技能（官方 passive=1）⇒ 不能主动施放");
                return false;
            }

            var level = GetLevel(skillId);
            if (level <= 0)
            {
                SkillLog.Warn($"TryCast：{def.name}#{skillId} 尚未学习（等级 0）⇒ 施放失败");
                return false;
            }

            var cdRemain = GetCooldownRemain(skillId);
            if (cdRemain > 0f)
            {
                SkillLog.WarnThrottled("cast.cd",
                    $"TryCast：{def.name}#{skillId} 冷却中（剩 {cdRemain:0.00}s / 共 {CdTotalOf(skillId):0.00}s）⇒ 施放失败");
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
                SkillLog.Warn($"TryCast：{def.name}#{skillId} 法力不足（需要 {cost}，当前 {player.Mana}）⇒ 施放失败");
                return false;
            }

            // ── 目标判定（在**扣蓝之前**做：否则"点空"会白扣蓝）──
            var row = _rows[skillId];
            var targetMonsterId = -1;
            switch (def.target)
            {
                case SkillTarget.Enemy:
                    targetMonsterId = FindMonsterNear(targetGrid, SkillTuning.EnemySearchRadius);
                    if (targetMonsterId < 0)
                    {
                        SkillLog.WarnThrottled("cast.notarget",
                            $"TryCast：{def.name}#{skillId} 需要敌方目标，但格 {targetGrid}（半径 " +
                            $"{SkillTuning.EnemySearchRadius}）附近没有存活怪物 ⇒ 施放失败（未扣蓝）");
                        return false;
                    }
                    break;

                case SkillTarget.Ground:
                case SkillTarget.Self:
                case SkillTarget.None:
                    break;

                default:
                    SkillLog.WarnThrottled("cast.unknowntarget",
                        $"TryCast：{def.name}#{skillId} 的目标类型 {(int)def.target} 未登记 ⇒ 按无目标处理");
                    break;
            }

            // ── 扣蓝 ──
            if (!SpendMana(player, cost, def.name)) return false;

            // ── 冷却 ──
            var cd = CooldownSeconds(def, level);
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

            // ── 效果 ──
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
            var hasDamage = dmgMax > 0 || weaponOnly;

            var missileCode = row.Missile;
            if (def.target == SkillTarget.Ground && !string.IsNullOrEmpty(missileCode))
            {
                SpawnProjectile(skillId, def, row, player, targetGrid, dmgMin, dmgMax, missileCode);
                return true;
            }

            if (!hasDamage)
            {
                //   判据（三选一都不成立）= 官方该行①无伤害值（`dmgMax<=0`）、
                //   ②非武器伤害源（`SrcDam==0`）、③无投射物（`Missile` 为空）。
                //   武器伤害类技能（`SrcDam>0`）已被上面的 `weaponOnly` 接走，不会再落到这里。
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
                return true;
            }

            // 即时结算：单目标 / 小范围
            if (def.target == SkillTarget.Ground)
            {
                var n = ResolveAreaDamage(targetGrid, SkillTuning.InstantAoeRadius, skillId, def, dmgMin, dmgMax);
                SkillLog.WarnThrottled("cast.ground.noaoe",
                    $"TryCast：{def.name}#{skillId} 是地面技能但 `skill_c.missile` 为空 ⇒ 按半径 " +
                    $"{SkillTuning.InstantAoeRadius} 的即时范围结算（命中 {n} 个目标）");
                return true;
            }

            var target = targetMonsterId >= 0 ? targetMonsterId : FindMonsterNear(targetGrid, SkillTuning.EnemySearchRadius);
            if (target < 0)
            {
                SkillLog.WarnThrottled("cast.losttarget",
                    $"TryCast：{def.name}#{skillId} 结算时目标已消失（召唤物/自身技能？）⇒ 只扣蓝进冷却");
                return true;
            }

            var raw = RollSkillDamage(dmgMin, dmgMax);
            ResolveOnMonster(target, raw, def.dmgType, skillId, def.name, "即时");
            return true;
        }

        /// <inheritdoc />
        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            TickCooldowns(dt);
            TickProjectiles(dt);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 技能树构建（配表 → SkillDef）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>从 `skill_c` 取本职业全部技能，排序并算好面板行列。</summary>
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

        /// <summary>飞出一个投射物（数值取 `skill_c` 伤害 + `missile_c` 速度/射程/半径）。</summary>
        private void SpawnProjectile(int skillId, SkillDef def, Table.BaseSkillRow row, IPlayerModule player,
            Vector2Int targetGrid, int dmgMin, int dmgMax, string missileCode)
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
            };
            p.Trail.Add(p.pos);

            // 表现层（离线自检宿主里会失败并返回 false ⇒ 只有逻辑没有画面，已在 ProjectileView 里留日志）
            ProjectileView.TryCreate(p);

            _projectiles.Add(p);
            SkillLog.Info($"[Skill] 投射物 #{p.id}「{def.name}#{skillId}」发出：{start} → {targetCenter} " +
                          $"速度={speed:0.00} 格/s 射程={range:0.00} 格 命中半径={p.hitRadius:0.00} 格 " +
                          $"伤害={dmgMin}-{dmgMax} {def.dmgType} 来源={(missile != null ? missile.Name + "(missile_c)" : "兜底")}");
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
        /// **该地形是否阻挡投射物** —— 逐类裁决的**唯一出处**（`internal` 供 `tools/combatcheck` 逐类断言）。
        ///
        /// <para><b>裁决表</b>（判据 = 该地形在本项目的**几何/占格语义**，见 `MapGenTown` /
        /// `MapGenWilderness` / `MapGenCave` 的铺图点；不是"随手把 Walkable 抄一遍"）：</para>
        /// <list type="table">
        /// <item><description><c>Grass / Dirt / Road / CaveFloor / TownFloor / Exit</c> = **可穿** —
        /// 全是**可穿的地面层**（= `TileKindInfo.IsGroundLayer` 的 6 个**非水**值；`Water` 虽然也是
        /// **阻挡**，故"可穿集合"与 `IsGroundLayer` **不再同集合**），投射物贴地飞过；桥面（deck）也是
        /// `Dirt` + deck 登记 ⇒ 桥上射出的投射物照常飞。</description></item>
        /// <item><description><c>Void</c> = **阻挡** — 图外 / 未生成，没有可飞的空间。</description></item>
        /// <item><description><c>Wall</c> = **阻挡** — 帐篷 / 摊位 / 货车 / 野外建筑墙
        /// （`MapGenTown.cs` <c>'o'</c>；`MapGenWilderness.cs` <c>'W'</c>）。</description></item>
        /// <item><description><c>CaveWall</c> = **阻挡** — 洞穴岩壁（`MapGenCave*`）。</description></item>
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

        /// <summary>投射物命中：掷伤害 → 交给 `DamagePipeline`（抗性 + 三件套只有一份实现）。</summary>
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

            var raw = RollSkillDamage(p.dmgMin, p.dmgMax);
            SkillLog.Info($"[Skill] 投射物 #{p.id}（{p.skillName}#{p.skillId}）命中 m#{monsterId} {state.name}：" +
                          $"raw={raw} {p.type}；飞行 {p.traveled:0.00} 格；轨迹 {p.TrailText()}");

            Combat.DamagePipeline.ApplyToMonster(GameConst.PlayerEntityId, state, raw, p.type, false,
                "技能#" + p.skillId);
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

        /// <summary>地面技能（无投射物）的小范围即时结算。</summary>
        private int ResolveAreaDamage(Vector2Int center, int radius, int skillId,
            SkillDef def, int dmgMin, int dmgMax)
        {
            var monster = AppContext.I != null ? AppContext.I.Monster : null;
            if (monster == null) return 0;

            var all = monster.All;
            var hit = 0;
            for (var i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.alive) continue;
                if (Iso.GridDistance(new Vector2Int(s.gridX, s.gridY), center) > radius) continue;

                ResolveOnMonster(s.id, RollSkillDamage(dmgMin, dmgMax), def.dmgType, skillId, def.name, "范围");
                hit++;
            }
            return hit;
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

        /// <summary>冷却时长（`skill_c` 无 delay 列 ⇒ 用 `SkillTuning` 的默认值，至少 `MinCooldownSeconds`）。</summary>
        private static float CooldownSeconds(SkillDef def, int level)
        {
            var cd = def.delayMs > 0
                ? def.delayMs / 1000f
                : (def.target == SkillTarget.Ground
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
                    "无法静态解析（非 ln12/ln34 前导）⇒ 技能倍率按 0% 结算，只算武器伤害本身（已登记）");
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
                    " ⇒ 武器区间为 0 / 倍率把伤害压到 0（已登记）");
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

            if (player.Level < def.reqLevel)
            {
                if (logReason)
                {
                    SkillLog.Warn($"CanLearn({def.name}#{skillId})：角色等级 {player.Level} < 需求等级 {def.reqLevel} ⇒ false");
                }
                return false;
            }

            if (def.reqSkill != 0 && GetLevel(def.reqSkill) < def.reqSkillLevel)
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
            if (player.SkillPoints < cost)
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
        private bool ValidateSelectable(int skillId, string who)
        {
            var def = FindDef(skillId);
            if (def == null)
            {
                SkillLog.WarnThrottled("sel.nodef", $"{who}({skillId})：该技能不属于当前职业 {_cls} 或不存在 ⇒ 忽略");
                return false;
            }
            if (IsPassive(skillId))
            {
                SkillLog.Warn($"{who}({def.name}#{skillId})：被动技能不能绑定到左右键 ⇒ 忽略");
                return false;
            }
            if (GetLevel(skillId) <= 0)
            {
                SkillLog.Warn($"{who}({def.name}#{skillId})：尚未学习（等级 0）⇒ 忽略；先 Learn 再绑定");
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

        private void ClearProjectiles()
        {
            for (var i = 0; i < _projectiles.Count; i++) ProjectileView.Destroy(_projectiles[i]);
            if (_projectiles.Count > 0)
            {
                SkillLog.Info($"ClearProjectiles：清掉 {_projectiles.Count} 个在飞的投射物（换职业/离场）");
            }
            _projectiles.Clear();
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

        // ═════════════════════════════════════════════════════════════════════
        // 自证用小工具（不在契约里；供 `tools/combatcheck` 打印）
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
                   $"左键={_buttons[0]} 右键={_buttons[1]} 选中={_selected}";
        }
    }
}
