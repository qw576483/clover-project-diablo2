// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Skill/Projectile.cs
// 投射物（火弹 / 冰弹 / 箭矢…）：**沿格直线飞行 + 命中判定 + 到达/超时消失**。
//
// 数值全部来自配表：
//   · 速度/射程/半径 ← `missile_c.speed/range/radius`（官方 `Missiles.txt` 的 `Vel/Range/Size`），
//     经 `SkillTuning` 的**可校准系数**换算成"格/秒、格"；
//   · 伤害 ← `skill_c.dmg_min/dmg_max`（官方 `Missiles.txt` 的伤害列几乎全是空的，
//     官方伤害落在**技能**上 —— 见 `docs/配表说明.md` §7 第 8 条）；
//   · 伤害类型 ← `skill_c.dmg_type`（官方 `EType`）。
//
// 本类**只有飞行数学**（无 MonoBehaviour、无 `new GameObject`）⇒ 可被离线自检宿主
// `tools/combatcheck/` 完整驱动（打印轨迹采样与命中记录）。
// 表现层是 `ProjectileView`（自绘，见该文件；`IViewModule` 契约里没有投射物入口，且不许改契约）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Skill
{
    /// <summary>一个飞行中的投射物（纯数据 + 飞行数学）。</summary>
    internal sealed class Projectile
    {
        /// <summary>投射物实例 id（本模块内自增，用于表现层与日志）。</summary>
        public int id;

        /// <summary>施放的技能 id（`skill_c` 主键）。</summary>
        public int skillId;

        /// <summary>技能名（日志用）。</summary>
        public string skillName;

        /// <summary>施法者实体 id（玩家 = `GameConst.PlayerEntityId`）。</summary>
        public int ownerId;

        /// <summary>连续格坐标（格中心制，与 `MonsterRuntime`/`Iso` 同一口径）。</summary>
        public Vector2 pos;

        /// <summary>单位方向（格空间）。</summary>
        public Vector2 dir;

        /// <summary>速度（格/秒）。</summary>
        public float speed;

        /// <summary>剩余射程（格）。</summary>
        public float rangeLeft;

        /// <summary>命中半径（格）= 配表半径换算 + 怪物身体半径。</summary>
        public float hitRadius;

        /// <summary>伤害下限（已按技能等级缩放）。</summary>
        public int dmgMin;

        /// <summary>伤害上限（已按技能等级缩放）。</summary>
        public int dmgMax;

        /// <summary>伤害类型。</summary>
        public DamageType type;

        /// <summary>官方动画文件名（`missile_c.cel_file`，如 `Firebolt`）——表现层据此找真图，找不到就用占位。</summary>
        public string celFile;

        /// <summary>是否仍在飞行。</summary>
        public bool alive = true;

        /// <summary>是否命中过目标（命中后 `alive=false`）。</summary>
        public bool hitSomething;

        /// <summary>命中的怪物 id（-1 = 未命中任何目标）。</summary>
        public int hitMonsterId = -1;

        /// <summary>
        /// 是否**命中地形**（撞墙 / 树 / 栅栏 / 水 / 桥栏杆…）后消散 —— 与 <see cref="hitSomething"/>
        /// （只表示命中怪物）互斥。判据与逐类裁决见 `SkillModule.BlocksProjectile`。
        /// </summary>
        public bool hitTerrain;

        /// <summary>命中的地形格（仅 <see cref="hitTerrain"/> == true 时有意义）。</summary>
        public Vector2Int terrainCell;

        /// <summary>命中的地形类型（仅 <see cref="hitTerrain"/> == true 时有意义；日志/自证用）。</summary>
        public TileKind terrainKind;

        /// <summary>累计飞行距离（格，自证用）。</summary>
        public float traveled;

        /// <summary>轨迹采样（每步一个点；自证要"打印轨迹采样"，见 `agent-07` §5）。</summary>
        public readonly List<Vector2> Trail = new List<Vector2>();

        /// <summary>表现层节点（**本项目新增**；离线自检宿主里为 null ⇒ 只有逻辑没有画面）。</summary>
        public GameObject View;

        /// <summary>表现层渲染器（自绘占位/帧图；离线为 null）。</summary>
        public SpriteRenderer Renderer;

        /// <summary>按 <paramref name="dt"/> 推进一步（不判命中——命中判定需要怪物列表，由 `SkillModule` 做）。</summary>
        public void Step(float dt)
        {
            if (!alive) return;

            var step = speed * dt;
            if (step <= 0f) return;

            if (step >= rangeLeft)
            {
                step = rangeLeft;
                rangeLeft = 0f;
                alive = false;              // 射程耗尽 ⇒ 自然消散
            }
            else
            {
                rangeLeft -= step;
            }

            pos += dir * step;
            traveled += step;
            Trail.Add(pos);
        }

        /// <summary>该点是否在命中范围内（与怪物身体半径一起放宽）。</summary>
        public bool Overlaps(Vector2 targetCenter)
        {
            return Vector2.Distance(pos, targetCenter) <= hitRadius;
        }

        /// <summary>格坐标（Floor；用于排序与日志）。</summary>
        public Vector2Int Grid
        {
            get { return new Vector2Int(Mathf.FloorToInt(pos.x), Mathf.FloorToInt(pos.y)); }
        }

        /// <summary>
        /// 连续格坐标 → 世界坐标。与 `Core/Iso.GridToWorld` 的正投影**同一口径**
        /// （`x=(px-py)*HalfW`、`y=-(px+py+1)*HalfH`），只是允许小数输入：
        /// 当 `pos == (gx+0.5, gy+0.5)` 时结果与 `Iso.GridToWorld(gx,gy)` 完全相同。
        /// <para>为什么本模块自己写这三行：`Core/` 契约冻结不许改，而 `Iso` 只接受整数格。</para>
        /// </summary>
        public static Vector3 WorldOf(Vector2 p)
        {
            return new Vector3((p.x - p.y) * Iso.HalfW, -(p.x + p.y + 1f) * Iso.HalfH, 0f);
        }

        /// <summary>轨迹的可读形式（自证打印）：`(x1.0,y1.0) → (x2.0,y2.0) …`（最多取前 <paramref name="max"/> 个点）。</summary>
        public string TrailText(int max = 8)
        {
            if (Trail.Count == 0) return "(空)";
            var n = Trail.Count;
            var parts = new List<string>();
            for (var i = 0; i < n && i < max; i++) parts.Add($"({Trail[i].x:0.00},{Trail[i].y:0.00})");
            var head = string.Join(" → ", parts);
            return n > max ? head + $" → …（共 {n} 点，末点 ({Trail[n - 1].x:0.00},{Trail[n - 1].y:0.00})）" : head;
        }
    }
}
