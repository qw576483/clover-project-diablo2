// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Flow/IAppFlow.cs
// 流程门面（**唯一对外入口**）。实现：`AppFlow`（`Module/Flow/AppFlow.cs`，internal）。
//
// 站点（`Events.Fsm.State*`）与迁移（`Events.Fsm.Trigger*`）：
//   Boot →(BootDone)→ MainMenu
//   MainMenu →(NewGame | Continue)→ CharSelect      （无角色时紧接 NeedCreate → CharCreate）
//   CharSelect →(NeedCreate)→ CharCreate →(Created)→ CharSelect
//   CharSelect →(EnterStage)→ Loading →(StageReady)→ Stage
//   Stage →(Pause)→ Pause →(Resume)→ Stage / →(ToMain)→ MainMenu
// 菜单类站点**共用一个 UI 场景 `Menu`**（只切面板，不切场景）；`Stage` 场景只在进图时加载。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Def;

namespace Diablo2.Module.Flow
{
    /// <summary>启动与流程编排门面（App 唯一需要的模块）。</summary>
    public interface IAppFlow
    {
        /// <summary>
        /// 进入流程：注册站点/迁移/订阅，并把站点置到 `Boot`（打开启动画面）。
        /// **应用起来后由 `App/Bootstrap` 调用一次**。
        /// </summary>
        void Enter();

        /// <summary>当前站点名（= `Game.Fsm.Current`，取值见 `Events.Fsm.State*`）。</summary>
        string CurrentState { get; }

        /// <summary>
        /// 读条进图：切到 `Loading` → `Game.Scene.Load(SceneNames.Stage, 真进度, onDone)` → `Stage`。
        /// 进度真的来自 `Game.Scene.Load` 回调（驱到 `LoadingPanel.SetProgress`）。
        /// <para><b>可重入守卫（agent-17 §A）</b>：已经在 Stage 且请求的就是**当前区域** ⇒ 直接忽略
        /// （不重载场景、不清场，只打一条 `[Flow] 进图请求被忽略（已在 Stage 同一区域）`）；
        /// 请求**不同区域** ⇒ 先走 7 项清场再进图（**绝不允许**不清场就重载场景）；
        /// 正在读条时收到重复请求 ⇒ 同样忽略（否则两次 `Game.Scene.Load` 并发）。</para>
        /// </summary>
        /// <param name="area">进图后所在的区域（`AreaId.Town` / `BloodMoor` / `DenOfEvil`）。</param>
        void GoStage(AreaId area);

        /// <summary>
        /// 回主菜单：清场（见 `AppFlow.LeaveStage`）→ 切到 `Menu` 场景 → 打开主菜单。
        /// 已在 `MainMenu` 时是幂等的。
        /// </summary>
        void BackToMain();

        /// <summary>退出游戏（编辑器里停 Play，打包后 `Application.Quit`）。</summary>
        void QuitGame();
    }
}
