// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · App/AppContext.cs
// **模块注册表（组合根）**：`App/Bootstrap` 装配、`Module/*` 只读。
//
// 为什么这么设计（与 `tools/ai-skill/conventions.md`「跨模块协作只走事件或 App 注入的接口」一致）：
//   · 模块之间**不许互相 using**；要协作就经这里注入的**接口**（`Module/Contracts.cs`）
//     或 `Core/Events.cs` 的事件；
//   · 字段全部是**门面接口**（照 `tools/ai-skill/registry.md` 的模块表），没有一个是实现类型。
//
// 装配方式：`AutoWire()` 在**本程序集**里为每个门面接口找唯一实现（`internal class X : IX`）
// 并实例化。这样做的原因：模块由多个实现者并行开发，**Bootstrap 不必随每个新模块改动**
// （否则每加一个模块都要改一次 App 装配，迟早漏接）。找不到实现 ⇒ 留 null 并打日志（降级，不崩）。
// `IAppFlow` **不参与 AutoWire**：`AppFlow` 是长驻编排器（构造即订阅事件），
//    自动再 new 一个会导致**事件被处理两次** ⇒ 只允许 Bootstrap 显式 new。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Module;
using Diablo2.Module.Flow;
using UnityEngine;

namespace Diablo2.App
{
    /// <summary>模块注册表（全局唯一实例：<see cref="I"/>）。</summary>
    internal sealed class AppContext
    {
        /// <summary>全局实例（`Bootstrap` 在 `Game.Launch` 之后创建）。</summary>
        public static AppContext I { get; private set; }

        // ── 模块门面（照 registry.md「客户端模块」表，全部字段）────────────────
        /// <summary>流程编排（启动→主菜单→选角→创角→读条→进图→暂停→回菜单）。</summary>
        public IAppFlow Flow;

        /// <summary>格子地图（随机生成 / 可走 / A* / 等距投影）。</summary>
        public IMapModule Map;

        /// <summary>主角（点击移动 / 属性 / 装备生效）。</summary>
        public IPlayerModule Player;

        /// <summary>战斗结算（伤害 / 命中 / 抗性 / 死亡）。</summary>
        public ICombatModule Combat;

        /// <summary>怪物 AI 与精英词缀。</summary>
        public IMonsterModule Monster;

        /// <summary>技能树 / 学习 / 施放 / 投射物。</summary>
        public ISkillModule Skill;

        /// <summary>物品（掉落 / 背包 / 装备 / 腰带 / 金币）。</summary>
        public IItemModule Item;

        /// <summary>任务状态机（邪恶洞穴）。</summary>
        public IQuestModule Quest;

        /// <summary>NPC / 对话 / 商店 / 修理。</summary>
        public INpcModule Npc;

        /// <summary>等距跟随相机。</summary>
        public ICameraRig Camera;

        /// <summary>精灵视图（朝向 / 动画 / 血条 / 飘字）。</summary>
        public IViewModule View;

        /// <summary>音效触发点。</summary>
        public IAudioModule Audio;

        /// <summary>存档（多角色，走 `Game.Setting`）。</summary>
        public ISaveModule Save;

        // ── 场景根节点（`Stage` 场景自带；由 `Bootstrap` 的序列化字段或 `App/StageRoots.cs` 注入）──
        // 为什么要经 AppContext：`IMapModule` / `IViewModule` 的 `AttachRoot(Transform)` 是**非契约**
        // 入口，而「Stage 场景里那个 MapRoot/EntityRoot」只有场景侧能拿到（**不许 `GameObject.Find`**）。
        // 为 null ⇒ 两个模块各自 `new GameObject` 自建根（功能正常，只是场景层级不干净）。

        /// <summary>`Stage` 场景的「地图根」（`MapModule.AttachRoot` 的落点）。</summary>
        public Transform MapRoot;

        /// <summary>`Stage` 场景的「实体根」（`ViewModule.AttachRoot` 的落点）。</summary>
        public Transform EntityRoot;

        /// <summary>
        /// 新一局 Play 的静态复位（见 `Bootstrap` 的 `[RuntimeInitializeOnLoadMethod]`／skill P-3）：
        /// 域不重载时 `I` 会指向上一局那个已销毁的对象图 ⇒ 不清掉就是"接了一堆死模块"。
        /// </summary>
        internal static void ResetStaticForNewPlaySession() => I = null;

        /// <summary>创建全局实例（只有 `Bootstrap` 调）。</summary>
        public static AppContext Create()
        {
            if (I != null)
            {
                Game.Logger?.Warn("App", "AppContext.Create 被重复调用：旧实例仍在，可能是重复的 Bootstrap");
            }
            I = new AppContext();
            return I;
        }

        /// <summary>
        /// 为每个门面接口自动找实现并实例化（**只填 null 字段**，显式装配的优先）。
        /// 找不到 / 实例化失败都只打日志，不抛异常。
        /// </summary>
        public void AutoWire()
        {
            try
            {
                Wire<IMapModule>(ref Map, nameof(Map));
                Wire<IPlayerModule>(ref Player, nameof(Player));
                Wire<ICombatModule>(ref Combat, nameof(Combat));
                Wire<IMonsterModule>(ref Monster, nameof(Monster));
                Wire<ISkillModule>(ref Skill, nameof(Skill));
                Wire<IItemModule>(ref Item, nameof(Item));
                Wire<IQuestModule>(ref Quest, nameof(Quest));
                Wire<INpcModule>(ref Npc, nameof(Npc));
                Wire<ICameraRig>(ref Camera, nameof(Camera));
                Wire<IViewModule>(ref View, nameof(View));
                Wire<IAudioModule>(ref Audio, nameof(Audio));
                Wire<ISaveModule>(ref Save, nameof(Save));
            }
            catch (Exception ex)
            {
                // ReflectionTypeLoadException 等：装配失败不该让游戏起不来，但必须留下可查的日志。
                Game.Logger?.Error("App", $"AutoWire 异常：{ex.GetType().Name}: {ex.Message}", ex);
            }
        }

        private static void Wire<T>(ref T field, string name) where T : class
        {
            if (field != null)
            {
                Game.Logger?.Info("App", $"模块 {name} 已由显式装配提供：{field.GetType().FullName}");
                return;
            }

            // 反射装配收敛到引擎 `CloverEngine.ServiceAutoWire`（本程序集内按接口找唯一实现并实例化）：
            // 候选类型缓存 / 多实现可诊断警告 / 逐个降级尝试 / ReflectionTypeLoadException 兜底
            // 都在引擎侧统一（见 ServiceAutoWire 类型注释）。此处只保留本项目的日志措辞。
            T resolved;
            string error;
            if (ServiceAutoWire.TryResolve(typeof(AppContext).Assembly, out resolved, out error))
            {
                field = resolved;
                Game.Logger?.Info("App", $"模块已装配：{name} ← {resolved.GetType().FullName}");
                return;
            }

            Game.Logger?.Warn("App", $"模块 {name} 尚无实现（{error}）⇒ 相关功能降级");
        }

        /// <summary>每帧转发给已注册模块（未注册的跳过；`IQuestModule` 无 Tick）。</summary>
        public void Tick(float dt)
        {
            Player?.Tick(dt);
            Monster?.Tick(dt);
            Combat?.Tick(dt);
            Skill?.Tick(dt);
            Item?.Tick(dt);
            Npc?.Tick(dt);
            View?.Tick(dt);
            Camera?.Tick(dt);
            Audio?.Tick(dt);
        }

        /// <summary>一行装配摘要（打日志用，验收时能一眼看出哪个模块没接上）。</summary>
        public string Describe()
        {
            return "模块装配："
                + $"Flow={(Flow != null ? "ok" : "NULL")} "
                + $"Map={(Map != null ? "ok" : "NULL")} "
                + $"Player={(Player != null ? "ok" : "NULL")} "
                + $"Combat={(Combat != null ? "ok" : "NULL")} "
                + $"Monster={(Monster != null ? "ok" : "NULL")} "
                + $"Skill={(Skill != null ? "ok" : "NULL")} "
                + $"Item={(Item != null ? "ok" : "NULL")} "
                + $"Quest={(Quest != null ? "ok" : "NULL")} "
                + $"Npc={(Npc != null ? "ok" : "NULL")} "
                + $"Camera={(Camera != null ? "ok" : "NULL")} "
                + $"View={(View != null ? "ok" : "NULL")} "
                + $"Audio={(Audio != null ? "ok" : "NULL")} "
                + $"Save={(Save != null ? "ok" : "NULL")}";
        }
    }
}
