// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Monster/MonsterModule.cs
// `IMonsterModule` 的**唯一实现**（门面，internal，无参构造 ⇒ 可被 `AppContext.AutoWire` 反射创建）。
//
// 职责：刷怪（`MonsterSpawner`）→ 每帧推进 AI（`MonsterAi`）→ 受击/死亡/尸体 → 对外的只读状态。
//
//   · **不自己实现寻路**：全部走 `IMapModule.FindPath`（`MonsterAi` 里做路径节流）。
//   · **伤害只算一次**：本模块**只**负责"抗性结算 + 扣血 + 死亡"，命中率与基础伤害在
//     `Module/Combat`（`DamageFormula` / `DamagePipeline`）。本模块被 `ICombatModule` 调用，不反向调用它。
//   · **三件套的③（头顶血条下降）**：扣血后**同一次调用栈**里调 `IViewModule.UpdateMonster(state)`
//     ⇒ 血条与扣血同帧成立（飘字/音效由 `DamagePipeline` 同一栈里做）。
//   · **区域归属**：`MonsterState`（冻结 DTO）没有区域字段 ⇒ 在本模块的运行时记录里带 `Area`，
//     供 `CountInArea`（任务判定）使用。
//   · **仇恨时效**：`MonsterAi` 内置遗忘/栓绳；玩家死亡或换区域时本模块**主动清仇恨**。
//   · 视觉、音效、掉落、经验**都不在这里**：视图走 `IViewModule`，音效走 `IAudioModule`，
//     经验/掉落走 `Events.MonsterKilled` → `Module/Combat/DeathFlow`。
//
// 单机：不碰引擎的网络类门面（`Game` 的 Net / Sync / Http 等，全为 null）。
// 随机一律注入式 `CloverEngine.Rng`（地图 seed 派生）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using AppContext = Diablo2.App.AppContext;

namespace Diablo2.Module.Monster
{
    /// <summary>怪物门面实现（生成 / AI / 精英词缀 / 受击 / 尸体）。</summary>
    internal sealed class MonsterModule : IMonsterModule
    {
        /// <summary>刷怪随机流 salt（与战斗/掉落流分开，保证"同 seed ⇒ 同刷怪"）。</summary>
        private const int SpawnSeedSalt = 0x0A0;

        private readonly List<MonsterRuntime> _all = new List<MonsterRuntime>();
        private readonly MonsterSpawner _spawner = new MonsterSpawner();
        private readonly HashSet<AreaId> _spawnedAreas = new HashSet<AreaId>();

        private List<MonsterState> _statesCache;

        private int _nextId = GameConst.MonsterIdBase;
        private int _hoveredId = -1;
        private float _corpseSweepTimer = CorpseSweepIntervalSeconds;
        private Rng _rng;

        /// <summary>尸体扫描间隔（秒）——不是每帧扫，避免无谓开销。</summary>
        private const float CorpseSweepIntervalSeconds = 1f;

        /// <summary>构造即订阅：玩家死亡 / 区域切换 **必须**清仇恨（不给"隔着屏幕还在追"留机会）。</summary>
        public MonsterModule()
        {
            if (Game.Event == null)
            {
                MonsterLog.Error("MonsterModule 构造时 Game.Event 为 null（Game.Launch 未调用？）" +
                                 "⇒ 玩家死亡/换区域时不会自动清仇恨");
                return;
            }

            Game.Event.On(Events.PlayerDied, OnPlayerDied);
            Game.Event.On<AreaId>(Events.AreaChanged, OnAreaChanged);
        }

        // ═════════════════════════════════════════════════════════════════════
        // IMonsterModule：只读状态
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public int AliveCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < _all.Count; i++)
                {
                    if (_all[i].State.alive) n++;
                }
                return n;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<MonsterState> All
        {
            get
            {
                if (_statesCache == null)
                {
                    _statesCache = new List<MonsterState>(_all.Count);
                    for (var i = 0; i < _all.Count; i++) _statesCache.Add(_all[i].State);
                }
                return _statesCache;
            }
        }

        /// <inheritdoc />
        public int CountInArea(AreaId area)
        {
            var n = 0;
            for (var i = 0; i < _all.Count; i++)
            {
                var m = _all[i];
                if (m.State.alive && m.Area == area) n++;
            }

            if (n == 0 && !_spawnedAreas.Contains(area))
            {
                // 区分"这个区域本来就没怪"与"还没刷过"——任务计数若在进洞前查询，日志里能看出来
                MonsterLog.WarnOnce("area.notspawned." + (int)area,
                    $"CountInArea({area}) 返回 0，但该区域**尚未刷过怪**（已刷过的区域：{DescribeSpawnedAreas()}）" +
                    "⇒ 若任务计数异常，先确认是否已进过该区域");
            }
            return n;
        }

        /// <inheritdoc />
        public MonsterState Get(int monsterId)
        {
            var m = Find(monsterId);
            if (m != null) return m.State;

            MonsterLog.WarnThrottled("get.miss",
                $"Get({monsterId})：找不到该怪物（已回收 / 尸体超时 / id 非法）⇒ 返回 null");
            return null;
        }

        /// <inheritdoc />
        public bool IsAlive(int monsterId)
        {
            var m = Find(monsterId);
            return m != null && m.State.alive;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 刷怪 / 清场
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void SpawnArea(AreaId area)
        {
            DespawnAll();

            var map = Map;
            if (map == null || !map.IsGenerated)
            {
                MonsterLog.Error($"SpawnArea({area})：地图未就绪（AppContext.Map == null 或未 Generate）⇒ 本次不刷怪");
                return;
            }

            // 注意：`AreaId` 是 0 基、`level_c.id` 是 1 基（契约不一致）
            // ⇒ 一律经 `AreaLevelTable` 解析（两种口径都兼容，且用官方 LevelName 校验）
            var areaRow = AreaLevelTable.Resolve(area);
            if (areaRow == null)
            {
                MonsterLog.Error($"SpawnArea({area})：解析不到 level_c 行 ⇒ 本次不刷怪（配表链先跑通）");
                return;
            }

            var built = new List<MonsterRuntime>();
            _spawner.Build(this, area, SpawnRng(), built);

            for (var i = 0; i < built.Count; i++) _all.Add(built[i]);
            _statesCache = null;
            _spawnedAreas.Add(area);

            // 造视图 + 发事件（`MonsterSpawned` 给 HUD/任务等 UI 侧用；视图走接口直调，避免事件顺序歧义）
            var view = ViewRef;
            for (var i = 0; i < _all.Count; i++)
            {
                var m = _all[i];
                if (view != null) view.CreateMonster(m.State);
                if (Game.Event != null) Game.Event.Emit(Events.MonsterSpawned, m.State);
            }

            MonsterLog.Info($"SpawnArea({area}={areaRow.Name})：本区域共 {_all.Count} 只（存活 {AliveCount}）；" +
                            $"地图 {map.Width}x{map.Height} seed={map.Seed} 刷新点来源=" +
                            $"{(map.MonsterSpawns.Count > 0 ? "IMapModule.MonsterSpawns（洞穴）" : "RandomWalkableTile（野外）")}");
        }

        /// <inheritdoc />
        public void DespawnAll()
        {
            if (_all.Count == 0)
            {
                _hoveredId = -1;
                return;
            }

            var view = ViewRef;
            for (var i = 0; i < _all.Count; i++)
            {
                if (view != null) view.RemoveMonster(_all[i].State.id);
            }

            var n = _all.Count;
            _all.Clear();
            _statesCache = null;
            _hoveredId = -1;
            MonsterLog.Info($"DespawnAll：清空 {n} 只怪物（含尸体）与其视图");
        }

        /// <inheritdoc />
        public void RemoveCorpse(int monsterId)
        {
            for (var i = 0; i < _all.Count; i++)
            {
                if (_all[i].State.id != monsterId) continue;

                if (_all[i].State.alive)
                {
                    MonsterLog.Warn($"RemoveCorpse({monsterId})：该怪还活着（alive=true）⇒ 拒绝移除（不是尸体）");
                    return;
                }

                var view = ViewRef;
                if (view != null) view.RemoveMonster(monsterId);
                _all.RemoveAt(i);
                _statesCache = null;
                MonsterLog.Info($"RemoveCorpse：m#{monsterId} 的尸体已回收（剩余 {_all.Count} 条记录）");
                return;
            }

            MonsterLog.WarnThrottled("corpse.miss", $"RemoveCorpse({monsterId})：找不到该怪物 ⇒ 忽略");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 推进
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void Tick(float dt)
        {
            if (dt <= 0f) return;

            _corpseSweepTimer -= dt;
            if (_corpseSweepTimer <= 0f)
            {
                _corpseSweepTimer = CorpseSweepIntervalSeconds;
                SweepCorpses();
            }

            if (_all.Count == 0) return;

            var view = ViewRef;
            for (var i = 0; i < _all.Count; i++)
            {
                var m = _all[i];

                //   必须放在 `alive` 判断**之前**：尸体的死亡音正是靠这条播出去的 ——
                if (m.PendingHitSfx != null)
                {
                    m.PendingHitSfxTimer -= dt;
                    if (m.PendingHitSfxTimer <= 0f)
                    {
                        PlayMonsterSfx(m, m.PendingHitSfx);
                        m.PendingHitSfx = null;
                        m.PendingHitSfxTimer = 0f;
                    }
                }

                if (!m.State.alive)
                {
                    m.CorpseTimer -= dt;         // 尸体只倒计时（回收交 SweepCorpses）
                    continue;
                }

                var posBefore = m.Pos;
                MonsterAi.Step(this, m, dt);

                if (posBefore != m.Pos) m.ViewDirty = true;   // Vector2 的 != 是近似比较，够用

                m.Sync();

                //   口径 = **每走 `1/FsCnt` 格出一次**（`MonSsounds.FsCnt` 有脚步的 6 类全是 2
                //   ⇒ **半格一步**，不是跨格一次）—— 判据是**累计位移**而不是"格号变了没"，
                //   这样慢速怪也不会把一步拖成一格。
                //   原版没有移动音的类（尖刺鼠 / 幽灵：`FsCnt` 列为空）⇒ 一次都不响。
                var stepKey = MonsterSfx.StepOf(m.State);
                var period = MonsterSfx.StepPeriodTiles(m.State);
                if (stepKey != null && period > 0f)
                {
                    m.StepAccum += Vector2.Distance(posBefore, m.Pos);
                    if (m.StepAccum >= period)
                    {
                        m.StepAccum -= period;
                        if (m.StepAccum < 0f) m.StepAccum = 0f;      // 别让误差堆积成"抢跑"
                        PlayMonsterSfx(m, stepKey);
                    }
                }


                if (m.ViewDirty)
                {
                    m.ViewDirty = false;
                    if (view != null) view.UpdateMonster(m.State);
                }
            }
        }

        /// <summary>回收超时的尸体（`MonsterTuning.CorpseLifetimeSeconds` 之后萨满就不能再复活了）。</summary>
        private void SweepCorpses()
        {
            for (var i = _all.Count - 1; i >= 0; i--)
            {
                var m = _all[i];
                if (m.State.alive || m.CorpseTimer > 0f) continue;

                var view = ViewRef;
                if (view != null) view.RemoveMonster(m.State.id);
                _all.RemoveAt(i);
                _statesCache = null;
                MonsterLog.Info($"尸体超时回收：m#{m.State.id} {m.State.name}" +
                                $"（保留 {MonsterTuning.CorpseLifetimeSeconds:0}s 后清除）");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 受击 / 死亡
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void ApplyDamage(int monsterId, int amount, DamageType type)
        {
            var m = Find(monsterId);
            if (m == null)
            {
                MonsterLog.WarnThrottled("dmg.miss", $"ApplyDamage({monsterId})：找不到该怪物 ⇒ 本次伤害丢弃");
                return;
            }
            if (!m.State.alive)
            {
                MonsterLog.WarnThrottled("dmg.corpse",
                    $"ApplyDamage({monsterId})：目标已是尸体（alive=false）⇒ 本次伤害丢弃（不做尸体伤害）");
                return;
            }

            // ── 抗性结算（官方：实际伤害 = 伤害 × (1 - 抗性/100)，抗性夹在 [-100, 75]）──
            var resist = m.ResistOf(type);
            var final = Combat.DamageFormula.ApplyResist(amount, resist);

            var hpBefore = m.State.hp;
            m.State.hp = hpBefore - final;
            if (m.State.hp < 0) m.State.hp = 0;

            m.HitStunTimer = MonsterTuning.HitStunSeconds;   // 受击硬直（AI 本 tick 起不再动/出手）
            m.AttackAnimTimer = 0f;
            m.ViewDirty = true;
            m.Sync();

            // 三件套的 ③：**同一次调用栈**里刷新视图 ⇒ 头顶血条与这次扣血同帧下降
            var view = ViewRef;
            if (view != null) view.UpdateMonster(m.State);

            MonsterLog.Info($"hurt m#{monsterId} {m.State.name}：raw={amount} {type} 抗性={resist}% " +
                            $"⇒ 实扣={final}，hp {hpBefore}/{m.State.maxHp} → {m.State.hp}/{m.State.maxHp}");

            if (Game.Event != null) Game.Event.Emit(Events.MonsterChanged, m.State);

            //   延迟 = 原版 `HitDelay` **帧** ÷ `MonsterTuning.LogicFps`（出处见 `MonsterSfx.Timing`）。
            //   撞击音（`Combat.SfxKeys.Hit`）在 `DamagePipeline` 里**同帧**响，两音按原版错开。
            //   击杀那一下不排：`DamagePipeline` 播的是**死亡音**，受击音不该再叠一次。
            if (m.State.hp > 0)
            {
                var ownHit = MonsterSfx.HitOf(m.State);
                if (ownHit != null)
                {
                    var delay = MonsterSfx.HitDelaySeconds(m.State);
                    if (delay <= 0f) PlayMonsterSfx(m, ownHit);
                    else
                    {
                        m.PendingHitSfx = ownHit;
                        m.PendingHitSfxTimer = delay;
                    }
                }
            }

            if (m.State.hp <= 0) Die(m);
        }

        /// <summary>死亡：**保留尸体**（`corpseUsable` 来自 `monster_c.corpse_usable`，供萨满复活），发击杀事件。</summary>
        private void Die(MonsterRuntime m)
        {
            var s = m.State;

            s.alive = false;
            s.corpseUsable = m.Row != null && m.Row.CorpseUsable != 0;
            s.attacking = false;
            s.hitStun = false;

            m.HitStunTimer = 0f;
            m.AttackAnimTimer = 0f;
            m.Engaged = false;
            m.Returning = false;
            m.FleeTimer = 0f;
            m.ClearPath();
            m.CorpseTimer = MonsterTuning.CorpseLifetimeSeconds;

            //   用的是**与受击音同一套**机制（`PendingHitSfx` / `PendingHitSfxTimer`）—— 不新造排期。
            //   `ApplyDamage` 在击杀那一下不会排受击音 ⇒ 这两个槽位此刻必为空，不会互相顶掉。
            //   延迟同样 = 帧 ÷ `MonsterTuning.LogicFps`（出处见 `MonsterSfx.Timing`）。
            var dieKey = MonsterSfx.DieOf(s) ?? Combat.SfxKeys.MonsterDie;
            var dieDelay = MonsterSfx.DeathDelaySeconds(s);
            if (dieDelay <= 0f) PlayMonsterSfx(m, dieKey);
            else
            {
                m.PendingHitSfx = dieKey;
                m.PendingHitSfxTimer = dieDelay;
            }
            m.ViewDirty = true;
            m.Sync();

            var view = ViewRef;
            if (view != null)
            {
                view.UpdateMonster(s);      // 先落到 0 血（血条见底）
                view.PlayDeath(s.id);       // 再播死亡表现（Death 动画后隐藏，**保留尸体节点**）
            }

            MonsterLog.Info($"m#{s.id} {s.name} 死亡（等级 {s.level}，经验 {s.exp}，格 ({s.gridX},{s.gridY})，" +
                            $"尸体保留 {MonsterTuning.CorpseLifetimeSeconds:0}s，corpseUsable={s.corpseUsable}）");

            if (Game.Event == null) return;
            Game.Event.Emit(Events.MonsterChanged, s);
            Game.Event.Emit(Events.MonsterKilled, s.id);     // → Combat/DeathFlow：经验结算 + 掉落触发
        }

        /// <inheritdoc />
        public void NotifyAttacked(int monsterId)
        {
            var m = Find(monsterId);
            if (m == null)
            {
                MonsterLog.WarnThrottled("aggro.miss", $"NotifyAttacked({monsterId})：找不到该怪物 ⇒ 忽略");
                return;
            }

            if (!m.State.alive)
            {
                MonsterLog.WarnThrottled("aggro.corpse", $"NotifyAttacked({monsterId})：目标已是尸体 ⇒ 忽略");
                return;
            }

            if (!m.Engaged)
            {
                MonsterLog.Info($"aggro(被打) m#{monsterId} {m.State.name}：被玩家攻击 ⇒ 转入仇恨");
            }
            m.Engaged = true;
            m.Returning = false;
            m.AggroMemory = MonsterTuning.AggroMemorySeconds;
            m.FleeTimer = 0f;                 // 挨打后不再逃（原版行为：被打就回头）
        }

        /// <inheritdoc />
        public void SetHovered(int monsterId)
        {
            // 契约原文：「不做 AI 决策，仅更新"当前悬停"」。本模块把它记下来供自证/调试读取。
            _hoveredId = monsterId;
        }

        /// <inheritdoc />
        public bool ConsumeCorpse(int monsterId)
        {
            var m = Find(monsterId);
            if (m == null)
            {
                MonsterLog.WarnThrottled("consume.miss", $"ConsumeCorpse({monsterId})：找不到该怪物 ⇒ false");
                return false;
            }
            if (m.State.alive)
            {
                MonsterLog.WarnThrottled("consume.alive", $"ConsumeCorpse({monsterId})：目标还活着 ⇒ false");
                return false;
            }
            if (!m.State.corpseUsable)
            {
                MonsterLog.WarnThrottled("consume.used",
                    $"ConsumeCorpse({monsterId})：该尸体不可用/已被利用（corpseUsable=false）⇒ false");
                return false;
            }

            m.State.corpseUsable = false;
            MonsterLog.Info($"ConsumeCorpse：m#{monsterId} {m.State.name} 的尸体已被利用（corpseUsable → false）");
            return true;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 给 MonsterAi / MonsterSpawner 用的模块内入口
        // （都在 Module/Monster 内，避免跨模块具体类型耦合）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>当前地图（`AppContext` 注入；未接入 ⇒ null）。</summary>
        internal IMapModule Map
        {
            get { return AppContext.I != null ? AppContext.I.Map : null; }
        }

        /// <summary>玩家模块（`AppContext` 注入；未接入 ⇒ null）。</summary>
        internal IPlayerModule Player
        {
            get { return AppContext.I != null ? AppContext.I.Player : null; }
        }

        /// <summary>视图模块（`AppContext` 注入；未接入 ⇒ null）。</summary>
        private IViewModule ViewRef
        {
            get { return AppContext.I != null ? AppContext.I.View : null; }
        }

        /// <summary>玩家当前格（玩家未接入时退回地图出生点）。</summary>
        internal Vector2Int PlayerGrid
        {
            get
            {
                var p = Player;
                if (p != null) return p.Grid;
                var map = Map;
                return map != null ? map.SpawnPoint : Vector2Int.zero;
            }
        }

        /// <summary>该怪的移动速度（格/秒）——`monster_c.speed`（官方 `Velocity`）× 换算系数。</summary>
        internal float SpeedOf(MonsterRuntime m)
        {
            if (m == null || m.Row == null) return MonsterTuning.MinMoveSpeed;
            var v = m.Row.Speed * MonsterTuning.SpeedToTilesPerSecond;
            return v < MonsterTuning.MinMoveSpeed ? MonsterTuning.MinMoveSpeed : v;
        }

        /// <summary>分配一个新的怪物实体 id（`GameConst.MonsterIdBase` 起递增）。</summary>
        internal int NextMonsterId()
        {
            return _nextId++;
        }

        /// <summary>
        /// 起播一条怪物音效（受击 / 脚步 / 死亡同走这里；`IAudioModule` 可空 ⇒ 跳过）。
        /// <para>为什么收成一个出口：`AudioModule` 侧已有**同键节流**（`Module/Audio/SfxThrottle.cs`），
        ///   这里只负责"到点就请求一次"，不做去重（去重归接收侧）。</para>
        /// </summary>
        private static void PlayMonsterSfx(MonsterRuntime m, string key)
        {
            if (m == null || string.IsNullOrEmpty(key)) return;
            var ctx = AppContext.I;
            var audio = ctx != null ? ctx.Audio : null;
            if (audio == null) return;
            audio.SfxAt(key, m.State.worldX, m.State.worldY, m.State.worldZ);
        }

        /// <summary>怪物出手：把命中/伤害结算交给 `ICombatModule`（**本模块不自己算命中**）。</summary>
        internal void RequestMonsterAttack(MonsterRuntime m)
        {
            var ctx = AppContext.I;
            var combat = ctx != null ? ctx.Combat : null;
            if (combat == null)
            {
                MonsterLog.WarnOnce("attack.nocombat",
                    "RequestMonsterAttack: ICombatModule 未接入（AppContext.Combat == null）⇒ 怪物打不出伤害");
                return;
            }

            var audio = ctx.Audio;
            if (audio != null)
            {
                //   未登记的类别回落到通用键 `MonsterAttack`（素材源 = 堕落者），
                //   并由 `MonsterSfx` 打一次 Warn 留痕（不用别的怪的叫声顶替）。
                audio.SfxAt(MonsterSfx.AttackOf(m.State) ?? Combat.SfxKeys.MonsterAttack,
                    m.State.worldX, m.State.worldY, m.State.worldZ);
            }

            combat.RequestMonsterAttack(m.State.id);
        }

        /// <summary>找一具**可复活**的同伴尸体（最近的一具；超出 `ShamanReviveRange` 返回 -1）。</summary>
        internal int FindRevivableCorpse(MonsterRuntime shaman)
        {
            var bestId = -1;
            var bestDist = MonsterTuning.ShamanReviveRange;

            for (var i = 0; i < _all.Count; i++)
            {
                var c = _all[i];
                if (c == shaman) continue;
                if (c.State.alive || !c.State.corpseUsable) continue;

                var d = Vector2.Distance(shaman.Pos, c.Pos);
                if (d > bestDist) continue;
                bestDist = d;
                bestId = c.State.id;
            }
            return bestId;
        }

        /// <summary>
        /// 萨满复活同伴：吃尸体（`ConsumeCorpse`）→ 半血复活 → 立刻转入仇恨。
        /// <para>日志固定包含 `[Monster] revive`（验收要 grep 这一行）。</para>
        /// </summary>
        internal bool ReviveMonster(int corpseId, MonsterRuntime shaman)
        {
            var c = Find(corpseId);
            if (c == null)
            {
                MonsterLog.WarnThrottled("revive.miss", $"ReviveMonster({corpseId})：找不到尸体 ⇒ false");
                return false;
            }
            if (c.State.alive)
            {
                MonsterLog.WarnThrottled("revive.alive", $"ReviveMonster({corpseId})：目标还活着 ⇒ false");
                return false;
            }
            if (!ConsumeCorpse(corpseId)) return false;

            var hp = Mathf.Max(1, Mathf.RoundToInt(c.State.maxHp * MonsterTuning.ReviveHpRatio));
            c.State.hp = hp;
            c.State.alive = true;
            c.State.hitStun = false;
            c.State.attacking = false;
            c.CorpseTimer = 0f;
            c.Engaged = true;
            c.Returning = false;
            c.AggroMemory = MonsterTuning.AggroMemorySeconds;
            c.FleeTimer = 0f;
            c.HitStunTimer = 0f;
            c.ClearPath();               // 回到 AI 控制（位置仍是尸体所在格，不需要传送到别处）
            c.ViewDirty = true;
            c.Sync();

            var view = ViewRef;
            if (view != null) view.UpdateMonster(c.State);

            var ctx = AppContext.I;
            var audio = ctx != null ? ctx.Audio : null;
            if (audio != null) audio.SfxAt(Combat.SfxKeys.MonsterRevive, c.State.worldX, c.State.worldY, c.State.worldZ);

            MonsterLog.Info($"revive m#{corpseId} {c.State.name} 被萨满 m#{shaman.State.id} 复活：" +
                            $"hp {hp}/{c.State.maxHp}（{MonsterTuning.ReviveHpRatio * 100f:0}%）位置=({c.State.gridX},{c.State.gridY})");

            if (Game.Event != null) Game.Event.Emit(Events.MonsterChanged, c.State);
            return true;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 事件 / 随机 / 小工具
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>玩家死亡 ⇒ 全场脱战（契约要求：玩家死亡时清仇恨）。</summary>
        private void OnPlayerDied()
        {
            var n = 0;
            for (var i = 0; i < _all.Count; i++)
            {
                var m = _all[i];
                if (!m.State.alive || !m.Engaged) continue;
                m.Engaged = false;
                m.Returning = true;
                m.AggroMemory = 0f;
                m.ClearPath();
                n++;
            }
            MonsterLog.Info($"玩家死亡 ⇒ 清空 {n} 只怪的仇恨（脱战回原位），其余 {AliveCount} 只怪保持巡逻");
        }

        private void OnAreaChanged(AreaId area)
        {
            var n = 0;
            for (var i = 0; i < _all.Count; i++)
            {
                var m = _all[i];
                if (!m.Engaged) continue;
                m.Engaged = false;
                m.Returning = false;
                m.AggroMemory = 0f;
                m.ClearPath();
                n++;
            }
            MonsterLog.Info($"区域切换为 {area} ⇒ 清空 {n} 只怪的仇恨与路径（避免拿着旧地图的路径走）");
        }

        /// <summary>刷怪随机流：由 `IMapModule.Seed` 派生（**同 seed ⇒ 同刷怪**）。</summary>
        private Rng SpawnRng()
        {
            if (_rng != null) return _rng;

            var map = Map;
            if (map != null && map.IsGenerated)
            {
                _rng = new Rng(map.Seed ^ SpawnSeedSalt);
            }
            else
            {
                _rng = Rng.FromTime();
                MonsterLog.WarnOnce("spawn.rng.fallback",
                    "MonsterModule: 地图未生成（拿不到 seed）⇒ 刷怪随机改用时钟 seed（本局刷怪不可复现）");
            }
            return _rng;
        }

        /// <summary>按 id 找运行时记录（怪物数量是几十级，线性扫足够且不会有索引失效 bug）。</summary>
        private MonsterRuntime Find(int monsterId)
        {
            for (var i = 0; i < _all.Count; i++)
            {
                if (_all[i].State.id == monsterId) return _all[i];
            }
            return null;
        }

        private string DescribeSpawnedAreas()
        {
            if (_spawnedAreas.Count == 0) return "(无)";
            var parts = new List<string>();
            foreach (var a in _spawnedAreas) parts.Add(a.ToString());
            return string.Join("、", parts);
        }

        /// <summary>自证用：一行状态摘要（不在契约里，供 `tools/probes/hosts/combatcheck` 打印）。</summary>
        internal string DumpStats()
        {
            var alive = AliveCount;
            var corpses = 0;
            var elite = 0;
            var engaged = 0;
            for (var i = 0; i < _all.Count; i++)
            {
                var m = _all[i];
                if (!m.State.alive) corpses++;
                if (m.State.isChampion) elite++;
                if (m.Engaged) engaged++;
            }
            return $"怪物统计：总 {_all.Count}（存活 {alive} / 尸体 {corpses} / 精英 {elite} / 交战中 {engaged}），" +
                   $"已刷过区域 {DescribeSpawnedAreas()}，下一个 id={_nextId}，悬停={_hoveredId}";
        }
    }
}
