// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Combat/CombatModule.cs
// `ICombatModule` 的**唯一实现**（门面，internal，无参构造 ⇒ 能被 `AppContext.AutoWire` 反射创建）。
//
// 职责：命中判定 → 伤害结算 → 受击/死亡 → 复活；并**主持** `DeathFlow`（击杀链）与
// `DamagePipeline`（唯一结算函数）。
//
// 数据来源（**不硬编码**）：
//   · 玩家方：`IPlayerModule`（等级/力量/敏捷/生命/AR/防御/抗性）+ `IItemModule.Equipment`
//             （武器的 `ItemStack.dmgMin/dmgMax` ← 官方 `Weapons.txt`）
//   · 怪物方：`MonsterState`（level / defense / attackRating / damageMin / damageMax ← 官方 `MonStats.txt`
//             × `MonLvl.txt`，由 `MonsterModule` 在刷怪时填入）
//   · 公式：`DamageFormula`（官方公式，出处见该文件头）
//
// ⛔ 本文件**不**碰引擎的网络类门面（`Game` 的 Net / Sync / Http / Alert 等，单机下全为 null，
//    见 `tools/ai-skill/constraints.md` #8）。
// ⛔ 随机一律注入式 `CloverEngine.Rng`（**本局地图 seed 派生**，同 seed 可复现）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using AppContext = Diablo2.App.AppContext;

namespace Diablo2.Module.Combat
{
    /// <summary>战斗结算门面实现。</summary>
    internal sealed class CombatModule : ICombatModule
    {
        /// <summary>战斗随机流 salt（与掉落流区分开，保证「同 seed ⇒ 同命中序列」）。</summary>
        private const int CombatSeedSalt = 0x00C0FFEE;

        /// <summary>致命一击主动技能代码（官方 `skills.txt` 的 skill 名）。</summary>
        private const string CriticalStrikeCode = "Critical Strike";

        private readonly DeathFlow _death;

        private int _targetId = -1;
        private float _attackCd;
        private Rng _rng;
        private int _critSkillId;
        private bool _critSkillResolved;

        /// <summary>构造即订阅（`Events.AttackRequest` / `Events.ReviveRequest` 由 Input/UI 侧发出）。</summary>
        public CombatModule()
        {
            _death = new DeathFlow();

            if (Game.Event == null)
            {
                CombatLog.Error("CombatModule 构造时 Game.Event 为 null（Game.Launch 未调用？）" +
                                "⇒ 攻击/复活请求不会到达本模块");
                return;
            }

            Game.Event.On<int>(Events.AttackRequest, OnAttackRequest);
            Game.Event.On(Events.ReviveRequest, OnReviveRequest);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ICombatModule：只读状态
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public int CurrentTargetId { get { return _targetId; } }

        /// <inheritdoc />
        public float AttackCooldownRemain { get { return _attackCd < 0f ? 0f : _attackCd; } }

        // ═════════════════════════════════════════════════════════════════════
        // ICombatModule：目标
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void SetTarget(int monsterId)
        {
            if (_targetId == monsterId) return;

            var from = _targetId;
            _targetId = monsterId;
            if (Game.Event != null) Game.Event.Emit(Events.TargetChanged, monsterId);
            CombatLog.Info($"目标变化：{from} → {monsterId}（-1 = 无目标）");
        }

        /// <inheritdoc />
        public void ClearTarget()
        {
            SetTarget(-1);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ICombatModule：玩家攻击
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void RequestAttack(int monsterId)
        {
            SetTarget(monsterId);

            if (_attackCd > 0f)
            {
                // 输入模块按住左键时会**每帧**调用 ⇒ 必须降频（真实手感：原版攻击间隔 ~0.5s）
                CombatLog.WarnThrottled("atk.cooldown",
                    $"攻击请求被丢弃：冷却中（剩 {_attackCd:0.00}s / 间隔 {GameConst.PlayerAttackInterval:0.00}s）");
                return;
            }

            PlayerBasicAttack(monsterId);
        }

        /// <summary>玩家**普通攻击**（左键）：命中判定 → 物理伤害 → 结算。</summary>
        private void PlayerBasicAttack(int monsterId)
        {
            var ctx = AppContext.I;
            if (ctx == null)
            {
                CombatLog.WarnOnce("atk.ctx.null", "RequestAttack: AppContext.I 为 null（Bootstrap 未装配）⇒ 攻击无效");
                return;
            }

            var monster = ctx.Monster;
            if (monster == null)
            {
                CombatLog.WarnOnce("atk.monster.missing",
                    "RequestAttack: IMonsterModule 未接入（AppContext.Monster == null）⇒ 攻击无效");
                return;
            }

            var state = monster.Get(monsterId);
            if (state == null || !state.alive)
            {
                CombatLog.WarnThrottled("atk.target.gone",
                    $"RequestAttack: 目标 m#{monsterId} 不存在或已死 ⇒ 清目标");
                ClearTarget();
                return;
            }

            var player = ctx.Player;
            if (player == null)
            {
                CombatLog.WarnOnce("atk.player.missing",
                    "RequestAttack: IPlayerModule 未接入（AppContext.Player == null）⇒ 攻击无效");
                return;
            }

            var monsterGrid = new Vector2Int(state.gridX, state.gridY);
            var dist = Iso.GridDistanceEuclidean(player.Grid, monsterGrid);
            if (dist > GameConst.MeleeRange)
            {
                // 距离不够 ⇒ **不消耗冷却**（否则"点远了也白打一轮"），由 Input/Player 先走过去
                CombatLog.WarnThrottled("atk.outofrange",
                    $"RequestAttack: 目标 m#{monsterId} 距离 {dist:0.00} > 近战范围 {GameConst.MeleeRange:0.00}" +
                    " ⇒ 本次不结算（先靠近）");
                return;
            }

            _attackCd = GameConst.PlayerAttackInterval;
            monster.NotifyAttacked(monsterId);         // 官方：被打的怪立刻转入仇恨

            // ★ 片 8 B35：**挥击动画的发送方**（原版一次普攻 = 挥击动作 + 音效 + 受击反馈三件套，
            //   而本工程原先只发了后两件 —— 玩家视图从不播 `attack` 动作）。
            //   位置刻意放在"冷却已扣、距离已够"之后：命中与未命中都算一次**真的挥了**，
            //   而超距/冷却丢弃的请求**不算**（那种情况角色并没有出手）⇒ 视图不会空挥。
            if (Game.Event != null) Game.Event.Emit(Events.PlayerAttacked, monsterId);
            else CombatLog.WarnOnce("atk.event.null",
                "Game.Event 为 null（Game.Launch 未调用？）⇒ 挥击动画事件未派发（伤害结算不受影响）");

            // ── 伤害（官方公式，见 DamageFormula 文件头）──
            // ★ 片 14（消除 E25）：连武器的 str_bonus/dex_bonus 一起取 —— 弓/弩是 DexBonus=100，
            //   旧实现一律按力量算 ⇒ 拿弓的伤害是错的。
            int wMin, wMax, wStrBonus, wDexBonus;
            GetWeaponDamage(ctx, out wMin, out wMax, out wStrBonus, out wDexBonus);
            var roll = DamageFormula.RollWeaponDamage(wMin, wMax, CombatRng());
            var raw = DamageFormula.PhysicalDamage(roll, player.Str, player.Dex, wStrBonus, wDexBonus,
                                                   SkillMultiplier());

            // 致命一击（官方被动 Critical Strike ⇒ 伤害翻倍）
            var critLevel = CritSkillLevel(ctx);
            var critical = raw > 0 && CombatRng().Chance(DamageFormula.CriticalStrikeChance(critLevel));
            if (critical) raw *= 2;

            var chance = DamageFormula.HitChance(player.Level, state.level, player.AttackRating, state.defense);
            var hit = CombatRng().Chance(chance);

            CombatLog.Info($"[普攻] m#{monsterId} {state.name}：武器 {wMin}-{wMax} 掷={roll} "
                           + $"力量={player.Str} 敏捷={player.Dex} 武器bonus={wStrBonus}/{wDexBonus} " +
                           $"⇒ raw={raw}{(critical ? "（暴击 ×2）" : "")}；命中率={chance * 100f:0.0}% " +
                           $"ALvl={player.Level} DLvl={state.level} AR={player.AttackRating} DR={state.defense} " +
                           $"⇒ {(hit ? "命中" : "未命中")}（距离 {dist:0.00}）");

            if (!hit)
            {
                DamagePipeline.ReportMiss(GameConst.PlayerEntityId, monsterId, false, raw, DamageType.Physical,
                    state.worldX, state.worldY, state.worldZ, "普攻");
                return;
            }

            DamagePipeline.ApplyToMonster(GameConst.PlayerEntityId, state, raw, DamageType.Physical, critical, "普攻");
        }

        /// <summary>
        /// 玩家武器伤害区间 + **该武器的官方加成系数**（所有已装备武器之和；无武器 ⇒ 官方空手 1~2）。
        /// <para>
        /// ★ 片 14（**消除【例外 E25】**）：新增 `strBonus` / `dexBonus` 两个出口 —— 逐武器从配表
        /// `item_c` 的 `str_bonus` / `dex_bonus` 读（打表来源 = 官方 `Weapons.txt` 的
        /// `StrBonus` / `DexBonus`）。近战多为 `100/0`、**弓弩为 `0/100`**；空手按官方近战口径 `100/0`。
        /// </para>
        /// </summary>
        private static void GetWeaponDamage(AppContext ctx, out int min, out int max,
                                            out int strBonus, out int dexBonus)
        {
            min = 0;
            max = 0;
            strBonus = 0;
            dexBonus = 0;

            var item = ctx != null ? ctx.Item : null;
            if (item != null)
            {
                var equip = item.Equipment;
                if (equip != null)
                {
                    for (var i = 0; i < equip.Count; i++)
                    {
                        var e = equip[i];
                        if (e == null || e.type != ItemType.Weapon) continue;
                        if (e.dmgMin > 0) min += e.dmgMin;
                        if (e.dmgMax > 0) max += e.dmgMax;

                        // 逐武器取官方系数（多把时取较大者；本工程为主手单武器）
                        var row = Table.Tables.Default.Item.Get(e.itemId);
                        if (row == null)
                        {
                            CombatLog.WarnThrottled("atk.itembonus.miss",
                                $"GetWeaponDamage: item_c 查不到 itemId={e.itemId} ⇒ 该武器按默认 100/0 计" +
                                "（配表未加载或 itemId 越界）");
                            continue;
                        }
                        if (row.StrBonus > strBonus) strBonus = row.StrBonus;
                        if (row.DexBonus > dexBonus) dexBonus = row.DexBonus;
                    }
                }
            }
            else
            {
                CombatLog.WarnThrottled("atk.noitem",
                    "GetWeaponDamage: IItemModule 未接入（AppContext.Item == null）⇒ 按空手结算");
            }

            if (max <= 0)
            {
                min = DamageFormula.UnarmedMinDamage;
                max = DamageFormula.UnarmedMaxDamage;
                // 官方空手（fists）走近战口径 ⇒ StrBonus = 100 / DexBonus = 0
                strBonus = DamageFormula.MeleeStrBonus;
                dexBonus = 0;
                CombatLog.WarnThrottled("atk.unarmed",
                    $"GetWeaponDamage: 没有装备武器 ⇒ 按官方空手 {min}-{max} 结算（加成 100/0）");
            }
            if (min > max) min = max;
        }

        /// <summary>技能倍率（普通攻击 = 1；技能伤害走 `SkillModule` 自己的通道，不在此处）。</summary>
        private static float SkillMultiplier()
        {
            return 1f;
        }

        /// <summary>致命一击被动的当前等级（技能未接入 / 未学 ⇒ 0）。</summary>
        private int CritSkillLevel(AppContext ctx)
        {
            var skill = ctx != null ? ctx.Skill : null;
            if (skill == null) return 0;
            var id = CritSkillId();
            return id > 0 ? skill.GetLevel(id) : 0;
        }

        /// <summary>`skill_c` 里代码为 `Critical Strike` 的行 id（查一次缓存；找不到 ⇒ 0）。</summary>
        private int CritSkillId()
        {
            if (_critSkillResolved) return _critSkillId;
            _critSkillResolved = true;
            _critSkillId = 0;

            var all = Table.Tables.Default.Skill.All();
            if (all == null || all.Count == 0)
            {
                CombatLog.WarnOnce("crit.notable", "CritSkillId: skill_c 未加载（表为空）⇒ 致命一击不生效");
                return _critSkillId;
            }
            for (var i = 0; i < all.Count; i++)
            {
                var row = all[i];
                if (row == null) continue;
                if (!string.Equals(row.Code, CriticalStrikeCode, StringComparison.Ordinal)) continue;
                _critSkillId = row.Id;
                break;
            }
            if (_critSkillId == 0)
            {
                CombatLog.WarnOnce("crit.notfound",
                    $"CritSkillId: skill_c 里找不到 code=\"{CriticalStrikeCode}\" ⇒ 致命一击不生效（配表核对）");
            }
            return _critSkillId;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ICombatModule：怪物攻击玩家
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void RequestMonsterAttack(int monsterId)
        {
            var ctx = AppContext.I;
            if (ctx == null || ctx.Monster == null || ctx.Player == null)
            {
                CombatLog.WarnOnce("monatk.missing",
                    "RequestMonsterAttack: AppContext/IMonsterModule/IPlayerModule 未就绪 ⇒ 本次怪物攻击丢弃");
                return;
            }

            var state = ctx.Monster.Get(monsterId);
            if (state == null || !state.alive)
            {
                CombatLog.WarnThrottled("monatk.gone",
                    $"RequestMonsterAttack: 怪物 m#{monsterId} 不存在或已死 ⇒ 忽略");
                return;
            }

            var player = ctx.Player;
            if (player.IsDead)
            {
                // 玩家已死：官方里怪物会停止攻击（尸体不挨打）
                CombatLog.WarnThrottled("monatk.playerdead",
                    $"RequestMonsterAttack: 玩家已死（m#{monsterId} {state.name}）⇒ 本次攻击取消");
                return;
            }

            var monsterGrid = new Vector2Int(state.gridX, state.gridY);
            var dist = Iso.GridDistanceEuclidean(player.Grid, monsterGrid);
            var range = state.ai == MonsterAI.Range || state.ai == MonsterAI.Shaman
                ? GameConst.RangedRange
                : GameConst.MeleeRange;
            if (dist > range)
            {
                CombatLog.WarnThrottled("monatk.range",
                    $"RequestMonsterAttack: m#{monsterId}（{state.ai}）距离 {dist:0.00} > 射程 {range:0.00} ⇒ 本次攻击取消");
                return;
            }

            var raw = CombatRng().Next(state.damageMin, state.damageMax + 1);
            var chance = DamageFormula.HitChance(state.level, player.Level, state.attackRating, player.Defense);
            var hit = CombatRng().Chance(chance);

            var w = player.World;
            CombatLog.Info($"[怪攻] m#{monsterId} {state.name}（{state.ai}）掷={raw} 命中率={chance * 100f:0.0}% " +
                           $"ALvl={state.level} DLvl={player.Level} AR={state.attackRating} DR={player.Defense} " +
                           $"⇒ {(hit ? "命中" : "未命中")}（距离 {dist:0.00}）");

            if (!hit)
            {
                DamagePipeline.ReportMiss(monsterId, GameConst.PlayerEntityId, true, raw, DamageType.Physical,
                    w.x, w.y + 1.1f, w.z, "怪攻");
                return;
            }

            DamagePipeline.ApplyToPlayer(monsterId, raw, DamageType.Physical, "怪攻");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ICombatModule：复活
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void RevivePlayer()
        {
            var ctx = AppContext.I;
            var player = ctx != null ? ctx.Player : null;
            if (player == null)
            {
                CombatLog.WarnOnce("revive.player.missing",
                    "RevivePlayer: IPlayerModule 未接入 ⇒ 复活流程无法执行");
                return;
            }

            if (!player.IsDead)
            {
                CombatLog.WarnThrottled("revive.notdead", "RevivePlayer: 玩家未死亡（重复请求？）⇒ 忽略");
                return;
            }

            var before = player.Grid;
            player.Revive();                 // 契约：回城并恢复一部分生命（内部发事件）
            _death.NotifyPlayerRevived();
            ClearTarget();

            CombatLog.Info($"[死亡链] 玩家复活：位置 {before} → {player.Grid}，生命 {player.Life}/{player.MaxLife} " +
                           $"（死亡屏由 UI 关闭）");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ICombatModule：Tick / Reset
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void Tick(float dt)
        {
            if (_attackCd > 0f)
            {
                _attackCd -= dt;
                if (_attackCd < 0f) _attackCd = 0f;
            }
        }

        /// <inheritdoc />
        public void Reset()
        {
            if (_targetId != -1) SetTarget(-1);
            _attackCd = 0f;
            _rng = null;
            _critSkillResolved = false;
            _critSkillId = 0;
            _death.Reset();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 事件 / 随机
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>`Events.AttackRequest`（参数 = 怪物 id）：Input/UI 的「点怪打怪」入口。</summary>
        private void OnAttackRequest(int monsterId)
        {
            RequestAttack(monsterId);
        }

        /// <summary>`Events.ReviveRequest`（无参）：死亡屏「复活」按钮。</summary>
        private void OnReviveRequest()
        {
            RevivePlayer();
        }

        /// <summary>
        /// 战斗随机流：由 `IMapModule.Seed` 派生 ⇒ **同 seed ⇒ 同命中/同伤害序列**（自证与复现现场的前提）。
        /// </summary>
        private Rng CombatRng()
        {
            if (_rng != null) return _rng;

            var map = AppContext.I != null ? AppContext.I.Map : null;
            if (map != null && map.IsGenerated)
            {
                _rng = new Rng(map.Seed ^ CombatSeedSalt);
            }
            else
            {
                _rng = Rng.FromTime();
                CombatLog.WarnOnce("combat.rng.fallback",
                    "CombatModule: 地图未生成（拿不到 seed）⇒ 战斗随机改用时钟 seed（本局不可复现）");
            }
            return _rng;
        }
    }
}
