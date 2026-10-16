// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Audio/SfxThrottle.cs
// 音效播放闸门第 3 维（同一路径最小重播间隔）的项目侧门面 —— 闸门本体在引擎
//   `CloverEngine.SoundRepeatGate`（`Runtime/Presentation/Sound.cs`）。
//   本类只保留本项目自己的**公开面与调参常量** —— `MinRepeatSeconds` / `Clock` /
//   `DropCount` / `ShouldDrop` / `ResetForTest`，内部一律转发，不自持计时表与计数。
//
//   · 引擎音源池只有 32 个源（`Runtime/Presentation/Sound.cs:78`），池满即**静默丢弃**后续音效
//     （同一文件 `:566-583`，且它的池满 Warn 自带"只报一次"）；
//   · `portal`（clip 时长 1.93s）被以 ~52 次/秒 请求时，32 个源会在 ~0.6s 内被 portal 占满
//     ⇒ 同一时刻的 `hit` / `monster_attack` / `miss` 全被丢弃。
//   ⇒ "某个键被高频重复请求"必须**在进池之前**被压住；发送侧去重（`Module/Map/ExitLatch`）只治"过门"这一条链，
//     本条闸门是**接收侧**的兜底（防别的键/别的上游重犯）。
//
// 口径 = **同一键（clip 键名）**两次起播之间的最小间隔 <see cref="MinRepeatSeconds"/> ⇒ 丢弃后一次（不排队、不打断）。
//   · 100ms ⇒ 单键 ≤ 10 声/秒；本项目正常键的真实节奏都远低于它（脚步 1.36 声/秒 = 每 2 格一声、
//     近战攻击循环 ≥ 0.3s、UI 点击 > 100ms）⇒ 不改变"听得出次数"的表现，只砍掉"同一瞬间/同一帧"的重复。
//   · 不同键**互不影响**（各自计时）。
//
// 时钟（可注入）：
//   · 生产 = `UnityEngine.Time.unscaledTime`（不受 `timeScale` 影响 ⇒ 暂停时也不误判）；
//   · 离线自检宿主（`tools/probes/hosts/audiocheck`，纯 .NET 进程）读不到 Unity 时钟 ⇒
//     本层读取失败即返回 0；
//   · **时间源不可用（now ≤ 0）时本闸门判"不丢弃"** —— 宁可漏节流，也不许把正常音效吞掉
//     （这也是离线宿主既有断言不受影响的原因）。
//   ⇒ 这三级口径由引擎 `SoundRepeatGate` 提供（含"非 Unity 宿主不崩"），
//     本文件只把 `Clock` 转发过去；本项目仍然只认 `MinRepeatSeconds` 这一个调参常量。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;

namespace Diablo2.Module.Audio
{
    /// <summary>
    /// 同一音效键的最小重播间隔闸门 —— 转发到引擎 <see cref="SoundRepeatGate"/>。
    /// <para>本类不自持计时表 / 计数 / 默认时钟。</para>
    /// </summary>
    internal static class SfxThrottle
    {
        /// <summary>同一键的最小重播间隔（秒）。改这一个数就是改**本项目**口径（见文件头推导）。</summary>
        public const float MinRepeatSeconds = 0.10f;

        /// <summary>
        /// 时钟（秒）。生产 = Unity `unscaledTime`；离线宿主可注入假时钟逐毫秒推进。
        /// <para>转发到引擎闸门的可注入时钟（默认 <c>null</c> = Unity 时钟，读不到即惰性 ⇒ 放行）。</para>
        /// </summary>
        internal static Func<float> Clock
        {
            get { return SoundRepeatGate.Clock; }
            set { SoundRepeatGate.Clock = value; }
        }

        /// <summary>累计被本闸门丢弃的次数（自检 / 排障用；生产只读）。= 引擎闸门的计数。</summary>
        internal static int DropCount
        {
            get { return SoundRepeatGate.DropCount; }
        }

        /// <summary>
        /// 是否应丢弃本次请求（= 引擎 <see cref="SoundRepeatGate.ShouldDrop"/>，间隔取本项目的
        /// <see cref="MinRepeatSeconds"/>）。
        /// <para>返回 true 时 <paramref name="sinceSeconds"/> = 距上一次**真正起播**的间隔（供日志/断言核数）。</para>
        /// <para>只有"允许起播"的那一次才刷新计时 ⇒ 被丢弃的请求不会把窗口越推越远（不会造成"永久静音"）。</para>
        /// </summary>
        public static bool ShouldDrop(string key, out float sinceSeconds)
        {
            return SoundRepeatGate.ShouldDrop(key, MinRepeatSeconds, out sinceSeconds);
        }

        /// <summary>清空计时与计数。**仅供离线自检宿主**（同一进程里跑多个用例）。</summary>
        internal static void ResetForTest()
        {
            SoundRepeatGate.Reset();
        }
    }
}
