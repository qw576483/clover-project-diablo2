// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Core/QualitySetting.cs  本项目新增
// **画质档位应用到引擎的唯一写点**（`QualitySettings.SetQualityLevel` 的唯一次调用）。
//
// 两个调用方都只走这里：`App/Bootstrap.ApplyStoredQuality`（冷启动应用持久化设置）
//   与 `UI/SettingsPanel`（面板内改档位）。
//
// 为什么不用引擎的 `Game.Quality.SetLevel(QualityTier)`：
//   `CloverEngine.QualityManager.Apply` 会写 `Application.targetFrameRate` +
//   `QualitySettings.vSyncCount = 0`（`Runtime/Presentation/Quality.cs`）—— 那是**帧节奏**，
//   而帧节奏的全工程唯一写点是 `Core/FramePacing.Pin`（`FramePacingPolicy`）。
//   两者同时写同一对全局值 ⇒ 帧节奏出现第二个写点、且两套口径互斥（引擎 30/60/60 + vSync 0
//   vs 帧节奏 刷新率可读 ⇒ vSync=1 锁刷新率）。
//   ⇒ 二选一：**画质内容走 Unity 质量资产（`QualitySettings.SetQualityLevel`），帧节奏走 FramePacing**。
//   画质档位与帧节奏解耦的论据（帧间隔必须恒定、与画质档位无关）见 `Core/FramePacing.cs` 文件头。
//
// `QualitySettings.SetQualityLevel` 会按档位把 `vSyncCount` 重置（Unity 语义）⇒ 本类在
//   每次应用后**立刻** `FramePacing.Pin` 重钉一次，保证帧节奏的最终值仍只由 FramePacing 决定。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using UnityEngine;

namespace Diablo2.Core
{
    /// <summary>画质档位应用（钳制 → `QualitySettings.SetQualityLevel` → 帧节奏重钉）的唯一写点。见文件头。</summary>
    public static class QualitySetting
    {
        /// <summary>日志 tag（`Core/Log.KnownTags` 白名单内的模块名）。</summary>
        private const string Tag = "App";

        /// <summary>引擎可用档位上限（`QualitySettings.names.Length - 1`）；引擎没有档位 ⇒ <c>-1</c>。</summary>
        public static int EngineMaxLevel => QualitySettings.names.Length - 1;

        /// <summary>本项目的有效档位上限 = <c>min(引擎上限, 档数配置)</c>；引擎没有档位 ⇒ <c>-1</c>。</summary>
        public static int Ceiling => Mathf.Min(EngineMaxLevel, MaxLevel);

        /// <summary>项目定义的档数上限（`LOW/MED/HIGH` ⇒ 0..2）。</summary>
        public const int MaxLevel = GameConst.MaxQualityLevel;

        /// <summary>引擎当前档位（`QualitySettings.GetQualityLevel()`）。</summary>
        public static int Current => QualitySettings.GetQualityLevel();

        /// <summary>
        /// 把档位应用到引擎。返回值 = **实际生效**档位；<c>-1</c> = 引擎没有质量档位 / 应用失败
        /// （两种情况都已 Warn，调用方不必重复报）。
        /// <para><paramref name="clamped"/> = 入参被钳到 <c>[0, Ceiling]</c>（调用方据此决定是否提示）。</para>
        /// </summary>
        /// <param name="level">期望档位（越界会被钳制，不静默）。</param>
        /// <param name="reason">为什么应用（进日志："启动" / "选项面板应用画质档位 2" …）。</param>
        /// <param name="clamped">出参：入参是否被钳制过。</param>
        public static int Apply(int level, string reason, out bool clamped)
        {
            clamped = false;

            var ceiling = Ceiling;
            if (ceiling < 0)
            {
                Game.Logger?.Warn(Tag, "[设置] 引擎没有质量档位（QualitySettings.names 为空）⇒ 画质档位只落盘、不应用");
                return -1;
            }

            var want = Mathf.Clamp(level, 0, ceiling);
            clamped = want != level;

            try
            {
                QualitySettings.SetQualityLevel(want, false);
            }
            catch (System.Exception e)
            {
                Game.Logger?.Warn(Tag, $"[设置] 应用画质档位 {want} 失败：{e.Message}（设置值已保存，下次启动生效）");
                return -1;
            }

            // `SetQualityLevel` 按档位重置了 `vSyncCount` ⇒ 立刻重钉，帧节奏的最终值仍只由 FramePacing 决定。
            FramePacing.Pin(reason);

            var actual = QualitySettings.GetQualityLevel();
            if (actual != want)
            {
                Game.Logger?.Warn(Tag, $"[设置] 画质未生效：期望档位={want}，引擎实际读回={actual}");
            }

            return actual;
        }

        /// <summary>不带钳制回执的重载（调用方不需要提示时用）。</summary>
        public static int Apply(int level, string reason) => Apply(level, reason, out _);
    }
}
