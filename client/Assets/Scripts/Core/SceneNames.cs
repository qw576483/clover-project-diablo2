// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Core/SceneNames.cs
// 契约冻结：三个场景名不许改；Build Settings 里的 Build Index 见 `tools/ai-skill/registry.md`。
// ─────────────────────────────────────────────────────────────────────────────

namespace Diablo2.Core
{
    /// <summary>场景名常量（与 `Assets/Scenes/*.unity` 同名）。</summary>
    public static class SceneNames
    {
        /// <summary>启动画面场景（仅 `Bootstrap` + Canvas），Build Index 0。</summary>
        public const string Boot = "Boot";

        /// <summary>纯 UI 场景（主菜单 / 选角 / 创角 / 设置，靠面板切换，**不切场景**），Build Index 1。</summary>
        public const string Menu = "Menu";

        /// <summary>游戏场景（相机 + 地图根 + 实体根 + HUD Canvas），Build Index 2。</summary>
        public const string Stage = "Stage";

        /// <summary>场景相对路径（供 `Editor/ProjectBuilder.cs` 生成脚本使用）。</summary>
        public const string BootPath = "Assets/Scenes/Boot.unity";

        /// <summary>场景相对路径（供生成脚本使用）。</summary>
        public const string MenuPath = "Assets/Scenes/Menu.unity";

        /// <summary>场景相对路径（供生成脚本使用）。</summary>
        public const string StagePath = "Assets/Scenes/Stage.unity";

        /// <summary>全部场景（Build Settings 顺序 = 数组顺序，Boot 为 0）。</summary>
        public static readonly string[] All = { Boot, Menu, Stage };
    }
}
