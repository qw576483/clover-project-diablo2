// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Player/PlayerModule.cs
// `IPlayerModule` 的**唯一实现**（门面，internal；`App/AppContext.AutoWire` 反射创建 ⇒ 无参构造）。
//
// 组成：`PlayerStats`（四维/派生）+ `PlayerMotor`（点击移动）+ `InputReader`（唯一读输入处）。
// 位置**由本模块本地解算**（原版点击移动语义），不被任何外部权威位置逐帧覆盖（见 `IPlayerModule` 注释）。
//
// ── 事件（只发/收 `Core/Events.cs` 的常量，禁止裸字符串）─────────────────────
//   收：`EquipChanged`（装备生效 → 重算派生）、`StatAllocateRequest`（人物面板加点）
//   发：`HudDirty` / `PlayerStatsChanged`（数值变化）、`LevelUp`、`PlayerDied`、
//       `PlayerGridChanged`（小地图揭迷雾 / 已探索）、`ExitEntered`（踩到出入口 → Flow 切区域）、
//       `MoveCommand`（移动意图，契约 §3.5）、`AttackRequest`（左键点怪 → 交 `ICombatModule` 结算）
//   不发：`PlayerDamaged` / `DamageDealt`（属 `ICombatModule` 的结算产物）、
//         `ReviveRequest`（`ICombatModule.RevivePlayer()` 的入口，Player 不重复订阅）
//
// ── 交互（`docs/agents/agent-13-修复轮.md` §A；**补上 `AttackRequest` 的发送方**）──────
//   原版左键语义：点怪 → `Emit(AttackRequest, id)`（并走过去）；点空地/物品/NPC → `MoveCommand` 意图。
//   `Shift` 按住 = **站立攻击**（只发 `AttackRequest`，不产生移动目标）。
//   走位说明：`ICombatModule.RequestAttack` 超距时**不结算也不走位**（只 Warn「先靠近」）⇒
//   由本模块在发 `AttackRequest` 的**同一帧**补发一条 `MoveCommand`（走向怪物相邻可走格）。
//   拉不到怪物 id 时**不改契约、不改 App**：经组合根 `AppContext.I.Monster`（**接口**，非 `Module.Monster`
//   实现类型）读取 —— 详见 `Module/Input/HoverPicker.cs` 头注释（分层 ②/③ 仍 0 命中）。
//
// ── 走 / 跑切换（原版 R 键，§A 第 5 项）──────────────────────────────────────
//   `_running` 缩放 `PlayerMotor.SpeedScale`（跑 = `GameConst.PlayerWalkSpeed`，
//   走 = `GameConst.PlayerWalkSpeedFactor` = **原版 7/15**，见该常量注释），
//   并 `Emit(HudDirty)`（HUD 侧只切贴图，见 `UI/HudPanel.cs` 的 runbutton）。
//   ★ 片 2b：`_running` 同时经契约 `IPlayerModule.IsRunning` 暴露给表现层
//     （`Module/View/ViewModule` 据此选 `ViewAnim.Run` / `ViewAnim.Walk`）。
//
// ── 契约歧义（**已回报主 agent，未擅自改契约**）─────────────────────────────
//   `IPlayerModule.ApplyDamage` 注释写「走抗性/防御结算」，但 `ICombatModule` 又明确
//   「命中/伤害/**抗性**结算只经本门面，禁止各自算一遍」⇒ 取后者：本方法把入参当作
//   **已结算的最终扣血量**（只做钳制/死亡判定），否则抗性会被减两次。
//   抗性「值」仍由本模块提供（`GetResist`），Combat 用它结算。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
// ★ agent-33 引擎下沉 A2：引擎侧新增了**同名**枚举 `CloverEngine.Dir8`（`Runtime/Core/Dir8.cs`），
//   本文件同时 `using CloverEngine;` ⇒ 裸 `Dir8` 会变成 CS0104 二义。
//   用别名把裸 `Dir8` 钉死为**项目枚举**（语义与序号和改动前**完全一致**）。
using Dir8 = Diablo2.Def.Dir8;
using UnityEngine;
// 别名：`AppContext` 与 BCL 的 `System.AppContext` 同名（同时 using System 会 CS0104）。
using AppContext = Diablo2.App.AppContext;

namespace Diablo2.Module.Player
{
    /// <summary>主角门面实现（属性 / 移动 / 死亡复活 / 经验升级）。</summary>
    internal sealed class PlayerModule : IPlayerModule
    {
        /// <summary>等级上限（官方 `experience.txt` 的 `MaxLvl` 行 = 99，见 `docs/配表说明.md` §2）。</summary>
        public const int MaxLevel = 99;

        /// <summary>升一级给的技能点（原版：每级 +1；任务奖励另算，见 `IQuestModule`）。</summary>
        public const int SkillPointsPerLevel = 1;

        /// <summary>
        /// 「走」的速度倍率。
        /// <para>★ 片 2b：**值已迁到** `GameConst.PlayerWalkSpeedFactor`（= 原版 `walkSpeed 7 / runSpeed 15`；
        /// 旧值 0.5「走 ≈ 跑的一半」**无出处**）。这里保留同名常量只为兼容既有引用方
        /// （离线宿主 `tools/playercheck/Program.cs` 读它），**不含第二份字面量**。</para>
        /// </summary>
        public const float WalkSpeedFactor = GameConst.PlayerWalkSpeedFactor;

        /// <summary>无效格（用于「还没发过」的标记，避免和 (0,0) 混淆）。</summary>
        private static readonly Vector2Int NoGrid = new Vector2Int(int.MinValue, int.MinValue);

        private readonly PlayerStats _stats = new PlayerStats();
        private readonly PlayerMotor _motor = new PlayerMotor();
        private readonly InputReader _input = new InputReader();

        private string _name = "(未创建)";
        private long _exp;
        private int _statPoints;
        private int _skillPoints;
        private int _gold;
        private int _life;
        private int _mana;
        private int _stamina;
        private bool _dead;
        private bool _created;

        private IMapModule _mapOverride;          // 非契约入口（集成/离线自检）注入的地图
        private Vector2Int? _holdTarget;          // 按住左键时上一次下发的目标格
        private bool _running = true;             // 跑/走切换（原版默认跑；R 键切换）
        private int _lastAttackTargetId = int.MinValue;   // 上一次打日志的攻击目标（防按住时刷屏）
        private Vector2Int _lastExitGrid = NoGrid;
        private bool _unreachableLogged;
        private bool _mapWarned;
        private bool _ctxWarned;
        private bool _noMapGeneratedWarned;
        private bool _notCreatedWarned;
        private bool _deadMoveWarned;
        private bool _selfHealLogged;
        private bool _equipSubscribed;

        /// <summary>
        /// 构造即订阅事件（`Game.Event` 没有句柄，注销必须用**同一个方法引用**）。
        /// 引擎未启动（`Game.Event == null`）时只打 Error 并降级，不抛异常
        /// （与 `Module/Flow/AppFlow` 同一处置）。
        /// </summary>
        public PlayerModule()
        {
            if (Game.Event == null)
            {
                PlayerLog.Error("PlayerModule 构造时 Game.Event 为 null（Game.Launch 未调用）⇒ " +
                                "装备生效 / 属性加点订阅未注册，相关功能降级");
                return;
            }

            Game.Event.On<InventoryChangedArgs>(Events.EquipChanged, OnEquipChanged);
            Game.Event.On<StatAllocArgs>(Events.StatAllocateRequest, OnStatAllocateRequest);
            // 契约 §3.5 的「点击移动命令」：**唯一的移动命令通道**（InputReader 点击 → 本事件 → MoveTo）。
            // Combat / UI 也能借此让角色走位（例如「点怪 → 走过去」）；`MoveTo` 本身**不再回发**本事件（防环）。
            Game.Event.On<Vector2Int>(Events.MoveCommand, OnMoveCommand);
            _equipSubscribed = true;
            PlayerLog.Info($"PlayerModule 已装配：订阅 {Events.EquipChanged} / {Events.StatAllocateRequest} / " +
                           $"{Events.MoveCommand}，派生属性公式见 PlayerStats.Recompute");
        }

        // ═════════════════════════════════════════════════════════════════════
        // IPlayerModule：身份与数值
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public PlayerClass Class => _stats.Cls;

        /// <inheritdoc />
        public string Name => _name;

        /// <inheritdoc />
        public int Level => _stats.Level;

        /// <inheritdoc />
        public int Str => _stats.Str;

        /// <inheritdoc />
        public int Dex => _stats.Dex;

        /// <inheritdoc />
        public int Vit => _stats.Vit;

        /// <inheritdoc />
        public int Eng => _stats.Eng;

        /// <inheritdoc />
        public int Life => _life;

        /// <inheritdoc />
        public int MaxLife => _stats.MaxLife;

        /// <inheritdoc />
        public int Mana => _mana;

        /// <inheritdoc />
        public int MaxMana => _stats.MaxMana;

        /// <inheritdoc />
        public int Stamina => _stamina;

        /// <inheritdoc />
        public int MaxStamina => _stats.MaxStamina;

        /// <inheritdoc />
        public long Exp => _exp;

        /// <inheritdoc />
        public long ExpNext => ExpAt(_stats.Level);

        /// <inheritdoc />
        public int StatPoints => _statPoints;

        /// <inheritdoc />
        public int SkillPoints => _skillPoints;

        /// <inheritdoc />
        public int Gold => _gold;

        /// <inheritdoc />
        public Vector2Int Grid => _motor.Grid;

        /// <inheritdoc />
        public Vector3 World => _motor.World;

        /// <inheritdoc />
        public Dir8 Dir => _motor.Dir;

        /// <inheritdoc />
        public bool IsMoving => _motor.IsMoving;

        /// <summary>
        /// ★ 片 2b 契约成员（`IPlayerModule.IsRunning`）：原版走/跑状态，供表现层选
        /// `ViewAnim.Run` / `ViewAnim.Walk`。实现直接用已有的 `_running`（默认跑，原版 R 键切换）。
        /// </summary>
        public bool IsRunning => _running;

        /// <inheritdoc />
        public bool IsDead => _dead;

        /// <inheritdoc />
        public int Defense => _stats.Defense;

        /// <inheritdoc />
        public int AttackRating => _stats.AttackRating;

        /// <inheritdoc />
        public int GetResist(DamageType type) => _stats.ResistOf(type);

        /// <inheritdoc />
        public PlayerStatsDto Snapshot()
        {
            var dto = new PlayerStatsDto
            {
                name = _name,
                cls = _stats.Cls,
                level = _stats.Level,
                exp = _exp,
                expNext = ExpNext,
                str = _stats.Str,
                dex = _stats.Dex,
                vit = _stats.Vit,
                eng = _stats.Eng,
                life = _life,
                maxLife = _stats.MaxLife,
                mana = _mana,
                maxMana = _stats.MaxMana,
                stamina = _stamina,
                maxStamina = _stats.MaxStamina,
                defense = _stats.Defense,
                attackRating = _stats.AttackRating,
                blockChance = _stats.BlockChance,
                fireResist = _stats.ResistOf(DamageType.Fire),
                coldResist = _stats.ResistOf(DamageType.Cold),
                lightResist = _stats.ResistOf(DamageType.Lightning),
                poisonResist = _stats.ResistOf(DamageType.Poison),
                statPoints = _statPoints,
                skillPoints = _skillPoints,
                gold = _gold,
                // 右键技能属 `ISkillModule`（Player 不持有技能状态）⇒ 默认 -1（普通攻击），
                // HUD/技能面板由 Skill 模块补齐；腰带数量由 Item 模块补齐（见回报「未决」）。
                selectedSkillId = -1,
            };

            for (var i = 0; i < GameConst.BeltSlots; i++) dto.beltCounts.Add(0);

            return dto;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 生命周期：建角 / 读档 / 写档 / 复位
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void CreateNew(PlayerClass cls, string name)
        {
            _name = string.IsNullOrWhiteSpace(name) ? PlayerLog.FallbackName : name.Trim();
            if (string.IsNullOrWhiteSpace(name))
                PlayerLog.Warn($"CreateNew 收到空角色名 ⇒ 退回默认名「{PlayerLog.FallbackName}」（正常由创角屏保证非空）");

            _stats.Bind(cls);
            _stats.Level = 1;
            _stats.BaseStr = _stats.Row != null ? _stats.Row.Str : 0;
            _stats.BaseDex = _stats.Row != null ? _stats.Row.Dex : 0;
            _stats.BaseVit = _stats.Row != null ? _stats.Row.Vit : 0;
            _stats.BaseEng = _stats.Row != null ? _stats.Row.Eng : 0;
            _stats.ClearBonuses();
            _stats.Recompute();

            _exp = 0;
            _statPoints = _stats.Row != null ? _stats.Row.StatPerLvl : 0;
            _skillPoints = 0;
            _gold = 0;
            _life = _stats.MaxLife;
            _mana = _stats.MaxMana;
            _stamina = _stats.MaxStamina;
            _dead = false;
            _created = true;
            _running = true;                          // 原版默认跑
            _lastAttackTargetId = int.MinValue;
            _motor.Reset();                           // 同时把 SpeedScale 复位为 1（跑）
            _holdTarget = null;
            _lastExitGrid = NoGrid;
            _selfHealLogged = false;
            _input.Reset();

            PlayerLog.Info(
                $"[Create] {_name} 职业={cls}({(int)cls}) 等级 1 " +
                $"力{_stats.Str}/敏{_stats.Dex}/体{_stats.Vit}/精{_stats.Eng} " +
                $"生命={_life}/{_stats.MaxLife} 法力={_mana}/{_stats.MaxMana} 耐力={_stamina}/{_stats.MaxStamina} " +
                $"防御={_stats.Defense} AR={_stats.AttackRating} 可分配属性点={_statPoints} " +
                $"经验 0/{ExpNext}（升级阈值来自 experience_c）");
            EmitStats();
        }

        /// <inheritdoc />
        public void LoadFrom(CharacterSave save)
        {
            if (save == null)
            {
                PlayerLog.Error("LoadFrom(null) ⇒ 忽略（保持当前状态）");
                return;
            }

            _name = string.IsNullOrWhiteSpace(save.name) ? PlayerLog.FallbackName : save.name;
            _stats.Bind(save.cls);
            _stats.Level = save.level > 0 ? save.level : 1;
            _stats.BaseStr = save.str;
            _stats.BaseDex = save.dex;
            _stats.BaseVit = save.vit;
            _stats.BaseEng = save.eng;
            _stats.ClearBonuses();
            _stats.Recompute();

            _exp = save.exp;
            _statPoints = save.statPoints;
            _skillPoints = save.skillPoints;
            _gold = save.gold;
            _dead = false;
            _created = true;
            _running = true;                          // 原版默认跑（跑/走不在存档里）
            _lastAttackTargetId = int.MinValue;
            _motor.SpeedScale = 1f;
            _holdTarget = null;
            _lastExitGrid = NoGrid;
            _selfHealLogged = false;

            // 位置：存档格必须可走；不可走（或地图尚未生成）则退回出生点并 Warn。
            var map = MapOrNull();
            var saved = new Vector2Int(save.gridX, save.gridY);
            _motor.Teleport(saved, map);
            if (map != null && map.IsGenerated && _motor.Grid != saved)
                PlayerLog.Warn($"存档位置 ({saved.x},{saved.y}) 不可走 ⇒ 落在出生点 " +
                               $"({_motor.Grid.x},{_motor.Grid.y})（存档数据/地图 seed 不一致？）");

            // 当前值：存档里存了就沿用（钳到上限），没存（0）则满血
            _life = save.life > 0 ? Mathf.Min(save.life, _stats.MaxLife) : _stats.MaxLife;
            _mana = save.mana > 0 ? Mathf.Min(save.mana, _stats.MaxMana) : _stats.MaxMana;
            _stamina = save.stamina > 0 ? Mathf.Min(save.stamina, _stats.MaxStamina) : _stats.MaxStamina;

            PlayerLog.Info(
                $"[Load] {_name} 职业={_stats.Cls} 等级={_stats.Level} 经验={_exp}/{ExpNext} " +
                $"力{_stats.Str}/敏{_stats.Dex}/体{_stats.Vit}/精{_stats.Eng} " +
                $"生命={_life}/{_stats.MaxLife} 法力={_mana}/{_stats.MaxMana} 耐力={_stamina}/{_stats.MaxStamina} " +
                $"防御={_stats.Defense} AR={_stats.AttackRating} 金币={_gold} " +
                $"落位=({_motor.Grid.x},{_motor.Grid.y})（存档位置 ({saved.x},{saved.y})，seed={save.mapSeed}）");
            EmitStats();
        }

        /// <inheritdoc />
        public void WriteTo(CharacterSave save)
        {
            if (save == null)
            {
                PlayerLog.Error("WriteTo(null) ⇒ 忽略");
                return;
            }

            save.version = GameConst.SaveVersion;
            save.name = _name;
            save.cls = _stats.Cls;
            save.level = _stats.Level;
            save.exp = _exp;
            save.str = _stats.BaseStr;
            save.dex = _stats.BaseDex;
            save.vit = _stats.BaseVit;
            save.eng = _stats.BaseEng;
            save.life = _life;
            save.mana = _mana;
            save.stamina = _stamina;
            save.statPoints = _statPoints;
            save.skillPoints = _skillPoints;
            save.gold = _gold;
            save.gridX = _motor.Grid.x;
            save.gridY = _motor.Grid.y;
            // mapSeed / areaId / 背包 / 任务：分别由 Flow、Map、Item、Quest 负责，这里不碰。

            PlayerLog.Info($"写回存档：{_name} 等级={_stats.Level} 经验={_exp} 生命={_life}/{_stats.MaxLife} " +
                           $"格=({_motor.Grid.x},{_motor.Grid.y}) 金币={_gold}");
        }

        /// <inheritdoc />
        public void Reset()
        {
            _name = "(未创建)";
            _stats.Level = 1;
            _stats.BaseStr = _stats.BaseDex = _stats.BaseVit = _stats.BaseEng = 0;
            _stats.ClearBonuses();
            _stats.Recompute();
            _exp = 0;
            _statPoints = 0;
            _skillPoints = 0;
            _gold = 0;
            _life = _mana = _stamina = 0;
            _dead = false;
            _created = false;
            _running = true;
            _lastAttackTargetId = int.MinValue;
            _holdTarget = null;
            _lastExitGrid = NoGrid;
            _motor.Reset();
            _input.Reset();
            _unreachableLogged = false;
            _notCreatedWarned = false;
            _deadMoveWarned = false;
            _selfHealLogged = false;
            PlayerLog.Info("复位：角色/数值/路径/输入缓存已清空（回主菜单）");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 移动
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void MoveTo(Vector2Int target)
        {
            if (!_created)
            {
                if (!_notCreatedWarned)
                {
                    _notCreatedWarned = true;
                    PlayerLog.Warn($"MoveTo({target}) 时还没有角色（未 CreateNew/LoadFrom）⇒ 忽略（只报一次）");
                }
                return;
            }

            if (_dead)
            {
                if (!_deadMoveWarned)
                {
                    _deadMoveWarned = true;
                    PlayerLog.Warn($"已死亡状态下收到 MoveTo({target}) ⇒ 不移动（只报一次，等 Revive）");
                }
                return;
            }

            var map = MapOrNull();
            if (map == null || !map.IsGenerated)
            {
                if (!_noMapGeneratedWarned)
                {
                    _noMapGeneratedWarned = true;
                    PlayerLog.Warn($"MoveTo({target}) 时地图不可用（IMapModule 未接入或未 Generate）⇒ 不移动（只报一次）");
                }
                return;
            }

            var from = _motor.Grid;
            if (target == from)
            {
                _motor.Stop();
                PlayerLog.Move($"已在目标格 ({(from.x)},{from.y}) ⇒ 清空路径（原地不动）");
                return;
            }

            // 目标格不可走：先判，给出比 A* 更可定位的日志（A* 也会拦，但只有一行泛泛的 Warn）
            if (!map.Walkable(target))
            {
                UnreachableCount++;
                if (!_unreachableLogged)
                {
                    _unreachableLogged = true;      // 同一段「不可达」只报一次（按住左键会每帧请求）
                    PlayerLog.Warn($"[Move] unreachable to=({target.x},{target.y}) 地形={map.TileAt(target)}（不可走）" +
                                   $" 当前格=({from.x},{from.y}) ⇒ 不移动（同类再点不重复刷屏）");
                }
                return;
            }

            var path = map.FindPath(from, target);
            if (path == null || path.Count == 0)
            {
                UnreachableCount++;
                if (!_unreachableLogged)
                {
                    _unreachableLogged = true;
                    PlayerLog.Info($"[Move] unreachable from=({from.x},{from.y}) to=({target.x},{target.y}) " +
                                   "FindPath 返回 null/空（连通性问题？）⇒ 不移动（同类再点不重复刷屏）");
                }
                return;
            }

            if (!_motor.SetPath(path, target)) return;
            _unreachableLogged = false;
            PlayerLog.Move($"steps={path.Count} path={AStar.Describe(path)} " +
                           $"from=({from.x},{from.y}) to=({target.x},{target.y}) " +
                           $"speed={_motor.Speed}格/秒({(_running ? "跑" : "走")})");
        }

        /// <inheritdoc />
        public void Stop() => _motor.Stop();

        /// <inheritdoc />
        public void TeleportTo(Vector2Int grid)
        {
            var map = MapOrNull();
            _motor.Teleport(grid, map);
            _holdTarget = null;
            _lastExitGrid = NoGrid;
            _selfHealLogged = false;
            PlayerLog.Info($"落位：格=({_motor.Grid.x},{_motor.Grid.y}) 世界=({_motor.World.x:0.00},{_motor.World.y:0.00})");
            Emit(Events.PlayerGridChanged, _motor.Grid);
        }

        /// <inheritdoc />
        public void Tick(float dt)
        {
            // 暂停（`Time.timeScale = 0` ⇒ dt = 0）与非法 dt：不推进、不读输入、不刷日志。
            if (dt <= 0f) return;
            if (!_created) return;

            var map = MapOrNull();

            // ① 位置自愈：站在不可走格上（读档位置非法 / 地图换过）⇒ 回出生点，只报一次日志
            if (map != null && map.IsGenerated && !_motor.IsMoving && !map.Walkable(_motor.Grid))
            {
                var bad = _motor.Grid;
                if (!_selfHealLogged)
                {
                    _selfHealLogged = true;
                    PlayerLog.Warn($"当前格 ({bad.x},{bad.y}) 不可走（读档位置非法或地图已重生成？）" +
                                   $"⇒ 回出生点 ({map.SpawnPoint.x},{map.SpawnPoint.y})");
                }
                TeleportTo(map.SpawnPoint);
                return;
            }

            _input.Poll();

            // ② 悬停 / 光标（每帧；目标变化才发事件）+ 走/跑切换（原版 R）
            _input.UpdateHover(!_dead);
            if (!_dead && _input.RunTogglePressed) ToggleRun();

            // ③ 推进移动（沿路点走；路点合法性在 PlayerMotor 内逐格校验）
            var before = _motor.Grid;
            _motor.Tick(dt, map);
            if (_motor.Grid != before)
            {
                Emit(Events.PlayerGridChanged, _motor.Grid);   // 小地图揭迷雾 / 已探索
                CheckExit(map);
            }

            // ④ 输入 → 移动 / 攻击意图
            if (!_dead) HandleMoveIntent(map);

            // ⑤ 腰带快捷键（存活时才发）
            _input.PollHotkeys(!_dead);
        }

        /// <summary>
        /// 点击 / 按住 → 移动或攻击意图（顺序：单击优先 → 持续按住）。
        /// 左键指向怪物的分支走 <see cref="AttackFrame"/>（发 `AttackRequest`）；其余仍走 `MoveCommand`。
        /// ⛔ **没有**方向键备选移动：原版 D2 只有鼠标点地面移动（验收表 U-1）。
        /// </summary>
        private void HandleMoveIntent(IMapModule map)
        {
            // ① 左键单击（本帧按下）：优先判「点怪」，否则移动意图
            if (_input.TryGetGroundClick(out var click))
            {
                HandlePrimaryClick(click);
                return;
            }

            // ② 按住左键：指向怪物 ⇒ 原版「按住持续攻击」（`ICombatModule.RequestAttack` 自带冷却降频）
            if (_input.PrimaryHeld && _input.TryGetGroundHoldTarget(out var held))
            {
                var hov = _input.HoverAt(held);
                if (IsAttackable(hov))
                {
                    AttackFrame(hov, map, _input.StandStill);
                    return;
                }

                // 原版 Shift = 站立不动：按住时忽略点击移动
                if (_input.StandStill) return;

                if (InputReader.ShouldRetarget(_motor.Grid, _holdTarget, held))
                {
                    _holdTarget = held;
                    PlayerLog.Move($"按住左键改目标 held=({held.x},{held.y})（变化超过 1 格才重算路径）");
                    Emit(Events.MoveCommand, held);
                }
                return;
            }

            // ③ 松开左键：清意图（移动完全由点击/按住驱动 —— 原版没有方向键移动）
            _holdTarget = null;
            _lastAttackTargetId = int.MinValue;
        }

        /// <summary>
        /// 以某格为左键点击目标派发一次（单击语义）。**非契约入口**（自证 / 集成 / 世界坐标拾取走它）。
        /// 规则：命中怪物 → `Emit(AttackRequest, id)`（Shift 时只攻击不移动，否则补发走位 `MoveCommand`）；
        /// 其余 → 保持既有 `MoveCommand` 意图（地面物品/NPC 的兜底自动拾取/对话在 `Module/Item`/`Npc`）。
        /// </summary>
        public void HandlePrimaryClick(Vector2Int grid)
        {
            var map = MapOrNull();
            var standStill = _input.StandStill;
            var hov = _input.HoverAt(grid);

            if (IsAttackable(hov))
            {
                AttackFrame(hov, map, standStill);
                return;
            }

            if (standStill) return;                    // 原版 Shift：普通点击不移动

            _holdTarget = grid;
            Emit(Events.MoveCommand, grid);            // 契约事件（本类的 OnMoveCommand 会执行 MoveTo）
        }

        /// <summary>该悬停目标是否"可攻击"（怪物）。</summary>
        private static bool IsAttackable(HoverTarget hov) =>
            hov != null && hov.hasTarget && hov.cursor == CursorKind.Attack;

        /// <summary>
        /// 一帧的攻击语义：发 `AttackRequest`（`ICombatModule` 结算；冷却/超距由它自己判）。
        /// `standStill`（原版 Shift）= 站立攻击：**不产生任何移动目标**。
        /// 否则补发一条 `MoveCommand` —— 因为 `ICombatModule.RequestAttack` 超距时只 Warn 不走位（见文件头）。
        /// </summary>
        private void AttackFrame(HoverTarget hov, IMapModule map, bool standStill)
        {
            Emit(Events.AttackRequest, hov.id);

            if (_lastAttackTargetId != hov.id)
            {
                _lastAttackTargetId = hov.id;
                PlayerLog.Info($"[Attack] 左键点怪 m#{hov.id}「{hov.name}」格=({hov.gridX},{hov.gridY}) " +
                               $"⇒ 发 {Events.AttackRequest}" +
                               (standStill ? "（Shift 按住：站立攻击，不产生移动目标）" : "（并走向怪物相邻格）"));
            }

            if (standStill)
            {
                _holdTarget = null;
                return;
            }

            var monster = new Vector2Int(hov.gridX, hov.gridY);
            var approach = ApproachGrid(map, monster);
            if (_holdTarget != approach)               // 目标格没变就不重算路径（防每帧 A*）
            {
                _holdTarget = approach;
                Emit(Events.MoveCommand, approach);
            }
        }

        /// <summary>
        /// 走向怪物时应落脚的格：怪物 8 邻域内**可走**且离玩家最近的一格（含怪物自己所在格）。
        /// 都不行（异常数据）⇒ 退回怪物所在格（`MoveTo` 会自行判可走/不可达并打日志）。
        /// </summary>
        private Vector2Int ApproachGrid(IMapModule map, Vector2Int monster)
        {
            if (map == null || !map.IsGenerated) return monster;

            var from = _motor.Grid;
            var best = monster;
            var bestD = float.MaxValue;

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    var g = new Vector2Int(monster.x + dx, monster.y + dy);
                    if (!map.Walkable(g)) continue;
                    var d = Iso.GridDistanceEuclidean(from, g);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = g;
                    }
                }
            }

            return best;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 走 / 跑切换（原版 R 键，agent-13 §A 第 5 项）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>当前是否「跑」（false = 走）。</summary>
        public bool Running => _running;

        /// <summary>当前实际移动速度（格/秒）= `GameConst.PlayerWalkSpeed` × (跑 ? 1 : 1/2)。</summary>
        public float MoveSpeed => GameConst.PlayerWalkSpeed * (_running ? 1f : WalkSpeedFactor);

        /// <summary>切换跑/走（原版 R 键；由 `Tick` 读 `InputReader.RunTogglePressed` 驱动）。</summary>
        public void ToggleRun() => SetRunning(!_running);

        /// <summary>设置跑/走（幂等；变化时更新移动速率并发 `HudDirty` 让 HUD 切贴图）。</summary>
        public void SetRunning(bool run)
        {
            if (_running == run) return;

            _running = run;
            _motor.SpeedScale = _running ? 1f : WalkSpeedFactor;
            PlayerLog.Info($"[Run] 走/跑切换 ⇒ {(_running ? "跑" : "走")}：移动速率 {_motor.Speed:0.00} 格/秒" +
                           $"（= PlayerWalkSpeed {GameConst.PlayerWalkSpeed:0.00} × {_motor.SpeedScale:0.##}）；" +
                           $"发 {Events.HudDirty}");
            EmitStats();      // HudDirty + PlayerStatsChanged（HUD 的 run/walk 按钮只切贴图）
        }

        /// <summary>踩到 `TileKind.Exit` 时发 `Events.ExitEntered`（Flow 据此切区域）。</summary>
        private void CheckExit(IMapModule map)
        {
            if (map == null || !map.IsGenerated) return;

            var g = _motor.Grid;
            if (map.TileAt(g) != TileKind.Exit)
            {
                _lastExitGrid = NoGrid;          // 离开出口格：允许下次再触发
                return;
            }

            if (_lastExitGrid == g) return;      // 同一格只触发一次（防每帧刷屏 / 反复切区域）
            _lastExitGrid = g;

            var to = ExitTargetArea(map, g);
            if (to == map.Area)
            {
                PlayerLog.Warn($"出入口格 ({g.x},{g.y}) 的目标区域 = 当前区域 {to} ⇒ 不发 {Events.ExitEntered}（数据异常？）");
                return;
            }

            PlayerLog.Info($"踩到出入口 格=({g.x},{g.y}) 区域 {map.Area} → {to}（发 {Events.ExitEntered}）");
            Emit(Events.ExitEntered, to);
        }

        /// <summary>
        /// 出入口 → 目标区域（`docs/agents/_common.md` §3.5 已冻结的规则）：
        /// `Town`→`BloodMoor`；`BloodMoor`→ 该格 == 洞穴入口 ? `DenOfEvil` : `Town`；`DenOfEvil`→`BloodMoor`。
        /// </summary>
        private static AreaId ExitTargetArea(IMapModule map, Vector2Int grid)
        {
            switch (map.Area)
            {
                case AreaId.Town:
                    return AreaId.BloodMoor;

                case AreaId.BloodMoor:
                    if (map.CaveEntrance.HasValue && map.CaveEntrance.Value == grid) return AreaId.DenOfEvil;
                    return AreaId.Town;

                case AreaId.DenOfEvil:
                    return AreaId.BloodMoor;

                default:
                    PlayerLog.Warn($"ExitTargetArea：未登记的区域 {(int)map.Area}" +
                                   "（契约只定义 Town/BloodMoor/DenOfEvil）⇒ 不切换");
                    return map.Area;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 战斗相关：受伤 / 治疗 / 死亡 / 复活
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public bool ApplyDamage(int amount, DamageType type)
        {
            if (_dead)
            {
                PlayerLog.Warn($"已死亡，忽略本次伤害 {amount}（type={type}）");
                return false;
            }

            if (amount < 0)
            {
                PlayerLog.Warn($"ApplyDamage 收到负数 {amount}（type={type}）⇒ 按 0 处理");
                amount = 0;
            }

            if (amount == 0)
            {
                // 未命中/被完全免疫：`ICombatModule` 会发 `DamageDealt`（hit=false），这里不刷 HUD。
                return false;
            }

            var beforeLive = _life;
            _life -= amount;
            if (_life < 0) _life = 0;

            if (_life == 0)
            {
                PlayerLog.Info($"受击致命：伤害 {amount}（type={type}）生命 {beforeLive} → 0");
                Kill();
                return true;
            }

            PlayerLog.Info($"受击：伤害 {amount}（type={type}）生命 {beforeLive} → {_life}/{_stats.MaxLife}");
            EmitStats();
            return false;
        }

        /// <inheritdoc />
        public void Heal(int amount)
        {
            if (amount <= 0)
            {
                PlayerLog.Warn($"Heal({amount}) 非正数 ⇒ 忽略");
                return;
            }
            if (_dead)
            {
                PlayerLog.Warn($"已死亡时收到 Heal({amount}) ⇒ 忽略（需先 Revive）");
                return;
            }

            var before = _life;
            _life = Mathf.Min(_stats.MaxLife, _life + amount);
            if (before == _life)
            {
                PlayerLog.Info($"Heal({amount})：生命已满（{_life}/{_stats.MaxLife}），无变化");
                return;
            }

            PlayerLog.Info($"回血 {amount}：生命 {before} → {_life}/{_stats.MaxLife}");
            EmitStats();
        }

        /// <inheritdoc />
        public void RestoreMana(int amount)
        {
            if (amount <= 0)
            {
                PlayerLog.Warn($"RestoreMana({amount}) 非正数 ⇒ 忽略");
                return;
            }

            var before = _mana;
            _mana = Mathf.Min(_stats.MaxMana, _mana + amount);
            PlayerLog.Info($"回蓝 {amount}：法力 {before} → {_mana}/{_stats.MaxMana}" +
                           (before == _mana ? "（已满，无变化）" : ""));
            if (before != _mana) EmitStats();
        }

        /// <inheritdoc />
        public void RestoreStamina(int amount)
        {
            if (amount <= 0)
            {
                PlayerLog.Warn($"RestoreStamina({amount}) 非正数 ⇒ 忽略");
                return;
            }

            var before = _stamina;
            _stamina = Mathf.Min(_stats.MaxStamina, _stamina + amount);
            PlayerLog.Info($"回耐力 {amount}：耐力 {before} → {_stamina}/{_stats.MaxStamina}" +
                           (before == _stamina ? "（已满，无变化）" : ""));
        }

        /// <inheritdoc />
        public void Kill()
        {
            if (_dead)
            {
                PlayerLog.Warn("Kill() 在已死亡状态下被调用 ⇒ 忽略（幂等）");
                return;
            }

            _life = 0;
            _dead = true;
            _motor.Stop();
            _holdTarget = null;
            PlayerLog.Info($"died {_name} 等级={_stats.Level}（发 {Events.PlayerDied}，" +
                           "死亡面板由 UI 侧监听；复活走 ICombatModule.RevivePlayer → Player.Revive）");
            Emit(Events.PlayerDied);
            EmitStats();
        }

        /// <inheritdoc />
        public void Revive()
        {
            if (!_dead)
            {
                PlayerLog.Warn("Revive() 在未死亡状态下被调用 ⇒ 忽略");
                return;
            }

            _dead = false;
            _life = _stats.MaxLife;
            _mana = _stats.MaxMana;
            _stamina = _stats.MaxStamina;

            var map = MapOrNull();
            var spawn = map != null && map.IsGenerated ? map.SpawnPoint : _motor.Grid;
            _motor.Teleport(spawn, map);
            _lastExitGrid = NoGrid;
            _selfHealLogged = false;

            PlayerLog.Info($"复活：回出生点 ({_motor.Grid.x},{_motor.Grid.y}) " +
                           $"生命={_life}/{_stats.MaxLife} 法力={_mana}/{_stats.MaxMana} 耐力={_stamina}/{_stats.MaxStamina}");
            Emit(Events.PlayerGridChanged, _motor.Grid);
            EmitStats();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 成长：经验 / 加点 / 金币
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void AddExp(int amount)
        {
            if (amount <= 0)
            {
                PlayerLog.Warn($"AddExp({amount}) 非正数 ⇒ 忽略");
                return;
            }

            _exp += amount;
            PlayerLog.Info($"获得经验 {amount}：累计 {_exp}（等级 {_stats.Level}，下一级阈值 {ExpNext}）");

            var levelUps = 0;
            while (_stats.Level < MaxLevel)
            {
                var need = ExpAt(_stats.Level);
                // `experience_c` 的口径：`Get(L).exp` = **从 L 升到 L+1 的累计阈值**
                // （官方 experience.txt 的 level 列 = 目标等级-1；实证：level 98 = 3,520,485,254
                //  正是公认的「99 级所需累计经验」；见回报「未决」第 2 条）
                if (need <= 0 || _exp < need) break;

                var oldLevel = _stats.Level;
                var oldLife = _stats.MaxLife;
                var oldMana = _stats.MaxMana;
                var oldStam = _stats.MaxStamina;

                _stats.Level++;
                _stats.Recompute();

                // 上限上升的量同步加到当前值（原版升级给的是"上限增量"，不是满血）
                _life = Mathf.Min(_stats.MaxLife, _life + (_stats.MaxLife - oldLife));
                _mana = Mathf.Min(_stats.MaxMana, _mana + (_stats.MaxMana - oldMana));
                _stamina = Mathf.Min(_stats.MaxStamina, _stamina + (_stats.MaxStamina - oldStam));

                var gained = _stats.Row != null ? _stats.Row.StatPerLvl : 0;
                _statPoints += gained;
                _skillPoints += SkillPointsPerLevel;
                levelUps++;

                PlayerLog.Info($"level {oldLevel}→{_stats.Level} 经验={_exp}/{ExpAt(_stats.Level)} " +
                               $"生命上限 {oldLife}→{_stats.MaxLife} 法力上限 {oldMana}→{_stats.MaxMana} " +
                               $"耐力上限 {oldStam}→{_stats.MaxStamina} 属性点+{gained}={_statPoints} " +
                               $"技能点+{SkillPointsPerLevel}={_skillPoints}（发 {Events.LevelUp} + {Events.HudDirty}）");
                Emit(Events.LevelUp, _stats.Level);
            }

            if (levelUps == 0 && _stats.Level >= MaxLevel)
                PlayerLog.Warn($"已达等级上限 {MaxLevel}，经验 {_exp} 溢出不产生升级（原版同样封顶）");

            EmitStats();
        }

        /// <inheritdoc />
        public bool AddGold(int amount)
        {
            if (amount == 0)
            {
                PlayerLog.Warn("AddGold(0) ⇒ 忽略");
                return false;
            }

            if (amount < 0 && _gold + amount < 0)
            {
                PlayerLog.Warn($"金币不足：现有 {_gold}，本次需要 {-amount} ⇒ 不改值");
                return false;
            }

            _gold += amount;
            PlayerLog.Info($"金币 {_gold}（本次 {amount:+#;-#;0}）");
            Emit(Events.GoldChanged, _gold);
            EmitStats();
            return true;
        }

        /// <inheritdoc />
        public void AddSkillPoint(int delta)
        {
            if (delta == 0)
            {
                PlayerLog.Warn("AddSkillPoint(0) ⇒ 忽略");
                return;
            }

            var next = _skillPoints + delta;
            if (next < 0)
            {
                PlayerLog.Warn($"技能点不能为负（当前 {_skillPoints}，本次 {delta}）⇒ 按 0 计");
                next = 0;
            }

            _skillPoints = next;
            PlayerLog.Info($"技能点 {_skillPoints}（本次 {delta:+#;-#;0}）");
            EmitStats();
        }

        /// <inheritdoc />
        public bool AllocateStat(StatKind kind, int delta)
        {
            if (!_created)
            {
                PlayerLog.Warn($"AllocateStat({kind},{delta}) 时还没有角色 ⇒ 忽略");
                return false;
            }

            if (delta == 0)
            {
                PlayerLog.Warn($"AllocateStat({kind},0) ⇒ 无变化，忽略");
                return false;
            }

            if (delta > 0 && _statPoints < delta)
            {
                PlayerLog.Warn($"属性点不足：需要 {delta}，剩余 {_statPoints}（{kind} 未变）");
                return false;
            }

            var before = BaseOf(kind);
            var after = before + delta;

            // 原版不允许把属性降到职业初始值以下
            var floor = _stats.Row != null ? FloorOf(kind) : 0;
            if (after < floor)
            {
                PlayerLog.Warn($"不能再降：{kind} 当前 {before}，下限 {floor}（职业初始值）");
                return false;
            }

            SetBase(kind, after);
            _statPoints -= delta;

            var oldMaxLife = _stats.MaxLife;
            var oldMaxMana = _stats.MaxMana;
            var oldMaxStam = _stats.MaxStamina;
            _stats.Recompute();

            // 上限变化 ⇒ 当前值按增量同步（不变负、不超上限）
            _life = Mathf.Min(_stats.MaxLife, _life + (_stats.MaxLife - oldMaxLife));
            _mana = Mathf.Min(_stats.MaxMana, _mana + (_stats.MaxMana - oldMaxMana));
            _stamina = Mathf.Min(_stats.MaxStamina, _stamina + (_stats.MaxStamina - oldMaxStam));

            PlayerLog.Info(
                $"[Stat] {kind} {before} → {after}（本次 {delta:+#;-#;0}，剩余属性点 {_statPoints}）" +
                $" 生命上限 {oldMaxLife}→{_stats.MaxLife} 法力上限 {oldMaxMana}→{_stats.MaxMana} " +
                $"耐力上限 {oldMaxStam}→{_stats.MaxStamina} 防御={_stats.Defense} AR={_stats.AttackRating} " +
                $"（发 {Events.HudDirty}）");
            EmitStats();
            return true;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 非契约入口 / 自证（`IPlayerModule` 上没有）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// **本项目新增的非契约方法**：显式绑定地图（与 `MapModule.AttachRoot` 同一处置）。
        /// 不调也能跑（那时经组合根 `AppContext.Map` 取），供离线自检 / 集成时定点注入。
        /// </summary>
        public void BindMap(IMapModule map)
        {
            _mapOverride = map;
            _mapWarned = false;
            PlayerLog.Info($"已绑定地图接口：{(map == null ? "(null)（恢复走 AppContext）" : map.GetType().Name)}");
        }

        /// <summary>属性计算器（自证/调试；非契约）。</summary>
        public PlayerStats Stats => _stats;

        /// <summary>移动器（自证/调试；非契约）。</summary>
        public PlayerMotor Motor => _motor;

        /// <summary>输入助手（自证/调试；非契约）。</summary>
        public InputReader Input => _input;

        /// <summary>累计「不可达」次数（自证；非契约）。</summary>
        public int UnreachableCount { get; private set; }

        /// <summary>是否已订阅装备/加点事件（自证；非契约）。</summary>
        public bool EquipSubscribed => _equipSubscribed;

        /// <summary>一行状态摘要（日志/自证用；非契约）。</summary>
        public string DumpStats()
        {
            return $"{_name} {_stats.Cls} Lv{_stats.Level} 力{_stats.Str}/敏{_stats.Dex}/体{_stats.Vit}/精{_stats.Eng} " +
                   $"生命{_life}/{_stats.MaxLife} 法力{_mana}/{_stats.MaxMana} 耐力{_stamina}/{_stats.MaxStamina} " +
                   $"防御{_stats.Defense} AR{_stats.AttackRating} 格挡{_stats.BlockChance}% " +
                   $"抗性(火/冰/电/毒){_stats.ResistOf(DamageType.Fire)}/{_stats.ResistOf(DamageType.Cold)}/" +
                   $"{_stats.ResistOf(DamageType.Lightning)}/{_stats.ResistOf(DamageType.Poison)} " +
                   $"经验{_exp}/{ExpNext} 属性点{_statPoints} 技能点{_skillPoints} 金币{_gold} " +
                   $"格({_motor.Grid.x},{_motor.Grid.y}) 朝向{_motor.Dir} 移动中={_motor.IsMoving} 死亡={_dead}";
        }

        // ═════════════════════════════════════════════════════════════════════
        // 内部
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>升到下一级所需的**累计**经验阈值（表里没有该等级行 / 已满级 ⇒ 0）。</summary>
        private static long ExpAt(int level)
        {
            var row = Table.TableLoader.Experience(level);
            if (row == null)
            {
                if (level < MaxLevel)
                    PlayerLog.Warn($"experience_c 取不到 level={level} 的行 ⇒ 该级升级阈值按 0（永不升级）");
                return 0L;
            }
            return row.Exp;
        }

        /// <summary>`Events.EquipChanged`：装备生效 → 重算派生（装备加成的唯一来源）。</summary>
        private void OnEquipChanged(InventoryChangedArgs args)
        {
            if (args == null)
            {
                PlayerLog.Warn($"收到 null 载荷的 {Events.EquipChanged} ⇒ 忽略");
                return;
            }

            var oldMaxLife = _stats.MaxLife;
            var oldMaxMana = _stats.MaxMana;
            var oldMaxStam = _stats.MaxStamina;

            _stats.ApplyEquipment(args.equip);

            _life = Mathf.Min(_stats.MaxLife, _life + (_stats.MaxLife - oldMaxLife));
            _mana = Mathf.Min(_stats.MaxMana, _mana + (_stats.MaxMana - oldMaxMana));
            _stamina = Mathf.Min(_stats.MaxStamina, _stamina + (_stats.MaxStamina - oldMaxStam));

            EmitStats();
        }

        /// <summary>
        /// `Events.MoveCommand`：移动命令（契约 §3.5，参数 = 目标格）。
        /// 点击/按住（`HandleMoveIntent`）走这条通道；Combat（点怪走位）、UI 也可直接发。
        /// </summary>
        private void OnMoveCommand(Vector2Int target)
        {
            MoveTo(target);
        }

        /// <summary>`Events.StatAllocateRequest`：人物属性面板的加点/撤回请求。</summary>
        private void OnStatAllocateRequest(StatAllocArgs args)
        {
            if (args == null)
            {
                PlayerLog.Warn($"收到 null 载荷的 {Events.StatAllocateRequest} ⇒ 忽略");
                return;
            }
            AllocateStat(args.kind, args.delta);
        }

        /// <summary>取地图（显式绑定优先，其次组合根 `AppContext.Map`）；缺失只报一次。</summary>
        private IMapModule MapOrNull()
        {
            if (_mapOverride != null) return _mapOverride;

            var ctx = AppContext.I;
            if (ctx == null)
            {
                if (!_ctxWarned)
                {
                    _ctxWarned = true;
                    PlayerLog.Warn("AppContext.I 为 null（Bootstrap 未装配？）⇒ 拿不到地图，无法寻路移动（只报一次）");
                }
                return null;
            }

            var map = ctx.Map;
            if (map == null && !_mapWarned)
            {
                _mapWarned = true;
                PlayerLog.Warn("IMapModule 未接入（AppContext.Map == null）⇒ 点击移动/落位降级（只报一次）");
            }
            return map;
        }

        /// <summary>发两个数值刷新事件（HUD 契约事件 + 人物面板增补事件）。</summary>
        private void EmitStats()
        {
            var dto = Snapshot();
            Emit(Events.HudDirty, dto);
            Emit(Events.PlayerStatsChanged, dto);
        }

        /// <summary>发事件（`Game.Event` 为 null 时只打日志，不崩）。</summary>
        private static void Emit(string eventName)
        {
            if (Game.Event == null)
            {
                PlayerLog.Warn($"Game.Event 为 null（Game.Launch 未调用？）⇒ 事件 {eventName} 未能发出");
                return;
            }
            Game.Event.Emit(eventName);
        }

        /// <summary>发带参事件。</summary>
        private static void Emit<T>(string eventName, T arg)
        {
            if (Game.Event == null)
            {
                PlayerLog.Warn($"Game.Event 为 null（Game.Launch 未调用？）⇒ 事件 {eventName} 未能发出");
                return;
            }
            Game.Event.Emit(eventName, arg);
        }

        /// <summary>四维读取（不含装备，加点/存档用基础值）。</summary>
        private int BaseOf(StatKind kind)
        {
            switch (kind)
            {
                case StatKind.Strength: return _stats.BaseStr;
                case StatKind.Dexterity: return _stats.BaseDex;
                case StatKind.Vitality: return _stats.BaseVit;
                case StatKind.Energy: return _stats.BaseEng;
                default:
                    PlayerLog.Warn($"BaseOf 收到未登记的 StatKind {(int)kind} ⇒ 按力量处理");
                    return _stats.BaseStr;
            }
        }

        /// <summary>四维写入（不含装备）。</summary>
        private void SetBase(StatKind kind, int value)
        {
            switch (kind)
            {
                case StatKind.Strength: _stats.BaseStr = value; return;
                case StatKind.Dexterity: _stats.BaseDex = value; return;
                case StatKind.Vitality: _stats.BaseVit = value; return;
                case StatKind.Energy: _stats.BaseEng = value; return;
                default:
                    PlayerLog.Warn($"SetBase 收到未登记的 StatKind {(int)kind} ⇒ 忽略");
                    return;
            }
        }

        /// <summary>该维的职业初始值（加点下限；配表不可用时为 0）。</summary>
        private int FloorOf(StatKind kind)
        {
            var row = _stats.Row;
            if (row == null) return 0;
            switch (kind)
            {
                case StatKind.Strength: return row.Str;
                case StatKind.Dexterity: return row.Dex;
                case StatKind.Vitality: return row.Vit;
                case StatKind.Energy: return row.Eng;
                default:
                    PlayerLog.Warn($"FloorOf 收到未登记的 StatKind {(int)kind} ⇒ 按 0");
                    return 0;
            }
        }

        // ⛔ 这里**没有**「方向键备选移动」开关属性：原版 D2 只有鼠标点地面移动，
        //   曾经那一族（`Game.Setting` 读开关 + 回退 `Cfg`）已按全局 skill §0「A 没有 ⇒ 不加」删除
        //   （验收表 U-1；本项目 bug 表 B35 记了同一件事）。
    }
}
