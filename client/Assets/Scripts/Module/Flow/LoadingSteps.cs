// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Flow/LoadingSteps.cs      （「经典 load 动画」轮新增）
// **进图读条屏的「分档表」**：10 帧读条图，逐帧绑一个**真实里程碑**。
//
// ── 为什么要有这个文件（本轮修掉的缺陷）──────────────────────────────────────
//   修前：读条屏的进度**只**来自 `Game.Scene.Load` 的 `op.progress`
//   （`clover-client-unity-engine/Runtime/Presentation/Scene.cs:34-43`，值域 [0, 0.9]）。
//   本工程 `Stage` 场景极小 ⇒ 引擎在 ~40ms 内就把进度推到门控上限 0.9
//   ⇒ 实机里读条屏只闪过「第 1/10 帧 → 第 10/10 帧」（Play 实测 20:36:40.229 → :40.231，**2ms**）
//   ⇒ 用户要的「经典的 load 动画」等于**看不见**。
//
//   原版是把读条屏**铺在世界构建上**的：Diablerie `Game/World/WorldBuilder.cs:29-64`
//   `LoadActCoroutine` 依次
//     `LoadingScreen.Show(0.5f)`（:33，注释原文 "It's not zero due to preceding Unity scene load"）
//     → `CreateAct(...)`（:39，建整个世界：地图 + 怪物 + 物件）
//     → `Show(0.75f)`（:41）→ 主角就位（:44-52）→ `Show(0.9f)`（:54，"load first DCC while screen is black"）
//     → `Show(1.0f)`（:57）→ `yield return null`（:58）→ 允许操作 + `LoadingScreen.Hide()`（:60-62）。
//
// ⇒ 本表把 10 档逐档绑到"世界里真做完的一件事"（`Reason` 里写清是哪件）：
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
// ── 两条硬纪律（对应任务书的 ② 与 ③）────────────────────────────────────────
//   ③ 进度不许假：档位**只能**由真实里程碑前移（`AppFlow.AdvanceReal`）；本表不做任何"随时间自增"。
//      呈现档位 = min（真实档位, 节奏放行的档位, 已呈现档位 + 1）⇒ 门**永不超前**真实进度，只可能滞后。
//   ② 动画要可见：`FrameCadenceSeconds` = 每档的**最短可见时间**（呈现节奏下限，不是进度来源）。
//      本工程真实管线只有 ~0.3s，不加下限就是"一帧跳到底"；加上它，门 10 档铺开 ≈ 0.63s
//      （≥ 任务书要求的 0.5s，且与原版进图观感同量级）。
//
//   ⚠️ 本表**只**提供纯函数（档↔completeness、节奏放行、引擎进度→档），可在离线宿主里逐条断言
//      （`.ai-tmp/hosts/uicheck` 的 `LoadingCheck.cs`）。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Core;
using Diablo2.UI;

namespace Diablo2.Module.Flow
{
    /// <summary>进图读条屏的分档表（10 档 = 原版 10 帧读条图；逐档绑一个真实里程碑）。</summary>
    internal static class LoadingSteps
    {
        /// <summary>档数 = 原版读条图帧数（10；`ResPaths.FrameCountLoadingScreen`，实测 `loadingscreen.dc6` dir=1/fpd=10）。</summary>
        public const int Count = ResPaths.FrameCountLoadingScreen;

        /// <summary>
        /// 每档的**最短可见时间**（秒）—— **呈现节奏下限**，不是进度来源。
        /// <para>为什么需要它：本工程真实管线（场景加载 + 世界构建 + 首帧）实测只有 ~0.3s，
        /// 一帧一档会"闪过去"（这正是本轮修掉的缺陷）。原版读条屏的时长被它自己的世界构建
        /// （`CreateAct` 建整个 act）撑到 ~1s 量级，我们按**原版同量级**给每档一个可见下限。
        /// 10 档铺开 ≈ 0.63s（≥ 任务书要求的「第 1 帧 → 第 10 帧可见时长 ≥ 0.5s」）。</para>
        /// <para>⛔ 它**不会**让门提前：呈现档位仍受「真实档位」上限约束（见 `AppFlow.PresentDoor`）。</para>
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
        /// <para>为什么取**区间中点**而不是 `index / (Count-1)`：`FrameIndex` 是 `(int)((Count-1) × c)`，
        /// 用端点值时浮点误差会把档 2（c=0.22222222 ×9 = 1.9999999）算成第 1 档。
        /// 中点 `(index+0.5)/(Count-1)` 距两端各半个档宽 ⇒ 浮点安全（末档夹到 1.0）。</para>
        /// </summary>
        public static float CompletenessOf(int index)
        {
            // `Count` 是 const（= 10）⇒ `Count - 1` 恒 ≥ 1，除法安全（不写 `Count <= 1` 的守卫，
            // 否则编译器会报 CS0162「无法访问的代码」）。
            if (index < 0) index = 0;
            if (index >= Count) return 1f;

            var c = (index + 0.5f) / (Count - 1);
            return c > 1f ? 1f : c;
        }

        /// <summary>
        /// **原版节奏**：读条屏已显示 <paramref name="elapsedSeconds"/> 秒时，最多允许开到第几档。
        /// <para>纯函数（离线可断言）：`elapsed=0 → 0`；每 `FrameCadenceSeconds` 放行一档；上限 = `Count-1`。</para>
        /// </summary>
        public static int MaxIndexAt(double elapsedSeconds)
        {
            if (elapsedSeconds <= 0d || double.IsNaN(elapsedSeconds)) return 0;

            // elapsedSeconds > 0 且 FrameCadenceSeconds > 0 ⇒ n ≥ 0（无需再防负）
            var n = (int)(elapsedSeconds / FrameCadenceSeconds);
            return n >= Count ? Count - 1 : n;
        }

        /// <summary>
        /// 引擎场景加载真进度 → 门的档号（0..<see cref="SceneLoadedFrame"/>）。
        /// <para>值域依据：引擎在 `allowSceneActivation = false` 期间回调，`op.progress` 上限 = 0.9
        /// （`Runtime/Presentation/Scene.cs:40` 的 `op.progress >= 0.9f` 门控）
        /// ⇒ 把 [0, 0.9] 线性映射到"门开到一半"（[0, 0.5] = 原版 `Show(0.5f)` 的位置）。</para>
        /// <para>⚠️ 修前的做法是把 [0,0.9] 映射到 **[0,1]**（整个门）—— 于是场景一加载完，
        /// 门就到第 10 帧（本轮的缺陷现场）。现在它**只**占前 5 档。</para>
        /// </summary>
        public static int SceneLoadFrameIndex(float engineProgress)
        {
            if (float.IsNaN(engineProgress)) return 0;

            const float engineCeiling = 0.9f;                 // Runtime/Presentation/Scene.cs:40
            var p = engineProgress <= 0f ? 0f : (engineProgress >= engineCeiling ? 1f : engineProgress / engineCeiling);
            var completeness = 0.5f * p;                      // [0,0.9] → [0,0.5]（原版 Show(0.5f) 的位置）

            var idx = LoadingPanel.FrameIndex(completeness, Count);
            return idx > SceneLoadedFrame ? SceneLoadedFrame : idx;
        }
    }
}
