// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · App/AppSnapshots.cs      （agent-12「最后一次接线」，agent-12 §3 的第 2、3 项）
// **进图全量快照 + 面板打开前补发**。
//
// 为什么必须有这一步（否则必然出现"面板打开是空的"）：
//   面板只吃两种输入：`OnOpen(param)` 与它自己订阅的 `Events.*` 事件。
//   · `MapGenerated` 在 `Generate` 里发出，而 HUD / 小地图面板是**之后**才被创建的；
//   · `InventoryChanged` / `SkillTreeChanged` / `QuestChanged` / `HudDirty` 只在"发生变化时"发，
//     进图那一刻没有任何变化 ⇒ 不广播的话，HUD 的缓存是 null，按 I/C/T/Q 打开的面板全空。
//   ⇒ 约定：**App 在 `Events.StageEntered` 之后主动广播一次全量快照**（本文件），
//     并在 `Events.PanelToggleRequest` 到达时**再补发一次对应快照**（HUD 打开面板时把缓存传进去）。
//
// ⚠️ 唯一没被这套机制覆盖的是 `MiniMapPanel`：HUD 对它传的是 `null`（`UI/HudPanel.cs` 里
//   `Toggle<MiniMapPanel>(null)`），而面板是在 `OnOpen` 里才订阅 `MapGenerated`
//   ⇒ 本次派发它收不到。本文件用「下一帧用 `AfterUnscaled` 再发一次」补齐，并已登记为**需返工**项。
//
// ⛔ 只做搬运，不改任何模块状态（`Snapshot()` / `BuildTree()` / `BuildMinimap()` 都是只读快照）。
//
// ★ `EchoInFlight`（agent-05 按 agent-14 §B 现象 3 加）：本类发 `Events.MapGenerated` 时用的是
//   **同一张已生成的地图**（`ctx.Map.BuildMinimap()`）⇒ 那是"把图再播一次"的**回声**，不是
//   "又生成了一张图"。`App/AppDoorGuard` 靠这个标志把回声从"重复生成"的计数里剔除
//   （否则每次进图都会出现 `…（>1）⇒ 重复生成！` 的假警报）。
//   ⚠️ 本文件里**每一处** `Emit(Events.MapGenerated, …)` 都必须走 `EmitMapEcho()`，不许裸 Emit。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;      // `MinimapArgs`（`EmitMapEcho` 的入参类型）
using Diablo2.UI;

namespace Diablo2.App
{
    /// <summary>进图 / 开面板时的全量快照广播。</summary>
    internal static class AppSnapshots
    {
        private const string Tag = AppWiring.Tag;

        /// <summary>
        /// 正在派发"地图快照回声"（= 本类把**已有的**地图再播一次，见文件头 ★）。
        /// `AppDoorGuard.OnMapGenerated` 见到它为 true 就直接跳过计数。
        /// 同步 `Emit` ⇒ 这个标志在派发期间绝对可靠（不需要锁，也不会跨线程）。
        /// </summary>
        public static bool EchoInFlight { get; private set; }

        /// <summary>派发一次 `Events.MapGenerated`，并标记它是回声（**本文件唯一允许的出口**）。</summary>
        private static void EmitMapEcho(MinimapArgs map)
        {
            if (map == null || Game.Event == null) return;
            EchoInFlight = true;
            try
            {
                Game.Event.Emit(Events.MapGenerated, map);
            }
            finally
            {
                EchoInFlight = false;
            }
        }

        /// <summary>补发小地图快照的延迟（秒，走 `AfterUnscaled`）：0.05s = 下一帧，够面板走完 `OnOpen`。</summary>
        private const float DeferredDelay = 0.05f;

        private static long _deferredMapTimer;

        /// <summary>订阅「面板开关」事件（`Stage 进/出` 由 `AppWiring` 负责）。</summary>
        public static void Install(AppContext ctx)
        {
            if (Game.Event == null)
            {
                Game.Logger.Error(Tag, "AppSnapshots.Install：Game.Event 为 null ⇒ 未订阅面板快照补发");
                return;
            }
            Game.Event.On<string>(Events.PanelToggleRequest, OnPanelToggleRequest);
        }

        /// <summary>
        /// 新一局 Play 的静态复位（见 `Bootstrap` 的 `[RuntimeInitializeOnLoadMethod]`／skill P-3）：
        /// 域不重载时待补发的定时器 id 与"回声进行中"标志会跨局残留。
        /// </summary>
        internal static void ResetStaticForNewPlaySession()
        {
            _deferredMapTimer = 0;
            EchoInFlight = false;
        }

        /// <summary>离场复位（停掉待补发的定时器）。</summary>
        public static void Reset()
        {
            if (_deferredMapTimer == 0) return;
            Game.Timer?.Stop(_deferredMapTimer);
            _deferredMapTimer = 0;
        }

        /// <summary>广播进图全量快照（由 `AppWiring` 在 HUD 打开**之后**调用）。</summary>
        public static void Broadcast(string reason)
        {
            var ctx = AppWiring.Ctx;
            var bus = Game.Event;
            if (ctx == null || bus == null)
            {
                Game.Logger.Error(Tag, $"BroadcastSnapshots({reason})：AppContext / Game.Event 为空 ⇒ 未广播");
                return;
            }

            var n = 0;

            if (ctx.Player != null)
            {
                bus.Emit(Events.HudDirty, ctx.Player.Snapshot());
                bus.Emit(Events.PlayerGridChanged, ctx.Player.Grid);
                n += 2;
            }
            else AppWiring.Missing("IPlayerModule");

            if (ctx.Item != null) { bus.Emit(Events.InventoryChanged, ctx.Item.Snapshot()); n++; }
            else AppWiring.Missing("IItemModule");

            if (ctx.Skill != null) { bus.Emit(Events.SkillTreeChanged, ctx.Skill.BuildTree()); n++; }
            else AppWiring.Missing("ISkillModule");

            n += EmitQuests(ctx);

            if (ctx.Map != null)
            {
                if (ctx.Map.IsGenerated) { EmitMapEcho(ctx.Map.BuildMinimap()); n++; }
                else Game.Logger.Warn(Tag, $"地图未生成 ⇒ 未广播 {Events.MapGenerated}（小地图将是空白）");
            }
            else AppWiring.Missing("IMapModule");

            Game.Logger.Info(Tag,
                $"[Stage] 全量快照已广播 {n} 条（{reason}）：{Events.HudDirty} / {Events.InventoryChanged} / " +
                $"{Events.SkillTreeChanged} / {Events.QuestChanged} / {Events.MapGenerated} / {Events.PlayerGridChanged}");
        }

        private static int EmitQuests(AppContext ctx)
        {
            if (ctx.Quest == null) { AppWiring.Missing("IQuestModule"); return 0; }

            var qs = ctx.Quest.Quests;
            if (qs == null || qs.Count == 0)
            {
                Game.Logger.Warn(Tag, $"任务模块的 Quests 为空 ⇒ 未广播 {Events.QuestChanged}（任务链装配异常？）");
                return 0;
            }

            for (var i = 0; i < qs.Count; i++) Game.Event.Emit(Events.QuestChanged, qs[i]);
            return qs.Count;
        }

        /// <summary>
        /// HUD 收到本事件后会 `Toggle&lt;T&gt;(缓存的快照)`；本类订阅得比 HUD 早（HUD 在 `Awake` 才订阅）
        /// ⇒ 本回调先跑，把**新鲜**快照灌进 HUD 的缓存，面板打开时就不是旧数据。
        /// </summary>
        private static void OnPanelToggleRequest(string panelName)
        {
            var ctx = AppWiring.Ctx;
            if (ctx == null) return;
            if (!AppWiring.StageActive) return;          // 非 Stage 内的面板开关无需快照

            switch (panelName)
            {
                case nameof(InventoryPanel):
                    if (ctx.Item != null) Game.Event.Emit(Events.InventoryChanged, ctx.Item.Snapshot());
                    else AppWiring.Missing("IItemModule");
                    break;

                case nameof(CharacterPanel):
                    if (ctx.Player != null) Game.Event.Emit(Events.HudDirty, ctx.Player.Snapshot());
                    else AppWiring.Missing("IPlayerModule");
                    break;

                case nameof(SkillTreePanel):
                    if (ctx.Skill != null) Game.Event.Emit(Events.SkillTreeChanged, ctx.Skill.BuildTree());
                    else AppWiring.Missing("ISkillModule");
                    break;

                case nameof(QuestLogPanel):
                    EmitQuests(ctx);
                    break;

                case nameof(MiniMapPanel):
                    // HUD 对小地图传的是 null ⇒ 刚创建的那个收不到本次派发（见文件头 ⚠️）
                    if (ctx.Map == null) { AppWiring.Missing("IMapModule"); break; }
                    if (!ctx.Map.IsGenerated)
                    {
                        Game.Logger.Warn(Tag, "小地图被请求打开，但地图未生成 ⇒ 面板会显示空白");
                        break;
                    }
                    EmitMapEcho(ctx.Map.BuildMinimap());
                    ScheduleDeferredMapSnapshot();
                    break;

                case nameof(HudPanel):
                case nameof(PausePanel):
                case nameof(DeathPanel):
                case nameof(NpcDialogPanel):
                case nameof(ShopPanel):
                    break;      // 数据由这些面板自己订阅的事件带（DialogOpen / ShopOpen / PlayerDied …）

                default:
                    Game.Logger.Warn(Tag, $"`{Events.PanelToggleRequest}` 收到未知面板名「{panelName}」⇒ 未补发任何快照");
                    break;
            }
        }

        private static void ScheduleDeferredMapSnapshot()
        {
            if (Game.Timer == null)
            {
                Game.Logger.Warn(Tag, "Game.Timer 为 null（引擎未启动）⇒ 无法补发小地图快照（面板会空白）");
                return;
            }
            if (_deferredMapTimer != 0) Game.Timer.Stop(_deferredMapTimer);
            _deferredMapTimer = Game.Timer.AfterUnscaled(DeferredDelay, EmitDeferredMapSnapshot);
        }

        private static void EmitDeferredMapSnapshot()
        {
            _deferredMapTimer = 0;
            var ctx = AppWiring.Ctx;
            if (ctx?.Map == null || !ctx.Map.IsGenerated) return;
            EmitMapEcho(ctx.Map.BuildMinimap());
            Game.Logger.Info(Tag, $"小地图快照补发（下一帧）：刚打开的面板已订完 {Events.MapGenerated}，能收到");
        }
    }
}
