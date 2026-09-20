// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Audio/IAudioClipProbe.cs
// **音频资源探测接缝**：`AudioModule` 判断「`Sound/{BGM,SFX}/{键}` 到底能不能取到音频」
// 只经这一个接口。
//
// 为什么要有这个接缝（而不是直接在 `AudioModule` 里写 `Game.Res.LoadAsset<AudioClip>`）：
//   · 引擎的 `ISoundManager.PlaySFX/PlayBGM` **对缺失文件是静默的**（`Sound.cs:109` `if (clip == null) return;`），
//     业务拿不到"到底有没有"这个信息 ⇒ 无法实现「缺文件只 Warn 一次 + 之后静默」这条硬要求；
//   · 离线自检宿主（`tools/audiocheck`，非 Unity 进程）**无法构造 `UnityEngine.AudioClip`**
//     ⇒ 若把探测写死在这里，就没法在无编辑器环境里验证"缺失降级"与"触发点是否被请求"。
//   把探测抽成接口后：生产用 `EngineAudioClipProbe`（走 `Game.Res`），自检宿主注入替身。
//
// 契约：`Probe` **允许同步回调**（引擎 `ResourceManager.LoadAsset` 命中缓存时就是同步回调，
//   见 `Runtime/Resource/ResourceManager.cs:159-166`）⇒ 实现方不许假设回调晚于 `Probe` 返回。
// ─────────────────────────────────────────────────────────────────────────────

using System;

namespace Diablo2.Module.Audio
{
    /// <summary>音频资源探测（唯一与引擎资源系统打交道的接缝）。</summary>
    internal interface IAudioClipProbe
    {
        /// <summary>
        /// 探测 <paramref name="path"/>（如 `Sound/SFX/hit`）能否取到音频。
        /// 无论可用与否都**恰好回调一次**（可能同步）；不可用 = `false`，不抛异常。
        /// </summary>
        void Probe(string path, Action<bool> onResult);
    }
}
