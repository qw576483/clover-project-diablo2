// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Audio/EngineAudioClipProbe.cs
// `IAudioClipProbe` 的**生产实现**：一层**薄转发**，直接问引擎既有的存在性入口
//   `Game.Res.Exists(path)`
//     · 接口声明：`Runtime/Core/Contracts.cs:1287  bool Exists(string path);`
//     · 实现：`Runtime/Resource/ResourceManager.cs:251  public bool Exists(string path)`
//       （:254 命中 `_existsCache` ⇒ **按路径缓存**，只降不升）。
//
// ★ 片 sinkup6-d2 · d2-audio（收敛与引擎平行的第二套探测）：
//   原实现 = `Game.Res.LoadAsset<AudioClip>(path, clip => …)` **＋ 自己算「按路径缓存」**，
//   等于把引擎的「加载 + 缓存」那条路又走了一遍（还多付一次真实加载）。引擎的既有口径是
//   「要问在不在用 `Game.Res.Exists`」（`Runtime/Presentation/Sound.cs` 文件头语义约束 ⑤ 原文），
//   本类改为照做。三点收益：
//     · `Exists` 同步返回 ⇒ 回调**立即**发生（契约允许，见 `IAudioClipProbe` 的契约段）；
//     · `Exists` **按路径缓存** ⇒ 重复问 = 字典命中，不打盘 —— 这正是 `AudioModule` 原
//       `_probedSfx/_probedBgm` 两张表想买的东西，现在由引擎提供（那两张表已删）；
//     · `Exists` 只回答「在不在」、**不驻留**（不占缓存、不动引用计数）⇒ 音频对象仍由引擎在
//       `PlaySFX/PlayBGM` 的真实加载路径上按需装入，探测与装载解耦。
//   · `Game.Res` 为 null（`CloverRes.Init` 未调用）→ 报一次 Warn 并按"未到位"处理，**不抛异常**。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;

namespace Diablo2.Module.Audio
{
    /// <summary>生产用存在性探测（薄转发到引擎 `Game.Res.Exists`；不再自建加载/缓存）。</summary>
    internal sealed class EngineAudioClipProbe : IAudioClipProbe
    {
        /// <inheritdoc />
        public void Probe(string path, Action<bool> onResult)
        {
            var res = Game.Res;
            if (res == null)
            {
                // 单机正常流程不会走到（Bootstrap 里 CloverRes.Init 必调）；走到这里说明启动顺序错了。
                AudioLog.NoResourceManager();
                onResult?.Invoke(false);
                return;
            }

            onResult?.Invoke(res.Exists(path));
        }
    }
}
