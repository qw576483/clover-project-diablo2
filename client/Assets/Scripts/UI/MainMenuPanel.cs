// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/MainMenuPanel.cs
// 站点：MainMenu。层：Normal。预制体：`Resources/UI/MainMenuPanel`（agent-10 生成空壳）。
//
// ★ agent-15 §A：**1:1 复刻**原版主菜单（依据 `Prefabs/Menu/MainMenu.prefab`）。
// ★ 片 4：**菜单项与文案回归原版** —— 原版主菜单是 **4 项**：
//     `SINGLE PLAYER / MULTIPLAYER / CINEMATICS / EXIT`
//   （出处 = `Prefabs/Menu/MainMenu.prefab` 的 `Buttons` 四个子节点：
//     `SinglePlayerButton`/`MultiPlayerButton`/`CinematicsButton`/`ExitButton` 的 `m_Text`；
//     中文版标题条资源也在：`data/local/ui/chi/{SINGLEPLAYER,MULTIPLAYER}.DC6`）
//
// ★ 片 1（本轮）：**用户点名删掉中间那两个按钮** —— 原话「9.菜单中，中间那两个既然没开发，
//   就不要那个按钮了。」⇒ 只建 `SINGLE PLAYER` / `EXIT`：
//     · `MULTIPLAYER`：本项目形态是**单机** ⇒ 点它只发 `Events.MultiplayerUnavailable`（Toast+Warn）；
//     · `CINEMATICS`：原版 4 段 CG 是 Bink `.bik` ⇒ 点它只弹 Toast。
//   ⚠️ **删的依据是"用户本轮点名"，不是"原版没有"**（层级：用户本轮明说 > 全局 skill > 原版本保真）：
//      这两项**原版有**（见下），所以这是一条**登记在案的偏离**，不是"回归原版"。
//   · 偏离怎么登记：`UiLayoutFlow.Menu.MultiPos` / `.CinematicsPos` 两个原版槽位坐标**仍保留**
//     （`uicheck` 的「主菜单 4 个原版槽位」断言直接引用它们），`UiLayoutFlow.Table` 的 `Panel.Menu`
//     段里那两行也**照旧留着**（Use 文案写明"槽位坐标保留"）⇒ 离线断言看得见这条偏离。
//   · 坐标纪律：`SINGLE PLAYER` / `EXIT` 的槽位与坐标**一个字符都没动**（仍走
//     `UiLayoutFlow.Menu.SinglePos` / `.ExitPos`）⇒ 画面上这两项不会"跑位"。
//   ⛔ **以后不许悄悄加回来**：要恢复必须先有实装（能联机 / 能播的过场 CG），并同时改掉上面那条登记。
//   · 底部**新增品牌署名行** `by clover-engine`：全局 skill §1.6 硬要求「**首页画面底部**必须有
//     `by clover-engine`，判据 = 实机截图可读」（用户本轮另一句原话「1.首界面 by clover-engine 呢？」）
//     ⇒ 与启动屏**共用** `UiLayoutFlow.Brand` 的几何（x=0 居中、y=-462），不是各写一套魔数。
//
//   ⛔ **本轮删掉的两次自加项**（用户点名的主菜单违例）：
//     · `CONTINUE`   —— 原版没有；**功能没丢**：原版"继续玩"就是 `SINGLE PLAYER` → 选角屏 → `ENTER`
//                        （本工程 `Events.Fsm.TriggerNewGame` → CharSelect；无存档时 Flow 直接进创角屏，
//                         见 `Module/Flow/AppFlow.cs:1001-1011`）；
//     · `SETTINGS`   —— 原版主菜单没有；**入口没丢**：原版选项在 **ESC 暂停菜单**里
//                        ⇒ 本工程的 `OPTIONS` 按钮（`UI/PausePanel.cs`，与 `UiLayoutFlow.Pause.OptionsPos`
//                        同一套原版按钮几何）→ `SettingsPanel`。**层级与原版一致**。
//
//   · 逐元素坐标全部来自 `UI/UiLayoutFlow.cs` 的 `Menu` 组（原版值 → ×1.8 居中）
//     · 背景：原版 `main_screen`（800×600）按高度 ×1.8 = 1440×1080、**水平居中**（不横向拉伸）
//     · 原版 4 个槽位（272×35、x=0、行节奏 45 原版px → 81）—— ★ 片 1 起**只有两个建按钮**：
//         SinglePlayerButton 原版 (0,-17.5) → 本工程 (0,-31.5)   = `SINGLE PLAYER`  ← 建（=原版第 1 槽）
//         MultiPlayerButton  原版 (0,-62.5) → 本工程 (0,-112.5)  = `MULTIPLAYER`    ⛔ 片 1 用户点名删
//         CinematicsButton   原版 (0,-107.5)→ 本工程 (0,-193.5)  = `CINEMATICS`     ⛔ 片 1 用户点名删
//         ExitButton         原版 (0,-152.5)→ 本工程 (0,-274.5)  = `EXIT`           ← 建（=原版第 4 槽）
//     · 按钮底图 = 原版帧（`ResPaths.MenuButtonWide` 的 0/1/2 = 常态/悬停/按下）
//     · 按钮文字 = **原版位图字体**（英文），色 `#191919` = 原版 WideButton.prefab 的 Text.m_Color
//     · 色调 = 原版亮度（白），不做任何提亮/压暗
//   原版 `LogoPlaceholder` 是**透明的空 Image**（m_Color.a=0、无 sprite，运行时由脚本喂图），
//   而 `main_screen` 贴图里已烘了 "EXPANSION SET / Lord of Destruction" 字标 ⇒ 不再另画 logo
//   （原版那枚火焰 logo 是 `FrontEnd/D2logoFireLeft/Right` 的**动画**；未接 —— 见回报的「范围边界」）。
//
//   · `MULTIPLAYER` / `CINEMATICS`（片 1 起**按钮不再建**，但"实装情况"照旧记在这里）：
//     `MULTIPLAYER` 在单机形态下只发 `Events.MultiplayerUnavailable`（收方 Toast + Warn）；
//     `CINEMATICS` 原版 4 段 CG 是 **Bink `.bik`**（`原版资源/d2mpq/d2video.mpq`），
//     Unity `VideoPlayer` 播不了 ⇒ 以前只弹 Toast。**用户本轮点名删掉这两个按钮。**
//     ⛔ **删按钮 ≠ 删事件**：事件常量（`Core/Events.cs:100-101`）与收方
//     （`Module/Flow/AppFlow.cs:437` 订阅 / `:1162` `OnMultiplayerUnavailable`）**原样保留** ——
//     将来真接了联机，只要把按钮建回来即可复用整条链路。
//   · `Args.hasSave` 仍由 Flow 传入（`Module/Flow/AppFlow.cs:516`），**不控制任何按钮**
//     （原版没有 CONTINUE 项）⇒ 只落一条日志，便于查"存档到底读没读到"。
// ⛔ 只发/收事件；**不得引用 `Diablo2.Module` 下的任何类型**（分层自检 ③）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using UnityEngine;

namespace Diablo2.UI
{
    /// <summary>
    /// 主菜单面板（1:1 复刻原版 LoD 主菜单布局）。
    /// <para>★ 片 1 起**只建 2 项**：`SINGLE PLAYER` / `EXIT`（原版 4 项里的第 1 / 第 4 项）——
    /// 中间两项 `MULTIPLAYER` / `CINEMATICS` 由用户本轮点名删除（原话见文件头）。</para>
    /// </summary>
    public class MainMenuPanel : UIPanel
    {
        /// <summary>`OnOpen(param)` 的参数（契约：面板状态值一律由调用方传入）。</summary>
        public class Args
        {
            /// <summary>是否存在任意存档。★ 片 4 起**只用于日志**（原版主菜单没有 CONTINUE 项）。</summary>
            public bool hasSave;
        }

        /// <summary>
        /// 按钮文案（**逐字 = 原版 prefab 的 `m_Text`**）。
        /// <para>★ 片 1：只留原版 4 项里的**第 1 项与第 4 项** —— 用户原话「菜单中，中间那两个既然
        /// 没开发，就不要那个按钮了」。删掉的两项常量（`Multi` / `Cinematics`）**从本类删除**：
        /// 常量留着就会出现"常量还在、按钮忘了建"的中间态（离线也查不出来）。</para>
        /// </summary>
        private static class Text
        {
            public const string Single = "SINGLE PLAYER";
            public const string Exit = "EXIT";
        }

        private bool _built;

        /// <summary>
        /// 层：<see cref="UILayer.Normal"/>（= 文件头「层：Normal」）。
        /// <para>★ 本片（w3 流程屏逐控件审计）**显式声明**：基类默认值就是 Normal
        /// （`PresentationContracts.cs:174`）⇒ **行为零变化**；加它是为了让它成为 `uicheck`
        /// `PanelSpec` 能断言的契约（修前本屏不在 PanelSpec 表里）。</para>
        /// </summary>
        public override UILayer Layer => UILayer.Normal;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();

            var args = param as Args;
            if (args == null)
            {
                Log.Warn("Ui", "MainMenuPanel.OnOpen 未收到 Args（应为 MainMenuPanel.Args）⇒ hasSave 按 false 记录");
            }

            // 两项都不依赖"有没有存档"（原版没有 CONTINUE 项）⇒ 这里只记一条，供查存档是否读到。
            //   ★ 片 1：日志里的项数改成**实际建出来的 2 项** —— 日志写"原版 4 项"而画面只有 2 项，
            //   正是"看日志以为没问题"的那类假证据（本轮的偏离必须让运行时日志也说得对得上）。
            var hasSave = args != null && args.hasSave;
            Log.Info("Ui", "主菜单已打开：片 1 起建 2 项（SINGLE PLAYER / EXIT）；" +
                           "原版 4 项里的 MULTIPLAYER / CINEMATICS 已按用户本轮点名删掉按钮" +
                           "（槽位坐标仍登记在 UiLayoutFlow 的对照表里）；" +
                           $"存档存在={hasSave}（只用于日志、不控制任何按钮 —— 原版主菜单没有 CONTINUE，" +
                           "进游戏走 SINGLE PLAYER → 选角屏 → ENTER）");
        }

        private void Build()
        {
            if (_built) return;
            _built = true;

            // 屏适配容器（主菜单原版 |y| ≤ 225 ⇒ 系数 = 1，即纯 ×1.8；见 UiLayoutFlow.Scale 的注释）
            var screen = UiLayoutFlow.FitRoot(transform, UiLayoutFlow.FitMenu);

            // ★ 原版主菜单屏（800×600）**按高度等比 ×1.8 = 1440×1080 水平居中**：
            //   高正好铺满画布、宽 1440 < 1920 ⇒ 左右各留 240 交给相机/背景（**不把 4:3 素材横向拉伸**）
            UiLayoutFlow.BackdropArt(screen, ResPaths.MenuMainScreen, new Color(0.05f, 0.05f, 0.06f, 1f));

            // ── ★ 片 1：原版那 4 个槽位里**只建 2 个**（第 2/3 槽由用户点名删除，见文件头）──
            //   ⚠️ 留下来的两个**坐标一个字符没动**（仍走 `Menu.SinglePos` / `Menu.ExitPos`）：
            //      `SINGLE PLAYER` 原版 (0,-17.5)×1.8 = (0,-31.5)、`EXIT` 原版 (0,-152.5)×1.8 = (0,-274.5)。
            //   中间空出来的两行**不补位、不重排**（用户说的是"不要那个按钮"，不是"把下面提上来"）
            //   —— 所以画面上 `EXIT` 仍在原版第 4 槽的位置，不会跑位。
            UiLayoutFlow.FlowButton.Create(screen, "Single", Text.Single,
                UiLayoutFlow.WideButtonOrig, UiLayoutFlow.Menu.SinglePos,
                () => Game.Event.Emit(Events.Fsm.TriggerNewGame));

            UiLayoutFlow.FlowButton.Create(screen, "Quit", Text.Exit,
                UiLayoutFlow.WideButtonOrig, UiLayoutFlow.Menu.ExitPos,
                () => Game.Event.Emit(Events.QuitRequest));

            // ★ 片 1：底部品牌署名行 —— 全局 skill §1.6 硬要求「**首页画面底部**必须有
            //   `by clover-engine`，判据 = 实机截图可读」（用户原话「1.首界面 by clover-engine 呢？」）。
            //   与启动屏 `BootPanel` 的 `ByLine` **共用 `UiLayoutFlow.Brand` 的同一套几何**
            //   （x=0 居中、y=-462、900×54、font24 档、色 0.78/0.76/0.72）⇒ 两个前面板的署名不可能错位。
            //   ⚠️ 节点名取 ByLine：`uicheck` 有一条「主菜单不再自画 logo / 页脚 / 提示行」的断言，
            //   它按**带双引号的字面量**查 Logo / Footer / Tip 三个名字（那是原版 LogoPlaceholder
            //   那一族的自画元素）；署名行不属那三类、也不是那三个名字 ⇒ 不会误撞。
            //   （本行刻意**不写带引号的那三个词** —— 写成带引号就会被那条断言当成真元素命中，
            //     实测踩过：第一版注释里写了它们，uicheck 直接 FAIL。）
            //   ★ 2026 品牌署名轮：与启动屏同一口径加 `forceChi: true` —— 原版拉丁字模不分大小写
            //   （`by clover-engine` 会被画成 `BY CLOVER-ENGINE`），逐字小写只有原版 `font24_chi` 能画。
            //   见 `BootPanel` 里同一行的注释与 `D2Text.D2Label._forceChi`。
            UiArt.Label(screen, "ByLine", UiLayoutFlow.Brand.ByLineText,
                UiLayoutFlow.ChineseFontSize(D2Text.D2Font.Font24), TextAnchor.MiddleCenter,
                UiLayoutFlow.Brand.ByLineColor, UiLayoutFlow.Brand.ByLineSize,
                UiLayoutFlow.Brand.ByLinePos, forceChi: true);

            // 对照表进日志（每个面板一次）：验收要的「面板 → 原版坐标 → 我们的坐标 → 依据节点名」
            UiLayoutFlow.LogTable(nameof(MainMenuPanel));
        }

        // ★ 片 1 **删除**：`OnCinematics()`（`CINEMATICS` 按钮的回调）——
        //   用户本轮点名删掉中间两个未实装按钮 ⇒ 回调没有调用方了。
        //   ⛔ 它原来的行为（`Log.Warn` + `Game.UI.Toast("本版本未实装过场动画")`）**不许换个地方复活**：
        //   要恢复必须先把过场 CG 真做出来（Bink `.bik` 转码 → `VideoPlayer` 能播）再建回按钮，
        //   见文件头的恢复条件。事件层没有对应常量（它当时是面板内私有方法，不发事件）。
    }
}
