// ─────────────────────────────────────────────────────────────────────────────
// **进图读条屏的「分档表」**：10 帧读条图，逐帧绑一个**真实里程碑**。
//
//   原版是把读条屏**铺在世界构建上**的：Diablerie `Game/World/WorldBuilder.cs:29-64`
//   `LoadActCoroutine` 依次
//     `LoadingScreen.Show(0.5f)`（:33，注释原文 "It's not zero due to preceding Unity scene load"）
//     → `CreateAct(...)`（:39，建整个世界：地图 + 怪物 + 物件）
//     → `Show(0.75f)`（:41）→ 主角就位（:44-52）→ `Show(0.9f)`（:54，"load first DCC while screen is black"）
//     → `Show(1.0f)`（:57）→ `yield return null`（:58）→ 允许操作 + `LoadingScreen.Hide()`（:60-62）。
//
//   档 0（第 1 帧）  读条屏打开、发起 `Game.Scene.Load`
//   档 1..3（2..4 帧）引擎场景加载**真进度**（[0, 0.9] → 门开到一半）
//   档 4（第 5 帧）  **Stage 场景就位**（引擎 `onDone`）—— 与原版 `Show(0.5f)` 同档
//   档 5（第 6 帧）  地图生成 + 地形渲染完成
//   档 6（第 7 帧）  主角数据 + 视图装配完成 —— 与原版 `Show(0.75f)` 同档
//   档 7（第 8 帧）  刷怪 / NPC 装配完成
//   档 8（第 9 帧）  相机就位 —— 与原版 `Show(0.9f)` 同档
//   档 9（第 10 帧） **世界就绪**（`StageEntered` / HUD 已开 + 首帧已渲染）—— 与原版 `Show(1.0f)` 同档
//
//   ① 进度不许假：档位**只能**由真实里程碑前移（`AppFlow.AdvanceReal`）；本表不做任何"随时间自增"。
//      呈现档位 = min（真实档位, 节奏放行的档位, 已呈现档位 + 1）⇒ 门**永不超前**真实进度，只可能滞后。
//   ② 动画要可见：`FrameCadenceSeconds` = 每档的**最短可见时间**（呈现节奏下限，不是进度来源）。
//      本工程真实管线只有 ~0.3s，不加下限就是"一帧跳到底"；加上它，门 10 档铺开 ≈ 0.63s。
//
//   本表**只**提供纯函数（档↔completeness、节奏放行、引擎进度→档），可在离线宿主里逐条断言
//      （`tools/probes/hosts/uicheck` 的 `LoadingCheck.cs`）。
//   算术本体在引擎件 `CloverEngine.LoadingPacing`
//      （`clover-client-unity-engine/Runtime/Presentation/LoadingPacing.cs`）
//      —— 档数 10 / 每档最短可见 0.07s / 各里程碑档号（档 4 = Stage 场景就位 …）**仍是本工程的取值**，
//      本表只把它们喂给引擎的纯函数。
//      离线宿主需把 `Runtime/Presentation/LoadingPacing.cs` 一并编入。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;

namespace Diablo2.Module.Flow
{
    /// <summary>进图读条屏的分档表（10 档 = 原版 10 帧读条图；逐档绑一个真实里程碑）。</summary>
    internal static class LoadingSteps
    {
        /// <summary>档数 = 原版读条图帧数（10；`ResPaths.FrameCountLoadingScreen`，实测 `loadingscreen.dc6` dir=1/fpd=10）。</summary>
        public const int Count = ResPaths.FrameCountLoadingScreen;

        /// <summary>
        /// 每档的**最短可见时间**（秒）—— **呈现节奏下限**，不是进度来源。
        /// <para>为什么需要它：本工程真实管线（场景加载 + 世界构建 + 首帧）只有 ~0.3s，
        /// 不加下限就是"一帧跳到底"；原版 `CreateAct`（建整个 act）在 ~1s 量级，
        /// 故按**原版同量级**给每档一个可见下限。10 档铺开 ≈ 0.63s。</para>
        /// <para>它**不会**让门提前：呈现档位仍受「真实档位」上限约束（见 `AppFlow.PresentDoor`）。</para>
        /// </summary>
        public const float FrameCadenceSeconds = 0.07f;

        /// <summary>档 0：读条屏刚打开（`Game.Scene.Load` 已发起）。</summary>
        public const int SceneOpenedFrame = 0;

        /// <summary>档 4（第 5 帧）：`Stage` 场景就位（引擎 `onDone`）—— 与原版 `Show(0.5f)` 同档。</summary>
        public const int SceneLoadedFrame = 4;

        /// <summary>档 5（第 6 帧）：地图生成 + 地形渲染完成。</summary>
        public const int MapFrame = 5;

        /// <summary>档 6（第 7 帧）：主角数据 + 视图装配完成 —— 与原版 `Show(0.75f)` 同档。</summary>
        public const int PlayerFrame = 6;

        /// <summary>档 7（第 8 帧）：刷怪 / NPC 装配完成。</summary>
        public const int MonsterFrame = 7;

        /// <summary>档 8（第 9 帧）：相机就位 —— 与原版 `Show(0.9f)` 同档。</summary>
        public const int CameraFrame = 8;

        /// <summary>档 9（第 10 帧）：世界就绪（`StageEntered` / HUD 已开 + 首帧已渲染）—— 与原版 `Show(1.0f)` 同档。</summary>
        public const int WorldReadyFrame = 9;

        /// <summary>每档的语义（日志用；下标 = 档号）。写的是"这一档背后真实做完了什么"。</summary>
        private static readonly string[] Reasons =
        {
            "第 1 档：读条屏打开（Game.Scene.Load 已发起）",
            "第 2 档：引擎场景加载真进度",
            "第 3 档：引擎场景加载真进度",
            "第 4 档：引擎场景加载真进度（门控上限 0.9 前）",
            "第 5 档：Stage 场景就位（引擎 onDone）＝ 原版 Show(0.5f)",
            "第 6 档：地图生成 + 地形渲染完成",
            "第 7 档：主角数据 + 视图装配完成 ＝ 原版 Show(0.75f)",
            "第 8 档：刷怪 / NPC 装配完成",
            "第 9 档：相机就位 ＝ 原版 Show(0.9f)",
            "第 10 档：世界就绪（装配完成 + 首帧已渲染）＝ 原版 Show(1.0f)",
        };

        /// <summary>档号的语义串（日志/`LoadingPanel` 的帧注释用）。越界 → 夹到最后/第一档。</summary>
        public static string ReasonOf(int index)
        {
            if (index < 0) index = 0;
            if (index >= Count) index = Count - 1;
            return Reasons[index];
        }

        /// <summary>
        /// 档号 → 交给 `LoadingPanel.SetProgress` 的 completeness（原版语义 [0,1]）。
        /// <para>算术在引擎件 `CloverEngine.LoadingPacing.CompletenessOf`
        /// （`clover-client-unity-engine/Runtime/Presentation/LoadingPacing.cs`）—— 本工程只给「档数 = 10」。
        /// 为什么取**区间中点**而不是 `index / (Count-1)`：`FrameIndex` 是 `(int)((Count-1) × c)`，
        /// 用端点值时浮点误差会把档 2（c=0.22222222 ×9 = 1.9999999）算成第 1 档。
        /// 中点 `(index+0.5)/(Count-1)` 距两端各半个档宽 ⇒ 浮点安全（末档夹到 1.0）。</para>
        /// </summary>
        public static float CompletenessOf(int index)
            => LoadingPacing.CompletenessOf(index, Count);

        /// <summary>
        /// **原版节奏**：读条屏已显示 <paramref name="elapsedSeconds"/> 秒时，最多允许开到第几档。
        /// <para>纯函数（离线可断言）：`elapsed=0 → 0`；每 `FrameCadenceSeconds` 放行一档；上限 = `Count-1`。</para>
        /// <para>算术在引擎件 `CloverEngine.LoadingPacing.MaxIndexAt`
        /// —— 节奏下限 `0.07s/档` 与档数 `10` 仍是**本工程**的参数。</para>
        /// </summary>
        public static int MaxIndexAt(double elapsedSeconds)
            => LoadingPacing.MaxIndexAt(elapsedSeconds, FrameCadenceSeconds, Count);

        /// <summary>
        /// 引擎场景加载真进度 → 门的档号（0..<see cref="SceneLoadedFrame"/>）。
        /// <para>值域依据：引擎在 `allowSceneActivation = false` 期间回调，`op.progress` 上限 = 0.9
        /// （`Runtime/Presentation/Scene.cs:40` 的 `op.progress >= 0.9f` 门控）
        /// ⇒ 把 [0, 0.9] 线性映射到"门开到一半"（[0, 0.5] = 原版 `Show(0.5f)` 的位置）。</para>
        /// <para>映射与取档的算术在引擎件
        /// `CloverEngine.LoadingPacing.SceneLoadFrameIndex`（上限 0.9 / 占比 0.5 / `FrameIndex` 都在那里）；
        /// 本工程只给「档数 10 + 档 4 = Stage 场景就位」这两个取值。</para>
        /// </summary>
        public static int SceneLoadFrameIndex(float engineProgress)
            => LoadingPacing.SceneLoadFrameIndex(engineProgress, Count, SceneLoadedFrame);
    }
}
