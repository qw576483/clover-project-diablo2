// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Quest/QuestModule.cs
// 任务门面实现：**主线「邪恶洞穴」的状态机 + 事件 + 奖励发放**。
//
// 装配契约：`internal sealed class QuestModule : IQuestModule`，无参构造（供 `AppContext.AutoWire()`）。
//
// 依赖（只走接口，**不许 using 别的模块的具体类型**）：
//   · `IMonsterModule.CountInArea(AreaId.DenOfEvil)` —— 「洞内剩余怪物数」的**唯一来源**
//     （契约原文：`DenRemaining = IMonsterModule.CountInArea(AreaId.DenOfEvil)`）；
//   · `IMapModule.Area` —— 判定"这次击杀发生在不在洞穴里"（**任务只认洞穴**）；
//   · `IPlayerModule.AddSkillPoint(+1)` —— 交付奖励。
//
// 事件：
//   收 `MonsterKilled(int)` / `AreaChanged(AreaId)` / `ExitEntered(AreaId)` /
//      `QuestAcceptRequest(int)` / `QuestTurnInRequest(int)`
//   发 `QuestChanged(QuestStateDto)` / `QuestCompleted(int)` / `QuestTurnInDenied(int)`
//   （⚠️ 契约里 `IQuestModule` **没有 Tick**，所以本模块不放 `Tick` 逻辑。）
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.App;
using Diablo2.Core;
using Diablo2.Def;

namespace Diablo2.Module.Quest
{
    /// <summary>任务门面实现（邪恶洞穴）。</summary>
    internal sealed class QuestModule : IQuestModule
    {
        /// <summary>交付奖励：技能点 +1（对应验收 #38「交付 → 技能点 +1」）。</summary>
        private const int TurnInSkillPointReward = 1;

        private readonly DenOfEvilQuest _den = new DenOfEvilQuest();
        private readonly List<QuestStateDto> _list = new List<QuestStateDto>();

        private bool _warnedNoMonster;
        private bool _warnedNoMap;

        /// <summary>
        /// 本局是否**亲眼见过洞穴里有怪**（进过洞穴，或统计到过 required &gt; 0）。
        /// 作用：`DenRemaining` 只是 `CountInArea(DenOfEvil)`，而"不在洞穴时"它天然是 0
        /// —— 没有这个标记就无法区分「洞被清光了」和「洞还没加载」，
        /// 会导致"刚进城就能交任务"（读档后尤其明显）。见回报「未决」第 6 条。
        /// </summary>
        private bool _caveObserved;

        public QuestModule()
        {
            _den.Reset();

            var bus = Game.Event;
            if (bus == null)
            {
                Log.Warn("Quest", "QuestModule 构造时 Game.Event 为 null（引擎未启动？）⇒ 不订阅事件（只能靠显式调用驱动）");
                return;
            }
            bus.On<int>(Events.MonsterKilled, OnMonsterKilled);
            bus.On<AreaId>(Events.AreaChanged, OnAreaChanged);
            bus.On<AreaId>(Events.ExitEntered, OnAreaChanged);
            bus.On<int>(Events.QuestAcceptRequest, OnAcceptRequest);
            bus.On<int>(Events.QuestTurnInRequest, OnTurnInRequest);
        }

        // ── 门面属性 ───────────────────────────────────────────────────────────

        /// <summary>邪恶洞穴任务状态。</summary>
        public QuestState DenOfEvil
        {
            get { return _den.State; }
        }

        /// <summary>洞内剩余怪物数（来自 `IMonsterModule.CountInArea(DenOfEvil)`）。</summary>
        public int DenRemaining
        {
            get
            {
                var m = Monster;
                if (m == null)
                {
                    if (!_warnedNoMonster)
                    {
                        _warnedNoMonster = true;
                        Log.Warn("Quest", "DenRemaining：`IMonsterModule` 未接入（AppContext.Monster == null）⇒ 按 0 处理，"
                            + "任务无法计数（洞穴内清怪不会推进）");
                    }
                    return 0;
                }
                return m.CountInArea(AreaId.DenOfEvil);
            }
        }

        /// <summary>
        /// 是否满足交付条件（目标未完成 且 已记录 required 且 洞内清空）。
        /// 额外要求：**本局见过洞穴里的怪**，或存档里已是「可交付」状态
        /// （否则"人还在城里、洞穴还没生成"会误判成已清光）。
        /// </summary>
        public bool CanTurnInDen
        {
            get
            {
                if (!_den.CanTurnIn(DenRemaining)) return false;
                return _caveObserved || _den.State == QuestState.ReadyToTurnIn;
            }
        }

        /// <summary>全部任务状态（任务日志面板用）。</summary>
        public IReadOnlyList<QuestStateDto> Quests
        {
            get
            {
                RebuildList();
                return _list;
            }
        }

        /// <summary>取某个任务状态（不存在返回 null 并打日志）。</summary>
        public QuestStateDto Get(int questId)
        {
            if (questId != DenOfEvilQuest.Id)
            {
                Log.Warn("Quest", $"Get(questId={questId})：本项目只有「{DenOfEvilQuest.Name}」"
                    + $"（id={DenOfEvilQuest.Id}），其余任务不在本轮范围（Act II~V 明确排除）");
                return null;
            }
            return _den.ToDto(DenRemaining);
        }

        // ── 接取 / 交付 ────────────────────────────────────────────────────────

        /// <summary>接取邪恶洞穴任务（重复接取打日志并忽略）。</summary>
        public void AcceptDen()
        {
            if (!_den.Accept())
            {
                Log.Warn("Quest", $"AcceptDen：任务「{DenOfEvilQuest.Name}」已处于 {_den.State} ⇒ 忽略重复接取");
                return;
            }

            EnsureRequired();
            Log.Info("Quest", $"[Quest] 接取「{DenOfEvilQuest.Name}」：state={_den.State} "
                + $"required={_den.Required}（洞穴尚未进入时 required=0，进洞后自动补记）");
            EmitChanged();
        }

        /// <summary>交付邪恶洞穴任务（不满足条件则发 `Events.QuestTurnInDenied` 并打日志）。</summary>
        public void TurnInDen()
        {
            var remaining = DenRemaining;
            if (!CanTurnInDen)
            {
                var reason = _den.State != QuestState.InProgress
                    ? $"任务状态是 {_den.State}（不是进行中）"
                    : (_den.Required <= 0 ? "还没记录洞穴内怪物总数（未进入过洞穴？）" : $"洞穴内还剩 {remaining} 只怪物");
                Log.Warn("Quest", $"[Quest] 交付被拒：「{DenOfEvilQuest.Name}」未达成条件 —— {reason}");
                Game.Event?.Emit(Events.QuestTurnInDenied, DenOfEvilQuest.Id);
                return;
            }

            if (!_den.TurnIn(remaining))
            {
                Log.Warn("Quest", $"TurnInDen：「{DenOfEvilQuest.Name}」交付失败（rewardClaimed={_den.RewardClaimed}）⇒ 忽略");
                return;
            }

            var p = Player;
            if (p != null) p.AddSkillPoint(TurnInSkillPointReward);
            else Log.Warn("Quest", $"交付奖励：`IPlayerModule` 未接入 ⇒ 技能点 +{TurnInSkillPointReward} **未真正发放**（请检查装配）");

            Log.Info("Quest", $"[Quest] 交付「{DenOfEvilQuest.Name}」完成：state={_den.State} "
                + $"progress={_den.Progress}/{_den.Required}，技能点 +{TurnInSkillPointReward}"
                + (p != null ? $"（当前技能点 {p.SkillPoints}）" : "")
                + "；后续任务（Act II 及以后）明确不在本轮范围");

            Game.Event?.Emit(Events.QuestCompleted, DenOfEvilQuest.Id);
            EmitChanged();
        }

        // ── 击杀统计 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 怪物被击杀时调用（重新统计洞内剩余并刷新状态）。
        /// **只认洞穴区域**：不在 `AreaId.DenOfEvil` 时不计入进度（`DenRemaining` 也不会变）。
        /// </summary>
        public void NotifyMonsterKilled(int monsterId)
        {
            if (_den.State == QuestState.NotStarted)
            {
                Log.Info("Quest", $"NotifyMonsterKilled(id={monsterId})：任务未接取 ⇒ 不计数");
                return;
            }
            if (_den.State == QuestState.Done)
            {
                Log.Info("Quest", $"NotifyMonsterKilled(id={monsterId})：任务已完成 ⇒ 不计数");
                return;
            }

            var map = Map;
            if (map == null)
            {
                if (!_warnedNoMap)
                {
                    _warnedNoMap = true;
                    Log.Warn("Quest", "NotifyMonsterKilled：`IMapModule` 未接入 ⇒ 无法判定当前区域，按洞穴处理（仅供降级运行）");
                }
            }
            else if (map.Area != AreaId.DenOfEvil)
            {
                // 验收 #70：在血腥荒野调 NotifyMonsterKilled ⇒ DenRemaining 不变
                Log.Info("Quest", $"NotifyMonsterKilled(id={monsterId})：当前区域 {map.Area} 不是洞穴 ⇒ 不计入任务进度"
                    + $"（DenRemaining 仍为 {DenRemaining}）");
                return;
            }
            else
            {
                _caveObserved = true;                 // 人在洞穴里杀怪 ⇒ 本局确实进过洞穴
            }

            EnsureRequired();
            var changed = _den.Refresh(DenRemaining);
            Log.Info("Quest", $"[Quest] DenOfEvil remaining={DenRemaining} progress={_den.Progress}/{_den.Required} "
                + $"state={_den.State}" + (changed ? "（状态已变化）" : ""));
            EmitChanged();
        }

        // ── 存档 / 复位 ────────────────────────────────────────────────────────

        /// <summary>按存档恢复任务状态。</summary>
        public void LoadFrom(CharacterSave save)
        {
            if (save == null)
            {
                Log.Warn("Quest", "QuestModule.LoadFrom 收到 null 存档 ⇒ 复位");
                _den.Reset();
                EmitChanged();
                return;
            }

            QuestStateDto dto = null;
            if (save.quests != null)
            {
                for (var i = 0; i < save.quests.Count; i++)
                {
                    var q = save.quests[i];
                    if (q != null && q.questId == DenOfEvilQuest.Id) { dto = q; break; }
                }
            }

            if (dto == null)
            {
                Log.Info("Quest", $"读档：存档里没有「{DenOfEvilQuest.Name}」的记录 ⇒ 按未接取处理");
                _den.Reset();
            }
            else
            {
                _den.LoadFrom(dto);
                // ⚠️ **读档这一刻不调 Refresh**：此时洞穴还没生成，`DenRemaining` 必然是 0，
                //    拿它刷新会把"进行中 3/5"误判成"已清光"。真正的刷新发生在
                //    进洞穴（AreaChanged）或收到击杀通知时 —— 见 `_caveObserved`。
                Log.Info("Quest", $"读档：「{DenOfEvilQuest.Name}」state={_den.State} progress={_den.Progress}/{_den.Required} "
                    + $"rewardClaimed={_den.RewardClaimed}（洞内剩余按进洞后实时统计；当前 {DenRemaining}）");
            }

            EmitChanged();
        }

        /// <summary>写回存档对象。</summary>
        public void WriteTo(CharacterSave save)
        {
            if (save == null)
            {
                Log.Warn("Quest", "QuestModule.WriteTo 收到 null 存档 ⇒ 跳过");
                return;
            }
            if (save.quests == null) save.quests = new List<QuestStateDto>();
            save.quests.Clear();
            save.quests.Add(_den.WriteTo(DenRemaining));
            Log.Info("Quest", $"存档：「{DenOfEvilQuest.Name}」state={_den.State} progress={_den.Progress}/{_den.Required}");
        }

        /// <summary>复位（回主菜单时调用）。</summary>
        public void Reset()
        {
            _den.Reset();
            _caveObserved = false;
            EmitChanged();
            Log.Info("Quest", "任务模块已复位（邪恶洞穴回到未接取）");
        }

        // ── 内部 ───────────────────────────────────────────────────────────────

        private static IMonsterModule Monster
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.Monster : null;
            }
        }

        private static IMapModule Map
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.Map : null;
            }
        }

        private static IPlayerModule Player
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.Player : null;
            }
        }

        /// <summary>第一次能统计到洞穴怪物时，把"洞内初始怪物总数"记下来。</summary>
        private void EnsureRequired()
        {
            if (_den.Required > 0) return;

            var n = DenRemaining;
            if (n <= 0) return;

            if (_den.SetRequired(n))
            {
                _caveObserved = true;                 // 统计到洞内有怪 ⇒ 本局见过洞穴
                Log.Info("Quest", $"[Quest] 「{DenOfEvilQuest.Name}」记录洞内初始怪物总数 required={n}");
            }
        }

        private void RebuildList()
        {
            _list.Clear();
            _list.Add(_den.ToDto(DenRemaining));
        }

        private void EmitChanged()
        {
            RebuildList();
            Game.Event?.Emit(Events.QuestChanged, _list[0]);
        }

        private void OnMonsterKilled(int monsterId)
        {
            NotifyMonsterKilled(monsterId);
        }

        private void OnAreaChanged(AreaId area)
        {
            if (area != AreaId.DenOfEvil) return;

            _caveObserved = true;                     // 进过洞穴
            Log.Info("Quest", $"[Quest] 进入洞穴（area={area}）：remaining={DenRemaining} state={_den.State}");
            EnsureRequired();
            _den.Refresh(DenRemaining);
            EmitChanged();
        }

        private void OnAcceptRequest(int questId)
        {
            if (questId != DenOfEvilQuest.Id)
            {
                Log.Warn("Quest", $"QuestAcceptRequest(questId={questId})：不是「{DenOfEvilQuest.Name}」⇒ 忽略");
                return;
            }
            AcceptDen();
        }

        private void OnTurnInRequest(int questId)
        {
            if (questId != DenOfEvilQuest.Id)
            {
                Log.Warn("Quest", $"QuestTurnInRequest(questId={questId})：不是「{DenOfEvilQuest.Name}」⇒ 忽略");
                return;
            }
            TurnInDen();
        }
    }
}
