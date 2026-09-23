// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Combat/DamagePipeline.cs
// **一次命中的统一结算与表现管线**（Combat 拥有；Skill 的施法伤害也走这里）。
//
// 为什么要有这个类（而不是把结算散在各处）：
//   `Module/Contracts.cs` 对 `ICombatModule` 的注释原文是
//   「怪物 AI 与玩家/怪物模块之间**只经本门面**结算伤害，**禁止各自算一遍**」，
//   而 `ICombatModule`（契约冻结，不许加方法）只暴露了「玩家普攻(`RequestAttack`)」与
//   「怪物打玩家(`RequestMonsterAttack`)」两个入口，**没有**「技能对怪物结算」的入口。
//   ⇒ 本类是该子系统内部的**唯一结算函数**，`CombatModule`（同命名空间）与
//     `SkillModule`（经 `Combat.DamagePipeline` 限定名访问，**不写 using**，故不违反
//     `_common.md` §4 的「Module/X 不许 using Module/Y 的具体类型」）都复用它，
//     保证「抗性只减一次、飘字/音效/血条三件套只写一遍」。
//
// ★ 命中反馈三件套（`docs/agents/agent-07-*.md` §4 硬要求 4）
//   **同一次命中**里必须都发生，本类按固定顺序一次做完：
//     ③ 目标头顶血条下降 —— `IMonsterModule.ApplyDamage` 内部会调
//        `IViewModule.UpdateMonster(state)`（同一次调用栈，立刻反映新血量）
//     ① 飘字             —— `IViewModule.ShowFloatingText(worldX/Y/Z, 数字, argb)`
//     ② 音效钩子         —— `IAudioModule.SfxAt(key, worldX/Y/Z)`（模块可为 null ⇒ 跳过并留痕）
//   三者都在**同一层调用栈**里完成，不存在"等下一帧/等事件回来"的空窗。
//
// 事件：目标不是玩家 ⇒ `Events.DamageDealt`；目标是玩家 ⇒ 额外再发 `Events.PlayerDamaged`
//       （两个事件都带同一份 `Def.DamageArgs` ⇒ HUD 只订阅一个也不会漏）。
// ⛔ 随机全部来自调用方传入的 `CloverEngine.Rng`（可复现）；本类**不自己掷随机**。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using AppContext = Diablo2.App.AppContext;

namespace Diablo2.Module.Combat
{
    /// <summary>命中结算 + 表现三件套的唯一入口。</summary>
    internal static class DamagePipeline
    {
        // ── 飘字配色（ARGB 打包，DTO/接口层不依赖 Unity 的 Color）──────────────
        private static readonly uint ColorPhysical = Pack(0xFF, 0xFF, 0x50, 0x50);   // 物理：红
        private static readonly uint ColorFire = Pack(0xFF, 0xFF, 0x8A, 0x1A);       // 火焰：橙
        private static readonly uint ColorCold = Pack(0xFF, 0x7A, 0xCF, 0xFF);       // 冰冷：蓝
        private static readonly uint ColorLight = Pack(0xFF, 0xFF, 0xE2, 0x4A);      // 闪电：黄
        private static readonly uint ColorPoison = Pack(0xFF, 0x8C, 0xE2, 0x4A);     // 毒素：绿
        private static readonly uint ColorCrit = Pack(0xFF, 0xFF, 0xFF, 0x9A);       // 暴击：亮金
        private static readonly uint ColorPlayerHit = Pack(0xFF, 0xFF, 0x30, 0x30);  // 玩家受伤：亮红
        private static readonly uint ColorUnknown = Pack(0xFF, 0xC8, 0xC8, 0xC8);

        /// <summary>把 (a,r,g,b) 打包成 0xAARRGGBB。</summary>
        public static uint Pack(byte a, byte r, byte g, byte b)
        {
            return ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
        }

        /// <summary>伤害飘字颜色（按伤害类型；暴击用亮金覆盖）。</summary>
        public static uint DamageColor(DamageType type, bool critical)
        {
            if (critical) return ColorCrit;
            switch (type)
            {
                case DamageType.Physical: return ColorPhysical;
                case DamageType.Fire: return ColorFire;
                case DamageType.Cold: return ColorCold;
                case DamageType.Lightning: return ColorLight;
                case DamageType.Poison: return ColorPoison;
                default: return ColorUnknown;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 对怪物结算（玩家普攻 / 玩家技能 / 宠物 / 环境）
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 对某只怪物结算一次**已命中**的伤害。
        /// <para>
        /// `raw` = 抗性结算**之前**的伤害（`DamageArgs.raw` 的口径）；抗性由
        /// `IMonsterModule.ApplyDamage` 内部按 `monster_c.res_*` 减一次（不会重复减）。
        /// </para>
        /// </summary>
        /// <returns>本次是否击杀。</returns>
        public static bool ApplyToMonster(int attackerId, MonsterState target, int raw, DamageType type,
            bool critical, string source)
        {
            if (target == null)
            {
                CombatLog.WarnOnce("pipe.monster.null", "DamagePipeline.ApplyToMonster: target 为 null ⇒ 本次伤害丢弃");
                return false;
            }

            var ctx = AppContext.I;
            var monster = ctx != null ? ctx.Monster : null;
            if (monster == null)
            {
                CombatLog.WarnOnce("pipe.monster.missing",
                    "DamagePipeline.ApplyToMonster: IMonsterModule 未接入（AppContext.Monster == null）⇒ 本次伤害丢弃");
                return false;
            }

            var hpBefore = target.hp;
            monster.ApplyDamage(target.id, raw, type);   // 内部：抗性结算 + 扣血 + 死亡 + 发事件 + 刷血条
            var hpAfter = target.hp;
            var amount = hpBefore - hpAfter;
            if (amount < 0) amount = 0;
            var killed = !target.alive;

            // ③ 血条：MonsterModule.ApplyDamage 内已调 View.UpdateMonster ⇒ 已在同一次栈里下降
            // ① 飘字（命中必飘；抗性把伤害压到 0 时飘 "0"，保证三件套语义一致）
            var view = ctx.View;
            if (view != null) view.ShowFloatingText(target.worldX, target.worldY, target.worldZ,
                amount.ToString(), DamageColor(type, critical));
            else CombatLog.WarnOnce("pipe.view.missing",
                "DamagePipeline: IViewModule 未接入（AppContext.View == null）⇒ 命中飘字/受击表现缺失（其余不受影响）");

            // 受击表现（闪白 + Hit 动画）—— ★ 片 Y（R1）：**击杀时不许再播 Hit**。
            //   理由：`monster.ApplyDamage` 内部已走 `MonsterModule.Die` ⇒ `IViewModule.PlayDeath`
            //   （死亡表现的唯一归属）；若这里再调 `PlayHit`，**同一次调用栈稍后**就会把死亡动作
            //   覆盖成受击动作 ⇒ 尸体停在受击末帧、`Death` 一次都没上屏（审计 D 的 R1，
            //   运行时 ViewAnim 观测集里没有 Death）。非击杀才播受击。
            if (view != null && !killed) view.PlayHit(target.id);

            // ② 音效钩子（IAudioModule 可空）
            // ★ 片 monster-audio：原版一次命中是**两层音** —— 武器撞击（`hit`）
            //   + **怪物自己的受击音**（`MonSounds.HitSound`，逐类一套；原版 `HitDelay` 列是它的延迟）。
            //   此前只有 `hit` 一层 ⇒ 用户听到的「打击没声音 / 怪物没音效」。
            //   击杀那一下只播死亡音（⛔ 不叠受击音：死亡表现归 `PlayDeath`，同前片 R1 的口径）。
            var audio = ctx.Audio;
            if (audio != null)
            {
                if (killed)
                {
                    audio.SfxAt(Monster.MonsterSfx.DieOf(target) ?? SfxKeys.MonsterDie,
                        target.worldX, target.worldY, target.worldZ);
                }
                else
                {
                    audio.SfxAt(SfxKeys.Hit, target.worldX, target.worldY, target.worldZ);
                    var ownHit = Monster.MonsterSfx.HitOf(target);
                    if (ownHit != null)
                        audio.SfxAt(ownHit, target.worldX, target.worldY, target.worldZ);
                }
            }

            if (amount > 0 || killed)
            {
                CombatLog.Info($"[{source}] 命中 m#{target.id} {target.name}：raw={raw} 实扣={amount} ({type}" +
                               $"{(critical ? " 暴击" : "")}) → hp {hpBefore}/{target.maxHp} → {hpAfter}/{target.maxHp}" +
                               $"{(killed ? "  【击杀】" : "")}");
            }
            else
            {
                CombatLog.WarnThrottled("pipe.monster.zero",
                    $"DamagePipeline: 命中但实扣 0（m#{target.id} {target.name} 对 {type} 抗性过高）raw={raw}");
            }

            Emit(attackerId, target.id, raw, amount, type, false, true, critical, hpAfter, killed,
                target.worldX, target.worldY, target.worldZ);
            return killed;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 对玩家结算（怪物 AI 出手）
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 对玩家结算一次**已命中**的伤害（抗性/防御由 `IPlayerModule.ApplyDamage` 内部处理，
        /// 契约原文：「受到伤害（走抗性/防御结算）」）。
        /// </summary>
        /// <returns>玩家是否因此死亡。</returns>
        public static bool ApplyToPlayer(int attackerId, int raw, DamageType type, string source)
        {
            var ctx = AppContext.I;
            var player = ctx != null ? ctx.Player : null;
            if (player == null)
            {
                CombatLog.WarnOnce("pipe.player.missing",
                    "DamagePipeline.ApplyToPlayer: IPlayerModule 未接入（AppContext.Player == null）⇒ 本次伤害丢弃");
                return false;
            }

            var hpBefore = player.Life;
            var died = player.ApplyDamage(raw, type);      // 内部：抗性结算 + 死亡（发 Events.PlayerDied）
            var hpAfter = player.Life;
            var amount = hpBefore - hpAfter;
            if (amount < 0) amount = 0;

            var w = player.World;
            var view = ctx.View;
            if (view != null)
            {
                // 玩家飘字抬高一点，别糊在角色身上
                view.ShowFloatingText(w.x, w.y + 1.1f, w.z, amount.ToString(),
                    died ? ColorCrit : ColorPlayerHit);
                // ★ 片 Y（R1 同类穷举）：**致死的那一下不播受击动作** —— 死亡表现归
                //   `ViewModule.TickPlayer` 的死亡档（Death > Hit 的优先级链）；这里再播 Hit
                //   会让死亡那一帧先出受击姿态（与怪物那条同源：击杀后不该再切非死亡动作）。
                if (!died) view.PlayHit(GameConst.PlayerEntityId);
            }

            var audio = ctx.Audio;
            if (audio != null)
            {
                audio.SfxAt(died ? SfxKeys.PlayerDie : SfxKeys.PlayerHurt, w.x, w.y, w.z);
            }

            CombatLog.Info($"[{source}] 玩家受击：raw={raw} 实扣={amount} ({type}) → 生命 {hpBefore} → {hpAfter}" +
                           $"{(died ? "  【死亡】" : "")}");

            Emit(attackerId, GameConst.PlayerEntityId, raw, amount, type, true, true, false, hpAfter, died, w.x, w.y, w.z);
            return died;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 未命中（也要发事件，且要有音效钩子——不然玩家听不出"打了但没中"）
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>记录一次未命中（`hit=false`、`amount=0`）：发事件 + 挥空音效（飘字不发，原版也不发）。</summary>
        public static void ReportMiss(int attackerId, int targetId, bool targetIsPlayer, int raw, DamageType type,
            float worldX, float worldY, float worldZ, string source)
        {
            var ctx = AppContext.I;
            var audio = ctx != null ? ctx.Audio : null;
            if (audio != null) audio.SfxAt(SfxKeys.Miss, worldX, worldY, worldZ);

            CombatLog.Info($"[{source}] 未命中：attacker={attackerId} target={targetId} " +
                           $"(raw={raw} 已丢弃) 目标免疫/闪避");

            // 未命中：`hit=false`（下游据此不飘伤害数字），但**音效钩子照发**（挥空声）
            Emit(attackerId, targetId, raw, 0, type, targetIsPlayer, false, false,
                targetIsPlayer ? SafePlayerHp(ctx) : SafeMonsterHp(ctx, targetId), false, worldX, worldY, worldZ);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 内部小工具
        // ═════════════════════════════════════════════════════════════════════

        private static int SafePlayerHp(AppContext ctx)
        {
            return ctx != null && ctx.Player != null ? ctx.Player.Life : 0;
        }

        private static int SafeMonsterHp(AppContext ctx, int monsterId)
        {
            if (ctx == null || ctx.Monster == null) return 0;
            var m = ctx.Monster.Get(monsterId);
            return m != null ? m.hp : 0;
        }

        private static void Emit(int attackerId, int targetId, int raw, int amount, DamageType type,
            bool targetIsPlayer, bool hit, bool critical, int targetHpAfter, bool killed,
            float x, float y, float z)
        {
            if (Game.Event == null)
            {
                CombatLog.WarnOnce("pipe.event.missing",
                    "DamagePipeline: Game.Event 为 null（Game.Launch 未调用？）⇒ 伤害事件未派发（飘字/音效已照常发生）");
                return;
            }

            var args = new DamageArgs
            {
                attackerId = attackerId,
                targetId = targetId,
                raw = raw,
                amount = amount,
                type = type,
                targetIsPlayer = targetIsPlayer,
                hit = hit,
                critical = critical,
                overTime = false,
                targetHpAfter = targetHpAfter,
                killed = killed,
                worldX = x,
                worldY = y,
                worldZ = z,
            };

            Game.Event.Emit(Events.DamageDealt, args);
            if (targetIsPlayer) Game.Event.Emit(Events.PlayerDamaged, args);
        }
    }
}
