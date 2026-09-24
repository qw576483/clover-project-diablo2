// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Quest/DenOfEvilQuest.cs
// **主线任务「邪恶洞穴」状态机**（验收表 #38~40 的核心）：
//
//   NotStarted --(阿卡拉处接取 AcceptDen)--> InProgress
//   InProgress --(洞内怪物清空 remaining == 0)--> ReadyToTurnIn
//   ReadyToTurnIn --(回城交付 TurnInDen)--> Done（奖励 +1 技能点，防重复发放）
//
// 「清光洞内全部怪物才可交付」是**硬条件**：`CanTurnIn` 要求 `required > 0 && remaining == 0`
//   （`required > 0` 同时防住"还没进过洞穴就交任务"）。
//
// `progress` 是**派生值**（= required - remaining），不是自增计数 ⇒ 重复收到击杀通知也不会多算
//   （`NotifyMonsterKilled` 与 `Events.MonsterKilled` 两条路径都进来时依然正确）。
//
// 本类只存状态；**事件/玩家奖励/NPC 文本联动在 QuestModule / NpcDialog**。
// 任务名与目标文本**已换成原版串表（TBL）里的原句**（2026「任务日志 = 原版串」轮），
//    串 id / 键名逐条写在下面常量的注释里；**画面中文 0 条自写**
//    出处：`原版资源/d2text/chi_string.txt`（格式 `id<TAB>文本`）+ 键名对照
//    `原版资源/参考工程_Diablierie/Diablerie/Assets/StreamingAssets/data/local/string.txt`。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Core;
using Diablo2.Def;

namespace Diablo2.Module.Quest
{
    /// <summary>邪恶洞穴任务状态机（纯状态，不碰事件/玩家）。</summary>
    internal sealed class DenOfEvilQuest
    {
        /// <summary>该任务的 id（= `Def.QuestId.DenOfEvil`）。</summary>
        public const int Id = (int)QuestId.DenOfEvil;

        /// <summary>
        /// 任务名（任务日志显示）= 原版串 **3714**（键 `qstsa1q1` = "Den of Evil" / 「邪惡洞穴」）。
        /// <para>同族的 Act I 任务名键：`qstsa1q1..q6`（旧译「邪惡洞穴 / 修女埋骨之地 / 交易的工具 /
        /// 凱恩的搜尋 / 遺忘之塔 / 屠殺的修女」）⇒ 与 `MENU/a1q1..a1q6.dc6` 的任务图一一对应。</para>
        /// </summary>
        public const string Name = "邪惡洞穴";

        /// <summary>目标行「在蘿格營地外面的荒野中，找尋一個洞穴。」= 原版串 **3735**（键 `qstsa1q11`）。</summary>
        public const string ObjectiveLookForDen = "在蘿格營地外面的荒野中，找尋一個洞穴。";

        /// <summary>目标行「殺死所有盤踞在洞穴中的怪物。」= 原版串 **3736**（键 `qstsa1q12`）。</summary>
        public const string ObjectiveKillAll = "殺死所有盤踞在洞穴中的怪物。";

        /// <summary>目标行「回去找阿卡拉領賞。」= 原版串 **3740**（键 `qstsa1q15`）。</summary>
        public const string ObjectiveReturnForReward = "回去找阿卡拉領賞。";

        /// <summary>目标行「任務結束。」= 原版串 **3726**（键 `qstsComplete`）。</summary>
        public const string ObjectiveComplete = "任務結束。";

        /// <summary>当前状态。</summary>
        public QuestState State { get; private set; }

        /// <summary>洞内初始怪物总数（= 第一次进入洞穴时统计到的存活数）。</summary>
        public int Required { get; private set; }

        /// <summary>已清怪数（派生值 = Required - 洞内剩余）。</summary>
        public int Progress { get; private set; }

        /// <summary>奖励是否已领（防重复发技能点）。</summary>
        public bool RewardClaimed { get; private set; }

        /// <summary>复位成未接取。</summary>
        public void Reset()
        {
            State = QuestState.NotStarted;
            Required = 0;
            Progress = 0;
            RewardClaimed = false;
        }

        /// <summary>接取（未接取 → 进行中）。重复接取返回 false。</summary>
        public bool Accept()
        {
            if (State != QuestState.NotStarted) return false;
            State = QuestState.InProgress;
            return true;
        }

        /// <summary>记录洞内初始怪物总数（只在 &gt; 0 且尚未记录时生效）。</summary>
        public bool SetRequired(int count)
        {
            if (count <= 0) return false;
            if (Required > 0) return false;
            Required = count;
            return true;
        }

        /// <summary>按洞内剩余怪物数刷新进度与状态（幂等）。返回状态是否发生变化。</summary>
        public bool Refresh(int remaining)
        {
            var changed = false;
            if (State != QuestState.InProgress && State != QuestState.ReadyToTurnIn) return false;

            var done = Required - remaining;
            if (done < 0) done = 0;
            if (done > Required) done = Required;
            if (Progress != done)
            {
                Progress = done;
                changed = true;
            }

            if (State == QuestState.InProgress)
            {
                if (Required > 0 && remaining <= 0)
                {
                    State = QuestState.ReadyToTurnIn;
                    changed = true;
                }
            }
            else if (Required > 0 && remaining > 0)
            {
                // 洞内又刷出怪物（离开后重进原版会重新生成）⇒ 退回可继续清理的状态
                State = QuestState.InProgress;
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// 是否满足交付条件：目标**未完成**（进行中 / 已达成待交付）且已记录 required 且洞内清空。
        /// （契约原文写作"进行中 且 剩余 = 0"，但清光后状态会变成 `ReadyToTurnIn` ⇒ 交付判定必须把它也算上，
        ///   否则会出现"清光后反而不能交"的自相矛盾。）
        /// </summary>
        public bool CanTurnIn(int remaining)
        {
            if (State != QuestState.InProgress && State != QuestState.ReadyToTurnIn) return false;
            return Required > 0 && remaining <= 0;
        }

        /// <summary>交付（只有可交付状态能成功）。</summary>
        public bool TurnIn(int remaining)
        {
            if (!CanTurnIn(remaining)) return false;
            if (RewardClaimed) return false;
            State = QuestState.Done;
            RewardClaimed = true;
            Progress = Required;
            return true;
        }

        /// <summary>
        /// 目标描述（任务日志按状态显示不同文本）—— **全部是原版串表里的原句**（串 id 见各常量注释）。
        /// <para>
        /// **目标行里不再有数字**（2026「任务日志 = 原版串」轮的定案）：
        /// **两行不同源**（目标行用 `IMonsterModule.CountInArea(DenOfEvil)`，人一出洞就恒 0；
        /// （`UI/QuestLogPanel.TextOf` 用 `Remaining()` = `required − progress` 生成），
        /// 目标行只显示原版的两句指令 ⇒ **结构上不可能再出现自相矛盾**。
        /// </para>
        /// <para>
        /// 参数 <paramref name="remaining"/> 保留（`ToDto` / `WriteTo` 的签名与状态机口径不变），
        /// 本函数不再用它拼数字。「人在洞里才报数」这条语义现在由**进度行的数字**表达
        /// （`required − progress` 只在进洞/洞内击杀时刷新 ⇒ 洞外不会误报 0）。
        /// </para>
        /// </summary>
        public string Objective(int remaining)
        {
            switch (State)
            {
                case QuestState.NotStarted:
                    // 未接取：原版任务日志此时显示 `noactivequest`（串 3723），由 `UI/QuestLogPanel.TextOf` 给
                    return string.Empty;
                case QuestState.InProgress:
                    // 原版串 3735 + 3736（两条硬换行：先找洞、再杀光）
                    return ObjectiveLookForDen + "\n" + ObjectiveKillAll;
                case QuestState.ReadyToTurnIn:
                    return ObjectiveReturnForReward;            // 原版串 3740
                case QuestState.Done:
                    return ObjectiveComplete;                   // 原版串 3726
                default:
                    Log.Warn("Quest", $"未知任务状态 {(int)State} ⇒ 目标行留空（不编文案）");
                    return string.Empty;
            }
        }

        /// <summary>打成 DTO（`Events.QuestChanged` 的参数）。</summary>
        public QuestStateDto ToDto(int remaining)
        {
            return new QuestStateDto
            {
                questId = Id,
                name = Name,
                state = State,
                progress = Progress,
                required = Required,
                rewardClaimed = RewardClaimed,
                objective = Objective(remaining),
            };
        }

        /// <summary>读档（非法数据只 Warn，不崩）。</summary>
        public void LoadFrom(QuestStateDto dto)
        {
            if (dto == null)
            {
                Reset();
                return;
            }
            State = dto.state;
            Required = dto.required < 0 ? 0 : dto.required;
            Progress = dto.progress < 0 ? 0 : dto.progress;
            if (Progress > Required) Progress = Required;
            RewardClaimed = dto.rewardClaimed;
        }

        /// <summary>写档。</summary>
        public QuestStateDto WriteTo(int remaining)
        {
            return ToDto(remaining);
        }
    }
}
