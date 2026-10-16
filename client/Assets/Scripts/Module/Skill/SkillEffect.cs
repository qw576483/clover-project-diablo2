// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Skill/SkillEffect.cs
// 技能**效果实例**（**本项目新增**）：一次施法在地面上留下的"看得见的东西" ——
//   冲击闪光 / 新星环 / 地面持续场（火墙·暴风雪·火焰之径）/ 直线（炼狱之火·闪电）/
//   连锁折线 / 落点标记（陨石）/ 光环（护甲·能量护盾）/ 召唤物（多头蛇）。
//
// 为什么与 `Projectile` 分开：
//   `Projectile` 的语义是"**一颗会飞的**、命中单体就消散"（引擎件 `ProjectileRuntime` 只为它服务）；
//   而本类的一条是"**留在原地 / 挂在施法者身上、按 `tick` 反复结算**的东西"
//   （火墙烧 3.5 秒、雷云劈 10 秒、陨石 1.2 秒后落地）—— 两者的推进、结算、生命周期都不同。
//   混进 `Projectile` 会让"命中即 `alive=false`"这条铁律失效（`ResolveHit` 依赖它）。
//
// 本类**只有数据 + 生命周期数学**（无 MonoBehaviour、无 `new GameObject`）⇒ 可被离线自足宿主
// `tools/probes/hosts/combatcheck` 完整驱动（逐技能断言"结算了几次 / 打到了谁"）。
// 表现层是 `SkillFxView`（自绘，与 `ProjectileView` 同一套口径：离线建节点失败 ⇒ 只掉画面不掉逻辑）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Skill
{
    /// <summary>效果的表现分类（决定 `SkillFxView` 怎么摆它）。</summary>
    internal enum SkillFxKind
    {
        /// <summary>命中/爆炸闪光（一次性，短）。</summary>
        Impact,

        /// <summary>以施法者为圆心的扩散环（新星 / 霜之新星 / 静电力场）。</summary>
        Nova,

        /// <summary>地面持续场（火墙 / 暴风雪）：`points` 里每一格各画一份。</summary>
        Field,

        /// <summary>直线（炼狱之火 / 闪电）：`from → to` 之间铺 `count` 份。</summary>
        Beam,

        /// <summary>连锁折线（连锁闪电）：`points` 是逐跳的顶点。</summary>
        Chain,

        /// <summary>延迟落点的圈（陨石）：落下前一直画在目标格。</summary>
        Marker,

        /// <summary>
        /// 从天而降的坠落物（陨石的火球 + 尾迹）：`pos` 每 tick 从 <see cref="fallFrom"/>（高空）
        /// 按重力曲线插值到 <see cref="landAt"/>（落点），到期与 <see cref="Marker"/> 的爆炸同步消失。
        /// 纯表现，不结算伤害（伤害由 Marker 到期那一下统一结算）。
        /// </summary>
        Falling,

        /// <summary>挂在施法者脚下的光环（护甲 / 能量护盾 / 附魔）。</summary>
        Aura,

        /// <summary>召唤物（多头蛇）：固定点位、逐帧播火焰。</summary>
        Summon,
    }

    /// <summary>一个技能效果实例（纯数据 + 生命周期数学）。</summary>
    internal sealed class SkillEffect
    {
        /// <summary>实例 id（本模块内自增；表现层与日志用）。</summary>
        public int id;

        /// <summary>施放的技能 id（`skill_c` 主键）。</summary>
        public int skillId;

        /// <summary>技能名（日志用）。</summary>
        public string skillName;

        /// <summary>表现分类。</summary>
        public SkillFxKind kind;

        /// <summary>中心点（连续格坐标；`Field` / `Nova` / `Marker` / `Summon` / `Impact` 用）。</summary>
        public Vector2 pos;

        /// <summary>直线起点（`Beam` 用；格坐标）。</summary>
        public Vector2 from;

        /// <summary>直线终点（`Beam` 用）。</summary>
        public Vector2 to;

        /// <summary>折线顶点（`Chain` 用；含起点与每一跳）。</summary>
        public readonly List<Vector2> points = new List<Vector2>();

        /// <summary>剩余寿命（秒）。</summary>
        public float lifeLeft;

        /// <summary>总寿命（秒；算进度用）。</summary>
        public float lifeTotal;

        /// <summary>下一次结算倒计时（秒；场 / 雷云 / 召唤开火用）。</summary>
        public float tickLeft;

        /// <summary>结算间隔（秒；≤0 = 不再周期结算）。</summary>
        public float tick;

        /// <summary>作用半径（格；0 = 单体/无范围）。</summary>
        public int radius;

        /// <summary>个数：召唤物数量 / 场格数（0 = 按单份处理）。</summary>
        public int count;

        /// <summary>伤害下限/上限（已按技能等级缩放）。</summary>
        public int dmgMin;

        /// <summary>伤害上限。</summary>
        public int dmgMax;

        /// <summary>伤害类型。</summary>
        public DamageType type;

        /// <summary>表现素材键（投射物 CelFile，或 `obj:` 开头的对象逐帧）。</summary>
        public string art;

        /// <summary>命中/爆炸素材键。</summary>
        public string impactArt;

        /// <summary>是否跟随施法者（`Aura` / `Trail` / `Storm`）。</summary>
        public bool followCaster;

        /// <summary>
        /// 是否是**火星带驱动器**（火焰之径）：自身不掉血、每 `tick` 秒在脚下**生出一簇独立的小火**
        /// （子场各自计时、各自结算）—— 这样"跑过去一条火路"才是真的"一条火路"，
        /// 而不是"一个跟着你走的火圈"。
        /// </summary>
        public bool markTrail;

        /// <summary>
        /// 是否是**暴风雪**（地面持续场的多层化标记）：除 9 格静态地面冰爆外，每 `tick` 再在随机场格
        /// 落 1~2 团 `IceBlast` 冰雹（原版「持续从天而降的冰柱」的近似），并在中心铺一圈 `FrostNova`
        /// 冰环（原版暴风雪的标志性「地面寒霜」）。仅表现增强，不改动任何伤害结算。
        /// </summary>
        public bool markBlizzard;

        /// <summary>是否**在到期时**才结算（`DelayedStrike`：陨石落地那一炸）。</summary>
        public bool resolveOnExpire;

        /// <summary>坠落物起点（`Falling` 用：高空，世界格坐标；相对落点抬 <see cref="SkillTuning.MeteorFallHeight"/> 格）。</summary>
        public Vector2 fallFrom;

        /// <summary>坠落物落点（`Falling` 用：与配套 `Marker` 同点）。</summary>
        public Vector2 landAt;

        /// <summary>已结算次数（自证用：火墙应结算 ~8 次、陨石恰好 1 次）。</summary>
        public int resolvedTicks;

        /// <summary>是否仍在活跃。</summary>
        public bool alive = true;

        /// <summary>累计伤害（自证用）。</summary>
        public int totalDamage;

        /// <summary>表现层节点（离线自检宿主里为 null ⇒ 只有逻辑没有画面）。</summary>
        public GameObject view;

        /// <summary>表现层渲染器列表（`Field`/`Beam`/`Chain` 是多份）。</summary>
        public List<SpriteRenderer> renderers;

        /// <summary>进度（0..1）。</summary>
        public float Progress
        {
            get { return lifeTotal <= 0f ? 1f : Mathf.Clamp01(1f - lifeLeft / lifeTotal); }
        }

        /// <summary>格坐标（Floor；排序与日志用）。转发引擎件，与投射物同一口径。</summary>
        public Vector2Int Grid
        {
            get { return CloverEngine.ProjectileRuntime.GridOf(pos); }
        }

        /// <summary>推进寿命（不结算；结算在 `SkillModule.TickEffects`）。</summary>
        public void StepLife(float dt)
        {
            if (!alive) return;
            lifeLeft -= dt;
            tickLeft -= dt;
            if (lifeLeft <= 0f)
            {
                lifeLeft = 0f;
                alive = false;
            }
        }
    }
}
