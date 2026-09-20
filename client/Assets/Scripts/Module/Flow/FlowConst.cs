// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Flow/FlowConst.cs
// Flow 模块自己的常量（**站点名/场景名/事件名另有唯一来源**：`Core/Events.cs`、
// `Core/SceneNames.cs`、`Core/ResPaths.cs`，本文件不许重复定义它们）。
// ─────────────────────────────────────────────────────────────────────────────

namespace Diablo2.Module.Flow
{
    /// <summary>Flow 模块局部常量。</summary>
    internal static class FlowConst
    {
        /// <summary>
        /// 舞台内定时器统一打的 scope（`Game.Timer.After/Every(..., scope)` 用这个值），
        /// 离场时 `Game.Timer.StopScope(StageScope)` 一次清干净。
        /// 其它模块在 Stage 内注册定时器时也用这个 scope。
        /// </summary>
        public const string StageScope = "stage";

        /// <summary>
        /// 进图看门狗：超过该秒数仍未拿到 `Game.Scene.Load` 的完成回调，
        /// 视为场景缺失/加载失败（引擎只打 Error、**不会**回调 onDone）⇒ 回主菜单，避免卡在读条屏。
        /// </summary>
        public const float StageLoadTimeoutSeconds = 20f;

        /// <summary>读条进图时的提示文案。</summary>
        public const string LoadingTipStage = "正在生成地图…";
    }
}
