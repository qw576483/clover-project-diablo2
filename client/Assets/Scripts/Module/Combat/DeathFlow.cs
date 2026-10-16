// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Combat/DeathFlow.cs
// **死亡链**：怪物死亡 → 经验结算 → 掉落触发 → 尸体保留；玩家死亡 → 脱战 + 死亡屏。
//
// 设计要点（为什么订阅事件而不是被 `MonsterModule` 直接调用）：
//   `MonsterModule` 只需 `Game.Event.Emit(Events.MonsterKilled, id)` —— 它**不必**知道
//   "谁给它经验、谁掉东西"，也不需要把 `monster_c.TreasureClass`（`IMonsterModule` 上没有）
//   传出来：本类自己经 `IMonsterModule.Get(id)` 拿回**保留了尸体的** `MonsterState`，
//   再用 `kindId` 去 `Tables.Default.Monster` 查掉落表名。这样两边都只依赖
//   `AppContext` 注入的**接口**与配表，不产生任何跨模块具体类型耦合。
//
// 死亡链的完整顺序（验收要抄的日志序列）：
//   [Monster] m#<id> <名> 死亡（尸体保留 corpseUsable=True，供萨满复活）
//   [Combat]  击杀链：exp +<n>（玩家 12 → 30）
//   [Combat]  掉落触发：TC="Act 1 H2H A"(id=4) 等级=1 格=(x,y)
//
// 玩家死亡：`Events.PlayerDied` 由 `IPlayerModule.Kill()` 发出（契约）；
//   本类负责**清仇恨 + 清目标 + 记死亡链日志**，死亡屏（`DeathPanel`）由 UI 侧监听同一事件打开
//   （`UI/**` 不许 using `Diablo2.Module`，它只能听事件 ⇒ 这是唯一正确的分工）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using AppContext = Diablo2.App.AppContext;

namespace Diablo2.Module.Combat
{
    /// <summary>死亡链（怪物击杀结算 + 玩家死亡处理）。</summary>
    internal sealed class DeathFlow
    {
        /// <summary>掉落随机流（**由本局地图 seed 派生**，同 seed 的掉落序列可复现）。</summary>
        private Rng _lootRng;

        /// <summary>玩家死亡时的脱战处理是否已做（避免同一次死亡重复刷日志）。</summary>
        private bool _playerDeadHandled;

        /// <summary>构造即订阅（`CombatModule` 在 `AutoWire` 里被创建时执行一次）。</summary>
        public DeathFlow()
        {
            if (Game.Event == null)
            {
                CombatLog.Error("DeathFlow 构造时 Game.Event 为 null（Game.Launch 未调用？）" +
                                "⇒ 击杀链（经验/掉落）不会触发");
                return;
            }

            Game.Event.On<int>(Events.MonsterKilled, OnMonsterKilled);
            Game.Event.On(Events.PlayerDied, OnPlayerDied);
            CombatLog.Info("DeathFlow 已订阅：MonsterKilled（经验+掉落）、PlayerDied（脱战+死亡屏）");
        }

        /// <summary>复位（回主菜单 / 换区域）：清随机流与一次性标记。</summary>
        public void Reset()
        {
            _lootRng = null;
            _playerDeadHandled = false;
            CombatLog.ResetThrottle();
            CombatLog.Info("DeathFlow.Reset：掉落随机流与一次性标记已清空");
        }

        /// <summary>
        /// 本局掉落随机流：用 `IMapModule.Seed` 派生 ⇒ **同 seed 同掉落**（可复现，便于自证与复现现场）。
        /// 地图未接入时退回时钟 seed 并告警（仍然只告一次）。
        /// </summary>
        private Rng LootRng()
        {
            if (_lootRng != null) return _lootRng;

            var map = AppContext.I != null ? AppContext.I.Map : null;
            if (map != null && map.IsGenerated)
            {
                _lootRng = new Rng(map.Seed ^ LootSeedSalt);
                CombatLog.Info($"掉落随机流已建立：mapSeed={map.Seed} ^ salt=0x{LootSeedSalt:X} ⇒ rngSeed={_lootRng.Seed}");
            }
            else
            {
                _lootRng = Rng.FromTime();
                CombatLog.WarnOnce("loot.rng.fallback",
                    "DeathFlow: 地图未生成（拿不到 seed）⇒ 掉落改用时钟 seed（本局掉落不可复现）");
            }
            return _lootRng;
        }

        /// <summary>掉落随机流的派生 salt（固定常量，保证可复现）。</summary>
        private const int LootSeedSalt = 0x1F35A9;

        // ═════════════════════════════════════════════════════════════════════
        // 怪物死亡
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>`Events.MonsterKilled` 回调（参数 = 怪物实体 id）。</summary>
        private void OnMonsterKilled(int monsterId)
        {
            var ctx = AppContext.I;
            if (ctx == null)
            {
                CombatLog.WarnOnce("death.ctx.null",
                    "DeathFlow.OnMonsterKilled: AppContext.I 为 null（Bootstrap 未装配）⇒ 经验与掉落都没发");
                return;
            }

            var monster = ctx.Monster;
            if (monster == null)
            {
                CombatLog.WarnOnce("death.monster.missing",
                    "DeathFlow.OnMonsterKilled: IMonsterModule 未接入 ⇒ 拿不到怪物状态，经验与掉落都没发");
                return;
            }

            var state = monster.Get(monsterId);
            if (state == null)
            {
                // 尸体可能已被回收（CorpseLifetime 到期 / DespawnAll）：这是**可预期**的时序，降频即可。
                CombatLog.WarnThrottled("death.state.gone",
                    $"DeathFlow.OnMonsterKilled: 找不到 m#{monsterId} 的状态" +
                    "（尸体已被回收或已 DespawnAll？）⇒ 经验与掉落都没发");
                return;
            }

            if (state.alive)
            {
                // 状态自相矛盾：不发 MonsterKilled 却还是活的 ⇒ 说出来，别静默
                CombatLog.Warn($"DeathFlow.OnMonsterKilled: m#{monsterId} 的 alive 仍为 true（不该发生）⇒ 忽略本次");
                return;
            }

            // ① 经验结算
            GrantExp(ctx, state);

            // ② 掉落触发
            TriggerLoot(ctx, state);
        }

        /// <summary>经验给玩家（`IPlayerModule.AddExp`，到阈值自动升级在 Player 内部）。</summary>
        private static void GrantExp(AppContext ctx, MonsterState state)
        {
            var player = ctx.Player;
            if (player == null)
            {
                CombatLog.WarnOnce("death.player.missing",
                    "DeathFlow: IPlayerModule 未接入（AppContext.Player == null）⇒ 击杀经验没有被发放");
                return;
            }

            if (state.exp <= 0)
            {
                CombatLog.WarnThrottled("death.exp.zero",
                    $"DeathFlow: m#{state.id} {state.name} 的 exp={state.exp}（配表 monster_c.exp=0？）⇒ 未加经验");
                return;
            }

            var before = player.Exp;
            player.AddExp(state.exp);
            CombatLog.Info($"[击杀链] m#{state.id} {state.name}（等级 {state.level}）→ 经验 +{state.exp}" +
                           $"（{before} → {player.Exp}，等级 {player.Level}）");
        }

        /// <summary>
        /// 掉落触发：`monster_c.treasure_class`（官方 `TreasureClass1`；**精英怪改用**
        /// `treasure_class_champ`/`treasure_class_unique`，即官方 `TreasureClass2/3` —— 见 R6）
        /// → `treasureclass_c` → `IItemModule.DropLoot(tcId, level, grid, rng)`。
        /// <para>
        /// **跨模块约定**：`IItemModule.DropLoot` 的第 1 个参数是 `int treasureClassId`，
        /// 而 `treasureclass_c` 的主键是 **string**（TC 名）⇒「int 从哪来」契约未定义。
        /// 本项目的口径（`TreasureClassIdOf`）：**行在 `Tables.Default.Treasureclass.All()` 里的 1 基序号**。
        /// </para>
        /// </summary>
        private void TriggerLoot(AppContext ctx, MonsterState state)
        {
            var row = Table.Tables.Default.Monster.Get(state.kindId);
            if (row == null)
            {
                CombatLog.WarnThrottled("death.monster.row",
                    $"DeathFlow: monster_c 里找不到 kindId={state.kindId}（配表未加载？）⇒ 本次不掉落");
                return;
            }

            var tcName = row.TreasureClass;

            //   出处（官方 1.10f `MonStats.txt`，本项目 8 只怪逐行核过）：官方有 4 个 TC 槽位
            //   `TreasureClass1..4`，实测取值 = 1 普通 / 2 **冠军怪** / 3 **唯一（精英）怪** / 4 空；
            //   且 `TreasureClassEx.txt` 里**没有任何** TC 把 `Act 1 Champ/Unique A` 当 Item 引用
            //   ⇒ 选槽发生在引擎侧，必须由"这只怪是什么"决定，不能沿用普通怪的 `TreasureClass1`
            //   （旧行为：`Act 1 Champ/Unique/Super*` 9 个 TC 全是死数据）。
            if (state.isChampion)
            {
                var eliteKind = EliteKindOf(state);
                var eliteTc = eliteKind == 1 ? row.TreasureClassUnique : row.TreasureClassChamp;
                if (!string.IsNullOrEmpty(eliteTc))
                {
                    CombatLog.Info($"DeathFlow: m#{state.id} 是{(eliteKind == 1 ? "唯一（精英）" : "冠军")}怪" +
                                   $"（词缀 {state.modName} id={state.modId}）⇒ 掉落改用官方 " +
                                   $"{(eliteKind == 1 ? "TreasureClass3" : "TreasureClass2")}「{eliteTc}」" +
                                   $"（普通怪槽位 TreasureClass1「{row.TreasureClass}」不适用）");
                    tcName = eliteTc;
                }
                else
                {
                    // 非预期分支：官方 1.10f 里这 8 只怪两列都有值 ⇒ 走到这里说明换了源表。点名，不静默。
                    CombatLog.WarnThrottled("death.tc.elite.empty",
                        $"DeathFlow: m#{state.id} {row.Name} 是精英怪，但 monster_c 的 " +
                        $"{(eliteKind == 1 ? "treasure_class_unique" : "treasure_class_champ")} 为空 " +
                        $"⇒ 沿用普通怪 TC「{row.TreasureClass}」（精英掉落与普通怪一致）");
                }
            }

            if (string.IsNullOrEmpty(tcName))
            {
                CombatLog.WarnThrottled("death.tc.empty",
                    $"DeathFlow: m#{state.id} {row.Name} 的 treasure_class 为空（官方 1.10 里 BloodHawk 等确实没有 TC）" +
                    "⇒ 本次不掉落（不是 bug）");
                return;
            }

            var tcId = TreasureClassIdOf(tcName);
            if (tcId <= 0)
            {
                CombatLog.Warn($"DeathFlow: treasureclass_c 里找不到 TC「{tcName}」⇒ 本次不掉落（配表/转换脚本核对）");
                return;
            }

            var item = ctx.Item;
            if (item == null)
            {
                CombatLog.WarnOnce("death.item.missing",
                    $"DeathFlow: IItemModule 未接入（AppContext.Item == null）⇒ 本次掉落丢失（TC=\"{tcName}\" id={tcId}）；" +
                    "等 Module/Item 落地后自动恢复，无需改本文件");
                return;
            }

            // 掉落格必须可走（验收要求：所有掉落格 Walkable == true）
            var grid = new Vector2Int(state.gridX, state.gridY);
            var map = ctx.Map;
            if (map != null && map.IsGenerated && !map.Walkable(grid))
            {
                var fixedGrid = map.RandomWalkableTile(LootRng());
                CombatLog.Warn($"DeathFlow: 怪物格 {grid} 不可走（尸体在障碍上？）⇒ 掉落到就近随机可走格 {fixedGrid}");
                grid = fixedGrid;
            }

            var rng = LootRng();
            CombatLog.Info($"[击杀链] 掉落触发：TC=\"{tcName}\"(id={tcId}) 等级={state.level} 格=({grid.x},{grid.y})" +
                           $" rng={rng.Seed}");
            item.DropLoot(tcId, state.level, grid, rng);
        }

        /// <summary>
        /// 精英怪的种类（决定用哪个 TC 槽位）：读 `monumod_c.kind`（0 = 冠军怪 / 1 = 唯一（精英）怪）。
        /// <para>
        /// 口径出处：与 `MonsterSpawner.PickEliteMod(rng, kind)` 的 `kind` 是**同一条配表列**
        /// （`MonsterSpawner.cs:202` 的 `kind = p % 2`、`:517` 的 `row.Kind == kind`）。
        /// 取不到词缀行 ⇒ 返回 0（冠军槽位）并 WarnOnce：不静默、也不许瞎猜一个槽位。
        /// </para>
        /// </summary>
        public static int EliteKindOf(MonsterState state)
        {
            if (state == null || state.modId <= 0)
            {
                CombatLog.WarnOnce("death.tc.elite.nomod",
                    "DeathFlow.EliteKindOf: 精英怪没有 modId（monumod_c 未加载 / 词缀未登记？）" +
                    "⇒ 按冠军槽位 treasure_class_champ 处理");
                return 0;
            }

            var mod = Table.Tables.Default.Monumod.Get(state.modId);
            if (mod != null) return mod.Kind;

            CombatLog.WarnOnce("death.tc.elite.badmod",
                $"DeathFlow.EliteKindOf: monumod_c 里找不到 id={state.modId}（配表与存档不一致？）" +
                "⇒ 按冠军槽位 treasure_class_champ 处理");
            return 0;
        }

        /// <summary>
        /// TC 名 → `int treasureClassId`（**本项目口径**：`All()` 的 1 基行序；0 = 找不到）。
        /// 见 <see cref="TriggerLoot"/> 的说明。
        /// </summary>
        public static int TreasureClassIdOf(string tcName)
        {
            if (string.IsNullOrEmpty(tcName)) return 0;
            var table = Table.Tables.Default.Treasureclass;
            var all = table.All();
            if (all == null || all.Count == 0)
            {
                CombatLog.WarnOnce("death.tc.notable",
                    "DeathFlow.TreasureClassIdOf: treasureclass_c 未加载（表为空）⇒ 无法解析 TC");
                return 0;
            }
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] != null && string.Equals(all[i].Name, tcName, StringComparison.Ordinal)) return i + 1;
            }
            return 0;
        }

        /// <summary>TC id → 名字（自证/日志用；0 或越界返回空串）。</summary>
        public static string TreasureClassNameOf(int tcId)
        {
            if (tcId <= 0) return string.Empty;
            var all = Table.Tables.Default.Treasureclass.All();
            var i = tcId - 1;
            if (all == null || i < 0 || i >= all.Count) return string.Empty;
            return all[i] != null ? all[i].Name : string.Empty;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 玩家死亡
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// `Events.PlayerDied` 回调（无参）。死亡屏（`DeathPanel`）由 UI 侧监听同一事件打开
        /// （`UI/**` 不许 using `Diablo2.Module`）；本类做**脱战 + 清目标 + 记链日志**。
        /// </summary>
        private void OnPlayerDied()
        {
            if (_playerDeadHandled)
            {
                CombatLog.WarnThrottled("death.player.dup", "DeathFlow: 收到重复的 PlayerDied（已在处理中）⇒ 忽略");
                return;
            }
            _playerDeadHandled = true;

            var ctx = AppContext.I;
            var player = ctx != null ? ctx.Player : null;
            CombatLog.Info($"[死亡链] 玩家死亡：level={player?.Level} 位置={player?.Grid} " +
                           $"→ 取消目标 + 怪物脱战 + 打开死亡屏（由 UI 监听 {Events.PlayerDied}）");

            // 清目标（经接口；本类与 CombatModule 同属 Combat 子系统，但仍然只走门面）
            if (ctx != null && ctx.Combat != null) ctx.Combat.ClearTarget();
            else CombatLog.WarnOnce("death.combat.missing",
                "DeathFlow: ICombatModule 未接入 ⇒ 玩家死亡后战斗目标未清");

            // 怪物仇恨清空：由 `MonsterModule` 订阅同一个事件自己处理（各管各的状态）。
            // 暂停怪物推进同理（Player.IsDead 后 AI 不再锁定玩家）。
        }

        /// <summary>玩家复活后允许再次处理死亡（`CombatModule.RevivePlayer` 调用）。</summary>
        public void NotifyPlayerRevived()
        {
            if (!_playerDeadHandled) return;
            _playerDeadHandled = false;
            CombatLog.Info("[死亡链] 玩家已复活 ⇒ 死亡处理标记复位");
        }
    }
}
