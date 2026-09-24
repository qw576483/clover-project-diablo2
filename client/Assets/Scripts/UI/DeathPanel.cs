// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/DeathPanel.cs
//   坐标全部改成「有出处的量」，**不再有裸魔数**。
//
// ── 用到的原版素材（逐条出处）─────────────────────────────────────────────────
//   ① 底图 = 原版 `data/global/ui/MENU/EndGame.dc6` **页 0 的 4 块**：
//      实测 `python tools/d2codec/dc6.py info 原版资源/d2dc6/data/global/ui/MENU/EndGame.dc6`
//        ⇒ `dir=1 fpd=8 frames=8  256x256 64x256 256x224 64x224  ×2`
//        ⇒ 每 4 帧一页：左列 256 + 右列 64 = 320 宽；上行 256 + 下行 224 = 480 高
//      帧号：页 0 = 帧 0..3 ⇒ 工程内 `D2/UI/Menu/endgame_{0..3}.png`
//      （`ResPaths.EndGameTile(0, i)`）；1080 下按全局口径 **×1.8 + 居中** = 面板 576×864。
//      为什么用**页 0**：原版两页各是一张 320×480 整幅画（页 0 = 暗厅 + 孤身人影；
//      页 1 = 金甲天使）。本项目**只有软核、没有专家（hardcore）模式**（见验收表「范围边界」），
//      而 **页 1 对应的是专家模式的「你的英勇長存人心」**（原版串表 id 5096，见下）
//      ⇒ 软核死亡用页 0。
//   ② 标题条 = 原版 `data/LOCAL/UI/chi/youdiedsoftcore.dc6` **帧 0 = 256×54**（实测）
//      ⇒ 工程内 `D2/UI/Banner/youdiedsoftcore_0.png`（`ResPaths.Banner`）。
//   ③ 按钮 = 原版 `data/global/ui/MENU/endgameok.dc6` **96×32 ×2 帧（常态/按下，实测）**
//      ⇒ `D2/UI/Menu/endgameok_{0,1}.png`（`ResPaths.MenuEndGameOK`）。
//      麻点度量 ACT1 **84.5** vs EndGame **28.8**（2.9 倍）+ 肉眼复核
//      （@ACT1 满屏彩色噪点 / @EndGame 干净深灰石板）。依据与复跑命令见 `ResPaths.cs`。
//   ④ 按钮文字 = **原版中文串表** id **3403**「繼續」（`data/d2text/chi_string.txt`；
//      英文同 id 行 = `Continue`，与参考工程键名版串表 `Continue` 键一致）。
//      语义对得上：原版该按钮按下去就是"回到游戏（在城里复活）"，与本项目
//        `Events.ReviveRequest` → `ICombatModule.RevivePlayer()` 的行为**同一件事**。
//
// ── 本屏的文案出处（不许自己写中文当原版）────────────────────────────────────
//   原版死亡屏是「图形标题条 + 引擎渲染的金币数字 + 一个 Continue 按钮」：
//     · 图形标题条正文 = 「你損失金錢數量為」（= 上面 ② 那张图里的字）
//     · 原版串表（`data/d2text/{eng,chi}_string.txt` 同 id 对照，见回报）
//         id **5094** = `Death takes its toll of %d Gold` /「死亡取走了%d金幣」（软核）
//         id **5096** = `Your deeds of valor will be remembered` /「你的英勇長存人心」（专家模式）
//         id **3403** = `Continue` /「繼續」
//
// ── 布局（本项目新增排布，登记 E22）───────────────────────────────────────────
//   原版**该屏控件坐标没有出处**：参考工程 `Diablerie/Assets/Prefabs/` 里**没有死亡屏 prefab**，
//   原版素材本身也没有画好的控件槽（底图是一整幅画）。
//   ⇒ 规则：**「标题条 / 提示行 / 按钮」三行自上而下，行高 = 各自原版像素高，行距固定 15 原版px，
//      整栈垂直居中于面板（= 画布中心 (0,0)）**；x 一律居中。
//   全部落在 `UiLayoutGame.Death*` 常量里（纯数据 + 纯函数）⇒ `uicheck` 离线逐条断言。
//
// 定时器必须用 `*Unscaled`（`constraints.md` #2）：死亡常伴随 `Time.timeScale = 0`
//   （例如暂停中被毒死、或结算流程压了时间缩放），此时引擎 `Game.Timer.After/Every`
//   永不触发、**零报错**。本面板的「淡入 → 点亮按钮」用 `Game.Timer.AfterUnscaled`，
//   并在 `OnClose` 里 `Stop(id)`（面板提前关闭时不留悬挂回调）。
// 打开方式：HUD 收到 `Events.PlayerDied` 后 `Game.UI.Open<DeathPanel>()`（HUD 是 Stage 常驻面板）；
//   本面板自己也订阅 `PlayerDied`（重复收到就重置倒计时），并订阅 `StageLeft` 兜底关闭。
// 复活请求：`Events.ReviveRequest`（无参）⇒ 由 `ICombatModule.RevivePlayer()` 负责回城 + 恢复生命。
//     即玩家 `IsDead == false` 时广播）⇒ **收到它才关闭**。点击后先置灰按钮 + 起一个 `*Unscaled`
//     看门狗，超时仍未收到 `Revived` 就放开按钮提示重试（复活失败时不再假装成功）。
// 提示行文字 = **本项目文案**（原版该屏没有状态提示行，串表里也没有对应文案）
//   ⇒ 登记在验收表「允许的差异」E24（为什么必须有：复活是异步的，必须有"等待中/失败重试"反馈）。
// 零 `using Diablo2.Module`（分层自检 ③）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>死亡屏。层：<see cref="UILayer.Top"/>。</summary>
    public class DeathPanel : UIPanel
    {
        /// <summary>死亡后到可点按钮的等待秒数（用 `*Unscaled` 计时；防误触 + 让死亡表演演完）。</summary>
        public const float ReviveDelaySeconds = 1.2f;

        /// <summary>发出 `ReviveRequest` 后等待 `Events.Revived` 的秒数（`*Unscaled` 计时；超时即放开按钮重试）。</summary>
        public const float ReviveWaitSeconds = 3f;

        /// <summary>按钮文字 = **原版中文串表 id 3403**（英文同 id = `Continue`）。代码写简体，字模渲染成原版繁体。</summary>
        public const string ContinueText = "继续";

        private bool _built;
        private bool _subscribed;
        private Image _shade;
        private Image _banner;
        private Image _reviveButton;
        private Text _hint;
        private long _timerId;
        private long _watchdogId;

        /// <inheritdoc/>
        public override UILayer Layer => UILayer.Top;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();
            Subscribe();

            // 按钮先置灰，等 `*Unscaled` 定时器到点再点亮
            UiArt.SetInteractable(_reviveButton, false);
            _shade.color = new Color(0f, 0f, 0f, 0f);
            _hint.text = WaitingText();

            StartFadeTimer();
            UiLog.Info($"死亡屏已打开（底图=原版 MENU/EndGame.dc6 页 0 ×{UiLayoutGame.K}，"
                       + $"标题条=原版 chi/youdiedsoftcore_0，按钮=原版 MENU/endgameok 帧 0/1；"
                       + $"按钮将在 {ReviveDelaySeconds:0.#}s 后可用，计时用 AfterUnscaled）");
        }

        /// <inheritdoc/>
        public override void OnClose()
        {
            StopTimer();
            StopWatchdog();
            Unsubscribe();
            UiLog.Info("死亡屏已关闭");
        }

        /// <summary>
        /// 等待期提示（本项目文案，见文件头 E24）。
        /// <para>**必须短到一行放得下**（实测踩过 B35）：原版位图字模的中文字形步进约
        /// 24.6 画布px，本行框宽 = (320−48)×1.8 = **489.6** ⇒ 超过 ~18 个中文字就会**折成 2 行**，
        /// `Screenshots/p5_1_death.png` 的放大图）。现改成单行短句 + `Overflow` 不换行。</para>
        /// </summary>
        private static string WaitingText()
            => $"你倒下了…… {ReviveDelaySeconds:0.#} 秒后可继续";

        // ═════════════════════════════════════════════════════════════════════
        // 构件（全部尺寸/坐标取自 `UiLayoutGame.Death*`，一个裸魔数都没有）
        // ═════════════════════════════════════════════════════════════════════
        private void Build()
        {
            if (_built) return;
            _built = true;

            // 整屏遮罩：原版死亡屏是把游戏画面压黑的（底图本身有大片纯黑边）
            _shade = UiArt.FullPanel(transform, "Shade", UiArt.Overlay, true);

            // ── 底图：原版 `MENU/EndGame.dc6` 页 0 的 4 块，按实测块表拼（无双击缩放、逐块 1:1）──
            for (var i = 0; i < UiLayoutGame.DeathBackTiles.Length; i++)
            {
                var name = ResPaths.EndGameTile(0, i);       // D2/UI/Menu/endgame_{0..3}
                UiArt.Art(transform, "EndGame" + i, name,
                    UiLayoutGame.DeathTileSize(i), UiLayoutGame.DeathTilePos(i));
            }

            // ── 标题条：原版 `chi/youdiedsoftcore.dc6` 帧 0（图形，正文「你損失金錢數量為」）──
            //    `preserveAspect` ⇒ 原版 256×54 的像素不会被外框拉变形。
            _banner = UiArt.Banner(transform, "Banner", ResPaths.Banner("youdiedsoftcore_0"),
                UiLayoutGame.DeathBannerSize, new Vector2(0f, UiLayoutGame.DeathRowCenterY(0)));

            // ── 提示行（本项目新增，原版无此行；见文件头 E24）──
            _hint = UiArt.Label(transform, "Hint", string.Empty, 20, TextAnchor.MiddleCenter,
                UiArt.TextColor,
                UiLayoutGame.Size(UiLayoutGame.DeathHintW, UiLayoutGame.DeathHintH),
                new Vector2(0f, UiLayoutGame.DeathRowCenterY(1)));
            // B35：**绝不换行**（本行只有 1 行的高度；换行 = 两行互相压字 + 压住按钮）。
            //   超长时左右对称溢出（居中锚点），仍然可读，不叠行。
            _hint.horizontalOverflow = HorizontalWrapMode.Overflow;

            // ── 按钮：原版 `MENU/endgameok.dc6`（96×32 ×2 帧），文字 = 原版串 id 3403「繼續」──
            //  传的是**不带结尾下划线**的路径前缀：`UiArt.OrigButton` 内部走
            //     `ResPaths.Frame(prefix, i)`（它会自己加 `_i`）。
            //     实测踩过：前缀**自己再拼一个下划线** ⇒ 变成 `…/endgameok__0`（双下划线）
            //     ⇒ `[Error] [Resource] 加载失败` + 按钮停在纯色块（本条实测见回报 B31）。
            _reviveButton = UiArt.OrigButton(transform, "Continue", ContinueText,
                ResPaths.MenuEndGameOK, UiLayoutGame.DeathButtonSize,
                new Vector2(0f, UiLayoutGame.DeathRowCenterY(2)), OnRevive,
                ResPaths.FrameCountEndGameOK);
        }

        /// <summary>淡入 + 延时点亮按钮（**一律 `*Unscaled`**）。</summary>
        private void StartFadeTimer()
        {
            StopTimer();

            if (Game.Timer == null)
            {
                // 引擎未启动（正常不会发生）：直接点亮，别让玩家卡在灰按钮上
                UiLog.Error("Game.Timer 为 null（Game.Launch 未调用？）⇒ 按钮立即点亮");
                UiArt.SetInteractable(_reviveButton, true);
                _shade.color = UiArt.Overlay;
                return;
            }

            _timerId = Game.Timer.AfterUnscaled(ReviveDelaySeconds, OnReviveReady);
        }

        private void StopTimer()
        {
            if (_timerId == 0) return;
            Game.Timer?.Stop(_timerId);
            _timerId = 0;
        }

        private void OnReviveReady()
        {
            _timerId = 0;
            _shade.color = UiArt.Overlay;
            UiArt.SetInteractable(_reviveButton, true);
            UiLog.Info("死亡屏按钮已可用（AfterUnscaled 到点；timeScale=0 时同样会触发）");
        }

        private void OnRevive()
        {
            UiLog.Info($"玩家点了「{ContinueText}」（原版串 id 3403）⇒ `{Events.ReviveRequest}`"
                       + "（回城与生命恢复由 Combat/Player 模块负责）");

            // 先置灰 + 提示，等待 `Events.Revived`（由 App 在复活确已完成时广播）再关闭
            UiArt.SetInteractable(_reviveButton, false);
            // 短句（单行不换行，见 WaitingText 的 B35 注释）；细节留在日志里（`ReviveRequest` / 看门狗）
            _hint.text = "正在复活……";
            Game.Event.Emit(Events.ReviveRequest);
            StartWatchdog();
        }

        /// <summary>
        /// 复活看门狗（`*Unscaled`）：发出 `ReviveRequest` 后 `ReviveWaitSeconds` 内没收到 `Revived`
        /// ⇒ 说明复活没成功（`ICombatModule.RevivePlayer` 可能拒绝），放开按钮让玩家重试（不许假装成功）。
        /// </summary>
        private void StartWatchdog()
        {
            StopWatchdog();
            if (Game.Timer == null)
            {
                UiLog.Warn("Game.Timer 为 null（引擎未启动）⇒ 复活看门狗未启动（收到 Revived 仍会关闭）");
                return;
            }
            _watchdogId = Game.Timer.AfterUnscaled(ReviveWaitSeconds, OnReviveTimeout);
        }

        private void StopWatchdog()
        {
            if (_watchdogId == 0) return;
            Game.Timer?.Stop(_watchdogId);
            _watchdogId = 0;
        }

        private void OnReviveTimeout()
        {
            _watchdogId = 0;
            if (!Game.UI.IsOpen<DeathPanel>()) return;

            UiLog.Warn($"发出 `{Events.ReviveRequest}` 后 {ReviveWaitSeconds:0.#}s 未收到 `{Events.Revived}`"
                       + " ⇒ 复活可能未成功，放开按钮供重试（见 [Combat] 日志）");
            UiArt.SetInteractable(_reviveButton, true);
            _hint.text = "复活未完成，请重试";
        }

        private void OnRevived()
        {
            if (!Game.UI.IsOpen<DeathPanel>()) return;
            UiLog.Info($"收到 `{Events.Revived}` ⇒ 玩家已复活，关闭死亡屏");
            Game.UI.Close<DeathPanel>();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 事件
        // ═════════════════════════════════════════════════════════════════════
        private void Subscribe()
        {
            if (_subscribed || Game.Event == null) return;
            _subscribed = true;
            Game.Event.On(Events.PlayerDied, OnPlayerDied);
            Game.Event.On(Events.Revived, OnRevived);
            Game.Event.On(Events.StageLeft, OnStageLeft);
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Game.Event == null) return;
            _subscribed = false;
            Game.Event.Off(Events.PlayerDied, OnPlayerDied);
            Game.Event.Off(Events.Revived, OnRevived);
            Game.Event.Off(Events.StageLeft, OnStageLeft);
        }

        private void OnPlayerDied()
        {
            // 已经开着 ⇒ 重置倒计时（重复死亡事件不会叠加定时器/看门狗）
            UiLog.Info("再次收到死亡事件 ⇒ 重置倒计时");
            StopWatchdog();
            UiArt.SetInteractable(_reviveButton, false);
            _shade.color = new Color(0f, 0f, 0f, 0f);
            _hint.text = WaitingText();
            StartFadeTimer();
        }

        private void OnStageLeft()
        {
            if (!Game.UI.IsOpen<DeathPanel>()) return;
            UiLog.Info("离开 Stage ⇒ 关闭死亡屏（兜底：正常路径由按钮关闭）");
            Game.UI.Close<DeathPanel>();
        }
    }
}
