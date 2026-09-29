// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Core/StageTimer.cs  本项目新增
// **舞台内「不受 timeScale 影响」定时器的 scope 薄封装**。
//
// 为什么需要它：引擎 `ITimer` 的两个 unscaled 入口
//   （`AfterUnscaled(float, Action)` / `EveryUnscaled(float, Action)`，见
//   `clover-client-unity-engine/Runtime/Core/Timer.cs`）**没有 scope 形参**，
//   只能靠 id 一个个 `Stop`；而它们的用途恰恰是「`Time.timeScale = 0` 时还要走时间」
//   （暂停菜单 / 结算屏 / 死亡屏）—— 这类回调最容易在离场时被漏停。
//   ⇒ 本类替它们记账，离场时 `StopAll()` 与 `Game.Timer.StopScope(舞台 scope)` 同一次清干净。
//
// 适用面：舞台内存活的一切 unscaled 定时器（面板 / 模块都可用；本类在 `Core`，
//   不引入 `Diablo2.Module` 依赖）。按 id 单独 `Stop` 仍照常可用（面板提前关闭场景）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;

namespace Diablo2.Core
{
    /// <summary>舞台内 unscaled 定时器的登记簿（见文件头）。引擎未接线时为安全空操作。</summary>
    public static class StageTimer
    {
        /// <summary>已登记、尚未停止的定时器 id（`StopAll` 一次清空）。</summary>
        private static readonly List<long> Ids = new List<long>();

        /// <summary>已登记条数（自检用）。</summary>
        public static int Count => Ids.Count;

        /// <summary>延迟执行一次回调，按不受 `timeScale` 影响的真实时间计时。</summary>
        public static long AfterUnscaled(float delay, Action callback)
            => Track(Game.Timer == null ? 0L : Game.Timer.AfterUnscaled(delay, callback));

        /// <summary>循环执行回调，按不受 `timeScale` 影响的真实时间计时。</summary>
        public static long EveryUnscaled(float interval, Action callback)
            => Track(Game.Timer == null ? 0L : Game.Timer.EveryUnscaled(interval, callback));

        /// <summary>停止一个本类登记的定时器（幂等；`0` / 未登记都安全）。</summary>
        public static void Stop(long id)
        {
            if (id == 0L) return;
            Ids.Remove(id);
            Game.Timer?.Stop(id);
        }

        /// <summary>停掉本类登记的全部定时器（离场清场用；幂等，可重复调用）。</summary>
        public static void StopAll()
        {
            for (var i = 0; i < Ids.Count; i++) Game.Timer?.Stop(Ids[i]);
            Ids.Clear();
        }

        /// <summary>登记非零 id（`0` = 引擎没接 / 参数非法 ⇒ 不记账）。</summary>
        private static long Track(long id)
        {
            if (id != 0L) Ids.Add(id);
            return id;
        }
    }
}
