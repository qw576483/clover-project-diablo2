// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Audio/IAudioClipProbe.cs
// **音频资源存在性接缝**：`AudioModule` 判断「`Sound/{BGM,SFX}/{键}` 到底取不取得到音频」
// 只经这一个接口。
//
//   生产实现 = `EngineAudioClipProbe`（转发 `Game.Res.Exists`，见该文件头）。
//
// 为什么还留这个接缝（而不是在 `AudioModule` 里直接写 `Game.Res.Exists`）：
//   · 离线自检宿主（`tools/probes/hosts/audiocheck`，非 Unity 进程）要能注入替身，断言两条路径
//     ——「取不到 ⇒ 只 Warn 一次 + **不调引擎**」与「取得到 ⇒ 正常发播放请求」；
//   · 生产实现只有 `EngineAudioClipProbe` 一个（见该文件头）。
//
// 契约：`Probe` **恰好回调一次**（可用 = `true` / 不可用 = `false`），**不抛异常**；
//   `onResult` 的时机**允许同步**（生产实现 `EngineAudioClipProbe` 就是同步 —— `Game.Res.Exists`
//   是同步入口）⇒ 实现方**不许**假设回调晚于 `Probe` 返回，调用方也不许假设回调一定晚于返回。
// ─────────────────────────────────────────────────────────────────────────────

using System;

namespace Diablo2.Module.Audio
{
    /// <summary>音频资源存在性探测（唯一与引擎资源存在性打交道的接缝）。</summary>
    internal interface IAudioClipProbe
    {
        /// <summary>
        /// 探测 <paramref name="path"/>（如 `Sound/SFX/hit`）能否取到音频。
        /// 无论可用与否都**恰好回调一次**（可能同步）；不可用 = `false`，不抛异常。
        /// </summary>
        void Probe(string path, Action<bool> onResult);
    }
}
