// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/MainMenuPanel.cs
//
//     `SINGLE PLAYER / MULTIPLAYER / CINEMATICS / EXIT`
//   （出处 = `Prefabs/Menu/MainMenu.prefab` 的 `Buttons` 四个子节点：
//     `SinglePlayerButton`/`MultiPlayerButton`/`CinematicsButton`/`ExitButton` 的 `m_Text`；
//     中文版标题条资源也在：`data/local/ui/chi/{SINGLEPLAYER,MULTIPLAYER}.DC6`）
//
//   就不要那个按钮了。」⇒ 只建 `SINGLE PLAYER` / `EXIT`：
//     · `MULTIPLAYER`：本项目形态是**单机** ⇒ 点它只发 `Events.MultiplayerUnavailable`（Toast+Warn）；
//     · `CINEMATICS`：原版 4 段 CG 是 Bink `.bik` ⇒ 点它只弹 Toast。
//      这两项**原版有**（见下），所以这是一条**登记在案的偏离**，不是"回归原版"。
//   · 偏离怎么登记：`UiLayoutFlow.Menu.MultiPos` / `.CinematicsPos` 两个原版槽位坐标**仍保留**
//     （`uicheck` 的「主菜单 4 个原版槽位」断言直接引用它们），`UiLayoutFlow.Table` 的 `Panel.Menu`
//     段里那两行也**照旧留着**（Use 文案写明"槽位坐标保留"）⇒ 离线断言看得见这条偏离。
//   · 坐标纪律：`SINGLE PLAYER` / `EXIT` 的槽位与坐标**一个字符都没动**（仍走
//     `UiLayoutFlow.Menu.SinglePos` / `.ExitPos`）⇒ 画面上这两项不会"跑位"。
//   **以后不许悄悄加回来**：要恢复必须先有实装（能联机 / 能播的过场 CG），并同时改掉上面那条登记。
//     ⇒ 与启动屏**共用** `UiLayoutFlow.Brand` 的几何（x=0 居中、y=-462），不是各写一套魔数。
//
//     · `CONTINUE`   —— 原版没有；**功能没丢**：原版"继续玩"就是 `SINGLE PLAYER` → 选角屏 → `ENTER`
//                        （本工程 `Events.Fsm.TriggerNewGame` → CharSelect；无存档时 Flow 直接进创角屏，
//                         见 `Module/Flow/AppFlow.cs:1001-1011`）；
//     · `SETTINGS`   —— 原版主菜单没有；**入口没丢**：原版选项在 **ESC 暂停菜单**里
//                        ⇒ 本工程的 `OPTIONS` 按钮（`UI/PausePanel.cs`，与 `UiLayoutFlow.Pause.OptionsPos`
//                        同一套原版按钮几何）→ `SettingsPanel`。**层级与原版一致**。
//
//   · 逐元素坐标全部来自 `UI/UiLayoutFlow.cs` 的 `Menu` 组（原版值 → ×1.8 居中）
//     · 背景：原版 `main_screen`（800×600）按高度 ×1.8 = 1440×1080、**水平居中**（不横向拉伸）
//         SinglePlayerButton 原版 (0,-17.5) → 本工程 (0,-31.5)   = `SINGLE PLAYER`  ← 建（=原版第 1 槽）
//         ExitButton         原版 (0,-152.5)→ 本工程 (0,-274.5)  = `EXIT`           ← 建（=原版第 4 槽）
//     · 按钮底图 = 原版帧（`ResPaths.MenuButtonWide` 的 0/1/2 = 常态/悬停/按下）
//     · 按钮文字 = **原版位图字体**（英文），色 `#191919` = 原版 WideButton.prefab 的 Text.m_Color
//     · 色调 = 原版亮度（白），不做任何提亮/压暗
//   原版 `LogoPlaceholder` 是**透明的空 Image**（m_Color.a=0、无 sprite，运行时由脚本喂图），
//   而 `main_screen` 贴图里已烘了 "EXPANSION SET / Lord of Destruction" 字标 ⇒ 不再另画 logo
//   （原版那枚火焰 logo 是 `FrontEnd/D2logoFireLeft/Right` 的**动画**，本屏未接）。
//
//     `MULTIPLAYER` 在单机形态下只发 `Events.MultiplayerUnavailable`（收方 Toast + Warn）；
//     `CINEMATICS` 原版 4 段 CG 是 **Bink `.bik`**（`原版资源/d2mpq/d2video.mpq`），
//     **删按钮 ≠ 删事件**：事件常量（`Core/Events.cs:100-101`）与收方
//     （`Module/Flow/AppFlow.cs:437` 订阅 / `:1162` `OnMultiplayerUnavailable`）**原样保留** ——
//     将来真接了联机，只要把按钮建回来即可复用整条链路。
//   · `Args.hasSave` 仍由 Flow 传入（`Module/Flow/AppFlow.cs:516`），**不控制任何按钮**
//     （原版没有 CONTINUE 项）⇒ 只落一条日志，便于查"存档到底读没读到"。
// 只发/收事件；**不得引用 `Diablo2.Module` 下的任何类型**（分层自检 ③）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using UnityEngine;

namespace Diablo2.UI
{
    /// <summary>
    /// 主菜单面板（1:1 复刻原版 LoD 主菜单布局）。
    /// <para> 起**只建 2 项**：`SINGLE PLAYER` / `EXIT`（原版 4 项里的第 1 / 第 4 项）——
    /// 中间两项 `MULTIPLAYER` / `CINEMATICS` 不建。</para>
    /// </summary>
    public class MainMenuPanel : UIPanel
    {
        /// <summary>`OnOpen(param)` 的参数（契约：面板状态值一律由调用方传入）。</summary>
        public class Args
        {
            public bool hasSave;
        }

        /// <summary>
        /// 仍需**自绘文字**的按钮文案（**逐字 = 原版 prefab 的 `m_Text`**）。
        /// <para>第 1 项（原版 `SINGLE PLAYER`）的文字改由**原版中文本地化图**
        /// `SINGLEPLAYER.DC6`（2 tile 拼成的 282×29，图上是同一句话的花体金字）承担 ⇒ 它的常量从本类删除
        /// （口径同下方"删掉的两项"：常量留着就会出现"常量还在、按钮忘了建"的中间态，离线也查不出来）。</para>
        /// <para>只留原版 4 项里的**第 4 项** —— 用户原话「菜单中，中间那两个既然没开发，
        /// 就不要那个按钮了」。`Multi` / `Cinematics` 两个常量亦已从本类删除。</para>
        /// </summary>
        private static class Text
        {
            public const string Exit = "EXIT";
        }

        private bool _built;

        /// <summary>
        /// 层：<see cref="UILayer.Normal"/>（= 文件头「层：Normal」）。
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

            // 原版主菜单屏（800×600）**按高度等比 ×1.8 = 1440×1080 水平居中**：
            //   高正好铺满画布、宽 1440 < 1920 ⇒ 左右各留 240 交给相机/背景（**不把 4:3 素材横向拉伸**）
            UiLayoutFlow.BackdropArt(screen, ResPaths.MenuMainScreen, new Color(0.05f, 0.05f, 0.06f, 1f));

            //   留下来的两个**坐标一个字符没动**（仍走 `Menu.SinglePos` / `Menu.ExitPos`）：
            //      `SINGLE PLAYER` 原版 (0,-17.5)×1.8 = (0,-31.5)、`EXIT` 原版 (0,-152.5)×1.8 = (0,-274.5)。
            //   中间空出来的两行**不补位、不重排**（用户说的是"不要那个按钮"，不是"把下面提上来"）
            //   —— 所以画面上 `EXIT` 仍在原版第 4 槽的位置，不会跑位。
            // 空文案 ⇒ 不建文字标签；文字 = 下面那张原版中文本地化图（图自带 `SINGLE PLAYER` 花体字）
            UiLayoutFlow.FlowButton.Create(screen, "Single", string.Empty,
                UiLayoutFlow.WideButtonOrig, UiLayoutFlow.Menu.SinglePos,
                () => Game.Event.Emit(Events.Fsm.TriggerNewGame));
            // 整幅 = 2 tile（256×29 + 26×29 = 282×29）；墨迹框 = 图内 y 0..28（28 高）
            // ⇒ 墨心比图中线**高** 0.5 原版px（= (0+28)/2 − 29/2 = −0.5，向下为正）
            UiLayoutFlow.BannerOnButton(screen, "SingleArt", ResPaths.BannerSinglePlayer,
                282f, 29f, 28f, UiLayoutFlow.Menu.SinglePos, -0.5f);

            UiLayoutFlow.FlowButton.Create(screen, "Quit", Text.Exit,
                UiLayoutFlow.WideButtonOrig, UiLayoutFlow.Menu.ExitPos,
                () => Game.Event.Emit(Events.QuitRequest));

            // 常驻品牌署名 `by clover-engine`（居底居中；几何/字号/颜色 = `UiLayoutFlow.Brand` 的同一套常量，
            //   与启动屏共用 ⇒ 两个前面板的署名不可能错位）。**最后建** ⇒ 画在按钮之上。
            UiLayoutFlow.Brand.Attach(screen);

            WarmUpGamePanelsArt();

            // 对照表进日志（每个面板一次）：验收要的「面板 → 原版坐标 → 我们的坐标 → 依据节点名」
            UiLayoutFlow.LogTable(nameof(MainMenuPanel));
        }

        /// <summary>
        /// 预热进入游戏世界后才会用到的原版底图：NPC 对话底图 / 对话标题条 / 窗框
        /// （路径见 <see cref="Core.ResPaths.PanelDialogBack"/> / <see cref="Core.ResPaths.Banner"/>
        /// / <see cref="Core.ResPaths.PanelBoxFrameSettings"/>）。
        /// <para>
        /// 这三张图由对话屏与传送点面板**在各自构建的那一帧**经 `UiArt.SetSprite` 请求；那两屏同帧
        /// 还有大量贴图请求在途，引擎的加载回调会悬在**在途**状态、当帧不落地，而 `UiImageLoader`
        /// 的「同路径去重」在在途期间直接返回 ⇒ 调用方无法重试，该合成会话内这两屏只能露占位色。
        /// 主菜单是进入游戏世界前玩家停留的最后一屏（Boot → MainMenu → CharSelect → Stage），
        /// 且本屏请求量小 ⇒ 在这里先请求一次，贴图落进引擎缓存后，那两屏的 `SetSprite` 命中缓存、
        /// 在调用点同步拿到贴图。
        /// </para>
        /// <para>`UiArt.WarmUp` 只把资源装进引擎缓存、不建任何控件；素材缺失只留一条 Warn。</para>
        /// </summary>
        private static void WarmUpGamePanelsArt()
        {
            UiArt.WarmUp(ResPaths.PanelDialogBack);
            UiArt.WarmUp(ResPaths.Banner("npcspeech_0"));
            UiArt.WarmUp(ResPaths.PanelBoxFrameSettings);
        }

        //   要恢复必须先把过场 CG 真做出来（Bink `.bik` 转码 → `VideoPlayer` 能播）再建回按钮，
        //   见文件头的恢复条件。事件层没有对应常量（它当时是面板内私有方法，不发事件）。
    }
}
