// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Input/HoverPicker.cs
// **悬停目标解析器**：把「鼠标所在格」翻译成 `Def.HoverTarget`（怪物 / 地面物品 / NPC / 无）。
//
// 为什么单列一个文件：`InputReader` 只读输入设备；「这一格上有什么」属于**查询其它模块的数据**，
// 是独立职责。放在 `Module/Input/` 下（同目录），与 `InputReader` 一起构成输入/交互层。
//
//   `IMonsterModule` / `INpcModule` / `IItemModule` / `IViewModule` 都声明在
//   `Module/Contracts.cs` 的命名空间 **`Diablo2.Module`** 里（接口 = 契约，不是实现）。
//   本文件所在命名空间就是 `Diablo2.Module` ⇒ 引用这些**接口**不需要任何 `using`，
//   实例则经组合根 `AppContext.I`（App 注入接口）取得 —— 与 `PlayerModule.MapOrNull` /
//   `CameraRig.MapOrNull` 完全同一套做法（`tools/ai-skill/conventions.md`
//   「跨模块协作只走事件或 App 注入的接口」）。
//   ⇒ 因此本文件**没有任何跨模块的具体命名空间 import**（只有接口来自契约，实例走 AppContext）。
//
// 契约缺口：`IItemModule.GroundItems` 只给 `(地面物品 id → ItemStack)`，
//    **不含格坐标** ⇒ 无法只用契约接口判断「某格上是哪个地面物品」。
//    故地面物品这一路走**可注入的查询委托** <see cref="GroundItemAt"/>：
//    默认实现经 `IViewModule.GetView(id).transform.position`（= `Iso.GridToWorld(格)`）反推格，
//    Play 下有效；离线自检宿主注入替身（见 `tools/probes/hosts/playercheck`）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
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

        /// <summary>`[Input]` 的「怪物贴图矩形查询异常」只报一次（见 `Resolve` 的 ①b）。</summary>
        private bool _noSpriteRectLogged;

        /// <summary>
        /// 「某格上是哪个地面物品」查询（**本项目新增的非契约注入点**）。
        /// 默认 = <see cref="GroundItemAtViaView"/>；离线宿主可整体替换。
        /// 返回 <c>null</c> / <c>id &lt; 0</c> 表示该格没有地面物品。
        /// </summary>
        public Func<Vector2Int, HoverHit?> GroundItemAt { get; set; }

        /// <summary>
        /// 「当前区域**全部**地面物品名牌」查询（非契约注入点；原版 `Alt` 常显用）。
        /// <para>默认 = <see cref="AllLabelsViaView"/>（经 `IItemModule.GroundItems` + `IViewModule.GetView`
        /// 反推格，与 <see cref="GroundItemAtViaView"/> 同一套数据来源）；离线宿主可整体替换。</para>
        /// <para>永不抛异常由**调用方**兜（`InputReader.PublishGroundItemLabels` 有 try/catch）。</para>
        /// </summary>
        public Func<List<Diablo2.Def.GroundItemLabel>> AllLabels { get; set; }

        /// <summary>
        /// 「地面物品 id → 品质」查询（非契约注入点；名牌配色用）。
        /// <para>为什么要它：`HoverTarget`（悬停载荷）里**没有**品质字段 ⇒ 悬停单件时靠它补配色；
        /// 默认 = <see cref="QualityOfViaItem"/>（查 `IItemModule.GroundItems`，O(1) 字典查找）。</para>
        /// </summary>
        public Func<int, ItemQuality> ItemQualityOf { get; set; }

        /// <summary>
        /// 「怪物 id → 该怪物**贴图在世界 xy 平面上的实际包围矩形**」查询
        /// （非契约注入点；悬停怪物按**精灵矩形**命中用）。
        /// <para>为什么要有它（实机实测依据）：`Resolve(grid)` 原来只接受「鼠标解出的格 == 怪物脚下格」，
        /// 而怪物精灵在屏幕上**向上覆盖 1.5~2 格**（实测：鼠标从怪 (15,53) 的脚下沿屏幕上移
        /// 0/24/48/72px 仍解出 (15,53) 命中；**96px 起**解出 (14,52) ⇒ 上半身完全无反馈）。
        /// 原版的命中语义是**精灵覆盖到就算悬停到**（不是"脚下格相等"）。</para>
        /// <para>默认 = <see cref="MonsterSpriteRectViaView"/>（经 `IViewModule.GetView(id)` 的
        /// `SpriteRenderer.bounds`）；离线宿主可整体替换（注入假矩形 ⇒ 可离线断言）。
        /// 返回 <c>null</c> = 该怪当前没有可用贴图（未建节点/异步未加载/离线进程）⇒
        /// **退回脚下格口径**（只认脚下格），不做任何"猜一个矩形"的兜底。</para>
        /// <para>这是**贴图实际矩形**，不是本项目自创的"命中半径/阈值" —— 见 <see cref="Resolve(Vector2Int, Vector2)"/>。</para>
        /// </summary>
        public Func<int, Rect?> MonsterSpriteRect { get; set; }

        /// <summary>
        /// 「NPC id → 该 NPC **贴图在世界 xy 平面上的实际包围矩形**」查询
        /// （非契约注入点；悬停/点击 NPC 按**精灵矩形**命中用）。
        /// <para>口径与 <see cref="MonsterSpriteRect"/> **完全同一套**（原版 D2 的悬停/点击命中
        /// 是按精灵在屏幕上的实际覆盖做的，不是按格）：NPC 精灵在屏幕上向上覆盖 1~2 格，
        /// 只认脚下格会让点它上半身没反应；反过来，"点在 NPC 附近就算点到"（按距离判）
        /// 会让只是路过 NPC 旁边的一次点击也弹对话窗。</para>
        /// <para>默认 = <see cref="NpcSpriteRectViaView"/>（经 `IViewModule.GetView` 的
        /// `SpriteRenderer.bounds`）；离线宿主可整体替换（注入假矩形 ⇒ 可离线断言）。
        /// 返回 <c>null</c> = 该 NPC 当前没有可用贴图（非城镇未建节点/异步未加载/离线进程）
        /// ⇒ **退回脚下格口径**（只认 NPC 所在格），不做任何"猜一个矩形"的兜底。</para>
        /// </summary>
        public Func<int, Rect?> NpcSpriteRect { get; set; }

        /// <summary>构造：装上默认的地面物品 / 贴图矩形查询实现。</summary>
        public HoverPicker()
        {
            GroundItemAt = GroundItemAtViaView;
            AllLabels = AllLabelsViaView;
            ItemQualityOf = QualityOfViaItem;
            MonsterSpriteRect = MonsterSpriteRectViaView;
            NpcSpriteRect = NpcSpriteRectViaView;
        }

        /// <summary>
        /// 解析某格的悬停目标（**脚下格口径**：怪物只认脚下格 == 该格）。
        /// 优先级：怪物 → 地面物品 → NPC → 空地。空地上若不可走 ⇒ 光标 `NoWalk`；否则 `Default`。
        /// **永不抛异常**（接口缺失只降级）。
        /// <para>保留它 = 离线宿主（`tools/probes/hosts/*`）与任何"只有格、没有鼠标世界点"的调用方
        /// 用这一版（贴图矩形口径只在新增的两参重载里生效）。</para>
        /// </summary>
        public HoverTarget Resolve(Vector2Int grid)
        {
            return Resolve(grid, Vector2.zero, false);
        }

        /// <summary>
        /// 解析悬停目标（**贴图矩形口径**：怪物与 NPC 除"脚下格相等"外，
        /// **贴图矩形覆盖到鼠标世界点**也算命中）。
        /// <para>原版口径与出处：D2 的悬停/点击命中是**按精灵在屏幕上的实际矩形**做的（不是按格），
        /// 命中判定用 `MonsterSpriteRect(id).Contains(world)` / `NpcSpriteRect(id).Contains(world)`，
        /// 矩形来自 `SpriteRenderer.bounds` —— **没有任何自创常数**（不许写"命中半径 N 格/像素"）。</para>
        /// <para>优先级：**脚下格精确命中优先**，其次才是贴图矩形；矩形命中里取
        /// 「离悬停格 Chebyshev 最近 → id 升序」的那只（确定性，不受 `All` 的遍历顺序影响）。</para>
        /// </summary>
        /// <param name="grid">鼠标解出的格（`Iso.ScreenToGrid`）。</param>
        /// <param name="world">鼠标反投影到地面的**世界点**（`Iso.ScreenToWorldOnGround`）；
        /// 用世界 xy 判定（精灵是正对相机的平面片，`Bounds.Contains` 的 z 厚度不可靠 ⇒ 只比 xy）。</param>
        public HoverTarget Resolve(Vector2Int grid, Vector2 world)
        {
            return Resolve(grid, world, true);
        }

        private HoverTarget Resolve(Vector2Int grid, Vector2 world, bool useSpriteRect)
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

            // ①b 怪物贴图矩形（原版命中语义 = 精灵覆盖到就算悬停到；只有拿到了鼠标世界点时才启用）
            //     为什么放在 ① 之后：脚下格精确命中永远优先。
            if (useSpriteRect)
            {
                var rectOf = MonsterSpriteRect;
                var best = (MonsterState)null;
                var bestDist = int.MaxValue;
                if (rectOf != null && allMon != null)
                {
                    for (var i = 0; i < allMon.Count; i++)
                    {
                        var m = allMon[i];
                        if (m == null || !m.alive) continue;

                        Rect? r;
                        try
                        {
                            r = rectOf(m.id);
                        }
                        catch (Exception e)
                        {
                            // 贴图查询（含 GetView / bounds 读取）出问题不该让输入层炸掉：只报一次并整体退回脚下格口径
                            if (!_noSpriteRectLogged)
                            {
                                _noSpriteRectLogged = true;
                                Log.Warn(Tag, $"怪物贴图矩形查询抛异常（{e.GetType().Name}: {e.Message}）⇒ 悬停退回「脚下格相等」口径（只报一次）");
                            }
                            break;
                        }

                        if (!r.HasValue || !r.Value.Contains(world)) continue;

                        var d = Mathf.Max(Mathf.Abs(m.gridX - grid.x), Mathf.Abs(m.gridY - grid.y));
                        if (d < bestDist || (d == bestDist && best != null && m.id < best.id))
                        {
                            best = m;
                            bestDist = d;
                        }
                    }
                }

                if (best != null)
                {
                    t.hasTarget = true;
                    t.cursor = CursorKind.Attack;
                    t.id = best.id;
                    t.name = best.name;
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

                // ③b NPC 贴图矩形（同上 ①b：精灵覆盖到就算悬停到；只有拿到了鼠标世界点时才启用）
                //     为什么放在 ③ 之后：脚下格精确命中永远优先。
                if (useSpriteRect)
                {
                    var rectOf = NpcSpriteRect;
                    var best = (NpcDef)null;
                    var bestDist = int.MaxValue;
                    if (rectOf != null)
                    {
                        for (var i = 0; i < allNpc.Count; i++)
                        {
                            var d = allNpc[i];
                            if (d == null) continue;
                            if (d.areaId != (int)map.Area) continue;   // 非同区域不算命中

                            Rect? r;
                            try
                            {
                                r = rectOf(d.id);
                            }
                            catch (Exception e)
                            {
                                // 贴图查询（含 GetView / bounds 读取）出问题不该让输入层炸掉：只报一次并整体退回脚下格口径
                                if (!_noSpriteRectLogged)
                                {
                                    _noSpriteRectLogged = true;
                                    Log.Warn(Tag, $"NPC 贴图矩形查询抛异常（{e.GetType().Name}: {e.Message}）⇒ 悬停退回「脚下格相等」口径（只报一次）");
                                }
                                break;
                            }

                            if (!r.HasValue || !r.Value.Contains(world)) continue;

                            var d2 = Mathf.Max(Mathf.Abs(d.gridX - grid.x), Mathf.Abs(d.gridY - grid.y));
                            if (d2 < bestDist || (d2 == bestDist && best != null && d.id < best.id))
                            {
                                best = d;
                                bestDist = d2;
                            }
                        }
                    }

                    if (best != null)
                    {
                        t.hasTarget = true;
                        t.cursor = CursorKind.Interact;
                        t.id = best.id;
                        t.name = best.name;
                        return t;
                    }
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

        /// <summary>
        /// 默认的「全部地面物品名牌」实现（原版 `Alt` 常显）：
        /// `IItemModule.GroundItems`（id → ItemStack，含名字/品质）+ `IViewModule.GetView(id)`
        /// 的世界坐标反推格（与 <see cref="GroundItemAtViaView"/> 同一口径）。
        /// <para>接口缺失 / 无渲染能力（离线进程）⇒ 返回空列表并只报一次 Info（不抛）。</para>
        /// </summary>
        private List<Diablo2.Def.GroundItemLabel> AllLabelsViaView()
        {
            var list = new List<Diablo2.Def.GroundItemLabel>();

            var ctx = AppContext.I;
            if (ctx == null) return list;

            var item = ctx.Item;
            var view = ctx.View;
            if (item == null || view == null)
            {
                if (!_noViewLogged)
                {
                    _noViewLogged = true;
                    Log.Info(Tag, "Alt 常显地面物品名：IItemModule / IViewModule 未接入 ⇒ 本帧没有可显的名牌（只报一次）");
                }
                return list;
            }

            var ground = item.GroundItems;
            if (ground == null) return list;

            for (var i = 0; i < ground.Count; i++)
            {
                var kv = ground[i];
                if (kv.Key < 0) continue;

                var go = view.GetView(kv.Key);
                if (go == null) continue;                    // 该 id 尚无视图（未建节点/离线）

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
                        Log.Warn(Tag, $"Alt 常显：读取地面物品视图世界坐标失败（{e.GetType().Name}: {e.Message}）" +
                                      "⇒ 本帧该项不可显（只报一次）");
                    }
                    return list;
                }

                var grid = Iso.WorldToGrid(p);
                var stack = kv.Value;
                list.Add(new Diablo2.Def.GroundItemLabel
                {
                    id = kv.Key,
                    name = stack != null && !string.IsNullOrEmpty(stack.name) ? stack.name : go.name,
                    quality = stack != null ? stack.quality : ItemQuality.Normal,
                    gridX = grid.x,
                    gridY = grid.y,
                });
            }

            return list;
        }

        /// <summary>
        /// 默认的「怪物贴图矩形」实现：`IViewModule.GetView(怪物 id)` → `SpriteRenderer.bounds`
        /// → 取**世界 xy 平面**的包围矩形（`new Rect(min.x, min.y, size.x, size.y)`）。
        /// <para>为什么不用 `Bounds.Contains(world)`：`SpriteRenderer.bounds` 的 z 厚度极小
        /// （精灵是正对相机的平面片），而鼠标反投影得到的地面点 z 与精灵 z 不同 ⇒ `Contains` 恒假。
        /// 本项目相机固定正交 ⇒ 只比 xy 才是"精灵在屏幕上的实际覆盖"。</para>
        /// <para>拿不到（无 `IViewModule` / 无节点 / 无 sprite / 抛异常）⇒ 返回 <c>null</c>，
        /// 悬停**退回脚下格口径**，不报错、不自创兜底矩形。只对**该 id** 降级，不影响别的怪。</para>
        /// </summary>
        private Rect? MonsterSpriteRectViaView(int monsterId)
        {
            var ctx = AppContext.I;
            var view = ctx != null ? ctx.View : null;
            if (view == null) return null;

            var go = view.GetView(monsterId);
            if (go == null) return null;                      // 该怪尚无视图（离线进程 / 未建节点）

            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return null; // 贴图还没加载完（异步）⇒ 本帧退回脚下格口径

            var b = sr.bounds;
            return new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
        }

        /// <summary>
        /// 默认的「NPC 贴图矩形」实现：`IViewModule.GetView(NPC 实体 id)` → `SpriteRenderer.bounds`
        /// → 取**世界 xy 平面**的包围矩形（与 <see cref="MonsterSpriteRectViaView"/> 逐字同一套做法）。
        /// <para>NPC 视图 id 段 = `ViewModule.NpcEntityId`：`-1 - npcId`（与玩家 1 / 怪物 1000+ /
        /// 地面物品 100000+ 都不冲突）—— 这里只按该 id 段取节点，不引用任何实现类型。</para>
        /// <para>拿不到（无 `IViewModule` / 无节点 / 无 sprite / 抛异常）⇒ 返回 <c>null</c>，
        /// 悬停**退回脚下格口径**，不报错、不自创兜底矩形。只对**该 id** 降级，不影响别的 NPC。</para>
        /// </summary>
        private Rect? NpcSpriteRectViaView(int npcId)
        {
            var ctx = AppContext.I;
            var view = ctx != null ? ctx.View : null;
            if (view == null) return null;

            var go = view.GetView(-1 - npcId);
            if (go == null) return null;                      // 该 NPC 尚无视图（非城镇 / 未建节点）
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return null; // 贴图还没加载完（异步）⇒ 本帧退回脚下格口径

            var b = sr.bounds;
            return new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
        }

        /// <summary>默认的「地面物品 id → 品质」实现（查 `IItemModule.GroundItems`；查不到按普通）。</summary>
        private ItemQuality QualityOfViaItem(int groundItemId)
        {
            var ctx = AppContext.I;
            var item = ctx != null ? ctx.Item : null;
            var ground = item != null ? item.GroundItems : null;
            if (ground == null) return ItemQuality.Normal;

            for (var i = 0; i < ground.Count; i++)
            {
                if (ground[i].Key != groundItemId) continue;
                var stack = ground[i].Value;
                return stack != null ? stack.quality : ItemQuality.Normal;
            }

            // 非预期但可解释：悬停目标已消失（刚被拾取/过期移除）⇒ 按普通配色，不报错
            return ItemQuality.Normal;
        }
    }
}
