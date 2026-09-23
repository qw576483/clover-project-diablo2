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

            // ★ C3（用户本轮：「你是圆形判断的打击范围」「为什么打击范围这么奇怪」）：
            //   旧口径 = **纯半径**（圆）⇒ 背后的 / 正侧方的 / 隔着墙水的目标只要在 1.6 格内就能打到。
            //   新口径 = **正面扇形（±60°）+ 以朝向为轴的矩形走廊 + 线段不得被地形阻断**，三关都过才结算。
            //   形状函数唯一出处 = `Module/Combat/MeleeShape.cs`（纯函数，`combatcheck` 第 18 节逐例驱动）。
            //   ⛔ 不消耗冷却（不合格 = 没挥出去，与"超距"同一口径）：由 Player/Input 先转身/靠近。
            //   ★ melee-samecell（2026-09-23）：**同格（偏移 0,0）必须命中** —— 原版近战触及是距离/框口径
            //     （`Weapons.txt` rangeadder / `MonStats2.txt` MeleeRng），0 ≤ reach 恒真；而玩家沿
            //     `MoveCommand` 会走到怪所在格（`report-audioverify2.md` §2.3 实测 40/40 被拒）
            //     ⇒ 该退化点已在 `MeleeShape.InFrontCone` 补为"命中"，本处形状闸门随之放行。
            var shape = ShapeGate(player.Dir, player.Grid, monsterGrid, GameConst.MeleeRange);
            if (shape != null)
            {
                CombatLog.WarnThrottled("atk.shape",
                    $"RequestAttack: 目标 m#{monsterId} 被**判定形状**拒绝：{shape}" +
                    $"（玩家朝向={player.Dir} 偏移=({monsterGrid.x - player.Grid.x},{monsterGrid.y - player.Grid.y})" +
                    $" 距离 {dist:0.00} ≤ 近战范围 {GameConst.MeleeRange:0.00}）" +
                    " ⇒ 本次不结算（转身/靠近后重试）");
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
        /// <para>
        /// ★ 片 N：改为 `internal` —— **武器伤害类技能**（官方 `skills.txt` 列 219 `SrcDam ≠ 0`，
        /// 如「重击」Bash）必须与普攻**同一处**取武器区间/加成系数，
        /// 调用点 = `Module/Skill/SkillModule.RollWeaponSkillDamage`。⛔ 不许在 Skill 侧另写一份。
        /// </para>
        /// </summary>
        internal static void GetWeaponDamage(AppContext ctx, out int min, out int max,
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

        /// <summary>
        /// 玩家近战**判定形状**闸门（★ C3）。返回 null = 通过；否则返回可读的拒绝原因。
        /// <para>
        /// 三关：① **正面扇形**（±`FrontConeHalfAngleDeg`）② **以朝向为轴的矩形走廊**
        /// （长 = `reach`，半宽 = `MeleeShape.MeleeHalfWidth`）③ **线段不得被不可走地形阻断**。
        /// </para>
        /// 形状口径只在 `MeleeShape` 里（⛔ 这儿不复制常量）；逐例判据 = `combatcheck` 第 18 节。
        /// </summary>
        // ⚠️ 2026-09-23 主 agent 修编译（C3 落卡时引入）：本文件既有 `using Diablo2.Def;` 又为
        //    `Iso.DirectionDelta` 加了 `using CloverEngine;` ⇒ **两个命名空间都有 `Dir8`** ⇒ CS0104 歧义，
        //    整棵树编不过（4 个并行片全部因它无法进 Play）。这里显式限定为**项目自己的** `Diablo2.Def.Dir8`
        //    （`Iso.DirectionDelta` 签名要的也是它），语义零改动。
        private static string ShapeGate(Diablo2.Def.Dir8 dir, Vector2Int from, Vector2Int to, float reach)
        {
            // 朝向 → 格增量：走引擎权威表 `Iso.DirectionDelta`（⛔ 不在 MeleeShape 里另写一份映射）
            var dv = Iso.DirectionDelta(dir);
            float fx, fy;
            if (!MeleeShape.ToUnit(dv.x, dv.y, out fx, out fy))
                return $"玩家朝向不可解（Dir={dir}）";

            var dx = (float)(to.x - from.x);
            var dy = (float)(to.y - from.y);
            if (!MeleeShape.InFrontCone(fx, fy, dx, dy, MeleeShape.FrontConeCos))
                return $"不在正面扇形内（锥半角 {MeleeShape.FrontConeHalfAngleDeg:0}°）";
            if (!MeleeShape.InMeleeRect(fx, fy, dx, dy, reach, MeleeShape.MeleeHalfWidth))
                return $"不在矩形走廊内（长 {reach:0.00} / 半宽 {MeleeShape.MeleeHalfWidth:0.00}）";
            if (!MeleeShape.LineClear(WalkableProbe, from, to))
                return "线段被不可走地形阻断（不许隔墙 / 隔水挥到）";
            return null;
        }

        /// <summary>
        /// 把 `IMapModule.Walkable` 适配成 `MeleeShape.LineClear` 需要的委托。
        /// ⛔ 拿不到地图（未接入 / 未生成）⇒ 一律 true = **放行**（不把"没地图"变成"打不到"，由调用方留痕）。
        /// </summary>
        private static bool WalkableProbe(Vector2Int grid)
        {
            var map = AppContext.I != null ? AppContext.I.Map : null;
            if (map == null || !map.IsGenerated) return true;
            return map.Walkable(grid);
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
            // ★ C3（用户本轮：「**屏幕外都能打我？？？？？**」）：旧口径里远程/萨满用
            //   `GameConst.RangedRange`(8 格) —— 8 格 = 16 世界单位，而**可见半宽只有
            //   6 ×(16/9) ÷ 2.0 格/单位 = 5.33 格**（相机 ortho 6 / 一格 2.0×1.0 世界单位，
            //   见 `GameConst.IsoTilePxW/HalfTilePxW` 与 `Editor/ProjectBuilder` 的主相机）
            //   ⇒ 怪可以在**画面外**开枪（用户看到的正是这个）。
            //   新口径：远程/萨满的出手距离再被 `MonsterTuning.RangedAttackMaxRange` 夹一次
            //   （推导见该常量），近战不受影响（1.6 格 ≪ 屏幕）。
            var ranged = state.ai == MonsterAI.Range || state.ai == MonsterAI.Shaman;
            var range = ranged
                ? Mathf.Min(GameConst.RangedRange, Diablo2.Module.Monster.MonsterTuning.RangedAttackMaxRange)
                : GameConst.MeleeRange;
            if (dist > range)
            {
                CombatLog.WarnThrottled("monatk.range",
                    $"RequestMonsterAttack: m#{monsterId}（{state.ai}）距离 {dist:0.00} > 射程 {range:0.00} ⇒ 本次攻击取消");
                return;
            }

            // ★ C3：**线段不得被不可走地形阻断**（隔墙 / 隔水 / 跨河不许打到 —— 与玩家侧同一把尺子）。
            //   形状函数唯一出处 = `Module/Combat/MeleeShape.LineClear`。
            if (!MeleeShape.LineClear(WalkableProbe, monsterGrid, player.Grid))
            {
                CombatLog.WarnThrottled("monatk.blocked",
                    $"RequestMonsterAttack: m#{monsterId}（{state.ai}）与玩家的线段被不可走地形阻断" +
                    $"（距离 {dist:0.00} ≤ 射程 {range:0.00}）⇒ 本次攻击取消（不许隔墙打）");
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
