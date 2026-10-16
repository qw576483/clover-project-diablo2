// ─────────────────────────────────────────────────────────────────────────────
// 把 `Stage` 场景里的两个空节点（`MapRoot` / `EntityRoot`）交给业务模块。
//
// 为什么需要这个组件（而不是 `Bootstrap` 直接引用）：
//   `Bootstrap` 常驻在 `Boot` 场景（`DontDestroyOnLoad`），**拿不到 Stage 场景里的对象**；
//   而 `Module/Map` / `Module/View` 的 `AttachRoot(Transform)` 是**非契约**入口，只能由 App 层转交。
//
//   在 `Stage` 场景放一个空物体 `StageRoots`，挂本脚本，并把两个字段拖成
//   **不挂也能跑**：`AppWiring` 会 Warn 一次，两个模块各自 `new GameObject` 自建根。
//
// 时序：场景加载 ⇒ 本组件 `Awake`（此时 `AppContext.I` 已由 Bootstrap 建好）⇒ 注入 +
//   `AppWiring.AttachRoots` ⇒ 之后 `AppFlow.OnEnterStage` 才 `Map.ShowArea`（`EnsureView` 会挂到已注入的根）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using UnityEngine;

namespace Diablo2.App
{
    /// <summary>`Stage` 场景的根节点注入器（挂在 Stage 场景的一个空物体上）。</summary>
    public class StageRoots : MonoBehaviour
    {
        /// <summary>Stage 场景里的「地图根」（`MapModule.AttachRoot` 的落点）。</summary>
        [SerializeField] private Transform mapRoot;

        /// <summary>Stage 场景里的「实体根」（`ViewModule.AttachRoot` 的落点）。</summary>
        [SerializeField] private Transform entityRoot;

        private void Awake()
        {
            var ctx = AppContext.I;
            if (ctx == null)
            {
                // 正常路径：Bootstrap 先跑（Boot 场景）再进 Stage。走到这里说明是从 Stage 直接 Play。
                Game.Logger.Warn("App",
                    "StageRoots.Awake 时 AppContext 尚未创建（未从 Boot 场景进入？）⇒ 本次不注入根节点，" +
                    "地图/实体的根由模块自建");
                return;
            }

            if (mapRoot == null) Game.Logger.Warn("App", "StageRoots.mapRoot 未设置（ProjectBuilder 未拖字段）⇒ 地图根由 MapModule 自建");
            if (entityRoot == null) Game.Logger.Warn("App", "StageRoots.entityRoot 未设置（ProjectBuilder 未拖字段）⇒ 实体根由 ViewModule 自建");

            ctx.MapRoot = mapRoot;
            ctx.EntityRoot = entityRoot;
            AppWiring.AttachRoots(ctx);

            Game.Logger.Info("App",
                $"Stage 场景根节点已注入：MapRoot={(mapRoot != null ? mapRoot.name : "(空)")} " +
                $"EntityRoot={(entityRoot != null ? entityRoot.name : "(空)")}");
        }
    }
}
