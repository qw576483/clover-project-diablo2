// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Audio/EngineAudioClipProbe.cs
// `IAudioClipProbe` 的**生产实现**：直接问引擎既有的存在性入口 `Game.Res.Exists(path)`
//     · 接口声明：`Runtime/Core/Contracts.cs:1287  bool Exists(string path);`
//     · 实现：`Runtime/Resource/ResourceManager.cs:251  public bool Exists(string path)`
//       （:254 命中 `_existsCache` ⇒ **按路径缓存**，只降不升）。
//
//   引擎的口径是「要问在不在用 `Game.Res.Exists`」（`Runtime/Presentation/Sound.cs` 文件头
//   语义约束 ⑤）。三点收益：
//     · `Exists` 同步返回 ⇒ 回调**立即**发生（契约允许，见 `IAudioClipProbe` 的契约段）；
//     · `Exists` **按路径缓存** ⇒ 重复问 = 字典命中，不打盘（`AudioModule` 侧不再自建探测表）；
//     · `Exists` 只回答「在不在」、**不驻留**（不占缓存、不动引用计数）⇒ 音频对象仍由引擎在
//       `PlaySFX/PlayBGM` 的真实加载路径上按需装入，探测与装载解耦。
//   · `Game.Res` 为 null（`CloverRes.Init` 未调用）→ 报一次 Warn 并按"未到位"处理，**不抛异常**。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;

namespace Diablo2.Module.Audio
{
    /// <summary>生产用存在性探测（转发到引擎 `Game.Res.Exists`；本类不自建加载/缓存）。</summary>
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
