// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Audio/EngineAudioClipProbe.cs
// `IAudioClipProbe` 的**生产实现**：用引擎资源管理器探测音频能否取到。
//
// 做法：`Game.Res.LoadAsset<AudioClip>("Sound/SFX/{键}", clip => …)`。
//   · 音频存在 → 回调 `clip != null`，且引擎 `ResourceManager` **按路径缓存**该资源
//     （`Runtime/Resource/ResourceManager.cs:159-166`，同路径并发加载还会合并）；
//     随后 `AudioModule` 让 `Game.Sound.PlaySFX(键)` 播放时，引擎内部同样的
//     `LoadAsset<AudioClip>($"Sound/SFX/{键}")`（`Sound.cs:107`）会**命中同一份缓存**，不重复读盘。
//   · 音频缺失 → 引擎回调 null，本类回报 `false`；`AudioModule` 据此"报一次 + 之后静默"。
//   · `Game.Res` 为 null（`CloverRes.Init` 未调用）→ 报一次 Warn 并按"未到位"处理，**不抛异常**。
//
// ⚠️ 刻意**不**在探测里 Release：音效/BGM 体量小且会反复播放，保持驻留避免每次播放都吃一次加载抖动。
//    真正需要腾内存时由引擎的字节水位 + LRU 淘汰（`ResourceManager.EnforceWatermark`）负责。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;
using UnityEngine;

namespace Diablo2.Module.Audio
{
    /// <summary>生产用音频探测（走 `Game.Res` + 引擎约定的 `Sound/**` 路径）。</summary>
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

            res.LoadAsset<AudioClip>(path, clip => onResult?.Invoke(clip != null));
        }
    }
}
