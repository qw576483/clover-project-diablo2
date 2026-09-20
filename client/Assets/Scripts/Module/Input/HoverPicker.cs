// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Input/HoverPicker.cs
// **悬停目标解析器**：把「鼠标所在格」翻译成 `Def.HoverTarget`（怪物 / 地面物品 / NPC / 无）。
//
// 为什么单列一个文件：`InputReader` 只读输入设备；「这一格上有什么」属于**查询其它模块的数据**，
// 是独立职责。放在 `Module/Input/` 下（同目录），与 `InputReader` 一起构成输入/交互层。
//
// ── 分层：为什么**不用** `using Module.Monster`（agent-13 §A 第 1 项）────────────────
//   `IMonsterModule` / `INpcModule` / `IItemModule` / `IViewModule` 都声明在
//   `Module/Contracts.cs` 的命名空间 **`Diablo2.Module`** 里（接口 = 契约，不是实现）。
//   本文件所在命名空间就是 `Diablo2.Module` ⇒ 引用这些**接口**不需要任何 `using`，
//   实例则经组合根 `AppContext.I`（App 注入接口）取得 —— 与 `PlayerModule.MapOrNull` /
//   `CameraRig.MapOrNull` 完全同一套做法（`tools/ai-skill/conventions.md`
//   「跨模块协作只走事件或 App 注入的接口」）。
//   ⇒ 因此本文件**没有任何跨模块的具体命名空间 import**（只有接口来自契约，实例走 AppContext），
//     分层自检 ②（正则匹配「import 某个 `Diablo2` 子模块命名空间」）**仍无新增真命中**，
//     且**不需要**改 `App/**`。
//
// ⛔ 契约缺口（**已回报主 agent**）：`IItemModule.GroundItems` 只给 `(地面物品 id → ItemStack)`，
//    **不含格坐标** ⇒ 无法只用契约接口判断「某格上是哪个地面物品」。
//    故地面物品这一路走**可注入的查询委托** <see cref="GroundItemAt"/>：
//    默认实现经 `IViewModule.GetView(id).transform.position`（= `Iso.GridToWorld(格)`）反推格，
//    Play 下有效；离线自检宿主注入替身（见 `tools/playercheck`）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
// 别名：`AppContext` 与 BCL 的 `System.AppContext` 同名（同时 using System 会 CS0104）。
using AppContext = Diablo2.App.AppContext;

namespace Diablo2.Module
{
    /// <summary>一次「悬停命中」的结果（怪物 / 地面物品 / NPC 共用；<see cref="id"/> &lt; 0 = 未命中）。</summary>
    internal struct HoverHit
    {
        /// <summary>目标 id（怪物 id / 地面物品 id / NPC id）。</summary>
        public int id;

        /// <summary>目标显示名（tooltip / 日志用；可为空串）。</summary>
        public string name;
    }

    /// <summary>悬停目标解析器（纯查询，**不发事件、不改变状态**）。</summary>
    internal sealed class HoverPicker
    {
        /// <summary>日志 tag（与 `InputReader` 同用 `Input`，已在 `Core/Log.cs` 白名单内）。</summary>
        private const string Tag = "Input";

        private bool _noViewLogged;

        /// <summary>
        /// 「某格上是哪个地面物品」查询（**本项目新增的非契约注入点**）。
        /// 默认 = <see cref="GroundItemAtViaView"/>；离线宿主可整体替换。
        /// 返回 <c>null</c> / <c>id &lt; 0</c> 表示该格没有地面物品。
        /// </summary>
        public Func<Vector2Int, HoverHit?> GroundItemAt { get; set; }

        /// <summary>构造：装上默认的地面物品查询实现。</summary>
        public HoverPicker()
        {
            GroundItemAt = GroundItemAtViaView;
        }

        /// <summary>
        /// 解析某格的悬停目标（优先级：怪物 → 地面物品 → NPC → 空地）。
        /// 空地上若不可走 ⇒ 光标 `NoWalk`；否则 `Default`。**永不抛异常**（接口缺失只降级）。
        /// </summary>
        public HoverTarget Resolve(Vector2Int grid)
        {
            var t = new HoverTarget
            {
                hasTarget = false,
                cursor = CursorKind.Default,
                id = -1,
                name = string.Empty,
                gridX = grid.x,
                gridY = grid.y,
            };

            var ctx = AppContext.I;
            if (ctx == null) return t;                   // 组合根未装配：无目标（不刷日志，调用方已报过）

            var map = ctx.Map;

            // ① 怪物（`MonsterState` 自带 gridX/gridY —— 契约接口即可判定，无需遍历实现类型）
            var monster = ctx.Monster;
            var allMon = monster != null ? monster.All : null;
            if (allMon != null)
            {
                for (var i = 0; i < allMon.Count; i++)
                {
                    var m = allMon[i];
                    if (m == null || !m.alive) continue;
                    if (m.gridX != grid.x || m.gridY != grid.y) continue;
                    t.hasTarget = true;
                    t.cursor = CursorKind.Attack;
                    t.id = m.id;
                    t.name = m.name;
                    return t;
                }
            }

            // ② 地面物品（经注入的查询委托；默认实现见 GroundItemAtViaView）
            var probe = GroundItemAt;
            if (probe != null)
            {
                HoverHit? hit = null;
                try
                {
                    hit = probe(grid);
                }
                catch (Exception e)
                {
                    // 注入方（含 View 读取）出问题不该让输入层炸掉：只报一次并当作"没有地面物品"
                    if (!_noViewLogged)
                    {
                        _noViewLogged = true;
                        Log.Warn(Tag, $"地面物品悬停查询抛异常（{e.GetType().Name}: {e.Message}）⇒ 本格按无地面物品处理（只报一次）");
                    }
                }

                if (hit.HasValue && hit.Value.id >= 0)
                {
                    t.hasTarget = true;
                    t.cursor = CursorKind.Pickup;
                    t.id = hit.Value.id;
                    t.name = hit.Value.name;
                    return t;
                }
            }

            // ③ NPC（`NpcDef` 自带 gridX/gridY/areaId；须同区域 —— 城镇 NPC 定义常驻，别在野外误判）
            var npc = ctx.Npc;
            var allNpc = npc != null ? npc.All : null;
            if (allNpc != null && map != null && map.IsGenerated)
            {
                for (var i = 0; i < allNpc.Count; i++)
                {
                    var d = allNpc[i];
                    if (d == null) continue;
                    if (d.gridX != grid.x || d.gridY != grid.y) continue;
                    if (d.areaId != (int)map.Area) continue;      // 非同区域不算命中
                    t.hasTarget = true;
                    t.cursor = CursorKind.Interact;
                    t.id = d.id;
                    t.name = d.name;
                    return t;
                }
            }

            // ④ 空地：不可走 ⇒ NoWalk，否则 Default
            if (map != null && map.IsGenerated && !map.Walkable(grid)) t.cursor = CursorKind.NoWalk;
            return t;
        }

        /// <summary>
        /// 默认的地面物品查询：经 `IViewModule.GetView(地面物品 id)` 的世界坐标反推格
        /// （`ViewModule.CreateGroundItem` 把节点摆在 `Iso.GridToWorld(grid)`）。
        /// `IItemModule.GroundItems` 提供 id 列表（契约接口），`IViewModule` 提供格。
        /// 两者缺一 / 无渲染能力（离线进程）⇒ 返回 null 并只报一次 Info。
        /// </summary>
        private HoverHit? GroundItemAtViaView(Vector2Int grid)
        {
            var ctx = AppContext.I;
            if (ctx == null) return null;

            var item = ctx.Item;
            var view = ctx.View;
            if (item == null || view == null)
            {
                if (!_noViewLogged)
                {
                    _noViewLogged = true;
                    Log.Info(Tag, "地面物品悬停：IItemModule / IViewModule 未接入 ⇒ 该项暂不可判（只报一次，" +
                                  "不影响怪物与 NPC 的悬停）");
                }
                return null;
            }

            var list = item.GroundItems;
            if (list == null) return null;

            for (var i = 0; i < list.Count; i++)
            {
                var kv = list[i];
                if (kv.Key < 0) continue;

                var go = view.GetView(kv.Key);
                if (go == null) continue;                     // 该 id 尚无视图（离线进程/未建节点）

                Vector3 p;
                try
                {
                    p = go.transform.position;
                }
                catch (Exception e)
                {
                    if (!_noViewLogged)
                    {
                        _noViewLogged = true;
                        Log.Warn(Tag, $"读取地面物品视图世界坐标失败（{e.GetType().Name}: {e.Message}）⇒ 该项不可判（只报一次）");
                    }
                    return null;
                }

                if (Iso.WorldToGrid(p) != grid) continue;

                var stack = kv.Value;
                return new HoverHit
                {
                    id = kv.Key,
                    name = stack != null ? stack.name : go.name,
                };
            }

            return null;
        }
    }
}
