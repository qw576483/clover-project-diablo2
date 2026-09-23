// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Audio/SfxThrottle.cs
// **同一音效键的最小重播间隔**（接收侧防御闸门；本片 C4 新增，⛔ 不是原版口径 —— 原版没有这条闸门，
//   它存在的唯一理由是"把刷屏挡在音源池之前"，见下）。
//
// 为什么需要（L3 证据，`client/Logs/2026-09-23.log`）：
//   · 引擎音源池只有 32 个源（`Runtime/Presentation/Sound.cs:78`），池满即**静默丢弃**后续音效
//     （同一文件 `:566-583`，且它的池满 Warn 自带"只报一次"）；
//   · 实测 `portal`（clip 时长 1.93s）被以 ~52 次/秒 请求时，32 个源会在 ~0.6s 内被 portal 占满
//     ⇒ 同一时刻的 `hit` / `monster_attack` / `miss` 全被丢弃（日志 `07:26:27.687` 的池满 Warn）。
//   ⇒ "某个键被高频重复请求"必须**在进池之前**被压住；发送侧去重（`Module/Map/ExitLatch`）只治"过门"这一条链，
//     本条闸门是**接收侧**的兜底（防别的键/别的上游重犯）。
//
// 口径 = **同一键（clip 键名）**两次起播之间的最小间隔 <see cref="MinRepeatSeconds"/> ⇒ 丢弃后一次（不排队、不打断）。
//   · 100ms ⇒ 单键 ≤ 10 声/秒；本项目正常键的真实节奏都远低于它（脚步 1.36 声/秒 = 每 2 格一声、
//     近战攻击循环 ≥ 0.3s、UI 点击 > 100ms）⇒ 不改变"听得出次数"的表现，只砍掉"同一瞬间/同一帧"的重复。
//   · 不同键**互不影响**（各自计时）。
//
// 时钟（为什么要注入）：
//   · 生产 = `UnityEngine.Time.unscaledTime`（不受 `timeScale` 影响 ⇒ 暂停时也不误判）；
//   · 离线自检宿主（`tools/probes/hosts/audiocheck`，纯 .NET 进程）读 Unity 时钟会抛异常
//     （与 `AudioLog.cs` 文件头记的 `Core/Log.cs:133` 同一个坑）⇒ 这里**读出失败就返回 0**；
//   · **时间源不可用（now ≤ 0）时本闸门判"不丢弃"** —— 宁可漏节流，也⛔ 不许把正常音效吞掉
//     （这也是离线宿主既有断言不受影响的原因）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;

namespace Diablo2.Module.Audio
{
    /// <summary>同一音效键的最小重播间隔闸门（纯逻辑 + 可注入时钟 ⇒ 离线可断言）。</summary>
    internal static class SfxThrottle
    {
        /// <summary>同一键的最小重播间隔（秒）。改这一个数就是改口径（见文件头推导）。</summary>
        public const float MinRepeatSeconds = 0.10f;

        /// <summary>时钟（秒）。生产 = Unity `unscaledTime`；离线宿主可注入假时钟逐毫秒推进。</summary>
        internal static Func<float> Clock = DefaultClock;

        /// <summary>每个键最近一次**被允许起播**的时刻。</summary>
        private static readonly Dictionary<string, float> LastPlayAt = new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>累计被本闸门丢弃的次数（自检 / 排障用；生产只读）。</summary>
        internal static int DropCount;

        /// <summary>默认时钟：读引擎时间，读不到（离线宿主）返回 0（= 闸门惰性）。</summary>
        private static float DefaultClock()
        {
            try
            {
                return UnityEngine.Time.unscaledTime;
            }
            catch (Exception)
            {
                // 非 Unity 宿主（离线自检）/ 引擎时钟不可读 ⇒ 返回 0 ⇒ ShouldDrop 一律放行
                return 0f;
            }
        }

        /// <summary>
        /// 是否应丢弃本次请求。
        /// <para>返回 true 时 <paramref name="sinceSeconds"/> = 距上一次**真正起播**的间隔（供日志/断言核数）。</para>
        /// <para>只有"允许起播"的那一次才刷新计时 ⇒ 被丢弃的请求不会把窗口越推越远（不会造成"永久静音"）。</para>
        /// </summary>
        public static bool ShouldDrop(string key, out float sinceSeconds)
        {
            sinceSeconds = 0f;
            if (string.IsNullOrEmpty(key)) return false;

            float now;
            try
            {
                now = Clock != null ? Clock() : 0f;
            }
            catch (Exception)
            {
                return false;                    // 时钟本身抛异常 ⇒ 放行（同"时间源不可用"口径）
            }

            if (now <= 0f) return false;         // 时间源不可用 ⇒ 闸门惰性（见文件头）

            float last;
            if (!LastPlayAt.TryGetValue(key, out last))
            {
                LastPlayAt[key] = now;
                return false;                    // 该键首次请求 ⇒ 放行
            }

            var since = now - last;
            if (since >= MinRepeatSeconds)
            {
                LastPlayAt[key] = now;
                return false;                    // 已超过最小间隔 ⇒ 放行并刷新计时
            }

            sinceSeconds = since;
            DropCount++;
            return true;                         // 间隔不足 ⇒ 丢弃（⛔ 不刷新计时）
        }

        /// <summary>清空计时与计数。**仅供离线自检宿主**（同一进程里跑多个用例）。</summary>
        internal static void ResetForTest()
        {
            LastPlayAt.Clear();
            DropCount = 0;
            Clock = DefaultClock;
        }
    }
}
