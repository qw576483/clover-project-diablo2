// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Audio/AudioHook.cs
// **事件触发点接线**：把"游戏里发生了什么"翻译成"该响哪个音效键"。
//
// ⛔ 接线方式只有一种：`Game.Event` 订阅（`_common.md` §2：**不许改别的模块的文件**）。
//    本类因此不持有任何别的模块的实现类型、也不读 `AppContext`（当前区域由 `Events.AreaChanged` 自己跟）。
//
// 覆盖的触发点（`docs/agents/agent-11-音频模块.md` §1 点名的那些里，**尚未被别的模块直连覆盖的**）：
//   脚步     ← `Events.PlayerGridChanged`（按移动速度节流；静止不发）
//   拾取     ← `Events.ItemPicked`（金币 / 普通物品两键）
//   使用物品 ← `Events.ItemUsed`
//   升级     ← `Events.LevelUp`
//   任务完成 ← `Events.QuestCompleted`
//   复活     ← `Events.Revived`（**复活完成**；发送方 = `App/AppEventRouting.cs`，它保证只在真的复活后广播。
//              刻意**不**用 `Events.ReviveRequest` —— 那是"点击请求"，播放会早于真实复活）
//   UI 点击  ← `Events.PanelToggleRequest` / `Events.DialogOptionChosen` / `Events.ShopBuyRequest` / `ShopSellRequest`
//   NPC 对话 ← `Events.DialogOpen`；商店 ← `Events.ShopOpen`
//   传送     ← `Events.ExitEntered`（踩出入口 / 区域切换前的"嗖"）
//   进图     ← `Events.StageEntered`
//   BGM 切换 ← `Events.AreaChanged`（Town/BloodMoor/DenOfEvil 各一首）+ `Events.StageEntered`（首次进图）
//   统一入口 ← `Events.PlaySfx` / `Events.PlayBgm`（`Core/Events.cs` 为本模块预留的两个键）
//   音量同步 ← `Events.VolumeChanged`（设置面板改音量后同步本模块缓存，不重复落盘）
//
// 刻意**不**订阅的（否则会与已有直连调用**同一次命中出两声**）：
//   `Events.DamageDealt` / `PlayerDamaged` / `MonsterKilled` / `MonsterSpawned` / `SkillCast`
//   —— 命中/未命中/受击/死亡/施法/怪物攻击/萨满复活已经由 `DamagePipeline` / `MonsterModule` /
//      `SkillModule` **直连** `ctx.Audio.SfxAt(SfxKeys.…)` 发出（见 `AudioModule.cs` 头部说明）。
//
// 脚步节流数学：间隔 = `TilesPerFootstep / GameConst.PlayerWalkSpeed`（常量来源 `Core/GameConst`，不写裸数值）。
//   本机 `PlayerWalkSpeed = 6 格/秒`，每 2 格一步 ⇒ 0.333s 一步（≈ 3 步/秒，与原版走/跑节奏同量级）。
//   时间由 `AudioModule.Tick(dt)` 推进（`AppContext.Tick` 每帧转发），**不碰任何 Unity 原生时钟**。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Audio
{
    /// <summary>事件 → 音效键 的接线器（与 <see cref="AudioModule"/> 同属 Audio 模块）。</summary>
    internal sealed class AudioHook
    {
        /// <summary>走多少格算"一步"（原版 D2 走跑每步覆盖约 2 格；调手感只改这个数）。</summary>
        private const float TilesPerFootstep = 2f;

        private readonly AudioModule _audio;

        /// <summary>是否已订阅（`Attach` 幂等；`Detach` 后可再次 `Attach`）。</summary>
        private bool _subscribed;

        /// <summary>本帧内是否发生过格变化（脚步节流用；每个 `Tick` 末尾清掉）。</summary>
        private bool _movedSinceTick;

        /// <summary>最近一次格变化的位置（脚步声用它的世界坐标）。</summary>
        private Vector2Int _lastGrid;

        /// <summary>距上一步累积的时间。</summary>
        private float _stepAccum;

        /// <summary>
        /// 当前区域。由 `Events.AreaChanged` 更新 —— **不读 `AppContext.Map`**（少一处模块耦合）：
        /// `MapModule.Generate` 完成后会发 `MapGenerated` + `AreaChanged`（`Module/Map/MapModule.cs:397-398`），
        /// 且 `AppFlow.EnterStage` 里 `Map.Generate` **先于** `StageEntered`（`Module/Flow/AppFlow.cs:426/453`）
        /// ⇒ 进图时这里已经是最新区域。
        /// </summary>
        private AreaId _area = AreaId.Town;

        /// <summary>是否收到过 `Events.AreaChanged`（没收到说明 Map 模块未接入，进图时给一条 Warn）。</summary>
        private bool _sawAreaChanged;

        /// <summary>构造（只记引用，不订阅 —— 订阅走 <see cref="Attach"/>）。</summary>
        public AudioHook(AudioModule audio)
        {
            _audio = audio;
        }

        /// <summary>脚步间隔（秒）= 每步格数 / 走速（`GameConst.PlayerWalkSpeed`）。</summary>
        public static float FootstepIntervalSeconds => TilesPerFootstep / GameConst.PlayerWalkSpeed;

        // ═════════════════════════════════════════════════════════════════════
        // 订阅 / 注销（**一律用具名私有方法**：`Game.Event` 没有句柄，匿名 lambda 注销不掉）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>接线（幂等）。`Game.Event` 未挂载时只报一次 Warn 并返回（降级：只剩直连调用出声）。</summary>
        public void Attach()
        {
            if (_subscribed) return;

            var bus = Game.Event;
            if (bus == null)
            {
                AudioLog.NoEventBus();
                return;
            }

            _subscribed = true;

            bus.On<Vector2Int>(Events.PlayerGridChanged, OnPlayerGridChanged);      // 脚步

            bus.On<ItemStack>(Events.ItemPicked, OnItemPicked);                     // 拾取
            bus.On<ItemStack>(Events.ItemUsed, OnItemUsed);                         // 使用物品

            bus.On<int>(Events.LevelUp, OnLevelUp);                                 // 升级
            bus.On<int>(Events.QuestCompleted, OnQuestCompleted);                   // 任务完成
            bus.On(Events.Revived, OnRevived);                                      // 复活（完成）

            bus.On<string>(Events.PanelToggleRequest, OnPanelToggleRequest);        // UI 点击
            bus.On<int>(Events.DialogOptionChosen, OnDialogOptionChosen);           // 对话选项点击
            bus.On<ShopTradeArgs>(Events.ShopBuyRequest, OnShopTradeRequest);       // 买入按钮
            bus.On<ShopTradeArgs>(Events.ShopSellRequest, OnShopTradeRequest);      // 卖出按钮
            bus.On<NpcDialogArgs>(Events.DialogOpen, OnDialogOpen);                 // 对话开始
            bus.On<ShopOpenArgs>(Events.ShopOpen, OnShopOpen);                      // 商店打开

            bus.On(Events.StageEntered, OnStageEntered);                            // 进图
            bus.On(Events.StageLeft, OnStageLeft);                                  // 离场
            bus.On<AreaId>(Events.ExitEntered, OnExitEntered);                      // 传送
            bus.On<AreaId>(Events.AreaChanged, OnAreaChanged);                      // BGM 切区域

            bus.On<string>(Events.PlaySfx, OnPlaySfxRequest);                       // 统一音效入口
            bus.On<string>(Events.PlayBgm, OnPlayBgmRequest);                       // 统一 BGM 入口
            bus.On<AudioVolumeArgs>(Events.VolumeChanged, OnVolumeChanged);         // 音量同步

            AudioLog.Info("音效触发点已接线：脚步/拾取/使用/升级/任务/复活/UI/对话/商店/进图/传送/BGM/音量");
        }

        /// <summary>注销全部订阅（用**同一方法引用**；`Detach` 幂等）。</summary>
        public void Detach()
        {
            if (!_subscribed) return;

            var bus = Game.Event;
            _subscribed = false;
            if (bus == null) return;

            bus.Off<Vector2Int>(Events.PlayerGridChanged, OnPlayerGridChanged);

            bus.Off<ItemStack>(Events.ItemPicked, OnItemPicked);
            bus.Off<ItemStack>(Events.ItemUsed, OnItemUsed);

            bus.Off<int>(Events.LevelUp, OnLevelUp);
            bus.Off<int>(Events.QuestCompleted, OnQuestCompleted);
            bus.Off(Events.Revived, OnRevived);

            bus.Off<string>(Events.PanelToggleRequest, OnPanelToggleRequest);
            bus.Off<int>(Events.DialogOptionChosen, OnDialogOptionChosen);
            bus.Off<ShopTradeArgs>(Events.ShopBuyRequest, OnShopTradeRequest);
            bus.Off<ShopTradeArgs>(Events.ShopSellRequest, OnShopTradeRequest);
            bus.Off<NpcDialogArgs>(Events.DialogOpen, OnDialogOpen);
            bus.Off<ShopOpenArgs>(Events.ShopOpen, OnShopOpen);

            bus.Off(Events.StageEntered, OnStageEntered);
            bus.Off(Events.StageLeft, OnStageLeft);
            bus.Off<AreaId>(Events.ExitEntered, OnExitEntered);
            bus.Off<AreaId>(Events.AreaChanged, OnAreaChanged);

            bus.Off<string>(Events.PlaySfx, OnPlaySfxRequest);
            bus.Off<string>(Events.PlayBgm, OnPlayBgmRequest);
            bus.Off<AudioVolumeArgs>(Events.VolumeChanged, OnVolumeChanged);
        }

        // ═════════════════════════════════════════════════════════════════════
        // Tick / 节流状态
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 每帧推进脚步节流。**只有本帧发生过格变化才累积时间** ⇒ 静止永远不出脚步（硬要求 ③）。
        /// </summary>
        public void Tick(float dt)
        {
            if (_movedSinceTick)
            {
                _stepAccum += dt;
                if (_stepAccum >= FootstepIntervalSeconds)
                {
                    _stepAccum = 0f;
                    var w = Iso.GridToWorld(_lastGrid);
                    _audio.SfxAt(SfxRegistry.Footstep, w.x, w.y, w.z);
                }
            }
            else
            {
                // 静止：清零，避免"站了一会儿刚起步就立刻响一声"。
                _stepAccum = 0f;
            }

            _movedSinceTick = false;
        }

        /// <summary>清掉移动节流状态（进图/传送落位、离场复位用：那一次格变化不算脚步）。</summary>
        public void ResetMotion()
        {
            _movedSinceTick = false;
            _stepAccum = 0f;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 事件处理（具名私有方法 = 能被 Off 掉）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>格变化（`IPlayerModule.Tick` 里 `_motor.Grid` 变了才发）⇒ 本帧"在动"。</summary>
        private void OnPlayerGridChanged(Vector2Int grid)
        {
            _lastGrid = grid;
            _movedSinceTick = true;
        }

        /// <summary>拾取（金币走另一键，原版两种"叮"是不同的）。</summary>
        private void OnItemPicked(ItemStack item)
        {
            if (item == null)
            {
                AudioLog.NullPayload("ItemStack @" + Events.ItemPicked);
                return;
            }
            _audio.Sfx(item.isGold ? SfxRegistry.GoldPickup : SfxRegistry.ItemPickup);
        }

        /// <summary>使用物品（药水 / 卷轴）。</summary>
        private void OnItemUsed(ItemStack item)
        {
            if (item == null)
            {
                AudioLog.NullPayload("ItemStack @" + Events.ItemUsed);
                return;
            }
            _audio.Sfx(SfxRegistry.ItemUse);
        }

        /// <summary>升级。</summary>
        private void OnLevelUp(int newLevel) => _audio.Sfx(SfxRegistry.LevelUp);

        /// <summary>任务完成。</summary>
        private void OnQuestCompleted(int questId) => _audio.Sfx(SfxRegistry.QuestComplete);

        /// <summary>
        /// 复活完成（`App/AppEventRouting.cs` 在 `ReviveRequest` 被处理完且玩家不再死亡时广播）。
        /// ⚠️ 不用 `Events.ReviveRequest`：那是"点击请求"，玩家点了却没复活成功（例如未死亡）时不该出声。
        /// </summary>
        private void OnRevived() => _audio.Sfx(SfxRegistry.PlayerRevive);

        /// <summary>UI 面板开关请求 ⇒ 一次点击音。</summary>
        private void OnPanelToggleRequest(string panelName) => _audio.Sfx(SfxRegistry.UiClick);

        /// <summary>对话选项点击 ⇒ 一次点击音。</summary>
        private void OnDialogOptionChosen(int optionIndex) => _audio.Sfx(SfxRegistry.UiClick);

        /// <summary>买卖按钮（买/卖共用）。</summary>
        private void OnShopTradeRequest(ShopTradeArgs args)
        {
            if (args == null)
            {
                AudioLog.NullPayload("ShopTradeArgs @ " + Events.ShopBuyRequest);
                return;
            }
            _audio.Sfx(SfxRegistry.UiClick);
        }

        /// <summary>NPC 对话开始。</summary>
        private void OnDialogOpen(NpcDialogArgs args)
        {
            if (args == null)
            {
                AudioLog.NullPayload("NpcDialogArgs @" + Events.DialogOpen);
                return;
            }
            _audio.Sfx(SfxRegistry.DialogOpen);
        }

        /// <summary>商店打开。</summary>
        private void OnShopOpen(ShopOpenArgs args)
        {
            if (args == null)
            {
                AudioLog.NullPayload("ShopOpenArgs @" + Events.ShopOpen);
                return;
            }
            _audio.Sfx(SfxRegistry.ShopOpen);
        }

        /// <summary>踩出入口 / 传送（Flow 收到后才真正切区域，这里先出一声）。</summary>
        private void OnExitEntered(AreaId target) => _audio.Sfx(SfxRegistry.Portal);

        /// <summary>区域切换 ⇒ 切 BGM（Town / BloodMoor / DenOfEvil 各一首）。</summary>
        private void OnAreaChanged(AreaId to)
        {
            _sawAreaChanged = true;
            _area = to;
            ResetMotion();                       // 换区域会把玩家传走 ⇒ 那一次格变化不算脚步
            PlayAreaBgm();
        }

        /// <summary>进图（Stage 装配完毕）⇒ 进图音 + 按当前区域起 BGM。</summary>
        private void OnStageEntered()
        {
            ResetMotion();                       // 进图落位的那一次格变化不算脚步
            _audio.Sfx(SfxRegistry.AreaEnter);

            if (!_sawAreaChanged) AudioLog.NoAreaChanged();
            PlayAreaBgm();
        }

        /// <summary>按当前区域切 BGM（未登记的区域只报一次 Warn，不切歌）。</summary>
        private void PlayAreaBgm()
        {
            var key = SfxRegistry.BgmKeyOf(_area);
            if (key == null)
            {
                AudioLog.UnknownArea(_area);
                return;
            }
            _audio.Bgm(key);
        }

        /// <summary>离场（BGM 停播由 Flow 清场的 `Game.Sound.StopAll` + `AudioModule.Reset` 负责，这里只清节流状态）。</summary>
        private void OnStageLeft() => ResetMotion();

        /// <summary>`Events.PlaySfx`（别处想放音效又不想直连音频接口时的统一入口）。</summary>
        private void OnPlaySfxRequest(string key) => _audio.Sfx(key);

        /// <summary>`Events.PlayBgm`（统一 BGM 入口；空串 = 停）。</summary>
        private void OnPlayBgmRequest(string key) => _audio.Bgm(key);

        /// <summary>音量变化（设置面板已自行落盘）⇒ 同步缓存并施加到引擎。</summary>
        private void OnVolumeChanged(AudioVolumeArgs args) => _audio.SyncVolumeFromEvent(args);
    }
}
